//=============================================================================//
// THE NOT-SHIPPED LEDGER, RUN -- plan_entity.md step 7g.
//
// 7g's gate is "the engine's interface probes show each interface answered or
// REFUSED BY NAME", and shell_notshipped.h is what tells a refusal from an
// absence. The properties worth checking are properties of the BODY -- that a
// reach counts, that it warns, that it warns ONCE, that a second name opens a
// second row, that an undeclared interface name does not match a declared one
// -- and none of those can be read out of the source. So this compiles the
// SHIPPED header. Nothing here is a transcription of it.
//
// THE ARRANGEMENT -- that shell_diagnostics.cpp's miss hook consults the list,
// that entity_client_tools.cpp refuses every method but two -- is source-read
// elsewhere, because both files include cbase.h's whole world and can never be
// linked into a gtest.
//
// "TWO LEDGERS SHARE NOTHING" is the fact that is easy to miss, and it is
// shell_clientdll_refusals_test.cpp's for the same reason: it is the only way,
// from inside a gtest, to tell a member from a file-scope static.
//=============================================================================//

#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "shell_notshipped.h"

//-----------------------------------------------------------------------------
// The warning half, defined here rather than linked from tier0. This is the
// seam the header declares, and it is what lets the test assert that a reach
// SAYS something as well as counting.
//-----------------------------------------------------------------------------
namespace
{

struct RecordedWarning
{
	std::string strName;
	std::string strReason;
	int         nTotal;
};

std::vector< RecordedWarning > g_Warnings;

void ResetWarnings()
{
	g_Warnings.clear();
}

} // namespace

void ShellNotShipped_Warning( const char *pName, const char *pReason, int nTotal )
{
	RecordedWarning w;
	w.strName   = ( pName   != NULL ) ? pName   : "(null)";
	w.strReason = ( pReason != NULL ) ? pReason : "(null)";
	w.nTotal    = nTotal;
	g_Warnings.push_back( w );
}


//-----------------------------------------------------------------------------
// The counter.
//-----------------------------------------------------------------------------

TEST( ShellNotShipped, AFreshLedgerIsEmpty )
{
	CShellNotShipped led;
	EXPECT_EQ( 0, led.Total() );
	EXPECT_EQ( 0, led.NameCount() );
}

TEST( ShellNotShipped, OneReachCountsOnceAndOpensOneRow )
{
	ResetWarnings();

	CShellNotShipped led;
	led.Reached( "a", "because" );

	EXPECT_EQ( 1, led.Total() );
	EXPECT_EQ( 1, led.NameCount() );
	EXPECT_STREQ( "a", led.NameAt( 0 ) );
	EXPECT_STREQ( "because", led.ReasonAt( 0 ) );
	EXPECT_EQ( 1, led.CountAt( 0 ) );
	EXPECT_EQ( 1, led.CountOf( "a" ) );
}

TEST( ShellNotShipped, ASecondReachOfOneNameOpensNoSecondRow )
{
	CShellNotShipped led;
	led.Reached( "a", "because" );
	led.Reached( "a", "because" );

	EXPECT_EQ( 2, led.Total() );
	EXPECT_EQ( 1, led.NameCount() );
	EXPECT_EQ( 2, led.CountOf( "a" ) );
}

TEST( ShellNotShipped, TwoNamesOpenTwoRows )
{
	CShellNotShipped led;
	led.Reached( "a", "one" );
	led.Reached( "b", "two" );
	led.Reached( "b", "two" );

	EXPECT_EQ( 3, led.Total() );
	EXPECT_EQ( 2, led.NameCount() );
	EXPECT_EQ( 1, led.CountOf( "a" ) );
	EXPECT_EQ( 2, led.CountOf( "b" ) );
}

TEST( ShellNotShipped, RowsAreMatchedByTextAndNotByPointer )
{
	// The names are string literals in a handful of translation units today, so
	// pointer identity would work today and would stop working silently the
	// moment one moved. Two distinct buffers holding the same text must be ONE
	// row.
	char szFirst[ 8 ];
	char szSecond[ 8 ];
	strcpy( szFirst,  "same" );
	strcpy( szSecond, "same" );
	ASSERT_NE( szFirst, szSecond );

	CShellNotShipped led;
	led.Reached( szFirst,  "r" );
	led.Reached( szSecond, "r" );

	EXPECT_EQ( 1, led.NameCount() );
	EXPECT_EQ( 2, led.CountOf( "same" ) );
}

TEST( ShellNotShipped, AnUnreachedNameCountsZero )
{
	CShellNotShipped led;
	led.Reached( "a", "r" );
	EXPECT_EQ( 0, led.CountOf( "b" ) );
}

TEST( ShellNotShipped, ANullNameStillCountsInTheTotal )
{
	// Losing the row is better than losing the number: the total is the gate.
	CShellNotShipped led;
	led.Reached( NULL, "r" );

	EXPECT_EQ( 1, led.Total() );
	EXPECT_EQ( 0, led.NameCount() );
	EXPECT_EQ( 0, led.CountOf( NULL ) );
}

