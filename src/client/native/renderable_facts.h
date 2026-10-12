//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the render path's facts, declared ==================//
//
// plan_entity.md step 7d, "Leaf system and shadows".
//
// WHAT THIS FILE IS FOR. The five render-path TUs of step 7d
// (clientleafsystem.cpp, clientshadowmgr.cpp, detailobjectsystem.cpp,
// c_pixel_visibility.cpp; glow_outline_effect.cpp, empty, left in C1) are ENGINE
// SYSTEMS, not entity classes: the engine asks each of them for something every
// frame and none of them is going away. What IS going away is the way they reached an
// entity to get their answers — `ClientEntityList()`, `C_BasePlayer::
// GetLocalPlayer()`, and the SDK move hierarchy. Step 7d's own sentence is
// "renderables addressed only through IClientRenderable and slot-table
// resolves", and this header is the surface that makes that sentence true.
//
// SAME SHAPE AND SAME REASON AS 7c's view_facts.h, which is deliberate: a stock
// TU asks for a FACT by name and never learns the type that answers it. In
// particular this header does NOT include shell_standalone_client.h. A render
// TU that pulled that in to ask "where are the players" would acquire the whole
// client entity surface, and the next lane's grep would read zero while the
// coupling was worse than before.
//
// Everything declared here takes and returns engine types only:
// IClientRenderable, ClientEntityHandle_t, Vector, float, int.
//
//=============================================================================//

#ifndef SOURCESHARP_RENDERABLE_FACTS_H
#define SOURCESHARP_RENDERABLE_FACTS_H
#ifdef _WIN32
#pragma once
#endif

#include "basehandle.h"
#include "mathlib/vector.h"

class IClientRenderable;

//-----------------------------------------------------------------------------
// THE HANDLE -> RENDERABLE RESOLVE. This is step 7d's "slot-table resolve".
//
// It replaces `ClientEntityList().GetClientRenderableFromHandle( h )`, and it
// is NOT a rename: the two are different objects.
//
//   ClientEntityList() is *cl_entitylist, the STOCK CClientEntityList.
//   The engine's VClientEntityList003 is CSourceSharpClientEntityList
//   (shell_cliententitylist.cpp), which answers out of §3.3's slot table and
//   falls back to the legacy list for the stock client renderables that are in
//   CClientEntityList and in no slot table.
//
// So a render TU asking the stock object was asking the OTHER of the client's
// two lists — it worked only because C_StandaloneEntity's constructor also does
// SourceSharp_LegacyListBind, i.e. because the fallback arm happened to be the
// arm that answered. This function asks the slot table FIRST, exactly as the
// interface the engine holds does, and keeps the legacy arm second so that
// nothing that resolves today stops resolving. Both arms are counted; see
// SourceSharp_RenderableResolveStats.
//
// MAY ANSWER NULL, and every one of the thirteen call sites in
// clientshadowmgr.cpp either tests it or Assert()s it, exactly as they did of
// the call this replaces. It is NOT under the constraint
// IClientEntityList::GetClientUnknownFromHandle is under (the engine's trace
// dereferences that one with no test); every caller of this one is in client.so
// and each was already written against a NULL-able answer.
//-----------------------------------------------------------------------------
IClientRenderable *SourceSharp_RenderableFromHandle( const CBaseHandle &hEnt );

//-----------------------------------------------------------------------------
// The two arms of the resolve above, counted, so that "the seam works" is a
// number rather than the absence of a crash. Reset by cs_cl_renderable_report.
//
//   nAsks         calls
//   nFromTable    answered by the slot table
//   nFromLegacy   answered by the legacy CClientEntityList arm
//   nUnresolved   answered NULL for a handle that was VALID
//
// nUnresolved is the one that matters: a NULL for an invalid handle is the
// caller asking about nothing, but a NULL for a valid handle is a renderable
// the render path has lost, and a lost renderable draws nothing and does not
// crash.
//-----------------------------------------------------------------------------
struct SourceSharpRenderableResolveStats_t
{
	int nAsks;
	int nFromTable;
	int nFromLegacy;
	int nUnresolved;
};

const SourceSharpRenderableResolveStats_t &SourceSharp_RenderableResolveStats();
void SourceSharp_ResetRenderableResolveStats();

//-----------------------------------------------------------------------------
// WHAT IS NOT HERE, AND WHY.
//
// 1. "players near a point", for the detail-sprite sway that pushes grass
//    aside. CDetailModel::UpdatePlayerAvoid needs it and both of stock's arms
//    for it are dead on this client; a restoration was written and then
//    withdrawn, because the function that would call it is not compiled:
//
//      detailobjectsystem.cpp:30   #if defined(DOD_DLL) || defined(CSTRIKE_DLL)
//                                  #define USE_DETAIL_SHAPES
//      grep -c 'DOD_DLL\|CSTRIKE_DLL' src/_vpc_/ninja/sdk_csharp_debug.ninja -> 0
//
//    The finding is recorded at the guard in detailobjectsystem.cpp instead, so
//    whoever turns that macro on reads it there rather than re-deriving it.
//
// 2. THE VIEWER'S FOV DISTANCE ADJUST FACTOR. It returned exactly 1.0f on every
//    path -- PlayerNetState carries no fov, so stock's localFOV/defaultFOV is 1
//    by construction -- and its three call sites multiplied or divided by it.
//    Deleted with those three sites by lane 4c-render. The absence it declared
//    on the not-shipped ledger is now row 6 of docs/absence-client.md, which is
//    where a reader wondering why a scope does not hold detail props in view
//    finds it; a per-frame call that always answers 1 is not what makes that
//    finding findable.
//
// 3. THE BONE-MERGE RENDER BOUNDS, THE SHADOW BONE-SETUP QUEUE, THE SHADOW
//    RENDERING CLIP PLANE AND THE NPC/PLAYER STATIC-PROP GATE. All four were
//    `return false`, and the queue's Add() was an empty body, in every build
//    that ever ran: each resolved a renderable back to an SDK C_BaseEntity, and
//    there is no such class on this link line. So the leaf system and the
//    shadow manager took their own general path at each of them every frame,
//    and Run() issued two zero-length ParallelProcess calls per frame over two
//    vectors nothing ever filled. Deleted with their call sites by lane
//    4c-render; leaf_cycle_test.cpp asserts the successor property, which is
//    that neither stock file reaches an SDK entity at those four places and
//    that each caller's general path still produces the bounds.
//-----------------------------------------------------------------------------

#endif // SOURCESHARP_RENDERABLE_FACTS_H
