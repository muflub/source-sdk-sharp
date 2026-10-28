//========= SourceSharp — one name-matching rule, pinned =====================//
//
// entity_name_match.h is the single copy of a rule that used to exist twice:
// stock's NamesMatch in baseentity.cpp, and GameEntityNameMatches in
// entitylist.cpp, which carried a comment saying it was a deliberate duplicate
// "kept identical on purpose: two name-matching rules that differ in a corner
// is a map that works for one kind of entity and not the other". Nothing in the
// build would have noticed the day they drifted. These are the corners.
//
// The rule is also load-bearing for a retype. FClassnameIs now asks
// EntityNames_Match against IServerNetworkable::GetClassName instead of
// CBaseEntity::ClassMatches, which frees every translation unit that asks an
// entity what it is from a reference into baseentity.o. That substitution is
// sound only if the string form answers exactly what the string_t form did,
// NULL_STRING included -- so the NULL/empty cases below are not edge-case
// pedantry, they ARE the identity the retype rests on.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/shared/entity_name_match.h" // src/shared/native/, the farm's sourcesharp/game/shared/

//-----------------------------------------------------------------------------
// The ordinary case, and the thing the rule is for.
//-----------------------------------------------------------------------------
TEST( EntityNameMatch, ExactNamesMatchAndDifferentOnesDoNot )
{
	EXPECT_TRUE( EntityNames_Match( "gate_north", "gate_north" ) );
	EXPECT_FALSE( EntityNames_Match( "gate_north", "gate_south" ) );
	EXPECT_FALSE( EntityNames_Match( "gate", "gate_north" ) );
	EXPECT_FALSE( EntityNames_Match( "gate_north", "gate" ) );
}

//-----------------------------------------------------------------------------
// Case-insensitive, both directions of conversion, which is what stock's
// three-armed comparison does. A rule that only lowered the query would pass
// the first of these and fail the second.
//-----------------------------------------------------------------------------
TEST( EntityNameMatch, ComparisonIsCaseInsensitiveBothWays )
{
	EXPECT_TRUE( EntityNames_Match( "GATE_NORTH", "gate_north" ) );
	EXPECT_TRUE( EntityNames_Match( "gate_north", "GATE_NORTH" ) );
	EXPECT_TRUE( EntityNames_Match( "GaTe_NoRtH", "gAtE_nOrTh" ) );

	// Non-alphabetic characters are not touched by the case arms.
	EXPECT_FALSE( EntityNames_Match( "gate-north", "gate_north" ) );
}

//-----------------------------------------------------------------------------
// A TRAILING '*' is a prefix wildcard, and it is the only wildcard there is.
// "gate*" matches "gate" itself as well as everything under it -- the walk
// consumes the shared prefix and then finds the '*' with nothing left to match.
//-----------------------------------------------------------------------------
TEST( EntityNameMatch, TrailingStarIsAPrefixWildcard )
{
	EXPECT_TRUE( EntityNames_Match( "gate*", "gate_north" ) );
	EXPECT_TRUE( EntityNames_Match( "gate*", "gate_south" ) );
	EXPECT_TRUE( EntityNames_Match( "gate*", "gate" ) );
	EXPECT_TRUE( EntityNames_Match( "*", "anything_at_all" ) );

	EXPECT_FALSE( EntityNames_Match( "gate*", "door_north" ) );
}

//-----------------------------------------------------------------------------
// A '*' IN THE MIDDLE ALSO WORKS, and stock's own comment says it does not.
//
// The comment beside this line in baseentity.cpp reads "Right now, only thing
// supported is trailing *", but the test is on wherever the query reached when
// the walk stopped -- so "ga*e_north" matches "gate_north" and "*_north"
// matches anything at all. The first draft of this test asserted the comment
// and went red against the code.
//
// Pinned as it really behaves, not as documented, because this function exists
// to answer what CBaseEntity::ClassMatches answered so FClassnameIs can stop
// asking the class. A tidier rule here would be a behaviour change for every
// map that ever used such a name.
//-----------------------------------------------------------------------------
TEST( EntityNameMatch, AStarAnywhereTheWalkStopsIsAWildcard )
{
	EXPECT_TRUE( EntityNames_Match( "ga*e_north", "gate_north" ) );
	EXPECT_TRUE( EntityNames_Match( "*_north", "gate_north" ) );

	// The star still has to be reached: a query that DIFFERS before it does not
	// match, so this is not "any query containing a star matches anything".
	EXPECT_FALSE( EntityNames_Match( "xa*e_north", "gate_north" ) );
}

