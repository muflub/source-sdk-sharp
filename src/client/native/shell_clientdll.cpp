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
#include "prediction.h"

#include "smoke_fog_overlay.h"
#include "ienginevgui.h"
#include "iefx.h"
#include "enginesprite.h"

#include "c_te_legacytempents.h"
#include "c_rope.h"

#include "engine/IStaticPropMgr.h"

#include "materialsystem/imaterialsystemstub.h"
#include "VGuiMatSurface/IMatSystemSurface.h"
#include "engine/ivdebugoverlay.h"
#include "vguicenterprint.h"
#include "iviewrender_beams.h"
#include "engine/IEngineTrace.h"

#include "physics.h"
#include "saverestore.h"

#include "client_world.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "iclientmode.h"
#include "inputsystem/iinputsystem.h"

#include "particle_parse.h"
#include "mouthinfo.h"

#include "engine/IEngineSound.h"

#include "cdll_util.h"

#include "networkstringtabledefs.h"

#include "usercmd.h"
#include "ss_client_message_write.h"

// The managed runtime this DLL loads, and the environment it loads it from.
#include "sourcesharp_gamesystem.h"
#include "shell_managed_areas_client.h"
#include "shell_view_executor_client.h"
#include "shell_map_material_reload.h"
#include "shell_effects_list_client.h"
#include "shell_effects_client.h"
#include "shell_managed_particles.h"
#include "bound_callback.h"
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

#include "ss_command_line.h"
#include "shell_particle_systems_client.h"
#include "shell_particle_query_client.h"
#include "shell_detail_props.h"
#include "shell_sound_patches_client.h"
#include "shell_prediction_slots_client.h"
#include "shell_worldquery_object.h"
#include "shell_precipitation_queries_client.h"
#include "shell_dust_render_object.h"
#include "shell_leaf_system_client.h"
#include "shell_shadows_client.h"
#include "shell_te_tables_client.h"
#include "shell_te_system_client.h"
#include "shell_precipitation_draw_client.h"
#include "shell_particle_manifest_client.h"
#include "shell_hud_ui_client.h"
#include "shell_clientdll_exports.h"
#include "shell_interface_member.h"
#include "shell_prediction_client.h"
#include "shell_worldtext_client.h"
#include "shell_glow_client.h"
#include "shell_pixelvis_client.h"
#include "shell_material_proxies_client.h"
#include "shell_beams_client.h"
#include "shell_sprites_client.h"
#include "shell_new_particles.h"
#include "shell_shim_console.h"
#include "managed_side.h"
#include "diagnostics_hooks.h"
#include "convar_registry.h"
#include "shell_log.h"
#include "tier0_spew_port.h"
#include "game_events.h"
#include "shell_shim_areas.h"
#include "shell_shim_root_client.h"
#include "shell_convars_client.h"
#include "shell_input.h"
#include "shell_managed_frame_system.h"
#include "shell_clientsystems.h"
#include "shell_physics_client.h"
#include "shell_decals_client.h"
#include "shell_standalone_clients.h"
#include "client_entity_list.h"
#include "shell_client_mode.h"
#include "shell_viewer_facts.h"
#include "shell_view_render.h"
#include "shell_material_cache_client.h"
#include "shell_vgui_object.h"
#include "shell_worldrender_object.h"
#include "shell_debugoverlay_object.h"
#include "shell_te_backends_object.h"
#include "shell_rope_client.h"
#include "shell_net_channel_client.h"
#include "shell_client_engine_interfaces.h"
#include "shell_gamemovement_client.h"
#include "shell_gamerules_client.h"
#include "shell_engine_area_client.h"
#include "shell_render_targets.h"

CClientSystem* ClientSystem_CreateDirtySpatialPartition(const CClientEngineInterfaces& engines);
#include "shell_effect_ledger.h"
#include "shell_view_targets.h"
#include "shell_wind_client.h"
#include "shell_member_command.h"
#include "shell_diagnostics_commands.h"
#include "shell_te_commands_client.h"
#include "shell_soundscape_audio_client.h"
#include "shell_gameui.h"

ISharedGameRules* sharedgamerules = NULL;
IClientReplayContext* g_pClientReplayContext = NULL;

// Engine player info.
class CHudChat;

ISaveRestoreBlockHandler* GetEntitySaveRestoreBlockHandler();
ISaveRestoreBlockHandler* GetViewEffectsRestoreBlockHandler();

class IMoveHelper;

void DispatchHudText(const char* pszName);

// The refusal tally, printed by a console command and at DLL shutdown.
static void ShellClientDll_PrintRefusals(const CShellClientDllRefusals& refusals)
{
	Msg("[SourceSharp] client refusals: %d call(s) across %d method(s)\n", refusals.Total(), refusals.MethodCount());

	for (int i = 0; i < refusals.MethodCount(); i++)
	{
		Msg("[SourceSharp]   %-52s %d\n", refusals.MethodAt(i), refusals.CountAt(i));
	}

	if (refusals.Total() == 0)
	{
		Msg("[SourceSharp]   none -- every refused IBaseClientDLL method is "
			"still unreached\n");
	}
}

// Declared in shell_clientdll_refusals.h; defined here because it needs the engine.
void ShellClientDll_RefusalWarning(const char* pMethod, int nTotal)
{
	Warning("[SourceSharp] REFUSED: %s was called. plan_entity.md 5b argues this "
			"method is unreachable in this build -- no caller of it was ever "
			"observed from the engine and its body is entirely "
			"inside #if defined( TF_CLIENT_DLL ) -- so it is a no-op. If you are "
			"reading this, that argument was wrong for this method. "
			"Refusal #%d; cs_cl_refusals for the tally.\n",
			(pMethod != NULL) ? pMethod : "(unnamed)", nTotal);
}

// The engine's IBaseClientDLL, and the environment the managed loader reads the factory
// and globals from.
class CShellClientDLL : public IBaseClientDLL, public IManagedRuntimeEnvironment
{
public:
	CShellClientDLL();

	virtual int Init(CreateInterfaceFn appSystemFactory, CreateInterfaceFn physicsFactory, CGlobalVarsBase* pGlobals);

	virtual void PostInit();
	virtual void Shutdown();

	virtual bool ReplayInit(CreateInterfaceFn fnReplayFactory);
	virtual bool ReplayPostInit();

	virtual void LevelInitPreEntity(const char* pMapName);
	virtual void LevelInitPostEntity();
	virtual void LevelShutdown();

	virtual ClientClass* GetAllClasses();

	virtual int HudVidInit();
	virtual void HudProcessInput(bool bActive);
	virtual void HudUpdate(bool bActive);
	virtual void HudReset();
	virtual void HudText(const char* message);

	virtual void IN_ActivateMouse();
	virtual void IN_DeactivateMouse();
	virtual void IN_Accumulate();
	virtual void IN_ClearStates();
	virtual bool IN_IsKeyDown(const char* name, bool& isdown);
	virtual void IN_OnMouseWheeled(int nDelta);
	virtual int IN_KeyEvent(int eventcode, ButtonCode_t keynum, const char* pszCurrentBinding);
	virtual void IN_SetSampleTime(float frametime);
	virtual void CreateMove(int sequence_number, float input_sample_frametime, bool active);
	virtual void ExtraMouseSample(float frametime, bool active);
	virtual bool WriteUsercmdDeltaToBuffer(bf_write* buf, int from, int to, bool isnewcommand);
	virtual void EncodeUserCmdToBuffer(bf_write& buf, int slot);
	virtual void DecodeUserCmdFromBuffer(bf_read& buf, int slot);

	virtual void View_Render(vrect_t* rect);
	virtual void RenderView(const CViewSetup& view, int nClearFlags, int whatToDraw);
	virtual void View_Fade(ScreenFade_t* pSF);

	virtual void SetCrosshairAngle(const QAngle& angle);

	virtual void InitSprite(CEngineSprite* pSprite, const char* loadname);
	virtual void ShutdownSprite(CEngineSprite* pSprite);

	virtual int GetSpriteSize() const;

	virtual void VoiceStatus(int entindex, qboolean bTalking);

	virtual void InstallStringTableCallback(const char* tableName);

	virtual void FrameStageNotify(ClientFrameStage_t curStage);

	virtual bool DispatchUserMessage(int msg_type, bf_read& msg_data);

	virtual CSaveRestoreData* SaveInit(int size);
	virtual void SaveWriteFields(CSaveRestoreData*, const char*, void*, datamap_t*, typedescription_t*, int);
	virtual void SaveReadFields(CSaveRestoreData*, const char*, void*, datamap_t*, typedescription_t*, int);
	virtual void PreSave(CSaveRestoreData*);
	virtual void Save(CSaveRestoreData*);
	virtual void WriteSaveHeaders(CSaveRestoreData*);
	virtual void ReadRestoreHeaders(CSaveRestoreData*);
	virtual void Restore(CSaveRestoreData*, bool);
	virtual void DispatchOnRestore();
	virtual void WriteSaveGameScreenshot(const char* pFilename);

	// Looks up the localized text for "S(wavname)" tokens and emits the close caption when
	// closecaption is 1.
	virtual void EmitSentenceCloseCaption(char const* tokenstream);
	virtual void EmitCloseCaption(char const* captionname, float duration);

	virtual CStandardRecvProxies* GetStandardRecvProxies();

	virtual bool CanRecordDemo(char* errorMsg, int length) const;

	virtual void OnDemoRecordStart(char const* pDemoBaseName);
	virtual void OnDemoRecordStop();
	virtual void OnDemoPlaybackStart(char const* pDemoBaseName);
	virtual void OnDemoPlaybackStop();

	virtual bool ShouldDrawDropdownConsole();

	virtual int GetScreenWidth();
	virtual int GetScreenHeight();

	virtual void WriteSaveGameScreenshotOfSize(const char* pFilename, int width, int height,
											   bool bCreatePowerOf2Padded /*=false*/, bool bWriteVTF /*=false*/);

	virtual bool GetPlayerView(CViewSetup& playerView);

	virtual void SetupGameProperties(CUtlVector<XUSER_CONTEXT>& contexts, CUtlVector<XUSER_PROPERTY>& properties);
	virtual uint GetPresenceID(const char* pIDName);
	virtual const char* GetPropertyIdString(const uint id);
	virtual void GetPropertyDisplayString(uint id, uint value, char* pOutput, int nBytes);
	virtual void StartStatsReporting(HANDLE handle, bool bArbitrated);

	virtual void InvalidateMdlCache();

	virtual void ReloadFilesInList(IFileList* pFilesToReload);

