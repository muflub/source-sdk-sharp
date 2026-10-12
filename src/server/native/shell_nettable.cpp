//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the server's payload and type-table console ========//
// SS_REASON f  a bit-level wire codec
//
// This file built the runtime-networked entity's tier tables. That shell is
// deleted — it was a CBaseEntity, and step 6 makes CBaseEntity an alias for
// IGameEntity, which leaves the class abstract. The parallel hierarchy builds
// its tiers the same way in shell_standalone.cpp, and every managed entity is
// created through one of them.
//
// What is left is the console over the generic payload and the published type
// table, which was never about that shell: each command already fell through to
// the hierarchy when the index named something on it. With one shell left, the
// fall-through is the whole body.
//
// That console is diagnostic, so it now lives in shell_nettable_debug.cpp,
// which only a debug build compiles. What stays here is the managed-ServerClass
// identity check.
//
//=============================================================================//

#include "tier1/convar.h"
#include "shell_standalone.h"
#include "shell_diagnostics.h"
#include "generic_nettable.h"
#include "generic_payload.h"
#include "sourcesharp.h"
#include "native_command_body.h"
#include "ss_string.h"

// The three halves of Part 5's meter, each answered where its list lives.
bool SourceSharp_IsStandaloneTierServerClass( const ServerClass *pClass );
bool SourceSharp_IsTempEntServerClass( const ServerClass *pClass );
bool SourceSharp_IsWorldServerClass( const ServerClass *pClass );

//-----------------------------------------------------------------------------
// WHETHER A ServerClass IS ON THE WIRE BECAUSE C# PUT IT THERE.
//
// plan_gamedll.md Part 5 wants two numbers at DLLInit -- how many server
// classes came from managed declarations and how many are surviving SDK
// IMPLEMENT_SERVERCLASS registrations -- so that "how much of the class list is
// still stock" is a number printed at startup rather than an estimate, and so
// that a regression which re-adds twenty is seen rather than inferred.
//
// It cannot be answered by walking the chain, because there is only one chain:
// SourceSharp's ServerClasses link themselves into g_pServerClassHead from
// their own constructors exactly as the SDK's do. So it is answered by IDENTITY
// against the three places SourceSharp constructs one, and this function is the
// list of those places.
//
// A FOURTH SITE THAT DOES NOT ANSWER HERE IS COUNTED AS STOCK, which is the
// direction that fails safe: the meter then says there is more stock left than
// there is, which reads as work still to do rather than as work already done.
//
// THE WORLD IS THE THIRD, AND ADDING IT HERE IS EXACTLY THE EDIT PHASE A5 SAID
// MUST NOT HAPPEN -- until it moved.
//
// A5 recorded, correctly, that CWorld was counted as stock because it was
// registered "in sourcesharp/game/server/world_adapter.cpp -- our own file --
// using the SDK macro", and that "what must not happen is somebody moving the
// meter by adding world_adapter.cpp to that identity list: it would read as an
// eviction and evict nothing."
//
// That rule is unchanged and this is not that edit. The macro is gone from
// world_adapter.cpp, there is no g_CWorld_ClassReg in the binary any more, and
// the object SourceSharp_IsWorldServerClass answers about is constructed in
// shell_world_nettable.cpp out of a C# declaration whose props, names, order and
// blob layout are all managed. The class MOVED, so counting it as managed is a
// report of the move. StockServerClassCensus.OnlyCWorldIsRegisteredFromSource-
// SharpsOwnTree is what keeps the two apart: it asserts BOTH that the macro is
// absent AND that the answerer exists, so neither half alone gets past it.
//-----------------------------------------------------------------------------
bool SourceSharp_IsManagedServerClass( const ServerClass *pClass )
{
	return SourceSharp_IsStandaloneTierServerClass( pClass )
	    || SourceSharp_IsTempEntServerClass( pClass )
	    || SourceSharp_IsWorldServerClass( pClass );
}

// The payload and type-table console -- cs_sv_payload_agree, cs_sv_poke_payload,
// cs_sv_payload_probe, cs_sv_break_schema and cs_sv_dump_types -- is a set of
// diagnostics: it lives in shell_nettable_debug.cpp, which only a debug build
// compiles.
