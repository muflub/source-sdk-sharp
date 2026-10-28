//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — baseentity_shared.cpp's five non-members ===========//
//
// Of the EIGHTY-FIVE (server) / EIGHTY-SIX (client) CBaseEntity members defined
// out of line in game/shared/baseentity_shared.cpp, five take no `this` at all.
// Under plan_baseentity.md Phase 13's `using CBaseEntity = IGameEntity;` an
// alias is not a class scope, so a definition written `CBaseEntity::Foo(...)`
// is not a definition — it is a COMPILE error. That is what puts
// baseentity_shared.cpp in step 3's removal set, and it is why these five have
// to leave the class scope rather than merely lose their callers.
//
// FOUR ARE DECLARED `static`, declaration by declaration, in
// game/server/baseentity.h (1666-1671, 2071) and game/client/c_baseentity.h
// (640-645, 1249):
//
//     IsPrecacheAllowed, SetAllowPrecache, IsSimulatingOnAlternateTicks,
//     SetPredictionRandomSeed
//
// A static member takes no `this`; the class was supplying a namespace and
// nothing else. Same finding, same fix, as the seventeen sound statics at
// dad2bc2e and the seven physics statics at 1c75302c.
//
// THE FIFTH, ComputeTracerStartPosition, is NOT declared static
// (baseentity.h:1876, c_baseentity.h:220) and is not virtual on either side.
// It reads no member: every arm that ever did — `IsPlayer()`,
// `ToBasePlayer( this )`, `MyCombatCharacterPointer()` — is already behind
// `#ifndef SOURCESHARP`, and SOURCESHARP is defined for both link lines. It
// lifts on the same terms Physics_ClipVelocity did, and it takes the entity as
// an explicit argument so those guarded arms keep saying what stock said
// instead of being deleted along with the `this` they used.
//
// WHERE THE BODIES ARE. They were in baseentity_shared.cpp, because
// EntitySim_IsSimulatingOnAlternateTicks reads `sv_alternateticks`, a ConVar
// object that lived in that translation unit. Step 4e moved both, and the
// bodies with them, to sourcesharp/game/shared/entity_shared_statics.cpp:
// exporting that state is exactly what the step's cross-edge gate asks for,
// and the ConVar had no other reader. The four k_flMaxEntity* limits,
// CheckEntityVelocity and CheckEmitReasonablePhysicsSpew went along, for the
// same reason and in the same commit.
//
// THE PRECACHE FLAG IS DIFFERENT AND IS THE REASON THIS HEADER CARRIES STORAGE.
// `CBaseEntity::m_bAllowPrecache` was a static DATA member, defined at
// baseentity_shared.cpp:85. A static data member of an ALIAS does not fail to
// link — it stops PARSING, everywhere, at once, which is a strictly worse
// failure than the ones step 3 is tracking and the one this file's side of the
// removal set owns. So the storage moved here, header-only and behind an
// accessor, on entity_prediction_seed.h's shape and for its reasons: a .cpp
// would mean editing server_csharp.vpc and client_csharp.vpc, and a C++17
// `inline` variable with a constant initialiser is one object, constant-
// initialised — no guard variable, no thread-safe-init branch, so the read
// costs what the member read cost.
//
// ONE FLAG PER DLL, exactly as before: server.so and client.so each compile
// their own copy of this header, and each had its own `CBaseEntity::` /
// `C_BaseEntity::` object before. Nothing crosses.
//
// THE FIVE CLASS-SCOPE NAMES ARE GONE FROM THIS BUILD, and the quarantine that
// held them — game/shared/baseentity_shared_forwarders.cpp — is retired. What
// made that possible was measuring the demand rather than assuming it:
// rta_referrers.sh names EIGHT referring objects on the server link line and
// FIVE on the client, 21 call sites across 11 files, and every one of them says
// EntityPrecache_* / EntitySim_* / EntityPrediction_* / EntityTracer_* now.
//
// The wider SDK still reaches them qualified — basecombatweapon_shared.cpp,
// the tf/ and hl2/ entity files, vscript_server.cpp — and none of those files
// is in this build; server_csharp.vpc drops them. So the DECLARATIONS stay in
// baseentity.h and c_baseentity.h and only the definitions left, which is what
// the removal set counts.
//
// Shared header: both server.so and client.so compile against it.
//
//=============================================================================//

