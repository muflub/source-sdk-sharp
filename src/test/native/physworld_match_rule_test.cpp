//========= SourceSharp — the physics-census matcher, and the silent subset ===//
//
// physworld_match_rule.h decides which objects `ss_physworld_object` reports
// on. It is three lines of policy and it is tested because the line it replaces
// was a CONTROL THAT COULD NOT FAIL.
//
// tools/3a-penetration-granularity asks the command for the whole environment,
// because its third control — "can the gameFlags field show a bit at all?" —
// is a statement about every object in the world and not about the two props
// the probe made. It asked with an empty substring, on the reading that
// everything contains the empty string. The command answered `matched=0 of 427`
// on both maps; the probe fell through to a "prop" fallback and reported that
// it had read the environment. It had read 415 of 427, and its scope line said
// otherwise.
//
// THE CASE THAT MATTERS IS EmptyNeedleMatchesNothing. It is the one a test
// written from the fixed code would never think to write, because the
// behaviour it pins belongs to tier1 and contradicts what every reader of
// "substring search" expects. Everything else here follows from it.
//
// tier1 on the link line rather than a stub: the whole point is what V_stristr
// really does, so a stand-in for it would test the belief instead of the
// function. See the note in tools/... — a fixture for native drifts silently.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "tier1/strtools.h"
#include "../game/server/physworld_match_rule.h"

namespace
{

//-----------------------------------------------------------------------------
// THE TRAP, ASSERTED AGAINST THE REAL FUNCTION.
//
// V_stristr walks the haystack comparing each letter against the needle's FIRST
// character; for an empty needle that character is the terminator, which no
// letter equals, so it falls out of the loop and returns NULL. Every probe that
// asks for "" therefore gets nothing, from a call that looks like it asks for
// everything.
//
// If tier1 ever changes this, the rule below stops being necessary and this
// test is where that shows up — which is the point of pinning it here rather
// than describing it in a comment.
//-----------------------------------------------------------------------------
TEST( PhysWorldMatchRule, EmptyNeedleMatchesNothingInTier1 )
{
	EXPECT_EQ( NULL, Q_stristr( "prop_physics", "" ) );
	EXPECT_EQ( NULL, Q_stristr( "world", "" ) );
}

//-----------------------------------------------------------------------------
// And so the empty request is NOT the whole environment. This is the assertion
// that would have reddened the first revision of the probe.
//-----------------------------------------------------------------------------
TEST( PhysWorldMatchRule, TheEmptyRequestIsNotEveryObject )
{
	EXPECT_FALSE( PhysWorldMatchAllRequested( "" ) );
	EXPECT_FALSE( PhysWorldObjectMatches( "prop_physics", "" ) );
}

//-----------------------------------------------------------------------------
// "*" is, which is the spelling the command documents and the probe uses.
//-----------------------------------------------------------------------------
TEST( PhysWorldMatchRule, TheStarRequestIsEveryObject )
{
	EXPECT_TRUE( PhysWorldMatchAllRequested( "*" ) );
	EXPECT_TRUE( PhysWorldObjectMatches( "prop_physics", "*" ) );
	EXPECT_TRUE( PhysWorldObjectMatches( "world", "*" ) );
}

//-----------------------------------------------------------------------------
// A NAMELESS OBJECT IS IN THE ENVIRONMENT TOO. IPhysicsObject names come from
// whoever made the object and are not guaranteed; a census that quietly skipped
// the unnamed ones would be the same class of subset the empty needle produced,
// arrived at from the other direction.
//
// Deleting the `return true` arm from PhysWorldObjectMatches reddens exactly
// this and nothing else: the star cases above still pass on a named object.
//-----------------------------------------------------------------------------
TEST( PhysWorldMatchRule, TheStarRequestIncludesAnObjectWithNoName )
{
	EXPECT_TRUE( PhysWorldObjectMatches( NULL, "*" ) );
}

//-----------------------------------------------------------------------------
// ...and no other request does. A nameless object cannot answer a substring.
//-----------------------------------------------------------------------------
TEST( PhysWorldMatchRule, AnObjectWithNoNameAnswersNoSubstring )
{
	EXPECT_FALSE( PhysWorldObjectMatches( NULL, "prop" ) );
}

//-----------------------------------------------------------------------------
// ONLY A BARE STAR, and this is the half that keeps the widening from being
// accidental. "prop*crate" is a request for a literal asterisk: a rule that
// treated any name containing one as match-all would turn a narrow census into
// a whole-world census on punctuation, and report far more than the caller
// asked for while looking like it worked.
//
// Relaxing the rule to `pszWanted[0] == '*'` reddens this.
//-----------------------------------------------------------------------------
TEST( PhysWorldMatchRule, OnlyABareStarWidensTheRequest )
{
	EXPECT_FALSE( PhysWorldMatchAllRequested( "*prop" ) );
	EXPECT_FALSE( PhysWorldMatchAllRequested( "prop*" ) );
	EXPECT_FALSE( PhysWorldMatchAllRequested( "**" ) );
}

//-----------------------------------------------------------------------------
// The ordinary case still works, and it is case-insensitive, because every
// probe in tools/ spells classnames in lower case and some object names are
// not.
//-----------------------------------------------------------------------------
TEST( PhysWorldMatchRule, AnOrdinarySubstringStillMatchesEitherCase )
{
	EXPECT_TRUE( PhysWorldObjectMatches( "prop_physics", "prop" ) );
	EXPECT_TRUE( PhysWorldObjectMatches( "PROP_PHYSICS", "prop" ) );
	EXPECT_FALSE( PhysWorldObjectMatches( "prop_physics", "ragdoll" ) );
}

} // namespace
