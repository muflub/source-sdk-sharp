//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The tier1 string routines with no exact standard-library counterpart, restated with
// tier1's behaviour so native code need not link tier1. Case folding is ASCII only and
// never consults the locale. Where tier1's behaviour is undefined these write nothing.
// No engine headers, so a unit test can include it bare.
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

// One character, ASCII only. A byte outside 'A'..'Z' (including every byte of
// 0x80 and above) comes back unchanged.
inline unsigned char AsciiLower( unsigned char c )
{
	return ( c >= 'A' && c <= 'Z' ) ? (unsigned char)( c + ( 'a' - 'A' ) ) : c;
}

inline bool IsPathSeparator( char c )
{
	return c == '/' || c == '\\';
}

// V_strncpy: always terminates, and zero-pads the rest like strncpy. dstSize <= 0
// writes nothing.
inline void StrCopy( char *dst, const char *src, int dstSize )
{
	if ( dstSize <= 0 )
		return;
	// Said directly: gcc's -Wstringop-* cannot see the terminator is written.
	const size_t n = strnlen( src, (size_t)dstSize - 1 );
	std::memcpy( dst, src, n );
	std::memset( dst + n, 0, (size_t)dstSize - n );
}

// V_strncat. dstSize is the whole buffer's size, not the room left; maxChars caps how
// much of src is taken (kCopyAll or below takes all). Nothing is appended when full.
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

// V_vsnprintf / V_snprintf: the characters written, or dstSize when cut short or on
// error; always terminated. dstSize <= 0 writes nothing.
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

// V_strnicmp over at most n characters: -1, 0 or 1 by the first differing folded byte,
// unsigned. n <= 0 answers 0.
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

// V_stricmp: StrNICmp without the bound.
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

// V_stristr, ignoring ASCII case. An empty needle is never found; NULL on either side
// answers NULL.
inline const char *StrIStr( const char *haystack, const char *needle )
{
	if ( !haystack || !needle )
		return nullptr;
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

// V_atoi64 / V_atoi: an optional sign, then "0x" hex, a quoted character, or decimal
// digits, stopping at the first other character. AtoI keeps the low 32 bits.
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

// V_atof. Not strtod: digits are accumulated and scaled in tier1's order so the result
// is the same float. Sign, hex and quote rules are AtoI64's; one '.', then e/E exponent.
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

// V_strlower, in place; ASCII letters only.
inline char *StrLower( char *s )
{
	for ( char *p = s; *p; ++p )
		*p = (char)AsciiLower( (unsigned char)*p );
	return s;
}

// nexttoken: copies str up to the first sep into token and answers where to continue.
// A token cut short by the buffer drops one character, as tier1 does. A null or empty
// str, or tokenSize 0, answers NULL.
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

// V_FixSlashes: every '/' and '\\' becomes sep.
inline void FixSlashes( char *path, char sep = '/' )
{
	for ( ; *path; ++path )
	{
		if ( IsPathSeparator( *path ) )
			*path = sep;
	}
}

// V_StripExtension: everything before the last '.' after the last separator. A leading
// '.' is not an extension. out may be in.
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

// V_FileBase: the file name without directory or extension, truncated to outSize.
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

// V_StripFilename, in place: cuts at the last separator, never looking at position 0.
inline void StripFilename( char *path )
{
	int at = (int)std::strlen( path ) - 1;
	if ( at <= 0 )
		return;
	while ( at > 0 && !IsPathSeparator( path[at] ) )
		--at;
	path[at] = '\0';
}

// V_StripLastDir, in place: "a/b/" and "a/b" become "a/"; one component becomes "./".
// Separators become '/'. An empty path, "./" and ".\\" answer false.
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

// V_GetFileName: just after the last separator, ignoring a trailing one, so "a/b/"
// answers "b/".
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

// V_strncmp: at most n characters, bytes compared unsigned, answering -1, 0
// or 1 (not the difference strncmp may answer).
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

// V_strnistr: StrIStr over the first n characters of haystack only -- the
// match must start AND end within them. An empty needle finds nothing.
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

// V_strnchr: the first c within the first n characters (stopping at the end
// of the string), or null.
inline const char *StrNChr( const char *s, char c, int n )
{
	for ( const char *end = s + n; s < end && *s; ++s )
	{
		if ( *s == c )
			return s;
	}
	return nullptr;
}

// V_strupr: ASCII letters only, in place; answers s.
inline char *StrUpper( char *s )
{
	for ( char *p = s; *p; ++p )
	{
		if ( *p >= 'a' && *p <= 'z' )
			*p = (char)( *p - ( 'a' - 'A' ) );
	}
	return s;
}

// V_StrTrim: removes leading and trailing white space (the C locale's six) in
// place and answers the new length.
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

// V_binarytohex: two lower-case hex digits a byte, appended as StrAppend
// would, so a short buffer keeps what fits. out is emptied first.
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

// V_GetFileExtension: the text after the last '.', or null when there is no
// dot, the only dot is the first character, or the dot is followed by a
// separator. It does not stop at separators while looking: "a.b/c" answers "b/c".
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

// V_ExtractFileExtension: GetFileExtension copied into dest, or "".
inline void ExtractFileExtension( const char *path, char *dest, int destSize )
{
	*dest = '\0';
	if ( const char *ext = GetFileExtension( path ) )
		StrCopy( dest, ext, destSize );
}

// V_SetExtension: the extension replaced (or added), a '.' supplied when
// extension lacks one, within pathSize.
inline void SetExtension( char *path, const char *extension, int pathSize )
{
	StripExtension( path, path, pathSize );
	if ( extension[0] != '.' )
		StrAppend( path, ".", (size_t)pathSize );
	StrAppend( path, extension, (size_t)pathSize );
}

// V_IsAbsolutePath: a drive letter's colon in second place, or a leading
// separator.
inline bool IsAbsolutePath( const char *path )
{
	return ( path[0] && path[1] == ':' ) || IsPathSeparator( path[0] );
}

// V_StripTrailingSlash: one trailing separator removed, in place.
inline void StripTrailingSlash( char *path )
{
	const size_t len = std::strlen( path );
	if ( len && IsPathSeparator( path[len - 1] ) )
		path[len - 1] = '\0';
}

// V_ExtractFilePath: everything up to and including the last separator,
// within destSize; answers whether anything was copied. A destSize below 1
// answers false and writes nothing.
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

// V_AppendSlash: a '/' added when the string is non-empty and does not end in a
// separator. False, writing nothing, when it would not fit.
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

// V_ComposeFileName: path, a separator, then filename, within destSize, with
// every separator '/'. False only where AppendSlash would have failed.
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

// V_RemoveDotSlashes, in place: every separator becomes sep, doubled separators
// collapse (not at the start, for UNC paths), "." components go, and each ".."
// removes the directory before it. False when a ".." has no directory to remove.
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

// V_MakeAbsolutePath: path when absolute, else joined onto startDir or the working
// directory, then RemoveDotSlashes. False where tier1 would call Error.
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

// UTF-8 conversion with invalid code points replaced by '?'. A decoder answers how many
// units it read; the encoder stops before a character that would not fit with the NUL.
// Answers bytes written plus the NUL; with no dst, the bytes needed.
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
