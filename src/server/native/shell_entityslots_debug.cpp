//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_entityslots.cpp's diagnostic console command ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
//=============================================================================//

#include "const.h"            // MAX_EDICTS
#include "tier0/dbg.h"
#include "slot_table.h"
#include "sourcesharp.h"
#include "native_command_body.h"

//-----------------------------------------------------------------------------
// What the slot table is holding. A DELTA is the measurement that matters here
// too: bound should return to its level after a round of spawns and removes,
// and a refusal count that moves is a bind the shell asked for and did not get.
//
// It prints no list, on purpose. There is nothing here that could produce one:
// see slot_table.h on why the table has no iteration API and on the five
// must-not-compile legs that keep it that way.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_slot_table )
{
	const CSourceSharpSlotTable &table = SourceSharp_SlotTable();

	Msg( "[SourceSharp] slot table: %d entries (%d edict, %d engine-invisible)\n",
	     CSourceSharpSlotTable::EntryCount(), MAX_EDICTS,
	     CSourceSharpSlotTable::EntryCount() - MAX_EDICTS );
	Msg( "  bound %d  binds %d  unbinds %d  refused %d\n",
	     table.BoundCount(), table.Binds(), table.Unbinds(), table.Refused() );
}
