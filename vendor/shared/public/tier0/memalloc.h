//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: This header should never be used directly from leaf code!!!
// Instead, just add the file memoverride.cpp into your project and all this
// will automagically be used

#pragma once

// These memory debugging switches aren't relevant under Linux builds since memoverride.cpp
// isn't built into Linux projects
#ifndef POSIX
// Define this in release to get memory tracking even in release builds
//#define USE_MEM_DEBUG 1
#endif

// Undefine this if using a compiler lacking threadsafe RTTI (like vc6)
#define MEM_DEBUG_CLASSNAME 1

#if defined( OSX )
#include <malloc/malloc.h>
#endif

#include "tier0/mem.h"

#if !defined( NO_MALLOC_OVERRIDE )

struct _CrtMemState;

typedef size_t ( *MemAllocFailHandler_t )( size_t );

// NOTE! This should never be called directly from leaf code
// Just use new,delete,malloc,free etc. They will call into this eventually
abstract_class IMemAlloc
{
public:
	// Release versions
	virtual void *Alloc( size_t nSize ) = 0;
	virtual void *Realloc( void *pMem, size_t nSize ) = 0;
	virtual void Free( void *pMem ) = 0;
	virtual void *Expand_NoLongerSupported( void *pMem, size_t nSize ) = 0;

	// Debug versions
	virtual void *Alloc( size_t nSize, const char *pFileName, int nLine ) = 0;
	virtual void *Realloc( void *pMem, size_t nSize, const char *pFileName, int nLine ) = 0;
	virtual void Free( void *pMem, const char *pFileName, int nLine ) = 0;
	virtual void *Expand_NoLongerSupported( void *pMem, size_t nSize, const char *pFileName, int nLine ) = 0;

	// Returns size of a particular allocation
	virtual size_t GetSize( void *pMem ) = 0;

	// Force file + line information for an allocation
	virtual void unused_PushAllocDbgInfo() = 0;
	virtual void unused_PopAllocDbgInfo() = 0;

	// FIXME: Remove when we have our own allocator
	// these methods of the Crt debug code is used in our codebase currently
	virtual void unused_CrtSetBreakAlloc() = 0;
	virtual void unused_CrtSetReportMode() = 0;
	virtual void unused_CrtIsValidHeapPointer() = 0;
	virtual void unused_CrtIsValidPointer() = 0;
	virtual void unused_CrtCheckMemory() = 0;
	virtual void unused_CrtSetDbgFlag() = 0;
	virtual void unused_CrtMemCheckpoint() = 0;

	// FIXME: Make a better stats interface
	virtual void unused_DumpStats() = 0;
	virtual void unused_DumpStatsFileBase() = 0;

	// FIXME: Remove when we have our own allocator
	virtual void unused_CrtSetReportFile() = 0;
	virtual void unused_CrtSetReportHook() = 0;
	virtual void unused_CrtDbgReport() = 0;

	virtual void unused_heapchk() = 0;

	virtual void unused_IsDebugHeap() = 0;

	virtual void unused_GetActualDbgInfo() = 0;
	virtual void unused_RegisterAllocation() = 0;
	virtual void unused_RegisterDeallocation() = 0;

	virtual void unused_GetVersion() = 0;

	virtual void unused_CompactHeap() = 0;

	// Function called when malloc fails or memory limits hit to attempt to free up memory (can come in any thread)
	virtual void unused_SetAllocFailHandler() = 0;

	virtual void unused_DumpBlockStats() = 0;

	// Returns 0 if no failure, otherwise the size_t of the last requested chunk
	//  "I'm sure this is completely thread safe!" Brian Deen 7/19/2012.
	virtual void unused_MemoryAllocFailed() = 0;

	// handles storing allocation info for coroutines
	virtual void unused_GetDebugInfoSize() = 0;
	virtual void unused_SaveDebugInfo() = 0;
	virtual void unused_RestoreDebugInfo() = 0;
	virtual void unused_InitDebugInfo() = 0;

	// Replacement for ::GlobalMemoryStatus which accounts for unused memory in our system
	virtual void unused_GlobalMemoryStatus() = 0;
};

