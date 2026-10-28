//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
// $NoKeywords: $
//
//===========================================================================//

#ifndef TIER1_STRTOOLS_H
#define TIER1_STRTOOLS_H

#include "tier0/platform.h"

#include <ctype.h>
#include <stdarg.h>
#ifdef _WIN32
#pragma once
#elif POSIX
#include <wchar.h>
#include <math.h>
#include <wctype.h>
#endif

#include <string.h>
#include <stdlib.h>

class CUtlBuffer;
class CUtlString;

#ifdef _WIN64

#else

#endif

template< class T, class I > class CUtlMemory;
template< class T, class A > class CUtlVector;


//-----------------------------------------------------------------------------
// Portable versions of standard string functions
//-----------------------------------------------------------------------------
void	_V_memset	( const char* file, int line, void *dest, int fill, int count );
void	_V_memcpy	( const char* file, int line, void *dest, const void *src, int count );
void	_V_memmove	( const char* file, int line, void *dest, const void *src, int count );
int		_V_memcmp	( const char* file, int line, const void *m1, const void *m2, int count );
int		_V_strlen	( const char* file, int line, const char *str );
void	_V_strcpy	( const char* file, int line, char *dest, const char *src );
char*	_V_strrchr	( const char* file, int line, const char *s, char c );
int		_V_strcmp	( const char* file, int line, const char *s1, const char *s2 );
int		_V_wcscmp	( const char* file, int line, const wchar_t *s1, const wchar_t *s2 );
char*	_V_strstr	( const char* file, int line, const char *s1, const char *search );
int		_V_wcslen	( const char* file, int line, const wchar_t *pwch );
wchar_t*	_V_wcslower (const char* file, int line, wchar_t *start);
wchar_t*	_V_wcsupr (const char* file, int line, wchar_t *start);

// ASCII-optimized functions which fall back to CRT only when necessary
char *V_strupr( char *start );
char *V_strtitlecase( char *start );
char *V_strlower( char *start );
int V_stricmp( const char *s1, const char *s2 );
int	V_strncmp( const char *s1, const char *s2, int count );
int V_strnicmp( const char *s1, const char *s2, int n );






// ASCII-only, if you want a full language-aware compare use V_UnicodeCaseCompare.
int V_stricmp_fast( const char *s1, const char *s2 );

#define V_stristr_fast V_stristr
const char* V_stristr_fast( const char* pStr, const char* pSearch );

//-----------------------------------------------------------------------------
// Purpose: Slightly modified strtok. Does not modify the input string. Does
//			not skip over more than one separator at a time. This allows parsing
//			strings where tokens between separators may or may not be present:
//
//			Door01,,,0 would be parsed as "Door01"  ""  ""  "0"
//			Door01,Open,,0 would be parsed as "Door01"  "Open"  ""  "0"
//
// Input  : token - Returns with a token, or zero length if the token was missing.
//			str - String to parse.
//			sep - Character to use as separator. UNDONE: allow multiple separator chars
// Output : Returns a pointer to the next token to be parsed.
//-----------------------------------------------------------------------------
const char *nexttoken(char *token, size_t nMaxTokenLen, const char *str, char sep);


#ifdef POSIX



inline wchar_t *_wcslwr( wchar_t *start )
{
	wchar_t *str = start;
	while( str && *str )
	{
		*str = (wchar_t)towlower(static_cast<wint_t>(*str));
		str++;
	}
	return start;
};

inline wchar_t *_wcsupr( wchar_t *start )
{
	wchar_t *str = start;
	while( str && *str )
	{
		*str = (wchar_t)towupper(static_cast<wint_t>(*str));
		str++;
	}
	return start;
};

#endif // POSIX


#ifdef _DEBUG

#define V_memset(dest, fill, count)		_V_memset   (__FILE__, __LINE__, (dest), (fill), (count))	
#define V_memcpy(dest, src, count)		_V_memcpy	(__FILE__, __LINE__, (dest), (src), (count))	
#define V_memmove(dest, src, count)		_V_memmove	(__FILE__, __LINE__, (dest), (src), (count))	
#define V_memcmp(m1, m2, count)			_V_memcmp	(__FILE__, __LINE__, (m1), (m2), (count))		
#define V_strlen(str)					_V_strlen	(__FILE__, __LINE__, (str))				
#define V_strcpy(dest, src)				_V_strcpy	(__FILE__, __LINE__, (dest), (src))			
#define V_strrchr(s, c)					_V_strrchr	(__FILE__, __LINE__, (s), (c))				
#define V_strcmp(s1, s2)				_V_strcmp	(__FILE__, __LINE__, (s1), (s2))			
#define V_wcscmp(s1, s2)				_V_wcscmp	(__FILE__, __LINE__, (s1), (s2))			
#define V_strstr(s1, search )			_V_strstr	(__FILE__, __LINE__, (s1), (search) )		
#define V_wcslen(pwch)					_V_wcslen	(__FILE__, __LINE__, (pwch))		
#define V_wcslower(start)				_V_wcslower (__FILE__, __LINE__, (start))		
#define V_wcsupr(start)					_V_wcsupr	(__FILE__, __LINE__, (start))				

#else


inline void		V_memset (void *dest, int fill, int count)			{ memset( dest, fill, count ); }
inline void		V_memcpy (void *dest, const void *src, int count)	{ memcpy( dest, src, count ); }
inline void		V_memmove (void *dest, const void *src, int count)	{ memmove( dest, src, count ); }
inline int		V_memcmp (const void *m1, const void *m2, int count){ return memcmp( m1, m2, count ); } 
inline int		V_strlen (const char *str)							{ return (int) strlen ( str ); }
inline void		V_strcpy (char *dest, const char *src)				{ strcpy( dest, src ); }
inline int		V_wcslen(const wchar_t *pwch)						{ return (int)wcslen(pwch); }
inline char*	V_strrchr (const char *s, char c)					{ return (char*)strrchr( s, c ); }
inline int		V_strcmp (const char *s1, const char *s2)			{ return strcmp( s1, s2 ); }
inline int		V_wcscmp (const wchar_t *s1, const wchar_t *s2)		{ return wcscmp( s1, s2 ); }
inline char*	V_strstr( const char *s1, const char *search )		{ return (char*)strstr( s1, search ); }