#pragma once
#define ENTITY_SHARED_STATICS_H

#include "mathlib/vector.h"

// The usercmd the prediction seeds are read from, named per side as
// imovementplayer.h names MvLocalData_t. Both arms are Valve's CUserCmd today;
// the server's becomes ss::CUserCmd with ss_usercmd.h (plan_server_novalve.md
// P5), and only that arm changes then.
#ifdef CLIENT_DLL
class CUserCmd;
typedef CUserCmd EntityPredictionCmd_t;
#else
// server.so's own command record (ss_usercmd.h, plan_server_novalve.md P5).
namespace ss { struct CUserCmd; }
typedef ss::CUserCmd EntityPredictionCmd_t;
#endif

// Guarded because `using CBaseEntity = IGameEntity;` and `class CBaseEntity;`
// cannot coexist: an unguarded forward declaration is a hard error the day step
// 4 arms the alias. alias_mechanics_test.cpp's NoForwardDeclarationSurvivesTheAlias
// enforces this on every header in the tree; takedamageinfo.h:21 is the
// canonical shape, and physicslink.h was caught by it for exactly this.
#include "igameentity.h"

//-----------------------------------------------------------------------------
// The precache gate.
//
// A process-wide bool that says whether precaching is legal right now. It was
// `static bool CBaseEntity::m_bAllowPrecache`, and it is not entity state by any
// reading: there are no per-entity copies and no entity ever consults its own.
// Callers bracket a block of precaching with it — read the old value, set true,
// precache, put the old value back — which is a scoped global, not a field.
//-----------------------------------------------------------------------------
inline bool s_bAllowPrecache = false;
inline bool &EntityPrecache_AllowedRef()
{
	return s_bAllowPrecache;
}

inline bool EntityPrecache_IsAllowed()
{
	return EntityPrecache_AllowedRef();
}

inline void EntityPrecache_SetAllowed( bool bAllow )
{
	EntityPrecache_AllowedRef() = bAllow;
}

//-----------------------------------------------------------------------------
// Whether the server is simulating entities on even-numbered ticks only.
// Reads gpGlobals->maxClients and the sv_alternateticks ConVar; no entity is
// involved, and the DEFINITION is in the shim TU named above, which is where
// the ConVar object lives now.
//-----------------------------------------------------------------------------
bool EntitySim_IsSimulatingOnAlternateTicks();

//-----------------------------------------------------------------------------
// Store the prediction seeds carried by one usercmd, or -1 for "not inside a
// usercmd" when handed NULL. The storage is entity_prediction_seed.h's; this
// is the arm that knows how to read a CUserCmd, which is why it is declared
// here and defined out of line rather than inline over there.
//-----------------------------------------------------------------------------
void EntityPrediction_SetSeedFromCommand( const EntityPredictionCmd_t *cmd );

//-----------------------------------------------------------------------------
// Where a tracer for a shot from vecShotSrc should be drawn from.
//
// IT TAKES NO SHOOTER, as of step 4e. It used to take one and not read it: the
// two arms that read it — the player eye-vector offset and the weapon muzzle
// attachment — are both compiled out of this build, so the only thing the
// parameter could do was oblige a caller to be holding a raw entity pointer.
// Step 4e's rule is that a utility the shim calls takes handles or nothing;
// this one needs nothing.
//-----------------------------------------------------------------------------
void EntityTracer_ComputeStartPosition( const Vector &vecShotSrc, Vector *pVecTracerStart );
