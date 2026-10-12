// tools/loadprof's LD_PRELOAD probe (plan_map_load P0). Original work, built
// against no SDK header: it interposes two tier0 exports and reads nothing else.
//
// COM_TimestampedLog -- the engine's own load milestones (166 sites, plus every
//   KeyValues file load). Every call is written to $SSPROBE_OUT.ts as
//   "<CLOCK_MONOTONIC> / <delta>:  <message>", the same clock perf's
//   -k CLOCK_MONOTONIC records, so the two line up. The engine loads tier0
//   privately: RTLD_NEXT finds nothing, so the original is not called and the
//   engine's own timestamped.log is not written while this is loaded.
//   Interesting events also get a backtrace: "S <id> <frames>" defines a stack
//   once, "E <seq> <id> <message>" is one event, both in $SSPROBE_OUT.
// SSPROBE_SAMPLE=1 -- a 2 ms CLOCK_MONOTONIC (wall-clock, so waits are seen)
//   SIGPROF sampler of the main thread, from the message prefix in
//   SSPROBE_SAMPLE_FROM (default "SV_SpawnServer(") to the "Model Loading time"
//   summary. libgcc unwinds the native frames; at JIT code (no unwind tables)
//   the walk continues along the RBP chain, after a "|FP|" marker. Samples go
//   to $SSPROBE_OUT.samples.
// SSPROBE_SLEEPS=1 -- counts ThreadSleep calls by caller between
//   SSPROBE_SLEEP_FROM and SSPROBE_SLEEP_TO (default: the server's
//   SendServerInfo(finished) to the client's ProcessServerInfo).
#define _GNU_SOURCE
#include <dlfcn.h>
#include <execinfo.h>
#include <pthread.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <link.h>
#include <sys/mman.h>
#include <unistd.h>
#include <signal.h>
#include <sys/syscall.h>
#include <stdatomic.h>
#include <unwind.h>

typedef void (*orig_t)(const char *, ...);
static orig_t s_orig;
static FILE *s_out, *s_ts;
#include <time.h>
static double s_last;
static pthread_mutex_t s_lock = PTHREAD_MUTEX_INITIALIZER;
#define MAXSIG 65536
static uint64_t s_sigs[MAXSIG];
static int s_nsigs;
static unsigned long s_seq;

__attribute__((constructor)) static void init(void)
{
	const char *p = getenv("SSPROBE_OUT");
	s_out = fopen(p ? p : "/tmp/ssprobe.log", "w");
	if (s_out)
		fprintf(s_out, "# ssprobe loaded\n"), fflush(s_out);
	char tp[600];
	snprintf(tp, sizeof tp, "%s.ts", p ? p : "/tmp/ssprobe.log");
	s_ts = fopen(tp, "w");
}

static int interesting(const char *msg)
{
	if (!strncmp(msg, "KeyValues::LoadFromFile(", 24))
		return strstr(msg, ": Begin") != NULL;
	return strncmp(msg, "KeyValues::LoadFromBuffer(", 26) != 0 && strncmp(msg, "cl:  create", 11) != 0;
}

static void frame_name(void *addr, char *buf, size_t n)
{
	Dl_info info;
	if (dladdr(addr, &info) && info.dli_fname) {
		const char *base = strrchr(info.dli_fname, '/');
		base = base ? base + 1 : info.dli_fname;
		uintptr_t off = (uintptr_t)addr - (uintptr_t)info.dli_fbase;
		if (info.dli_sname && info.dli_saddr)
			snprintf(buf, n, "%s+0x%lx(%s+0x%lx)", base, (unsigned long)off, info.dli_sname,
				 (unsigned long)((uintptr_t)addr - (uintptr_t)info.dli_saddr));
		else
			snprintf(buf, n, "%s+0x%lx", base, (unsigned long)off);
	} else {
		snprintf(buf, n, "?%p", addr);
	}
}

