using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// One managed&lt;-&gt;native transition through an ABI thunk, named.
///
/// <para>
/// THE VALUES ARE POSITIONS, NOT A CONTRACT. Nothing on the wire and nothing in
/// native carries a <c>CrossingOp</c> number: the native side reads the report
/// as text, keyed by NAME. So this enum may be reordered and appended to
/// without an ABI version bump, and a lane that adds a thunk adds its name here
/// in the same commit. The serialised ABI pass (2026-10-20) removed the 26 ops
/// whose bindings had left with their slots and renumbered the rest once,
/// densely in declaration order; the per-member "Value N since" remarks that
/// tracked earlier hole-filling went with that renumber. <c>CrossingsAuditTests</c> is what makes "in the
/// same commit" true rather than hoped for — it reads each side's own entry
/// and the bindings, and fails when the lists disagree.
/// </para>
///
/// <para>
/// ONE MEMBER PER <c>[UnmanagedCallersOnly]</c> METHOD in each side's own
/// entry (<c>ServerSideEntry</c>, <c>ClientSideEntry</c>, plan_side_entries SE2)
/// or binding, spelt exactly as the method is spelt, with ONE
/// deliberate exception: <c>CrossingsReport</c>, the thunk that READS the
/// counters, because counting it would put the act of measuring into the
/// measurement.
/// </para>
///
/// <para>
/// THE BENCHMARK PAIR WAS THE SECOND EXEMPTION AND IS GONE. <c>BenchmarkNoop</c>
/// was uncounted on purpose (a floor with a counter in it is not a floor) and
/// <c>BenchmarkTypical</c> was counted on purpose (a real shipped thunk costs an
/// interlocked increment). Their only caller anywhere was
/// <c>shell_benchmark.cpp</c>'s <c>cs_bench_boundary</c>, deleted by lane 1c-sv
/// with 0 consumers, and lane 1e-abi removed both ABI slots at version 245.
/// Whatever prices a crossing next wants the same pair back, and the argument
/// for the asymmetry is recorded here rather than re-derived.
/// </para>
/// </summary>
public enum CrossingOp
{
    /// <summary>
    /// plan_managed_vtable_reload.md L5: native's reload asks whether the build
    /// on disk keeps every held slot hash. 0 since plan_client_vtables CC-last
    /// (2b), the value LoadSide V1 left when it was deleted (the ops stay
    /// contiguous from 0); it was 144.
    /// </summary>
    ReloadCheckServer = 0,
    UnloadServer = 1,
    /// <summary>IManagedClientSystemsClient::Stage: one stage of a kClientSystems[] row whose system is managed's (C7, vgui_game_viewport.cpp's row first). CLIENT ONLY.</summary>
    ClientSystemStage = 2,
    /// <summary>IManagedPixelVisClient::EndView: a view's end asks the managed pixel visibility for the proxies to draw (C7, c_pixel_visibility.cpp). CLIENT ONLY.</summary>
    ClientPixelVisEndView = 3,
    /// <summary>IManagedRenderClient::ScreenSpaceEffectsStage: the view's level start or end runs the managed screen-space effect manager's Init or Shutdown (C7 screen-space effects). CLIENT ONLY.</summary>
    ClientScreenSpaceEffectsStage = 4,
    ClientFrame = 5,
    /// <summary>IManagedPixelVisClient::Commands: a view's end whose commands did not fit asks for them again into a larger buffer. CLIENT ONLY.</summary>
    ClientPixelVisCommands = 6,
    ClientLevelInit = 7,
    /// <summary>IManagedPixelVisClient::EndScene: the scene's end frees the sets and queries nobody asked about. CLIENT ONLY.</summary>
    ClientPixelVisEndScene = 8,
    ClientLevelShutdown = 9,
    ServerConCommand = 10,
    ClientConCommand = 11,
    ServerSystemStart = 12,
    ServerGameStart = 13,
    ServerGamePostStart = 14,
    ServerGameStop = 15,
    ServerMapLoadTransition = 16,
    ServerMapLoadSaveGame = 17,
    ServerMapLoadBackground = 18,
    ServerMapLoadNewGame = 19,
    ServerMapUnload = 20,
    ServerActivate = 21,
    ServerCreateNetworkStringTables = 22,
    ServerGameFrame = 23,
    ServerPreClientUpdate = 24,
    ServerThink = 25,
    ServerTickInterval = 26,
    ServerShouldHideServer = 27,
    ServerBrowserMapOverride = 28,
    ServerBrowserGameData = 29,
    ServerIsManualMapChangeOkay = 30,
    ServerQueryCvarValueFinished = 31,
    ServerHibernation = 32,
    ServerInvalidateMdlCache = 33,
    ServerSteamGameServerActivated = 34,
    ServerSteamGameServerShutdown = 35,
    ServerMapLoadPreEntity = 36,
    /// <summary>IManagedGlowClient::DrawOverlays: the plan's glow overlay draw asks the managed system for its commands (C7, glow_overlay.cpp). CLIENT ONLY.</summary>
    ClientGlowDrawOverlays = 37,
    ServerSkyboxAreaForClient = 38,
    ServerEarPositionForClient = 39,
    ServerDecalIndexForName = 40,
    ServerDecalNameForIndex = 41,
    ServerDecalTranslateForGameMaterial = 42,
    ServerSoundEntInit = 43,
    ServerSoundEntShutdown = 44,
    ServerSoundEntResetClientSound = 45,
    ServerStatusLines = 46,
    ServerGetGameDescription = 47,
    ServerOnClientPutInServer = 48,
    ServerOnClientActive = 49,
    /// <summary>IManagedResources::ModelPrecacheCheck: Precache_Model's late-precache warning, decided in managed, once per model precache (plan_server_native_v2 T4e).</summary>
    ServerModelPrecacheCheck = 50,
    ServerConVarChanged = 51,
    ClientConVarChanged = 52,
    ServerOnPlayerSpawn = 53,
    ServerOnPlayerKilled = 54,
    ServerOnClientConnect = 55,
    ServerOnClientCommand = 56,
    ClientOnUserMessage = 57,
    ClientNativeTempEntity = 58,
    ClientOnTempEntity = 59,
    ClientTempEntPoolUpdate = 60,
    ClientTempEntPoolClear = 61,
    ServerOnGameEvent = 62,
    ClientOnGameEvent = 63,
    ServerOnPlayerSpawned = 64,
    // EIGHTEEN MORE OPS ARE GONE, on the anim-list pair's reasoning exactly --
    // plan_shim.md §5, the third ABI tail (lane 4e-abi, ABI 249).
    //
    // ServerGetFallDamage and ServerIsTeamplay counted the two questions
    // CSourceSharpGameRules asked managed code, and that class no longer asks
    // them. Server/ClientOnPlayerJump, Server/ClientOnPlayerFriction and
    // Server/ClientOnPlayerMaxSpeed counted the three movement decisions asked
    // from inside stock's PlayerMove chain; the managed loop reaches
    // GameMovement's Rules (jump, friction, max speed) with no boundary
    // in between, which is the REDUCTION MoveProcessMovement's own comment in
    // Entry.cs predicted -- those three crossings going to zero is the whole
    // point of the loop being managed, and an op that can only ever report zero
    // is an instrument lying about a cost nobody pays. The ten Move* ops
    // counted the piecewise E1 forwarders, whose only call sites were in
    // game/shared/gamemovement.cpp, a file that left both vpcs.
    //
    // Renumbered, for the reason given below: the values index s_Names and must
    // stay 0..OpCount-1 with no gaps. The ABI slots behind all eighteen are
    // REMOVED in the same commit rather than left in place -- this is that pass.
    //
    // THE TWO ANIM-LIST OPS ARE GONE, and the members below them
    // renumbered -- plan_shim.md Phase 2d.
    //
    // AnimListActivityNames and AnimListEventNames counted the per-level
    // feed into a NATIVE registry of activity and event names. The registry
    // is deleted and so are the thunks, and an op left behind by a deleted
    // thunk compiles fine and reports a permanent zero -- which is why
    // CrossingsAuditTests states the enum/thunk equality in BOTH directions.
    //
    // RENUMBERING THIS ENUM IS NOT THE ABI PASS'S JOB, and the distinction
    // matters: CrossingOp is managed-only, its values are an index into a
    // name table derived from the enum itself, and nothing native reads
    // them. The ABI SLOTS that fed these two are marked SS_RETIRED and left
    // in place, which is the thing a lane must not renumber.
    ClientStandaloneCreate = 65,
    ClientStandaloneDraw = 66,
    ClientStandaloneSpawn = 67,
    ClientStandaloneDataChanged = 68,
    ClientStandaloneRemoved = 69,
    ClientStandaloneDormancyChanged = 70,
    ServerSelectSpawnPoint = 71,
    /// <summary>IManagedClients::ServerAreaPortalVisibility: ClientSetupVisibility's area portal walk, once per client per frame, over the managed AreaPortals (plan_server_native_v2 T4d).</summary>
    ServerAreaPortalVisibility = 72,
    ClientNetStateFieldOffset = 73,
    ClientNetStateFieldBits = 74,
    ServerReloadSave = 75,
    ServerReloadRestore = 76,
    ClientReloadSave = 77,
    ClientReloadRestore = 78,
    /// <summary>IManagedSaveRestore::SaveWriteBody: IServerGameDLL::Save's one body block, the managed world, once per save. Was ServerSaveCollect (the entity block's retired SaveCollectEntities slot) until plan_server_native_v2 T4c; the value is kept.</summary>
    ServerSaveWriteBody = 79,
    /// <summary>IManagedSaveRestore::SaveReadBody: IServerGameDLL::Restore's body block, once per load. Was ServerSaveRestore (SaveRestoreEntities) until plan_server_native_v2 T4c; the value is kept.</summary>
    ServerSaveReadBody = 80,
    /// <summary>IManagedUiClient::Paint, once per root panel a frame (plan_client_ui U2): the HUD's paint, which was ClientOnHudPaint. CLIENT ONLY.</summary>
    ClientUiPaint = 81,
    ServerWorldKeyValue = 82,
    ClientStage = 83,
    ServerDispatch = 84,

