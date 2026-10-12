//========= SourceSharp — finding a player by the name somebody typed ========//
//
// UTIL_PlayerByName and its family walk the player slots asking
// UTIL_PlayerByIndex for a CBasePlayer, and this build has none, so they answer
// "nobody" whatever you type and their loop bodies have never run. The rule
// those bodies contain is here, over slots, and is checked without a server.
//
// The interesting half is the one stock got inconsistent: an AMBIGUOUS partial
// name and NOBODY are the same NULL there, so "no such player" and "say which
// one you mean" arrive as the same silence.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <fstream>
#include <sstream>
#include <string>

#include "../game/shared/player_name_match.h"

using namespace PlayerNameMatch;

//-----------------------------------------------------------------------------
// The comparisons.
//-----------------------------------------------------------------------------
TEST( ExactMatch, WholeNameCaseInsensitive )
{
	EXPECT_TRUE ( ExactMatch( "Gordon", "Gordon" ) );
	EXPECT_TRUE ( ExactMatch( "Gordon", "gordon" ) );
	EXPECT_TRUE ( ExactMatch( "GORDON", "gordon" ) );
	EXPECT_FALSE( ExactMatch( "Gordon", "Gord" ) );
	EXPECT_FALSE( ExactMatch( "Gord",   "Gordon" ) );
	EXPECT_FALSE( ExactMatch( "Gordon", "" ) );
	EXPECT_TRUE ( ExactMatch( "", "" ) );
}

TEST( ExactMatch, ANullNameIsNobody )
{
	// A slot with no name in it is a real state -- a client that has connected
	// and not yet sent its userinfo -- and it must not match, not even an empty
	// argument.
	EXPECT_FALSE( ExactMatch( 0, "Gordon" ) );
	EXPECT_FALSE( ExactMatch( "Gordon", 0 ) );
}

TEST( PartialMatch, AnywhereInTheNameCaseInsensitive )
{
	EXPECT_TRUE ( PartialMatch( "Gordon Freeman", "gordon" ) );
	EXPECT_TRUE ( PartialMatch( "Gordon Freeman", "FREE" ) );
	EXPECT_TRUE ( PartialMatch( "Gordon Freeman", "n F" ) );
	EXPECT_TRUE ( PartialMatch( "Gordon Freeman", "Gordon Freeman" ) );
	EXPECT_FALSE( PartialMatch( "Gordon Freeman", "Alyx" ) );
	EXPECT_FALSE( PartialMatch( "Gordon", "Gordon Freeman" ) );
}

TEST( PartialMatch, TheNeedleMayRestartInsideAFalseStart )
{
	// "aab" contains "ab" only if the walk resumes at the second 'a' rather
	// than skipping past the failed attempt. The naive single-pass version of
	// this function gets it wrong and no player name in the sandbox would ever
	// have shown it.
	EXPECT_TRUE( PartialMatch( "aab", "ab" ) );
	EXPECT_TRUE( PartialMatch( "Freefreeman", "freeman" ) );
}

TEST( PartialMatch, AnEmptyNeedleMatchesEverything )
{
	// strstr's behaviour, and the reason every caller rejects an empty argument
	// before reaching here: "match anybody" answers AMBIGUOUS on a populated
	// server and NOBODY on an empty one, and neither is what typing nothing
	// meant.
	EXPECT_TRUE( PartialMatch( "Gordon", "" ) );
}

//-----------------------------------------------------------------------------
// The search accumulator -- ambiguity told apart from absence.
//-----------------------------------------------------------------------------
TEST( Search, NothingOfferedIsNobody )
{
	Search_t search;

	EXPECT_EQ( NOBODY, search.Result() );
	EXPECT_FALSE( IsPlayer( search.Result() ) );
}

TEST( Search, OneMatchIsThatSlot )
{
	Search_t search;
	search.Offer( 3 );

	EXPECT_EQ( 3, search.Result() );
	EXPECT_TRUE( IsPlayer( search.Result() ) );
}

TEST( Search, TwoDIFFERENTMatchesAreAmbiguousAndNotNobody )
{
	// The whole point of this type. Stock answers NULL here and NULL for no
	// match at all, so a console command cannot tell "nobody is called that"
	// from "two people are, say which".
	Search_t search;
	search.Offer( 3 );
	search.Offer( 5 );

	EXPECT_EQ( AMBIGUOUS, search.Result() );
	EXPECT_FALSE( IsPlayer( search.Result() ) );
	EXPECT_NE( NOBODY, search.Result() );
}

