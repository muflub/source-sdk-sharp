//========= SourceSharp =====================================================//
//
// THE ENGINE BLOCK, ASSERTED FIELD BY FIELD ON THE NATIVE SIDE
// (plan_entity.md §3.2, and §7's "engine block drift").
//
// This is the third of the three checks the plan asks for. The other two are in
// SourceSharp.Tests/EngineBlockTests.cs: one xUnit fact per field against
// Marshal.OffsetOf, and the fingerprint the join handshake exchanges.
//
// WHY A GTEST WHEN engine_block_generated.h ALREADY static_asserts THE SAME
// NUMBERS. Two reasons, and neither is belt-and-braces.
//
//   * A static_assert that stops the build is invisible in a test run. The gate
//     for this step is "one gtest per field against offsetof", and a check that
//     never appears in the run is a check nobody can point at afterwards.
//   * THE NUMBERS BELOW ARE HAND-WRITTEN AND THE STRUCT IS NOT. EngineBlock_t
//     used to be typed by a person and asserted against generated offsets;
//     since the struct became generated too, both halves of every
//     static_assert in that header come out of the same C# declaration. What
//     they still catch is real -- the engine's own type widths, and the
//     C#-to-C++ type map -- but they are no longer two opinions. These literals
//     are the second opinion. Reorder the C# block and forget to say so here,
//     and this file goes red while every static_assert in the generated header
//     goes on agreeing with itself.
//
// AND ONE THING ONLY THIS SIDE CAN CHECK: that the room left for the engine's
// own structs is still big enough for them. PVSInfo_t and CPlayerState are
// Valve's layouts. The C# block reserves bytes for them by size, because
// restating an engine struct in C# would be a second opinion about something we
// do not own. What makes the reservation honest rather than hopeful is that
// this file asks the compiler how big the real ones are.
//
//===========================================================================//

#include <stddef.h>

#include "gtest/gtest.h"

#include "mathlib_test_env.h"

#include "engine_block.h"

#include "iservernetworkable.h"
#include "PlayerState.h"

