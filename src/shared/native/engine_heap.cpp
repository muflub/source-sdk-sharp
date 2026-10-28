//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets

#include "engine_heap.h"
#include "loaded_symbol.h"

#include <stdio.h>
#include <stdlib.h>

namespace ss
{

static IEngineHeap *HeapSeenFrom( const char *pszFile )
{
	IEngineHeap **ppHeap = (IEngineHeap **)FindLoadedSymbol( pszFile, "g_pMemAlloc" );
	return ppHeap ? *ppHeap : NULL;
}

IEngineHeap *FindEngineHeap()
{
	return HeapSeenFrom( "libtier0.so" );
}

IEngineHeap *EngineHeapSeenBy( const char *pszModuleFile )
{
	return HeapSeenFrom( pszModuleFile );
}

void EngineHeapFree( void *pMem )
{
	if ( !pMem )
		return;

	IEngineHeap *pHeap = FindEngineHeap();
	if ( !pHeap )
	{
		fprintf( stderr,
				 "[SourceSharp] EngineHeapFree(%p): no libtier0.so g_pMemAlloc in "
				 "this process, so the block cannot be tier0's. Refusing to free "
				 "it into another heap.\n",
				 pMem );
		abort();
	}

	pHeap->Free( pMem );
}

} // namespace ss
