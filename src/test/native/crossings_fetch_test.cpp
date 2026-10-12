//========= SourceSharp — the native half of the crossing counters ============//
//
// plan_entity.md step 0c. Managed counts every native->managed thunk entry and
// hands the table back through ONE ABI slot, CrossingsReport, as text.
// crossings_fetch.h is the loop that calls it, and this is that loop's test.
//
// WHAT IS ACTUALLY AT RISK HERE. Not the printing -- the SIZE-THEN-FILL
// CONTRACT, which has four failure modes and three of them produce a
// plausible-looking short report if they are read as success: a slot that
// refuses, a slot that answers an absurd size, and a slot that sizes N and then
// will not fill N. The counters are the instrument every later phase of the
// entity port is measured with, so a truncated table read as a whole one is a
// budget met on paper.
//
// The header is engine-free for exactly this reason: shell_diagnostics.cpp
// includes cbase.h and cannot be compiled into a gtest, so the loop lives in a
// header both it and this file can take.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <string.h>

#include "crossings_fetch.h"

//-----------------------------------------------------------------------------
// The fakes. A slot is a plain function pointer, so each failure mode is one
// function -- no mocking framework, and each fake is legible as the thing it
// stands for.
//
// File-scope state rather than captures: the typedef is a C function pointer
// and a capturing lambda cannot become one.
//-----------------------------------------------------------------------------
namespace
{

const char *g_pText = "[SourceSharp] inbound ABI crossings\n";

int g_nSizingCalls = 0;
int g_nFillingCalls = 0;
int g_nLastReset = -1;
int g_nSizedWithReset = -1;

void ResetFakes()
{
	g_nSizingCalls = 0;
	g_nFillingCalls = 0;
	g_nLastReset = -1;
	g_nSizedWithReset = -1;
}

// The honest slot: answers the size, fills on the second call, records the
// reset flag it was given each time.
int FakeGood( char *pBuffer, int nBytes, int bReset )
{
	const int nNeeded = (int)strlen( g_pText ) + 1;

	if ( pBuffer == NULL || nBytes < nNeeded )
	{
		g_nSizingCalls++;
		g_nSizedWithReset = bReset;
		return nNeeded;
	}

	g_nFillingCalls++;
	g_nLastReset = bReset;
	memcpy( pBuffer, g_pText, (size_t)nNeeded );
	return nNeeded;
}

int FakeRefuses( char *, int, int )
{
	return -1;
}

// Answers 1: the terminator alone, which is a report of no characters.
int FakeEmpty( char *pBuffer, int nBytes, int )
{
	if ( pBuffer != NULL && nBytes >= 1 )
		pBuffer[ 0 ] = '\0';

	return 1;
}

int FakeZero( char *, int, int )
{
	return 0;
}

int FakeAbsurd( char *, int, int )
{
	return SOURCESHARP_CROSSINGS_MAX_BYTES + 1;
}

// Sizes, then answers a DIFFERENT size on the filling call -- the contract
// violation that would otherwise leave a half-written table looking whole.
int FakeGrew( char *pBuffer, int nBytes, int )
{
	const int nNeeded = (int)strlen( g_pText ) + 1;

	if ( pBuffer == NULL || nBytes < nNeeded )
		return nNeeded;

	// Writes something plausible AND answers the wrong length.
	memcpy( pBuffer, g_pText, (size_t)nNeeded );
	return nNeeded + 1;
}

// Answers the right size and writes nothing at all. The buffer must still be a
// valid C string afterwards or the caller prints off the end of it.
int FakeWritesNothing( char *, int, int )
{
	return (int)strlen( g_pText ) + 1;
}

// Fills every byte and forgets the terminator.
int FakeForgetsTheNul( char *pBuffer, int nBytes, int )
{
	const int nNeeded = (int)strlen( g_pText ) + 1;

	if ( pBuffer == NULL || nBytes < nNeeded )
		return nNeeded;

	memset( pBuffer, 'x', (size_t)nNeeded );
	return nNeeded;
}

} // namespace

