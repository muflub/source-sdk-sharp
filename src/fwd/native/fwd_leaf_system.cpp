//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client leaf system, built natively (libss_fwd_client) ===//
// SS_REASON a  a vtable the engine calls through
//
// C7 cluster 12 L2 (docs/plans/c7_leaf_system.md). The leaf system is
// managed's -- the registry and every decision (SourceSharp.Client's
// LeafRegistry and RenderableListBuilder) -- but two kinds of caller reach it
// through C++ vtables: the engine, which asks client.so for ClientLeafSystem002
// and calls IClientLeafSystemEngine's four methods for its static props, and
// the C++ left in client.so (our shells), which call IClientLeafSystem. So the object they call is built here,
// deriving IClientLeafSystem, and each override calls one slot of the managed
// area IManagedLeafSystemClient (generated from docs/managed-interfaces.tsv),
// as fwd_physics.cpp's and fwd_steam.cpp's classes do for vphysics and Steam.
//
// IT DECIDES NOTHING. Each override hands managed its arguments (and what the
// renderable or engine says that the call needs: a model's kind, the leaf
// count), then carries out what managed answers: a render handle written into
// a renderable (IClientRenderable::RenderHandle() is a reference), a
// sub-system's data deleted, the render list written, the view models
// appended. Those come back as operations
// (four int64 words each, SourceSharp.Client.Interop.LeafOpKind), in the
// order stock did them, and Drain executes them; a reply longer than the
// buffer is fetched with TakeOps. The `if`s below are bounds and null
// guards, and the switch over an operation's kind.
//
// THREE CALLS ARE QUEUED, NOT FORWARDED (L3): RenderableChanged,
// ChangeRenderableRenderGroup and SetRenderGroup arrive per renderable per
// frame, so they are
// kept in order and reach managed in the arguments of the calls made every
// frame (PreRender and the per-view three), in a blend batch's answer, or
// (before any other call, or when the queue is full) through Pending.
//
// THE SHADOW HALF calls nothing (C7 cluster 12 S2): its one caller was the C++
// shadow manager, and the managed manager calls the managed leaf system
// in-process. Its five overrides answer an invalid handle and do nothing.
//
// THE BATCHES below the class are the other direction: the questions the
// managed registry asks native, each one call for any number of renderables,
// boxes or leaves (tools/engine-forwarders' HAND row LeafSystem).
//
// THE LIBRARY ALLOCATES NOTHING and links no runtime (NO_RUNTIME): client.so
// gives the object its storage, and appends to the view models' CUtlVectors
// itself (pfnAppendRenderable). CClientSystem's destructor is virtual, so the
// class names its own operator delete, which does nothing, as
// fwd_recipient_filter.cpp's does. Built without RTTI (CMakeLists.txt).
//=============================================================================//
#include "ss_fwd_sdk.h"
#include "fwd_leaf_system.h"

#include "globalvars_base.h"
#include "engine/ivmodelinfo.h"

#include "bsptreedata.h"

#include "detailobjectsystem.h"

#include "imanaged_areas_client.h"

// igamesystem.h's two destructors are defined out of line (stock's
// igamesystem.cpp), and those definitions emit IGameSystem's and
// IGameSystemPerFrame's vtables, which the leaf system's constructor stores.
// This library links neither DLL's copy, so it carries its own.
IGameSystem::~IGameSystem() {}

IGameSystemPerFrame::~IGameSystemPerFrame() {}

// Those vtables carry deleting destructors that name the global operator
// delete. Nothing here deletes a client system, and the
// library has no heap: its own, which the version script keeps out of the
// exports, does nothing. On MSVC fwd_physics.cpp defines it for both
// libraries under SS_NO_CRT (a second definition is LNK2005).
#if !defined(_MSC_VER)
void operator delete(void*, size_t) noexcept {}
#endif

