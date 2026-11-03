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

#include "imanaged_areas_client.h"
#include "shell_managed_areas_client.h"
#include "import_slot.h"
#include "shell_gameui.h"

#include <string.h>

#if defined(_WIN32)
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace
{

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

#if defined(_WIN32)
const char kEngine[] = "engine.dll";
const char kLookup[] = "GetProcAddress";
#else
const char kEngine[] = "/engine.so";
const char kLookup[] = "dlsym";
#endif

} // namespace

void CGameUiClient::Initialize(CreateInterfaceFn appFactory)
{
	Notify(GameUiEarly::Call::Initialize, reinterpret_cast<uintptr_t>(appFactory));
}

void CGameUiClient::PostInit()
{
	Notify(GameUiEarly::Call::PostInit);
}

void CGameUiClient::Connect(CreateInterfaceFn gameFactory)
{
	Notify(GameUiEarly::Call::Connect, reinterpret_cast<uintptr_t>(gameFactory));
}

void CGameUiClient::Start()
{
	Notify(GameUiEarly::Call::Start);
}

void CGameUiClient::Shutdown()
{
	Notify(GameUiEarly::Call::Shutdown);
}

void CGameUiClient::RunFrame()
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->RunFrame();
}

void CGameUiClient::OnGameUIActivated()
{
	Notify(GameUiEarly::Call::OnGameUIActivated);
}

void CGameUiClient::OnGameUIHidden()
{
	Notify(GameUiEarly::Call::OnGameUIHidden);
}

void CGameUiClient::OLD_OnConnectToServer(const char* game, int IP, int port)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->OldOnConnectToServer(game, IP, port);
}

void CGameUiClient::OnDisconnectFromServer_OLD(uint8 eSteamLoginFailure, const char* username)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->OnDisconnectFromServerOld(eSteamLoginFailure, username);
}

void CGameUiClient::OnLevelLoadingStarted(bool bShowProgressDialog)
{
	Notify(GameUiEarly::Call::OnLevelLoadingStarted, bShowProgressDialog ? 1 : 0);
}

void CGameUiClient::OnLevelLoadingFinished(bool bError, const char* failureReason, const char* extendedReason)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->OnLevelLoadingFinished(bError, failureReason, extendedReason);
}

bool CGameUiClient::UpdateProgressBar(float progress, const char* statusText)
{
	IManagedGameUiClient* pManaged = Managed();
	return pManaged && pManaged->UpdateProgressBar(progress, statusText) != 0;
}

bool CGameUiClient::SetShowProgressText(bool show)
{
	IManagedGameUiClient* pManaged = Managed();
	return pManaged && pManaged->SetShowProgressText(show) != 0;
}

void CGameUiClient::ShowNewGameDialog(int chapter)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->ShowNewGameDialog(chapter);
}

void CGameUiClient::SessionNotification(const int notification, const int param)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->SessionNotification(notification, param);
}

void CGameUiClient::SystemNotification(const int notification)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->SystemNotification(notification);
}

void CGameUiClient::ShowMessageDialog(const uint nType, vgui::Panel* pOwner)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->ShowMessageDialog(nType, pOwner);
}

void CGameUiClient::UpdatePlayerInfo(uint64 nPlayerId, const char* pName, int nTeam, byte cVoiceState,
									 int nPlayersNeeded, bool bHost)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->UpdatePlayerInfo(nPlayerId, pName, nTeam, cVoiceState, nPlayersNeeded, bHost);
}

void CGameUiClient::SessionSearchResult(int searchIdx, void* pHostData, XSESSION_SEARCHRESULT* pResult, int ping)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->SessionSearchResult(searchIdx, pHostData, pResult, ping);
}

void CGameUiClient::OnCreditsFinished()
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->OnCreditsFinished();
}

void CGameUiClient::SetLoadingBackgroundDialog(vgui::VPANEL panel)
{
	Notify(GameUiEarly::Call::SetLoadingBackgroundDialog, static_cast<uintptr_t>(panel));
}

void CGameUiClient::BonusMapUnlock(const char* pchFileName, const char* pchMapName)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->BonusMapUnlock(pchFileName, pchMapName);
}

void CGameUiClient::BonusMapComplete(const char* pchFileName, const char* pchMapName)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->BonusMapComplete(pchFileName, pchMapName);
}

void CGameUiClient::BonusMapChallengeUpdate(const char* pchFileName, const char* pchMapName,
											const char* pchChallengeName, int iBest)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->BonusMapChallengeUpdate(pchFileName, pchMapName, pchChallengeName, iBest);
}

void CGameUiClient::BonusMapChallengeNames(char* pchFileName, char* pchMapName, char* pchChallengeName)
{
	if (pchFileName)
		pchFileName[0] = 0;
	if (pchMapName)
		pchMapName[0] = 0;
	if (pchChallengeName)
		pchChallengeName[0] = 0;
}

void CGameUiClient::BonusMapChallengeObjectives(int& iBronze, int& iSilver, int& iGold)
{
	iBronze = iSilver = iGold = 0;
}

void CGameUiClient::BonusMapDatabaseSave()
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->BonusMapDatabaseSave();
}

int CGameUiClient::BonusMapNumAdvancedCompleted()
{
	IManagedGameUiClient* pManaged = Managed();
	return pManaged ? pManaged->BonusMapNumAdvancedCompleted() : 0;
}

void CGameUiClient::BonusMapNumMedals(int piNumMedals[3])
{
	if (piNumMedals)
		piNumMedals[0] = piNumMedals[1] = piNumMedals[2] = 0;
}

void CGameUiClient::OnConnectToServer2(const char* game, int IP, int connectionPort, int queryPort)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->OnConnectToServer2(game, IP, connectionPort, queryPort);
}