TEST( Search, TheSAMESlotTwiceIsOnePlayer )
{
	// Reached when two readings of one argument find the same person -- "4"
	// read as a userid and as a name both landing on slot 2. Stock's own
	// ambiguity check exempts this case explicitly and it is easy to lose.
	Search_t search;
	search.Offer( 2 );
	search.Offer( 2 );

	EXPECT_EQ( 2, search.Result() );
}

TEST( Search, AmbiguityIsNotResolvedByALaterMatch )
{
	Search_t search;
	search.Offer( 3 );
	search.Offer( 5 );
	search.Offer( 3 );

	EXPECT_EQ( AMBIGUOUS, search.Result() );
}

TEST( Search, ASlotOfZeroOrLessIsNotAMatch )
{
	// NOBODY offered as though it were a slot must not count as one, or a
	// caller that forwards its own failed lookup turns "nothing matched" into
	// a match and then into an ambiguity.
	Search_t search;
	search.Offer( NOBODY );
	search.Offer( AMBIGUOUS );

	EXPECT_EQ( NOBODY, search.Result() );

	search.Offer( 4 );
	search.Offer( NOBODY );

	EXPECT_EQ( 4, search.Result() );
}

//-----------------------------------------------------------------------------
// Reading the argument.
//-----------------------------------------------------------------------------
TEST( ReadArg, NothingIsEmpty )
{
	EXPECT_TRUE( ReadArg( 0 ).bEmpty );
	EXPECT_TRUE( ReadArg( "" ).bEmpty );
	EXPECT_FALSE( ReadArg( "a" ).bEmpty );
}

TEST( ReadArg, ABareNumberIsBothASteamId64AndAUserid )
{
	// Stock's split: an all-digit argument is tried as a SteamID64 FIRST and as
	// a bare userid LAST, with the name search in between. Both readings come
	// off the same flag.
	const ArgReading_t r = ReadArg( "76561197960287930" );

	EXPECT_TRUE ( r.bAllDigits );
	EXPECT_FALSE( r.bPoundUserId );
	EXPECT_FALSE( r.bBracketed );
	EXPECT_FALSE( r.bLegacySteamId );
}

TEST( ReadArg, APoundedNumberIsAUseridAndCarriesIt )
{
	const ArgReading_t r = ReadArg( "#42" );

	EXPECT_TRUE ( r.bPoundUserId );
	EXPECT_FALSE( r.bAllDigits );
	EXPECT_EQ   ( 42, r.nUserId );
}

TEST( ReadArg, ABareNumberCarriesItsUseridToo )
{
	EXPECT_EQ( 7, ReadArg( "7" ).nUserId );
	EXPECT_EQ( 1234, ReadArg( "1234" ).nUserId );
}

TEST( ReadArg, ABareHashIsNotAUserid )
{
	// "#" alone: stock requires length > 1, so this falls through to the name
	// search rather than being read as userid 0 -- and userid 0 is the engine's
	// word for the world, which no player has.
	const ArgReading_t r = ReadArg( "#" );

	EXPECT_FALSE( r.bPoundUserId );
	EXPECT_EQ   ( 0, r.nUserId );
}

TEST( ReadArg, ANameIsNoneOfThem )
{
	const ArgReading_t r = ReadArg( "Gordon" );

	EXPECT_FALSE( r.bEmpty );
	EXPECT_FALSE( r.bAllDigits );
	EXPECT_FALSE( r.bPoundUserId );
	EXPECT_FALSE( r.bBracketed );
	EXPECT_FALSE( r.bLegacySteamId );
	EXPECT_EQ   ( 0, r.nUserId );
}

TEST( ReadArg, ANameThatStartsWithADigitIsNotANumber )
{
	// "4chan" -- stock's bAllNumbers requires the FIRST character to be a digit
	// as well as the rest, and a name beginning with one is the case that tells
	// the two apart.
	EXPECT_FALSE( ReadArg( "4chan" ).bAllDigits );
	EXPECT_FALSE( ReadArg( "g42" ).bAllDigits );
}

