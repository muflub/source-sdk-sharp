//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load

#include "shell_diagnostics.h"
#include "shell_gameclients.h"
#include "ss_entityfactory.h"
#include "managed_side.h"
#include "diagnostics_hooks.h"
#include "convar_registry.h"
#include "shell_log.h"
#include "tier0_spew_port.h"
#include "late_bound_game_ents.g.h"
#include "shell_gametags.h"
#include "shell_sharedappsystems.h"
#include "tier_connect.h"

#include "eiface.h"

#include "tier1/tier1.h"
#include "tier2/tier2.h"
#include "tier3/tier3.h"

#include "icvar.h"
#include "tier0/icommandline.h"
#include "server_class.h"
#include "dt_send.h"

#include "sourcesharp_gamesystem.h"
#include "shell_managed_areas.h"
#include "shell_server.h"
#include "shell_savecomment.h"
#include "shell_steporder.h"
#include "shell_gamedll_internal.h"
#include "shell_factory.h"
#include "ss_string.h"
#include "server_state.h"
#include "shell_shim_root_server.h"

// The two the engine follows into a C++ ABI, seeded into members at DLLInit.
extern ServerClass *g_pServerClassHead;
extern CStandardSendProxies g_StandardSendProxies;

class CShellGameDll : public IServerGameDLL, public IConCommandBaseAccessor, public IManagedRuntimeEnvironment
{
public:
	virtual ~CShellGameDll()
	{
		AssertMsg( ManagedHolders_Empty( m_Managed ),
				   "the game DLL object is destroyed holding a managed-built object" );
	}

	bool DLLInit( CreateInterfaceFn engineFactory, CreateInterfaceFn physicsFactory,
				  CreateInterfaceFn fileSystemFactory, CGlobalVars *pGlobals ) override
	{
		// The DLL tier first: everything below that is DLL state lives in it.
		m_pDll.Begin();

		m_pEngineFactory = engineFactory;
		m_pPhysicsFactory = physicsFactory;
		m_pFileSystemFactory = fileSystemFactory;
		m_pGlobals = pGlobals;
		m_ShimRoot.m_pGlobals = pGlobals;

		Msg( "[SourceSharp] shell game DLL: answering from the shell "
			 "-- there is no delegate any more (Phase 7a)\n" );

		SourceSharp_ConnectTier3( engineFactory );

		m_pEngine = (IVEngineServer *)engineFactory( INTERFACEVERSION_VENGINESERVER, nullptr );
		m_pCVar = (ICvar *)engineFactory( CVAR_INTERFACE_VERSION, nullptr );
		m_CVars.Connect( m_pCVar );
		Tier0_ConnectConsole( m_pCVar );

		if ( m_pEngine == nullptr || m_pCVar == nullptr )
		{
			return false;
		}

		Dll()->m_Engine.m_pEngine = m_pEngine;

		if ( !SourceSharp_AcquireEngineInterfaces( engineFactory, physicsFactory, fileSystemFactory, m_pCVar ) )
		{
			return false;
		}

		// CBaseEdict::StateChanged dereferences this unconditionally on every networked
		// write, so it must be set before anything networks.
		Dll()->m_Engine.m_pChangeInfo = m_pEngine->GetSharedEdictChangeInfo();

		// Before the runtime loads: managed acquires the areas these implement.
		Dll()->m_pServices = std::make_unique<CServerDllServices>(
			Dll()->m_Engine, pGlobals, m_Managed, m_ManagedSide, m_TransmitSet, Dll()->m_Players, Dll()->m_nWorldSerial,
			Dll()->m_nInvisibleRangeReach, Dll()->m_SlotTable, Dll()->m_EntitySlots );
		BindServiceAreas( Dll()->m_pServices.get() );

		m_CVars.Register( FCVAR_GAMEDLL, this );

		// The runtime is held by the shim root and acquired afresh on every load.
		if ( LoadManagedRuntime( this, m_ManagedSide ) == nullptr )
		{
			return false;
		}

		// A ServerClass built after this is not in the list the engine walks.
		Dll()->m_pServices->m_TempEnts.CloseRegistration();

		if ( !Runtime()->OnSystemStart() )
		{
			return false;
		}

		// After OnSystemStart: managed startup fills both the server class list and the
		// temp-entity classes this snapshots.
		SeedNetworkingPointers();

		// The engine asks GetTickInterval once, so the interval is fixed for the run.
		m_flTickInterval = Runtime()->OnTickInterval();

		return true;
	}

