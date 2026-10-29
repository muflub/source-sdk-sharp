//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client leaf system's functions, as the table names them =//
//
// C7 cluster 12 L2 (docs/plans/c7_leaf_system.md). fwd_leaf_system.cpp writes
// these and the client library's generated table (fwd_client.g.inc,
// tools/engine-forwarders' HAND row LeafSystem) lays them out, in this order.
// They are the library's own: its version script exports none of them.
// client.so reaches SizeOf and Construct through the table to build the leaf
// system it exports; managed calls the batches.
//=============================================================================//
#pragma once

#include <stdint.h>

class IClientRenderable;
class IDetailObjectSystem;
class IManagedLeafSystemClient;
class IVEngineClient;
class IVModelInfoClient;
class IVRenderView;
class CGlobalVarsBase;

// What the forwarder class reaches in client.so, handed to it once by
// address: the library has no globals of client.so's, and the pointers are
// set after the object is built (the managed area outside a load is NULL).
struct SSLeafSystemEnv
{
	IManagedLeafSystemClient* const* ppTarget;
	IVEngineClient* const* ppEngine;
	IVModelInfoClient* const* ppModelInfo;
	IVRenderView* const* ppRender;
	CGlobalVarsBase* const* ppGlobals;
	IDetailObjectSystem* (*pfnDetailObjectSystem)();
	// CUtlVector< IClientRenderable * >::AddToTail, which allocates: the
	// library has no heap, so client.so appends.
	void (*pfnAppendRenderable)(void* pVector, IClientRenderable* pRenderable);
};

// The forwarder object's size, at most: client.so's storage for it.
enum
{
	kSSLeafSystemBytes = 4096
};

// How many bytes Construct needs.
int SSFwd_LeafSystem_SizeOf();

// Builds the leaf system in pMemory (SizeOf bytes, pointer-aligned) over the
// environment, which must outlive it; answers it as an IClientLeafSystem *.
// NULL, and nothing built, without memory or an environment.
void* SSFwd_LeafSystem_Construct(void* pMemory, const SSLeafSystemEnv* pEnv);

// IClientRenderable::ComputeFxBlend of each, in order; then the calls the
// blends queued on pSystem (the leaf system Construct built, or NULL) are
// written to pPending (three words each, nMaxPending at most) and their count
// returned. 0 with no system; a queue that does not fit is handed to managed
// through Pending instead, and 0 returned.
int32_t SSFwd_LeafSystem_ComputeFxBlends(void* pSystem, void** ppRenderables, int32_t nCount, int32_t* pPending,
										 int32_t nMaxPending);

// Each renderable's answers its mask asks for, into its 17 words (see
// SourceSharp.Client.Interop.LeafFacts): 1 GetFxBlend, 2 IsTransparent, 4
// GetRenderBoundsWorldspace, 8 GetRenderBounds, 16 GetRenderOrigin.
void SSFwd_LeafSystem_Facts(void** ppRenderables, const int32_t* pWhat, int32_t nCount, int32_t* pFacts);

// ShouldReceiveProjectedTextures( flags ) of each, 1 or 0.
void SSFwd_LeafSystem_Receives(void** ppRenderables, const int32_t* pFlags, int32_t nCount, int32_t* pAnswers);

// The leaves each box (mins, maxs) touches through the engine's
// EnumerateLeavesInBox: per box its count, then its leaves. The words
// written, or the negative of the words needed.
int32_t SSFwd_LeafSystem_EnumerateLeaves(void* pEngine, const float* pBoxes, int32_t nBoxes, int32_t* pOut,
										 int32_t nMaxWords);

// GetLeavesArea of each run (a count, then the leaves).
void SSFwd_LeafSystem_LeavesAreas(void* pEngine, const int32_t* pRuns, int32_t nRuns, int32_t* pAreas);

// The culling questions over boxes, each box's program its tests in order
// (1 DoesBoxTouchAreaFrustum, the area in bits 8 and up; 2 CullBox; 4
// IsOccluded) up to the first that rejects; each answer that test's bit, or 0.
void SSFwd_LeafSystem_BoxQueries(void* pEngine, const float* pBoxes, const int32_t* pPrograms, int32_t nCount,
								 int32_t* pAnswers);

// IDetailObjectSystem::GetDetailModel over ranges (a first and a count each).
void SSFwd_LeafSystem_DetailModels(void* pDetailSystem, const int32_t* pRanges, int32_t nRanges, void** ppModels);