	// Let the client handle UI toggle - if this function returns false, the UI will toggle, otherwise it will not.
	virtual bool HandleUiToggle();

	virtual bool ShouldAllowConsole();

	virtual CRenamedRecvTableInfo* GetRenamedRecvTableInfos();

	virtual CMouthInfo* GetClientUIMouthInfo();

	// Notify the client that a file has been received from the game server
	virtual void FileReceived(const char* fileName, unsigned int transferID);

	virtual const char* TranslateEffectForVisionFilter(const char* pchEffectType, const char* pchEffectName);

	virtual void ClientAdjustStartSoundParams(struct StartSoundParams_t& params);

	// Returns true if the disconnect command has been handled by the client
	virtual bool DisconnectAttempt();

public:
	virtual bool IsConnectedUserInfoChangeAllowed(IConVar* pCvar);

	virtual bool BHaveChatSuspensionInCurrentMatch();

	virtual void DisplayVoiceUnavailableMessage();

private:
	void ResetStringTablePointers();

	static void OnMaterialTableChanged(void* object, INetworkStringTable* stringTable, int stringNumber,
									   const char* newString, void const* newData);
	static void OnParticleTableChanged(void* object, INetworkStringTable* stringTable, int stringNumber,
									   const char* newString, void const* newData);

	// Set between LevelInit and LevelShutdown.
	bool m_bLevelInitialized = false;

	// The map-specific particle files the previous level read; -1 before the first level.
	int m_nPreviousMapParticleFiles = -1;

public:
	// What the interfaces' globals point at until Init.
	CGlobalVarsBase m_DummyGlobals;

	// Bodies of stock free functions of nearly the same name, which forward here.

private:
	uint64_t m_nClientStageCrossings;
	uint64_t m_nFrameStageNotifyCalls;
	CMouthInfo m_ClientUIMouth;
	ClientFrameStage_t m_CurFrameStage;

	CHLVoiceStatusHelper m_VoiceStatusHelper;

	// Members of a file-scope object, so they are constructed during static initialisation
	// and register in Init's ConVar_Register. The server reads them off the wire by name.
	ConVar m_cl_team;
	ConVar m_cl_class;

	void ReloadCommand(const CCommand& args) { m_ManagedSide.RequestReload(); }

	void CrossingsCommand(const CCommand& args)
	{
		SourceSharp_PrintCrossings(m_Managed.m_Areas.m_pDiagnosticsClient,
								   args.ArgC() > 1 && ss::StrICmp(args.Arg(1), "reset") == 0);
	}

	void RefusalsCommand(const CCommand& args);
	void StageCrossingsCommand(const CCommand& args);
	void EffectsReportCommand(const CCommand& args);
	void DumpPayloadCommand(const CCommand& args);
	void NotShippedCommand(const CCommand& args);

	CMemberCommand<CShellClientDLL, &CShellClientDLL::ReloadCommand> m_ReloadCommand{
		*this, "cs_cl_reload", "Reload the CLIENT's managed assemblies in place, without restarting the game", 0};
	CMemberCommand<CShellClientDLL, &CShellClientDLL::RefusalsCommand> m_RefusalsCommand{
		*this, "cs_cl_refusals",
		"SourceSharp: how many times each refused IBaseClientDLL method was called (plan_entity.md 5b)", 0};
	CMemberCommand<CShellClientDLL, &CShellClientDLL::StageCrossingsCommand> m_StageCrossingsCommand{
		*this, "ss_cl_stage_crossings", "ss_cl_stage_crossings -- FrameStageNotify calls vs ClientStage crossings", 0};
	CMemberCommand<CShellClientDLL, &CShellClientDLL::EffectsReportCommand> m_EffectsReportCommand{
		*this, "cs_cl_effects_report",
		"SourceSharp: how many primitives each of step 7a's leaf effect renderers has emitted.", 0};
	CMemberCommand<CShellClientDLL, &CShellClientDLL::DumpPayloadCommand> m_DumpPayloadCommand{
		*this, "cs_cl_dump_payload",
		"Build the client's generic-table payload prop set standalone and dump its offsets and proxies", 0};
	CMemberCommand<CShellClientDLL, &CShellClientDLL::NotShippedCommand> m_NotShippedCommand{
		*this, "cs_cl_notshipped", "SourceSharp: what reached a path this client build does not ship.", 0};
	CMemberCommand<CShellClientDLL, &CShellClientDLL::CrossingsCommand> m_CrossingsCommand{
		*this, "cs_cl_crossings",
		"Print the managed<->native ABI crossing counts. "
		"Pass 'reset' to print the interval and start a new one.",
		0};

	// SDL_PumpEvents from the launcher's libSDL2, or NULL. Called during long loads so the
	// window answers the compositor; SDL wants its main thread.
	void (*m_pfnPumpEvents)() = nullptr;

	void PumpWindowDuringLoad()
	{
		if (m_pfnPumpEvents && ThreadInMainThread())
			m_pfnPumpEvents();
	}

public:
	// Read by the refusal console command: zero means every method the engine called was
	// answered.

	// The particles shell's refused IParticleSystemQuery methods count here too.

private:
	void ShellClientDll_Unreachable(const char* pMethod) { m_Refusals.Unreachable(pMethod); }

	// The managed shakes and fades start over.
	void ResetManagedViewEffects();

	// A screen fade, managed's: the engine's View_Fade and the map's start-dark fade.
	void ViewFade(const ScreenFade_t& sf);

	// FRAME_RENDER_START's body: the frame's simulation before the view draws.
	void OnRenderStart();

	CShellClientDllRefusals m_Refusals;
	SourceSharpEffectLedger_t m_EffectLedger = {};

public:
	void* GetEngineInterface(const char* pName, int* pReturnCode) override
	{
		return m_pEngineFactory(pName, pReturnCode);
	}

	// Engine-owned memory, changing every frame; managed reads through the pointer.
	void UnloadManagedSide() override { m_ManagedSide.Unload(); }

private:
	CreateInterfaceFn m_pEngineFactory;
	CGlobalVarsBase* m_pGlobals;

	// One load and one release per DLL.
	IClientRuntimeAdapter* m_pManagedRuntime;

public:
	// Everything the client holds from managed: the root, the runtime, the areas.
	CManagedSide& ManagedSide() { return m_ManagedSide; }

	bool LoadManagedSide() override { return m_ManagedSide.Load(m_Engines.m_pEngine->GetGameDirectory()); }

#ifdef SS_CLIENT_PROBES
	const CClientEngineInterfaces& Engines() const { return m_Engines; }

	CNewParticlesClient& NewParticles() { return m_NewParticles; }

	CTempEntsSystemClient& TempEntsSystem() { return m_TempEntsSystem; }

	CSoundPatchesClient& SoundPatches() { return m_SoundPatches; }

	CEffectsListClient& EffectsList() { return m_EffectsList; }

	CClientEntityList& EntityList() { return m_EntityList; }
#endif

	virtual ~CShellClientDLL()
	{
		AssertMsg(ManagedHoldersClient_Empty(m_Managed),
				  "the client DLL object is destroyed holding a managed-built object");
	}

private:
	// First, so everything the root builds after it can log.
	CShellLog m_Log;

	CManagedHoldersClient m_Managed = {};
	// Before the managed side, which installs them at load.
	CDiagnosticsHooks m_Diagnostics;

	// The console: this DLL's carriers and the ICvar they reach.
	CCVarRegistry m_CVars;

	// The engine's VClientEntityList003 and the slot table behind it; the engine fetches
	// it by name when it loads the DLL.
	CWorldClient m_World{m_Managed, m_Engines, m_ManagedSide};
	CRenderTargetsClient m_RenderTargets{m_Engines};
	CClientEntityList m_EntityList{m_Engines};

	CInterfaceMember m_EntityListReg{static_cast<IClientEntityList*>(&m_EntityList),
									 VCLIENTENTITYLIST_INTERFACE_VERSION};

	// The engine's VClientPrediction001, fetched by name the same way.
	CSourceSharpPrediction m_Prediction{m_Managed.m_Areas};

	CInterfaceMember m_PredictionReg{static_cast<IPrediction*>(&m_Prediction), VCLIENT_PREDICTION_INTERFACE_VERSION};

	// The voice ban and speaker state, over the managed UI area.
	CVoiceStatus m_VoiceStatus{m_Managed.m_Areas};

	// VClientDllSharedAppSystems001, the engine's first call into the client, and the
	// GameUI's GameClientExports001.
	// GameUI011 and GameConsole004, handed to the engine by the swap the app systems install.
	CGameUiClient m_GameUi{m_Managed.m_Areas};

	CClientDLLSharedAppSystems m_AppSystems{m_GameUi};
	CInterfaceMember m_AppSystemsReg{static_cast<IClientDLLSharedAppSystems*>(&m_AppSystems),
									 CLIENT_DLL_SHARED_APPSYSTEMS};
	CGameClientExports m_GameClientExports{m_Engines, m_VoiceStatus};
	CInterfaceMember m_GameClientExportsReg{static_cast<IGameClientExports*>(&m_GameClientExports),
											GAMECLIENTEXPORTS_INTERFACE_VERSION};

	// The engine's center print, over the managed UI area.
	CCenterPrint m_CenterPrint{m_Managed.m_Areas};
	CInterfaceMember m_CenterPrintReg{static_cast<ICenterPrint*>(&m_CenterPrint), VCENTERPRINT_INTERFACE_VERSION};

	// What managed acquires from native, handed to it at load.
	CShimRootClient m_ShimRoot;
	CManagedSide m_ManagedSide{m_Managed, m_Diagnostics, m_CVars, m_ShimRoot};

	// The convar area: watches through the root's console.
	CConVarsClient m_ConVars{m_ShimRoot.m_Console, m_Engines};

	// The engine interfaces Init acquires, handed to what calls them.
	CClientEngineInterfaces m_Engines;
	CEngineAreaClient m_EngineArea{m_Engines};

	// The main and current cameras: CShellViewRender writes them, whatever draws reads them.
	CViewCameras m_Cameras;

	// The materials the string tables precache, and the sweep FRAME_RENDER_START runs.
	CMaterialCacheClient m_MaterialCache{m_Engines};

	// The HUD framework's one object: the sensitivity, key bits and elements.
	CHud m_Hud;

	// The vgui startup and shutdown.
	CVguiClient m_Vgui{m_Engines};

	// The world render state and colour correction lookups managed sets.
	CWorldRenderClient m_WorldRender{m_Engines, m_ViewRender};

