//========= SourceSharp — tests for the blast blocking rule ==================//
//
// Stock's rule that something standing between a blast and its victim keeps a
// share of the damage. It lived inside CGameRules::RadiusDamage and was written
// over CBaseEntity pointers, so it was skipped entirely whenever the thing in
// the way was on the parallel hierarchy: a managed crate shielded NOTHING while
// an SDK crate of the same mass shielded a quarter of its weight.
//
// What is pinned here is the rule stated over identities, which is what makes
// the two hierarchies answer alike:
//
//   - a blocker is anything the trace stopped on that is not the victim and is
//     not owned by the victim, WHATEVER kind of entity it is — the rule never
//     asks, and cannot, because it is handed handles;
//   - the world (and a static prop, and anything else that is not an entity)
//     is not a blocker: it arrives as NULL and is handled by the CONTENTS_SOLID
//     retrace instead;
//   - the share is mass/350, so 350kg and up keeps everything;
//   - something the solver does not move keeps a flat quarter.
//
// The identity arguments are IHandleEntity pointers that are never
// dereferenced, so this binary needs no engine and no entity: two distinct
// addresses are two distinct entities, which is exactly as much as the rule
// knows about them. That is the whole reason the rule is in a header — see
// blast_blocking.h.
//
// WHAT IS NOT HERE, deliberately: whether a managed crate and an SDK crate of
// the same mass shield the same amount in the running game. This binary cannot
// build either one. That is measured live, and the numbers are in the commit
// message.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../../game/shared/blast_blocking.h"