TEST( CrossingsFetch, AGoodSlotAnswersTheReport )
{
	ResetFakes();

	char *pOut = NULL;
	EXPECT_EQ( SOURCESHARP_CROSSINGS_OK,
	           SourceSharp_FetchCrossingsReport( FakeGood, false, &pOut ) );
	ASSERT_TRUE( pOut != NULL );
	EXPECT_STREQ( g_pText, pOut );
	free( pOut );
}

TEST( CrossingsFetch, ItSizesBeforeItFills )
{
	ResetFakes();

	char *pOut = NULL;
	SourceSharp_FetchCrossingsReport( FakeGood, false, &pOut );
	free( pOut );

	EXPECT_EQ( 1, g_nSizingCalls );
	EXPECT_EQ( 1, g_nFillingCalls );
}

//-----------------------------------------------------------------------------
// THE ONE THAT MAKES THE TWO-CALL SHAPE SAFE TO COMBINE WITH A RESET.
//
// A reset honoured on the SIZING call would zero the counters between sizing
// and filling, and the report that came back would describe the empty interval
// rather than the one being closed. Managed ignores bReset on a sizing call
// (Crossings.WriteReport), but this side sends 0 anyway rather than resting on
// the far side's promise -- it is the far side that a version skew changes.
//-----------------------------------------------------------------------------
TEST( CrossingsFetch, TheSizingCallNeverAsksForAReset )
{
	ResetFakes();

	char *pOut = NULL;
	SourceSharp_FetchCrossingsReport( FakeGood, true, &pOut );
	free( pOut );

	EXPECT_EQ( 0, g_nSizedWithReset );
}

TEST( CrossingsFetch, TheFillingCallCarriesTheReset )
{
	ResetFakes();

	char *pOut = NULL;
	SourceSharp_FetchCrossingsReport( FakeGood, true, &pOut );
	free( pOut );

	EXPECT_EQ( 1, g_nLastReset );
}

TEST( CrossingsFetch, TheFillingCallCarriesNoResetWhenNoneWasAsked )
{
	ResetFakes();

	char *pOut = NULL;
	SourceSharp_FetchCrossingsReport( FakeGood, false, &pOut );
	free( pOut );

	EXPECT_EQ( 0, g_nLastReset );
}

TEST( CrossingsFetch, ANullSlotIsNotAnError )
{
	char *pOut = NULL;
	EXPECT_EQ( SOURCESHARP_CROSSINGS_NO_SLOT,
	           SourceSharp_FetchCrossingsReport( NULL, false, &pOut ) );
	EXPECT_TRUE( pOut == NULL );
}

TEST( CrossingsFetch, ARefusalIsReportedAsARefusal )
{
	char *pOut = NULL;
	EXPECT_EQ( SOURCESHARP_CROSSINGS_REFUSED,
	           SourceSharp_FetchCrossingsReport( FakeRefuses, false, &pOut ) );
	EXPECT_TRUE( pOut == NULL );
}

// Distinct from a refusal: a managed side with nothing to say is not a managed
// side that broke, and one message for both would send the reader looking in
// the wrong place.
TEST( CrossingsFetch, ATerminatorAloneIsEmptyAndNotARefusal )
{
	char *pOut = NULL;
	EXPECT_EQ( SOURCESHARP_CROSSINGS_EMPTY,
	           SourceSharp_FetchCrossingsReport( FakeEmpty, false, &pOut ) );
}

TEST( CrossingsFetch, AZeroSizeIsEmpty )
{
	char *pOut = NULL;
	EXPECT_EQ( SOURCESHARP_CROSSINGS_EMPTY,
	           SourceSharp_FetchCrossingsReport( FakeZero, false, &pOut ) );
}

TEST( CrossingsFetch, AnAbsurdSizeIsRefusedRatherThanAllocated )
{
	char *pOut = NULL;
	EXPECT_EQ( SOURCESHARP_CROSSINGS_TOO_BIG,
	           SourceSharp_FetchCrossingsReport( FakeAbsurd, false, &pOut ) );
	EXPECT_TRUE( pOut == NULL );
}