namespace
{

//-----------------------------------------------------------------------------
// The reservations, against the engine structs that go in them.
//
// A `<=` and not an `==`: the reservation is allowed to be roomier than the
// struct it holds, and a smaller engine struct in some future SDK drop is not a
// reason to fail. Too SMALL is the failure, and it is the one that would let the
// engine write past the end of the block into whatever field the generator
// placed next.
//-----------------------------------------------------------------------------

TEST( EngineBlockReservations, PvsInfoFitsTheEnginesPvsInfoStruct )
{
	EXPECT_LE( sizeof( PVSInfo_t ), sizeof( EngineBlockServerTail_t::PvsInfo ) )
		<< "the engine fills PVSInfo_t through this storage in BuildEntityClusterList";
}

TEST( EngineBlockReservations, PlayerStateFitsTheEnginesPlayerStateStruct )
{
	// The engine reaches this through GetPlayerState, and its SourceTV and
	// Replay servers write through the result with NO null test. The storage
	// has to be real, and it has to be big enough.
	EXPECT_LE( sizeof( CPlayerState ), sizeof( EngineBlockServerTail_t::PlayerState ) )
		<< "step 3c constructs a real CPlayerState in this room; it no longer fits";
}

TEST( EngineBlockReservations, TheReservationsAreEightAligned )
{
	// Both engine structs contain pointers. A byte array would have given the
	// reservations an alignment of one and left that to luck about where the
	// block happened to land.
	EXPECT_EQ( 0u, offsetof( EngineBlockServerTail_t, PvsInfo ) % 8 );
	EXPECT_EQ( 0u, offsetof( EngineBlockServerTail_t, PlayerState ) % 8 );
	EXPECT_EQ( 0u, offsetof( EngineBlock_t, Server ) % 8 );
}

//-----------------------------------------------------------------------------
// The composite offsets the shell's send props are registered at.
//
// A send prop into the block is spelled BlockBase() + <offset of Head in the
// block> + <offset of the field in the head>, because the block is a head and
// two tails rather than one flat struct. Step 1b writes that as a macro over
// the two offsetof()s below. Both spellings need EngineBlock_t AND
// EngineBlockHead_t to be complete types at the point of use, which they are
// only because engine_block.h includes the generated header that declares them
// -- so this test is also the one that would stop compiling if the generated
// declarations went missing or were split apart.
//-----------------------------------------------------------------------------

TEST( EngineBlockCompositeOffsets, TheHeadSpellingCompilesAndResolves )
{
	// A COMPILE CHECK HONESTLY LABELLED. The head sits at offset zero, so this
	// sum cannot come out wrong however the block is rearranged -- what it
	// asserts is that the spelling RESOLVES: both EngineBlock_t and
	// EngineBlockHead_t complete, in that order, from one #include of
	// engine_block.h. The tail below is the one whose arithmetic can fail.
	EXPECT_EQ( 16u, offsetof( EngineBlock_t, Head ) + offsetof( EngineBlockHead_t, Origin ) );
}

TEST( EngineBlockCompositeOffsets, ARenderFieldIsReachableThroughTheClientTail )
{
	// The far end of the same arithmetic, and this one can be wrong: the client
	// tail starts at 280, so a field 36 bytes into it is at 316. Reach it
	// through the SERVER tail's base by mistake -- the two tails are adjacent
	// and both offsets are plausible numbers -- and the prop lands in the room
	// reserved for CPlayerState, where the engine writes.
	EXPECT_EQ( 316u, offsetof( EngineBlock_t, Client ) + offsetof( EngineBlockClientTail_t, RenderMaxs ) );
}

//-----------------------------------------------------------------------------
// The fingerprint the C# declaration produced, compiled into this side.
//-----------------------------------------------------------------------------

TEST( EngineBlockFingerprint, TheGeneratedHeaderDefinesOne )
{
	// Not a tautology: engine_block_generated.h is written by the managed side,
	// and a native build whose copy of it was never regenerated would carry a
	// value that disagrees with what the server puts on the wire. Nothing here
	// can compare the two -- that is EngineBlockTests' job -- but a build that
	// somehow lost the define entirely would not compile past this line.
	EXPECT_NE( 0u, (unsigned int)SOURCESHARP_ENGINE_BLOCK_FINGERPRINT );
}

//-----------------------------------------------------------------------------
// EngineBlockHead_t: 21 fields, 160 bytes.
//-----------------------------------------------------------------------------

TEST( EngineBlockHeadLayout, TheStructIs160BytesWide )
{
	EXPECT_EQ( 160u, sizeof( EngineBlockHead_t ) );
}

TEST( EngineBlockHeadLayout, HandleIsAt0 )
{
	EXPECT_EQ( 0u, offsetof( EngineBlockHead_t, Handle ) );
}

TEST( EngineBlockHeadLayout, ClassnameIndexIsAt4 )
{
	EXPECT_EQ( 4u, offsetof( EngineBlockHead_t, ClassnameIndex ) );
}

TEST( EngineBlockHeadLayout, ModelIndexIsAt8 )
{
	EXPECT_EQ( 8u, offsetof( EngineBlockHead_t, ModelIndex ) );
}

TEST( EngineBlockHeadLayout, CollisionModelIndexIsAt12 )
{
	EXPECT_EQ( 12u, offsetof( EngineBlockHead_t, CollisionModelIndex ) );
}

TEST( EngineBlockHeadLayout, OriginIsAt16 )
{
	EXPECT_EQ( 16u, offsetof( EngineBlockHead_t, Origin ) );
}

TEST( EngineBlockHeadLayout, AnglesIsAt28 )
{
	EXPECT_EQ( 28u, offsetof( EngineBlockHead_t, Angles ) );
}

TEST( EngineBlockHeadLayout, MinsIsAt40 )
{
	EXPECT_EQ( 40u, offsetof( EngineBlockHead_t, Mins ) );
}

TEST( EngineBlockHeadLayout, MaxsIsAt52 )
{
	EXPECT_EQ( 52u, offsetof( EngineBlockHead_t, Maxs ) );
}

TEST( EngineBlockHeadLayout, WorldTransformIsAt64 )
{
	EXPECT_EQ( 64u, offsetof( EngineBlockHead_t, WorldTransform ) );
}

TEST( EngineBlockHeadLayout, SolidTypeIsAt112 )
{
	EXPECT_EQ( 112u, offsetof( EngineBlockHead_t, SolidType ) );
}

TEST( EngineBlockHeadLayout, SolidFlagsIsAt116 )
{
	EXPECT_EQ( 116u, offsetof( EngineBlockHead_t, SolidFlags ) );
}

TEST( EngineBlockHeadLayout, CollisionGroupIsAt120 )
{
	EXPECT_EQ( 120u, offsetof( EngineBlockHead_t, CollisionGroup ) );
}

TEST( EngineBlockHeadLayout, ParentHandleIsAt124 )
{
	EXPECT_EQ( 124u, offsetof( EngineBlockHead_t, ParentHandle ) );
}

TEST( EngineBlockHeadLayout, OwnerHandleIsAt128 )
{
	EXPECT_EQ( 128u, offsetof( EngineBlockHead_t, OwnerHandle ) );
}

TEST( EngineBlockHeadLayout, MoveTypeIsAt132 )
{
	EXPECT_EQ( 132u, offsetof( EngineBlockHead_t, MoveType ) );
}

TEST( EngineBlockHeadLayout, FlagsIsAt136 )
{
	EXPECT_EQ( 136u, offsetof( EngineBlockHead_t, Flags ) );
}

TEST( EngineBlockHeadLayout, EffectsIsAt140 )
{
	EXPECT_EQ( 140u, offsetof( EngineBlockHead_t, Effects ) );
}

TEST( EngineBlockHeadLayout, WaterLevelIsAt144 )
{
	EXPECT_EQ( 144u, offsetof( EngineBlockHead_t, WaterLevel ) );
}

// The word that was Padding0. It is the same word at the same offset -- what
// changed is that it now carries the parenting registry's child count, which is
// what lets the tail of a position write decide whether to cross into managed
// code without crossing to find out.
TEST( EngineBlockHeadLayout, ChildCountIsAt148 )
{
	EXPECT_EQ( 148u, offsetof( EngineBlockHead_t, ChildCount ) );
}

// THE TELEPORT PAIR, and this offset is load-bearing twice over: it is where
// m_ubInterpolationFrame's SendProp and RecvProp both point
// (standalone_nettable.h, built as BlockBase() + SS_BLOCK_HEAD_OFFSET), and it
// is the offset managed code announces through EntityMarkBlockChanged after
// bumping the counter. A prop built from one number and a mark computed from
// another matches no prop at all in MapPropOffsetsToIndices.
TEST( EngineBlockHeadLayout, InterpolationFrameIsAt152 )
{
	EXPECT_EQ( 152u, offsetof( EngineBlockHead_t, InterpolationFrame ) );
}

// The client's own note of what it last reset on, beside it. The two words
// together are what keeps the head a multiple of eight, which is what the
// server tail behind it needs -- see the generated header's prose.
TEST( EngineBlockHeadLayout, OldInterpolationFrameIsAt156 )
{
	EXPECT_EQ( 156u, offsetof( EngineBlockHead_t, OldInterpolationFrame ) );
}

//-----------------------------------------------------------------------------
// EngineBlockServerTail_t: 4 fields, 120 bytes.
//-----------------------------------------------------------------------------

TEST( EngineBlockServerLayout, TheStructIs120BytesWide )
{
	EXPECT_EQ( 120u, sizeof( EngineBlockServerTail_t ) );
}

TEST( EngineBlockServerLayout, PvsInfoIsAt0 )
{
	EXPECT_EQ( 0u, offsetof( EngineBlockServerTail_t, PvsInfo ) );
}

TEST( EngineBlockServerLayout, PlayerStateIsAt40 )
{
	EXPECT_EQ( 40u, offsetof( EngineBlockServerTail_t, PlayerState ) );
}

TEST( EngineBlockServerLayout, ViewOriginIsAt104 )
{
	EXPECT_EQ( 104u, offsetof( EngineBlockServerTail_t, ViewOrigin ) );
}

TEST( EngineBlockServerLayout, ViewOriginFrameIsAt116 )
{
	EXPECT_EQ( 116u, offsetof( EngineBlockServerTail_t, ViewOriginFrame ) );
}

//-----------------------------------------------------------------------------
// EngineBlockClientTail_t: 36 fields, 208 bytes.
//-----------------------------------------------------------------------------

TEST( EngineBlockClientLayout, TheStructIs208BytesWide )
{
	EXPECT_EQ( 208u, sizeof( EngineBlockClientTail_t ) );
}

TEST( EngineBlockClientLayout, RenderOriginIsAt0 )
{
	EXPECT_EQ( 0u, offsetof( EngineBlockClientTail_t, RenderOrigin ) );
}

TEST( EngineBlockClientLayout, RenderAnglesIsAt12 )
{
	EXPECT_EQ( 12u, offsetof( EngineBlockClientTail_t, RenderAngles ) );
}

TEST( EngineBlockClientLayout, RenderMinsIsAt24 )
{
	EXPECT_EQ( 24u, offsetof( EngineBlockClientTail_t, RenderMins ) );
}

TEST( EngineBlockClientLayout, RenderMaxsIsAt36 )
{
	EXPECT_EQ( 36u, offsetof( EngineBlockClientTail_t, RenderMaxs ) );
}

TEST( EngineBlockClientLayout, RenderColorRIsAt48 )
{
	EXPECT_EQ( 48u, offsetof( EngineBlockClientTail_t, RenderColorR ) );
}

TEST( EngineBlockClientLayout, RenderColorGIsAt49 )
{
	EXPECT_EQ( 49u, offsetof( EngineBlockClientTail_t, RenderColorG ) );
}

TEST( EngineBlockClientLayout, RenderColorBIsAt50 )
{
	EXPECT_EQ( 50u, offsetof( EngineBlockClientTail_t, RenderColorB ) );
}

TEST( EngineBlockClientLayout, RenderColorAIsAt51 )
{
	EXPECT_EQ( 51u, offsetof( EngineBlockClientTail_t, RenderColorA ) );
}

TEST( EngineBlockClientLayout, FxBlendIsAt52 )
{
	EXPECT_EQ( 52u, offsetof( EngineBlockClientTail_t, FxBlend ) );
}

TEST( EngineBlockClientLayout, SkinIsAt56 )
{
	EXPECT_EQ( 56u, offsetof( EngineBlockClientTail_t, Skin ) );
}

TEST( EngineBlockClientLayout, BodyIsAt60 )
{
	EXPECT_EQ( 60u, offsetof( EngineBlockClientTail_t, Body ) );
}

TEST( EngineBlockClientLayout, ShadowChoiceIsAt64 )
{
	EXPECT_EQ( 64u, offsetof( EngineBlockClientTail_t, ShadowChoice ) );
}

TEST( EngineBlockClientLayout, RenderGroupIsAt68 )
{
	EXPECT_EQ( 68u, offsetof( EngineBlockClientTail_t, RenderGroup ) );
}

TEST( EngineBlockClientLayout, SequenceIsAt72 )
{
	EXPECT_EQ( 72u, offsetof( EngineBlockClientTail_t, Sequence ) );
}

TEST( EngineBlockClientLayout, CycleIsAt76 )
{
	EXPECT_EQ( 76u, offsetof( EngineBlockClientTail_t, Cycle ) );
}

TEST( EngineBlockClientLayout, PlaybackRateIsAt80 )
{
	EXPECT_EQ( 80u, offsetof( EngineBlockClientTail_t, PlaybackRate ) );
}

TEST( EngineBlockClientLayout, AnimTimeIsAt84 )
{
	EXPECT_EQ( 84u, offsetof( EngineBlockClientTail_t, AnimTime ) );
}

TEST( EngineBlockClientLayout, SimulationTimeIsAt88 )
{
	EXPECT_EQ( 88u, offsetof( EngineBlockClientTail_t, SimulationTime ) );
}

TEST( EngineBlockClientLayout, RenderTransformIsAt92 )
{
	EXPECT_EQ( 92u, offsetof( EngineBlockClientTail_t, RenderTransform ) );
}

TEST( EngineBlockClientLayout, DrawnIsAt140 )
{
	EXPECT_EQ( 140u, offsetof( EngineBlockClientTail_t, Drawn ) );
}

TEST( EngineBlockClientLayout, TransparentIsAt144 )
{
	EXPECT_EQ( 144u, offsetof( EngineBlockClientTail_t, Transparent ) );
}

TEST( EngineBlockClientLayout, ShadowTypeIsAt148 )
{
	EXPECT_EQ( 148u, offsetof( EngineBlockClientTail_t, ShadowType ) );
}

TEST( EngineBlockClientLayout, ShadowFlagsIsAt152 )
{
	EXPECT_EQ( 152u, offsetof( EngineBlockClientTail_t, ShadowFlags ) );
}

TEST( EngineBlockClientLayout, DormantIsAt156 )
{
	EXPECT_EQ( 156u, offsetof( EngineBlockClientTail_t, Dormant ) );
}

TEST( EngineBlockClientLayout, ViewModelIsAt160 )
{
	EXPECT_EQ( 160u, offsetof( EngineBlockClientTail_t, ViewModel ) );
}

TEST( EngineBlockClientLayout, CustomDrawIsAt164 )
{
	EXPECT_EQ( 164u, offsetof( EngineBlockClientTail_t, CustomDraw ) );
}

TEST( EngineBlockClientLayout, TranslucentIsAt168 )
{
	EXPECT_EQ( 168u, offsetof( EngineBlockClientTail_t, Translucent ) );
}

TEST( EngineBlockClientLayout, OwnerSeenRuleIsAt172 )
{
	EXPECT_EQ( 172u, offsetof( EngineBlockClientTail_t, OwnerSeenRule ) );
}

TEST( EngineBlockClientLayout, FadeStyleIsAt176 )
{
	EXPECT_EQ( 176u, offsetof( EngineBlockClientTail_t, FadeStyle ) );
}

TEST( EngineBlockClientLayout, FadeStartDistanceIsAt180 )
{
	EXPECT_EQ( 180u, offsetof( EngineBlockClientTail_t, FadeStartDistance ) );
}

TEST( EngineBlockClientLayout, FadeEndDistanceIsAt184 )
{
	EXPECT_EQ( 184u, offsetof( EngineBlockClientTail_t, FadeEndDistance ) );
}

TEST( EngineBlockClientLayout, FadeMinAlphaIsAt188 )
{
	EXPECT_EQ( 188u, offsetof( EngineBlockClientTail_t, FadeMinAlpha ) );
}

TEST( EngineBlockClientLayout, ModelTypeIsAt192 )
{
	EXPECT_EQ( 192u, offsetof( EngineBlockClientTail_t, ModelType ) );
}

TEST( EngineBlockClientLayout, PolicyInputsIsAt196 )
{
	EXPECT_EQ( 196u, offsetof( EngineBlockClientTail_t, PolicyInputs ) );
}

TEST( EngineBlockClientLayout, PolicyOutputsIsAt200 )
{
	EXPECT_EQ( 200u, offsetof( EngineBlockClientTail_t, PolicyOutputs ) );
}

TEST( EngineBlockClientLayout, HitboxSetIsAt204 )
{
	EXPECT_EQ( 204u, offsetof( EngineBlockClientTail_t, HitboxSet ) );
}

//-----------------------------------------------------------------------------
// EngineBlock_t: 3 fields, 488 bytes.
//-----------------------------------------------------------------------------

TEST( EngineBlockBlockLayout, TheStructIs488BytesWide )
{
	EXPECT_EQ( 488u, sizeof( EngineBlock_t ) );
}

TEST( EngineBlockBlockLayout, HeadIsAt0 )
{
	EXPECT_EQ( 0u, offsetof( EngineBlock_t, Head ) );
}

TEST( EngineBlockBlockLayout, ServerIsAt160 )
{
	EXPECT_EQ( 160u, offsetof( EngineBlock_t, Server ) );
}

TEST( EngineBlockBlockLayout, ClientIsAt280 )
{
	EXPECT_EQ( 280u, offsetof( EngineBlock_t, Client ) );
}

} // namespace
