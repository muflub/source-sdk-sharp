//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the two rules a de-entified effect has to keep =====//
//
// When the six trail classes stopped deriving from C_BaseEntity
// (shell_particle_effect.h), they lost two guarantees that the entity had been
// providing silently. Both are pure C++ with no engine in them, so both live
// here where trail_lifetime_test.cpp can drive them.
//
//-----------------------------------------------------------------------------
// 1. THE ZEROING ALLOCATOR.
//
// C_BaseEntity::operator new memsets the whole block: "All fields in the object
// are all initialized to 0", says the comment above it. Every one of the six
// leaned on that without saying so.
//
//   C_SporeTrail's constructor sets six of its seven fields. The seventh is
//   m_bEmit, and "nothing a map or a player can do will make one emit" is a
//   documented property of the entity that is TRUE ONLY BECAUSE the flag
//   arrives false.
//
//   C_ParticleTrail's constructor sets nothing at all, and its destructor then
//   does `if ( m_pParticleMgr ) m_pParticleMgr->RemoveEffect(...)`. With a
//   non-zeroing allocator that is a call through a garbage pointer on the very
//   first trail that fails to Start().
//
// Plain ::operator new does not zero. So the allocator came across with the
// classes, as a macro rather than a base, because operator new/delete are not
// inherited usefully through multiple inheritance and because spelling out the
// four overloads at every site is how one of them gets forgotten.
//
//-----------------------------------------------------------------------------
// 2. THE DESTRUCTION LATCH.
//
// An entity had exactly one owner — the client entity list — and CHandle made
// every other holder's pointer read as null the moment it went. A CTrailEffect
// has two callers that can destroy it and neither knows about the other:
//
//   Release()      shell_trail_render.cpp, when the managed owner goes away.
//   NotifyRemove() CParticleMgr, from RemoveAllEffects() at level shutdown.
//
// and a third path that looks like a destroy and is not: the subclass
// destructor's own `m_pParticleMgr->RemoveEffect( &m_ParticleEffect )`, which
// reaches CParticleMgr, which calls NotifyRemove — on an object that is already
// half destroyed.
//
// So destruction is CLAIMED rather than performed. Whoever claims first owns
// the delete; everyone after gets a no-op. And the registry that holds the raw
// pointer is told exactly once, from the destructor, so that it hears about
// both routes without having to know which one ran.
//
//=============================================================================//

#ifndef SOURCESHARP_TRAIL_LIFETIME_H
#define SOURCESHARP_TRAIL_LIFETIME_H
#ifdef _WIN32
#pragma once
#endif

#include <stddef.h>
#include <string.h>

//-----------------------------------------------------------------------------
// AND THE REASON THE ZEROING IS NOT JUST A memset, which cost a real bug and
// was caught by the test below rather than by anything else.
//
// C_BaseEntity::operator new is defined out of line, in c_baseentity.cpp. Its
// memset therefore survives, because the compiler compiling a `new C_SmokeTrail`
// somewhere else cannot see into it. Move the same three lines into a HEADER and
// they get inlined into the new-expression, and at that point GCC's
// -flifetime-dse -- on by default from -O1, and this build is -O2 -- sees a
// store to storage whose object lifetime has not begun yet and deletes the whole
// memset as dead. Silently. The allocator still runs, still allocates, still
// returns; every field the constructor does not set is simply garbage again.
//
// TrailLatch.AFieldTheConstructorNeverTouchesArrivesZeroed failed on exactly
// that, with 0xCD in every untouched byte, on the first run of this file.
//
// An empty asm with a memory clobber is the fix: it tells the optimiser that
// something it cannot see may read the block, so the store before it is live.
// It emits no instructions.
//-----------------------------------------------------------------------------
#if defined( _MSC_VER )
	#include <intrin.h>
	#define SOURCESHARP_KEEP_STORES( p )	_ReadWriteBarrier()
#else
	#define SOURCESHARP_KEEP_STORES( p )	__asm__ __volatile__( "" : : "r"( p ) : "memory" )
#endif

//-----------------------------------------------------------------------------
// The zeroing itself, in one place so that both the macro below and anything
// else that needs C_BaseEntity's guarantee get the barrier with it.
//-----------------------------------------------------------------------------
inline void *SourceSharp_ZeroBlock( void *pMem, size_t nBytes )
{
	if ( pMem )
	{
		memset( pMem, 0, nBytes );
		SOURCESHARP_KEEP_STORES( pMem );
	}

	return pMem;
}

//-----------------------------------------------------------------------------
// C_BaseEntity's allocator, for a class that is not one.
//
// alloc_fn is expected to have MemAlloc_Alloc's two shapes — (size) and
// (size, file, line) — and free_fn to have MemAlloc_Free's one. The zeroing is
// the entire point of the macro: an allocator that forgets it compiles, links,
// runs, and produces a class whose documented-inert flag is on about half the
// time. It goes through SourceSharp_ZeroBlock above rather than calling memset
// here, because an inline memset in a header IS forgetting it — see the note on
// -flifetime-dse.
//
// The two placement-ish forms taking (int nBlockUse, const char *, int) are the
// ones memdbgon.h rewrites `new` into inside the game, so a class that declares
// only the plain pair silently keeps ::operator new in every file that includes
// memdbgon.h — which is every .cpp in the build.
//-----------------------------------------------------------------------------
#define SOURCESHARP_ZEROING_ALLOCATOR( alloc_fn, free_fn )                     \
	void *operator new( size_t stAllocateBlock )                               \
	{                                                                          \
		return SourceSharp_ZeroBlock( alloc_fn( stAllocateBlock ),             \
		                              stAllocateBlock );                       \
	}                                                                          \
	void *operator new( size_t stAllocateBlock, int nBlockUse,                 \
	                    const char *pFileName, int nLine )                     \
	{                                                                          \
		return SourceSharp_ZeroBlock( alloc_fn( stAllocateBlock, pFileName,    \
		                                        nLine ),                       \
		                              stAllocateBlock );                       \
	}                                                                          \
	void operator delete( void *pMem )                                         \
	{                                                                          \
		free_fn( pMem );                                                       \
	}                                                                          \
	void operator delete( void *pMem, int nBlockUse,                           \
	                      const char *pFileName, int nLine )                   \
	{                                                                          \
		free_fn( pMem );                                                       \
	}

//-----------------------------------------------------------------------------
// Destroy-once, and tell-the-holder-once.
//
// Claim() answers true to exactly one caller for the life of the object. Every
// destroy route asks it first and only the winner deletes.
//
// Announce() fires the holder's callback at most once, ever, and clears the
// pointer BEFORE calling — so a callback that somehow reaches back into the
// object cannot start a second round.
//-----------------------------------------------------------------------------
template < typename T >
class CTrailDestroyLatch
{
public:
	typedef void (*GoneFn_t)( T *pOwner );

	CTrailDestroyLatch()
		: m_bDeleting( false ), m_pfnGone( NULL ) {}

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
	bool		m_bDeleting;
	GoneFn_t	m_pfnGone;
};

#endif // SOURCESHARP_TRAIL_LIFETIME_H
