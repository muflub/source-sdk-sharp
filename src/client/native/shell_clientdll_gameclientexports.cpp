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
#include "shell_clientdll_exports.h"

// shell_clientdll.cpp's, set in its Init.

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

// Interface for gameui to modify voice bans.
bool CGameClientExports::IsPlayerGameVoiceMuted(int playerIndex)
{
	return m_Voice.IsPlayerBlocked(playerIndex);
}

void CGameClientExports::MutePlayerGameVoice(int playerIndex)
{
	m_Voice.SetPlayerBlockedState(playerIndex, true);
}

void CGameClientExports::UnmutePlayerGameVoice(int playerIndex)
{
	m_Voice.SetPlayerBlockedState(playerIndex, false);
}

void CGameClientExports::OnGameUIActivated()
{
	FireClientGameEvent("gameui_activated");
}

void CGameClientExports::OnGameUIHidden()
{
	FireClientGameEvent("gameui_hidden");
}

int CGameClientExports::GetAchievementsPanelMinWidth() const
{
	return 0;
}

const char* CGameClientExports::GetHolidayString()
{
	return NULL;
}

const char* CGameClientExports::GetOperationString()
{
	return NULL;
}
