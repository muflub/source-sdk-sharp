//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — give a block back to the allocator that made it ====//
//
// A vector an engine interface grows for its caller (IEngineTrace::
// GetBrushesInAABB, IStaticPropMgr::GetAllStaticProps*, ...) is grown by the
// engine's copy of tier1's template, so its block comes from tier0's allocator:
// libtier0.so's g_pMemAlloc, the one engine.so, datacache.so and the rest
// import. server.so does not link tier0 (plan_server_novalve.md P4), so that
// block must go back through the same object, found where the engine already
// loaded it: the loaded object whose file is libtier0.so, reopened
// RTLD_NOLOAD, dlsym( "g_pMemAlloc" ).
//
// Only the first three methods of tier0's IMemAlloc are declared, in its order;
// engine_heap_test holds their slots to public/tier0/memalloc.h.
//
// No libtier0 in the process means the block cannot have come from it: that is
// a broken process, and it stops here, loudly, rather than leak or free into
// the wrong heap.
//
//=============================================================================//

#pragma once

#include <cstddef>

namespace ss
{

// tier0's IMemAlloc, as far as its third slot.
class IEngineHeap
{
public:
	virtual void *Alloc( size_t nSize ) = 0;
	virtual void *Realloc( void *pMem, size_t nSize ) = 0;
	virtual void  Free( void *pMem ) = 0;
};

// The process's tier0 allocator, or NULL when no libtier0.so is loaded.
IEngineHeap *FindEngineHeap();

// The g_pMemAlloc a loaded module (named by file, e.g. "engine.so") resolves,
// or NULL. For checking that FindEngineHeap is the allocator that module uses.
IEngineHeap *EngineHeapSeenBy( const char *pszModuleFile );

// Free a block tier0's allocator made. NULL is a no-op; no allocator aborts.
void EngineHeapFree( void *pMem );

} // namespace ss