TEST( ReadArg, TheSteamIdSpellings )
{
	EXPECT_TRUE ( ReadArg( "[U:1:1234]" ).bBracketed );
	EXPECT_FALSE( ReadArg( "[U:1:1234" ).bBracketed );
	EXPECT_TRUE ( ReadArg( "STEAM_0:1:1234" ).bLegacySteamId );
	EXPECT_FALSE( ReadArg( "steam_0:1:1234" ).bLegacySteamId );
	EXPECT_FALSE( ReadArg( "STEAM" ).bLegacySteamId );
}

//=============================================================================//
// WHERE THE WALK LIVES
//
// plan_entity.md step 4e (Utilities). The rule above has no engine in it, which
// is why it can be tested at all; the WALK it needs -- which slots are
// occupied, and what name, userid and flags each holds -- is the other half,
// and step 4e moved that half out of game/server/util.cpp into
// sourcesharp/game/server/shell_playerslots.cpp.
//
// The move is asserted here rather than nowhere, because nothing else in the
// tree named these five by name and a family with no reader can be relocated,
// duplicated or dropped without a single test going red. (It also pinned that
// no copy was left behind in game/server/util.cpp; server.so no longer
// compiles util.cpp and it has left the source tree, so that half was culled.)
//
//   * THE RULE FOLLOWING THE WALK. If the comparisons above ever migrate into
//     the shim file beside the walk, the split that makes this suite possible
//     is gone and the rule stops being testable without an engine. So the shim
//     file is asserted NOT to contain them.
//=============================================================================//

namespace
{

std::string SlurpForWalk( const char *pszPath )
{
	std::ifstream f( pszPath );
	EXPECT_TRUE( f.good() ) << "cannot open " << pszPath;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

size_t CountForWalk( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos;
	      i = hay.find( needle, i + needle.size() ) )
	{
		n++;
	}
	return n;
}

const char *const kWalkFile = "sourcesharp/game/server/shell_playerslots.cpp";

// The five, as each appears at the start of its definition.
const char *const kWalkDefinitions[] = {
	"const char *UTIL_PlayerNameBySlot( int nSlot )",
	"int UTIL_PlayerUserIdBySlot( int nSlot )",
	"int UTIL_PlayerSlotByName( const char *pName )",
	"int UTIL_PlayerSlotByPartialName( const char *pName )",
	"int UTIL_PlayerSlotByCommandArg( const char *pArg )",
	"void UTIL_GetPlayerConnectionInfo( int playerIndex, int& ping, int &packetloss )",
};
const size_t kNumWalk = sizeof( kWalkDefinitions ) / sizeof( kWalkDefinitions[ 0 ] );

} // anonymous namespace

TEST( PlayerSlotWalk, EveryMemberIsDefinedInTheShimFile )
{
	const std::string walk = SlurpForWalk( kWalkFile );
	ASSERT_FALSE( walk.empty() ) << kWalkFile << " did not load";

	for ( size_t i = 0; i < kNumWalk; i++ )
	{
		EXPECT_EQ( 1u, CountForWalk( walk, kWalkDefinitions[ i ] ) )
			<< kWalkDefinitions[ i ] << " is not defined exactly once in "
			<< kWalkFile;
	}
}

// UtilHeaderStillDeclaresThemSoNoCallerChanged WAS HERE and read util.h for
// the walk's declarations. game/server/util.h is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6, so the fact has lost its
// subject.

TEST( PlayerSlotWalk, TheWalkDidNotSwallowTheRule )
{
	const std::string walk = SlurpForWalk( kWalkFile );
	ASSERT_FALSE( walk.empty() );

	// It USES the rule -- that is the point of the split -- and must not
	// reimplement it. PlayerNameMatch:: is the only spelling allowed here.
	EXPECT_LT( 0u, CountForWalk( walk, "PlayerNameMatch::" ) )
		<< "the walk stopped asking the rule";

	EXPECT_EQ( 0u, CountForWalk( walk, "struct ArgReading_t" ) )
		<< "the argument reader has been copied into the walk, so the engine-"
		   "free half this suite tests is no longer the half the game runs";
	EXPECT_EQ( 0u, CountForWalk( walk, "ArgReading_t ReadArg(" ) )
		<< "the same, for the reader's definition.";
}