// The contract says this cannot happen. Reading it as success is how a
// truncated table gets printed as a whole one, so it is a refusal.
TEST( CrossingsFetch, ASlotThatGrewBetweenTheTwoCallsIsRefused )
{
	char *pOut = NULL;
	EXPECT_EQ( SOURCESHARP_CROSSINGS_GREW,
	           SourceSharp_FetchCrossingsReport( FakeGrew, false, &pOut ) );
	EXPECT_TRUE( pOut == NULL );
}

// The buffer is pre-terminated before the filling call, so a slot that writes
// nothing produces an empty string rather than a walk off the end of a fresh
// malloc.
TEST( CrossingsFetch, ASlotThatWritesNothingLeavesAValidString )
{
	char *pOut = NULL;
	ASSERT_EQ( SOURCESHARP_CROSSINGS_OK,
	           SourceSharp_FetchCrossingsReport( FakeWritesNothing, false, &pOut ) );
	ASSERT_TRUE( pOut != NULL );
	EXPECT_STREQ( "", pOut );
	free( pOut );
}

// This side terminates the buffer itself rather than trusting the far side to.
TEST( CrossingsFetch, TheReportIsTerminatedWhateverTheSlotWrote )
{
	char *pOut = NULL;
	ASSERT_EQ( SOURCESHARP_CROSSINGS_OK,
	           SourceSharp_FetchCrossingsReport( FakeForgetsTheNul, false, &pOut ) );
	ASSERT_TRUE( pOut != NULL );
	EXPECT_EQ( strlen( g_pText ), strlen( pOut ) );
	free( pOut );
}

TEST( CrossingsFetch, ANullOutPointerIsRefusedRatherThanWritten )
{
	EXPECT_EQ( SOURCESHARP_CROSSINGS_NO_SLOT,
	           SourceSharp_FetchCrossingsReport( FakeGood, false, NULL ) );
}

//-----------------------------------------------------------------------------
// Every status has a sentence, and no two share one.
//
// A caller prints this straight into the console with %s, so a NULL for an
// unhandled case is a crash in the diagnostic -- and two statuses sharing a
// sentence would send whoever read it looking in the wrong place, which is the
// whole reason the enum has six members instead of a bool.
//-----------------------------------------------------------------------------
TEST( CrossingsFetch, EveryStatusHasItsOwnSentence )
{
	static const SourceSharpCrossingsStatus kAll[] = {
		SOURCESHARP_CROSSINGS_OK,
		SOURCESHARP_CROSSINGS_NO_SLOT,
		SOURCESHARP_CROSSINGS_REFUSED,
		SOURCESHARP_CROSSINGS_EMPTY,
		SOURCESHARP_CROSSINGS_TOO_BIG,
		SOURCESHARP_CROSSINGS_NO_MEMORY,
		SOURCESHARP_CROSSINGS_GREW,
	};

	const size_t nAll = sizeof( kAll ) / sizeof( kAll[ 0 ] );

	for ( size_t i = 0; i < nAll; i++ )
	{
		const char *pText = SourceSharp_CrossingsStatusText( kAll[ i ] );
		ASSERT_TRUE( pText != NULL ) << "status " << (int)kAll[ i ] << " has no sentence";
		EXPECT_GT( strlen( pText ), 1u ) << "status " << (int)kAll[ i ] << " has an empty sentence";

		for ( size_t j = 0; j < i; j++ )
		{
			EXPECT_STRNE( SourceSharp_CrossingsStatusText( kAll[ j ] ), pText )
				<< "statuses " << (int)kAll[ j ] << " and " << (int)kAll[ i ]
				<< " share one sentence, so the console cannot tell them apart";
		}
	}
}

TEST( CrossingsFetch, AnUnknownStatusStillHasASentence )
{
	const char *pText = SourceSharp_CrossingsStatusText( (SourceSharpCrossingsStatus)99 );
	ASSERT_TRUE( pText != NULL );
	EXPECT_STREQ( "unknown failure", pText );
}
