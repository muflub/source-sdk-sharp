//========= SourceSharp — the push filter's two rejections, both arms ========//
//
// push_blocker_rule.h states what CTraceFilterPushMove refuses to hit. The rule
// is worth a test rather than three lines at the call site for one reason: the
// two arms differ, and every way of getting the difference wrong produces a
// picture nobody can read from outside.
//
//   Give the standalone arm the SDK's move-type clause and it rejects nothing,
//   because there is no move type to compare — a managed crate becomes an
//   immovable blocker and the pusher stops dead against it.
//
//   Give the standalone arm the root-parent clause and it is vacuously false
//   forever, which looks exactly like the clause working.
//
//   Answer PUSH_BLOCKER_NOT_AN_ENTITY the way either entity arm answers and a
//   static prop starts being ignored (or not) by a rule that was never about
//   props.
//
// All three of those are a pusher that moves slightly differently in a map
// nobody is looking at.
//
//=============================================================================//
#include "gtest/gtest.h"

#include "../game/server/push_blocker_rule.h"

//-----------------------------------------------------------------------------
// The SDK arm, which is stock's behaviour and must not have changed.
//-----------------------------------------------------------------------------
TEST( PushBlockerRule, SdkRejectsItsOwnHierarchy )
{
	// Sharing the pusher's root move parent is enough on its own — a pusher
	// does not block on its own parts.
	EXPECT_TRUE( PushFilter_Rejects( PUSH_BLOCKER_SDK, true, false, false ) );
	EXPECT_TRUE( PushFilter_Rejects( PUSH_BLOCKER_SDK, true, true, true ) );
}

TEST( PushBlockerRule, SdkNeedsBothHalvesOfTheVPhysicsClause )
{
	// MOVETYPE_VPHYSICS with a moveable object: the solver will deal with it.
	EXPECT_TRUE( PushFilter_Rejects( PUSH_BLOCKER_SDK, false, true, true ) );

	// A VPHYSICS move type whose object is not moveable — a static physics
	// brush — is a real blocker and must be hit.
	EXPECT_FALSE( PushFilter_Rejects( PUSH_BLOCKER_SDK, false, true, false ) );

	// And an entity with a moveable object that is NOT MOVETYPE_VPHYSICS is
	// moved by something other than the solver, so the pusher must block on it.
	// This is the half the standalone arm deliberately does not have, and
	// getting it backwards here would make the two arms agree by accident.
	EXPECT_FALSE( PushFilter_Rejects( PUSH_BLOCKER_SDK, false, false, true ) );

	EXPECT_FALSE( PushFilter_Rejects( PUSH_BLOCKER_SDK, false, false, false ) );
}

//-----------------------------------------------------------------------------
// The standalone arm. One clause, and it is the object's.
//-----------------------------------------------------------------------------
TEST( PushBlockerRule, StandaloneRejectsWhateverTheSolverIsMoving )
{
	// A managed physics prop: the solver moves it, so the pusher does not block
	// on it. The SDK reaches the same verdict by a different route.
	EXPECT_TRUE( PushFilter_Rejects( PUSH_BLOCKER_STANDALONE, false, false, true ) );

	// No object, or an object the solver will not take anywhere: a real blocker.
	EXPECT_FALSE( PushFilter_Rejects( PUSH_BLOCKER_STANDALONE, false, false, false ) );
}

TEST( PushBlockerRule, StandaloneIgnoresTheTwoSdkOnlyFacts )
{
	// A caller that cannot answer the SDK facts passes false, but the rule must
	// not depend on them at all: nothing on that hierarchy can share an SDK root
	// move parent, and nothing over there has a move type. If either of these
	// ever changed the answer, the arm would have quietly acquired a clause it
	// cannot honestly evaluate.
	for ( int nRootParent = 0; nRootParent < 2; nRootParent++ )
	{
		for ( int nMoveType = 0; nMoveType < 2; nMoveType++ )
		{
			EXPECT_TRUE( PushFilter_Rejects( PUSH_BLOCKER_STANDALONE,
			                                 nRootParent != 0, nMoveType != 0, true ) );
			EXPECT_FALSE( PushFilter_Rejects( PUSH_BLOCKER_STANDALONE,
			                                  nRootParent != 0, nMoveType != 0, false ) );
		}
	}
}

//-----------------------------------------------------------------------------
// The third case, which is neither entity: a static prop is in the partition,
// answers no entity lookup, and this filter has nothing to say about it.
//-----------------------------------------------------------------------------
TEST( PushBlockerRule, SomethingThatIsNotAnEntityIsLeftToTheBaseFilter )
{
	for ( int a = 0; a < 2; a++ )
	{
		for ( int b = 0; b < 2; b++ )
		{
			for ( int c = 0; c < 2; c++ )
			{
				EXPECT_FALSE( PushFilter_Rejects( PUSH_BLOCKER_NOT_AN_ENTITY,
				                                  a != 0, b != 0, c != 0 ) );
			}
		}
	}
}

//-----------------------------------------------------------------------------
// The two arms are not the same function. Pinned explicitly, because a
// refactor that "unified" them would pass every test above except this one.
//-----------------------------------------------------------------------------
TEST( PushBlockerRule, TheTwoArmsDisagreeWhereTheyShould )
{
	// A moveable object without a VPHYSICS move type: hit on the SDK side,
	// ignored on the other. That is the whole of the asymmetry.
	EXPECT_FALSE( PushFilter_Rejects( PUSH_BLOCKER_SDK,        false, false, true ) );
	EXPECT_TRUE ( PushFilter_Rejects( PUSH_BLOCKER_STANDALONE, false, false, true ) );

	// And the root-parent clause exists on one side only.
	EXPECT_TRUE ( PushFilter_Rejects( PUSH_BLOCKER_SDK,        true, false, false ) );
	EXPECT_FALSE( PushFilter_Rejects( PUSH_BLOCKER_STANDALONE, true, false, false ) );
}
