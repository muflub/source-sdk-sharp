//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_gameents.cpp's diagnostic console commands ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching rows under #if DEBUG.
//
//=============================================================================//

#include "edict.h"            // CGlobalVars
#include "tier0/dbg.h"
#include "shell_boundary_probe.h"
#include "native_command_body.h"

// gpGlobals is defined in shell_engineboundary.cpp.
extern CGlobalVars *gpGlobals;

//-----------------------------------------------------------------------------
// THE READOUT -- plan_entity.md Phase 0 step 0e.
//
// HERE AND NOT IN shell_gameclients.cpp, arbitrarily but once: both files arm
// the same probe, so a report command in each would print the same table twice
// and invite the reader to believe one of them was the server's and the other
// the client's. Neither is; this is one game DLL.
//
// PRINTED WITH Msg AND NOT THROUGH THE DEV API, because debug-api's log is the
// managed one -- native output lands in console.log, which is where a run
// launched with -condebug leaves it, and where docs/engine-boundary.md's
// numbers are read from. `./debug-api cmd "ss_boundary_probe_report"` runs it;
// the answer appears in game/mod_sharp/console.log, not in `debug-api log`.
//
// THE MAP NAME AND THE TICK GO IN THE HEADER LINE. A count with no idea how
// much game it covers is the failure this whole step exists to avoid: "one map
// load hides a whole bug class", and a report that cannot say how many loads it
// spans cannot be checked for that.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( ss_boundary_probe_report )
{
	Msg( "[SourceSharp] engine-boundary probe (server): map '%s', tick %d\n",
	     gpGlobals->mapname.ToCStr() ? gpGlobals->mapname.ToCStr() : "(none)",
	     gpGlobals->tickcount );

	for ( int id = 0; id < kBoundaryProbeCount; id++ )
	{
		// Sized for the widest line the format can make: a 56-column name,
		// three counts, and sixteen callers of up to 64 characters each.
		char szLine[ 2048 ];
		BoundaryProbes().FormatLine( id, szLine, sizeof( szLine ) );
		Msg( "  %s\n", szLine );
	}
}

// Zeroing between stages is how one run answers "what does a map LOAD call"
// separately from "what does a minute of play call". Without it every number
// is cumulative from DLL load and the two questions have one answer.
SS_NATIVE_COMMAND( ss_boundary_probe_reset )
{
	BoundaryProbes().Reset();
	Msg( "[SourceSharp] engine-boundary probe: counters zeroed\n" );
}
