//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- the client DLL the engine holds ====================//
// SS_REASON b  an interface resolved by name at load
//
// plan_entity.md Phase 5 step 5b. THE CLASS BEHIND IBaseClientDLL.
//
// WHERE IT CAME FROM. This was src/game/client/cdll_client_int.cpp, a stock
// translation unit this mod compiled and then carved with twenty-three
// SOURCESHARP guards -- the shape plan_entity.md 4g calls out as the thing to
// end ("every SOURCESHARP guard left in a compiled file"). 5b moves the whole
// unit here and resolves the guards for the arm this build actually takes, so
// the file that IS the client DLL is a SourceSharp file that says what it
// does, and cdll_client_int.cpp is off client_csharp.vpc and left untouched
// for stock's own build.
//
// WHAT THE ENGINE ASKS OF IT. Seventy-nine virtuals on Linux (eighty declared;
// StartStatsReporting is declared twice, once per platform --
// public/cdll_int.h:174-188). plan_entity.md §2 lists IBaseClientDLL among the
// interfaces resolved BY NAME at DLL load, so every slot has to exist and be
// callable whether or not anything ever calls it. Which ones the engine
// actually calls WAS measured, by a probe that armed all seventy-nine and
// printed the engine's own call site behind each row. That measurement is
// finished and recorded -- docs/clientdll-boundary.md carries the per-slot
// table and docs/engine-boundary.md §6a the summary -- so the probe and its
// two console commands were removed here rather than left armed forever.
//
// THE FRAME STAGE IS THE ONE CROSSING. FrameStageNotify reaches managed code
// through exactly one ABI slot, ClientStage, once per stage -- the client's
// counterpart of the server's GameFrame -> RunFrame (§3.4). Every later phase
// that moves per-frame client work into C# hangs it off a stage of that call
// rather than taking a slot of its own, which is what keeps the client's
// per-frame crossing count flat as the port proceeds.
//
// WHAT IS STILL NATIVE HERE, and why it is not a failure of the step: the
// engine interface globals, the string table callbacks, OnRenderStart /
// OnRenderEnd and the client's simulation order. Those are what a client DLL
// IS to the engine, and moving them is Phases 5c-6d's work, not 5b's. 5b's
// claim is narrower and checkable: the interface is a SourceSharp shell, every
// method the engine calls is answered, and the frame reaches managed code
// once per stage.
//
//===========================================================================//
#include "cbase.h"
#include "inetchannel.h"
#include "ss_client_message_write.h"   // IShimNetClient::SendServerMessage
#include "cdll_bounded_cvars.h"         // GetClientInterpAmount, for ClientNetChannel
#include "sourcesharp.h"			// the managed API table -- ClientStage lives in it
// The managed runtime this DLL loads, and the environment it loads it from.
// The server's counterpart of this pair of lines is shell_gamedll.cpp's
// `#include "managed_runtime_server.h"`; the client's loader lives beside it in
// managed_runtime_client.cpp, and the shell needs only the interfaces.
#include "sourcesharp_gamesystem.h"
#include "shell_managed_areas_client.h"   // CManagedHoldersClient: the client DLL object owns them (plan_client_vtables CD)
#include "shell_view_executor_client.h"   // View_Render's body (C7)
#include "shell_clientdll_refusals.h"
#include "shell_entitystatics.h"
#include "shell_clientsystem.h"	// plan_cworld_trace.md T4: the walkers
// bone_leaf_library.h (the bone gate, counter and per-frame sweeps) went
// 2026-10-18: nothing read the gate or the counter and no sweep was
// registered; the frame's bones are ClientSkeletons' (docs/client_evictions.md).
#include "shell_clientdll_voicehelper.h"	// IVoiceStatusHelper, a member below
#include "shell_cliententitylist.h"	// 5d: the handle resolve DispatchOnRestore makes
#include "gamerules.h"
#include "sharedInterface.h"
#include "vgui_int.h"
#include "clientmode.h"
#include "iinput.h"
#include "iviewrender.h"
#include "ivieweffects.h"
#include "ivmodemanager.h"
#include "prediction.h"
#include "clientsideeffects.h"
#include "shell_particle_mgr.h"	// CParticleMgr's Init, Term, LevelInit and level shutdown (C7 particles phase 4)
#include "smoke_fog_overlay.h"
#include "view.h"
#include "ienginevgui.h"
#include "iefx.h"
#include "enginesprite.h"
#include "networkstringtable_clientdll.h"
#include "voice_status.h"
#include "filesystem.h"
#include "c_te_legacytempents.h"
#include "c_rope.h"
#include "engine/ishadowmgr.h"
#include "engine/IStaticPropMgr.h"
#include "hud_basechat.h"
#include "view_shared.h"
#include "env_wind_shared.h"
#include "detailobjectsystem.h"
#include "clienteffectprecachesystem.h"
#include "soundenvelope.h"
#include "materialsystem/imaterialsystemstub.h"
#include "VGuiMatSurface/IMatSystemSurface.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"
#include "c_soundscape.h"
#include "engine/ivdebugoverlay.h"
#include "vguicenterprint.h"
#include "iviewrender_beams.h"
#include "engine/IEngineTrace.h"
#include "engine/ivmodelinfo.h"
#include "physics.h"
#include "usermessages.h"
#include "gamestringpool.h"
#include "IGameUIFuncs.h"
#include "saverestoretypes.h"
#include "saverestore.h"
#include "physics_saverestore.h"
#include "igameevents.h"
#include "datacache/idatacache.h"
#include "datacache/imdlcache.h"
#include "kbutton.h"
#include "tier0/icommandline.h"
#include "gamerules_register.h"
#include "bitmap/tgawriter.h"
#include "c_world.h"
#include "perfvisualbenchmark.h"	
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "c_vguiscreen.h"
#include "imessagechars.h"
#include "game/client/IGameClientExports.h"
#include "client_factorylist.h"
#include "rendertexture.h"
#include "view_scene.h"
#include "iclientmode.h"
#include "con_nprint.h"
#include "inputsystem/iinputsystem.h"
#include "appframework/IAppSystemGroup.h"
#include "scenefilecache/ISceneFileCache.h"
#include "tier2/tier2dm.h"
#include "tier3/tier3.h"
#include "tier_connect.h"
#include "ss_bitblock.h"

// The largest user-message payload copied for C# (UserMessageReceiver.MaxPayloadBytes;
// the engine's own limit is 255).
enum { kMaxUserMessageBlockBytes = 512 };
#include "ihudlcd.h"
#include "vgui/ILocalize.h"
#include "vgui/IVGui.h"
#include "ixboxsystem.h"
#include "engine/imatchmaking.h"
#include "cdll_bounded_cvars.h"
#include "gamestats.h"
#include "particle_parse.h"
#include "renamed_recvtable_compat.h"
#include "mouthinfo.h"
#include "mumble.h"

// NVNT includes
#include "hud_macros.h"
#include "haptics/ihaptics.h"
#include "haptics/haptic_utils.h"
#include "haptics/haptic_msgs.h"

extern vgui::IInputInternal *g_InputInternal;

//=============================================================================
// HPE_BEGIN
// [dwenger] Necessary for stats display
//=============================================================================

#include "imanaged_areas_client.h"   // lane c6-input: IN_IsKeyDown asks IManagedInputClient

//=============================================================================
// HPE_END
//=============================================================================

#include "ss_command_line.h"
#include "shell_particle_systems.h"
#include "shell_new_particles.h"	// ShellNewParticles_Frame / _RemoveAll (C7 particles phase 3)

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

extern IClientMode *GetClientModeNormal();

// IF YOU ADD AN INTERFACE, EXTERN IT IN THE HEADER FILE.
IVEngineClient	*engine = NULL;
IVModelRender *modelrender = NULL;
IVEfx *effects = NULL;
IVRenderView *render = NULL;
IVDebugOverlay *debugoverlay = NULL;
IMaterialSystemStub *materials_stub = NULL;
IDataCache *datacache = NULL;
IVModelInfoClient *modelinfo = NULL;
IEngineVGui *enginevgui = NULL;
INetworkStringTableContainer *networkstringtable = NULL;
ISpatialPartition* partition = NULL;
IFileSystem *filesystem = NULL;
IShadowMgr *shadowmgr = NULL;
IStaticPropMgrClient *staticpropmgr = NULL;
IEngineSound *enginesound = NULL;
IUniformRandomStream *random = NULL;
// randomgaussian (vstdlib's CGaussianRandomStream over `random`) is gone: no
// object on client.so's link reads it since mathproxy.cpp left (nm -u, C9).
ISharedGameRules *sharedgamerules = NULL;
IEngineTrace *enginetrace = NULL;
IGameUIFuncs *gameuifuncs = NULL;
IGameEventManager2 *gameeventmanager = NULL;
ISoundEmitterSystemBase *soundemitterbase = NULL;
IInputSystem *inputsystem = NULL;
ISceneFileCache *scenefilecache = NULL;
IXboxSystem *xboxsystem = NULL;	// Xbox 360 only
IMatchmaking *matchmaking = NULL;
IUploadGameStats *gamestatsuploader = NULL;
IClientReplayContext *g_pClientReplayContext = NULL;

IHaptics* haptics = NULL;// NVNT haptics system interface singleton

// g_pAchievementsAndStatsInterface stood here: nothing ever set it, and its
// readers (the achievements panel methods) answer its NULL directly now.

// (SoundEmitterSystem() was declared here, the way stock declared it. Its row
// is shell_soundemitter_client.cpp's ClientSystem_SoundEmitter now, which
// shell_clientsystems.cpp declares; ToolFrameworkClientSystem, declared beside
// it, is deleted: SFM is dead, user ruling 2026-10-19.)

// Engine player info, no game related infos here
BEGIN_BYTESWAP_DATADESC( player_info_s )
	DEFINE_ARRAY( name, FIELD_CHARACTER, MAX_PLAYER_NAME_LENGTH ),
	DEFINE_FIELD( userID, FIELD_INTEGER ),
	DEFINE_ARRAY( guid, FIELD_CHARACTER, SIGNED_GUID_LEN + 1 ),
	DEFINE_FIELD( friendsID, FIELD_INTEGER ),
	DEFINE_ARRAY( friendsName, FIELD_CHARACTER, MAX_PLAYER_NAME_LENGTH ),
	DEFINE_FIELD( fakeplayer, FIELD_BOOLEAN ),
	DEFINE_FIELD( ishltv, FIELD_BOOLEAN ),
	DEFINE_ARRAY( customFiles, FIELD_INTEGER, MAX_CUSTOM_FILES ),
	DEFINE_FIELD( filesDownloaded, FIELD_INTEGER ),
END_BYTESWAP_DATADESC()

// RequestCacheUsedMaterials and ProcessCacheUsedMaterials are defined below
// g_ShellClientDLL: the flag they read is a member of it now, so they cannot
// be written before the object they forward to exists.

// String tables
INetworkStringTable *g_pStringTableParticleEffectNames = NULL;
INetworkStringTable *g_StringTableEffectDispatch = NULL;
INetworkStringTable *g_StringTableVguiScreen = NULL;
INetworkStringTable *g_pStringTableMaterials = NULL;
INetworkStringTable *g_pStringTableInfoPanel = NULL;
INetworkStringTable *g_pStringTableClientSideChoreoScenes = NULL;
INetworkStringTable *g_pStringTableServerMapCycle = NULL;

// gpGlobals is defined further down, next to g_ShellClientDLL: the placeholder
// it starts out pointing at is a MEMBER of that object now (plan.md 1a).
class CHudChat;

extern void StopAllRumbleEffects( void );

// The C_EntityClassList registry (stock's s_pClassLists head and
// C_BaseEntityClassList's two members) is GONE: nothing on the client link
// line constructs a C_EntityClassList< T > any more, so the registry walked an
// empty list. The OnDataChanged queue (CDataChangedEvent and
// g_DataChangedEvents) is inlined at its one producer, C_World, in
// world_adapter_client.cpp. Neither was IBaseClientDLL's.

ISaveRestoreBlockHandler *GetEntitySaveRestoreBlockHandler();
ISaveRestoreBlockHandler *GetViewEffectsRestoreBlockHandler();



class IMoveHelper;

void DispatchHudText( const char *pszName );

// cl_team and cl_class are MEMBERS of CShellClientDLL (plan.md 1a). They keep
// their registration order: g_ShellClientDLL is a file-scope object, so its
// ConVar members are still constructed during static initialisation, still put
// themselves on the pending list there, and are still picked up by the one
// ConVar_Register( FCVAR_CLIENTDLL ) in Init. Nothing outside this file names
// either, so neither needs a proxy.
//
// IT WAS FOUR UNTIL PHASE 1c. showparticlecounts and r_lightmap_bicubic_set
// are MANAGED declarations now -- SourceSharp.Client/ConVars.cs -- because §6's
// ConVar row is that no ConVar's default, flags or help text is declared
// natively, and neither of those two is an engine-protocol name.
//
// cl_team AND cl_class STAY, and that is the row read the other way. The engine
// transmits them: both are FCVAR_USERINFO and the SERVER reads them back off
// the wire BY NAME, so a declaration that moved would leave the server's
// QUICKGETCVARVALUE finding an empty string. They are protocol, not tuning.
// localplayer_visionflags and cl_interpolate are native for the same reason,
// elsewhere in this shim.

