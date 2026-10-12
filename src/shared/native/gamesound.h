//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the sound entry points that never took a `this` =====//
//
// Seventeen of the twenty-six CBaseEntity members defined in
// game/shared/SoundEmitterSystem.cpp are declared `static`
// (baseentity.h:1615-1642 and 2022-2023; c_baseentity.h:609-634 and 881-882).
// A static member takes no `this`. The class was supplying a namespace and
// nothing else, and plan_baseentity.md Phase 13 deletes that namespace: under
// `using CBaseEntity = IGameEntity;` the text `CBaseEntity::EmitSound(...)` at
// the head of a definition stops being a definition scope at all. That is a
// COMPILE error, not an undefined symbol, so no amount of caller work reaches
// it -- the definition has to leave the class or die with it.
//
// These are the lifts. Same move as CBaseEntity::PrecacheModel ->
// Precache_Model (game/server/precache_model.h), and the same reasoning that
// landed for CBaseEntity::LookupSoundLevel's callers in commit a1e3d479.
//
// WHAT DID *NOT* COME TRUE OF ALL SEVENTEEN, said out loud because the phrase
// "reads no entity" is in the plan and is wrong for four of them:
//
//   * GameSound_Emit( filter, iEntIndex, params[, handle] ) looks the entity up
//     by index and calls ModifyEmitSoundParams on it -- a VIRTUAL, with real
//     per-class overrides. It reads an entity.
//   * GameSound_EmitCloseCaption walks the owner chain from the entity at
//     `entindex` looking for a player. It reads entities.
//   * GameSound_Precache asks CBaseEntity::IsPrecacheAllowed(), which is
//     another static, defined in baseentity_shared.o.
//
// They are still lifts: what makes a definition impossible after Phase 13 is
// the definition SCOPE, not the body. Lifting them means their bodies survive
// step 3 in a file that is kept.
//
// THE CLASS-SCOPE NAMES ARE GONE FROM THIS BUILD. They spent one phase as
// one-line forwarders quarantined together in
// game/shared/baseentity_sound_forwarders.cpp; that file is retired.
// rta_referrers.sh named the demand exactly -- FOURTEEN referring objects on
// the server link line and NINETEEN on the client -- and all 78 call sites
// across 31 files now name GameSound_* directly. Five of the seventeen
// (LookupSoundLevel x2, PrefetchScriptSound, PrefetchSound, EmitAmbientSound)
// had no caller in this build at all.
//
// The DECLARATIONS stay in baseentity.h and c_baseentity.h, and so do the NINE
// non-static sound members in SoundEmitterSystem.cpp: those take a `this` and
// were never lifts. The SDK reaches the seventeen from hundreds of sites in
// files server_csharp.vpc does not compile, which is why only the DEFINITIONS
// left -- and the definitions are what the removal set counts.
//
// The bodies live in game/shared/SoundEmitterSystem.cpp next to
// g_SoundEmitterSystem, UTIL_TranslateSoundName and g_bPermitDirectSoundPrecache,
// which is where they already were. Nothing moved between translation units;
// what moved is which SCOPE the definition is written in.
//
// Engine-facing, and does not pretend otherwise: these talk to enginesound,
// soundemitterbase and the sound emitter game system directly.
//
//=============================================================================//
#ifndef GAMESOUND_H
#define GAMESOUND_H
#ifdef _WIN32
#pragma once
#endif

#include "tier1/utlvector.h"
#include "mathlib/vector.h"
#include "soundflags.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"

class IRecipientFilter;

// The parameter block of the two GameSound_Emit overloads below. On the server
// it is server.so's own ss::EmitSound_t (ss_emitsound.h, plan_server_novalve.md
// P5); the client keeps shareddefs.h's EmitSound_t. One name for both, so the
// shared code that fills one is written once.
#ifdef CLIENT_DLL
struct EmitSound_t;
typedef EmitSound_t GameSoundParams_t;
#else
#include "ss_emitsound.h"
typedef ss::EmitSound_t GameSoundParams_t;
#endif