// --- wall-clock sampler of the main thread during each load (SSPROBE_SAMPLE=1) ---
#define MAXSAMP 20000
#define SDEPTH 40
typedef struct { double t; int n; void *pc[SDEPTH]; } samp_t;
static samp_t *s_samp;
static atomic_int s_nsamp;
static timer_t s_timer;
static int s_sampling, s_timerMade;
static double mono(void) { struct timespec ts; clock_gettime(CLOCK_MONOTONIC, &ts); return ts.tv_sec + ts.tv_nsec * 1e-9; }
typedef struct { void **pc; int n, max; uintptr_t rbp; } uw_t;
static _Unwind_Reason_Code uwcb(struct _Unwind_Context *ctx, void *arg)
{
	uw_t *u = arg;
	if (u->n >= u->max) return _URC_END_OF_STACK;
	u->pc[u->n++] = (void *)_Unwind_GetIP(ctx);
	u->rbp = _Unwind_GetGR(ctx, 6);
	return _URC_NO_REASON;
}
static uintptr_t s_stkLo, s_stkHi;
static void onprof(int sig, siginfo_t *si, void *uc)
{
	(void)sig; (void)si; (void)uc;
	int i = atomic_fetch_add(&s_nsamp, 1);
	if (i >= MAXSAMP) return;
	samp_t *sp = &s_samp[i];
	sp->t = mono();
	uw_t u = { sp->pc, 0, SDEPTH - 1, 0 };
	_Unwind_Backtrace(uwcb, &u);
	int n = u.n;
	// libgcc stops at JIT code (no FDE). Continue along the RBP chain, which CoreCLR's
	// x64 JIT keeps; a 0 marks where the frame-pointer walk starts.
	uintptr_t rbp = u.rbp;
	if (n < SDEPTH - 2 && rbp) {
		sp->pc[n++] = 0;
		for (int k = 0; k < 24 && n < SDEPTH; k++) {
			if (rbp < s_stkLo || rbp + 16 > s_stkHi || (rbp & 7)) break;
			uintptr_t ret = ((uintptr_t *)rbp)[1], next = ((uintptr_t *)rbp)[0];
			if (!ret) break;
			sp->pc[n++] = (void *)ret;
			if (next <= rbp) break;
			rbp = next;
		}
	}
	sp->n = n;
}
static void startSampling(void)
{
	if (!s_samp) {
		pthread_attr_t at; void *lo; size_t sz;
		if (!pthread_getattr_np(pthread_self(), &at)) { pthread_attr_getstack(&at, &lo, &sz); s_stkLo = (uintptr_t)lo; s_stkHi = (uintptr_t)lo + sz; pthread_attr_destroy(&at); }
		s_samp = calloc(MAXSAMP, sizeof *s_samp);
		void *warm[4]; backtrace(warm, 4); // load libgcc before any signal arrives
		struct sigaction sa; memset(&sa, 0, sizeof sa);
		sa.sa_sigaction = onprof; sa.sa_flags = SA_SIGINFO | SA_RESTART;
		sigaction(SIGPROF, &sa, NULL);
	}
	if (!s_timerMade) {
		struct sigevent sev; memset(&sev, 0, sizeof sev);
		sev.sigev_notify = SIGEV_THREAD_ID; sev.sigev_signo = SIGPROF;
		sev._sigev_un._tid = (pid_t)syscall(SYS_gettid);
		if (timer_create(CLOCK_MONOTONIC, &sev, &s_timer)) return;
		s_timerMade = 1;
	}
	atomic_store(&s_nsamp, 0);
	struct itimerspec it = { { 0, 2000000 }, { 0, 2000000 } };
	timer_settime(s_timer, 0, &it, NULL);
	s_sampling = 1;
}
static void stopSampling(void)
{
	struct itimerspec it; memset(&it, 0, sizeof it);
	timer_settime(s_timer, 0, &it, NULL);
	s_sampling = 0;
	int n = atomic_load(&s_nsamp); if (n > MAXSAMP) n = MAXSAMP;
	char path[600]; snprintf(path, sizeof path, "%s.samples", getenv("SSPROBE_OUT") ? getenv("SSPROBE_OUT") : "/tmp/ssprobe.log");
	FILE *f = fopen(path, "a");
	if (!f) return;
	fprintf(f, "LOAD %d\n", n);
	for (int i = 0; i < n; i++) {
		fprintf(f, "T %.4f", s_samp[i].t);
		for (int k = 2; k < s_samp[i].n; k++) {
			if (!s_samp[i].pc[k]) { fprintf(f, " |FP|"); continue; }
			char fb[256]; frame_name(s_samp[i].pc[k], fb, sizeof fb); fprintf(f, " %s", fb);
		}
		fputc('\n', f);
	}
	fclose(f);
}

