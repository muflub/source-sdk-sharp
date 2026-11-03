//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The engine's GameUI (plan_gameui_managed.md G4): GameUI011 and
// GameConsole004 answered in managed. The engine loads the stock GameUI
// module and resolves its factory through its own dlsym import; the swap
// below answers that one lookup with this file's factory instead, so the
// stock module is loaded and never called. Calls before the managed root
// exists are held (gameui_early_calls.h) and played to it at the first call
// after; a question before then answers no.

#include "GameUI/IGameUI.h"
#include "GameUI/IGameConsole.h"
#include "imanaged_areas_client.h"
#include "import_slot.h"
#include "gameui_early_calls.h"
#include "shell_gameui.h"

#include <string.h>

#if defined(_WIN32)
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace
{

GameUiEarly::Queue s_Early;

void Play(IManagedGameUiClient* pManaged, const GameUiEarly::Entry& entry)
{
	using GameUiEarly::Call;
	void* pArg = reinterpret_cast<void*>(entry.arg);
	switch (entry.call)
	{
		case Call::Initialize:
			pManaged->Initialize(pArg);
			break;
		case Call::PostInit:
			pManaged->PostInit();
			break;
		case Call::Connect:
			pManaged->Connect(pArg);
			break;
		case Call::Start:
			pManaged->Start();
			break;
		case Call::Shutdown:
			pManaged->Shutdown();
			break;
		case Call::OnGameUIActivated:
			pManaged->OnGameUIActivated();
			break;
		case Call::OnGameUIHidden:
			pManaged->OnGameUIHidden();
			break;
		case Call::OnLevelLoadingStarted:
			pManaged->OnLevelLoadingStarted(entry.arg != 0);
			break;
		case Call::SetLoadingBackgroundDialog:
			pManaged->SetLoadingBackgroundDialog(pArg);
			break;
		case Call::SetMainMenuOverride:
			pManaged->SetMainMenuOverride(pArg);
			break;
		case Call::ConsoleActivate:
			pManaged->ConsoleActivate();
			break;
		case Call::ConsoleInitialize:
			pManaged->ConsoleInitialize();
			break;
		case Call::ConsoleHide:
			pManaged->ConsoleHide();
			break;
		case Call::ConsoleClear:
			pManaged->ConsoleClear();
			break;
		case Call::ConsoleSetParent:
			pManaged->ConsoleSetParent(pArg);
			break;
	}
}

// The managed GameUI, the held calls played to it first; NULL before it exists.
IManagedGameUiClient* Managed()
{
	IManagedGameUiClient* pManaged = ManagedGameUiClient();
	if (pManaged && s_Early.Count() > 0)
	{
		auto sink = [pManaged](const GameUiEarly::Entry& entry)
		{
			Play(pManaged, entry);
		};
		s_Early.Replay(sink);
	}
	return pManaged;
}

// A call with no answer: to managed, or held until it exists.
void Notify(GameUiEarly::Call call, uintptr_t arg = 0)
{
	if (IManagedGameUiClient* pManaged = Managed())
	{
		GameUiEarly::Entry entry = {call, arg};
		Play(pManaged, entry);
	}
	else
		s_Early.Push(call, arg);
}

class SSGameUI final : public IGameUI
{
public:
	void Initialize(CreateInterfaceFn appFactory) override
	{
		Notify(GameUiEarly::Call::Initialize, reinterpret_cast<uintptr_t>(appFactory));
	}

	void PostInit() override { Notify(GameUiEarly::Call::PostInit); }

	void Connect(CreateInterfaceFn gameFactory) override
	{
		Notify(GameUiEarly::Call::Connect, reinterpret_cast<uintptr_t>(gameFactory));
	}

	void Start() override { Notify(GameUiEarly::Call::Start); }

	void Shutdown() override { Notify(GameUiEarly::Call::Shutdown); }

	void RunFrame() override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->RunFrame();
	}

	void OnGameUIActivated() override { Notify(GameUiEarly::Call::OnGameUIActivated); }

	void OnGameUIHidden() override { Notify(GameUiEarly::Call::OnGameUIHidden); }

	void OLD_OnConnectToServer(const char* game, int IP, int port) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->OldOnConnectToServer(game, IP, port);
	}

	void OnDisconnectFromServer_OLD(uint8 eSteamLoginFailure, const char* username) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->OnDisconnectFromServerOld(eSteamLoginFailure, username);
	}

	void OnLevelLoadingStarted(bool bShowProgressDialog) override
	{
		Notify(GameUiEarly::Call::OnLevelLoadingStarted, bShowProgressDialog ? 1 : 0);
	}

	void OnLevelLoadingFinished(bool bError, const char* failureReason, const char* extendedReason) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->OnLevelLoadingFinished(bError, failureReason, extendedReason);
	}

	bool UpdateProgressBar(float progress, const char* statusText) override
	{
		IManagedGameUiClient* pManaged = Managed();
		return pManaged && pManaged->UpdateProgressBar(progress, statusText) != 0;
	}

	bool SetShowProgressText(bool show) override
	{
		IManagedGameUiClient* pManaged = Managed();
		return pManaged && pManaged->SetShowProgressText(show) != 0;
	}

	void ShowNewGameDialog(int chapter) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->ShowNewGameDialog(chapter);
	}

	void SessionNotification(const int notification, const int param) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->SessionNotification(notification, param);
	}

	void SystemNotification(const int notification) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->SystemNotification(notification);
	}

	void ShowMessageDialog(const uint nType, vgui::Panel* pOwner) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->ShowMessageDialog(nType, pOwner);
	}

	void UpdatePlayerInfo(uint64 nPlayerId, const char* pName, int nTeam, byte cVoiceState, int nPlayersNeeded,
						  bool bHost) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->UpdatePlayerInfo(nPlayerId, pName, nTeam, cVoiceState, nPlayersNeeded, bHost);
	}

	void SessionSearchResult(int searchIdx, void* pHostData, XSESSION_SEARCHRESULT* pResult, int ping) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->SessionSearchResult(searchIdx, pHostData, pResult, ping);
	}

	void OnCreditsFinished() override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->OnCreditsFinished();
	}

	void SetLoadingBackgroundDialog(vgui::VPANEL panel) override
	{
		Notify(GameUiEarly::Call::SetLoadingBackgroundDialog, static_cast<uintptr_t>(panel));
	}

	void BonusMapUnlock(const char* pchFileName, const char* pchMapName) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->BonusMapUnlock(pchFileName, pchMapName);
	}

	void BonusMapComplete(const char* pchFileName, const char* pchMapName) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->BonusMapComplete(pchFileName, pchMapName);
	}

	void BonusMapChallengeUpdate(const char* pchFileName, const char* pchMapName, const char* pchChallengeName,
								 int iBest) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->BonusMapChallengeUpdate(pchFileName, pchMapName, pchChallengeName, iBest);
	}

	// The single-player surface's out-parameters, answered empty here
	// (docs/absence-client.md §16).
	void BonusMapChallengeNames(char* pchFileName, char* pchMapName, char* pchChallengeName) override
	{
		if (pchFileName)
			pchFileName[0] = 0;
		if (pchMapName)
			pchMapName[0] = 0;
		if (pchChallengeName)
			pchChallengeName[0] = 0;
	}

	void BonusMapChallengeObjectives(int& iBronze, int& iSilver, int& iGold) override { iBronze = iSilver = iGold = 0; }

	void BonusMapDatabaseSave() override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->BonusMapDatabaseSave();
	}

	int BonusMapNumAdvancedCompleted() override
	{
		IManagedGameUiClient* pManaged = Managed();
		return pManaged ? pManaged->BonusMapNumAdvancedCompleted() : 0;
	}

	void BonusMapNumMedals(int piNumMedals[3]) override
	{
		if (piNumMedals)
			piNumMedals[0] = piNumMedals[1] = piNumMedals[2] = 0;
	}

	void OnConnectToServer2(const char* game, int IP, int connectionPort, int queryPort) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->OnConnectToServer2(game, IP, connectionPort, queryPort);
	}

	bool ValidateStorageDevice(int* pStorageDeviceValidated) override
	{
		IManagedGameUiClient* pManaged = Managed();
		const bool bValid = pManaged && pManaged->ValidateStorageDevice() != 0;
		if (pStorageDeviceValidated)
			*pStorageDeviceValidated = bValid ? 1 : 0;
		return bValid;
	}

	void SetProgressOnStart() override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->SetProgressOnStart();
	}

	void OnDisconnectFromServer(uint8 eSteamLoginFailure) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->OnDisconnectFromServer(eSteamLoginFailure);
	}

	void OnConfirmQuit() override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->OnConfirmQuit();
	}

	bool IsMainMenuVisible() override
	{
		IManagedGameUiClient* pManaged = Managed();
		return pManaged && pManaged->IsMainMenuVisible() != 0;
	}

	void SetMainMenuOverride(vgui::VPANEL panel) override
	{
		Notify(GameUiEarly::Call::SetMainMenuOverride, static_cast<uintptr_t>(panel));
	}

	void SendMainMenuCommand(const char* pszCommand) override
	{
		if (IManagedGameUiClient* pManaged = Managed())
			pManaged->SendMainMenuCommand(pszCommand);
	}
};

