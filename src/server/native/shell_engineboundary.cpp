//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load

// The engine interface globals the SDK headers declare extern, and their acquisition
// at DLLInit, in stock's order.

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

#include "eiface.h"
#include "ss_init_factory.h"
#include "vstdlib/random.h"
#include "tier0/icommandline.h"
#include "datacache/imdlcache.h"
#include "shell_server.h"

IUploadGameStats *gamestatsuploader = NULL;

// Engine interfaces, assigned only by SourceSharp_AcquireEngineInterfaces.
IVEngineServer	*engine = NULL;
IFileSystem		*filesystem = NULL;
INetworkStringTableContainer *networkstringtable = NULL;
IStaticPropMgrServer *staticpropmgr = NULL;
// sharedInterface.h renames `random` to random_valve under POSIX, so that is the link name.
IUniformRandomStream *random_valve = NULL;
ISpatialPartition *partition = NULL;
IVModelInfo *modelinfo = NULL;
IEngineTrace *enginetrace = NULL;
IGameEventManager2 *gameeventmanager = NULL;
IVDebugOverlay * debugoverlay = NULL;

// Created by SourceSharp_CreateNetworkStringTables (shell_stringtables.cpp).
INetworkStringTable *g_pStringTableParticleEffectNames = NULL;
INetworkStringTable *g_pStringTableEffectDispatch = NULL;
INetworkStringTable *g_pStringTableVguiScreen = NULL;
INetworkStringTable *g_pStringTableMaterials = NULL;
INetworkStringTable *g_pStringTableInfoPanel = NULL;
INetworkStringTable *g_pStringTableClientSideChoreoScenes = NULL;
INetworkStringTable *g_pStringTableServerMapCycle = NULL;

CGlobalVars *gpGlobals;

CSharedEdictChangeInfo *g_pSharedChangeInfo = NULL;

IChangeInfoAccessor *CBaseEdict::GetChangeAccessor()
{
	return engine->GetChangeAccessor( (const edict_t *)this );
}

extern INetworkStringTableContainer *networkstringtable;
extern IUniformRandomStream *random_valve;
extern IUploadGameStats *gamestatsuploader;

extern IMDLCache *mdlcache;

// Acquires the engine interfaces and loads the game event files. Returns false on
// the first missing interface, as stock's DLLInit does.
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

	if ( (gamestatsuploader = (IUploadGameStats *)appSystemFactory( INTERFACEVERSION_UPLOADGAMESTATS, NULL )) == NULL )
		return false;
	ISoundEmitterSystemBase *pSoundEmitter =
		(ISoundEmitterSystemBase *)appSystemFactory( SOUNDEMITTERSYSTEM_INTERFACE_VERSION, NULL );
	if ( pSoundEmitter == NULL )
		return false;

	// Connected by ConnectTier3Libraries, already called; checked, as stock does.
	if ( !mdlcache )
		return false;

	// Both the client and the game DLL Connect this; the soundemittersystem
	// handles the second one gracefully.
	if ( !pSoundEmitter->Connect( appSystemFactory ) )
		return false;

	ss::factorylist_t factories;
	factories.engineFactory = appSystemFactory;
	factories.fileSystemFactory = fileSystemFactory;
	factories.physicsFactory = physicsFactory;
	ss::FactoryList_Store( factories );

	// Both event files load before anything can fire an event, or it is dropped.
	gameeventmanager->LoadEventsFromFile( "resource/gameevents.res" );

	gameeventmanager->LoadEventsFromFile( "resource/ModEvents.res" );

	// Required, as in stock, though nothing native reads it.
	if ( !g_pCVar->FindVar( "sv_cheats" ) )
		return false;

	// May be NULL on HLDS; stock does not check it.
	debugoverlay = (IVDebugOverlay *)appSystemFactory( VDEBUG_OVERLAY_INTERFACE_VERSION, NULL );

	// DLLShutdown calls InitConnection again to reset it, as stock does.
	gamestatsuploader->InitConnection();

	return true;
}