    // Parenting, step 2h made live. ELEVEN THUNKS AND NOT FIFTEEN CALL SITES:
    // "is this entity parented" and "who carries it" are answered from the
    // engine block with no crossing at all, and the once-a-frame sweep and the
    // level teardown run inside crossings that already happen.
    //
    // The two that matter to a budget are the first two, and both are gated on a
    // block word: ParentMoved fires only for an entity something hangs off, and
    // ChildMoved only for one something carries. In a map that parents nothing
    // every counter below stays at zero, which is what makes them worth reading
    // in `cs_sv_crossings`.
    ServerHierarchyParentMoved = 85,
    ServerHierarchyChildMoved = 86,
    ServerHierarchySetParent = 87,
    ServerHierarchyGetParentAttachment = 88,
    ServerHierarchySetParentName = 89,
    ServerHierarchySetParentAttachmentName = 90,
    ServerHierarchyToLocal = 91,
    ServerHierarchyToWorld = 92,
    ServerHierarchySetLocal = 93,

    /// <summary>
    /// plan_entity.md step 2c. Native code asking the directory to mark an
    /// entity for removal.
    ///
    /// <para>
    /// <b>This counter should read ZERO on a normal map.</b> Managed code
    /// removing something marks in the directory without crossing at all
    /// (<c>EntityRemovalRoutes</c>), so the only thing that reaches this thunk
    /// is a native caller — today exactly one,
    /// <c>mapentities.cpp</c>'s template-source removal, which dies with that
    /// file in step 2d. A non-zero reading here is the measurement that says
    /// which native caller is left.
    /// </para>
    /// </summary>
    ServerEntityRemoveRequested = 94,
    // plan_entity.md step 3e. ONE crossing per command per player: the whole of
    // ProcessMovement.
    //
    // ONE op for a thunk both sides share, which is what the ten Move* leaves
    // above already do and what CrossingsAuditTests requires -- a thunk counts
    // the op of its own name. "Which side ran it" is answered by
    // cs_sv_move_managed_report and cs_cl_move_managed_report, which count in
    // the NATIVE forwarder and can therefore tell "the managed body ran" from
    // "the managed body was asked for"; tools/movemath-gate reads those.
    ClientMoveProcessMovement = 95,

    /// <summary>
    /// plan_entity.md step 3d. ONE crossing per <c>ProcessUsercmds</c>, WHATEVER
    /// N IS — the whole decoded packet, and this side answers the flatten.
    ///
    /// <para>
    /// <b>THIS COUNTER ON ITS OWN CANNOT SAY THE STEP WORKS.</b> A build that
    /// stopped running commands altogether would also cost exactly one crossing
    /// per packet and would read identically here. The commands actually RUN
    /// are counted separately, in the managed loop itself, and printed by
    /// <c>cs_sv_playercmds</c> — see <c>PlayerCommandQueue.Counters</c>.
    /// </para>
    /// </summary>

    /// <summary>
    /// plan_entity.md step 3d. ONE crossing per player per frame: the queue's
    /// run. It does not grow with the queue's depth — the loop is managed and
    /// the three per-command calls it replaced are ordinary method calls now.
    /// </summary>
    ServerPlayerRunCommands = 96,
    // plan_entity.md step 6f. ONE crossing per CLIENT FRAME, not one per
    // replayed command: IPrediction::Update forwarded whole, and everything
    // under it -- the ring compare, the basis restore, the movement loop and
    // the weapon machine -- is managed code calling managed code.
    //
    // It REPLACES three crossings per replayed command (the two
    // ClientOnStandalonePlayerCommand phases and MoveProcessMovement's client
    // arm), so the client's per-frame crossing count falls with this step
    // rather than rising. cs_cl_crossings is where that is read.
    ClientPredictionUpdate = 97,
    /// <summary>IManagedGlowClient::Commands: a draw whose commands did not fit asks for them again into a larger buffer. CLIENT ONLY.</summary>
    ClientGlowCommands = 98,

    // An impact's dust (FX_DustImpact, dirt and sand), asked once per impact by
    // shell_impact_effects.cpp (lane c5-particles). CLIENT ONLY. It took 125 when
    // ClientImpactEffectParams retired with the native impact emitters: the
    // blocks are built by the managed effects themselves now.
    ClientParticlesImpactDust = 99,

    /// <summary>
    /// A sprite draw asking the managed planner (shell_sprite_leaf.cpp's
    /// SourceSharp_PlanSprite reaching SpriteRenderer): one per sprite drawn.
    /// It took the value of ClientSpriteRenderRules, the once-a-level rule
    /// table it replaced. CLIENT ONLY.
    /// </summary>
    ClientFxSpritePlan = 100,

    // plan_entity.md §3.10, the policy-player lane. CLIENT ONLY, and entered
    // only while `thirdperson` is on -- a developer command, not a mode this mod
    // ships in. So this op reads 0 in every ordinary frame and twice per frame
    // in third person, which is what makes it worth counting separately rather
    // than folding into a client-view op.
    ClientThirdPersonCamera = 101,

    /// <summary>
    /// IBaseClientDLL::InvalidateMdlCache, lane diag-visibility. CLIENT ONLY,
    /// and rare by construction: it is reached from `flush` and `flush_locked`
    /// (both FCVAR_CHEAT) and from the engine's own model reload, so it reads 0
    /// in every ordinary session.
    /// </summary>
    ClientInvalidateMdlCache = 102,

    /// <summary>
    /// Which effect family an impact is, plan_shim.md §6 lane 4b-effects.
    /// CLIENT ONLY. One per impact, asked before the trace, and it is the
    /// ceiling this move has to stay under: a bullet that hits something now
    /// crosses twice (this, then ImpactEffectParams for the family it chose)
    /// where it crossed once.
    /// </summary>
    ClientImpactChoice = 103,

    /// <summary>
    /// One packet of user commands, decoded, planned and queued in C# —
    /// plan_server_novalve.md P2.2. SERVER ONLY. Replaces
    /// the retired <c>ServerPlayerCommands</c> crossing (which was the plan alone,
    /// after a native decode) at one crossing per packet.
    /// </summary>
    ServerPlayerDecodeCommands = 104,

    /// <summary>
    /// A newly precached model's components (sounds, particles), precached in
    /// C# — plan_server_novalve.md P2.4/P3. SERVER ONLY. One per new model.
    /// </summary>
    ServerModelPrecacheComponents = 105,

    /// <summary>
    /// A new user command's random seed (MD5_PseudoRandom of its number),
    /// answered in C#. CLIENT ONLY. One per command the client creates.
    /// </summary>
    ClientMd5PseudoRandom = 106,

    /// <summary>
    /// plan_engineapi_vtables.md step A3: the server's load with its shim
    /// root. Once per side load, like <see cref="LoadSide"/>.
    /// </summary>
    LoadServer = 107,

    /// <summary>
    /// plan_engineapi_vtables E13: the server's native temp-entity forward,
    /// counted by TempEntsBinding since the shared thunk became the client's
    /// (<see cref="ClientNativeTempEntity"/>).
    /// </summary>
    ServerNativeTempEntity = 108,

    /// <summary>
    /// plan_engineapi_vtables E16: the server's net-state layout asks, counted by
    /// NetStateLayoutBinding since the shared thunks became the client's.
    /// </summary>
    ServerNetStateFieldOffset = 109,
    ServerNetStateFieldBits = 110,

    /// <summary>IManagedTraceFilter::ShouldHitEntity: the engine asks the managed trace filter about one candidate entity, mid-trace, once per candidate per server trace (plan_server_native_v2 T4f).</summary>
    ServerTraceFilterShouldHitEntity = 111,

    /// <summary>
    /// The client's three entries from native (plan_side_entries SE1), beside
    /// the server's (LoadServer, ReloadCheckServer, UnloadServer): one per side,
    /// so no side travels as an argument.
    /// </summary>
    LoadClient = 112,
    ReloadCheckClient = 113,
    UnloadClient = 114,

