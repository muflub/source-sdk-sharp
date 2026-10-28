//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds, server only.

#include "tier0/dbg.h"
#include "gamestringpool_internal.h"
#include "native_command_body.h"

bool UTIL_IsCommandIssuedByServerAdmin( void );

// Entries are numbered by position; stock's RB-tree slot means nothing outside its tree.
static void DumpGameStringEntry( const char *pszString, void *pContext )
{
	int &i = *static_cast< int * >( pContext );
	DevMsg( "  %d (0x%p) : %s\n", i, (const void *)pszString, pszString );
	i++;
}

void CC_DumpGameStringTable( void )
{
	if ( !UTIL_IsCommandIssuedByServerAdmin() )
		return;

	int i = 0;
	GameStringPool_Visit( DumpGameStringEntry, &i );
	DevMsg( "\n" );
	DevMsg( "Size:  %d items\n", GameStringPool_Count() );
}
SS_NATIVE_COMMAND( dumpgamestringtable ) { CC_DumpGameStringTable(); }