TEST( ShellNotShipped, AnOutOfRangeRowAnswersNullRatherThanReadingPastTheTable )
{
	CShellNotShipped led;
	led.Reached( "a", "r" );

	EXPECT_TRUE( led.NameAt( -1 ) == NULL );
	EXPECT_TRUE( led.NameAt( 1 ) == NULL );
	EXPECT_TRUE( led.ReasonAt( 99 ) == NULL );
	EXPECT_EQ( 0, led.CountAt( -1 ) );
	EXPECT_EQ( 0, led.CountAt( 1 ) );
}

TEST( ShellNotShipped, TwoLedgersShareNothing )
{
	// The only way, from inside a gtest, to tell a member from the file-scope
	// static this project's rules forbid: if the count lived in a static, the
	// second ledger would see the first one's reach.
	CShellNotShipped a;
	CShellNotShipped b;

	a.Reached( "x", "r" );

	EXPECT_EQ( 1, a.Total() );
	EXPECT_EQ( 0, b.Total() );
}

TEST( ShellNotShipped, AFullTableKeepsCountingTheTotal )
{
	CShellNotShipped led;

	// One more distinct name than the table can hold. The names must outlive the
	// call, so they are held in a vector of std::string rather than built in a
	// loop-local buffer.
	std::vector< std::string > names;
	for ( int i = 0; i < CShellNotShipped::kMaxNames + 1; i++ )
	{
		char sz[ 16 ];
		snprintf( sz, sizeof( sz ), "n%d", i );
		names.push_back( std::string( sz ) );
	}

	for ( size_t i = 0; i < names.size(); i++ )
		led.Reached( names[ i ].c_str(), "r" );

	EXPECT_EQ( CShellNotShipped::kMaxNames + 1, led.Total() );
	EXPECT_EQ( CShellNotShipped::kMaxNames, led.NameCount() );
	EXPECT_EQ( 0, led.CountOf( names.back().c_str() ) )
		<< "the overflowed name should have no row, and its reach should still "
		   "be in the total";
}


//-----------------------------------------------------------------------------
// The warning.
//-----------------------------------------------------------------------------

TEST( ShellNotShipped, AReachWarnsWithItsNameReasonAndRunningTotal )
{
	ResetWarnings();

	CShellNotShipped led;
	led.Reached( "cap", "why not" );

	ASSERT_EQ( (size_t)1, g_Warnings.size() );
	EXPECT_EQ( "cap", g_Warnings[ 0 ].strName );
	EXPECT_EQ( "why not", g_Warnings[ 0 ].strReason );
	EXPECT_EQ( 1, g_Warnings[ 0 ].nTotal );
}

TEST( ShellNotShipped, ANameWarnsOnceHoweverOftenItIsReached )
{
	// The difference from CShellClientDllRefusals, which warns every time. These
	// sit on paths a stock translation unit can take every frame -- view.cpp's
	// HLTV branch is per view setup -- so an unlimited warning would bury the
	// first line, which is the one worth reading. The COUNT is unlimited.
	ResetWarnings();

	CShellNotShipped led;
	for ( int i = 0; i < 50; i++ )
		led.Reached( "hot", "r" );

	EXPECT_EQ( (size_t)1, g_Warnings.size() );
	EXPECT_EQ( 50, led.CountOf( "hot" ) );
	EXPECT_EQ( 50, led.Total() );
}

TEST( ShellNotShipped, EachNameGetsItsOwnFirstWarning )
{
	ResetWarnings();

	CShellNotShipped led;
	led.Reached( "a", "r" );
	led.Reached( "a", "r" );
	led.Reached( "b", "r" );

	ASSERT_EQ( (size_t)2, g_Warnings.size() );
	EXPECT_EQ( "a", g_Warnings[ 0 ].strName );
	EXPECT_EQ( "b", g_Warnings[ 1 ].strName );

	// The total carried by the second line is the RUNNING total, not that row's
	// count -- what a reader sees must be the number the readout will print.
	EXPECT_EQ( 3, g_Warnings[ 1 ].nTotal );
}

TEST( ShellNotShipped, AnOverflowedNameWarnsEveryTimeRatherThanNever )
{
	// A name with no row has no "first time" to remember, and the recoverable
	// failure is a repeated line rather than a silently swallowed one.
	ResetWarnings();

	CShellNotShipped led;
	std::vector< std::string > names;
	for ( int i = 0; i < CShellNotShipped::kMaxNames; i++ )
	{
		char sz[ 16 ];
		snprintf( sz, sizeof( sz ), "n%d", i );
		names.push_back( std::string( sz ) );
	}
	for ( size_t i = 0; i < names.size(); i++ )
		led.Reached( names[ i ].c_str(), "r" );

	const size_t nBefore = g_Warnings.size();
	ASSERT_EQ( (size_t)CShellNotShipped::kMaxNames, nBefore );

	led.Reached( "overflow", "r" );
	led.Reached( "overflow", "r" );

	EXPECT_EQ( nBefore + 2, g_Warnings.size() );
}


