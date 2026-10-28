//=============================================================================//
// THE SAVE COMMENT, RUN -- docs/plan-audit.md D16.
//
// Two halves, and they need different instruments, which is
// shell_gamedll_refusals_test.cpp's split for the same reason:
//
//   1. THE BODY. That a short map name is PADDED so the clock always starts at
//      the same column, that a long one is CUT at 64, that 90 seconds reads as
//      one minute thirty, that a short buffer still comes back
//      null-terminated. Those are properties of the shipped function's body, so
//      this file compiles the shipped header
//      (sourcesharp/game/server/shell_savecomment.h) and RUNS it. Nothing here
//      is a transcription of it.
//
//   2. THE ARRANGEMENT in shell_gamedll.cpp -- that GetSaveComment calls it AND
//      still counts its refusal, in that order. That file includes
//      gameinterface.h and cbase.h's whole world and can never be linked here,
//      so it is source-read, comment-stripped.
//
// WHY THE COLUMNS ARE THE SUBJECT AND NOT THE PROSE. The save UI parses this
// string BY POSITION -- it takes the last seven characters as the clock and the
// rest as the title. A formatter that dropped the `-64` width would produce a
// comment that reads perfectly in a console and is unparseable by the thing
// that consumes it, and no assertion about "does it contain the map name" would
// ever see that. So every fact here is about a column or a boundary.
//=============================================================================//

#include <fstream>
#include <string>
#include <string.h>
#include <sys/stat.h>

#include "gtest/gtest.h"

#include "shell_savecomment.h"

namespace
{

// The engine's own buffer size, 80 chars, so the sizes under test are the
// sizes that actually occur.
const int kEngineComment = 80;

// A buffer poisoned before every call. A formatter that writes NOTHING and one
// that writes the right answer are indistinguishable if the buffer starts
// zeroed, and "the refusal left the engine's stack untouched" is the exact
// defect this function exists to close -- so the fill has to be visible.
struct PoisonedBuffer
{
	PoisonedBuffer() { memset( buf, '\xAB', sizeof( buf ) ); }

	char buf[ kEngineComment + 8 ];
};

} // namespace

//-----------------------------------------------------------------------------
// THE BODY.
//-----------------------------------------------------------------------------

TEST( SaveComment, AShortMapNameIsPaddedToSixtyFourSoTheClockStartsAtAFixedColumn )
{
	PoisonedBuffer a, b;

	ShellGameDll_FormatSaveComment( a.buf, kEngineComment, "dm_lockdown", 0, 0, false );
	ShellGameDll_FormatSaveComment( b.buf, kEngineComment, "dm_powerhouse", 0, 0, false );

	// The clock is the last seven characters of both, at the same offset,
	// regardless of how long the two names are.
	ASSERT_EQ( strlen( a.buf ), strlen( b.buf ) )
		<< "two different map names produced different lengths: '"
		<< a.buf << "' vs '" << b.buf << "'";
	EXPECT_EQ( 71u, strlen( a.buf ) ) << "'" << a.buf << "'";
	EXPECT_STREQ( "000:00", a.buf + 65 );
	EXPECT_EQ( ' ', a.buf[ 64 ] );
}

TEST( SaveComment, ALongMapNameIsCutAtSixtyFour )
{
	PoisonedBuffer p;
	const std::string strLong( 100, 'm' );

	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, strLong.c_str(), 0, 0, false );

	EXPECT_EQ( 71u, strlen( p.buf ) ) << "'" << p.buf << "'";
	EXPECT_EQ( 'm', p.buf[ 63 ] );
	EXPECT_EQ( ' ', p.buf[ 64 ] ) << "the 65th character of a 100-character name "
	                                 "reached the buffer: the .64 precision is gone";
}

TEST( SaveComment, TheClockIsThreeDigitMinutesAndTwoDigitSeconds )
{
	PoisonedBuffer p;

	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, "dm_lockdown", 7, 4, false );

	EXPECT_STREQ( "007:04", p.buf + 65 ) << "'" << p.buf << "'";
}

TEST( SaveComment, SecondsAtOrAboveSixtyRollIntoTheMinutes )
{
	PoisonedBuffer p;

	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, "dm_lockdown", 2, 90, false );

	EXPECT_STREQ( "003:30", p.buf + 65 ) << "'" << p.buf << "'";
}

TEST( SaveComment, AThousandMinutesClampsToNineNineNineFiftyNine )
{
	PoisonedBuffer p;

	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, "dm_lockdown", 1000, 0, false );

	EXPECT_STREQ( "999:59", p.buf + 65 ) << "'" << p.buf << "'";
}

// STOCK'S ORDER, AND IT IS A CARRIED QUIRK RATHER THAN AN EDGE NOBODY THOUGHT
// ABOUT. Stock clamps the minutes and THEN adds the rollover, so the clamp does
// not bound what is printed. This is pinned by name so that a later reader who
// "fixes" it has to come here and say why the bytes should stop matching
// stock's -- the save UI parses this string by column.
TEST( SaveComment, TheClampRunsBeforeTheRolloverExactlyAsStockDoesIt )
{
	PoisonedBuffer p;

	// 999 is UNDER the clamp, so the clamp does not fire; the two extra minutes
	// the seconds carry then push the printed figure to four digits and every
	// column after it moves.
	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, "dm_lockdown", 999, 120, false );

	EXPECT_STREQ( "1001:00", p.buf + 65 )
		<< "the clamp moved relative to the rollover; stock prints 1001:00 here";
}