	// GetAllServerClasses and GetStandardSendProxies answer from members seeded here.
	// The chain is kept in its own order: the engine assigns class IDs by walking it,
	// and the client rebuilds them by walking its own list sorted the same way.
	void SeedNetworkingPointers()
	{
		int nManaged = 0;
		int nStock = 0;

		for ( ServerClass *pClass = g_pServerClassHead; pClass != nullptr; pClass = pClass->m_pNext )
		{
			if ( SourceSharp_IsManagedServerClass( Dll()->m_pServices->m_TempEnts, pClass ) )
				nManaged++;
			else
				nStock++;
		}

		m_pServerClasses = g_pServerClassHead;

		Msg( "[SourceSharp] server classes: %d from managed, %d from g_pServerClassHead\n", nManaged, nStock );

		// By value: the engine compares the function pointers inside, not the struct's address.
		m_SendProxies = g_StandardSendProxies;
		m_pSendProxies = &m_SendProxies;
	}

	void DLLShutdown() override
	{
		// The engine calls DLLShutdown after a failed DLLInit too, when the runtime may be absent.
		if ( Runtime() != nullptr )
		{
			m_ManagedSide.Unload();
		}

		// Stock's order reversed; the unregister needs the console, so it runs before it goes.
		SourceSharp_DisconnectTier3();
		m_CVars.Unregister();
		m_ShimRoot.m_Console.ReleaseObjects();
		m_CVars.Connect( nullptr );
		Tier0_ConnectConsole( nullptr );

		m_pEngine = nullptr;
		m_ShimRoot.m_pGlobals = nullptr;
		m_pCVar = nullptr;

		// Level tier, then DLL tier, last.
		BindServiceAreas( nullptr );
		m_pLevel.End();
		m_pDll.End();
	}

	// Keeps its vtable slot: the engine calls IServerGameDLL by position. Replay is refused.
	bool ReplayInit( CreateInterfaceFn fnReplayFactory ) override
	{
		(void)fnReplayFactory;
		return false;
	}

	bool GameInit() override { return Runtime()->OnGameStart(); }

	void PostInit() override { Runtime()->OnGamePostStart(); }

	void GameShutdown() override { Runtime()->OnGameStop(); }

	bool LevelInit( char const *pMapName, char const *pMapEntities, char const *pOldLevel, const char *pLandmarkName,
					bool loadGame, bool background ) override
	{
		// A new level tier for every map, before anything keeps per-map state.
		m_pLevel.Begin();
		Dll()->m_pServices->m_EntityContext.m_pLevel = Level();

		SourceSharpMapLoad load = {};
		load.pMapName = pMapName;
		load.pMapEntities = pMapEntities;
		load.pOldLevel = pOldLevel;
		load.pLandmarkName = pLandmarkName;

		return Runtime()->OnMapLoad( &load, loadGame, background );
	}

	void LevelShutdown() override
	{
		Runtime()->OnMapUnload();

		Dll()->m_pServices->m_EntityContext.m_pLevel = nullptr;
		m_pLevel.End();
	}

	void ServerActivate( edict_t *pEdictList, int edictCount, int clientMax ) override
	{
		Runtime()->OnServerActivate();
	}

	void Think( bool finalTick ) override
	{
		// Services a queued cs_sv_reload. Here rather than in GameFrame because Think is
		// called every frame even with no level loaded, and never nested in GameFrame, so
		// a reload is atomic with respect to the frame. A failed reload does not return.
		m_ManagedSide.ServiceReloadRequest();

		Runtime()->OnThink();
	}

	void GameFrame( bool simulating ) override { Runtime()->OnGameFrame( simulating ); }

	void PreClientUpdate( bool simulating ) override
	{
		if ( !simulating )
			return;

		Runtime()->OnPreClientUpdate();
	}

	float GetTickInterval() const override { return m_flTickInterval; }

	ServerClass *GetAllServerClasses() override { return m_pServerClasses; }

