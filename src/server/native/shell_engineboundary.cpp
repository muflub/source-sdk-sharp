//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the engine boundary, defined and acquired ==========//
// SS_REASON b  an interface resolved by name at load
//
// plan_shim.md §5 3a. TWO TRANSLATION UNITS BECAME ONE: shell_engineglobals.cpp
// DEFINED every global the engine hands this DLL, and shell_engineinterfaces.cpp
// ACQUIRED them from the factories. One file held the names and the other held
// the only thing that ever writes them, and a reader asking "what is `modelinfo`
// and who set it" had to open both.
//
// THE DEFINITIONS AND THE ACQUISITION ARE NOT TWO SUBJECTS. Every name defined
// in the first half is assigned in the second and nowhere else, which is the
// property that makes the pair safe to read as one thing and would make either
// half alone misleading: a definition list with no writer reads as state, and an
// acquisition with its definitions elsewhere reads as a cache.
//
//=============================================================================
// WHY THESE ARE ALLOWED TO BE GLOBALS AT ALL, when the standing owner rule for
// src/sourcesharp is "no file-scope statics or globals; state goes in a class as
// fields". It is an IMPOSSIBILITY rather than a preference:
//
//   * `engine`, `gpGlobals`, `filesystem` and the rest are the SDK's own
//     spelling, named by well over a thousand call sites across game code that
//     this port does not own and is not rewriting. They are the boundary, not
//     state behind it;
//   * `g_pSharedChangeInfo` is a pointer the ENGINE follows, handed over at
//     DLLInit;
//   * `CBaseEdict::GetChangeAccessor` is a member of a class declared in
//     public/edict.h, so it cannot live anywhere but a .cpp that includes it,
//     and both its bodies are one call on `engine`.
//
// tools/statics-audit gates `static`, and none of these is one -- they are
// non-static file-scope definitions with external linkage, which is the only
// shape that can answer an `extern` in somebody else's header. The two Steam
// contexts DID have to change shape: stock wrote them `static`, and they are in
// an anonymous namespace here, which is the arrangement shell_gamedll.cpp
// already uses for its singleton.
//
//=============================================================================
// THE ACQUISITION WAS NOT IN THE PLAN AND HAD TO BE. plan_gamedll.md gives
// DLLInit's contents to Part 9 ("DLLInit's eight TODOs"), and all eight of those
// are GAME SYSTEMS -- CPhysicsHook, the particle system manager, GameStringSystem
// and so on -- which Phase 7 wires through kServerSystems[]. None of the eight is
// an engine interface, and nothing else in the plan assigned them either.
// Measured rather than assumed: before that half existed the shell's DLLInit
// acquired exactly two, `engine` and `cvar`, and every other one of the twenty
// was still NULL with -shellgamedll.
//
// That is invisible until a map loads, which is why it surfaced in Phase 6 and
// not in Phase 4: `networkstringtable` is NULL, so Part 8's very first line --
// networkstringtable->CreateStringTable -- is a null dereference; `filesystem`
// is NULL, so the particle manifests and the MOTD cannot be read; `modelinfo`,
// `partition`, `enginetrace` and `soundemitterbase` are NULL, so nothing that
// spawns can precache a model, link into the partition, trace, or emit.
//
// TRANSLITERATED from CServerGameDLL::DLLInit (gameinterface.cpp:659-732), in
// stock's order, with stock's "return false on the first miss". The console
// arms are dropped (a compile-time false on PC) and REPLAY is not defined here.
//
// NOTHING WAS RETYPED, RENAMED OR REORDERED IN EITHER HALF. Both are verbatim
// carries, with the declarations that only stock's CServerGameDLL used left
// behind.
//
//===========================================================================//

