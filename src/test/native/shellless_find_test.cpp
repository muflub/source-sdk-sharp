//========= SourceSharp — collecting handles from a size-then-fill slot ======//
//
// plan_entity.md step 8e-entfire. `ent_fire` could not reach any entity with no
// edict — every point entity `ent_create` makes — because every native find in
// the build walks CEntInfo and a shell-less entity has no C++ object to have an
// entry. The fix asks MANAGED code, which holds the only index that has one.
//
// This file tests the one piece of that which is arithmetic: the collect, in
// sourcesharp/game/server/shellless_find.h. It is worth having off the game
// because THE
// CASE IT EXISTS FOR CANNOT BE REACHED FROM A MAP. The slot answers the TOTAL
// that matched and fills at most nOutMax, so a caller with a fixed buffer has a
// second path — more matches than the buffer holds — that needs more shell-less
// entities sharing one targetname than dm_lockdown has in total (81, across
// every class).
//
// An untested grow path here would be a SILENT TRUNCATION: the first 64
// entities take the input, the rest do not, and nothing anywhere says so. That
// is the exact shape of the defect this step closes, so the fix is not allowed
// to re-introduce it one buffer along.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/shellless_find.h"

#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// A stand-in for CUtlVector, with the three members the collect and its caller
// use.
//
// FIVE LINES AND NOT A MOCK: the template parameter exists so that this test
// does not have to link tier1 (standalone_names.h's reason for avoiding
// Q_stricmp, one file over). It has to AGREE with CUtlVector on all three
// members, and the agreement is READ OFF THE NATIVE SOURCE rather than
// assumed, because a stand-in that drifts from the thing it stands in for
// fails nothing:
//
//   public/tier1/utlvector.h:1129  SetSize( n )  -> SetCount( n )
//                            1122  SetCount( n ) -> RemoveAll(); AddMultipleToTail( n )
//                             785  Count()       -> m_Size
//
// so after SetSize(n) the vector holds exactly n elements and Base() answers
// the first, which is what assign(n, 0) and &m_Storage[0] do here.
//-----------------------------------------------------------------------------
struct Overflow
{
	std::vector< uint32_t > m_Storage;

	void SetSize( int n ) { m_Storage.assign( (size_t)n, 0u ); }
	uint32_t *Base() { return m_Storage.empty() ? NULL : &m_Storage[ 0 ]; }
	int Count() const { return (int)m_Storage.size(); }
};

//-----------------------------------------------------------------------------
// A finder with a settable population, recording what it was asked.
//
// It obeys the slot's contract exactly: fills min(population, nOutMax) and
// answers the population whatever the buffer was. The handles it hands out are
// 2048 + i, which is the engine-invisible range these entities really live in
// (EntityDirectory.FirstInvisibleEntry), so a test that accidentally read a
// zero would be reading a value the real slot cannot produce.
//-----------------------------------------------------------------------------
int g_nPopulation = 0;
int g_nCalls = 0;
int g_nLastMax = -1;

// Set non-zero to make the SECOND ask answer fewer than the first did.
int g_nSecondAskPopulation = -1;

int FakeFind( const char *pQuery, uint32_t *pOut, int nOutMax )
{
	g_nCalls++;
	g_nLastMax = nOutMax;

	const int nPopulation = ( g_nCalls >= 2 && g_nSecondAskPopulation >= 0 )
		? g_nSecondAskPopulation
		: g_nPopulation;

	if ( pOut && nOutMax > 0 )
	{
		const int nFill = ( nPopulation < nOutMax ) ? nPopulation : nOutMax;

		for ( int i = 0; i < nFill; i++ )
			pOut[ i ] = (uint32_t)( 2048 + i );
	}

	(void)pQuery;
	return nPopulation;
}

void Reset( int nPopulation )
{
	g_nPopulation = nPopulation;
	g_nCalls = 0;
	g_nLastMax = -1;
	g_nSecondAskPopulation = -1;
}

} // namespace


//=============================================================================//
// The ordinary case: everything fits, one ask, nothing allocated.
//=============================================================================//

TEST( ShellLessFindCollect, AFittingPopulationIsAnsweredInOneAsk )
{
	Reset( 3 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	EXPECT_EQ( 3, ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow ) );
	EXPECT_EQ( 1, g_nCalls );
}

TEST( ShellLessFindCollect, AFittingPopulationLeavesTheOverflowEmpty )
{
	Reset( 3 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow );

	// WHICH BUFFER IS THE OVERFLOW'S OWN ANSWER, and the caller reads it that
	// way rather than comparing the count against kShellLessFindInline. An
	// empty overflow is how "the handles are in the inline buffer" is said.
	EXPECT_EQ( 0, overflow.Count() );
}

TEST( ShellLessFindCollect, AFittingPopulationLandsInTheInlineBuffer )
{
	Reset( 3 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow );

	EXPECT_EQ( 2048u, inlineBuf[ 0 ] );
	EXPECT_EQ( 2049u, inlineBuf[ 1 ] );
	EXPECT_EQ( 2050u, inlineBuf[ 2 ] );
}

TEST( ShellLessFindCollect, TheFirstAskOffersTheWholeInlineBuffer )
{
	Reset( 3 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow );

	EXPECT_EQ( (int)kShellLessFindInline, g_nLastMax );
}

TEST( ShellLessFindCollect, ExactlyTheInlineCapacityStillTakesOneAsk )
{
	// The boundary. An off-by-one here allocates and asks twice for a
	// population that fitted, which is invisible: the answer is right either
	// way.
	Reset( (int)kShellLessFindInline );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	EXPECT_EQ( (int)kShellLessFindInline,
	           ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow ) );
	EXPECT_EQ( 1, g_nCalls );
	EXPECT_EQ( 0, overflow.Count() );
}