namespace
{
// SourceSharp.Client.Interop.LeafOpKind.
// 1, 2 and 8 were the shadow manager's (retired at S2).
enum
{
	OP_SET_RENDER_HANDLE = 3,
	OP_DELETE_SUBSYSTEM_DATA = 4,
	OP_LIST_ENTRY = 5,
	OP_LIST_OVERFLOW = 6,
	OP_VIEW_MODEL = 7,
};

enum
{
	OP_WORDS = 4,
	OP_CHUNK = 64, // operations per buffer: 2 KB of stack; Drain loops for more
	LEAVES_PER_RENDERABLE = 128,
	FLAG_DRAW_DETAIL = 1,
	FLAG_DRAW_TRANSLUCENT = 2,
	FLAG_DRAW_LEAF_FILTER = 4, // a debug build: cl_drawleaf filters the list
};

// UNDER A PAGE PER BUFFER: MSVC probes a frame over 4 KB through __chkstk,
// the CRT's, which this runtime-free library cannot link (LNK2019 on CI at
// f6f1389, when a buffer was 16 KB). A function may hold a buffer and the
// leaves array beside it.
static_assert(OP_WORDS * OP_CHUNK * sizeof(int64_t) <= 2048, "an op buffer stays under half a page");

// Where an operation's results go, per call. Its members are set one by
// one: a zeroing initialiser is a memset, which this library does not have.
struct DrainTarget_t
{
	DrainTarget_t()
		: pList(nullptr)
		, pOpaque(nullptr)
		, pTranslucent(nullptr)
	{
	}

	CClientRenderablesList* pList;
	void* pOpaque;
	void* pTranslucent;
};

int32_t Bits(float f)
{
	union
	{
		float f;
		int32_t i;
	} u;

	u.f = f;
	return u.i;
}

void PutVector(int32_t* pOut, const Vector& v)
{
	pOut[0] = Bits(v.x);
	pOut[1] = Bits(v.y);
	pOut[2] = Bits(v.z);
}

class SSClientLeafSystem final : public IClientLeafSystem
{
public:
	explicit SSClientLeafSystem(const SSLeafSystemEnv* pEnv)
		: m_pEnv(pEnv)
		, m_nPending(0)
	{
	}

	static void operator delete(void*, size_t) {}

	// -- IGameSystemPerFrame: stock's were empty but for these ----------
	char const* Name() override { return "CClientLeafSystem"; }

	bool IsPerFrame() override { return true; }

	bool Init() override { return true; }

	void PostInit() override {}

	void Shutdown() override {}

	void LevelInitPostEntity() override {}

	void LevelShutdownPreEntity() override {}

	void OnSave() override {}

	void OnRestore() override {}

	void SafeRemoveIfDesired() override {}

	void Update(float) override {}

	void PostRender() override {}

	void LevelInitPreEntity() override
	{
		IManagedLeafSystemClient* pTarget = Target();
		IVEngineClient* pEngine = *m_pEnv->ppEngine;
		if (pTarget && pEngine)
			pTarget->LevelInitPreEntity(pEngine->LevelLeafCount());
	}

	void LevelShutdownPostEntity() override
	{
		if (IManagedLeafSystemClient* pTarget = Target())
		{
			int64_t ops[OP_WORDS * OP_CHUNK];
			Drain(pTarget, ops, pTarget->LevelShutdownPostEntity(ops, OP_CHUNK), DrainTarget_t());
		}
	}

	void PreRender() override
	{
		// The frame's queue rides in PreRender's own arguments: managed
		// reads it before anything it does can queue more.
		if (IManagedLeafSystemClient* pTarget = *m_pEnv->ppTarget)
		{
			int64_t ops[OP_WORDS * OP_CHUNK];
			int nPending;
			const int32_t* pPending = TakeQueue(nPending);
			Drain(pTarget, ops, pTarget->PreRender(pPending, nPending, ops, OP_CHUNK), DrainTarget_t());
		}
	}

