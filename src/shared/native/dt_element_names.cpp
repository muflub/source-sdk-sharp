//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON f  a bit-level wire codec
//
// The names a network array's elements are given (dt_utlvector_common.cpp's
// helper, split out in plan_server_native_v2 T2): both DLLs' send and receive
// table builders ask it, and server.so needs nothing else of that file.

#include "dt_common.h"
#include "dt_utlvector_common.h"

// "000" to "999", then "1000" to "2047": the name each element of a network
// array is given, written the first time it is asked for. A table of 2048
// pointers to string literals was 16 KB of initialised data in each game DLL,
// with a relocation per entry; this one is zero until used, so it is .bss and
// costs the file nothing (dt_element_names_test). The pointer handed out is
// the slot's own, and a prop keeps it, so a name is never written twice: its
// first byte is non-zero from then on. The callers are the send table
// builders, which run as the DLL's tables are set up, one at a time.
static char s_DTElementNames[ MAX_ARRAY_ELEMENTS ][ 5 ];   // four digits and the terminator

const char *DT_ArrayElementNameForIdx( size_t i )
{
	Assert( i < MAX_ARRAY_ELEMENTS );
	char *pszName = s_DTElementNames[ i ];
	if ( pszName[ 0 ] == 0 )
	{
		// Three digits below 1000, four from there; the terminator is the
		// table's own zero. Written out rather than formatted: the server
		// links no tier1, so V_snprintf is not there to call.
		const int n = (int)i;
		char *p = pszName;
		if ( n >= 1000 )
			*p++ = (char)( '0' + n / 1000 );
		p[ 0 ] = (char)( '0' + n / 100 % 10 );
		p[ 1 ] = (char)( '0' + n / 10 % 10 );
		p[ 2 ] = (char)( '0' + n % 10 );
	}
	return pszName;
}
