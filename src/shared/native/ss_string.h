//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the string helpers our native code calls ===========//
//
// The handful of tier1 string routines (strtools.cpp) that have no exact
// counterpart in the C and C++ standard libraries, restated so that our
// server-compiled sources call these instead and server.so stops extracting
// strtools.cpp -- the last member of libtier1.a it pulled in. Where the
// standard library already does the same thing (memcpy, memset, memcmp,
// strlen, strcmp, strncmp, strchr, strstr, std::swap) callers use it
// directly and nothing is here.
//
// Each helper names the tier1 function it replaces and the detail of that
// function's behaviour it keeps. The details are what differ from the obvious
// standard call, and they are what a caller may have come to rely on:
//
//   StrCopy     V_strncpy     strncpy that always terminates; zero-pads the rest
//   StrAppend   V_strncat     appends within the WHOLE buffer size; a cap of
//                             kCopyAll (or anything below it) means no cap
//   Format(V)   V_(v)snprintf on truncation, answers the buffer size, not the
//                             length that would have been written
//   StrICmp     V_stricmp     ASCII-only case folding, answers -1 / 0 / 1
//   StrNICmp    V_strnicmp    the same over at most n characters
//   StrIStr     V_stristr     ASCII-only folding; an empty needle finds nothing
//   AtoI(64)    V_atoi(64)    optional sign, then "0x" hex, a quoted character,
//                             or decimal digits; stops at the first other char
//   AtoF        V_atof        its own digit loop (not strtod): the same sign,
//                             hex and quote rules, one '.', an e/E exponent
//   StrLower    V_strlower    ASCII letters only
//   NextToken   nexttoken     copies up to a separator into a bounded buffer
//   FixSlashes, StripExtension, FileBase, StripFilename, StripLastDir,
//   GetFileName               the path helpers of the same V_ names; '/' and
//                             '\\' both count as separators on every platform
//
// Case folding never consults the C locale. tier1 falls back to the CRT for a
// byte of 0x80 or above; in the "C" locale the game runs under, the CRT leaves
// such a byte as it is, which is what these do unconditionally.
//
// The oracle (src/test/oracle/native/ss_string_oracle_test.cpp) links the
// pristine strtools.cpp and holds every helper to the function it replaces.
// Where tier1's behaviour is undefined (a negative size, a zero-size format
// buffer, GetFileName of an empty string) these write nothing instead; each
// such case is marked below.
//
// No engine headers: C++17 and the C library only, so a unit test can include
// it bare.
//
//=============================================================================//
#pragma once

#include <cmath>
#include <cstdarg>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "ss_platform.h"	// SS_GetCwd, for MakeAbsolutePath

#if defined( __GNUC__ ) || defined( __clang__ )
#define SS_STRING_PRINTF( fmt, args ) __attribute__( ( format( printf, fmt, args ) ) )
#else
#define SS_STRING_PRINTF( fmt, args )
#endif