// Singleton interface
MEM_INTERFACE IMemAlloc *g_pMemAlloc;

#undef MEMALLOC_REGION

inline void *MemAlloc_Alloc( size_t nSize )
{
	return g_pMemAlloc->Alloc( nSize );
}

inline void *MemAlloc_Alloc( size_t nSize, const char *pFileName, int nLine )
{
	return g_pMemAlloc->Alloc( nSize, pFileName, nLine );
}

inline void MemAlloc_Free( void *ptr, const char *pFileName, int nLine )
{
	g_pMemAlloc->Free( ptr, pFileName, nLine );
}

inline bool ValueIsPowerOfTwo( size_t value ) // don't clash with mathlib definition
{
	return ( value & ( value - 1 ) ) == 0;
}

inline void *MemAlloc_AllocAligned( size_t size, size_t align )
{
	unsigned char *pAlloc, *pResult;

	if ( !IsPowerOfTwo( align ) )
		return NULL;

	align = ( align > sizeof( void * ) ? align : sizeof( void * ) ) - 1;

	if ( ( pAlloc = (unsigned char *)g_pMemAlloc->Alloc( sizeof( void * ) + align + size ) ) == (unsigned char *)NULL )
		return NULL;

	pResult = (unsigned char *)( (size_t)( pAlloc + sizeof( void * ) + align ) & ~align );
	( (unsigned char **)( pResult ) )[-1] = pAlloc;

	return (void *)pResult;
}

inline void *MemAlloc_AllocAligned( size_t size, size_t align, const char *pszFile, int nLine )
{
	unsigned char *pAlloc, *pResult;

	if ( !IsPowerOfTwo( align ) )
		return NULL;

	align = ( align > sizeof( void * ) ? align : sizeof( void * ) ) - 1;

	if ( ( pAlloc = (unsigned char *)g_pMemAlloc->Alloc( sizeof( void * ) + align + size, pszFile, nLine ) ) ==
		 (unsigned char *)NULL )
		return NULL;

	pResult = (unsigned char *)( (size_t)( pAlloc + sizeof( void * ) + align ) & ~align );
	( (unsigned char **)( pResult ) )[-1] = pAlloc;

	return (void *)pResult;
}

inline void *MemAlloc_AllocAlignedFileLine( size_t size, size_t align, const char *pszFile, int nLine )
{
	unsigned char *pAlloc, *pResult;

	if ( !ValueIsPowerOfTwo( align ) )
		return NULL;

	align = ( align > sizeof( void * ) ? align : sizeof( void * ) ) - 1;

	if ( ( pAlloc = (unsigned char *)MemAlloc_Alloc( sizeof( void * ) + align + size, pszFile, nLine ) ) ==
		 (unsigned char *)NULL )
		return NULL;

	pResult = (unsigned char *)( (size_t)( pAlloc + sizeof( void * ) + align ) & ~align );
	( (unsigned char **)( pResult ) )[-1] = pAlloc;

	return (void *)pResult;
}

inline void *MemAlloc_ReallocAligned( void *ptr, size_t size, size_t align )
{
	if ( !IsPowerOfTwo( align ) )
		return NULL;

	// Don't change alignment between allocation + reallocation.
	if ( ( (size_t)ptr & ( align - 1 ) ) != 0 )
		return NULL;

	if ( !ptr )
		return MemAlloc_AllocAligned( size, align );

	void *pAlloc, *pResult;

	// Figure out the actual allocation point
	pAlloc = ptr;
	pAlloc = (void *)( ( (size_t)pAlloc & ~( sizeof( void * ) - 1 ) ) - sizeof( void * ) );
	pAlloc = *( (void **)pAlloc );

	// See if we have enough space
	size_t nOffset = (size_t)ptr - (size_t)pAlloc;
	size_t nOldSize = g_pMemAlloc->GetSize( pAlloc );
	if ( nOldSize >= size + nOffset )
		return ptr;

	pResult = MemAlloc_AllocAligned( size, align );
	memcpy( pResult, ptr, nOldSize - nOffset );
	g_pMemAlloc->Free( pAlloc );
	return pResult;
}

