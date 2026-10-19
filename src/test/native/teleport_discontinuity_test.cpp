//========= SourceSharp — a teleport is drawn, not travelled =================//
//
// plan_entity.md §3.9, and the LAST entry of ContractBaseline.OpenDefects:
// "An entity that teleports is drawn sliding to where it went".
//
// THE DEFECT THIS STANDS OVER. The client draws an entity at a point read out
// of a history of networked poses, slightly in the past — that is what
// interpolation IS. A pose 500 units from the last one is, to that history,
// indistinguishable from an entity that MOVED 500 units, so a teleported
// entity was drawn travelling the whole distance over the interpolation window.
// Stock answers it by networking a counter a teleport bumps
// (CBaseEntity::IncrementInterpolationFrame, baseentity.cpp:8301) and having
// the client throw the history away when the value it receives differs from the
// one it last acted on (C_BaseEntity::IsNoInterpolationFrame,
// c_baseentity.h:2416). SourceSharp_EntityTeleport's standalone arm bumped
// nothing, and the managed pass reset a ring on exactly one condition — a
// HANDLE REBIND, which is a create.
//
// WHY THIS FILE EXISTS, given that InterpolationTests measures the DRAWN POSE
// with a control arm and TeleportDiscontinuityTests measures the state.
//
// **IT IS TWO HALVES AND THEY MUST LAND TOGETHER.** A counter nothing reads is
// dead state; a reader with no counter never fires. Neither half is a compile
// error without the other, neither is a crash, and each on its own leaves a
// managed suite entirely green — the reader arm still has its unit facts,
// because those bump the counter themselves. What no managed fact can see is
// the WIRE between them: the block field, the SendProp, the RecvProp and the
// change mark are four separate declarations in three languages, and the value
// arriving is the only thing that makes the pair one mechanism.
//
// So each link is asserted apart, and each is a place the state stops reaching
// the client if it is removed:
//
//   1. the SHARED wire list really carries m_ubInterpolationFrame, with stock's
//      own encoding — a COMPILED check over the real table, so an entry
//      inserted in the middle of the array without the enum moving with it is
//      caught rather than mis-addressing every prop after it on BOTH sides;
//   2. the SERVER's SendProp addresses the block's own word;
//   3. the CLIENT's RecvProp addresses the same word on its side;
//   4. the SERVER BUMPS IT, in managed code, on the managed side of every
//      teleport crossing — and ANNOUNCES the write, which is the half a block
//      write on its own would miss;
//   5. the CLIENT READS IT, in the interpolation pass, and acknowledges it.
//
// Links 4 and 5 are the two halves. They are read out of the sources, in the
// idiom nodraw_visibility_test.cpp and fog_wire_test.cpp already use here,
// because they are statements about code that cannot be linked without an
// engine — and about C#, which this binary cannot link at all.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <string>
#include <fstream>
#include <sstream>

// What standalone_nettable.h wants from the engine, and no more. All three are
// the engine's own numbers: MAX_EDICT_BITS and EF_MAX_BITS from public/const.h,
// NOINTERP_PARITY_MAX_BITS from game/shared/shareddefs.h. Spelled here rather
// than reached for, because this binary deliberately links no engine — and each
// is checked against its own header below, so no copy can drift into asserting
// agreement with itself.
#define MAX_EDICT_BITS           11
#define EF_MAX_BITS              10
#define NOINTERP_PARITY_MAX_BITS 2
#define ARRAYSIZE( p ) ( (int)( sizeof( p ) / sizeof( (p)[0] ) ) )

#include "../game/shared/standalone_nettable.h"

