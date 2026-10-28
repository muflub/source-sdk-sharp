//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration

// Debug builds only.

#include <stdint.h>
#include "tier0/dbg.h"
#include "ss_platform.h"
#include "sourcesharp_entrypoints.h"
#include "shell_scripted_internal.h"
#include "native_command_body.h"

static void SourceSharp_PrintReachAddress( const char *pWhat, void *pAddress )
{
	SSModuleInfo info;

	// The offset survives the process; the raw pointer is ASLR'd. The nearest
	// exported symbol is only a hint.
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
