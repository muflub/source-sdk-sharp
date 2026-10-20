//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — relays for SDK callers that have no shell ==========//
//
// The migration interfaces are gone; these are what their callers hold now.
//
// IEntityIo, IEntityDamageTarget and IEntityMapPlaceable existed so that game
// code compiled into the game DLL — the event queue, the bullet path, the map
// loader, point_template, ent_create — could ask a standalone entity for more
// than the engine boundary carries, without seeing the concrete class. That
// put three accessors on IGameEntity, and Phase 13's typedef cannot land while
// the boundary carries anything the engine does not reach.
//
// So the crossing moved HERE, behind functions named for what each caller
// means, following the precedent StandaloneFilterAnswer set: "down to the
// shell rather than through the interface — every IGameEntity in this build
// is one of these." The static_cast is stated once, in standalone_relays.cpp,
// which is shell code that owns the class layout; the callers hold an opaque
// IGameEntity * and a function name.
//
// EACH RELAY DIES WITH ITS CALLERS, exactly as the interfaces were dated to:
//
//   StandaloneEntity_Name          — name resolution for outputs and debug
//                                    names; dies with cbase.cpp's event queue
//                                    and the dual-hierarchy shims that print.
//   StandaloneEntity_TakeDamage,
//   StandaloneEntity_IsPlayer,
//   StandaloneEntity_PhysicsObject,
//   StandaloneEntity_OwnerHandle   — the bullet and blast paths; die with
//                                    baseentity_shared.cpp's FireBullets and
//                                    the sweeps in anyentity.h.
//   StandaloneEntity_ApplyMapKey,
//   StandaloneEntity_PlaceAndSpawn — map placement; die with mapentities.cpp's
//                                    parse loop, point_template.cpp and
//                                    ent_create.
//   StandaloneEntity_Velocity      — the movement loop asking how fast the
//                                    floor under a player is going; dies with
//                                    gamemovement.cpp's ground handling.
//
// When a caller file goes, its relay goes in the same commit — a relay nothing
// calls is the linker's to report, not a thing to keep warm. Nothing should be
// added here that intends to survive its caller.
//
// THE SERVER GC CULL (2026-10-01) took the ones whose callers had already gone:
// StandaloneEntity_DeliverInput, the five ShellLessEntity_* (the event queue's
// and ent_fire's questions about shell-less entities), StandaloneEntity_SpawnFlags,
// StandaloneEntity_SetOwner and StandaloneEntity_Remove. --print-gc-sections
// discarded each; the definitions went then and the declarations after.
//
//=============================================================================//
#ifndef SOURCESHARP_STANDALONE_RELAYS_H
#define SOURCESHARP_STANDALONE_RELAYS_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>
#include "mathlib/vector.h"

#include "igameentity_fwd.h"
#include "igameentity.h"
namespace ss { class CTakeDamageInfo; }	// ss_takedamageinfo.h
class IHandleEntity;
class IPhysicsObject;

//-----------------------------------------------------------------------------
// I/O — what cbase.cpp's event queue and the name-printing shims ask.
//-----------------------------------------------------------------------------

// The name a mapper gave it. Empty means unnamed, which is most entities and
// is not an error.
const char *StandaloneEntity_Name( const IGameEntity *pEnt );

//-----------------------------------------------------------------------------
// Damage — what FireBullets and the blast sweeps ask.
//-----------------------------------------------------------------------------

// Returns the damage actually taken, as CBaseEntity::TakeDamage does.
int StandaloneEntity_TakeDamage( IGameEntity *pEnt, const ss::CTakeDamageInfo &info );

// A game concept the damage rules branch on; the engine never asks.
bool StandaloneEntity_IsPlayer( const IGameEntity *pEnt );

// The MOVETYPE_* the solver's callbacks see for this entity, by
// standalone_movetype.h's precedence: a player's own; for anything holding a
// physics object the movetype managed wrote into its block (VPHYSICS for a
// moveable object, NONE beside a static one); NONE otherwise. Stock's collision record and ShouldCollide
// read CBaseEntity::GetMoveType() here, and VPhysicsCollision's response is gated
// on a side being MOVETYPE_VPHYSICS.
int StandaloneEntity_MoveType( const IGameEntity *pEnt );

