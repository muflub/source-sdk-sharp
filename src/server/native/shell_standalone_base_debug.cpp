//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_standalone_base.cpp's diagnostic console command ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
// The entity list stays shell_standalone_base.cpp's; this walks it through
// StandaloneEntities(), the accessor every other reader uses.
//
//=============================================================================//

#include "shell_standalone.h"
#include "slot_table.h"
#include "tier1/convar.h"   // CCommand
#include "native_command_body.h"

#include <vector>

std::vector< CStandaloneEntity * > &StandaloneEntities();

//-----------------------------------------------------------------------------
// THE SLOT TABLE, CHECKED AGAINST THE COMPILER, ON REAL ENTITIES.
//
// WHAT THIS USED TO BE. A typed-handle fixture: it built a typed handle over
// IGameEntity from each entity's own handle, resolved it, and compared the
// answer with the compiler's conversion from the concrete type. plan_entity.md
// 1e takes typed handles out of the shim, so the typed handle is gone and what
// stands in its place is the thing that replaced it -- the slot table of 3.3.
// The QUESTION is unchanged and so is every reason it is asked here rather
// than in a unit test.
//
// WHY A LIVE CHECK AND NOT A UNIT TEST. slot_table_test.cpp proves the table on
// a MOCK: a stand-in shell, bound and resolved. Every one of those tests is
// worth exactly as much as that premise, and a mock drifts from the class it
// stands for silently -- a reordered base list here would leave the suite green
// and meaningless. So this asks the real class, on real entities, four
// questions a mock answers by construction:
//
//   Do the two views actually differ?  The compiler's own conversion from
//        CStandaloneEntity to IGameEntity against the address the handle table
//        stores. Zero here means the hazard is not reproducible and the mock
//        suite is decoration.
//
//   IS THE ENTITY IN THE TABLE AT ALL?  SourceSharp_ShellFromHandle on the
//        entity's own handle. This is the question the whole of 1e turns on:
//        with nothing bound it answers NULL for every entity in the map, and
//        so does every caller that resolves through it -- Toucher_t::pGame,
//        CSoundPatch::EntIndex, CSoundPatch::Build. `bound 0` in
//        cs_sv_slot_table is the same fact counted rather than named.
//
//   Does the table agree with the compiler?  The resolved shell, crossed to
//        IGameEntity by the compiler from the concrete type, against the same
//        crossing performed directly. A disagreement here is a table that
//        holds the wrong pointer -- which is what handing SourceSharp_SlotBindAt
//        a base pointer instead of `this` would produce, silently.
//
//   Does the BY-INDEX arm agree with the by-handle one?  SourceSharp_ShellFromIndex
//        on the handle's entry. It is a separate arm of the table with no
//        serial to check, and it is the arm CSoundPatch::Build takes -- the
//        engine hands that function an edict INDEX and there is no serial
//        anywhere in the call. Nothing else in a live run exercises it, so it
//        is exercised here.
//
// Prints and counts; changes nothing. Safe under sv_cheats 0 because it is a
// read.
//
// MEASURED ON dm_lockdown, and both ways round -- the numbers are in
// .claude/entity-port/status/1e.md. With the bind removed from SetRefEHandle
// the whole map reads `resolved 0`, which is the state 1c and 1d both
// predicted and the state this command exists to make visible in one line.
//-----------------------------------------------------------------------------

