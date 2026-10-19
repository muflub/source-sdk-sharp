using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables Phase A0: which side serves each former engine API
/// slot.
///
/// Since plan_client_vtables CC-last neither side has a struct: each serves a
/// slot as a method of its own shim areas. The SERVER's are ishim_areas.h,
/// ishim_console.h and the root's AcquireInterface; the CLIENT's are
/// ishim_areas_client.h, the shared bases in ishim_areas.h, its console and the
/// root. A slot in both is BOTH; one only in the server's is SERVER; one only
/// in the client's is CLIENT. Neither source is the slot table, so
/// <see cref="EngineApiAreasTests"/> can check the table against them.
///
/// The counts are pinned so that a slot moved or added without being ruled
/// changes a number here.
/// </summary>
public partial class EngineApiCensusTests
{
    // At cdbdd90 (ABI 252), and unchanged by C6c (ABI 253), which moved where
    // the server's 262 are declared and not how many there are. 260 when the
    // two command-line slots were retired (the server reads ILocalCommandLine).
    // 261 when plan_server_novalve.md P5 appended TemplateSpawnKeyed (the
    // template text slot stays, refusing: append-only). 260 and 54 when lane
    // side-audit (2026-10-02) moved RegisterCollisionRules into the shared
    // IShimCollision base: the client's collision matrix had never arrived.
    // 259 and 55 when lane side-audit-2 (2026-10-02) moved EntityIsValid into
    // the shared IShimEntities base with a client body: a client restore had
    // dropped every saved handle.
    // 266 when physics stock parity's six server slots merged in (A5
    // EntityAddPhysicsGameFlags; C3/L EntityPhysicsImpactSound and BreakSound;
    // D2 SoundScriptParameters, SoundPatchCreate and SurfaceScrapeSound).
    // 265 at the merge of the two (2026-10-02): main's 266 less EntityIsValid,
    // which side-audit-2 made dual. Measured.
    // plan_side_isolation P2 (2026-10-02): SendUserMessage left for
    // IShimUserMessages' UserMessageSend (265 stays), RegisterUserMessage went
    // with the native registry (55 -> 54), and the client's
    // IShimUserMessagesClient added UserMessageRead and UserMessageRunHook
    // (137 -> 139). Measured.
    // User-message payload model (2026-10-04): IShimEffectsServer's ScreenShake
    // and ScreenFade went, unreached since C#'s ScreenEffects sends both
    // payloads (265 -> 263). Measured.
    // Physics parity B7 appended EntityTraceHull (main: 265 -> 266); at the
    // landing merge (2026-10-04) that is 263 -> 264. Measured.
    // The dev API's IShimDiagnostics (debug-api lane, 2026-10-04): the
    // player-shadow counters and the exit recorders' arm and read (264 -> 267).
    // Measured.
    // plan_physics_managed A9 (2026-10-04): EntityPairCollisionsDisabled (stock's
    // PhysEntityCollisionsAreDisabled, for the entity solver) and
    // EntityPhysicsObject (the IPhysicsObject managed calls through directly)
    // -> 269 on main 7dd48e61. Measured. A30: IShimDamage.ApplyDamageForce
    // (damage carrying stock's bullet / melee force and position) -> 270.
    // Measured.
    // Lane c2-sims (plan_client_novalve C2): FilesGameFileSize and
    // FilesReadGameFile moved from IShimEngineServer to the shared IShimEngine,
    // the client's decal table and soundscapes read their scripts through
    // them (264 -> 262; both 54 -> 56). Measured.
    // (Rebased onto lane c2-beams: 264 -2 = 262.)
    // Rebased onto main ace0f66a: main's 267 - 2 = 265.
    // Sound cluster 17 (lane c2-sims): the four SoundPatch slots became dual
    // (IShimEngine), the client\'s shim owning its own patches (262 -> 258;
    // both 56 -> 60). Measured.
    // Rebased onto main ace0f66a: 265 -4 = 261.
    // Rebased onto main ace0f66a: 270 -6 = 264.
    // Lane player-msgs: IShimEventsServer's seven fire slots (EventCreate,
    // EventSet{Int,Float,Bool,String}, EventFire, EventFree), server-only
    // (258 -> 265). Measured.
    // Rebased onto main ace0f66a: 261 +7 = 268.
    // Rebased onto main ace0f66a: 264 +7 = 271.
    // plan_physics_managed B2.1 (2026-10-05): PhysicsEnvironmentPointer, the
    // environment managed installs its vphysics callback tables on -> 272.
    // Measured.
    // B2.2 (2026-10-05): PhysicsNativeHandler, native's handlers for managed's
    // tables to forward unported slots to (transitional) -> 273. Measured.
    // B2.2's fluid slots: PhysicsCollideExtent (CollideGetExtent, a by-value
    // Vector return managed cannot call through the vtable) -> 274. Measured.
    // PostCollision's impact tail: PhysicsInertia (GetInertia, the same
    // by-value Vector refusal) -> 275. Measured.
    // The SIGTERM-exits-139 fix (2026-10-06): ExitWatchDisarm, the shutdown
    // half of the exit recorders -> 276. Measured.
    // The router retirement's first cut (2026-10-07): TemplateSpawnInstance,
    // the refused map-text parser P5 retired, generated-but-never-called -> 275.
    // Measured (the typed-call walk in ShimAreaTypedCallsTests found it).
    // The Geiger PVS gate (2026-10-09): PlayerBoxInPvs, the generic "is this
    // box in this player's PVS" read, server-only -> 276. Measured.
    // plan_physics_bones_native_zero 2.1 (2026-10-11): PhysicsWorldObject, the
    // world's physics object, known by identity to managed's solver -> 277.
    // And PhysicsDrainEvents, the event ring's drain called out of managed's
    // PostSimulationFrame (native's CCollisionEvent handler retires) -> 278.
    // And PhysicsNativeHandler goes with the last native handler managed's
    // tables forwarded to (CCollisionEvent deleted) -> 277.
    // And EntitySetPairCollisions / EntityPairCollisionsDisabled: the disabled
    // pairs are managed's (EntityPairCollisions), native's hash unwritten -> 275.
    // C6 cluster 9 (lane c6-input): SoundScriptParameters, SoundPatchCreate,
    // SurfaceScrapeSound and DustEffect gained client bodies (276 - 4 = 272; on main's 275 after physics 2.1, 271).
    // plan_physics_bones_native_zero 2.4: IShimConstraints (6), IShimSprings
    // (7) and IShimMotionControllers (13) left; the three tables are managed's
    // own, calling vphysics through its vtables, and EntitySetSolidToWorld
    // (func_physbox's notsolid goes through EntitySetNotSolid): 271 - 27 = 244.
    // Measured.
    // plan_physics_bones_native_zero 2.3: EntityPhysicsImpactSound and
    // EntityPhysicsBreakSound left (the queued physics sounds are managed's
    // PhysicsSoundBatch), EmitWorldSoundScript came for its emit: 244 - 2 + 1
    // = 243. Measured.
    // plan_physics_bones_native_zero 2.5: PlayerInitVCollision, PlayerShadowProbe
    // and PlayerShadowApply left (the player shadow is managed's
    // PlayerShadowBodies); PlayerShadowFacts, PlayerShadowAdopt,
    // PlayerShadowRelease and PlayerShadowCommit came: 243 - 3 + 4 = 244. Measured.
    // plan_physics_bones_native_zero 2.3: PhysicsDrainEvents left with the
    // event ring (PhysFrame is managed's PhysicsFrame), PhysicsUpdateOwners
    // came for the owners' transform update: 244 - 1 + 1 = 244. Measured.
    // DustEffect went back to the server alone: the client's scrape throws its dust
    // through its own managed IEffects (244 + 1 = 245 on main's physics 2.4).
    // The entity physics init (2026-10-12, 0b8787ad5): EntitySwapPhysicsObject
    // came, the shell holding the object managed made: 245 + 1 = 246. Measured.
    // EntityInitPhysics and EntitySetPhysicsStatic left (managed's
    // EntityPhysicsCreation answers both): 246 - 2 = 244. Measured.
    // EntityConvertToPhysics left (phys_convert is PhysicsConversion's,
    // stock's port): 244 - 1 = 243. Measured.
    // PhysicsCollideExtent and PhysicsInertia left: a Vector by-value return
    // is spelled, so managed calls both through the vtables: 243 - 2 = 241.
    // Measured.
    // The last thin vphysics forwards left (EntityPhysicsMotion makes each
    // call through PhysicsObjectVtbl): EntityGetMass, EntitySetMass,
    // EntityEnableMotion, EntityPhysicsWake, EntityPhysicsSleep,
    // EntityApplyVelocityImpulse, EntityApplyForceCenter, EntityApplyForceOffset,
    // EntityApplyTorque, EntityAddPhysicsGameFlags, EntityBecomePhysicsTrigger:
    // 241 - 11 = 230. Measured.
    // tf2 stage 0.5 (2026-10-11): PlayerSetTeam, the generic "put this
    // player on this team" store the managed ITeams hands down, server-only
    // -> 231 (277 at the lane's base; re-counted on the rebase onto main
    // 7532a33ff). Measured.
    // plan_server_native_v2 T2 (2026-10-18): EntityDissolve, a refusal no
    // managed code called, deleted with its slot -> 230. Measured.
    // Then eleven player/inventory slots whose bodies were constants; the
    // constants are EngineBinding's now -> 219. Measured.
    // Then sixteen movement and entity slots likewise -> 203. Measured.
    // Then EntityIgnite, ShootGib, the three FireEffect refusals (no caller)
    // and TemplateCapture (a constant, EngineBinding's now) -> 197. Measured.
    // Then nine more: FindBaseEntityByName, PurgeMapRefs, CleanupDeleteList,
    // the three retired movers, the two SDK-player steps and the dead
    // PlayerSetSoundscape -> 188. Measured.
    // Then the fifteen no-op weapon slots (IShimWeaponsServer, which left
    // with them; the server binds IShimWeapons itself) -> 173. Measured.
    // plan_server_native_v2 T2: IShimDamage's RadiusDamage and ExplosionDamage,
    // no managed caller (173 - 2 = 171).
    // plan_server_native_v2 T2: BloodDecal, the decal managed BloodDecals
    // paints now (171 - 1 = 170).
    // plan_server_native_v2 T3: EventCreate, EventFire and EventFree folded
    // through IGameEventManager2's forwarders (170 - 3 = 167).
    // plan_server_native_v2 T3: StringTableCreate and StringTableAddString
    // folded (ForwardedStringTables; 167 - 2 = 165).
    // plan_server_native_v2 T3: five IVEngineServer one-call slots folded
    // (GetPlayerUserId, ExecuteServerCommand, PlayerSetViewEntity,
    // PlayerBoxInPvs, EngineLightStyle; 165 - 5 = 160).
    // 2026-10-18 (the skeleton slots): EntitySetSequence and
    // EntityLookupAttachmentAny retire, ServerSkeletons answering both
    // (160 - 2 = 158).
    // 2026-10-19: SurfacePropIndex folded onto IPhysicsSurfaceProps (158 - 1 = 157).
    // 2026-10-19: SparkEffect, MuzzleFlashEffect and DustEffect, never reached
    // past EffectsOverTempEnts, deleted (157 - 3 = 154).
    // 2026-10-19: PrecacheMaterial and PrecacheParticleSystem folded onto the
    // string tables' forwarders (154 - 2 = 152).
    // 2026-10-19: DrawWorldText folded onto IVDebugOverlay (152 - 1 = 151).
    // plan_server_native_v2 T4a: IShimSound's five emits retired, the managed
    // SoundEmitter answering them (151 - 5 = 146).
    // plan_server_native_v2 T4a, the precache path: IShimPrecache::PrecacheSound
    // retired, the managed SoundEmitterSystem answering it (146 - 1 = 145).
    // plan_server_native_v2 T4b: TempEntPlayback and EffectDispatchIndex
    // retired (the managed ServerTempEntEngine answering them through the
    // engine and string table forwarders), TempEntBuild appended (145 - 2 + 1 = 144).
    private const int ServerOnly = 144;
    // User-message payload model (2026-10-04): IShimUserMessagesClient gains the
    // seven slots the ported stock hooks carry their effects out through
    // (139 -> 146). Measured.
    // Lane HC (2026-10-04): UserMessageRunHook went with the native hook route
    // (146 -> 145). Measured.
    // IShimNetClient appended SendServerMessage (a client's net message to its
    // server, steam-auth-game; main: 137 -> 138); at the second landing merge
    // (2026-10-04) that is 145 -> 146. Measured.
    // plan_client_novalve §4.4, the client dev API routes: IShimStandaloneClient
    // appended ClientRecvProp and ClientSchemaRefusal (nettables),
    // IShimNetClient ClientNetFlow, ClientNetChannel and ClientNetPacket
    // (netgraph), and the new IShimDiagnosticsClient ClientNotShipped and
    // ClientRefusal (absences) (146 -> 153).
    // The C2 rope port (lane c2-beams): RopeUpdate and RopeDestroy went, the
    // rope executor's RopeMaterial and RopeDraw and the reads ClientViewState,
    // ClientComputeLighting and ClientTraceHullWorld came (153 -> 156).
    // The C2 beam port (lane c2-beams): the six BeamCreate* backends and
    // DrawBeamPoints/DrawBeamQuadratic went, the beam executor's seventeen
    // (BeamView .. BeamDrawMesh) came (156 -> 165).
    // The UI's draw primitives (lane ui, plan_client_ui U2): IShimUiClient's
    // seventeen (UiDrawFilledRect .. UiPlaySound) came (165 -> 182), and the
    // old HUD's HudDrawRect, HudDrawText and HudMeasureText went (182 -> 179).
    // Lane c2-sims (plan_client_novalve C2): IShimParticlesClient's three
    // (ParticlesCreate, ParticlesConfigure, ParticlesDestroy) for the managed
    // particle sims' executor, and IShimEffectsRenderClient's SteamJetUpdate and
    // SteamJetDestroy retired with the steam jet's native shells
    // (153 + 3 - 2 = 154). Measured. Then ParticlesSetLights (the sphere
    // renderer's lights) came and SmokeStackUpdate/SmokeStackDestroy went with
    // the smoke stack's shells (154 + 1 - 2 = 153). Measured. Then
    // ParticlesTraceLine (a trace that answers the plane) came and
    // SmokeVolumeUpdate/SmokeVolumeDestroy went with the smoke volume's shells
    // (153 + 1 - 2 = 152). Measured. Then the six trails' updates and
    // TrailDestroy went with shell_trail_sim/render.cpp (152 - 7 = 145). Measured.
    // Then WorldTextUpdate/WorldTextDestroy went and IShimParticlesClient's
    // WorldTextMeshUpdate/WorldTextMeshDestroy came (145 - 2 + 2 = 145). Measured.
    // Then WindUpdate/WindDestroy went and WindPublish came with the client
    // wind's simulation in C# (145 - 2 + 1 = 144). Measured.
    // Then EmbersUpdate/EmbersDestroy went with env_embers' simulation (156 - 2 = 154). Measured.
    // (Rebased onto client-novalve: 170 -2 = 168.)
    // plan_client_novalve §2 cluster 4 (lane c2-fx): the new IShimFxClient's
    // EffectRegistrationCount, EffectRegistrationName and EffectRegistrationRun
    // (165 -> 168). Measured.
    // Lane c2-fx: IShimTempEntBackendsClient's ClientDispatchEffect went, its
    // one managed caller (CTEEffectDispatch's player) now dispatching through
    // the managed table (168 -> 167). Measured.
    // Lane c2-fx: IShimFxClient's nine IEffects carry-out slots (FxSmoke ..
    // FxRecipientCount) as IEffects became a thunk (167 -> 176). Measured.
    // Lane c2-fx: the client-side effect list's twelve (FxMaterialFind ..
    // FxDispatchParticleEffect) as the list went to C# (176 -> 188). Measured.
    // Lane c2-fx: FxEntityRenderAngles and FxEjectBrass for the managed
    // shell-eject effects (188 -> 190). Measured.
    // Lane c2-fx: FxSpriteGlowFraction and FxGlowSightDistance, the sprite
    // planner's two glow answers (190 -> 192). Measured.
    // Lane c2-fx: FxPrecacheCount .. FxCacheMaterialHandles, the client effect
    // precache registry's carry-out (192 -> 196). Measured.
    // (Lane c2-fx rebased onto client-novalve 3571da88: its counts above were
    // taken on a base of 165; on 168 the same +31 is 168 -> 199.)
    // The menu layer's UiSetMenuInput and UiSetCursor came (lane ui, U3) (199 + 2 = 201).
    // Sound cluster 17 (lane c2-sims): IShimSoundscapeClient's eight executors
    // came and IShimViewClient's ClientSetSoundscape went, the push landing in
    // the managed ClientSoundscapes (201 + 8 - 1 = 208). Measured.
    // Lane c2-fx: the legacy tempents' carry-out on IShimFxClient (FxEmitterCreate
    // .. FxLoadModel: the muzzle flashes' emitters and answers, the bounce
    // sounds, the named shell models) (201 -> 224, measured; on the base of 208
    // above the same +23 is 208 -> 231).
    // Lane c2-fx: TempEntPoolPlaySound left; the tempent frame plays its bounce
    // sound in C# (231 -> 230). Measured.
    // Lane c2-fx: FxModelFrameCount, the managed RocketFlare's frame count of a
    // model loaded by name (230 -> 231). Measured.
    // Lane c2-sims, the weather: IShimPrecipitationClient's seven engine reads
    // came and IShimEffectsRenderClient's PrecipitationUpdate/Destroy went
    // (230 + 7 - 2 = 235). Measured on the rebase onto client-novalve 491b5bf2.
    // Rebased onto main ace0f66a: 231 +5 = 236.
    // Lane c2-fx (render budget): FxSpriteDrawBegin and FxSpriteDrawEnd, a
    // managed sprite's draw planned in managed with no crossing back
    // (230 -> 232). Measured.
    // Rebased onto main ace0f66a: 236 +2 = 238.
    // Lane c5-proxies: IShimMaterialProxyClient's seven (ProxyFindVar ..
    // ProxyNotShipped), the material proxy trampoline's engine reads (230 -> 237
    // on client-novalve df8808cc). Ported onto main 2eafcb77: 238 +7 = 245.
    // Lane ui, C3: UiDebugOverlayText and UiDebugOverlayClearDead, the debug
    // overlay's text read for the managed UI (230 -> 232; on client-novalve 26adc6bf 245 -> 247).
    // Lane ui, C3 step 4: ClientVoiceMasks and ClientVoiceRequestState left; the
    // voice manager is managed end to end (232 -> 230; on client-novalve 26adc6bf 247 -> 245).
    // Lane camproto: IShimViewClient's RenderViewToTarget and ViewTargetInfo, the
    // second camera's executor (230 -> 232). Rebased onto main 375193a4c: 245 +2 = 247.
    // C7 item 7: IShimFxSpriteDraw::Store keeps a sprite plan made in the view's draw batch (247 -> 248).
    // Lane c5-particles (/client/{slot}/particles, the particles shell's started systems): 248 -> 250.
    // Lane c5-particles (the particle query's answers are C#'s, replayed against stock): 250 -> 251.
    // Lane c5-particles (the lit smoke stack's colours are C#'s (SphereLighting), replayed against stock): 251 -> 252.
    // Lane c5-particles (ClientParticleEffects, the managed CParticleMgr for effects no entity owns): 252 -> 253.
    // Lane c5-particles (the ParticleEffect and ParticleEffectStop client effects, a dead stock arm ported): 253 -> 255.
    // Lane c5-particles (the impact flecks are C# (FleckSystems), replayed against stock): 255 -> 256.
    // Lane c5-particles (the executor draws a view-space tracer quad and hands managed its view once per view): 256 -> 258.
    // Lane c5-particles (fx_sparks.cpp's routines are C# (SparkEffects), replayed against stock): 258 -> 259.
    // Lane c5-particles (IShimFxClient's three spark carry-outs leave; IEffects' sparks end in C#): 259 -> 256.
    // Lane c5-particles (the native gauss discharge leaves (FX_GaussExplosion, its backend, slot and counter)): 256 -> 255.
    // Lane c5-particles (fx_water.cpp's routines are C# (WaterSplashes), replayed against stock): 255 -> 256.
    // Lane c5-particles (fx_explosion.cpp leaves client.so, with the native explosion chain): 256 -> 254.
    // Lane c5-particles (fx.cpp's routines are C# (FxEffects), replayed against stock): 254 -> 255.
    // Lane c5-particles (fx.cpp's native routine chains leave (FxSmoke, FxMuzzleEffect, FxRicochetSound, FxMuzzleEffectAttached)): 255 -> 251.
    // Lane c5-particles (fx.cpp leaves client.so): 251 -> 248.
    // Lane c5-particles (the temp entities' muzzle-flash emitters are managed (PoolEmitters)): 248 -> 249.
    // Lane c5-particles (the pool's native FxEmitter* chain leaves): 249 -> 239.
    // Lane c5-particles (the native shard chains leave (FX_GlassImpact, ClientShatterSurface)): 239 -> 238.
    // Lane c5-particles (the native blood, bug blood and impact-block plumbing leave): 238 -> 237.
    // Lane c5-particles (the native large funnel leaves (ClientLargeFunnel)): 237 -> 236.
    // Lane c5-particles (func_dust's motes run on DustVolumeSim, drawn by the executor): 236 -> 237.
    // Lane c5-particles (the native dust volume leaves (CSourceSharpDustVolume, DustUpdate/DustDestroy)): 237 -> 235.
    // Lane c5-particles (the native dust puff leaves (FX_Dust, FxDust/ClientDust slots, IFxCarryOut)): 235 -> 233.
    // Lane c5-particles (shell_te_particle_render.cpp leaves, with the blood stream and line backends): 233 -> 231.
    // IShimInputClient's eleven came with the managed input (lane c6-input);
    // rebased onto client-novalve 49cff99fc: 231 + 11 = 242.
    // ClientRumble left with c_rumble.cpp, the mixer being managed (242 - 1 = 241).
    // InputMailbox came with the input mailbox, the render budget's batching (241 + 1 = 242).
    // IShimPhysicsClient's four came with the client physics callbacks (lane c6-input,
    // C6 cluster 9): 242 + 4 = 246.
    // and the splash's four reads and effects: 246 + 4 = 250.
    // The client's managed skeletons (plan physics_bones_native_zero 1.2, client
    // half): StandaloneEngineBlock and StandaloneStoreSequence, 250 + 2 = 252.
    // And its flip retired the client's thirteen native bone slots (pose
    // parameters x6, layers x2, sequence state/info, ground speed, procedural
    // counts, merge source): 252 - 13 = 239.
    // The managed ragdolls (plan physics_bones_native_zero Phase 3): the shell
    // told the solver owns a pose, StandaloneSetRagdoll, 239 + 1 = 240; and the
    // three native body slots left with the native bodies (BecomeRagdoll,
    // RagdollState, RagdollApplyForceCenter): 240 - 3 = 237.
    // IShimConVarsClient's two came with the bounded convars' push (C8, lane c6-input): 250 + 2 = 252.
    // C7 item 5: the observer's camera, ClientTraceHull, ClientEntityRenderPose and
    // ViewOverrideSet: 252 + 3 = 255.
    // C7: ClientViewShake and ClientViewFade went (the Shake and Fade messages
    // reach managed's ViewEffects directly): 242 - 2 = 240.
    // C9: UserMessageRead went (DispatchUserMessage hands C# the payload as one
    // BitBlock): 240 - 1 = 239.
    // plan_particles_so.md: ParticlesThrottleScaling went (managed asks
    // libss_particles.so's ISSParticleLib directly): 239 - 1 = 238.
    // The client physics leftovers (2026-10-18): PhysicsClientEnvironment and
    // PhysicsClientCollisionEvents went (managed holds the level's environment;
    // fork physics.cpp's g_Collisions left client.so): 238 - 2 = 236.
    // plan_server_native_v2 T2: EntitySetSkin is client-only (the server's
    // body was empty): 238 + 1 = 239.
    // plan_server_native_v2 T2: RegisterAmmoType is client-only (239 + 1 = 240).
    // Combined at the rebase onto the forwarders main: 236 + 2 = 238.
    // C7 cluster 12: IShimFlashlightClient's six, the flashlight's executor
    // (texture, border colour, create, update, project, destroy): 238 + 6 = 244.
    // plan_server_native_v2 T4a: the five SoundPatch slots are the client's
    // alone (IShimSoundscapeClient), the server's patches managed (238 + 5 = 243; on main's flashlight 244 + 5 = 249).
    // plan_server_native_v2 T4b: TempEntRegister is the client's alone
    // (IShimTempEntBackendsClient), the server's tables planned in C# (249 + 1 = 250).
    // The client sound emitter's port: its eight executors retired with their callers
    // (ClientEmitSound, PhysicsClientEmitImpactSound, FxEmitTracerSound,
    // FxSoundParameters, FxEmitWorldSound, SoundscapeEmitAmbient/EmitWorld/StopWorld),
    // which the managed ClientSoundEmitter answers: 250 - 8 = 242.
    private const int ClientOnly = 242;
    // (Rebased onto lane c2-beams: 54 +2 = 56.)
    // Sound cluster 17: IShimConsole's RevertNamedConVar (60 + 1 = 61). Measured.
    // C6 cluster 9: the four above, both-sided now (61 + 4 = 65).
    // plan physics_bones_native_zero 1.2: both skeletons managed, the seven
    // shared IShimModels bone slots retired (65 - 7 = 58).
    // DustEffect, the server's alone again (65 - 1 = 64).
    // C9: IShimEngine's FilesSizeOnPath and FilesReadOnPath (57 + 2 = 59).
    // studio.cpp's eviction (2026-10-18): IShimModels' ModelDescriptor retired,
    // the descriptor read from the managed header (59 - 1 = 58).
    // plan_server_native_v2 T2: EntitySetSkin left for the client (59 - 1 = 58).
    // plan_server_native_v2 T2: RegisterAmmoType left for the client and
    // RegisterWeaponType went (58 - 2 = 56).
    // plan_server_native_v2 T3: the four shared StringTable* slots folded
    // (55 - 4 = 51).
    // 2026-10-19: the four game-file read slots folded onto IBaseFileSystem (51 - 4 = 47).
    // 2026-10-19: SurfacePropGameMaterial folded (47 - 1 = 46).
    // 2026-10-19: PointContents folded onto IEngineTrace (46 - 1 = 45).
    // 2026-10-19: SurfaceScrapeSound and MoveSurface folded onto
    // IPhysicsSurfaceProps (45 - 2 = 43).
    // 2026-10-19: MovePointContents folded onto IEngineTrace, its cache to
    // managed (43 - 1 = 42).
    // 2026-10-19: SoundScriptParameters folded onto ISoundEmitterSystemBase
    // (42 - 1 = 41).
    // plan_server_native_v2 T4a: IShimEngine's five SoundPatch slots left for
    // the client (41 - 5 = 36).
    // plan_server_native_v2 T4b: TempEntRegister left for the client (36 - 1 = 35).
    private const int BothSides = 35;