	// Asked of C#, with a default when it is not up.
	const char *GetGameDescription() override
	{
		SOURCESHARP_TRACE_HOT();

		if ( m_ManagedSide.Loaded() && Runtime() )
		{
			m_szGameDescription[0] = '\0';
			Runtime()->OnGetGameDescription( m_szGameDescription, sizeof( m_szGameDescription ) );
			if ( m_szGameDescription[0] )
				return m_szGameDescription;
		}

		return "SourceSharp";
	}

	void CreateNetworkStringTables() override
	{
		// Native tables first: the engine and this binary depend on them, and a mod's
		// strings must not take index 0 of Materials ("debug/debugempty").
		Dll()->m_pServices->m_EntityContext.m_pGameRulesTable = SourceSharp_CreateNetworkStringTables();

		// Mod-declared tables; the engine allows creating tables only in this window.
		Runtime()->CreateNetworkStringTables();
	}

	CSaveRestoreData *SaveInit( int size ) override
	{
		IManagedSaveRestore *pArea = m_Managed.m_Areas.m_pSaveRestore;
		CSaveRestoreData *pSaveData = pArea
			? static_cast<CSaveRestoreData *>( pArea->SaveInit( size, m_pGlobals ? m_pGlobals->curtime : 0.0f ) )
			: nullptr;

		if ( pSaveData == nullptr )
		{
			// The engine abandons the save on NULL and prints nothing.
			Warning( "[SourceSharp] SaveInit is answering NULL. The engine will abandon the "
					 "save at this call site and print NOTHING -- no file and no error is "
					 "what that looks like from outside\n" );
			return nullptr;
		}

		// As stock's ::SaveInit does: gpGlobals->pSaveData reaches the save in progress.
		if ( m_pGlobals )
			m_pGlobals->pSaveData = pSaveData;

		return pSaveData;
	}

