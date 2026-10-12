//========= SourceSharp — the self-penetration rule, and the crash it fixes ===//
//
// self_penetration_rule.h is one boolean expression, and it is here because the
// version it replaces was a LIVE SEGFAULT rather than a tidy-up.
//
// Stock's CCollisionEvent::ShouldSolvePenetration opens with
//
//     if ( pGameData0 == pGameData1 )
//
// meaning "these two objects belong to the same entity, so this is a ragdoll
// overlapping itself". That reading is right for every object stock ever put in
// the world and wrong the moment something has NO owner, because TWO NULLS
// COMPARE EQUAL -- and three lines later the branch reinterpret_casts that null
// to a C_BaseEntity * and makes a VIRTUAL CALL on it.
//
// Reproduced: SIGSEGV in PhysicsSimulate on the first frame after
// C_StandaloneEntity::BecomeRagdoll. A standalone ragdoll's physics objects
// carry no game data and cannot -- the field is read back as a C_BaseEntity *
// and a standalone entity is not one -- while RagdollActivate gives every one
// of them FVPHYSICS_PART_OF_RAGDOLL, which is the other half of the test. So
// the very first standalone ragdoll to overlap itself killed the game.
//
// THE CASE THAT MATTERS IS BelongToNothingAndAreNotTheSameThing, and it is the
// one a test written from the fixed code would not have thought to write: the
// input that reddens without the guard is not an unusual pair, it is the
// ORDINARY pair on the parallel hierarchy.
//
// No engine, no physics, no matrices: the header holds only the decision.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../../game/client/self_penetration_rule.h"

namespace
{

//-----------------------------------------------------------------------------
// THE CRASH, as a truth value.
//
// Two objects with no game data at all, both flagged as ragdoll parts. Stock's
// test says "same" because null equals null; the rule says no, because there is
// no owner to ask.
//
// Deleting `bHasGameData &&` from the rule reddens exactly this.
//-----------------------------------------------------------------------------
TEST( SelfPenetrationRule, TwoObjectsThatBelongToNothingAreNotTheSameThing )
{
	EXPECT_FALSE( SelfPenetrationSolvedByOwner( /*bHasGameData=*/false,
	                                            /*bSameGameData=*/true,
	                                            /*bPartOfRagdoll=*/true ) );
}

//-----------------------------------------------------------------------------
// And the behaviour that must NOT change: a real ragdoll, with a real owner,
// overlapping itself, still has its own constraint group solve it.
//
// This is the half that makes the fix a guard rather than a removal. A rule
// that answered false here would leave every stock client ragdoll to the
// default solver and the limbs would push each other apart.
//-----------------------------------------------------------------------------
TEST( SelfPenetrationRule, ARealRagdollOverlappingItselfIsStillSolvedByItsOwner )
{
	EXPECT_TRUE( SelfPenetrationSolvedByOwner( true, true, true ) );
}

//-----------------------------------------------------------------------------
// Two DIFFERENT entities touching. Stock's own answer, unchanged: not the same
// thing, so the default solver takes it.
//-----------------------------------------------------------------------------
TEST( SelfPenetrationRule, TwoDifferentEntitiesFallThrough )
{
	EXPECT_FALSE( SelfPenetrationSolvedByOwner( true, false, true ) );
}

//-----------------------------------------------------------------------------
// One entity's two objects, neither of them part of a ragdoll -- a prop with a
// multi-part collision model, say. There is no constraint group to ask.
//-----------------------------------------------------------------------------
TEST( SelfPenetrationRule, TheSameEntityWithoutARagdollFallsThrough )
{
	EXPECT_FALSE( SelfPenetrationSolvedByOwner( true, true, false ) );
}

//-----------------------------------------------------------------------------
// EVERY REMAINING COMBINATION, so the rule is pinned whole rather than at the
// four corners somebody thought of.
//
// Written as an exhaustive walk instead of four more named tests because the
// claim IS exhaustive: exactly one of the eight inputs answers true. A rule
// that started answering true for a second one -- an `||` typed where an `&&`
// was meant -- would keep every test above green.
//-----------------------------------------------------------------------------
TEST( SelfPenetrationRule, ExactlyOneOfTheEightInputsAnswersTrue )
{
	int nTrue = 0;

	for ( int i = 0; i < 8; i++ )
	{
		const bool bHasGameData  = ( i & 1 ) != 0;
		const bool bSameGameData = ( i & 2 ) != 0;
		const bool bPartOfRagdoll = ( i & 4 ) != 0;

		if ( SelfPenetrationSolvedByOwner( bHasGameData, bSameGameData, bPartOfRagdoll ) )
		{
			nTrue++;

			// And it is THAT one. A count of one satisfied by the wrong
			// combination would be a rule that is exactly as broken and just as
			// green.
			EXPECT_TRUE( bHasGameData ) << "answered true for a pair with no owner";
			EXPECT_TRUE( bSameGameData ) << "answered true for two different owners";
			EXPECT_TRUE( bPartOfRagdoll ) << "answered true for a pair with no ragdoll";
		}
	}

	EXPECT_EQ( 1, nTrue );
}

} // namespace
