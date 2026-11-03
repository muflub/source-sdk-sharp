//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The gameui's channel into the client: voice bans, the gameui_activated and
// gameui_hidden events, and the achievements panel this client does not have.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "voice_status.h"

#include "game/client/IGameClientExports.h"
#include "ienginevgui.h"
#include "gameui_edge.h"

// shell_clientdll.cpp's, set in its Init.
extern IEngineVGui* enginevgui;

// One fire of a client-side game event, the whole of both exports' bodies and
// of the visibility edge below.
static void FireClientGameEvent(const char* pName)
{
	IGameEvent* event = gameeventmanager->CreateEvent(pName);
	if (event)
	{
		gameeventmanager->FireEventClientSide(event);
	}
}

// The client frame's watch (CShellClientDLL::HudUpdate). The engine hands its
// GameUI activation to the exports above only when -game names tf exactly
// (gameui_edge.h), so the client answers the engine's own visibility itself
// and fires the same two events on the edge.
static GameUIVisibility s_GameUIVisibility;

void SourceSharp_GameUIEdge()
{
	if (!gameeventmanager || !enginevgui)
		return;

	const bool bVisible = enginevgui->IsGameUIVisible();

	switch (s_GameUIVisibility.Update(bVisible))
	{
		case GameUIEdge::Activated:
			FireClientGameEvent("gameui_activated");
			break;
		case GameUIEdge::Hidden:
			FireClientGameEvent("gameui_hidden");
			break;
		default:
			break;
	}

	// Every frame, not only on the edge: the root and the gameui's own root can
	// move after the edge named them (the launch). The follow early-outs.
	SourceSharp_MenuRootFollowGameUI(bVisible);
}

// Interface for gameui to modify voice bans.
class CGameClientExports : public IGameClientExports
{
public:
	// ingame voice manipulation
	bool IsPlayerGameVoiceMuted(int playerIndex) { return GetClientVoiceMgr()->IsPlayerBlocked(playerIndex); }

	void MutePlayerGameVoice(int playerIndex) { GetClientVoiceMgr()->SetPlayerBlockedState(playerIndex, true); }

	void UnmutePlayerGameVoice(int playerIndex) { GetClientVoiceMgr()->SetPlayerBlockedState(playerIndex, false); }

	void OnGameUIActivated() { FireClientGameEvent("gameui_activated"); }

	void OnGameUIHidden() { FireClientGameEvent("gameui_hidden"); }

	// Stock's achievements interface is never set here and the econ strings are NULL
	// without econ items, so these answer nothing.
	void CreateAchievementsPanel(vgui::Panel* pParent) {}

	void DisplayAchievementPanel() {}

	void ShutdownAchievementPanel() {}

	int GetAchievementsPanelMinWidth() const { return 0; }

	const char* GetHolidayString() { return NULL; }

	const char* GetOperationString() { return NULL; }
};

EXPOSE_SINGLE_INTERFACE(CGameClientExports, IGameClientExports, GAMECLIENTEXPORTS_INTERFACE_VERSION);