    /// <summary>
    /// Each side's managed build stamp (plan_side_entries SE2, the integrator's
    /// ruling A): the Bootstrap's fourth call into the side's own entry, right
    /// after its Load, once the module's start-up has read its assemblies.
    /// </summary>
    StampedServer = 115,
    StampedClient = 116,

    /// <summary>
    /// The client asking, at each level's LevelInitPreEntity, whether the
    /// particle definitions must be re-read from disk or only re-precached
    /// (plan_map_load P8). CLIENT ONLY, once per level.
    /// </summary>
    ClientParticlesNeedFullReload = 117,

    /// <summary>
    /// A listen server's spawn asking the client which materials to reload in
    /// place of the engine's full ReloadMaterials(NULL) (plan_map_load P1).
    /// CLIENT ONLY, once per spawn.
    /// </summary>
    ClientMapMaterialReload = 118,

    /// <summary>A client's SourceSharp net message (ss_client_message.h) reaching the server.</summary>
    ServerOnClientNetMessage = 119,

    /// <summary>IManagedUiClient::ScreenSizeChanged, on a video-mode change. CLIENT ONLY.</summary>
    ClientUiScreenSizeChanged = 120,

    /// <summary>
    /// The client's decal tables (decals.cpp's CDecalEmitterSystem, lane
    /// c2-sims): its three system steps, once each, and the three questions
    /// impact effects and temp entities ask (shell_decals_client.cpp).
    /// CLIENT ONLY.
    /// </summary>
    ClientDecalsInit = 121,
    ClientDecalsLevelInitPreEntity = 122,
    ClientDecalsShutdown = 123,
    ClientDecalIndexForName = 124,
    ClientDecalNameForIndex = 125,
    ClientDecalTranslate = 126,

    /// <summary>
    /// The old-style particle manager's crossings (C7 particles phase 2.3):
    /// the frame's update of every binding, once a frame; each drawn
    /// executor's draw decisions and its sort keys back. They took the values
    /// of the sims' three per-effect steps (Emit, Simulate, Draw), which are
    /// calls inside managed now. CLIENT ONLY.
    /// </summary>
    ClientParticlesFrame = 127,
    ClientParticlesDrawBegin = 128,
    ClientParticlesDrawEnd = 129,

    /// <summary>
    /// point_worldtext's glyph quads, asked per draw by the native text mesh
    /// renderer (shell_worldtext_render.cpp, lane c2-sims). CLIENT ONLY.
    /// </summary>
    ClientWorldTextDraw = 130,
    /// <summary>
    /// A native caller's effect dispatch (<c>DispatchEffectToCallback</c>)
    /// reaching the managed dispatch table, plan_client_novalve §2 cluster 4.
    /// CLIENT ONLY. One per dispatch a native effect or forward raises.
    /// </summary>
    ClientDispatchEffectIn = 131,

    /// <summary>
    /// The client's IEffects thunk (shell_effects_client.cpp) carrying one call
    /// to ClientEffects' decision, plan_client_novalve §2 cluster 3. CLIENT
    /// ONLY. One per IEffects call a native caller makes.
    /// </summary>
    ClientFxSmoke = 132,
    ClientFxSparks = 133,
    ClientFxDust = 134,
    ClientFxMuzzleFlash = 135,
    ClientFxMetalSparks = 136,
    ClientFxEnergySplash = 137,
    ClientFxRicochet = 138,
    ClientFxSuppressEffectsSounds = 139,

    /// <summary>
    /// The client-side effect list's native entries (shell_effects_list_client.cpp)
    /// reaching ClientEffectList: the view's once-a-frame draw, the client DLL's
    /// flush, the level start, and the FX_Add*/FX_TracerSound calls the fork's
    /// effect TUs make. CLIENT ONLY.
    /// </summary>
    ClientFxDrawEffects = 140,
    ClientFxFlushEffects = 141,
    ClientFxEffectsLevelInit = 142,
    ClientFxAddLine = 143,
    ClientFxAddQuad = 144,
    ClientFxAddStaticLine = 145,
    ClientFxAddDiscreetLine = 146,
    ClientFxTracerSound = 147,
    ClientFxAddQuadNamed = 148,

    /// <summary>
    /// A managed env_spritetrail's renderable reaching ClientSpriteTrails:
    /// once a frame per trail to tick (PreRender), once per draw. CLIENT ONLY.
    /// </summary>
    ClientFxSpriteTrailTick = 149,
    ClientFxSpriteTrailDraw = 150,

    /// <summary>
    /// The client effect precache system's level start and end reaching
    /// ClientEffectPrecache: twice a level. CLIENT ONLY.
    /// </summary>
    ClientFxEffectsPrecache = 151,

    /// <summary>
    /// The smoke fog overlay asking SmokeFogOverlay whether to draw: once a
    /// frame. CLIENT ONLY.
    /// </summary>
    ClientFxSmokeFogPlan = 152,

    /// <summary>IManagedUiClient::Input, one per event the menu root receives (plan_client_ui U3). CLIENT ONLY.</summary>
    ClientUiInput = 153,

    /// <summary>IManagedUiClient::ViewportPanel, a viewport panel shown or hidden (+showscores, VGUIMenu). CLIENT ONLY.</summary>
    ClientUiViewportPanel = 154,

    /// <summary>
    /// The client's sound envelope controller (sound cluster 17, lane c2-sims):
    /// shell_soundenvelope_client.cpp's calls into the shared controller. CLIENT ONLY.
    /// </summary>
    ClientSoundControllerCreate = 155,
    ClientSoundControllerDestroy = 156,
    ClientSoundControllerPlay = 157,
    ClientSoundControllerPlayEnvelope = 158,
    ClientSoundControllerChangePitch = 159,
    ClientSoundControllerChangeVolume = 160,
    ClientSoundControllerGetPitch = 161,
    ClientSoundControllerGetVolume = 162,
    ClientSoundControllerFadeOut = 163,
    ClientSoundControllerCommandAdd = 164,
    ClientSoundControllerCommandClear = 165,
    ClientSoundControllerReset = 166,
    ClientSoundControllerShutdown = 167,

    /// <summary>
    /// The client soundscape system's client-system shell
    /// (shell_soundscape_audio_client.cpp) reaching ClientSoundscapes: a level
    /// init and a level shutdown per level, an Update per frame. CLIENT ONLY.
    /// </summary>
    ClientSoundscapesLevelInit = 168,
    ClientSoundscapesUpdate = 169,
    ClientSoundscapesLevelShutdown = 170,

    /// <summary>
    /// The legacy tempents' thunk (shell_legacy_tempents_client.cpp) reaching
    /// ClientTempEntDecisions: the render choices once or twice per drawn
    /// tempent a frame, the rest per flash, casing, attach, bounce and level.
    /// CLIENT ONLY.
    /// </summary>
    ClientFxTempEntIsTransparent = 171,
    ClientFxTempEntRenderGroup = 172,
    ClientFxTempEntBlend = 173,
    ClientFxTempEntOccludedAlpha = 174,
    ClientFxTempEntLightingOrigin = 175,
    ClientFxTempEntMuzzleFlash = 176,
    ClientFxTempEntEjectBrass = 177,
    ClientFxTempEntAttach = 178,
    ClientFxTempEntsLevelInit = 179,

    /// <summary>
    /// The client weather's render plan (shell_precipitation_client.cpp's
    /// DrawPrecipitation reaching PrecipitationSystem): once a render pass that
    /// draws the weather. CLIENT ONLY.
    /// </summary>
    ClientPrecipitationRender = 180,

    /// <summary>
    /// vphysics' IPhysicsObjectEvent::ObjectWake, on managed's own table
    /// (plan_physics_managed B2.1). Mid-step: queued, routed after.
    /// </summary>
    ServerPhysicsObjectWake = 181,

    /// <summary>IPhysicsObjectEvent::ObjectSleep, the same.</summary>
    ServerPhysicsObjectSleep = 182,

    // vphysics' IPhysicsCollisionEvent on managed's own table (plan_physics_managed
    // B2.2), one op per slot in Valve's order; a slot not yet taken over
    // forwards to native's handler.

    /// <summary>IPhysicsCollisionEvent::PreCollision.</summary>
    ServerPhysicsCollisionPreCollision = 183,

    /// <summary>IPhysicsCollisionEvent::PostCollision.</summary>
    ServerPhysicsCollisionPostCollision = 184,

    /// <summary>IPhysicsCollisionEvent::Friction.</summary>
    ServerPhysicsCollisionFriction = 185,

    /// <summary>IPhysicsCollisionEvent::StartTouch.</summary>
    ServerPhysicsCollisionStartTouch = 186,

    /// <summary>IPhysicsCollisionEvent::EndTouch.</summary>
    ServerPhysicsCollisionEndTouch = 187,

    /// <summary>IPhysicsCollisionEvent::FluidStartTouch.</summary>
    ServerPhysicsCollisionFluidStartTouch = 188,