class SSGameConsole final : public IGameConsole
{
public:
	void Activate() override { Notify(GameUiEarly::Call::ConsoleActivate); }

	void Initialize() override { Notify(GameUiEarly::Call::ConsoleInitialize); }

	void Hide() override { Notify(GameUiEarly::Call::ConsoleHide); }

	void Clear() override { Notify(GameUiEarly::Call::ConsoleClear); }

	bool IsConsoleVisible() override
	{
		IManagedGameUiClient* pManaged = Managed();
		return pManaged && pManaged->ConsoleIsVisible() != 0;
	}

	void SetParent(vgui::VPANEL parent) override
	{
		Notify(GameUiEarly::Call::ConsoleSetParent, static_cast<uintptr_t>(parent));
	}
};

SSGameUI s_GameUI;
SSGameConsole s_GameConsole;

// The factory the engine is answered with: GameUI011 and GameConsole004.
void* GameUiFactory(const char* pszName, int* pReturnCode)
{
	void* pInterface = nullptr;
	if (pszName && strcmp(pszName, GAMEUI_INTERFACE_VERSION) == 0)
		pInterface = &s_GameUI;
	else if (pszName && strcmp(pszName, GAMECONSOLE_INTERFACE_VERSION) == 0)
		pInterface = &s_GameConsole;
	if (pReturnCode)
		*pReturnCode = pInterface ? IFACE_OK : IFACE_FAILED;
	return pInterface;
}

