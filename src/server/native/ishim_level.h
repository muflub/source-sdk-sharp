//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — a level's native steps, as an interface ============//
//
// plan_native_api.md Part 4, wave 2. Twenty-two flat ABI fields become
// twenty-two slots of one vtable, on the recipe IShimFrame proved.
//
// THE SAME RULE THE FRAME'S HEADER CARRIES AND FOR THE SAME REASON: APPEND
// ONLY. New methods at the END, nothing inserted, nothing reordered, nothing
// removed -- a retired method keeps its slot as a stub until a deliberate
// ...002 version string. The span check is `managed <= native`, which protects
// against APPENDS and nothing else; an insert or a reorder in the middle passes
// every count there is and silently misbinds. What enforces it is the committed
// generated layout: end-only growth is the only diff shape the rule permits.
//
// NO VIRTUAL DESTRUCTOR. One would take the first TWO slots under the Itanium
// ABI and move everything below it, and the object behind this is a singleton
// that lives as long as the DLL and is never deleted through a base pointer.
//
//-----------------------------------------------------------------------------
// WHY THIS AREA IS SECOND, and it is the plan's own choice rather than a
// convenience: twenty-two fields, all server-only, and every one of them runs
// on EVERY MAP LOAD. `make live-gate` loads dm_lockdown twice, so both the
// init half and the shutdown half of this interface are exercised, in order,
// four times over -- and the reload is where a level-scoped mistake lives.
//
//-----------------------------------------------------------------------------
// A STEP IS NEVER A DECISION -- plan_server.md Part 4, unchanged by the move.
// There is no LevelInit() and no LevelShutdown() member here: the ORDER, the
// conditions and the early returns are in C#, and each method below is one
// line of what stock does. That is why there are twenty-two of them.
//
//=============================================================================//

#ifndef ISHIM_LEVEL_H
#define ISHIM_LEVEL_H
#ifdef _WIN32
#pragma once
#endif

#include "tier0/platform.h"

#define SOURCESHARP_SHIM_LEVEL_VERSION "SourceSharpLevel001"