namespace ss
{

//-----------------------------------------------------------------------------
// One character, ASCII only. A byte outside 'A'..'Z' (including every byte of
// 0x80 and above) comes back unchanged.
//-----------------------------------------------------------------------------
inline unsigned char AsciiLower( unsigned char c )
{
	return ( c >= 'A' && c <= 'Z' ) ? (unsigned char)( c + ( 'a' - 'A' ) ) : c;
}

inline bool IsPathSeparator( char c )
{
	return c == '/' || c == '\\';
}

//-----------------------------------------------------------------------------
// V_strncpy. Copies at most dstSize-1 characters and always writes a
// terminator at dst[dstSize-1]; like strncpy, every byte after the copied
// string up to dstSize is zero. dstSize 0 writes nothing. (Negative is
// undefined in tier1; here it writes nothing.)
//-----------------------------------------------------------------------------
inline void StrCopy( char *dst, const char *src, int dstSize )
{
	if ( dstSize <= 0 )
		return;
	// strncpy then dst[dstSize-1] = 0, said directly: gcc's -Wstringop-*
	// cannot tell that the terminator is written, and -Werror makes it a stop.
	const size_t n = strnlen( src, (size_t)dstSize - 1 );
	std::memcpy( dst, src, n );
	std::memset( dst + n, 0, (size_t)dstSize - n );
}

//-----------------------------------------------------------------------------
// V_strncat. dstSize is the size of the WHOLE buffer dst lives in, not the
// room left in it. maxChars caps how much of src is taken; kCopyAll, or any
// value at or below it, takes all of it. The result always fits with its
// terminator; when dst is already full (or overfull) nothing is appended.
//-----------------------------------------------------------------------------
const int kCopyAll = -1;

inline char *StrAppend( char *dst, const char *src, size_t dstSize, int maxChars = kCopyAll )
{
	const size_t have = std::strlen( dst );
	const size_t srcLen = std::strlen( src );
	size_t take = srcLen;
	if ( maxChars > kCopyAll && (size_t)maxChars < srcLen )
		take = (size_t)maxChars;
	if ( have + take >= dstSize )
		take = dstSize - have - 1;	// wraps when have >= dstSize; the test below refuses it
	if ( (int)take <= 0 )
		return dst;
	// strncat( dst, src, take ), said directly for the same gcc reason.
	std::memcpy( dst + have, src, take );
	dst[have + take] = '\0';
	return dst;
}

//-----------------------------------------------------------------------------
// V_vsnprintf / V_snprintf. The answer is the number of characters written
// when everything fit, and dstSize itself when the output was cut short (or
// the C library reported an error); the buffer is terminated either way.
// (dstSize <= 0 is undefined in tier1, which writes dst[-1]; here nothing is
// written and dstSize comes back.)
//-----------------------------------------------------------------------------
inline int FormatV( char *dst, int dstSize, const char *fmt, va_list args )
{
	if ( dstSize <= 0 )
		return dstSize;
	const int n = std::vsnprintf( dst, (size_t)dstSize, fmt, args );
	if ( n >= 0 && n < dstSize )
		return n;
	dst[dstSize - 1] = '\0';
	return dstSize;
}

inline int Format( char *dst, int dstSize, const char *fmt, ... ) SS_STRING_PRINTF( 3, 4 );
inline int Format( char *dst, int dstSize, const char *fmt, ... )
{
	va_list args;
	va_start( args, fmt );
	const int n = FormatV( dst, dstSize, fmt, args );
	va_end( args );
	return n;
}

//-----------------------------------------------------------------------------
// V_strnicmp: case-insensitive over at most n characters, ASCII folding only.
// The sign is that of the first differing folded byte, taken as unsigned,
// and the magnitude is always 1. (tier1 answers 1 too, except when a byte of
// 0x80 or above differs: its CRT fallback then answers the difference, of
// the same sign. Every caller reads only the sign.) n <= 0 answers 0.
//-----------------------------------------------------------------------------
inline int StrNICmp( const char *a, const char *b, int n )
{
	const unsigned char *pa = (const unsigned char *)a;
	const unsigned char *pb = (const unsigned char *)b;
	for ( ; n > 0; --n, ++pa, ++pb )
	{
		const unsigned char ca = AsciiLower( *pa );
		const unsigned char cb = AsciiLower( *pb );
		if ( ca != cb )
			return ca < cb ? -1 : 1;
		if ( ca == '\0' )
			break;
	}
	return 0;
}

//-----------------------------------------------------------------------------
// V_stricmp: StrNICmp without the bound. The same pointer twice is equal
// without being read.
//-----------------------------------------------------------------------------
inline int StrICmp( const char *a, const char *b )
{
	if ( a == b )
		return 0;
	const unsigned char *pa = (const unsigned char *)a;
	const unsigned char *pb = (const unsigned char *)b;
	for ( ;; ++pa, ++pb )
	{
		const unsigned char ca = AsciiLower( *pa );
		const unsigned char cb = AsciiLower( *pb );
		if ( ca != cb )
			return ca < cb ? -1 : 1;
		if ( ca == '\0' )
			return 0;
	}
}

//-----------------------------------------------------------------------------
// V_stristr: the first place needle occurs in haystack, ignoring ASCII case.
// Unlike strstr, an EMPTY needle is never found (tier1 answers NULL for it),
// and a null pointer on either side answers NULL.
//-----------------------------------------------------------------------------
inline const char *StrIStr( const char *haystack, const char *needle )
{
	if ( !haystack || !needle )
		return nullptr;
	// An empty needle needs no test of its own: its first byte is the
	// terminator, which no haystack character equals.
	const unsigned char first = AsciiLower( (unsigned char)needle[0] );
	for ( const char *start = haystack; *start; ++start )
	{
		if ( AsciiLower( (unsigned char)*start ) != first )
			continue;
		size_t i = 1;
		while ( needle[i] && start[i]
			&& AsciiLower( (unsigned char)start[i] ) == AsciiLower( (unsigned char)needle[i] ) )
		{
			++i;
		}
		if ( !needle[i] )
			return start;
		if ( !start[i] )
			return nullptr;	// the rest of the haystack is shorter than the needle
	}
	return nullptr;
}

inline char *StrIStr( char *haystack, const char *needle )
{
	return const_cast<char *>( StrIStr( (const char *)haystack, needle ) );
}

//-----------------------------------------------------------------------------
// V_atoi64 / V_atoi. An optional '-' or '+' (no leading whitespace), then
// one of: "0x"/"0X" and hex digits; a quote character, answering the code of
// the character after it; or decimal digits. Reading stops at the first
// character that does not belong, and no digits at all is 0. AtoI keeps the
// low 32 bits, as tier1's cast does.
//-----------------------------------------------------------------------------
inline int64_t AtoI64( const char *s )
{
	int64_t sign = 1;
	if ( *s == '-' || *s == '+' )
	{
		sign = ( *s == '-' ) ? -1 : 1;
		++s;
	}

	uint64_t value = 0;	// unsigned so that an over-long run wraps instead of being undefined
	if ( s[0] == '0' && ( s[1] == 'x' || s[1] == 'X' ) )
	{
		for ( s += 2;; ++s )
		{
			int digit;
			if ( *s >= '0' && *s <= '9' )
				digit = *s - '0';
			else if ( *s >= 'a' && *s <= 'f' )
				digit = *s - 'a' + 10;
			else if ( *s >= 'A' && *s <= 'F' )
				digit = *s - 'A' + 10;
			else
				break;
			value = ( value << 4 ) + (uint64_t)digit;
		}
		return (int64_t)( value * (uint64_t)sign );
	}

	if ( s[0] == '\'' )
		return sign * s[1];

	for ( ; *s >= '0' && *s <= '9'; ++s )
		value = value * 10 + (uint64_t)( *s - '0' );
	return (int64_t)( value * (uint64_t)sign );
}

inline int AtoI( const char *s )
{
	return (int)AtoI64( s );
}

//-----------------------------------------------------------------------------
// V_atof. Not strtod: the digits are accumulated in a double one at a time,
// then scaled, in the same order tier1 does it, so the float that comes back
// is the same float. The sign, hex and quote rules are AtoI64's. Decimal
// input takes at most one '.', and an 'e' or 'E' after the digits reads its
// exponent with AtoI rules (so "1e+2" and "1e-2" work and "1e" is 1).
//-----------------------------------------------------------------------------
inline float AtoF( const char *s )
{
	int sign = 1;
	if ( *s == '-' || *s == '+' )
	{
		sign = ( *s == '-' ) ? -1 : 1;
		++s;
	}

	double value = 0;
	if ( s[0] == '0' && ( s[1] == 'x' || s[1] == 'X' ) )
	{
		for ( s += 2;; ++s )
		{
			const int c = *s;
			if ( c >= '0' && c <= '9' )
				value = ( value * 16 ) + c - '0';
			else if ( c >= 'a' && c <= 'f' )
				value = ( value * 16 ) + c - 'a' + 10;
			else if ( c >= 'A' && c <= 'F' )
				value = ( value * 16 ) + c - 'A' + 10;
			else
				return (float)( value * sign );
		}
	}

	if ( s[0] == '\'' )
		return (float)( sign * s[1] );

	int digits = 0;			// digits read, before and after the point
	int pointAt = -1;		// how many had been read when the '.' came
	int exponent = 0;
	for ( ;; )
	{
		const int c = *s++;
		if ( c == '.' )
		{
			if ( pointAt != -1 )
				break;
			pointAt = digits;
			continue;
		}
		if ( c < '0' || c > '9' )
		{
			if ( c == 'e' || c == 'E' )
				exponent = AtoI( s );
			break;
		}
		value = value * 10 + c - '0';
		++digits;
	}

	if ( exponent != 0 )
		value *= std::pow( 10.0, exponent );
	if ( pointAt != -1 )
	{
		for ( ; digits > pointAt; --digits )
			value /= 10;
	}
	return (float)( value * sign );
}

//-----------------------------------------------------------------------------
// V_strlower, in place; ASCII letters only.
//-----------------------------------------------------------------------------
inline char *StrLower( char *s )
{
	for ( char *p = s; *p; ++p )
		*p = (char)AsciiLower( (unsigned char)*p );
	return s;
}

//-----------------------------------------------------------------------------
// nexttoken. Copies str up to (not including) the first sep, or its end, into
// token, stopping early when token (tokenSize bytes) is full, and terminates
// it. Answers where to continue: past the separator, or at the terminator
// when str ran out. A token cut short by the buffer answers one character
// PAST where the copy stopped, dropping that character, as tier1 does. A
// null or empty str empties token and answers NULL; tokenSize 0 answers NULL
// and writes nothing.
//-----------------------------------------------------------------------------
inline const char *NextToken( char *token, size_t tokenSize, const char *str, char sep )
{
	if ( tokenSize < 1 )
		return nullptr;
	if ( !str || !*str )
	{
		*token = '\0';
		return nullptr;
	}
	char *const last = token + tokenSize - 1;
	for ( ; *str && *str != sep && token < last; ++str )
		*token++ = *str;
	*token = '\0';
	return *str ? str + 1 : str;
}

template <size_t N>
inline const char *NextToken( char ( &token )[N], const char *str, char sep )
{
	return NextToken( token, N, str, sep );
}

//-----------------------------------------------------------------------------
// V_FixSlashes: every '/' and '\\' becomes sep ('/' by default, the POSIX
// separator tier1 defaults to there).
//-----------------------------------------------------------------------------
inline void FixSlashes( char *path, char sep = '/' )
{
	for ( ; *path; ++path )
	{
		if ( IsPathSeparator( *path ) )
			*path = sep;
	}
}

//-----------------------------------------------------------------------------
// V_StripExtension. out gets in without its extension: everything before the
// last '.' that comes after the last separator. A '.' at position 0 is not an
// extension (".cfg" stays ".cfg"), and neither is a dot in a directory name
// ("a.b/c" is copied whole). When the dot is at or beyond outSize, or there
// is none, in is copied as StrCopy would. out may be in.
//-----------------------------------------------------------------------------
inline void StripExtension( const char *in, char *out, int outSize )
{
	int dot = (int)std::strlen( in ) - 1;
	while ( dot > 0 && in[dot] != '.' && !IsPathSeparator( in[dot] ) )
		--dot;

	if ( dot > 0 && !IsPathSeparator( in[dot] ) && dot < outSize )
	{
		const int keep = dot < outSize - 1 ? dot : outSize - 1;
		if ( out != in )
			std::memcpy( out, in, (size_t)keep );
		out[keep] = '\0';
	}
	else if ( out != in )
	{
		StrCopy( out, in, outSize );
	}
}

//-----------------------------------------------------------------------------
// V_FileBase: the file name with neither its directory nor its extension,
// truncated to fit outSize. The extension is the part from the last '.'
// after the last separator; a name that starts with its only dot (".cfg")
// has an empty base. An empty or null in gives "".
//-----------------------------------------------------------------------------
inline void FileBase( const char *in, char *out, int outSize )
{
	if ( !in || !in[0] )
	{
		*out = '\0';
		return;
	}

	const int len = (int)std::strlen( in );

	int last = len - 1;		// last character of the base
	while ( last > 0 && in[last] != '.' && !IsPathSeparator( in[last] ) )
		--last;
	if ( in[last] == '.' )
		--last;
	else
		last = len - 1;

	int first = len - 1;	// first character of the base
	while ( first >= 0 && !IsPathSeparator( in[first] ) )
		--first;
	++first;

	const int baseLen = last - first + 1;
	StrCopy( out, in + first, baseLen + 1 < outSize ? baseLen + 1 : outSize );
}

//-----------------------------------------------------------------------------
// V_StripFilename, in place: cuts at the last separator, removing it too.
// The search never looks at position 0, so a path with no separator after
// its first character is emptied ("abc" and "/abc" both become ""), and a
// string of one character is left as it is.
//-----------------------------------------------------------------------------
inline void StripFilename( char *path )
{
	int at = (int)std::strlen( path ) - 1;
	if ( at <= 0 )
		return;
	while ( at > 0 && !IsPathSeparator( path[at] ) )
		--at;
	path[at] = '\0';
}

//-----------------------------------------------------------------------------
// V_StripLastDir, in place: drops the last directory, keeping the separator
// before it, and turns every separator into '/'. "a/b/" and "a/b" both become
// "a/". A path with one component ("tf2/", "tf2") becomes "./" and still
// answers true. An empty path, "./" and ".\\" (any case) answer false and are
// left alone. maxLen is the buffer's size, used only for that "./".
//-----------------------------------------------------------------------------
inline bool StripLastDir( char *dir, int maxLen )
{
	if ( !dir[0] || !StrICmp( dir, "./" ) || !StrICmp( dir, ".\\" ) )
		return false;

	int len = (int)std::strlen( dir );
	if ( IsPathSeparator( dir[len - 1] ) )
		--len;

	for ( ; len > 0; --len )
	{
		if ( IsPathSeparator( dir[len - 1] ) )
		{
			dir[len] = '\0';
			FixSlashes( dir, '/' );
			return true;
		}
	}

	Format( dir, maxLen, "./" );
	return true;
}

//-----------------------------------------------------------------------------
// V_GetFileName (V_UnqualifiedFileName): a pointer into path just after its
// last separator, or path itself when it has none. A separator that is the
// LAST character does not count, so "dir/" answers "dir/" and "a/b/" answers
// "b/". (An empty path is undefined in tier1, which answers path-1; here it
// answers path.)
//-----------------------------------------------------------------------------
inline const char *GetFileName( const char *path )
{
	const size_t len = std::strlen( path );
	if ( len == 0 )
		return path;
	const char *p = path + len - 1;
	while ( p > path && !IsPathSeparator( p[-1] ) )
		--p;
	return p;
}

//-----------------------------------------------------------------------------
// THE CLIENT'S SET (plan_client_novalve C9, tier1): the rest of the strtools
// names client.so's objects -- and the tier1 members that stay on its link for
// particles.a and dmxloader.a -- still call, so strtools.cpp leaves the
// client's link too. strtools_client.cpp spells the V_ names over these.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// V_strncmp: at most n characters, bytes compared unsigned, answering -1, 0
// or 1 (not the difference strncmp may answer).
//-----------------------------------------------------------------------------
inline int StrNCmp( const char *a, const char *b, int n )
{
	for ( ; n > 0; --n, ++a, ++b )
	{
		const unsigned char ca = (unsigned char)*a, cb = (unsigned char)*b;
		if ( ca != cb )
			return ca < cb ? -1 : 1;
		if ( !ca )
			break;
	}
	return 0;
}

//-----------------------------------------------------------------------------
// V_strnistr: StrIStr over the first n characters of haystack only -- the
// match must start AND end within them. An empty needle finds nothing.
//-----------------------------------------------------------------------------
inline const char *StrNIStr( const char *haystack, const char *needle, int n )
{
	if ( !haystack || !needle )
		return nullptr;
	for ( const char *start = haystack; *start; ++start, --n )
	{
		if ( n <= 0 )
			return nullptr;
		if ( AsciiLower( (unsigned char)*start ) != AsciiLower( (unsigned char)*needle ) )
			continue;
		int left = n - 1;
		const char *h = start + 1, *t = needle + 1;
		for ( ; *t; ++h, ++t, --left )
		{
			if ( left <= 0 || !*h )
				return nullptr;
			if ( AsciiLower( (unsigned char)*h ) != AsciiLower( (unsigned char)*t ) )
				break;
		}
		if ( !*t )
			return start;
	}
	return nullptr;
}

//-----------------------------------------------------------------------------
// V_strnchr: the first c within the first n characters (stopping at the end
// of the string), or null.
//-----------------------------------------------------------------------------
inline const char *StrNChr( const char *s, char c, int n )
{
	for ( const char *end = s + n; s < end && *s; ++s )
	{
		if ( *s == c )
			return s;
	}
	return nullptr;
}

//-----------------------------------------------------------------------------
// V_strupr: ASCII letters only, in place; answers s.
//-----------------------------------------------------------------------------
inline char *StrUpper( char *s )
{
	for ( char *p = s; *p; ++p )
	{
		if ( *p >= 'a' && *p <= 'z' )
			*p = (char)( *p - ( 'a' - 'A' ) );
	}
	return s;
}

//-----------------------------------------------------------------------------
// V_StrTrim: removes leading and trailing white space (the C locale's six) in
// place and answers the new length.
//-----------------------------------------------------------------------------
inline int StrTrim( char *s )
{
	auto space = []( char c ) { return c == ' ' || ( c >= '\t' && c <= '\r' ); };
	const char *from = s;
	while ( *from && space( *from ) )
		++from;
	int len = 0, kept = 0;	// kept: the length up to the last non-space
	for ( ; *from; ++from )
	{
		s[len++] = *from;
		if ( !space( *from ) )
			kept = len;
	}
	s[len] = '\0';		// the copy is terminated, then cut at the trailing space
	s[kept] = '\0';
	return kept;
}

//-----------------------------------------------------------------------------
// V_binarytohex: two lower-case hex digits a byte, appended as StrAppend
// would, so a short buffer keeps what fits. out is emptied first.
//-----------------------------------------------------------------------------
inline void BinaryToHex( const unsigned char *in, int inBytes, char *out, int outSize )
{
	out[0] = '\0';
	for ( int i = 0; i < inBytes; ++i )
	{
		char pair[3];
		Format( pair, sizeof( pair ), "%02x", in[i] );
		StrAppend( out, pair, (size_t)outSize );
	}
}

//-----------------------------------------------------------------------------
// V_GetFileExtension: the text after the last '.', or null when there is no
// dot, the only dot is the first character, or the dot is followed by a
// separator. Like tier1 it does not stop at separators while looking, so
// "a.b/c" answers "b/c".
//-----------------------------------------------------------------------------
inline const char *GetFileExtension( const char *path )
{
	const size_t len = std::strlen( path );
	if ( len == 0 )
		return nullptr;
	const char *p = path + len - 1;
	while ( p != path && p[-1] != '.' )
		--p;
	if ( p == path || IsPathSeparator( *p ) )
		return nullptr;
	return p;
}

//-----------------------------------------------------------------------------
// V_ExtractFileExtension: GetFileExtension copied into dest, or "".
//-----------------------------------------------------------------------------
inline void ExtractFileExtension( const char *path, char *dest, int destSize )
{
	*dest = '\0';
	if ( const char *ext = GetFileExtension( path ) )
		StrCopy( dest, ext, destSize );
}

//-----------------------------------------------------------------------------
// V_SetExtension: the extension replaced (or added), a '.' supplied when
// extension lacks one, within pathSize.
//-----------------------------------------------------------------------------
inline void SetExtension( char *path, const char *extension, int pathSize )
{
	StripExtension( path, path, pathSize );
	if ( extension[0] != '.' )
		StrAppend( path, ".", (size_t)pathSize );
	StrAppend( path, extension, (size_t)pathSize );
}

//-----------------------------------------------------------------------------
// V_IsAbsolutePath: a drive letter's colon in second place, or a leading
// separator.
//-----------------------------------------------------------------------------
inline bool IsAbsolutePath( const char *path )
{
	return ( path[0] && path[1] == ':' ) || IsPathSeparator( path[0] );
}

//-----------------------------------------------------------------------------
// V_StripTrailingSlash: one trailing separator removed, in place.
//-----------------------------------------------------------------------------
inline void StripTrailingSlash( char *path )
{
	const size_t len = std::strlen( path );
	if ( len && IsPathSeparator( path[len - 1] ) )
		path[len - 1] = '\0';
}

//-----------------------------------------------------------------------------
// V_ExtractFilePath: everything up to and including the last separator,
// within destSize; answers whether anything was copied. A destSize below 1
// answers false and writes nothing.
//-----------------------------------------------------------------------------
inline bool ExtractFilePath( const char *path, char *dest, int destSize )
{
	if ( destSize < 1 )
		return false;
	const size_t len = std::strlen( path );
	const char *p = path + ( len ? len - 1 : 0 );
	while ( p != path && !IsPathSeparator( p[-1] ) )
		--p;
	int n = (int)( p - path );
	if ( n > destSize - 1 )
		n = destSize - 1;
	std::memcpy( dest, path, (size_t)n );
	dest[n] = '\0';
	return n != 0;
}

//-----------------------------------------------------------------------------
// V_AppendSlash: a '/' added when the string is non-empty and does not end in
// a separator. tier1 calls Error when it would not fit; this answers false
// and writes nothing (the V_ name's caller decides).
//-----------------------------------------------------------------------------
inline bool AppendSlash( char *s, int size )
{
	const int len = (int)std::strlen( s );
	if ( len == 0 || IsPathSeparator( s[len - 1] ) )
		return true;
	if ( len + 1 >= size )
		return false;
	s[len] = '/';
	s[len + 1] = '\0';
	return true;
}

//-----------------------------------------------------------------------------
// V_ComposeFileName: path, a separator, then filename, within destSize, with
// every separator '/'. False only where AppendSlash would have failed.
//-----------------------------------------------------------------------------
inline bool ComposeFileName( const char *path, const char *filename, char *dest, int destSize )
{
	StrCopy( dest, path, destSize );
	FixSlashes( dest );
	if ( !AppendSlash( dest, destSize ) )
		return false;
	StrAppend( dest, filename, (size_t)destSize );
	FixSlashes( dest );
	return true;
}

//-----------------------------------------------------------------------------
// V_RemoveDotSlashes, in place, with sep as the separator (every separator
// becomes sep first). In order: doubled separators collapse (the first
// character is left alone, for UNC paths); "<sep>.<sep>" becomes "<sep>"; a
// leading "./" goes and any other "./" not preceded by '.' loses its '.'; a
// trailing "<sep>." goes; a leading "./" goes again; then each ".." component
// removes itself and the directory before it. Answers false, leaving the
// string part-way, when a ".." has no directory to remove.
//-----------------------------------------------------------------------------
inline bool RemoveDotSlashes( char *s, char sep = '/' )
{
	FixSlashes( s, sep );
	int len = (int)std::strlen( s );

	// Remove s[at] by sliding the rest (and the terminator) down one.
	auto removeAt = [&]( int at, int count )
	{
		std::memmove( s + at, s + at + count, (size_t)( len - at - count + 1 ) );
		len -= count;
	};

	for ( int i = 1; i + 1 < len; )
	{
		if ( s[i] == sep && s[i + 1] == sep )
			removeAt( i, 1 );
		else
			++i;
	}

	for ( int i = 0; i + 2 < len; )
	{
		if ( s[i] == sep && s[i + 1] == '.' && s[i + 2] == sep )
			removeAt( i, 2 );
		else
			++i;
	}

	for ( int i = 0; i + 1 < len; ++i )
	{
		if ( i == 0 )
		{
			if ( s[0] == '.' && s[1] == sep )
			{
				removeAt( 0, 2 );
				--i;	// look at the new first character again
			}
		}
		else if ( s[i - 1] != '.' && s[i] == '.' && s[i + 1] == sep )
		{
			removeAt( i, 1 );
		}
	}

	if ( len > 2 && s[len - 1] == '.' && s[len - 2] == sep )
	{
		s[len - 2] = '\0';
		len -= 2;
	}

	if ( len > 1 && s[0] == '.' && s[1] == sep )
		removeAt( 0, 2 );

	for ( char *p = s; *p; )
	{
		const bool dotdot = p[0] == '.' && p[1] == '.' && ( p == s || p[-1] == sep ) &&
		                    ( p[2] == '\0' || p[2] == sep );
		if ( !dotdot )
		{
			++p;
			continue;
		}
		if ( p == s || p - 1 == s )
			return false;

		char *from = p + 2;
		char *to = p - 2;
		while ( to > s && *to != sep )
			--to;
		if ( to == s && *from )
			++from;
		std::memmove( to, from, std::strlen( from ) + 1 );
		p = s;
	}
	return true;
}

//-----------------------------------------------------------------------------
// V_MakeAbsolutePath: path as it is when absolute, else joined onto
// startDir (when that is absolute) or onto the working directory (with
// startDir, if given, between); then RemoveDotSlashes. tier1 calls Error
// where this answers false: getcwd failing, a separator that would not fit,
// or a ".." past the root.
//-----------------------------------------------------------------------------
inline bool MakeAbsolutePath( char *out, int outLen, const char *path, const char *startDir )
{
	if ( IsAbsolutePath( path ) )
	{
		StrCopy( out, path, outLen );
	}
	else
	{
		if ( startDir && IsAbsolutePath( startDir ) )
		{
			StrCopy( out, startDir, outLen );
		}
		else
		{
			if ( !SS_GetCwd( out, (size_t)outLen ) )
				return false;
			if ( startDir )
			{
				if ( !AppendSlash( out, outLen ) )
					return false;
				StrAppend( out, startDir, (size_t)outLen );
			}
		}
		if ( !AppendSlash( out, outLen ) )
			return false;
		StrAppend( out, path, (size_t)outLen );
	}
	return RemoveDotSlashes( out );
}

//-----------------------------------------------------------------------------
// The UTF-8 side of strtools_unicode.cpp's conversions under their default
// policy, REPLACE: one encoder over a per-encoding decoder, as Valve's
// Q_UnicodeConvertT is. A decoder reads the code point at src (an invalid one
// as '?') and answers how many units it took. The encoder writes each code
// point as UTF-8 into dst, stopping before a character that would not fit
// with the NUL. Answers the bytes written plus the NUL; 0 for no input or no
// room. With no dst, the bytes the whole string needs, NUL included.
//-----------------------------------------------------------------------------
namespace utf8detail
{
	// Q_IsValidUChar32: in range, not a surrogate half, not a U+xxFFFE/xxFFFF,
	// not U+FDD0..FDEF.
	inline bool ValidUChar32( uint32_t u )
	{
		return u < 0x110000u && ( u - 0xD800u ) > 0x7FFu && ( u & 0xFFFFu ) < 0xFFFEu && ( u - 0xFDD0u ) > 0x1Fu;
	}

