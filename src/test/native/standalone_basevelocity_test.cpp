//========= SourceSharp — tests for the base-velocity push rule ==============//
//
// This one exists because of a conveyor that carried nobody.
//
// SourceSharp_EntityAddBaseVelocity resolved its subject as `.pBase` — a
// CBaseEntity — and returned quietly when that was NULL. In this mod the player
// is a CStandalonePlayer, on the parallel hierarchy, so `.pBase` is NULL for
// every player there has ever been. Riders.Ride called it once per tick per
// belt and it did nothing at all, in silence: the sixth outbreak of the
// NULL-subject family (docs/divergences.md) reaching a channel rather than a
// message.
//
// Fixing it means a second arm, and two arms are what drift. The rule the two
// share is in game/server/standalone_basevelocity.h and is pinned here, because
// the ways it can be wrong are all quiet ones: an overwrite instead of a sum
// loses one of two simultaneous pushes, and a missing ground release turns
// every upward push into nothing at all.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/standalone_basevelocity.h"

namespace
{

//-----------------------------------------------------------------------------
// The ordinary belt tick: nothing unconsumed in the channel, so the push IS the
// channel. Renewed every tick, which is why this is by far the commonest case.
//-----------------------------------------------------------------------------
TEST( StandaloneBaseVelocity, AFreshChannelTakesThePushWhole )
{
	float x, y, z;
	StandaloneBaseVelocity_Accumulate( false, 999.0f, 999.0f, 999.0f,
	                                   100.0f, 0.0f, 0.0f, &x, &y, &z );

	EXPECT_FLOAT_EQ( 100.0f, x );
	EXPECT_FLOAT_EQ( 0.0f, y );
	EXPECT_FLOAT_EQ( 0.0f, z );
}

//-----------------------------------------------------------------------------
// The stale-value trap the flag exists for. With the flag clear the member
// still holds last tick's push, which the movement code is about to fold into
// real velocity; adding to it would count that push twice. 999 above is there
// to fail loudly if the guard is ever dropped.
//-----------------------------------------------------------------------------
TEST( StandaloneBaseVelocity, TwoPushesInOneTickSum )
{
	float x, y, z;

	// A belt at 100 along X, then a blast adding 40 along X and 200 up, on the
	// same tick and before anything consumed the first.
	StandaloneBaseVelocity_Accumulate( true, 100.0f, 0.0f, 0.0f,
	                                   40.0f, 0.0f, 200.0f, &x, &y, &z );

	EXPECT_FLOAT_EQ( 140.0f, x );
	EXPECT_FLOAT_EQ( 0.0f, y );
	EXPECT_FLOAT_EQ( 200.0f, z );
}

//-----------------------------------------------------------------------------
// Opposing belts cancel rather than the second winning. An overwrite would
// answer -100 here, which is a rider sliding backwards off a floor that is
// pulling both ways.
//-----------------------------------------------------------------------------
TEST( StandaloneBaseVelocity, OpposingPushesCancel )
{
	float x, y, z;
	StandaloneBaseVelocity_Accumulate( true, 100.0f, 0.0f, 0.0f,
	                                   -100.0f, 0.0f, 0.0f, &x, &y, &z );

	EXPECT_FLOAT_EQ( 0.0f, x );
}

//-----------------------------------------------------------------------------
// The ground release. A supported player absorbs an upward push entirely, so
// without this every lift-by-blast and every upward point_push does nothing —
// and does it silently, which is how it would survive.
//-----------------------------------------------------------------------------
TEST( StandaloneBaseVelocity, UpwardPushOnTheGroundReleasesIt )
{
	EXPECT_TRUE( StandaloneBaseVelocity_ReleasesGround( 200.0f, true ) );
}

TEST( StandaloneBaseVelocity, NothingIsReleasedInMidAir )
{
	// Already airborne: there is no ground to let go of, and clearing it again
	// would be a second unstick nudge every tick of a long push.
	EXPECT_FALSE( StandaloneBaseVelocity_ReleasesGround( 200.0f, false ) );
}

TEST( StandaloneBaseVelocity, ASidewaysBeltDoesNotLiftItsRider )
{
	// The conveyor case, and the one that would be most obviously wrong: a belt
	// that unsticks everybody it carries hands them to gravity once a tick.
	EXPECT_FALSE( StandaloneBaseVelocity_ReleasesGround( 0.0f, true ) );
	EXPECT_FALSE( StandaloneBaseVelocity_ReleasesGround( -50.0f, true ) );
}

//-----------------------------------------------------------------------------
// The release is decided on the SUM, not on the push. A downward push landing
// on top of a larger upward one still leaves the subject going up, and it is
// what the movement code will see that has to survive the ground check.
//-----------------------------------------------------------------------------
TEST( StandaloneBaseVelocity, TheSumDecidesTheRelease )
{
	float x, y, z;
	StandaloneBaseVelocity_Accumulate( true, 0.0f, 0.0f, 300.0f,
	                                   0.0f, 0.0f, -100.0f, &x, &y, &z );

	EXPECT_FLOAT_EQ( 200.0f, z );
	EXPECT_TRUE( StandaloneBaseVelocity_ReleasesGround( z, true ) );

	// And the other way: an upward push cancelled by a larger downward one is
	// not an upward push any more.
	StandaloneBaseVelocity_Accumulate( true, 0.0f, 0.0f, 100.0f,
	                                   0.0f, 0.0f, -300.0f, &x, &y, &z );

	EXPECT_FLOAT_EQ( -200.0f, z );
	EXPECT_FALSE( StandaloneBaseVelocity_ReleasesGround( z, true ) );
}

} // namespace