bool CGameUiClient::ValidateStorageDevice(int* pStorageDeviceValidated)
{
	IManagedGameUiClient* pManaged = Managed();
	const bool bValid = pManaged && pManaged->ValidateStorageDevice() != 0;
	if (pStorageDeviceValidated)
		*pStorageDeviceValidated = bValid ? 1 : 0;
	return bValid;
}

void CGameUiClient::SetProgressOnStart()
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->SetProgressOnStart();
}

void CGameUiClient::OnDisconnectFromServer(uint8 eSteamLoginFailure)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->OnDisconnectFromServer(eSteamLoginFailure);
}

void CGameUiClient::OnConfirmQuit()
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->OnConfirmQuit();
}

bool CGameUiClient::IsMainMenuVisible()
{
	IManagedGameUiClient* pManaged = Managed();
	return pManaged && pManaged->IsMainMenuVisible() != 0;
}

void CGameUiClient::SetMainMenuOverride(vgui::VPANEL panel)
{
	Notify(GameUiEarly::Call::SetMainMenuOverride, static_cast<uintptr_t>(panel));
}

void CGameUiClient::SendMainMenuCommand(const char* pszCommand)
{
	if (IManagedGameUiClient* pManaged = Managed())
		pManaged->SendMainMenuCommand(pszCommand);
}

void CGameConsoleClient::Activate()
{
	m_Owner.Notify(GameUiEarly::Call::ConsoleActivate);
}

void CGameConsoleClient::Initialize()
{
	m_Owner.Notify(GameUiEarly::Call::ConsoleInitialize);
}

void CGameConsoleClient::Hide()
{
	m_Owner.Notify(GameUiEarly::Call::ConsoleHide);
}

void CGameConsoleClient::Clear()
{
	m_Owner.Notify(GameUiEarly::Call::ConsoleClear);
}

bool CGameConsoleClient::IsConsoleVisible()
{
	IManagedGameUiClient* pManaged = m_Owner.Managed();
	return pManaged && pManaged->ConsoleIsVisible() != 0;
}

void CGameConsoleClient::SetParent(vgui::VPANEL parent)
{
	m_Owner.Notify(GameUiEarly::Call::ConsoleSetParent, static_cast<uintptr_t>(parent));
}

CGameUiClient::CGameUiClient(const CManagedAreasClient& areas)
	: m_Areas(areas)
	, m_Console(*this)
	, m_Factory(this, &Factory_t::Trampoline<CGameUiClient, &CGameUiClient::Factory>)
	, m_SwapLookup(this, &Lookup_t::Trampoline<CGameUiClient, &CGameUiClient::SwapLookup>)
{
}

// The managed GameUI, the held calls played to it first; NULL before it exists.
IManagedGameUiClient* CGameUiClient::Managed()
{
	IManagedGameUiClient* pManaged = m_Areas.m_pGameUiClient;
	if (pManaged && m_Early.Count() > 0)
	{
		auto sink = [pManaged](const GameUiEarly::Entry& entry)
		{
			Play(pManaged, entry);
		};
		m_Early.Replay(sink);
	}
	return pManaged;
}

// A call with no answer: to managed, or held until it exists.
void CGameUiClient::Notify(GameUiEarly::Call call, uintptr_t arg)
{
	if (IManagedGameUiClient* pManaged = Managed())
	{
		GameUiEarly::Entry entry = {call, arg};
		Play(pManaged, entry);
	}
	else
		m_Early.Push(call, arg);
}

// The factory the engine is answered with: GameUI011 and GameConsole004.
void* CGameUiClient::Factory(const char* pszName, int* pReturnCode)
{
	void* pInterface = nullptr;
	if (pszName && strcmp(pszName, GAMEUI_INTERFACE_VERSION) == 0)
		pInterface = static_cast<IGameUI*>(this);
	else if (pszName && strcmp(pszName, GAMECONSOLE_INTERFACE_VERSION) == 0)
		pInterface = static_cast<IGameConsole*>(&m_Console);
	if (pReturnCode)
		*pReturnCode = pInterface ? IFACE_OK : IFACE_FAILED;
	return pInterface;
}

// The one lookup answered differently: CreateInterface resolving inside the
// stock GameUI module. The import is put back first, so the swap lives for
// that lookup alone.
void* CGameUiClient::Answer(void* pFound, const char* pszName)
{
	if (!pFound || !pszName || !m_pSwapSlot || strcmp(pszName, "CreateInterface") != 0)
		return pFound;
	if (!ShellGameUi_IsStockModule(pFound))
		return pFound;
	ImportSlot::Write(m_pSwapSlot, m_pSwapOriginal);
	m_pSwapSlot = nullptr;
	return reinterpret_cast<void*>(m_Factory.Get());
}

void* CGameUiClient::SwapLookup(void* hModule, const char* pszName)
{
#if defined(_WIN32)
	FARPROC pFound = GetProcAddress(static_cast<HMODULE>(hModule), pszName);
	// An ordinal is no name (its high word is zero).
	if (reinterpret_cast<uintptr_t>(pszName) >> 16 == 0)
		return reinterpret_cast<void*>(pFound);
	return Answer(reinterpret_cast<void*>(pFound), pszName);
#else
	return Answer(dlsym(hModule, pszName), pszName);
#endif
}

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

bool CGameUiClient::InstallFactorySwap()
{
	if (m_pSwapSlot)
		return true;
	void** pSlot = ImportSlot::Find(kEngine, kLookup);
	if (!pSlot)
		return false;
	m_pSwapOriginal = *pSlot;
	m_pSwapSlot = pSlot;
	if (ImportSlot::Write(pSlot, reinterpret_cast<void*>(m_SwapLookup.Get())))
		return true;
	m_pSwapSlot = nullptr;
	return false;
}
