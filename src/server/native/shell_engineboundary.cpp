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
#include "server_state.h"
#include "server_engine.h"

#include "engine/IEngineSound.h"
#include "engine/IStaticPropMgr.h"
#include "engine/ivdebugoverlay.h"

#include "igameevents.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"

#include "eiface.h"
#include "ss_init_factory.h"

#include "datacache/imdlcache.h"
#include "shell_server.h"

// Engine interfaces, assigned only by SourceSharp_AcquireEngineInterfaces.
CGlobalVars *gpGlobals;

// edict.h's StateChanged reads the engine's change list through this.
CSharedEdictChangeInfo *SharedEdictChangeInfo()
{
	return ShellGameDll_Dll()->m_Engine.m_pChangeInfo;
}

IChangeInfoAccessor *CBaseEdict::GetChangeAccessor()
{
	return ServerEngine()->GetChangeAccessor( (const edict_t *)this );
}

// Acquires the engine interfaces and loads the game event files. Returns false on
// the first missing interface, as stock's DLLInit does.
bool SourceSharp_AcquireEngineInterfaces( CreateInterfaceFn appSystemFactory, CreateInterfaceFn physicsFactory,
										  CreateInterfaceFn fileSystemFactory )
{
	CServerEngineInterfaces &bound = ShellGameDll_Dll()->m_Engine;

	if ( ( bound.m_pStringTables = (INetworkStringTableContainer *)appSystemFactory(
			   INTERFACENAME_NETWORKSTRINGTABLESERVER, NULL ) ) == NULL )
		return false;
	if ( ( bound.m_pStaticProps =
			   (IStaticPropMgrServer *)appSystemFactory( INTERFACEVERSION_STATICPROPMGR_SERVER, NULL ) ) == NULL )
		return false;
	if ( ( bound.m_pRandom =
			   (IUniformRandomStream *)appSystemFactory( VENGINE_SERVER_RANDOM_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( bound.m_pPartition = (ISpatialPartition *)appSystemFactory( INTERFACEVERSION_SPATIALPARTITION, NULL ) ) ==
		 NULL )
		return false;
	if ( ( bound.m_pModelInfo = (IVModelInfo *)appSystemFactory( VMODELINFO_SERVER_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( bound.m_pTrace = (IEngineTrace *)appSystemFactory( INTERFACEVERSION_ENGINETRACE_SERVER, NULL ) ) == NULL )
		return false;
	if ( ( bound.m_pFileSystem = (IFileSystem *)fileSystemFactory( FILESYSTEM_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( bound.m_pEvents = (IGameEventManager2 *)appSystemFactory( INTERFACEVERSION_GAMEEVENTSMANAGER2, NULL ) ) ==
		 NULL )
		return false;

	if ( ( bound.m_pGameStats = (IUploadGameStats *)appSystemFactory( INTERFACEVERSION_UPLOADGAMESTATS, NULL ) ) ==
		 NULL )
		return false;
	ISoundEmitterSystemBase *pSoundEmitter =
		(ISoundEmitterSystemBase *)appSystemFactory( SOUNDEMITTERSYSTEM_INTERFACE_VERSION, NULL );
	if ( pSoundEmitter == NULL )
		return false;

	// Connected by ConnectTier3Libraries, already called; checked, as stock does.
	if ( !bound.m_pMdlCache )
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
	bound.m_pEvents->LoadEventsFromFile( "resource/gameevents.res" );

	bound.m_pEvents->LoadEventsFromFile( "resource/ModEvents.res" );

	// Required, as in stock, though nothing native reads it.
	if ( !g_pCVar->FindVar( "sv_cheats" ) )
		return false;

	// May be NULL on HLDS; stock does not check it.
	bound.m_pOverlay = (IVDebugOverlay *)appSystemFactory( VDEBUG_OVERLAY_INTERFACE_VERSION, NULL );

	// DLLShutdown calls InitConnection again to reset it, as stock does.
	bound.m_pGameStats->InitConnection();

	return true;
}

// The DLL tier's engine interfaces (server_engine.h).
IVEngineServer *ServerEngine()
{
	return ShellGameDll_Dll()->m_Engine.m_pEngine;
}

IFileSystem *ServerFileSystem()
{
	return ShellGameDll_Dll()->m_Engine.m_pFileSystem;
}

ISpatialPartition *ServerPartition()
{
	return ShellGameDll_Dll()->m_Engine.m_pPartition;
}

IUploadGameStats *ServerGameStats()
{
	return ShellGameDll_Dll()->m_Engine.m_pGameStats;
}

INetworkStringTableContainer *ServerStringTables()
{
	return ShellGameDll_Dll()->m_Engine.m_pStringTables;
}

IVModelInfo *ServerModelInfo()
{
	return ShellGameDll_Dll()->m_Engine.m_pModelInfo;
}

IStaticPropMgrServer *ServerStaticProps()
{
	return ShellGameDll_Dll()->m_Engine.m_pStaticProps;
}

IEngineTrace *ServerTrace()
{
	return ShellGameDll_Dll()->m_Engine.m_pTrace;
}

IMDLCache *ServerMdlCache()
{
	return ShellGameDll_Dll()->m_Engine.m_pMdlCache;
}

IGameEventManager2 *ServerEvents()
{
	return ShellGameDll_Dll()->m_Engine.m_pEvents;
}

IVDebugOverlay *ServerOverlay()
{
	return ShellGameDll_Dll()->m_Engine.m_pOverlay;
}

IUniformRandomStream *ServerRandom()
{
	return ShellGameDll_Dll()->m_Engine.m_pRandom;
}
