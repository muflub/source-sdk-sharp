//========= SourceSharp — the string helpers (ss_string.h) =================//
//
// One fact per behaviour of each helper, edge cases included. These say what
// the helpers do; the oracle (src/test/oracle/native/ss_string_oracle_test.cpp)
// says that it is what tier1's strtools.cpp does, by running both.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <cstring>
#include <string>

#include "../game/shared/ss_string.h"

namespace
{

// A buffer pre-filled with a marker byte, so a test can see exactly which
// bytes a helper wrote.
struct Buf
{
	char b[16];
	Buf() { std::memset( b, '#', sizeof( b ) ); }
};

int Sign( int v ) { return ( v > 0 ) - ( v < 0 ); }

int CallFormatV( char *dst, int size, const char *fmt, ... )
{
	va_list args;
	va_start( args, fmt );
	const int n = ss::FormatV( dst, size, fmt, args );
	va_end( args );
	return n;
}

} // namespace

//-----------------------------------------------------------------------------
// StrCopy (V_strncpy)
//-----------------------------------------------------------------------------
TEST( SsStrCopy, CopiesAStringThatFits )
{
	Buf d;
	ss::StrCopy( d.b, "abc", 8 );
	EXPECT_STREQ( "abc", d.b );
}

TEST( SsStrCopy, ZeroPadsTheRestOfTheBufferLikeStrncpy )
{
	Buf d;
	ss::StrCopy( d.b, "ab", 6 );
	EXPECT_EQ( 0, std::memcmp( d.b, "ab\0\0\0\0", 6 ) );
	EXPECT_EQ( '#', d.b[6] ) << "wrote past dstSize";
}

TEST( SsStrCopy, TruncatesAndStillTerminates )
{
	Buf d;
	ss::StrCopy( d.b, "abcdef", 4 );
	EXPECT_STREQ( "abc", d.b );
	EXPECT_EQ( '#', d.b[4] );
}

TEST( SsStrCopy, ExactFitLosesTheLastCharacter )
{
	Buf d;
	ss::StrCopy( d.b, "abcd", 4 );
	EXPECT_STREQ( "abc", d.b );
}

TEST( SsStrCopy, SizeOneWritesOnlyTheTerminator )
{
	Buf d;
	ss::StrCopy( d.b, "abc", 1 );
	EXPECT_EQ( '\0', d.b[0] );
	EXPECT_EQ( '#', d.b[1] );
}

TEST( SsStrCopy, SizeZeroWritesNothing )
{
	Buf d;
	ss::StrCopy( d.b, "abc", 0 );
	EXPECT_EQ( '#', d.b[0] );
}

TEST( SsStrCopy, EmptySourceGivesEmptyString )
{
	Buf d;
	ss::StrCopy( d.b, "", 4 );
	EXPECT_EQ( 0, std::memcmp( d.b, "\0\0\0\0", 4 ) );
}

//-----------------------------------------------------------------------------
// StrAppend (V_strncat)
//-----------------------------------------------------------------------------
TEST( SsStrAppend, AppendsAllByDefault )
{
	char d[16] = "ab";
	EXPECT_EQ( d, ss::StrAppend( d, "cde", sizeof( d ) ) );
	EXPECT_STREQ( "abcde", d );
}

TEST( SsStrAppend, SizeIsTheWholeBufferNotTheRoomLeft )
{
	char d[16] = "ab";
	ss::StrAppend( d, "cdefgh", 5 );
	EXPECT_STREQ( "abcd", d ) << "5 bytes hold 4 characters and the terminator";
}

TEST( SsStrAppend, MaxCharsCapsTheSource )
{
	char d[16] = "ab";
	ss::StrAppend( d, "cdefgh", sizeof( d ), 2 );
	EXPECT_STREQ( "abcd", d );
}

TEST( SsStrAppend, MaxCharsLargerThanTheSourceTakesTheSource )
{
	char d[16] = "ab";
	ss::StrAppend( d, "cd", sizeof( d ), 10 );
	EXPECT_STREQ( "abcd", d );
}