	// The engine hands the game its own save header and light styles to encode. The
	// struct size is passed because typedescription_t holds a member-function pointer.
	void SaveWriteFields( CSaveRestoreData *pSave, const char *pname, void *pBaseData, datamap_t *pMap,
						  typedescription_t *pFields, int fieldCount ) override
	{
		if ( IManagedSaveRestore *pArea = m_Managed.m_Areas.m_pSaveRestore )
			pArea->SaveWriteFields( pSave, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ),
									fieldCount );
	}

	void SaveReadFields( CSaveRestoreData *pSave, const char *pname, void *pBaseData, datamap_t *pMap,
						 typedescription_t *pFields, int fieldCount ) override
	{
		if ( IManagedSaveRestore *pArea = m_Managed.m_Areas.m_pSaveRestore )
			pArea->SaveReadFields( pSave, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ), fieldCount );
	}

	void SaveGlobalState( CSaveRestoreData *pSave ) override
	{
		Msg( "[SourceSharp][save] SaveGlobalState  -- nothing written; gGlobalState is empty in "
			 "this build and globalstate.cpp's body is #ifndef SOURCESHARP\n" );
	}

	void RestoreGlobalState( CSaveRestoreData *pSave ) override
	{
		Msg( "[SourceSharp][save] RestoreGlobalState  -- nothing read; symmetric with "
			 "SaveGlobalState\n" );
	}

	void PreSave( CSaveRestoreData *pSave ) override
	{
		Msg( "[SourceSharp][save] PreSave  -- no entity table is built; tableCount stays 0\n" );
	}

	void Save( CSaveRestoreData *pSave ) override
	{
		if ( IManagedSaveRestore *pArea = m_Managed.m_Areas.m_pSaveRestore )
			pArea->SaveWriteBody( pSave );
	}

	void GetSaveComment( char *comment, int maxlength, float flMinutes, float flSeconds, bool bNoTime = false ) override
	{
		ShellGameDll_Unreachable( "IServerGameDLL::GetSaveComment" );

		ShellGameDll_FormatSaveComment( comment, maxlength, STRING( m_pGlobals->mapname ), flMinutes, flSeconds,
										bNoTime );
	}

	void WriteSaveHeaders( CSaveRestoreData *pSave ) override
	{
		Msg( "[SourceSharp][save] WriteSaveHeaders  -- 0 header blocks\n" );
	}

	void ReadRestoreHeaders( CSaveRestoreData *pSave ) override
	{
		Msg( "[SourceSharp][save] ReadRestoreHeaders  -- 0 header blocks expected\n" );
	}

	void Restore( CSaveRestoreData *pSave, bool createPlayers ) override
	{
		// The map's native entities are already respawned; this lays the managed state over them.
		if ( IManagedSaveRestore *pArea = m_Managed.m_Areas.m_pSaveRestore )
			pArea->SaveReadBody( pSave );
	}

	// Restores complete inside the engine's call, so this is never true. GameFrame
	// reads it every frame, so it must not warn.
	bool IsRestoring() override { return false; }

	int CreateEntityTransitionList( CSaveRestoreData *pSave, int nBytes ) override
	{
		ShellGameDll_Unreachable( "IServerGameDLL::CreateEntityTransitionList" );

		return 0;
	}

	void BuildAdjacentMapList() override { ShellGameDll_Unreachable( "IServerGameDLL::BuildAdjacentMapList" ); }

	bool GetUserMessageInfo( int msg_type, char *name, int maxnamelength, int &size ) override
	{
		IManagedUserMessages *pMessages = m_Managed.m_Areas.m_pUserMessages;
		return pMessages && pMessages->GetUserMessageInfo( msg_type, name, maxnamelength, &size );
	}

	CStandardSendProxies *GetStandardSendProxies() override { return m_pSendProxies; }

	void PreSaveGameLoaded( char const *pSaveName, bool bCurrentlyInGame ) override
	{
		Msg( "[SourceSharp][save] PreSaveGameLoaded( '%s', currentlyInGame=%d )\n", pSaveName ? pSaveName : "<null>",
			 bCurrentlyInGame ? 1 : 0 );
	}

	bool ShouldHideServer() override { return Runtime()->OnShouldHideServer(); }

	void InvalidateMdlCache() override { Runtime()->OnInvalidateMdlCache(); }

	// The edict becomes an entity index; NULL (the client left) becomes -1, since 0 is worldspawn.
	void OnQueryCvarValueFinished( QueryCvarCookie_t iCookie, edict_t *pPlayerEntity, EQueryCvarValueStatus eStatus,
								   const char *pCvarName, const char *pCvarValue ) override
	{
		ShellGameDll_Unreachable( "IServerGameDLL::OnQueryCvarValueFinished" );

		const int nEntIndex =
			( pPlayerEntity != nullptr && m_pEngine != nullptr ) ? m_pEngine->IndexOfEdict( pPlayerEntity ) : -1;

		Runtime()->OnQueryCvarValueFinished( (int)iCookie, nEntIndex, (int)eStatus, pCvarName, pCvarValue );
	}

	void GameServerSteamAPIActivated() override { Runtime()->OnSteamGameServerActivated(); }

	// Managed runs first and clears the context itself, so its handlers still see it.
	void GameServerSteamAPIShutdown() override { Runtime()->OnSteamGameServerShutdown(); }

	void SetServerHibernation( bool bHibernating ) override
	{
		ShellGameDll_Unreachable( "IServerGameDLL::SetServerHibernation" );

		Runtime()->OnHibernation( bHibernating );
	}

	IServerGCLobby *GetServerGCLobby() override { return nullptr; }

	const char *GetServerBrowserMapOverride() override
	{
		// Null means "show the real map name"; "" means "show nothing". Managed returns
		// -1 for no answer and 0 for empty.
		return Answer( m_szBrowserMapOverride, sizeof( m_szBrowserMapOverride ),
					   Runtime()->OnBrowserMapOverride( m_szBrowserMapOverride, sizeof( m_szBrowserMapOverride ) ) );
	}

	const char *GetServerBrowserGameData() override
	{
		return Answer( m_szBrowserGameData, sizeof( m_szBrowserGameData ),
					   Runtime()->OnBrowserGameData( m_szBrowserGameData, sizeof( m_szBrowserGameData ) ) );
	}

	// The engine's `status` command. `print` is variadic and cannot cross to managed,
	// so managed returns '\n'-separated lines and the shell prints them; empty lines
	// are dropped.
	void Status( void ( *print )( const char *fmt, ... ) ) override
	{
		if ( print == nullptr )
			return;

		const int nWritten = Runtime()->OnStatusLines( m_szStatusLines, sizeof( m_szStatusLines ) );

		if ( nWritten < 0 || nWritten >= (int)sizeof( m_szStatusLines ) )
			return;

		m_szStatusLines[nWritten] = '\0';

		char *pLine = m_szStatusLines;
		while ( pLine != nullptr && *pLine != '\0' )
		{
			char *pNext = strchr( pLine, '\n' );
			if ( pNext != nullptr )
				*pNext++ = '\0';

			if ( *pLine != '\0' )
				print( "%s\n", pLine );

			pLine = pNext;
		}
	}

	void PrepareLevelResources( /* in/out */ char *pszMapName, size_t nMapNameSize,
								/* in/out */ char *pszMapFile, size_t nMapFileSize ) override
	{
	}

	ePrepareLevelResourcesResult AsyncPrepareLevelResources( /* in/out */ char *pszMapName, size_t nMapNameSize,
															 /* in/out */ char *pszMapFile, size_t nMapFileSize,
															 float *flProgress = NULL ) override
	{
		if ( flProgress )
		{
			*flProgress = 1.f;
		}

		return IServerGameDLL::ePrepareLevelResources_Prepared;
	}

	eCanProvideLevelResult CanProvideLevel( /* in/out */ char *pMapName, int nMapNameMax ) override
	{
		return IServerGameDLL::eCanProvideLevel_CannotProvide;
	}

	bool IsManualMapChangeOkay( const char **pszReason ) override
	{
		// Cleared first, since managed writes a reason only when it refuses.
		m_szMapChangeReason[0] = '\0';

		const bool bOkay = Runtime()->OnIsManualMapChangeOkay( m_szMapChangeReason, sizeof( m_szMapChangeReason ) );

		if ( pszReason != nullptr )
		{
			// NULL when there is nothing to say: the engine tests the pointer.
			*pszReason = ( !bOkay && m_szMapChangeReason[0] != '\0' ) ? m_szMapChangeReason : nullptr;
		}

		return bOkay;
	}

	bool GetWorkshopMap( uint32 uIndex, WorkshopMapDesc_t *pDesc ) override { return false; }

	bool RegisterConCommandBase( ConCommandBase *pCommand ) override
	{
		// Stock's CGameDLL_ConVarAccessor. Without the command-line apply `+sv_cheats 1` is
		// ignored, and without the replicated default a debug DLL takes the client's value.

		// Remember the "unlinked" default value for replicated cvars.
		const bool bReplicated = pCommand->IsFlagSet( FCVAR_REPLICATED );
		const char *pDefault = nullptr;
		if ( bReplicated && !pCommand->IsCommand() )
		{
			pDefault = ( (ConVar *)pCommand )->GetDefault();
		}

		// Link to engine's list instead
		m_pCVar->RegisterConCommand( pCommand );

		// Apply any command-line values.
		const char *pValue = m_pCVar->GetCommandLineValue( pCommand->GetName() );
		if ( pValue )
		{
			if ( !pCommand->IsCommand() )
			{
				( (ConVar *)pCommand )->SetValue( pValue );
			}
		}
		else if ( bReplicated && pDefault && !pCommand->IsCommand() )
		{
			// Not overridden at the command line, so a replicated ConVar takes the server's value.
			( (ConVar *)pCommand )->SetValue( pDefault );
		}

		return true;
	}

	void *GetEngineInterface( const char *pName, int *pReturnCode ) override
	{
		return m_pEngineFactory( pName, pReturnCode );
	}

	// Engine-owned memory, alive for the process and changing every frame; read through the pointer.
	CGlobalVars &GetGlobalVars() override { return *m_pGlobals; }

