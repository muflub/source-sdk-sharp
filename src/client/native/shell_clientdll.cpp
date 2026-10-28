//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The client DLL the engine holds: CShellClientDLL implements IBaseClientDLL, which the
// engine resolves by name, so every slot must exist. FrameStageNotify reaches managed
// code through one crossing per stage.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "inetchannel.h"

#include "igamerules.h"
#include "sharedInterface.h"
#include "vgui_int.h"
#include "iinput.h"
#include "iviewrender.h"
#include "ivmodemanager.h"
#include "prediction.h"

#include "smoke_fog_overlay.h"
#include "ienginevgui.h"
#include "iefx.h"
#include "enginesprite.h"

#include "c_te_legacytempents.h"
#include "c_rope.h"

#include "engine/IStaticPropMgr.h"

#include "env_wind_shared.h"
#include "materialsystem/imaterialsystemstub.h"
#include "VGuiMatSurface/IMatSystemSurface.h"
#include "engine/ivdebugoverlay.h"
#include "vguicenterprint.h"
#include "iviewrender_beams.h"
#include "engine/IEngineTrace.h"

#include "physics.h"
#include "saverestore.h"

#include "gamerules_register.h"
#include "client_world.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "imessagechars.h"
#include "client_factorylist.h"
#include "iclientmode.h"
#include "inputsystem/iinputsystem.h"

#include "particle_parse.h"
#include "renamed_recvtable_compat.h"
#include "mouthinfo.h"
#include "haptics/ihaptics.h"
#include "haptics/haptic_utils.h"

#include "engine/IEngineSound.h"

#include "cdll_util.h"

#include "networkstringtabledefs.h"

#include "usercmd.h"
#include "ss_client_message_write.h"

// The managed runtime this DLL loads, and the environment it loads it from.
#include "sourcesharp_gamesystem.h"
#include "shell_managed_areas_client.h"
#include "shell_view_executor_client.h"
#include "shell_clientdll_refusals.h"

#include "shell_clientsystem.h"
#include "shell_clientdll_voicehelper.h"
#include "shell_cliententitylist.h"
#include "shell_particle_mgr.h"
#include "tier_connect.h"
#include "ss_bitblock.h"

// The largest user-message payload copied for managed; the engine's own limit is 255.
enum
{
	kMaxUserMessageBlockBytes = 512
};

extern vgui::IInputInternal *g_InputInternal;

#include "ss_command_line.h"
#include "shell_particle_systems.h"
#include "shell_new_particles.h"
#include "shell_shim_console.h"
#include "managed_side.h"
#include "diagnostics_hooks.h"
#include "convar_registry.h"
#include "shell_log.h"
#include "tier0_spew_port.h"
#include "game_events.h"
#include "shell_shim_areas.h"

// The root's areas, defined beside the root (shell_internalfactory.cpp).
CShimAreasClient &ShimClientAreasOfRoot();

extern IClientMode *GetClientModeNormal();

IVEngineClient *engine = NULL;
IVModelRender *modelrender = NULL;
IVEfx *effects = NULL;
IVRenderView *render = NULL;
IVDebugOverlay *debugoverlay = NULL;
IMaterialSystemStub *materials_stub = NULL;
IVModelInfoClient *modelinfo = NULL;
IEngineVGui *enginevgui = NULL;
INetworkStringTableContainer *networkstringtable = NULL;
ISpatialPartition *partition = NULL;
IFileSystem *filesystem = NULL;
IShadowMgr *shadowmgr = NULL;
IStaticPropMgrClient *staticpropmgr = NULL;
IEngineSound *enginesound = NULL;
IUniformRandomStream *random = NULL;
ISharedGameRules *sharedgamerules = NULL;
IEngineTrace *enginetrace = NULL;
IGameEventManager2 *gameeventmanager = NULL;
ISoundEmitterSystemBase *soundemitterbase = NULL;
IClientReplayContext *g_pClientReplayContext = NULL;

IHaptics *haptics = NULL;

// Engine player info.
BEGIN_BYTESWAP_DATADESC( player_info_s )
DEFINE_ARRAY( name, FIELD_CHARACTER, MAX_PLAYER_NAME_LENGTH ), DEFINE_FIELD( userID, FIELD_INTEGER ),
	DEFINE_ARRAY( guid, FIELD_CHARACTER, SIGNED_GUID_LEN + 1 ), DEFINE_FIELD( friendsID, FIELD_INTEGER ),
	DEFINE_ARRAY( friendsName, FIELD_CHARACTER, MAX_PLAYER_NAME_LENGTH ), DEFINE_FIELD( fakeplayer, FIELD_BOOLEAN ),
	DEFINE_FIELD( ishltv, FIELD_BOOLEAN ), DEFINE_ARRAY( customFiles, FIELD_INTEGER, MAX_CUSTOM_FILES ),
	DEFINE_FIELD( filesDownloaded, FIELD_INTEGER ),
	END_BYTESWAP_DATADESC()

		INetworkStringTable *g_pStringTableParticleEffectNames = NULL;
INetworkStringTable *g_StringTableEffectDispatch = NULL;
INetworkStringTable *g_StringTableVguiScreen = NULL;
INetworkStringTable *g_pStringTableMaterials = NULL;
INetworkStringTable *g_pStringTableInfoPanel = NULL;
INetworkStringTable *g_pStringTableClientSideChoreoScenes = NULL;
INetworkStringTable *g_pStringTableServerMapCycle = NULL;

class CHudChat;

extern void StopAllRumbleEffects();

ISaveRestoreBlockHandler *GetEntitySaveRestoreBlockHandler();
ISaveRestoreBlockHandler *GetViewEffectsRestoreBlockHandler();

class IMoveHelper;

void DispatchHudText( const char *pszName );

bool g_bLevelInitialized;
bool g_bTextMode = false;

// The refusal tally, printed by a console command and at DLL shutdown.
static void ShellClientDll_PrintRefusals( const CShellClientDllRefusals &refusals )
{
	Msg( "[SourceSharp] client refusals: %d call(s) across %d method(s)\n", refusals.Total(), refusals.MethodCount() );

	for ( int i = 0; i < refusals.MethodCount(); i++ )
	{
		Msg( "[SourceSharp]   %-52s %d\n", refusals.MethodAt( i ), refusals.CountAt( i ) );
	}

	if ( refusals.Total() == 0 )
	{
		Msg( "[SourceSharp]   none -- every refused IBaseClientDLL method is "
			 "still unreached\n" );
	}
}

// Declared in shell_clientdll_refusals.h; defined here because it needs the engine.
void ShellClientDll_RefusalWarning( const char *pMethod, int nTotal )
{
	Warning( "[SourceSharp] REFUSED: %s was called. plan_entity.md 5b argues this "
			 "method is unreachable in this build -- no caller of it was ever "
			 "observed from the engine and its body is entirely "
			 "inside #if defined( TF_CLIENT_DLL ) -- so it is a no-op. If you are "
			 "reading this, that argument was wrong for this method. "
			 "Refusal #%d; cs_cl_refusals for the tally.\n",
			 ( pMethod != NULL ) ? pMethod : "(unnamed)", nTotal );
}

// The engine's IBaseClientDLL, and the environment the managed loader reads the factory
// and globals from.
class CShellClientDLL : public IBaseClientDLL, public IManagedRuntimeEnvironment
{
public:
	CShellClientDLL();

	virtual int Init( CreateInterfaceFn appSystemFactory, CreateInterfaceFn physicsFactory, CGlobalVarsBase *pGlobals );

	virtual void PostInit();
	virtual void Shutdown();

	virtual bool ReplayInit( CreateInterfaceFn fnReplayFactory );
	virtual bool ReplayPostInit();

	virtual void LevelInitPreEntity( const char *pMapName );
	virtual void LevelInitPostEntity();
	virtual void LevelShutdown();

	virtual ClientClass *GetAllClasses();

	virtual int HudVidInit();
	virtual void HudProcessInput( bool bActive );
	virtual void HudUpdate( bool bActive );
	virtual void HudReset();
	virtual void HudText( const char *message );