TEST( SsStrAppend, MaxCharsZeroAppendsNothing )
{
	char d[16] = "ab";
	ss::StrAppend( d, "cd", sizeof( d ), 0 );
	EXPECT_STREQ( "ab", d );
}

TEST( SsStrAppend, AnyCapBelowCopyAllMeansNoCap )
{
	char d[16] = "ab";
	ss::StrAppend( d, "cd", sizeof( d ), -7 );
	EXPECT_STREQ( "abcd", d );
}

TEST( SsStrAppend, AFullBufferIsLeftAlone )
{
	char d[8] = "abcdefg";
	ss::StrAppend( d, "x", sizeof( d ) );
	EXPECT_STREQ( "abcdefg", d );
}

TEST( SsStrAppend, AnOverfullSizeIsLeftAlone )
{
	char d[16] = "abcdefg";
	ss::StrAppend( d, "x", 3 );
	EXPECT_STREQ( "abcdefg", d );
}

TEST( SsStrAppend, SizeZeroIsLeftAlone )
{
	char d[16] = "";
	ss::StrAppend( d, "x", 0 );
	EXPECT_STREQ( "", d );
}

//-----------------------------------------------------------------------------
// Format / FormatV (V_snprintf / V_vsnprintf)
//-----------------------------------------------------------------------------
TEST( SsFormat, AnswersTheLengthWhenItFits )
{
	Buf d;
	EXPECT_EQ( 5, ss::Format( d.b, 8, "%s-%d", "ab", 42 ) );
	EXPECT_STREQ( "ab-42", d.b );
}

TEST( SsFormat, AnswersTheBufferSizeWhenCut )
{
	Buf d;
	EXPECT_EQ( 4, ss::Format( d.b, 4, "%s", "abcdef" ) );
	EXPECT_STREQ( "abc", d.b );
	EXPECT_EQ( '#', d.b[4] );
}

TEST( SsFormat, AnExactFitIsACut )
{
	Buf d;
	EXPECT_EQ( 4, ss::Format( d.b, 4, "abcd" ) ) << "4 characters need 5 bytes";
	EXPECT_STREQ( "abc", d.b );
}

TEST( SsFormat, OneShortOfFullFits )
{
	Buf d;
	EXPECT_EQ( 3, ss::Format( d.b, 4, "abc" ) );
	EXPECT_STREQ( "abc", d.b );
}

TEST( SsFormat, SizeOneGivesEmptyAndAnswersOne )
{
	Buf d;
	EXPECT_EQ( 1, ss::Format( d.b, 1, "abc" ) );
	EXPECT_EQ( '\0', d.b[0] );
	EXPECT_EQ( '#', d.b[1] );
}

TEST( SsFormat, EmptyOutputAnswersZero )
{
	Buf d;
	EXPECT_EQ( 0, ss::Format( d.b, 4, "%s", "" ) );
	EXPECT_STREQ( "", d.b );
}

TEST( SsFormat, SizeZeroWritesNothing )
{
	Buf d;
	EXPECT_EQ( 0, ss::Format( d.b, 0, "abc" ) );
	EXPECT_EQ( '#', d.b[0] );
}

TEST( SsFormatV, BehavesAsFormat )
{
	Buf d;
	EXPECT_EQ( 4, CallFormatV( d.b, 4, "%d", 123456 ) );
	EXPECT_STREQ( "123", d.b );
	EXPECT_EQ( 2, CallFormatV( d.b, 4, "%d", 12 ) );
	EXPECT_STREQ( "12", d.b );
}

//-----------------------------------------------------------------------------
// StrICmp / StrNICmp (V_stricmp / V_strnicmp)
//-----------------------------------------------------------------------------
TEST( SsStrICmp, EqualIgnoringAsciiCase )
{
	EXPECT_EQ( 0, ss::StrICmp( "Hello", "hELLO" ) );
}

TEST( SsStrICmp, AnswersMinusOneAndOne )
{
	EXPECT_EQ( -1, ss::StrICmp( "abc", "ABD" ) );
	EXPECT_EQ( 1, ss::StrICmp( "ABD", "abc" ) );
}