//-----------------------------------------------------------------------------
// Emit a game sound to a filter's recipients, on behalf of entity `iEntIndex`.
//
// The entity is looked up by index only so that its ModifyEmitSoundParams
// override gets a say in the parameters; a -1 index simply gets no override.
//-----------------------------------------------------------------------------
void GameSound_Emit( IRecipientFilter& filter, int iEntIndex, const char *soundname,
                     const Vector *pOrigin = NULL, float soundtime = 0.0f, float *duration = NULL );
void GameSound_Emit( IRecipientFilter& filter, int iEntIndex, const char *soundname,
                     HSOUNDSCRIPTHANDLE& handle, const Vector *pOrigin = NULL,
                     float soundtime = 0.0f, float *duration = NULL );
void GameSound_Emit( IRecipientFilter& filter, int iEntIndex, const GameSoundParams_t & params );
void GameSound_Emit( IRecipientFilter& filter, int iEntIndex, const GameSoundParams_t & params,
                     HSOUNDSCRIPTHANDLE& handle );

//-----------------------------------------------------------------------------
// Stop a sound playing on an entity index -- by script name, or by channel and
// wave name for a sound the caller started directly.
//-----------------------------------------------------------------------------
void GameSound_Stop( int iEntIndex, const char *soundname );
void GameSound_Stop( int iEntIndex, int iChannel, const char *pSample );

//-----------------------------------------------------------------------------
// The same stop, for a caller that already has the soundscript handle.
//
// NOT ONE OF THE SEVENTEEN. It is step 4b's own lift: the emitter system object
// is a file-static of sound_emitter.cpp, so the one member that reached
// StopSoundByHandle directly needed a free function once it moved out of that
// translation unit. Same move, same reason, one phase later.
//-----------------------------------------------------------------------------
void GameSound_StopByHandle( int iEntIndex, const char *soundname, HSOUNDSCRIPTHANDLE& handle );

//-----------------------------------------------------------------------------
// The soundscript's attenuation for a sound, by name or by an already-resolved
// handle. A pure question for the soundscript system.
//-----------------------------------------------------------------------------
soundlevel_t GameSound_LookupSoundLevel( const char *soundname );
soundlevel_t GameSound_LookupSoundLevel( const char *soundname, HSOUNDSCRIPTHANDLE& handle );

//-----------------------------------------------------------------------------
// The soundscript entry for a sound, resolved for the gender of `actormodel`.
//-----------------------------------------------------------------------------
bool GameSound_GetParameters( const char *soundname, CSoundParameters &params,
                              const char *actormodel );
bool GameSound_GetParameters( const char *soundname, HSOUNDSCRIPTHANDLE& handle,
                              CSoundParameters &params, const char *actormodel );

//-----------------------------------------------------------------------------
// An ambient sound at a world position, owned by an entity index.
//-----------------------------------------------------------------------------
void GameSound_EmitAmbient( int entindex, const Vector& origin, const char *soundname,
                            int flags = 0, float soundtime = 0.0f, float *duration = NULL );

//-----------------------------------------------------------------------------
// Show a close caption token to a filter's recipients.
//
// Reads entities: it walks the owner chain from `entindex` so that a caption
// coming from the local player can be styled as such.
//-----------------------------------------------------------------------------
void GameSound_EmitCloseCaption( IRecipientFilter& filter, int entindex, char const *token,
                                 CUtlVector< Vector >& soundorigins, float duration,
                                 bool warnifmissing = false );

//-----------------------------------------------------------------------------
// How long the wave behind a soundscript entry runs, in seconds.
//-----------------------------------------------------------------------------
float GameSound_Duration( const char *soundname, char const *actormodel );

//-----------------------------------------------------------------------------
// Precaching and prefetching. The *Script forms go through the soundscript
// system and are what map logic should use; the bare forms hand a wave name
// straight to the engine and warn when they are used outside precaching.
//-----------------------------------------------------------------------------
HSOUNDSCRIPTHANDLE GameSound_PrecacheScript( const char *soundname );
void GameSound_PrefetchScript( const char *soundname );
bool GameSound_Precache( const char *name );
void GameSound_Prefetch( const char *name );

#endif // GAMESOUND_H
