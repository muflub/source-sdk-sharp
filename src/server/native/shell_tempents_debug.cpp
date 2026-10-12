//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_tempents.cpp's diagnostic console command =====//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
// The registry and the built tables stay shell_tempents.cpp's; this reads them
// through shell_tempents_internal.h.
//
//=============================================================================//

#include <string.h>
#include "tier0/dbg.h"
#include "tier1/convar.h"   // CCommand
#include "dt_send.h"
#include "server_class.h"
#include "tempent_schema.h"
#include "shell_tempents_internal.h"
#include "native_command_body.h"
#include "ss_string.h"

//-----------------------------------------------------------------------------
// Whether a table is one this file built.
//-----------------------------------------------------------------------------
static bool IsOurTable( const SendTable *pTable )
{
	for ( int i = 0; i < SOURCESHARP_TE_MAX_CLASSES; i++ )
	{
		if ( SourceSharpTe_BuiltTable( i ) == pTable )
			return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// A table of this name that STOCK still registers -- never one of ours.
//
// THE SECOND MEASUREMENT. Both columns of the dump below come, in the end, from
// the same C# declaration: the engine TRANSFORMS it (it flips a negated offset
// and sets a flag; SendPropFloat widens an unspecified width to NOSCALE) but it
// does not independently know what DT_TESparks should contain, so a width that
// is wrong and CONSISTENT is invisible to it. shell_nettable.cpp's warning about
// not comparing a computation with itself is exactly that, and it applies here.
//
// This is a different source: a prop list Valve typed into a te_*.cpp, in a file
// this port has not touched, still registered while the eviction is staged. It
// is what makes the pair of dumps two measurements rather than one printed
// twice -- and its client twin does the same against stock's RecvTable, so a
// managed table that agrees with stock on both sides agrees with itself across
// them by two independent references rather than by assertion.
//-----------------------------------------------------------------------------
static SendTable *FindStockTableIn( SendTable *pTable, const char *pName, int nDepth )
{
	if ( !pTable || nDepth > 8 )
		return NULL;

	if ( !IsOurTable( pTable ) && pTable->GetName()
	  && !strcmp( pTable->GetName(), pName ) )
		return pTable;

	for ( int i = 0; i < pTable->GetNumProps(); i++ )
	{
		SendProp *pProp = pTable->GetProp( i );

		if ( pProp->m_Type != DPT_DataTable )
			continue;

		SendTable *pFound = FindStockTableIn( pProp->GetDataTable(), pName, nDepth + 1 );

		if ( pFound )
			return pFound;
	}

	return NULL;
}

static SendTable *FindStockTableNamed( const char *pName )
{
	if ( !pName || !pName[0] )
		return NULL;

	for ( ServerClass *pClass = g_pServerClassHead; pClass; pClass = pClass->m_pNext )
	{
		SendTable *pFound = FindStockTableIn( pClass->m_pTable, pName, 0 );

		if ( pFound )
			return pFound;
	}

	return NULL;
}

//=============================================================================//
// THE DUMP — what the engine holds, beside what C# declared.
//
// cs_sv_payload_agree's rule applies verbatim: the left-hand side is
// pProp->GetOffset() and pProp->m_nBits AS THE ENGINE WILL USE THEM, and the
// right-hand side is the description C# handed over. Rederiving the offset here
// would compare a computation with itself and pass forever.
//
// It reports three things nothing else can:
//
//   the engine's post-InitTable view, which is where SENDINFO_VECTORELEM's
//     negated offset has been flipped back and turned into a flag — so a table
//     dumped after a map has loaded says whether that actually happened;
//   DUPLICATE TABLE NAMES across the whole server class list, which is the
//     transitional state a staged port cannot avoid: a managed base table has to
//     carry stock's name while stock's own copy is still registered, and this
//     says out loud how many of those there are rather than leaving it to be
//     discovered from a hung join;
//   which registered descriptions got a ServerClass and which did not.
//=============================================================================//

static const char *PropTypeName( int nType )
{
	switch ( nType )
	{
	case DPT_Int:       return "int";
	case DPT_Float:     return "float";
	case DPT_Vector:    return "vector";
	case DPT_VectorXY:  return "vectorxy";
	case DPT_String:    return "string";
	case DPT_Array:     return "array";
	case DPT_DataTable: return "datatable";
	}

	return "?";
}

static int CountTablesNamed( const char *pName )
{
	int nFound = 0;

	for ( ServerClass *pClass = g_pServerClassHead; pClass; pClass = pClass->m_pNext )
	{
		// Only the class's OWN table, not its nested ones: an intermediate table
		// is reached through several classes and counting it once per class
		// would report every base in the game as a duplicate.
		if ( pClass->m_pTable && pClass->m_pTable->GetName()
		  && !strcmp( pClass->m_pTable->GetName(), pName ) )
			nFound++;
	}

	return nFound;
}

SS_NATIVE_COMMAND( cs_sv_te_dump )
{
	const CSourceSharpTeRegistry &registry = SourceSharpTe_Registry();
	const char *pWanted = ( args.ArgC() > 1 ) ? args[1] : NULL;

	Msg( "[SourceSharp] --- temp entities: %d description(s), registration %s ---\n",
		registry.Count(), registry.IsClosed() ? "closed" : "OPEN" );

	int nAgreed = 0;
	int nChecked = 0;

	for ( int i = 0; i < registry.Count(); i++ )
	{
		const SourceSharpTeClass_t *pClass = registry.At( i );
		SendTable *pTable = SourceSharpTe_BuiltTable( i );

		if ( pWanted && ss::StrICmp( pClass->pClassName, pWanted )
		             && ss::StrICmp( pClass->pTableName, pWanted ) )
			continue;

		Msg( "%2d %-28s %-28s %s, %d blob byte(s), base %s\n",
			i, pClass->pClassName, pClass->pTableName,
			SourceSharpTe_BuiltClass( i ) ? "ServerClass" : "table only",
			pClass->nBlobBytes,
			pClass->pBaseTable ? pClass->pBaseTable : "(none)" );

		if ( !pTable )
		{
			Warning( "     no table was built\n" );
			continue;
		}

		// The transitional duplicate, named rather than assumed.
		// THE BUILT NAME, which is what the engine holds and what the other side
		// resolves on. Asking about the declared one would count stock's copy as
		// a duplicate of a table that is deliberately no longer called that.
		const int nSameName = CountTablesNamed( pTable->GetName() );

		if ( nSameName > 1 )
		{
			Msg( "     NOTE: %d ServerClass(es) publish a table named '%s'. That is what a "
			     "half-ported base table looks like — stock's copy is still registered — and "
			     "it is only safe because the two are prop-for-prop identical.\n",
				nSameName, pTable->GetName() );
		}

		// THE PLAN, walked beside the table the engine actually holds. Not
		// rederived here: the plan is the same list this file BUILT from and
		// the same list the client builds from, so the two columns are the
		// engine's answer and the description's, never one computation
		// compared with itself.
		const int nPlanned = SourceSharpTe_PlanCount( pClass );
		SourceSharpTePropPlan_t *pPlan =
			new SourceSharpTePropPlan_t[ nPlanned > 0 ? nPlanned : 1 ];

		const int nHave = SourceSharpTe_PlanProps( pClass, pPlan, nPlanned );

		for ( int iProp = 0; iProp < nHave && iProp < pTable->GetNumProps(); iProp++ )
		{
			SendProp *pProp = pTable->GetProp( iProp );

			// The engine's own view. GetOffset() is what SendTable_Encode will
			// add to the blob pointer, m_nBits is what it will spend, and the
			// flags are what it will branch on.
			const int nEngineOffset = pProp->GetOffset();
			const int nEngineFlags  = pProp->GetFlags();

			// A vector-elem prop's declared offset was NEGATED on the way in and
			// the engine flips it back. Agreement is on the POSITIVE offset the
			// plan carries plus the flag having been set, which is exactly the
			// thing this dump exists to observe rather than assume.
			const bool bNegated = pPlan[ iProp ].nDeclareOffset < 0;
			const bool bFlipped = bNegated && nEngineOffset == pPlan[ iProp ].nOffset
				&& ( nEngineFlags & SPROP_IS_A_VECTOR_ELEM );
			const bool bStillNegative = bNegated && nEngineOffset < 0;

			// A DPT_Array PROP HAS NO OFFSET OF ITS OWN, and this used to say the
			// opposite. MEASURED: InternalSendPropArray takes an element count,
			// a stride, a name and a length proxy and NO offset
			// (dt_send.h:784), so the engine holds zero here while the plan
			// carries the field's real blob offset for the ELEMENT TEMPLATE
			// beside it to use. cs_sv_te_dump and cs_cl_te_dump both printed
			// "*** OFFSET DISAGREES ***" against DT_TEBeamSpline for that
			// reason, identically, on the first live join with a managed array
			// -- and a line every reader learns to ignore is a line that hides
			// the next real disagreement.
			const bool bOffsetAgrees =
				( pPlan[ iProp ].nKind == SOURCESHARP_TE_PROP_ARRAY )
				|| ( nEngineOffset == pPlan[ iProp ].nOffset );

			nChecked++;
			if ( bOffsetAgrees )
				nAgreed++;

			Msg( "     %-24s %-9s engine off %5d bits %2d flags 0x%05x | "
			     "plan off %5d bits %2d flags 0x%05x%s%s%s\n",
				pProp->GetName(), PropTypeName( pProp->m_Type ),
				nEngineOffset, pProp->m_nBits, nEngineFlags,
				pPlan[ iProp ].nOffset, pPlan[ iProp ].nBits, pPlan[ iProp ].nFlags,
				bOffsetAgrees ? "" : "   *** OFFSET DISAGREES ***",
				bFlipped ? "  [vector elem, engine flipped it]" : "",
				bStillNegative ? "  [vector elem, STILL NEGATIVE — tables not built yet]" : "" );

			if ( pProp->m_Type == DPT_DataTable && pPlan[ iProp ].iField >= 0
			  && ( nEngineFlags & SPROP_COLLAPSIBLE ) )
			{
				Warning( "     *** NESTED TABLE IS COLLAPSIBLE: the receiver will apply it "
				         "at the object base and write over offset 0 ***\n" );
			}

			// THE LINE THE TWO DUMPS ARE DIFFED ON (plan 9, gate 2).
			//
			// Everything on it is comparable across the two sides and nothing
			// else is: a RecvProp has no m_nBits at all -- widths arrive with
			// the send table at connect -- and a SendProp's flags carry
			// SPROP_IS_A_VECTOR_ELEM and the NOSCALE that SendPropFloat ORs in,
			// neither of which the recv side has. What MUST match is the prop
			// ORDER, the NAME the two sides pair on, the TYPE, and the OFFSET
			// each engine will read through; a difference in any of those is a
			// client that begins joining and stops.
			//
			// Prefixed so `grep '^TE|'` on the two console logs is a diff and
			// not an eyeballing exercise.
			Msg( "TE|%s|%d|%s|%s|%d|%d|%d|0x%x|%s\n",
				pTable->GetName(), iProp, pProp->GetName(),
				PropTypeName( pProp->m_Type ), nEngineOffset,
				pPlan[ iProp ].nOffset, pPlan[ iProp ].nBits, pPlan[ iProp ].nFlags,
				// The NESTED TABLE, by the name the ENGINE holds -- which is the
				// half-ported hazard stated out loud: a client whose "baseclass"
				// points at stock's DT_TEParticleSystem while the server's points
				// at the managed one has two tables of one name and no symptom.
				( pProp->m_Type == DPT_DataTable && pProp->GetDataTable()
				  && pProp->GetDataTable()->GetName() )
					? pProp->GetDataTable()->GetName() : "" );
		}

		if ( nHave != pTable->GetNumProps() )
		{
			Warning( "     the plan has %d prop(s) and the built table has %d\n",
				nHave, pTable->GetNumProps() );
		}

		delete[] pPlan;

		// AND AGAINST STOCK'S OWN TABLE, where stock still has one -- the only
		// reading in this file that does not come from the C# declaration. See
		// FindStockTableNamed.
		SendTable *pStock = FindStockTableNamed( pClass->pTableName );

		if ( !pStock )
		{
			Msg( "     no stock table of this name is registered, so there is nothing "
			     "independent to check the prop list against\n" );
			continue;
		}

		int nSame = 0;

		Msg( "     STOCK '%s' has %d prop(s) against this table's %d\n",
			pStock->GetName(), pStock->GetNumProps(), pTable->GetNumProps() );

		for ( int iProp = 0; iProp < pTable->GetNumProps(); iProp++ )
		{
			SendProp *pMine = pTable->GetProp( iProp );

			if ( iProp >= pStock->GetNumProps() )
			{
				Warning( "     %-24s *** STOCK HAS NO PROP %d ***\n",
					pMine->GetName(), iProp );
				continue;
			}

			SendProp *pTheirs = pStock->GetProp( iProp );

			const bool bNameAgrees = pMine->GetName() && pTheirs->GetName()
				&& !strcmp( pMine->GetName(), pTheirs->GetName() );
			const bool bTypeAgrees = ( pMine->m_Type == pTheirs->m_Type );

			// THE WIDTH, WHICH IS THE ONE THING NOTHING ELSE HERE CAN SEE. Both
			// numbers are what the ENGINE will spend, one derived from C# and one
			// from Valve's own declaration, so a width that is wrong and
			// consistent across our two sides is caught HERE and nowhere else.
			const bool bBitsAgree = ( pMine->m_Type == DPT_DataTable )
				|| ( pMine->m_nBits == pTheirs->m_nBits );

			if ( bNameAgrees && bTypeAgrees && bBitsAgree )
			{
				nSame++;
				continue;
			}

			// The offsets are EXPECTED to differ and are not compared: stock's
			// address a C++ object and these address a blob.
			Warning( "     prop %d: this table has '%s' (%s, %d bits), stock has "
			         "'%s' (%s, %d bits)   *** DISAGREES ***\n",
				iProp, pMine->GetName(), PropTypeName( pMine->m_Type ), pMine->m_nBits,
				pTheirs->GetName(), PropTypeName( pTheirs->m_Type ), pTheirs->m_nBits );
		}

		for ( int iProp = pTable->GetNumProps(); iProp < pStock->GetNumProps(); iProp++ )
		{
			Warning( "     *** STOCK HAS '%s' AND THIS TABLE DOES NOT ***\n",
				pStock->GetProp( iProp )->GetName() );
		}

		Msg( "TESTOCK|%s|%d|%d|%d\n", pTable->GetName(),
			pTable->GetNumProps(), pStock->GetNumProps(), nSame );
	}

	Msg( "[SourceSharp] --- %d of %d prop offset(s) agree with the description ---\n",
		nAgreed, nChecked );

	if ( !registry.IsClosed() )
		Warning( "     registration is still OPEN, which it should not be after DLL init\n" );
}
