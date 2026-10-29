//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// Forced into every translation unit of a Windows target that keeps the C
// allocator (NO_MALLOC_OVERRIDE: server.dll and client.dll, as server.so and
// client.so do). tier0/memalloc.h gives such a module its MemAlloc_* inlines
// only under POSIX; Valve never built Windows without tier0's allocator, so
// on Windows that arm is empty and tier1's containers, tslist.h and
// memdbgon.h name functions nobody declares. This supplies the same set for
// the Windows CRT.
//
// The one difference from the POSIX arm is alignment: POSIX frees an aligned
// block with free(), the Windows CRT only with _aligned_free(). So memalign
// and every aligned allocation here go through _aligned_malloc, and every
// aligned free through _aligned_free; the unaligned pair stays malloc/free.
// A block is always freed by the family that allocated it, as on Linux.
//
// memalloc.h is included first, so its include guard makes every later
// #include of it a no-op and these definitions follow its declarations.
//=============================================================================//

#pragma once

#if defined( _WIN32 ) && defined( NO_MALLOC_OVERRIDE ) && !defined( STEAM )

#include <malloc.h>
#include <stdlib.h>
#include <string.h>

// memalloc.h itself defines MemAlloc_GetDebugInfoSize and its three
// siblings twice for this case (once in the arm for a module without its
// POSIX inlines, once in the NO_MALLOC_OVERRIDE arm, which Valve never
// combined on Windows): C4005, an error under /WX. The second definitions,
// the NO_MALLOC_OVERRIDE ones, are what this module wants, and they win.
#ifdef _MSC_VER
#pragma warning( push )
#pragma warning( disable : 4005 )
#endif
#include "tier0/memalloc.h"
#ifdef _MSC_VER
#pragma warning( pop )
#endif

inline void MemAlloc_CheckAlloc( void *ptr, size_t nSize )
{
	if ( !ptr )
		MemAllocOOMError( nSize );
}

// memdbgon.h's NO_MALLOC_OVERRIDE arm defines a checked memalign wrapper
// (then #undefs the macro that would use it); it must still compile.
inline void *memalign( size_t align, size_t nSize )
{
	return _aligned_malloc( nSize, align );
}

inline void *MemAlloc_Alloc( size_t nSize, const char * = NULL, int = 0 )
{
	void *ptr = malloc( nSize );
	MemAlloc_CheckAlloc( ptr, nSize );
	return ptr;
}

inline void MemAlloc_Free( void *ptr, const char * = NULL, int = 0 )
{
	free( ptr );
}

inline void *MemAlloc_AllocAligned( size_t nSize, size_t align, const char * = NULL, int = 0 )
{
	void *ptr = _aligned_malloc( nSize, align );
	MemAlloc_CheckAlloc( ptr, nSize );
	return ptr;
}

inline void *MemAlloc_AllocAlignedFileLine( size_t nSize, size_t align, const char * = NULL, int = 0 )
{
	return MemAlloc_AllocAligned( nSize, align );
}

inline void MemAlloc_FreeAligned( void *pMemBlock, const char * = NULL, int = 0 )
{
	_aligned_free( pMemBlock );
}

inline void *MemAlloc_ReallocAligned( void *ptr, size_t nSize, size_t align )
{
	void *pNew = _aligned_realloc( ptr, nSize, align );
	MemAlloc_CheckAlloc( pNew, nSize );
	return pNew;
}

#endif // _WIN32 && NO_MALLOC_OVERRIDE && !STEAM