    /// <summary>IPhysicsCollisionEvent::FluidEndTouch.</summary>
    ServerPhysicsCollisionFluidEndTouch = 189,

    /// <summary>IPhysicsCollisionEvent::PostSimulationFrame.</summary>
    ServerPhysicsCollisionPostSimulationFrame = 190,

    /// <summary>IPhysicsCollisionEvent::ObjectEnterTrigger.</summary>
    ServerPhysicsCollisionObjectEnterTrigger = 191,

    /// <summary>IPhysicsCollisionEvent::ObjectLeaveTrigger.</summary>
    ServerPhysicsCollisionObjectLeaveTrigger = 192,

    /// <summary>
    /// The material proxies (lane c5-proxies): the trampoline's Init, each
    /// OnBind (per bound material per frame: the hot one), Release, and
    /// CMatStubHandler's per-frame mat_stub question. CLIENT ONLY.
    /// </summary>
    ClientMaterialProxyInit = 193,
    ClientMaterialProxyBind = 194,
    ClientMaterialProxyRelease = 195,
    ClientMaterialProxyMatStub = 196,

    /// <summary>IManagedUiClient::CenterPrint, the engine's ICenterPrint (VCENTERPRINT002) print. CLIENT ONLY.</summary>
    ClientUiCenterPrint = 197,

    /// <summary>IManagedUiClient::CenterClear, the engine's ICenterPrint clear (level change). CLIENT ONLY.</summary>
    ClientUiCenterClear = 198,

    /// <summary>IManagedUiClient::PlaqueState, the engine facts the loading and paused plaques are decided from (on change only). CLIENT ONLY.</summary>
    ClientUiPlaqueState = 199,

    /// <summary>IManagedUiClient::VoiceStatus, the engine's IClientDLL::VoiceStatus (a speaker started or stopped). CLIENT ONLY.</summary>
    ClientUiVoiceStatus = 200,

    /// <summary>IManagedUiClient::KeyInput, a key the engine offers the client mode (stock ClientModeShared::KeyInput). CLIENT ONLY.</summary>
    ClientUiKeyInput = 201,

    /// <summary>IManagedUiClient::VoiceIsBlocked, whether this client muted a player (IGameClientExports). CLIENT ONLY.</summary>
    ClientUiVoiceIsBlocked = 202,

    /// <summary>IManagedUiClient::VoiceToggleBlocked, a player's mute flipped (IGameClientExports). CLIENT ONLY.</summary>
    ClientUiVoiceToggleBlocked = 203,

    /// <summary>IManagedClients::OnClientSettingsChanged: a client's userinfo changed (CTeamplayRules::ClientSettingsChanged's name half).</summary>
    ServerOnClientSettingsChanged = 204,

    /// <summary>IManagedConsoleOutput::ConsoleBufferAttach: the forwarder's line buffer, once per load.</summary>
    ServerConsoleBufferAttach = 205,

    /// <summary>IManagedConsoleOutputClient::ClientConsoleBufferAttach: the client forwarder's line buffer.</summary>
    ClientConsoleBufferAttach = 206,

    // vphysics' IPhysicsCollisionSolver on managed's own table
    // (plan_physics_bones_native_zero 2.1), one op per slot in Valve's order;
    // asked mid-step.

    /// <summary>IPhysicsCollisionSolver::ShouldCollide.</summary>
    ServerPhysicsSolverShouldCollide = 207,

    /// <summary>IPhysicsCollisionSolver::ShouldSolvePenetration.</summary>
    ServerPhysicsSolverShouldSolvePenetration = 208,

    /// <summary>IPhysicsCollisionSolver::ShouldFreezeObject.</summary>
    ServerPhysicsSolverShouldFreezeObject = 209,

    /// <summary>IPhysicsCollisionSolver::AdditionalCollisionChecksThisTick.</summary>
    ServerPhysicsSolverAdditionalCollisionChecksThisTick = 210,

    /// <summary>IPhysicsCollisionSolver::ShouldFreezeContacts.</summary>
    ServerPhysicsSolverShouldFreezeContacts = 211,

    /// <summary>IManagedBones::TestHitboxes: an engine trace against a standalone entity's posed hitboxes.</summary>
    ServerBonesTestHitboxes = 212,

    /// <summary>IManagedClients::ServerTransmitAlwaysForClient, CheckTransmit's per-recipient "always send" edict (stock ShouldTransmit's own-player arm).</summary>
    ServerTransmitAlwaysForClient = 213,
    /// <summary>
    /// The render plan (plan_client_novalve C7): View_Render's per-frame
    /// question (which path draws the frame, and is it recorded), and a
    /// recorded frame's text handed back. CLIENT ONLY.
    /// </summary>
    ClientRenderFrame = 214,
    ClientRenderTraceDone = 215,

    /// <summary>The render plan for one RenderView: the executor hands the inputs, managed writes the ops. CLIENT ONLY.</summary>
    ClientRenderPlanBuild = 216,

    /// <summary>A view's custom-draw entities prepared in one crossing (C7 item 7, shell_standalone_client.cpp's draw batch). CLIENT ONLY.</summary>
    ClientStandaloneDrawBatch = 217,

    /// <summary>
    /// The particle query particles.a calls back (IParticleSystemQuery, its
    /// five live methods), answered by the managed ParticleSystemQuery through
    /// the native thunk (shell_managed_particles.cpp, lane c5-particles).
    /// CLIENT ONLY.
    /// </summary>
    ClientParticleQueryCollisionGroup = 218,
    ClientParticleQueryLighting = 219,
    ClientParticleQueryTrace = 220,
    ClientParticleQueryHitBoxPoints = 221,
    ClientParticleQueryHitBoxInfo = 222,
    /// <summary>An impact's debris (FX_DebrisFlecks: the flecks and the dust behind them), asked once per impact by shell_impact_effects.cpp (lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesImpactDebris = 223,

    /// <summary>An impact's metal spark, asked once per metal impact by shell_impact_effects.cpp (lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesImpactMetalSpark = 224,

    /// <summary>A splash or ripple fx_water drew, asked by the native code that still calls it (physics.cpp's splashes, the rain's ripple; lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesWaterSplash = 225,

    /// <summary><c>UTIL_GetNormalizedColorTintAndLuminosity</c>, asked by physics.cpp's splash lighting (lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesSplashTint = 226,

    /// <summary>A frame's rain tracers' corners, one crossing per precipitation render (shell_precipitation_client.cpp's DrawPrecipitation; lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesTracerQuads = 227,
    /// <summary>A world view's draws over its lists, asked once a view with its scene (C7 step 2, shell_view_executor_client.cpp's ViewScene op). CLIENT ONLY.</summary>
    ClientRenderViewProgram = 228,
    /// <summary>viewpostprocess.cpp's passes as a program of ops, asked at the plan's EnginePostProcessing and MotionBlur ops (C7 post-processing). CLIENT ONLY.</summary>
    ClientRenderPostProgram = 229,

    /// <summary>
    /// The client's input (cluster 13, lane c6-input): the native IInput thunk
    /// set's calls into ClientInput, one per IManagedInputClient slot. CLIENT ONLY.
    /// </summary>
    ClientInputInit = 230,
    ClientInputShutdown = 231,
    ClientInputLevelInit = 232,
    ClientInputCreateMove = 233,
    ClientInputExtraMouseSample = 234,
    ClientInputGetButtonBits = 235,
    ClientInputMakeWeaponSelection = 236,
    ClientInputKeyState = 237,
    ClientInputKeyEvent = 238,
    ClientInputIsKeyDown = 239,
    ClientInputJoystickAdvanced = 240,
    ClientInputAccumulateMouse = 241,
    ClientInputActivateMouse = 242,
    ClientInputDeactivateMouse = 243,
    ClientInputClearStates = 244,
    ClientInputGetLookSpring = 245,
    ClientInputGetFullscreenMousePos = 246,
    ClientInputSetFullscreenMousePos = 247,
    ClientInputResetMouse = 248,
    ClientInputGetLastForwardMove = 249,
    ClientInputJoystickAxes = 250,
    ClientInputClearInputButton = 251,
    ClientInputCamThink = 252,
    ClientInputCamOp = 253,
    ClientInputCamOrthographicSize = 254,
    ClientInputCamSetCameraThirdData = 255,
    ClientInputSetPreferredGameActionSet = 256,
    ClientInputGetPreferredGameActionSet = 257,
    ClientInputSetGameActionSetFlags = 258,
    ClientInputEnableJoystickMode = 259,
    ClientInputIsSteamControllerActive = 260,
    ClientInputThirdPersonInit = 261,
    ClientInputRumbleScreenShake = 262,
    ClientInputRumbleStopAll = 263,

