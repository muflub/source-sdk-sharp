//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — C0 probe: a logging GameUI.so that forwards to stock ===//
//
// plan_client_novalve.md §3.2a / §6 C0 item 7. A PROBE, never shipped: nothing
// in the build deploys it. tools/probes/c0-gameui-session builds it, puts it
// where the engine might look for GameUI.so for one run, and removes it.
//
// It exports CreateInterface. GameUI011 and GameConsole004 come back as
// wrappers that log every call (arguments included) to SOURCESHARP_PROBE_LOG
// and forward to the stock module named by SOURCESHARP_PROBE_STOCK_GAMEUI;
// every other name is forwarded untouched. The log line format matches the
// client's probe (shell_probes_debug.cpp): "<monotonic ns> <pid> gameui ...".
//
// The two interfaces are declared here by their vtable shape, transcribed from
// common/GameUI/IGameUI.h (38 virtuals, no base) and IGameConsole.h
// (IBaseInterface's virtual destructor, then 6); the farm does not carry those
// headers and sdk-links only shrinks. Arguments the log shows (a progress in
// [0,1], status text) are the check that the transcription is right.
//
//=============================================================================//

#include <dlfcn.h>
#include <fcntl.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

typedef void* (*CreateInterfaceFn)(const char* pName, int* pReturnCode);
typedef uintptr_t VPANEL;

namespace
{

int ProbeFd()
{
	static int s_fd = -2;
	if (s_fd == -2)
	{
		const char* pPath = getenv("SOURCESHARP_PROBE_LOG");
		s_fd = (pPath && *pPath) ? open(pPath, O_WRONLY | O_CREAT | O_APPEND | O_CLOEXEC, 0644) : -1;
	}
	return s_fd;
}

long long NowNs()
{
	timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return (long long)ts.tv_sec * 1000000000LL + ts.tv_nsec;
}

void ProbeLog(const char* pFmt, ...) __attribute__((format(printf, 1, 2)));

void ProbeLog(const char* pFmt, ...)
{
	int fd = ProbeFd();
	if (fd < 0)
		return;
	char buf[2048];
	int n = snprintf(buf, sizeof(buf), "%lld %d gameui ", NowNs(), (int)getpid());
	va_list ap;
	va_start(ap, pFmt);
	int m = vsnprintf(buf + n, sizeof(buf) - n - 1, pFmt, ap);
	va_end(ap);
	if (m < 0)
		m = 0;
	n += (m < (int)sizeof(buf) - n - 1) ? m : (int)sizeof(buf) - n - 2;
	for (int i = 0; i < n; i++)
	{
		if (buf[i] == '\n' || buf[i] == '\r')
			buf[i] = ' ';
	}
	buf[n++] = '\n';
	ssize_t w = write(fd, buf, n);
	(void)w;
}

const char* S(const char* p)
{
	return p ? p : "(null)";
}

const char* ModuleOf(const void* pAddr)
{
	Dl_info info;
	if (!pAddr || !dladdr(pAddr, &info) || !info.dli_fname)
		return "?";
	return info.dli_fname;
}

//-----------------------------------------------------------------------------
// IGameUI (GameUI011), slot for slot.
//-----------------------------------------------------------------------------
class IGameUIShape
{
public:
	virtual void Initialize(CreateInterfaceFn appFactory) = 0;
	virtual void PostInit() = 0;
	virtual void Connect(CreateInterfaceFn gameFactory) = 0;
	virtual void Start() = 0;
	virtual void Shutdown() = 0;
	virtual void RunFrame() = 0;
	virtual void OnGameUIActivated() = 0;
	virtual void OnGameUIHidden() = 0;
	virtual void OLD_OnConnectToServer(const char* game, int IP, int port) = 0;
	virtual void OnDisconnectFromServer_OLD(uint8_t eSteamLoginFailure, const char* username) = 0;
	virtual void OnLevelLoadingStarted(bool bShowProgressDialog) = 0;
	virtual void OnLevelLoadingFinished(bool bError, const char* failureReason, const char* extendedReason) = 0;
	virtual bool UpdateProgressBar(float progress, const char* statusText) = 0;
	virtual bool SetShowProgressText(bool show) = 0;
	virtual void ShowNewGameDialog(int chapter) = 0;
	virtual void SessionNotification(const int notification, const int param) = 0;
	virtual void SystemNotification(const int notification) = 0;
	virtual void ShowMessageDialog(const unsigned int nType, void* pOwner) = 0;
	virtual void UpdatePlayerInfo(uint64_t nPlayerId, const char* pName, int nTeam, uint8_t cVoiceState,
								  int nPlayersNeeded, bool bHost) = 0;
	virtual void SessionSearchResult(int searchIdx, void* pHostData, void* pResult, int ping) = 0;
	virtual void OnCreditsFinished() = 0;
	virtual void SetLoadingBackgroundDialog(VPANEL panel) = 0;
	virtual void BonusMapUnlock(const char* pchFileName, const char* pchMapName) = 0;
	virtual void BonusMapComplete(const char* pchFileName, const char* pchMapName) = 0;
	virtual void BonusMapChallengeUpdate(const char* pchFileName, const char* pchMapName, const char* pchChallengeName,
										 int iBest) = 0;
	virtual void BonusMapChallengeNames(char* pchFileName, char* pchMapName, char* pchChallengeName) = 0;
	virtual void BonusMapChallengeObjectives(int& iBronze, int& iSilver, int& iGold) = 0;
	virtual void BonusMapDatabaseSave() = 0;
	virtual int BonusMapNumAdvancedCompleted() = 0;
	virtual void BonusMapNumMedals(int piNumMedals[3]) = 0;
	virtual void OnConnectToServer2(const char* game, int IP, int connectionPort, int queryPort) = 0;
	virtual bool ValidateStorageDevice(int* pStorageDeviceValidated) = 0;
	virtual void SetProgressOnStart() = 0;
	virtual void OnDisconnectFromServer(uint8_t eSteamLoginFailure) = 0;
	virtual void OnConfirmQuit() = 0;
	virtual bool IsMainMenuVisible() = 0;
	virtual void SetMainMenuOverride(VPANEL panel) = 0;
	virtual void SendMainMenuCommand(const char* pszCommand) = 0;
};

// RunFrame, IsMainMenuVisible and UpdateProgressBar are per frame / per load
// step; they are counted, and logged on the first call and on every change.
struct Quiet
{
	long long n = 0;
	int last = -1;
};

class CLoggingGameUI : public IGameUIShape
{
public:
	explicit CLoggingGameUI(IGameUIShape* p)
		: m(p)
	{
	}