	virtual void IN_ActivateMouse();
	virtual void IN_DeactivateMouse();
	virtual void IN_Accumulate();
	virtual void IN_ClearStates();
	virtual bool IN_IsKeyDown( const char *name, bool &isdown );
	virtual void IN_OnMouseWheeled( int nDelta );
	virtual int IN_KeyEvent( int eventcode, ButtonCode_t keynum, const char *pszCurrentBinding );
	virtual void IN_SetSampleTime( float frametime );
	virtual void CreateMove( int sequence_number, float input_sample_frametime, bool active );
	virtual void ExtraMouseSample( float frametime, bool active );
	virtual bool WriteUsercmdDeltaToBuffer( bf_write *buf, int from, int to, bool isnewcommand );
	virtual void EncodeUserCmdToBuffer( bf_write &buf, int slot );
	virtual void DecodeUserCmdFromBuffer( bf_read &buf, int slot );

	virtual void View_Render( vrect_t *rect );
	virtual void RenderView( const CViewSetup &view, int nClearFlags, int whatToDraw );
	virtual void View_Fade( ScreenFade_t *pSF );

	virtual void SetCrosshairAngle( const QAngle &angle );

	virtual void InitSprite( CEngineSprite *pSprite, const char *loadname );
	virtual void ShutdownSprite( CEngineSprite *pSprite );

	virtual int GetSpriteSize() const;

	virtual void VoiceStatus( int entindex, qboolean bTalking );

	virtual void InstallStringTableCallback( const char *tableName );

	virtual void FrameStageNotify( ClientFrameStage_t curStage );

	virtual bool DispatchUserMessage( int msg_type, bf_read &msg_data );

	virtual CSaveRestoreData *SaveInit( int size );
	virtual void SaveWriteFields( CSaveRestoreData *, const char *, void *, datamap_t *, typedescription_t *, int );
	virtual void SaveReadFields( CSaveRestoreData *, const char *, void *, datamap_t *, typedescription_t *, int );
	virtual void PreSave( CSaveRestoreData * );
	virtual void Save( CSaveRestoreData * );
	virtual void WriteSaveHeaders( CSaveRestoreData * );
	virtual void ReadRestoreHeaders( CSaveRestoreData * );
	virtual void Restore( CSaveRestoreData *, bool );
	virtual void DispatchOnRestore();
	virtual void WriteSaveGameScreenshot( const char *pFilename );

	// Looks up the localized text for "S(wavname)" tokens and emits the close caption when
	// closecaption is 1.
	virtual void EmitSentenceCloseCaption( char const *tokenstream );
	virtual void EmitCloseCaption( char const *captionname, float duration );

	virtual CStandardRecvProxies *GetStandardRecvProxies();

	virtual bool CanRecordDemo( char *errorMsg, int length ) const;

	virtual void OnDemoRecordStart( char const *pDemoBaseName );
	virtual void OnDemoRecordStop();
	virtual void OnDemoPlaybackStart( char const *pDemoBaseName );
	virtual void OnDemoPlaybackStop();

	virtual bool ShouldDrawDropdownConsole();

	virtual int GetScreenWidth();
	virtual int GetScreenHeight();

	virtual void WriteSaveGameScreenshotOfSize( const char *pFilename, int width, int height,
												bool bCreatePowerOf2Padded /*=false*/, bool bWriteVTF /*=false*/ );

	virtual bool GetPlayerView( CViewSetup &playerView );

	virtual void SetupGameProperties( CUtlVector<XUSER_CONTEXT> &contexts, CUtlVector<XUSER_PROPERTY> &properties );
	virtual uint GetPresenceID( const char *pIDName );
	virtual const char *GetPropertyIdString( const uint id );
	virtual void GetPropertyDisplayString( uint id, uint value, char *pOutput, int nBytes );
	virtual void StartStatsReporting( HANDLE handle, bool bArbitrated );

	virtual void InvalidateMdlCache();

	virtual void ReloadFilesInList( IFileList *pFilesToReload );

	// Let the client handle UI toggle - if this function returns false, the UI will toggle, otherwise it will not.
	virtual bool HandleUiToggle();

	virtual bool ShouldAllowConsole();

	virtual CRenamedRecvTableInfo *GetRenamedRecvTableInfos();

	virtual CMouthInfo *GetClientUIMouthInfo();

	// Notify the client that a file has been received from the game server
	virtual void FileReceived( const char *fileName, unsigned int transferID );

	virtual const char *TranslateEffectForVisionFilter( const char *pchEffectType, const char *pchEffectName );

	virtual void ClientAdjustStartSoundParams( struct StartSoundParams_t &params );

	// Returns true if the disconnect command has been handled by the client
	virtual bool DisconnectAttempt();

public:
	void PrecacheMaterial( const char *pMaterialName );

	virtual bool IsConnectedUserInfoChangeAllowed( IConVar *pCvar );

	virtual bool BHaveChatSuspensionInCurrentMatch();

	virtual void DisplayVoiceUnavailableMessage();

private:
	void UncacheAllMaterials();
	void ResetStringTablePointers();

	CUtlVector<IMaterial *> m_CachedMaterials;

	// The map-specific particle files the previous level read; -1 before the first level.
	int m_nPreviousMapParticleFiles = -1;

public:
	// Public and addressed directly: gpGlobals points at m_DummyGlobals from static
	// initialisation, and an address constant keeps that initialisation constant.
	CGlobalVarsBase m_DummyGlobals;

	// Bodies of stock free functions of nearly the same name, which forward here.
	void RequestCacheMaterials();
	void ProcessCacheMaterials();
	uint64_t ClientStageCrossings() const;
	uint64_t FrameStageNotifyCalls() const;

private:
	bool m_bRequestCacheUsedMaterials;
	uint64_t m_nClientStageCrossings;
	uint64_t m_nFrameStageNotifyCalls;
	CMouthInfo m_ClientUIMouth;
	ClientFrameStage_t m_CurFrameStage;

	CHLVoiceStatusHelper m_VoiceStatusHelper;

	// Members of a file-scope object, so they are constructed during static initialisation
	// and register in Init's ConVar_Register. The server reads them off the wire by name.
	ConVar m_cl_team;
	ConVar m_cl_class;

	// SDL_PumpEvents from the launcher's libSDL2, or NULL. Called during long loads so the
	// window answers the compositor; SDL wants its main thread.
	void ( *m_pfnPumpEvents )() = nullptr;

	void PumpWindowDuringLoad()
	{
		if ( m_pfnPumpEvents && ThreadInMainThread() )
			m_pfnPumpEvents();
	}

public:
	// Read by the refusal console command: zero means every method the engine called was
	// answered.
	const CShellClientDllRefusals &Refusals() const { return m_Refusals; }

	// The particles shell's refused IParticleSystemQuery methods count here too.
	void RefuseExternal( const char *pMethod ) { ShellClientDll_Unreachable( pMethod ); }

private:
	void ShellClientDll_Unreachable( const char *pMethod ) { m_Refusals.Unreachable( pMethod ); }

	CShellClientDllRefusals m_Refusals;

public:
	void *GetEngineInterface( const char *pName, int *pReturnCode ) override
	{
		return m_pEngineFactory( pName, pReturnCode );
	}

	// Engine-owned memory, changing every frame; managed reads through the pointer.
	CGlobalVarsBase &GetGlobalVars() override { return *m_pGlobals; }

private:
	CreateInterfaceFn m_pEngineFactory;
	CGlobalVarsBase *m_pGlobals;

	// One load and one release per DLL.
	IClientRuntimeAdapter *m_pManagedRuntime;

public:
	// Everything the client holds from managed: the root, the runtime, the areas.
	CManagedHoldersClient &Managed() { return m_Managed; }

	CManagedSide &ManagedSide() { return m_ManagedSide; }

	virtual ~CShellClientDLL()
	{
		AssertMsg( ManagedHoldersClient_Empty( m_Managed ),
				   "the client DLL object is destroyed holding a managed-built object" );
	}

private:
	// First, so everything the root builds after it can log.
	CShellLog m_Log;