// Physics system
bool g_bLevelInitialized;
bool g_bTextMode = false;


//-----------------------------------------------------------------------------
// THE THREE CLASSES THAT LEFT THIS FILE, and where they went. plan_entity.md
// Phase 5, the 5b split: cdll_client_int.cpp put ten classes in one 3,118-line
// translation unit, and the three below have nothing to do with IBaseClientDLL.
//
//   CGameClientExports           shell_clientdll_gameclientexports.cpp
//   CClientDLLSharedAppSystems   shell_clientdll_appsystems.cpp
//   CHLVoiceStatusHelper         shell_clientdll_voicehelper.{h,cpp}
//
// The fourth was CBoneSetupEnt, the cl_ShowBoneSetupEnts readout, and it is
// gone rather than moved: its feeder TrackBoneSetupEnt had no caller on the
// client link line, so the tree it printed was always empty.
// CDataChangedEvent and the C_EntityClassList registry left earlier in the
// file, for the same reason. WHAT STAYS IS CShellClientDLL -- the IBaseClientDLL
// implementation and its EXPOSE_SINGLE_INTERFACE_GLOBALVAR, so the interface
// registration keeps its home and there is exactly one InterfaceReg for
// CLIENT_DLL_INTERFACE_VERSION.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE REFUSAL TALLY, PRINTED. CShellGameDll's ShellGameDll_PrintRefusals, with
// the same body and the same two callers -- a console command and the DLL's
// shutdown -- because a readout written twice is two readouts that can disagree
// about what zero looks like.
//
// A static FUNCTION, which is the desirable form of `static` and not the one
// this project's rule forbids: internal linkage, no state.
//-----------------------------------------------------------------------------
static void ShellClientDll_PrintRefusals( const CShellClientDllRefusals &refusals )
{
	Msg( "[SourceSharp] client refusals: %d call(s) across %d method(s)\n",
	     refusals.Total(), refusals.MethodCount() );

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

//-----------------------------------------------------------------------------
// THE WARNING HALF, which is the one part that needs the engine. Declared in
// shell_clientdll_refusals.h and defined here, exactly as the server defines
// ShellGameDll_RefusalWarning in shell_gamedll.cpp -- that seam is what lets
// the gtest compile the shipped counter class against a recorder of its own.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// Purpose: engine to client .dll interface
//-----------------------------------------------------------------------------
// THE SECOND BASE IS THE SERVER'S SECOND BASE.
//
//     class CShellGameDll : public IServerGameDLL,
//                           public IConCommandBaseAccessor,
//                           public IManagedRuntimeEnvironment
//
// The shell owns Init, so the shell owns the factory and the globals, and the
// managed loader asks IT rather than fetching its own. Until this change the
// client had no such inversion at all: it reached the CLR through a void free
// function calling a file-static game system's Init(), with no interface
// between them and no partner for the release.
//
// IConCommandBaseAccessor is NOT here, and that is a real difference rather
// than an omission: the server passes `this` to ConVar_Register so replicated
// ConVars take the server's value, and the client's ConVar_Register(
// FCVAR_CLIENTDLL ) has no such argument on this side. STATED DIVERGENCE.
class CShellClientDLL : public IBaseClientDLL, public IManagedRuntimeEnvironment
{
public:
	CShellClientDLL();

	virtual int						Init( CreateInterfaceFn appSystemFactory, CreateInterfaceFn physicsFactory, CGlobalVarsBase *pGlobals );

	virtual void					PostInit();
	virtual void					Shutdown( void );

	virtual bool					ReplayInit( CreateInterfaceFn fnReplayFactory );
	virtual bool					ReplayPostInit();

	virtual void					LevelInitPreEntity( const char *pMapName );
	virtual void					LevelInitPostEntity();
	virtual void					LevelShutdown( void );

	virtual ClientClass				*GetAllClasses( void );

	virtual int						HudVidInit( void );
	virtual void					HudProcessInput( bool bActive );
	virtual void					HudUpdate( bool bActive );
	virtual void					HudReset( void );
	virtual void					HudText( const char * message );

	// Mouse Input Interfaces
	virtual void					IN_ActivateMouse( void );
	virtual void					IN_DeactivateMouse( void );
	virtual void					IN_Accumulate( void );
	virtual void					IN_ClearStates( void );
	virtual bool					IN_IsKeyDown( const char *name, bool& isdown );
	virtual void					IN_OnMouseWheeled( int nDelta );
	// Raw signal
	virtual int						IN_KeyEvent( int eventcode, ButtonCode_t keynum, const char *pszCurrentBinding );
	virtual void					IN_SetSampleTime( float frametime );
	// Create movement command
	virtual void					CreateMove ( int sequence_number, float input_sample_frametime, bool active );
	virtual void					ExtraMouseSample( float frametime, bool active );
	virtual bool					WriteUsercmdDeltaToBuffer( bf_write *buf, int from, int to, bool isnewcommand );	
	virtual void					EncodeUserCmdToBuffer( bf_write& buf, int slot );
	virtual void					DecodeUserCmdFromBuffer( bf_read& buf, int slot );


	virtual void					View_Render( vrect_t *rect );
	virtual void					RenderView( const CViewSetup &view, int nClearFlags, int whatToDraw );
	virtual void					View_Fade( ScreenFade_t *pSF );
	
	virtual void					SetCrosshairAngle( const QAngle& angle );

	virtual void					InitSprite( CEngineSprite *pSprite, const char *loadname );
	virtual void					ShutdownSprite( CEngineSprite *pSprite );

	virtual int						GetSpriteSize( void ) const;

	virtual void					VoiceStatus( int entindex, qboolean bTalking );

	virtual void					InstallStringTableCallback( const char *tableName );

	virtual void					FrameStageNotify( ClientFrameStage_t curStage );

	virtual bool					DispatchUserMessage( int msg_type, bf_read &msg_data );

	// Save/restore system hooks
	virtual CSaveRestoreData  *SaveInit( int size );
	virtual void			SaveWriteFields( CSaveRestoreData *, const char *, void *, datamap_t *, typedescription_t *, int );
	virtual void			SaveReadFields( CSaveRestoreData *, const char *, void *, datamap_t *, typedescription_t *, int );
	virtual void			PreSave( CSaveRestoreData * );
	virtual void			Save( CSaveRestoreData * );
	virtual void			WriteSaveHeaders( CSaveRestoreData * );
	virtual void			ReadRestoreHeaders( CSaveRestoreData * );
	virtual void			Restore( CSaveRestoreData *, bool );
	virtual void			DispatchOnRestore();
	virtual void			WriteSaveGameScreenshot( const char *pFilename );

	// Given a list of "S(wavname) S(wavname2)" tokens, look up the localized text and emit
	//  the appropriate close caption if running with closecaption = 1
	virtual void			EmitSentenceCloseCaption( char const *tokenstream );
	virtual void			EmitCloseCaption( char const *captionname, float duration );

	virtual CStandardRecvProxies* GetStandardRecvProxies();

	virtual bool			CanRecordDemo( char *errorMsg, int length ) const;

	virtual void			OnDemoRecordStart( char const* pDemoBaseName );
	virtual void			OnDemoRecordStop();
	virtual void			OnDemoPlaybackStart( char const* pDemoBaseName );
	virtual void			OnDemoPlaybackStop();

	virtual bool			ShouldDrawDropdownConsole();

	// Get client screen dimensions
	virtual int				GetScreenWidth();
	virtual int				GetScreenHeight();

	// save game screenshot writing
	virtual void			WriteSaveGameScreenshotOfSize( const char *pFilename, int width, int height, bool bCreatePowerOf2Padded/*=false*/, bool bWriteVTF/*=false*/ );

	// Gets the location of the player viewpoint
	virtual bool			GetPlayerView( CViewSetup &playerView );

	// Matchmaking
	virtual void			SetupGameProperties( CUtlVector< XUSER_CONTEXT > &contexts, CUtlVector< XUSER_PROPERTY > &properties );
	virtual uint			GetPresenceID( const char *pIDName );
	virtual const char		*GetPropertyIdString( const uint id );
	virtual void			GetPropertyDisplayString( uint id, uint value, char *pOutput, int nBytes );
	virtual void			StartStatsReporting( HANDLE handle, bool bArbitrated );

	virtual void			InvalidateMdlCache();

	virtual void			ReloadFilesInList( IFileList *pFilesToReload );

	// Let the client handle UI toggle - if this function returns false, the UI will toggle, otherwise it will not.
	virtual bool			HandleUiToggle();

	// Allow the console to be shown?
	virtual bool			ShouldAllowConsole();

	// Get renamed recv tables
	virtual CRenamedRecvTableInfo	*GetRenamedRecvTableInfos();

	// Get the mouthinfo for the sound being played inside UI panels
	virtual CMouthInfo		*GetClientUIMouthInfo();

	// Notify the client that a file has been received from the game server
	virtual void			FileReceived( const char * fileName, unsigned int transferID );

	virtual const char* TranslateEffectForVisionFilter( const char *pchEffectType, const char *pchEffectName );
	
	virtual void			ClientAdjustStartSoundParams( struct StartSoundParams_t& params );
	
	// Returns true if the disconnect command has been handled by the client
	virtual bool DisconnectAttempt( void );
public:
	void PrecacheMaterial( const char *pMaterialName );

	virtual bool IsConnectedUserInfoChangeAllowed( IConVar *pCvar );

	virtual bool BHaveChatSuspensionInCurrentMatch();

	virtual void DisplayVoiceUnavailableMessage();

private:
	void UncacheAllMaterials( );
	void ResetStringTablePointers();

	CUtlVector< IMaterial * > m_CachedMaterials;

	// The map-specific particle files the previous level read; -1 before the
	// first level (plan_map_load P8, LevelInitPreEntity).
	int m_nPreviousMapParticleFiles = -1;

public:
	//-------------------------------------------------------------------------
	// WHAT USED TO BE AT FILE SCOPE. plan.md 1a: nothing is a static or a
	// global. Every one of these was a loose object in cdll_client_int.cpp; the
	// state is owned here and stock's callers reach it through the thin
	// proxies at the bottom of this file, each of which goes when its last
	// caller does.
	//
	// THESE TWO ARE PUBLIC AND ARE ADDRESSED DIRECTLY, which is a static
	// initialisation requirement rather than laxity. `gpGlobals` is a
	// pointer other translation units read from THEIR OWN static
	// initialisers, so it must be initialised before any dynamic
	// initialisation runs. `&g_ShellClientDLL.m_DummyGlobals` is an
	// address constant -- a fixed offset into an object with static storage --
	// so the pointer keeps the constant initialisation stock's `&dummyvars`
	// had. Routing it through an accessor CALL would make it a dynamic
	// initialiser and reintroduce exactly the order hazard the placeholder
	// exists to avoid.
	//-------------------------------------------------------------------------
	CGlobalVarsBase			m_DummyGlobals;

	// The proxied half. Each of these is the body of a stock free function of
	// (nearly) the same name, which now forwards here.
	void					RequestCacheMaterials();
	void					ProcessCacheMaterials();
	uint64_t				ClientStageCrossings() const;
	uint64_t				FrameStageNotifyCalls() const;

private:
	bool					m_bRequestCacheUsedMaterials;
	uint64_t				m_nClientStageCrossings;
	uint64_t				m_nFrameStageNotifyCalls;
	CMouthInfo				m_ClientUIMouth;
	ClientFrameStage_t		m_CurFrameStage;

	// The voice manager is handed &m_VoiceStatusHelper in Init. Stock handed it
	// the address of a file-scope static; this is the same object with an owner.
	CHLVoiceStatusHelper	m_VoiceStatusHelper;

	// The two PROTOCOL ConVars. Members of a FILE-SCOPE object, so they are
	// still constructed during static initialisation and still register in the
	// one ConVar_Register( FCVAR_CLIENTDLL ) that Init makes -- a ConVar built
	// lazily would be built after that call and would never register at all.
	// Neither is read here: they exist for their registration, exactly as they
	// did at file scope, because the SERVER reads them off the wire by name.
	ConVar					m_cl_team;
	ConVar					m_cl_class;

	// plan_map_load P6: SDL_PumpEvents from the libSDL2 the launcher already
	// loaded, or NULL. Called from load steps this DLL owns (PumpWindowDuringLoad)
	// so the window answers the compositor's ping during a long load; the events
	// stay queued for the engine's own poll. SDL wants its main thread.
	void ( *m_pfnPumpEvents )() = nullptr;
	void PumpWindowDuringLoad() { if ( m_pfnPumpEvents && ThreadInMainThread() ) m_pfnPumpEvents(); }

public:
	// THE REFUSAL READOUT -- CShellGameDll::Refusals()'s counterpart, and
	// public for its reason: the ConCommand at the bottom of this file is the
	// only channel this tree has for getting a number out of a running client.
	// 5b's gate is "every IBaseClientDLL method the engine calls is answered",
	// and this reading ZERO is the evidence. A client that did not crash is
	// not: every refused method is a no-op whose absence nothing would notice.
	//
	// SEVEN ROWS NOW, not 5b's two: step 7g added the five matchmaking and rich
	// presence methods when it deleted basepresence.cpp, for the same reason --
	// the bodies they forwarded to were empty on PC.
	const CShellClientDllRefusals &Refusals() const { return m_Refusals; }

	// ShellClientDll_RefuseExternal's way in: the particles shell's refused
	// IParticleSystemQuery methods count on this tally too.
	void RefuseExternal( const char *pMethod ) { ShellClientDll_Unreachable( pMethod ); }

private:
	// THE SERVER'S SPELLING, KEPT. CShellGameDll::ShellGameDll_Unreachable is
	// one private line forwarding to a FIELD, for a project rule that forbids
	// the free function the plan writes ("no file-scope statics or globals in
	// SourceSharp's C++"), and this is the same line with the same job. The
	// name is greppable and the state is owned; nothing is given up.
	void ShellClientDll_Unreachable( const char *pMethod )
	{
		m_Refusals.Unreachable( pMethod );
	}

	CShellClientDllRefusals m_Refusals;

	// -- IManagedRuntimeEnvironment ------------------------------------------
	// CShellGameDll's block, word for word, on the client's two spellings. Both
	// members are assigned at the top of Init(); they were simply never
	// exposed, because there was no interface to expose them through.

public:
	void *GetEngineInterface( const char *pName, int *pReturnCode ) override
	{
		return m_pEngineFactory( pName, pReturnCode );
	}

	// Engine-owned memory at a fixed address, alive for the whole process and
	// changing every frame. Managed code reads fields through the pointer -- a
	// snapshot taken at load would be one frame stale everywhere.
	//
	// CGlobalVarsBase rather than the server's CGlobalVars: that is the type
	// IBaseClientDLL::Init is handed, and it is the base of the server's, not a
	// different one.
	CGlobalVarsBase &GetGlobalVars() override
	{
		return *m_pGlobals;
	}

private:
	CreateInterfaceFn		m_pEngineFactory;
	CGlobalVarsBase		   *m_pGlobals;

	// THE RUNTIME THIS DLL LOADED, and the member CShellGameDll has had since
	// Phase 7a. One load and one release per DLL: see the ownership block in
	// sourcesharp_gamesystem.h for why that is safe on a listen server, where
	// both DLLs do this in one process.
	IClientRuntimeAdapter  *m_pManagedRuntime;

public:
	// plan_client_vtables CD: everything the client holds from managed (the
	// root, the runtime, the areas), as CShellGameDll holds the server's.
	CManagedHoldersClient &Managed() { return m_Managed; }

	virtual ~CShellClientDLL()
	{
		AssertMsg( ManagedHoldersClient_Empty( m_Managed ),
		           "the client DLL object is destroyed holding a managed-built object" );
	}

private:
	CManagedHoldersClient m_Managed = {};
};

//-----------------------------------------------------------------------------
// ONE LINE AT A REFUSING METHOD, AND IT RECORDS BOTH THINGS.
//
// A refusal is two facts and they are kept in two places on purpose:
//
//   * the PROBE TABLE row, which carries the engine's own return address and so
//     says WHERE the call came from -- the thing that makes a refusal
//     actionable rather than merely known;
//   * the REFUSAL TALLY, which is the server's mechanism and the one 5b's gate
//     is read out of, and which warns the first time as well as counting.
//
// They are written by one macro at one call site so they cannot disagree about
// whether a refusal happened, and the method NAME comes out of the probe's own
// table rather than being spelt again at the call site -- so it cannot drift
// from the enum either.
//
// SS_BOUNDARY_CALLER() expands HERE, inside the refusing method, which is why
// this is a macro and not a helper: a helper would record its own caller (the
// refusing method) instead of the engine's instruction after the call.
//-----------------------------------------------------------------------------
#define SS_CLIENTDLL_UNREACHABLE( pszMethod ) \
	ShellClientDll_Unreachable( ( pszMethod ) )


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

//-----------------------------------------------------------------------------
// THE PROXY POINTER, and why it is HERE rather than at the top of the file
// where stock declared it: it points at a MEMBER of g_ShellClientDLL, so it
// cannot be written before that object is declared. It keeps CONSTANT
// initialisation -- the address of a subobject of an object with static
// storage duration is an address constant -- which matters because other
// translation units read gpGlobals from their own static initialisers.
//-----------------------------------------------------------------------------

// So stuff that might reference gpGlobals during DLL initialization won't have a NULL pointer.
// Once the engine calls Init on this DLL, this pointer gets assigned to the shared data in the engine
CGlobalVarsBase *gpGlobals = &g_ShellClientDLL.m_DummyGlobals;

//-----------------------------------------------------------------------------
// THE MATERIAL CACHE FLAG'S TWO PROXIES. Stock's names, stock's callers -- the
// two string-table callbacks below ask for the sweep and FRAME_NET_UPDATE_END
// runs it -- forwarding to the member that replaced the file-scope bool.
//-----------------------------------------------------------------------------
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


//-----------------------------------------------------------------------------
// Precaches a material
//-----------------------------------------------------------------------------
void PrecacheMaterial( const char *pMaterialName )
{
	g_ShellClientDLL.PrecacheMaterial( pMaterialName );
}

// GetMaterialIndex, GetMaterialNameFromIndex and PrecacheParticleSystem stood
// here: no object on client.so's link edge imported them (nm -u, lane M1-B).

//-----------------------------------------------------------------------------
// Converts a previously precached particle system into an index
//-----------------------------------------------------------------------------
int GetParticleSystemIndex( const char *pParticleSystemName )
{
	if ( pParticleSystemName )
	{
		int nIndex = g_pStringTableParticleEffectNames->FindStringIndex( pParticleSystemName );
		if ( nIndex != INVALID_STRING_INDEX )
			return nIndex;
		DevWarning("Client: Missing precache for particle system \"%s\"!\n", pParticleSystemName );
	}

	// This is the invalid string index
	return 0;
}

//-----------------------------------------------------------------------------
// Converts precached particle system indices into strings
//-----------------------------------------------------------------------------
const char *GetParticleSystemNameFromIndex( int nIndex )
{
	if ( nIndex < g_pStringTableParticleEffectNames->GetMaxStrings() )
		return g_pStringTableParticleEffectNames->GetString( nIndex );
	return "error";
}

// IsEngineThreaded and its host_thread_mode ConVar* stood here: no object on
// client.so's link edge imported it (nm -u, lane M1-B).

//-----------------------------------------------------------------------------
// Constructor
//-----------------------------------------------------------------------------

CShellClientDLL::CShellClientDLL()
	: m_DummyGlobals( true )
	, m_bRequestCacheUsedMaterials( false )
	, m_nClientStageCrossings( 0 )
	, m_nFrameStageNotifyCalls( 0 )
	, m_CurFrameStage( FRAME_UNDEFINED )
	, m_cl_team( "cl_team", "default", FCVAR_USERINFO|FCVAR_ARCHIVE,
	             "Default team when joining a game" )
	, m_cl_class( "cl_class", "default", FCVAR_USERINFO|FCVAR_ARCHIVE,
	              "Default class when joining a game" )
	, m_pEngineFactory( NULL )
	, m_pGlobals( NULL )
	, m_pManagedRuntime( NULL )
{
	// Kinda bogus, but the logic in the engine is too convoluted to put it there
	g_bLevelInitialized = false;
}



//-----------------------------------------------------------------------------
// SteamVR is deleted (plan_client_novalve C1, user ruling 2026-10-04), and so
// is client_virtualreality.cpp with its ClientVirtualReality001 export. The
// no-op g_ClientVirtualReality and g_pSourceVR that stood here for the fork
// view TUs went with the last of those TUs (absence row 9 in
// docs/absence-client.md; lane M1-B, nm -u: no importer).
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Purpose: Called when the DLL is first loaded.
// Input  : engineFactory - 
// Output : int
//-----------------------------------------------------------------------------
int CShellClientDLL::Init( CreateInterfaceFn appSystemFactory, CreateInterfaceFn physicsFactory, CGlobalVarsBase *pGlobals )
{
	// The CRT memory-debug hook left with crtmemdebug.cpp (C8): empty without MSVC's CRT.
	// No MathLib_Init: mathlib's members are mathlib_port.cpp's, which keep no
	// tables to fill (C9, as server.so's DLLInit).


	// Hook up global variables
	gpGlobals = pGlobals;

	// AND THE SAME TWO AS MEMBERS, which is what IManagedRuntimeEnvironment
	// serves. CShellGameDll::DLLInit opens with exactly this pair of
	// assignments and for exactly this reason.
	m_pEngineFactory = appSystemFactory;
	m_pGlobals = pGlobals;

	ConnectTier1Libraries( &appSystemFactory, 1 );
	SourceSharp_ConnectTier2( appSystemFactory );	// tier_connect.cpp (C9)
	// tier3's lookups are tier_connect.cpp's (plan_client_novalve C9): the
	// client asks only for what its objects import.
	SourceSharp_ConnectTier3( appSystemFactory );

	// Client needs to protect from writing files into random locations to avoid becoming a remote-code
	// execution platform.
	if ( g_pFullFileSystem )
	{
		g_pFullFileSystem->SetWriteProtectionEnable( true );
	}

	// ClientSteamContext().Activate() is MANAGED now (plan_client_novalve C6,
	// Steam to managed): ClientModule activates its ClientSteamContext and
	// registers the five Steam callbacks when it builds its root.

	// We aren't happy unless we get all of our interfaces.
	// please don't collapse this into one monolithic boolean expression (impossible to debug)
	if ( (engine = (IVEngineClient *)appSystemFactory( VENGINE_CLIENT_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( (modelrender = (IVModelRender *)appSystemFactory( VENGINE_HUDMODEL_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( (effects = (IVEfx *)appSystemFactory( VENGINE_EFFECTS_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( (enginetrace = (IEngineTrace *)appSystemFactory( INTERFACEVERSION_ENGINETRACE_CLIENT, NULL )) == NULL )
		return false;
	if ( (render = (IVRenderView *)appSystemFactory( VENGINE_RENDERVIEW_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( (debugoverlay = (IVDebugOverlay *)appSystemFactory( VDEBUG_OVERLAY_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( (datacache = (IDataCache*)appSystemFactory(DATACACHE_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( !mdlcache )
		return false;
	if ( (modelinfo = (IVModelInfoClient *)appSystemFactory(VMODELINFO_CLIENT_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( (enginevgui = (IEngineVGui *)appSystemFactory(VENGINE_VGUI_VERSION, NULL )) == NULL )
		return false;
	if ( (networkstringtable = (INetworkStringTableContainer *)appSystemFactory(INTERFACENAME_NETWORKSTRINGTABLECLIENT,NULL)) == NULL )
		return false;
	if ( (::partition = (ISpatialPartition *)appSystemFactory(INTERFACEVERSION_SPATIALPARTITION, NULL)) == NULL )
		return false;
	if ( (shadowmgr = (IShadowMgr *)appSystemFactory(ENGINE_SHADOWMGR_INTERFACE_VERSION, NULL)) == NULL )
		return false;
	if ( (staticpropmgr = (IStaticPropMgrClient *)appSystemFactory(INTERFACEVERSION_STATICPROPMGR_CLIENT, NULL)) == NULL )
		return false;
	if ( (enginesound = (IEngineSound *)appSystemFactory(IENGINESOUND_CLIENT_INTERFACE_VERSION, NULL)) == NULL )
		return false;
	if ( (filesystem = (IFileSystem *)appSystemFactory(FILESYSTEM_INTERFACE_VERSION, NULL)) == NULL )
		return false;
	if ( (random = (IUniformRandomStream *)appSystemFactory(VENGINE_CLIENT_RANDOM_INTERFACE_VERSION, NULL)) == NULL )
		return false;
	if ( (gameuifuncs = (IGameUIFuncs * )appSystemFactory( VENGINE_GAMEUIFUNCS_VERSION, NULL )) == NULL )
		return false;
	if ( (gameeventmanager = (IGameEventManager2 *)appSystemFactory(INTERFACEVERSION_GAMEEVENTSMANAGER2,NULL)) == NULL )
		return false;
	if ( (soundemitterbase = (ISoundEmitterSystemBase *)appSystemFactory(SOUNDEMITTERSYSTEM_INTERFACE_VERSION, NULL)) == NULL )
		return false;
	if ( (inputsystem = (IInputSystem *)appSystemFactory(INPUTSYSTEM_INTERFACE_VERSION, NULL)) == NULL )
		return false;
	if ( (scenefilecache = (ISceneFileCache *)appSystemFactory( SCENE_FILE_CACHE_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( IsX360() && (xboxsystem = (IXboxSystem *)appSystemFactory( XBOXSYSTEM_INTERFACE_VERSION, NULL )) == NULL )
		return false;
	if ( IsX360() && (matchmaking = (IMatchmaking *)appSystemFactory( VENGINE_MATCHMAKING_VERSION, NULL )) == NULL )
		return false;
	if ( ( gamestatsuploader = (IUploadGameStats *)appSystemFactory( INTERFACEVERSION_UPLOADGAMESTATS, NULL )) == NULL )
		return false;

	if (!g_pMatSystemSurface)
		return false;


	// SteamVR is deleted (plan_client_novalve C1): `-vr` no longer asks for
	// sourcevr's interface, so g_pSourceVR stays NULL and UseVR() is false.

	factorylist_t factories;
	factories.appSystemFactory = appSystemFactory;
	factories.physicsFactory = physicsFactory;
	FactoryList_Store( factories );

	// Yes, both the client and game .dlls will try to Connect, the soundemittersystem.dll will handle this gracefully
	if ( !soundemitterbase->Connect( appSystemFactory ) )
	{
		return false;
	}

	if ( ss::CommandLineHas( "-textmode" ) )
		g_bTextMode = true;

	// Not fatal if the material system stub isn't around.
	materials_stub = (IMaterialSystemStub*)appSystemFactory( MATERIAL_SYSTEM_STUB_INTERFACE_VERSION, NULL );

	if( !g_pMaterialSystemHardwareConfig )
		return false;

	// Initialize the console variables.
	ConVar_Register( FCVAR_CLIENTDLL );

	// The Initializer registry's walk left with initializer.cpp (C8): no object
	// on the link line registered an Initializer, so it walked nothing.

	if ( !ShellParticleMgr_Init( materials ) )
		return false;

	// plan_map_load P1: a listen server's spawn reloads only what the map paks
	// can have changed (shell_map_material_reload.cpp). Failing to install is
	// the stock full reload, never a failed Init.
	bool SourceSharp_InstallMapMaterialReload( IFileSystem *pFileSystem, IMaterialSystem *pMaterials );
	SourceSharp_InstallMapMaterialReload( filesystem, materials );

	// plan_map_load P6: the window pump (see m_pfnPumpEvents). RTLD_NOLOAD: the
	// launcher's copy or nothing; this never loads a second SDL. Valve's
	// Windows engine has no SDL (its window is Win32's), so there is no pump
	// to find there and the window is pumped between frames, as it is here
	// when no SDL is loaded.
#ifndef _WIN32
	if ( void *hSDL = dlopen( "libSDL2-2.0.so.0", RTLD_NOW | RTLD_NOLOAD ) )
		m_pfnPumpEvents = (void ( * )())dlsym( hSDL, "SDL_PumpEvents" );
#endif
	Msg( "[SourceSharp] load pump: %s\n", m_pfnPumpEvents ? "SDL_PumpEvents" : "none (the window is pumped only between frames)" );


	if (!VGui_Startup( appSystemFactory ))
		return false;

	// matsys_controls' VGui_InitMatSysInterfacesList stood here. What it set --
	// the material system, its surface, the model cache, studio render and the
	// hardware config -- tier2 and tier3 connected above (ConnectTier2/3Libraries),
	// and nothing here is a matsys_controls panel; the library left the link.
	Assert( g_pMaterialSystem && g_pMatSystemSurface && g_pMaterialSystemHardwareConfig );

	// SOURCESHARP plan_cworld_trace.md T4: THE ELEVEN Adds THAT STOOD HERE ARE
	// ROWS NOW.
	//
	// They were the first eleven entries of an anonymous list that every other
	// client game system also appended itself to, from CAutoGameSystem's
	// constructor, in link order. Eleven ordered by hand and twenty-nine ordered
	// by the linker. The whole forty is
	// sourcesharp/game/client/shell_clientsystems.cpp's kClientSystems[], in the
	// order a live census measured them running in, and stock's two load-bearing
	// comments here -- "Client Leaf System has to be initialized first, since
	// DetailObjectSystem uses it" and "This must happen prior to ClientThinkList"
	// -- moved with the rows they are about.
	//
	// The three under #ifndef SOURCESHARP (PerfVisualBenchmark, MumbleSystem,
	// SteamShareSystem) and the four under TF_CLIENT_DLL / COPY_CHECK_STRESSTEST
	// were already out of this build and are not rows.

	modemanager->Init( );

	g_pClientMode->InitViewport();

	// BRINGING THE MANAGED RUNTIME UP, BY NAME, FROM A NAMED POINT -- the
	// client's `m_pManagedRuntime = LoadManagedRuntime(this)`, which is
	// CShellGameDll::DLLInit's line and now this one too.
	//
	// BEFORE THE HUD, NOT AFTER, and that has not changed: gHUD.Init() hooks
	// every user message the HUD listens for, this mod's message list is
	// declared in C#, and hooking against an empty list is one failed hook per
	// message -- silent in release, fifteen assertion dialogs in a debug build.
	// What used to stand here was `SourceSharp_ClientEarlyInit()`, a void free
	// function whose answer was thrown away; this returns one and it is
	// checked.
	//
	// THE ORDER IS THE SERVER'S. LoadManagedRuntime brings the CLR up, and only
	// then does SourceSharp_ClientSystemsInit() walk the table below -- whose
	// SourceSharp row does no load of its own for exactly that reason.
	m_pManagedRuntime = LoadManagedRuntime( this );

	if ( m_pManagedRuntime == NULL )
		return false;

	// The row that drives the per-level and per-frame forwards gets the adapter
	// the loader just handed over. One line, and it is the whole of the client's
	// equivalent of kServerSystems[] finding the CLR already up.
	SourceSharp_SetClientRuntime( m_pManagedRuntime );

	// The particle manifest read CParticleMgr::Init asked for above, held until
	// the managed reader was up (particle_manifest.cpp, C9 KeyValues K1).
	void SourceSharp_ParticleManifestRuntimeUp();
	SourceSharp_ParticleManifestRuntimeUp();

	gHUD.Init();

	g_pClientMode->Init();

	if ( !SourceSharp_ClientSystemsInit() )
		return false;

	g_pClientMode->Enable();

	// The client's IViewRender is the shell's (C7 step 3): CShellViewRender.
	if ( !view )
	{
		view = ShellViewRender();
	}

	view->Init();

	// 7b: C_BaseTempEntity::PrecacheTempEnts() WAS HERE, and it is gone with
	// c_basetempentity.cpp. It walked s_pTempEntities calling Precache() on
	// each, and that list is built by exactly one thing: the C_BaseTempEntity
	// constructor. No compiled TU constructs one any more -- the last native
	// subclass in this build was C_TEEffectDispatch, which is inside
	// c_te_effect_dispatch.cpp's SOURCESHARP guard -- so the walk had nothing
	// in it to walk. Every temp entity in this build is a managed declaration
	// planned by ClientTempEntEngine and built through TempEntBuildClient, and
	// what it precaches it precaches from C#.
	input->Init_All();

	VGui_CreateGlobalPanels();

	InitSmokeFogOverlay();

	// CUserMessageRegister::RegisterAll() stood here: no compiled TU declares a
	// USER_MESSAGE_REGISTER, so its list was empty (docs/client_evictions.md).

	ClientVoiceMgr_Init();

	// Embed voice status icons inside chat element
	{
		vgui::VPANEL parent = enginevgui->GetPanel( PANEL_CLIENTDLL );
		GetClientVoiceMgr()->Init( &m_VoiceStatusHelper, parent );
	}

	if ( !PhysicsDLLInit( physicsFactory ) )
		return false;


	ClientWorldFactoryInit();

#if defined( WIN32 )
	// NVNT connect haptics sytem
	ConnectHaptics(appSystemFactory);
#endif

	// RegisterSecureLaunchProcessFunc( &UnsafeCmdLineProcessor ) is MANAGED
	// now: SecureLaunch (src/client/managed/Steam) takes the URL launch and the
	// rich-presence join callbacks and runs only a safe `connect`.

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

//-----------------------------------------------------------------------------
// Purpose: Called after client & server DLL are loaded and all systems initialized
//-----------------------------------------------------------------------------
void CShellClientDLL::PostInit()
{
	SourceSharp_ClientSystemsPostInit();

	// The once-per-install r_lightmap_bicubic default is managed's
	// (LightmapBicubicDefault, lane M1-B): this hands it the one fact only the
	// material system has, the adapter's maximum DX support level, and -1 with
	// no material system (stock's `&& materials` skipped the default).
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

//-----------------------------------------------------------------------------
// Purpose: Called when the client .dll is being dismissed
//-----------------------------------------------------------------------------
void CShellClientDLL::Shutdown( void )
{
	// First: the hooked slots name functions in this module (plan_map_load P1).
	void SourceSharp_UninstallMapMaterialReload();
	SourceSharp_UninstallMapMaterialReload();

	// THE SECOND CALLER OF THE READOUT, and the server's placement: the tally
	// is printed on the way out whether or not anybody typed the command, so a
	// refusal that fired during a session nobody was watching still reaches the
	// log. See CShellGameDll::DLLShutdown.
	ShellClientDll_PrintRefusals( m_Refusals );

	ClientWorldFactoryShutdown();


	ClientVoiceMgr_Shutdown();


	g_pClientMode->Disable();
	g_pClientMode->Shutdown();

	input->Shutdown_All();
	// 7b: C_BaseTempEntity::ClearDynamicTempEnts() was here -- see LevelInit
	// PreEntity for why the dynamic list is permanently empty.
	TermSmokeFogOverlay();
	view->Shutdown();
	g_pParticleSystemMgr->UncacheAllParticleSystems();
	UncacheAllMaterials();

	// THE MANAGED SIDE STOPS HERE, AND THIS IS THE POSITION IT ALREADY STOPPED
	// AT. Before this change the teardown was CSourceSharpSystem::Shutdown()'s,
	// reached from inside the walk below -- and that row was the ONLY Shutdown()
	// override among the forty in kClientSystems[] (every other row inherits
	// CClientSystem's empty body), so moving it to the line above the walk moves
	// it past no other body at all. Measured, not assumed.
	//
	// GUARDED, and not defensively: Init() returns false on many paths and the
	// engine calls Shutdown() after a failed Init(), so an unconditional forward
	// would turn "the managed runtime would not load" into a null dereference --
	// the one outcome that hides the diagnostic saying what was missing.
	// CShellGameDll::DLLShutdown carries the same guard for the same reason.
	if ( m_pManagedRuntime != NULL )
	{
		m_pManagedRuntime->OnSystemStop();
	}

	SourceSharp_ClientSystemsShutdown();

	// And released, because LoadManagedRuntime allocated it. AFTER the walk,
	// which is where the row cleared its own copy of the pointer -- so nothing
	// holds it by the time it is freed. Null-safe, so it needs no guard of its
	// own.
	UnloadManagedRuntime( m_pManagedRuntime );
	m_pManagedRuntime = NULL;

	gHUD.Shutdown();
	VGui_Shutdown();
	
	ShellParticleMgr_Term();
	
	// vgui_controls' .res file cache was cleared here; nothing loads a .res now.

	// ClientSteamContext().Shutdown() is managed: ClientModule.Shutdown.

	
	// This call disconnects the VGui libraries which we rely on later in the shutdown path, so don't do it
//	DisconnectTier3Libraries( );
	SourceSharp_DisconnectTier2();	// tier_connect.cpp (C9)
	ConVar_Unregister();
	DisconnectTier1Libraries( );

	gameeventmanager = NULL;

#if defined( WIN32 )
	// NVNT Disconnect haptics system
	DisconnectHaptics();
#endif
}


//-----------------------------------------------------------------------------
// Purpose: 
//  Called when the game initializes
//  and whenever the vid_mode is changed
//  so the HUD can reinitialize itself.
// Output : int
//-----------------------------------------------------------------------------
int CShellClientDLL::HudVidInit( void )
{
	
	gHUD.VidInit();

	GetClientVoiceMgr()->VidInit();

	return 1;
}

//-----------------------------------------------------------------------------
// Method used to allow the client to filter input messages before the 
// move record is transmitted to the server
//-----------------------------------------------------------------------------
void CShellClientDLL::HudProcessInput( bool bActive )
{
	g_pClientMode->ProcessInput( bActive );
}

//-----------------------------------------------------------------------------
// Purpose: Called when shared data gets changed, allows dll to modify data
// Input  : bActive - 
//-----------------------------------------------------------------------------
void CShellClientDLL::HudUpdate( bool bActive )
{
	float frametime = gpGlobals->frametime;

	GetClientVoiceMgr()->Frame( frametime );

	gHUD.UpdateHud( bActive );

	SourceSharp_ClientSystemsUpdate( frametime );

	// vgui_controls' global animation controller was stepped here; no panel in
	// this client is a vgui_controls panel, so nothing animates through it.


	// 7b: C_BaseTempEntity::CheckDynamicTempEnts() was here, under stock's own
	// "I don't think this is necessary any longer". It asked ShouldDestroy() of
	// every entry in s_pDynamicEntities and deleted the ones that said yes; the
	// list has no way to gain an entry in this build (see LevelInitPreEntity),
	// so it asked nothing of nobody every frame.
}

//-----------------------------------------------------------------------------
// Purpose: Called to restore to "non"HUD state.
//-----------------------------------------------------------------------------
void CShellClientDLL::HudReset( void )
{
	gHUD.VidInit();
	PhysicsReset();
}

//-----------------------------------------------------------------------------
// Purpose: Called to add hud text message
//-----------------------------------------------------------------------------
void CShellClientDLL::HudText( const char * message )
{
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
bool CShellClientDLL::ShouldDrawDropdownConsole()
{
	return true;
}

//-----------------------------------------------------------------------------
// Purpose: 
// Output : ClientClass
//-----------------------------------------------------------------------------
ClientClass *CShellClientDLL::GetAllClasses( void )
{
	return g_pClientClassHead;
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void CShellClientDLL::IN_ActivateMouse( void )
{
	input->ActivateMouse();
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void CShellClientDLL::IN_DeactivateMouse( void )
{
	input->DeactivateMouse();
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void CShellClientDLL::IN_Accumulate ( void )
{
	input->AccumulateMouse();
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void CShellClientDLL::IN_ClearStates ( void )
{
	input->ClearStates();
}

//-----------------------------------------------------------------------------
// Purpose: Engine can query for particular keys
// Input  : *name - 
//-----------------------------------------------------------------------------
bool CShellClientDLL::IN_IsKeyDown( const char *name, bool& isdown )
{
	// The button's state is managed's (ClientInput, lane c6-input): stock read
	// key->state off the kbutton_t FindKey returned.
	IManagedInputClient *pInput = ManagedInputClient();
	int32_t bDown = 0;
	if ( !pInput || !pInput->InputIsKeyDown( name, &bDown ) )
		return false;

	isdown = bDown != 0;
	return true;
}

//-----------------------------------------------------------------------------
// Purpose: Engine can issue a key event
// Input  : eventcode - 
//			keynum - 
//			*pszCurrentBinding - 
void CShellClientDLL::IN_OnMouseWheeled( int nDelta )
{
}

//-----------------------------------------------------------------------------
// Purpose: Engine can issue a key event
// Input  : eventcode - 
//			keynum - 
//			*pszCurrentBinding - 
// Output : int
//-----------------------------------------------------------------------------
int CShellClientDLL::IN_KeyEvent( int eventcode, ButtonCode_t keynum, const char *pszCurrentBinding )
{
	return input->KeyEvent( eventcode, keynum, pszCurrentBinding );
}

void CShellClientDLL::ExtraMouseSample( float frametime, bool active )
{
	// SOURCESHARP: STATIC-NS, and INVISIBLE to the armed survey -- an Assert
	// body is compiled out by the survey's -DNDEBUG, so these four sites are
	// reported by no release measurement and fail only in a debug armed build.
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
//-----------------------------------------------------------------------------
// Purpose: Fills in usercmd_s structure based on current view angles and key/controller inputs
// Input  : frametime - timestamp for last frame
//			*cmd - the command to fill in
//			active - whether the user is fully connected to a server
//-----------------------------------------------------------------------------
void CShellClientDLL::CreateMove ( int sequence_number, float input_sample_frametime, bool active )
{

	// SOURCESHARP: STATIC-NS, and INVISIBLE to the armed survey -- an Assert
	// body is compiled out by the survey's -DNDEBUG, so these four sites are
	// reported by no release measurement and fail only in a debug armed build.
	Assert( EntityAbs_AreRecomputationsEnabled() );
	Assert( EntityAbs_AreQueriesValid() );

	MDLCACHE_CRITICAL_SECTION();
	input->CreateMove( sequence_number, input_sample_frametime, active );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : *buf - 
//			from - 
//			to - 
//-----------------------------------------------------------------------------
bool CShellClientDLL::WriteUsercmdDeltaToBuffer( bf_write *buf, int from, int to, bool isnewcommand )
{
	return input->WriteUsercmdDeltaToBuffer( buf, from, to, isnewcommand );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : buf - 
//			buffersize - 
//			slot - 
//-----------------------------------------------------------------------------
void CShellClientDLL::EncodeUserCmdToBuffer( bf_write& buf, int slot )
{
	input->EncodeUserCmdToBuffer( buf, slot );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : buf - 
//			buffersize - 
//			slot - 
//-----------------------------------------------------------------------------
void CShellClientDLL::DecodeUserCmdFromBuffer( bf_read& buf, int slot )
{
	input->DecodeUserCmdFromBuffer( buf, slot );
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void CShellClientDLL::View_Render( vrect_t *rect )
{

	// UNDONE: This gets hit at startup sometimes, investigate - will cause NaNs in calcs inside Render()
	if ( rect->width == 0 || rect->height == 0 )
		return;

	// The frame's path and its recorder are the render plan's
	// (shell_view_executor_client.cpp).
	SourceSharp_ViewRender( rect );
}


//-----------------------------------------------------------------------------
// Gets the location of the player viewpoint
//-----------------------------------------------------------------------------
bool CShellClientDLL::GetPlayerView( CViewSetup &playerView )
{
	playerView = *view->GetPlayerViewSetup();
	return true;
}

//-----------------------------------------------------------------------------
// MATCHMAKING AND RICH PRESENCE -- FIVE DECLARED REFUSALS, plan_entity.md 7g.
//
// These five used to forward to `presence`, the IPresence singleton that
// game/client/basepresence.cpp defined. Step 7g deletes that file because this
// mod does not ship rich presence, and the deletion changes NOTHING a caller
// can observe: read basepresence.cpp on PC and every body it gave these five is
// empty or returns 0/NULL, each one under the comment "TODO: Implement for PC".
// That is the silent no-op §7 forbids, shipped by Valve; a refusal is the same
// answer with its name attached.
//
// THE ENGINE MAY STILL CALL THEM, so this is a refusal and not a deletion. The
// engine's matchmaking is built into the PC engine, and "CONTEXT_SCENARIO", a
// presence-ID name, is a string in the shipped bin/linux64/engine.so
// (`strings -a`). So an Xbox-style session on PC would reach these.
// Refusing by name is what makes that visible; returning NULL from a vanished
// singleton would have been a crash and a stub would have been a lie.
//
// The return values are stock's, unchanged: 0 from GetPresenceID and NULL from
// GetPropertyIdString are what CBasePresence answered.
//-----------------------------------------------------------------------------
void CShellClientDLL::SetupGameProperties( CUtlVector< XUSER_CONTEXT > &contexts, CUtlVector< XUSER_PROPERTY > &properties )
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

//-----------------------------------------------------------------------------
//
//-----------------------------------------------------------------------------
// THE SDK HALF OF THE FLUSH; THE FLUSH ITSELF GOES TO C# -- lane
// diag-visibility. The method below walked FirstBaseEntity(), which answers
// C_BaseEntity, and C_StandaloneEntity is not one -- so it reached ZERO managed
// entities and said nothing (dm_lockdown: 0). This half stays in THIS file
// because client_entity_resolve_test.cpp waives it by name for iterating the
// stock list; it stays at all because it frees m_pStudioHdr. The reasoning and
// the gates are entity_visibility_test.cpp.
int32_t SourceSharp_ClientEntitiesInvalidateMdlCacheAll( void )
{
	int32_t nInvalidated = 0;

	// THE SDK ARM STOOD HERE and there is no arm left: it walked the stock list
	// and asked GetBaseAnimating, a C_BaseEntity member and not one of
	// IGameEntity's, and it was never compiled. `nInvalidated` therefore starts
	// at 0 and stays there until the walk below adds to it; it is kept as the
	// accumulator so the return reads as a sum rather than a forward. Same shape
	// and same reason as the server's twin in shell_mdlcache.cpp.

	// THE HIERARCHY THIS MOD ACTUALLY HAS, which is the half that was
	// missing entirely. Declared here rather than included: shell_standalone_client.h
	// is 1500 lines of a class this file has no other use for.
	int32_t SourceSharp_StandaloneClientsInvalidateBoneCaches( void );

	return nInvalidated + SourceSharp_StandaloneClientsInvalidateBoneCaches();
}

void CShellClientDLL::InvalidateMdlCache()
{
	// The managed-built runtime (CE9a); NULL outside a load, which skips this.
	if ( IManagedRuntimeClient *pRuntime = ManagedRuntimeClient() )
		pRuntime->ClientInvalidateMdlCache();
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : *pSF - 
//-----------------------------------------------------------------------------
// CViewEffects::LevelInit: the managed shakes and fades start over.
static void ResetManagedViewEffects()
{
	if ( IManagedRenderClient *p = ManagedRenderClient() )
		p->ViewEffectsReset( 0 );
}

// ResetToneMapping( 1.0 ) (viewpostprocess.cpp): its native half; the managed
// half is ClientModule's.
static void ResetToneMappingScale()
{
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->ResetToneMappingScale( 1.0f );
}

// A screen fade, managed's (ViewEffects): stock's CViewEffects::Fade, from the
// engine's View_Fade and the map's start-dark fade.
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

//-----------------------------------------------------------------------------
// Purpose: Per level init
//-----------------------------------------------------------------------------
void CShellClientDLL::LevelInitPreEntity( char const* pMapName )
{
	// plan_map_load P8. Stock re-read every particle definition file here at
	// every level; managed decides whether this level needs that or only the
	// re-precache (ParticleReloadPolicy). With no runtime (outside a load), the
	// stock re-read.
	PumpWindowDuringLoad();
	int PrecacheDefinedParticleSystems();
	IManagedRuntimeClient *pRuntime = ManagedRuntimeClient();
	if ( !pRuntime || pRuntime->ParticlesNeedFullReload( m_nPreviousMapParticleFiles ) )
		ReloadParticleEffects();
	else
		PrecacheDefinedParticleSystems();
	PumpWindowDuringLoad();

	// HACK: Bogus, but the logic is too complicated in the engine
	if (g_bLevelInitialized)
		return;
	g_bLevelInitialized = true;

	input->LevelInit();

	ResetManagedViewEffects();
	
	//Tony; loadup per-map manifests. How many files it read is what the next
	// level's reload decision needs (P8).
	int ParseParticleEffectsMapFiles( const char *pMapName, bool bLoadSheets );
	m_nPreviousMapParticleFiles = ParseParticleEffectsMapFiles( pMapName, true );
	
	// Tell mode manager that map is changing
	modemanager->LevelInit( pMapName );
	ShellParticleMgr_LevelInit();


	// 7b: C_BaseTempEntity::ClearDynamicTempEnts() was here. It deleted every
	// entry of s_pDynamicEntities, which C_BaseTempEntity::Init() is the only
	// writer of, which nothing in this build calls -- the last native
	// C_BaseTempEntity subclass went inside a SOURCESHARP guard at C-Phase 3.
	// The per-level teardown of the temp entities this build DOES have is the
	// managed pool's, two lines below: tempents->LevelInit() is the seam into
	// ClientTempEntPool, and SourceSharp.Client's registry owns the rest.
	clienteffects->Flush();
	view->LevelInit();
	tempents->LevelInit();
	ResetToneMappingScale();

	// pMapName is not passed on. Stock's LevelInitPreEntityAllSystems used it
	// only to fill IGameSystem::MapName(), a static this build has no reader for
	// -- and a static is the one thing this phase is not putting back.
	SourceSharp_ClientSystemsLevelInitPreEntity();

	ResetWindspeed();

	// Stock's "don't do prediction if single player!" stood here: whether this
	// level predicts, and the cl_predict command that says so, is managed's
	// (LevelPrediction, lane M1-B). Past the gate above, so once per level, as
	// stock's was.
	if ( pRuntime )
		pRuntime->LevelInitPreEntity();

	// STOCK CHECKED LOW VIOLENCE FOR THIS MAP HERE
	// (`g_RagdollLVManager.SetLowViolence( pMapName )`, cdll_client_int.cpp:1709)
	// and the manager went with plan_shim.md 4a item 3. Its only reader was the
	// ragdoll LRU's ShouldRemoveThisRagdoll, and that list had no writer on
	// either link line, so the flag this line set was never read on this side
	// either. The live answer is the server's, computed once per map load by
	// SourceSharp.Server/Corpses.cs.

	gHUD.LevelInit();

}


//-----------------------------------------------------------------------------
// Purpose: Per level init
//-----------------------------------------------------------------------------
void CShellClientDLL::LevelInitPostEntity( )
{
	PumpWindowDuringLoad();
	SourceSharp_ClientSystemsLevelInitPostEntity();
	// C_PhysPropClientside::RecreateAll() stood here. It parsed the map's own
	// entity lump for prop_physics_multiplayer and func_proprrespawnzone and
	// built a client-only physics prop for each. Both halves of that are gone:
	// prop_physics_multiplayer is a C# server entity now, and
	// PropPhysicsMultiplayer.ResolveMode maps stock's CLIENTSIDE mode onto a
	// server-simulated one precisely because "this hierarchy has no
	// client-side prop simulation". Leaving the call meant the client built a
	// SECOND, unnetworked copy of a prop the server was already simulating.
	internalCenterPrint->Clear();
}

//-----------------------------------------------------------------------------
// Purpose: Reset our global string table pointers
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// Purpose: Per level de-init
//-----------------------------------------------------------------------------
void CShellClientDLL::LevelShutdown( void )
{
	// HACK: Bogus, but the logic is too complicated in the engine
	if (!g_bLevelInitialized)
		return;

	g_bLevelInitialized = false;

	// Disable abs recomputations when everything is shutting down
	EntityAbs_SetRecomputationsEnabled( false );

	// Level shutdown sequence.
	// First do the pre-entity shutdown of all systems
	SourceSharp_ClientSystemsLevelShutdownPreEntity();

	// C_PhysPropClientside::DestroyAll() stood here; it emptied the list that
	// RecreateAll above no longer fills.

	modemanager->LevelShutdown();

	// Remove temporary entities before removing entities from the client entity list so that the te_* may
	// clean up before hand.
	tempents->LevelShutdown();

	// Now release/delete the entities.
	//
	// THREE LINES WHERE STOCK HAD ONE, AND THE ORDER IS THE SERVER'S.
	//
	// `cl_entitylist->Release()` alone used to do all of this, because both of
	// the port's own populations are in the stock list: a C_StandaloneEntity is
	// put there by SourceSharp_ClientEntityBind and the C_World singleton by
	// C_World::Init, both through SourceSharp_LegacyListBind. The flip's client
	// removal set takes game/client/cliententitylist.cpp, so armed that walk
	// does not exist -- CClientEntityList::Release() and cl_entitylist are two
	// of the ten symbols the armed client link is owed, and this line is where
	// the second one is owed from.
	//
	// Lane `f-list` removed the bind with none of this in place and measured
	// what it costs on a running game: a segfault in the client shadow manager
	// on the SECOND map load, 0 of 5 runs, against 3 of 3 on the untouched tip.
	// Every shell survived the level change holding leaf-system and shadow
	// handles into subsystems this very function had already reset.
	//
	// SHELLS, THEN THE WORLD, THEN THE POST-ENTITY SYSTEM SHUTDOWN. That is the
	// server's order (SourceSharp_LevelShutdownStandaloneEntities runs, and
	// SourceSharp_LevelClearEntityList releases the world after it), and it is
	// the order in which the dependencies point: a shell's destructor can reach
	// the world, and nothing in the world's Release reaches a shell.
	//
	// THE THIRD TERM USED TO BE "what is left of the stock list" -- a walk over
	// the C_EntityClassList registry. Phase 1c deleted that walk, because
	// nothing on the client link line ever registered in it. What the two
	// teardowns actually have to precede is the line further down that resets
	// the SUBSYSTEMS a shell holds handles into, and that is what
	// shell_clientdll_test.cpp orders them against now.
	//
	// THE COUNTS ARE READ AND NOT DISCARDED. "The teardown destroyed n shells"
	// is a positive reading; a level shutdown that quietly destroyed nothing
	// looks exactly like one that had nothing to destroy, and that is the shape
	// of bug this whole sequence exists for. cs_cl_slot_table's `bound` counter
	// is the other end of the same assertion -- it must return to zero here.
	// Declared here rather than included, which is this file's own idiom for the
	// hierarchy (see InvalidateMdlCacheObjects): shell_standalone_client.h is
	// 1600 lines of a class this file has no other use for. The canonical
	// declaration is in that header, beside SourceSharp_StandaloneClientCount.
	int SourceSharp_StandaloneClientsLevelShutdown();

	const int nShells = SourceSharp_StandaloneClientsLevelShutdown();
	const bool bWorld = SourceSharp_ClientWorldLevelShutdown();

	// Msg and not DevMsg: this is the ONE native reading of a teardown that has
	// no dev-API surface at all -- the dev API's census is the server's list,
	// and this is the client's. tools/live-gate's own rule is that a log check
	// may be an ADDITION to a step's evidence and never its evidence, so the
	// step that reads this also asserts the server census reaches zero and that
	// the next map reaches its own entity count.
	Msg( "[SourceSharp] client level shutdown: destroyed %d standalone shell(s), "
	     "world %s\n", nShells, bWorld ? "released" : "was not bound" );

	// Now do the post-entity shutdown of all systems
	SourceSharp_ClientSystemsLevelShutdownPostEntity();

	view->LevelShutdown();
	beams->ClearBeams();
	// CParticleMgr::RemoveAllEffects: the new-style effects removed, then the
	// particle materials released (C7 particles phases 3 and 4).
	ShellNewParticles_RemoveAll();
	ShellParticleMgr_LevelShutdown();
	
	StopAllRumbleEffects();

	gHUD.LevelShutdown();

	internalCenterPrint->Clear();

	messagechars->Clear();

	// don't want to do this for TF2 because we have particle systems in our
	// character loadout screen that can be viewed when we're not connected to a server
	g_pParticleSystemMgr->UncacheAllParticleSystems();
	UncacheAllMaterials();

	// string tables are cleared on disconnect from a server, so reset our global pointers to NULL
	ResetStringTablePointers();
}


//-----------------------------------------------------------------------------
// Purpose: Engine received crosshair offset ( autoaim )
// Input  : angle - 
//-----------------------------------------------------------------------------
//
// No crosshair element exists (CSourceSharpHud is the client's one
// DECLARE_HUDELEMENT), so stock's GET_HUDELEMENT( CHudCrosshair ) answered NULL
// here and the autoaim offset went nowhere; the managed HUD draws the
// crosshair. The slot stays: the engine calls it.
//-----------------------------------------------------------------------------
void CShellClientDLL::SetCrosshairAngle( const QAngle& angle )
{
}

//-----------------------------------------------------------------------------
// Purpose: Helper to initialize sprite from .spr semaphor
// Input  : *pSprite - 
//			*loadname - 
//-----------------------------------------------------------------------------
void CShellClientDLL::InitSprite( CEngineSprite *pSprite, const char *loadname )
{
	if ( pSprite )
	{
		pSprite->Init( loadname );
	}
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : *pSprite - 
//-----------------------------------------------------------------------------
void CShellClientDLL::ShutdownSprite( CEngineSprite *pSprite )
{
	if ( pSprite )
	{
		pSprite->Shutdown();
	}
}

//-----------------------------------------------------------------------------
// Purpose: Tells engine how much space to allocate for sprite objects
// Output : int
//-----------------------------------------------------------------------------
int CShellClientDLL::GetSpriteSize( void ) const
{
	return sizeof( CEngineSprite );
}


//-----------------------------------------------------------------------------
// Purpose: 
// Input  : entindex - 
//			bTalking - 
//-----------------------------------------------------------------------------
void CShellClientDLL::VoiceStatus( int entindex, qboolean bTalking )
{
	GetClientVoiceMgr()->UpdateSpeakerStatus( entindex, !!bTalking );

	// And to the managed UI, whose voice panel shows who is speaking (C3; stock
	// CHudVoiceStatus / CHudVoiceSelfStatus asked the voice manager each think).
	if ( IManagedUiClient *pUi = ManagedUiClient() )
		pUi->VoiceStatus( entindex, bTalking ? 1 : 0 );
}


//-----------------------------------------------------------------------------
// Called when the string table for materials changes
//-----------------------------------------------------------------------------
void OnMaterialStringTableChanged( void *object, INetworkStringTable *stringTable, int stringNumber, const char *newString, void const *newData )
{
	// Make sure this puppy is precached
	g_ShellClientDLL.PrecacheMaterial( newString );
	RequestCacheUsedMaterials();
}


//-----------------------------------------------------------------------------
// Called when the string table for particle systems changes
//-----------------------------------------------------------------------------
void OnParticleSystemStringTableChanged( void *object, INetworkStringTable *stringTable, int stringNumber, const char *newString, void const *newData )
{
	// Make sure this puppy is precached
	g_pParticleSystemMgr->PrecacheParticleSystem( newString );
	RequestCacheUsedMaterials();
}


//-----------------------------------------------------------------------------
// Called when the string table for VGUI changes
//-----------------------------------------------------------------------------
void OnVguiScreenTableChanged( void *object, INetworkStringTable *stringTable, int stringNumber, const char *newString, void const *newData )
{
	// Stock precached the named vgui screen panel through the panel metaclass
	// manager. No vgui screen class is compiled into this client
	// (c_vguiscreen.cpp is not), so that manager could only ever answer NULL;
	// it left the link with vgui_controls (plan_client_novalve C3 step 4).
}

//-----------------------------------------------------------------------------
// Purpose: Preload the string on the client (if single player it should already be in the cache from the server!!!)
// Input  : *object - 
//			*stringTable - 
//			stringNumber - 
//			*newString - 
//			*newData - 
//-----------------------------------------------------------------------------
void OnSceneStringTableChanged( void *object, INetworkStringTable *stringTable, int stringNumber, const char *newString, void const *newData )
{
}

//-----------------------------------------------------------------------------
// Purpose: Hook up any callbacks here, the table definition has been parsed but 
//  no data has been added yet
//-----------------------------------------------------------------------------
void CShellClientDLL::InstallStringTableCallback( const char *tableName )
{
	// Here, cache off string table IDs
	if (!Q_strcasecmp(tableName, "VguiScreen"))
	{
		// Look up the id 
		g_StringTableVguiScreen = networkstringtable->FindTable( tableName );

		// When the material list changes, we need to know immediately
		g_StringTableVguiScreen->SetStringChangedCallback( NULL, OnVguiScreenTableChanged );
	}
	else if (!Q_strcasecmp(tableName, "Materials"))
	{
		// Look up the id 
		g_pStringTableMaterials = networkstringtable->FindTable( tableName );

		// When the material list changes, we need to know immediately
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
		// When the particle system list changes, we need to know immediately
		g_pStringTableParticleEffectNames->SetStringChangedCallback( NULL, OnParticleSystemStringTableChanged );
	}
	else if ( !Q_strcasecmp( tableName, "ServerMapCycle" ) )
	{
		g_pStringTableServerMapCycle = networkstringtable->FindTable( tableName );
	}

	InstallStringTableCallback_GameRules();
}


//-----------------------------------------------------------------------------
// Material precache
//-----------------------------------------------------------------------------
void CShellClientDLL::PrecacheMaterial( const char *pMaterialName )
{
	Assert( pMaterialName );

	int nLen = Q_strlen( pMaterialName );
	char *pTempBuf = (char*)stackalloc( nLen + 1 );
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
		if (IsOSX())
		{
			printf("\n ##### CShellClientDLL::PrecacheMaterial could not find material %s (%s)", pMaterialName, pTempBuf );
		}
	}
}

void CShellClientDLL::UncacheAllMaterials( )
{
	for (int i = m_CachedMaterials.Count(); --i >= 0; )
	{
		m_CachedMaterials[i]->DecrementReferenceCount();
	}
	m_CachedMaterials.RemoveAll();
}

//-----------------------------------------------------------------------------
// A user message the engine received. ONE FORWARD of (id, bf_read) to C#
// (plan_side_isolation §2.2a), which maps the id to its key and hands the
// payload to the managed handler. False outside a managed load.
//-----------------------------------------------------------------------------
// The message's payload crosses once, copied out of the engine's reader whole
// as one length-prefixed BitBlock (ss_bitblock.h; the engine's reader does not
// move). A payload past the block answers its prefix as minus the size it
// needed, and C# says so.
bool CShellClientDLL::DispatchUserMessage( int msg_type, bf_read &msg_data )
{
	IManagedUserMessagesClient *pMessages = ManagedUserMessagesClient();
	if ( !pMessages )
		return false;
	uint8_t block[ ss::kBitBlockHeaderBytes + kMaxUserMessageBlockBytes ];
	const int32_t nBits = ss::BitBlockFromEngine( msg_data, block, sizeof( block ) );
	if ( nBits < 0 )
		memcpy( block, &nBits, sizeof( nBits ) );
	return pMessages->DispatchUserMessage( msg_type, block, sizeof( block ) );
}


// SimulateEntities() stood here, empty: the think list's walk left with
// client_thinklist.cpp (plan_client_novalve C8, lane c6-input) and Simulate()
// is no IGameEntity member, so it ran nothing. It and its one call in
// OnRenderStart went in lane M1-B; the managed entity's per-frame work is the
// managed frame's.



//-----------------------------------------------------------------------------
// THE TWO PVS-NOTIFY WALKS, AND ARMED THE LIST THEY WALK CANNOT BE POPULATED.
//
// This is the last thing in the port that names `cl_entitylist`, and it is
// guarded rather than re-pointed because there is nothing to re-point it AT:
// the list is CClientEntityList's own, and the ONLY two lines in the tree that
// ever put a row in it are CClientEntityList::AddPVSNotifier's callers,
// game/client/cliententitylist.cpp:279 and :300 -- a removal-set row.
//
// AND NO OCCUPANT SURVIVES EITHER, which is the half that makes this a dead
// walk rather than a removed capability. A row is made only for a renderable
// whose GetPVSNotifyInterface() answers non-NULL, and in the WHOLE TREE --
// headers included, swept rather than listed -- exactly ONE does:
//
//     c_baseanimating.h:761     `return this`   <- C_ClientRagdoll's, and
//                               C_ClientRagdoll's alone. It is declared inside
//                               `class C_ClientRagdoll : public C_BaseAnimating,
//                               public IPVSNotify`, NOT in C_BaseAnimating, and
//                               it is defined in c_baseanimating.cpp, which
//                               tools/client-flip-closure's DEFAULT_SEED removes.
//
//     c_baseentity.cpp:1562     `return NULL`   <- C_BaseEntity's. An earlier
//                               version of this banner listed it among the
//                               implementations that matter; it answers NULL,
//                               so no ordinary entity has ever been a notifier.
//     c_world.h:369             `return NULL`
//     shell_standalone_client.h:554 `return NULL`   -- every managed entity
//     iclientrenderable.h:218   `return NULL`   -- the default
//     detailobjectsystem.cpp:634 `return NULL`  -- CDetailModel, and it never
//                               reaches this list in any case.
//
// MEASURED, NOT ARGUED -- lane `pvs-ragdoll`, tools/live-gate on dm_lockdown,
// two passes in one process, so across a map RELOAD and not one map load:
//
//     AddPVSNotifier entered 371 times in level 1 and 354 more in level 2.
//     Rows added: 0.  C_ClientRagdoll constructed by the game: 0.
//     The walks ran 161,873 times and visited 79,626 rows, EVERY ONE of them
//     belonging to a C_ClientRagdoll the probe itself built as a positive
//     control -- which entered and left the PVS three times and got its three
//     CreateShadow/DestroyShadow pairs, so the machinery is live and working.
//
// So the list is empty because nothing in this build is a client ragdoll, and
// armed it is empty because C_ClientRagdoll is deleted outright. Those are two
// different reasons and both hold.
//
// AND THIS IS NOT THE SHADOW-LEAK CRASH CLASS, which is the question a reader
// of `f-list` and `shell-lifetime` will arrive with. A C_ClientRagdoll's shadow
// is created by InitAsClientRagdoll (`DestroyShadow(); CreateShadow();`) and
// destroyed by its own Release -> RemoveFromLeafSystem -> DestroyShadow. The
// notify only TOGGLES it while the body is out of the PVS. Guarding these walks
// out therefore loses an optimisation; it cannot strand a shadow in a manager
// that is about to be reset.
//
// SO THE CAPABILITY IS STATED PLAINLY RATHER THAN GLOSSED: armed, no client
// entity is told when it enters or leaves the PVS. Nothing asks to be told.
// Lane `f-list` is the reason this is spelled out -- its own refusal note
// argued the list was "empty by construction" and quoted the c_baseanimating
// inline IN SUPPORT while it returned `this`. It had read a STATIC REACHABILITY
// argument as a LIFETIME argument. The facts above are pinned by
// src/sourcesharp/test/pvs_notify_ruling_test.cpp, which SWEEPS for a second
// non-NULL implementation rather than listing the ones we know about, and which
// strips comments first -- its first run named THIS BANNER as an implementation.
//-----------------------------------------------------------------------------
void UpdateClientRenderableInPVSStatus()
{
}

// The banner above UpdateClientRenderableInPVSStatus carries the argument for
// both halves; this is the same list, walked at the other end of the frame.
void UpdatePVSNotifiers()
{
}


void OnRenderStart()
{
	MDLCACHE_CRITICAL_SECTION();
	MDLCACHE_COARSE_LOCK();

	::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, true );
	EntityAbs_SetQueriesValid( false );

	// Rope_ResetCounters() was here: it zeroed c_rope.cpp's VPROF tick
	// counters, which left with the stock rope (the ropes are C#'s).
	// UpdateLocalPlayerVisionFlags() was here: it read C_BasePlayer's NULL local
	// player into two counters nothing imported (lane M1-B).

	// EntityInterp_InterpolateServerEntities stood here. Its two passes walked
	// SDK lists that retired with C_BaseEntity; its frame-level policy is
	// SourceSharp.Client's InterpolationGate, asked by the managed render pass.

	{
		// vprof node for this bloc of math
		EntityAbs_SetQueriesValid( true );
		EntityAbs_SetRecomputationsEnabled( true );

		// SOURCESHARP: EntityInterp_MarkAimEntsDirty stood here. It walked
		// g_AimEntsList, which only C_BaseEntity added to; a managed entity
		// follows its source entity's BONES per draw instead.
		// RETIRED and ruled: `cs_cl_entitystatics`.
	}

	// Make sure the camera simulation happens before OnRenderStart, where it's used.
	// NOTE: the only thing that happens in CAM_Think is thirdperson related code.
	input->CAM_Think();

	// This will place the player + the view models + all parent
	// entities	at the correct abs position so that their attachment points
	// are at the correct location
	view->OnRenderStart();

	RopeManager()->OnRenderStart();
	
	::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, false );

	// Deliver the world's deferred OnDataChanged, if one is owed.
	//
	// THIS WAS A QUEUE DRAIN. Phase 1c inlined the queue at its one producer:
	// C_World was the only IClientNetworkable on the client link line that ever
	// registered, so the list could hold at most one element. The DEFERRAL is
	// the part that matters and it is unchanged -- OnDataChanged must see a
	// complete net update, and PreDataUpdate runs before the fields arrive.
	if ( C_World *pWorld = GetClientWorldEntity() )
	{
		pWorld->DeliverPendingDataChanged();
	}

	// Reset the overlay alpha. Entities can change the state of this in their think functions.
	g_SmokeFogOverlayAlpha = 0;	

	// This must occur prior to SimulatEntities,
	// which is where the client thinks for c_colorcorrection + c_colorcorrectionvolumes
	// update the color correction weights.
	// FIXME: The place where IGameSystem::Update is called should be in here
	// so we don't have to explicitly call ResetColorCorrectionWeights + SimulateEntities, etc.
	void SourceSharp_ColorCorrectionResetWeights();
	SourceSharp_ColorCorrectionResetWeights();

	PhysicsSimulate();

	{
		// This creates things like temp entities.
		engine->FireEvents();

		// Update temp entities
		tempents->Update();

		// Update temp ent beams (the managed beam system, through `beams`).
		// Stock locked the frame from beam additions here and unlocked it at
		// FRAME_START (SetBeamCreationAllowed); the only reader of that flag
		// was #if 0'd in CreateGenericBeam, so both stores left with
		// view_beams.cpp.
		beams->UpdateTempEntBeams();
	}

	// Update particle effects (eventually, the effects should use Simulate() instead of having
	// their own update system).
	{
		// Enable FP exceptions here when FP_EXCEPTIONS_ENABLED is defined,
		// to help track down bad math.
		FPExceptionEnabler enableExceptions;
		// The old-style effects first, as UpdateAllEffects updated them before
		// the new ones: managed's (C7 particles phase 2.3).
		ShellParticles_Frame( gpGlobals->frametime );
		// The new-style effects', which UpdateAllEffects updated after the
		// old (C7 particles phase 3).
		ShellNewParticles_Frame( gpGlobals->frametime );
	}

	// SOURCESHARP: EntityInterp_CalcAimEntPositions stood here -- the other half
	// of MarkAimEntsDirty over the same empty list, reaching a private
	// C_BaseEntity member by friendship.
	// RETIRED and ruled: `cs_cl_entitystatics`.

	// SOURCESHARP: stock's `if ( ToolsEnabled() ) EntityTool_RecordEntities();`
	// stood here, posting bone messages to the tools. Deleted: SFM is dead
	// (user ruling 2026-10-19).

	// SOURCESHARP: EntityRender_AddVisibleEntities stood here. It updated
	// visibility for CLIENT-CREATED predictables only, off a list only
	// C_BaseEntity added to; a managed entity reaches the leaf system through
	// the render-policy pass. RETIRED and ruled: `cs_cl_entitystatics`.
}


void OnRenderEnd()
{
	UpdatePVSNotifiers();
}



//-----------------------------------------------------------------------------
// THE CLIENT FRAME, AND THE ONE CROSSING IT MAKES -- plan_entity.md 5b.
//
// §3.4: "the server frame is GameFrame -> managed RunFrame(curtime); the client
// frame is FrameStageNotify(stage) -> managed OnStage(stage)." This is that
// sentence, and the ClientStage slot below is the whole of the client's
// per-frame path into managed code.
//
// EXACTLY ONE CALL, UNCONDITIONALLY, AND BEFORE THE SWITCH.
//
//   * ONE, because a slot per stage would make the client's per-frame crossing
//     count grow with the number of things ported -- the shape §3.4 forbids on
//     the server, where 38,578 EntityIsValid calls in a single frame is the
//     tree's own worked example of what it costs.
//   * UNCONDITIONALLY, including the four stages managed code currently does
//     nothing in. A native filter is a policy decision living in C++, which is
//     what this plan removes; and a stage that is silently never delivered is
//     indistinguishable, from managed code, from a stage the engine never
//     raised. What a stage MEANS is decided in C#.
//   * BEFORE the switch, not after it. The stages are ordered and the native
//     work in the switch is what the stage announces is about to happen (or has
//     just happened); managed code that will eventually run alongside that work
//     -- interpolation at NET_UPDATE_END, the render tail at RENDER_START --
//     wants to be told before the native arm runs, not after. Moving this line
//     below the switch is a behaviour change, not a tidy-up.
//
// THE STAGE NUMBER IS PASSED THROUGH UNCHANGED. ClientFrameStage_t is the
// ENGINE's enum (public/cdll_int.h:135) and managed code mirrors it
// (SourceSharp.Abi/ClientFrameStage.cs); translating here would put a second
// numbering in the tree for no reader's benefit.
//
// COST, for the performance pass that follows the port: one null test and one
// indirect call per stage, seven stages per client frame, plus the interlocked
// increment every counted thunk pays (Crossings.Count). Measured as
// crossings-per-client-frame by `cs_cl_crossings`; the ClientStage row is
// exactly the stage count and nothing else.
//-----------------------------------------------------------------------------

// How many times the crossing below was actually MADE. It was a function-local
// static; it is m_nClientStageCrossings, a member of the object that does the
// crossing, and this free function -- read by ss_cl_stage_crossings at the
// bottom of the file -- is the proxy onto it.
uint64_t SourceSharp_ClientStageCrossings()
{
	return g_ShellClientDLL.ClientStageCrossings();
}

uint64_t CShellClientDLL::ClientStageCrossings() const
{
	return m_nClientStageCrossings;
}

// How many times the ENGINE called FrameStageNotify. It used to be read off the
// IBaseClientDLL probe's own row; the probe was a measuring instrument whose
// measurement is finished (docs/clientdll-boundary.md), so the one number
// ss_cl_stage_crossings still needs is kept here -- a FIELD on the object the
// engine calls, for the same reason m_Refusals is one.
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

	// THE ONE CROSSING. See the block comment above for why it is here, why
	// there is one of it, and why no stage is filtered out.
	// The runtime is managed-built (plan_client_vtables CE1); NULL before the
	// first load and after the last unload, which is when this is skipped.
	if ( IManagedRuntimeClient *pRuntime = ManagedRuntimeClient() )
	{
		++m_nClientStageCrossings;
		pRuntime->ClientStage( (int32_t)curStage );
	}

	switch( curStage )
	{
	default:
		break;

	case FRAME_RENDER_START:
		{

			// Last thing before rendering, run simulation.
			OnRenderStart();
		}
		break;
		
	case FRAME_RENDER_END:
		{
			OnRenderEnd();

			PREDICTION_SPEWVALUECHANGES();
		}
		break;
		
	case FRAME_NET_UPDATE_START:
		{
			// disabled all recomputations while we update entities
			EntityAbs_SetRecomputationsEnabled( false );
			EntityAbs_SetQueriesValid( false );
			// Interpolation_SetLastPacketTimeStamp left with interpolatedvar.cpp (C8):
			// only CInterpolatedVar read the stamp, and the managed rings keep their own.
			::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, true );

			PREDICTION_STARTTRACKVALUE( "netupdate" );
		}
		break;
	case FRAME_NET_UPDATE_END:
		{
			ProcessCacheUsedMaterials();

			// reenable abs recomputation since now all entities have been updated
			EntityAbs_SetRecomputationsEnabled( true );
			EntityAbs_SetQueriesValid( true );
			::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, false );

			PREDICTION_ENDTRACKVALUE();
		}
		break;
	case FRAME_NET_UPDATE_POSTDATAUPDATE_START:
		{
			PREDICTION_STARTTRACKVALUE( "postdataupdate" );
		}
		break;
	case FRAME_NET_UPDATE_POSTDATAUPDATE_END:
		{
			PREDICTION_ENDTRACKVALUE();
			// Let prediction copy off pristine data
			prediction->PostEntityPacketReceived();
			// step 7g: `HLTVCamera()->PostEntityPacketReceived()` stood here.
			// Its body was one flag store read only by the camera this build
			// does not ship, and it ran on EVERY net update, HLTV or not --
			// so there is no behaviour to lose and nothing to refuse.
		}
		break;
	case FRAME_START:
		{
			// Mark the frame as open for client fx additions
			SetFXCreationAllowed( true );

			// SOURCESHARP: EntityInterp_CheckCLInterpChanged stood here. It
			// notified per-entity CInterpolatedVar histories of a changed
			// cl_interp; those are managed rings now and
			// StandaloneInterpolationAmount() re-reads the amount every fetch,
			// so nothing has to be notified.
			// RETIRED and ruled: `cs_cl_entitystatics`.
		}
		break;
	}
}


// Save/restore system hooks.
//
// THE BODIES ARE OUT OF THIS BUILD AND THE METHODS ARE NOT, which is the same
// split the server shell already makes. These eight are IBaseClientDLL
// virtuals: the engine holds their slots and calls them by offset, so they have
// to exist. What they DO used to be nothing at all, because
// game/shared/saverestore.cpp is out of the file list (plan_shared.md Phase 2,
// docs/client_evictions.md) and the whole feature was refused on the server.
//
// THAT IS NO LONGER TRUE, AND THE NULL WAS MEASURED STOPPING A REAL SAVE.
// Stage 1 of the save shim (docs/managed-saverestore.md) made the server's
// cluster answer, and the very first live `save` at `-maxplayers 1` then wrote
// `save/dm_lockdown.hl1` (5,415 bytes) and `.hl3` -- and NO `.hl2` and NO
// `.sav`, in total silence. `.HL2` is the CLIENT state file: the engine's
// SaveClientState asks THIS class for a buffer first, and on the NULL below it
// abandons the save and returns, which takes SaveGameSlot's `.sav` with it.
// docs/managed-saverestore.md §5 item 4 predicted exactly this -- "SaveInit
// answering NULL is the load-bearing one, because it is what the engine asks
// first" -- and it is now a transcript rather than a prediction.
//
// So the three that move bytes answer, and each is one forward into
// IManagedSaveRestoreClient (the client save codec): the shared managed
// SaveBlockSession (src/shared/interop/SaveBlockSession.cs) sizes, allocates
// through IVEngineClient's SaveAllocMemory and places the block, and encodes
// the engine's DECALLIST and MUSICLIST structs as stock's CSave does. WHAT
// THEY DO NOT DO is write client entity state -- no more than the server
// writes server entity state through a datamap. The client entity block a
// save produces is EMPTY, which is a complete answer to the engine (it wants
// a file, and an empty one round-trips) and not a partial one.
CSaveRestoreData  *CShellClientDLL::SaveInit( int size )
{
	IManagedSaveRestoreClient *pArea = ManagedSaveRestoreClient();
	CSaveRestoreData *pSaveData = pArea
		? static_cast< CSaveRestoreData * >( pArea->SaveInit( size, gpGlobals->curtime ) )
		: NULL;

	if ( pSaveData == NULL )
	{
		// The engine abandons the save here and prints nothing -- which is
		// exactly what the NULL this replaced looked like from outside, and
		// exactly why it took a save-file audit to find. The session has
		// already named which allocation failed (or there is no managed side).
		Warning( "[SourceSharp] client SaveInit is answering NULL: the engine will "
		         "abandon the client state file and, with it, the whole save, "
		         "printing nothing\n" );
	}

	return pSaveData;
}

// pMap IS UNUSED, as stock's is: CSave::WriteFields takes it only for
// FTYPEDESC_OVERRIDE chains, which the engine's flat maps do not have.
// sizeof( typedescription_t ) is passed rather than known in C#: the struct
// holds a member-function pointer, whose width is the C++ ABI's.
void CShellClientDLL::SaveWriteFields( CSaveRestoreData *pSaveData, const char *pname, void *pBaseData, datamap_t *pMap, typedescription_t *pFields, int fieldCount )
{
	if ( IManagedSaveRestoreClient *pArea = ManagedSaveRestoreClient() )
		pArea->SaveWriteFields( pSaveData, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ), fieldCount );
}

void CShellClientDLL::SaveReadFields( CSaveRestoreData *pSaveData, const char *pname, void *pBaseData, datamap_t *pMap, typedescription_t *pFields, int fieldCount )
{
	if ( IManagedSaveRestoreClient *pArea = ManagedSaveRestoreClient() )
		pArea->SaveReadFields( pSaveData, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ), fieldCount );
}

// THE REMAINING SIX MOVE NO BYTES, and that is the same decision the server's
// PreSave/Save/WriteSaveHeaders/Restore make for the same reason: what they
// would encode is game entity state, and this port's entities are not saved
// through a datamap. Their stock bodies are g_pGameSaveRestoreBlockSet, which
// left both builds in Phase 2. A no-op here is symmetric across save and load,
// so the cursor agrees on both sides of the file.
void CShellClientDLL::PreSave( CSaveRestoreData *s )
{
}

void CShellClientDLL::Save( CSaveRestoreData *s )
{
}

void CShellClientDLL::WriteSaveHeaders( CSaveRestoreData *s )
{
}

void CShellClientDLL::ReadRestoreHeaders( CSaveRestoreData *s )
{
}

void CShellClientDLL::Restore( CSaveRestoreData *s, bool b )
{
}

// THE RESTORE LIST IS GONE (plan_client_novalve C8, lane c6-input). Its one
// writer was the free AddRestoredEntity, game/shared/saverestore.cpp's proxy,
// which no compiled TU calls (nm -u over the link line), so the list was
// always empty and this walk visited nothing. A managed entity's restore is a
// C# concern. The slot stays: the engine calls it.
void CShellClientDLL::DispatchOnRestore()
{
}

void CShellClientDLL::WriteSaveGameScreenshot( const char *pFilename )
{
	view->WriteSaveGameScreenshot( pFilename );
}

// The engine's two close-caption slots. Stock handed them to the
// CHudCloseCaption element; no such element is created here (CSourceSharpHud
// is the one DECLARE_HUDELEMENT), so GET_HUDELEMENT answered NULL and neither
// body did anything. The captions this client shows are managed's (the
// client emitter forwards the close caption's facts to C#).
void CShellClientDLL::EmitSentenceCloseCaption( char const *tokenstream )
{
}

void CShellClientDLL::EmitCloseCaption( char const *captionname, float duration )
{
}

CStandardRecvProxies* CShellClientDLL::GetStandardRecvProxies()
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

void CShellClientDLL::OnDemoRecordStart( char const* pDemoBaseName )
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

void CShellClientDLL::OnDemoPlaybackStart( char const* pDemoBaseName )
{
}

void CShellClientDLL::OnDemoPlaybackStop()
{
}

int CShellClientDLL::GetScreenWidth()
{
	return ScreenWidth();
}

int CShellClientDLL::GetScreenHeight()
{
	return ScreenHeight();
}

// NEW INTERFACES
// save game screenshot writing
void CShellClientDLL::WriteSaveGameScreenshotOfSize( const char *pFilename, int width, int height, bool bCreatePowerOf2Padded/*=false*/,
											   bool bWriteVTF/*=false*/ )
{
	view->WriteSaveGameScreenshotOfSize( pFilename, width, height, bCreatePowerOf2Padded, bWriteVTF );
}

// See RenderViewInfo_t
void CShellClientDLL::RenderView( const CViewSetup &setup, int nClearFlags, int whatToDraw )
{
	view->RenderView( setup, nClearFlags, whatToDraw );
}

void ReloadSoundEntriesInList( IFileList *pFilesToReload );

//-----------------------------------------------------------------------------
// For sv_pure mode. The filesystem figures out which files the client needs to reload to be "pure" ala the server's preferences.
//-----------------------------------------------------------------------------
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

// Get the mouthinfo for the sound being played inside UI panels. The buffer was
// a file-scope CMouthInfo; it is m_ClientUIMouth, and nothing outside this file
// names it, so it needs no proxy.
CMouthInfo *CShellClientDLL::GetClientUIMouthInfo()
{
	return &m_ClientUIMouth;
}

void CShellClientDLL::FileReceived( const char * fileName, unsigned int transferID )
{
	if ( g_pGameRules )
	{
		g_pGameRules->OnFileReceived( fileName, transferID );
	}
}

void CShellClientDLL::ClientAdjustStartSoundParams( StartSoundParams_t& params )
{
}

const char* CShellClientDLL::TranslateEffectForVisionFilter( const char *pchEffectType, const char *pchEffectName )
{
	if ( !GameRules() )
		return pchEffectName;

	return GameRules()->TranslateEffectForVisionFilter( pchEffectType, pchEffectName );
}

bool CShellClientDLL::DisconnectAttempt( void )
{
	bool bRet = false;

	return bRet;
}

bool CShellClientDLL::IsConnectedUserInfoChangeAllowed( IConVar *pCvar )
{
	return GameRules() ? GameRules()->IsConnectedUserInfoChangeAllowed( NULL ) : true;
}

//-----------------------------------------------------------------------------
// THE TWO METHODS NOTHING CALLS.
//
// No live run has observed the engine calling either one. Both are TF-only
// additions -- their bodies are entirely inside `#if defined( TF_CLIENT_DLL )`
// and this mod is not TF -- so even a caller would reach a body that does
// nothing.
//
// THAT IS EVIDENCE, NOT PROOF, and the refusal is shaped accordingly: a method
// no run reached may still have a caller on a path no run took. So these two
// do NOT assert, do NOT crash and do NOT change what they return: they return the
// same harmless value they always did, and RECORD that the shell had no answer.
// `cs_cl_refusals` prints a row per refused method, so a live run that reaches
// either one says so in a table instead of taking the client down.
//-----------------------------------------------------------------------------
bool CShellClientDLL::BHaveChatSuspensionInCurrentMatch()
{
	SS_CLIENTDLL_UNREACHABLE( "IBaseClientDLL::BHaveChatSuspensionInCurrentMatch" );
	return false;
}

void CShellClientDLL::DisplayVoiceUnavailableMessage()
{
	SS_CLIENTDLL_UNREACHABLE( "IBaseClientDLL::DisplayVoiceUnavailableMessage" );
}

// GetSteamIDForPlayerIndex is gone (C6, Steam to managed): no object on the
// client link line imported it (nm -u), and its steamapicontext went with
// clientsteamcontext.cpp.



//-----------------------------------------------------------------------------
// THE REFUSAL READOUT -- the server's cs_sv_refusals, client side.
//
// "Play a map, then read the counter." ZERO IS THE EVIDENCE; a client that did
// not crash is not, because both refusals are no-ops whose absence nothing
// would notice. A non-zero row is a FINDING and not a failure: it names a
// method 5b argued was unreachable and was not.
//
// A ConCommand rather than a dev-API verb, for cs_sv_refusals' reason: that
// API's subcommands are managed and this number is native.
//-----------------------------------------------------------------------------
CON_COMMAND( cs_cl_refusals,
	"SourceSharp: how many times each refused IBaseClientDLL method was called (plan_entity.md 5b)" )
{
	ShellClientDll_PrintRefusals( g_ShellClientDLL.Refusals() );
}

//-----------------------------------------------------------------------------
// THE STAGE COUNT, native side.
//
// SourceSharp_FrameStageNotifyCalls() says how many times the engine called
// FrameStageNotify. This says how many times the shell CROSSED, which is the other
// half of "one crossing per stage": the two numbers must be equal, and a
// difference is either a filtered stage (there are none, on purpose) or a
// managed runtime that was not up.
//
// The managed counter is the authority -- `cs_cl_crossings` prints the
// ClientStage row from SourceSharp.Abi's own table -- and this is the native
// side of the same fact, kept because it counts even before the runtime loads
// and because a gate that reads only one side cannot tell "never crossed" from
// "never called".
//-----------------------------------------------------------------------------
uint64_t SourceSharp_ClientStageCrossings();
uint64_t SourceSharp_FrameStageNotifyCalls();

CON_COMMAND( ss_cl_stage_crossings,
	"ss_cl_stage_crossings -- FrameStageNotify calls vs ClientStage crossings" )
{
	Msg( "[SourceSharp] FrameStageNotify: %llu call(s), %llu crossing(s) into managed code\n",
	     (unsigned long long)SourceSharp_FrameStageNotifyCalls(),
	     (unsigned long long)SourceSharp_ClientStageCrossings() );
}

//-----------------------------------------------------------------------------
// IShimNetClient::SendServerMessage's body: a CSourceSharpClientMessage
// (ss_client_message.h) on the engine's net channel to the server, reliable.
// Managed code decides what to send and when; this carries it. Here, not in
// a TU of its own: bitbuf.h brings mathlib's static vectors, and this TU
// already has them, so the message adds no static initialiser.
//-----------------------------------------------------------------------------
int32_t SourceSharp_SendServerMessage( const char *pKey, const uint8_t *pData, int32_t nLen )
{
	// The channel the engine has to the server; none when not connected.
	INetChannel *pChannel = engine ? static_cast< INetChannel * >( engine->GetNetChannelInfo() ) : NULL;
	if ( !pChannel )
		return 0;

	// The message is ~4 KiB: on the heap, not the stack, and gone once
	// SendNetMsg has written it into the reliable stream.
	CSourceSharpClientMessage *pMsg = new CSourceSharpClientMessage( NULL, SourceSharp_WriteClientMessage );
	const bool bSent = pMsg->Set( pKey, pData, nLen ) && pChannel->SendNetMsg( *pMsg, true );
	delete pMsg;
	return bSent ? 1 : 0;
}

//-----------------------------------------------------------------------------
// WHAT CNetGraphPanel READ, for the dev API's /client/{slot}/netgraph
// (plan_client_novalve §4.4). Three forwards onto the engine's
// INetChannelInfo, in fixed orders the managed reader names
// (SourceSharp.Client.Interop.ClientNetChannel); each answers how many values
// it wrote, 0 with no channel. Mechanism only: which frames, what to sum and
// what to call a spike is managed's.
//-----------------------------------------------------------------------------
static INetChannelInfo *ShellNetChannel()
{
	return engine ? engine->GetNetChannelInfo() : NULL;
}

static int32_t ShellPut( float *pOut, int32_t nOut, int32_t i, float flValue )
{
	if ( pOut && i < nOut )
		pOut[ i ] = flValue;
	return i + 1;
}

// latency, avg latency, avg loss, avg choke, avg data, avg packets, total
// data, sequence number: one flow (0 outgoing, 1 incoming).
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

// net time, time connected, buffer size, data rate, loopback, timing out,
// time since last received, remote frame time and its deviation, timeout
// seconds, this client's interpolation amount.
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

// One packet of one flow, by sequence number: valid, send time, command
// interpolation amount, response latency (ms), choke, then its bytes per
// message group, INetChannelInfo::GENERIC through TOTAL.
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

//-----------------------------------------------------------------------------
// THE CLIENT DLL'S REFUSALS, for the dev API's /client/{slot}/absences: one
// refused method per index, with how often it was reached; the answer is how
// many methods, and the total through pOutTotal. A readout of the ledger
// cs_cl_refusals prints.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientRefusal( int32_t iIndex, char *pOutMethod, int32_t nMethodBytes,
                                   int32_t *pOutCount, int32_t *pOutTotal )
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
