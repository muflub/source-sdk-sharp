//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration

// Debug builds only.

#include "slot_table.h"

#include "native_command_body.h"

// The slot table's counts. Bound should return to its level after spawns and
// removes; a growing refusal count is a bind the shell did not get.
SS_NATIVE_COMMAND( cs_sv_slot_table )
{
	const CSourceSharpSlotTable &table = SourceSharp_SlotTable();

	Msg( "[SourceSharp] slot table: %d entries (%d edict, %d engine-invisible)\n", CSourceSharpSlotTable::EntryCount(),
		 MAX_EDICTS, CSourceSharpSlotTable::EntryCount() - MAX_EDICTS );
	Msg( "  bound %d  binds %d  unbinds %d  refused %d\n", table.BoundCount(), table.Binds(), table.Unbinds(),
		 table.Refused() );
}