abstract_class IShimLevel
{
public:
	//-------------------------------------------------------------------------
	// LEVELINIT'S STEPS, in the fill's order.
	//-------------------------------------------------------------------------

	// SS_RETIRED ResetServerTime lane=t6-globals g_flServerCurTime had no reader (baseprojectile.cpp is in neither DLL); managed no longer calls it
	// Was g_flServerCurTime = gpGlobals->curtime at the new map. Empty; slot
	// kept at its index (slot 0) until the table's next --reorder pass.
	virtual void ResetServerTime() = 0;

	// The particle manifest, and then the map's own. TWO steps because they are
	// two calls doing two things -- stock's are two lines and a step that ran
	// both would be a decision about their order.
	virtual void ParseParticleManifest() = 0;
	virtual void ParseParticleManifestForMap( const char *pMapName ) = 0;

	// RETIRED (plan_server_novalve.md P5): the map's entity residue as text,
	// parsed here. The world now arrives parsed through SpawnWorld at the tail;
	// this slot keeps its index and refuses out loud.
	virtual void ParseAllEntities( const char *pMapEntities ) = 0;

	// The point_templates' contents, precached.
	//
	// THE BODY IS EMPTY AND THE SLOT IS NOT RETIRED, which is a distinction
	// this interface has to be able to make. game/server/TemplateEntities.cpp
	// left the vpc and a managed template entity is precached by the managed
	// spawner, so there is no work here -- but the CALL is a sequencing point
	// and its position is load-bearing twice over: stock sets low violence
	// (gameinterface.cpp:1049) BEFORE precaching templates (:1053), and the
	// templates are precached AFTER the entities they read. The first of those
	// was inverted in this port from the day the method was written, under a
	// [Fact] that pinned the inversion, and tools/gate5 found it on its first
	// real run -- no census, no live gate and no managed test could see it.
	//
	// A retired slot is not called, so retiring this one would delete the only
	// marker either ordering can be observed through. ONE no-op crossing per
	// LEVEL is the price of that, and per-level crossings are not what the
	// crossing budget is about.
	virtual void PrecachePointTemplates( const char *pMapDataBlobs,
	                                     const int32_t *pStringSizes,
	                                     int32_t nCount ) = 0;

	// g_RagdollLVManager.SetLowViolence( pMapName ).
	virtual void SetLowViolence( const char *pMapName ) = 0;

	virtual void LoadMessageOfTheDay() = 0;

	//-------------------------------------------------------------------------
	// SERVERACTIVATE'S AND LEVELSHUTDOWN'S STEPS.
	//-------------------------------------------------------------------------

	// SS_RETIRED ResetDeleteList lane=3b-enginebind armed there is no SDK delete list, so the count is a constant 0 and asking costs a crossing per level
	// How many entities the delete list held when it was reset. Armed the
	// answer cannot be anything but 0; the parallel hierarchy's own count is
	// EntityDirectory.PendingRemovalCount, which is managed and free to read.
	virtual int32_t ResetDeleteList() = 0;

	// SS_RETIRED ActivateAllEntities lane=3b-enginebind every line of stock's walk reaches the hierarchy server_csharp.vpc evicts; _spawner.ActivateAll() is the whole pass
	virtual void ActivateAllEntities() = 0;

	virtual void SetPrecacheAllowed( int32_t bAllowed ) = 0;

	virtual void ClearSteamAPIContext() = 0;

	// The parallel hierarchy's teardown, before the SDK entity list is cleared.
	virtual void ShutdownStandaloneEntities() = 0;

	virtual void ShutdownSoundEnt() = 0;
	virtual void ClearEntityList() = 0;

	// Uncache, then rebuild. Stock's reason for the first, in its own words:
	// otherwise every particle system leaks into the particle precache string
	// table on every change of level.
	virtual void UncacheAllParticleSystems() = 0;
	virtual void RecreateParticleDictionary() = 0;

	//-------------------------------------------------------------------------
	// CPropData'S TWO REMAINING NATIVE STEPS. On this interface rather than an
	// IShimPropData of their own because they are LEVEL steps: the load is
	// LevelInitPreEntity's and the clear is LevelShutdownPostEntity's, and an
	// area with two members whose whole lifecycle is another area's is a
	// taxonomy nobody asked for.
	//-------------------------------------------------------------------------
	virtual void PropDataLoadFile() = 0;
	virtual void PropDataClearFile() = 0;

	//-------------------------------------------------------------------------
	// THE SAVEGAME ARM'S ONE STEP -- docs/managed-saverestore.md stage 2.
	//
	// `engine->LoadGameState` is IVEngineServer's (public/eiface.h:280) and the
	// GAME DLL is what calls it: stock's CServerGameDLL::LevelInit does so from
	// its loadGame arm, and NOTHING ELSE IN THE ENGINE DOES. Stage 1 is the
	// measurement: with no LoadGameState call from the game, the engine never
	// started a load-game read on its own -- and that is
	// why stage 1's ReadRestoreHeaders and Restore never fired: not because the
	// engine had nothing to hand them, but because this build never asked.
	//
	// It is the call that drives ReadRestoreHeaders, the 'Save Header' and
	// LIGHTSTYLE reads, and Restore, in that order.
	virtual int32_t LoadSaveGameState( const char *pMapName, int32_t bCreatePlayers ) = 0;

	//-------------------------------------------------------------------------
	// plan_server_novalve.md P2.1: WHOLE-FILE READS ON A NAMED SEARCH PATH, the
	// two a managed parser needs and IBaseFileSystem cannot give it without a
	// handle. "GAME" and "BSP" are the two the particle manifests use (the map's
	// own manifest can live inside the BSP's pak lump). Bytes, not text: a .pcf
	// is binary DMX.
	//
	// FileSizeOnPath answers -1 for a file that is not there (0 is a real,
	// empty file). ReadFileOnPath answers the bytes read, or -1 for a file that
	// is not there or does not fit nOutSize -- refused, never truncated.
	//-------------------------------------------------------------------------
	virtual int32_t FileSizeOnPath( const char *pPath, const char *pPathId ) = 0;
	virtual int32_t ReadFileOnPath( const char *pPath, const char *pPathId,
	                                void *pOut, int32_t nOutSize ) = 0;

	//-------------------------------------------------------------------------
	// plan_server_novalve.md P5: THE WORLD, ARRIVING PARSED. The map's
	// worldspawn block as its key/value pairs, in block order -- managed parses
	// the lump (MapEntityBlock) and picks the block; native only builds what it
	// is told to. Creates the world through CWorldFactory, hands each pair to
	// CWorld::KeyValue and spawns it, before anything else in the level exists.
	//
	// Answers 1 when the world was created and spawned, 0 when the factory gave
	// nothing or the object did not claim the world singleton. Replaces
	// ParseAllEntities, which took the residue as text and parsed it here; that
	// slot stays where it is (append-only) and refuses.
	//-------------------------------------------------------------------------
	virtual int32_t SpawnWorld( const char *const *ppKeys, const char *const *ppValues,
	                            int32_t nKeys ) = 0;

	//-------------------------------------------------------------------------
	// plan_server_native_v2 T4a, the precache path: WHETHER A PRECACHE IS IN
	// ORDER NOW -- the flag SetPrecacheAllowed above writes and the world's
	// own precache opens (EntityPrecache_IsAllowed). 1 or 0. The managed
	// SoundEmitterSystem asks it before stock's "Late precache of" warning;
	// the flag itself stays native, where the world's precache sets it.
	//-------------------------------------------------------------------------
	virtual int32_t IsPrecacheAllowed() = 0;

	//-------------------------------------------------------------------------
	// plan_server_native_v2 T4e: gpGlobals->eLoadType = eLoadType, the first
	// line of each LevelInit arm. Which arm is managed's (MapLoadArms); this
	// writes the MapLoadType_t it chose.
	//-------------------------------------------------------------------------
	virtual void SetLoadType( int32_t eLoadType ) = 0;

	//-------------------------------------------------------------------------
	// plan_server_native_v2 T4e: THE NATIVE SYSTEMS' STAGES, one step each.
	// kServerSystems[] and its walks are gone: the ORDER -- the string pool
	// first in and last out, physics before the managed row on the way in and
	// after it on the way out -- is managed's (ServerSystems.cs), and each
	// line below is one stage of one native system, carried out.
	//-------------------------------------------------------------------------
	virtual int32_t PhysicsInit() = 0;
	virtual void PhysicsLevelInitPreEntity() = 0;
	virtual void PhysicsLevelInitPostEntity() = 0;
	virtual void PhysicsLevelShutdownPreEntity() = 0;
	virtual void PhysicsLevelShutdownPostEntity() = 0;
	virtual void StringPoolLevelShutdownPostEntity() = 0;

	// Every entity control-block slot closed, after the managed level's end:
	// what the managed row's native half did after IManagedFrame::LevelShutdown.
	virtual void ResetEntitySlots() = 0;

	// NEW METHODS GO HERE, AT THE END.
};

#endif // ISHIM_LEVEL_H