SS_NATIVE_COMMAND( cs_sv_handle_cross_check )
{
	int nChecked   = 0;
	int nAgreed    = 0;
	int nDistinct  = 0;
	int nResolved  = 0;
	int nByIndex   = 0;
	const bool bVerbose = args.ArgC() > 1;

	for ( CStandaloneEntity *pEnt : StandaloneEntities() )
	{
		if ( !pEnt )
			continue;

		nChecked++;

		// The compiler's crossing, from the concrete type, where the layout is
		// a fact rather than an assumption. This is the known-good answer.
		// An UPCAST from the concrete type, which C++ performs on its own and
		// with the whole layout visible. It needed no cast to say so.
		IGameEntity   *pGood  = pEnt;

		// What the handle table holds, and what a (IGameEntity *) cast off it
		// would have handed every reader. Printed, never followed.
		IHandleEntity *pTable = static_cast< IServerEntity * >( pEnt );

		const long nDelta = ( long )( ( char * )pGood - ( char * )pTable );
		if ( nDelta != 0 )
			nDistinct++;

		// THE SLOT TABLE, exercised for real: the entity's own handle, the
		// table's serial check, and the one allowlisted downcast inside
		// SourceSharp_ShellFromHandle. Nothing here casts.
		const CBaseHandle handle = pEnt->GetRefEHandle();

		CStandaloneEntity *pShell   = SourceSharp_ShellFromHandle( handle );
		IGameEntity       *pCrossed = pShell;   // upcast, implicit

		if ( pCrossed )
			nResolved++;

		// The OTHER arm, on the same entity: entry index, no serial. This is
		// the route CSoundPatch::Build takes and the only live exercise it has.
		const bool bIndexAgrees =
			( SourceSharp_ShellFromIndex( handle.GetEntryIndex() ) == pShell );

		if ( bIndexAgrees )
			nByIndex++;

		const bool bAgrees = ( pCrossed == pGood );
		if ( bAgrees )
			nAgreed++;

		// ONE LINE PER ENTITY ONLY WHEN ASKED. dm_lockdown carries 472 of them
		// and the tail summary is the verdict; the per-entity rows are for the
		// run where the summary says something disagreed and the question is
		// WHICH. `cs_sv_handle_cross_check all` prints them.
		if ( bVerbose || !bAgrees || !pCrossed || !bIndexAgrees )
			Msg( "[SourceSharp]   %-28s handle %8x  compiler %p  table %p  %s"
			     "  (by-index %s, handle view %p, an unchecked cast would be off "
			     "by %ld bytes)\n",
				pEnt->GetClassname(), handle.ToInt(),
				(void *)pGood, (void *)pCrossed,
				bAgrees ? "agree" : "DISAGREE",
				bIndexAgrees ? "agrees" : "DISAGREES",
				(void *)pTable, nDelta );
	}

	Msg( "[SourceSharp] cs_sv_handle_cross_check: %d standalone entit%s, "
	     "%d resolved through the slot table, %d agreed with the compiler, "
	     "%d agreed by index, "
	     "%d had the two views at different addresses\n",
		nChecked, nChecked == 1 ? "y" : "ies", nResolved, nAgreed, nByIndex,
		nDistinct );

	if ( nChecked > 0 && nDistinct != nChecked )
		Msg( "[SourceSharp] WARNING: the game-entity view is at offset zero on "
		     "%d of them. handle_cross_test's mock no longer stands for this "
		     "class and its suite proves nothing.\n", nChecked - nDistinct );

	// THE 1e FAILURE, NAMED. An unbound table is not a subtle wrong answer: it
	// is NULL for everything, and every caller that resolves through it goes
	// quiet at once. Said here in the words of the symptom, because the symptom
	// is what somebody will be looking at.
	if ( nChecked > 0 && nResolved == 0 )
		Warning( "[SourceSharp] WARNING: NOTHING is bound in the slot table. "
		         "Toucher_t::pGame is NULL for every managed entity, "
		         "CSoundPatch::EntIndex answers -1 for every patch, and "
		         "CSoundPatch::Build attaches every looping sound to nothing. "
		         "CStandaloneEntity::SetRefEHandle is where the bind lives.\n" );
	else if ( nResolved != nChecked )
		Warning( "[SourceSharp] WARNING: %d of %d did not resolve through the "
		         "slot table.\n", nChecked - nResolved, nChecked );

	if ( nAgreed != nChecked )
		Warning( "[SourceSharp] WARNING: %d disagreed. The slot table is not "
		         "handing back the pointer that was bound -- which is what "
		         "binding a BASE pointer instead of the concrete shell "
		         "produces.\n", nChecked - nAgreed );

	if ( nByIndex != nChecked )
		Warning( "[SourceSharp] WARNING: %d disagreed by index. "
		         "SourceSharp_ShellFromIndex and SourceSharp_ShellFromHandle "
		         "answer different objects for the same entry.\n",
			nChecked - nByIndex );
}
