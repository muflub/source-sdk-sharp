//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_engineboundary.cpp's diagnostic console command ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
//=============================================================================//

#include "tier0/dbg.h"
#include "mathlib/vector.h"
#include "worldsize.h"                // MAX_COORD_FLOAT
#include "engine/IEngineTrace.h"
#include "engine/IStaticPropMgr.h"
#include "engine_heap.h"
#include "engine_utlvector.h"
#include "native_command_body.h"
#ifdef SOURCESHARP_ALLOC_CHECK
#include "alloc_check.h"
#endif

// Defined and acquired in shell_engineboundary.cpp.
extern IEngineTrace         *enginetrace;
extern IStaticPropMgrServer *staticpropmgr;

//=============================================================================//
// 3. VECTORS THE ENGINE FILLS FOR US -- engine_heap.h, checked live
//=============================================================================//

//-----------------------------------------------------------------------------
// Has the engine grow two out-parameters on the running map, reads them
// through the view and releases them to the allocator the engine grew them
// with. The live gate's engine-vector step reads the one line this prints:
//
//   heap  -- the g_pMemAlloc FindEngineHeap found IS the one engine.so resolves
//            (not merely a libtier0 somewhere): same pointer, both non-NULL.
//   brushes / props -- what IEngineTrace::GetBrushesInAABB and
//            IStaticPropMgrServer::GetAllStaticProps put in them, over the
//            whole map, and whether the brush indices are all non-negative.
//   foreign / mismatch -- the allocation checker's counters across the two
//            releases (debug builds carry it; -1 when this one does not). A
//            release that went through server.so's own free() would show
//            here, because the checker never saw these blocks allocated.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_engine_vectors )
{
	ss::IEngineHeap *pHeap   = ss::FindEngineHeap();
	ss::IEngineHeap *pEngine = ss::EngineHeapSeenBy( "engine.so" );

#ifdef SOURCESHARP_ALLOC_CHECK
	const SourceSharpAllocCheckCounts before = SourceSharp_AllocCheckCounts();
#endif

	const Vector vMins( -MAX_COORD_FLOAT, -MAX_COORD_FLOAT, -MAX_COORD_FLOAT );
	const Vector vMaxs(  MAX_COORD_FLOAT,  MAX_COORD_FLOAT,  MAX_COORD_FLOAT );

	int nBrushes = -1;
	int nNegative = 0;
	int nBrushAlloc = 0;
	if ( enginetrace )
	{
		ss::EngineFilledUtlVector< int > brushes;
		enginetrace->GetBrushesInAABB( vMins, vMaxs, brushes.ForEngine< CUtlVector< int > >() );
		const ss::EngineUtlVector< int > &view = brushes.View();
		nBrushes = view.Count();
		nBrushAlloc = view.m_nAllocationCount;
		for ( int iBrush : view )
			nNegative += ( iBrush < 0 );
	}

	int nProps = -1;
	int nNullProps = 0;
	if ( staticpropmgr )
	{
		ss::EngineFilledUtlVector< ICollideable * > props;
		staticpropmgr->GetAllStaticProps( props.ForEngine< CUtlVector< ICollideable * > >() );
		const ss::EngineUtlVector< ICollideable * > &view = props.View();
		nProps = view.Count();
		for ( ICollideable *pProp : view )
			nNullProps += ( pProp == NULL );
	}

	long long nForeign = -1, nMismatch = -1;
#ifdef SOURCESHARP_ALLOC_CHECK
	const SourceSharpAllocCheckCounts after = SourceSharp_AllocCheckCounts();
	nForeign  = (long long)( after.foreignFrees - before.foreignFrees );
	nMismatch = (long long)( after.mismatches - before.mismatches );
#endif

	Msg( "[SourceSharp] engine vectors: heap %s (%p, engine.so %p) brushes %d "
	     "(allocated %d, negative %d) props %d (null %d) foreign %lld mismatch %lld\n",
	     ( pHeap && pHeap == pEngine ) ? "same" : "DIFFERENT", (void *)pHeap, (void *)pEngine,
	     nBrushes, nBrushAlloc, nNegative, nProps, nNullProps, nForeign, nMismatch );
}