// P5 lane R: on the R5 include set -- public/ headers and server.so's own.
// cbase.h, gameinterface.h, mapentities.h, networkstringtable_gamedll.h,
// sharedInterface.h and enginecallback.h are gone; what this file used from
// them is the interfaces' own public/ headers and the declarations below.
#include "networkstringtabledefs.h"
#include "engine/IEngineTrace.h"
#include "filesystem.h"
#include "ivoiceserver.h"
#include "ispatialpartition.h"
#include "engine/IEngineSound.h"
#include "engine/IStaticPropMgr.h"
#include "engine/ivdebugoverlay.h"
#include "datacache/idatacache.h"
#include "igameevents.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "scenefilecache/ISceneFileCache.h"

// Only the acquisition half needed these; the definitions above
// compile without them.
#include "eiface.h"
#include "ss_init_factory.h"
#include "vstdlib/random.h"
#include "tier0/icommandline.h"
#include "datacache/imdlcache.h"
#include "shell_server.h"


//=============================================================================//
// 1. THE DEFINITIONS -- was shell_engineglobals.cpp
//=============================================================================//

//-----------------------------------------------------------------------------
// THE TWO STEAM CONTEXTS WERE DEFINED HERE -- plan_server_novalve.md P2.5.
// steamapicontext had no server reader but GetSteamUtils (client-only now), and
// steamgameserverapicontext's one user was the game server context, which is
// C#'s own (SteamGameServerFlat). server.so includes no steam/ header.
//-----------------------------------------------------------------------------

IUploadGameStats *gamestatsuploader = NULL;

// SOURCESHARP: `CTimedEventMgr g_NetworkPropertyEventMgr;` was here.
// plan_shim.md Phase 1d deleted it with game/server/timedeventmgr.cpp: the
// only thing that could put an entity into its heap was
// CServerNetworkProperty::SetUpdateInterval, which had no caller in any
// compiled translation unit, so the manager was an empty heap walked once per
// frame forever. IShimFrame::FireNetworkPropertyEvents, the step that walked
// it, left in the serialised ABI pass (2026-10-20).

// g_MapEntityRefs is gone: its only writer was CMapLoadEntityFilter
// (gameinterface.h), which no compiled TU uses, and nothing read it; the one
// other reference was shell_level.cpp's Purge.

//-----------------------------------------------------------------------------
// Engine interfaces. Every one is acquired in
// sourcesharp/game/server/shell_engineinterfaces.cpp, which transliterates
// stock's DLLInit acquisition list and returns false on the first miss.
//-----------------------------------------------------------------------------
IVEngineServer	*engine = NULL;
#if !defined(_STATIC_LINKED)
IFileSystem		*filesystem = NULL;
#else
extern IFileSystem *filesystem;
#endif
INetworkStringTableContainer *networkstringtable = NULL;
IStaticPropMgrServer *staticpropmgr = NULL;
// sharedInterface.h's `random`. Under POSIX that header renames it with
// `#define random random_valve` (stdlib.h's random() would collide), so its
// link name is random_valve, and that is the name defined here.
IUniformRandomStream *random_valve = NULL;
ISpatialPartition *partition = NULL;
IVModelInfo *modelinfo = NULL;
IEngineTrace *enginetrace = NULL;
IGameEventManager2 *gameeventmanager = NULL;
IVDebugOverlay * debugoverlay = NULL;
// serverenginetools, the console system and matchmaking are gone: nothing assigned or
// read them (the globals ledger ruled all three DEAD, dropped by the linker).
// g_pVoiceServer, enginesound, datacache, soundemitterbase, scenefilecache and
// sv_cheats went as globals too (plan_server_native_v2 T6): nothing on the
// server read them. Of the interfaces, only the sound emitter is still asked
// for (its Connect below); the voice server, the engine's sound, the data
// cache and the scene file cache are not acquired at all (2026-10-25), since
// nothing here calls them -- managed acquires the engine sound it calls itself.

