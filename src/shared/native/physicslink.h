//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the physics touch/ground link machinery, unscoped ===//
//
// Seven definitions in game/shared/physics_main_shared.cpp were written
// `CBaseEntity::Foo(...)` while taking no `this` at all. Under
// `using CBaseEntity = IGameEntity;` an alias is not a class scope, so every
// one of them stops being a definition and becomes a COMPILE error — which is
// what puts physics_main_shared.cpp in Phase 13's step-3 removal set. Six of
// the seven are already declared `static` in game/server/baseentity.h
// (1186-1195) and game/client/c_baseentity.h (947-956); the seventh,
// PhysicsClipVelocity, is not static but is pure arithmetic over its four
// arguments and reads no member either.
//
// They are the same finding, and the same fix, as the seventeen sound statics
// lifted at dad2bc2e: the class scope was only ever being used as a namespace.
// So the CODE lives here-as-declared / physics_main_shared.cpp-as-defined under
// a Physics* free-function name.
//
// THE CLASS-SCOPE NAMES ARE GONE FROM THIS BUILD. They spent one phase as
// one-line forwarders quarantined in game/shared/baseentity_physics_forwarders.cpp;
// that file is retired. rta_referrers.sh named the demand exactly -- SIX
// referring objects on the server link line and THREE on the client -- and all
// 22 call sites across eight files now name the free function. Two of the eight,
// PhysicsRemoveGround and GetTouchTrace, had no caller in this build at all.
//
// The DECLARATIONS stay in both headers, for the reason spelled out under
// Physics_GetTouchTrace below: files that are not compiled into either .so
// still reach those names through the class, and lifting is not deleting.
//
// WHY THE BODIES STAY IN physics_main_shared.cpp AND NOT HERE. All six link
// functions reach file-static state in that translation unit — the
// g_EdictTouchLinks / g_EntityGroundLinks memory pools, g_bCleanupDatObject,
// linksallocated, and the FreeTouchLink / FreeGroundLink helpers. Moving the
// bodies would mean exporting that state, which is a bigger change than the
// lift and buys nothing. Only the SCOPE each definition is written in moves,
// exactly as it did for GameSound_*.
//
// WHY THE FORWARDERS CANNOT SIMPLY BE DROPPED NOW. The SDK calls these names
// unqualified from inside its own CBaseEntity member functions — SetGroundEntity
// and WakeRestingObjects in this very file, UpdateOnRemove in baseentity.cpp —
// and every one of those call sites resolves through the class. The names have
// to stay until their callers are ported or evicted.
//
// Shared header: both server.so and client.so compile against it.
//
//=============================================================================//

#ifndef PHYSICSLINK_H
#define PHYSICSLINK_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"

struct touchlink_t;
struct groundlink_t;

// `trace_t` is a typedef, not a tag, so it cannot be forward-declared on its
// own — the pair below is how public/icliententity.h:21 and
// public/vphysics_interface.h:84 do it, and a repeated identical typedef is
// legal. Only a reference to it is named here, so the incomplete type is
// enough and gametrace.h does not have to be dragged in.
class CGameTrace;
typedef CGameTrace trace_t;

// Guarded because `using CBaseEntity = IGameEntity;` and `class CBaseEntity;`
// cannot coexist: the forward declaration is a hard error the day step 4 arms
// the alias. alias_mechanics_test.cpp's NoForwardDeclarationSurvivesTheAlias
// enforces this on every header in the tree, and caught this one.
#include "igameentity.h"

//-----------------------------------------------------------------------------
// Touch links.
//
// Each reaches its two entities only through the arguments it is handed.
// PhysicsLink_RemoveToucher calls otherEntity->EndTouch( link->entityTouched ),
// which is a virtual on the ARGUMENT, not on a subject — that is what makes it
// a lift rather than a member.
//-----------------------------------------------------------------------------
void PhysicsLink_RemoveTouchedList( IGameEntity *ent );
void PhysicsLink_NotifyOtherOfUntouch( IGameEntity *ent, IGameEntity *other );
void PhysicsLink_RemoveToucher( IGameEntity *other, touchlink_t *link );

//-----------------------------------------------------------------------------
// Ground links.
//-----------------------------------------------------------------------------
void PhysicsLink_NotifyOtherOfGroundRemoval( IGameEntity *ent, IGameEntity *other );
void PhysicsLink_RemoveGround( IGameEntity *other, groundlink_t *link );
void PhysicsLink_RemoveGroundList( IGameEntity *ent );

//-----------------------------------------------------------------------------
// Slide a velocity off an impacted plane. Returns the blocked flags
// (1 = floor, 2 = step / wall). Pure arithmetic over the four arguments.
//-----------------------------------------------------------------------------
int Physics_ClipVelocity( const Vector &in, const Vector &normal, Vector &out, float overbounce );

//-----------------------------------------------------------------------------
// The last touch trace. An eighth lift out of physics_main_shared.cpp, and the
// only one this lane found in 120 members.
//
// It was ALREADY `static` in both headers — baseentity.h:1181,
// c_baseentity.h:964 — which is the whole finding: the class scope was being
// used as a namespace and nothing else. The storage it hands back,
// `g_TouchTrace`, is a file-static of physics_main_shared.cpp written by
// PhysicsMarkEntitiesAsTouching and PhysicsMarkEntitiesAsTouchingEventDriven,
// so the BODY stays there and only the scope the definition is written in
// moves — the same shape as the six PhysicsLink_* above.
//
// IT IS ALSO DEAD IN THIS BUILD, and that is recorded rather than acted on.
// Zero undefined references to `CBaseEntity::GetTouchTrace()` across all 200
// server and all 328 client objects, and one textual use in its own
// translation unit: the definition. Every caller the tree has —
// basegrenade_shared.cpp:379, hl2mp/weapon_crossbow.cpp:209 and :274,
// episodic/npc_hunter.cpp:672, and twenty-one files under game/shared/tf/ —
// is in a file that is not compiled into either .so. So it does NOT meet the
// deletion standard the three retirements in baseentity_shared.cpp set, which
// is "zero source references anywhere under src/ outside this definition and
// the two declarations that went with it". It is lifted, not deleted, and the
// class-scope DECLARATION stays for the day one of those files comes back --
// but its out-of-line DEFINITION went with the quarantine, so a file that does
// come back gets a link error naming the member rather than silently reaching a
// second implementation.
//
// (The twenty-four `&CBaseEntity::GetTouchTrace()` sites in those files are
// NOT pointers-to-member. The trailing `()` calls it and the `&` takes the
// address of the returned reference. Read as pointers-to-member they would
// have blocked this lift outright.)
//-----------------------------------------------------------------------------
const trace_t &Physics_GetTouchTrace( void );

#endif // PHYSICSLINK_H