public:
	CShellTransmitSet &TransmitSet() { return m_TransmitSet; }

	CManagedHolders &Managed() { return m_Managed; }

	// This load's managed runtime, or NULL before it loads and after it unloads.
	IManagedRuntimeServer *Runtime() const { return m_Managed.m_pRuntimeServer; }

	CServerDllState *Dll() const { return m_pDll.Get(); }

	CServerLevelState *Level() const { return m_pLevel.Get(); }

	CShimRootServer &ShimRoot() { return m_ShimRoot; }

	// The engine fetches this when it loads the DLL, before DLLInit.
	IServerGameClients *GameClients() { return &m_GameClients; }

	IServerGameEnts *GameEnts() { return &m_GameEnts; }

	IServerGameTags *GameTags() { return &m_GameTags; }

	IServerDLLSharedAppSystems *SharedAppSystems() { return &m_SharedAppSystems; }

private:
	void ShellGameDll_Unreachable( const char *pMethod )
	{
		Warning( "[SourceSharp] REFUSED: %s was called. plan_gamedll.md Part 7 "
				 "argues this method is unreachable in this build, so it is a "
				 "no-op -- if you are reading this, that argument was wrong for "
				 "this method. `grep -c REFUSED` over console.log is the tally.\n",
				 ( pMethod != nullptr ) ? pMethod : "(unnamed)" );
	}

	// Hands the shim root the areas the services implement; NULL withdraws them.
	void BindServiceAreas( CServerDllServices *pServices )
	{
		m_ShimRoot.m_Areas.m_pUserMessages = pServices ? &pServices->m_UserMessages : nullptr;
		m_ShimRoot.m_Areas.m_pDebugOverlay = pServices ? &pServices->m_DebugOverlays : nullptr;
		m_ShimRoot.m_Areas.m_pEntityLifecycle = pServices ? &pServices->m_EntityLifecycle : nullptr;
		m_ShimRoot.m_Areas.m_pPrecache = pServices ? &pServices->m_Precache : nullptr;
		m_ShimRoot.m_Areas.m_pWorldServer = pServices ? &pServices->m_WorldQueries : nullptr;
		m_ShimRoot.m_Areas.m_pEffectsServer = pServices ? &pServices->m_TempEnts : nullptr;
		m_ShimRoot.m_Areas.m_pDamage = pServices ? &pServices->m_Damage : nullptr;
		m_ShimRoot.m_Areas.m_pPhysics = pServices ? &pServices->m_Physics : nullptr;
		m_ShimRoot.m_Areas.m_pConsoleServer = pServices ? &pServices->m_ConsoleCommands : nullptr;
		m_ShimRoot.m_Areas.m_pModelsServer = pServices ? &pServices->m_Models : nullptr;
		m_ShimRoot.m_Areas.m_pNetworkServer = pServices ? &pServices->m_Network : nullptr;
		m_ShimRoot.m_Areas.m_pEntitiesServer = pServices ? &pServices->m_Entities : nullptr;
		m_ShimRoot.m_Areas.m_pMovementServer = pServices ? &pServices->m_Movement : nullptr;
		m_ShimRoot.m_Areas.m_pCollisionServer = pServices ? &pServices->m_Collision : nullptr;
		m_ShimRoot.m_Areas.m_pPlayers = pServices ? &pServices->m_PlayerQueries : nullptr;
		m_ShimRoot.m_Areas.m_pTransform = pServices ? &pServices->m_Transform : nullptr;
		m_ShimRoot.m_Areas.m_pHierarchy = pServices ? &pServices->m_Hierarchy : nullptr;
		m_ShimRoot.m_pFrame = pServices ? &pServices->m_Frame : nullptr;
		m_ShimRoot.m_pLevel = pServices ? &pServices->m_Level : nullptr;
		m_ShimRoot.m_pFactories = pServices ? &Dll()->m_Engine.m_Factories : nullptr;
		m_ShimRoot.m_Console.m_pServices = pServices;
		m_ShimRoot.m_Console.m_pCVars = pServices ? &m_CVars : nullptr;
		m_ShimRoot.m_Console.m_ppCommands = pServices ? &m_Managed.m_Areas.m_pCommands : nullptr;
		m_GameClients.Bind( pServices ? &pServices->m_GameClients : nullptr );
		m_GameEnts.Bind( pServices ? &pServices->m_GameEnts : nullptr );
	}

	static const char *Answer( const char *pBuffer, int nSize, int nWritten )
	{
		if ( nWritten < 0 || nWritten >= nSize )
			return nullptr;

		return pBuffer;
	}

	// First, so everything the root builds after it can log.
	CShellLog m_Log;

	CreateInterfaceFn m_pEngineFactory = {};
	CreateInterfaceFn m_pPhysicsFactory = {};
	CreateInterfaceFn m_pFileSystemFactory = {};
	CGlobalVars *m_pGlobals = {};

	ServerClass *m_pServerClasses = {};

	// m_SendProxies is the storage the engine is handed the address of.
	CStandardSendProxies m_SendProxies = {};
	CStandardSendProxies *m_pSendProxies = {};

	// Buffers managed writes into; the engine reads them after the call returns.
	float m_flTickInterval = DEFAULT_TICK_INTERVAL;
	char m_szBrowserMapOverride[128] = {};
	char m_szBrowserGameData[128] = {};
	char m_szMapChangeReason[128] = {};

	char m_szStatusLines[1024] = {};

	// CheckTransmit records through the free functions below, not through this class.
	CShellTransmitSet m_TransmitSet;

	CManagedHolders m_Managed = {};
	// Before the managed side, which installs them at load.
	CDiagnosticsHooks m_Diagnostics;

	// The console: this DLL's carriers and the ICvar they reach.
	CCVarRegistry m_CVars;
	CManagedSide m_ManagedSide{ m_Managed, m_Diagnostics, m_CVars };

	// GetGameDescription's answer; the engine reads it after the call returns.
	char m_szGameDescription[128] = {};

	IVEngineServer *m_pEngine = {};
	ICvar *m_pCVar = {};

	CStateTier<CServerDllState> m_pDll;
	CStateTier<CServerLevelState> m_pLevel;

	// The object handed to managed at load; every other area is acquired from it.
	CShimRootServer m_ShimRoot;

	CRootServerGameClients m_GameClients;
	CLateBoundServerGameEnts m_GameEnts;

	// Asked for at load, before DLLInit; they need nothing.
	CServerGameTags m_GameTags;
	CServerDLLSharedAppSystems m_SharedAppSystems;
};

