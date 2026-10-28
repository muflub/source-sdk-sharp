//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — a length-prefixed bit buffer, native half ==========//
//
// User ruling 2026-10-12: buffers cross between managed and native code
// length-prefixed, so a read or a write is one atomic hand-over of the whole
// buffer. A block is a little-endian int32 count of valid BITS, then the bits,
// LSB-first in each byte (bf_write's order). The managed half is
// SourceSharp.Abi.BitBlock. The prefix is only on this crossing: what reaches
// an engine buffer is the payload's bits alone, in one copy.
//=============================================================================//

#pragma once

#include "tier1/bitbuf.h"

#include <stdint.h>
#include <string.h>

namespace ss
{
enum { kBitBlockHeaderBytes = 4 };

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

// The block's payload into an engine bf_write, whole: one WriteBits, which
// writes nothing and sets the overflow flag when it does not fit. A block
// whose count is negative (managed's encoder overflowed) or past its own
// capacity is refused the same way. Answers whether it was written.
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

// The bits left in an engine bf_read into a block, whole, WITHOUT moving the
// engine's read position (read from a copy of the reader): one ReadBits. When
// they do not fit in nBlockBytes, answers -(the bytes needed) and writes no
// payload; else the bit count, which is also written as the prefix.
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
