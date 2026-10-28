//========= SourceSharp — ss_containers.h's own facts ========================//
//
// The parts of src/shared/native/ss_containers.h that have no tier1 original to
// be held to (ss_containers_oracle_test holds the rest): the string pool the
// entity classes keep their names in, and the case-blind hash/equality pair the
// hashed name indexes use.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_containers.h"

#include <string>
#include <unordered_map>

namespace
{

TEST( StringPool, EqualStringsShareOnePointer )
{
	ss::StringPool pool;
	char szName[] = "OnTrigger";
	const char *pA = pool.Intern( szName );
	szName[0] = 'X'; // the caller's buffer is not what was kept
	const char *pB = pool.Intern( "OnTrigger" );

	EXPECT_EQ( pA, pB );
	EXPECT_STREQ( "OnTrigger", pA );
	EXPECT_EQ( 1, pool.Count() );
}

TEST( StringPool, IsCaseSensitive )
{
	ss::StringPool pool;
	EXPECT_NE( pool.Intern( "OnTrigger" ), pool.Intern( "ontrigger" ) );
	EXPECT_EQ( 2, pool.Count() );
}

TEST( StringPool, APointerSurvivesManyLaterInterns )
{
	ss::StringPool pool;
	const char *pFirst = pool.Intern( "prop_physics" );

	// Enough to rehash the set many times over.
	for ( int i = 0; i < 20000; i++ )
		pool.Intern( ( "name_" + std::to_string( i ) ).c_str() );

	EXPECT_STREQ( "prop_physics", pFirst );
	EXPECT_EQ( pFirst, pool.Intern( "prop_physics" ) );
}

TEST( CaselessHash, NamesEqualInAnyCaseHashAndCompareEqual )
{
	ss::CaselessHash hash;
	ss::CaselessEqual equal;
	const std::string a = "Weapon_Crowbar_2", b = "wEAPON_cROWBAR_2";

	EXPECT_TRUE( equal( a, b ) );
	EXPECT_EQ( hash( a ), hash( b ) );
	EXPECT_FALSE( equal( a, "weapon_crowbar_3" ) );
}

TEST( CaselessHash, OnlyAsciiLettersFold )
{
	ss::CaselessEqual equal;
	ss::CaselessHash hash;

	// '[' is 'Z' + 1 and '{' is 'z' + 1: neither is a letter.
	EXPECT_FALSE( equal( "a[", "a{" ) );
	EXPECT_NE( hash( "a[" ), hash( "a{" ) );
	// Latin-1 e-acute and E-acute: not ASCII, so not folded.
	EXPECT_FALSE( equal( "\xe9", "\xc9" ) );
	EXPECT_NE( hash( "\xe9" ), hash( "\xc9" ) );
}

TEST( CaselessHash, AnUnorderedMapUnderItFindsAnyCaseAndKeepsTheFirst )
{
	std::unordered_map<std::string, int, ss::CaselessHash, ss::CaselessEqual> map;

	EXPECT_TRUE( map.emplace( "Info_Player_Start", 1 ).second );
	EXPECT_FALSE( map.emplace( "info_player_start", 2 ).second ) << "another case is the same key";

	ASSERT_EQ( 1u, map.size() );
	auto it = map.find( "INFO_PLAYER_START" );
	ASSERT_TRUE( it != map.end() );
	EXPECT_EQ( 1, it->second );
	EXPECT_EQ( "Info_Player_Start", it->first ) << "the first spelling is the key kept";
}

} // namespace