    /// <summary>
    /// The client's IPhysicsCollisionEvent on managed's own table (plan_client_novalve
    /// C6, cluster 9; lane c6-input): one op per slot in Valve's order; a slot not yet
    /// taken over forwards to native's g_Collisions. CLIENT ONLY.
    /// </summary>
    ClientPhysicsPreCollision = 264,
    ClientPhysicsPostCollision = 265,
    ClientPhysicsFriction = 266,
    ClientPhysicsStartTouch = 267,
    ClientPhysicsEndTouch = 268,
    ClientPhysicsFluidStartTouch = 269,
    ClientPhysicsFluidEndTouch = 270,
    ClientPhysicsPostSimulationFrame = 271,
    ClientPhysicsObjectEnterTrigger = 272,
    ClientPhysicsObjectLeaveTrigger = 273,

    /// <summary>The client's IPhysicsCollisionSolver on managed's own table (lane c6-input), one op per slot. CLIENT ONLY.</summary>
    ClientPhysicsShouldCollide = 274,
    ClientPhysicsShouldSolvePenetration = 275,
    ClientPhysicsShouldFreezeObject = 276,
    ClientPhysicsAdditionalCollisionChecks = 277,
    ClientPhysicsShouldFreezeContacts = 278,

    // vphysics' IPhysicsConstraintEvent and IMotionEvent on managed's own
    // tables (plan_physics_bones_native_zero 2.2/2.4). Simulate is asked
    // mid-step, once per object a controller acts on. SERVER.

    /// <summary>IPhysicsConstraintEvent::ConstraintBroken.</summary>
    ServerPhysicsConstraintBroken = 279,

    /// <summary>IMotionEvent::Simulate.</summary>
    ServerPhysicsMotionSimulate = 280,

    /// <summary>IManagedBonesClient (plan physics_bones_native_zero 1.1-1.2, client half): the engine's skeleton asks of a standalone entity. CLIENT ONLY.</summary>
    ClientBonesSetupBones = 281,
    ClientBonesTestHitboxes = 282,
    ClientBonesGetAttachment = 283,
    ClientBonesLookupAttachment = 284,

    /// <summary>IManagedBonesClient::PoseModel: a legacy temp entity's model posed at sequence 0 (no entity behind it). CLIENT ONLY.</summary>
    ClientBonesPoseModel = 285,

    /// <summary>IManagedPhysicsClient: the client physics system's level events (pre/post entity init), forwarded by native's CPhysicsSystem row. They took IManagedRagdollsClient's 313/314 when the step called the ragdolls itself (the client physics leftovers, 2026-10-18). CLIENT ONLY.</summary>
    ClientPhysicsLevelInitPreEntity = 286,
    ClientPhysicsLevelInitPostEntity = 287,

    /// <summary>IVPhysicsKeyHandler on managed's custom-block table: one key of a ragdoll's .phy block (collision rules, animated friction), and its defaults, asked from inside vphysics' parse. CLIENT ONLY.</summary>
    ClientRagdollKeyValue = 288,
    ClientRagdollKeyDefaults = 289,

    /// <summary>IVPhysicsKeyHandler on managed's solid-defaults table (stock's g_SolidSetup): a solid's unparsed key, and its defaults. CLIENT ONLY.</summary>
    ClientRagdollSolidKeyValue = 290,
    ClientRagdollSolidDefaults = 291,

    /// <summary>IManagedPhysicsFrame (plan physics_bones_native_zero 2.6): the level's world physics built and the surface sounds precached, each asked by native's CPhysicsHook. SERVER ONLY.</summary>
    ServerPhysicsCreateWorld = 292,
    ServerPhysicsPrecacheSurfaceSounds = 293,
    // 322 was ServerPhysicsParseModelSolid until native's InitPhysics retired
    // (2026-10-12), then ClientSolidDefaults until g_SolidSetup left with fork
    // physics.cpp (2026-10-18); ClientPhysicsLevelShutdownPreEntity has it now.

    /// <summary>IVPhysicsKeyHandler on the server's solid-defaults table (stock's g_SolidSetup): a solid's unparsed key, and its defaults, asked from inside vphysics' parse. SERVER ONLY.</summary>
    ServerSolidKeyValue = 294,
    ServerSolidDefaults = 295,

    /// <summary>IManagedPhysicsClient (plan physics_bones_native_zero 2.6): the surface property manifest parsed, asked by native's PhysicsDLLInit. CLIENT ONLY.</summary>
    ClientPhysicsParseSurfaceData = 296,

    /// <summary>IManagedPhysicsClient: the level's shutdown events, the step and the reset (the client physics leftovers, 2026-10-18), in the numbers CreateWorld, PrecacheSurfaceSounds and g_SolidSetup's two slots left: the world, the precache and the static props' key table are ClientPhysicsSystem's own now. CLIENT ONLY.</summary>
    ClientPhysicsLevelShutdownPreEntity = 297,
    ClientPhysicsLevelShutdownPostEntity = 298,
    ClientPhysicsSimulate = 299,
    ClientPhysicsReset = 300,
    /// <summary>A fade from native (the engine's View_Fade, the world's start-dark fade) into the managed view effects (C7). CLIENT ONLY.</summary>
    ClientViewEffectsFade = 301,
    /// <summary>The view's CalcShake, once a frame: the managed view effects' offset and roll (C7). CLIENT ONLY.</summary>
    ClientViewEffectsCalcShake = 302,
    /// <summary>The renderer's GetFadeParams, once a frame (C7). CLIENT ONLY.</summary>
    ClientViewEffectsFadeParams = 303,
    /// <summary>The level's view-effects reset and the clears (C7). CLIENT ONLY.</summary>
    ClientViewEffectsReset = 304,

    /// <summary>IManagedBonesClient::BoneBatch (plan_physics_bones_perf A2): once a frame, at its first SetupBones ask, the frame's skeletons posed ahead and the table native answers the rest of the frame's asks from. CLIENT ONLY.</summary>
    ClientBonesBatch = 305,
    /// <summary>A shadow-depth view's plan, asked by the shadow manager inside the frame's ShadowComputeDepthTextures op (C7 step 3). CLIENT ONLY.</summary>
    ClientRenderShadowDepthPlan = 306,
    /// <summary>CViewRender::SetUpViews' planes and FOV, once a frame before the client mode's override (C7 step 3). CLIENT ONLY.</summary>
    ClientViewSetUp = 307,
    /// <summary>IManagedInputClient::InputEncodeUserCmd.</summary>
    ClientInputEncodeUserCmd = 308,
    /// <summary>IManagedInputClient::InputDecodeUserCmd.</summary>
    ClientInputDecodeUserCmd = 309,
    /// <summary>IManagedParticlesClient::ManifestFiles: a particle manifest's file list, read in C# (C9 KeyValues K1). CLIENT ONLY.</summary>
    ClientParticlesManifestFiles = 310,
    /// <summary>The save-game screenshot's view (CViewRender::WriteSaveGameScreenshotOfSize), once a screenshot (C7 step 3). CLIENT ONLY.</summary>
    ClientViewScreenshot = 311,
    /// <summary>CViewRender::FreezeFrame into the managed freeze state (test_freezeframe; C7 step 3). CLIENT ONLY.</summary>
    ClientViewFreezeFrame = 312,
    /// <summary>IManagedMaterialFilesClient::SpriteMaterial: a sprite model's material name and .vmt, read in C# (C9 KeyValues K4). CLIENT ONLY.</summary>
    ClientMaterialFilesSpriteMaterial = 313,
    /// <summary>The demo free camera (view.cpp's CalcDemoViewOverride), twice a frame while a demo plays (C7 step 3). CLIENT ONLY.</summary>
    ClientViewDemoCamera = 314,
    /// <summary>The old-style particle manager's frame code (CParticleMgr::IncrementFrameCode), once per scene view (C7 particles phase 2.3). CLIENT ONLY.</summary>
    ClientParticlesFrameCode = 315,
    /// <summary>The old-style particle manager's PostRender, once a frame (C7 particles phase 2.3). CLIENT ONLY.</summary>
    ClientParticlesPostRender = 316,

    /// <summary>IManagedMaterialFilesClient::EncodeImage: a screenshot's pixels encoded as its TGA or VTF file in C# (C9: bitmap.a and vtf.a off client.so). CLIENT ONLY.</summary>
    ClientMaterialFilesEncodeImage = 317,