    // TheStructIsExactlyTheSlotsTheClientFills and AServerOnlySlotHasNoStructField
    // went with the struct (plan_client_vtables CC-last 2c): there are no fields
    // left to compare with a side. AbiParityTests.TheEngineApiStructIsGone holds
    // the absence.

    /// <summary>
    /// plan_client_vtables CC-last: neither side fills the struct. The server
    /// stopped at C6c; the client's slots are methods of its shim areas.
    /// </summary>
    [Fact]
    public void NeitherSideFillsTheStruct()
    {
        string path = RepoLayout.Repo("src", "shared", "native", "sourcesharp_gamesystem.cpp");
        string src = AbiParityTests.CodeOnly(File.ReadAllText(path));
        Assert.Contains("SourceSharp_LoadMySide_Host(", src);   // the control: this is the load's file

        Assert.Empty(Regex.Matches(src, @"\bengineApi\.\w+\s*=").Select(m => m.Value));
        Assert.DoesNotContain("SourceSharpEngineApi g_EngineApi", src);
    }

    [Fact]
    public void TheSideCountsAreThePinnedOnes()
    {
        var sides = Sides();

        Assert.Equal(ServerOnly, sides.Values.Count(s => s == Side.ServerOnly));
        Assert.Equal(ClientOnly, sides.Values.Count(s => s == Side.ClientOnly));
        Assert.Equal(BothSides, sides.Values.Count(s => s == Side.Both));
    }

    [Fact]
    public void TheWalkTellsTheThreeKindsApart()
    {
        // The control: one known slot of each kind, so a census that stopped
        // reading one of its sources cannot pass by calling everything one kind.
        var sides = Sides();

        Assert.Equal(Side.Both, sides["Msg"]);
        Assert.Equal(Side.Both, sides["AcquireInterface"]);
        Assert.Equal(Side.ServerOnly, sides["EntityGetHealth"]);
        Assert.Equal(Side.ClientOnly, sides["ConNPrintf"]);
    }

    internal enum Side { ServerOnly, ClientOnly, Both }

    /// <summary>Slot name → the side(s) that serve it.</summary>
    internal static Dictionary<string, Side> Sides()
    {
        var client = ClientMethods();
        var server = ServerMethods();

        return client.Union(server).ToDictionary(
            s => s,
            s => client.Contains(s) && server.Contains(s) ? Side.Both
               : server.Contains(s) ? Side.ServerOnly : Side.ClientOnly);
    }

}
