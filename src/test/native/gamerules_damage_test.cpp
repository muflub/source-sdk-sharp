//====== SourceSharp — the damage masks, ported out of CMultiplayRules =======//
//
// Five bitmasks of DMG_* flags that used to be CMultiplayRules's and are POLICY
// rather than mechanism: acid keeps hurting you, a fall applies no physics
// force, a crowbar can gib a corpse. Each is a sentence about how this mod's
// combat feels, and a mod that wanted burning to stop being time-based had to
// edit C++ to say so. DamageRules.cs says it now, and pushes the five to
// shell_gamerules.cpp once at startup.
//
// THE COMPARISON WITH STOCK has moved to the oracle project: DamageRulesTests'
// stock half and the NATIVE DEFAULTS (src/test/oracle/native/
// gamerules_damage_oracle_test.cpp) are both checked against upstream's
// multiplay_gamerules.cpp in the pristine Valve SDK. What is checked HERE is
// that the OVERRIDES EXIST AT ALL, and that each predicate asks its own getter.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <set>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
			return kCandidates[ i ];
	}
	return NULL;
}

std::string Read( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream f( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	EXPECT_TRUE( f.is_open() ) << "could not open " << pszRel;
	if ( !f.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( f ) ),
	                    std::istreambuf_iterator<char>() );
}

const char *kShell = "sourcesharp/game/shared/shell_gamerules.cpp";
const char *kShellH = "sourcesharp/game/shared/shell_gamerules.h";

} // namespace

//-----------------------------------------------------------------------------
// THREE SURVIVE, SEVEN ARE GONE, AND THE ORIGINAL REASON FOR THE CHECK IS GONE
// WITH THEM.
//
// This required all TEN, and its argument was that a missing one "falls through
// to CMultiplayRules -- which still compiles and still answers, so the port is
// half-done with nothing to say so." There is no base to fall through to any
// more: CSourceSharpGameRules implements IGameRules and nothing else, so a
// method that is not here does not exist and any caller is a compile error
// rather than a silent stock answer.
//
// Seven were then removed because nothing in this build reached them through
// the rules pointer -- their g_pGameRules-> callers are all in player.cpp,
// ai_basenpc.cpp and physics.cpp, none of which this build compiles. The three
// that remain are the three IGameRules declares, each with a live caller:
// Damage_GetTimeBased and Damage_GetNoPhysicsForce (EntityDissolve.cpp:358,
// physics.cpp:3079) and Damage_NoPhysicsForce (takedamageinfo.cpp:355).
//
// The check is re-taken both ways: the three must be present, and the seven
// must stay absent, so this cannot be satisfied by putting them back.
//-----------------------------------------------------------------------------
TEST( GameRulesDamage, TheThreeReachableOverridesArePresentAndTheSevenDeadOnesAreNot )
{
	const std::string h = Read( kShellH );
	const std::string cpp = Read( kShell );
	ASSERT_FALSE( h.empty() );
	ASSERT_FALSE( cpp.empty() );

	static const char *kReachable[] = {
		"Damage_GetTimeBased", "Damage_GetNoPhysicsForce", "Damage_NoPhysicsForce",
	};

	for ( size_t i = 0; i < sizeof( kReachable ) / sizeof( kReachable[ 0 ] ); i++ )
	{
		EXPECT_NE( h.find( kReachable[ i ] ), std::string::npos )
			<< kReachable[ i ] << " is not declared in shell_gamerules.h. It is "
			<< "an IGameRules member with a live caller, so removing it is a "
			<< "compile error somewhere -- but the interface could be trimmed "
			<< "to match, and then the answer would just be gone.";

		EXPECT_NE( cpp.find( std::string( "CSourceSharpGameRules::" ) + kReachable[ i ] ),
		           std::string::npos )
			<< kReachable[ i ] << " is declared but not defined in " << kShell << ".";
	}

	// ...and the seven with no caller stay gone. Without this the check above
	// is satisfied by a class that declares all ten again.
	//
	// COMMENTS REMOVED FIRST. The header explains at length what the damage
	// masks are and why, and that prose names Damage_IsTimeBased while
	// describing the stock behaviour being replaced. Searching the raw file
	// finds the explanation and reports it as the thing being explained -- which
	// is exactly what happened the first time this ran.
	std::string hCode;
	for ( size_t i = 0; i < h.size(); )
	{
		if ( h[i] == '/' && i + 1 < h.size() && h[i + 1] == '/' )
		{
			while ( i < h.size() && h[i] != '\n' ) i++;
		}
		else if ( h[i] == '/' && i + 1 < h.size() && h[i + 1] == '*' )
		{
			i += 2;
			while ( i + 1 < h.size() && !( h[i] == '*' && h[i + 1] == '/' ) ) i++;
			i += 2;
		}
		else
		{
			hCode += h[i++];
		}
	}
	static const char *kDead[] = {
		"Damage_GetShouldGibCorpse", "Damage_GetShouldNotBleed", "Damage_GetShowOnHud",
		"Damage_IsTimeBased", "Damage_ShouldGibCorpse", "Damage_ShouldNotBleed",
		"Damage_ShowOnHUD",
	};

	for ( size_t i = 0; i < sizeof( kDead ) / sizeof( kDead[ 0 ] ); i++ )
	{
		EXPECT_EQ( hCode.find( kDead[ i ] ), std::string::npos )
			<< kDead[ i ] << " is back on the class. Nothing in this build calls "
			<< "it through the rules pointer, so it is a method that answers a "
			<< "question nobody asks.";
	}
}

//-----------------------------------------------------------------------------
// Each predicate reads its OWN getter, which is the thing stock did not do.
//-----------------------------------------------------------------------------
TEST( GameRulesDamage, EachPredicateAsksItsOwnGetterRatherThanRepeatingTheList )
{
	const std::string cpp = Read( kShell );
	ASSERT_FALSE( cpp.empty() );

	// ONE PAIR LEFT, and it is still worth checking. The other four predicates
	// were removed with the getters they asked -- nothing called them through
	// the rules pointer. The improvement this test describes is unchanged for
	// the pair that remains: stock answered the question twice, in a getter and
	// again inline in the predicate, and those two lists were free to drift.
	struct Pair_t { const char *pszPredicate; const char *pszGetter; };
	static const Pair_t kPairs[] = {
		{ "Damage_NoPhysicsForce",  "Damage_GetNoPhysicsForce()" },
	};

	for ( size_t i = 0; i < sizeof( kPairs ) / sizeof( kPairs[ 0 ] ); i++ )
	{
		const size_t at = cpp.find( std::string( "CSourceSharpGameRules::" )
			+ kPairs[ i ].pszPredicate );
		ASSERT_NE( at, std::string::npos ) << kPairs[ i ].pszPredicate << " is missing";

		const size_t end = cpp.find( "\n}", at );
		const std::string body = cpp.substr( at, end - at );

		EXPECT_NE( body.find( kPairs[ i ].pszGetter ), std::string::npos )
			<< kPairs[ i ].pszPredicate << " does not call "
			<< kPairs[ i ].pszGetter << ". Stock answered each of these "
			<< "questions TWICE -- a getter returning the mask and a predicate "
			<< "re-spelling the same flags inline -- which is two lists free to "
			<< "drift with nothing to report it if they did. One list per "
			<< "question is the whole improvement here.";

		// ...and does not re-spell the flags itself.
		EXPECT_EQ( body.find( "DMG_" ), std::string::npos )
			<< kPairs[ i ].pszPredicate << " names DMG_* flags directly. That is "
			<< "stock's second copy coming back.";
	}
}
