//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Two small interfaces the engine and the GameUI fetch by name, members of the client DLL
// object: the shared app systems the engine asks for before it loads the DLL's systems, and
// the GameUI's channel into the client.

#pragma once

#include "appframework/IAppSystemGroup.h"
#include "cdll_int.h"
#include "utlvector.h"

class CVoiceStatus;

namespace vgui
{
class Panel;
}

#include "game/client/IGameClientExports.h"

// The shared app systems: soundemittersystem and scenefilecache.
class CGameUiClient;

class CClientDLLSharedAppSystems final : public IClientDLLSharedAppSystems
{
public:
	explicit CClientDLLSharedAppSystems(CGameUiClient& gameUi);

	// The engine's first call into the client, in CModAppSystemGroup::Create,
	// before Host_Init loads the GameUI module: the GameUI factory swap goes in
	// here (plan_gameui_managed.md G4).
	virtual int Count();

	virtual char const* GetDllName(int idx);

	virtual char const* GetInterfaceName(int idx);

private:
	void AddAppSystem(char const* moduleName, char const* interfaceName);

	CGameUiClient& m_GameUi;
	CUtlVector<AppSystemInfo_t> m_Systems;
};

// Voice bans, the gameui_activated and gameui_hidden events, and the achievements panel this
// client does not have.
class CClientEngineInterfaces;

class CGameClientExports final : public IGameClientExports
{
public:
	CGameClientExports(const CClientEngineInterfaces& engines, CVoiceStatus& voice)
		: m_Engines(engines)
		, m_Voice(voice)
	{
	}

	// ingame voice manipulation
	bool IsPlayerGameVoiceMuted(int playerIndex);

	void MutePlayerGameVoice(int playerIndex);

	void UnmutePlayerGameVoice(int playerIndex);

	void OnGameUIActivated();

	void OnGameUIHidden();

	// Stock's achievements interface is never set here and the econ strings are NULL
	// without econ items, so these answer nothing.
	void CreateAchievementsPanel(vgui::Panel* pParent) {}

	void DisplayAchievementPanel() {}

	void ShutdownAchievementPanel() {}

	int GetAchievementsPanelMinWidth() const;

	const char* GetHolidayString();

	const char* GetOperationString();

private:
	const CClientEngineInterfaces& m_Engines;
	CVoiceStatus& m_Voice;
};
