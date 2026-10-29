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

// Interface for gameui to modify voice bans.
class CGameClientExports : public IGameClientExports
{
public:
	// ingame voice manipulation
	bool IsPlayerGameVoiceMuted( int playerIndex ) { return GetClientVoiceMgr()->IsPlayerBlocked( playerIndex ); }

	void MutePlayerGameVoice( int playerIndex ) { GetClientVoiceMgr()->SetPlayerBlockedState( playerIndex, true ); }

	void UnmutePlayerGameVoice( int playerIndex ) { GetClientVoiceMgr()->SetPlayerBlockedState( playerIndex, false ); }

	void OnGameUIActivated()
	{
		IGameEvent *event = gameeventmanager->CreateEvent( "gameui_activated" );
		if ( event )
		{
			gameeventmanager->FireEventClientSide( event );
		}
	}

	void OnGameUIHidden()
	{
		IGameEvent *event = gameeventmanager->CreateEvent( "gameui_hidden" );
		if ( event )
		{
			gameeventmanager->FireEventClientSide( event );
		}
	}

	// Stock's achievements interface is never set here and the econ strings are NULL
	// without econ items, so these answer nothing.
	void CreateAchievementsPanel( vgui::Panel *pParent ) {}

	void DisplayAchievementPanel() {}

	void ShutdownAchievementPanel() {}

	int GetAchievementsPanelMinWidth() const { return 0; }

	const char *GetHolidayString() { return NULL; }

	const char *GetOperationString() { return NULL; }
};

EXPOSE_SINGLE_INTERFACE( CGameClientExports, IGameClientExports, GAMECLIENTEXPORTS_INTERFACE_VERSION );
