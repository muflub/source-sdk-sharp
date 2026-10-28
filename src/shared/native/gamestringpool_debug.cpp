//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds, server only.

#include "tier0/dbg.h"
#include "native_command_body.h"
#include "server_services.h"
#include "server_state.h"
#include "ss_shareddefs.h"
#include "ss_world.h"
#include "shell_standalone_player.h"

bool UTIL_IsCommandIssuedByServerAdmin();

// The pools are their owners': the world's, then each player's. Entries are numbered by
// position; stock's RB-tree slot means nothing outside its tree.
static int DumpPool( const char *pszOwner, int nOwnerIndex, const CServerStringPool &pool, int i )
{
	DevMsg( "  %s %d:\n", pszOwner, nOwnerIndex );
	pool.Visit(
		[&i]( const char *pszString )
		{
			DevMsg( "  %d (0x%p) : %s\n", i, (const void *)pszString, pszString );
			i++;
		} );
	return i;
}

SS_NATIVE_COMMAND( dumpgamestringtable )
{
	if ( !UTIL_IsCommandIssuedByServerAdmin() )
		return;

	int i = 0;
	const CServerLevelState *pLevel = services.m_EntityContext.m_pLevel;
	if ( pLevel && pLevel->m_pWorld )
		i = DumpPool( "world", 0, pLevel->m_pWorld->Strings(), i );

	for ( int nIndex = 1; nIndex <= ss::kMaxPlayers; nIndex++ )
	{
		if ( const CStandalonePlayer *pPlayer = CStandalonePlayer::FromIndex( nIndex ) )
			i = DumpPool( "player", nIndex, pPlayer->Strings(), i );
	}

	DevMsg( "\n" );
	DevMsg( "Size:  %d items\n", i );
}
