//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
//
// shell_standalone_class.cpp's diagnostic console commands. Debug builds only; managed
// NativeCommands declares the matching rows under #if DEBUG.

#include "ss_util.h"
#include "shell_entity_lifecycle.h"
#include "server_services.h"
#include "native_command_body.h"

// Lists the entity factories (with sizes when given an argument, matching the client's
// cl_dumpentityfactories) and the classnames asked for that no factory answers.
SS_NATIVE_COMMAND( dumpentityfactories )
{
	if ( !UTIL_IsCommandIssuedByServerAdmin() )
		return;

	CServerEntityLifecycle &lifecycle = services.m_EntityLifecycle;
	const bool bDetail = args.ArgC() > 1;
	const int nCount = lifecycle.EntityFactoryCount();

	for ( int i = 0; i < nCount; i++ )
	{
		const char *pName = lifecycle.EntityFactoryNameAt( i );

		if ( !bDetail )
		{
			Msg( "%s\n", pName );
			continue;
		}

		Msg( "%-40s %llu\n", pName, (uint64)lifecycle.EntityFactorySizeAt( i ) );
	}

	Msg( "[SourceSharp] %d entity factor%s\n", nCount, nCount == 1 ? "y" : "ies" );

	const int nRefused = lifecycle.EntityFactoryRefusalCount();

	if ( nRefused == 0 )
		return;

	Msg( "[SourceSharp] and %d classname%s asked for that nothing answers to:\n", nRefused, nRefused == 1 ? "" : "s" );

	for ( int i = 0; i < nRefused; i++ )
	{
		Msg( "    %-40s refused %d time(s)\n", lifecycle.EntityFactoryRefusalNameAt( i ),
			 lifecycle.EntityFactoryRefusalTimesAt( i ) );
	}
}