//=============================================================================//
// THE IDENTITY THE RETYPE RESTS ON.
//
// Stock's NamesMatch took a string_t and answered NULL_STRING with its own arm:
//
//     if ( nameToMatch == NULL_STRING )
//         return ( !pszQuery || *pszQuery == 0 || *pszQuery == '*' );
//
// The retype passes STRING(m_iClassname), and STRING(NULL_STRING) is "". So the
// answers for "" must be that same three-case arm, or FClassnameIs quietly
// changed behaviour for every unnamed entity in the game.
//=============================================================================//

TEST( EntityNameMatch, EmptyNameAnswersTheOldNullStringArm )
{
	// The three that matched NULL_STRING.
	EXPECT_TRUE( EntityNames_Match( NULL, "" ) );
	EXPECT_TRUE( EntityNames_Match( "", "" ) );
	EXPECT_TRUE( EntityNames_Match( "*", "" ) );

	// ..and anything else did not.
	EXPECT_FALSE( EntityNames_Match( "gate_north", "" ) );
	EXPECT_FALSE( EntityNames_Match( "g*", "" ) );
}

//-----------------------------------------------------------------------------
// NULL is accepted as a second spelling of "unnamed", and answers identically
// to "". A caller holding a string_t has "" ; one holding an optional pointer
// has NULL; both mean the same thing and must get the same answer.
//-----------------------------------------------------------------------------
TEST( EntityNameMatch, NullNameAnswersTheSameAsEmpty )
{
	EXPECT_EQ( EntityNames_Match( NULL, "" ), EntityNames_Match( NULL, NULL ) );
	EXPECT_EQ( EntityNames_Match( "", "" ), EntityNames_Match( "", NULL ) );
	EXPECT_EQ( EntityNames_Match( "*", "" ), EntityNames_Match( "*", NULL ) );
	EXPECT_EQ( EntityNames_Match( "gate_north", "" ), EntityNames_Match( "gate_north", NULL ) );
}

//-----------------------------------------------------------------------------
// A NULL QUERY against a real name is false, not a crash and not a match. This
// is the arm that stops the NULL-name early-out from being read as "NULL
// matches everything".
//-----------------------------------------------------------------------------
TEST( EntityNameMatch, NullQueryAgainstARealNameIsFalse )
{
	EXPECT_FALSE( EntityNames_Match( NULL, "gate_north" ) );
}

//-----------------------------------------------------------------------------
// The pooled-pointer fast path. Names come from a string pool, so an exact
// match is usually the same pointer, and stock short-circuited on that with
// IDENT_STRINGS. It must agree with the walk rather than merely be faster --
// and it must not make an empty string match a non-empty query by accident.
//-----------------------------------------------------------------------------
TEST( EntityNameMatch, IdenticalPointersAgreeWithTheWalk )
{
	const char *pszPooled = "gate_north";
	EXPECT_TRUE( EntityNames_Match( pszPooled, pszPooled ) );

	// The same text at a different address gets the same answer the long way.
	char szCopy[] = "gate_north";
	EXPECT_TRUE( EntityNames_Match( szCopy, pszPooled ) );
	EXPECT_NE( (const void *)szCopy, (const void *)pszPooled );
}

//-----------------------------------------------------------------------------
// An empty QUERY does not match a named entity. Worth its own case because the
// unnamed arm answers TRUE for an empty query, and it would be easy to write a
// rule where "" matched everything in both directions.
//-----------------------------------------------------------------------------
TEST( EntityNameMatch, EmptyQueryDoesNotMatchANamedEntity )
{
	EXPECT_FALSE( EntityNames_Match( "", "gate_north" ) );
}

//-----------------------------------------------------------------------------
// Symmetry is NOT a property of this rule, and that is deliberate: the query is
// the side that may carry a wildcard. Pinned so nobody "tidies" it into a
// symmetric compare.
//-----------------------------------------------------------------------------
TEST( EntityNameMatch, TheQuerySideIsTheOnlyWildcardSide )
{
	EXPECT_TRUE( EntityNames_Match( "gate*", "gate_north" ) );
	EXPECT_FALSE( EntityNames_Match( "gate_north", "gate*" ) );
}
