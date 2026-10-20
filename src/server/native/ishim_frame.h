//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the frame's native steps, as an interface ==========//
//
// plan_native_api.md Part 4, and the pilot of it. Seventeen flat ABI fields
// became seventeen slots of one vtable, and two have been appended since, so it
// is NINETEEN now -- reached by the same acquire-by-name,
// call-by-vtable mechanism the five ENGINE interfaces already use — the
// difference being only which factory answers: SOURCESHARP_FACTORY_INTERNAL
// rather than the engine's.
//
// WHY VERIFYING THIS IS CHEAP, AND IT IS NOT THE SAME ARGUMENT AS §2's.
// docs/vtable-binding.md's apparatus exists because a generated layout can go
// stale against a PREBUILT module compiled from headers nobody here controls.
// A shim class has one provenance: this header is in this tree, the object is
// compiled from this header in the same build, by the same compiler, with the
// same defines, and tools/vtable-layout generates the managed layout from this
// same header and diffs it against the committed file on every run.
//
//-----------------------------------------------------------------------------
// APPEND-ONLY, BY RULE, AND THE SPAN CHECK IS WHY.
//
// Acquisition holds the managed layout's slot count N against the count native
// takes from the compiler, M, and requires N <= M. That is sufficient for
// APPENDS and for nothing else: adding a virtual to a C++ class puts it at the
// end and moves nothing, so every slot managed already knows keeps its index.
// An INSERT or a REORDER in the middle passes the span check and silently
// misbinds — the wrong function returns whatever it left in the return
// register and the symptom arrives somewhere else, later.
//
// So: new methods go at the END. Nothing is inserted, nothing is reordered,
// nothing is removed — a retired method keeps its slot as a stub until the next
// deliberate ...002 version string. What enforces it is the COMMITTED generated
// layout: an insert shows up as a diff in the middle of a file whose rule is
// end-only growth, and tools/vtable-layout fails the run.
//
//-----------------------------------------------------------------------------
// NO VIRTUAL DESTRUCTOR, AND THAT IS A DECISION RATHER THAN AN OMISSION.
//
// A virtual destructor takes the FIRST TWO SLOTS under the Itanium ABI
// (complete and deleting), so declaring one here would shift every method below
// it by two and buy nothing: the object behind this interface is a singleton
// that lives as long as the DLL and is never deleted through a base pointer.
// IUniformRandomStream is the same shape and says so at
// public/vstdlib/random.h:28, where the declaration is commented out.
//
//-----------------------------------------------------------------------------
// THE ORDER IS THE FILL'S ORDER, and it is not arbitrary. These nineteen
// methods appear here in the order sourcesharp.h declares the fields they
// replace, so that the header, the layout, the impl and the facade can all be
// read against each other in one direction. It is NOT the order the frame runs
// in — that order is in C#, in ServerGameDll.cs, which is the whole point of
// there being nineteen steps rather than one GameFrame().
//
//=============================================================================//

#ifndef ISHIM_FRAME_H
#define ISHIM_FRAME_H
#ifdef _WIN32
#pragma once
#endif

#include "tier0/platform.h"

//-----------------------------------------------------------------------------
// The version string, in the engine's own convention. SourceSharp<Area>001,
// with ...Client001 / ...Server001 where an area splits — this one does not:
// every step below is a line of CServerGameDLL::GameFrame or ::PreClientUpdate
// and the client has no GameFrame to have steps of.
//-----------------------------------------------------------------------------
#define SOURCESHARP_SHIM_FRAME_VERSION "SourceSharpFrame001"