	void Initialize(CreateInterfaceFn f) override
	{
		ProbeLog("IGameUI Initialize factory=%p", (void*)f);
		m->Initialize(f);
		ProbeLog("IGameUI Initialize done");
	}

	void PostInit() override
	{
		ProbeLog("IGameUI PostInit");
		m->PostInit();
	}

	void Connect(CreateInterfaceFn f) override
	{
		ProbeLog("IGameUI Connect factory=%p from %s", (void*)f, ModuleOf((void*)f));
		m->Connect(f);
	}

	void Start() override
	{
		ProbeLog("IGameUI Start");
		m->Start();
	}

	void Shutdown() override
	{
		ProbeLog("IGameUI Shutdown (RunFrame calls=%lld)", m_RunFrame.n);
		m->Shutdown();
	}

	void RunFrame() override
	{
		if (m_RunFrame.n++ == 0)
			ProbeLog("IGameUI RunFrame (first)");
		m->RunFrame();
	}

	void OnGameUIActivated() override
	{
		ProbeLog("IGameUI OnGameUIActivated");
		m->OnGameUIActivated();
	}

	void OnGameUIHidden() override
	{
		ProbeLog("IGameUI OnGameUIHidden");
		m->OnGameUIHidden();
	}

	void OLD_OnConnectToServer(const char* g, int ip, int port) override
	{
		ProbeLog("IGameUI OLD_OnConnectToServer game=%s ip=%d port=%d", S(g), ip, port);
		m->OLD_OnConnectToServer(g, ip, port);
	}

	void OnDisconnectFromServer_OLD(uint8_t e, const char* u) override
	{
		ProbeLog("IGameUI OnDisconnectFromServer_OLD failure=%u user=%s", e, S(u));
		m->OnDisconnectFromServer_OLD(e, u);
	}

	void OnLevelLoadingStarted(bool b) override
	{
		ProbeLog("IGameUI OnLevelLoadingStarted show_progress=%d", b);
		m->OnLevelLoadingStarted(b);
		ProbeLog("IGameUI OnLevelLoadingStarted done");
	}

	void OnLevelLoadingFinished(bool e, const char* r, const char* x) override
	{
		ProbeLog("IGameUI OnLevelLoadingFinished error=%d reason=%s extended=%s (UpdateProgressBar calls=%lld)", e,
				 S(r), S(x), m_nProgress);
		m->OnLevelLoadingFinished(e, r, x);
	}

	bool UpdateProgressBar(float p, const char* t) override
	{
		m_nProgress++;
		ProbeLog("IGameUI UpdateProgressBar enter progress=%.4f text=%s", p, S(t));
		bool r = m->UpdateProgressBar(p, t);
		// SOURCESHARP_PROBE_PROGRESS_FORCE=1 answers true whatever stock said:
		// does the engine repaint when GameUI asks it to?
		static const bool s_bForce =
			getenv("SOURCESHARP_PROBE_PROGRESS_FORCE") && atoi(getenv("SOURCESHARP_PROBE_PROGRESS_FORCE"));
		ProbeLog("IGameUI UpdateProgressBar exit ret=%d%s", r || s_bForce, s_bForce && !r ? " (forced)" : "");
		return r || s_bForce;
	}

