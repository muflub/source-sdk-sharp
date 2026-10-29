//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
//
// Who typed the command: the question asked of the console client slot the engine names
// (CServerPlayersDll::m_nCommandClientSlot).

#include "server_state.h"
#include "server_entity_context.h"
#include "eiface.h"

// Returns true if the command was issued by the listenserver host, or by the dedicated
// server, via rcon or the server console. This is valid during ConCommand execution.
bool CServerEntityContext::CommandIssuedByServerAdmin() const
{
	// The slot is 0-based, -1 for the dedicated console; as an entindex, 0 is the console
	// and 1 is player 1.
	const int issuingPlayerIndex = m_Players.m_nCommandClientSlot + 1;

	if (m_pEngine->IsDedicatedServer() && issuingPlayerIndex > 0)
		return false;

	// This mod ships no Replay, so "not slot 1" is the whole question.
	if (issuingPlayerIndex > 1)
	{
		return false;
	}

	return true;
}
