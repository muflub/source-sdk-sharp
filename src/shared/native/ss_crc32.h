//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// tier1's CRC-32 (reflected IEEE 802.3, polynomial 0xEDB88320), byte at a time;
// stock's word-at-a-time loop gives the same value. A length of zero or less
// leaves the running value unchanged.
#pragma once

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

inline uint32_t Crc32Start()
{
	return 0xFFFFFFFFu;
}

inline uint32_t Crc32Add( uint32_t crc, const void *pData, int nBytes )
{
	const unsigned char *p = (const unsigned char *)pData;
	for ( int i = 0; i < nBytes; i++ )
		crc = kCrc32Table[( crc ^ p[i] ) & 0xFFu] ^ ( crc >> 8 );
	return crc;
}

inline uint32_t Crc32Finish( uint32_t crc )
{
	return crc ^ 0xFFFFFFFFu;
}

inline uint32_t Crc32TableEntry( unsigned int slot )
{
	return kCrc32Table[slot & 0xFFu];
}
} // namespace ss
