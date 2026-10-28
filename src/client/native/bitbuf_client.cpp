//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the bit buffer members only client.so calls ========//
// SS_REASON f  a bit-level wire codec
//
// bitbuf.h's out-of-line members client.so still calls (C9, tier1): the raw
// whole-buffer copy in and out -- WriteBits, which copies a BitBlock's payload
// (ss_bitblock.h) or a client message's bytes into an engine bf_write, and
// ReadBits, which copies an engine bf_read's bits out -- and what bitbuf.h's
// inline writers name in debug builds (GetDebugName, the error hook). Every
// field-level encode is C#'s now (UserCmdCodec, the user-message payloads).
// The buffers are the engine's; these write and read them by bitbuf.h's
// layout and its inline single-word helpers. The server answered its half in
// C# (plan_server_novalve 3.3), so these are client-only (ruling 2026-10-12).
//
// bitbuf_client_test holds them to pristine bitbuf.cpp's results, recorded by
// bitbuf_stock_oracle_test (src/test/client/Fixtures/bitbuf-stock.tsv).
//=============================================================================//

#include "tier1/bitbuf.h"
#include "tier0/platform.h"

#include <cstdint>
#include <cstring>

// The debug builds' error hook. Nothing on client.so installs a handler
// (SetBitBufErrorHandler has no caller), so there is nothing to call.
void InternalBitBufErrorHandler( BitBufErrorType, const char * )
{
}

const char *bf_write::GetDebugName()
{
	return m_pDebugName;
}

// Raw bits from pIn: refused whole (overflow set) when they do not fit;
// byte-aligned runs of a word or more are copied, the rest go a byte, then
// the tail bits, at a time.
bool bf_write::WriteBits( const void *pIn, int nBits )
{
	if ( m_iCurBit + nBits > m_nDataBits )
	{
		SetOverflowFlag();
		CallErrorHandler( BITBUFERROR_BUFFER_OVERRUN, GetDebugName() );
		return false;
	}

	const unsigned char *src = (const unsigned char *)pIn;
	int left = nBits;

	// Bytes until the source pointer is word-aligned.
	while ( ( (uintptr_t)src & 3 ) && left >= 8 )
	{
		WriteUBitLong( *src++, 8, false );
		left -= 8;
	}

	// A byte-aligned destination takes the whole bytes in one copy.
	if ( left >= 32 && !( m_iCurBit & 7 ) )
	{
		const int bytes = left >> 3;
		memcpy( (unsigned char *)m_pData + ( m_iCurBit >> 3 ), src, (size_t)bytes );
		src += bytes;
		left -= bytes << 3;
		m_iCurBit += bytes << 3;
	}

	// Otherwise a word at a time, each split across two destination words.
	if ( left >= 32 )
	{
		const uint32 shift = m_iCurBit & 31;
		const uint32 keepLow = shift ? ( ( 1u << shift ) - 1 ) : 0;	// bits below shift in the first word
		uint32 *dst = &m_pData[ m_iCurBit >> 5 ];
		for ( ; left >= 32; left -= 32, m_iCurBit += 32 )
		{
			uint32 word;
			memcpy( &word, src, 4 );
			src += 4;
			*dst = ( *dst & keepLow ) | ( word << shift );
			++dst;
			if ( shift )
				*dst = ( *dst & ~( ( 1u << shift ) - 1 ) ) | ( word >> ( 32 - shift ) );
		}
	}

	for ( ; left >= 8; left -= 8 )
		WriteUBitLong( *src++, 8, false );
	if ( left )
		WriteUBitLong( *src, left, false );

	return !IsOverflowed();
}

// Raw bits into pOut: bytes until pOut is word-aligned, then words, bytes,
// and the tail bits into the next byte.
void bf_read::ReadBits( void *pOut, int nBits )
{
	unsigned char *dst = (unsigned char *)pOut;
	int left = nBits;

	while ( ( (uintptr_t)dst & 3 ) && left >= 8 )
	{
		*dst++ = (unsigned char)ReadUBitLong( 8 );
		left -= 8;
	}
	for ( ; left >= 32; left -= 32, dst += 4 )
	{
		const uint32 word = ReadUBitLong( 32 );
		memcpy( dst, &word, 4 );
	}
	for ( ; left >= 8; left -= 8 )
		*dst++ = (unsigned char)ReadUBitLong( 8 );
	if ( left )
		*dst = (unsigned char)ReadUBitLong( left );
}