//-----------------------------------------------------------------------------
// WHAT USED TO STAND HERE, AND WHY THE SPACE IS EMPTY -- plan_shim.md step 5c.
//
// Three globals and the two replay headers that declared their types:
//
//     #if defined( REPLAY_ENABLED )
//     IReplaySystem        *g_pReplay              = NULL;
//     IServerReplayContext *g_pReplayServerContext = NULL;
//     #endif
//     ConVar *sv_maxreplay = NULL;
//
// HLTV AND REPLAY ARE DROPPED FROM THIS MOD. That is a decision, not an
// accident of the build, and these three are the first thing it deletes.
//
// The first two never existed in any binary this tree produces: REPLAY_ENABLED
// is defined only by src/vpc_scripts/source_replay_private.vpc, which is
// included only from source_replay.vpc, which NEITHER project script includes.
// Measured rather than assumed -- `grep -c REPLAY_ENABLED` over BOTH generated
// graphs (src/_vpc_/ninja/sdk_csharp_debug.ninja and ..._release.ninja) is 0.
//
// sv_maxreplay did exist, as real storage in this object, and its comment said
// it was kept because game/server/player.cpp dereferences it after a NULL test.
// THAT READER IS NOT ON EITHER LINK LINE: player.cpp is not compiled here, and
// the census over the ninja graph's objects reports 0 importers on both sides
// (server 0/108, client 0/253, 0 ghosts). It was storage for a reader that had
// already gone.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// String tables. Created by SourceSharp_CreateNetworkStringTables in
// sourcesharp/game/server/shell_stringtables.cpp, which is also where the six
// accessors that read them live (Phase A4).
//-----------------------------------------------------------------------------
INetworkStringTable *g_pStringTableParticleEffectNames = NULL;
INetworkStringTable *g_pStringTableEffectDispatch = NULL;
INetworkStringTable *g_pStringTableVguiScreen = NULL;
INetworkStringTable *g_pStringTableMaterials = NULL;
INetworkStringTable *g_pStringTableInfoPanel = NULL;
INetworkStringTable *g_pStringTableClientSideChoreoScenes = NULL;
INetworkStringTable *g_pStringTableServerMapCycle = NULL;

#ifdef TF_DLL
INetworkStringTable *g_pStringTableServerPopFiles = NULL;
INetworkStringTable *g_pStringTableServerMapCycleMvM = NULL;
#endif

// g_VguiScreenStringOps is gone: its one reader was vguiscreen.cpp's datadesc,
// which server.so does not compile; the only reference left was its own Init.

// Holds global variables shared between engine and game.
CGlobalVars *gpGlobals;

CSharedEdictChangeInfo *g_pSharedChangeInfo = NULL;

//-----------------------------------------------------------------------------
// CBaseEdict is declared in public/edict.h and these two are its only
// out-of-line members on the server. Nineteen objects in the compiled set call
// them.
//-----------------------------------------------------------------------------
IChangeInfoAccessor *CBaseEdict::GetChangeAccessor()
{
	return engine->GetChangeAccessor( (const edict_t *)this );
}

//-----------------------------------------------------------------------------
// The ConVars that outlived stock's CServerGameDLL because their READERS are
// elsewhere. Each is named with its reader, so a later pass can tell them from
// the twelve A9 step 3 deleted for having none.
//
// TWO OF THE THREE ARE DECLARED IN MANAGED CODE NOW -- see §6's ConVar rule and
// SourceSharp.Server/ConVars.cs. Their readers are named there instead, and
// neither reader is compiled into THIS build, so nothing here holds a ref:
//
//   ai_post_frame_navigation   game/server/ai_basenpc.cpp (three reads) and
//                              ai_navigator.cpp; declared extern in
//                              ai_basenpc.h:3250. Neither .cpp is in this vpc.
//   sv_unlockedchapters        game/server/EnvMessage.cpp sets it to "15" when
//                              a chapter is unlocked and GameStats.cpp reads
//                              the name out of config.cfg as text. Neither is
//                              in this vpc.
//
// They are still REGISTERED, which was the whole point of keeping them: a
// setting an operator or a save writes has to have somewhere to land. What
// changed is that the default, the flags and the help text are declared where
// managed code can read them.
//
// A THIRD ENTRY STOOD ON THIS LIST AND HAS BEEN DELETED, not moved:
// sv_maxreplay was a POINTER rather than a declaration, so this rule never
// applied to it, and step 5c dropped it with the rest of Replay. See the block
// above IScriptManager for the 0-consumer measurement.
//-----------------------------------------------------------------------------

