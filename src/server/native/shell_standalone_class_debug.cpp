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
#include "shell_standalone_class.h"
#include "native_command_body.h"

// Lists the entity factories (with sizes when given an argument, matching the client's
// cl_dumpentityfactories) and the classnames asked for that no factory answers.
SS_NATIVE_COMMAND( dumpentityfactories )
{
	if ( !UTIL_IsCommandIssuedByServerAdmin() )
		return;

	const bool bDetail = args.ArgC() > 1;
	const int nCount = SourceSharp_EntityFactoryCount();

	for ( int i = 0; i < nCount; i++ )
	{
		const char *pName = SourceSharp_EntityFactoryNameAt( i );

		if ( !bDetail )
		{
			Msg( "%s\n", pName );
			continue;
		}

		Msg( "%-40s %llu\n", pName, (uint64)SourceSharp_EntityFactorySizeAt( i ) );
	}

	Msg( "[SourceSharp] %d entity factor%s\n", nCount, nCount == 1 ? "y" : "ies" );

	const int nRefused = SourceSharp_EntityFactoryRefusalCount();

	if ( nRefused == 0 )
		return;

	Msg( "[SourceSharp] and %d classname%s asked for that nothing answers to:\n", nRefused, nRefused == 1 ? "" : "s" );

	for ( int i = 0; i < nRefused; i++ )
	{
		Msg( "    %-40s refused %d time(s)\n", SourceSharp_EntityFactoryRefusalNameAt( i ),
			 SourceSharp_EntityFactoryRefusalTimesAt( i ) );
	}
}
