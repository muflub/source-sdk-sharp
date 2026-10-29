//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds only; NativeCommands declares the matching row under #if DEBUG.

#include "server_services.h"
#include "tier0/dbg.h"
#include "mathlib/vector.h"
#include "worldsize.h"
#include "engine/IEngineTrace.h"
#include "engine/IStaticPropMgr.h"
#include "engine_heap.h"
#include "engine_utlvector.h"
#include "native_command_body.h"
#ifdef SOURCESHARP_ALLOC_CHECK
#include "alloc_check.h"
#endif

// Defined and acquired in shell_engineboundary.cpp.

// Has the engine fill two vectors over the whole map, reads them and releases
// them to the engine's allocator. Prints whether FindEngineHeap's heap is
// engine.so's, the counts, and the allocation checker's foreign/mismatch frees
// across the releases (-1 without the checker). The live gate reads this line.
SS_NATIVE_COMMAND(cs_sv_engine_vectors)
{
	ss::IEngineHeap* pHeap = ss::FindEngineHeap();
	ss::IEngineHeap* pEngine = ss::EngineHeapSeenBy("engine.so");

#ifdef SOURCESHARP_ALLOC_CHECK
	const SourceSharpAllocCheckCounts before = SourceSharp_AllocCheckCounts();
#endif

	const Vector vMins(-MAX_COORD_FLOAT, -MAX_COORD_FLOAT, -MAX_COORD_FLOAT);
	const Vector vMaxs(MAX_COORD_FLOAT, MAX_COORD_FLOAT, MAX_COORD_FLOAT);

	int nBrushes = -1;
	int nNegative = 0;
	int nBrushAlloc = 0;
	IEngineTrace* pTrace = services.m_EntityContext.m_pTrace;
	if (pTrace)
	{
		ss::EngineFilledUtlVector<int> brushes;
		pTrace->GetBrushesInAABB(vMins, vMaxs, brushes.ForEngine<CUtlVector<int>>());
		const ss::EngineUtlVector<int>& view = brushes.View();
		nBrushes = view.Count();
		nBrushAlloc = view.m_nAllocationCount;
		for (int iBrush : view)
			nNegative += (iBrush < 0);
	}

	int nProps = -1;
	int nNullProps = 0;
	IStaticPropMgrServer* pStaticProps = services.m_EntityContext.m_pStaticProps;
	if (pStaticProps)
	{
		ss::EngineFilledUtlVector<ICollideable*> props;
		pStaticProps->GetAllStaticProps(props.ForEngine<CUtlVector<ICollideable*>>());
		const ss::EngineUtlVector<ICollideable*>& view = props.View();
		nProps = view.Count();
		for (ICollideable* pProp : view)
			nNullProps += (pProp == NULL);
	}

	long long nForeign = -1, nMismatch = -1;
#ifdef SOURCESHARP_ALLOC_CHECK
	const SourceSharpAllocCheckCounts after = SourceSharp_AllocCheckCounts();
	nForeign = (long long)(after.foreignFrees - before.foreignFrees);
	nMismatch = (long long)(after.mismatches - before.mismatches);
#endif

	Msg("[SourceSharp] engine vectors: heap %s (%p, engine.so %p) brushes %d "
		"(allocated %d, negative %d) props %d (null %d) foreign %lld mismatch %lld\n",
		(pHeap && pHeap == pEngine) ? "same" : "DIFFERENT", (void*)pHeap, (void*)pEngine, nBrushes, nBrushAlloc,
		nNegative, nProps, nNullProps, nForeign, nMismatch);
}
