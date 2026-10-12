//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — tier1's small members, as server.so uses them ========//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// plan_server_novalve.md P3. server.so does not link tier1. Each member here
// is one a header on the allowlist declares and server code (or inline code in
// that header) calls, answered with tier1's documented behaviour; each arrived
// in the commit that took its tier1 object off the link.
//
//=============================================================================//

#include "tier1/bitbuf.h"
#include "tier1/strtools.h"

#include "ss_string.h"

#include <stdarg.h>
#include <stdio.h>

#include <string.h>

//-----------------------------------------------------------------------------
// bitbuf.h's inline Seek (and the reads) flag a read past the end through this.
// A buffer that asked to assert on overflow does; release builds only set it.
//-----------------------------------------------------------------------------
void bf_read::SetOverflowFlag()
{
	Assert( !m_bAssertOnOverflow );
	m_bOverflow = true;
}

//-----------------------------------------------------------------------------
// strtools.cpp's members that inline code in fork headers still calls (P4:
// libtier1.a leaves the link): CUtlVector's ShiftElements* (V_memmove,
// V_memset in debug), mapentities_shared.h's parser (Q_strncpy, Q_memset),
// CaselessStringLessThan / StringLessThan (V_stricmp, V_strcmp), CFmtStr
// (V_vsnprintfRet), saverestore's string ops (V_strlen). Our own code calls
// ss_string.h; these go when P5's header move takes the callers away.
//
// The debug forms take __FILE__/__LINE__ for tier0's pointer asserts, which
// server.so does not have; they are the C library call.
//-----------------------------------------------------------------------------
void _V_memset( const char *, int, void *dest, int fill, int count )
{
	memset( dest, fill, count );
}

void _V_memmove( const char *, int, void *dest, const void *src, int count )
{
	memmove( dest, src, count );
}

int _V_strcmp( const char *, int, const char *s1, const char *s2 )
{
	return strcmp( s1, s2 );
}

// ASCII case folding, the sign of V_stricmp (ss_string_oracle_test holds it).
int V_stricmp( const char *s1, const char *s2 )
{
	return ss::StrICmp( s1, s2 );
}

// strncpy's semantics, kept: the rest of pDest is zero-filled, then the last
// byte is always the terminator.
void V_strncpy( char *pDest, const char *pSrc, int maxLen )
{
	if ( maxLen <= 0 )
		return;
	strncpy( pDest, pSrc, maxLen );
	pDest[ maxLen - 1 ] = 0;
}

// The length written; on truncation (or an encoding error) maxLen, with the
// output cut and terminated, and *pbTruncated set.
int V_vsnprintfRet( char *pDest, int maxLen, const char *pFormat, va_list params, bool *pbTruncated )
{
	int len = vsnprintf( pDest, maxLen, pFormat, params );
	const bool bTruncated = len < 0 || len >= maxLen;
	if ( pbTruncated )
		*pbTruncated = bTruncated;
	if ( bTruncated )
	{
		len = maxLen;
		if ( maxLen > 0 )
			pDest[ maxLen - 1 ] = 0;
	}
	return len;
}
