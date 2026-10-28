// Collects the handles a size-then-fill shell-less finder answers, growing into an
// overflow buffer when they don't fit inline. Engine-free so it can be tested.
#pragma once

#include <stdint.h>

#include <vector>

// Handles collected without allocating; more costs a second call and an allocation.
enum { kShellLessFindInline = 64 };

// Fills up to nOutMax handles and answers the total that matched. pOut may be
// NULL when nOutMax is 0.
typedef int (*ShellLessFindFn)( const char *pQuery, uint32_t *pOut, int nOutMax );

// The overflow buffer, with CUtlVector's SetSize/Base/Count meaning.
struct ShellLessOverflow
{
	std::vector< uint32_t > m_Storage;

	void SetSize( int n ) { m_Storage.assign( (size_t)n, 0u ); }
	uint32_t *Base() { return m_Storage.empty() ? NULL : &m_Storage[ 0 ]; }
	int Count() const { return (int)m_Storage.size(); }
};

// Collects every handle that answers to pQuery and returns how many. They are in
// pInlineBuf unless overflow came back non-empty; callers must test that, not the
// count, because the second call can answer fewer than the first. The second
// call's answer is the one returned, clamped to the buffer. A null finder or an
// empty query answers 0.
template < typename OverflowT >
inline int ShellLessFind_Collect( ShellLessFindFn pfnFind, const char *pQuery,
                                  uint32_t *pInlineBuf, OverflowT &overflow )
{
	if ( !pfnFind || !pQuery || !pQuery[ 0 ] || !pInlineBuf )
		return 0;

	const int nTotal = pfnFind( pQuery, pInlineBuf, (int)kShellLessFindInline );

	if ( nTotal <= 0 )
		return 0;

	if ( nTotal <= (int)kShellLessFindInline )
		return nTotal;

	overflow.SetSize( nTotal );

	const int nAgain = pfnFind( pQuery, overflow.Base(), nTotal );

	if ( nAgain <= 0 )
		return 0;

	return ( nAgain < nTotal ) ? nAgain : nTotal;
}