namespace
{

// Three entities, as the only thing the rule knows about them: distinct
// addresses. Never dereferenced — IHandleEntity is incomplete in this binary
// and that is the point of the rule taking handles.
IHandleEntity *const kVictim  = reinterpret_cast< IHandleEntity * >( 0x1000 );
IHandleEntity *const kCrate   = reinterpret_cast< IHandleEntity * >( 0x2000 );
IHandleEntity *const kShooter = reinterpret_cast< IHandleEntity * >( 0x3000 );

//=============================================================================//
// Who is in the way.
//=============================================================================//

//-----------------------------------------------------------------------------
// The case the bug was. The rule is handed a blocker and says so; nothing in it
// looks at what KIND of entity the handle names, because there is nothing in a
// handle to look at. The version this replaced resolved the handle to a
// CBaseEntity first and answered "nobody is in the way" for every entity that
// was not one.
//-----------------------------------------------------------------------------
TEST( BlastBlocking, SomethingBetweenTheBlastAndTheVictimIsInTheWay )
{
	EXPECT_TRUE( BlastBlockerCounts( kVictim, kCrate, NULL ) )
		<< "a crate between the blast and the victim is in the way";
}

//-----------------------------------------------------------------------------
// The world, a static prop, and anything else that is not an entity at all.
// NULL is how they arrive; the CONTENTS_SOLID retrace above this rule is what
// deals with them, and answering "in the way" here would double-count.
//-----------------------------------------------------------------------------
TEST( BlastBlocking, NothingHitIsNobodyInTheWay )
{
	EXPECT_FALSE( BlastBlockerCounts( kVictim, NULL, NULL ) )
		<< "a trace that stopped on the world found nobody in the way";
}

//-----------------------------------------------------------------------------
// The trace stopping ON the victim is the ordinary case — it means the blast
// reached it. A victim that shielded itself would take a quarter less from
// every blast that could see it perfectly well.
//-----------------------------------------------------------------------------
TEST( BlastBlocking, TheVictimDoesNotShieldItself )
{
	EXPECT_FALSE( BlastBlockerCounts( kVictim, kVictim, NULL ) )
		<< "the victim is not in its own way";
}

//-----------------------------------------------------------------------------
// Stock's "shouldn't let children block parents". A rocket owned by the player
// who fired it must not be counted as cover for that player.
//-----------------------------------------------------------------------------
TEST( BlastBlocking, SomethingTheVictimOwnsDoesNotShieldTheVictim )
{
	EXPECT_FALSE( BlastBlockerCounts( kVictim, kCrate, kVictim ) )
		<< "a thing the victim owns is not cover for the victim";
}

//-----------------------------------------------------------------------------
// The other half of that: an owner who is somebody ELSE changes nothing. Only
// the victim's own property is exempt, and a rule that read "owned by anyone"
// would let every rocket in the map shield everybody.
//-----------------------------------------------------------------------------
TEST( BlastBlocking, SomethingOwnedByAThirdPartyStillShields )
{
	EXPECT_TRUE( BlastBlockerCounts( kVictim, kCrate, kShooter ) )
		<< "a crate somebody else owns is still in the way";
}

//=============================================================================//
// How much it keeps.
//=============================================================================//

//-----------------------------------------------------------------------------
// The ratio, at a mass that is not a round fraction of the threshold — 87.5kg
// is a quarter, which would also be the answer if the physics arm had been
// wired to the flat non-physics constant. So the mass here is deliberately NOT
// 87.5.
//-----------------------------------------------------------------------------
TEST( BlastBlocking, APhysicsBlockerKeepsItsShareOfTheThreshold )
{
	EXPECT_FLOAT_EQ( 140.0f / 350.0f, BlastBlockedFraction( true, 140.0f ) )
		<< "a 140kg blocker keeps 140/350 of the blast";

	EXPECT_FLOAT_EQ( 35.0f / 350.0f, BlastBlockedFraction( true, 35.0f ) )
		<< "a 35kg blocker keeps a tenth";
}

//-----------------------------------------------------------------------------
// The threshold. At and above it the caller stops rather than scaling, so the
// value has to cross 1.0 exactly at 350 and not one kilogramme either side.
//-----------------------------------------------------------------------------
TEST( BlastBlocking, TheThresholdIsWhereItKeepsEverything )
{
	EXPECT_LT( BlastBlockedFraction( true, BLAST_MASS_ABSORB_ALL - 1.0f ), 1.0f )
		<< "a kilogramme under the threshold still lets some blast past";

	EXPECT_FLOAT_EQ( 1.0f, BlastBlockedFraction( true, BLAST_MASS_ABSORB_ALL ) )
		<< "at the threshold it keeps exactly all of it";

	EXPECT_GT( BlastBlockedFraction( true, BLAST_MASS_ABSORB_ALL + 1.0f ), 1.0f )
		<< "a kilogramme over the threshold keeps everything";
}

//-----------------------------------------------------------------------------
// Something the solver does not move has no mass to weigh, so there is no ratio
// to take and stock's flat quarter is the answer. The mass argument is ignored,
// and a heavy one must not leak into it — that would be the wire-up mistake
// this checks for.
//-----------------------------------------------------------------------------
TEST( BlastBlocking, ABlockerWithNoPhysicsKeepsAFlatQuarter )
{
	EXPECT_FLOAT_EQ( 0.25f, BlastBlockedFraction( false, 0.0f ) )
		<< "a non-physics blocker keeps a quarter";

	EXPECT_FLOAT_EQ( 0.25f, BlastBlockedFraction( false, 10000.0f ) )
		<< "a mass it does not have does not change what it keeps";
}

//-----------------------------------------------------------------------------
// The two arms are different rules, and a build that folded them together would
// still pass every test above taken alone. A physics blocker at the SAME mass
// the flat quarter corresponds to is the one place they coincide; everywhere
// else they must not.
//-----------------------------------------------------------------------------
TEST( BlastBlocking, PhysicsAndNonPhysicsAreDifferentRules )
{
	EXPECT_FLOAT_EQ( BlastBlockedFraction( false, 0.0f ),
	                 BlastBlockedFraction( true, BLAST_MASS_ABSORB_ALL * 0.25f ) )
		<< "the arms meet at 87.5kg, which is what makes the rest of this a real comparison";

	EXPECT_NE( BlastBlockedFraction( false, 0.0f ),
	           BlastBlockedFraction( true, 30.0f ) )
		<< "a 30kg blocker does not keep a flat quarter";
}

} // namespace