// g_flServerCurTime, the rollback-independent server clock, went in
// plan_server_native_v2 T6: its one reader was game/shared/baseprojectile.cpp,
// which neither DLL compiles, so the frame and the level reset wrote it for
// nobody. IShimFrame::SetServerCurTime and IShimLevel::ResetServerTime, the
// slots that wrote it, left in the serialised ABI pass (2026-10-20).

//=============================================================================//
// 2. THE ACQUISITION -- was shell_engineinterfaces.cpp
//=============================================================================//


// Stock's, all defined in gameinterface.cpp outside the class. enginecallback.h
// declares most of them; these four it does not.
extern INetworkStringTableContainer *networkstringtable;
extern IUniformRandomStream *random_valve;
extern IUploadGameStats *gamestatsuploader;

// InitializeCvars went with shell_gamedll_convars.cpp
// (plan_server_native_min P1): it assigned g_pDeveloper, whose last two
// dereferences left with their files' ports. Stock's DLLInit called it before
// anything read a game convar; nothing reads one natively any more.

// The engine's model cache (connected with the tiers).
extern IMDLCache *mdlcache;

// The particle system's questions were answered here by shell_particlequery.cpp
// (plan_entity.md step 4c) for CParticleSystemMgr::Init. Both left server.so in
// plan_server_novalve.md P2.1: nothing on the server asks the manager anything.

