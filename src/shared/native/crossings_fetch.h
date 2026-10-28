//========= SourceSharp — reading the crossing counters =======================//
//
// plan_entity.md step 0c. Managed counts every native->managed thunk entry
// (SourceSharp.Abi/Crossings.cs) and hands the table back through ONE ABI slot,
// CrossingsReport, as text. This is the native half of that call.
//
// WHY IT IS A HEADER AND NOT PART OF shell_diagnostics.cpp. The interesting
// part of this call is not the printing, it is the SIZE-THEN-FILL LOOP: a
// contract with four failure modes (refused, empty, absurd, grew between the
// two calls), three of which produce a plausible-looking short report if they
// are read as success. shell_diagnostics.cpp includes cbase.h and cannot be
// compiled into a gtest; this can, and crossings_fetch_test.cpp does exactly
// that with a fake slot per failure mode. The alternative was a loop nothing
// could test, guarding the instrument every later phase of the port is
// measured with.
//
// Engine-free on purpose: <stdlib.h> and nothing else. Anything from tier0 or
// tier1 in here would take the test's compile with it.
//
//=============================================================================//
#pragma once

#include <stdlib.h>

//-----------------------------------------------------------------------------
// The CrossingsReport slot's shape, named so the loop below can be handed a
// fake. Matches sourcesharp.h:
//
//   int32_t (*CrossingsReport)( char *pBuffer, int32_t nBytes, int32_t bReset );
//-----------------------------------------------------------------------------
typedef int ( *SourceSharpCrossingsReportFn )( char *pBuffer, int nBytes, int bReset );

//-----------------------------------------------------------------------------
// WHY THE FETCH FAILED, or that it did not.
//
// Enumerated rather than folded into a NULL return, because the four failures
// want four different sentences in the console and "no report" is not one of
// them: a build with no managed side, a managed side that refused to encode,
// and a managed side that answered a size it then would not fill are three
// different bugs, and a caller that printed one message for all three would
// send whoever read it looking in the wrong place.
//-----------------------------------------------------------------------------
enum SourceSharpCrossingsStatus
{
	// pOut holds a NUL-terminated report the caller must free().
	SOURCESHARP_CROSSINGS_OK = 0,

	// No managed side, or a managed side too old to have the slot. Not an
	// error: a native-only build has no managed crossings to count.
	SOURCESHARP_CROSSINGS_NO_SLOT = 1,

	// The slot answered -1: it could not encode the report at all.
	SOURCESHARP_CROSSINGS_REFUSED = 2,

	// The slot answered a size at or below the terminator alone, so there is no
	// report -- distinct from REFUSED, which is a managed fault.
	SOURCESHARP_CROSSINGS_EMPTY = 3,

	// A size past the cap. The report is bounded by the op count, so this means
	// the slot is answering nonsense and the allocation is refused rather than
	// attempted.
	SOURCESHARP_CROSSINGS_TOO_BIG = 4,

	// malloc said no.
	SOURCESHARP_CROSSINGS_NO_MEMORY = 5,

	// It sized N and then would not fill N. The size-then-fill contract says
	// this cannot happen; treating it as success is how a truncated table gets
	// read as a whole one, so it is a refusal.
	SOURCESHARP_CROSSINGS_GREW = 6,
};

//-----------------------------------------------------------------------------
// The cap. The report is one line per op ENTERED, of at most 162 ops, plus
// three lines of header -- call it 16 KB with every op live and every count
// wide. 256 KB is two orders of margin over that, which is the point: it is a
// bound on ABSURDITY, not a bound on the report, so it can never be the reason
// a real report does not print.
//-----------------------------------------------------------------------------
#define SOURCESHARP_CROSSINGS_MAX_BYTES ( 256 * 1024 )

//-----------------------------------------------------------------------------
// Sizes, allocates, fills.
//
// bReset is passed on the FILLING call only. Managed ignores it on a sizing
// call anyway (Crossings.WriteReport says why), but sending 0 on the first call
// makes this side's intent legible rather than resting on the far side's
// promise -- and it is the far side that a version skew would change.
//
// On SOURCESHARP_CROSSINGS_OK, *ppOut is a NUL-terminated buffer the caller
// frees. On anything else *ppOut is NULL.
//-----------------------------------------------------------------------------
inline SourceSharpCrossingsStatus SourceSharp_FetchCrossingsReport(
	SourceSharpCrossingsReportFn pFn, bool bReset, char **ppOut )
{
	if ( ppOut == NULL )
		return SOURCESHARP_CROSSINGS_NO_SLOT;

	*ppOut = NULL;

	if ( pFn == NULL )
		return SOURCESHARP_CROSSINGS_NO_SLOT;

	const int nNeeded = pFn( NULL, 0, 0 );

	if ( nNeeded < 0 )
		return SOURCESHARP_CROSSINGS_REFUSED;

	// 1 is the terminator on its own: a report of no characters.
	if ( nNeeded <= 1 )
		return SOURCESHARP_CROSSINGS_EMPTY;

	if ( nNeeded > SOURCESHARP_CROSSINGS_MAX_BYTES )
		return SOURCESHARP_CROSSINGS_TOO_BIG;

	char *pBuffer = (char *)malloc( (size_t)nNeeded );

	if ( pBuffer == NULL )
		return SOURCESHARP_CROSSINGS_NO_MEMORY;

	// Pre-terminated, so a slot that answers a size and writes nothing produces
	// an empty string rather than a walk off the end of the allocation.
	pBuffer[ 0 ] = '\0';
	pBuffer[ nNeeded - 1 ] = '\0';

	const int nFilled = pFn( pBuffer, nNeeded, bReset ? 1 : 0 );

	if ( nFilled != nNeeded )
	{
		free( pBuffer );
		return nFilled < 0 ? SOURCESHARP_CROSSINGS_REFUSED : SOURCESHARP_CROSSINGS_GREW;
	}

	// The far side terminates; this side does not trust it to. A report that
	// filled every byte and forgot the NUL would otherwise be printed off the
	// end of the buffer.
	pBuffer[ nNeeded - 1 ] = '\0';

	*ppOut = pBuffer;
	return SOURCESHARP_CROSSINGS_OK;
}

//-----------------------------------------------------------------------------
// One sentence per failure, for the console. Never NULL, so a caller cannot
// print a null format argument by forgetting a case.
//-----------------------------------------------------------------------------
inline const char *SourceSharp_CrossingsStatusText( SourceSharpCrossingsStatus status )
{
	switch ( status )
	{
	case SOURCESHARP_CROSSINGS_OK:
		return "ok";
	case SOURCESHARP_CROSSINGS_NO_SLOT:
		return "no managed side is loaded, so nothing has crossed the boundary";
	case SOURCESHARP_CROSSINGS_REFUSED:
		return "the managed side refused to encode the report";
	case SOURCESHARP_CROSSINGS_EMPTY:
		return "the managed side answered an empty report";
	case SOURCESHARP_CROSSINGS_TOO_BIG:
		return "the managed side asked for an absurd buffer; refused";
	case SOURCESHARP_CROSSINGS_NO_MEMORY:
		return "out of memory sizing the report";
	case SOURCESHARP_CROSSINGS_GREW:
		return "the managed side sized the report and then would not fill it";
	}

	return "unknown failure";
}
