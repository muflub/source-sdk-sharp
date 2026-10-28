//======= Copyright © 2005, , Valve Corporation, All rights reserved. =========
// Purpose: Variant Pearson Hash general purpose hashing algorithm described
//			by Cargill in C++ Report 1994. Generates a 16-bit result.

#pragma once

unsigned FASTCALL HashString( const char *pszKey );
unsigned FASTCALL HashStringCaseless( const char *pszKey );

unsigned FASTCALL HashInt( const int key );

inline uint64 HashUint64( uint64 s )
{
	// Thomas Wang hash, http://www.concentric.net/~ttwang/tech/inthash.htm
	s = ( ~s ) + ( s << 21 ); // s = (s << 21) - s - 1;
	s = s ^ ( s >> 24 );
	s = ( s + ( s << 3 ) ) + ( s << 8 ); // s * 265
	s = s ^ ( s >> 14 );
	s = ( s + ( s << 2 ) ) + ( s << 4 ); // s * 21
	s = s ^ ( s >> 28 );
	s = s + ( s << 31 );
	return s;
}

inline intp HashIntp( intp s )
{
#ifdef PLATFORM_64BITS
	COMPILE_TIME_ASSERT( sizeof( s ) == sizeof( uint64 ) );
	return ( intp )HashUint64( ( uint64 )s );
#else
	return HashInt( s );
#endif
}

inline unsigned HashIntConventional( const int n ) // faster but less effective
{
	// first byte
	unsigned hash = 0xAAAAAAAA + (n & 0xFF);
	// second byte
	hash = ( hash << 5 ) + hash + ( (n >> 8) & 0xFF );
	// third byte
	hash = ( hash << 5 ) + hash + ( (n >> 16) & 0xFF );
	// fourth byte
	hash = ( hash << 5 ) + hash + ( (n >> 24) & 0xFF );

	return hash;

	/* this is the old version, which would cause a load-hit-store on every
	   line on a PowerPC, and therefore took hundreds of clocks to execute!
	  
	byte *p = (byte *)&n;
	unsigned hash = 0xAAAAAAAA + *p++;
	hash = ( ( hash << 5 ) + hash ) + *p++;
	hash = ( ( hash << 5 ) + hash ) + *p++;
	return ( ( hash << 5 ) + hash ) + *p;
	*/
}

// Murmur hash
uint32 MurmurHash2( const void * key, int len, uint32 seed );