//-----------------------------------------------------------------------------
// The declared interface list.
//-----------------------------------------------------------------------------

TEST( ShellNotShippedInterfaces, TheListIsNotEmpty )
{
	// A list that lost its rows would make every lookup below answer NULL, and
	// the miss hook would report every declared absence as a broken export --
	// which is the opposite finding, and would look exactly like health in a
	// suite that only checked "an undeclared name is not found".
	int nCount = -1;
	const ShellNotShippedRow_t *pRows = ShellNotShipped_Interfaces( &nCount );

	ASSERT_TRUE( pRows != NULL );
	EXPECT_GE( nCount, 1 );
}

TEST( ShellNotShippedInterfaces, EveryRowCarriesANameAndAReason )
{
	int nCount = 0;
	const ShellNotShippedRow_t *pRows = ShellNotShipped_Interfaces( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		ASSERT_TRUE( pRows[ i ].pszName != NULL ) << "row " << i;
		ASSERT_TRUE( pRows[ i ].pszReason != NULL ) << "row " << i;
		EXPECT_GT( strlen( pRows[ i ].pszName ), (size_t)0 ) << "row " << i;
		EXPECT_GT( strlen( pRows[ i ].pszReason ), (size_t)8 )
			<< "row " << i << " (" << pRows[ i ].pszName << ") has no reason "
			<< "worth reading. A declared absence without an argument is the "
			<< "silent no-op with a table entry.";
	}
}

TEST( ShellNotShippedInterfaces, TheReplayInterfaceIsDeclared )
{
	// The one interface plan_entity.md step 7g takes out of client.so's export
	// set. Named rather than counted so that a row disappearing is a failure
	// about THIS interface and not about a total.
	const ShellNotShippedRow_t *pRow =
		ShellNotShipped_FindInterface( "ClientReplay001" );

	ASSERT_TRUE( pRow != NULL )
		<< "ClientReplay001 is no longer declared not-shipped. If replay is "
		   "back, the row should go with cdll_replay.cpp returning to the vpc; "
		   "if it is not, the engine's miss now reads as a broken export.";
	EXPECT_STREQ( "ClientReplay001", pRow->pszName );
}

TEST( ShellNotShippedInterfaces, AnUndeclaredNameIsNotFound )
{
	// The other half, and the one that makes the first mean something: a lookup
	// that matched anything would turn every broken export into a "deliberate"
	// absence.
	EXPECT_TRUE( ShellNotShipped_FindInterface( "VEngineServer023" ) == NULL );
	EXPECT_TRUE( ShellNotShipped_FindInterface( "" ) == NULL );
	EXPECT_TRUE( ShellNotShipped_FindInterface( NULL ) == NULL );
}

TEST( ShellNotShippedInterfaces, TheMatchIsExactAndNotASuffixOrPrefix )
{
	// THIS IS THE ONE THAT NEARLY WENT WRONG. "ClientReplay001" is a SUFFIX of
	// "EngineClientReplay001", the engine's own replay interface, and the two
	// really do collide: the linker tail-merges the shorter string into the
	// longer one, which is why `strings -a bin/linux64/engine.so | grep -x
	// ClientReplay001` reads ZERO on an engine whose code asks for it. A
	// substring match here would answer the replay row for a probe of the
	// ENGINE's interface, and a prefix match would answer it for anything
	// beginning "ClientReplay".
	EXPECT_TRUE( ShellNotShipped_FindInterface( "EngineClientReplay001" ) == NULL );
	EXPECT_TRUE( ShellNotShipped_FindInterface( "ClientReplay0011" ) == NULL );
	EXPECT_TRUE( ShellNotShipped_FindInterface( "ClientReplay" ) == NULL );
	EXPECT_TRUE( ShellNotShipped_FindInterface( "ClientReplay001" ) != NULL );
}

TEST( ShellNotShippedInterfaces, NoNameIsDeclaredTwice )
{
	// Two rows for one name would make the lookup answer the first reason and
	// leave the second unreachable -- a reason nobody could ever see, which is
	// the same failure as no reason at all.
	int nCount = 0;
	const ShellNotShippedRow_t *pRows = ShellNotShipped_Interfaces( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		for ( int j = i + 1; j < nCount; j++ )
		{
			EXPECT_STRNE( pRows[ i ].pszName, pRows[ j ].pszName )
				<< "declared twice at rows " << i << " and " << j;
		}
	}
}

TEST( ShellNotShippedInterfaces, ALookupHandsBackTheRowItFound )
{
	// Not a fresh copy: the miss hook stores pszName and pszReason in the
	// ledger without copying them, so they have to be the table's own storage.
	int nCount = 0;
	const ShellNotShippedRow_t *pRows = ShellNotShipped_Interfaces( &nCount );
	ASSERT_GE( nCount, 1 );

	const ShellNotShippedRow_t *pFound =
		ShellNotShipped_FindInterface( pRows[ 0 ].pszName );

	EXPECT_EQ( pRows[ 0 ].pszName, pFound->pszName );
	EXPECT_EQ( pRows[ 0 ].pszReason, pFound->pszReason );
}
