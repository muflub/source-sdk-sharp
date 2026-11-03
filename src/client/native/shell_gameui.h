// The engine's GameUI answered in managed (plan_gameui_managed.md G4,
// shell_gameui.cpp).
#pragma once

#include <stdint.h>

#include "GameUI/IGameUI.h"
#include "GameUI/IGameConsole.h"
#include "bound_callback.h"
#include "gameui_early_calls.h"

class CGameUiClient;
class IManagedGameUiClient;
struct CManagedAreasClient;

// GameConsole004, a member of CGameUiClient.
class CGameConsoleClient final : public IGameConsole
{
public:
	explicit CGameConsoleClient(CGameUiClient& owner)
		: m_Owner(owner)
	{
	}

	void Activate() override;

	void Initialize() override;

	void Hide() override;

	void Clear() override;

	bool IsConsoleVisible() override;

	void SetParent(vgui::VPANEL parent) override;

private:
	CGameUiClient& m_Owner;
};

// GameUI011, and the swap that hands it to the engine. A member of the client DLL object.
class CGameUiClient final : public IGameUI
{
public:
	explicit CGameUiClient(const CManagedAreasClient& areas);

	CGameUiClient(const CGameUiClient&) = delete;
	CGameUiClient& operator=(const CGameUiClient&) = delete;

	// Swaps engine's import of the symbol lookup (dlsym; GetProcAddress on
	// Windows) so the one lookup of the stock GameUI module's CreateInterface
	// answers this object's factory; the import is put back at that lookup.
	// Called at the first call the engine makes into the client, before it
	// loads the GameUI module. False when the import is not found, and the
	// stock module then answers.
	bool InstallFactorySwap();

	// The managed GameUI, the held calls played to it first; NULL before it exists.
	IManagedGameUiClient* Managed();

	// A call with no answer: to managed, or held until it exists.
	void Notify(GameUiEarly::Call call, uintptr_t arg = 0);

	void Initialize(CreateInterfaceFn appFactory) override;

	void PostInit() override;

	void Connect(CreateInterfaceFn gameFactory) override;

	void Start() override;

	void Shutdown() override;

	void RunFrame() override;

	void OnGameUIActivated() override;

	void OnGameUIHidden() override;

	void OLD_OnConnectToServer(const char* game, int IP, int port) override;

	void OnDisconnectFromServer_OLD(uint8 eSteamLoginFailure, const char* username) override;

	void OnLevelLoadingStarted(bool bShowProgressDialog) override;

	void OnLevelLoadingFinished(bool bError, const char* failureReason, const char* extendedReason) override;

	bool UpdateProgressBar(float progress, const char* statusText) override;

	bool SetShowProgressText(bool show) override;

	void ShowNewGameDialog(int chapter) override;

	void SessionNotification(const int notification, const int param) override;

	void SystemNotification(const int notification) override;

	void ShowMessageDialog(const uint nType, vgui::Panel* pOwner) override;

	void UpdatePlayerInfo(uint64 nPlayerId, const char* pName, int nTeam, byte cVoiceState, int nPlayersNeeded,
						  bool bHost) override;

	void SessionSearchResult(int searchIdx, void* pHostData, XSESSION_SEARCHRESULT* pResult, int ping) override;

	void OnCreditsFinished() override;

	void SetLoadingBackgroundDialog(vgui::VPANEL panel) override;

	void BonusMapUnlock(const char* pchFileName, const char* pchMapName) override;

	void BonusMapComplete(const char* pchFileName, const char* pchMapName) override;

	void BonusMapChallengeUpdate(const char* pchFileName, const char* pchMapName, const char* pchChallengeName,
								 int iBest) override;

	// The single-player surface's out-parameters, answered empty here
	// (docs/absence-client.md §16).
	void BonusMapChallengeNames(char* pchFileName, char* pchMapName, char* pchChallengeName) override;

	void BonusMapChallengeObjectives(int& iBronze, int& iSilver, int& iGold) override;

	void BonusMapDatabaseSave() override;

	int BonusMapNumAdvancedCompleted() override;

	void BonusMapNumMedals(int piNumMedals[3]) override;

	void OnConnectToServer2(const char* game, int IP, int connectionPort, int queryPort) override;

	bool ValidateStorageDevice(int* pStorageDeviceValidated) override;

	void SetProgressOnStart() override;

	void OnDisconnectFromServer(uint8 eSteamLoginFailure) override;

	void OnConfirmQuit() override;

	bool IsMainMenuVisible() override;

	void SetMainMenuOverride(vgui::VPANEL panel) override;

	void SendMainMenuCommand(const char* pszCommand) override;

private:
	typedef CBoundCallback<void*(const char*, int*)> Factory_t;
	typedef CBoundCallback<void*(void*, const char*)> Lookup_t;

	void* Factory(const char* pszName, int* pReturnCode);
	void* SwapLookup(void* hModule, const char* pszName);
	void* Answer(void* pFound, const char* pszName);

	const CManagedAreasClient& m_Areas;
	GameUiEarly::Queue m_Early;
	CGameConsoleClient m_Console;
	Factory_t m_Factory;
	Lookup_t m_SwapLookup;

	// The swapped import: the slot and what it held.
	void** m_pSwapSlot = nullptr;
	void* m_pSwapOriginal = nullptr;
};

// Whether pAddress lies in the stock GameUI module (GameUI.so, GameUI.dll).
bool ShellGameUi_IsStockModule(void* pAddress);

// The menu layer's root under the GameUI root while the engine's GameUI is up,
// and under the client viewport otherwise (shell_hud.cpp); once a frame.
