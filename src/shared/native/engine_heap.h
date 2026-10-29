//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Frees blocks the engine's tier0 allocator made (vectors an engine interface grows
// for its caller). server.so does not link tier0, so the allocator is found in the
// loaded libtier0.so. No libtier0 in the process aborts rather than freeing into the wrong heap.

#pragma once

#include <cstddef>

namespace ss
{

// tier0's IMemAlloc, as far as its third slot; the slot order must match memalloc.h.
class IEngineHeap
{
public:
	virtual void* Alloc(size_t nSize) = 0;
	virtual void* Realloc(void* pMem, size_t nSize) = 0;
	virtual void Free(void* pMem) = 0;
};

// The process's tier0 allocator, or NULL when no libtier0.so is loaded.
IEngineHeap* FindEngineHeap();

// The g_pMemAlloc a loaded module (named by file, e.g. "engine.so") resolves,
// or NULL. For checking that FindEngineHeap is the allocator that module uses.
IEngineHeap* EngineHeapSeenBy(const char* pszModuleFile);

// Free a block tier0's allocator made. NULL is a no-op; no allocator aborts.
void EngineHeapFree(void* pMem);

} // namespace ss
