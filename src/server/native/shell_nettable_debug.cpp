//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
//
// shell_nettable.cpp's diagnostic console commands. Debug builds only; managed
// NativeCommands declares the matching rows under #if DEBUG.

#include "tier0/dbg.h"
#include "tier1/convar.h"
#include "generic_nettable.h"
#include "native_command_body.h"
#include "ss_string.h"

// Implemented beside the standalone hierarchy, which walks its own entities.
void SourceSharp_StandalonePayloadAgreeReport( int nWantedIndex, int *pOutChecked, int *pOutAgreed );
bool SourceSharp_StandalonePokePayload( int nIndex, int iWord, uint32 nValue );
bool SourceSharp_StandaloneProbePayload( int nIndex, uint32 nSeed, int *pOutWords );
bool SourceSharp_StandaloneBreakSchema( int nIndex, uint32 nHash );

// Whether each payload prop, read at the offset the engine uses, sees the bytes C#
// wrote into the blob.
SS_NATIVE_COMMAND( cs_sv_payload_agree )
{
	const int nWanted = args.ArgC() > 1 ? ss::AtoI( args[1] ) : -1;

	int nChecked = 0;
	int nAgreed  = 0;

	SourceSharp_StandalonePayloadAgreeReport( nWanted, &nChecked, &nAgreed );

	if ( nChecked == 0 )
	{
		Warning( "[SourceSharp] payload agreement: no entity carrying a payload%s\n",
			nWanted >= 0 ? " at that index" : " is alive" );
		return;
	}

	Msg( "[SourceSharp] payload agreement: %d of %d entity(ies) agree\n", nAgreed, nChecked );
}

// Writes one dword into a live entity's blob and marks it changed, so the delta path
// can be exercised.
SS_NATIVE_COMMAND( cs_sv_poke_payload )
{
	if ( args.ArgC() < 4 )
	{
		Msg( "usage: cs_sv_poke_payload <entindex> <word> <value>\n" );
		return;
	}

	const int nIndex = ss::AtoI( args[1] );
	const int iWord  = ss::AtoI( args[2] );
	const uint32 nValue = (uint32)strtoul( args[3], NULL, 0 );

	if ( SourceSharp_StandalonePokePayload( nIndex, iWord, nValue ) )
		return;

	Warning( "[SourceSharp] nothing carrying a payload at index %d\n", nIndex );
}

// Writes the probe pattern (a function of seed and word index) into a live entity's
// payload; cs_cl_payload_probe recomputes it on the client and names the first word that
// differs.
SS_NATIVE_COMMAND( cs_sv_payload_probe )
{
	if ( args.ArgC() < 2 )
	{
		Msg( "usage: cs_sv_payload_probe <entindex> [seed]   (default seed 1)\n" );
		return;
	}

	const int nIndex = ss::AtoI( args[1] );
	const uint32 nSeed = ( args.ArgC() > 2 ) ? (uint32)strtoul( args[2], NULL, 0 ) : 1u;

	int nWords = 0;

	if ( SourceSharp_StandaloneProbePayload( nIndex, nSeed, &nWords ) )
		return;

	Warning( "[SourceSharp] nothing carrying a payload at index %d\n", nIndex );
}

// Breaks an entity's schema hash on purpose, so the client's refusal path runs.
SS_NATIVE_COMMAND( cs_sv_break_schema )
{
	if ( args.ArgC() < 2 )
	{
		Msg( "usage: cs_sv_break_schema <entindex> [hash]   (default 0xDEADBEEF)\n" );
		return;
	}

	const int nIndex = ss::AtoI( args[1] );
	const uint32 nHash = ( args.ArgC() > 2 ) ? (uint32)strtoul( args[2], NULL, 0 ) : 0xDEADBEEFu;

	if ( SourceSharp_StandaloneBreakSchema( nIndex, nHash ) )
		return;

	Warning( "[SourceSharp] no managed entity at index %d\n", nIndex );
}

// The published type table.
SS_NATIVE_COMMAND( cs_sv_dump_types )
{
	CSourceSharpTypeTable &types = SourceSharp_NetTypes();

	Msg( "[SourceSharp] --- published types: %d, %d collision(s) ---\n",
		types.Count(), types.Collisions() );

	for ( int i = 0; i < types.Count(); i++ )
	{
		const SourceSharpNetType_t *pType = types.At( i );

		Msg( "[SourceSharp]   %-28s id=0x%08x schema=0x%08x layout=%-5d shell=%d tier=%d (%s)\n",
			pType->szClass, pType->nTypeId, pType->nSchemaHash, pType->nLayoutBytes,
			pType->nShell, pType->iTier,
			SourceSharp_TierTableName( pType->nShell, pType->iTier ) );
	}
}