	// -- IClientLeafSystemEngine ---------------------------------------
	void CreateRenderableHandle(IClientRenderable* pRenderable, bool bIsStaticProp) override
	{
		IManagedLeafSystemClient* pTarget = Target();
		if (!pTarget || !pRenderable)
			return;
		pRenderable->RenderHandle() = (ClientRenderHandle_t)pTarget->CreateRenderableHandle(
			pRenderable, bIsStaticProp ? 1 : 0, ModelType(pRenderable), pRenderable->IsTransparent() ? 1 : 0,
			pRenderable->IsTwoPass() ? 1 : 0);
	}

	void RemoveRenderable(ClientRenderHandle_t handle) override
	{
		if (IManagedLeafSystemClient* pTarget = Target())
		{
			int64_t ops[OP_WORDS * OP_CHUNK];
			Drain(pTarget, ops, pTarget->RemoveRenderable(handle, ops, OP_CHUNK), DrainTarget_t());
		}
	}

	void AddRenderableToLeaves(ClientRenderHandle_t handle, int nLeafCount, unsigned short* pLeaves) override
	{
		if (IManagedLeafSystemClient* pTarget = Target())
		{
			int64_t ops[OP_WORDS * OP_CHUNK];
			Drain(pTarget, ops, pTarget->AddRenderableToLeaves(handle, pLeaves, nLeafCount, ops, OP_CHUNK),
				  DrainTarget_t());
		}
	}

	// Queued (L3): the engine's static props call it from every fade's
	// ComputeFxBlend, per prop per frame. Handed over with the next call.
	void ChangeRenderableRenderGroup(ClientRenderHandle_t handle, RenderGroup_t group) override
	{
		Queue(PENDING_CHANGE_GROUP, handle, group);
	}

	// -- IClientLeafSystem ---------------------------------------------
	void AddRenderable(IClientRenderable* pRenderable, RenderGroup_t group) override
	{
		IManagedLeafSystemClient* pTarget = Target();
		if (!pTarget || !pRenderable)
			return;
		pRenderable->RenderHandle() =
			(ClientRenderHandle_t)pTarget->AddRenderable(pRenderable, group, ModelType(pRenderable));
	}

	bool IsRenderableInPVS(IClientRenderable* pRenderable) override
	{
		IManagedLeafSystemClient* pTarget = Target();
		IVRenderView* pRender = *m_pEnv->ppRender;
		if (!pTarget || !pRender || !pRenderable)
			return false;
		int leaves[LEAVES_PER_RENDERABLE];
		const int nLeaves = pTarget->GetRenderableLeaves(pRenderable->RenderHandle(), leaves);
		if (nLeaves < 0 || nLeaves > LEAVES_PER_RENDERABLE)
			return false;
		return pRender->AreAnyLeavesVisible(leaves, nLeaves);
	}

	void SetSubSystemDataInLeaf(int leaf, int nSubSystemIdx, CClientLeafSubSystemData* pData) override
	{
		if (IManagedLeafSystemClient* pTarget = Target())
			delete static_cast<CClientLeafSubSystemData*>(pTarget->SetSubSystemDataInLeaf(leaf, nSubSystemIdx, pData));
	}

	CClientLeafSubSystemData* GetSubSystemDataInLeaf(int leaf, int nSubSystemIdx) override
	{
		IManagedLeafSystemClient* pTarget = Target();
		return pTarget ? static_cast<CClientLeafSubSystemData*>(pTarget->GetSubSystemDataInLeaf(leaf, nSubSystemIdx))
					   : nullptr;
	}

	void SetDetailObjectsInLeaf(int leaf, int firstDetailObject, int detailObjectCount) override
	{
		if (IManagedLeafSystemClient* pTarget = Target())
			pTarget->SetDetailObjectsInLeaf(leaf, firstDetailObject, detailObjectCount);
	}