TEST( SsStrICmp, APrefixIsLess )
{
	EXPECT_EQ( -1, ss::StrICmp( "ab", "ABC" ) );
	EXPECT_EQ( 1, ss::StrICmp( "abc", "AB" ) );
}

TEST( SsStrICmp, EmptyStrings )
{
	EXPECT_EQ( 0, ss::StrICmp( "", "" ) );
	EXPECT_EQ( -1, ss::StrICmp( "", "a" ) );
	EXPECT_EQ( 1, ss::StrICmp( "a", "" ) );
}

TEST( SsStrICmp, FoldsLettersIntoLowerCaseBeforeOrdering )
{
	// '_' (0x5F) sits between 'Z' and 'a'. Folded to lower case, every letter
	// is above it; a raw compare would put 'B' below it.
	EXPECT_EQ( 1, ss::StrICmp( "B", "_" ) );
	EXPECT_EQ( -1, ss::StrICmp( "_", "b" ) );
}

TEST( SsStrICmp, NonLettersThatDifferByCaseBitAreDifferent )
{
	// '@' is 0x40 and '`' is 0x60: one bit apart, like 'A' and 'a', but not
	// letters.
	EXPECT_EQ( -1, ss::StrICmp( "@", "`" ) );
	EXPECT_EQ( -1, ss::StrICmp( "[", "{" ) );
}

TEST( SsStrICmp, BytesAbove0x7FAreNotFolded )
{
	// 0xC0 and 0xE0 are the Latin-1 capital and small A-grave; a locale might
	// fold them together, ASCII folding does not.
	EXPECT_EQ( -1, ss::StrICmp( "\xC0", "\xE0" ) );
	EXPECT_EQ( 0, ss::StrICmp( "\xC0x", "\xC0X" ) );
}

TEST( SsStrICmp, HighBytesOrderAboveAscii )
{
	EXPECT_EQ( 1, ss::StrICmp( "\x80", "z" ) );
	EXPECT_EQ( -1, ss::StrICmp( "Z", "\xFF" ) );
}

TEST( SsStrICmp, SamePointerIsEqual )
{
	const char *s = "abc";
	EXPECT_EQ( 0, ss::StrICmp( s, s ) );
}

TEST( SsStrNICmp, ComparesOnlyNCharacters )
{
	EXPECT_EQ( 0, ss::StrNICmp( "models/x", "MODELS/y", 7 ) );
	EXPECT_EQ( -1, ss::StrNICmp( "models/x", "MODELS/y", 8 ) );
}

TEST( SsStrNICmp, NZeroOrNegativeIsEqual )
{
	EXPECT_EQ( 0, ss::StrNICmp( "a", "b", 0 ) );
	EXPECT_EQ( 0, ss::StrNICmp( "a", "b", -3 ) );
}

TEST( SsStrNICmp, StopsAtTheEndOfEqualStrings )
{
	EXPECT_EQ( 0, ss::StrNICmp( "ab", "AB", 100 ) );
	EXPECT_EQ( -1, ss::StrNICmp( "ab", "ABc", 100 ) );
	EXPECT_EQ( 1, ss::StrNICmp( "abc", "AB", 100 ) );
}

TEST( SsStrNICmp, ASignedCharIsOrderedAsUnsigned )
{
	EXPECT_EQ( 1, Sign( ss::StrNICmp( "\xE9", "e", 1 ) ) );
}

//-----------------------------------------------------------------------------
// StrIStr (V_stristr)
//-----------------------------------------------------------------------------
TEST( SsStrIStr, FindsIgnoringCase )
{
	const char *h = "Sound/Blood.WAV";
	EXPECT_EQ( h + 6, ss::StrIStr( h, "blood" ) );
	EXPECT_EQ( h + 11, ss::StrIStr( h, ".wav" ) );
}

TEST( SsStrIStr, FindsTheFirstOccurrence )
{
	const char *h = "aXbxc";
	EXPECT_EQ( h + 1, ss::StrIStr( h, "x" ) );
}

