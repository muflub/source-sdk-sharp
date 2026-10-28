//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
//
// Who typed the command: the console client slot the engine names, and the queries over
// it. Declarations stay in util.h.

#include "server_state.h"
#include "eiface.h"

extern CGlobalVars *gpGlobals;
#include "shell_standalone_player.h"
#include "server_engine.h"

// The slot from IServerGameClients::SetCommandClient, held by the DLL tier: 0-based,
// -1 for the dedicated console. The initial 0 is stock's.
static int &CommandClientSlot()
{
	AssertMsg( ShellGameDll_Dll(), "the command client was asked for outside DLLInit..DLLShutdown" );
	return ShellGameDll_Dll()->m_nCommandClientSlot;
}

// The engine naming the console client, from IServerGameClients::SetCommandClient.
void UTIL_SetCommandClientIndex( int index )
{
	CommandClientSlot() = index;
}

int UTIL_GetCommandClientIndex()
{
	// -1 == unknown,dedicated server console
	// 0  == player 1

	// Convert to 1 based offset
	return ( CommandClientSlot() + 1 );
}

// Returns true if the command was issued by the listenserver host, or by the dedicated
// server, via rcon or the server console. This is valid during ConCommand execution.
bool UTIL_IsCommandIssuedByServerAdmin()
{
	int issuingPlayerIndex = UTIL_GetCommandClientIndex();

	if ( ServerEngine()->IsDedicatedServer() && issuingPlayerIndex > 0 )
		return false;

	// This mod ships no Replay, so "not slot 1" is the whole question.
	if ( issuingPlayerIndex > 1 )
	{
		return false;
	}

	return true;
}
