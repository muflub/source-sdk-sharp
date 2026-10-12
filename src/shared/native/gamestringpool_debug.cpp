//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — gamestringpool.cpp's diagnostic console command =====//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
// SERVER ONLY, so the `#ifndef CLIENT_DLL` the body sat under in
// gamestringpool.cpp (a file both DLLs compile) is not repeated here: this file
// is on server.so's debug list and nowhere else.
//
//=============================================================================//

#include "tier0/dbg.h"
#include "gamestringpool_internal.h"
#include "native_command_body.h"

// The admin check, from shell_commandclient.cpp.
bool UTIL_IsCommandIssuedByServerAdmin( void );

// stock also guarded on `!defined( GC )` -- the Steam GC build, which this tree
// has no link line for; tools/dead-conditionals refuses a shim TU conditioning
// on a define no build carries, so the arm this build takes is what is written.

// In order, numbered by position (the RB-tree slot stock printed has no
// meaning outside its tree). This was CGameStringPool::Dump's loop body; the
// pool is walked through GameStringPool_Visit now that the dump lives here.
static void DumpGameStringEntry( const char *pszString, void *pContext )
{
	int &i = *static_cast< int * >( pContext );
	DevMsg( "  %d (0x%p) : %s\n", i, (const void *)pszString, pszString );
	i++;
}

//------------------------------------------------------------------------------
// Purpose:
//------------------------------------------------------------------------------
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
