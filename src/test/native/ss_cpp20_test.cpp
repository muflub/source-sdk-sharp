//========= SourceSharp — ss_cpp20.h keeps C++20's contract ===================//
//
// ss::erase / ss::erase_if (src/shared/native/ss_cpp20.h) are C++20's
// std::erase / std::erase_if for a C++17 tree. These facts are the C++20
// contract: every match goes, the survivors keep their order, and the answer is
// how many went. The same file is built twice -- ss_cpp20_test as C++17 (the
// shim's own code) and ss_cpp20_std_test as C++20 (where the names are std's) --
// so a fact that passes under one and fails under the other is the shim
// disagreeing with the standard.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_cpp20.h"

#include <deque>
#include <list>
#include <map>
#include <set>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

TEST( SsCpp20, TheShimIsTheStandardOneUnderCpp20AndOursUnderCpp17 )
{
#if defined( __cpp_lib_erase_if ) && __cpp_lib_erase_if >= 202002L
	// The header's own condition, so the test and the shim cannot disagree on
	// which half is live. The same function, not a lookalike: both names resolve to one address.
	using EraseIf = std::vector<int>::size_type ( * )( std::vector<int> &, bool ( * )( int ) );
	using Erase = std::vector<int>::size_type ( * )( std::vector<int> &, const int & );
	EraseIf ourIf = &ss::erase_if;
	EraseIf stdIf = &std::erase_if;
	Erase ours = &ss::erase;
	Erase std_ = &std::erase;
	EXPECT_EQ( stdIf, ourIf );
	EXPECT_EQ( std_, ours );
	EXPECT_GE( __cpp_lib_erase_if, 202002L );
#else
	EXPECT_LT( __cplusplus, 202002L ) << "a C++20 library without std::erase_if";
#endif
}

TEST( SsCpp20, EraseOnAVectorRemovesEveryMatchAndKeepsTheOrder )
{
	std::vector<int> v = { 2, 1, 2, 3, 2, 4 };
	EXPECT_EQ( 3u, ss::erase( v, 2 ) );
	EXPECT_EQ( ( std::vector<int>{ 1, 3, 4 } ), v );
}

TEST( SsCpp20, EraseOfAValueThatIsNotThereRemovesNothing )
{
	std::vector<int> v = { 1, 3 };
	EXPECT_EQ( 0u, ss::erase( v, 2 ) );
	EXPECT_EQ( ( std::vector<int>{ 1, 3 } ), v );
}

TEST( SsCpp20, EraseIfOnAVectorRemovesWhatThePredicateAccepts )
{
	std::vector<int> v = { 1, 2, 3, 4, 5, 6 };
	EXPECT_EQ( 3u, ss::erase_if( v, []( int x ) { return x % 2 == 0; } ) );
	EXPECT_EQ( ( std::vector<int>{ 1, 3, 5 } ), v );
}

TEST( SsCpp20, EraseOnAVectorOfPointersRemovesThatPointer )
{
	// The shape the entity code has: a list of object pointers, one to drop.
	int a = 0, b = 0, c = 0;
	std::vector<int *> v = { &a, &b, &c, &b };
	EXPECT_EQ( 2u, ss::erase( v, &b ) );
	EXPECT_EQ( ( std::vector<int *>{ &a, &c } ), v );
}

TEST( SsCpp20, EraseAndEraseIfOnADeque )
{
	std::deque<int> d = { 5, 1, 5, 2 };
	EXPECT_EQ( 2u, ss::erase( d, 5 ) );
	EXPECT_EQ( ( std::deque<int>{ 1, 2 } ), d );
	EXPECT_EQ( 1u, ss::erase_if( d, []( int x ) { return x == 1; } ) );
	EXPECT_EQ( ( std::deque<int>{ 2 } ), d );
}

TEST( SsCpp20, EraseAndEraseIfOnAString )
{
	std::string s = "a/b/c/";
	EXPECT_EQ( 3u, ss::erase( s, '/' ) );
	EXPECT_EQ( "abc", s );
	EXPECT_EQ( 1u, ss::erase_if( s, []( char ch ) { return ch == 'b'; } ) );
	EXPECT_EQ( "ac", s );
}

TEST( SsCpp20, EraseAndEraseIfOnAListCountTheRemoved )
{
	std::list<int> l = { 7, 8, 7, 9 };
	EXPECT_EQ( 2u, ss::erase( l, 7 ) );
	EXPECT_EQ( ( std::list<int>{ 8, 9 } ), l );
	EXPECT_EQ( 1u, ss::erase_if( l, []( int x ) { return x > 8; } ) );
	EXPECT_EQ( ( std::list<int>{ 8 } ), l );
}

TEST( SsCpp20, EraseIfOnAMapTakesKeyValuePairs )
{
	std::map<std::string, int> m = { { "a", 1 }, { "b", 2 }, { "c", 3 } };
	EXPECT_EQ( 2u, ss::erase_if( m, []( const std::pair<const std::string, int> &kv ) { return kv.second != 2; } ) );
	ASSERT_EQ( 1u, m.size() );
	EXPECT_EQ( 2, m.at( "b" ) );
}

TEST( SsCpp20, EraseIfOnAMultimapRemovesEveryMatchingNode )
{
	std::multimap<int, int> m = { { 1, 1 }, { 1, 2 }, { 2, 3 } };
	EXPECT_EQ( 2u, ss::erase_if( m, []( const std::pair<const int, int> &kv ) { return kv.first == 1; } ) );
	EXPECT_EQ( 1u, m.size() );
}

TEST( SsCpp20, EraseIfOnSets )
{
	std::set<int> s = { 1, 2, 3, 4 };
	EXPECT_EQ( 2u, ss::erase_if( s, []( int x ) { return x <= 2; } ) );
	EXPECT_EQ( ( std::set<int>{ 3, 4 } ), s );

	std::multiset<int> ms = { 1, 1, 2 };
	EXPECT_EQ( 2u, ss::erase_if( ms, []( int x ) { return x == 1; } ) );
	EXPECT_EQ( 1u, ms.size() );
}

TEST( SsCpp20, EraseIfOnUnorderedContainers )
{
	std::unordered_map<int, int> um = { { 1, 10 }, { 2, 20 }, { 3, 30 } };
	EXPECT_EQ( 1u, ss::erase_if( um, []( const std::pair<const int, int> &kv ) { return kv.second == 20; } ) );
	EXPECT_EQ( 0u, um.count( 2 ) );
	EXPECT_EQ( 2u, um.size() );

	std::unordered_set<int> us = { 1, 2, 3 };
	EXPECT_EQ( 3u, ss::erase_if( us, []( int ) { return true; } ) );
	EXPECT_TRUE( us.empty() );
}
