//========= SourceSharp — tests for the particle follow mask =================//
//
// The rule in particle_follow_mask.h, pinned in the two ways it can be wrong
// without anything reporting it:
//
//   A cleared bit 0 strands the effect where it was born. The flame would still
//   draw, in a plausible place, while its owner walked away — so this is the
//   failure that a screenshot does not catch.
//
//   A walk bounded by the ARRAY (64) rather than by the MASK (32 bits) reads a
//   bit that does not exist. In C++ that is `1 << 32`, which is undefined
//   behaviour and in practice wraps to bit 0 — so a request for point 32 would
//   quietly become a request for point 0, and every point past 31 would alias
//   onto a low one.
//
// Engine-free.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/particle_follow_mask.h"

namespace
{

//-----------------------------------------------------------------------------
// Bit 0 is not optional.
//-----------------------------------------------------------------------------

TEST( ParticleFollowMask, PointZeroIsAlwaysFollowed )
{
	// The flame's own request, which already has it.
	EXPECT_TRUE( SourceSharpParticleFollow_Wants(
		SourceSharpParticleFollow_Normalise( 0x3 ), 0 ) );

	// A request that left it out. Point 0 is where the effect was created, so a
	// system that stopped following it would emit at the place the flame was
	// lit for ever.
	EXPECT_TRUE( SourceSharpParticleFollow_Wants(
		SourceSharpParticleFollow_Normalise( 0x2 ), 0 ) );

	// And the degenerate request.
	EXPECT_TRUE( SourceSharpParticleFollow_Wants(
		SourceSharpParticleFollow_Normalise( 0 ), 0 ) );
}

TEST( ParticleFollowMask, NothingElseIsAddedForFree )
{
	const int nMask = SourceSharpParticleFollow_Normalise( 0 );

	// The default is point 0 ALONE. If this ever became "everything", every
	// env_fire_* system in the map would start being fed a control point stock
	// leaves at the origin, which is a change to eight shipped effects made by
	// accident.
	for ( int nPoint = 1; nPoint <= kSourceSharpParticleMaxFollowPoint; ++nPoint )
		EXPECT_FALSE( SourceSharpParticleFollow_Wants( nMask, nPoint ) )
			<< "point " << nPoint << " is fed by the default mask";
}

TEST( ParticleFollowMask, TheFlameAsksForPointsZeroAndOne )
{
	// ClientEntityFlame.FollowPoints, which is 0b11. This is the value that
	// stops burning_character emitting at the centre of the map.
	const int nMask = SourceSharpParticleFollow_Normalise( 0x3 );

	EXPECT_TRUE( SourceSharpParticleFollow_Wants( nMask, 0 ) );
	EXPECT_TRUE( SourceSharpParticleFollow_Wants( nMask, 1 ) );
	EXPECT_FALSE( SourceSharpParticleFollow_Wants( nMask, 2 ) );
}

TEST( ParticleFollowMask, AHigherPointIsHonoured )
{
	const int nMask = SourceSharpParticleFollow_Normalise( 1 << 7 );

	EXPECT_TRUE( SourceSharpParticleFollow_Wants( nMask, 7 ) );
	EXPECT_TRUE( SourceSharpParticleFollow_Wants( nMask, 0 ) );
	EXPECT_FALSE( SourceSharpParticleFollow_Wants( nMask, 6 ) );
	EXPECT_FALSE( SourceSharpParticleFollow_Wants( nMask, 8 ) );
}

//-----------------------------------------------------------------------------
// The bound is the mask's, not the array's.
//-----------------------------------------------------------------------------

TEST( ParticleFollowMask, TheTopBitOfTheMaskIsReachable )
{
	// 31 is the last point a 32-bit mask can name, and it must not be off by
	// one in the safe direction either: a walk to < 31 would silently drop it.
	const int nMask = SourceSharpParticleFollow_Normalise( 1 << kSourceSharpParticleMaxFollowPoint );

	EXPECT_TRUE( SourceSharpParticleFollow_Wants( nMask, kSourceSharpParticleMaxFollowPoint ) );
}

TEST( ParticleFollowMask, PointsPastTheMaskAreRefusedRatherThanAliased )
{
	// Every bit set. Point 32 and beyond still answer NO — the shift that would
	// test them is undefined and in practice wraps onto a low bit, which would
	// make "follow point 32" mean "follow point 0".
	const int nMask = -1;

	EXPECT_TRUE( SourceSharpParticleFollow_Wants( nMask, kSourceSharpParticleMaxFollowPoint ) );
	EXPECT_FALSE( SourceSharpParticleFollow_Wants( nMask, 32 ) );
	EXPECT_FALSE( SourceSharpParticleFollow_Wants( nMask, 63 ) );

	// MAX_PARTICLE_CONTROL_POINTS is 64, so this is the first point that is not
	// even in the engine's array.
	EXPECT_FALSE( SourceSharpParticleFollow_Wants( nMask, 64 ) );
}

TEST( ParticleFollowMask, ANegativePointIsRefused )
{
	EXPECT_FALSE( SourceSharpParticleFollow_Wants( -1, -1 ) );
	EXPECT_FALSE( SourceSharpParticleFollow_Wants( -1, -100 ) );
}

//-----------------------------------------------------------------------------
// The default is the value the existing effects depend on.
//-----------------------------------------------------------------------------

TEST( ParticleFollowMask, TheDefaultIsBitZero )
{
	// Written down rather than inferred: this constant is what
	// SourceSharp_ParticleSystemStart stores for a system nobody configures,
	// and every ss_firesmoke and ss_plasma in the game relies on it being
	// exactly point 0.
	EXPECT_EQ( 1, (int)kSourceSharpParticleFollowDefault );
	EXPECT_EQ( 31, (int)kSourceSharpParticleMaxFollowPoint );
}

} // namespace
