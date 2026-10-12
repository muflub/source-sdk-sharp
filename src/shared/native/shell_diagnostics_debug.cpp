//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_diagnostics.cpp's diagnostic console commands ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching rows under #if DEBUG.
//
//=============================================================================//

#include "tier1/convar.h"                 // CCommand
#include "shell_diagnostics_commands.h"   // SourceSharp_PrintNotShipped, _PrintCrossings
#include "native_command_body.h"
#include "ss_string.h"

//-----------------------------------------------------------------------------
// The server's half of the not-shipped readout (plan_entity.md step 7g). The
// ledger is per DLL, so this answers about server.so and cs_cl_notshipped about
// client.so; shell_diagnostics.cpp has the reasoning and the shared body.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_notshipped )
{
	SourceSharp_PrintNotShipped();
}

//-----------------------------------------------------------------------------
// The server's name for the crossing counters (plan_entity.md 0c and 3.15).
// The table is ONE process-wide table, so this and cs_cl_crossings print the
// same counters; shell_diagnostics.cpp has the reasoning and the shared body.
// Native, so it answers even when the managed side has not come up.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_crossings )
{
	SourceSharp_PrintCrossings( args.ArgC() > 1 && ss::StrICmp( args.Arg( 1 ), "reset" ) == 0 );
}