	void GetDetailObjectsInLeaf(int leaf, int& firstDetailObject, int& detailObjectCount) override
	{
		IManagedLeafSystemClient* pTarget = Target();
		int32_t range[2];
		range[0] = range[1] = 0; // not `= { 0, 0 }`, which is a memset
		if (pTarget && pTarget->GetDetailObjectsInLeaf(leaf, range))
		{
			firstDetailObject = range[0];
			detailObjectCount = range[1];
		}
	}

	void DrawDetailObjectsInLeaf(int leaf, int frameNumber, int& firstDetailObject, int& detailObjectCount) override
	{
		int32_t range[2];
		range[0] = range[1] = 0; // not `= { 0, 0 }`, which is a memset
		if (IManagedLeafSystemClient* pTarget = Target())
			pTarget->DrawDetailObjectsInLeaf(leaf, frameNumber, range);
		firstDetailObject = range[0];
		detailObjectCount = range[1];
	}

	bool ShouldDrawDetailObjectsInLeaf(int leaf, int frameNumber) override
	{
		IManagedLeafSystemClient* pTarget = Target();
		return pTarget && pTarget->ShouldDrawDetailObjectsInLeaf(leaf, frameNumber) != 0;
	}

	// Queued (L3): every moving renderable calls it, per frame. Handed over
	// with the next call, PreRender's at the latest.
	void RenderableChanged(ClientRenderHandle_t handle) override { Queue(PENDING_RENDERABLE_CHANGED, handle, 0); }

	// Queued too: a fading renderable sets its group every frame.
	void SetRenderGroup(ClientRenderHandle_t handle, RenderGroup_t group) override
	{
		Queue(PENDING_SET_GROUP, handle, group);
	}

	void ComputeTranslucentRenderLeaf(int count, const LeafIndex_t* pLeafList, const LeafFogVolume_t*, int frameNumber,
									  int viewID) override
	{
		IManagedLeafSystemClient* pTarget = *m_pEnv->ppTarget;
		CGlobalVarsBase* pGlobals = *m_pEnv->ppGlobals;
		if (!pTarget || !pGlobals)
			return;
		int nPending;
		const int32_t* pPending = TakeQueue(nPending);
		pTarget->ComputeTranslucentRenderLeaf(pLeafList, count, frameNumber, viewID, pGlobals->framecount, this,
											  pPending, nPending);
	}

	void BuildRenderablesList(const SetupRenderInfo_t& info) override
	{
		IManagedLeafSystemClient* pTarget = *m_pEnv->ppTarget;
		if (!pTarget || !info.m_pWorldListInfo || !info.m_pRenderList)
			return;
		const float originForward[6] = {info.m_vecRenderOrigin.x,  info.m_vecRenderOrigin.y,
										info.m_vecRenderOrigin.z,  info.m_vecRenderForward.x,
										info.m_vecRenderForward.y, info.m_vecRenderForward.z};
		int nFlags = (info.m_bDrawDetailObjects ? FLAG_DRAW_DETAIL : 0) |
			(info.m_bDrawTranslucentObjects ? FLAG_DRAW_TRANSLUCENT : 0);
#ifdef _DEBUG
		nFlags |= FLAG_DRAW_LEAF_FILTER;
#endif
		IDetailObjectSystem* pDetail = m_pEnv->pfnDetailObjectSystem ? m_pEnv->pfnDetailObjectSystem() : nullptr;
		DrainTarget_t target;
		target.pList = info.m_pRenderList;
		int64_t ops[OP_WORDS * OP_CHUNK];
		int nPending;
		const int32_t* pPending = TakeQueue(nPending);
		const int nOps = pTarget->BuildRenderablesList(
			info.m_pWorldListInfo->m_pLeafList, info.m_pWorldListInfo->m_LeafCount, originForward, info.m_nRenderFrame,
			info.m_nDetailBuildFrame, nFlags, pDetail, ops, OP_CHUNK, pPending, nPending);
		Drain(pTarget, ops, nOps, target);
	}