#endif

int			V_atoi (const char *str);
int64 		V_atoi64(const char *str);
uint64 		V_atoui64(const char *str);
int64		V_strtoi64( const char *nptr, char **endptr, int base );
uint64		V_strtoui64( const char *nptr, char **endptr, int base );
float		V_atof(const char *str);
char*		V_stristr( char* pStr, const char* pSearch );
const char*	V_stristr( const char* pStr, const char* pSearch );
const char*	V_strnistr( const char* pStr, const char* pSearch, int n );
const char*	V_strnchr( const char* pStr, char c, int n );
inline int V_strcasecmp (const char *s1, const char *s2) { return V_stricmp(s1, s2); }
inline int V_strncasecmp (const char *s1, const char *s2, int n) { return V_strnicmp(s1, s2, n); }
void		V_qsort_s( void *base, size_t num, size_t width, int ( __cdecl *compare )(void *, const void *,
const void *), void *context );

inline const char* V_strchr( const char *s, char c )
{
#if defined( USE_TIER0_STRTOOLS_FUNCTIONS )
    return V_tier0_strchr( s, c );
#else
    return ::strchr( s, c );
#endif
}
inline char *V_strchr( char *s, char c )
{
#if defined( USE_TIER0_STRTOOLS_FUNCTIONS )
    return (char*)V_tier0_strchr( s, c );
#else
    return (char*)::strchr( s, c );
#endif
}


// returns string immediately following prefix, (ie str+strlen(prefix)) or NULL if prefix not found
const char *StringAfterPrefix             ( const char *str, const char *prefix );
const char *StringAfterPrefixCaseSensitive( const char *str, const char *prefix );
inline bool	StringHasPrefix             ( const char *str, const char *prefix ) { return StringAfterPrefix             ( str, prefix ) != NULL; }


template< bool CASE_SENSITIVE > inline bool _V_strEndsWithInner( const char *pStr, const char *pSuffix )
{
	int nSuffixLen = V_strlen( pSuffix );
	int nStringLen = V_strlen( pStr );
	if ( nSuffixLen == 0 )
		return true; // All strings end with the empty string (matches Java & .NET behaviour)
	if ( nStringLen < nSuffixLen )
		return false;
	pStr += nStringLen - nSuffixLen;
	if ( CASE_SENSITIVE )
		return !V_strcmp(  pStr, pSuffix );
	else
		return !V_stricmp( pStr, pSuffix );
}



// Normalizes a float string in place.  
// (removes leading zeros, trailing zeros after the decimal point, and the decimal point itself where possible)
void			V_normalizeFloatString( char* pFloat );




//#undef isalpha
//#define isalpha use_V_isalpha_instead_of_isalpha
inline bool V_isalnum(char c) { return isalnum( (unsigned char)c ) != 0; }
//#undef isalnum
//#define isalnum use_V_isalnum_instead_of_isalnum
inline bool V_isprint(char c) { return isprint( (unsigned char)c ) != 0; }
//#undef islower
//#define islower use_V_islower_instead_of_islower
inline bool V_iscntrl(char c) { return iscntrl( (unsigned char)c ) != 0; }
//#undef iscntrl
//#define iscntrl use_V_iscntrl_instead_of_iscntrl
inline bool V_isspace(char c) { return isspace( (unsigned char)c ) != 0; }
//#undef isspace
//#define isspace use_V_isspace_instead_of_isspace







// These are versions of functions that guarantee NULL termination.
//
// maxLen is the maximum number of bytes in the destination string.
// pDest[maxLen-1] is always NULL terminated if pSrc's length is >= maxLen.
//
// This means the last parameter can usually be a sizeof() of a string.
void V_strncpy( OUT_Z_CAP(maxLenInChars) char *pDest, const char *pSrc, int maxLenInChars );


// A function which duplicates a string using new[] to allocate the new string.
inline char *V_strdup( const char *pSrc )
{
	int nLen = V_strlen( pSrc );
	char *pResult = new char [ nLen+1 ];
	V_memcpy( pResult, pSrc, nLen+1 );
	return pResult;
}

void V_wcsncpy( OUT_Z_BYTECAP(maxLenInBytes) wchar_t *pDest, wchar_t const *pSrc, int maxLenInBytes );
template <size_t maxLenInChars> void V_wcscpy_safe( OUT_Z_ARRAY wchar_t (&pDest)[maxLenInChars], wchar_t const *pSrc ) 
{ 
	V_wcsncpy( pDest, pSrc, maxLenInChars * sizeof(*pDest) ); 
}

#define COPY_ALL_CHARACTERS -1
char *V_strncat( INOUT_Z_CAP(cchDest) char *pDest, const char *pSrc, size_t cchDest, int max_chars_to_copy=COPY_ALL_CHARACTERS );

wchar_t *V_wcsncat( INOUT_Z_CAP(cchDest) wchar_t *pDest, const wchar_t *pSrc, size_t cchDest, int nMaxCharsToCopy=COPY_ALL_CHARACTERS );
template <size_t cchDest> wchar_t *V_wcscat_safe( INOUT_Z_ARRAY wchar_t (&pDest)[cchDest], const wchar_t *pSrc, int nMaxCharsToCopy=COPY_ALL_CHARACTERS )
{ 
	return V_wcsncat( pDest, pSrc, (int)cchDest, nMaxCharsToCopy ); 
}

char *V_strnlwr( INOUT_Z_CAP(cchBuf) char *pBuf, size_t cchBuf);

// Unicode string conversion policies - what to do if an illegal sequence is encountered
enum EStringConvertErrorPolicy
{
	_STRINGCONVERTFLAG_SKIP =		1,
	_STRINGCONVERTFLAG_FAIL =		2,
	_STRINGCONVERTFLAG_ASSERT =		4,

	STRINGCONVERT_REPLACE =			0,
	STRINGCONVERT_SKIP =			_STRINGCONVERTFLAG_SKIP,
	STRINGCONVERT_FAIL =			_STRINGCONVERTFLAG_FAIL,