	CManagedHoldersClient m_Managed = {};
	// Before the managed side, which installs them at load.
	CDiagnosticsHooks m_Diagnostics;

	// The console: this DLL's carriers and the ICvar they reach.
	CCVarRegistry m_CVars;
	CManagedSide m_ManagedSide{ m_Managed, m_Diagnostics, m_CVars };

	// The game events managed listens for: the client's shim events area.
	CGameEvents m_Events{ gameeventmanager, m_Managed.m_Areas.m_pEventsClient, false };
};

// Records a refusal in the boundary probe table and the refusal tally. A macro so
// SS_BOUNDARY_CALLER() captures the engine's return address, not a helper's.
#define SS_CLIENTDLL_UNREACHABLE( pszMethod ) ShellClientDll_Unreachable( ( pszMethod ) )

CShellClientDLL g_ShellClientDLL;
IBaseClientDLL *clientdll = &g_ShellClientDLL;

void ShellClientDll_RefuseExternal( const char *pMethod )
{
	g_ShellClientDLL.RefuseExternal( pMethod );
}

CManagedHoldersClient &ShellClientDll_Managed()
{
	return g_ShellClientDLL.Managed();
}

CManagedSide &ShellClientDll_ManagedSide()
{
	return g_ShellClientDLL.ManagedSide();
}

unsigned int SourceSharp_PlayerSyncGeneration()
{
	return ShellClientDll_ManagedSide().Generation();
}

// The client's loader entry points (managed_runtime_client.cpp and its adapter).
bool SourceSharp_LoadManagedSide()
{
	return g_ShellClientDLL.ManagedSide().Load( engine->GetGameDirectory() );
}

void SourceSharp_UnloadManagedSide()
{
	g_ShellClientDLL.ManagedSide().Unload();
}

// Never NULL during DLL initialisation; Init points it at the engine's globals. Other TUs
// read it from their static initialisers, so it must stay an address constant.
CGlobalVarsBase *gpGlobals = &g_ShellClientDLL.m_DummyGlobals;

// The engine area's slot: the globals gpGlobals points at, for managed to read.
void *SourceSharp_GetGlobalVarsPtr()
{
	return gpGlobals;
}

// Stock's names: the string-table callbacks request the sweep and FRAME_NET_UPDATE_END
// runs it.
void RequestCacheUsedMaterials()
{
	g_ShellClientDLL.RequestCacheMaterials();
}

void ProcessCacheUsedMaterials()
{
	g_ShellClientDLL.ProcessCacheMaterials();
}

void CShellClientDLL::RequestCacheMaterials()
{
	m_bRequestCacheUsedMaterials = true;
}

void CShellClientDLL::ProcessCacheMaterials()
{
	if ( !m_bRequestCacheUsedMaterials )
		return;

	m_bRequestCacheUsedMaterials = false;
	if ( materials )
	{
		materials->CacheUsedMaterials();
	}
}

EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CShellClientDLL, IBaseClientDLL, CLIENT_DLL_INTERFACE_VERSION, g_ShellClientDLL );

void PrecacheMaterial( const char *pMaterialName )
{
	g_ShellClientDLL.PrecacheMaterial( pMaterialName );
}

int GetParticleSystemIndex( const char *pParticleSystemName )
{
	if ( pParticleSystemName )
	{
		int nIndex = g_pStringTableParticleEffectNames->FindStringIndex( pParticleSystemName );
		if ( nIndex != INVALID_STRING_INDEX )
			return nIndex;
		DevWarning( "Client: Missing precache for particle system \"%s\"!\n", pParticleSystemName );
	}

	// The invalid string index.
	return 0;
}

const char *GetParticleSystemNameFromIndex( int nIndex )
{
	if ( nIndex < g_pStringTableParticleEffectNames->GetMaxStrings() )
		return g_pStringTableParticleEffectNames->GetString( nIndex );
	return "error";
}

CShellClientDLL::CShellClientDLL()
	: m_DummyGlobals( true )
	, m_bRequestCacheUsedMaterials( false )
	, m_nClientStageCrossings( 0 )
	, m_nFrameStageNotifyCalls( 0 )
	, m_CurFrameStage( FRAME_UNDEFINED )
	, m_cl_team( "cl_team", "default", FCVAR_USERINFO | FCVAR_ARCHIVE, "Default team when joining a game" )
	, m_cl_class( "cl_class", "default", FCVAR_USERINFO | FCVAR_ARCHIVE, "Default class when joining a game" )
	, m_pEngineFactory( NULL )
	, m_pGlobals( NULL )
	, m_pManagedRuntime( NULL )
{
	g_bLevelInitialized = false;
}

