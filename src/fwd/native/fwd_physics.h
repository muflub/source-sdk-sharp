//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the vphysics callbacks' functions, as the table names them =//
//
// plan_forwarders_everywhere.md phase 5. fwd_physics.cpp writes these and both
// libraries' generated table (tools/engine-forwarders' HAND row
// PhysicsCallbacks) lays them out, in this order. They are the library's own:
// its version script exports none of them.
//=============================================================================//
#pragma once

class IManagedPhysicsObjectEvent;
class IManagedPhysicsCollisionEvent;
class IManagedPhysicsCollisionSolver;
class IManagedPhysicsConstraintEvent;
class IManagedMotionEvent;
class IManagedVPhysicsKeyHandler;

// How many bytes each Construct builds in: at most the words a managed block
// reserves for it (kManagedPhysicsCallbackWords), which managed holds this to.
int SSFwd_PhysicsCallbacks_SizeOf();

// Each builds, in pMemory (SizeOf bytes, pointer-aligned: the block's reserved
// words), the object vphysics calls -- an IPhysicsObjectEvent, an
// IPhysicsCollisionEvent, and so on -- over its managed side, and answers it as
// the interface vphysics is handed. NULL, and nothing built, without memory or
// a target.
void* SSFwd_PhysicsCallbacks_ConstructObjectEvent(void* pMemory, IManagedPhysicsObjectEvent* pTarget);
void* SSFwd_PhysicsCallbacks_ConstructCollisionEvent(void* pMemory, IManagedPhysicsCollisionEvent* pTarget);
void* SSFwd_PhysicsCallbacks_ConstructCollisionSolver(void* pMemory, IManagedPhysicsCollisionSolver* pTarget);
void* SSFwd_PhysicsCallbacks_ConstructConstraintEvent(void* pMemory, IManagedPhysicsConstraintEvent* pTarget);
void* SSFwd_PhysicsCallbacks_ConstructMotionEvent(void* pMemory, IManagedMotionEvent* pTarget);
void* SSFwd_PhysicsCallbacks_ConstructKeyHandler(void* pMemory, IManagedVPhysicsKeyHandler* pTarget);