	STRINGCONVERT_ASSERT_REPLACE =	_STRINGCONVERTFLAG_ASSERT + STRINGCONVERT_REPLACE,
	STRINGCONVERT_ASSERT_SKIP =		_STRINGCONVERTFLAG_ASSERT + STRINGCONVERT_SKIP,
	STRINGCONVERT_ASSERT_FAIL =		_STRINGCONVERTFLAG_ASSERT + STRINGCONVERT_FAIL,
}; 

// Unicode (UTF-8, UTF-16, UTF-32) fundamental conversion functions.
bool Q_IsValidUChar32( uchar32 uValue );
int Q_UChar32ToUTF8Len( uchar32 uValue );
int Q_UChar32ToUTF8( uchar32 uValue, char *pOut );
int Q_UChar32ToUTF16Len( uchar32 uValue );
int Q_UChar32ToUTF16( uchar32 uValue, uchar16 *pOut );

// Validate that a Unicode string is well-formed and contains only valid code points
bool Q_UnicodeValidate( const char *pUTF8 );
bool Q_UnicodeValidate( const uchar16 *pUTF16 );
bool Q_UnicodeValidate( const uchar32 *pUTF32 );

// Returns length of string in Unicode code points (printed glyphs or non-printing characters)
int Q_UnicodeLength( const char *pUTF8 );
int Q_UnicodeLength( const uchar16 *pUTF16 );
int Q_UnicodeLength( const uchar32 *pUTF32 );



// Repair invalid Unicode strings by dropping truncated characters and fixing improperly-double-encoded UTF-16 sequences.
// Unlike conversion functions which replace with '?' by default, a repair operation assumes that you know that something
// is wrong with the string (eg, mid-sequence truncation) and you just want to do the best possible job of cleaning it up.
// You can pass a REPLACE or FAIL policy if you would prefer to replace characters with '?' or clear the entire string.
// Returns nonzero on success, or 0 if the policy is FAIL and an invalid sequence was found.
int Q_UnicodeRepair( char *pUTF8, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_SKIP );
int Q_UnicodeRepair( uchar16 *pUTF16, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_SKIP );
int Q_UnicodeRepair( uchar32 *pUTF32, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_SKIP );

// Advance pointer forward by N Unicode code points (printed glyphs or non-printing characters), stopping at terminating null if encountered.
char *Q_UnicodeAdvance( char *pUTF8, int nCharacters );
uchar16 *Q_UnicodeAdvance( uchar16 *pUTF16, int nCharactersnCharacters );
uchar32 *Q_UnicodeAdvance( uchar32 *pUTF32, int nChars );