inline void MemAlloc_FreeAligned( void *pMemBlock )
{
	void *pAlloc;

	if ( pMemBlock == NULL )
		return;

	pAlloc = pMemBlock;

	// pAlloc points to the pointer to starting of the memory block
	pAlloc = (void *)( ( (size_t)pAlloc & ~( sizeof( void * ) - 1 ) ) - sizeof( void * ) );

	// pAlloc is the pointer to the start of memory block
	pAlloc = *( (void **)pAlloc );
	g_pMemAlloc->Free( pAlloc );
}

inline void MemAlloc_FreeAligned( void *pMemBlock, const char *pFileName, int nLine )
{
	void *pAlloc;

	if ( pMemBlock == NULL )
		return;

	pAlloc = pMemBlock;

	// pAlloc points to the pointer to starting of the memory block
	pAlloc = (void *)( ( (size_t)pAlloc & ~( sizeof( void * ) - 1 ) ) - sizeof( void * ) );

	// pAlloc is the pointer to the start of memory block
	pAlloc = *( (void **)pAlloc );
	g_pMemAlloc->Free( pAlloc, pFileName, nLine );
}

#if defined( USE_MEM_DEBUG )
#define MEM_ALLOC_CREDIT_JOIN_AGAIN( a, b ) a##b
#define MEM_ALLOC_CREDIT_JOIN( a, b ) MEM_ALLOC_CREDIT_JOIN_AGAIN( a, b )
#define MEM_ALLOC_CREDIT_( tag ) \
	CMemAllocAttributeAlloction MEM_ALLOC_CREDIT_JOIN( memAllocAttributeAlloction, __LINE__ )( tag, __LINE__ )
#define MemAlloc_PushAllocDbgInfo( pszFile, line ) g_pMemAlloc->PushAllocDbgInfo( pszFile, line )
#define MemAlloc_PopAllocDbgInfo() g_pMemAlloc->PopAllocDbgInfo()
#define MemAlloc_RegisterAllocation( pFileName, nLine, nLogicalSize, nActualSize, nTime ) \
	g_pMemAlloc->RegisterAllocation( pFileName, nLine, nLogicalSize, nActualSize, nTime )
#define MemAlloc_RegisterDeallocation( pFileName, nLine, nLogicalSize, nActualSize, nTime ) \
	g_pMemAlloc->RegisterDeallocation( pFileName, nLine, nLogicalSize, nActualSize, nTime )
#else
#define MEM_ALLOC_CREDIT_( tag ) ( (void)0 )
#define MemAlloc_PushAllocDbgInfo( pszFile, line ) ( (void)0 )
#define MemAlloc_PopAllocDbgInfo() ( (void)0 )
#define MemAlloc_RegisterAllocation( pFileName, nLine, nLogicalSize, nActualSize, nTime ) ( (void)0 )
#define MemAlloc_RegisterDeallocation( pFileName, nLine, nLogicalSize, nActualSize, nTime ) ( (void)0 )
#endif

class CMemAllocAttributeAlloction
{
public:
	CMemAllocAttributeAlloction( const char *pszFile, int line ) { MemAlloc_PushAllocDbgInfo( pszFile, line ); }

	~CMemAllocAttributeAlloction() { MemAlloc_PopAllocDbgInfo(); }
};

#define MEM_ALLOC_CREDIT() MEM_ALLOC_CREDIT_( __FILE__ )

#if defined( _WIN32 ) && defined( USE_MEM_DEBUG )

#pragma warning( disable : 4290 )
#pragma warning( push )
#include <typeinfo.h>

// MEM_DEBUG_CLASSNAME is opt-in.
// Note: typeid().name() is not threadsafe, so if the project needs to access it in multiple threads
// simultaneously, it'll need a mutex.
#if defined( _CPPRTTI ) && defined( MEM_DEBUG_CLASSNAME )
#define MEM_ALLOC_CREDIT_CLASS() MEM_ALLOC_CREDIT_( typeid( *this ).name() )
#define MEM_ALLOC_CLASSNAME( type ) ( typeid( (type *)( 0 ) ).name() )
#else
#define MEM_ALLOC_CREDIT_CLASS() MEM_ALLOC_CREDIT_( __FILE__ )
#define MEM_ALLOC_CLASSNAME( type ) ( __FILE__ )
#endif

