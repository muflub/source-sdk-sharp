//========= SourceSharp — G0 probe: who opens what, and who looks up what ==========//
//
// plan_gameui_managed.md G0. A PROBE, never shipped: tools/probes/dl-trace/build
// writes it under farm/, and a run names it in LD_PRELOAD for one session.
//
// It interposes dlopen and dlsym for the whole process and logs, to
// SOURCESHARP_PROBE_DLTRACE, every dlopen (path, the handle's file) and every
// dlsym of CreateInterface (the handle's file, the caller's file), each with a
// monotonic time: the order in which engine.so opens client.so and GameUI.so and
// resolves their factories, which is the plan's load-order checkpoint. Other
// lookups pass through unlogged. RTLD_NEXT and RTLD_DEFAULT lookups pass through
// with the caller's meaning lost (the real dlsym sees this file as the caller),
// so they are logged as such: a run that depends on them shows it.
//
//=============================================================================//

#include <dlfcn.h>
#include <fcntl.h>
#include <link.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

namespace
{
typedef void* (*DlsymFn)(void*, const char*);
typedef void* (*DlopenFn)(const char*, int);

DlsymFn RealDlsym()
{
	static DlsymFn s_pfn = NULL;
	if (!s_pfn)
	{
		s_pfn = (DlsymFn)dlvsym(RTLD_NEXT, "dlsym", "GLIBC_2.34");
		if (!s_pfn)
			s_pfn = (DlsymFn)dlvsym(RTLD_NEXT, "dlsym", "GLIBC_2.2.5");
	}
	return s_pfn;
}

int TraceFd()
{
	static int s_fd = -2;
	if (s_fd == -2)
	{
		const char* pPath = getenv("SOURCESHARP_PROBE_DLTRACE");
		s_fd = (pPath && *pPath) ? open(pPath, O_WRONLY | O_CREAT | O_APPEND | O_CLOEXEC, 0644) : -1;
	}
	return s_fd;
}

double Now()
{
	timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return (double)ts.tv_sec + ts.tv_nsec / 1e9;
}

const char* Base(const char* pszPath)
{
	if (!pszPath)
		return "(null)";
	const char* pszSlash = strrchr(pszPath, '/');
	return pszSlash ? pszSlash + 1 : pszPath;
}

const char* HandleFile(void* hModule)
{
	if (hModule == RTLD_NEXT)
		return "RTLD_NEXT";
	if (hModule == RTLD_DEFAULT)
		return "RTLD_DEFAULT";
	link_map* pMap = NULL;
	if (dlinfo(hModule, RTLD_DI_LINKMAP, &pMap) != 0 || !pMap)
		return "?";
	return pMap->l_name && *pMap->l_name ? Base(pMap->l_name) : "(main)";
}

const char* AddressFile(const void* pAddress)
{
	Dl_info info;
	if (!pAddress || !dladdr(pAddress, &info) || !info.dli_fname)
		return "?";
	return Base(info.dli_fname);
}

void Line(const char* pszFormat, ...) __attribute__((format(printf, 1, 2)));

void Line(const char* pszFormat, ...)
{
	int fd = TraceFd();
	if (fd < 0)
		return;
	char szBuf[1024];
	int n = snprintf(szBuf, sizeof(szBuf), "%.6f %d ", Now(), (int)getpid());
	va_list args;
	va_start(args, pszFormat);
	n += vsnprintf(szBuf + n, sizeof(szBuf) - n - 1, pszFormat, args);
	va_end(args);
	if (n > (int)sizeof(szBuf) - 2)
		n = sizeof(szBuf) - 2;
	szBuf[n++] = '\n';
	(void)!write(fd, szBuf, n);
}
} // namespace

extern "C" __attribute__((visibility("default"))) void* dlopen(const char* pszPath, int nFlags)
{
	static DlopenFn s_pfnReal = NULL;
	if (!s_pfnReal)
		s_pfnReal = (DlopenFn)RealDlsym()(RTLD_NEXT, "dlopen");
	void* hModule = s_pfnReal(pszPath, nFlags);
	Line("dlopen %s flags=0x%x -> %s caller=%s", pszPath ? pszPath : "(null)", nFlags,
		 hModule ? HandleFile(hModule) : "NULL", AddressFile(__builtin_return_address(0)));
	return hModule;
}

extern "C" __attribute__((visibility("default"))) void* dlsym(void* hModule, const char* pszSymbol)
{
	void* pResult = RealDlsym()(hModule, pszSymbol);
	if (strcmp(pszSymbol, "CreateInterface") == 0)
		Line("dlsym %s CreateInterface -> %s caller=%s", HandleFile(hModule), pResult ? AddressFile(pResult) : "NULL",
			 AddressFile(__builtin_return_address(0)));
	return pResult;
}