TEST( SsStrIStr, MissingIsNull )
{
	EXPECT_EQ( nullptr, ss::StrIStr( "abc", "abd" ) );
}

TEST( SsStrIStr, ANeedleLongerThanTheRestIsNull )
{
	EXPECT_EQ( nullptr, ss::StrIStr( "xab", "abc" ) );
}

TEST( SsStrIStr, AnEmptyNeedleIsNeverFound )
{
	EXPECT_EQ( nullptr, ss::StrIStr( "abc", "" ) );
	EXPECT_EQ( nullptr, ss::StrIStr( "", "" ) );
}

TEST( SsStrIStr, AnEmptyHaystackIsNull )
{
	EXPECT_EQ( nullptr, ss::StrIStr( "", "a" ) );
}

TEST( SsStrIStr, NullPointersAreNull )
{
	EXPECT_EQ( nullptr, ss::StrIStr( (const char *)nullptr, "a" ) );
	EXPECT_EQ( nullptr, ss::StrIStr( "a", nullptr ) );
}

TEST( SsStrIStr, HighBytesMatchOnlyThemselves )
{
	EXPECT_EQ( nullptr, ss::StrIStr( "x\xC0y", "\xE0" ) );
	const char *h = "x\xC0y";
	EXPECT_EQ( h + 1, ss::StrIStr( h, "\xC0Y" ) );
}

TEST( SsStrIStr, TheMutableOverloadAnswersAMutablePointer )
{
	char h[] = "abCD";
	char *p = ss::StrIStr( h, "cd" );
	EXPECT_EQ( h + 2, p );
}

//-----------------------------------------------------------------------------
// AtoI / AtoI64 (V_atoi / V_atoi64)
//-----------------------------------------------------------------------------
TEST( SsAtoI, Decimal )
{
	EXPECT_EQ( 1234, ss::AtoI( "1234" ) );
}

TEST( SsAtoI, Signs )
{
	EXPECT_EQ( -42, ss::AtoI( "-42" ) );
	EXPECT_EQ( 42, ss::AtoI( "+42" ) );
}

TEST( SsAtoI, StopsAtTheFirstNonDigit )
{
	EXPECT_EQ( 12, ss::AtoI( "12ab3" ) );
	EXPECT_EQ( 3, ss::AtoI( "3.9" ) );
}

TEST( SsAtoI, NoDigitsIsZero )
{
	EXPECT_EQ( 0, ss::AtoI( "" ) );
	EXPECT_EQ( 0, ss::AtoI( "-" ) );
	EXPECT_EQ( 0, ss::AtoI( "abc" ) );
}

TEST( SsAtoI, LeadingWhitespaceIsNotSkipped )
{
	EXPECT_EQ( 0, ss::AtoI( " 5" ) );
}

TEST( SsAtoI, OnlyOneSign )
{
	EXPECT_EQ( 0, ss::AtoI( "--5" ) );
	EXPECT_EQ( 0, ss::AtoI( "+-5" ) );
}

TEST( SsAtoI, Hex )
{
	EXPECT_EQ( 255, ss::AtoI( "0xff" ) );
	EXPECT_EQ( 0xABC, ss::AtoI( "0XaBc" ) );
	EXPECT_EQ( 0x1F, ss::AtoI( "0x1Fg" ) );
}

TEST( SsAtoI, NegativeHex )
{
	EXPECT_EQ( -16, ss::AtoI( "-0x10" ) );
}

TEST( SsAtoI, HexPrefixWithNoDigitsIsZero )
{
	EXPECT_EQ( 0, ss::AtoI( "0x" ) );
	EXPECT_EQ( 0, ss::AtoI( "0xg1" ) );
}

TEST( SsAtoI, AQuotedCharacterIsItsCode )
{
	EXPECT_EQ( 'A', ss::AtoI( "'A'" ) );
	EXPECT_EQ( -'A', ss::AtoI( "-'A" ) );
	EXPECT_EQ( 0, ss::AtoI( "'" ) );
}