abstract_class IShimFrame
{
public:
	// Whether this server simulates on alternate ticks. A QUESTION, not a
	// decision: C# does the branching.
	virtual int32_t IsSimulatingOnAlternateTicks() = 0;

	// gpGlobals->tickcount.
	//
	// [SuppressGCTransition] IS NOT CARRIED ONTO THIS ONE, and its flat field
	// did not carry it either. The managed facade reads the tick out of the
	// engine's own CGlobalVars through the bound pointer when it has one and
	// only falls back to this call when it does not, so this is the cold path.
	virtual int32_t TickCount() = 0;

	// gpGlobals->frametime, read. Cold for the same reason as TickCount.
	virtual float GetFrameTime() = 0;

	// gpGlobals->frametime, written.
	//
	// ITS FLAT FIELD CARRIED [SuppressGCTransition] AND THE REASON IS RECORDED
	// HERE RATHER THAN LOST: one field write, a plain float, no call, and it
	// crosses once per frame. The read beside it is a Globals load and the
	// write is a mutation of the engine's own struct in the middle of its
	// frame, which is why the pair is asymmetric.
	virtual void SetFrameTime( float flFrameTime ) = 0;

	// g_flServerCurTime = gpGlobals->curtime -- the rollback-independent server
	// clock. Its flat field carried [SuppressGCTransition] too: one read and
	// one field write, both plain floats, no call, 1.000 crossings a frame.
	virtual void SetServerCurTime() = 0;

	// SS_RETIRED PhysicsRunThinkFunctions lane=3b-enginebind physics_main.cpp left the vpc; the SimThink list has no members and the thinks are managed
	// Stock's every-entity think pass. The body is empty and the slot is kept
	// at its index until the SourceSharpFrame002 pass; see shell_frame.cpp.
	virtual void PhysicsRunThinkFunctions( int32_t bSimulating ) = 0;

	// SS_RETIRED ServiceEventQueue lane=3b-enginebind the native queue and every producer that posted into it left the vpc; the managed EntityIoQueue is the queue
	// "service event queue, firing off any actions whos time has come" --
	// stock's comment and stock's spelling. Empty; slot kept at its index.
	virtual void ServiceEventQueue() = 0;

	virtual void UpdateAllClientData() = 0;

	// The rules' end of frame, where the third managed frame point lives. The
	// null test on g_pGameRules stays native: it is not a policy, it is the
	// fact that the rules do not exist between levels, and a C# caller would
	// need a second step to ask and the answer would be stale by the time it
	// acted on it.
	virtual void EndGameFrame() = 0;

	// SS_RETIRED FireNetworkPropertyEvents lane=3b-enginebind g_NetworkPropertyEventMgr went with timedeventmgr.cpp at Phase 1d; nothing could ever register
	// "Any entities that detect network state changes on a timer do it here."
	// Empty since two lanes met in it; slot kept at its index.
	virtual void FireNetworkPropertyEvents() = 0;

	virtual void GamestatsUpdateConnection() = 0;

	// NOT behind _DEBUG -- stock's own comment beside the call is "allow this
	// in release for now", and it is what makes every NDebugOverlay call in
	// managed code visible.
	virtual void DrawAllDebugOverlays() = 0;

	// The parallel hierarchy's post-think steps. Separate rather than one
	// because the order BETWEEN them was a COMMENT in C++ ("Players first, so
	// an entity's think this tick sees where the players moved this tick"),
	// and a comment that decides an order is a decision, which a step may not
	// be.
	//
	// StandaloneUpdate and StandaloneFinish are the two halves of what used to
	// be one StandaloneRunFrame, and the reason they are two is the same rule
	// one step further on: the DUE THINKS run between them and they are managed
	// now (plan_entity.md §3.4, step 1g). Update does touch links, movement and
	// flight -- everything a think should see this frame; Finish composes the
	// attachment-parented children and drains the deferred deletes -- everything
	// that has to see what the thinks did. Which side each belongs on is
	// argued in shell_standalone_base.cpp, and the order is now stated in C#.
	virtual void StandalonePlayerCommands() = 0;
	virtual void StandaloneUpdate() = 0;
	virtual void StandaloneFinish() = 0;

	// THE SDK HIERARCHY'S HALF OF ONE ENTITY I/O DELIVERY -- the sub-ioqueue
	// lane. NOT a frame step: the managed queue asks it for ONE event, and only
	// when the managed directory answered nothing at all, so nothing can be
	// delivered twice. Answers how many SDK entities took the input.
	//
	// It is on this interface rather than a new one because the queue's frame
	// step is, and because a second interface for one leaf would be a second
	// vtable to keep in step for no gain.
	//
	// SS_RETIRED DeliverToSdkEntities lane=3b-enginebind all four stock classes it walked to are absent from the armed build; returns 0 and the managed directory is the whole population
	virtual int32_t DeliverToSdkEntities( const char *pTarget, const char *pInput,
	                                      const char *pValue ) = 0;

	// plan_server_native_v2 T4e: one NetworkStateChanged per entity whose
	// managed state was written this frame -- what the managed row's native
	// half did after the managed frame, when kServerSystems[] walked it. The
	// position, after the managed frame and before the snapshot, is managed's
	// (ServerSystems.FramePostThink).
	virtual void DrainEntitySlots() = 0;

	// plan_server_native_v2 T4e: mdlcache->BeginLock() / EndLock(), the
	// MDLCACHE_CRITICAL_SECTION every system's stage ran inside when
	// kServerSystems[]'s walk was native (stock's igamesystem.cpp walks take
	// it per system). The managed row's stages take it through these two,
	// paired, around the stage.
	virtual void BeginModelCacheLock() = 0;
	virtual void EndModelCacheLock() = 0;

	// NEW METHODS GO HERE, AT THE END. See the append-only rule above.
};

#endif // ISHIM_FRAME_H