	void CollateViewModelRenderables(CUtlVector<IClientRenderable*>& opaque,
									 CUtlVector<IClientRenderable*>& translucent) override
	{
		IManagedLeafSystemClient* pTarget = *m_pEnv->ppTarget;
		if (!pTarget || !m_pEnv->pfnAppendRenderable)
			return;
		DrainTarget_t target;
		target.pOpaque = &opaque;
		target.pTranslucent = &translucent;
		int64_t ops[OP_WORDS * OP_CHUNK];
		int nPending;
		const int32_t* pPending = TakeQueue(nPending);
		Drain(pTarget, ops, pTarget->CollateViewModelRenderables(ops, OP_CHUNK, this, pPending, nPending), target);
	}

	void DrawStaticProps(bool enable) override
	{
		if (IManagedLeafSystemClient* pTarget = Target())
			pTarget->DrawStaticProps(enable ? 1 : 0);
	}

	void DrawSmallEntities(bool enable) override
	{
		if (IManagedLeafSystemClient* pTarget = Target())
			pTarget->DrawSmallEntities(enable ? 1 : 0);
	}

	// The shadow half: no caller since S2 (see the header).
	ClientLeafShadowHandle_t AddShadow(ClientShadowHandle_t, unsigned short) override
	{
		return (ClientLeafShadowHandle_t)CLIENT_LEAF_SHADOW_INVALID_HANDLE;
	}

	void RemoveShadow(ClientLeafShadowHandle_t) override {}

	void ProjectShadow(ClientLeafShadowHandle_t, int, const int*) override {}

	void ProjectFlashlight(ClientLeafShadowHandle_t, int, const int*) override {}

	void EnumerateShadowsInLeaves(int, LeafIndex_t*, IClientLeafShadowEnum*) override {}

	int GetRenderableLeaves(ClientRenderHandle_t handle, int leaves[128]) override
	{
		IManagedLeafSystemClient* pTarget = Target();
		return pTarget ? pTarget->GetRenderableLeaves(handle, leaves) : -1;
	}

	bool GetRenderableLeaf(ClientRenderHandle_t handle, int* pOutLeaf, const int* pInIterator,
						   int* pOutIterator) override
	{
		IManagedLeafSystemClient* pTarget = Target();
		if (!pTarget || !pOutLeaf)
			return false;
		int32_t leafAndIterator[2];
		leafAndIterator[0] = leafAndIterator[1] = -1;
		if (!pTarget->GetRenderableLeaf(handle, pInIterator ? 1 : 0, pInIterator ? *pInIterator : -1, leafAndIterator))
			return false;
		*pOutLeaf = leafAndIterator[0];
		if (pOutIterator)
			*pOutIterator = leafAndIterator[1];
		return true;
	}

	void EnableAlternateSorting(ClientRenderHandle_t handle, bool bEnable) override
	{
		if (IManagedLeafSystemClient* pTarget = Target())
			pTarget->EnableAlternateSorting(handle, bEnable ? 1 : 0);
	}

	// The queued calls, in order, to managed: before any other call reaches
	// it (Target), and at the end of a blend batch, whose static props queue
	// their group changes while managed waits for the batch. Nothing to do
	// with no managed area (outside a load): the queue is dropped, as the
	// calls themselves were.
	// The queue for a call that carries it in its arguments, emptied: managed
	// reads the entries before anything it does can queue more.
	const int32_t* TakeQueue(int& nCount)
	{
		nCount = m_nPending;
		m_nPending = 0;
		return m_Pending;
	}