    /// <summary>IManagedDetailPropsClient::DetailInit: the detail props' Init, once (C7 cluster 10). CLIENT ONLY.</summary>
    ClientDetailInit = 318,
    /// <summary>IManagedDetailPropsClient::DetailLevelInitPreEntity: the detail lump parsed in C#, once a level. CLIENT ONLY.</summary>
    ClientDetailLevelInitPreEntity = 319,
    /// <summary>IManagedDetailPropsClient::DetailLevelInitPostEntity: the fades restored and the sprite material named, once a level. CLIENT ONLY.</summary>
    ClientDetailLevelInitPostEntity = 320,
    /// <summary>IManagedDetailPropsClient::DetailLevelShutdownPreEntity, once a level. CLIENT ONLY.</summary>
    ClientDetailLevelShutdownPreEntity = 321,
    /// <summary>IManagedDetailPropsClient::DetailBuildLists: a view's fade and model states, once per view that builds entities. CLIENT ONLY.</summary>
    ClientDetailBuildLists = 322,
    /// <summary>IManagedDetailPropsClient::DetailBeginTranslucent, once per translucent pass. CLIENT ONLY.</summary>
    ClientDetailBeginTranslucent = 323,
    /// <summary>IManagedDetailPropsClient::DetailPrepare: one of a translucent draw's halves, up to its render context. CLIENT ONLY.</summary>
    ClientDetailPrepare = 324,
    /// <summary>IManagedDetailPropsClient::DetailEmit: a prepared draw's quads. CLIENT ONLY.</summary>
    ClientDetailEmit = 325,
    /// <summary>The new-style particle effects' frame (CParticleMgr::UpdateNewEffects), once a frame; the library steps the listed collections during it (C7 particles phase 3). CLIENT ONLY.</summary>
    ClientParticlesNewEffectsFrame = 326,
    /// <summary>One new-style effect's draw decisions (CNewParticleEffect::DrawModel), per drawn effect per view (C7 particles phase 3). CLIENT ONLY.</summary>
    ClientParticlesNewEffectDraw = 327,
    /// <summary>IManagedGlowClient::UpdateSkyOverlays: the sky overlays' obstruction. CLIENT ONLY.</summary>
    ClientGlowUpdateSkyOverlays = 328,
    /// <summary>IManagedSaveRestore::SaveInit: the save block allocated and placed by the managed SaveGameSession, once per save (plan_server_native_v2 T4c).</summary>
    ServerSaveInit = 329,
    /// <summary>IManagedSaveRestore::SaveWriteFields: one of the engine's own structs into the save through the managed codec, a few dozen per save (plan_server_native_v2 T4c).</summary>
    ServerSaveWriteFields = 330,
    /// <summary>IManagedSaveRestore::SaveReadFields: one of the engine's own structs read back, a few dozen per load (plan_server_native_v2 T4c).</summary>
    ServerSaveReadFields = 331,
    /// <summary>IManagedLeafSystemClient::LevelInitPreEntity: the level's leaves made, at LevelInitPreEntity (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafLevelInitPreEntity = 332,
    /// <summary>IManagedLeafSystemClient::LevelShutdownPostEntity: the registry purged and the sub-systems' data handed back to delete, at LevelShutdownPostEntity (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafLevelShutdownPostEntity = 333,
    /// <summary>IManagedLeafSystemClient::PreRender: the dirty renderables re-inserted into their leaves, once a frame (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafPreRender = 334,
    /// <summary>IManagedLeafSystemClient::TakeOps: the rest of an answer's operations, when they filled the forwarder's buffer (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafTakeOps = 335,
    /// <summary>IManagedLeafSystemClient::CreateRenderableHandle: a renderable registered by handle (the engine's static props; particles) (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafCreateRenderableHandle = 336,
    /// <summary>IManagedLeafSystemClient::AddRenderable: a renderable registered in a group, inserted at the next PreRender (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafAddRenderable = 337,
    /// <summary>IManagedLeafSystemClient::RemoveRenderable: a renderable removed (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafRemoveRenderable = 338,
    /// <summary>IManagedLeafSystemClient::AddRenderableToLeaves: the engine's static prop into the leaves it computed (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafAddRenderableToLeaves = 339,
    /// <summary>IManagedLeafSystemClient::SetRenderGroup: a renderable's render group set (a temp entity's blend sets one) (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafSetRenderGroup = 340,
    /// <summary>IManagedLeafSystemClient::RenderableChanged: a renderable moved or changed bounds: dirty until PreRender (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafRenderableChanged = 341,
    /// <summary>IManagedLeafSystemClient::EnableAlternateSorting: a translucent renderable's alternate sorting set (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafEnableAlternateSorting = 342,
    /// <summary>IManagedLeafSystemClient::DrawStaticProps: the static props' switch (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafDrawStaticProps = 343,
    /// <summary>IManagedLeafSystemClient::DrawSmallEntities: the small entities' switch (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafDrawSmallEntities = 344,
    /// <summary>IManagedLeafSystemClient::GetRenderableLeaves: a renderable's leaves (IsRenderableInPVS, GetRenderableLeaves) (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafGetRenderableLeaves = 345,
    /// <summary>IManagedLeafSystemClient::GetRenderableLeaf: one step of a walk over a renderable's leaves (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafGetRenderableLeaf = 346,
    /// <summary>IManagedLeafSystemClient::SetSubSystemDataInLeaf: a leaf's sub-system data set (the detail props' fast-sprite marker) (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafSetSubSystemDataInLeaf = 347,
    /// <summary>IManagedLeafSystemClient::GetSubSystemDataInLeaf: a leaf's sub-system data asked (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafGetSubSystemDataInLeaf = 348,
    /// <summary>IManagedLeafSystemClient::SetDetailObjectsInLeaf: a leaf's detail objects set, at level load (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafSetDetailObjectsInLeaf = 349,
    /// <summary>IManagedLeafSystemClient::GetDetailObjectsInLeaf: a leaf's detail objects asked (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafGetDetailObjectsInLeaf = 350,
    /// <summary>IManagedLeafSystemClient::DrawDetailObjectsInLeaf: a leaf in the detail props' sphere marked, per leaf per detail build (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafDrawDetailObjectsInLeaf = 351,
    /// <summary>IManagedLeafSystemClient::ShouldDrawDetailObjectsInLeaf: decision 3 for one leaf (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafShouldDrawDetailObjectsInLeaf = 352,
    /// <summary>IManagedLeafSystemClient::DetailLeaves: decision 3 for a view's leaves, once per view scene (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafDetailLeaves = 353,
    /// <summary>IManagedLeafSystemClient::ComputeTranslucentRenderLeaf: a view's blends and translucent leaves, once per view with a translucent-leaf pass (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafComputeTranslucentRenderLeaf = 354,
    /// <summary>IManagedLeafSystemClient::BuildRenderablesList: a view's render list, once per view that draws entities (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafBuildRenderablesList = 355,
    /// <summary>IManagedLeafSystemClient::CollateViewModelRenderables: the view models' split, once per view-model draw (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafCollateViewModelRenderables = 356,
    /// <summary>IManagedLeafSystemClient::Pending: the RenderableChanged and ChangeRenderableRenderGroup calls the forwarder queued, in order, before the next call or at a blend batch's end (C7 cluster 12 L3). CLIENT ONLY.</summary>
    ClientLeafPending = 357,
    /// <summary>IManagedSoundEmitterClient::ClientSoundSystemInit: the CSoundEmitterSystem row's Init, the client's script table loaded by the managed ClientSoundEmitterSystem (ModInit). CLIENT ONLY.</summary>
    ClientSoundSystemInit = 358,
    /// <summary>IManagedSoundEmitterClient::ClientSoundSystemShutdown: the row's Shutdown (ModShutdown). CLIENT ONLY.</summary>
    ClientSoundSystemShutdown = 359,
    /// <summary>IManagedSoundEmitterClient::ClientSoundLevelInitPreEntity: the row's LevelInitPreEntity, the level's own override script. CLIENT ONLY.</summary>
    ClientSoundLevelInitPreEntity = 360,
    /// <summary>IManagedSoundEmitterClient::ClientSoundLevelShutdownPostEntity: the row's LevelShutdownPostEntity, the overrides cleared. CLIENT ONLY.</summary>
    ClientSoundLevelShutdownPostEntity = 361,
    /// <summary>IManagedSoundEmitterClient::ClientSoundEmit: a native caller's GameSound_Emit (an impact, a sound patch, a broadcast), to the managed ClientSoundEmitter. CLIENT ONLY.</summary>
    ClientSoundEmit = 362,
    /// <summary>IManagedSoundEmitterClient::ClientSoundStop: a native caller's GameSound_Stop (a sound patch). CLIENT ONLY.</summary>
    ClientSoundStop = 363,
    /// <summary>IManagedSoundEmitterClient::ClientSoundEmitCloseCaption: a native caller's GameSound_EmitCloseCaption (a sound patch's caption). CLIENT ONLY.</summary>
    ClientSoundEmitCloseCaption = 364,
    /// <summary>IManagedSaveRestoreClient::SaveInit: the client state file's block allocated and placed by the shared SaveBlockSession, once per save (the client save codec). CLIENT ONLY.</summary>
    ClientSaveInit = 365,
    /// <summary>IManagedSaveRestoreClient::SaveWriteFields: one DECALLIST or MUSICLIST struct into the client state file through the managed codec, one per decal and song per save (the client save codec). CLIENT ONLY.</summary>
    ClientSaveWriteFields = 366,
    /// <summary>IManagedSaveRestoreClient::SaveReadFields: one DECALLIST or MUSICLIST struct read back, one per decal and song per load (the client save codec). CLIENT ONLY.</summary>
    ClientSaveReadFields = 367,
    /// <summary>IManagedShadowsClient::Init: the CClientShadowMgr row's Init: the shadow materials, the render-to-texture and depth targets the modes ask for (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsInit = 368,
    /// <summary>IManagedShadowsClient::Shutdown: the row's Shutdown: every reference released (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsShutdown = 369,
    /// <summary>IManagedShadowsClient::LevelInitPreEntity: the row's LevelInitPreEntity: the ambient shadow colour, clamped (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsLevelInitPreEntity = 370,
    /// <summary>IManagedShadowsClient::LevelShutdownPostEntity: the row's LevelShutdownPostEntity: the shadows left removed (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsLevelShutdownPostEntity = 371,
    /// <summary>IManagedShadowsClient::PreRender: the walk's and the plan's PreRender, with the shells' queue: the modes and the dirty walk, once or twice a frame (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsPreRender = 372,
    /// <summary>IManagedShadowsClient::AdvanceFrame: the plan's ShadowAdvanceFrame op: the atlas's frame (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsAdvanceFrame = 373,
    /// <summary>IManagedShadowsClient::ComputeShadowTextures: the plan's per-world-view render-to-texture pass (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsComputeShadowTextures = 374,
    /// <summary>IManagedShadowsClient::ComputeShadowDepthTextures: the plan's flashlight depth views (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsComputeShadowDepthTextures = 375,
    /// <summary>IManagedShadowsClient::UnlockAllShadowDepthTextures: the plan's depth-texture unlock (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsUnlockAllShadowDepthTextures = 376,
    /// <summary>IManagedShadowsClient::RestoreRenderState: the material system's restore callback (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsRestoreRenderState = 377,
    /// <summary>IManagedShadowsClient::RenderShadowTexture: cl_drawshadowtexture's overlay (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsRenderShadowTexture = 378,
    /// <summary>IManagedShadowsClient::Pending: the shells' queue handed over before another call, or when full (C7 cluster 12 S2). CLIENT ONLY.</summary>
    ClientShadowsPending = 379,
}