TEST( SsAtoI, KeepsTheLow32BitsOfAWideValue )
{
	EXPECT_EQ( 0x7FFFFFFFFFLL, ss::AtoI64( "0x7FFFFFFFFF" ) );
	EXPECT_EQ( -1, ss::AtoI( "0xFFFFFFFF" ) );
	EXPECT_EQ( 1, ss::AtoI( "4294967297" ) );
}

TEST( SsAtoI64, SixtyFourBitDecimal )
{
	EXPECT_EQ( -9000000000000000000LL, ss::AtoI64( "-9000000000000000000" ) );
}

//-----------------------------------------------------------------------------
// AtoF (V_atof)
//-----------------------------------------------------------------------------
TEST( SsAtoF, Integer )
{
	EXPECT_EQ( 12.0f, ss::AtoF( "12" ) );
}

TEST( SsAtoF, Fraction )
{
	EXPECT_EQ( 1.5f, ss::AtoF( "1.5" ) );
	EXPECT_EQ( 0.25f, ss::AtoF( ".25" ) );
	EXPECT_EQ( 3.0f, ss::AtoF( "3." ) );
}

TEST( SsAtoF, Signs )
{
	EXPECT_EQ( -2.5f, ss::AtoF( "-2.5" ) );
	EXPECT_EQ( 2.5f, ss::AtoF( "+2.5" ) );
}

TEST( SsAtoF, ASecondPointEndsTheNumber )
{
	EXPECT_EQ( 1.5f, ss::AtoF( "1.5.7" ) );
}

TEST( SsAtoF, Exponent )
{
	EXPECT_EQ( 150.0f, ss::AtoF( "1.5e2" ) );
	EXPECT_FLOAT_EQ( 0.015f, ss::AtoF( "1.5E-2" ) );
	EXPECT_EQ( 300.0f, ss::AtoF( "3e+2" ) );
}

TEST( SsAtoF, AnEmptyExponentIsNone )
{
	EXPECT_EQ( 7.0f, ss::AtoF( "7e" ) );
}

TEST( SsAtoF, NoDigitsIsZero )
{
	EXPECT_EQ( 0.0f, ss::AtoF( "" ) );
	EXPECT_EQ( 0.0f, ss::AtoF( "x1" ) );
}

TEST( SsAtoF, Hex )
{
	EXPECT_EQ( 255.0f, ss::AtoF( "0xFF" ) );
	EXPECT_EQ( -16.0f, ss::AtoF( "-0x10" ) );
}

TEST( SsAtoF, HexHasNoFraction )
{
	EXPECT_EQ( 16.0f, ss::AtoF( "0x10.8" ) );
}

TEST( SsAtoF, AQuotedCharacterIsItsCode )
{
	EXPECT_EQ( 65.0f, ss::AtoF( "'A" ) );
}

TEST( SsAtoF, LeadingWhitespaceIsNotSkipped )
{
	EXPECT_EQ( 0.0f, ss::AtoF( " 1" ) );
}

//-----------------------------------------------------------------------------
// StrLower (V_strlower)
//-----------------------------------------------------------------------------
TEST( SsStrLower, LowersAsciiLettersInPlace )
{
	char s[] = "Maps/DM_Lockdown.BSP";
	EXPECT_EQ( s, ss::StrLower( s ) );
	EXPECT_STREQ( "maps/dm_lockdown.bsp", s );
}

TEST( SsStrLower, LeavesOtherBytesAlone )
{
	char s[] = "@[`{\xC0\xDE";
	ss::StrLower( s );
	EXPECT_STREQ( "@[`{\xC0\xDE", s );
}

TEST( SsStrLower, EmptyIsEmpty )
{
	char s[] = "";
	ss::StrLower( s );
	EXPECT_STREQ( "", s );
}

//-----------------------------------------------------------------------------
// NextToken (nexttoken)
//-----------------------------------------------------------------------------
TEST( SsNextToken, SplitsAtTheSeparator )
{
	char t[16];
	const char *s = "a,bc,d";
	const char *p = ss::NextToken( t, s, ',' );
	EXPECT_STREQ( "a", t );
	EXPECT_EQ( s + 2, p );
	p = ss::NextToken( t, p, ',' );
	EXPECT_STREQ( "bc", t );
	p = ss::NextToken( t, p, ',' );
	EXPECT_STREQ( "d", t );
	EXPECT_EQ( s + 6, p ) << "the last token answers the terminator";
	EXPECT_EQ( nullptr, ss::NextToken( t, p, ',' ) );
	EXPECT_STREQ( "", t );
}