	// The queue into pOut (nMax entries of three words), emptied: the
	// entries written, or 0 with them handed over through Pending when
	// they do not fit.
	int32_t TakePending(int32_t* pOut, int32_t nMax)
	{
		const int n = m_nPending;
		if (!pOut || n > nMax)
		{
			Flush();
			return 0;
		}
		for (int i = 0; i < PENDING_WORDS * n; ++i)
			pOut[i] = m_Pending[i];
		m_nPending = 0;
		return n;
	}

	void Flush()
	{
		const int n = m_nPending;
		m_nPending = 0;
		IManagedLeafSystemClient* pTarget = *m_pEnv->ppTarget;
		if (pTarget && n > 0)
			pTarget->Pending(m_Pending, n);
	}

private:
	IManagedLeafSystemClient* Target()
	{
		if (m_nPending > 0)
			Flush();
		return *m_pEnv->ppTarget;
	}

	enum
	{
		PENDING_RENDERABLE_CHANGED = 1,
		PENDING_CHANGE_GROUP = 2,
		PENDING_SET_GROUP = 3,
		PENDING_WORDS = 3,
		PENDING_MAX = 256,
	};

	void Queue(int32_t kind, int32_t handle, int32_t value)
	{
		if (m_nPending == PENDING_MAX)
			Flush();
		int32_t* p = m_Pending + PENDING_WORDS * m_nPending++;
		p[0] = kind;
		p[1] = handle;
		p[2] = value;
	}

	int ModelType(IClientRenderable* pRenderable) const
	{
		IVModelInfoClient* pModelInfo = *m_pEnv->ppModelInfo;
		return pModelInfo ? pModelInfo->GetModelType(pRenderable->GetModel()) : 0;
	}

	// The first nOps operations in ops, then any further ones a full
	// buffer says are waiting, each carried out in order.
	void Drain(IManagedLeafSystemClient* pTarget, int64_t* pOps, int nOps, const DrainTarget_t& target)
	{
		while (nOps > 0)
		{
			const int n = nOps > OP_CHUNK ? (int)OP_CHUNK : nOps;
			for (int i = 0; i < n; ++i)
				Execute(pOps + OP_WORDS * i, target);
			if (n < OP_CHUNK)
				return;
			nOps = pTarget->TakeOps(pOps, OP_CHUNK);
		}
	}

	void Execute(const int64_t* op, const DrainTarget_t& target)
	{
		IClientRenderable* pRenderable = reinterpret_cast<IClientRenderable*>((intptr_t)op[1]);
		switch (op[0])
		{
			case OP_SET_RENDER_HANDLE:
				if (pRenderable)
					pRenderable->RenderHandle() = (ClientRenderHandle_t)op[2];
				break;
			case OP_DELETE_SUBSYSTEM_DATA:
				delete reinterpret_cast<CClientLeafSubSystemData*>((intptr_t)op[1]);
				break;
			case OP_LIST_ENTRY:
			{
				CClientRenderablesList* pList = target.pList;
				const int group = (int)op[3];
				if (!pList || group < 0 || group >= RENDER_GROUP_COUNT)
					break;
				int& count = pList->m_RenderGroupCounts[group];
				if (count < 0 || count >= CClientRenderablesList::MAX_GROUP_ENTITIES)
					break;
				CClientRenderablesList::CEntry& entry = pList->m_RenderGroups[group][count++];
				entry.m_pRenderable = pRenderable;
				entry.m_RenderHandle = (ClientRenderHandle_t)(op[2] & 0xFFFF);
				entry.m_iWorldListInfoLeaf = (unsigned short)((op[2] >> 16) & 0xFFFF);
				entry.m_TwoPass = (unsigned short)((op[2] >> 32) & 0xFFFF);
				break;
			}
			case OP_LIST_OVERFLOW:
			{
				IVEngineClient* pEngine = *m_pEnv->ppEngine;
				for (int g = 0; g < RENDER_GROUP_COUNT && pEngine; ++g)
				{
					if (op[2] & (1LL << g))
						pEngine->Con_NPrintf(10, "Warning: overflowed CClientRenderablesList group %d", g);
				}
				break;
			}
			case OP_VIEW_MODEL:
			{
				void* pVector = op[2] ? target.pTranslucent : target.pOpaque;
				if (pVector && pRenderable && m_pEnv->pfnAppendRenderable)
					m_pEnv->pfnAppendRenderable(pVector, pRenderable);
				break;
			}
			default:
				break;
		}
	}