	bool SetShowProgressText(bool b) override
	{
		bool r = m->SetShowProgressText(b);
		ProbeLog("IGameUI SetShowProgressText show=%d ret=%d", b, r);
		return r;
	}

	void ShowNewGameDialog(int c) override
	{
		ProbeLog("IGameUI ShowNewGameDialog chapter=%d", c);
		m->ShowNewGameDialog(c);
	}

	void SessionNotification(const int n, const int p) override
	{
		ProbeLog("IGameUI SessionNotification n=%d param=%d", n, p);
		m->SessionNotification(n, p);
	}

	void SystemNotification(const int n) override
	{
		ProbeLog("IGameUI SystemNotification n=%d", n);
		m->SystemNotification(n);
	}

	void ShowMessageDialog(const unsigned int t, void* o) override
	{
		ProbeLog("IGameUI ShowMessageDialog type=%u owner=%p", t, o);
		m->ShowMessageDialog(t, o);
	}

	void UpdatePlayerInfo(uint64_t id, const char* n, int team, uint8_t v, int need, bool host) override
	{
		ProbeLog("IGameUI UpdatePlayerInfo id=%llu name=%s team=%d voice=%u need=%d host=%d", (unsigned long long)id,
				 S(n), team, v, need, host);
		m->UpdatePlayerInfo(id, n, team, v, need, host);
	}

	void SessionSearchResult(int i, void* h, void* r, int ping) override
	{
		ProbeLog("IGameUI SessionSearchResult %d", i);
		m->SessionSearchResult(i, h, r, ping);
	}

	void OnCreditsFinished() override
	{
		ProbeLog("IGameUI OnCreditsFinished");
		m->OnCreditsFinished();
	}

	void SetLoadingBackgroundDialog(VPANEL p) override
	{
		ProbeLog("IGameUI SetLoadingBackgroundDialog panel=%#lx", (unsigned long)p);
		m->SetLoadingBackgroundDialog(p);
	}

	void BonusMapUnlock(const char* f, const char* n) override
	{
		ProbeLog("IGameUI BonusMapUnlock %s %s", S(f), S(n));
		m->BonusMapUnlock(f, n);
	}

	void BonusMapComplete(const char* f, const char* n) override
	{
		ProbeLog("IGameUI BonusMapComplete %s %s", S(f), S(n));
		m->BonusMapComplete(f, n);
	}

	void BonusMapChallengeUpdate(const char* f, const char* n, const char* c, int b) override
	{
		ProbeLog("IGameUI BonusMapChallengeUpdate");
		m->BonusMapChallengeUpdate(f, n, c, b);
	}

	void BonusMapChallengeNames(char* f, char* n, char* c) override
	{
		ProbeLog("IGameUI BonusMapChallengeNames");
		m->BonusMapChallengeNames(f, n, c);
	}

	void BonusMapChallengeObjectives(int& b, int& s, int& g) override
	{
		ProbeLog("IGameUI BonusMapChallengeObjectives");
		m->BonusMapChallengeObjectives(b, s, g);
	}

	void BonusMapDatabaseSave() override
	{
		ProbeLog("IGameUI BonusMapDatabaseSave");
		m->BonusMapDatabaseSave();
	}

	int BonusMapNumAdvancedCompleted() override
	{
		int r = m->BonusMapNumAdvancedCompleted();
		ProbeLog("IGameUI BonusMapNumAdvancedCompleted ret=%d", r);
		return r;
	}

	void BonusMapNumMedals(int p[3]) override
	{
		ProbeLog("IGameUI BonusMapNumMedals");
		m->BonusMapNumMedals(p);
	}

	void OnConnectToServer2(const char* g, int ip, int cp, int qp) override
	{
		ProbeLog("IGameUI OnConnectToServer2 game=%s ip=%#x port=%d query=%d", S(g), (unsigned)ip, cp, qp);
		m->OnConnectToServer2(g, ip, cp, qp);
	}

	bool ValidateStorageDevice(int* p) override
	{
		bool r = m->ValidateStorageDevice(p);
		ProbeLog("IGameUI ValidateStorageDevice ret=%d", r);
		return r;
	}

	void SetProgressOnStart() override
	{
		ProbeLog("IGameUI SetProgressOnStart");
		m->SetProgressOnStart();
	}

	void OnDisconnectFromServer(uint8_t e) override
	{
		ProbeLog("IGameUI OnDisconnectFromServer failure=%u", e);
		m->OnDisconnectFromServer(e);
	}

