//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- IGameClientExports ================================//
// SS_REASON b  an interface resolved by name at load
//
// plan_entity.md Phase 5, the 5b split. ONE CLASS, ONE FILE.
//
// CGameClientExports came into sourcesharp/game/client/shell_clientdll.cpp
// with the rest of stock's cdll_client_int.cpp, which put ten classes in one
// 3,118-line translation unit. It is the gameui's channel into the client:
// voice bans, the two gameui_activated / gameui_hidden events, and the
// achievements panel. Nothing in it is about IBaseClientDLL, so nothing in it
// belongs beside IBaseClientDLL.
//
// It has NO state of its own -- every method forwards to a system that owns
// its own -- which is why this file has no globals and needs no proxy.
//
//===========================================================================//
#include "cbase.h"
#include "voice_status.h"
#include "igameevents.h"
#include "game/client/IGameClientExports.h"
#include "achievements_and_stats_interface.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// Purpose: interface for gameui to modify voice bans
//-----------------------------------------------------------------------------
class CGameClientExports : public IGameClientExports
{
public:
	// ingame voice manipulation
	bool IsPlayerGameVoiceMuted(int playerIndex)
	{
		return GetClientVoiceMgr()->IsPlayerBlocked(playerIndex);
	}

	void MutePlayerGameVoice(int playerIndex)
	{
		GetClientVoiceMgr()->SetPlayerBlockedState(playerIndex, true);
	}

	void UnmutePlayerGameVoice(int playerIndex)
	{
		GetClientVoiceMgr()->SetPlayerBlockedState(playerIndex, false);
	}

	void OnGameUIActivated( void )
	{
		IGameEvent *event = gameeventmanager->CreateEvent( "gameui_activated" );
		if ( event )
		{
			gameeventmanager->FireEventClientSide( event );
		}
	}

	void OnGameUIHidden( void )
	{
		IGameEvent *event = gameeventmanager->CreateEvent( "gameui_hidden" );
		if ( event )
		{
			gameeventmanager->FireEventClientSide( event );
		}
	}

    //=============================================================================
    // HPE_BEGIN
    // [dwenger] Necessary for stats display
    //=============================================================================

    void CreateAchievementsPanel( vgui::Panel* pParent )
    {
        if (g_pAchievementsAndStatsInterface)
        {
            g_pAchievementsAndStatsInterface->CreatePanel( pParent );
        }
    }

    void DisplayAchievementPanel()
    {
        if (g_pAchievementsAndStatsInterface)
        {
            g_pAchievementsAndStatsInterface->DisplayPanel();
        }
    }

    void ShutdownAchievementPanel()
    {
        if (g_pAchievementsAndStatsInterface)
        {
            g_pAchievementsAndStatsInterface->ReleasePanel();
        }
    }

	int GetAchievementsPanelMinWidth( void ) const
	{
        if ( g_pAchievementsAndStatsInterface )
        {
            return g_pAchievementsAndStatsInterface->GetAchievementsPanelMinWidth();
        }

		return 0;
	}

    //=============================================================================
    // HPE_END
    //=============================================================================

	const char *GetHolidayString()
	{
		return UTIL_GetActiveHolidayString();
	}

	const char *GetOperationString()
	{
		return UTIL_GetActiveOperationString();
	}
};

EXPOSE_SINGLE_INTERFACE( CGameClientExports, IGameClientExports, GAMECLIENTEXPORTS_INTERFACE_VERSION );