	// The engine's text overlays, for the UI's draw.
	CDebugOverlayClient m_DebugOverlay{m_Engines};

	// IRopeManager, and the rope slots.
	CRopeClient m_Rope{m_Engines};

	// The engine's net channel for managed: its messages and its statistics.
	CNetChannelClient m_NetChannel{m_Engines};

	// IClientRenderTargets (ClientRenderTargets001, asked for by name), and the view targets.
	CViewTargetsClient m_ViewTargets{m_Engines};
	CInterfaceMember m_ViewTargetsReg{static_cast<IClientRenderTargets*>(&m_ViewTargets),
									  CLIENTRENDERTARGETS_INTERFACE_VERSION};

	// The wind managed publishes.
	CWindClient m_Wind{m_Engines};

	// The soundscape slots: the room type, DSP and main view managed asks for.
	CSoundscapeAudioClient m_SoundscapeAudio{m_Engines, m_Cameras};

	// The game systems: the managed frame's row (which holds the runtime adapter) and the
	// table that walks every row. The frame row clears its adapter in its Shutdown, which
	// runs before the adapter is released.
	CSourceSharpSystem m_ManagedFrame{m_ManagedSide, m_Engines};
	CPhysicsClient m_Physics{m_Managed.m_Areas, m_Engines};
	CShellDecalSystemClient m_Decals{m_Managed.m_Areas};
	// IViewRenderBeams, its renderables and its sprite bind.
	CBeamsClient m_Beams{m_Managed.m_Areas, m_Engines, m_Cameras, m_EntityList};

	// The managed sprites and trails, their plans and their client systems row.
	CSpritesClient m_Sprites{m_Managed.m_Areas, m_Engines, m_Cameras, m_Standalone};

	// The shadow manager's client half.
	CShadowsClient m_Shadows{m_Managed.m_Areas, m_Engines, m_EntityList};

	CStandaloneClients m_Standalone{m_Managed.m_Areas, m_Engines, m_EntityList, m_Beams, m_Sprites, m_Shadows};

	// The local viewer's facts and the fog and sky managed pushes.
	CViewerFacts m_ViewerFacts{m_Engines, m_Standalone, m_EntityList};

	// The managed particle executors, their frame and their client systems row.
	CManagedParticlesClient m_ManagedParticles{m_Managed.m_Areas, m_Engines, m_Cameras, m_Standalone};

	// The material proxy trampoline: the registrations, the live proxies, the area.
	CMaterialProxiesClient m_MaterialProxies{m_Managed.m_Areas, m_Engines, m_Cameras, m_EntityList};

	// The managed point_worldtext renderables.
	CWorldTextClient m_WorldText{m_Managed.m_Areas, m_Engines, m_Cameras, m_Standalone};

	// The particle manifests' reads.
	CParticleManifestClient m_ParticleManifest{m_Managed.m_Areas, m_Engines};

	// The detail props' executor.
	CShellDetailProps m_DetailProps{m_Managed.m_Areas, m_Engines, m_Cameras, m_MaterialCache};

	// The leaf system's storage; its ClientLeafSystem002, by name.
	CLeafSystemClient m_LeafSystem{m_Managed.m_Areas, m_Engines, m_DetailProps};
	CInterfaceMember m_LeafSystemReg{static_cast<IClientLeafSystemEngine*>(m_LeafSystem.System()),
									 CLIENTLEAFSYSTEM_INTERFACE_VERSION};

	CDustClient m_Dust{m_Engines, m_Standalone};

	CPrecipitationQueriesClient m_PrecipitationQueries{m_Engines, m_Standalone};

	CWorldQueryClient m_WorldQuery{m_Engines, m_Standalone};

	// IGameMovement (GameMovement001: no engine binary asks for it, but HL2MP and TF export it).
	CGameRulesClient m_GameRules;
	CShellGameMovement m_GameMovement{m_Managed.m_Areas, m_Engines, m_EntityList, m_GameRules};
	CInterfaceMember m_GameMovementReg{static_cast<IGameMovement*>(&m_GameMovement), INTERFACENAME_GAMEMOVEMENT};

	CPredictionSlotsClient m_PredictionSlots{m_Engines, m_Standalone, m_GameMovement};

	// The sound envelope's patches.
	CSoundPatchesClient m_SoundPatches{m_Managed.m_Areas, m_Engines, m_Standalone, m_GameRules};

	// The new-style particle effects' list and frame.
	CNewParticlesClient m_NewParticles{m_Managed.m_Areas, m_Engines, m_EntityList};

	// The new-style particle systems managed starts, and their client systems row.
	CParticleSystemsClient m_ParticleSystems{m_Engines, m_Standalone, m_NewParticles};

public:
	// The query particles.a is handed at the particle manager's Init (g_pParticleSystemQuery).
	CParticleSystemQueryClient m_ParticleQuery{m_Managed.m_Areas, m_Engines, m_Refusals};
	CParticleMgrClient m_ParticleMgr{m_Engines};

private:
	// IInput and the shim input area, over the managed input area.
	CSourceSharpInput m_Input{m_Managed.m_Areas, m_Engines, m_Hud, m_ViewerFacts, m_Prediction, m_VoiceStatus};

	// The UI's native half: the paint window, the HUD element and the menu root.
	CHudUiClient m_HudUi{m_Managed.m_Areas, m_Engines, m_Hud, m_ClientMode};

	// IClientMode (g_pClientMode points at it from Init).
	CSourceSharpClientMode m_ClientMode{m_Managed.m_Areas,  m_Engines,     m_Hud, m_Standalone,
										m_ManagedParticles, m_ViewTargets, m_Vgui};

	// The client-side effect list the view draws, its material table and smoke fog overlay.
	CEffectsListClient m_EffectsList{m_Managed.m_Areas, m_Engines, m_Cameras, m_EntityList};

	// The glow overlays' and pixel visibility's native halves.
	CGlowOverlaysClient m_Glow{m_Managed.m_Areas, m_Engines, m_Cameras, m_EffectsList};
	CPixelVisClient m_PixelVis{m_Managed.m_Areas, m_Engines, m_Cameras, m_EffectsList};

	// The weather's draw.
	CPrecipitationClient m_Precipitation{m_Managed.m_Areas, m_Engines, m_Cameras};

	// The temp entity tables managed registers.
	CTempEntTablesClient m_TempEntTables{m_Managed.m_Areas};

	// The client's ITempEntsSystem, on SuppressHostEvents' list.
	CTempEntsSystemClient m_TempEntsSystem{m_Managed.m_Areas, m_Engines, m_EntityList};
	CTempEntBackendsClient m_TempEntBackends{m_Engines, m_Effects};
	CTempEntCommandsClient m_TempEntCommands{m_Engines,  m_TempEntBackends, m_Cameras,
											 m_TempEnts, m_TempEntsSystem,  m_Standalone};

	// IEffects (g_pEffects points at it from Init), over the managed FX area.
	CEffectsClient m_Effects{m_Managed.m_Areas, m_Engines};

	// ITempEnts (tempents points at it from Init): the legacy temp entities' object.
	CTempEnts m_TempEnts{m_Managed.m_Areas, m_Engines, m_Cameras, m_Sprites, m_EntityList};

	// IViewRender (`view` points at it from Init).
	CShellViewRender m_ViewRender{m_Managed.m_Areas, m_Engines,      m_Cameras, m_ViewerFacts,
								  m_ClientMode,      m_ViewExecutor, m_Beams};
	CClientSystems m_Systems{m_Engines,     m_Managed.m_Areas, m_Rope,        m_ManagedFrame,     m_Physics,
							 m_Decals,      m_Standalone,      m_EffectsList, m_ManagedParticles, m_ParticleSystems,
							 m_DetailProps, m_WorldText,       m_Sprites,     m_Shadows,          m_LeafSystem};

	// The render plan's executor; the view render calls it, and it calls the systems' PreRender.
	CViewExecutor m_ViewExecutor{
		m_Managed.m_Areas,  m_Systems,     m_ViewerFacts, m_Standalone, m_ClientMode,    m_ViewRender, m_EffectsList,
		m_ManagedParticles, m_DetailProps, m_Glow,        m_PixelVis,   m_Precipitation, m_Shadows,    m_HudUi,
		m_Cameras,          m_ViewTargets, m_Engines,     m_Rope,       m_WorldRender};

	// The game events managed listens for: the client's shim events area.
	CGameEvents m_Events{m_Engines.m_pEvents, m_Managed.m_Areas.m_pEventsClient, false};
	CMapMaterialReload m_MapMaterials{m_Managed};
};

// Records a refusal in the boundary probe table and the refusal tally. A macro so
// SS_BOUNDARY_CALLER() captures the engine's return address, not a helper's.
#define SS_CLIENTDLL_UNREACHABLE(pszMethod) ShellClientDll_Unreachable((pszMethod))

CShellClientDLL g_ShellClientDLL;
IBaseClientDLL* clientdll = &g_ShellClientDLL;

// The query the particle manager's Init hands particles.a: the client DLL object's. A
// pointer, constant-initialised, so the probe build can wrap it at static init.
#ifdef SS_CLIENT_PROBES
// The recorders draw from the engine's stream.
IUniformRandomStream* random = NULL;

// The probe recorder wraps the query at static initialisation.
IParticleSystemQuery* g_pParticleSystemQuery = &g_ShellClientDLL.m_ParticleQuery;
#define SS_PARTICLE_QUERY g_pParticleSystemQuery
#else
#define SS_PARTICLE_QUERY (&m_ParticleQuery)
#endif

#ifdef SS_CLIENT_PROBES
#include "probe_accessors.h"

// The probe build's recorders start from console commands, and tools/probes is not handed
// the objects it records.
const CClientEngineInterfaces& ShellClientDll_Engines()
{
	return g_ShellClientDLL.Engines();
}

CNewParticlesClient& ShellClientDll_NewParticles()
{
	return g_ShellClientDLL.NewParticles();
}

CTempEntsSystemClient& ShellClientDll_TempEntsSystem()
{
	return g_ShellClientDLL.TempEntsSystem();
}

CSoundPatchesClient& ShellClientDll_SoundPatches()
{
	return g_ShellClientDLL.SoundPatches();
}

CEffectsListClient& ShellClientDll_EffectsList()
{
	return g_ShellClientDLL.EffectsList();
}

CClientEntityList& ShellClientDll_EntityList()
{
	return g_ShellClientDLL.EntityList();
}
#endif

EXPOSE_SINGLE_INTERFACE_GLOBALVAR(CShellClientDLL, IBaseClientDLL, CLIENT_DLL_INTERFACE_VERSION, g_ShellClientDLL);

