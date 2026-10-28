//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Two guarantees C_BaseEntity gave that trail effects, which are not entities, still
// need: a zeroing allocator (their constructors rely on zeroed fields) and a latch that
// lets exactly one of their destroy routes delete them.

#pragma once

#include <stddef.h>
#include <string.h>

// Keeps a memset alive: inlined into a new-expression, GCC's -flifetime-dse deletes a
// store to storage whose lifetime has not begun. Emits no instructions.
#if defined( _MSC_VER )
#include <intrin.h>
#define SOURCESHARP_KEEP_STORES( p ) _ReadWriteBarrier()
#else
#define SOURCESHARP_KEEP_STORES( p ) __asm__ __volatile__( "" : : "r"( p ) : "memory" )
#endif

inline void *SourceSharp_ZeroBlock( void *pMem, size_t nBytes )
{
	if ( pMem )
	{
		memset( pMem, 0, nBytes );
		SOURCESHARP_KEEP_STORES( pMem );
	}

	return pMem;
}

// C_BaseEntity's zeroing allocator, for a class that is not one. alloc_fn takes (size)
// and (size, file, line); free_fn takes MemAlloc_Free's one. The (int, const char *, int)
// forms are what memdbgon.h rewrites `new` into, so both pairs are needed.
#define SOURCESHARP_ZEROING_ALLOCATOR( alloc_fn, free_fn )                                              \
	void *operator new( size_t stAllocateBlock )                                                        \
	{                                                                                                   \
		return SourceSharp_ZeroBlock( alloc_fn( stAllocateBlock ), stAllocateBlock );                   \
	}                                                                                                   \
	void *operator new( size_t stAllocateBlock, int nBlockUse, const char *pFileName, int nLine )       \
	{                                                                                                   \
		return SourceSharp_ZeroBlock( alloc_fn( stAllocateBlock, pFileName, nLine ), stAllocateBlock ); \
	}                                                                                                   \
	void operator delete( void *pMem )                                                                  \
	{                                                                                                   \
		free_fn( pMem );                                                                                \
	}                                                                                                   \
	void operator delete( void *pMem, int nBlockUse, const char *pFileName, int nLine )                 \
	{                                                                                                   \
		free_fn( pMem );                                                                                \
	}

// Claim() answers true to exactly one caller; only that caller deletes. Announce() fires
// the gone callback at most once, clearing it before the call.
template <typename T>
class CTrailDestroyLatch
{
public:
	typedef void ( *GoneFn_t )( T *pOwner );

	CTrailDestroyLatch()
		: m_bDeleting( false )
		, m_pfnGone( NULL )
	{
	}

	void SetGoneCallback( GoneFn_t pfnGone ) { m_pfnGone = pfnGone; }

	// True for the first caller and nobody else.
	bool Claim()
	{
		if ( m_bDeleting )
			return false;

		m_bDeleting = true;
		return true;
	}

	bool IsDeleting() const { return m_bDeleting; }

	// At most once, ever, claimed or not.
	void Announce( T *pOwner )
	{
		GoneFn_t pfnGone = m_pfnGone;
		m_pfnGone = NULL;

		if ( pfnGone )
			pfnGone( pOwner );
	}

private:
	bool m_bDeleting;
	GoneFn_t m_pfnGone;
};
