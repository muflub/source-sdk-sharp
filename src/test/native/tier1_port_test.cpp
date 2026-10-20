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

// V_stricmp moved to the client's strtools_client.cpp and V_vsnprintfRet went
// (plan_server_native_v2 T5: no server object imports either; nothing imports
// V_vsnprintfRet). The fold V_stricmp forwards to is ss::StrICmp, held to
// pristine strtools.cpp by ss_string_oracle_test.
