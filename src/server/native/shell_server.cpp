//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load

// Server-side symbols server_base.vpc expects a mod to supply; game decisions
// route into C#.

#include "shell_gamerules.h"
#include "shell_standalone_player.h"
#include "shell_diagnostics.h"

#include "shell_gameclients.h"

#include "shell_server.h"

#include "ss_shareddefs.h"

void InstallGameRules()
{
	SOURCESHARP_TRACE();
	SourceSharp_CreateGameRules();
}

void ClientPutInServer( edict_t *pEntity, const char *playername )
{
	// The engine hands us an edict and expects the mod to attach a player
	// entity to it. Nothing spawns without this.
	SOURCESHARP_TRACE_MSG( playername );

	// Create fails only if managed registered no player class. Fail loudly: a client
	// admitted with no player entity dies somewhere unrelated later.
	if ( !StandalonePlayer_Create( pEntity, playername ) )
	{
		Error( "[SourceSharp] ClientPutInServer: managed side registered no player class — cannot put '%s' in server\n",
			   playername );
		return;
	}

	SourceSharp_OnClientPutInServer( pEntity, playername );
}

void ClientActive( edict_t *pEdict, bool bLoadGame )
{
	SOURCESHARP_TRACE();

	// Spawns when the client is fully connected, as stock's player does.
	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEdict ) )
		pStandalone->Spawn();

	SourceSharp_OnClientActive( pEdict, bLoadGame );
}