TEST( SaveComment, NoTimeIsTheNameAloneWithNoClock )
{
	PoisonedBuffer p;

	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, "dm_lockdown", 7, 4, true );

	EXPECT_EQ( 64u, strlen( p.buf ) ) << "'" << p.buf << "'";
	EXPECT_EQ( NULL, strchr( p.buf, ':' ) ) << "a clock was written under bNoTime";
}

// THE NO-TIME SPELLING TRUNCATES TOO, and this fact exists because arm 6 of
// shell_savecomment_mutate.sh stayed GREEN without it: `.64` could be dropped
// from the bNoTime branch and every fact here still passed, because the only
// map name any of them handed that branch was eleven characters long. The
// engine's buffer is 80 bytes, so an untruncated name is an overrun waiting for
// a long enough map.
TEST( SaveComment, ALongMapNameIsCutAtSixtyFourInTheNoTimeSpellingToo )
{
	PoisonedBuffer p;
	const std::string strLong( 100, 'm' );

	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, strLong.c_str(), 0, 0, true );

	EXPECT_EQ( 64u, strlen( p.buf ) ) << "'" << p.buf << "'";
	EXPECT_EQ( '\0', p.buf[ 64 ] );
}

TEST( SaveComment, TheNameLeadsTheStringInBothSpellings )
{
	PoisonedBuffer withTime, noTime;

	ShellGameDll_FormatSaveComment( withTime.buf, kEngineComment, "dm_lockdown", 7, 4, false );
	ShellGameDll_FormatSaveComment( noTime.buf, kEngineComment, "dm_lockdown", 7, 4, true );

	EXPECT_EQ( 0, strncmp( withTime.buf, "dm_lockdown", 11 ) ) << "'" << withTime.buf << "'";
	EXPECT_EQ( 0, strncmp( noTime.buf, "dm_lockdown", 11 ) ) << "'" << noTime.buf << "'";
}

TEST( SaveComment, FractionalMinutesAndSecondsTruncateTheWayStocksIntCastDoes )
{
	PoisonedBuffer p;

	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, "dm_lockdown", 7.9f, 4.9f, false );

	EXPECT_STREQ( "007:04", p.buf + 65 ) << "'" << p.buf << "'";
}

TEST( SaveComment, AShortBufferTruncatesAndStillNullTerminates )
{
	PoisonedBuffer p;

	// THE TRUNCATION IS THE SUBJECT, and gcc warns about it because the
	// formatter is `inline` in a header: with a literal 10 it constant-folds
	// nMaxLength, sees `%-64.64s` cannot fit, and reports -Wformat-truncation
	// against the header. A call-site `#pragma GCC diagnostic` does NOT
	// suppress it -- that warning is raised by the optimiser after inlining,
	// past the point the pragma's region applies -- so the length is handed
	// over through a volatile instead. The call is byte-for-byte the same at
	// runtime; the only thing that changes is that gcc can no longer prove the
	// length, which is exactly the fact the warning was reporting.
	//
	// The header is deliberately NOT changed: its format string is stock's, and
	// a column the save/load UI parses is not the place to be quietly better
	// than the thing being replaced.
	volatile int nShortLength = 10;

	ShellGameDll_FormatSaveComment( p.buf, (int)nShortLength, "dm_lockdown", 7, 4, false );

	EXPECT_EQ( 9u, strlen( p.buf ) ) << "'" << p.buf << "'";
	EXPECT_EQ( '\0', p.buf[ 9 ] );
	EXPECT_EQ( '\xAB', p.buf[ 10 ] ) << "the formatter wrote past the length it was given";
}

TEST( SaveComment, ANonPositiveLengthWritesNothingRatherThanOneNullByte )
{
	PoisonedBuffer zero, negative;

	ShellGameDll_FormatSaveComment( zero.buf, 0, "dm_lockdown", 7, 4, false );
	ShellGameDll_FormatSaveComment( negative.buf, -1, "dm_lockdown", 7, 4, false );

	EXPECT_EQ( '\xAB', zero.buf[ 0 ] );
	EXPECT_EQ( '\xAB', negative.buf[ 0 ] );
}

TEST( SaveComment, ANullBufferIsNotADereference )
{
	// The assertion is that this returns at all.
	ShellGameDll_FormatSaveComment( NULL, kEngineComment, "dm_lockdown", 7, 4, false );
	SUCCEED();
}

TEST( SaveComment, ANullMapNameProducesADefinedStringRatherThanACrash )
{
	PoisonedBuffer p;

	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, NULL, 7, 4, false );

	EXPECT_EQ( 71u, strlen( p.buf ) ) << "'" << p.buf << "'";
	EXPECT_STREQ( "007:04", p.buf + 65 );
	EXPECT_EQ( ' ', p.buf[ 0 ] ) << "a NULL name should pad, not print a pointer";
}

// THE DEFECT ITSELF, stated as a fact rather than implied by the others: the
// engine's buffer comes back WRITTEN. Before this function existed the refusal
// returned without touching it and the engine used whatever was on its stack.
TEST( SaveComment, TheEnginesBufferIsWrittenAndNotLeftAsItWasFound )
{
	PoisonedBuffer p;

	ShellGameDll_FormatSaveComment( p.buf, kEngineComment, "dm_lockdown", 0, 0, false );

	EXPECT_NE( '\xAB', p.buf[ 0 ] ) << "the buffer came back exactly as it went in";
}

//-----------------------------------------------------------------------------
// THE ARRANGEMENT in shell_gamedll.cpp, source-read.
//-----------------------------------------------------------------------------

namespace
{

} // namespace