TEST( SsNextToken, AnEmptyTokenBetweenSeparators )
{
	char t[16];
	const char *s = ",x";
	EXPECT_EQ( s + 1, ss::NextToken( t, s, ',' ) );
	EXPECT_STREQ( "", t );
}

TEST( SsNextToken, NullOrEmptyInputEmptiesTheTokenAndAnswersNull )
{
	char t[4] = "zz";
	EXPECT_EQ( nullptr, ss::NextToken( t, nullptr, ',' ) );
	EXPECT_STREQ( "", t );
	t[0] = 'z';
	EXPECT_EQ( nullptr, ss::NextToken( t, "", ',' ) );
	EXPECT_STREQ( "", t );
}

TEST( SsNextToken, ATokenCutByTheBufferDropsOneCharacter )
{
	char t[3];
	const char *s = "abcd,e";
	const char *p = ss::NextToken( t, sizeof( t ), s, ',' );
	EXPECT_STREQ( "ab", t );
	EXPECT_EQ( s + 3, p ) << "tier1 steps over 'c' as if it were the separator";
}

TEST( SsNextToken, SizeZeroWritesNothing )
{
	char t[2] = { '#', '#' };
	EXPECT_EQ( nullptr, ss::NextToken( t, 0, "abc", ',' ) );
	EXPECT_EQ( '#', t[0] );
}

//-----------------------------------------------------------------------------
// FixSlashes (V_FixSlashes)
//-----------------------------------------------------------------------------
TEST( SsFixSlashes, BothSeparatorsBecomeForwardByDefault )
{
	char s[] = "a\\b/c\\\\d";
	ss::FixSlashes( s );
	EXPECT_STREQ( "a/b/c//d", s );
}

TEST( SsFixSlashes, ToAChosenSeparator )
{
	char s[] = "a/b\\c";
	ss::FixSlashes( s, '\\' );
	EXPECT_STREQ( "a\\b\\c", s );
}

//-----------------------------------------------------------------------------
// StripExtension (V_StripExtension)
//-----------------------------------------------------------------------------
namespace
{
std::string Stripped( const char *in, int size = 64 )
{
	char out[64];
	std::memset( out, '#', sizeof( out ) );
	ss::StripExtension( in, out, size );
	return out;
}
} // namespace

TEST( SsStripExtension, RemovesTheExtension )
{
	EXPECT_EQ( "maps/dm_x", Stripped( "maps/dm_x.bsp" ) );
}

TEST( SsStripExtension, OnlyTheLastOne )
{
	EXPECT_EQ( "a.tar", Stripped( "a.tar.gz" ) );
}

TEST( SsStripExtension, NoExtensionCopiesWhole )
{
	EXPECT_EQ( "maps/dm_x", Stripped( "maps/dm_x" ) );
}

TEST( SsStripExtension, ADotInADirectoryIsNotAnExtension )
{
	EXPECT_EQ( "a.b/c", Stripped( "a.b/c" ) );
	EXPECT_EQ( "a.b\\c", Stripped( "a.b\\c" ) );
}

TEST( SsStripExtension, ALeadingDotIsNotAnExtension )
{
	EXPECT_EQ( ".cfg", Stripped( ".cfg" ) );
}

TEST( SsStripExtension, AHiddenFileAfterASeparatorIsStripped )
{
	EXPECT_EQ( "dir/", Stripped( "dir/.cfg" ) );
}

TEST( SsStripExtension, EmptyIsEmpty )
{
	EXPECT_EQ( "", Stripped( "" ) );
}

TEST( SsStripExtension, ADotBeyondTheBufferCopiesTruncated )
{
	// The dot is at 6; a 5-byte buffer cannot reach it, so the whole name is
	// copied as StrCopy copies it.
	EXPECT_EQ( "abcd", Stripped( "abcdef.x", 5 ) );
}

