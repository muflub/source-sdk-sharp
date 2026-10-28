//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — three of the shooting path's four, unscoped =========//
//
// sourcesharp/game/server/shell_ai_orphans.cpp defines FOUR CBaseEntity members
// that Valve left inside ai_basenpc.cpp and that came here when the AI left the
// build. Under plan_baseentity.md Phase 13's `using CBaseEntity = IGameEntity;`
// an alias is not a class scope, so a definition written `CBaseEntity::Foo(...)`
// is not a definition at all -- it is a COMPILE error. That is what puts
// shell_ai_orphans.cpp in Phase 13's step-3 removal set.
//
// THREE OF THE FOUR TAKE NO `this`, so the class scope was only ever being used
// as a namespace -- the same finding, and the same fix, as the seventeen sound
// statics lifted at dad2bc2e, the seven physics statics at 1c75302c, and
// baseentity_shared.cpp's five. All three are declared plain (non-virtual) at
// game/server/baseentity.h:1247, 1871 and 1886, and NONE of them is declared in
// game/client/c_baseentity.h at all -- they are server-only, so the
// ShouldDrawUnderwaterBulletBubbles trap (virtual on one side, plain on the
// other, liftable-looking if you read only one header) does not apply here.
// Both headers were checked; that is the point of saying so.
//
// THE FOURTH, HandleShotImpactingGlass, IS NOT LIFTED and stays a member. It
// reads `this` outright -- `info.m_pAttacker ? info.m_pAttacker : this` names
// the shooter as the attacker of the round refired behind the glass -- and it
// calls DoImpactEffect and FireBullets, both members, the first of them
// virtual. It is not a lift and step 3 has to answer for it separately.
//
// WHY THE BODIES STAY IN shell_ai_orphans.cpp. TraceAttackToTriggers drives
// CTriggerTraceEnum, a file-local enumerator class in that translation unit,
// and UpdateShotStatistics reads `ai_shot_stats`, a ConVar object that lives
// there too. Moving the bodies would mean exporting both, which is a bigger
// change than the lift and buys nothing. Only the SCOPE each definition is
// written in moves, exactly as it did for GameSound_* and PhysicsLink_*.
//
// WHY CreateBubbleTrailTracer IS HERE, AGAINST THE PLAN. plan_baseentity.md
// records it as NOT liftable because it calls ComputeTracerStartPosition and
// that is a member. That was true when it was written and is not true now:
// ComputeTracerStartPosition was itself lifted to the free
// EntityTracer_ComputeStartPosition( CBaseEntity *pEntity, ... ), declared at
// game/shared/entity_shared_statics.h:135 and defined at
// baseentity_shared.cpp, and the class-scope name is not defined in this build
// at all any more -- its quarantine has been retired. With the entity passed
// explicitly, the last thing CreateBubbleTrailTracer needed a `this` for is
// gone -- everything else it touches (VectorMA, sqrt, UTIL_BubbleTrail) is
// free. The plan's entry is stale, not wrong-headed.
//
// THE CLASS-SCOPE NAMES ARE GONE FROM THIS BUILD, and the quarantine that held
// them -- shell_ai_orphans_forwarders.cpp -- is retired. What made that
// possible was measuring the demand rather than assuming it. This build
// compiles only TWO objects that ever named them, baseentity_shared.o and
// gamerules.o, between them four call sites: FireBullets' three and
// anyentity.h's TraceAttackToTriggers wrapper. All four say EntityShot_* now.
//
// The long list of other callers this note used to carry -- tf_gamerules.cpp,
// sdk_gamerules.cpp, the melee weapons -- is real and untouched, and none of
// those files is in this build. They keep reaching CBaseEntity::
// TraceAttackToTriggers, which ai_basenpc.cpp still defines for the builds that
// compile it; ai_basenpc.cpp is one of server_csharp.vpc's 350 removals here,
// which is why the forwarder existed at all. The DECLARATIONS therefore stay in
// baseentity.h. Only the definitions left, which is what the removal set counts.
//
// Server-only header: shell_ai_orphans.cpp is in server_csharp.vpc and has no
// client counterpart.
//
//=============================================================================//

#pragma once
#define SHELL_SHOT_PATH_H

#include "mathlib/vector.h"

namespace ss { class CTakeDamageInfo; }	// ss_takedamageinfo.h
class CGameTrace;
typedef CGameTrace trace_t;

// Guarded because `using CBaseEntity = IGameEntity;` and `class CBaseEntity;`
// cannot coexist: an unguarded forward declaration is a hard error the day step
// 4 arms the alias. alias_mechanics_test.cpp's NoForwardDeclarationSurvivesTheAlias
// enforces this on every header in the tree; takedamageinfo.h:21 is the
// canonical shape.
#include "igameentity.h"

//-----------------------------------------------------------------------------
// Tell every trigger volume along a shot's path that it was shot through.
//
// Takes no entity of any kind. It builds a Ray_t from the two points it is
// handed and gives it to CTriggerTraceEnum, which reaches both ends of every
// hit through the enumerator's arguments -- the damage is dispatched to the
// entity the partition hands back, never to a subject. There was never a
// `this` here to remove.
//-----------------------------------------------------------------------------
void EntityShot_TraceAttackToTriggers( const ss::CTakeDamageInfo &info, const Vector &start,
                                       const Vector &end, const Vector &dir );

//-----------------------------------------------------------------------------
// The bubble trail a bullet leaves underwater.
//
// pEntity is the shooter, and is passed straight through to
// EntityTracer_ComputeStartPosition, which is the only thing in the body that
// ever wanted it. Under SOURCESHARP that function does not read it either --
// both arms that did, the player eye-vector offset and the weapon muzzle
// attachment, are behind `#ifndef SOURCESHARP` -- so NULL is accepted here for
// the same reason it is accepted there. It is taken explicitly rather than
// dropped so that those guarded arms keep saying what stock said.
//-----------------------------------------------------------------------------
void EntityShot_CreateBubbleTrail( IGameEntity *pEntity, const Vector &vecShotSrc,
                                   const Vector &vecShotEnd, const Vector &vecShotDir );

//-----------------------------------------------------------------------------
// NPC accuracy accounting.
//
// Under SOURCESHARP this counts nothing and reads neither argument: nothing in
// the build is an NPC and MyNPCPointer() is a constant NULL, which is why the
// body was already emptied where it is defined. pEntity is taken so that the
// `#else` arm -- the one an AI-bearing build still compiles -- keeps reaching
// MyNPCPointer() and TraceEntity() through the shooter instead of being deleted
// along with the `this` it used. Same shape, and same reason, as
// EntityTracer_ComputeStartPosition.
//-----------------------------------------------------------------------------
void EntityShot_UpdateStatistics( IGameEntity *pEntity, const trace_t &tr );