	template < typename Unit, typename Decode >
	inline int ToUTF8( const Unit *src, char *dst, int dstSize, Decode decode )
	{
		if ( !src )
			return 0;
		auto length = []( uint32_t u ) { return u <= 0x7F ? 1 : u <= 0x7FF ? 2 : u <= 0xFFFF ? 3 : 4; };
		int nOut = 0;
		uint32_t u;
		if ( !dst )
		{
			while ( *src )
			{
				src += decode( src, u );
				nOut += length( u );
			}
			return nOut + 1;
		}
		if ( dstSize <= 0 )
			return 0;
		const int nMaxOut = dstSize - 1;
		while ( *src )
		{
			src += decode( src, u );
			const int n = length( u );
			if ( nOut + n > nMaxOut )
				break;
			unsigned char *p = (unsigned char *)dst + nOut;
			if ( n == 1 )
				p[0] = (unsigned char)u;
			else if ( n == 2 )
			{
				p[0] = (unsigned char)( u >> 6 ) | 0xC0;
				p[1] = (unsigned char)( u & 0x3F ) | 0x80;
			}
			else if ( n == 3 )
			{
				p[0] = (unsigned char)( u >> 12 ) | 0xE0;
				p[1] = (unsigned char)( ( u >> 6 ) & 0x3F ) | 0x80;
				p[2] = (unsigned char)( u & 0x3F ) | 0x80;
			}
			else
			{
				p[0] = (unsigned char)( ( u >> 18 ) & 0x07 ) | 0xF0;
				p[1] = (unsigned char)( ( u >> 12 ) & 0x3F ) | 0x80;
				p[2] = (unsigned char)( ( u >> 6 ) & 0x3F ) | 0x80;
				p[3] = (unsigned char)( u & 0x3F ) | 0x80;
			}
			nOut += n;
		}
		dst[nOut] = '\0';
		return nOut + 1;
	}
}

// Q_UTF32ToUTF8: one unit per code point.
inline int UTF32ToUTF8( const uint32_t *src, char *dst, int dstSize )
{
	return utf8detail::ToUTF8( src, dst, dstSize, []( const uint32_t *s, uint32_t &u ) {
		u = utf8detail::ValidUChar32( *s ) ? *s : '?';
		return 1;
	} );
}

// Q_UTF16ToUTF8 (Q_UTF16ToUChar32's decode): a valid unit is itself; a high
// surrogate followed by a low one is the pair's code point, '?' when that is
// not valid, two units either way; anything else (an unpaired surrogate) is
// '?' for one unit.
inline int UTF16ToUTF8( const uint16_t *src, char *dst, int dstSize )
{
	return utf8detail::ToUTF8( src, dst, dstSize, []( const uint16_t *s, uint32_t &u ) {
		if ( utf8detail::ValidUChar32( s[0] ) )
		{
			u = s[0];
			return 1;
		}
		if ( ( s[0] - 0xD800u ) < 0x400u && ( s[1] - 0xDC00u ) < 0x400u )
		{
			const uint32_t v = 0x010000u + ( ( s[0] - 0xD800u ) << 10 ) + ( s[1] - 0xDC00u );
			u = utf8detail::ValidUChar32( v ) ? v : '?';
			return 2;
		}
		u = '?';
		return 1;
	} );
}

// A wchar_t string as UTF-8, the way the platform's ILocalize does it: wchar_t
// is UTF-32 on Linux and macOS (Q_UTF32ToUTF8) and UTF-16 on Windows
// (Q_UTF16ToUTF8).
inline int WideToUTF8( const wchar_t *src, char *dst, int dstSize )
{
	if constexpr ( sizeof( wchar_t ) == sizeof( uint32_t ) )
		return UTF32ToUTF8( (const uint32_t *)src, dst, dstSize );
	else
		return UTF16ToUTF8( (const uint16_t *)src, dst, dstSize );
}

} // namespace ss

#undef SS_STRING_PRINTF