//=============================================================================//
// The grow: more matches than the inline buffer holds. THE PATH A MAP CANNOT
// REACH, which is why this file exists.
//=============================================================================//

TEST( ShellLessFindCollect, MoreThanFitsAsksASecondTime )
{
	Reset( (int)kShellLessFindInline + 1 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow );

	EXPECT_EQ( 2, g_nCalls );
}

TEST( ShellLessFindCollect, MoreThanFitsAnswersTheWholePopulation )
{
	Reset( (int)kShellLessFindInline + 7 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	// NOT kShellLessFindInline. Answering what fitted is the silent truncation
	// this whole step exists to end.
	EXPECT_EQ( (int)kShellLessFindInline + 7,
	           ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow ) );
}

TEST( ShellLessFindCollect, MoreThanFitsPutsThemAllInTheOverflow )
{
	const int nPopulation = (int)kShellLessFindInline + 7;
	Reset( nPopulation );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow );

	ASSERT_EQ( nPopulation, overflow.Count() );
	EXPECT_EQ( 2048u, overflow.Base()[ 0 ] );
	EXPECT_EQ( (uint32_t)( 2048 + nPopulation - 1 ), overflow.Base()[ nPopulation - 1 ] );
}

TEST( ShellLessFindCollect, TheSecondAskOffersTheWholePopulation )
{
	Reset( (int)kShellLessFindInline + 7 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow );

	EXPECT_EQ( (int)kShellLessFindInline + 7, g_nLastMax );
}

TEST( ShellLessFindCollect, APopulationThatShrankBetweenTheAsksIsReportedSmaller )
{
	// The race, made deterministic. Reading the FIRST total and trusting the
	// second fill would walk past what was actually written -- handles that are
	// whatever the allocation happened to contain, delivered as inputs.
	Reset( (int)kShellLessFindInline + 7 );
	g_nSecondAskPopulation = 3;

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	EXPECT_EQ( 3, ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow ) );
}

TEST( ShellLessFindCollect, APopulationThatShrankToNothingAnswersZero )
{
	Reset( (int)kShellLessFindInline + 7 );
	g_nSecondAskPopulation = 0;

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	EXPECT_EQ( 0, ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow ) );
}


//=============================================================================//
// The refusals. Every one of them answers 0 and asks nothing, which is the
// state the build was in before this step -- so a fault degrades to the old
// behaviour rather than to a wild pointer.
//=============================================================================//

TEST( ShellLessFindCollect, NothingMatchingAnswersZero )
{
	Reset( 0 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	EXPECT_EQ( 0, ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow ) );
	EXPECT_EQ( 0, overflow.Count() );
}

TEST( ShellLessFindCollect, ANullFinderAnswersZeroAndAsksNothing )
{
	Reset( 5 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	EXPECT_EQ( 0, ShellLessFind_Collect( NULL, "shaker", inlineBuf, overflow ) );
	EXPECT_EQ( 0, g_nCalls );
}

TEST( ShellLessFindCollect, ANullQueryAnswersZeroAndAsksNothing )
{
	Reset( 5 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	EXPECT_EQ( 0, ShellLessFind_Collect( &FakeFind, NULL, inlineBuf, overflow ) );
	EXPECT_EQ( 0, g_nCalls );
}

TEST( ShellLessFindCollect, AnEmptyQueryAnswersZeroAndAsksNothing )
{
	Reset( 5 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	EXPECT_EQ( 0, ShellLessFind_Collect( &FakeFind, "", inlineBuf, overflow ) );
	EXPECT_EQ( 0, g_nCalls );
}

TEST( ShellLessFindCollect, ANullInlineBufferAnswersZeroAndAsksNothing )
{
	Reset( 5 );

	Overflow overflow;

	EXPECT_EQ( 0, ShellLessFind_Collect( &FakeFind, "shaker", (uint32_t *)NULL, overflow ) );
	EXPECT_EQ( 0, g_nCalls );
}

TEST( ShellLessFindCollect, ARefusalFromTheFinderAnswersZero )
{
	// The slot answers 0 when there is no managed side at all -- early startup,
	// and after an unload. No shell-less entity can exist then, so 0 is the
	// true answer rather than a degraded one.
	Reset( -1 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	Overflow overflow;

	EXPECT_EQ( 0, ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow ) );
}

//=============================================================================//
// THE GAME'S OWN OVERFLOW BUFFER. standalone_relays.cpp passes
// ShellLessOverflow (shellless_find.h), a std::vector, where it passed a
// CUtlVector; these run the collect against it rather than against the
// stand-in above, over both paths.
//=============================================================================//

TEST( ShellLessFindCollect, TheGamesOverflowStaysEmptyWhenThePopulationFits )
{
	Reset( 3 );

	uint32_t inlineBuf[ kShellLessFindInline ];
	ShellLessOverflow overflow;

	EXPECT_EQ( 3, ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow ) );
	EXPECT_EQ( 0, overflow.Count() );
	EXPECT_TRUE( overflow.Base() == NULL );
}

TEST( ShellLessFindCollect, TheGamesOverflowHoldsEveryHandleOfALargePopulation )
{
	const int nMany = (int)kShellLessFindInline + 36;
	Reset( nMany );

	uint32_t inlineBuf[ kShellLessFindInline ];
	ShellLessOverflow overflow;

	ASSERT_EQ( nMany, ShellLessFind_Collect( &FakeFind, "shaker", inlineBuf, overflow ) );
	ASSERT_EQ( nMany, overflow.Count() );
	ASSERT_TRUE( overflow.Base() != NULL );
	for ( int i = 0; i < nMany; i++ )
		EXPECT_EQ( (uint32_t)( 2048 + i ), overflow.Base()[ i ] ) << "handle " << i;
}
