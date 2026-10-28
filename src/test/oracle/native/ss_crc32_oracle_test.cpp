// ss_crc32.h against pristine tier1/checksum_crc.cpp: seeded buffers of every
// length 0..300 at every start alignment, fed whole and in random pieces, the
// running value and the finished one compared after each piece, and the table
// entry by entry. Negative lengths are not generated: stock's switch falls
// into its alignment path with them, and no caller passes one.

#include "gtest/gtest.h"

#include "tier1/checksum_crc.h"
#include "ss_crc32.h"

#include <cstdint>
#include <cstring>

namespace
{
struct Rng
{
	uint32_t s = 0x9E3779B9u;

	uint32_t Next()
	{
		s ^= s << 13;
		s ^= s >> 17;
		s ^= s << 5;
		return s;
	}

	int Pick( int lo, int hi ) { return lo + (int)( Next() % (uint32_t)( hi - lo + 1 ) ); }
};
}

TEST( SsCrc32Oracle, TheTableIsStocks )
{
	for ( unsigned int i = 0; i < 300; i++ )
		ASSERT_EQ( CRC32_GetTableEntry( i ), ss::Crc32TableEntry( i ) ) << i;
}

TEST( SsCrc32Oracle, EveryBufferChecksumsAsStocks )
{
	Rng r;
	unsigned char data[320 + 8];
	int n = 0;
	for ( int len = 0; len <= 300; len++ )
	{
		for ( int align = 0; align < 8; align++ )
		{
			for ( int i = 0; i < (int)sizeof( data ); i++ )
				data[i] = (unsigned char)r.Next();
			const unsigned char *p = data + align;

			CRC32_t stock;
			CRC32_Init( &stock );
			uint32_t ours = ss::Crc32Start();
			ASSERT_EQ( stock, ours );

			int at = 0;
			while ( at < len )
			{
				const int piece = ( r.Pick( 0, 3 ) == 0 ) ? len - at : r.Pick( 0, len - at );
				CRC32_ProcessBuffer( &stock, p + at, piece );
				ours = ss::Crc32Add( ours, p + at, piece );
				ASSERT_EQ( stock, ours ) << "len " << len << " align " << align << " at " << at << " piece " << piece;
				at += piece;
			}
			CRC32_Final( &stock );
			ASSERT_EQ( stock, ss::Crc32Finish( ours ) ) << "len " << len << " align " << align;
			ASSERT_EQ( CRC32_ProcessSingleBuffer( p, len ),
					   ss::Crc32Finish( ss::Crc32Add( ss::Crc32Start(), p, len ) ) );
			n++;
		}
	}
	EXPECT_EQ( n, 301 * 8 );
}
