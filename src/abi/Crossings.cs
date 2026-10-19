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
/// in the same commit. <c>CrossingsAuditTests</c> is what makes "in the
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
    ServerFrame = 2,
    ServerFramePreThink = 3,
    ServerFramePostThink = 4,
    ClientFrame = 5,
    ServerLevelInit = 6,
    ClientLevelInit = 7,
    ServerLevelShutdown = 8,
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
    ServerMapUnloadPreEntity = 37,
    ServerSkyboxAreaForClient = 38,
    ServerEarPositionForClient = 39,
    ServerTemplatesAddNamed = 40,
    ServerDecalIndexForName = 41,
    ServerDecalNameForIndex = 42,
    ServerDecalTranslateForGameMaterial = 43,
    ServerPropDataRandomChunkModel = 44,
    ServerSoundEntInit = 45,
    ServerSoundEntShutdown = 46,
    ServerSoundEntInsert = 47,
    ServerSoundEntResetClientSound = 48,
    ServerStatusLines = 49,
    ServerGetGameDescription = 50,
    ServerOnClientPutInServer = 51,
    ServerOnClientActive = 52,
    ServerOnClientGamePrecache = 53,
    ServerOnRespawn = 54,
    ServerConVarChanged = 55,
    ClientConVarChanged = 56,
    ServerOnPlayerSpawn = 57,
    ServerOnPlayerKilled = 58,
    ServerOnClientConnect = 59,
    ServerOnClientCommand = 60,
    ServerGetPlayerRelationship = 61,
    ClientOnCreateMove = 62,
    ClientOnUserMessage = 63,
    ClientNativeTempEntity = 64,
    ClientOnTempEntity = 65,
    ClientTempEntPoolUpdate = 66,
    ClientTempEntPoolClear = 67,
    ServerOnGameEvent = 68,
    ClientOnGameEvent = 69,
    ServerOnPlayerSpawned = 70,
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
    ServerOnFireAddHeatInRadius = 71,
    ServerOnEntityIgnite = 72,
    ClientStandaloneCreate = 73,
    ClientStandaloneDraw = 74,
    ClientStandaloneSpawn = 75,
    ClientStandaloneDataChanged = 76,
    ClientStandaloneRemoved = 77,
    ClientStandaloneDormancyChanged = 78,
    ServerSelectSpawnPoint = 79,
    ServerOnPlayerSurfaceChanged = 80,
    ClientNetStateFieldOffset = 81,
    ClientNetStateFieldBits = 82,
    ServerReloadSave = 83,
    ServerReloadRestore = 84,
    ClientReloadSave = 85,
    ClientReloadRestore = 86,
    /// <summary>IManagedSaveRestore::SaveWriteBody: IServerGameDLL::Save's one body block, the managed world, once per save. Was ServerSaveCollect (the entity block's retired SaveCollectEntities slot) until plan_server_native_v2 T4c; the value is kept.</summary>
    ServerSaveWriteBody = 87,
    /// <summary>IManagedSaveRestore::SaveReadBody: IServerGameDLL::Restore's body block, once per load. Was ServerSaveRestore (SaveRestoreEntities) until plan_server_native_v2 T4c; the value is kept.</summary>
    ServerSaveReadBody = 88,
    ServerOnRagdollMagnetForce = 89,
    /// <summary>IManagedUiClient::Paint, once per root panel a frame (plan_client_ui U2): the HUD's paint, which was ClientOnHudPaint. CLIENT ONLY.</summary>
    ClientUiPaint = 90,
    ServerWorldKeyValue = 91,
    ClientStage = 92,
    ServerDispatch = 93,

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
    ServerHierarchyParentMoved = 94,
    ServerHierarchyChildMoved = 95,
    ServerHierarchySetParent = 96,
    ServerHierarchyGetParentAttachment = 97,
    ServerHierarchySetParentName = 98,
    ServerHierarchySetParentAttachmentName = 99,
    ServerHierarchyEntityRemoved = 100,
    ServerHierarchyResolveParentNames = 101,
    ServerHierarchyToLocal = 102,
    ServerHierarchyToWorld = 103,
    ServerHierarchySetLocal = 104,

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
    ServerEntityRemoveRequested = 105,
    // plan_entity.md step 3e. ONE crossing per command per player: the whole of
    // ProcessMovement.
    //
    // ONE op for a thunk both sides share, which is what the ten Move* leaves
    // above already do and what CrossingsAuditTests requires -- a thunk counts
    // the op of its own name. "Which side ran it" is answered by
    // cs_sv_move_managed_report and cs_cl_move_managed_report, which count in
    // the NATIVE forwarder and can therefore tell "the managed body ran" from
    // "the managed body was asked for"; tools/movemath-gate reads those.
    ClientMoveProcessMovement = 106,

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
    ServerPlayerRunCommands = 107,
    // plan_entity.md step 6f. ONE crossing per CLIENT FRAME, not one per
    // replayed command: IPrediction::Update forwarded whole, and everything
    // under it -- the ring compare, the basis restore, the movement loop and
    // the weapon machine -- is managed code calling managed code.
    //
    // It REPLACES three crossings per replayed command (the two
    // ClientOnStandalonePlayerCommand phases and MoveProcessMovement's client
    // arm), so the client's per-frame crossing count falls with this step
    // rather than rising. cs_cl_crossings is where that is read.
    ClientPredictionUpdate = 108,
    // The server's physics frame, managed (plan_physics_bones_native_zero 2.3):
    // CPhysicsHook::FrameUpdatePostEntityThink forwarding to PhysicsFrame. ONE
    // crossing per server frame. It took 122 when ServerPhysicsEvents, the
    // event ring's drain, retired with the ring: PhysicsFrame posts the
    // step's events to the router with no crossing at all.
    ServerPhysicsFrame = 109,



    // plan_entity.md step 8e-entfire. NOT per frame: these are entered only
    // when the event queue services an event that names a target, and when
    // `ent_fire` asks whether anything answers to a word before it queues one.
    // A frame with no I/O in it enters neither.
    //
    // They exist because a shell-less entity is in NO native list -- no edict,
    // no index, no C++ object -- so the only index that holds one is this
    // side's, and native code asking for one has to cross to reach it.
    ServerShellLessFindByName = 110,
    ServerShellLessFindByClassname = 111,

    // An impact's dust (FX_DustImpact, dirt and sand), asked once per impact by
    // shell_impact_effects.cpp (lane c5-particles). CLIENT ONLY. It took 125 when
    // ClientImpactEffectParams retired with the native impact emitters: the
    // blocks are built by the managed effects themselves now.
    ClientParticlesImpactDust = 112,

    /// <summary>
    /// A sprite draw asking the managed planner (shell_sprite_leaf.cpp's
    /// SourceSharp_PlanSprite reaching SpriteRenderer): one per sprite drawn.
    /// It took the value of ClientSpriteRenderRules, the once-a-level rule
    /// table it replaced. CLIENT ONLY.
    /// </summary>
    ClientFxSpritePlan = 113,

    // plan_entity.md §3.10, the policy-player lane. CLIENT ONLY, and entered
    // only while `thirdperson` is on -- a developer command, not a mode this mod
    // ships in. So this op reads 0 in every ordinary frame and twice per frame
    // in third person, which is what makes it worth counting separately rather
    // than folding into a client-view op.
    ClientThirdPersonCamera = 114,

    /// <summary>
    /// IBaseClientDLL::InvalidateMdlCache, lane diag-visibility. CLIENT ONLY,
    /// and rare by construction: it is reached from `flush` and `flush_locked`
    /// (both FCVAR_CHEAT) and from the engine's own model reload, so it reads 0
    /// in every ordinary session.
    /// </summary>
    ClientInvalidateMdlCache = 115,

    /// <summary>
    /// Which effect family an impact is, plan_shim.md §6 lane 4b-effects.
    /// CLIENT ONLY. One per impact, asked before the trace, and it is the
    /// ceiling this move has to stay under: a bullet that hits something now
    /// crosses twice (this, then ImpactEffectParams for the family it chose)
    /// where it crossed once.
    /// </summary>
    ClientImpactChoice = 116,

    /// <summary>
    /// One packet of user commands, decoded, planned and queued in C# —
    /// plan_server_novalve.md P2.2. SERVER ONLY. Replaces
    /// the retired <c>ServerPlayerCommands</c> crossing (which was the plan alone,
    /// after a native decode) at one crossing per packet.
    /// </summary>
    ServerPlayerDecodeCommands = 117,

    /// <summary>
    /// A newly precached model's components (sounds, particles), precached in
    /// C# — plan_server_novalve.md P2.4/P3. SERVER ONLY. One per new model.
    /// </summary>
    ServerModelPrecacheComponents = 118,

    /// <summary>
    /// A new user command's random seed (MD5_PseudoRandom of its number),
    /// answered in C#. CLIENT ONLY. One per command the client creates.
    /// </summary>
    ClientMd5PseudoRandom = 119,

    /// <summary>
    /// plan_engineapi_vtables.md step A3: the server's load with its shim
    /// root. Once per side load, like <see cref="LoadSide"/>.
    /// </summary>
    LoadServer = 120,

    /// <summary>
    /// plan_engineapi_vtables E13: the server's native temp-entity forward,
    /// counted by TempEntsBinding since the shared thunk became the client's
    /// (<see cref="ClientNativeTempEntity"/>).
    /// </summary>
    ServerNativeTempEntity = 121,

    /// <summary>
    /// plan_engineapi_vtables E16: the server's net-state layout asks, counted by
    /// NetStateLayoutBinding since the shared thunks became the client's.
    /// </summary>
    ServerNetStateFieldOffset = 122,
    ServerNetStateFieldBits = 123,

    /// <summary>
    /// plan_engineapi_vtables E17: the server's ProcessMovement, counted by
    /// PlayerCommandsBinding since the shared thunk became the client's
    /// (<see cref="ClientMoveProcessMovement"/>).
    /// </summary>
    ServerMoveProcessMovement = 124,

    /// <summary>
    /// The client's three entries from native (plan_side_entries SE1), beside
    /// the server's (LoadServer, ReloadCheckServer, UnloadServer): one per side,
    /// so no side travels as an argument.
    /// </summary>
    LoadClient = 125,
    ReloadCheckClient = 126,
    UnloadClient = 127,

    /// <summary>
    /// Each side's managed build stamp (plan_side_entries SE2, the integrator's
    /// ruling A): the Bootstrap's fourth call into the side's own entry, right
    /// after its Load, once the module's start-up has read its assemblies.
    /// </summary>
    StampedServer = 128,
    StampedClient = 129,

    /// <summary>
    /// The client asking, at each level's LevelInitPreEntity, whether the
    /// particle definitions must be re-read from disk or only re-precached
    /// (plan_map_load P8). CLIENT ONLY, once per level.
    /// </summary>
    ClientParticlesNeedFullReload = 130,

    /// <summary>
    /// A listen server's spawn asking the client which materials to reload in
    /// place of the engine's full ReloadMaterials(NULL) (plan_map_load P1).
    /// CLIENT ONLY, once per spawn.
    /// </summary>
    ClientMapMaterialReload = 131,

    /// <summary>A client's SourceSharp net message (ss_client_message.h) reaching the server.</summary>
    ServerOnClientNetMessage = 132,

    /// <summary>IManagedUiClient::ScreenSizeChanged, on a video-mode change. CLIENT ONLY.</summary>
    ClientUiScreenSizeChanged = 133,

    /// <summary>
    /// The client's decal tables (decals.cpp's CDecalEmitterSystem, lane
    /// c2-sims): its three system steps, once each, and the three questions
    /// impact effects and temp entities ask (shell_decals_client.cpp).
    /// CLIENT ONLY.
    /// </summary>
    ClientDecalsInit = 134,
    ClientDecalsLevelInitPreEntity = 135,
    ClientDecalsShutdown = 136,
    ClientDecalIndexForName = 137,
    ClientDecalNameForIndex = 138,
    ClientDecalTranslate = 139,

    /// <summary>
    /// The old-style particle manager's crossings (C7 particles phase 2.3):
    /// the frame's update of every binding, once a frame; each drawn
    /// executor's draw decisions and its sort keys back. They took the values
    /// of the sims' three per-effect steps (Emit, Simulate, Draw), which are
    /// calls inside managed now. CLIENT ONLY.
    /// </summary>
    ClientParticlesFrame = 140,
    ClientParticlesDrawBegin = 141,
    ClientParticlesDrawEnd = 142,

    /// <summary>
    /// point_worldtext's glyph quads, asked per draw by the native text mesh
    /// renderer (shell_worldtext_render.cpp, lane c2-sims). CLIENT ONLY.
    /// </summary>
    ClientWorldTextDraw = 143,
    /// <summary>
    /// A native caller's effect dispatch (<c>DispatchEffectToCallback</c>)
    /// reaching the managed dispatch table, plan_client_novalve §2 cluster 4.
    /// CLIENT ONLY. One per dispatch a native effect or forward raises.
    /// </summary>
    ClientDispatchEffectIn = 144,

    /// <summary>
    /// The client's IEffects thunk (shell_effects_client.cpp) carrying one call
    /// to ClientEffects' decision, plan_client_novalve §2 cluster 3. CLIENT
    /// ONLY. One per IEffects call a native caller makes.
    /// </summary>
    ClientFxSmoke = 145,
    ClientFxSparks = 146,
    ClientFxDust = 147,
    ClientFxMuzzleFlash = 148,
    ClientFxMetalSparks = 149,
    ClientFxEnergySplash = 150,
    ClientFxRicochet = 151,
    ClientFxSuppressEffectsSounds = 152,

    /// <summary>
    /// The client-side effect list's native entries (shell_effects_list_client.cpp)
    /// reaching ClientEffectList: the view's once-a-frame draw, the client DLL's
    /// flush, the level start, and the FX_Add*/FX_TracerSound calls the fork's
    /// effect TUs make. CLIENT ONLY.
    /// </summary>
    ClientFxDrawEffects = 153,
    ClientFxFlushEffects = 154,
    ClientFxEffectsLevelInit = 155,
    ClientFxAddLine = 156,
    ClientFxAddQuad = 157,
    ClientFxAddStaticLine = 158,
    ClientFxAddDiscreetLine = 159,
    ClientFxTracerSound = 160,
    ClientFxAddQuadNamed = 161,


    /// <summary>
    /// A managed env_spritetrail's renderable reaching ClientSpriteTrails:
    /// once a frame per trail to tick (PreRender), once per draw. CLIENT ONLY.
    /// </summary>
    ClientFxSpriteTrailTick = 162,
    ClientFxSpriteTrailDraw = 163,

    /// <summary>
    /// The client effect precache system's level start and end reaching
    /// ClientEffectPrecache: twice a level. CLIENT ONLY.
    /// </summary>
    ClientFxEffectsPrecache = 164,

    /// <summary>
    /// The smoke fog overlay asking SmokeFogOverlay whether to draw: once a
    /// frame. CLIENT ONLY.
    /// </summary>
    ClientFxSmokeFogPlan = 165,

    /// <summary>IManagedUiClient::Input, one per event the menu root receives (plan_client_ui U3). CLIENT ONLY.</summary>
    ClientUiInput = 166,

    /// <summary>IManagedUiClient::ViewportPanel, a viewport panel shown or hidden (+showscores, VGUIMenu). CLIENT ONLY.</summary>
    ClientUiViewportPanel = 167,

    /// <summary>
    /// The client's sound envelope controller (sound cluster 17, lane c2-sims):
    /// shell_soundenvelope_client.cpp's calls into the shared controller. CLIENT ONLY.
    /// </summary>
    ClientSoundControllerCreate = 168,
    ClientSoundControllerDestroy = 169,
    ClientSoundControllerPlay = 170,
    ClientSoundControllerPlayEnvelope = 171,
    ClientSoundControllerChangePitch = 172,
    ClientSoundControllerChangeVolume = 173,
    ClientSoundControllerGetPitch = 174,
    ClientSoundControllerGetVolume = 175,
    ClientSoundControllerFadeOut = 176,
    ClientSoundControllerCommandAdd = 177,
    ClientSoundControllerCommandClear = 178,
    ClientSoundControllerReset = 179,
    ClientSoundControllerShutdown = 180,

    /// <summary>
    /// The client soundscape system's client-system shell
    /// (shell_soundscape_audio_client.cpp) reaching ClientSoundscapes: a level
    /// init and a level shutdown per level, an Update per frame. CLIENT ONLY.
    /// </summary>
    ClientSoundscapesLevelInit = 181,
    ClientSoundscapesUpdate = 182,
    ClientSoundscapesLevelShutdown = 183,

    /// <summary>
    /// The legacy tempents' thunk (shell_legacy_tempents_client.cpp) reaching
    /// ClientTempEntDecisions: the render choices once or twice per drawn
    /// tempent a frame, the rest per flash, casing, attach, bounce and level.
    /// CLIENT ONLY.
    /// </summary>
    ClientFxTempEntIsTransparent = 184,
    ClientFxTempEntRenderGroup = 185,
    ClientFxTempEntBlend = 186,
    ClientFxTempEntOccludedAlpha = 187,
    ClientFxTempEntLightingOrigin = 188,
    ClientFxTempEntMuzzleFlash = 189,
    ClientFxTempEntEjectBrass = 190,
    ClientFxTempEntAttach = 191,
    ClientFxTempEntsLevelInit = 192,

    /// <summary>
    /// The client weather's render plan (shell_precipitation_client.cpp's
    /// DrawPrecipitation reaching PrecipitationSystem): once a render pass that
    /// draws the weather. CLIENT ONLY.
    /// </summary>
    ClientPrecipitationRender = 193,

    /// <summary>
    /// vphysics' IPhysicsObjectEvent::ObjectWake, on managed's own table
    /// (plan_physics_managed B2.1). Mid-step: queued, routed after.
    /// </summary>
    ServerPhysicsObjectWake = 194,

    /// <summary>IPhysicsObjectEvent::ObjectSleep, the same.</summary>
    ServerPhysicsObjectSleep = 195,


    // vphysics' IPhysicsCollisionEvent on managed's own table (plan_physics_managed
    // B2.2), one op per slot in Valve's order; a slot not yet taken over
    // forwards to native's handler.

    /// <summary>IPhysicsCollisionEvent::PreCollision.</summary>
    ServerPhysicsCollisionPreCollision = 196,

    /// <summary>IPhysicsCollisionEvent::PostCollision.</summary>
    ServerPhysicsCollisionPostCollision = 197,

    /// <summary>IPhysicsCollisionEvent::Friction.</summary>
    ServerPhysicsCollisionFriction = 198,

    /// <summary>IPhysicsCollisionEvent::StartTouch.</summary>
    ServerPhysicsCollisionStartTouch = 199,

    /// <summary>IPhysicsCollisionEvent::EndTouch.</summary>
    ServerPhysicsCollisionEndTouch = 200,

    /// <summary>IPhysicsCollisionEvent::FluidStartTouch.</summary>
    ServerPhysicsCollisionFluidStartTouch = 201,

    /// <summary>IPhysicsCollisionEvent::FluidEndTouch.</summary>
    ServerPhysicsCollisionFluidEndTouch = 202,

    /// <summary>IPhysicsCollisionEvent::PostSimulationFrame.</summary>
    ServerPhysicsCollisionPostSimulationFrame = 203,

    /// <summary>IPhysicsCollisionEvent::ObjectEnterTrigger.</summary>
    ServerPhysicsCollisionObjectEnterTrigger = 204,

    /// <summary>IPhysicsCollisionEvent::ObjectLeaveTrigger.</summary>
    ServerPhysicsCollisionObjectLeaveTrigger = 205,

    /// <summary>IManagedConsoleOutput::ConsoleLine: an engine print handed to managed.</summary>
    ServerConsoleLine = 206,

    /// <summary>IManagedConsoleOutputClient::ClientConsoleLine: the client engine's print.</summary>
    ClientConsoleLine = 207,

    /// <summary>
    /// The material proxies (lane c5-proxies): the trampoline's Init, each
    /// OnBind (per bound material per frame: the hot one), Release, and
    /// CMatStubHandler's per-frame mat_stub question. CLIENT ONLY.
    /// </summary>
    ClientMaterialProxyInit = 208,
    ClientMaterialProxyBind = 209,
    ClientMaterialProxyRelease = 210,
    ClientMaterialProxyMatStub = 211,

    /// <summary>IManagedUiClient::CenterPrint, the engine's ICenterPrint (VCENTERPRINT002) print. CLIENT ONLY.</summary>
    ClientUiCenterPrint = 212,

    /// <summary>IManagedUiClient::CenterClear, the engine's ICenterPrint clear (level change). CLIENT ONLY.</summary>
    ClientUiCenterClear = 213,

    /// <summary>IManagedUiClient::PlaqueState, the engine facts the loading and paused plaques are decided from (on change only). CLIENT ONLY.</summary>
    ClientUiPlaqueState = 214,

    /// <summary>IManagedUiClient::VoiceStatus, the engine's IClientDLL::VoiceStatus (a speaker started or stopped). CLIENT ONLY.</summary>
    ClientUiVoiceStatus = 215,

    /// <summary>IManagedUiClient::KeyInput, a key the engine offers the client mode (stock ClientModeShared::KeyInput). CLIENT ONLY.</summary>
    ClientUiKeyInput = 216,

    /// <summary>IManagedUiClient::VoiceIsBlocked, whether this client muted a player (IGameClientExports). CLIENT ONLY.</summary>
    ClientUiVoiceIsBlocked = 217,

    /// <summary>IManagedUiClient::VoiceToggleBlocked, a player's mute flipped (IGameClientExports). CLIENT ONLY.</summary>
    ClientUiVoiceToggleBlocked = 218,

    /// <summary>IManagedClients::OnClientSettingsChanged: a client's userinfo changed (CTeamplayRules::ClientSettingsChanged's name half).</summary>
    ServerOnClientSettingsChanged = 219,

    /// <summary>IManagedConsoleOutput::ConsoleBufferAttach: the forwarder's line buffer, once per load.</summary>
    ServerConsoleBufferAttach = 220,

    /// <summary>IManagedConsoleOutputClient::ClientConsoleBufferAttach: the client forwarder's line buffer.</summary>
    ClientConsoleBufferAttach = 221,


    // vphysics' IPhysicsCollisionSolver on managed's own table
    // (plan_physics_bones_native_zero 2.1), one op per slot in Valve's order;
    // asked mid-step.

    /// <summary>IPhysicsCollisionSolver::ShouldCollide.</summary>
    ServerPhysicsSolverShouldCollide = 222,

    /// <summary>IPhysicsCollisionSolver::ShouldSolvePenetration.</summary>
    ServerPhysicsSolverShouldSolvePenetration = 223,

    /// <summary>IPhysicsCollisionSolver::ShouldFreezeObject.</summary>
    ServerPhysicsSolverShouldFreezeObject = 224,

    /// <summary>IPhysicsCollisionSolver::AdditionalCollisionChecksThisTick.</summary>
    ServerPhysicsSolverAdditionalCollisionChecksThisTick = 225,

    /// <summary>IPhysicsCollisionSolver::ShouldFreezeContacts.</summary>
    ServerPhysicsSolverShouldFreezeContacts = 226,

    /// <summary>IManagedBones::TestHitboxes: an engine trace against a standalone entity's posed hitboxes.</summary>
    ServerBonesTestHitboxes = 227,

    /// <summary>IManagedClients::ServerTransmitAlwaysForClient, CheckTransmit's per-recipient "always send" edict (stock ShouldTransmit's own-player arm).</summary>
    ServerTransmitAlwaysForClient = 228,
    /// <summary>
    /// The render plan (plan_client_novalve C7): View_Render's per-frame
    /// question (which path draws the frame, and is it recorded), and a
    /// recorded frame's text handed back. CLIENT ONLY.
    /// </summary>
    ClientRenderFrame = 229,
    ClientRenderTraceDone = 230,

    /// <summary>The render plan for one RenderView: the executor hands the inputs, managed writes the ops. CLIENT ONLY.</summary>
    ClientRenderPlanBuild = 231,

    /// <summary>A view's custom-draw entities prepared in one crossing (C7 item 7, shell_standalone_client.cpp's draw batch). CLIENT ONLY.</summary>
    ClientStandaloneDrawBatch = 232,

    /// <summary>
    /// The particle query particles.a calls back (IParticleSystemQuery, its
    /// five live methods), answered by the managed ParticleSystemQuery through
    /// the native thunk (shell_managed_particles.cpp, lane c5-particles).
    /// CLIENT ONLY.
    /// </summary>
    ClientParticleQueryCollisionGroup = 233,
    ClientParticleQueryLighting = 234,
    ClientParticleQueryTrace = 235,
    ClientParticleQueryHitBoxPoints = 236,
    ClientParticleQueryHitBoxInfo = 237,
    /// <summary>An impact's debris (FX_DebrisFlecks: the flecks and the dust behind them), asked once per impact by shell_impact_effects.cpp (lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesImpactDebris = 238,

    /// <summary>An impact's metal spark, asked once per metal impact by shell_impact_effects.cpp (lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesImpactMetalSpark = 239,

    /// <summary>A splash or ripple fx_water drew, asked by the native code that still calls it (physics.cpp's splashes, the rain's ripple; lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesWaterSplash = 240,

    /// <summary><c>UTIL_GetNormalizedColorTintAndLuminosity</c>, asked by physics.cpp's splash lighting (lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesSplashTint = 241,

    /// <summary>A frame's rain tracers' corners, one crossing per precipitation render (shell_precipitation_client.cpp's DrawPrecipitation; lane c5-particles). CLIENT ONLY.</summary>
    ClientParticlesTracerQuads = 242,
    /// <summary>A world view's draws over its lists, asked once a view with its scene (C7 step 2, shell_view_executor_client.cpp's ViewScene op). CLIENT ONLY.</summary>
    ClientRenderViewProgram = 243,
    /// <summary>viewpostprocess.cpp's passes as a program of ops, asked at the plan's EnginePostProcessing and MotionBlur ops (C7 post-processing). CLIENT ONLY.</summary>
    ClientRenderPostProgram = 244,

    /// <summary>
    /// The client's input (cluster 13, lane c6-input): the native IInput thunk
    /// set's calls into ClientInput, one per IManagedInputClient slot. CLIENT ONLY.
    /// </summary>
    ClientInputInit = 245,
    ClientInputShutdown = 246,
    ClientInputLevelInit = 247,
    ClientInputCreateMove = 248,
    ClientInputExtraMouseSample = 249,
    ClientInputGetButtonBits = 250,
    ClientInputMakeWeaponSelection = 251,
    ClientInputKeyState = 252,
    ClientInputKeyEvent = 253,
    ClientInputIsKeyDown = 254,
    ClientInputJoystickAdvanced = 255,
    ClientInputAccumulateMouse = 256,
    ClientInputActivateMouse = 257,
    ClientInputDeactivateMouse = 258,
    ClientInputClearStates = 259,
    ClientInputGetLookSpring = 260,
    ClientInputGetFullscreenMousePos = 261,
    ClientInputSetFullscreenMousePos = 262,
    ClientInputResetMouse = 263,
    ClientInputGetLastForwardMove = 264,
    ClientInputJoystickAxes = 265,
    ClientInputClearInputButton = 266,
    ClientInputCamThink = 267,
    ClientInputCamOp = 268,
    ClientInputCamOrthographicSize = 269,
    ClientInputCamSetCameraThirdData = 270,
    ClientInputSetPreferredGameActionSet = 271,
    ClientInputGetPreferredGameActionSet = 272,
    ClientInputSetGameActionSetFlags = 273,
    ClientInputEnableJoystickMode = 274,
    ClientInputIsSteamControllerActive = 275,
    ClientInputThirdPersonInit = 276,
    ClientInputRumbleScreenShake = 277,
    ClientInputRumbleStopAll = 278,

    /// <summary>
    /// The client's IPhysicsCollisionEvent on managed's own table (plan_client_novalve
    /// C6, cluster 9; lane c6-input): one op per slot in Valve's order; a slot not yet
    /// taken over forwards to native's g_Collisions. CLIENT ONLY.
    /// </summary>
    ClientPhysicsPreCollision = 279,
    ClientPhysicsPostCollision = 280,
    ClientPhysicsFriction = 281,
    ClientPhysicsStartTouch = 282,
    ClientPhysicsEndTouch = 283,
    ClientPhysicsFluidStartTouch = 284,
    ClientPhysicsFluidEndTouch = 285,
    ClientPhysicsPostSimulationFrame = 286,
    ClientPhysicsObjectEnterTrigger = 287,
    ClientPhysicsObjectLeaveTrigger = 288,

    /// <summary>The client's IPhysicsCollisionSolver on managed's own table (lane c6-input), one op per slot. CLIENT ONLY.</summary>
    ClientPhysicsShouldCollide = 289,
    ClientPhysicsShouldSolvePenetration = 290,
    ClientPhysicsShouldFreezeObject = 291,
    ClientPhysicsAdditionalCollisionChecks = 292,
    ClientPhysicsShouldFreezeContacts = 293,

    // vphysics' IPhysicsConstraintEvent and IMotionEvent on managed's own
    // tables (plan_physics_bones_native_zero 2.2/2.4). Simulate is asked
    // mid-step, once per object a controller acts on. SERVER.

    /// <summary>IPhysicsConstraintEvent::ConstraintBroken.</summary>
    ServerPhysicsConstraintBroken = 294,

    /// <summary>IMotionEvent::Simulate.</summary>
    ServerPhysicsMotionSimulate = 295,

    /// <summary>IManagedBonesClient (plan physics_bones_native_zero 1.1-1.2, client half): the engine's skeleton asks of a standalone entity. CLIENT ONLY.</summary>
    ClientBonesSetupBones = 296,
    ClientBonesTestHitboxes = 297,
    ClientBonesGetAttachment = 298,
    ClientBonesLookupAttachment = 299,

    /// <summary>IManagedBonesClient::PoseModel: a legacy temp entity's model posed at sequence 0 (no entity behind it). CLIENT ONLY.</summary>
    ClientBonesPoseModel = 300,

    /// <summary>IManagedPhysicsClient: the client physics system's level events (pre/post entity init), forwarded by native's CPhysicsSystem row. They took IManagedRagdollsClient's 313/314 when the step called the ragdolls itself (the client physics leftovers, 2026-10-18). CLIENT ONLY.</summary>
    ClientPhysicsLevelInitPreEntity = 301,
    ClientPhysicsLevelInitPostEntity = 302,

    /// <summary>IVPhysicsKeyHandler on managed's custom-block table: one key of a ragdoll's .phy block (collision rules, animated friction), and its defaults, asked from inside vphysics' parse. CLIENT ONLY.</summary>
    ClientRagdollKeyValue = 303,
    ClientRagdollKeyDefaults = 304,

    /// <summary>IVPhysicsKeyHandler on managed's solid-defaults table (stock's g_SolidSetup): a solid's unparsed key, and its defaults. CLIENT ONLY.</summary>
    ClientRagdollSolidKeyValue = 305,
    ClientRagdollSolidDefaults = 306,

    /// <summary>IManagedPhysicsFrame (plan physics_bones_native_zero 2.6): the level's world physics built and the surface sounds precached, each asked by native's CPhysicsHook. SERVER ONLY.</summary>
    ServerPhysicsCreateWorld = 307,
    ServerPhysicsPrecacheSurfaceSounds = 308,
    // 322 was ServerPhysicsParseModelSolid until native's InitPhysics retired
    // (2026-10-12), then ClientSolidDefaults until g_SolidSetup left with fork
    // physics.cpp (2026-10-18); ClientPhysicsLevelShutdownPreEntity has it now.

    /// <summary>IVPhysicsKeyHandler on the server's solid-defaults table (stock's g_SolidSetup): a solid's unparsed key, and its defaults, asked from inside vphysics' parse. SERVER ONLY.</summary>
    ServerSolidKeyValue = 309,
    ServerSolidDefaults = 310,

    /// <summary>IManagedPhysicsClient (plan physics_bones_native_zero 2.6): the surface property manifest parsed, asked by native's PhysicsDLLInit. CLIENT ONLY.</summary>
    ClientPhysicsParseSurfaceData = 311,

    /// <summary>IManagedPhysicsClient: the level's shutdown events, the step and the reset (the client physics leftovers, 2026-10-18), in the numbers CreateWorld, PrecacheSurfaceSounds and g_SolidSetup's two slots left: the world, the precache and the static props' key table are ClientPhysicsSystem's own now. CLIENT ONLY.</summary>
    ClientPhysicsLevelShutdownPreEntity = 312,
    ClientPhysicsLevelShutdownPostEntity = 313,
    ClientPhysicsSimulate = 314,
    ClientPhysicsReset = 315,
    /// <summary>A fade from native (the engine's View_Fade, the world's start-dark fade) into the managed view effects (C7). CLIENT ONLY.</summary>
    ClientViewEffectsFade = 316,
    /// <summary>The view's CalcShake, once a frame: the managed view effects' offset and roll (C7). CLIENT ONLY.</summary>
    ClientViewEffectsCalcShake = 317,
    /// <summary>The renderer's GetFadeParams, once a frame (C7). CLIENT ONLY.</summary>
    ClientViewEffectsFadeParams = 318,
    /// <summary>The level's view-effects reset and the clears (C7). CLIENT ONLY.</summary>
    ClientViewEffectsReset = 319,

    /// <summary>IManagedBonesClient::BoneBatch (plan_physics_bones_perf A2): once a frame, at its first SetupBones ask, the frame's skeletons posed ahead and the table native answers the rest of the frame's asks from. CLIENT ONLY.</summary>
    ClientBonesBatch = 320,
    /// <summary>A shadow-depth view's plan, asked by the shadow manager inside the frame's ShadowComputeDepthTextures op (C7 step 3). CLIENT ONLY.</summary>
    ClientRenderShadowDepthPlan = 321,
    /// <summary>CViewRender::SetUpViews' planes and FOV, once a frame before the client mode's override (C7 step 3). CLIENT ONLY.</summary>
    ClientViewSetUp = 322,
    /// <summary>IManagedInputClient::InputEncodeUserCmd.</summary>
    ClientInputEncodeUserCmd = 323,
    /// <summary>IManagedInputClient::InputDecodeUserCmd.</summary>
    ClientInputDecodeUserCmd = 324,
    /// <summary>IManagedParticlesClient::ManifestFiles: a particle manifest's file list, read in C# (C9 KeyValues K1). CLIENT ONLY.</summary>
    ClientParticlesManifestFiles = 325,
    /// <summary>The save-game screenshot's view (CViewRender::WriteSaveGameScreenshotOfSize), once a screenshot (C7 step 3). CLIENT ONLY.</summary>
    ClientViewScreenshot = 326,
    /// <summary>CViewRender::FreezeFrame into the managed freeze state (test_freezeframe; C7 step 3). CLIENT ONLY.</summary>
    ClientViewFreezeFrame = 327,
    /// <summary>IManagedMaterialFilesClient::SpriteMaterial: a sprite model's material name and .vmt, read in C# (C9 KeyValues K4). CLIENT ONLY.</summary>
    ClientMaterialFilesSpriteMaterial = 328,
    /// <summary>The demo free camera (view.cpp's CalcDemoViewOverride), twice a frame while a demo plays (C7 step 3). CLIENT ONLY.</summary>
    ClientViewDemoCamera = 329,
    /// <summary>The old-style particle manager's frame code (CParticleMgr::IncrementFrameCode), once per scene view (C7 particles phase 2.3). CLIENT ONLY.</summary>
    ClientParticlesFrameCode = 330,
    /// <summary>The old-style particle manager's PostRender, once a frame (C7 particles phase 2.3). CLIENT ONLY.</summary>
    ClientParticlesPostRender = 331,

    /// <summary>IManagedMaterialFilesClient::EncodeImage: a screenshot's pixels encoded as its TGA or VTF file in C# (C9: bitmap.a and vtf.a off client.so). CLIENT ONLY.</summary>
    ClientMaterialFilesEncodeImage = 332,

    /// <summary>IManagedDetailPropsClient::DetailInit: the detail props' Init, once (C7 cluster 10). CLIENT ONLY.</summary>
    ClientDetailInit = 333,
    /// <summary>IManagedDetailPropsClient::DetailLevelInitPreEntity: the detail lump parsed in C#, once a level. CLIENT ONLY.</summary>
    ClientDetailLevelInitPreEntity = 334,
    /// <summary>IManagedDetailPropsClient::DetailLevelInitPostEntity: the fades restored and the sprite material named, once a level. CLIENT ONLY.</summary>
    ClientDetailLevelInitPostEntity = 335,
    /// <summary>IManagedDetailPropsClient::DetailLevelShutdownPreEntity, once a level. CLIENT ONLY.</summary>
    ClientDetailLevelShutdownPreEntity = 336,
    /// <summary>IManagedDetailPropsClient::DetailBuildLists: a view's fade and model states, once per view that builds entities. CLIENT ONLY.</summary>
    ClientDetailBuildLists = 337,
    /// <summary>IManagedDetailPropsClient::DetailBeginTranslucent, once per translucent pass. CLIENT ONLY.</summary>
    ClientDetailBeginTranslucent = 338,
    /// <summary>IManagedDetailPropsClient::DetailPrepare: one of a translucent draw's halves, up to its render context. CLIENT ONLY.</summary>
    ClientDetailPrepare = 339,
    /// <summary>IManagedDetailPropsClient::DetailEmit: a prepared draw's quads. CLIENT ONLY.</summary>
    ClientDetailEmit = 340,
    /// <summary>The new-style particle effects' frame (CParticleMgr::UpdateNewEffects), once a frame; the library steps the listed collections during it (C7 particles phase 3). CLIENT ONLY.</summary>
    ClientParticlesNewEffectsFrame = 341,
    /// <summary>One new-style effect's draw decisions (CNewParticleEffect::DrawModel), per drawn effect per view (C7 particles phase 3). CLIENT ONLY.</summary>
    ClientParticlesNewEffectDraw = 342,
    /// <summary>IManagedSoundEmitter::ServerMoveSound: the movement's IMoveHelper::StartSound with its parameters, to the managed SoundEmitter (plan_server_native_v2 T4a).</summary>
    ServerMoveSound = 343,
    /// <summary>IManagedSoundEmitter::ServerMoveSoundScript: the movement's IMoveHelper::StartSound by script name (plan_server_native_v2 T4a).</summary>
    ServerMoveSoundScript = 344,
    /// <summary>IManagedRenderClient::RenderLeafList: one view's render list from its leaf-system buffer, once per view that draws entities (C7 cluster 12 L1). CLIENT ONLY.</summary>
    ClientRenderLeafList = 345,
    /// <summary>IManagedRenderClient::RenderLeafViewModels: the view models' opaque/translucent split, once per CollateViewModelRenderables (C7 cluster 12 L1). CLIENT ONLY.</summary>
    ClientRenderLeafViewModels = 346,
    /// <summary>IManagedSoundEmitter::ServerSoundSystemInit: the SourceSharpManaged row's Init runs the managed SoundEmitterSystem's (ModInit) first (plan_server_native_v2 T4a, the precache path).</summary>
    ServerSoundSystemInit = 347,
    /// <summary>IManagedSoundEmitter::ServerPrecacheScriptSound: the world's and EffectsPrecache's script precaches, to the managed SoundEmitterSystem (plan_server_native_v2 T4a, the precache path).</summary>
    ServerPrecacheScriptSound = 348,
    /// <summary>IManagedSaveRestore::SaveInit: the save block allocated and placed by the managed SaveGameSession, once per save (plan_server_native_v2 T4c).</summary>
    ServerSaveInit = 349,
    /// <summary>IManagedSaveRestore::SaveWriteFields: one of the engine's own structs into the save through the managed codec, a few dozen per save (plan_server_native_v2 T4c).</summary>
    ServerSaveWriteFields = 350,
    /// <summary>IManagedSaveRestore::SaveReadFields: one of the engine's own structs read back, a few dozen per load (plan_server_native_v2 T4c).</summary>
    ServerSaveReadFields = 351,
    /// <summary>IManagedLeafSystemClient::LevelInitPreEntity: the level's leaves made, at LevelInitPreEntity (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafLevelInitPreEntity = 352,
    /// <summary>IManagedLeafSystemClient::LevelShutdownPostEntity: the registry purged and the sub-systems' data handed back to delete, at LevelShutdownPostEntity (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafLevelShutdownPostEntity = 353,
    /// <summary>IManagedLeafSystemClient::PreRender: the dirty renderables re-inserted into their leaves, once a frame (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafPreRender = 354,
    /// <summary>IManagedLeafSystemClient::TakeOps: the rest of an answer's operations, when they filled the forwarder's buffer (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafTakeOps = 355,
    /// <summary>IManagedLeafSystemClient::CreateRenderableHandle: a renderable registered by handle (the engine's static props; particles) (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafCreateRenderableHandle = 356,
    /// <summary>IManagedLeafSystemClient::AddRenderable: a renderable registered in a group, inserted at the next PreRender (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafAddRenderable = 357,
    /// <summary>IManagedLeafSystemClient::RemoveRenderable: a renderable removed (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafRemoveRenderable = 358,
    /// <summary>IManagedLeafSystemClient::AddRenderableToLeaves: the engine's static prop into the leaves it computed (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafAddRenderableToLeaves = 359,
    /// <summary>IManagedLeafSystemClient::ChangeRenderableRenderGroup: the engine's static-prop fade between opaque and translucent (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafChangeRenderableRenderGroup = 360,
    /// <summary>IManagedLeafSystemClient::SetRenderGroup: a renderable's render group set (a temp entity's blend sets one) (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafSetRenderGroup = 361,
    /// <summary>IManagedLeafSystemClient::RenderableChanged: a renderable moved or changed bounds: dirty until PreRender (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafRenderableChanged = 362,
    /// <summary>IManagedLeafSystemClient::EnableAlternateSorting: a translucent renderable's alternate sorting set (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafEnableAlternateSorting = 363,
    /// <summary>IManagedLeafSystemClient::DrawStaticProps: the static props' switch (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafDrawStaticProps = 364,
    /// <summary>IManagedLeafSystemClient::DrawSmallEntities: the small entities' switch (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafDrawSmallEntities = 365,
    /// <summary>IManagedLeafSystemClient::GetRenderableLeaves: a renderable's leaves (IsRenderableInPVS, GetRenderableLeaves) (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafGetRenderableLeaves = 366,
    /// <summary>IManagedLeafSystemClient::GetRenderableLeaf: one step of a walk over a renderable's leaves (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafGetRenderableLeaf = 367,
    /// <summary>IManagedLeafSystemClient::SetSubSystemDataInLeaf: a leaf's sub-system data set (the detail props' fast-sprite marker) (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafSetSubSystemDataInLeaf = 368,
    /// <summary>IManagedLeafSystemClient::GetSubSystemDataInLeaf: a leaf's sub-system data asked (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafGetSubSystemDataInLeaf = 369,
    /// <summary>IManagedLeafSystemClient::SetDetailObjectsInLeaf: a leaf's detail objects set, at level load (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafSetDetailObjectsInLeaf = 370,
    /// <summary>IManagedLeafSystemClient::GetDetailObjectsInLeaf: a leaf's detail objects asked (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafGetDetailObjectsInLeaf = 371,
    /// <summary>IManagedLeafSystemClient::DrawDetailObjectsInLeaf: a leaf in the detail props' sphere marked, per leaf per detail build (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafDrawDetailObjectsInLeaf = 372,
    /// <summary>IManagedLeafSystemClient::ShouldDrawDetailObjectsInLeaf: decision 3 for one leaf (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafShouldDrawDetailObjectsInLeaf = 373,
    /// <summary>IManagedLeafSystemClient::DetailLeaves: decision 3 for a view's leaves, once per view scene (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafDetailLeaves = 374,
    /// <summary>IManagedLeafSystemClient::ComputeTranslucentRenderLeaf: a view's blends and translucent leaves, once per view with a translucent-leaf pass (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafComputeTranslucentRenderLeaf = 375,
    /// <summary>IManagedLeafSystemClient::BuildRenderablesList: a view's render list, once per view that draws entities (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafBuildRenderablesList = 376,
    /// <summary>IManagedLeafSystemClient::CollateViewModelRenderables: the view models' split, once per view-model draw (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafCollateViewModelRenderables = 377,
    /// <summary>IManagedLeafSystemClient::AddShadow: a shadow or flashlight registered (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafAddShadow = 378,
    /// <summary>IManagedLeafSystemClient::RemoveShadow: a shadow or flashlight removed (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafRemoveShadow = 379,
    /// <summary>IManagedLeafSystemClient::ProjectShadow: a shadow projected into leaves, per shadow update (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafProjectShadow = 380,
    /// <summary>IManagedLeafSystemClient::ProjectFlashlight: a flashlight projected into leaves, per flashlight update (C7 cluster 12 L2). CLIENT ONLY.</summary>
    ClientLeafProjectFlashlight = 381,
    ClientLeafEnumerateShadowsInLeaves = 382,
    /// <summary>IManagedLeafSystemClient::Pending: the RenderableChanged and ChangeRenderableRenderGroup calls the forwarder queued, in order, before the next call or at a blend batch's end (C7 cluster 12 L3). CLIENT ONLY.</summary>
    ClientLeafPending = 383,
    /// <summary>IManagedSoundEmitterClient::ClientSoundSystemInit: the CSoundEmitterSystem row's Init, the client's script table loaded by the managed ClientSoundEmitterSystem (ModInit). CLIENT ONLY.</summary>
    ClientSoundSystemInit = 384,
    /// <summary>IManagedSoundEmitterClient::ClientSoundSystemShutdown: the row's Shutdown (ModShutdown). CLIENT ONLY.</summary>
    ClientSoundSystemShutdown = 385,
    /// <summary>IManagedSoundEmitterClient::ClientSoundLevelInitPreEntity: the row's LevelInitPreEntity, the level's own override script. CLIENT ONLY.</summary>
    ClientSoundLevelInitPreEntity = 386,
    /// <summary>IManagedSoundEmitterClient::ClientSoundLevelShutdownPostEntity: the row's LevelShutdownPostEntity, the overrides cleared. CLIENT ONLY.</summary>
    ClientSoundLevelShutdownPostEntity = 387,
    /// <summary>IManagedSoundEmitterClient::ClientSoundEmit: a native caller's GameSound_Emit (an impact, a sound patch, a broadcast), to the managed ClientSoundEmitter. CLIENT ONLY.</summary>
    ClientSoundEmit = 388,
    /// <summary>IManagedSoundEmitterClient::ClientSoundStop: a native caller's GameSound_Stop (a sound patch). CLIENT ONLY.</summary>
    ClientSoundStop = 389,
    /// <summary>IManagedSoundEmitterClient::ClientSoundEmitCloseCaption: a native caller's GameSound_EmitCloseCaption (a sound patch's caption). CLIENT ONLY.</summary>
    ClientSoundEmitCloseCaption = 390,
    /// <summary>IManagedSaveRestoreClient::SaveInit: the client state file's block allocated and placed by the shared SaveBlockSession, once per save (the client save codec). CLIENT ONLY.</summary>
    ClientSaveInit = 391,
    /// <summary>IManagedSaveRestoreClient::SaveWriteFields: one DECALLIST or MUSICLIST struct into the client state file through the managed codec, one per decal and song per save (the client save codec). CLIENT ONLY.</summary>
    ClientSaveWriteFields = 392,
    /// <summary>IManagedSaveRestoreClient::SaveReadFields: one DECALLIST or MUSICLIST struct read back, one per decal and song per load (the client save codec). CLIENT ONLY.</summary>
    ClientSaveReadFields = 393,
    /// <summary>IManagedClients::ServerAreaPortalVisibility: ClientSetupVisibility's area portal walk, once per client per frame, over the managed AreaPortals (plan_server_native_v2 T4d).</summary>
    ServerAreaPortalVisibility = 394,
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
    /// <see cref="CrossingOp.ServerFrame"/> and not
    /// <see cref="CrossingOp.ServerGameFrame"/>: both run once a frame, but
    /// ServerFrame is the managed module's own frame hook and exists whether or
    /// not the shell game DLL is wired, so it is the one that is there in every
    /// configuration a measurement might be taken in.
    /// </para>
    /// </summary>
    public long ServerFrames => Read(CrossingOp.ServerFrame);

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