int CShellClientDLL::Init( CreateInterfaceFn appSystemFactory, CreateInterfaceFn physicsFactory,
						   CGlobalVarsBase *pGlobals )
{
	gpGlobals = pGlobals;

	// The same two as members, served through IManagedRuntimeEnvironment.
	m_pEngineFactory = appSystemFactory;
	m_pGlobals = pGlobals;

	ConnectTier1Libraries( &appSystemFactory, 1 );
	m_CVars.Connect( g_pCVar );
	Tier0_ConnectConsole( g_pCVar );
	ShimConsole().m_pCVars = &m_CVars;
	SourceSharp_ConnectTier2( appSystemFactory );
	SourceSharp_ConnectTier3( appSystemFactory );

	// Keep the client from writing files into arbitrary locations.
	if ( g_pFullFileSystem )
	{
		g_pFullFileSystem->SetWriteProtectionEnable( true );
	}

	if ( ( engine = (IVEngineClient *)appSystemFactory( VENGINE_CLIENT_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( modelrender = (IVModelRender *)appSystemFactory( VENGINE_HUDMODEL_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( effects = (IVEfx *)appSystemFactory( VENGINE_EFFECTS_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( enginetrace = (IEngineTrace *)appSystemFactory( INTERFACEVERSION_ENGINETRACE_CLIENT, NULL ) ) == NULL )
		return false;
	if ( ( render = (IVRenderView *)appSystemFactory( VENGINE_RENDERVIEW_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( debugoverlay = (IVDebugOverlay *)appSystemFactory( VDEBUG_OVERLAY_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( !mdlcache )
		return false;
	if ( ( modelinfo = (IVModelInfoClient *)appSystemFactory( VMODELINFO_CLIENT_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( enginevgui = (IEngineVGui *)appSystemFactory( VENGINE_VGUI_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( networkstringtable = (INetworkStringTableContainer *)appSystemFactory(
			   INTERFACENAME_NETWORKSTRINGTABLECLIENT, NULL ) ) == NULL )
		return false;
	if ( ( ::partition = (ISpatialPartition *)appSystemFactory( INTERFACEVERSION_SPATIALPARTITION, NULL ) ) == NULL )
		return false;
	if ( ( shadowmgr = (IShadowMgr *)appSystemFactory( ENGINE_SHADOWMGR_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( staticpropmgr = (IStaticPropMgrClient *)appSystemFactory( INTERFACEVERSION_STATICPROPMGR_CLIENT, NULL ) ) ==
		 NULL )
		return false;
	if ( ( enginesound = (IEngineSound *)appSystemFactory( IENGINESOUND_CLIENT_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( filesystem = (IFileSystem *)appSystemFactory( FILESYSTEM_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;
	if ( ( random = (IUniformRandomStream *)appSystemFactory( VENGINE_CLIENT_RANDOM_INTERFACE_VERSION, NULL ) ) ==
		 NULL )
		return false;
	if ( ( gameeventmanager = (IGameEventManager2 *)appSystemFactory( INTERFACEVERSION_GAMEEVENTSMANAGER2, NULL ) ) ==
		 NULL )
		return false;
	if ( ( soundemitterbase =
			   (ISoundEmitterSystemBase *)appSystemFactory( SOUNDEMITTERSYSTEM_INTERFACE_VERSION, NULL ) ) == NULL )
		return false;

	if ( !g_pMatSystemSurface )
		return false;

	factorylist_t factories;
	factories.appSystemFactory = appSystemFactory;
	factories.physicsFactory = physicsFactory;
	FactoryList_Store( factories );

	// The server DLL connects too; the sound emitter system handles that.
	if ( !soundemitterbase->Connect( appSystemFactory ) )
	{
		return false;
	}

	if ( ss::CommandLineHas( "-textmode" ) )
		g_bTextMode = true;

	materials_stub = (IMaterialSystemStub *)appSystemFactory( MATERIAL_SYSTEM_STUB_INTERFACE_VERSION, NULL );

	if ( !g_pMaterialSystemHardwareConfig )
		return false;

	m_CVars.Register( FCVAR_CLIENTDLL );

	if ( !ShellParticleMgr_Init( materials ) )
		return false;

	// A listen server's spawn reloads only what the map paks can have changed. Failing to
	// install falls back to the stock full reload.
	bool SourceSharp_InstallMapMaterialReload( IFileSystem * pFileSystem, IMaterialSystem * pMaterials );
	SourceSharp_InstallMapMaterialReload( filesystem, materials );

	// RTLD_NOLOAD: the launcher's SDL or nothing. Windows has no SDL to find.
#ifndef _WIN32
	if ( void *hSDL = dlopen( "libSDL2-2.0.so.0", RTLD_NOW | RTLD_NOLOAD ) )
		m_pfnPumpEvents = (void ( * )())dlsym( hSDL, "SDL_PumpEvents" );
#endif
	Msg( "[SourceSharp] load pump: %s\n",
		 m_pfnPumpEvents ? "SDL_PumpEvents" : "none (the window is pumped only between frames)" );

	if ( !VGui_Startup( appSystemFactory ) )
		return false;

	Assert( g_pMaterialSystem && g_pMatSystemSurface && g_pMaterialSystemHardwareConfig );

	modemanager->Init();

	g_pClientMode->InitViewport();

	// Before the HUD: gHUD.Init() hooks the user messages managed declares. The CLR comes up
	// before SourceSharp_ClientSystemsInit() walks the client systems.
	ShimConsole().m_ppCommands = &m_Managed.m_Areas.m_pCommandsClient;
	ShimClientAreasOfRoot().m_pEvents = &m_Events;
	m_pManagedRuntime = LoadManagedRuntime( this );

	if ( m_pManagedRuntime == NULL )
		return false;

	SourceSharp_SetClientRuntime( m_pManagedRuntime );

	// The particle manifest read CParticleMgr::Init deferred until managed was up.
	void SourceSharp_ParticleManifestRuntimeUp();
	SourceSharp_ParticleManifestRuntimeUp();

	gHUD.Init();

	g_pClientMode->Init();

	if ( !SourceSharp_ClientSystemsInit() )
		return false;

	g_pClientMode->Enable();

	if ( !view )
	{
		view = ShellViewRender();
	}

	view->Init();

	input->Init_All();

	VGui_CreateGlobalPanels();

	InitSmokeFogOverlay();

	ClientVoiceMgr_Init();

	{
		vgui::VPANEL parent = enginevgui->GetPanel( PANEL_CLIENTDLL );
		GetClientVoiceMgr()->Init( &m_VoiceStatusHelper, parent );
	}

	if ( !PhysicsDLLInit( physicsFactory ) )
		return false;

	ClientWorldFactoryInit();

#if defined( WIN32 )
	ConnectHaptics( appSystemFactory );
#endif

	return true;
}

bool CShellClientDLL::ReplayInit( CreateInterfaceFn fnReplayFactory )
{
	return false;
}

bool CShellClientDLL::ReplayPostInit()
{
	return false;
}

// Called after the client and server DLLs are loaded and all systems initialized.
void CShellClientDLL::PostInit()
{
	SourceSharp_ClientSystemsPostInit();

	// Managed sets the r_lightmap_bicubic default from the adapter's max DX support level, -1
	// with no material system.
	IManagedRuntimeClient *pRuntime = ManagedRuntimeClient();
	if ( !pRuntime )
		return;

	int nMaxDxLevel = -1;
	if ( materials )
	{
		MaterialAdapterInfo_t info{};
		materials->GetDisplayAdapterInfo( materials->GetCurrentAdapter(), info );
		nMaxDxLevel = info.m_nMaxDXSupportLevel;
	}
	pRuntime->PostInit( nMaxDxLevel );
}

void CShellClientDLL::Shutdown()
{
	// First: the hooked slots name functions in this module.
	void SourceSharp_UninstallMapMaterialReload();
	SourceSharp_UninstallMapMaterialReload();

	// Printed on the way out so refusals from an unwatched session reach the log.
	ShellClientDll_PrintRefusals( m_Refusals );

	ClientWorldFactoryShutdown();

	ClientVoiceMgr_Shutdown();

	g_pClientMode->Disable();
	g_pClientMode->Shutdown();

	input->Shutdown_All();
	TermSmokeFogOverlay();
	view->Shutdown();
	g_pParticleSystemMgr->UncacheAllParticleSystems();
	UncacheAllMaterials();

	// The engine calls Shutdown after a failed Init, when the runtime may be absent.
	// Unhooked even when the runtime failed after subscribing: the engine keeps its
	// listener list for the whole process.
	m_Events.StopListening();

	if ( m_pManagedRuntime != NULL )
	{
		m_pManagedRuntime->OnSystemStop();
	}

	SourceSharp_ClientSystemsShutdown();

	// After the walk, which cleared the row's copy of the pointer. Null-safe.
	UnloadManagedRuntime( m_pManagedRuntime );
	m_pManagedRuntime = NULL;

	gHUD.Shutdown();
	VGui_Shutdown();

	ShellParticleMgr_Term();

	SourceSharp_DisconnectTier2();
	m_CVars.Unregister();

	// After the unregister, while the console is still connected.
	ShimConsole().ReleaseObjects();
	ShimConsole().m_ppCommands = NULL;
	ShimConsole().m_pCVars = NULL;
	m_CVars.Connect( NULL );
	Tier0_ConnectConsole( NULL );
	DisconnectTier1Libraries();

	ShimClientAreasOfRoot().m_pEvents = NULL;
	gameeventmanager = NULL;

#if defined( WIN32 )
	DisconnectHaptics();
#endif
}

// Called at startup and on every vid_mode change so the HUD can reinitialize.
int CShellClientDLL::HudVidInit()
{
	gHUD.VidInit();

	GetClientVoiceMgr()->VidInit();

	return 1;
}

// Lets the client filter input messages before the move record is sent.
void CShellClientDLL::HudProcessInput( bool bActive )
{
	g_pClientMode->ProcessInput( bActive );
}

void CShellClientDLL::HudUpdate( bool bActive )
{
	float frametime = gpGlobals->frametime;

	GetClientVoiceMgr()->Frame( frametime );

	gHUD.UpdateHud( bActive );

	SourceSharp_ClientSystemsUpdate( frametime );
}

void CShellClientDLL::HudReset()
{
	gHUD.VidInit();
	PhysicsReset();
}

void CShellClientDLL::HudText( const char *message ) {}

bool CShellClientDLL::ShouldDrawDropdownConsole()
{
	return true;
}

ClientClass *CShellClientDLL::GetAllClasses()
{
	return g_pClientClassHead;
}

void CShellClientDLL::IN_ActivateMouse()
{
	input->ActivateMouse();
}

void CShellClientDLL::IN_DeactivateMouse()
{
	input->DeactivateMouse();
}

void CShellClientDLL::IN_Accumulate()
{
	input->AccumulateMouse();
}

void CShellClientDLL::IN_ClearStates()
{
	input->ClearStates();
}

bool CShellClientDLL::IN_IsKeyDown( const char *name, bool &isdown )
{
	// The button's state is managed's.
	IManagedInputClient *pInput = ManagedInputClient();
	int32_t bDown = 0;
	if ( !pInput || !pInput->InputIsKeyDown( name, &bDown ) )
		return false;

	isdown = bDown != 0;
	return true;
}

void CShellClientDLL::IN_OnMouseWheeled( int nDelta ) {}

int CShellClientDLL::IN_KeyEvent( int eventcode, ButtonCode_t keynum, const char *pszCurrentBinding )
{
	return input->KeyEvent( eventcode, keynum, pszCurrentBinding );
}

void CShellClientDLL::ExtraMouseSample( float frametime, bool active )
{
	// Compiled out in release builds, so only a debug build checks it.
	Assert( EntityAbs_AreRecomputationsEnabled() );
	Assert( EntityAbs_AreQueriesValid() );

	MDLCACHE_CRITICAL_SECTION();
	input->ExtraMouseSample( frametime, active );
}

void CShellClientDLL::IN_SetSampleTime( float frametime )
{
	input->Joystick_SetSampleTime( frametime );
	input->IN_SetSampleTime( frametime );
}

// Fills in the usercmd from the current view angles and inputs.
void CShellClientDLL::CreateMove( int sequence_number, float input_sample_frametime, bool active )
{
	// Compiled out in release builds, so only a debug build checks it.
	Assert( EntityAbs_AreRecomputationsEnabled() );
	Assert( EntityAbs_AreQueriesValid() );

	MDLCACHE_CRITICAL_SECTION();
	input->CreateMove( sequence_number, input_sample_frametime, active );
}

bool CShellClientDLL::WriteUsercmdDeltaToBuffer( bf_write *buf, int from, int to, bool isnewcommand )
{
	return input->WriteUsercmdDeltaToBuffer( buf, from, to, isnewcommand );
}

void CShellClientDLL::EncodeUserCmdToBuffer( bf_write &buf, int slot )
{
	input->EncodeUserCmdToBuffer( buf, slot );
}

void CShellClientDLL::DecodeUserCmdFromBuffer( bf_read &buf, int slot )
{
	input->DecodeUserCmdFromBuffer( buf, slot );
}

void CShellClientDLL::View_Render( vrect_t *rect )
{
	// UNDONE: This gets hit at startup sometimes, investigate - will cause NaNs in calcs inside Render()
	if ( rect->width == 0 || rect->height == 0 )
		return;

	SourceSharp_ViewRender( rect );
}

bool CShellClientDLL::GetPlayerView( CViewSetup &playerView )
{
	playerView = *view->GetPlayerViewSetup();
	return true;
}

// Matchmaking and rich presence: the engine may call these, and stock's PC bodies were
// empty, so they are counted refusals with stock's return values.
void CShellClientDLL::SetupGameProperties( CUtlVector<XUSER_CONTEXT> &contexts, CUtlVector<XUSER_PROPERTY> &properties )
{
	SS_CLIENTDLL_UNREACHABLE( "IBaseClientDLL::SetupGameProperties" );
}

uint CShellClientDLL::GetPresenceID( const char *pIDName )
{
	SS_CLIENTDLL_UNREACHABLE( "IBaseClientDLL::GetPresenceID" );
	return 0;
}

const char *CShellClientDLL::GetPropertyIdString( const uint id )
{
	SS_CLIENTDLL_UNREACHABLE( "IBaseClientDLL::GetPropertyIdString" );
	return NULL;
}

void CShellClientDLL::GetPropertyDisplayString( uint id, uint value, char *pOutput, int nBytes )
{
	SS_CLIENTDLL_UNREACHABLE( "IBaseClientDLL::GetPropertyDisplayString" );
}

void CShellClientDLL::StartStatsReporting( HANDLE handle, bool bArbitrated )
{
	SS_CLIENTDLL_UNREACHABLE( "IBaseClientDLL::StartStatsReporting" );
}

// The SDK half of the mdl cache flush; managed flushes its own entities. Kept here because
// it frees m_pStudioHdr.
int32_t SourceSharp_ClientEntitiesInvalidateMdlCacheAll()
{
	int32_t nInvalidated = 0;

	// Declared rather than included: shell_standalone_client.h is long.
	int32_t SourceSharp_StandaloneClientsInvalidateBoneCaches();

	return nInvalidated + SourceSharp_StandaloneClientsInvalidateBoneCaches();
}

void CShellClientDLL::InvalidateMdlCache()
{
	if ( IManagedRuntimeClient *pRuntime = ManagedRuntimeClient() )
		pRuntime->ClientInvalidateMdlCache();
}

// The managed shakes and fades start over.
static void ResetManagedViewEffects()
{
	if ( IManagedRenderClient *p = ManagedRenderClient() )
		p->ViewEffectsReset( 0 );
}

// ResetToneMapping( 1.0 )'s native half.
static void ResetToneMappingScale()
{
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->ResetToneMappingScale( 1.0f );
}

// A screen fade, managed's: the engine's View_Fade and the map's start-dark fade.
void SourceSharp_ViewFade( const ScreenFade_t &sf )
{
	if ( IManagedRenderClient *p = ManagedRenderClient() )
		p->ViewEffectsFade( sf.duration, sf.holdTime, sf.fadeFlags, sf.r, sf.g, sf.b, sf.a, gpGlobals->curtime );
}

void CShellClientDLL::View_Fade( ScreenFade_t *pSF )
{
	if ( pSF != NULL )
		SourceSharp_ViewFade( *pSF );
}

void CShellClientDLL::LevelInitPreEntity( char const *pMapName )
{
	// Managed decides whether this level re-reads every particle definition file or only
	// re-precaches; with no runtime, the stock re-read.
	PumpWindowDuringLoad();
	int PrecacheDefinedParticleSystems();
	IManagedRuntimeClient *pRuntime = ManagedRuntimeClient();
	if ( !pRuntime || pRuntime->ParticlesNeedFullReload( m_nPreviousMapParticleFiles ) )
		ReloadParticleEffects();
	else
		PrecacheDefinedParticleSystems();
	PumpWindowDuringLoad();

	if ( g_bLevelInitialized )
		return;
	g_bLevelInitialized = true;

	input->LevelInit();

	ResetManagedViewEffects();

	// How many files this read is what the next level's reload decision needs.
	int ParseParticleEffectsMapFiles( const char *pMapName, bool bLoadSheets );
	m_nPreviousMapParticleFiles = ParseParticleEffectsMapFiles( pMapName, true );

	modemanager->LevelInit( pMapName );
	ShellParticleMgr_LevelInit();

	clienteffects->Flush();
	view->LevelInit();
	tempents->LevelInit();
	ResetToneMappingScale();

	SourceSharp_ClientSystemsLevelInitPreEntity();

	ResetWindspeed();

	// Whether this level predicts is managed's; once per level, past the gate above.
	if ( pRuntime )
		pRuntime->LevelInitPreEntity();

	gHUD.LevelInit();
}

void CShellClientDLL::LevelInitPostEntity()
{
	PumpWindowDuringLoad();
	SourceSharp_ClientSystemsLevelInitPostEntity();
	internalCenterPrint->Clear();
}

void CShellClientDLL::ResetStringTablePointers()
{
	g_pStringTableParticleEffectNames = NULL;
	g_StringTableEffectDispatch = NULL;
	g_StringTableVguiScreen = NULL;
	g_pStringTableMaterials = NULL;
	g_pStringTableInfoPanel = NULL;
	g_pStringTableClientSideChoreoScenes = NULL;
	g_pStringTableServerMapCycle = NULL;
}

void CShellClientDLL::LevelShutdown()
{
	if ( !g_bLevelInitialized )
		return;

	g_bLevelInitialized = false;

	EntityAbs_SetRecomputationsEnabled( false );

	SourceSharp_ClientSystemsLevelShutdownPreEntity();

	modemanager->LevelShutdown();

	// Temp entities go before the entity list so the te_* can clean up.
	tempents->LevelShutdown();

	// Shells, then the world, then the post-entity system shutdown: the server's order, and
	// a shell's destructor can reach the world. Both must precede the subsystem reset below,
	// which shells hold handles into.
	int SourceSharp_StandaloneClientsLevelShutdown();

	const int nShells = SourceSharp_StandaloneClientsLevelShutdown();
	const bool bWorld = SourceSharp_ClientWorldLevelShutdown();

	Msg( "[SourceSharp] client level shutdown: destroyed %d standalone shell(s), "
		 "world %s\n",
		 nShells, bWorld ? "released" : "was not bound" );

	SourceSharp_ClientSystemsLevelShutdownPostEntity();

	view->LevelShutdown();
	beams->ClearBeams();
	// The new-style effects removed, then the particle materials released.
	ShellNewParticles_RemoveAll();
	ShellParticleMgr_LevelShutdown();

	StopAllRumbleEffects();

	gHUD.LevelShutdown();

	internalCenterPrint->Clear();

	messagechars->Clear();

	g_pParticleSystemMgr->UncacheAllParticleSystems();
	UncacheAllMaterials();

	// String tables are cleared on disconnect, so reset the global pointers.
	ResetStringTablePointers();
}

// No crosshair element exists, so the autoaim offset goes nowhere; the engine calls it.
void CShellClientDLL::SetCrosshairAngle( const QAngle &angle ) {}

// Initializes a sprite from its .spr.
void CShellClientDLL::InitSprite( CEngineSprite *pSprite, const char *loadname )
{
	if ( pSprite )
	{
		pSprite->Init( loadname );
	}
}

void CShellClientDLL::ShutdownSprite( CEngineSprite *pSprite )
{
	if ( pSprite )
	{
		pSprite->Shutdown();
	}
}

// Tells the engine how much space to allocate for sprite objects.
int CShellClientDLL::GetSpriteSize() const
{
	return sizeof( CEngineSprite );
}

void CShellClientDLL::VoiceStatus( int entindex, qboolean bTalking )
{
	GetClientVoiceMgr()->UpdateSpeakerStatus( entindex, !!bTalking );

	// The managed UI's voice panel shows who is speaking.
	if ( IManagedUiClient *pUi = ManagedUiClient() )
		pUi->VoiceStatus( entindex, bTalking ? 1 : 0 );
}

void OnMaterialStringTableChanged( void *object, INetworkStringTable *stringTable, int stringNumber,
								   const char *newString, void const *newData )
{
	g_ShellClientDLL.PrecacheMaterial( newString );
	RequestCacheUsedMaterials();
}

void OnParticleSystemStringTableChanged( void *object, INetworkStringTable *stringTable, int stringNumber,
										 const char *newString, void const *newData )
{
	g_pParticleSystemMgr->PrecacheParticleSystem( newString );
	RequestCacheUsedMaterials();
}

void OnVguiScreenTableChanged( void *object, INetworkStringTable *stringTable, int stringNumber, const char *newString,
							   void const *newData )
{
	// No vgui screen class is compiled into this client, so there is nothing to precache.
}

// Preloads the string on the client.
void OnSceneStringTableChanged( void *object, INetworkStringTable *stringTable, int stringNumber, const char *newString,
								void const *newData )
{
}

// The table definition has been parsed but no data added yet.
void CShellClientDLL::InstallStringTableCallback( const char *tableName )
{
	if ( !Q_strcasecmp( tableName, "VguiScreen" ) )
	{
		g_StringTableVguiScreen = networkstringtable->FindTable( tableName );

		g_StringTableVguiScreen->SetStringChangedCallback( NULL, OnVguiScreenTableChanged );
	}
	else if ( !Q_strcasecmp( tableName, "Materials" ) )
	{
		g_pStringTableMaterials = networkstringtable->FindTable( tableName );

		g_pStringTableMaterials->SetStringChangedCallback( NULL, OnMaterialStringTableChanged );
	}
	else if ( !Q_strcasecmp( tableName, "EffectDispatch" ) )
	{
		g_StringTableEffectDispatch = networkstringtable->FindTable( tableName );
	}
	else if ( !Q_strcasecmp( tableName, "InfoPanel" ) )
	{
		g_pStringTableInfoPanel = networkstringtable->FindTable( tableName );
	}
	else if ( !Q_strcasecmp( tableName, "Scenes" ) )
	{
		g_pStringTableClientSideChoreoScenes = networkstringtable->FindTable( tableName );
		g_pStringTableClientSideChoreoScenes->SetStringChangedCallback( NULL, OnSceneStringTableChanged );
	}
	else if ( !Q_strcasecmp( tableName, "ParticleEffectNames" ) )
	{
		g_pStringTableParticleEffectNames = networkstringtable->FindTable( tableName );
		networkstringtable->SetAllowClientSideAddString( g_pStringTableParticleEffectNames, true );
		g_pStringTableParticleEffectNames->SetStringChangedCallback( NULL, OnParticleSystemStringTableChanged );
	}
	else if ( !Q_strcasecmp( tableName, "ServerMapCycle" ) )
	{
		g_pStringTableServerMapCycle = networkstringtable->FindTable( tableName );
	}

	InstallStringTableCallback_GameRules();
}

void CShellClientDLL::PrecacheMaterial( const char *pMaterialName )
{
	Assert( pMaterialName );

	int nLen = Q_strlen( pMaterialName );
	char *pTempBuf = (char *)stackalloc( nLen + 1 );
	memcpy( pTempBuf, pMaterialName, nLen + 1 );
	char *pFound = Q_strstr( pTempBuf, ".vmt\0" );
	if ( pFound )
	{
		*pFound = 0;
	}

	IMaterial *pMaterial = materials->FindMaterial( pTempBuf, TEXTURE_GROUP_PRECACHED );
	if ( !IsErrorMaterial( pMaterial ) )
	{
		pMaterial->IncrementReferenceCount();
		m_CachedMaterials.AddToTail( pMaterial );
	}
	else
	{
		if ( IsOSX() )
		{
			printf( "\n ##### CShellClientDLL::PrecacheMaterial could not find material %s (%s)", pMaterialName,
					pTempBuf );
		}
	}
}

void CShellClientDLL::UncacheAllMaterials()
{
	for ( int i = m_CachedMaterials.Count(); --i >= 0; )
	{
		m_CachedMaterials[i]->DecrementReferenceCount();
	}
	m_CachedMaterials.RemoveAll();
}

// One forward of the user message to managed: the payload is copied whole into one
// length-prefixed BitBlock; one too large answers its prefix as minus the size needed.
// False outside a managed load.
bool CShellClientDLL::DispatchUserMessage( int msg_type, bf_read &msg_data )
{
	IManagedUserMessagesClient *pMessages = ManagedUserMessagesClient();
	if ( !pMessages )
		return false;
	uint8_t block[ss::kBitBlockHeaderBytes + kMaxUserMessageBlockBytes];
	const int32_t nBits = ss::BitBlockFromEngine( msg_data, block, sizeof( block ) );
	if ( nBits < 0 )
		memcpy( block, &nBits, sizeof( nBits ) );
	return pMessages->DispatchUserMessage( msg_type, block, sizeof( block ) );
}

// The PVS-notify walks. No renderable in this build answers a non-NULL
// GetPVSNotifyInterface(), so the list is always empty.
void UpdateClientRenderableInPVSStatus() {}

void UpdatePVSNotifiers() {}

void OnRenderStart()
{
	MDLCACHE_CRITICAL_SECTION();
	MDLCACHE_COARSE_LOCK();

	::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, true );
	EntityAbs_SetQueriesValid( false );

	{
		EntityAbs_SetQueriesValid( true );
		EntityAbs_SetRecomputationsEnabled( true );
	}

	// Camera simulation before OnRenderStart, which uses it.
	input->CAM_Think();

	// Places the player, the view models and parent entities so attachment points are right.
	view->OnRenderStart();

	RopeManager()->OnRenderStart();

	::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, false );

	// The world's deferred OnDataChanged: it must see a complete net update.
	if ( C_World *pWorld = GetClientWorldEntity() )
	{
		pWorld->DeliverPendingDataChanged();
	}

	// Entities can change the overlay alpha in their thinks.
	g_SmokeFogOverlayAlpha = 0;

	PhysicsSimulate();

	{
		engine->FireEvents();

		tempents->Update();

		beams->UpdateTempEntBeams();
	}

	{
		FPExceptionEnabler enableExceptions;
		// The old-style effects, then the new-style ones, as UpdateAllEffects ordered them.
		ShellParticles_Frame( gpGlobals->frametime );
		ShellNewParticles_Frame( gpGlobals->frametime );
	}
}

void OnRenderEnd()
{
	UpdatePVSNotifiers();
}

// The client frame's one crossing: every stage, unconditionally, before the switch, so
// managed hears a stage before the native work it announces. The stage number is the
// engine's ClientFrameStage_t, passed through unchanged.

// How many times the crossing below was made; read by ss_cl_stage_crossings.
uint64_t SourceSharp_ClientStageCrossings()
{
	return g_ShellClientDLL.ClientStageCrossings();
}

uint64_t CShellClientDLL::ClientStageCrossings() const
{
	return m_nClientStageCrossings;
}

// How many times the engine called FrameStageNotify.
uint64_t CShellClientDLL::FrameStageNotifyCalls() const
{
	return m_nFrameStageNotifyCalls;
}

uint64_t SourceSharp_FrameStageNotifyCalls()
{
	return g_ShellClientDLL.FrameStageNotifyCalls();
}

void CShellClientDLL::FrameStageNotify( ClientFrameStage_t curStage )
{
	++m_nFrameStageNotifyCalls;
	m_CurFrameStage = curStage;

	// NULL before the first load and after the last unload.
	if ( IManagedRuntimeClient *pRuntime = ManagedRuntimeClient() )
	{
		++m_nClientStageCrossings;
		pRuntime->ClientStage( (int32_t)curStage );
	}

	switch ( curStage )
	{
		default:
			break;

		case FRAME_RENDER_START:
		{
			OnRenderStart();
		}
		break;

		case FRAME_RENDER_END:
		{
			OnRenderEnd();
		}
		break;

		case FRAME_NET_UPDATE_START:
		{
			EntityAbs_SetRecomputationsEnabled( false );
			EntityAbs_SetQueriesValid( false );
			::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, true );
		}
		break;
		case FRAME_NET_UPDATE_END:
		{
			ProcessCacheUsedMaterials();

			EntityAbs_SetRecomputationsEnabled( true );
			EntityAbs_SetQueriesValid( true );
			::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, false );
		}
		break;
		case FRAME_NET_UPDATE_POSTDATAUPDATE_START:
			break;
		case FRAME_NET_UPDATE_POSTDATAUPDATE_END:
		{
			prediction->PostEntityPacketReceived();
		}
		break;
		case FRAME_START:
		{
			SetFXCreationAllowed( true );
		}
		break;
	}
}

// Save/restore hooks. The engine asks SaveInit for the client state buffer first and
// abandons the save on NULL, so the three that move bytes forward to the managed save
// codec. The client entity block is empty, which the engine accepts.
CSaveRestoreData *CShellClientDLL::SaveInit( int size )
{
	IManagedSaveRestoreClient *pArea = ManagedSaveRestoreClient();
	CSaveRestoreData *pSaveData =
		pArea ? static_cast<CSaveRestoreData *>( pArea->SaveInit( size, gpGlobals->curtime ) ) : NULL;

	if ( pSaveData == NULL )
	{
		// The engine abandons the save here silently, so say so.
		Warning( "[SourceSharp] client SaveInit is answering NULL: the engine will "
				 "abandon the client state file and, with it, the whole save, "
				 "printing nothing\n" );
	}

	return pSaveData;
}

// pMap is unused, as stock's. sizeof( typedescription_t ) is passed because the struct
// holds a member-function pointer whose width is the C++ ABI's.
void CShellClientDLL::SaveWriteFields( CSaveRestoreData *pSaveData, const char *pname, void *pBaseData, datamap_t *pMap,
									   typedescription_t *pFields, int fieldCount )
{
	if ( IManagedSaveRestoreClient *pArea = ManagedSaveRestoreClient() )
		pArea->SaveWriteFields( pSaveData, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ),
								fieldCount );
}

void CShellClientDLL::SaveReadFields( CSaveRestoreData *pSaveData, const char *pname, void *pBaseData, datamap_t *pMap,
									  typedescription_t *pFields, int fieldCount )
{
	if ( IManagedSaveRestoreClient *pArea = ManagedSaveRestoreClient() )
		pArea->SaveReadFields( pSaveData, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ), fieldCount );
}

// The other six move no bytes: entities are not saved through a datamap. A no-op is
// symmetric across save and load.
void CShellClientDLL::PreSave( CSaveRestoreData *s ) {}

void CShellClientDLL::Save( CSaveRestoreData *s ) {}

void CShellClientDLL::WriteSaveHeaders( CSaveRestoreData *s ) {}

void CShellClientDLL::ReadRestoreHeaders( CSaveRestoreData *s ) {}

void CShellClientDLL::Restore( CSaveRestoreData *s, bool b ) {}

// The engine calls it; a managed entity's restore is managed's.
void CShellClientDLL::DispatchOnRestore() {}

void CShellClientDLL::WriteSaveGameScreenshot( const char *pFilename )
{
	view->WriteSaveGameScreenshot( pFilename );
}

// The engine's close-caption slots; the captions this client shows are managed's.
void CShellClientDLL::EmitSentenceCloseCaption( char const *tokenstream ) {}

void CShellClientDLL::EmitCloseCaption( char const *captionname, float duration ) {}

CStandardRecvProxies *CShellClientDLL::GetStandardRecvProxies()
{
	return &g_StandardRecvProxies;
}

bool CShellClientDLL::CanRecordDemo( char *errorMsg, int length ) const
{
	if ( GetClientModeNormal() )
	{
		return GetClientModeNormal()->CanRecordDemo( errorMsg, length );
	}

	return true;
}

void CShellClientDLL::OnDemoRecordStart( char const *pDemoBaseName )
{
	if ( GetClientModeNormal() )
	{
		return GetClientModeNormal()->OnDemoRecordStart( pDemoBaseName );
	}
}

void CShellClientDLL::OnDemoRecordStop()
{
	if ( GetClientModeNormal() )
	{
		return GetClientModeNormal()->OnDemoRecordStop();
	}
}

void CShellClientDLL::OnDemoPlaybackStart( char const *pDemoBaseName ) {}

void CShellClientDLL::OnDemoPlaybackStop() {}

int CShellClientDLL::GetScreenWidth()
{
	return ScreenWidth();
}

int CShellClientDLL::GetScreenHeight()
{
	return ScreenHeight();
}

void CShellClientDLL::WriteSaveGameScreenshotOfSize( const char *pFilename, int width, int height,
													 bool bCreatePowerOf2Padded /*=false*/, bool bWriteVTF /*=false*/ )
{
	view->WriteSaveGameScreenshotOfSize( pFilename, width, height, bCreatePowerOf2Padded, bWriteVTF );
}

// See RenderViewInfo_t
void CShellClientDLL::RenderView( const CViewSetup &setup, int nClearFlags, int whatToDraw )
{
	view->RenderView( setup, nClearFlags, whatToDraw );
}

void ReloadSoundEntriesInList( IFileList *pFilesToReload );

// For sv_pure: the filesystem works out which files to reload.
void CShellClientDLL::ReloadFilesInList( IFileList *pFilesToReload )
{
	ReloadSoundEntriesInList( pFilesToReload );
}

bool CShellClientDLL::HandleUiToggle()
{
	return false;
}

bool CShellClientDLL::ShouldAllowConsole()
{
	return true;
}

CRenamedRecvTableInfo *CShellClientDLL::GetRenamedRecvTableInfos()
{
	return g_pRenamedRecvTableInfoHead;
}

// The mouth info for sounds played inside UI panels.
CMouthInfo *CShellClientDLL::GetClientUIMouthInfo()
{
	return &m_ClientUIMouth;
}

void CShellClientDLL::FileReceived( const char *fileName, unsigned int transferID )
{
	if ( g_pGameRules )
	{
		g_pGameRules->OnFileReceived( fileName, transferID );
	}
}

void CShellClientDLL::ClientAdjustStartSoundParams( StartSoundParams_t &params ) {}

const char *CShellClientDLL::TranslateEffectForVisionFilter( const char *pchEffectType, const char *pchEffectName )
{
	if ( !GameRules() )
		return pchEffectName;

	return GameRules()->TranslateEffectForVisionFilter( pchEffectType, pchEffectName );
}

bool CShellClientDLL::DisconnectAttempt()
{
	bool bRet = false;

	return bRet;
}

bool CShellClientDLL::IsConnectedUserInfoChangeAllowed( IConVar *pCvar )
{
	return GameRules() ? GameRules()->IsConnectedUserInfoChangeAllowed( NULL ) : true;
}

// TF-only methods nothing is seen to call: they return stock's harmless value and record
// a refusal.
bool CShellClientDLL::BHaveChatSuspensionInCurrentMatch()
{
	SS_CLIENTDLL_UNREACHABLE( "IBaseClientDLL::BHaveChatSuspensionInCurrentMatch" );
	return false;
}

void CShellClientDLL::DisplayVoiceUnavailableMessage()
{
	SS_CLIENTDLL_UNREACHABLE( "IBaseClientDLL::DisplayVoiceUnavailableMessage" );
}

// Prints the refusal tally; zero means every method the engine called was answered.
CON_COMMAND( cs_cl_refusals,
			 "SourceSharp: how many times each refused IBaseClientDLL method was called (plan_entity.md 5b)" )
{
	ShellClientDll_PrintRefusals( g_ShellClientDLL.Refusals() );
}

// The native count of stage crossings, which must equal the engine's FrameStageNotify
// calls. It counts even before the runtime loads.
uint64_t SourceSharp_ClientStageCrossings();
uint64_t SourceSharp_FrameStageNotifyCalls();

CON_COMMAND( ss_cl_stage_crossings, "ss_cl_stage_crossings -- FrameStageNotify calls vs ClientStage crossings" )
{
	Msg( "[SourceSharp] FrameStageNotify: %llu call(s), %llu crossing(s) into managed code\n",
		 (unsigned long long)SourceSharp_FrameStageNotifyCalls(),
		 (unsigned long long)SourceSharp_ClientStageCrossings() );
}

// Sends a managed message to the server on the engine's net channel, reliable.
int32_t SourceSharp_SendServerMessage( const char *pKey, const uint8_t *pData, int32_t nLen )
{
	INetChannel *pChannel = engine ? static_cast<INetChannel *>( engine->GetNetChannelInfo() ) : NULL;
	if ( !pChannel )
		return 0;

	// About 4 KiB: on the heap, freed once SendNetMsg has written it.
	CSourceSharpClientMessage *pMsg = new CSourceSharpClientMessage( NULL, SourceSharp_WriteClientMessage );
	const bool bSent = pMsg->Set( pKey, pData, nLen ) && pChannel->SendNetMsg( *pMsg, true );
	delete pMsg;
	return bSent ? 1 : 0;
}

// The net channel reads for the dev API's netgraph route. Each answers how many values it
// wrote, 0 with no channel.
static INetChannelInfo *ShellNetChannel()
{
	return engine ? engine->GetNetChannelInfo() : NULL;
}

static int32_t ShellPut( float *pOut, int32_t nOut, int32_t i, float flValue )
{
	if ( pOut && i < nOut )
		pOut[i] = flValue;
	return i + 1;
}

// Latency, avg latency, avg loss, avg choke, avg data, avg packets, total data, sequence
// number, for one flow (0 outgoing, 1 incoming).
int32_t SourceSharp_ClientNetFlow( int32_t nFlow, float *pOut, int32_t nOut )
{
	INetChannelInfo *pInfo = ShellNetChannel();
	if ( !pInfo || nFlow < 0 || nFlow >= MAX_FLOWS )
		return 0;

	int32_t i = 0;
	i = ShellPut( pOut, nOut, i, pInfo->GetLatency( nFlow ) );
	i = ShellPut( pOut, nOut, i, pInfo->GetAvgLatency( nFlow ) );
	i = ShellPut( pOut, nOut, i, pInfo->GetAvgLoss( nFlow ) );
	i = ShellPut( pOut, nOut, i, pInfo->GetAvgChoke( nFlow ) );
	i = ShellPut( pOut, nOut, i, pInfo->GetAvgData( nFlow ) );
	i = ShellPut( pOut, nOut, i, pInfo->GetAvgPackets( nFlow ) );
	i = ShellPut( pOut, nOut, i, (float)pInfo->GetTotalData( nFlow ) );
	i = ShellPut( pOut, nOut, i, (float)pInfo->GetSequenceNr( nFlow ) );
	return i;
}

// Net time, time connected, buffer size, data rate, loopback, timing out, time since last
// received, remote frame time and deviation, timeout seconds, interpolation amount.
int32_t SourceSharp_ClientNetChannel( float *pOut, int32_t nOut )
{
	INetChannelInfo *pInfo = ShellNetChannel();
	if ( !pInfo )
		return 0;

	float flFrameTime = 0.0f, flDeviation = 0.0f;
	pInfo->GetRemoteFramerate( &flFrameTime, &flDeviation );

	int32_t i = 0;
	i = ShellPut( pOut, nOut, i, pInfo->GetTime() );
	i = ShellPut( pOut, nOut, i, pInfo->GetTimeConnected() );
	i = ShellPut( pOut, nOut, i, (float)pInfo->GetBufferSize() );
	i = ShellPut( pOut, nOut, i, (float)pInfo->GetDataRate() );
	i = ShellPut( pOut, nOut, i, pInfo->IsLoopback() ? 1.0f : 0.0f );
	i = ShellPut( pOut, nOut, i, pInfo->IsTimingOut() ? 1.0f : 0.0f );
	i = ShellPut( pOut, nOut, i, pInfo->GetTimeSinceLastReceived() );
	i = ShellPut( pOut, nOut, i, flFrameTime );
	i = ShellPut( pOut, nOut, i, flDeviation );
	i = ShellPut( pOut, nOut, i, pInfo->GetTimeoutSeconds() );
	i = ShellPut( pOut, nOut, i, GetClientInterpAmount() );
	return i;
}

// One packet of one flow by sequence number: valid, send time, interpolation amount,
// latency (ms), choke, then its bytes per message group, GENERIC through TOTAL.
int32_t SourceSharp_ClientNetPacket( int32_t nFlow, int32_t nFrame, float *pOut, int32_t nOut )
{
	INetChannelInfo *pInfo = ShellNetChannel();
	if ( !pInfo || nFlow < 0 || nFlow >= MAX_FLOWS )
		return 0;

	int nLatency = 0, nChoke = 0;
	pInfo->GetPacketResponseLatency( nFlow, nFrame, &nLatency, &nChoke );

	int32_t i = 0;
	i = ShellPut( pOut, nOut, i, pInfo->IsValidPacket( nFlow, nFrame ) ? 1.0f : 0.0f );
	i = ShellPut( pOut, nOut, i, pInfo->GetPacketTime( nFlow, nFrame ) );
	i = ShellPut( pOut, nOut, i, pInfo->GetCommandInterpolationAmount( nFlow, nFrame ) );
	i = ShellPut( pOut, nOut, i, (float)nLatency );
	i = ShellPut( pOut, nOut, i, (float)nChoke );
	for ( int nGroup = INetChannelInfo::GENERIC; nGroup <= INetChannelInfo::TOTAL; nGroup++ )
		i = ShellPut( pOut, nOut, i, (float)pInfo->GetPacketBytes( nFlow, nFrame, nGroup ) );
	return i;
}

// The refused methods, one per index, with how often each was reached. Returns the method
// count; the total goes through pOutTotal.
int32_t SourceSharp_ClientRefusal( int32_t iIndex, char *pOutMethod, int32_t nMethodBytes, int32_t *pOutCount,
								   int32_t *pOutTotal )
{
	const CShellClientDllRefusals &refusals = g_ShellClientDLL.Refusals();
	if ( pOutTotal )
		*pOutTotal = refusals.Total();

	if ( iIndex >= 0 && iIndex < refusals.MethodCount() )
	{
		if ( pOutMethod && nMethodBytes > 0 )
			V_strncpy( pOutMethod, refusals.MethodAt( iIndex ) ? refusals.MethodAt( iIndex ) : "", nMethodBytes );
		if ( pOutCount )
			*pOutCount = refusals.CountAt( iIndex );
	}

	return refusals.MethodCount();
}