	const SSLeafSystemEnv* m_pEnv;
	int m_nPending;
	int32_t m_Pending[PENDING_WORDS * PENDING_MAX];
};

static_assert(sizeof(SSClientLeafSystem) <= kSSLeafSystemBytes, "fits client.so's storage");

// The engine's EnumerateLeavesInBox calls this back per leaf: the leaves
// are written after the box's count, as far as the buffer goes; past it,
// only counted, so the caller learns the size it needs.
class CLeafCollector final : public ISpatialLeafEnumerator
{
public:
	CLeafCollector(int32_t* pOut, int32_t nMax)
		: m_pOut(pOut)
		, m_nMax(nMax)
	{
	}

	bool EnumerateLeaf(int leaf, intp) override
	{
		if (m_nWords < m_nMax)
			m_pOut[m_nWords] = leaf;
		++m_nWords;
		++m_nInBox;
		return true;
	}

	int32_t* m_pOut;
	int32_t m_nMax;
	int32_t m_nWords = 0;
	int32_t m_nInBox = 0;
};
}

int SSFwd_LeafSystem_SizeOf()
{
	return int(kSSLeafSystemBytes);
}

void* SSFwd_LeafSystem_Construct(void* pMemory, const SSLeafSystemEnv* pEnv)
{
	if (!pMemory || !pEnv || !pEnv->ppTarget || !pEnv->ppEngine || !pEnv->ppModelInfo || !pEnv->ppRender ||
		!pEnv->ppGlobals)
		return nullptr;
	return static_cast<IClientLeafSystem*>(new (pMemory) SSClientLeafSystem(pEnv));
}

int32_t SSFwd_LeafSystem_ComputeFxBlends(void* pSystem, void** ppRenderables, int32_t nCount, int32_t* pPending,
										 int32_t nMaxPending)
{
	for (int32_t i = 0; ppRenderables && i < nCount; ++i)
	{
		if (IClientRenderable* p = static_cast<IClientRenderable*>(ppRenderables[i]))
			p->ComputeFxBlend();
	}
	// The group changes the blends queued (a fading static prop's), returned
	// to the managed decision that reads them.
	if (!pSystem)
		return 0;
	return static_cast<SSClientLeafSystem*>(static_cast<IClientLeafSystem*>(pSystem))
		->TakePending(pPending, nMaxPending);
}

void SSFwd_LeafSystem_Facts(void** ppRenderables, const int32_t* pWhat, int32_t nCount, int32_t* pFacts)
{
	if (!ppRenderables || !pWhat || !pFacts)
		return;
	for (int32_t i = 0; i < nCount; ++i)
	{
		IClientRenderable* p = static_cast<IClientRenderable*>(ppRenderables[i]);
		if (!p)
			continue;
		const int32_t what = pWhat[i];
		int32_t* pOut = pFacts + 17 * i;
		if (what & 1)
			pOut[0] = p->GetFxBlend();
		if (what & 2)
			pOut[1] = p->IsTransparent() ? 1 : 0;
		if (what & 4)
		{
			Vector mins, maxs;
			p->GetRenderBoundsWorldspace(mins, maxs);
			PutVector(pOut + 2, mins);
			PutVector(pOut + 5, maxs);
		}
		if (what & 8)
		{
			Vector mins, maxs;
			p->GetRenderBounds(mins, maxs);
			PutVector(pOut + 8, mins);
			PutVector(pOut + 11, maxs);
		}
		if (what & 16)
			PutVector(pOut + 14, p->GetRenderOrigin());
	}
}

