//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — tier1's CRC-32, as checksum_crc.cpp computes it ====//
//
// The reflected CRC-32 of IEEE 802.3 (polynomial 0xEDB88320), started at
// 0xFFFFFFFF and finished by XOR-ing 0xFFFFFFFF; the table is built at
// compile time from the polynomial. Stock processes a word at a time where it
// can; a byte at a time gives the same value. A length of zero or less
// leaves the running value as it was. ss_crc32_oracle_test holds this to
// pristine checksum_crc.cpp.
//=============================================================================//
#pragma once

#include <cstdint>

namespace ss
{
struct Crc32Table
{
	uint32_t m_Entries[256] = {};

	constexpr Crc32Table()
	{
		for ( unsigned int i = 0; i < 256; i++ )
		{
			uint32_t c = i;
			for ( int k = 0; k < 8; k++ )
				c = ( c & 1 ) ? ( 0xEDB88320u ^ ( c >> 1 ) ) : ( c >> 1 );
			m_Entries[i] = c;
		}
	}

	constexpr uint32_t operator[]( unsigned int i ) const { return m_Entries[i]; }
};

inline constexpr Crc32Table kCrc32Table;

static_assert( kCrc32Table[1] == 0x77073096u, "the IEEE polynomial, reflected" );

inline uint32_t Crc32Start() { return 0xFFFFFFFFu; }

inline uint32_t Crc32Add( uint32_t crc, const void *pData, int nBytes )
{
	const unsigned char *p = (const unsigned char *)pData;
	for ( int i = 0; i < nBytes; i++ )
		crc = kCrc32Table[( crc ^ p[i] ) & 0xFFu] ^ ( crc >> 8 );
	return crc;
}

inline uint32_t Crc32Finish( uint32_t crc ) { return crc ^ 0xFFFFFFFFu; }

inline uint32_t Crc32TableEntry( unsigned int slot ) { return kCrc32Table[slot & 0xFFu]; }
} // namespace ss
