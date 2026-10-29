// DT_ArrayElementNameForIdx (dt_utlvector_common.cpp): the name each element of
// a network array is given. The engine matches a send table to its receive
// table by these names, so they are the wire contract: "000" to "999", then
// "1000" to "2047", exactly as the 2048-entry table of literals the function
// used to index spelled them. Each name is now written into a zeroed table the
// first time it is asked for, rather than held as 16 KB of pointers.

#include "gtest/gtest.h"
#include "mathlib_test_env.h" // a mathlib binary initialises mathlib (mathlib_init_guard_test)

#include <cstdio>
#include <cstring>
#include <set>
#include <string>

const char *DT_ArrayElementNameForIdx( size_t i );

namespace
{
const size_t kElements = 2048; // MAX_ARRAY_ELEMENTS
}

TEST( DTElementNames, EveryIndexIsNamedAsTheTableOfLiteralsNamedIt )
{
	for ( size_t i = 0; i < kElements; i++ )
	{
		char expected[8];
		snprintf( expected, sizeof( expected ), i < 1000 ? "%03zu" : "%zu", i );
		EXPECT_STREQ( expected, DT_ArrayElementNameForIdx( i ) ) << "index " << i;
	}
}

TEST( DTElementNames, TheEdgesOfEachWidth )
{
	EXPECT_STREQ( "000", DT_ArrayElementNameForIdx( 0 ) );
	EXPECT_STREQ( "009", DT_ArrayElementNameForIdx( 9 ) );
	EXPECT_STREQ( "999", DT_ArrayElementNameForIdx( 999 ) );
	EXPECT_STREQ( "1000", DT_ArrayElementNameForIdx( 1000 ) );
	EXPECT_STREQ( "2047", DT_ArrayElementNameForIdx( 2047 ) );
}

TEST( DTElementNames, EachNameIsItsOwnTerminatedStorage )
{
	// A prop keeps the pointer it was handed for the life of the table, so each
	// name must be stable, distinct and terminated within its own slot.
	std::set<std::string> seen;
	for ( size_t i = 0; i < kElements; i++ )
	{
		const char *psz = DT_ArrayElementNameForIdx( i );
		EXPECT_EQ( psz, DT_ArrayElementNameForIdx( i ) ) << "index " << i;
		EXPECT_LE( strlen( psz ), 4u ) << "index " << i;
		seen.insert( psz );
	}
	EXPECT_EQ( kElements, seen.size() );
}
