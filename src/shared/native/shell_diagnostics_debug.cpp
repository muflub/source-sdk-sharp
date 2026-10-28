//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds only.

#include "tier1/convar.h"
#include "shell_diagnostics_commands.h"
#include "native_command_body.h"
#include "ss_string.h"

// The not-shipped ledger is per DLL, so this answers about server.so.
SS_NATIVE_COMMAND( cs_sv_notshipped )
{
	SourceSharp_PrintNotShipped();
}

// One process-wide table, so this and cs_cl_crossings print the same counters.
SS_NATIVE_COMMAND( cs_sv_crossings )
{
	SourceSharp_PrintCrossings( args.ArgC() > 1 && ss::StrICmp( args.Arg( 1 ), "reset" ) == 0 );
}
