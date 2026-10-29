//========= SourceSharp — tests for the standalone movetype report ===========//
//
// This one exists because of a lift that rose THROUGH its passenger.
//
// Riders.Carry filters candidates by movetype before it looks at where any of
// them is standing. The standalone arm of SourceSharp_EntityGetState only ever
// wrote a movetype for an entity with a VPhysics object, and a standalone
// player has none — so every player crossed as MOVETYPE_NONE and was discarded
// one line before the geometry test. Measured live on ss_sandbox at b5920b61:
// belts_lift sent GoUp with a player aboard rose 16 -> 112 while the player
// stayed at 26 and then fell.
//
// What makes this worth pinning rather than eyeballing is that every way it
// can be wrong is SILENT. MOVETYPE_NONE is a legal answer, the carry loop's
// `continue` is a legal path, and the lift still visibly works in the one
// direction that does not need the carry at all. There is no error anywhere in
// the failing case — only a passenger left standing in the air.
//
// So the precedence is a pure function in a header with no engine in it, and
// the three cases that actually occur are checked here.
//
// See game/server/standalone_movetype.h. The carrying itself is C#
// (SourceSharp.Server Riders.Carry) and is tested there.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/standalone_movetype.h"

namespace
{

//-----------------------------------------------------------------------------
// THE REGRESSION. A walking player is MOVETYPE_WALK, which is the one answer
// Riders.Carry accepts. Before the player arm existed this was NONE and the
// lift's own passenger was skipped.
//
// Asserted against SOURCESHARP_MOVETYPE_WALK rather than against 2, so that a
// renumbering of either enum has to break this rather than pass quietly: the
// managed MoveType.Walk and the engine's MOVETYPE_WALK are the same number by
// construction, and that construction is the thing being relied on.
//-----------------------------------------------------------------------------
TEST( StandaloneMoveType, WalkingPlayerIsCarryable )
{
	EXPECT_EQ( SOURCESHARP_MOVETYPE_WALK,
			   StandaloneMoveType_Report( true, SOURCESHARP_MOVETYPE_WALK, false, SOURCESHARP_MOVETYPE_NONE ) );
}

//-----------------------------------------------------------------------------
// And the reason the player arm reports the player's OWN movetype instead of
// hardcoding WALK for anything that IsPlayer(): a noclipping player is not
// standing on anything and must not be dragged along by a brush they are
// flying through. MOVETYPE_NOCLIP is not one of the two Riders.Carry accepts,
// so reporting it truthfully is what leaves them alone.
//-----------------------------------------------------------------------------
TEST( StandaloneMoveType, NoclippingPlayerIsNotCarryable )
{
	const int32_t nReported =
		StandaloneMoveType_Report( true, SOURCESHARP_MOVETYPE_NOCLIP, false, SOURCESHARP_MOVETYPE_NONE );

	EXPECT_EQ( SOURCESHARP_MOVETYPE_NOCLIP, nReported );
	EXPECT_NE( SOURCESHARP_MOVETYPE_WALK, nReported );
	EXPECT_NE( SOURCESHARP_MOVETYPE_STEP, nReported );
}

//-----------------------------------------------------------------------------
// A player wins over the physics arm. Nothing on this hierarchy is both today,
// but the precedence is the load-bearing half of the rule and an order swap
// would be invisible until something gave the player a shadow object.
//-----------------------------------------------------------------------------
TEST( StandaloneMoveType, PlayerOutranksPhysics )
{
	EXPECT_EQ( SOURCESHARP_MOVETYPE_WALK,
			   StandaloneMoveType_Report( true, SOURCESHARP_MOVETYPE_WALK, true, SOURCESHARP_MOVETYPE_VPHYSICS ) );

	EXPECT_EQ( SOURCESHARP_MOVETYPE_NOCLIP,
			   StandaloneMoveType_Report( true, SOURCESHARP_MOVETYPE_NOCLIP, true, SOURCESHARP_MOVETYPE_VPHYSICS ) );
}

//-----------------------------------------------------------------------------
// What was already right, kept right. A simulated standalone entity is moved
// by the solver and says so; trigger_wind and phys_magnet ask for the movetype
// rather than the flag and both meant this.
//-----------------------------------------------------------------------------
TEST( StandaloneMoveType, SimulatedEntityIsVPhysics )
{
	EXPECT_EQ( SOURCESHARP_MOVETYPE_VPHYSICS,
			   StandaloneMoveType_Report( false, 0, true, SOURCESHARP_MOVETYPE_VPHYSICS ) );
}

TEST( StandaloneMoveType, StaticObjectKeepsTheEntitysOwnMoveType )
{
	// VPhysicsInitStatic leaves the entity's movetype alone: a solid
	// prop_dynamic holds a static object and stays MOVETYPE_NONE, which
	// managed writes into the block's MoveType word (EntityPhysicsCreation).
	EXPECT_EQ( SOURCESHARP_MOVETYPE_NONE, StandaloneMoveType_Report( false, 0, true, SOURCESHARP_MOVETYPE_NONE ) );
}

TEST( StandaloneMoveType, AHeldMoveTypeNeedsAnObject )
{
	// The word follows the object: with none held the entity is not simulated,
	// whatever an earlier object left in it.
	EXPECT_EQ( SOURCESHARP_MOVETYPE_NONE, StandaloneMoveType_Report( false, 0, false, SOURCESHARP_MOVETYPE_VPHYSICS ) );
}

//-----------------------------------------------------------------------------
// And the honest silence. A managed brush mover is stepped by UpdateMovement,
// not by any movetype, so it reports NONE — which is also what keeps a lift
// from trying to carry another lift.
//-----------------------------------------------------------------------------
TEST( StandaloneMoveType, PlainStandaloneEntityIsNone )
{
	EXPECT_EQ( SOURCESHARP_MOVETYPE_NONE, StandaloneMoveType_Report( false, 0, false, SOURCESHARP_MOVETYPE_NONE ) );

	// The player movetype argument is not read when the subject is not a
	// player: a mover must not become carryable because of a stale int.
	EXPECT_EQ( SOURCESHARP_MOVETYPE_NONE,
			   StandaloneMoveType_Report( false, SOURCESHARP_MOVETYPE_WALK, false, SOURCESHARP_MOVETYPE_NONE ) );
}

} // namespace