/// <summary>
/// How many times each ABI thunk has been entered. The instrument the entity
/// port is measured with.
///
/// <para>
/// <b>Why it exists.</b> <c>plan_entity.md</c> §3.15 adds two counters because
/// the design depends on them, and this is the first: crossings per frame per
/// op. Every rule the port is built on is a claim about this number — one
/// <c>RunFrame</c> per server frame rather than one think per entity, one
/// <c>ProcessUsercmds</c> per command batch, one trigger-overlap report per
/// frame — and a claim about a number that nothing measures is a preference.
/// The tree already paid for that once: <c>EntityIsValid</c> ran 38,578 times
/// in ONE server frame before <see cref="LivenessMirror"/>, and it ran that way
/// for as long as it did because nothing counted it.
/// </para>
///
/// <para>
/// <b>Cheap enough to leave on, and it is left on.</b> One
/// <see cref="Interlocked.Increment(ref long)"/> against a fixed array element:
/// no allocation, no lock, no branch, nothing conditional on a ConVar. A
/// counter that has to be switched on is a counter that is off in the session
/// where the regression happened, which is the only session that mattered. The
/// cost is paid at the top of every thunk, the benchmark pair that priced it
/// included it, and the number it published was therefore the tree's own rather
/// than an argument.
/// </para>
///
/// <para>
/// <b>Interlocked and not <c>++</c>.</b> Thunks are entered from the engine's
/// main thread almost always — but not only: sound, the material system and the
/// job threads all reach the game DLL, and a lost increment on a counter whose
/// whole job is to say "this ran once per frame" would be indistinguishable
/// from the thing it is watching for. The increment is uncontended in the
/// common case, which is what makes it affordable.
/// </para>
///
/// <para>
/// <b>An instance, held by the side's <see cref="SideState"/>.</b> The side's
/// entry creates its state on the first load and the Bootstrap keeps it for
/// the side root's life (statics to zero), so each side, and each game
/// instance of a side, has its own table (cs_sv_crossings and cs_cl_crossings
/// report their own side) and it survives every hot reload of that side's game
/// assemblies. The bindings that count are handed it by their composition.
/// That is deliberate: a reload is one
/// of the things whose crossing cost is worth watching, and a counter that
/// zeroed itself across one could not see it. <see cref="Reset"/> is the only
/// thing that zeroes them, and <see cref="Resets"/> says how often it has.
/// </para>
///
/// <para>
/// <b>Inbound only, and said out loud.</b> These count native-&gt;managed
/// entries. The other direction — managed calling an engine
/// slot — is not counted here and must not be read out of these numbers;
/// <c>plan_entity.md</c> step 0e owns that half (the engine-boundary probes).
/// <see cref="Report"/> labels itself accordingly so nobody quotes a total as
/// "crossings" without the qualifier.
/// </para>
/// </summary>
public sealed class Crossings
{
    /// <summary>How many ops there are. One per counted thunk.</summary>
    public static int OpCount => s_Names.Count;

    /// <summary>
    /// Op name by value. Cached because <see cref="Report"/> is called from a
    /// console command and from the dev API's status poll, and
    /// <see cref="Enum.GetNames{T}()"/> allocates a fresh array every call.
    ///
    /// <para>
    /// Indexed by the enum value directly, which is safe only while the values
    /// are 0..OpCount-1 with no gaps. <c>CrossingsAuditTests</c> asserts
    /// exactly that rather than leaving it to the declaration's good behaviour.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyList<string> s_Names = Enum.GetNames<CrossingOp>();

    private readonly long[] _counts = new long[s_Names.Count];

    /// <summary>
    /// THE SAME INSTRUMENT, SUB-DIVIDED BY ENTITY OP (plan_entity.md §3.4,
    /// step 1f).
    ///
    /// <para>
    /// <c>ServerDispatch</c> is ONE thunk carrying fourteen different events,
    /// so its <see cref="CrossingOp"/> counter says how many entity crossings
    /// happened and not which. The override bit is a claim about a PARTICULAR
    /// op — "a class that does not override <c>OnThink</c> costs zero crossings
    /// for Think" — and a claim about a particular op cannot be tested against
    /// a total that lumps thinks in with keyvalues.
    /// </para>
    ///
    /// <para>
    /// So the dispatch thunk increments twice: once against
    /// <see cref="CrossingOp.ServerDispatch"/>, which keeps the audit's
    /// one-counter-per-thunk rule and keeps the total honest, and once here.
    /// The second increment is what the override-bit gate reads, and it is the
    /// reason that gate can tell "the op was not dispatched" from "the op was
    /// dispatched and did nothing" — a behavioural side effect cannot.
    /// </para>
    ///
    /// <para>
    /// Zeroed by <see cref="Reset"/> with everything else, so a stage's numbers
    /// describe that stage.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyList<string> s_EntityOpNames = Enum.GetNames<EntityOp>();

    private readonly long[] _entityOpCounts = new long[s_EntityOpNames.Count];

    private long _resets;

    /// <summary>
    /// The whole instrument, at the top of a thunk.
    ///
    /// <para>
    /// AggressiveInlining because the call itself would otherwise be a
    /// meaningful fraction of what it measures: the body is one indexed
    /// interlocked increment, and a non-inlined call to it costs about as much
    /// again.
    /// </para>
    ///
    /// <para>
    /// An op outside the enum's range is IGNORED rather than throwing. This runs
    /// inside a thunk, above its try/catch in most of them, and an exception
    /// thrown here would unwind into the engine's C++ frames — the one failure
    /// this whole file is not allowed to cause. The only way to reach it is a
    /// cast of a bad int, which nothing in the tree does.
    /// </para>
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Count(CrossingOp op)
    {
        int i = (int)op;
        if ((uint)i < (uint)_counts.Length)
            Interlocked.Increment(ref _counts[i]);
    }

    /// <summary>
    /// One entity op crossed. See <see cref="_entityOpCounts"/> for why this
    /// exists beside <see cref="Count"/> rather than instead of it.
    ///
    /// <para>
    /// Takes the RAW int the ABI carried rather than an
    /// <see cref="EntityOp"/>, because that is what the thunk has and because
    /// a value outside the enum has to be ignored rather than cast: a native
    /// build newer than the managed one can legitimately send an op this
    /// build has never heard of.
    /// </para>
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void CountEntityOp(int op)
    {
        if ((uint)op < (uint)_entityOpCounts.Length)
            Interlocked.Increment(ref _entityOpCounts[op]);
    }

    /// <summary>How many crossings one entity op has made since the last <see cref="Reset"/>.</summary>
    public long Read(EntityOp op)
    {
        int i = (int)op;
        return (uint)i < (uint)_entityOpCounts.Length
            ? Interlocked.Read(ref _entityOpCounts[i])
            : 0;
    }

    /// <summary>How many entity ops there are counters for.</summary>
    public static int EntityOpCount => s_EntityOpNames.Count;

