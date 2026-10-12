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
std::string Read( const char *pRelative )
{
	std::ifstream f( pRelative );
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

// Comment-free, so a needle cannot be satisfied by the paragraph explaining why
// the code does not do that any more. Every file this test reads carries long
// arguments in comments, and both defects this project has found of the
// "needle in a comment" kind were exactly that.
std::string ReadCode( const char *pRelative )
{
	const std::string src = Read( pRelative );
	std::string out;
	out.reserve( src.size() );

	bool bLine = false, bBlock = false;

	for ( size_t i = 0; i < src.size(); i++ )
	{
		if ( bLine )
		{
			if ( src[i] == '\n' ) { bLine = false; out += '\n'; }
			continue;
		}
		if ( bBlock )
		{
			if ( src[i] == '*' && i + 1 < src.size() && src[i+1] == '/' ) { bBlock = false; i++; }
			continue;
		}
		if ( src[i] == '/' && i + 1 < src.size() && src[i+1] == '/' ) { bLine = true; i++; continue; }
		if ( src[i] == '/' && i + 1 < src.size() && src[i+1] == '*' ) { bBlock = true; i++; continue; }

		out += src[i];
	}

	return out;
}

const char *const kServerBase = "sourcesharp/game/server/shell_standalone_base.cpp";
const char *const kServerNet  = "sourcesharp/game/server/shell_standalone.cpp";
const char *const kClientCpp  = "sourcesharp/game/client/shell_standalone_client.cpp";
const char *const kClientH    = "sourcesharp/game/client/shell_standalone_client.h";
const char *const kPolicy     = "sourcesharp/managed/SourceSharp.Client/Standalone/ClientRenderPolicy.cs";
const char *const kConstH     = "public/const.h";

// The body of one function, from its signature to the closing brace in column
// zero. Everything below that asks "what does SetVisible do" asks it of this,
// not of the file: a grep over the file would go on passing on a tree where
// some OTHER function had picked the habit up.
std::string BodyOf( const std::string &code, const char *pSignature )
{
	const size_t at = code.find( pSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = code.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	const size_t close = code.find( "\n}", open );
	if ( close == std::string::npos )
		return std::string();

	return code.substr( open + 1, close - open - 1 );
}

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

TEST( NoDrawVisibility, TheAssumedEfMaxBitsIsTheHeadersOwn )
{
	// This binary links no engine and #defines EF_MAX_BITS above. A copy that
	// drifted from const.h would make the test above assert agreement with
	// itself, which is the shape of a check that cannot fail.
	const std::string constants = Read( kConstH );
	ASSERT_FALSE( constants.empty() ) << "public/const.h could not be read";

	EXPECT_NE( constants.find( "EF_MAX_BITS = 10" ), std::string::npos )
		<< "const.h's EF_MAX_BITS is no longer 10, so the value this file "
		   "assumes -- and therefore the wire width it checks -- is stale.";

	EXPECT_NE( constants.find( "EF_NODRAW\t\t\t\t= 0x020" ), std::string::npos )
		<< "EF_NODRAW is no longer 0x020 in const.h.";
}

//=============================================================================//
// 3: THE SERVER WRITES THE BIT, AND STOPPED WRITING THE INDEX.
//=============================================================================//

TEST( NoDrawVisibility, SetVisibleSetsTheBitAndNotTheIndex )
{
	const std::string body = BodyOf( ReadCode( kServerBase ),
	                                 "void CStandaloneEntity::SetVisible" );
	ASSERT_FALSE( body.empty() ) << "CStandaloneEntity::SetVisible was not found";

	EXPECT_NE( body.find( "EF_NODRAW" ), std::string::npos )
		<< "SetVisible does not mention EF_NODRAW. The bit is the whole fix.";

	EXPECT_EQ( body.find( "ModelIndex = 0" ), std::string::npos )
		<< "SetVisible is zeroing the model index again. That is the defect step "
		   "7a measured: an invisible managed brush reaches the client with no "
		   "shape, and the dust, smoke and precipitation shims all refuse an "
		   "owner with no model.";

	// AND IT STILL RESOLVES THE MODEL. Hiding by never resolving the index at
	// all would satisfy both assertions above and leave exactly the bug they
	// exist to stop.
	EXPECT_NE( body.find( "modelinfo->GetModelIndex( m_szModel )" ), std::string::npos )
		<< "SetVisible no longer resolves the model index, so a hidden entity "
		   "still has no shape -- by a different route.";

	// AND IT TELLS THE CLIENT. A bit written into the block and never marked is
	// a bit the delta encoder never sends, which looks from outside exactly
	// like a bit that was never set.
	EXPECT_NE( body.find( "MarkBlockChanged( SS_BLOCK_HEAD_OFFSET( Effects ) )" ),
	           std::string::npos )
		<< "SetVisible does not mark the Effects word changed, so the engine "
		   "never puts it in a delta and the client never hears about it.";
}

TEST( NoDrawVisibility, IsVisibleAsksTheBitRatherThanTheIndex )
{
	const std::string code = ReadCode( "sourcesharp/game/server/shell_standalone.h" );

	EXPECT_NE( code.find( "bool IsVisible() const { return ( Block().Head.Effects & EF_NODRAW ) == 0; }" ),
	           std::string::npos )
		<< "IsVisible still reads the model index. With SetVisible no longer "
		   "zeroing it, that answer is 'yes' for everything that has a model, "
		   "hidden or not.";
}

//=============================================================================//
// 4: BOTH SIDES MAP THE FIELD ONTO THE BLOCK'S Effects WORD.
//=============================================================================//

TEST( NoDrawVisibility, TheServerSendsTheBlocksEffectsWord )
{
	const std::string code = ReadCode( kServerNet );

	EXPECT_NE( code.find( "nOffsets[ STANDALONE_NET_FIELD_EFFECTS ]" ), std::string::npos );
	EXPECT_NE( code.find( "SS_BLOCK_HEAD_OFFSET( Effects )" ), std::string::npos )
		<< "the server's effects prop does not point at the block's Effects "
		   "word, so MarkBlockChanged's offset and the prop's offset are two "
		   "different numbers and every partial update matches nothing.";
}

TEST( NoDrawVisibility, TheClientReceivesIntoTheBlocksEffectsWord )
{
	const std::string code = ReadCode( kClientCpp );

	EXPECT_NE( code.find( "nOffsets[ STANDALONE_NET_FIELD_EFFECTS ]" ), std::string::npos );
	EXPECT_NE( code.find( "SS_CLIENT_BLOCK_HEAD_OFFSET( Effects )" ), std::string::npos )
		<< "the client's effects prop does not decode into the block's Effects "
		   "word, so the render policy reads a word nothing writes.";
}

//=============================================================================//
// 5: THE CLIENT NOTICES THE WORD MOVING.
//
// The piece with no other trigger, and therefore the piece most likely to be
// lost. Everything else in this chain fails loudly if it is wrong; this one
// fails by the game looking completely normal on every entity that happens to
// move, and wrong only on the ones that do not -- which is precisely the
// func_dust that started all of this.
//=============================================================================//

TEST( NoDrawVisibility, PostDataUpdateNoticesTheEffectsWordMoving )
{
	const std::string body = BodyOf( ReadCode( kClientCpp ),
	                                 "void C_StandaloneEntity::PostDataUpdate" );
	ASSERT_FALSE( body.empty() ) << "C_StandaloneEntity::PostDataUpdate was not found";

	EXPECT_NE( body.find( "Block().Head.Effects != m_nPublishedEffects" ), std::string::npos )
		<< "nothing compares the decoded Effects word against what the policy "
		   "last saw. The engine's decoder writes it with no code of ours "
		   "running, so an entity that never moves would carry EF_NODRAW in its "
		   "block and go on being drawn forever.";

	EXPECT_NE( body.find( "m_nPublishedEffects = Block().Head.Effects" ), std::string::npos )
		<< "the comparison above is never satisfied: the remembered value is "
		   "not updated, so the policy serial is bumped on every single update "
		   "of every single entity.";

	EXPECT_NE( body.find( "BumpPolicyInputs()" ), std::string::npos )
		<< "the change is noticed and nothing is done with it.";
}

TEST( NoDrawVisibility, TheShellRemembersWhichEffectsThePolicySaw )
{
	const std::string code = ReadCode( kClientH );

	EXPECT_NE( code.find( "int    m_nPublishedEffects;" ), std::string::npos )
		<< "the member PostDataUpdate compares against is not declared.";
}

//=============================================================================//
// 6: THE MANAGED POLICY REFUSES THE DRAW ON THE BIT.
//
// The rule itself is tested as a rule by ClientRenderPolicyTests over real
// block bytes. What is asserted here is the WIRING: that the fact is read from
// the HEAD's Effects word and not from somewhere in the client tail, which is a
// mistake that compiles, runs, and answers about a neighbouring field.
//=============================================================================//

TEST( NoDrawVisibility, ThePolicyReadsTheNoDrawBitOutOfTheHead )
{
	const std::string code = Read( kPolicy );
	ASSERT_FALSE( code.empty() ) << "ClientRenderPolicy.cs could not be read";

	EXPECT_NE( code.find( "Head(nameof(EngineBlockHead.Effects))" ), std::string::npos )
		<< "the effects offset is not taken from the block HEAD. m_fEffects is "
		   "networked into EngineBlockHead.Effects; a Tail() offset of the same "
		   "name would be a different field entirely.";

	EXPECT_NE( code.find( "EntityEffect.NoDraw" ), std::string::npos )
		<< "the policy does not test EF_NODRAW.";

	EXPECT_NE( code.find( "if (facts.NoDraw)" ), std::string::npos )
		<< "Drawn() does not refuse on the bit, so the bit is read and then "
		   "ignored -- which draws exactly the entity the server hid.";
}

} // namespace