// The swapped import: the slot and what it held.
struct Swap
{
	void** pSlot;
	void* pOriginal;
};

Swap s_Swap;

#if defined(_WIN32)
const char kEngine[] = "engine.dll";
const char kLookup[] = "GetProcAddress";
#else
const char kEngine[] = "/engine.so";
const char kLookup[] = "dlsym";
#endif

// The one lookup answered differently: CreateInterface resolving inside the
// stock GameUI module. The import is put back first, so the swap lives for
// that lookup alone.
void* Answer(void* pFound, const char* pszName)
{
	if (!pFound || !pszName || !s_Swap.pSlot || strcmp(pszName, "CreateInterface") != 0)
		return pFound;
	if (!ShellGameUi_IsStockModule(pFound))
		return pFound;
	ImportSlot::Write(s_Swap.pSlot, s_Swap.pOriginal);
	s_Swap.pSlot = nullptr;
	return reinterpret_cast<void*>(&GameUiFactory);
}

#if defined(_WIN32)
FARPROC WINAPI SwapLookup(HMODULE hModule, LPCSTR pszName)
{
	FARPROC pFound = GetProcAddress(hModule, pszName);
	// An ordinal is no name (its high word is zero).
	if (reinterpret_cast<uintptr_t>(pszName) >> 16 == 0)
		return pFound;
	return reinterpret_cast<FARPROC>(Answer(reinterpret_cast<void*>(pFound), pszName));
}
#else
void* SwapLookup(void* hModule, const char* pszName)
{
	return Answer(dlsym(hModule, pszName), pszName);
}
#endif

} // namespace

bool ShellGameUi_IsStockModule(void* pAddress)
{
#if defined(_WIN32)
	HMODULE hOwner = nullptr;
	if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
							static_cast<LPCSTR>(pAddress), &hOwner))
		return false;
	char szPath[MAX_PATH];
	const DWORD nLength = GetModuleFileNameA(hOwner, szPath, sizeof(szPath));
	return nLength > 0 && nLength < sizeof(szPath) && ImportSlot::EndsWith(szPath, "\\GameUI.dll");
#else
	Dl_info info;
	return dladdr(pAddress, &info) != 0 && ImportSlot::EndsWith(info.dli_fname, "/GameUI.so");
#endif
}

bool ShellGameUi_InstallFactorySwap()
{
	if (s_Swap.pSlot)
		return true;
	void** pSlot = ImportSlot::Find(kEngine, kLookup);
	if (!pSlot)
		return false;
	s_Swap.pOriginal = *pSlot;
	s_Swap.pSlot = pSlot;
	if (ImportSlot::Write(pSlot, reinterpret_cast<void*>(&SwapLookup)))
		return true;
	s_Swap.pSlot = nullptr;
	return false;
}
