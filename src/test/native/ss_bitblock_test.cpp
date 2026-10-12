// ss_bitblock.h, the native half of the length-prefixed bit buffer
// (user ruling 2026-10-12): a block's payload reaches an engine bf_write
// whole and at any bit offset, is refused whole when it does not fit or its
// prefix lies, and an engine bf_read's remaining bits come out as a block
// without moving the reader. Over client.so's bitbuf members
// (bitbuf_client.cpp, tier1_port.cpp).

#include "gtest/gtest.h"

#include "ss_bitblock.h"

#include <cstdint>
#include <cstring>

namespace
{
struct Writer
{
	alignas( bf_write ) unsigned char storage[ sizeof( bf_write ) ];
	uint32 data[ 16 ];
	bf_write *w;
	Writer( int start )
	{
		memset( storage, 0, sizeof( storage ) );
		memset( data, 0, sizeof( data ) );
		w = reinterpret_cast< bf_write * >( storage );
		w->m_pData = data;
		w->m_nDataBytes = sizeof( data );
		w->m_nDataBits = sizeof( data ) * 8;
		w->m_iCurBit = start;
	}
};

int Bit( const void *p, int i ) { return ( ( (const uint8_t *)p )[ i >> 3 ] >> ( i & 7 ) ) & 1; }
}

TEST( SsBitBlock, APayloadLandsWholeAtEveryOffset )
{
	uint8_t block[ ss::kBitBlockHeaderBytes + 16 ];
	for ( int i = 0; i < 16; i++ )
		block[ ss::kBitBlockHeaderBytes + i ] = (uint8_t)( 0x5A ^ ( i * 37 ) );
	int nChecked = 0;
	for ( int nBits = 0; nBits <= 128; nBits += 13 )
	{
		const int32_t n = nBits;
		memcpy( block, &n, 4 );
		for ( int start = 0; start < 40; start++ )
		{
			Writer wr( start );
			ASSERT_TRUE( ss::BitBlockToEngine( wr.w, block, sizeof( block ) ) );
			ASSERT_EQ( start + nBits, wr.w->m_iCurBit );
			for ( int i = 0; i < nBits; i++ )
				ASSERT_EQ( Bit( block + ss::kBitBlockHeaderBytes, i ), Bit( wr.data, start + i ) ) << nBits << "@" << start;
			for ( int i = 0; i < start; i++ )
				ASSERT_EQ( 0, Bit( wr.data, i ) );
			nChecked++;
		}
	}
	EXPECT_GT( nChecked, 300 );
}

TEST( SsBitBlock, ABlockThatDoesNotFitOrLiesIsRefusedWhole )
{
	uint8_t block[ ss::kBitBlockHeaderBytes + 8 ] = {};
	memset( block + 4, 0xFF, 8 );

	int32_t n = 64;
	memcpy( block, &n, 4 );
	Writer wr( 16 * 32 - 10 );	// ten bits left
	EXPECT_FALSE( ss::BitBlockToEngine( wr.w, block, sizeof( block ) ) );
	EXPECT_TRUE( wr.w->IsOverflowed() );
	EXPECT_EQ( 0u, wr.data[ 15 ] );

	n = -1;	// managed's encoder overflowed
	memcpy( block, &n, 4 );
	Writer a( 0 );
	EXPECT_FALSE( ss::BitBlockToEngine( a.w, block, sizeof( block ) ) );
	EXPECT_TRUE( a.w->IsOverflowed() );

	n = 65;	// more bits than the block holds
	memcpy( block, &n, 4 );
	Writer b( 0 );
	EXPECT_FALSE( ss::BitBlockToEngine( b.w, block, sizeof( block ) ) );
	EXPECT_EQ( 0, b.w->m_iCurBit );
}

TEST( SsBitBlock, AReadersRemainingBitsComeOutWithoutMovingIt )
{
	uint32 data[ 4 ];
	for ( int i = 0; i < 4; i++ )
		data[i] = 0x9E3779B9u * ( i + 1 );
	alignas( bf_read ) unsigned char storage[ sizeof( bf_read ) ];
	memset( storage, 0, sizeof( storage ) );
	bf_read *r = reinterpret_cast< bf_read * >( storage );
	r->m_pData = reinterpret_cast< const unsigned char * >( data );
	r->m_nDataBytes = sizeof( data );
	r->m_nDataBits = 100;
	r->m_iCurBit = 23;

	uint8_t block[ ss::kBitBlockHeaderBytes + 16 ];
	ASSERT_EQ( 77, ss::BitBlockFromEngine( *r, block, sizeof( block ) ) );
	EXPECT_EQ( 77, ss::BitBlockBits( block ) );
	EXPECT_EQ( 23, r->m_iCurBit );
	for ( int i = 0; i < 77; i++ )
		ASSERT_EQ( Bit( data, 23 + i ), Bit( block + ss::kBitBlockHeaderBytes, i ) ) << i;

	uint8_t small[ ss::kBitBlockHeaderBytes + 4 ];
	EXPECT_EQ( -( ss::kBitBlockHeaderBytes + 10 ), ss::BitBlockFromEngine( *r, small, sizeof( small ) ) );
}