// Conversion between Unicode string types (UTF-8, UTF-16, UTF-32). Deals with bytes, not element counts,
// to minimize harm from the programmer mistakes which continue to plague our wide-character string code.
// Returns the number of bytes written to the output, or if output is NULL, the number of bytes required.
int Q_UTF8ToUTF16( const char *pUTF8, OUT_Z_BYTECAP(cubDestSizeInBytes) uchar16 *pUTF16, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF8ToUTF32( const char *pUTF8, OUT_Z_BYTECAP(cubDestSizeInBytes) uchar32 *pUTF32, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF16ToUTF8( const uchar16 *pUTF16, OUT_Z_BYTECAP(cubDestSizeInBytes) char *pUTF8, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF16ToUTF32( const uchar16 *pUTF16, OUT_Z_BYTECAP(cubDestSizeInBytes) uchar32 *pUTF32, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF32ToUTF8( const uchar32 *pUTF32, OUT_Z_BYTECAP(cubDestSizeInBytes) char *pUTF8, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF32ToUTF16( const uchar32 *pUTF32, OUT_Z_BYTECAP(cubDestSizeInBytes) uchar16 *pUTF16, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );

// This is disgusting and exist only easily to facilitate having 16-bit and 32-bit wchar_t's on different platforms
int Q_UTF32ToUTF32( const uchar32 *pUTF32Source, OUT_Z_BYTECAP(cubDestSizeInBytes) uchar32 *pUTF32Dest, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );

// Conversion between count-limited UTF-n character arrays, including any potential NULL characters.
// Output has a terminating NULL for safety; strip the last character if you want an unterminated string.
// Returns the number of bytes written to the output, or if output is NULL, the number of bytes required.
int Q_UTF8CharsToUTF16( const char *pUTF8, int nElements, OUT_Z_BYTECAP(cubDestSizeInBytes) uchar16 *pUTF16, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF8CharsToUTF32( const char *pUTF8, int nElements, OUT_Z_BYTECAP(cubDestSizeInBytes) uchar32 *pUTF32, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF16CharsToUTF8( const uchar16 *pUTF16, int nElements, OUT_Z_BYTECAP(cubDestSizeInBytes) char *pUTF8, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF16CharsToUTF32( const uchar16 *pUTF16, int nElements, OUT_Z_BYTECAP(cubDestSizeInBytes) uchar32 *pUTF32, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF32CharsToUTF8( const uchar32 *pUTF32, int nElements, OUT_Z_BYTECAP(cubDestSizeInBytes) char *pUTF8, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );
int Q_UTF32CharsToUTF16( const uchar32 *pUTF32, int nElements, OUT_Z_BYTECAP(cubDestSizeInBytes) uchar16 *pUTF16, int cubDestSizeInBytes, EStringConvertErrorPolicy ePolicy = STRINGCONVERT_ASSERT_REPLACE );

// Decode a single UTF-8 character to a uchar32, returns number of UTF-8 bytes parsed
int Q_UTF8ToUChar32( const char *pUTF8_, uchar32 &uValueOut, bool &bErrorOut );

// Decode a single UTF-16 character to a uchar32, returns number of UTF-16 characters (NOT BYTES) consumed
int Q_UTF16ToUChar32( const uchar16 *pUTF16, uchar32 &uValueOut, bool &bErrorOut );


// NOTE: WString means either UTF32 or UTF16 depending on the platform and compiler settings.
#if defined( _MSC_VER ) || defined( _WIN32 )
#define Q_UTF8ToWString Q_UTF8ToUTF16
#define Q_UTF8CharsToWString Q_UTF8CharsToUTF16

#define Q_WStringToUTF8 Q_UTF16ToUTF8


#else
#define Q_UTF8ToWString Q_UTF8ToUTF32
#define Q_UTF8CharsToWString Q_UTF8CharsToUTF32

#define Q_WStringToUTF8 Q_UTF32ToUTF8


#endif

// These are legacy names which don't make a lot of sense but are used everywhere. Prefer the WString convention wherever possible
#define V_UTF8ToUnicode Q_UTF8ToWString
#define V_UnicodeToUTF8 Q_WStringToUTF8


#ifdef WIN32
#endif





// UNDONE: Find a non-compiler-specific way to do this
#ifdef _WIN32
#ifndef _VA_LIST_DEFINED

#ifdef  _M_ALPHA

struct va_list 
{
    char *a0;       /* pointer to first homed integer argument */
    int offset;     /* byte offset of next parameter */
};

#else  // !_M_ALPHA

typedef char *  va_list;

#endif // !_M_ALPHA

#define _VA_LIST_DEFINED

#endif   // _VA_LIST_DEFINED

#elif POSIX
#include <stdarg.h>
#endif

#ifdef _WIN32
#define CORRECT_PATH_SEPARATOR '\\'

#define INCORRECT_PATH_SEPARATOR '/'

#elif POSIX
#define CORRECT_PATH_SEPARATOR '/'

#define INCORRECT_PATH_SEPARATOR '\\'

#endif

int V_vsnprintf( OUT_Z_CAP(maxLenInCharacters) char *pDest, int maxLenInCharacters, PRINTF_FORMAT_STRING const char *pFormat, va_list params );
template <size_t maxLenInCharacters> int V_vsprintf_safe( OUT_Z_ARRAY char (&pDest)[maxLenInCharacters], PRINTF_FORMAT_STRING const char *pFormat, va_list params ) { return V_vsnprintf( pDest, maxLenInCharacters, pFormat, params ); }

int V_snprintf( OUT_Z_CAP(maxLenInChars) char *pDest, int maxLenInChars, PRINTF_FORMAT_STRING const char *pFormat, ... ) FMTFUNCTION( 3, 4 );
// gcc insists on only having format annotations on declarations, not definitions, which is why I have both.
template <size_t maxLenInChars> int V_sprintf_safe( OUT_Z_ARRAY char (&pDest)[maxLenInChars], PRINTF_FORMAT_STRING const char *pFormat, ... ) FMTFUNCTION( 2, 3 );
template <size_t maxLenInChars> int V_sprintf_safe( OUT_Z_ARRAY char (&pDest)[maxLenInChars], PRINTF_FORMAT_STRING const char *pFormat, ... )
{
	va_list params;
	va_start( params, pFormat );
	int result = V_vsnprintf( pDest, maxLenInChars, pFormat, params );
	va_end( params );
	return result;
}


int V_vsnwprintf( OUT_Z_CAP(maxLenInCharacters) wchar_t *pDest, int maxLenInCharacters, PRINTF_FORMAT_STRING const wchar_t *pFormat, va_list params );
int V_vsnprintfRet( OUT_Z_CAP(maxLenInCharacters) char *pDest, int maxLenInCharacters, PRINTF_FORMAT_STRING const char *pFormat, va_list params, bool *pbTruncated );

// FMTFUNCTION can only be used on ASCII functions, not wide-char functions.
int V_snwprintf( OUT_Z_CAP(maxLenInCharacters) wchar_t *pDest, int maxLenInCharacters, PRINTF_FORMAT_STRING const wchar_t *pFormat, ... );
template <size_t maxLenInChars> int V_swprintf_safe( OUT_Z_ARRAY wchar_t (&pDest)[maxLenInChars], PRINTF_FORMAT_STRING const wchar_t *pFormat, ... )
{
	va_list params;
	va_start( params, pFormat );
	int result = V_vsnwprintf( pDest, maxLenInChars, pFormat, params );
	va_end( params );
	return result;
}

// Prints out a pretified memory counter string value ( e.g., 7,233.27 Mb, 1,298.003 Kb, 127 bytes )
char *V_pretifymem( float value, int digitsafterdecimal = 2, bool usebinaryonek = false );

// Prints out a pretified integer with comma separators (eg, 7,233,270,000)
char *V_pretifynum( int64 value );

int _V_UCS2ToUnicode( const ucs2 *pUCS2, OUT_Z_BYTECAP(cubDestSizeInBytes) wchar_t *pUnicode, int cubDestSizeInBytes );

int _V_UCS2ToUTF8( const ucs2 *pUCS2, OUT_Z_BYTECAP(cubDestSizeInBytes) char *pUTF8, int cubDestSizeInBytes );

int _V_UnicodeToUCS2( const wchar_t *pUnicode, int cubSrcInBytes, OUT_Z_BYTECAP(cubDestSizeInBytes) char *pUCS2, int cubDestSizeInBytes );

int _V_UTF8ToUCS2( const char *pUTF8, int cubSrcInBytes, OUT_Z_BYTECAP(cubDestSizeInBytes) ucs2 *pUCS2, int cubDestSizeInBytes );
template< typename T, typename U > inline int V_UTF8ToUCS2( const char *pUTF8, T cubSrcInBytes, OUT_Z_BYTECAP(cubDestSizeInBytes) ucs2 *pUCS2, U cubDestSizeInBytes )
{
	return _V_UTF8ToUCS2( pUTF8, static_cast<int>(cubSrcInBytes), pUCS2, static_cast<int>(cubDestSizeInBytes) );
}

// strips leading and trailing whitespace; returns true if any characters were removed. UTF-8 and UTF-16 versions.
bool Q_StripPrecedingAndTrailingWhitespace( char *pch );
bool Q_StripPrecedingAndTrailingWhitespaceW( wchar_t *pwch );

// strips leading and trailing whitespace, also taking "aggressive" characters 
// like punctuation spaces, non-breaking spaces, composing characters, and so on
bool Q_AggressiveStripPrecedingAndTrailingWhitespace( char *pch );
bool Q_AggressiveStripPrecedingAndTrailingWhitespaceW( wchar_t *pwch );
bool Q_RemoveAllEvilCharacters( char *pch );

// Functions for converting hexidecimal character strings back into binary data etc.
//
// e.g., 
// int output;
// V_hextobinary( "ffffffff", 8, &output, sizeof( output ) );
// would make output == 0xfffffff or -1
// Similarly,
// char buffer[ 9 ];
// V_binarytohex( &output, sizeof( output ), buffer, sizeof( buffer ) );
// would put "ffffffff" into buffer (note null terminator!!!)
unsigned char V_nibble( char c );
void V_hextobinary( char const *in, int numchars, byte *out, int maxoutputbytes );
void V_binarytohex( const byte *in, int inputbytes, char *out, int outsize );

// Tools for working with filenames
// Extracts the base name of a file (no path, no extension, assumes '/' or '\' as path separator)
void V_FileBase( const char *in, char *out,int maxlen );
// Remove the final characters of ppath if it's '\' or '/'.
void V_StripTrailingSlash( char *ppath );

// Remove the final characters of ppline if they are whitespace (uses V_isspace)
void V_StripTrailingWhitespace( char *ppline );

// Remove the initial characters of ppline if they are whitespace (uses V_isspace)
void V_StripLeadingWhitespace( char *ppline );

// Remove the initial/final characters of ppline if they are " quotes
void V_StripSurroundingQuotes( char *ppline );

// Remove any extension from in and return resulting string in out
void V_StripExtension( const char *in, char *out, int outLen );
// Make path end with extension if it doesn't already have an extension
void V_DefaultExtension( char *path, const char *extension, int pathStringLength );
// Strips any current extension from path and ensures that extension is the new extension
void V_SetExtension( char *path, const char *extension, int pathStringLength );
// Removes any filename from path ( strips back to previous / or \ character )
void V_StripFilename( char *path );
// Remove the final directory from the path
bool V_StripLastDir( char *dirName, int maxlen );
// Returns a pointer to the unqualified file name (no path) of a file name
const char * V_UnqualifiedFileName( const char * in );
// Given a path and a filename, composes "path\filename", inserting the (OS correct) separator if necessary
void V_ComposeFileName( const char *path, const char *filename, char *dest, int destSize );

// Copy out the path except for the stuff after the final pathseparator
bool V_ExtractFilePath( const char *path, char *dest, int destSize );
// Copy out the file extension into dest
void V_ExtractFileExtension( const char *path, char *dest, int destSize );

const char *V_GetFileExtension( const char * path );


// returns a pointer to just the filename part of the path
// (everything after the last path seperator)
const char *V_GetFileName( const char * path );

// This removes "./" and "../" from the pathname. pFilename should be a full pathname.
// Also incorporates the behavior of V_FixSlashes and optionally V_FixDoubleSlashes.
// Returns false if it tries to ".." past the root directory in the drive (in which case 
// it is an invalid path).
bool V_RemoveDotSlashes( char *pFilename, char separator = CORRECT_PATH_SEPARATOR, bool bRemoveDoubleSlashes = true );

// If pPath is a relative path, this function makes it into an absolute path
// using the current working directory as the base, or pStartingDir if it's non-NULL.
// Returns false if it runs out of room in the string, or if pPath tries to ".." past the root directory.
void V_MakeAbsolutePath( char *pOut, int outLen, const char *pPath, const char *pStartingDir = NULL );
inline void V_MakeAbsolutePath( char *pOut, int outLen, const char *pPath, const char *pStartingDir, bool bLowercaseName )
{
	V_MakeAbsolutePath( pOut, outLen, pPath, pStartingDir );
	if ( bLowercaseName )
	{
		V_strlower( pOut );
	}
}


// Creates a relative path given two full paths
// The first is the full path of the file to make a relative path for.
// The second is the full path of the directory to make the first file relative to
// Returns false if they can't be made relative (on separate drives, for example)
bool V_MakeRelativePath( const char *pFullPath, const char *pDirectory, char *pRelativePath, int nBufLen );

// Fixes up a file name, removing dot slashes, fixing slashes, converting to lowercase, etc.
void V_FixupPathName( OUT_Z_CAP(nOutLen) char *pOut, int nOutLen, const char *pPath );

// Adds a path separator to the end of the string if there isn't one already. Returns false if it would run out of space.
void V_AppendSlash( INOUT_Z_CAP(strSize) char *pStr, int strSize );

// Returns true if the path is an absolute path.
bool V_IsAbsolutePath( IN_Z const char *pPath );

// Scans pIn and replaces all occurences of pMatch with pReplaceWith.
// Writes the result to pOut.
// Returns true if it completed successfully.
// If it would overflow pOut, it fills as much as it can and returns false.
bool V_StrSubst( IN_Z const char *pIn, IN_Z const char *pMatch, const char *pReplaceWith,
	OUT_Z_CAP(outLen) char *pOut, int outLen, bool bCaseSensitive=false );

// Split the specified string on the specified separator.
// Returns a list of strings separated by pSeparator.
// You are responsible for freeing the contents of outStrings (call outStrings.PurgeAndDeleteElements).
void V_SplitString( IN_Z const char *pString, IN_Z const char *pSeparator, CUtlVector<char*, CUtlMemory<char*, int> > &outStrings );

void V_SplitString( const char *pString, const char *pSeparator, CUtlVector< CUtlString, CUtlMemory<CUtlString, int> > &outStrings, bool bIncludeEmptyStrings = false );



// Just like V_SplitString, but it can use multiple possible separators.
void V_SplitString2( IN_Z const char *pString, const char **pSeparators, int nSeparators, CUtlVector<char*, CUtlMemory<char*, int> > &outStrings );

// Returns false if the buffer is not large enough to hold the working directory name.
bool V_GetCurrentDirectory( OUT_Z_CAP(maxLen) char *pOut, int maxLen );

// Set the working directory thus.
bool V_SetCurrentDirectory( const char *pDirName );


// This function takes a slice out of pStr and stores it in pOut.
// It follows the Python slice convention:
// Negative numbers wrap around the string (-1 references the last character).
// Large numbers are clamped to the end of the string.
void V_StrSlice( const char *pStr, int firstChar, int lastCharNonInclusive, OUT_Z_CAP(outSize) char *pOut, int outSize );

// Chop off the left nChars of a string.
void V_StrLeft( const char *pStr, int nChars, OUT_Z_CAP(outSize) char *pOut, int outSize );

// Chop off the right nChars of a string.
void V_StrRight( const char *pStr, int nChars, OUT_Z_CAP(outSize) char *pOut, int outSize );

// change "special" characters to have their c-style backslash sequence. like \n, \r, \t, ", etc.
// returns a pointer to a newly allocated string, which you must delete[] when finished with.
char *V_AddBackSlashesToSpecialChars( char const *pSrc );

// Force slashes of either type to be = separator character
void V_FixSlashes( char *pname, char separator = CORRECT_PATH_SEPARATOR );

// This function fixes cases of filenames like materials\\blah.vmt or somepath\otherpath\\ and removes the extra double slash.
void V_FixDoubleSlashes( char *pStr );

// Convert multibyte to wchar + back
// Specify -1 for nInSize for null-terminated string
void V_strtowcs( const char *pString, int nInSize, OUT_Z_BYTECAP(nOutSizeInBytes) wchar_t *pWString, int nOutSizeInBytes );
void V_wcstostr( const wchar_t *pWString, int nInSize, OUT_Z_CAP(nOutSizeInBytes) char *pString, int nOutSizeInBytes );

// buffer-safe strcat
inline void V_strcat( INOUT_Z_CAP(cchDest) char *dest, const char *src, int cchDest )
{
	V_strncat( dest, src, cchDest, COPY_ALL_CHARACTERS );
}


// Encode a string for display as HTML -- this only encodes ' " & < >, which are the important ones to encode for 
// security and ensuring HTML display doesn't break.  Other special chars like the ? sign and so forth will not
// be encoded
//
// Returns false if there was not enough room in pDest to encode the entire source string, otherwise true
bool V_BasicHtmlEntityEncode( OUT_Z_CAP( nDestSize ) char *pDest, const int nDestSize, char const *pIn, const int nInSize, bool bPreserveWhitespace = false );


// Decode a string with htmlentities HTML -- this should handle all special chars, not just the ones Q_BasicHtmlEntityEncode uses.
//
// Returns false if there was not enough room in pDest to decode the entire source string, otherwise true
bool V_HtmlEntityDecodeToUTF8( OUT_Z_CAP( nDestSize ) char *pDest, const int nDestSize, char const *pIn, const int nInSize );

// strips HTML from a string.  Should call Q_HTMLEntityDecodeToUTF8 afterward.
void V_StripAndPreserveHTML( CUtlBuffer *pbuffer, const char *pchHTML, const char **rgszPreserveTags, uint cPreserveTags, uint cMaxResultSize );
void V_StripAndPreserveHTMLCore( CUtlBuffer *pbuffer, const char *pchHTML, const char **rgszPreserveTags, uint cPreserveTags, const char **rgszNoCloseTags, uint cNoCloseTags, uint cMaxResultSize );

// Extracts the domain from a URL
bool V_ExtractDomainFromURL( const char *pchURL, OUT_Z_CAP( cchDomain ) char *pchDomain, int cchDomain );

// returns true if the url passed in is on the specified domain
bool V_URLContainsDomain( const char *pchURL, const char *pchDomain );

//-----------------------------------------------------------------------------
// returns true if the character is allowed in a URL, false otherwise
//-----------------------------------------------------------------------------
bool V_IsValidURLCharacter( const char *pch, int *pAdvanceBytes );

//-----------------------------------------------------------------------------
// returns true if the character is allowed in a DNS doman name, false otherwise
//-----------------------------------------------------------------------------
bool V_IsValidDomainNameCharacter( const char *pch, int *pAdvanceBytes );

 // Converts BBCode tags to HTML tags
bool V_BBCodeToHTML( OUT_Z_CAP( nDestSize ) char *pDest, const int nDestSize, char const *pIn, const int nInSize );


// helper to identify "mean" spaces, which we don't like in visible identifiers
// such as player Name
bool V_IsMeanSpaceW( wchar_t wch );

// helper to identify characters which are deprecated in Unicode,
// and we simply don't accept
bool V_IsDeprecatedW( wchar_t wch );

//-----------------------------------------------------------------------------
// generic unique name helper functions
//-----------------------------------------------------------------------------

// returns startindex if none found, 2 if "prefix" found, and n+1 if "prefixn" found
template < class NameArray >
int V_GenerateUniqueNameIndex( const char *prefix, const NameArray &nameArray, int startindex = 0 )
{
	if ( prefix == NULL )
		return 0;

	int freeindex = startindex;

	int nNames = nameArray.Count();
	for ( int i = 0; i < nNames; ++i )
	{
		const char *pName = nameArray[ i ];
		if ( !pName )
			continue;

		const char *pIndexStr = StringAfterPrefix( pName, prefix );
		if ( pIndexStr )
		{
			int index = *pIndexStr ? atoi( pIndexStr ) : 1;
			if ( index >= freeindex )
			{
				// TODO - check that there isn't more junk after the index in pElementName
				freeindex = index + 1;
			}
		}
	}

	return freeindex;
}

template < class NameArray >
bool V_GenerateUniqueName( OUT_Z_CAP(memsize) char *name, int memsize, const char *prefix, const NameArray &nameArray )
{
	if ( name == NULL || memsize == 0 )
		return false;

	if ( prefix == NULL )
	{
		name[ 0 ] = '\0';
		return false;
	}

	int prefixLength = V_strlen( prefix );
	if ( prefixLength + 1 > memsize )
	{
		name[ 0 ] = '\0';
		return false;
	}

	int i = V_GenerateUniqueNameIndex( prefix, nameArray );
	if ( i <= 0 )
	{
		V_strncpy( name, prefix, memsize );
		return true;
	}

	int newlen = prefixLength + ( int )log10( ( float )i ) + 1;
	if ( newlen + 1 > memsize )
	{
		V_strncpy( name, prefix, memsize );
		return false;
	}

	V_snprintf( name, memsize, "%s%d", prefix, i );
	return true;
}


//
// This utility class is for performing UTF-8 <-> UTF-16 conversion.
// It is intended for use with function/method parameters.
//
// For example, you can call
//     FunctionTakingUTF16( CStrAutoEncode( utf8_string ).ToWString() )
// or
//     FunctionTakingUTF8( CStrAutoEncode( utf16_string ).ToString() )
//
// The converted string is allocated off the heap, and destroyed when
// the object goes out of scope.
//
// if the string cannot be converted, NULL is returned.
//
// This class doesn't have any conversion operators; the intention is
// to encourage the developer to get used to having to think about which
// encoding is desired.
//
class CStrAutoEncode
{
public:

	// ctor
	explicit CStrAutoEncode( const char *pch )
	{
		m_pch = pch;
		m_pwch = NULL;
#if !defined( WIN32 ) && !defined(_WIN32)
		m_pucs2 = NULL;
		m_bCreatedUCS2 = false;
#endif
		m_bCreatedUTF16 = false;
	}

	// ctor
	explicit CStrAutoEncode( const wchar_t *pwch )
	{
		m_pch = NULL;
		m_pwch = pwch;
#if !defined( WIN32 ) && !defined(_WIN32)
		m_pucs2 = NULL;
		m_bCreatedUCS2 = false;
#endif
		m_bCreatedUTF16 = true;
	}

#if !defined(WIN32) && !defined(_WINDOWS) && !defined(_WIN32)
	explicit CStrAutoEncode( const ucs2 *pwch )
	{
		m_pch = NULL;
		m_pwch = NULL;
		m_pucs2 = pwch;
		m_bCreatedUCS2 = true;
		m_bCreatedUTF16 = false;
	}
#endif

	// returns the UTF-8 string, converting on the fly.
	const char* ToString()
	{
		PopulateUTF8();
		return m_pch;
	}



#if !defined( WIN32 ) && !defined(_WIN32)
#endif


	// dtor
	~CStrAutoEncode()
	{
		// if we're "native unicode" then the UTF-8 string is something we allocated,
		// and vice versa.
		if ( m_bCreatedUTF16 )
		{
			delete [] m_pch;
		}
		else
		{
			delete [] m_pwch;
		}
#if !defined( WIN32 ) && !defined(_WIN32)
		if ( !m_bCreatedUCS2 && m_pucs2 )
			delete [] m_pucs2;
#endif
	}

private:
	// ensure we have done any conversion work required to farm out a
	// UTF-8 encoded string.
	//
	// We perform two heap allocs here; the first one is the worst-case
	// (four bytes per Unicode code point). This is usually quite pessimistic,
	// so we perform a second allocation that's just the size we need.
	void PopulateUTF8()
	{
		if ( !m_bCreatedUTF16 )
			return;					// no work to do
		if ( m_pwch == NULL )
			return;					// don't have a UTF-16 string to convert
		if ( m_pch != NULL )
			return;					// already been converted to UTF-8; no work to do

		// each Unicode code point can expand to as many as four bytes in UTF-8; we
		// also need to leave room for the terminating NUL.
		uint32 cbMax = 4 * static_cast<uint32>( V_wcslen( m_pwch ) ) + 1;
		char *pchTemp = new char[ cbMax ];
		if ( V_UnicodeToUTF8( m_pwch, pchTemp, cbMax ) )
		{
			uint32 cchAlloc = static_cast<uint32>( V_strlen( pchTemp ) ) + 1;
			char *pchHeap = new char[ cchAlloc ];
			V_strncpy( pchHeap, pchTemp, cchAlloc );
			delete [] pchTemp;
			m_pch = pchHeap;
		}
		else
		{
			// do nothing, and leave the UTF-8 string NULL
			delete [] pchTemp;
		}
	}

	// ensure we have done any conversion work required to farm out a
	// UTF-16 encoded string.
	//
	// We perform two heap allocs here; the first one is the worst-case
	// (one code point per UTF-8 byte). This is sometimes pessimistic,
	// so we perform a second allocation that's just the size we need.
	void PopulateUTF16()
	{
		if ( m_bCreatedUTF16 )
			return;					// no work to do
		if ( m_pch == NULL )
			return;					// no UTF-8 string to convert
		if ( m_pwch != NULL )
			return;					// already been converted to UTF-16; no work to do

		uint32 cchMax = static_cast<uint32>( V_strlen( m_pch ) ) + 1;
		wchar_t *pwchTemp = new wchar_t[ cchMax ];
		if ( V_UTF8ToUnicode( m_pch, pwchTemp, cchMax * sizeof( wchar_t ) ) )
		{
			uint32 cchAlloc = static_cast<uint32>( V_wcslen( pwchTemp ) ) + 1;
			wchar_t *pwchHeap = new wchar_t[ cchAlloc ];
			V_wcsncpy( pwchHeap, pwchTemp, cchAlloc * sizeof( wchar_t ) );
			delete [] pwchTemp;
			m_pwch = pwchHeap;
		}
		else
		{
			// do nothing, and leave the UTF-16 string NULL
			delete [] pwchTemp;
		}
	}

#if !defined( WIN32 ) && !defined(_WIN32)
	// ensure we have done any conversion work required to farm out a
	// UTF-16 encoded string.
	//
	// We perform two heap allocs here; the first one is the worst-case
	// (one code point per UTF-8 byte). This is sometimes pessimistic,
	// so we perform a second allocation that's just the size we need.
	void PopulateUCS2()
	{
		if ( m_bCreatedUCS2 )
			return;
		if ( m_pch == NULL )
			return;					// no UTF-8 string to convert
		if ( m_pucs2 != NULL )
			return;					// already been converted to UTF-16; no work to do

		uint32 cchMax = static_cast<uint32>( V_strlen( m_pch ) ) + 1;
		ucs2 *pwchTemp = new ucs2[ cchMax ];
		if ( V_UTF8ToUCS2( m_pch, cchMax, pwchTemp, cchMax * sizeof( ucs2 ) ) )
		{
			uint32 cchAlloc = cchMax;
			ucs2 *pwchHeap = new ucs2[ cchAlloc ];
			memcpy( pwchHeap, pwchTemp, cchAlloc * sizeof( ucs2 ) );
			delete [] pwchTemp;
			m_pucs2 = pwchHeap;
		}
		else
		{
			// do nothing, and leave the UTF-16 string NULL
			delete [] pwchTemp;
		}
	}
#endif

	// one of these pointers is an owned pointer; whichever
	// one is the encoding OTHER than the one we were initialized
	// with is the pointer we've allocated and must free.
	const char *m_pch;
	const wchar_t *m_pwch;
#if !defined( WIN32 ) && !defined(_WIN32)
	const ucs2 *m_pucs2;
	bool m_bCreatedUCS2;
#endif
	// "created as UTF-16", means our owned string is the UTF-8 string not the UTF-16 one.
	bool m_bCreatedUTF16;

};

// Encodes a string (or binary data) in URL encoding format, see rfc1738 section 2.2.
// Dest buffer should be 3 times the size of source buffer to guarantee it has room to encode.
void Q_URLEncodeRaw( OUT_Z_CAP(nDestLen) char *pchDest, int nDestLen, const char *pchSource, int nSourceLen );

// Decodes a string (or binary data) from URL encoding format, see rfc1738 section 2.2.
// Dest buffer should be at least as large as source buffer to gurantee room for decode.
// Dest buffer being the same as the source buffer (decode in-place) is explicitly allowed.
//
// Returns the amount of space actually used in the output buffer.  
size_t Q_URLDecodeRaw( OUT_CAP(nDecodeDestLen) char *pchDecodeDest, int nDecodeDestLen, const char *pchEncodedSource, int nEncodedSourceLen );



// Encodes a string (or binary data) in URL encoding format, this isn't the strict rfc1738 format, but instead uses + for spaces.  
// This is for historical reasons and HTML spec foolishness that lead to + becoming a de facto standard for spaces when encoding form data.
// Dest buffer should be 3 times the size of source buffer to guarantee it has room to encode.
void Q_URLEncode( OUT_Z_CAP(nDestLen) char *pchDest, int nDestLen, const char *pchSource, int nSourceLen );

// Decodes a string (or binary data) in URL encoding format, this isn't the strict rfc1738 format, but instead uses + for spaces.  
// This is for historical reasons and HTML spec foolishness that lead to + becoming a de facto standard for spaces when encoding form data.
// Dest buffer should be at least as large as source buffer to gurantee room for decode.
// Dest buffer being the same as the source buffer (decode in-place) is explicitly allowed.
//
// Returns the amount of space actually used in the output buffer.  
size_t Q_URLDecode( OUT_CAP(nDecodeDestLen) char *pchDecodeDest, int nDecodeDestLen, const char *pchEncodedSource, int nEncodedSourceLen );


// NOTE: This is for backward compatability!
// We need to DLL-export the Q methods in vstdlib but not link to them in other projects
#if !defined( VSTDLIB_BACKWARD_COMPAT )

#define Q_memset				V_memset
#define Q_memcpy				V_memcpy
#define Q_memmove				V_memmove
#define Q_memcmp				V_memcmp
#define Q_strlen				V_strlen


#define Q_strcmp				V_strcmp

#define Q_stricmp				V_stricmp
#define Q_strstr				V_strstr

#define Q_strlower				V_strlower
#define Q_wcslen				V_wcslen
#define	Q_strncmp				V_strncmp 
#define	Q_strcasecmp			V_strcasecmp

#define	Q_strnicmp				V_strnicmp
#define	Q_atoi					V_atoi
#define	Q_atoi64				V_atoi64


#define	Q_stristr				V_stristr
#define	Q_strnistr				V_strnistr
#define	Q_strnchr				V_strnchr

#define Q_strncpy				V_strncpy
#define Q_snprintf				V_snprintf
#define Q_wcsncpy				V_wcsncpy
#define Q_strncat				V_strncat

#define Q_vsnprintf				V_vsnprintf



#define Q_UTF8ToUnicode			V_UTF8ToUnicode
#define Q_UnicodeToUTF8			V_UnicodeToUTF8

#define Q_binarytohex			V_binarytohex









#define Q_ExtractFilePath		V_ExtractFilePath


#define Q_RemoveDotSlashes		V_RemoveDotSlashes












#define Q_strcat				V_strcat




#define Q_StrTrim				V_StrTrim

#endif // !defined( VSTDLIB_DLL_EXPORT )


#ifdef POSIX

#else

#endif



// Strip white space at the beginning and end of a string
int V_StrTrim( char *pStr );

bool PATHSEPARATOR( char c );

#define USE_FAST_CASE_CONVERSION 1
#if USE_FAST_CASE_CONVERSION
/// Faster conversion of an ascii char to upper case. This function does not obey locale or any language
/// setting. It should not be used to convert characters for printing, but it is a better choice
/// for internal strings such as used for hash table keys, etc. It's meant to be inlined and used
/// in places like the various dictionary classes. Not obeying locale also protects you from things
/// like your hash values being different depending on the locale setting.

/// similar to FastASCIIToLower
#define FastASCIIToLower( c ) ( ( ( (c) >= 'A' ) && ( (c) <= 'Z' ) ) ? ( (c) + 32 ) : (c) )
#else
#define FastASCIIToLower tolower
#define FastASCIIToUpper toupper
#endif

// A special high-performance case-insensitive compare function that in
// a single call distinguishes between exactly matching strings,
// strings equal in case-insensitive way, and not equal strings:
//   returns 0 if strings match exactly
//   returns >0 if strings match in a case-insensitive way, but do not match exactly
//   returns <0 if strings do not match even in a case-insensitive way
int	_V_stricmp_NegativeForUnequal	  ( const char *s1, const char *s2 );

#endif	// TIER1_STRTOOLS_H
