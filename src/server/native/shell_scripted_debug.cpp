//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_scripted.cpp's diagnostic console command =====//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
// What it reads -- the engine-invisible range counter and the call sites it
// kept -- is shell_scripted.cpp's, with the whole argument for counting it
// there; this file reads it through SourceSharp_InvisibleRangeReach and
// shell_scripted_internal.h.
//
//=============================================================================//

#include <stdint.h>
#include "tier0/dbg.h"
#include "ss_platform.h"   // SS_ModuleOfAddress
#include "sourcesharp_entrypoints.h"   // SourceSharp_InvisibleRangeReach
#include "shell_scripted_internal.h"
#include "native_command_body.h"

//-----------------------------------------------------------------------------
// One address, as the file and offset addr2line takes. Static so the command
// below reads the same way for both frames of a site.
//-----------------------------------------------------------------------------
static void SourceSharp_PrintReachAddress( const char *pWhat, void *pAddress )
{
	SSModuleInfo info;

	// The nearest exported symbol is, for a static entry point, somebody
	// else's -- so it is a hint beside the offset and not the answer (and
	// Windows gives none). The OFFSET is what survives the process: the raw
	// pointer is ASLR'd and means nothing once the game has exited.
	if ( SS_ModuleOfAddress( pAddress, &info ) )
	{
		Msg( "    %-9s %p  %s+0x%lx  (near %s)\n",
		     pWhat, pAddress,
		     info.szPath[ 0 ] ? info.szPath : "?",
		     (unsigned long)( (const char *)pAddress - (const char *)info.pBase ),
		     info.pszSymbol ? info.pszSymbol : "?" );
	}
	else
	{
		Msg( "    %-9s %p\n", pWhat, pAddress );
	}
}

SS_NATIVE_COMMAND( cs_sv_invisible_reach )
{
	const int32_t nReach = SourceSharp_InvisibleRangeReach();
	bool bCapped = false;
	const int nSites = ShellScripted_InvisibleReachSiteCount( &bCapped );

	Msg( "[SourceSharp] engine-invisible range reach: %d\n", nReach );

	if ( nReach == 0 )
	{
		Msg( "  nothing crossed. Read this beside status.logical.live -- a build that "
		     "made no shell-less entities would read zero here too.\n" );
		return;
	}

	Msg( "  %d distinct call site(s)%s; resolve with "
	     "addr2line -f -e game/mod_sharp/bin/linux64/server.so <addr>\n",
	     nSites, bCapped ? " (capped)" : "" );

	for ( int i = 0; i < nSites; i++ )
	{
		SourceSharp_PrintReachAddress( "resolved", ShellScripted_InvisibleReachSite( i ) );
	}
}
