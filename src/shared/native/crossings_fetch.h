// Native half of the CrossingsReport slot: the size-then-fill loop, kept
// engine-free so it can be tested.
#pragma once

#include <stdlib.h>

// The CrossingsReport slot's shape.
typedef int ( *SourceSharpCrossingsReportFn )( char *pBuffer, int nBytes, int bReset );

enum SourceSharpCrossingsStatus
{
	// pOut holds a NUL-terminated report the caller must free().
	SOURCESHARP_CROSSINGS_OK = 0,

	SOURCESHARP_CROSSINGS_NO_SLOT = 1,

	// The slot answered -1: it could not encode the report at all.
	SOURCESHARP_CROSSINGS_REFUSED = 2,

	// The slot answered a size at or below the terminator alone.
	SOURCESHARP_CROSSINGS_EMPTY = 3,

	// A size past SOURCESHARP_CROSSINGS_MAX_BYTES; the allocation is refused.
	SOURCESHARP_CROSSINGS_TOO_BIG = 4,

	SOURCESHARP_CROSSINGS_NO_MEMORY = 5,

	// It sized N and then would not fill N.
	SOURCESHARP_CROSSINGS_GREW = 6,
};

// A bound on nonsense, far above any real report.
#define SOURCESHARP_CROSSINGS_MAX_BYTES ( 256 * 1024 )

// Sizes, allocates, fills. bReset goes on the filling call only. On OK, *ppOut is
// a NUL-terminated buffer the caller frees; otherwise it is NULL.
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

	// Pre-terminated, so a slot that writes nothing yields an empty string.
	pBuffer[ 0 ] = '\0';
	pBuffer[ nNeeded - 1 ] = '\0';

	const int nFilled = pFn( pBuffer, nNeeded, bReset ? 1 : 0 );

	if ( nFilled != nNeeded )
	{
		free( pBuffer );
		return nFilled < 0 ? SOURCESHARP_CROSSINGS_REFUSED : SOURCESHARP_CROSSINGS_GREW;
	}

	// Do not trust the far side to terminate.
	pBuffer[ nNeeded - 1 ] = '\0';

	*ppOut = pBuffer;
	return SOURCESHARP_CROSSINGS_OK;
}

// One sentence per failure, for the console. Never NULL.
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
