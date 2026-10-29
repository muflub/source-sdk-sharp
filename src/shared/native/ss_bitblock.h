//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// A length-prefixed bit buffer crossing between managed and native code in one copy:
// a little-endian int32 count of valid bits, then the bits LSB-first (bf_write's order).
// The managed half is SourceSharp.Abi.BitBlock; engine buffers get the payload alone.

#pragma once

#include "tier1/bitbuf.h"

namespace ss
{
enum
{
	kBitBlockHeaderBytes = 4
};

inline int32_t BitBlockBits( const uint8_t *pBlock )
{
	int32_t nBits;
	memcpy( &nBits, pBlock, sizeof( nBits ) );
	return nBits;
}

// Writes the prefix BitBlockBits reads.
inline void BitBlockSetBits( uint8_t *pBlock, int32_t nBits )
{
	memcpy( pBlock, &nBits, sizeof( nBits ) );
}

// Writes the payload with one WriteBits. A negative count (managed overflow) or one past
// the block's capacity sets the overflow flag instead. Returns whether it was written.
inline bool BitBlockToEngine( bf_write *pBuf, const uint8_t *pBlock, int nBlockBytes )
{
	const int32_t nBits = BitBlockBits( pBlock );
	if ( nBits < 0 || nBits > ( nBlockBytes - kBitBlockHeaderBytes ) * 8 )
	{
		pBuf->SetOverflowFlag();
		return false;
	}
	return pBuf->WriteBits( pBlock + kBitBlockHeaderBytes, nBits );
}

// Copies the bits left in buf into a block without moving buf's read position.
// Returns the bit count, or -(bytes needed) when the block is too small.
inline int32_t BitBlockFromEngine( const bf_read &buf, uint8_t *pBlock, int nBlockBytes )
{
	bf_read copy = buf;
	const int32_t nBits = copy.GetNumBitsLeft();
	const int nNeeded = kBitBlockHeaderBytes + ( nBits + 7 ) / 8;
	if ( nNeeded > nBlockBytes )
		return -nNeeded;
	memcpy( pBlock, &nBits, sizeof( nBits ) );
	if ( nBits > 0 )
		copy.ReadBits( pBlock + kBitBlockHeaderBytes, nBits );
	return nBits;
}
} // namespace ss
