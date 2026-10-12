//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_nettable.cpp's diagnostic console commands ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching rows under #if DEBUG.
//
//=============================================================================//

#include "tier0/dbg.h"
#include "tier1/convar.h"     // CCommand
#include "generic_nettable.h"
#include "native_command_body.h"
#include "ss_string.h"

// The hierarchy's implementations. The walk that knows about its entities has
// to be its own: nothing on it is a CBaseEntity, so gEntList cannot see them.
void SourceSharp_StandalonePayloadAgreeReport( int nWantedIndex, int *pOutChecked, int *pOutAgreed );
bool SourceSharp_StandalonePokePayload( int nIndex, int iWord, uint32 nValue );
bool SourceSharp_StandaloneProbePayload( int nIndex, uint32 nSeed, int *pOutWords );
bool SourceSharp_StandaloneBreakSchema( int nIndex, uint32 nHash );

//-----------------------------------------------------------------------------
// Whether the payload props of an entity's table read the bytes the object
// thinks its blob holds.
//
// THE SERVER HALF, and it checks something the client half cannot. Reading
// through the built props' OWN offsets is the whole test: an offset off by one
// word still produces a plausible number, still encodes, still decodes, and the
// only thing that ever notices is a comparison against the bytes it was meant
// to be.
//
// Deliberately not "compare the blob with itself". The left-hand side is
// pProp->GetOffset() as the ENGINE will use it; the right-hand side is the blob
// as C# wrote it. Rederiving the offset here would compare a computation with
// itself and pass forever.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// Writes one dword into a live entity's blob and marks it changed.
//
// The only way, in this build, to make a payload word move on demand — and
// without that the whole design is only ever tested at its baseline, which is
// the one case the delta encoder does not run for (Part 1B is entirely about
// what a CHANGED prop costs).
//
// FCVAR_CHEAT, because it writes replicated state from the console.
//-----------------------------------------------------------------------------
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

//=============================================================================//
// THE PAIR-PROOF, and it is now the ONLY one.
//
// The server writes a pattern that is a function of the seed and the WORD INDEX
// into a live entity's whole payload; cs_cl_payload_probe recomputes it on the
// client and names the first word that differs. That crosses the encoder, the
// delta and the decoder.
//
// It replaced the typed-prop oracle at Step 6, and it has now also replaced the
// netentity's managed round trip — the only other thing that showed a generated
// SendTable and its generated RecvTable actually PAIRING. This reaches further:
// every word of an entity a shipping map already has, rather than four fields
// on a fixture nothing but a console command ever spawned.
//
// IT CAN FAIL, which is what makes it a measurement: check with a different
// seed and word 0 disagrees; poke half the words by hand with
// cs_sv_poke_payload and the first unpoked one does.
//=============================================================================//
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

//-----------------------------------------------------------------------------
// Breaks an entity's schema hash on purpose, so the client's refusal path runs.
// A path that has never run is a path nobody has tested.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// The published type table, on demand. What m_szClassname used to make readable
// off a packet dump and no longer can.
//-----------------------------------------------------------------------------
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
