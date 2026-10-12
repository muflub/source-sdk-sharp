//========= SourceSharp — tests for the scaled-model collision rule ==========//
//
// The same shape as bullet_glass_test.cpp, and here for the same reason: a
// guard that tested one expression and dereferenced another.
//
// CBaseAnimating::TestCollision decided the whole thing with
// `if ( GetModelScale() != 1.0f )` and then read VPhysicsGetObject() and that
// object's GetCollide() without looking at either. Scale is a property of the
// MODEL; the two pointers are facts about the SOLVER, and a scaled model is not
// evidence of a solver object -- this port's SourceSharp_ShootGib sets the
// scale on a gib before it knows whether the gib got one.
//
// A test cannot fire a trace. What it CAN pin is the decision, which has three
// inputs and one non-obvious half: what to do when there is no solver copy to
// trace against. Both readings compile and neither logs. Answering "nothing was
// hit" makes a visible, solid entity untraceable -- shots through it, no line
// of sight blocked -- which is silent, wrong, and worse than the crash. Falling
// through to the hitbox and bounding-box tests gives a trace that is off by the
// scale factor, which is what an unscaled entity has always got.
//
// Wrong by a factor is a bad trace. Absent is a ghost. That choice is what
// these tests hold, and it is invisible in the call site because it lives in
// the shape of an if.
//
// See game/server/scaled_collision_rule.h.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../../game/server/scaled_collision_rule.h"

namespace
{

//-----------------------------------------------------------------------------
// The ordinary case, and the reason the special case exists at all: a model at
// scale 1 has a compiled hull that is already the right size, so the solver's
// copy is not wanted even when there is one.
//
// This is the half stock had right, and it is asserted rather than assumed
// because a rule rewritten to lead on the pointers -- `bHasObject &&
// bHasCollide && bScaled` reordered by someone tidying -- reads the same and is
// the same, but one written as `bHasObject && bHasCollide` alone would send
// every unscaled physics prop down the scaled path and change the trace for
// every crate in the game with nothing logged.
//-----------------------------------------------------------------------------
TEST( ScaledCollisionRule, AnUnscaledModelNeverUsesTheSolverCopy )
{
	EXPECT_FALSE( ScaledCollisionUsesSolverHull( false, true, true ) );
}

//-----------------------------------------------------------------------------
// And it stays false with no solver object either, which is the overwhelmingly
// common entity: unscaled, unsimulated, traced against its hitboxes.
//-----------------------------------------------------------------------------
TEST( ScaledCollisionRule, AnUnscaledUnsimulatedModelNeverUsesTheSolverCopy )
{
	EXPECT_FALSE( ScaledCollisionUsesSolverHull( false, false, false ) );
}

//-----------------------------------------------------------------------------
// THE CRASH. Stock's whole test was this first argument, so this combination
// answered "use the solver copy" and the next line dereferenced NULL. It is the
// case a scaled gib is in for its entire life when its model has no collision
// data.
//-----------------------------------------------------------------------------
TEST( ScaledCollisionRule, AScaledModelWithNoSolverObjectFallsThrough )
{
	EXPECT_FALSE( ScaledCollisionUsesSolverHull( true, false, false ) );
}

//-----------------------------------------------------------------------------
// The second pointer, which is a DIFFERENT crash one call further on: the
// object exists, GetPosition works, and GetCollide answers NULL because the
// solver holds this one as a sphere. physcollision reads what it is handed.
//
// Guarding only the object -- the obvious half-fix, and the one the reported
// site list would have produced -- passes every test above and dies here.
//-----------------------------------------------------------------------------
TEST( ScaledCollisionRule, AScaledSphereHasNoHullToTraceAgainst )
{
	EXPECT_FALSE( ScaledCollisionUsesSolverHull( true, true, false ) );
}

//-----------------------------------------------------------------------------
// And the one case the special case is for. A rule that answered false here
// would be safe and useless: every scaled model would trace against a hull of
// the wrong size, which is the bug the branch was written to fix.
//-----------------------------------------------------------------------------
TEST( ScaledCollisionRule, AScaledModelWithABuiltHullUsesIt )
{
	EXPECT_TRUE( ScaledCollisionUsesSolverHull( true, true, true ) );
}

//-----------------------------------------------------------------------------
// Totality: all eight input combinations, and exactly one of them is true.
//
// Written as a count rather than as eight more EXPECTs because what is being
// held is a property of the whole function -- "there is one way in" -- and a
// property stated as a property survives someone adding a fourth input. A rule
// that grew a second true case would pass every test above it.
//-----------------------------------------------------------------------------
TEST( ScaledCollisionRule, ExactlyOneOfTheEightCombinationsUsesTheSolverCopy )
{
	int nTrue = 0;

	for ( int i = 0; i < 8; i++ )
	{
		const bool bScaled     = ( i & 1 ) != 0;
		const bool bHasObject  = ( i & 2 ) != 0;
		const bool bHasCollide = ( i & 4 ) != 0;

		if ( ScaledCollisionUsesSolverHull( bScaled, bHasObject, bHasCollide ) )
			nTrue++;
	}

	EXPECT_EQ( 1, nTrue );
}

} // namespace