    /// <summary>
    /// The entity ops that have crossed at least once, biggest first. The
    /// shape <see cref="Active"/> has, and for the same reason: fourteen zeroes
    /// in a poll that happens every second is how a field stops being read.
    /// </summary>
    public List<(string Name, long Count)> ActiveEntityOps()
    {
        var active = new List<(string, long)>();

        for (int i = 0; i < _entityOpCounts.Length; i++)
        {
            long n = Interlocked.Read(ref _entityOpCounts[i]);
            if (n != 0)
                active.Add((s_EntityOpNames[i], n));
        }

        active.Sort(static (a, b) => b.Item2 != a.Item2
            ? b.Item2.CompareTo(a.Item2)
            : string.CompareOrdinal(a.Item1, b.Item1));

        return active;
    }

    /// <summary>How many times one op has been entered since the last <see cref="Reset"/>.</summary>
    public long Read(CrossingOp op)
    {
        int i = (int)op;
        return (uint)i < (uint)_counts.Length ? Interlocked.Read(ref _counts[i]) : 0;
    }

    /// <summary>
    /// Every counter added up.
    ///
    /// <para>
    /// Not a running total kept alongside the array: a second interlocked
    /// increment would double the cost of the hot path to save a 162-element
    /// sum on a path that runs once a second at worst. It is also not an atomic
    /// snapshot — ops read early may be stale by the time the last is read —
    /// and that is fine for a rate but would not be fine for an invariant, so
    /// no invariant is stated over it.
    /// </para>
    /// </summary>
    public long Total
    {
        get
        {
            long total = 0;
            for (int i = 0; i < _counts.Length; i++)
                total += Interlocked.Read(ref _counts[i]);
            return total;
        }
    }

    /// <summary>
    /// How many server frames have been entered since the last <see cref="Reset"/>.
    /// The denominator for "crossings per frame" on the server.
    ///
    /// <para>
    /// <see cref="CrossingOp.ServerGameFrame"/>: <c>IServerGameDLL::GameFrame</c>'s
    /// one crossing, once a frame. It was <c>ServerFrame</c>, the managed frame
    /// row's own hook, until plan_server_native_v2 T4e made that a managed call
    /// with no crossing (<c>ServerSystems</c>).
    /// </para>
    /// </summary>
    public long ServerFrames => Read(CrossingOp.ServerGameFrame);

    /// <summary>The same for the client. See <see cref="ServerFrames"/>.</summary>
    public long ClientFrames => Read(CrossingOp.ClientFrame);

    /// <summary>How many times <see cref="Reset"/> has been called this process.</summary>
    public long Resets => Interlocked.Read(ref _resets);

    /// <summary>
    /// Zeroes every counter and bumps <see cref="Resets"/>.
    ///
    /// <para>
    /// This is how a measurement is scoped to a stage: the live gate resets at
    /// the top of a scenario step and reads at the bottom, so the numbers
    /// describe that step and not the map load in front of it. Counters that
    /// only ever accumulate would make every stage's budget a subtraction, and
    /// a subtraction of two numbers taken a frame apart is where an off-by-one
    /// frame hides.
    /// </para>
    ///
    /// <para>
    /// Not atomic against concurrent crossings, and it does not need to be: a
    /// crossing that lands between two elements being zeroed is counted in the
    /// new interval or the old one, and no budget is stated to that precision.
    /// It IS an interlocked write per element, so a reader never sees a torn
    /// value.
    /// </para>
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < _counts.Length; i++)
            Interlocked.Exchange(ref _counts[i], 0);

        // The per-op half goes with them. A stage that reset the totals and
        // kept the op counters would report a Think rate against a frame count
        // from a different interval, which is worse than no number at all.
        for (int i = 0; i < _entityOpCounts.Length; i++)
            Interlocked.Exchange(ref _entityOpCounts[i], 0);

        Interlocked.Increment(ref _resets);
    }

    /// <summary>
    /// The ops that have been entered at least once, biggest first, as
    /// (name, count).
    ///
    /// <para>
    /// Only the non-zero ones, because the interesting shape of this report is
    /// short: on a running server maybe two dozen of the 162 are ever entered,
    /// and 140 lines of "0" is how a report stops being read. The zero ops are
    /// not lost — <see cref="Read"/> answers for any of them by name.
    /// </para>
    /// </summary>
    public List<(string Name, long Count)> Active()
    {
        var active = new List<(string, long)>();

        for (int i = 0; i < _counts.Length; i++)
        {
            long n = Interlocked.Read(ref _counts[i]);
            if (n != 0)
                active.Add((s_Names[i], n));
        }

        active.Sort(static (a, b) => b.Item2 != a.Item2
            ? b.Item2.CompareTo(a.Item2)
            : string.CompareOrdinal(a.Item1, b.Item1));

        return active;
    }

    /// <summary>
    /// The whole report as text, for the native console command.
    ///
    /// <para>
    /// Composed managed-side rather than as numbers native formats, for the
    /// reason the op enum's own comment gives: the names are the contract and
    /// the values are positions. A native side that formatted this itself would
    /// need the name table, which would make the enum's numbering an ABI
    /// concern and every appended thunk an ABI change.
    /// </para>
    ///
    /// <para>
    /// It says INBOUND on its first line. A reader who takes "crossings: 4,812"
    /// for the whole boundary cost has been told a number that is true of half
    /// of it.
    /// </para>
    /// </summary>
    public string Report()
    {
        var active = Active();
        long total = 0;
        foreach (var (_, n) in active)
            total += n;

        long frames = ServerFrames;
        long clientFrames = ClientFrames;

        var sb = new StringBuilder();
        sb.Append("[SourceSharp] inbound ABI crossings (native->managed thunk entries)\n");
        sb.Append("[SourceSharp]   resets ").Append(Resets)
          .Append(", server frames ").Append(frames)
          .Append(", client frames ").Append(clientFrames)
          .Append(", ops entered ").Append(active.Count)
          .Append('/').Append(_counts.Length).Append('\n');
        sb.Append("[SourceSharp]   total ").Append(total);

        if (frames > 0)
            sb.Append(" (").Append(PerFrame(total, frames)).Append(" per server frame)");

        sb.Append('\n');

        foreach (var (name, n) in active)
        {
            sb.Append("[SourceSharp]   ").Append(name).Append(' ').Append(n);

            if (frames > 0)
                sb.Append(" (").Append(PerFrame(n, frames)).Append("/frame)");

            sb.Append('\n');
        }

        if (active.Count == 0)
            sb.Append("[SourceSharp]   (nothing has crossed since the last reset)\n");

        // The entity ops, broken out of ServerDispatch. One line is the whole
        // point of the override bitmask: an op a class does not override should
        // read ZERO here, and a total that lumped every entity event together
        // could not show that.
        var entityOps = ActiveEntityOps();

        foreach (var (name, n) in entityOps)
        {
            sb.Append("[SourceSharp]     op ").Append(name).Append(' ').Append(n);

            if (frames > 0)
                sb.Append(" (").Append(PerFrame(n, frames)).Append("/frame)");

            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// A rate to three decimals, invariant culture.
    ///
    /// <para>
    /// InvariantCulture explicitly: this string is parsed by the live gate, and
    /// a machine running under a comma-decimal locale would otherwise emit
    /// "1,573" for a number the gate reads as one thousand five hundred.
    /// </para>
    /// </summary>
    internal static string PerFrame(long count, long frames)
        => frames <= 0
            ? "0.000"
            : ((double)count / frames).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// <see cref="Report"/> as UTF-8, for the <c>CrossingsReport</c> ABI slot.
    ///
    /// <para>
    /// SIZE, THEN FILL — the contract <c>AnimListActivityNames</c> and
    /// <c>ISideModule.SaveCollect</c> already use in this ABI, and it is used again
    /// here for the same reason: the caller cannot know how long the report is
    /// until it is composed, and a truncating slot would hand the console half
    /// a table that reads as a whole one. So this answers the bytes the WHOLE
    /// report needs — the terminating NUL included, because native prints it as
    /// a C string — and writes NOTHING unless every one of them fits.
    /// </para>
    ///
    /// <para>
    /// <b><paramref name="reset"/> IS IGNORED ON A SIZING CALL.</b> A caller
    /// sizes with an empty buffer and fills on the second call; a reset honoured
    /// on the first would zero the counters between the two and the report that
    /// came back would describe nothing. So the reset happens only on a call
    /// that actually filled a buffer, and it happens AFTER the text is composed
    /// — <c>cs_sv_crossings reset</c> prints the interval it just closed rather
    /// than the empty one it opened.
    /// </para>
    ///
    /// <para>
    /// Returns -1 if the report could not be encoded at all, which cannot
    /// happen for ASCII and is here so a caller has one value to test rather
    /// than a length it must not trust.
    /// </para>
    /// </summary>
    public int WriteReport(Span<byte> buffer, bool reset)
    {
        string text = Report();

        int needed;
        try
        {
            needed = Encoding.UTF8.GetByteCount(text) + 1;
        }
        catch (EncoderFallbackException)
        {
            return -1;
        }

        if (buffer.Length < needed)
            return needed;

        int written = Encoding.UTF8.GetBytes(text, buffer);
        buffer[written] = 0;

        if (reset)
            Reset();

        return needed;
    }
}