void SSFwd_LeafSystem_Receives(void** ppRenderables, const int32_t* pFlags, int32_t nCount, int32_t* pAnswers)
{
	for (int32_t i = 0; ppRenderables && pFlags && pAnswers && i < nCount; ++i)
	{
		IClientRenderable* p = static_cast<IClientRenderable*>(ppRenderables[i]);
		pAnswers[i] = p && p->ShouldReceiveProjectedTextures(pFlags[i]) ? 1 : 0;
	}
}

int32_t SSFwd_LeafSystem_EnumerateLeaves(void* pEngine, const float* pBoxes, int32_t nBoxes, int32_t* pOut,
										 int32_t nMaxWords)
{
	IVEngineClient* pClient = static_cast<IVEngineClient*>(pEngine);
	ISpatialQuery* pQuery = pClient ? pClient->GetBSPTreeQuery() : nullptr;
	if (!pQuery || !pBoxes || !pOut)
		return 0;
	CLeafCollector collector(pOut, nMaxWords);
	for (int32_t b = 0; b < nBoxes; ++b)
	{
		const float* pBox = pBoxes + 6 * b;
		const int32_t at = collector.m_nWords++;
		collector.m_nInBox = 0;
		pQuery->EnumerateLeavesInBox(Vector(pBox[0], pBox[1], pBox[2]), Vector(pBox[3], pBox[4], pBox[5]), &collector,
									 0);
		if (at < nMaxWords)
			pOut[at] = collector.m_nInBox;
	}
	return collector.m_nWords <= nMaxWords ? collector.m_nWords : -collector.m_nWords;
}

void SSFwd_LeafSystem_LeavesAreas(void* pEngine, const int32_t* pRuns, int32_t nRuns, int32_t* pAreas)
{
	IVEngineClient* pClient = static_cast<IVEngineClient*>(pEngine);
	if (!pClient || !pRuns || !pAreas)
		return;
	for (int32_t r = 0, at = 0; r < nRuns; ++r)
	{
		const int32_t n = pRuns[at++];
		pAreas[r] = pClient->GetLeavesArea(const_cast<int*>(pRuns + at), n);
		at += n;
	}
}

void SSFwd_LeafSystem_BoxQueries(void* pEngine, const float* pBoxes, const int32_t* pPrograms, int32_t nCount,
								 int32_t* pAnswers)
{
	IVEngineClient* pClient = static_cast<IVEngineClient*>(pEngine);
	if (!pClient || !pBoxes || !pPrograms || !pAnswers)
		return;
	for (int32_t b = 0; b < nCount; ++b)
	{
		const float* pBox = pBoxes + 6 * b;
		const Vector mins(pBox[0], pBox[1], pBox[2]), maxs(pBox[3], pBox[4], pBox[5]);
		const int32_t program = pPrograms[b];
		int32_t answer = 0;
		if ((program & 1) && !pClient->DoesBoxTouchAreaFrustum(mins, maxs, program >> 8))
			answer = 1;
		else if ((program & 2) && pClient->CullBox(mins, maxs))
			answer = 2;
		else if ((program & 4) && pClient->IsOccluded(mins, maxs))
			answer = 4;
		pAnswers[b] = answer;
	}
}

void SSFwd_LeafSystem_DetailModels(void* pDetailSystem, const int32_t* pRanges, int32_t nRanges, void** ppModels)
{
	IDetailObjectSystem* pSystem = static_cast<IDetailObjectSystem*>(pDetailSystem);
	if (!pRanges || !ppModels)
		return;
	for (int32_t r = 0, at = 0; r < nRanges; r += 2)
	{
		for (int32_t k = 0; k < pRanges[r + 1]; ++k)
			ppModels[at++] = pSystem ? pSystem->GetDetailModel(pRanges[r] + k) : nullptr;
	}
}