TEST( SsStripExtension, AStemThatExactlyFillsTheBufferIsKept )
{
	EXPECT_EQ( "abcdef", Stripped( "abcdef.x", 7 ) );
}

TEST( SsStripExtension, ADotAtTheBufferSizeCopiesTruncated )
{
	// The dot is at 6 and the buffer is 6: not "< outSize", so the copy
	// branch, which cuts the WHOLE name to 5 characters.
	EXPECT_EQ( "abcde", Stripped( "abcdef.x", 6 ) );
}

TEST( SsStripExtension, SizeZeroWritesNothing )
{
	char out[4] = { '#', '#', '#', '#' };
	ss::StripExtension( "a.b", out, 0 );
	EXPECT_EQ( '#', out[0] );
}

TEST( SsStripExtension, InPlace )
{
	char s[] = "a/b.txt";
	ss::StripExtension( s, s, sizeof( s ) );
	EXPECT_STREQ( "a/b", s );
}

TEST( SsStripExtension, InPlaceWithoutExtensionIsUntouched )
{
	char s[] = "a/b";
	ss::StripExtension( s, s, sizeof( s ) );
	EXPECT_STREQ( "a/b", s );
}

//-----------------------------------------------------------------------------
// FileBase (V_FileBase)
//-----------------------------------------------------------------------------
namespace
{
std::string Base( const char *in, int size = 64 )
{
	char out[64];
	std::memset( out, '#', sizeof( out ) );
	ss::FileBase( in, out, size );
	return out;
}
} // namespace

TEST( SsFileBase, DropsDirectoryAndExtension )
{
	EXPECT_EQ( "dm_lockdown", Base( "maps/dm_lockdown.bsp" ) );
	EXPECT_EQ( "b", Base( "a\\b.c" ) );
}

TEST( SsFileBase, NoDirectory )
{
	EXPECT_EQ( "x", Base( "x.bsp" ) );
}

TEST( SsFileBase, NoExtension )
{
	EXPECT_EQ( "b", Base( "a/b" ) );
}

TEST( SsFileBase, OnlyTheLastExtension )
{
	EXPECT_EQ( "a.tar", Base( "a.tar.gz" ) );
}

TEST( SsFileBase, ADotInADirectoryIsIgnored )
{
	EXPECT_EQ( "c", Base( "a.b/c" ) );
}

TEST( SsFileBase, ALeadingDotIsTheExtension )
{
	EXPECT_EQ( "", Base( ".cfg" ) );
	EXPECT_EQ( "", Base( "dir/.cfg" ) );
}

TEST( SsFileBase, ATrailingSeparatorIsEmpty )
{
	EXPECT_EQ( "", Base( "dir/" ) );
	EXPECT_EQ( "", Base( "/" ) );
}

TEST( SsFileBase, EmptyAndNullAreEmpty )
{
	EXPECT_EQ( "", Base( "" ) );
	char out[4] = "zz";
	ss::FileBase( nullptr, out, sizeof( out ) );
	EXPECT_STREQ( "", out );
}

TEST( SsFileBase, TruncatesToTheBuffer )
{
	EXPECT_EQ( "dm_", Base( "maps/dm_lockdown.bsp", 4 ) );
}

//-----------------------------------------------------------------------------
// StripFilename (V_StripFilename)
//-----------------------------------------------------------------------------
namespace
{
std::string StrippedName( const char *in )
{
	char s[64];
	ss::StrCopy( s, in, sizeof( s ) );
	ss::StripFilename( s );
	return s;
}
} // namespace

TEST( SsStripFilename, CutsAtTheLastSeparator )
{
	EXPECT_EQ( "maps/sub", StrippedName( "maps/sub/x.bsp" ) );
	EXPECT_EQ( "a\\b", StrippedName( "a\\b\\c" ) );
}

TEST( SsStripFilename, ATrailingSeparatorIsTheOneCut )
{
	EXPECT_EQ( "a/b", StrippedName( "a/b/" ) );
}