// MEM_ALLOC_CREDIT_FUNCTION is used when no this pointer is available ( inside 'new' overloads, for example )
#ifdef _MSC_VER
#define MEM_ALLOC_CREDIT_FUNCTION() MEM_ALLOC_CREDIT_( __FUNCTION__ )
#else
#define MEM_ALLOC_CREDIT_FUNCTION() ( __FILE__ )
#endif

#pragma warning( pop )
#else
#define MEM_ALLOC_CREDIT_CLASS()
#define MEM_ALLOC_CLASSNAME( type ) NULL
#define MEM_ALLOC_CREDIT_FUNCTION()
#endif

#if defined( USE_MEM_DEBUG )
struct MemAllocFileLine_t
{
	const char *pszFile;
	int line;
};

#define MEMALLOC_DEFINE_EXTERNAL_TRACKING( tag )                                              \
	static CUtlMap<void *, MemAllocFileLine_t, int> g_##tag##Allocs( DefLessFunc( void * ) ); \
	static const char *g_psz##tag##Alloc =                                                    \
		strcpy( (char *)g_pMemAlloc->Alloc( strlen( #tag "Alloc" ) + 1, "intentional leak", 0 ), #tag "Alloc" );

#define MemAlloc_RegisterExternalAllocation( tag, p, size )                            \
	if ( !p )                                                                          \
		;                                                                              \
	else                                                                               \
	{                                                                                  \
		MemAllocFileLine_t fileLine = { g_psz##tag##Alloc, 0 };                        \
		g_pMemAlloc->GetActualDbgInfo( fileLine.pszFile, fileLine.line );              \
		if ( fileLine.pszFile != g_psz##tag##Alloc )                                   \
		{                                                                              \
			g_##tag##Allocs.Insert( p, fileLine );                                     \
		}                                                                              \
                                                                                       \
		MemAlloc_RegisterAllocation( fileLine.pszFile, fileLine.line, size, size, 0 ); \
	}

#define MemAlloc_RegisterExternalDeallocation( tag, p, size )                                                \
	if ( !p )                                                                                                \
		;                                                                                                    \
	else                                                                                                     \
	{                                                                                                        \
		MemAllocFileLine_t fileLine = { g_psz##tag##Alloc, 0 };                                              \
		CUtlMap<void *, MemAllocFileLine_t, int>::IndexType_t iRecordedFileLine = g_##tag##Allocs.Find( p ); \
		if ( iRecordedFileLine != g_##tag##Allocs.InvalidIndex() )                                           \
		{                                                                                                    \
			fileLine = g_##tag##Allocs[iRecordedFileLine];                                                   \
			g_##tag##Allocs.RemoveAt( iRecordedFileLine );                                                   \
		}                                                                                                    \
                                                                                                             \
		MemAlloc_RegisterDeallocation( fileLine.pszFile, fileLine.line, size, size, 0 );                     \
	}

#else

#define MEMALLOC_DEFINE_EXTERNAL_TRACKING( tag )
#define MemAlloc_RegisterExternalAllocation( tag, p, size ) ( (void)0 )
#define MemAlloc_RegisterExternalDeallocation( tag, p, size ) ( (void)0 )

#endif

#elif defined( POSIX )

inline void MemAlloc_CheckAlloc( void *ptr, size_t nSize )
{
	if ( !ptr )
		MemAllocOOMError( nSize );
}

#if defined( OSX )
// Mac always aligns allocs, don't need to call posix_memalign which doesn't exist in 10.5.8 which TF2 still needs to run on
//inline void *memalign(size_t alignment, size_t size) {void *pTmp=NULL; posix_memalign(&pTmp, alignment, size); return pTmp;}
inline void *memalign( size_t alignment, size_t size )
{
	void *pTmp = NULL;
	pTmp = malloc( size );
	MemAlloc_CheckAlloc( pTmp, size );
	return pTmp;
}
#endif

inline void *_aligned_malloc( size_t nSize, size_t align )
{
	void *ptr = memalign( align, nSize );
	MemAlloc_CheckAlloc( ptr, nSize );
	return ptr;
}

inline void *MemAlloc_Alloc( size_t nSize, const char *pFileName = NULL, int nLine = 0 )
{
	void *ptr = malloc( nSize );
	MemAlloc_CheckAlloc( ptr, nSize );
	return ptr;
}

inline void MemAlloc_Free( void *ptr, const char *pFileName = NULL, int nLine = 0 )
{
	free( ptr );
}

inline void *MemAlloc_AllocAligned( size_t size, size_t align, const char *pszFile = NULL, int nLine = 0 )
{
	void *ptr = memalign( align, size );
	MemAlloc_CheckAlloc( ptr, size );
	return ptr;
}

inline void *MemAlloc_AllocAlignedFileLine( size_t size, size_t align, const char *pszFile = NULL, int nLine = 0 )
{
	void *ptr = memalign( align, size );
	MemAlloc_CheckAlloc( ptr, size );
	return ptr;
}

inline void MemAlloc_FreeAligned( void *pMemBlock, const char *pszFile = NULL, int nLine = 0 )
{
	free( pMemBlock );
}

#if defined( OSX )
inline size_t _msize( void *ptr )
{
	return malloc_size( ptr );
}
#else
inline size_t _msize( void *ptr )
{
	return malloc_usable_size( ptr );
}
#endif

inline void *MemAlloc_ReallocAligned( void *ptr, size_t size, size_t align )
{
	void *ptr_new_aligned = memalign( align, size );

	if ( ptr_new_aligned )
	{
		size_t old_size = _msize( ptr );
		size_t copy_size = ( size < old_size ) ? size : old_size;

		memcpy( ptr_new_aligned, ptr, copy_size );
		free( ptr );
	}

	MemAlloc_CheckAlloc( ptr_new_aligned, size );
	return ptr_new_aligned;
}
#endif // !STEAM && !NO_MALLOC_OVERRIDE

#if defined( NO_MALLOC_OVERRIDE )

#define MEM_ALLOC_CREDIT_( tag ) ( (void)0 )
#define MEM_ALLOC_CREDIT() MEM_ALLOC_CREDIT_( __FILE__ )
#define MEM_ALLOC_CREDIT_FUNCTION()
#define MEM_ALLOC_CREDIT_CLASS()
#define MEM_ALLOC_CLASSNAME( type ) NULL

#define MemAlloc_PushAllocDbgInfo( pszFile, line )
#define MemAlloc_PopAllocDbgInfo()
#define MemAlloc_RegisterAllocation( pFileName, nLine, nLogicalSize, nActualSize, nTime ) ( (void)0 )
#define MemAlloc_RegisterDeallocation( pFileName, nLine, nLogicalSize, nActualSize, nTime ) ( (void)0 )

#define MEMALLOC_DEFINE_EXTERNAL_TRACKING( tag )
#define MemAlloc_RegisterExternalAllocation( tag, p, size ) ( (void)0 )
#define MemAlloc_RegisterExternalDeallocation( tag, p, size ) ( (void)0 )

#endif // !STEAM && NO_MALLOC_OVERRIDE

// linux memory tracking via hooks.

#ifdef POSIX
// ApproximateProcessMemoryUsage returns the approximate memory footprint of this process.
#endif

struct aligned_tmp_t
{
	// empty base class
};

/*
This class used to be required if you wanted an object to be allocated with a specific
alignment. ALIGN16 and ALIGN16_POST are not actually sufficient for this because they
guarantee that the globals, statics, locals, and function parameters are appropriately
aligned they do not affect memory allocation alignment.
However this class is usually not needed because as of 2012 our policy is that our
allocator should take care of this automatically. Any object whose size is a multiple
of 16 will be 16-byte aligned. Existing uses of this class were not changed because
the cost/benefit did not justify it.
*/
