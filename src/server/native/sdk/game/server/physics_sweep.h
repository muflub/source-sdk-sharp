//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — physics_main.cpp's one non-member, unscoped ========//
//
// Of the NINETEEN CBaseEntity members defined out of line in
// game/server/physics_main.cpp, exactly ONE takes no `this` of its own:
// PhysicsCheckSweep, which was already nothing but
//
//     ::PhysicsCheckSweep( this, vecAbsStart, vecAbsDelta, pTrace );
//
// forwarding to a FILE-STATIC free function of the same name that took the
// entity as an explicit argument. The class scope was being used as a namespace
// and the real function was already unscoped -- it just could not be reached
// from anywhere else, being static.
//
// Under plan_baseentity.md Phase 13's `using CBaseEntity = IGameEntity;` an
// alias is not a class scope, so `void CBaseEntity::PhysicsCheckSweep( ... )`
// stops being a definition and becomes a COMPILE error. That is what puts
// physics_main.cpp in step 3's removal set. So the static loses its `static`
// and gains a name that cannot collide, and the BODY does not move: it stays in
// physics_main.cpp beside Physics_TraceEntity and the pushed-entities machinery
// it belongs to. Only the SCOPE the definition is written in moves, exactly as
// it did for PhysicsLink_* and GameSound_*.
//
// THE CLASS-SCOPE NAME IS GONE. It spent one phase as a one-line forwarder
// quarantined in baseentity_sweep_forwarders.cpp; that file and the
// baseentity.h declaration have both been retired, because the name had no
// callers at all -- zero referring objects on the server link line and one
// call site in the source, physics_main.cpp's own, which calls
// PhysicsSweep_CheckEntity( this, ... ) directly. A new caller is therefore a
// compile error naming this function rather than a link error naming a member
// that no longer exists.
//
// THE OTHER EIGHTEEN ARE NOT LIFTS and are not here. They are the movement core
// -- PhysicsTryMove, PerformPush, PhysicsStep, StepSimulationThink and the rest
// -- and every one of them reads and writes the entity through its own `this`:
// origin, velocity, flags, ground entity, the step-simulation data object.
// PerformCustomPhysics is the one that looks liftable and is not: its whole
// body is `Assert(0)`, so it takes no `this`, but it is `virtual` on BOTH sides
// (baseentity.h:1804, c_baseentity.h:1507) and a virtual name IS a vtable slot.
// Lifting it would leave every override without a base implementation.
//
// SERVER-ONLY HEADER, and deliberately not part of game/shared/physicslink.h.
// The client's C_BaseEntity::PhysicsCheckSweep (physics_main_client.cpp:50) is
// a DIFFERENT function -- a real member with a real body that reads the
// entity's solidity and bounds through `this`, with no free counterpart and no
// GetMoveParent() early-out. The two sides only share a name, so giving them a
// shared declaration would be a lie about what the client does.
//
//=============================================================================//

#ifndef PHYSICS_SWEEP_H
#define PHYSICS_SWEEP_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"

class CGameTrace;
typedef CGameTrace trace_t;

// Guarded because `using CBaseEntity = IGameEntity;` and `class CBaseEntity;`
// cannot coexist: an unguarded forward declaration is a hard error the day step
// 4 arms the alias. alias_mechanics_test.cpp's NoForwardDeclarationSurvivesTheAlias
// enforces this on every header in the tree; takedamageinfo.h:21 is the
// canonical shape, and physicslink.h was caught by it for exactly this.
class CBaseEntity;

//-----------------------------------------------------------------------------
// Sweep pEntity's bounds from vecAbsStart along vecAbsDelta and report what it
// hit. Does not change the entity's velocity at all.
//
// pEntity is read -- PhysicsSolidMaskForEntity, IsSolid, IsSolidFlagSet,
// GetMoveParent -- but it is read through the ARGUMENT, which is what it always
// was: this body was already a file-static taking the entity explicitly, and
// the member it hung off of never did anything but hand `this` to it.
//-----------------------------------------------------------------------------
void PhysicsSweep_CheckEntity( CBaseEntity *pEntity, const Vector &vecAbsStart,
                               const Vector &vecAbsDelta, trace_t *pTrace );

#endif // PHYSICS_SWEEP_H
