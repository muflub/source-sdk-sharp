// client.so's bit-buffer members (src/client/native/bitbuf_client.cpp, with
// tier1_port.cpp's bf_read::SetOverflowFlag) over bitbuf_scenario.h, against
// the golden pristine bitbuf.cpp recorded (bitbuf_stock_oracle_test,
// src/test/client/Fixtures/bitbuf-stock.tsv): every buffer, bit position,
// overflow flag and byte read, the same.

#include "gtest/gtest.h"

#include "bitbuf_scenario.h"

#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>

TEST( BitbufClient, OurMembersWriteAndReadWhatPristineBitbufDid )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	const std::string golden = std::string( pszRepo ? pszRepo : "." ) + "/src/test/client/Fixtures/bitbuf-stock.tsv";
	std::ifstream in( golden );
	ASSERT_TRUE( in.good() ) << golden << " is missing";

	std::istringstream ours( bitbuf_scenario::Run() );
	std::string a, b;
	int n = 0;
	while ( true )
	{
		const bool ha = (bool)std::getline( ours, a );
		const bool hb = (bool)std::getline( in, b );
		if ( !ha && !hb )
			break;
		ASSERT_TRUE( ha && hb && a == b )
			<< "line " << n + 1 << ": ours\n  " << ( ha ? a : "<end>" ) << "\nstock\n  " << ( hb ? b : "<end>" );
		++n;
	}
	EXPECT_GT( n, 6000 );
}
