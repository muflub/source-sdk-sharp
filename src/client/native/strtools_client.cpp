//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// The strtools.h functions only client.so calls; the ones both DLLs call are in
// tier1_port.cpp. Each forwards to ss_string.h or the C library call tier1 made;
// where tier1 called Error, so do these.

#include "tier0/dbg.h"
#include "tier0/basetypes.h"
#include "tier0/platform.h"
#include "ss_string.h"

#include <stdarg.h>
#include <string.h>
#include <wchar.h>

void _V_memcpy( const char *, int, void *dest, const void *src, int count )
{
	memcpy( dest, src, (size_t)count );
}

int _V_memcmp( const char *, int, const void *m1, const void *m2, int count )
{
	return memcmp( m1, m2, (size_t)count );
}

// CUtlVector's ShiftElements* call these two (V_memset in debug builds).
void _V_memset( const char *, int, void *dest, int fill, int count )
{
	memset( dest, fill, count );
}

void _V_memmove( const char *, int, void *dest, const void *src, int count )
{
	memmove( dest, src, count );
}

int _V_strlen( const char *, int, const char *str )
{
	return (int)strlen( str );
}

void _V_strcpy( const char *, int, char *dest, const char *src )
{
	strcpy( dest, src );
}

char *_V_strstr( const char *, int, const char *s1, const char *search )
{
	return const_cast< char * >( strstr( s1, search ) );
}

int _V_wcslen( const char *, int, const wchar_t *pwch )
{
	return (int)wcslen( pwch );
}

int _V_strcmp( const char *, int, const char *s1, const char *s2 )
{
	return strcmp( s1, s2 );
}

// strncpy's semantics: the rest is zero-filled, then the last byte is always
// the terminator.
void V_strncpy( char *pDest, const char *pSrc, int maxLen )
{
	if ( maxLen <= 0 )
		return;
	strncpy( pDest, pSrc, maxLen );
	pDest[ maxLen - 1 ] = 0;
}

int V_strncmp( const char *s1, const char *s2, int count )
{
	return ss::StrNCmp( s1, s2, count );
}

// ASCII case folding, with V_stricmp's sign.
int V_stricmp( const char *s1, const char *s2 )
{
	return ss::StrICmp( s1, s2 );
}

int V_strnicmp( const char *s1, const char *s2, int n )
{
	return ss::StrNICmp( s1, s2, n );
}

char *V_strlower( char *start )
{
	return ss::StrLower( start );
}

char *V_strupr( char *start )
{
	return ss::StrUpper( start );
}

int V_StrTrim( char *pStr )
{
	return ss::StrTrim( pStr );
}

int64 V_atoi64( const char *str )
{
	return ss::AtoI64( str );
}

const char *V_stristr( const char *pStr, const char *pSearch )
{
	return ss::StrIStr( pStr, pSearch );
}

char *V_stristr( char *pStr, const char *pSearch )
{
	return ss::StrIStr( pStr, pSearch );
}

const char *V_strnistr( const char *pStr, const char *pSearch, int n )
{
	return ss::StrNIStr( pStr, pSearch, n );
}

const char *V_strnchr( const char *pStr, char c, int n )
{
	return ss::StrNChr( pStr, c, n );
}

char *V_strncat( char *pDest, const char *pSrc, size_t cchDest, int max_chars_to_copy )
{
	return ss::StrAppend( pDest, pSrc, cchDest, max_chars_to_copy );
}

int V_vsnprintf( char *pDest, int maxLen, const char *pFormat, va_list params )
{
	return ss::FormatV( pDest, maxLen, pFormat, params );
}

int V_snprintf( char *pDest, int maxLen, const char *pFormat, ... )
{
	va_list params;
	va_start( params, pFormat );
	const int n = ss::FormatV( pDest, maxLen, pFormat, params );
	va_end( params );
	return n;
}

void V_binarytohex( const byte *in, int inputbytes, char *out, int outsize )
{
	ss::BinaryToHex( in, inputbytes, out, outsize );
}

void V_FixSlashes( char *pname, char separator )
{
	ss::FixSlashes( pname, separator );
}

void V_FileBase( const char *in, char *out, int maxlen )
{
	ss::FileBase( in, out, maxlen );
}

void V_StripExtension( const char *in, char *out, int outLen )
{
	ss::StripExtension( in, out, outLen );
}

void V_SetExtension( char *path, const char *extension, int pathStringLength )
{
	ss::SetExtension( path, extension, pathStringLength );
}

const char *V_GetFileExtension( const char *path )
{
	return ss::GetFileExtension( path );
}

void V_ExtractFileExtension( const char *path, char *dest, int destSize )
{
	ss::ExtractFileExtension( path, dest, destSize );
}

bool V_ExtractFilePath( const char *path, char *dest, int destSize )
{
	return ss::ExtractFilePath( path, dest, destSize );
}

bool V_StripLastDir( char *dirName, int maxlen )
{
	return ss::StripLastDir( dirName, maxlen );
}

void V_StripTrailingSlash( char *ppath )
{
	ss::StripTrailingSlash( ppath );
}

const char *V_UnqualifiedFileName( const char *in )
{
	return ss::GetFileName( in );
}

bool V_IsAbsolutePath( const char *pPath )
{
	return ss::IsAbsolutePath( pPath );
}

bool V_RemoveDotSlashes( char *pFilename, char separator, bool )
{
	return ss::RemoveDotSlashes( pFilename, separator );
}

void V_ComposeFileName( const char *path, const char *filename, char *dest, int destSize )
{
	if ( !ss::ComposeFileName( path, filename, dest, destSize ) )
		Error( "V_ComposeFileName: %s does not fit in %d bytes with a separator.", path, destSize );
}

void V_MakeAbsolutePath( char *pOut, int outLen, const char *pPath, const char *pStartingDir )
{
	if ( !ss::MakeAbsolutePath( pOut, outLen, pPath, pStartingDir ) )
		Error( "V_MakeAbsolutePath: %s could not be made absolute in %d bytes.", pPath, outLen );
}
