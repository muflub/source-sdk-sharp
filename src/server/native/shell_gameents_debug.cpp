//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds only; NativeCommands declares the matching rows under #if DEBUG.

#include "edict.h"

#include "shell_boundary_probe.h"
#include "native_command_body.h"
#include "server_services.h"

// Prints the engine-boundary probe's counters to the console (not the dev API
// log), headed by the map and tick they cover.
SS_NATIVE_COMMAND( ss_boundary_probe_report )
{
	const CGlobalVars *pGlobals = services.m_EntityContext.m_pGlobals;
	Msg( "[SourceSharp] engine-boundary probe (server): map '%s', tick %d\n",
		 pGlobals->mapname.ToCStr() ? pGlobals->mapname.ToCStr() : "(none)", pGlobals->tickcount );

	for ( int id = 0; id < kBoundaryProbeCount; id++ )
	{
		// The widest line: a 56-column name, three counts, sixteen 64-character callers.
		char szLine[2048];
		services.m_BoundaryProbes.FormatLine( id, szLine, sizeof( szLine ) );
		Msg( "  %s\n", szLine );
	}
}

// Lets one run separate what a map load calls from what play calls.
SS_NATIVE_COMMAND( ss_boundary_probe_reset )
{
	services.m_BoundaryProbes.Reset();
	Msg( "[SourceSharp] engine-boundary probe: counters zeroed\n" );
}
