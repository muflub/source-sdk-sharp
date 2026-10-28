//========= SourceSharp — collecting handles from a size-then-fill slot ======//
//
// The ONE piece of `ent_fire`'s shell-less pass that is arithmetic rather than
// plumbing (plan_entity.md step 8e-entfire), pulled out so it can be tested
// without a running Source — the same reason `ent_fire_access.h` and
// `standalone_names.h` are their own headers.
//
// It is worth testing on its own because the case it exists for CANNOT BE
// REACHED FROM A MAP. `ShellLessFindByName` answers the TOTAL that matched and
// fills at most `nOutMax`, so a caller with a fixed buffer has two paths and
// the second one — more matches than the buffer holds — needs more shell-less
// entities sharing one targetname than any map has. An untested grow path here
// would be a silent truncation: the first N entities take the input, the rest
// do not, and nothing anywhere says so. That is precisely the shape of defect
// this step exists to close, so it is not allowed to be introduced by the fix.
//
// No engine headers, and no tier1 either: the overflow container is a template
// parameter. The game hands it ShellLessOverflow below (a std::vector, so the
// caller instantiates no CUtlMemory and imports no g_pMemAlloc); the test hands
// it the same and its own stand-in.
//
//=============================================================================//
#pragma once

#include <stdint.h>

#include <vector>

//-----------------------------------------------------------------------------
// How many handles are collected without allocating.
//
// 64 because that is comfortably more than any real query answers — the
// measured map, dm_lockdown, holds 81 shell-less entities IN TOTAL across every
// class — while still being a small stack frame in the event queue's inner
// loop. Exceeding it costs one extra crossing and one allocation, which is the
// right trade for a case that should not happen rather than a truncation.
//-----------------------------------------------------------------------------
enum { kShellLessFindInline = 64 };

//-----------------------------------------------------------------------------
// The slot's own shape: fill up to nOutMax handles, answer the TOTAL that
// matched. pOut may be NULL when nOutMax is 0, which makes the same call a
// query.
//-----------------------------------------------------------------------------
typedef int (*ShellLessFindFn)( const char *pQuery, uint32_t *pOut, int nOutMax );

//-----------------------------------------------------------------------------
// The overflow buffer the game passes: the three CUtlVector members the collect
// and its caller use, with CUtlVector's meaning (SetSize(n) leaves exactly n
// elements; Base() is the first, NULL when empty; Count() is the size).
//-----------------------------------------------------------------------------
struct ShellLessOverflow
{
	std::vector< uint32_t > m_Storage;

	void SetSize( int n ) { m_Storage.assign( (size_t)n, 0u ); }
	uint32_t *Base() { return m_Storage.empty() ? NULL : &m_Storage[ 0 ]; }
	int Count() const { return (int)m_Storage.size(); }
};

//-----------------------------------------------------------------------------
// Collect every handle that answers to pQuery.
//
// Answers how many there are. They are in `inlineBuf` unless `overflow` came
// back non-empty, in which case they are in THAT — which is the test the caller
// must make, and not a comparison of the count against kShellLessFindInline.
// The two are not the same test: the second ask can answer FEWER than the first
// (see below), so a count of 3 can legitimately be sitting in the overflow
// buffer, and a caller keying on the count would read three stale handles out
// of the inline one.
//
// `overflow` is grown to fit and refilled by a SECOND call — the finder is
// stateless, so asking again with a bigger buffer is the whole of the grow.
//
// THE SECOND CALL'S ANSWER IS THE ONE THAT COUNTS. Between the two asks the
// matching set could in principle have changed; taking the first total and
// trusting the second fill would read past what was actually written. So the
// second answer is clamped to what the buffer could hold and returned, and a
// set that SHRANK between the two is reported at its new size rather than its
// old one.
//
// A NULL finder, an empty query and a refusal all answer 0 and touch neither
// buffer.
//-----------------------------------------------------------------------------
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
