//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_standalone_class.cpp's diagnostic console commands ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching rows under #if DEBUG.
//
//=============================================================================//

#include "tier0/dbg.h"
#include "tier1/convar.h"           // CCommand
#include "ss_util.h"                // UTIL_IsCommandIssuedByServerAdmin
#include "shell_standalone_class.h" // the factory dictionary's accessors
#include "native_command_body.h"

//-----------------------------------------------------------------------------
// `dumpentityfactories`, moved off util.cpp with the dictionary.
//
// SHAPED LIKE ITS CLIENT COUNTERPART, which is the lane brief's rule that both
// sides are implemented the same way. `cl_dumpentityfactories`
// (shell_diagnostics.cpp:1402) prints bare names, takes any argument as a
// request for detail, and ends with one `[SourceSharp]` summary line; the two
// dumps exist to be diffed against each other, so the server's format is the
// client's.
//
// TWO THINGS IT SAYS THAT STOCK'S DID NOT. Stock printed the names and stopped.
// This prints the entity size beside each in detail mode -- which is what the
// separate `dump_entity_sizes` command existed for, and it is now one command
// with two modes rather than two commands over one table -- and it prints the
// REFUSALS, the classnames this build was asked for and has no factory for.
// That second list is the whole of what p4-flip had to grep a console log to
// find.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( dumpentityfactories )
{
	if ( !UTIL_IsCommandIssuedByServerAdmin() )
		return;

	// THROUGH THE ACCESSORS, not through the table behind them, and that is
	// deliberate: they are the five entry points the ABI slot requested in
	// .claude/entity-port/abi-requests/sub-factory.md will be bound to, and a
	// function with no caller is a function the linker drops and nobody has
	// ever run. This command is what keeps all five live and exercised until
	// the managed side can call them.
	const bool bDetail = args.ArgC() > 1;
	const int  nCount  = SourceSharp_EntityFactoryCount();

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

	Msg( "[SourceSharp] and %d classname%s asked for that nothing answers to:\n",
	     nRefused, nRefused == 1 ? "" : "s" );

	for ( int i = 0; i < nRefused; i++ )
	{
		Msg( "    %-40s refused %d time(s)\n",
		     SourceSharp_EntityFactoryRefusalNameAt( i ),
		     SourceSharp_EntityFactoryRefusalTimesAt( i ) );
	}
}

