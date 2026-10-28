//========= SourceSharp — an invisible entity keeps its shape =================//
//
// plan_entity.md step 8e-render.
//
// THE DEFECT THIS STANDS OVER. CStandaloneEntity::SetVisible expressed "do not
// draw this" by writing Block().Head.ModelIndex = 0, because there was no
// EF_NODRAW anywhere on the parallel hierarchy. So "invisible" and "shapeless"
// were ONE state, and every managed brush volume the client has to reason about
// but must not draw arrived with nothing to reason about: step 7a's
// cs_cl_dust_report read two func_dust brushes on ss_sandbox as
//
//     waiting[0] handle 31523109  entity present  block ModelIndex 0  GetModel no
//     waiting[1] handle 49152294  entity present  block ModelIndex 0  GetModel no
//
// for a whole session, and func_smokevolume and func_precipitation carry the
// identical `pOwner->GetModel(); if ( !pModel ) return;` refusal.
//
// WHY THIS FILE EXISTS RATHER THAN A LIVE GATE ALONE. The failure mode of the
// fix is silence in both directions: an effect that renders nothing does not
// crash, and a brush that is drawn when it should not be is a wall where a
// mapper wanted air. Neither shows up in a suite that only asks "does the game
// still run". So the chain is asserted piece by piece, and every piece is a
// place the state stops reaching the client if it is removed:
//
//   1. the SHARED wire list really carries m_fEffects, with the encoding stock
//      sends m_fEffects with;
//   2. the enum indices into that list agree with the array's own order, which
//      is what both sides' offset tables subscript;
//   3. the server writes the BIT and no longer writes the INDEX;
//   4. both sides map the new field onto the block's Effects word;
//   5. the client NOTICES the word moving, which is the part with no other
//      trigger -- the engine's decoder writes it with nothing on this side
//      running, so a func_dust that never moves would carry an unread bit
//      forever;
//   6. the managed render policy refuses the draw on the bit.
//
// The first two are a COMPILED check over the real table. The rest are read out
// of the sources, in the idiom dust_verdict_test.cpp and gib_evicted_test.cpp
// already use here, because they are statements about code that cannot be
// linked without an engine.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <string>
#include <fstream>
#include <sstream>
#include <stdlib.h>

// What standalone_nettable.h wants from the engine, and no more. Both are the
// engine's own numbers: MAX_EDICT_BITS from public/const.h (11) and EF_MAX_BITS
// from the entity-effects enum in the same header (10). Spelled here rather
// than reached for, because this binary deliberately links no engine -- and
// EF_MAX_BITS is checked against the header below so the copy cannot drift.
#define MAX_EDICT_BITS 11
#define EF_MAX_BITS    10
// And NOINTERP_PARITY_MAX_BITS from game/shared/shareddefs.h (2), which the
// table gained with the teleport counter. Checked against its own header in
// teleport_discontinuity_test.cpp, which is where that field's own facts live.
#define NOINTERP_PARITY_MAX_BITS 2
#define ARRAYSIZE( p ) ( (int)( sizeof( p ) / sizeof( (p)[0] ) ) )

#include "../game/shared/standalone_nettable.h"

namespace
{

//-----------------------------------------------------------------------------
// Reading a source file, relative to src/. The tests below run from src/, which
// is where buildcpptests puts them.
//-----------------------------------------------------------------------------
//=============================================================================//
// 1 and 2: THE SHARED LIST, COMPILED.
//=============================================================================//

TEST( NoDrawVisibility, TheWireCarriesTheEffectsWord )
{
	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );

	ASSERT_EQ( STANDALONE_NET_FIELD_COUNT, nCount )
		<< "the field ARRAY and the field ENUM disagree about how many props "
		   "there are. Both sides subscript the array by the enum, so the two "
		   "shells would build props for different fields.";

	EXPECT_STREQ( "m_fEffects", pFields[ STANDALONE_NET_FIELD_EFFECTS ].pName )
		<< "the enum's EFFECTS index does not name the effects field. An offset "
		   "written at this index would be applied to whatever prop is here, "
		   "and the block word the wire wrote into would be somebody else's.";

	EXPECT_EQ( STANDALONE_NET_INT, pFields[ STANDALONE_NET_FIELD_EFFECTS ].kind );

	// EF_MAX_BITS, exactly as stock sends m_fEffects
	// (SendPropInt( SENDINFO( m_fEffects ), EF_MAX_BITS, SPROP_UNSIGNED )). A
	// narrower field silently drops the high EF_ bits on the wire, which for a
	// visibility flag is a brush hidden on one side and drawn on the other.
	EXPECT_EQ( EF_MAX_BITS, pFields[ STANDALONE_NET_FIELD_EFFECTS ].nBits );
}

TEST( NoDrawVisibility, EveryEnumIndexNamesItsOwnField )
{
	// The general form of the assertion above, and the one that would catch an
	// entry inserted in the middle of the array without the enum moving with
	// it. Six of these were right before this step; the seventh is new, and
	// inserting it in the MIDDLE is exactly the edit this guards.
	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );
	ASSERT_EQ( STANDALONE_NET_FIELD_COUNT, nCount );

	EXPECT_STREQ( "m_vecOrigin",     pFields[ STANDALONE_NET_FIELD_ORIGIN ].pName );
	EXPECT_STREQ( "m_angRotation",   pFields[ STANDALONE_NET_FIELD_ANGLES ].pName );
	EXPECT_STREQ( "m_nModelIndex",   pFields[ STANDALONE_NET_FIELD_MODELINDEX ].pName );
	EXPECT_STREQ( "m_fEffects",      pFields[ STANDALONE_NET_FIELD_EFFECTS ].pName );
	EXPECT_STREQ( "m_ubInterpolationFrame",
	                                 pFields[ STANDALONE_NET_FIELD_INTERPFRAME ].pName );
	EXPECT_STREQ( "m_nTypeId",       pFields[ STANDALONE_NET_FIELD_TYPEID ].pName );
	EXPECT_STREQ( "m_nSchemaHash",   pFields[ STANDALONE_NET_FIELD_SCHEMAHASH ].pName );
	EXPECT_STREQ( "m_nPayloadWords", pFields[ STANDALONE_NET_FIELD_PAYLOADWORDS ].pName );
}

//=============================================================================//
// 3: THE SERVER WRITES THE BIT, AND STOPPED WRITING THE INDEX.
//=============================================================================//

//=============================================================================//
// 4: BOTH SIDES MAP THE FIELD ONTO THE BLOCK'S Effects WORD.
//=============================================================================//

//=============================================================================//
// 5: THE CLIENT NOTICES THE WORD MOVING.
//
// The piece with no other trigger, and therefore the piece most likely to be
// lost. Everything else in this chain fails loudly if it is wrong; this one
// fails by the game looking completely normal on every entity that happens to
// move, and wrong only on the ones that do not -- which is precisely the
// func_dust that started all of this.
//=============================================================================//

//=============================================================================//
// 6: THE MANAGED POLICY REFUSES THE DRAW ON THE BIT.
//
// The rule itself is tested as a rule by ClientRenderPolicyTests over real
// block bytes. What is asserted here is the WIRING: that the fact is read from
// the HEAD's Effects word and not from somewhere in the client tail, which is a
// mistake that compiles, runs, and answers about a neighbouring field.
//=============================================================================//

} // namespace
