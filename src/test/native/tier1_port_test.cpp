//========= SourceSharp — tier1's small members, ported into server.so ======//
//
// plan_server_novalve.md P3. tier1_port.cpp replaced bitbuf's SetOverflowFlag
// and checksum_crc.cpp's CRC32; these pin stock's behaviour for each. (The
// interface registry it also held is shell_factory.cpp's table now:
// server_factory_test.)
//
//=============================================================================//

#include "gtest/gtest.h"

#include "tier1/bitbuf.h"
#include "tier1/checksum_crc.h"
#include "tier1/strtools.h"

#include <stdarg.h>
#include <string.h>

TEST( Tier1Port, SetOverflowFlagMarksTheReaderOverflowed )
{
	// bf_read's constructor is tier1's bitbuf.cpp, which this binary must not
	// link (it defines SetOverflowFlag too): a zeroed reader is one that does
	// not assert on overflow and has not overflowed.
	alignas( bf_read ) unsigned char mem[sizeof( bf_read )];
	memset( mem, 0, sizeof( mem ) );
	bf_read *r = reinterpret_cast< bf_read * >( mem );
	EXPECT_FALSE( r->IsOverflowed() );
	r->SetOverflowFlag();
	EXPECT_TRUE( r->IsOverflowed() );
}

// V_strncpy's two facts retired with it (plan_server_native_v2 T2): its two
// callers spell ss::StrCopy, whose own facts are the strtools tests'.

TEST( Tier1PortStrtools, StricmpFoldsAsciiCaseAndKeepsTheSign )
{
	EXPECT_EQ( 0, V_stricmp( "Worldspawn", "WORLDSPAWN" ) );
	EXPECT_LT( V_stricmp( "abc", "ABD" ), 0 );
	EXPECT_GT( V_stricmp( "abd", "ABC" ), 0 );
	EXPECT_LT( V_stricmp( "ab", "ABC" ), 0 );
	// Folding is to LOWER case: '_' (0x5F) sorts before 'A' as 'a' (0x61).
	// An upper-case fold would put it after 'A' (0x41).
	EXPECT_LT( V_stricmp( "_", "A" ), 0 );
}

namespace
{
int Vsn( char *pDest, int maxLen, bool *pbTruncated, const char *pFormat, ... )
{
	va_list args;
	va_start( args, pFormat );
	const int n = V_vsnprintfRet( pDest, maxLen, pFormat, args, pbTruncated );
	va_end( args );
	return n;
}
}

TEST( Tier1PortStrtools, VsnprintfRetAnswersTheLengthWhenItFits )
{
	char buf[ 16 ];
	bool bTruncated = true;
	EXPECT_EQ( 5, Vsn( buf, sizeof( buf ), &bTruncated, "%s-%d", "ab", 12 ) );
	EXPECT_STREQ( "ab-12", buf );
	EXPECT_FALSE( bTruncated );
}

TEST( Tier1PortStrtools, VsnprintfRetAnswersMaxLenAndFlagsATruncation )
{
	char buf[ 4 ];
	bool bTruncated = false;
	EXPECT_EQ( 4, Vsn( buf, sizeof( buf ), &bTruncated, "%s", "abcdef" ) );
	EXPECT_STREQ( "abc", buf );
	EXPECT_TRUE( bTruncated );
}