// The solver's object, or NULL for something the solver does not move. What a
// blast weighs a blocker by: stock scales the damage a thing in the way absorbs
// by its mass, and mass is a property of the object rather than of the entity.
//
// Deliberately NOT on IGameEntity, and this is the one place that rule is worth
// restating: physicsowner.h says "only one hierarchy can be asked for an
// entity's object at all — IGameEntity deliberately has no VPhysicsGetObject",
// because the engine never asks an entity for its object. It asks the object
// for its entity, which is PhysicsStandaloneOwner. This is the other direction,
// wanted by game code only, so it is a relay and not a boundary method.
IPhysicsObject *StandaloneEntity_PhysicsObject( const IGameEntity *pEnt );

// Who owns it, as the handle a trace and a sweep compare against. Stock's blast
// rule asks this so that a thing owned by the victim does not shield the victim
// from a blast; a rocket must not be counted as cover for the player who fired
// it. Already a handle on this hierarchy — see CStandaloneEntity::SetOwnerHandle
// — where the SDK's GetOwnerEntity returns a CBaseEntity that has to be upcast.
IHandleEntity *StandaloneEntity_OwnerHandle( const IGameEntity *pEnt );

// ITS OWN HANDLE is NOT here. It is GameEntity_RefHandle, in
// sourcesharp/game/shared/game_entity_relays.h, because the callers that need
// it include files compiled into BOTH DLLs -- and the reasoning for the shape
// is written there.

//-----------------------------------------------------------------------------
// Map placement — what the map loader, point_template and ent_create ask.
//-----------------------------------------------------------------------------

// One parsed key. Returns whether anything wanted it, which is what lets a
// caller warn about a key nothing claimed.
bool StandaloneEntity_ApplyMapKey( IGameEntity *pEnt, const char *szKeyName,
                                   const char *szValue );

// Places and spawns in the one order the hierarchy allows: a standalone
// entity's Spawn is the whole of DispatchSpawn for it, so it must already be
// standing where it belongs. NULL leaves the mapper's value alone — the map
// loader passes NULL for both, ent_create passes a drop point and no angles,
// point_template passes both.
// PLACEMENT ALONE, without the Spawn. Split out of _PlaceAndSpawn below for
// EntityCreate_NoSpawn, whose whole contract is that it does not spawn. Stock
// spelled this SetLocalOrigin/SetLocalAngles; the shell's abs and local
// spellings are the same call for an entity that has no move parent, which a
// just-constructed one has not.
void StandaloneEntity_Place( IGameEntity *pEnt, const Vector *pOrigin,
                             const QAngle *pAngles );

void StandaloneEntity_PlaceAndSpawn( IGameEntity *pEnt, const Vector *pOrigin,
                                     const QAngle *pAngles );

//-----------------------------------------------------------------------------
// Movement — what the player movement loop asks of the floor.
//-----------------------------------------------------------------------------

// How fast it is actually going, in world space: the velocity MANAGED holds on
// the entity (CStandaloneEntity::MoveVelocity), read per trace and decided
// nowhere here (plan_physics_bones_perf C2, 2026-10-12). Managed's physics
// frame gives an entity its own object's velocity every step (stock's
// CBaseEntity::GetVelocity reads the object for a simulated entity), and an
// entity with no object holds its own member, which managed's
// EntityPhysicsMotion writes only then. This used to pick between the two
// homes itself (shell_velocity_home.h), a native decision; the header went.
Vector StandaloneEntity_Velocity( const IGameEntity *pEnt );

// The velocity managed's physics frame says the entity's own object moves at,
// held for StandaloneEntity_Velocity (plan_physics_bones_perf C2).
void StandaloneEntity_HoldMoveVelocity( IGameEntity *pEnt, const Vector &vecVelocity );

#endif // SOURCESHARP_STANDALONE_RELAYS_H