CShellClientDLL::CShellClientDLL()
	: m_DummyGlobals(true)
	, m_nClientStageCrossings(0)
	, m_nFrameStageNotifyCalls(0)
	, m_CurFrameStage(FRAME_UNDEFINED)
	, m_cl_team("cl_team", "default", FCVAR_USERINFO | FCVAR_ARCHIVE, "Default team when joining a game")
	, m_cl_class("cl_class", "default", FCVAR_USERINFO | FCVAR_ARCHIVE, "Default class when joining a game")
	, m_pEngineFactory(NULL)
	, m_pGlobals(NULL)
	, m_pManagedRuntime(NULL)
{
	m_Engines.m_pGlobals = &m_DummyGlobals;
	m_Engines.m_pConVars = &m_ConVars;
	m_Engines.m_pWorld = &m_World;
	m_Engines.m_pRenderTargets = &m_RenderTargets;
	m_Engines.m_pParticles = &m_ParticleMgr;
	m_Engines.m_pEffectLedger = &m_EffectLedger;
	m_bLevelInitialized = false;
}

int CShellClientDLL::Init(CreateInterfaceFn appSystemFactory, CreateInterfaceFn physicsFactory,
						  CGlobalVarsBase* pGlobals)
{
	m_Engines.m_pGlobals = pGlobals;
	m_Engines.m_pInput = &m_Input;
	m_Engines.m_pClientMode = &m_ClientMode;
	m_Engines.m_pLeafSystem = m_LeafSystem.System();
	m_Engines.m_pDecalSystem = &m_Decals;
	m_Engines.m_pGameEffects = &m_Effects;
	m_Engines.m_pTempEnts = &m_TempEnts;

	// The same two as members, served through IManagedRuntimeEnvironment.
	m_pEngineFactory = appSystemFactory;
	m_pGlobals = pGlobals;

	m_Engines.m_pCvar = (ICvar*)appSystemFactory(CVAR_INTERFACE_VERSION, NULL);
	m_CVars.Connect(m_Engines.m_pCvar);
	Tier0_ConnectConsole(m_Engines.m_pCvar);
	m_ShimRoot.m_Console.m_pCVars = &m_CVars;
	SourceSharp_ConnectTier2(m_Engines, appSystemFactory);
	SourceSharp_ConnectTier3(m_Engines, appSystemFactory);

	// Keep the client from writing files into arbitrary locations.
	if (m_Engines.m_pFileSystem)
	{
		m_Engines.m_pFileSystem->SetWriteProtectionEnable(true);
	}

	if ((m_Engines.m_pEngine = (IVEngineClient*)appSystemFactory(VENGINE_CLIENT_INTERFACE_VERSION, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pModelRender = (IVModelRender*)appSystemFactory(VENGINE_HUDMODEL_INTERFACE_VERSION, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pEffects = (IVEfx*)appSystemFactory(VENGINE_EFFECTS_INTERFACE_VERSION, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pTrace = (IEngineTrace*)appSystemFactory(INTERFACEVERSION_ENGINETRACE_CLIENT, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pRender = (IVRenderView*)appSystemFactory(VENGINE_RENDERVIEW_INTERFACE_VERSION, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pDebugOverlay = (IVDebugOverlay*)appSystemFactory(VDEBUG_OVERLAY_INTERFACE_VERSION, NULL)) == NULL)
		return false;
	if (!m_Engines.m_pMdlCache)
		return false;
	if ((m_Engines.m_pModelInfo = (IVModelInfoClient*)appSystemFactory(VMODELINFO_CLIENT_INTERFACE_VERSION, NULL)) ==
		NULL)
		return false;
	if ((m_Engines.m_pVGui = (IEngineVGui*)appSystemFactory(VENGINE_VGUI_VERSION, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pStringTables =
			 (INetworkStringTableContainer*)appSystemFactory(INTERFACENAME_NETWORKSTRINGTABLECLIENT, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pPartition = (ISpatialPartition*)appSystemFactory(INTERFACEVERSION_SPATIALPARTITION, NULL)) ==
		NULL)
		return false;
	if ((m_Engines.m_pShadows = (IShadowMgr*)appSystemFactory(ENGINE_SHADOWMGR_INTERFACE_VERSION, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pStaticProps =
			 (IStaticPropMgrClient*)appSystemFactory(INTERFACEVERSION_STATICPROPMGR_CLIENT, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pSound = (IEngineSound*)appSystemFactory(IENGINESOUND_CLIENT_INTERFACE_VERSION, NULL)) == NULL)
		return false;
	if ((m_Engines.m_pFileSystem = (IFileSystem*)appSystemFactory(FILESYSTEM_INTERFACE_VERSION, NULL)) == NULL)
		return false;

#ifdef SS_CLIENT_PROBES
	if ((random = (IUniformRandomStream*)appSystemFactory(VENGINE_CLIENT_RANDOM_INTERFACE_VERSION, NULL)) == NULL)
		return false;
#endif
	if ((m_Engines.m_pEvents = (IGameEventManager2*)appSystemFactory(INTERFACEVERSION_GAMEEVENTSMANAGER2, NULL)) ==
		NULL)
		return false;
	if ((m_Engines.m_pSoundEmitter =
			 (ISoundEmitterSystemBase*)appSystemFactory(SOUNDEMITTERSYSTEM_INTERFACE_VERSION, NULL)) == NULL)
		return false;

	if (!m_Engines.m_pMatSystemSurface)
		return false;

	m_ShimRoot.m_pAppSystemFactory = appSystemFactory;
	m_ShimRoot.m_pPhysicsFactory = physicsFactory;

	// The server DLL connects too; the sound emitter system handles that.
	if (!m_Engines.m_pSoundEmitter->Connect(appSystemFactory))
	{
		return false;
	}

	if (ss::CommandLineHas("-textmode"))
		m_Engines.m_bTextMode = true;

	m_Engines.m_pMaterialsStub = (IMaterialSystemStub*)appSystemFactory(MATERIAL_SYSTEM_STUB_INTERFACE_VERSION, NULL);

	if (!m_Engines.m_pHardwareConfig)
		return false;

	m_CVars.Register(FCVAR_CLIENTDLL);

	if (!m_ParticleMgr.Init(m_Engines.m_pMaterials, SS_PARTICLE_QUERY))
		return false;

	// Send true to load the sheets.
	m_ParticleManifest.Parse(true, false);

	// A listen server's spawn reloads only what the map paks can have changed. Failing to
	// install falls back to the stock full reload.
	m_MapMaterials.Install(m_Engines.m_pFileSystem, m_Engines.m_pMaterials);

	// RTLD_NOLOAD: the launcher's SDL or nothing. Windows has no SDL to find.
#ifndef _WIN32
	if (void* hSDL = dlopen("libSDL2-2.0.so.0", RTLD_NOW | RTLD_NOLOAD))
		m_pfnPumpEvents = (void (*)())dlsym(hSDL, "SDL_PumpEvents");
#endif
	Msg("[SourceSharp] load pump: %s\n",
		m_pfnPumpEvents ? "SDL_PumpEvents" : "none (the window is pumped only between frames)");

	if (!m_Vgui.Startup(appSystemFactory))
		return false;

	Assert(m_Engines.m_pMaterials && m_Engines.m_pMatSystemSurface && m_Engines.m_pHardwareConfig);

	m_ClientMode.InitViewport();
	m_Engines.m_pViewPort = m_ClientMode.ViewPort();

	// Before the HUD: m_Hud.Init() hooks the user messages managed declares. The CLR comes up
	// before m_Systems.Init() walks the client systems.
	m_ShimRoot.m_Console.m_ppCommands = &m_Managed.m_Areas.m_pCommandsClient;
	m_ShimRoot.m_Areas.m_pEvents = &m_Events;
	m_ShimRoot.m_Areas.m_pConVarsClient = &m_ConVars;
	m_ShimRoot.m_Areas.m_pInputClient = &m_Input;
	m_ShimRoot.m_Areas.m_pPhysicsClient = &m_Physics;
	m_ShimRoot.m_Areas.m_pStandaloneClient = &m_Standalone;
	m_ShimRoot.m_pShadows = &m_Shadows;
	m_ShimRoot.m_Areas.m_TempEntPoolClient.m_pTempEnts = &m_TempEnts;
	m_ShimRoot.m_Areas.m_FxClient.m_pTempEnts = &m_TempEnts;
	m_ShimRoot.m_Areas.m_TempEntBackendsClient.m_pTempEntTablesClient = &m_TempEntTables;
	m_ShimRoot.m_Areas.m_FxClient.m_pTempEntsSystemClient = &m_TempEntsSystem;
	m_ShimRoot.m_Areas.m_pMaterialProxyClient = &m_MaterialProxies;
	m_ShimRoot.m_Areas.m_ViewClient.m_pSourceSharpClientMode = &m_ClientMode;
	m_ShimRoot.m_Areas.m_pUserMessagesClient = &m_ClientMode;
	m_ShimRoot.m_Areas.m_ViewClient.m_pViewerFacts = &m_ViewerFacts;
	m_ShimRoot.m_Areas.m_UiClient.m_pSourceSharpClientMode = &m_ClientMode;
	m_ShimRoot.m_Areas.m_ViewClient.m_pWorldRenderClient = &m_WorldRender;
	m_ShimRoot.m_Areas.m_UiClient.m_pDebugOverlayClient = &m_DebugOverlay;
	m_ShimRoot.m_Areas.m_TempEntBackendsClient.m_pTempEntBackendsClient = &m_TempEntBackends;
	m_ShimRoot.m_Areas.m_HudClient.m_pHudUiClient = &m_HudUi;
	m_ShimRoot.m_Areas.m_ViewClient.m_pRenderTargetsClient = &m_RenderTargets;
	m_ShimRoot.m_Areas.m_pEngine = &m_EngineArea;
	m_ShimRoot.m_Areas.m_DiagnosticsClient.m_pShellClientDllRefusals = &m_Refusals;
	m_ShimRoot.m_Areas.m_Network.m_pWorldClient = &m_World;
	m_ShimRoot.m_Areas.m_Network.m_pStandaloneClients = &m_Standalone;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pRopeClient = &m_Rope;
	m_ShimRoot.m_Areas.m_pNetClient = &m_NetChannel;
	m_ShimRoot.m_Areas.m_ViewClient.m_pShellViewRender = &m_ViewRender;
	m_ShimRoot.m_Areas.m_pMovement = &m_GameMovement;
	m_ShimRoot.m_Areas.m_ViewClient.m_pViewTargetsClient = &m_ViewTargets;
	m_ShimRoot.m_Areas.m_ViewClient.m_pStandaloneClients = &m_Standalone;
	m_ShimRoot.m_Areas.m_pEntities = &m_Standalone;
	m_ShimRoot.m_Areas.m_Models.m_pStandaloneClients = &m_Standalone;
	m_ShimRoot.m_Areas.m_HudClient.m_pStandaloneClients = &m_Standalone;
	m_ShimRoot.m_Areas.m_TempEntBackendsClient.m_pStandaloneClients = &m_Standalone;
	m_ShimRoot.m_Areas.m_pFlashlightClient = &m_Shadows;
	m_ShimRoot.m_Areas.m_SoundscapeClient.m_pSoundPatchesClient = &m_SoundPatches;
	m_ShimRoot.m_Areas.m_SoundscapeClient.m_pSoundscapeAudioClient = &m_SoundscapeAudio;
	m_ShimRoot.m_Areas.m_pPredictionClient = &m_PredictionSlots;
	m_ShimRoot.m_Areas.m_ViewClient.m_pWorldQueryClient = &m_WorldQuery;
	m_ShimRoot.m_Areas.m_pPrecipitationClient = &m_PrecipitationQueries;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pDustClient = &m_Dust;
	m_ShimRoot.m_Areas.m_UiClient.m_pHudUiClient = &m_HudUi;
	m_ShimRoot.m_Areas.m_ViewClient.m_pGlowOverlaysClient = &m_Glow;
	m_ShimRoot.m_Areas.m_ViewClient.m_pPixelVisClient = &m_PixelVis;
	m_ShimRoot.m_Areas.m_FxClient.m_pEffectsListClient = &m_EffectsList;
	m_ShimRoot.m_Areas.m_FxClient.m_pEffectsClient = &m_Effects;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pManagedParticlesClient = &m_ManagedParticles;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pParticleManifestClient = &m_ParticleManifest;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pWorldTextClient = &m_WorldText;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pParticleSystemsClient = &m_ParticleSystems;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pBeamsClient = &m_Beams;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pWindClient = &m_Wind;
	m_ShimRoot.m_Areas.m_TempEntPoolClient.m_pWindClient = &m_Wind;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pSpritesClient = &m_Sprites;
	m_ShimRoot.m_Areas.m_FxClient.m_pSpritesClient = &m_Sprites;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pParticleSystemQueryClient = &m_ParticleQuery;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pParticleSystemsClient = &m_ParticleSystems;
	if (const char* pszUnset = ShimAreasClient_FirstUnset(m_ShimRoot.m_Areas))
		Error("[SourceSharp] the client DLL object never set shim area member %s", pszUnset);

	m_pManagedRuntime = LoadManagedRuntime(this, m_Managed);

	if (m_pManagedRuntime == NULL)
		return false;

	// A ClientClass linked after GetAllClasses is not in the handshake, and the build window
	// ends before m_Hud.Init().
	m_TempEntTables.TempEntClientCloseRegistration();

	m_ManagedFrame.SetRuntime(m_pManagedRuntime);

	// The particle manifest read Init deferred until managed was up.
	m_ParticleManifest.RuntimeUp();

	m_HudUi.CreateHud();
	m_Hud.Init();

	m_ClientMode.Init();

	m_Engines.m_pDirtyPartition = ClientSystem_CreateDirtySpatialPartition(m_Engines);
	if (!m_Systems.Init())
		return false;

	m_ClientMode.Enable();

	m_Engines.m_pViewRender = &m_ViewRender;
	m_Engines.m_pViewRender->Init();

	m_Input.Init_All();

	m_EffectsList.InitSmokeFog();

	{
		vgui::VPANEL parent = m_Engines.m_pVGui->GetPanel(PANEL_CLIENTDLL);
		m_VoiceStatus.Init(&m_VoiceStatusHelper, parent);
	}

	if (!m_Physics.DLLInit(physicsFactory))
		return false;

	m_World.CreateEntity();

	return true;
}

bool CShellClientDLL::ReplayInit(CreateInterfaceFn fnReplayFactory)
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
	m_Systems.PostInit();

	// Managed sets the r_lightmap_bicubic default from the adapter's max DX support level, -1
	// with no material system.
	IManagedRuntimeClient* pRuntime = m_Managed.m_pRuntimeClient;
	if (!pRuntime)
		return;

	int nMaxDxLevel = -1;
	if (m_Engines.m_pMaterials)
	{
		MaterialAdapterInfo_t info{};
		m_Engines.m_pMaterials->GetDisplayAdapterInfo(m_Engines.m_pMaterials->GetCurrentAdapter(), info);
		nMaxDxLevel = info.m_nMaxDXSupportLevel;
	}
	pRuntime->PostInit(nMaxDxLevel);
}

void CShellClientDLL::Shutdown()
{
	// First: the hooked slots name functions in this module.
	m_MapMaterials.Uninstall();

	// Printed on the way out so refusals from an unwatched session reach the log.
	ShellClientDll_PrintRefusals(m_Refusals);

	m_World.DestroyEntity();

	m_ClientMode.Disable();
	m_ClientMode.Shutdown();
	m_Engines.m_pViewPort = NULL;

	m_Input.Shutdown_All();
	m_EffectsList.TermSmokeFog();
	m_Engines.m_pViewRender->Shutdown();
	m_Engines.m_pParticleMgr->UncacheAllParticleSystems();
	m_MaterialCache.UncacheAll();

	// The engine calls Shutdown after a failed Init, when the runtime may be absent.
	// Unhooked even when the runtime failed after subscribing: the engine keeps its
	// listener list for the whole process.
	m_Events.StopListening();

	if (m_pManagedRuntime != NULL)
	{
		m_pManagedRuntime->OnSystemStop();
	}

	m_Systems.Shutdown();

	// After the walk, which cleared the row's copy of the pointer. Null-safe.
	UnloadManagedRuntime(m_pManagedRuntime);
	m_pManagedRuntime = NULL;

	m_Hud.Shutdown();
	m_Vgui.Shutdown();

	m_ParticleMgr.Term();

	SourceSharp_DisconnectTier2(m_Engines);
	m_CVars.Unregister();

	// After the unregister, while the console is still connected.
	m_ShimRoot.m_Console.ReleaseObjects();
	m_ShimRoot.m_Console.m_ppCommands = NULL;
	m_ShimRoot.m_Console.m_pCVars = NULL;
	m_CVars.Connect(NULL);
	Tier0_ConnectConsole(NULL);
	m_Engines.m_pCvar = nullptr;

	m_ShimRoot.m_Areas.m_pEvents = NULL;
	m_ShimRoot.m_Areas.m_pConVarsClient = NULL;
	m_ShimRoot.m_Areas.m_pInputClient = NULL;
	m_ShimRoot.m_Areas.m_pPhysicsClient = NULL;
	m_ShimRoot.m_Areas.m_pStandaloneClient = NULL;
	m_ShimRoot.m_Areas.m_TempEntPoolClient.m_pTempEnts = NULL;
	m_ShimRoot.m_Areas.m_FxClient.m_pTempEnts = NULL;
	m_ShimRoot.m_Areas.m_TempEntBackendsClient.m_pTempEntTablesClient = NULL;
	m_ShimRoot.m_Areas.m_FxClient.m_pTempEntsSystemClient = NULL;
	m_ShimRoot.m_Areas.m_pMaterialProxyClient = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pSourceSharpClientMode = NULL;
	m_ShimRoot.m_Areas.m_pUserMessagesClient = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pViewerFacts = NULL;
	m_ShimRoot.m_Areas.m_UiClient.m_pSourceSharpClientMode = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pWorldRenderClient = NULL;
	m_ShimRoot.m_Areas.m_UiClient.m_pDebugOverlayClient = NULL;
	m_ShimRoot.m_Areas.m_TempEntBackendsClient.m_pTempEntBackendsClient = NULL;
	m_ShimRoot.m_Areas.m_HudClient.m_pHudUiClient = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pRenderTargetsClient = NULL;
	m_ShimRoot.m_Areas.m_pEngine = NULL;
	m_ShimRoot.m_Areas.m_DiagnosticsClient.m_pShellClientDllRefusals = NULL;
	m_ShimRoot.m_Areas.m_Network.m_pWorldClient = NULL;
	m_ShimRoot.m_Areas.m_Network.m_pStandaloneClients = NULL;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pRopeClient = NULL;
	m_ShimRoot.m_Areas.m_pNetClient = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pShellViewRender = NULL;
	m_ShimRoot.m_Areas.m_pMovement = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pViewTargetsClient = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pStandaloneClients = NULL;
	m_ShimRoot.m_Areas.m_pEntities = NULL;
	m_ShimRoot.m_Areas.m_Models.m_pStandaloneClients = NULL;
	m_ShimRoot.m_Areas.m_HudClient.m_pStandaloneClients = NULL;
	m_ShimRoot.m_Areas.m_TempEntBackendsClient.m_pStandaloneClients = NULL;
	m_ShimRoot.m_Areas.m_pFlashlightClient = NULL;
	m_ShimRoot.m_Areas.m_SoundscapeClient.m_pSoundPatchesClient = NULL;
	m_ShimRoot.m_Areas.m_SoundscapeClient.m_pSoundscapeAudioClient = NULL;
	m_ShimRoot.m_Areas.m_pPredictionClient = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pWorldQueryClient = NULL;
	m_ShimRoot.m_Areas.m_pPrecipitationClient = NULL;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pDustClient = NULL;
	m_ShimRoot.m_Areas.m_UiClient.m_pHudUiClient = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pGlowOverlaysClient = NULL;
	m_ShimRoot.m_Areas.m_ViewClient.m_pPixelVisClient = NULL;
	m_ShimRoot.m_Areas.m_FxClient.m_pEffectsListClient = NULL;
	m_ShimRoot.m_Areas.m_FxClient.m_pEffectsClient = NULL;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pManagedParticlesClient = NULL;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pParticleManifestClient = NULL;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pWorldTextClient = NULL;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pParticleSystemsClient = NULL;
	m_ShimRoot.m_Areas.m_ParticlesClient.m_pParticleSystemQueryClient = NULL;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pParticleSystemsClient = NULL;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pBeamsClient = NULL;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pWindClient = NULL;
	m_ShimRoot.m_Areas.m_TempEntPoolClient.m_pWindClient = NULL;
	m_ShimRoot.m_Areas.m_EffectsRenderClient.m_pSpritesClient = NULL;
	m_ShimRoot.m_Areas.m_FxClient.m_pSpritesClient = NULL;
	m_ShimRoot.m_pAppSystemFactory = nullptr;
	m_ShimRoot.m_pPhysicsFactory = nullptr;
	m_Engines.m_pEvents = nullptr;
}

// Called at startup and on every vid_mode change so the HUD can reinitialize.
int CShellClientDLL::HudVidInit()
{
	m_Hud.VidInit();

	m_VoiceStatus.VidInit();

	return 1;
}

// Lets the client filter input messages before the move record is sent.
void CShellClientDLL::HudProcessInput(bool bActive)
{
	m_ClientMode.ProcessInput(bActive);
}

void CShellClientDLL::HudUpdate(bool bActive)
{
	float frametime = m_Engines.m_pGlobals->frametime;

	m_VoiceStatus.Frame(frametime);

	// The menu layer's home: the GameUI root while the engine's GameUI is up.
	m_HudUi.MenuRootFollowGameUI();

	m_Hud.UpdateHud(bActive);

	m_Systems.Update(frametime);
}

void CShellClientDLL::HudReset()
{
	m_Hud.VidInit();
	m_Physics.Reset();
}

void CShellClientDLL::HudText(const char* message) {}

bool CShellClientDLL::ShouldDrawDropdownConsole()
{
	return true;
}

ClientClass* CShellClientDLL::GetAllClasses()
{
	return g_pClientClassHead;
}

void CShellClientDLL::IN_ActivateMouse()
{
	m_Input.ActivateMouse();
}

void CShellClientDLL::IN_DeactivateMouse()
{
	m_Input.DeactivateMouse();
}

void CShellClientDLL::IN_Accumulate()
{
	m_Input.AccumulateMouse();
}

void CShellClientDLL::IN_ClearStates()
{
	m_Input.ClearStates();
}

bool CShellClientDLL::IN_IsKeyDown(const char* name, bool& isdown)
{
	// The button's state is managed's.
	IManagedInputClient* pInput = m_Managed.m_Areas.m_pInputClient;
	int32_t bDown = 0;
	if (!pInput || !pInput->InputIsKeyDown(name, &bDown))
		return false;

	isdown = bDown != 0;
	return true;
}

void CShellClientDLL::IN_OnMouseWheeled(int nDelta) {}

int CShellClientDLL::IN_KeyEvent(int eventcode, ButtonCode_t keynum, const char* pszCurrentBinding)
{
	return m_Input.KeyEvent(eventcode, keynum, pszCurrentBinding);
}

void CShellClientDLL::ExtraMouseSample(float frametime, bool active)
{
	// Compiled out in release builds, so only a debug build checks it.

	MDLCACHE_CRITICAL_SECTION_(m_Engines.m_pMdlCache);
	m_Input.ExtraMouseSample(frametime, active);
}

void CShellClientDLL::IN_SetSampleTime(float frametime)
{
	m_Input.Joystick_SetSampleTime(frametime);
	m_Input.IN_SetSampleTime(frametime);
}

// Fills in the usercmd from the current view angles and inputs.
void CShellClientDLL::CreateMove(int sequence_number, float input_sample_frametime, bool active)
{
	// Compiled out in release builds, so only a debug build checks it.

	MDLCACHE_CRITICAL_SECTION_(m_Engines.m_pMdlCache);
	m_Input.CreateMove(sequence_number, input_sample_frametime, active);
}

bool CShellClientDLL::WriteUsercmdDeltaToBuffer(bf_write* buf, int from, int to, bool isnewcommand)
{
	return m_Input.WriteUsercmdDeltaToBuffer(buf, from, to, isnewcommand);
}

void CShellClientDLL::EncodeUserCmdToBuffer(bf_write& buf, int slot)
{
	m_Input.EncodeUserCmdToBuffer(buf, slot);
}

void CShellClientDLL::DecodeUserCmdFromBuffer(bf_read& buf, int slot)
{
	m_Input.DecodeUserCmdFromBuffer(buf, slot);
}

void CShellClientDLL::View_Render(vrect_t* rect)
{
	// UNDONE: This gets hit at startup sometimes, investigate - will cause NaNs in calcs inside Render()
	if (rect->width == 0 || rect->height == 0)
		return;

	m_ViewExecutor.ViewRender(rect);
}

bool CShellClientDLL::GetPlayerView(CViewSetup& playerView)
{
	playerView = *m_Engines.m_pViewRender->GetPlayerViewSetup();
	return true;
}

// Matchmaking and rich presence: the engine may call these, and stock's PC bodies were
// empty, so they are counted refusals with stock's return values.
void CShellClientDLL::SetupGameProperties(CUtlVector<XUSER_CONTEXT>& contexts, CUtlVector<XUSER_PROPERTY>& properties)
{
	SS_CLIENTDLL_UNREACHABLE("IBaseClientDLL::SetupGameProperties");
}

uint CShellClientDLL::GetPresenceID(const char* pIDName)
{
	SS_CLIENTDLL_UNREACHABLE("IBaseClientDLL::GetPresenceID");
	return 0;
}

const char* CShellClientDLL::GetPropertyIdString(const uint id)
{
	SS_CLIENTDLL_UNREACHABLE("IBaseClientDLL::GetPropertyIdString");
	return NULL;
}

void CShellClientDLL::GetPropertyDisplayString(uint id, uint value, char* pOutput, int nBytes)
{
	SS_CLIENTDLL_UNREACHABLE("IBaseClientDLL::GetPropertyDisplayString");
}

void CShellClientDLL::StartStatsReporting(HANDLE handle, bool bArbitrated)
{
	SS_CLIENTDLL_UNREACHABLE("IBaseClientDLL::StartStatsReporting");
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
	if (IManagedRuntimeClient* pRuntime = m_Managed.m_pRuntimeClient)
		pRuntime->ClientInvalidateMdlCache();
}

void CShellClientDLL::ResetManagedViewEffects()
{
	if (IManagedRenderClient* p = m_Managed.m_Areas.m_pRenderClient)
		p->ViewEffectsReset(0);
}

void CShellClientDLL::ViewFade(const ScreenFade_t& sf)
{
	if (IManagedRenderClient* p = m_Managed.m_Areas.m_pRenderClient)
		p->ViewEffectsFade(sf.duration, sf.holdTime, sf.fadeFlags, sf.r, sf.g, sf.b, sf.a,
						   m_Engines.m_pGlobals->curtime);
}

void CShellClientDLL::View_Fade(ScreenFade_t* pSF)
{
	if (pSF != NULL)
		ViewFade(*pSF);
}

void CShellClientDLL::LevelInitPreEntity(char const* pMapName)
{
	// Managed decides whether this level re-reads every particle definition file or only
	// re-precaches; with no runtime, the stock re-read.
	PumpWindowDuringLoad();
	IManagedRuntimeClient* pRuntime = m_Managed.m_pRuntimeClient;
	if (!pRuntime || pRuntime->ParticlesNeedFullReload(m_nPreviousMapParticleFiles))
		m_ParticleManifest.Reload();
	else
		m_ParticleManifest.PrecacheDefined();
	PumpWindowDuringLoad();

	if (m_bLevelInitialized)
		return;
	m_bLevelInitialized = true;

	m_Input.LevelInit();

	ResetManagedViewEffects();

	// How many files this read is what the next level's reload decision needs.
	m_nPreviousMapParticleFiles = m_ParticleManifest.ParseMapFiles(pMapName, true);

	m_ClientMode.LevelInit(pMapName);
	m_ParticleMgr.LevelInit();

	m_EffectsList.Flush();
	m_Engines.m_pViewRender->LevelInit();
	m_Engines.m_pTempEnts->LevelInit();
	// ResetToneMapping( 1.0 )'s native half.
	{
		CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
		pRenderContext->ResetToneMappingScale(1.0f);
	}

	m_Systems.LevelInitPreEntity();

	m_Wind.Reset();

	// Whether this level predicts is managed's; once per level, past the gate above.
	if (pRuntime)
		pRuntime->LevelInitPreEntity();

	m_Hud.LevelInit();
}

void CShellClientDLL::LevelInitPostEntity()
{
	PumpWindowDuringLoad();
	m_Systems.LevelInitPostEntity();
	m_CenterPrint.Clear();
}

void CShellClientDLL::ResetStringTablePointers()
{
	m_Engines.m_pEffectDispatchTable = nullptr;
	m_Engines.m_pParticleEffectNames = nullptr;
}

void CShellClientDLL::LevelShutdown()
{
	if (!m_bLevelInitialized)
		return;

	m_bLevelInitialized = false;

	m_Systems.LevelShutdownPreEntity();

	m_ClientMode.LevelShutdown();

	// The client mode's level shutdown reset the third-person camera (stock's
	// g_ThirdPersonManager.Init); the camera is managed's.
	m_Input.ThirdPersonInit();

	// Temp entities go before the entity list so the te_* can clean up.
	m_Engines.m_pTempEnts->LevelShutdown();

	// Shells, then the world, then the post-entity system shutdown: the server's order, and
	// a shell's destructor can reach the world. Both must precede the subsystem reset below,
	// which shells hold handles into.

	// After the temp entities and before the post-entity system pass, which resets
	// subsystems a surviving shell would hold handles into.
	const int nShells = m_Standalone.LevelShutdown();
	const bool bWorld = m_World.LevelShutdown();

	Msg("[SourceSharp] client level shutdown: destroyed %d standalone shell(s), "
		"world %s\n",
		nShells, bWorld ? "released" : "was not bound");

	m_Systems.LevelShutdownPostEntity();

	m_Engines.m_pViewRender->LevelShutdown();
	m_Beams.ClearBeams();
	// The new-style effects removed, then the particle materials released.
	m_NewParticles.RemoveAll();
	m_ParticleMgr.LevelShutdown();

	m_Input.StopAllRumble();

	m_Hud.LevelShutdown();

	m_CenterPrint.Clear();

	m_Engines.m_pParticleMgr->UncacheAllParticleSystems();
	m_MaterialCache.UncacheAll();

	// String tables are cleared on disconnect, so reset the global pointers.
	ResetStringTablePointers();
}

// No crosshair element exists, so the autoaim offset goes nowhere; the engine calls it.
void CShellClientDLL::SetCrosshairAngle(const QAngle& angle) {}

// Initializes a sprite from its .spr.
void CShellClientDLL::InitSprite(CEngineSprite* pSprite, const char* loadname)
{
	if (pSprite)
	{
		pSprite->Init(loadname, m_Managed.m_Areas.m_pMaterialFilesClient, m_Engines.m_pMaterials);
	}
}

void CShellClientDLL::ShutdownSprite(CEngineSprite* pSprite)
{
	if (pSprite)
	{
		pSprite->Shutdown();
	}
}

// Tells the engine how much space to allocate for sprite objects.
int CShellClientDLL::GetSpriteSize() const
{
	return sizeof(CEngineSprite);
}

void CShellClientDLL::VoiceStatus(int entindex, qboolean bTalking)
{
	m_VoiceStatus.UpdateSpeakerStatus(entindex, !!bTalking);

	// The managed UI's voice panel shows who is speaking.
	if (IManagedUiClient* pUi = m_Managed.m_Areas.m_pUiClient)
		pUi->VoiceStatus(entindex, bTalking ? 1 : 0);
}

// String-table callbacks: the object they are installed with is this DLL object.
void CShellClientDLL::OnMaterialTableChanged(void* object, INetworkStringTable* stringTable, int stringNumber,
											 const char* newString, void const* newData)
{
	CMaterialCacheClient& cache = static_cast<CShellClientDLL*>(object)->m_MaterialCache;
	cache.Precache(newString);
	cache.RequestCache();
}

void CShellClientDLL::OnParticleTableChanged(void* object, INetworkStringTable* stringTable, int stringNumber,
											 const char* newString, void const* newData)
{
	CShellClientDLL* pDll = static_cast<CShellClientDLL*>(object);
	pDll->m_Engines.m_pParticleMgr->PrecacheParticleSystem(newString);
	pDll->m_MaterialCache.RequestCache();
}

// The table definition has been parsed but no data added yet.
void CShellClientDLL::InstallStringTableCallback(const char* tableName)
{
	if (!Q_strcasecmp(tableName, "Materials"))
	{
		m_Engines.m_pStringTables->FindTable(tableName)->SetStringChangedCallback(this, OnMaterialTableChanged);
	}
	else if (!Q_strcasecmp(tableName, "EffectDispatch"))
	{
		m_Engines.m_pEffectDispatchTable = m_Engines.m_pStringTables->FindTable(tableName);
	}
	else if (!Q_strcasecmp(tableName, "ParticleEffectNames"))
	{
		m_Engines.m_pParticleEffectNames = m_Engines.m_pStringTables->FindTable(tableName);
		m_Engines.m_pStringTables->SetAllowClientSideAddString(m_Engines.m_pParticleEffectNames, true);
		m_Engines.m_pParticleEffectNames->SetStringChangedCallback(this, OnParticleTableChanged);
	}

	m_GameRules.InstallStringTableCallback(m_Engines.m_pStringTables);
}

void CMaterialCacheClient::Precache(const char* pMaterialName)
{
	Assert(pMaterialName);

	int nLen = Q_strlen(pMaterialName);
	char* pTempBuf = (char*)stackalloc(nLen + 1);
	memcpy(pTempBuf, pMaterialName, nLen + 1);
	char* pFound = Q_strstr(pTempBuf, ".vmt\0");
	if (pFound)
	{
		*pFound = 0;
	}

	IMaterial* pMaterial = m_Engines.m_pMaterials->FindMaterial(pTempBuf, TEXTURE_GROUP_PRECACHED);
	if (!IsErrorMaterial(pMaterial))
	{
		pMaterial->IncrementReferenceCount();
		m_Cached.AddToTail(pMaterial);
	}
	else
	{
		if (IsOSX())
		{
			printf("\n ##### CMaterialCacheClient::Precache could not find material %s (%s)", pMaterialName, pTempBuf);
		}
	}
}

void CMaterialCacheClient::UncacheAll()
{
	for (int i = m_Cached.Count(); --i >= 0;)
	{
		m_Cached[i]->DecrementReferenceCount();
	}
	m_Cached.RemoveAll();
}

void CMaterialCacheClient::ProcessCache()
{
	if (!m_bRequestCache)
		return;

	m_bRequestCache = false;
	m_Engines.m_pMaterials->CacheUsedMaterials();
}

// One forward of the user message to managed: the payload is copied whole into one
// length-prefixed BitBlock; one too large answers its prefix as minus the size needed.
// False outside a managed load.
bool CShellClientDLL::DispatchUserMessage(int msg_type, bf_read& msg_data)
{
	IManagedUserMessagesClient* pMessages = m_Managed.m_Areas.m_pUserMessagesClient;
	if (!pMessages)
		return false;
	uint8_t block[ss::kBitBlockHeaderBytes + kMaxUserMessageBlockBytes];
	const int32_t nBits = ss::BitBlockFromEngine(msg_data, block, sizeof(block));
	if (nBits < 0)
		memcpy(block, &nBits, sizeof(nBits));
	return pMessages->DispatchUserMessage(msg_type, block, sizeof(block));
}

// The PVS-notify walks. No renderable in this build answers a non-NULL
// GetPVSNotifyInterface(), so the list is always empty.
void UpdateClientRenderableInPVSStatus() {}

void UpdatePVSNotifiers() {}

void CShellClientDLL::OnRenderStart()
{
	MDLCACHE_CRITICAL_SECTION_(m_Engines.m_pMdlCache);
	MDLCACHE_COARSE_LOCK_(m_Engines.m_pMdlCache);

	m_Engines.m_pPartition->SuppressLists(PARTITION_ALL_CLIENT_EDICTS, true);

	// Camera simulation before OnRenderStart, which uses it.
	m_Input.CAM_Think();

	// Places the player, the view models and parent entities so attachment points are right.
	m_Engines.m_pViewRender->OnRenderStart();

	m_Rope.OnRenderStart();

	m_Engines.m_pPartition->SuppressLists(PARTITION_ALL_CLIENT_EDICTS, false);

	// The world's deferred OnDataChanged: it must see a complete net update.
	if (C_World* pWorld = m_World.Entity())
	{
		pWorld->DeliverPendingDataChanged();
	}

	// Entities can change the overlay alpha in their thinks.
	m_EffectsList.SetSmokeFogAlpha(0);

	m_Physics.Simulate();

	{
		m_Engines.m_pEngine->FireEvents();

		m_Engines.m_pTempEnts->Update();

		m_Beams.UpdateTempEntBeams();
	}

	{
		FPExceptionEnabler enableExceptions;
		// The old-style effects, then the new-style ones, as UpdateAllEffects ordered them.
		m_ManagedParticles.Frame(m_Engines.m_pGlobals->frametime);
		m_NewParticles.Frame(m_Engines.m_pGlobals->frametime);
	}
}

void OnRenderEnd()
{
	UpdatePVSNotifiers();
}

// The client frame's one crossing: every stage, unconditionally, before the switch, so
// managed hears a stage before the native work it announces. The stage number is the
// engine's ClientFrameStage_t, passed through unchanged.

// How many times the engine called FrameStageNotify.
void CShellClientDLL::FrameStageNotify(ClientFrameStage_t curStage)
{
	++m_nFrameStageNotifyCalls;
	m_CurFrameStage = curStage;

	// NULL before the first load and after the last unload.
	if (IManagedRuntimeClient* pRuntime = m_Managed.m_pRuntimeClient)
	{
		++m_nClientStageCrossings;
		pRuntime->ClientStage((int32_t)curStage);
	}

	switch (curStage)
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
			m_Engines.m_pPartition->SuppressLists(PARTITION_ALL_CLIENT_EDICTS, true);
		}
		break;
		case FRAME_NET_UPDATE_END:
		{
			m_MaterialCache.ProcessCache();

			m_Engines.m_pPartition->SuppressLists(PARTITION_ALL_CLIENT_EDICTS, false);
		}
		break;
		case FRAME_NET_UPDATE_POSTDATAUPDATE_START:
			break;
		case FRAME_NET_UPDATE_POSTDATAUPDATE_END:
		{
			m_Prediction.PostEntityPacketReceived();
		}
		break;
		case FRAME_START:
		{
			m_EffectsList.SetCreationAllowed(true);
		}
		break;
	}
}

// Save/restore hooks. The engine asks SaveInit for the client state buffer first and
// abandons the save on NULL, so the three that move bytes forward to the managed save
// codec. The client entity block is empty, which the engine accepts.
CSaveRestoreData* CShellClientDLL::SaveInit(int size)
{
	IManagedSaveRestoreClient* pArea = m_Managed.m_Areas.m_pSaveRestoreClient;
	CSaveRestoreData* pSaveData =
		pArea ? static_cast<CSaveRestoreData*>(pArea->SaveInit(size, m_Engines.m_pGlobals->curtime)) : NULL;

	if (pSaveData == NULL)
	{
		// The engine abandons the save here silently, so say so.
		Warning("[SourceSharp] client SaveInit is answering NULL: the engine will "
				"abandon the client state file and, with it, the whole save, "
				"printing nothing\n");
	}

	return pSaveData;
}

// pMap is unused, as stock's. sizeof( typedescription_t ) is passed because the struct
// holds a member-function pointer whose width is the C++ ABI's.
void CShellClientDLL::SaveWriteFields(CSaveRestoreData* pSaveData, const char* pname, void* pBaseData, datamap_t* pMap,
									  typedescription_t* pFields, int fieldCount)
{
	if (IManagedSaveRestoreClient* pArea = m_Managed.m_Areas.m_pSaveRestoreClient)
		pArea->SaveWriteFields(pSaveData, pname, pBaseData, pFields, (int32_t)sizeof(typedescription_t), fieldCount);
}

void CShellClientDLL::SaveReadFields(CSaveRestoreData* pSaveData, const char* pname, void* pBaseData, datamap_t* pMap,
									 typedescription_t* pFields, int fieldCount)
{
	if (IManagedSaveRestoreClient* pArea = m_Managed.m_Areas.m_pSaveRestoreClient)
		pArea->SaveReadFields(pSaveData, pname, pBaseData, pFields, (int32_t)sizeof(typedescription_t), fieldCount);
}

// The other six move no bytes: entities are not saved through a datamap. A no-op is
// symmetric across save and load.
void CShellClientDLL::PreSave(CSaveRestoreData* s) {}

void CShellClientDLL::Save(CSaveRestoreData* s) {}

void CShellClientDLL::WriteSaveHeaders(CSaveRestoreData* s) {}

void CShellClientDLL::ReadRestoreHeaders(CSaveRestoreData* s) {}

void CShellClientDLL::Restore(CSaveRestoreData* s, bool b) {}

// The engine calls it; a managed entity's restore is managed's.
void CShellClientDLL::DispatchOnRestore() {}

void CShellClientDLL::WriteSaveGameScreenshot(const char* pFilename)
{
	m_Engines.m_pViewRender->WriteSaveGameScreenshot(pFilename);
}

// The engine's close-caption slots; the captions this client shows are managed's.
void CShellClientDLL::EmitSentenceCloseCaption(char const* tokenstream) {}

void CShellClientDLL::EmitCloseCaption(char const* captionname, float duration) {}

CStandardRecvProxies* CShellClientDLL::GetStandardRecvProxies()
{
	return &g_StandardRecvProxies;
}

bool CShellClientDLL::CanRecordDemo(char* errorMsg, int length) const
{
	return m_ClientMode.CanRecordDemo(errorMsg, length);
}

void CShellClientDLL::OnDemoRecordStart(char const* pDemoBaseName)
{
	m_ClientMode.OnDemoRecordStart(pDemoBaseName);
}

void CShellClientDLL::OnDemoRecordStop()
{
	m_ClientMode.OnDemoRecordStop();
}

void CShellClientDLL::OnDemoPlaybackStart(char const* pDemoBaseName) {}

void CShellClientDLL::OnDemoPlaybackStop() {}

int CShellClientDLL::GetScreenWidth()
{
	return ScreenWidth(m_Engines);
}

int CShellClientDLL::GetScreenHeight()
{
	return ScreenHeight(m_Engines);
}

void CShellClientDLL::WriteSaveGameScreenshotOfSize(const char* pFilename, int width, int height,
													bool bCreatePowerOf2Padded /*=false*/, bool bWriteVTF /*=false*/)
{
	m_Engines.m_pViewRender->WriteSaveGameScreenshotOfSize(pFilename, width, height, bCreatePowerOf2Padded, bWriteVTF);
}

// See RenderViewInfo_t
void CShellClientDLL::RenderView(const CViewSetup& setup, int nClearFlags, int whatToDraw)
{
	m_Engines.m_pViewRender->RenderView(setup, nClearFlags, whatToDraw);
}

// For sv_pure: the filesystem works out which files to reload.
void CShellClientDLL::ReloadFilesInList(IFileList* pFilesToReload)
{
	m_Engines.m_pSoundEmitter->ReloadSoundEntriesInList(pFilesToReload);
}

bool CShellClientDLL::HandleUiToggle()
{
	return false;
}

bool CShellClientDLL::ShouldAllowConsole()
{
	return true;
}

CRenamedRecvTableInfo* CShellClientDLL::GetRenamedRecvTableInfos()
{
	return NULL;
}

// The mouth info for sounds played inside UI panels.
CMouthInfo* CShellClientDLL::GetClientUIMouthInfo()
{
	return &m_ClientUIMouth;
}

void CShellClientDLL::FileReceived(const char* fileName, unsigned int transferID)
{
	if (m_GameRules.Rules())
	{
		m_GameRules.Rules()->OnFileReceived(fileName, transferID);
	}
}

void CShellClientDLL::ClientAdjustStartSoundParams(StartSoundParams_t& params) {}

const char* CShellClientDLL::TranslateEffectForVisionFilter(const char* pchEffectType, const char* pchEffectName)
{
	if (!m_GameRules.Rules())
		return pchEffectName;

	return m_GameRules.Rules()->TranslateEffectForVisionFilter(pchEffectType, pchEffectName);
}

bool CShellClientDLL::DisconnectAttempt()
{
	bool bRet = false;

	return bRet;
}

bool CShellClientDLL::IsConnectedUserInfoChangeAllowed(IConVar* pCvar)
{
	return m_GameRules.Rules() ? m_GameRules.Rules()->IsConnectedUserInfoChangeAllowed(NULL) : true;
}

// TF-only methods nothing is seen to call: they return stock's harmless value and record
// a refusal.
bool CShellClientDLL::BHaveChatSuspensionInCurrentMatch()
{
	SS_CLIENTDLL_UNREACHABLE("IBaseClientDLL::BHaveChatSuspensionInCurrentMatch");
	return false;
}

void CShellClientDLL::DisplayVoiceUnavailableMessage()
{
	SS_CLIENTDLL_UNREACHABLE("IBaseClientDLL::DisplayVoiceUnavailableMessage");
}

// Prints the refusal tally; zero means every method the engine called was answered.
void CShellClientDLL::RefusalsCommand(const CCommand& args)
{
	ShellClientDll_PrintRefusals(m_Refusals);
}

// The native count of stage crossings, which must equal the engine's FrameStageNotify
// calls. It counts even before the runtime loads.
void CShellClientDLL::StageCrossingsCommand(const CCommand& args)
{
	Msg("[SourceSharp] FrameStageNotify: %llu call(s), %llu crossing(s) into managed code\n",
		(unsigned long long)m_nFrameStageNotifyCalls, (unsigned long long)m_nClientStageCrossings);
}

void CShellClientDLL::EffectsReportCommand(const CCommand& args)
{
	SourceSharpEffects_Report(m_EffectLedger);
}

void CShellClientDLL::DumpPayloadCommand(const CCommand& args)
{
	SourceSharpPayload_DumpCommand(args);
}

void CShellClientDLL::NotShippedCommand(const CCommand& args)
{
	SourceSharp_PrintNotShipped();
}

// Sends a managed message to the server on the engine's net channel, reliable.
int32_t CNetChannelClient::SendServerMessage(const char* pKey, const uint8_t* pData, int32_t nLen)
{
	INetChannel* pChannel = static_cast<INetChannel*>(m_Engines.m_pEngine->GetNetChannelInfo());
	if (!pChannel)
		return 0;

	// About 4 KiB: on the heap, freed once SendNetMsg has written it.
	CSourceSharpClientMessage* pMsg = new CSourceSharpClientMessage(NULL, SourceSharp_WriteClientMessage);
	const bool bSent = pMsg->Set(pKey, pData, nLen) && pChannel->SendNetMsg(*pMsg, true);
	delete pMsg;
	return bSent ? 1 : 0;
}

// The net channel reads for the dev API's netgraph route. Each answers how many values it
// wrote, 0 with no channel.
INetChannelInfo* CNetChannelClient::Channel()
{
	return m_Engines.m_pEngine->GetNetChannelInfo();
}

static int32_t ShellPut(float* pOut, int32_t nOut, int32_t i, float flValue)
{
	if (pOut && i < nOut)
		pOut[i] = flValue;
	return i + 1;
}

// Latency, avg latency, avg loss, avg choke, avg data, avg packets, total data, sequence
// number, for one flow (0 outgoing, 1 incoming).
int32_t CNetChannelClient::ClientNetFlow(int32_t nFlow, float* pOut, int32_t nOut)
{
	INetChannelInfo* pInfo = Channel();
	if (!pInfo || nFlow < 0 || nFlow >= MAX_FLOWS)
		return 0;

	int32_t i = 0;
	i = ShellPut(pOut, nOut, i, pInfo->GetLatency(nFlow));
	i = ShellPut(pOut, nOut, i, pInfo->GetAvgLatency(nFlow));
	i = ShellPut(pOut, nOut, i, pInfo->GetAvgLoss(nFlow));
	i = ShellPut(pOut, nOut, i, pInfo->GetAvgChoke(nFlow));
	i = ShellPut(pOut, nOut, i, pInfo->GetAvgData(nFlow));
	i = ShellPut(pOut, nOut, i, pInfo->GetAvgPackets(nFlow));
	i = ShellPut(pOut, nOut, i, (float)pInfo->GetTotalData(nFlow));
	i = ShellPut(pOut, nOut, i, (float)pInfo->GetSequenceNr(nFlow));
	return i;
}

// Net time, time connected, buffer size, data rate, loopback, timing out, time since last
// received, remote frame time and deviation, timeout seconds, interpolation amount.
int32_t CNetChannelClient::ClientNetChannel(float* pOut, int32_t nOut)
{
	INetChannelInfo* pInfo = Channel();
	if (!pInfo)
		return 0;

	float flFrameTime = 0.0f, flDeviation = 0.0f;
	pInfo->GetRemoteFramerate(&flFrameTime, &flDeviation);

	int32_t i = 0;
	i = ShellPut(pOut, nOut, i, pInfo->GetTime());
	i = ShellPut(pOut, nOut, i, pInfo->GetTimeConnected());
	i = ShellPut(pOut, nOut, i, (float)pInfo->GetBufferSize());
	i = ShellPut(pOut, nOut, i, (float)pInfo->GetDataRate());
	i = ShellPut(pOut, nOut, i, pInfo->IsLoopback() ? 1.0f : 0.0f);
	i = ShellPut(pOut, nOut, i, pInfo->IsTimingOut() ? 1.0f : 0.0f);
	i = ShellPut(pOut, nOut, i, pInfo->GetTimeSinceLastReceived());
	i = ShellPut(pOut, nOut, i, flFrameTime);
	i = ShellPut(pOut, nOut, i, flDeviation);
	i = ShellPut(pOut, nOut, i, pInfo->GetTimeoutSeconds());
	i = ShellPut(pOut, nOut, i, m_Engines.m_pConVars->ClientInterpAmount());
	return i;
}

// One packet of one flow by sequence number: valid, send time, interpolation amount,
// latency (ms), choke, then its bytes per message group, GENERIC through TOTAL.
int32_t CNetChannelClient::ClientNetPacket(int32_t nFlow, int32_t nFrame, float* pOut, int32_t nOut)
{
	INetChannelInfo* pInfo = Channel();
	if (!pInfo || nFlow < 0 || nFlow >= MAX_FLOWS)
		return 0;

	int nLatency = 0, nChoke = 0;
	pInfo->GetPacketResponseLatency(nFlow, nFrame, &nLatency, &nChoke);

	int32_t i = 0;
	i = ShellPut(pOut, nOut, i, pInfo->IsValidPacket(nFlow, nFrame) ? 1.0f : 0.0f);
	i = ShellPut(pOut, nOut, i, pInfo->GetPacketTime(nFlow, nFrame));
	i = ShellPut(pOut, nOut, i, pInfo->GetCommandInterpolationAmount(nFlow, nFrame));
	i = ShellPut(pOut, nOut, i, (float)nLatency);
	i = ShellPut(pOut, nOut, i, (float)nChoke);
	for (int nGroup = INetChannelInfo::GENERIC; nGroup <= INetChannelInfo::TOTAL; nGroup++)
		i = ShellPut(pOut, nOut, i, (float)pInfo->GetPacketBytes(nFlow, nFrame, nGroup));
	return i;
}