namespace
{
CShellGameDll s_gameShellDll;
}

// The managed holders, for code not yet handed them.
CManagedHolders &ShellGameDll_Managed()
{
	return s_gameShellDll.Managed();
}

CServerDllState *ShellGameDll_Dll()
{
	return s_gameShellDll.Dll();
}

CServerLevelState *ShellGameDll_Level()
{
	return s_gameShellDll.Level();
}

CShimRootServer &ShellGameDll_ShimRoot()
{
	return s_gameShellDll.ShimRoot();
}

CShellTransmitSet &ShellGameDll_TransmitSet()
{
	return s_gameShellDll.TransmitSet();
}

static void *SourceSharp_GetServerGameDll()
{
	return (IServerGameDLL *)&s_gameShellDll;
}

const SourceSharpExposed kExposeServerGameDll = { INTERFACEVERSION_SERVERGAMEDLL, SourceSharp_GetServerGameDll };

static void *SourceSharp_GetServerGameEnts()
{
	return s_gameShellDll.GameEnts();
}

const SourceSharpExposed kExposeServerGameEnts = { INTERFACEVERSION_SERVERGAMEENTS, SourceSharp_GetServerGameEnts };

static void *SourceSharp_GetServerGameTags()
{
	return s_gameShellDll.GameTags();
}

