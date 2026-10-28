//========= SourceSharp — ss_containers.h against the pristine tier1 =========//
//
// src/shared/native/ss_containers.h restates tier1's case-blind name compare
// on the standard library so server.so's own containers stop instantiating
// CUtlMemory (and with it tier0's g_pMemAlloc). This runs it beside the
// pristine code it replaces, from the upstream clone's public/tier1 (first on
// the include path), over the same inputs:
//
//   ss::CaselessLess  vs CUtlDict (k_eDictCompareTypeCaseInsensitive) and
//                     utlrbtree.h's CaselessStringLessThan: the same order,
//                     the same equivalence, the same spelling kept.
//   ss::CaselessEqual vs V_stricmp == 0, with CaselessHash equal for every
//                     equal pair (the unordered indexes' requirement).
//
//=============================================================================//

#include "tier1/utldict.h"

#include "gtest/gtest.h"

#include "ss_containers.h"

#include <map>
#include <string>
#include <vector>

namespace
{

// Keys chosen to separate a case-folding compare from its neighbours: case
// variants, '_' and '[' / '`' (which sort differently folded up and folded
// down), digits, prefixes, the empty string and high-bit bytes.
const char *const kKeys[] = {
	"info_player_start",
	"Info_Player_Start",
	"info_node",
	"INFO_NODE_HINT",
	"prop_physics",
	"prop_physics_multiplayer",
	"Prop_Physics_Override",
	"a",
	"A",
	"ab",
	"aB",
	"a_b",
	"A[b",
	"a`b",
	"a{b",
	"a0",
	"a9",
	"Z",
	"z_",
	"_underscore",
	"[bracket",
	"`tick",
	"",
	"func_door",
	"func_door_rotating",
	"\xe9t\xe9",
	"\xc9T\xc9",
	"weapon_357",
	"weapon_crowbar",
	"WEAPON_AR2",
};
const int kNumKeys = (int)( sizeof( kKeys ) / sizeof( kKeys[0] ) );

TEST( CaselessLess, AgreesWithCaselessStringLessThanOnEveryPair )
{
	ss::CaselessLess less;
	int nTrue = 0;
	for ( int i = 0; i < kNumKeys; i++ )
	{
		for ( int j = 0; j < kNumKeys; j++ )
		{
			const bool stock = CaselessStringLessThan( kKeys[i], kKeys[j] );
			EXPECT_EQ( stock, less( kKeys[i], kKeys[j] ) ) << "'" << kKeys[i] << "' < '" << kKeys[j] << "'";
			EXPECT_EQ( stock, less( std::string( kKeys[i] ), std::string( kKeys[j] ) ) );
			EXPECT_EQ( stock, less( std::string( kKeys[i] ), kKeys[j] ) );
			EXPECT_EQ( stock, less( kKeys[i], std::string( kKeys[j] ) ) );
			nTrue += stock;
		}
	}
	// The stimulus reached both arms: some pairs order each way.
	EXPECT_GT( nTrue, kNumKeys );
	EXPECT_LT( nTrue, kNumKeys * kNumKeys - kNumKeys );
}

// The hashed indexes' pair: equal exactly when V_stricmp says 0, and equal
// names hash equal (an unordered_map needs both, or a name in another case
// lands in another bucket and is never found).
TEST( CaselessEqual, AgreesWithStricmpAndEqualNamesHashEqual )
{
	ss::CaselessEqual equal;
	ss::CaselessHash hash;
	int nEqualPairs = 0;
	for ( int i = 0; i < kNumKeys; i++ )
	{
		for ( int j = 0; j < kNumKeys; j++ )
		{
			const bool stock = V_stricmp( kKeys[i], kKeys[j] ) == 0;
			EXPECT_EQ( stock, equal( kKeys[i], kKeys[j] ) ) << "'" << kKeys[i] << "' == '" << kKeys[j] << "'";
			if ( stock )
			{
				EXPECT_EQ( hash( kKeys[i] ), hash( kKeys[j] ) ) << "'" << kKeys[i] << "' and '" << kKeys[j] << "'";
				nEqualPairs += ( i != j );
			}
		}
	}
	// The stimulus reached the case rule: some distinct spellings are equal.
	EXPECT_GT( nEqualPairs, 0 );
}

// A std::map under CaselessLess, filled find-then-insert as our dictionaries
// are, against a CUtlDict filled the same way: same keys kept (first spelling
// wins), same in-order walk, same answer to every probe.
TEST( CaselessLess, AMapUnderItKeepsWhatCUtlDictKeepsInItsOrder )
{
	CUtlDict<int, int> dict( k_eDictCompareTypeCaseInsensitive );
	std::map<std::string, int, ss::CaselessLess> map;

	for ( int i = 0; i < kNumKeys; i++ )
	{
		if ( dict.Find( kKeys[i] ) == dict.InvalidIndex() )
			dict.Insert( kKeys[i], i );
		map.emplace( kKeys[i], i );
	}

	ASSERT_EQ( (size_t)dict.Count(), map.size() );
	ASSERT_LT( map.size(), (size_t)kNumKeys ) << "no key folded onto another: the case rule was not exercised";

	std::vector<std::string> stockOrder;
	for ( int i = dict.First(); i != dict.InvalidIndex(); i = dict.Next( i ) )
		stockOrder.push_back( dict.GetElementName( i ) );
	std::vector<std::string> ourOrder;
	for ( const auto &kv : map )
		ourOrder.push_back( kv.first );
	EXPECT_EQ( stockOrder, ourOrder );

	for ( int i = 0; i < kNumKeys; i++ )
	{
		// Every key, and the same key with its case flipped.
		std::string probes[2] = { kKeys[i], kKeys[i] };
		for ( char &c : probes[1] )
			c = ( c >= 'a' && c <= 'z' ) ? c - 32 : ( c >= 'A' && c <= 'Z' ) ? c + 32 : c;
		for ( const std::string &p : probes )
		{
			const int d = dict.Find( p.c_str() );
			auto it = map.find( p.c_str() );
			ASSERT_EQ( d != dict.InvalidIndex(), it != map.end() ) << p;
			if ( it != map.end() )
			{
				EXPECT_STREQ( dict.GetElementName( d ), it->first.c_str() ) << p;
				EXPECT_EQ( dict[d], it->second ) << p;
			}
		}
	}
	EXPECT_EQ( dict.Find( "no_such_class" ), dict.InvalidIndex() );
	EXPECT_TRUE( map.find( "no_such_class" ) == map.end() );
}

} // namespace
