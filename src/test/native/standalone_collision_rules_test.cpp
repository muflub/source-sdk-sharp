//========= SourceSharp — tests for the collision-rules invalidation ==========//
//
// THIS ONE EXISTS BECAUSE OF A CRATE THAT FELL SEVEN THOUSAND UNITS THROUGH A
// FLOOR EVERY OTHER PROP WAS STANDING ON.
//
// The live gate's `push` step spawns one physics prop and reports how far it
// falls before it settles. In lane 3f the number came back as 5777.9, and in
// lane 3f-shadow as 7389.6 — while the map's own hundred and eighty props were
// exactly where they had always been, the world's physics object was present
// with its collisions on, and the collision rule table said in so many words
// that the crate and the world SHOULD collide (`refusedByTheWorld` counted the
// crate and did not include it).
//
// Nothing in the rule table was wrong. The answer was cached one layer below
// it: `IPhysicsCollisionSolver::ShouldCollide` is asked by vphysics ONCE, when
// a pair comes into proximity, and never again until somebody calls
// `IPhysicsObject::RecheckCollisionFilter`. Stock's CBaseEntity calls that from
// every setter that can change the answer and says why in a comment
// (baseentity_shared.cpp:2714). The parallel hierarchy's equivalent setters —
// CStandaloneEntity::SetSolidity and ::SetCollisionGroup — called nothing.
//
// EVERY WAY THIS CAN BE WRONG IS SILENT. There is no error, no warning, no
// assert; the rule table reads correct, the object is in the environment, the
// world is solid, and one prop simply does not touch anything for the rest of
// its life. So the rule is a pure function in a header with no engine in it and
// the three cases it can be in are checked here.
//
// See game/server/standalone_collision_rules.h, and the two call sites in
// shell_standalone_base.cpp.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/standalone_collision_rules.h"

namespace
{

//-----------------------------------------------------------------------------
// A stand-in for IPhysicsObject that counts. The real interface is fifty
// virtuals deep and every one of them needs vphysics_interface.h and the tier
// libraries; the ONE thing this rule does to an object is call
// RecheckCollisionFilter on it, so that is the whole of the stub.
//
// A counter and not a bool, because "asked once" and "asked four times" are
// different answers and only one of them is the rule: a setter that told
// vphysics twice would pass a boolean test and would be doing twice the work
// per solidity change, on every prop, for the life of the map.
//-----------------------------------------------------------------------------
struct CountingObject
{
	int nRechecks = 0;

	void RecheckCollisionFilter() { nRechecks++; }
};

//-----------------------------------------------------------------------------
// The rule, on an object that exists. This is the case the bug was: the crate
// HAD a physics object, its solidity changed after that object was already in
// the solver, and nothing asked vphysics again.
//-----------------------------------------------------------------------------
TEST( StandaloneCollisionRules, AnObjectIsAskedAgain )
{
	CountingObject object;

	EXPECT_TRUE( SourceSharpCollisionRulesChanged( &object ) );
	EXPECT_EQ( 1, object.nRechecks );
}

//-----------------------------------------------------------------------------
// ONCE PER CHANGE, not once per anything else. RecheckCollisionFilter walks
// IVP's mindist list for the object and re-asks the filter for each pair; it is
// not free, and a caller that fired it twice for one change would be paying for
// the whole list twice on every solidity change in the map.
//-----------------------------------------------------------------------------
TEST( StandaloneCollisionRules, ExactlyOnceEachTimeItIsAsked )
{
	CountingObject object;

	SourceSharpCollisionRulesChanged( &object );
	SourceSharpCollisionRulesChanged( &object );
	SourceSharpCollisionRulesChanged( &object );

	EXPECT_EQ( 3, object.nRechecks );
}

//-----------------------------------------------------------------------------
// NO OBJECT IS NOT AN ERROR, and it is the common case rather than an edge one:
// CStandaloneEntity::SetSolidity is called during spawn, before InitPhysics has
// made anything, for every entity in the map. There is nothing cached about an
// object that does not exist.
//
// The RETURN VALUE is what makes this a check rather than a shrug: "there was
// no object" and "the object was told" have to be distinguishable, or a version
// of this function that quietly did nothing at all would pass.
//-----------------------------------------------------------------------------
TEST( StandaloneCollisionRules, NoObjectIsAskedNothingAndSaysSo )
{
	CountingObject *pNothing = nullptr;

	EXPECT_FALSE( SourceSharpCollisionRulesChanged( pNothing ) );
}

//-----------------------------------------------------------------------------
// AND IT DOES NOT REACH FOR A SECOND OBJECT. One entity, one object on this
// hierarchy — CStandaloneEntity::InitPhysics creates exactly one and
// physicsowner.h states the rule ("an entity on the parallel hierarchy has
// exactly one object") — so telling one must not touch another.
//
// Written as two stubs rather than argued, because the stock function this
// replaces walks a LIST (VPhysicsGetObjectList, up to
// VPHYSICS_MAX_OBJECT_LIST_COUNT) and a port that carried the walk across
// without the list would be reading past the one object it has.
//-----------------------------------------------------------------------------
TEST( StandaloneCollisionRules, TellingOneObjectDoesNotTouchAnother )
{
	CountingObject mine;
	CountingObject somebodyElses;

	SourceSharpCollisionRulesChanged( &mine );

	EXPECT_EQ( 1, mine.nRechecks );
	EXPECT_EQ( 0, somebodyElses.nRechecks );
}

//-----------------------------------------------------------------------------
// THE OFF SWITCH, both ways. `sourcesharp_collision_rules_recheck 0` has to be
// the build before the fix EXACTLY -- not "mostly" -- because it is the control
// arm of the measurement, and an arm that still told vphysics half the time
// would make the two arms agree for a reason that has nothing to do with the
// bug.
//-----------------------------------------------------------------------------
TEST( StandaloneCollisionRules, DisarmedAsksNothing )
{
	CountingObject object;

	EXPECT_FALSE( SourceSharpCollisionRulesChanged( &object, false ) );
	EXPECT_EQ( 0, object.nRechecks );
}

TEST( StandaloneCollisionRules, ArmedAsksExactlyAsTheUnswitchedRuleDoes )
{
	CountingObject object;

	EXPECT_TRUE( SourceSharpCollisionRulesChanged( &object, true ) );
	EXPECT_EQ( 1, object.nRechecks );
}

//-----------------------------------------------------------------------------
// AND ARMED WITH NOTHING TO TELL IS STILL "NOTHING WAS ASKED". The two
// conditions are independent and the switch must not turn a missing object into
// a claim that something was told.
//-----------------------------------------------------------------------------
TEST( StandaloneCollisionRules, ArmedWithNoObjectAsksNothing )
{
	CountingObject *pNothing = nullptr;

	EXPECT_FALSE( SourceSharpCollisionRulesChanged( pNothing, true ) );
}

} // namespace