TEST( SsStripFilename, NoSeparatorEmptiesIt )
{
	EXPECT_EQ( "", StrippedName( "abc" ) );
}

TEST( SsStripFilename, ALeadingSeparatorIsNeverKept )
{
	EXPECT_EQ( "", StrippedName( "/abc" ) );
}

TEST( SsStripFilename, OneCharacterIsLeftAlone )
{
	EXPECT_EQ( "a", StrippedName( "a" ) );
	EXPECT_EQ( "/", StrippedName( "/" ) );
	EXPECT_EQ( "", StrippedName( "" ) );
}

//-----------------------------------------------------------------------------
// StripLastDir (V_StripLastDir)
//-----------------------------------------------------------------------------
TEST( SsStripLastDir, DropsTheLastDirectoryKeepingItsSeparator )
{
	char s[32] = "a/b/c/";
	EXPECT_TRUE( ss::StripLastDir( s, sizeof( s ) ) );
	EXPECT_STREQ( "a/b/", s );
}

TEST( SsStripLastDir, WithoutATrailingSeparatorToo )
{
	char s[32] = "a/b/c";
	EXPECT_TRUE( ss::StripLastDir( s, sizeof( s ) ) );
	EXPECT_STREQ( "a/b/", s );
}

TEST( SsStripLastDir, FixesTheSlashes )
{
	char s[32] = "a\\b\\c";
	EXPECT_TRUE( ss::StripLastDir( s, sizeof( s ) ) );
	EXPECT_STREQ( "a/b/", s );
}

TEST( SsStripLastDir, OneComponentBecomesDotSlash )
{
	char s[32] = "tf2/";
	EXPECT_TRUE( ss::StripLastDir( s, sizeof( s ) ) );
	EXPECT_STREQ( "./", s );
	char t[32] = "tf2";
	EXPECT_TRUE( ss::StripLastDir( t, sizeof( t ) ) );
	EXPECT_STREQ( "./", t );
}

TEST( SsStripLastDir, TheRootBecomesDotSlash )
{
	char s[32] = "/";
	EXPECT_TRUE( ss::StripLastDir( s, sizeof( s ) ) );
	EXPECT_STREQ( "./", s );
}

TEST( SsStripLastDir, EmptyAndDotSlashAreRefusedUntouched )
{
	char e[8] = "";
	EXPECT_FALSE( ss::StripLastDir( e, sizeof( e ) ) );
	EXPECT_STREQ( "", e );
	char d[8] = "./";
	EXPECT_FALSE( ss::StripLastDir( d, sizeof( d ) ) );
	EXPECT_STREQ( "./", d );
	char b[8] = ".\\";
	EXPECT_FALSE( ss::StripLastDir( b, sizeof( b ) ) );
	EXPECT_STREQ( ".\\", b );
}

TEST( SsStripLastDir, TheDotSlashIsCutToTheBuffer )
{
	char s[32] = "tf2";
	EXPECT_TRUE( ss::StripLastDir( s, 2 ) );
	EXPECT_STREQ( ".", s );
}

//-----------------------------------------------------------------------------
// GetFileName (V_GetFileName)
//-----------------------------------------------------------------------------
TEST( SsGetFileName, AfterTheLastSeparator )
{
	const char *p = "maps/sub\\dm_x.bsp";
	EXPECT_EQ( p + 9, ss::GetFileName( p ) );
}

TEST( SsGetFileName, NoSeparatorIsTheWholePath )
{
	const char *p = "dm_x.bsp";
	EXPECT_EQ( p, ss::GetFileName( p ) );
}

TEST( SsGetFileName, ATrailingSeparatorDoesNotCount )
{
	const char *p = "a/b/";
	EXPECT_EQ( p + 2, ss::GetFileName( p ) );
	const char *q = "dir/";
	EXPECT_EQ( q, ss::GetFileName( q ) );
}

TEST( SsGetFileName, EmptyIsItself )
{
	const char *p = "";
	EXPECT_EQ( p, ss::GetFileName( p ) );
}