// --- ThreadSleep interposer: count sleeps by caller inside the handshake gap (SSPROBE_SLEEPS=1) ---
typedef void (*sleep_t)(unsigned);
static sleep_t s_origSleep;
static int s_inGap;
#define MAXC 32
static void *s_sc[MAXC]; static unsigned long s_sn[MAXC]; static double s_st[MAXC]; static int s_nc;
static double s_gapStart;
void ThreadSleep(unsigned ms);
static char s_t0Path[512];
static int findt0(struct dl_phdr_info *info, size_t size, void *d)
{
	(void)size; (void)d;
	if (info->dlpi_name && strstr(info->dlpi_name, "/libtier0.so")) { snprintf(s_t0Path, sizeof s_t0Path, "%s", info->dlpi_name); return 1; }
	return 0;
}
static void realSleep(unsigned ms)
{
	if (!s_origSleep) {
		dl_iterate_phdr(findt0, NULL);
		void *h = s_t0Path[0] ? dlopen(s_t0Path, RTLD_NOW | RTLD_NOLOAD) : NULL;
		s_origSleep = h ? (sleep_t)dlsym(h, "ThreadSleep") : NULL;
		if (s_origSleep == (sleep_t)ThreadSleep) s_origSleep = NULL;
	}
	if (s_origSleep) s_origSleep(ms); else usleep(ms ? ms * 1000 : 0);
}
void ThreadSleep(unsigned ms);
void ThreadSleep(unsigned ms)
{
	if (!s_inGap) { realSleep(ms); return; }
	void *caller = __builtin_return_address(0);
	double t0 = mono();
	realSleep(ms);
	double dt = mono() - t0;
	pthread_mutex_lock(&s_lock);
	int i;
	for (i = 0; i < s_nc && s_sc[i] != caller; i++) {}
	if (i == s_nc && s_nc < MAXC) { s_sc[i] = caller; s_sn[i] = 0; s_st[i] = 0; s_nc++; }
	if (i < MAXC) { s_sn[i]++; s_st[i] += dt; }
	pthread_mutex_unlock(&s_lock);
}
static void gapBegin(void) { s_nc = 0; s_gapStart = mono(); s_inGap = 1; }
static void gapEnd(void)
{
	s_inGap = 0;
	if (!s_out) return;
	pthread_mutex_lock(&s_lock);
	fprintf(s_out, "# GAP %.3fs:", mono() - s_gapStart);
	for (int i = 0; i < s_nc; i++) { char fb[256]; frame_name(s_sc[i], fb, sizeof fb); fprintf(s_out, " [%s x%lu %.3fs]", fb, s_sn[i], s_st[i]); }
	fputc('\n', s_out); fflush(s_out);
	pthread_mutex_unlock(&s_lock);
}

void COM_TimestampedLog(const char *fmt, ...)
{
	char msg[1024];
	va_list ap;
	va_start(ap, fmt);
	vsnprintf(msg, sizeof msg, fmt, ap);
	va_end(ap);

	if (getenv("SSPROBE_SLEEPS")) {
		const char *f = getenv("SSPROBE_SLEEP_FROM"), *t = getenv("SSPROBE_SLEEP_TO");
		if (!f) f = " CBaseClient::SendServerInfo(finished)";
		if (!t) t = " CBaseClient::ProcessServerInfo";
		if (!strcmp(msg, f)) gapBegin();
		else if (s_inGap && !strcmp(msg, t)) gapEnd();
	}
	if (getenv("SSPROBE_SAMPLE")) {
		const char *from = getenv("SSPROBE_SAMPLE_FROM"); if (!from) from = "SV_SpawnServer(";
		if (!s_sampling && !strncmp(msg, from, strlen(from))) startSampling();
		else if (s_sampling && !strncmp(msg, "Model Loading time ", 19) && msg[19] >= '0' && msg[19] <= '9') stopSampling();
	}
	if (s_ts) {
		struct timespec ts;
		clock_gettime(CLOCK_MONOTONIC, &ts);
		double now = ts.tv_sec + ts.tv_nsec * 1e-9;
		pthread_mutex_lock(&s_lock);
		fprintf(s_ts, "%10.4f / %8.4f:  %s\n", now, s_last ? now - s_last : 0.0, msg);
		fflush(s_ts);
		s_last = now;
		pthread_mutex_unlock(&s_lock);
	}
	if (!s_orig)
		s_orig = (orig_t)dlsym(RTLD_NEXT, "COM_TimestampedLog");
	if (s_orig)
		s_orig("%s", msg);
	if (!s_out || !interesting(msg))
		return;

	void *frames[40];
	int n = backtrace(frames, 40);
	uint64_t h = 1469598103934665603ull;
	for (int i = 1; i < n; i++)
		h = (h ^ (uintptr_t)frames[i]) * 1099511628211ull;

	pthread_mutex_lock(&s_lock);
	int id = -1;
	for (int i = 0; i < s_nsigs; i++)
		if (s_sigs[i] == h) { id = i; break; }
	if (id < 0 && s_nsigs < MAXSIG) {
		id = s_nsigs;
		s_sigs[s_nsigs++] = h;
		fprintf(s_out, "S %d", id);
		for (int i = 1; i < n; i++) {
			char fb[256];
			frame_name(frames[i], fb, sizeof fb);
			fprintf(s_out, " %s", fb);
		}
		fputc('\n', s_out);
	}
	fprintf(s_out, "E %lu %d %s\n", ++s_seq, id, msg);
	fflush(s_out);
	pthread_mutex_unlock(&s_lock);
}