const SourceSharpExposed kExposeServerGameTags = { INTERFACEVERSION_SERVERGAMETAGS, SourceSharp_GetServerGameTags };

static void *SourceSharp_GetServerDllSharedAppSystems()
{
	return s_gameShellDll.SharedAppSystems();
}

const SourceSharpExposed kExposeServerDllSharedAppSystems = { SERVER_DLL_SHARED_APPSYSTEMS,
															  SourceSharp_GetServerDllSharedAppSystems };

// IServerGameClients only appends, so one object answers all three versions.
static void *SourceSharp_GetServerGameClients()
{
	return s_gameShellDll.GameClients();
}

const SourceSharpExposed kExposeServerGameClients = { INTERFACEVERSION_SERVERGAMECLIENTS,
													  SourceSharp_GetServerGameClients };
const SourceSharpExposed kExposeServerGameClients004 = { INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_4,
														 SourceSharp_GetServerGameClients };
const SourceSharpExposed kExposeServerGameClients003 = { INTERFACEVERSION_SERVERGAMECLIENTS_VERSION_3,
														 SourceSharp_GetServerGameClients };

// When bumping the version to this interface, check that our assumption is
// still valid and expose the older version in the same way.
COMPILE_TIME_ASSERT( INTERFACEVERSION_SERVERGAMEDLL_INT == 12 );