//-----------------------------------------------------------------------------
// Purpose: The straight-line half of stock's DLLInit -- acquire, connect, and
//          the two files of game events. No game system is added or inited
//          here; that is Part 9 and Phase 7.
//
// Returns false the moment one interface is missing, which is stock's own
// behaviour and the only useful one: a server missing `modelinfo` does not
// half-work, it crashes on the first model, several seconds later, somewhere
// else.
//-----------------------------------------------------------------------------
bool SourceSharp_AcquireEngineInterfaces( CreateInterfaceFn appSystemFactory,
                                          CreateInterfaceFn physicsFactory,
                                          CreateInterfaceFn fileSystemFactory )
{
	if ( (networkstringtable = (INetworkStringTableContainer *)appSystemFactory( INTERFACENAME_NETWORKSTRINGTABLESERVER, NULL )) == NULL )
		return false;
	if ( (staticpropmgr = (IStaticPropMgrServer *)appSystemFactory( INTERFACEVERSION_STATICPROPMGR_SERVER, NULL )) == NULL )
		return false;
	if ( (random_valve = (IUniformRandomStream *)appSystemFactory( VENGINE_SERVER_RANDOM_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( (::partition = (ISpatialPartition *)appSystemFactory( INTERFACEVERSION_SPATIALPARTITION, NULL )) == NULL )
		return false;
	if ( (modelinfo = (IVModelInfo *)appSystemFactory( VMODELINFO_SERVER_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( (enginetrace = (IEngineTrace *)appSystemFactory( INTERFACEVERSION_ENGINETRACE_SERVER, NULL )) == NULL )
		return false;
	if ( (filesystem = (IFileSystem *)fileSystemFactory( FILESYSTEM_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( (gameeventmanager = (IGameEventManager2 *)appSystemFactory( INTERFACEVERSION_GAMEEVENTSMANAGER2, NULL )) == NULL )
		return false;

	// TWENTY-FIRST, AND IT WAS MISSING. Phase 6 transliterated stock's
	// acquisition block and this one is at gameinterface.cpp:703, inside it --
	// it was dropped, and nothing noticed because nothing on the shell path
	// had reached CServerGameDLL::GameFrame, whose
	// `gamestatsuploader->UpdateConnection()` is the only per-frame reader.
	// Phase 7 puts that line in the frame's order, so the NULL would have been
	// a dereference sixty-six times a second on the first map.
	if ( (gamestatsuploader = (IUploadGameStats *)appSystemFactory( INTERFACEVERSION_UPLOADGAMESTATS, NULL )) == NULL )
		return false;
	ISoundEmitterSystemBase *pSoundEmitter =
		(ISoundEmitterSystemBase *)appSystemFactory( SOUNDEMITTERSYSTEM_INTERFACE_VERSION, NULL );
	if ( pSoundEmitter == NULL )
		return false;

	// mdlcache is connected by ConnectTier3Libraries, which the shell's DLLInit
	// has already called. Checked, not acquired -- exactly as stock does.
	if ( !mdlcache )
		return false;

	// IServerPluginHelpers stood here (serverpluginhelpers) and went with the
	// plugin ABI (plan_server_native_min P1, Decision 1): third-party plugins
	// are dropped, nothing read the acquired pointer, and the engine asks this
	// DLL for no plugin interface (engine-boundary.md §6).


	// NO VSCRIPT. Stock acquired IScriptManager here (optional, skipped under
	// -noscripting); nothing in server.so ever called it, and plan_server_novalve
	// dropped it: vscript/ivscript.h is the header whose closure reached cbase.h
	// (variant.h -> ehandle.h). engine.so carries no VScriptManager string, so
	// the engine never asks this DLL for one either.

	// Both the client and the game DLL Connect this; the soundemittersystem
	// handles the second one gracefully. Stock's comment, and its call.
	if ( !pSoundEmitter->Connect( appSystemFactory ) )
		return false;

	// The three factories, stored for whatever inits later and needs one.
	// physics_impact_damage, the physics hook and the save/restore blocks all
	// read this list rather than being handed a factory.
	ss::factorylist_t factories;
	factories.engineFactory = appSystemFactory;
	factories.fileSystemFactory = fileSystemFactory;
	factories.physicsFactory = physicsFactory;
	ss::FactoryList_Store( factories );

	// The engine's own event list, then the mod's. BOTH before anything can
	// fire one -- an event fired with no descriptor loaded is dropped with a
	// warning, which reads as "the event does not happen".
	gameeventmanager->LoadEventsFromFile( "resource/gameevents.res" );

	// stock's DLLInit loads this one much later, immediately before
	// InitAllSystems, and its comment says why: "MUST be before
	// InitAllSystems() so it can pickup the mod specific events". The shell
	// calls no InitAllSystems, so the only ordering that still matters is
	// "before anything fires one", and both files are loaded together here.
	gameeventmanager->LoadEventsFromFile( "resource/ModEvents.res" );

	// The game's convars: InitializeCvars stood here and went with
	// shell_gamedll_convars.cpp (plan_server_native_min P1) -- its pointer's
	// readers are gone and the registration is shell_gamedll.cpp:258's.
	// No particle system manager: stock inited CParticleSystemMgr here so
	// LevelInit could read the manifests through it. They are read in C# since
	// plan_server_novalve.md P2.1, and server.so links neither particles.a nor
	// dmxloader.a.

	// sv_cheats is looked up and required, as stock's DLLInit did; nothing on
	// the server reads the handle any more (managed's DevCommand owns the cheat
	// gate), so it is not kept (plan_server_native_v2 T6).
	if ( !g_pCVar->FindVar( "sv_cheats" ) )
		return false;

	// try to get debug overlay, may be NULL if on HLDS -- stock's comment, and
	// stock does NOT check the answer.
	debugoverlay = (IVDebugOverlay *)appSystemFactory( VDEBUG_OVERLAY_INTERFACE_VERSION, NULL );

	// "init the gamestatsupload connection" -- stock's comment, at
	// gameinterface.cpp:842-843, the last thing its DLLInit does before
	// returning true. DLLShutdown calls the SAME method again to reset it,
	// which is stock's own arrangement and not a mistake here.
	gamestatsuploader->InitConnection();

	return true;
}

// cs_sv_engine_vectors, the live check of engine_heap.h's vectors, is a
// diagnostic: it lives in shell_engineboundary_debug.cpp, which only a debug
// build compiles.
