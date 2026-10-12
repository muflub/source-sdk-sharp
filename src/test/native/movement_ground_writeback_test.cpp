//========= SourceSharp — what the movement crossing writes back ============//
//
// plan_entity.md step 2g. The managed movement loop carries "IS there ground"
// as a bool; native carries WHICH entity, remembered from the trace it serviced
// for the loop's own decision. Putting the two back together is a decision, and
// it was written as a pair of EDGE tests — nothing to something, something to
// nothing — which never writes when the ground changes from one entity to
// ANOTHER with no frame of air between.
//
// Nothing in this build could reach that until step 2g gave managed movers
// their riders: the only way to arrive on a new floor was to fall onto it, and
// falling clears the ground on the way. A carried rider does not fall.
//
// Measured on dm_lockdown with the live gate's own lift, before this rule
// existed: the player stands on a prop_physics at z 2.07, a func_movelinear
// rises into them and carries them to z 96.67, and their GroundEntity reads the
// crate for the whole ride and for as long as they stand at the top.
//
// The rule is engine-free in movement_ground_writeback.h so it is checked here
// rather than by riding a lift.
//
//=============================================================================//
#include <gtest/gtest.h>

#include "../game/shared/movement_ground_writeback.h"

//-----------------------------------------------------------------------------
// The bug this exists for, stated as the one case the edge tests missed.
//-----------------------------------------------------------------------------
TEST( MovementGroundWriteBack, GroundChangingFromOneEntityToAnotherIsWritten )
{
	// The loop found ground; the player is holding ground; it is NOT this one.
	EXPECT_EQ( MOVEMENT_GROUND_SET,
	           MovementGround_WriteBack( true, true, false ) )
		<< "a rider carried from a crate onto a lift keeps reporting the crate";
}

TEST( MovementGroundWriteBack, LandingOnGroundFromTheAirIsWritten )
{
	EXPECT_EQ( MOVEMENT_GROUND_SET,
	           MovementGround_WriteBack( true, false, false ) );
}

TEST( MovementGroundWriteBack, LeavingTheGroundIsCleared )
{
	EXPECT_EQ( MOVEMENT_GROUND_CLEAR,
	           MovementGround_WriteBack( false, true, false ) );
}

//-----------------------------------------------------------------------------
// And the two that must NOT write, because writing costs a network mark and a
// prediction word on every tick a player stands still — which is most ticks of
// most players.
//-----------------------------------------------------------------------------
TEST( MovementGroundWriteBack, StandingStillOnTheSameFloorWritesNothing )
{
	EXPECT_EQ( MOVEMENT_GROUND_LEAVE,
	           MovementGround_WriteBack( true, true, true ) );
}

TEST( MovementGroundWriteBack, StayingInTheAirWritesNothing )
{
	EXPECT_EQ( MOVEMENT_GROUND_LEAVE,
	           MovementGround_WriteBack( false, false, false ) );
}

//-----------------------------------------------------------------------------
// THE LOOP'S ANSWER IS THE ONE THAT DECIDES, and the third argument is only
// consulted when it says there is ground.
//
// Its own fact because the arguments are three bools and an implementation that
// read them in the wrong order would satisfy every case above but one: a player
// who has LET GO of the ground while the remembered pointer still happens to
// name what they were standing on. That is every jump.
//-----------------------------------------------------------------------------
TEST( MovementGroundWriteBack, LettingGoWinsEvenWhenTheRememberedGroundMatches )
{
	EXPECT_EQ( MOVEMENT_GROUND_CLEAR,
	           MovementGround_WriteBack( false, true, true ) );
}

TEST( MovementGroundWriteBack, NoGroundAndNoneHeldIsNotAClear )
{
	// Distinguished from CLEAR deliberately: a clear on a player who already
	// has no ground is a wasted network mark on every airborne tick of every
	// jump, and the caller's `if` used to be what stopped it.
	EXPECT_EQ( MOVEMENT_GROUND_LEAVE,
	           MovementGround_WriteBack( false, false, true ) );
}
