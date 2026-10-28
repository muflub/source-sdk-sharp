//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
//
// shell_standalone_base.cpp's diagnostic console command. Debug builds only; managed
// NativeCommands declares the matching row under #if DEBUG.

#include "shell_standalone.h"
#include "slot_table.h"
#include "tier1/convar.h"
#include "native_command_body.h"

std::vector<CStandaloneEntity *> &StandaloneEntities();

// Checks the slot table against the compiler on live entities: that the IGameEntity and
// handle views differ, that each entity resolves by handle, that the resolved shell
// matches the compiler's upcast, and that the by-index arm agrees. Read only.

SS_NATIVE_COMMAND( cs_sv_handle_cross_check )
{
	int nChecked = 0;
	int nAgreed = 0;
	int nDistinct = 0;
	int nResolved = 0;
	int nByIndex = 0;
	const bool bVerbose = args.ArgC() > 1;

	for ( CStandaloneEntity *pEnt : StandaloneEntities() )
	{
		if ( !pEnt )
			continue;

		nChecked++;

		// The compiler's upcast from the concrete type: the known-good answer.
		IGameEntity *pGood = pEnt;

		// What the handle table holds. Printed, never followed.
		IHandleEntity *pTable = static_cast<IServerEntity *>( pEnt );

		const long nDelta = (long)( (char *)pGood - (char *)pTable );
		if ( nDelta != 0 )
			nDistinct++;

		// The entity's own handle through the table's serial check.
		const CBaseHandle handle = pEnt->GetRefEHandle();

		CStandaloneEntity *pShell = SourceSharp_ShellFromHandle( handle );
		IGameEntity *pCrossed = pShell;

		if ( pCrossed )
			nResolved++;

		// By entry index, with no serial: the route CSoundPatch::Build takes.
		const bool bIndexAgrees = ( SourceSharp_ShellFromIndex( handle.GetEntryIndex() ) == pShell );

		if ( bIndexAgrees )
			nByIndex++;

		const bool bAgrees = ( pCrossed == pGood );
		if ( bAgrees )
			nAgreed++;

		// One line per entity only with an argument or on a disagreement.
		if ( bVerbose || !bAgrees || !pCrossed || !bIndexAgrees )
			Msg( "[SourceSharp]   %-28s handle %8x  compiler %p  table %p  %s"
				 "  (by-index %s, handle view %p, an unchecked cast would be off "
				 "by %ld bytes)\n",
				 pEnt->GetClassname(), handle.ToInt(), (void *)pGood, (void *)pCrossed, bAgrees ? "agree" : "DISAGREE",
				 bIndexAgrees ? "agrees" : "DISAGREES", (void *)pTable, nDelta );
	}

	Msg( "[SourceSharp] cs_sv_handle_cross_check: %d standalone entit%s, "
		 "%d resolved through the slot table, %d agreed with the compiler, "
		 "%d agreed by index, "
		 "%d had the two views at different addresses\n",
		 nChecked, nChecked == 1 ? "y" : "ies", nResolved, nAgreed, nByIndex, nDistinct );

	if ( nChecked > 0 && nDistinct != nChecked )
		Msg( "[SourceSharp] WARNING: the game-entity view is at offset zero on "
			 "%d of them. handle_cross_test's mock no longer stands for this "
			 "class and its suite proves nothing.\n",
			 nChecked - nDistinct );

	// An unbound table answers NULL for everything; name the symptoms.
	if ( nChecked > 0 && nResolved == 0 )
		Warning( "[SourceSharp] WARNING: NOTHING is bound in the slot table. "
				 "Toucher_t::pGame is NULL for every managed entity, "
				 "CSoundPatch::EntIndex answers -1 for every patch, and "
				 "CSoundPatch::Build attaches every looping sound to nothing. "
				 "CStandaloneEntity::SetRefEHandle is where the bind lives.\n" );
	else if ( nResolved != nChecked )
		Warning( "[SourceSharp] WARNING: %d of %d did not resolve through the "
				 "slot table.\n",
				 nChecked - nResolved, nChecked );

	if ( nAgreed != nChecked )
		Warning( "[SourceSharp] WARNING: %d disagreed. The slot table is not "
				 "handing back the pointer that was bound -- which is what "
				 "binding a BASE pointer instead of the concrete shell "
				 "produces.\n",
				 nChecked - nAgreed );

	if ( nByIndex != nChecked )
		Warning( "[SourceSharp] WARNING: %d disagreed by index. "
				 "SourceSharp_ShellFromIndex and SourceSharp_ShellFromHandle "
				 "answer different objects for the same entry.\n",
				 nChecked - nByIndex );
}