	void OnConfirmQuit() override
	{
		ProbeLog("IGameUI OnConfirmQuit");
		m->OnConfirmQuit();
	}

	bool IsMainMenuVisible() override
	{
		bool r = m->IsMainMenuVisible();
		m_MenuVisible.n++;
		if ((int)r != m_MenuVisible.last)
		{
			ProbeLog("IGameUI IsMainMenuVisible ret=%d (call %lld)", r, m_MenuVisible.n);
			m_MenuVisible.last = r;
		}
		return r;
	}

	void SetMainMenuOverride(VPANEL p) override
	{
		ProbeLog("IGameUI SetMainMenuOverride panel=%#lx", (unsigned long)p);
		m->SetMainMenuOverride(p);
	}

	void SendMainMenuCommand(const char* c) override
	{
		ProbeLog("IGameUI SendMainMenuCommand %s", S(c));
		m->SendMainMenuCommand(c);
	}

private:
	IGameUIShape* m;
	Quiet m_RunFrame;
	Quiet m_MenuVisible;
	long long m_nProgress = 0;
};

//-----------------------------------------------------------------------------
// IGameConsole (GameConsole004): IBaseInterface's virtual destructor, then 6.
//-----------------------------------------------------------------------------
class IGameConsoleShape
{
public:
	virtual ~IGameConsoleShape() {}

	virtual void Activate() = 0;
	virtual void Initialize() = 0;
	virtual void Hide() = 0;
	virtual void Clear() = 0;
	virtual bool IsConsoleVisible() = 0;
	virtual void SetParent(VPANEL parent) = 0;
};

class CLoggingGameConsole : public IGameConsoleShape
{
public:
	explicit CLoggingGameConsole(IGameConsoleShape* p)
		: m(p)
	{
	}

	void Activate() override
	{
		ProbeLog("IGameConsole Activate");
		m->Activate();
	}

	void Initialize() override
	{
		ProbeLog("IGameConsole Initialize");
		m->Initialize();
	}

	void Hide() override
	{
		ProbeLog("IGameConsole Hide");
		m->Hide();
	}

	void Clear() override
	{
		ProbeLog("IGameConsole Clear");
		m->Clear();
	}

	bool IsConsoleVisible() override
	{
		bool r = m->IsConsoleVisible();
		m_n++;
		if ((int)r != m_last)
		{
			ProbeLog("IGameConsole IsConsoleVisible ret=%d (call %lld)", r, m_n);
			m_last = r;
		}
		return r;
	}

	void SetParent(VPANEL p) override
	{
		ProbeLog("IGameConsole SetParent panel=%#lx", (unsigned long)p);
		m->SetParent(p);
	}

private:
	IGameConsoleShape* m;
	long long m_n = 0;
	int m_last = -1;
};

CreateInterfaceFn StockFactory()
{
	static CreateInterfaceFn s_fn = NULL;
	static bool s_tried = false;
	if (s_tried)
		return s_fn;
	s_tried = true;
	const char* pPath = getenv("SOURCESHARP_PROBE_STOCK_GAMEUI");
	if (!pPath || !*pPath)
	{
		ProbeLog("stock-missing SOURCESHARP_PROBE_STOCK_GAMEUI unset");
		return NULL;
	}
	void* h = dlopen(pPath, RTLD_NOW | RTLD_LOCAL);
	if (!h)
	{
		ProbeLog("stock-dlopen-failed %s: %s", pPath, dlerror());
		return NULL;
	}
	s_fn = (CreateInterfaceFn)dlsym(h, "CreateInterface");
	ProbeLog("stock-loaded %s CreateInterface=%p", pPath, (void*)s_fn);
	return s_fn;
}

struct CLoadLine
{
	CLoadLine()
	{
		Dl_info info;
		ProbeLog("stub-loaded %s", dladdr((void*)&ModuleOf, &info) && info.dli_fname ? info.dli_fname : "?");
	}
} g_LoadLine;

} // namespace

extern "C" __attribute__((visibility("default"))) void* CreateInterface(const char* pName, int* pReturnCode)
{
	const char* pCaller = ModuleOf(__builtin_return_address(0));
	CreateInterfaceFn stock = StockFactory();
	void* p = stock ? stock(pName, pReturnCode) : NULL;
	ProbeLog("CreateInterface %s from %s -> %s", S(pName), pCaller, p ? "answered" : "NULL");
	if (!p || !pName)
		return p;
	if (!strcmp(pName, "GameUI011"))
	{
		static CLoggingGameUI* s_p = new CLoggingGameUI((IGameUIShape*)p);
		return s_p;
	}
	if (!strcmp(pName, "GameConsole004"))
	{
		static CLoggingGameConsole* s_p = new CLoggingGameConsole((IGameConsoleShape*)p);
		return s_p;
	}
	return p;
}