namespace
{

std::string Read( const char *pRelative )
{
	std::ifstream f( pRelative );
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

// Comment-free, so a needle cannot be satisfied by the paragraph explaining why
// the code does not do that any more. Every file this test reads carries long
// arguments in comments, and this project has twice found a needle that was
// only ever matching one.
//
// C# line comments are `//` and C# block comments are `/* */`, so one stripper
// serves both languages here. XML doc comments (`///`) are line comments and go
// with them, which is the point: TeleportDiscontinuity's prose names every
// symbol this file looks for.
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

const char *const kSharedDefs = "game/shared/shareddefs.h";
const char *const kServerNet  = "sourcesharp/game/server/shell_standalone.cpp";
const char *const kClientCpp  = "sourcesharp/game/client/shell_standalone_client.cpp";
// EngineSide.cs since plan_engineapi_vtables B1: the per-side instance holds the
// bodies; the server's half since plan_shim_areas_split phase 7, because the
// teleport reaches only the server's areas.
const char *const kEngineCs   = "sourcesharp/managed/SourceSharp.Server.Interop/EngineSide.Server.cs";
const char *const kInterpCs   = "sourcesharp/managed/SourceSharp.Client/Interpolation.cs";
const char *const kBlockCs    = "sourcesharp/managed/SourceSharp.Abi/EngineBlock.cs";
const char *const kGenerated  = "sourcesharp/game/shared/engine_block_generated.h";
const char *const kBaseline   = "sourcesharp/managed/SourceSharp.Tests.Common/ContractBaseline.cs";

// The body of one function, from its signature to the closing brace in column
// zero. Everything below that asks "what does this function do" asks it of
// this, not of the file: a grep over the file would go on passing on a tree
// where some OTHER function had picked the habit up.
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

// The body of one C# method, whose closing brace is indented four spaces rather
// than sitting in column zero.
std::string MethodBodyOf( const std::string &code, const char *pSignature )
{
	const size_t at = code.find( pSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = code.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	const size_t close = code.find( "\n    }", open );
	if ( close == std::string::npos )
		return std::string();

	return code.substr( open + 1, close - open - 1 );
}

//=============================================================================//
// 1: THE SHARED LIST, COMPILED.
//=============================================================================//

TEST( TeleportDiscontinuity, TheWireCarriesTheInterpolationFrame )
{
	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );

	ASSERT_EQ( STANDALONE_NET_FIELD_COUNT, nCount )
		<< "the field ARRAY and the field ENUM disagree about how many props "
		   "there are. Both sides subscript the array by the enum, so the two "
		   "shells would build props for different fields.";

	EXPECT_STREQ( "m_ubInterpolationFrame",
	              pFields[ STANDALONE_NET_FIELD_INTERPFRAME ].pName )
		<< "the enum's INTERPFRAME index does not name the discontinuity "
		   "counter. An offset written at this index would be applied to "
		   "whatever prop is here, and the block word the wire wrote into "
		   "would be somebody else's.";

	EXPECT_EQ( STANDALONE_NET_INT, pFields[ STANDALONE_NET_FIELD_INTERPFRAME ].kind );

	// NOINTERP_PARITY_MAX_BITS, exactly as stock sends m_ubInterpolationFrame
	// (baseentity.cpp:320). Two bits carry the four values the counter wraps
	// through; a ONE-bit field would make every second teleport invisible,
	// which is the failure this mechanism exists to avoid and which no test
	// that only ever teleports once would notice.
	EXPECT_EQ( NOINTERP_PARITY_MAX_BITS,
	           pFields[ STANDALONE_NET_FIELD_INTERPFRAME ].nBits );
}

TEST( TeleportDiscontinuity, EveryEnumIndexStillNamesItsOwnField )
{
	// Inserting the counter in the MIDDLE of the list is exactly the edit this
	// guards: everything after it renumbered, on both sides at once.
	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );
	ASSERT_EQ( STANDALONE_NET_FIELD_COUNT, nCount );

	EXPECT_STREQ( "m_vecOrigin",            pFields[ STANDALONE_NET_FIELD_ORIGIN ].pName );
	EXPECT_STREQ( "m_angRotation",          pFields[ STANDALONE_NET_FIELD_ANGLES ].pName );
	EXPECT_STREQ( "m_nModelIndex",          pFields[ STANDALONE_NET_FIELD_MODELINDEX ].pName );
	EXPECT_STREQ( "m_fEffects",             pFields[ STANDALONE_NET_FIELD_EFFECTS ].pName );
	EXPECT_STREQ( "m_ubInterpolationFrame", pFields[ STANDALONE_NET_FIELD_INTERPFRAME ].pName );
	EXPECT_STREQ( "m_nTypeId",              pFields[ STANDALONE_NET_FIELD_TYPEID ].pName );
	EXPECT_STREQ( "m_nSchemaHash",          pFields[ STANDALONE_NET_FIELD_SCHEMAHASH ].pName );
	EXPECT_STREQ( "m_nPayloadWords",        pFields[ STANDALONE_NET_FIELD_PAYLOADWORDS ].pName );
}

TEST( TeleportDiscontinuity, TheAssumedParityWidthIsTheHeadersOwn )
{
	// This binary links no engine and #defines NOINTERP_PARITY_MAX_BITS above.
	// A copy that drifted from shareddefs.h would make the test above assert
	// agreement with itself, which is the shape of a check that cannot fail.
	const std::string defs = Read( kSharedDefs );
	ASSERT_FALSE( defs.empty() ) << "game/shared/shareddefs.h could not be read";

	EXPECT_NE( defs.find( "#define NOINTERP_PARITY_MAX_BITS\t2" ), std::string::npos )
		<< "shareddefs.h's NOINTERP_PARITY_MAX_BITS is no longer 2, so the "
		   "value this file assumes -- and therefore the wire width it checks "
		   "-- is stale.";

	EXPECT_NE( defs.find( "#define NOINTERP_PARITY_MAX\t\t\t4" ), std::string::npos )
		<< "shareddefs.h's NOINTERP_PARITY_MAX is no longer 4, so managed "
		   "code's TeleportDiscontinuity.ParityMax is no longer stock's.";
}

//=============================================================================//
// 2 and 3: BOTH SIDES MAP THE FIELD ONTO THE BLOCK'S OWN WORD.
//=============================================================================//

TEST( TeleportDiscontinuity, TheBlockDeclaresBothWords )
{
	// The generated header, not the C# it is generated from: this is the
	// declaration the two shells actually compile against, and a stale copy of
	// it is the one way the offsets below can be right in C# and wrong in the
	// game.
	const std::string block = ReadCode( kGenerated );
	ASSERT_FALSE( block.empty() ) << "engine_block_generated.h could not be read";

	EXPECT_NE( block.find( "int32_t     InterpolationFrame;" ), std::string::npos )
		<< "the engine block has no InterpolationFrame word, so the SendProp "
		   "and RecvProp below address nothing.";

	EXPECT_NE( block.find( "int32_t     OldInterpolationFrame;" ), std::string::npos )
		<< "the engine block has no OldInterpolationFrame word. Without it the "
		   "client has nowhere to record that it acted on a jump, and every "
		   "update after the first teleport would reset the history again -- an "
		   "entity drawn in snapshot steps for the rest of the map.";

	// The two are ADJACENT and in this order, which is what keeps the head a
	// multiple of eight. Either one alone leaves a hole the server tail behind
	// it would have to be padded across.
	const size_t frame = block.find( "int32_t     InterpolationFrame;" );
	const size_t seen  = block.find( "int32_t     OldInterpolationFrame;" );
	EXPECT_LT( frame, seen );
}

TEST( TeleportDiscontinuity, TheServerSendsTheBlocksOwnWord )
{
	const std::string body = BodyOf( ReadCode( kServerNet ),
	                                 "static void BuildStandaloneProps" );
	ASSERT_FALSE( body.empty() ) << "BuildStandaloneProps was not found";

	EXPECT_NE( body.find( "SS_BLOCK_HEAD_OFFSET( InterpolationFrame )" ), std::string::npos )
		<< "the server's SendProp for the counter does not address the block's "
		   "InterpolationFrame word. Managed code writes THAT word and marks "
		   "THAT offset, and MapPropOffsetsToIndices matches an offset exactly "
		   "-- a prop built from any other number is a prop the mark never "
		   "names, so the value arrives by the slow path if it arrives at all.";

	EXPECT_NE( body.find( "nOffsets[ STANDALONE_NET_FIELD_INTERPFRAME ]" ), std::string::npos )
		<< "the server supplies no offset at the counter's enum index, so the "
		   "prop is built from an uninitialised entry.";
}

TEST( TeleportDiscontinuity, TheClientReceivesIntoTheSameWord )
{
	const std::string body = BodyOf( ReadCode( kClientCpp ),
	                                 "static void BuildStandaloneRecvProps" );
	ASSERT_FALSE( body.empty() ) << "BuildStandaloneRecvProps was not found";

	EXPECT_NE( body.find( "SS_CLIENT_BLOCK_HEAD_OFFSET( InterpolationFrame )" ),
	           std::string::npos )
		<< "the client's RecvProp for the counter does not address the block's "
		   "InterpolationFrame word. The engine's decoder would write the "
		   "networked value somewhere the interpolation pass never reads, and "
		   "nothing on either side would say so.";

	EXPECT_NE( body.find( "nOffsets[ STANDALONE_NET_FIELD_INTERPFRAME ]" ), std::string::npos )
		<< "the client supplies no offset at the counter's enum index.";
}

//=============================================================================//
// 4 AND 5: THE TWO HALVES, AND NEITHER IS ANY USE ALONE.
//=============================================================================//

TEST( TeleportDiscontinuity, TheServerHalfBumpsTheCounterOnEveryTeleport )
{
	// Engine.EntityTeleport is where every teleport on this hierarchy crosses:
	// point_teleport, trigger_teleport, the trains, the dev commands and any
	// managed caller yet to be written all arrive here. Bumping on THIS side of
	// the crossing is what makes the fix cover them without a line of C++.
	const std::string body = MethodBodyOf(
		ReadCode( kEngineCs ),
		"public void EntityTeleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity)" );

	ASSERT_FALSE( body.empty() ) << "Engine.EntityTeleport was not found";

	EXPECT_NE( body.find( "TeleportDiscontinuity.Bump(" ), std::string::npos )
		<< "THE SERVER HALF IS GONE. A teleport that bumps nothing is a pose a "
		   "long way from the last one, and the client draws the entity "
		   "travelling the whole distance -- which is the defect this closed. "
		   "Nothing else fails when this line goes: the client half still has "
		   "its unit facts, because those bump the counter themselves.";

	EXPECT_NE( body.find( "MarkEntityBlockChanged(h, TeleportDiscontinuity.FrameOffset)" ),
	           std::string::npos )
		<< "the bump is not announced. A block write nothing marks is a value "
		   "the local backdoor never copies -- and the origin's own mark says "
		   "nothing about the word beside it, because an offset names exactly "
		   "one prop.";
}

TEST( TeleportDiscontinuity, TheClientHalfResetsTheHistoryOnIt )
{
	const std::string body = MethodBodyOf(
		ReadCode( kInterpCs ),
		"public int SamplePass(IInterpolationBlocks blocks, float curTime)" );

	ASSERT_FALSE( body.empty() ) << "Interpolation.SamplePass was not found";

	EXPECT_NE( body.find( "TeleportDiscontinuity.HasTeleported(block)" ), std::string::npos )
		<< "THE CLIENT HALF IS GONE. The pass resets a ring on one condition "
		   "again -- `fresh`, a HANDLE REBIND, which is a different entity "
		   "arriving in the same slot. A create is covered by that; a live "
		   "entity jumping is not. The counter still crosses the wire and now "
		   "nothing reads it.";

	EXPECT_NE( body.find( "fresh || teleported" ), std::string::npos )
		<< "the jump is no longer one of the conditions the ring is reset on, "
		   "so the value is read and then ignored.";

	EXPECT_NE( body.find( "TeleportDiscontinuity.Acknowledge(block)" ), std::string::npos )
		<< "the pass never acknowledges the counter, so every update after a "
		   "teleport resets the history again and the entity is drawn in "
		   "snapshot steps for the rest of the map.";
}

TEST( TeleportDiscontinuity, TheCounterIsWrittenByManagedCodeAndNotByTheShells )
{
	// The rule CLAUDE.md states and this lane kept: the decision "this move is
	// a jump and not a motion" is policy, so it lives in C#. The native
	// teleport path is unchanged -- SourceSharp_EntityTeleport still bumps
	// nothing, on purpose, because the bump happens before the crossing.
	//
	// Asserted as an ABSENCE, which is the direction that rots: adding a
	// convenient native bump later would give the counter two authors and no
	// owner.
	const std::string serverNet = ReadCode( kServerNet );
	const std::string clientCpp = ReadCode( kClientCpp );

	EXPECT_EQ( serverNet.find( "InterpolationFrame =" ), std::string::npos )
		<< "the server shell assigns the counter. It is managed code's word: "
		   "see SourceSharp.Abi.TeleportDiscontinuity.";

	EXPECT_EQ( clientCpp.find( "InterpolationFrame =" ), std::string::npos )
		<< "the client shell assigns the counter or its acknowledgement. Both "
		   "words belong to managed code -- one to the server's teleport, one "
		   "to the client's interpolation pass.";
}

TEST( TeleportDiscontinuity, TheCSharpDeclarationAndTheGeneratedHeaderAgreeOnBothWords )
{
	// Non-vacuity for the header check above, and the one thing that says the
	// generated file was regenerated rather than hand-edited: the names have to
	// be in the C# declaration too, because that is what the generator reads.
	const std::string decl = ReadCode( kBlockCs );
	ASSERT_FALSE( decl.empty() ) << "EngineBlock.cs could not be read";

	EXPECT_NE( decl.find( "public int InterpolationFrame;" ), std::string::npos );
	EXPECT_NE( decl.find( "public int OldInterpolationFrame;" ), std::string::npos );
}

//=============================================================================//
// THE LEDGER ROW, INVERTED.
//=============================================================================//

TEST( TeleportDiscontinuity, TheOpenDefectsRowIsRetiredAndNotJustQuiet )
{
	// A row that simply disappears from the tests makes a closed gap and a
	// broken wire look identical again, which is the whole thing the ledger
	// exists to stop. So the fact is that the row is GONE, and putting it back
	// fails here as well as in ContractDiffTests -- two languages, one claim.
	//
	// COMMENT-STRIPPED, which is what makes the absence mean anything: the
	// retirement note that replaced the row names the defect at length, in a
	// comment, and a raw grep would read that as the row still being there.
	const std::string code = ReadCode( kBaseline );
	ASSERT_FALSE( code.empty() ) << "ContractBaseline.cs could not be read";

	EXPECT_EQ( code.find( "An entity that teleports is drawn sliding" ), std::string::npos )
		<< "the teleport defect is back on OpenDefects. If it really is open "
		   "again, the two halves in TeleportDiscontinuity are what to look at "
		   "-- and this test should be the one deleted, not the row.";

	// THE POSITIVE CONTROL, and it is not decoration: OpenDefects is EMPTY, so
	// an absence check over this file could pass because the file failed to
	// read, because the stripper ate everything, or because the needle was
	// misspelt. A subject that IS a live row in the same file, found by the same
	// search, is what rules all three out.
	EXPECT_NE( code.find( "a point gib's bounce sheds no energy" ), std::string::npos )
		<< "the control needle is gone too, so the absence above says nothing "
		   "about the teleport row -- it says this search stopped working.";
}

}  // namespace
