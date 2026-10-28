# To do

Work that has been decided on but has no plan of its own. Each entry says
what is wrong or missing and its evidence (file:line, a command or a number,
with the commit it was measured at), whose it is, and the next step. Entries
re-checked on 2026-10-20 say so, with the sha.

Pruned 2026-10-20 at 4c808928c: the closed entries and the duplicates left,
and their text stays in `git show 4c808928c:docs/todo.md`. The last section
lists the closed ones that code or docs still cite.

Entries by owner (the file keeps its order; this is the index):

- **The live gate** (tools/live-gate, tools/daily-gate): live-gate's
  fault check across a hot reload; chat's second
  client exits during connect; no step covers screen shakes and fades; a
  rename cannot be driven on a Steam client.
- **The server lane** (plan_server_native_v2): player-movement controls and
  entity fields; player weapon and score questions; managed decisions whose
  native caller went in T2; the sound emit path (T4a); temp entities and
  datatables (T4b); entity API and world (T4d); the trace filter (T4f); the
  player commands (T4g); substrate collapse (T5); bitvec.h's two tables (T6);
  the spectator port's gaps; trigger_push's two divergences.
- **The client port** (plan_client_novalve C7/C9):
  entity dissolve (client render first); the client's sound emit path; client
  port follow-ups (small); the render plan (C7) follow-ups; the render debug
  views to Debug.Api; one managed input system; C7 particles; the new-style
  effects' camera object; QueueOverlayRenderView; the client flashlight;
  Client C9; entity entry 264 after the second map load.
- **The ABI tail**: the serialised pass ran 2026-10-20 (docs/abi-retirements.md);
  its follow-up is "Game logic the ABI pass left unreferenced" under client
  port follow-ups.
- **TF2**: the stage-0 class-script fixture; Pyro vision's facts.
- **descent and the host**: the hosted-reload hazard; descent-death P3-P6.
- **Unowned infrastructure**: the vtable trap's re-entry; -assertstack per
  DLL; sdk-links/windows.txt.
- **The perf pass** (after the port, user ruling 2026-10-12): the ViewEffects
  fold; the render budget and trace cost rows inside the client and T4f
  entries.
- **User directions waiting on a stage**: the usercmd wire format.
- **Not open work**: the rulings and divergences kept only here, and the
  closed entries still cited, in the last two sections.

## live-gate's fault check does not see across a hot reload

Every step fails on a managed EXCEPTION IN or `could not acquire "` line in
its log (c0902624e). The hot-reload step restarts the managed log, so the
step's window is re-read from the new log's start and reported "NOT ALL": the
lines between the step's start and the reload are not looked at (500 lines
read in each pass at 760ca80fc). Reading the native console feed (/console,
epoch-aware, which ConsoleFeed already pages) beside the managed log, or
marking the log before the reload, would close it.

**Owner:** the live gate. The window's line count is printed with "NOT ALL"
(tools/live-gate:9828 at 4c808928c). **Next:** mark the managed log before the
reload, or read /console beside it, and give the hot-reload step a self-test
row that fails when a pre-reload EXCEPTION line goes unread.

## Player-movement controls and entity fields that answer nothing: ports owed

Added 2026-10-18 by plan_server_native_v2 T2, which deleted sixteen more
server slots whose native bodies were empty or constant. The no-ops and
constants are EngineBinding's now, each commented; nothing changed for a
caller. What the managed entities ask for, and nobody does yet:
- the player command path: a lagged-movement factor and disabled buttons
  (player_speedmod), forced buttons and no auto-movement
  (trigger_playermovement, trigger_camera), freezing and reading held buttons
  (trigger_camera), the movement constraint sphere
  (point_playermoveconstraint), FOV (env_zoom);
- entity state that left with CBaseEntity/CBasePlayer: armour (Armour.cs owns
  it in managed; GetArmor answers 0), the take-damage mode (always DAMAGE_YES),
  per-entity friction (func_friction), the server-ragdoll flag
  (trigger_serverragdoll), the texture frame index (env_texturetoggle), and
  whether an entity sits in the 3D skybox (env_smokestack; GetArea against the
  managed sky cameras is the port).

Re-measured 2026-10-20 at 4c808928c: the constants are still EngineBinding.cs's
(src/shared/interop: GetArmor :641, SetFriction :804, Get/SetTextureFrame
:895/:1140, DetectInSkybox :1142, SetForceServerRagdoll :1172,
SetTakeDamageMode :1174, SetFov :1267, ForceButtons :1297, SetLaggedMovement
:1317). **Owner:** the server lane (plan_server_native_v2 named them in T2).
**Next:** not chosen; each port gives its state a managed owner and replaces
one constant.

## Player weapon and score questions answer constants: the Arsenal and a score store are the port

Added 2026-10-18 by plan_server_native_v2 T2, which deleted eleven IShimPlayers/
IShimInventory slots whose native bodies were constants (the IShimInventory area
went with them). The constants now live in EngineBinding's EnginePlayers, each
with a comment, and nothing changed for a caller. They are gaps, not answers:
- HolsterActiveWeapon / DeployLastWeapon answer false: player_speedmod
  (PlayerSpeedMod.cs) asks the Arsenal to holster and redeploy.
- GetActiveWeaponEntity / GetWeaponEntity / GetViewModelEntity answer
  EntityHandle.None: trigger_camera and WeaponInventory ask for entities the
  Arsenal does not name yet.
- AddScore does nothing: the engine's frag counter left with CBasePlayer, and
  game_score has no managed score store to add to.
GetActiveWeapon, DropActiveWeapon, RemoveAllItems, GiveAmmo and GiveItem are
answered by ArsenalPlayers for its players; the constants are the no-Arsenal arm.

Re-measured 2026-10-20 at 4c808928c: EngineBinding.cs:1237-1259 still holds
AddScore, DeployLastWeapon, GetActiveWeaponEntity, GetViewModelEntity and
HolsterActiveWeapon as constants. **Owner:** the server lane, with whoever
owns the Arsenal. **Next:** the Arsenal names its weapon and view-model
entities and holsters; a managed score store gives game_score a target.

## Entity dissolve (stock's env_entity_dissolver look) is not ported

Added 2026-10-18 by plan_server_native_v2 T2, which deleted the server's
`EntityDissolve` slot: it was a declared refusal (Warning, answered 0) that no
managed code called. Stock's dissolve is a CLIENT effect: a material proxy run
over the target's studio model for a while, driven by the target's render mode
and a dissolve entity parented to it. The standalone client hierarchy has
neither, so a managed env_entity_dissolver or combine-ball kill has nothing to
ask. Porting it is client render work (a render-mode override and the proxy
driver), then a managed server decision that sends it; no native server slot is
needed for that.

**Owner:** the client port (the render-mode override and the proxy driver),
then the server lane (the decision that sends it). anim_cluster_ledger_test
pins this heading: keep it verbatim. **Next:** not started; no plan names it.

## Managed decisions whose native caller went in plan_server_native_v2 T2

Each is ported stock behaviour with no native path left to reach it.
Managed callers should call these directly; the managed-built slot goes in T3.

- **PlayerRelationship.** `IManagedClients.GetPlayerRelationship` and
  `ServerModule.GetPlayerRelationship` hold the teammate rule. Its native caller,
  `CSourceSharpGameRules::PlayerRelationship`, had no caller and went.
- **The bubble trail clamp.** `TE_BubbleTrail` clamped its count to 255
  (`BUBBLE_TRAIL_MAX_COUNT`) before writing it. The native forward went in the
  server gc cull, and the orphaned enum in T2. A managed sender of `TeBubbleTrail`
  owes the clamp.

`IManagedClients GetPlayerRelationship` was retired 2026-10-20 by the
serialised ABI pass (docs/abi-retirements.md). No managed code sends a
TeBubbleTrail yet (grep, 4c808928c). **Owner:** the server lane. **Next:** add
the 255 clamp with the first managed TeBubbleTrail sender.

## The sound emit path in C# (plan_server_native_v2 T4a step 1): follow-ups

- **env_microphone's OnSoundPlayed.** Stock's `EmitSoundByHandle` let
  `CEnvMicrophone::OnSoundPlayed` hear (and swallow) every script emit; neither
  the native emitter nor the managed `SoundEmitter` that replaced it calls one.
  The emitter is managed now, so `EnvMicrophone` can be offered each emit there.
- **The `_DEBUG` "Sound %s was not precached" line.** Stock printed it from
  the emit in debug builds; the managed emitter drops it. The forwarder it
  needs, `IEngineSound::IsSoundPrecached`, exists since T4a's precache path
  (`IFwdEngineSoundServer.IsSoundPrecached`); `ISoundEmitterEngine` would
  carry it to the emit.
- **No runtime decal reached the client state file in the live save run**
  (2026-10-19, the client save codec lane): with sv_cheats 1, an SMG burst at
  the floor and an env_explosion's scorch (spawnflags 1) left the engine's decal
  list at the map's own 76 (ClientDecalTranslate 15, ClientSaveWriteFields 76
  before and after). Either this build's impact and scorch decals are not
  engine decals or they are not placed; until that is answered the live save
  check cannot tell a reapplied decal from one the map placed again.

Re-measured 2026-10-20 at 4c808928c: no managed code calls OnSoundPlayed, and
"was not precached" appears only in SoundEmitterCore.cs:55's remark.
**Owner:** the server lane (the first two rows); the client port's save
codec (the decal row). **Next:** offer EnvMicrophone each emit in
SoundEmitter; carry IsSoundPrecached on ISoundEmitterEngine for the debug
line; for the decal row, find whether impact and scorch decals reach the
engine's decal list at all before the live save check relies on them.

## Temp entities and datatables in C# (plan_server_native_v2 T4b): what stayed native, and why

Measured at the lane's tip (2026-10-19). The server plans its temp entity
tables in C# (TePropPlanner, TeSchemaRegistry, SendPropPlan, ServerTempEntEngine)
and plays them through the PlaybackTempEntity forwarder; native stores the plan
(shell_tempents_store.cpp) because the engine walks SendProp/SendTable/ServerClass
by layout. What is left, each with its exit:

- **shell_te_forwards.cpp's TE_* forwards, DispatchEffect and ss::Effects()**,
  and **SourceSharpTe_Forward** (shell_tempents.cpp), the round trip that packs
  a native caller's arguments in managed and sends them with the caller's own
  IRecipientFilter. They stay while their native callers do. Re-measured
  2026-10-20 at 4c808928c (grep): the forwards left are TE_BloodStream,
  TE_Decal, TE_DispatchEffect, TE_Smoke, TE_Sparks and TE_WorldDecal, called
  from world_adapter.cpp (CWorld::DecalTrace's TE_Decal / TE_WorldDecal),
  util_shared_library.cpp (Tracer, bloodimpact, ss::Effects()->Sparks),
  server_util_library.cpp (TE_BloodStream, ss::Effects()->Smoke),
  particle_manifest.cpp ("ParticleEffect") and shell_absent.cpp ("bloodspray").
  shell_worldquery.cpp calls none of them since T4d moved its five effect slots
  and T4f its traces.
- **EffectNameIndex** (shell_te_forwards.cpp) serves the native TE_DispatchEffect
  only; managed asks the same table through EffectDispatchNames.
- **dt_send.cpp's SendPropInt, SendPropVector (with AssignRangeMultiplier) and
  SendPropString**: their callers are shell_standalone.cpp's tiers and
  generic_payload.cpp, which build their tables at DLL init from native
  constants. SendPropPlan already ports all three; they go with the standalone
  shell's own move.
- **generic_nettable.h** (the tiers, FNV class hash, CSourceSharpTypeTable,
  SchemaNeedsRestamp): its server readers are CStandaloneEntity's
  GetServerClass (the engine calls it per entity through the networkable's
  vtable, so the answer has to be native-local), shell_standalone.cpp's
  NetPublishType / NetHierarchyDeclare slots (shared with the client's
  shell_recvtable.cpp) and the reload restamp. Moving the decisions means the
  shell taking its tier, type id and schema hash from managed at create: the
  standalone entity base's move, not this lane's.
- **ss_sandbox.bsp is not built in a fresh worktree** (it needs the map-tools
  checkout beside this one), so live-gate's map-change step fails there on the
  map's absence; T4b ran it and saw 473/61 dm_lockdown both passes. The sandbox
  map's ss_te_tester fires every temp entity and is the fullest live exercise of
  the managed send; run it from a tree with the map built.

**Owner:** the server lane; the sandbox-map row is the live gate's (this
worktree has bin/debug/mods/test/maps/ss_sandbox.vmf and no .bsp at
4c808928c). **Next:** each row's exit, as written; the forwards go caller by
caller.

## Entity API and world in C# (plan_server_native_v2 T4d): what stayed native, and why

Measured at the lane's tip (2026-10-19). T4d moved: the five effect slots
EffectsOverTempEnts already answered, four collision compositions, a player's
respawn / shoot origin / aim, the trace mask table (TraceMasks), the decal
precache (onto IVEngineServer), the area portal records and their per-client
walk (AreaPortals), worldspawn's own keys, the default light styles, and the two
TE forwards left without callers. What is left in the lane's files, each with
its exit:

- **BloodEffect**: g_pGameRules->IsMultiplayer() (the amount x5) has no managed
  route (BloodEffectPortabilityTests says so); UTIL_BloodStream keeps
  TE_BloodStream, UTIL_Smoke and UTIL_BloodDrips keep ss::Effects()'s Smoke and
  Sparks and their TE forwards.
- **CWorld::DecalTrace** (world_adapter.cpp:703) holds TE_Decal and
  TE_WorldDecal. Its native caller, UTIL_DecalTrace, went in T2 and
  UTIL_ImpactTrace / CWorld::ImpactTrace in T5; grep at 4c808928c finds no
  other caller, but ss_world.h:48 lists DecalTrace in CWorld's engine-facing
  vtable. Next: check whether the engine calls that slot; if not, delete it and
  the two TE forwards with it.
- **W_Precache and CPrecacheRegister** fill g_sModelIndex* globals the native
  util libraries and TE forwards read; they go when those readers do.
- **CWorld::Precache's sequence** (game rules install, sound ent, the
  pre-entity pass, standard entities, W_Precache, ClientGamePrecache) is the
  lifecycle lane's (P3e: kServerSystems' order to LifecycleStages).
- **CWorldCollideable::GetCollisionModel** is an ICollideable method the engine
  calls through the vtable; its FOLD row is wrong for that reason.
- **EntityGetHealth / EntitySetHealth / PlayerIsAlive, EntityGetEyePosition /
  EyeAngles, EntityGetVelocity / SetVelocity / Teleport, EntityAddBaseVelocity**:
  the native movement block reads health and deadflag (MvIsAlive), v_angle sits
  in the CPlayerState the engine reads by layout (GetPlayerState), and the
  velocity home is the native physics object's: the movement lane's (P3g).
- **EntityGetOrigin / Angles / ModelName** are both-sided slots with client
  bodies; the server half moving to block reads needs the row split per side.
- **Not reached this lane, no blocker found**: EntityIsPlayer, EntityGetClassName,
  GetPlayerHandle and FindEntityByClassname (the directory answers them; the
  EngineSide members are shared and [SuppressGCTransition]),
  TemplateSpawnKeyed, EntitySetKeyValue,
  EntitiesInSphere, EntityGetState, EntityTakeDamage, EntityModelSurfaceProp (it
  wants IMaterial / IMaterialVar forwarders) and AngleVectorsFromForward (the
  client's pane split is native and the two must agree to the bit).

**Owner:** the server lane (P3e is the lifecycle lane's, P3g the movement
lane's). **Next:** CWorld::DecalTrace's engine question first: it frees
TE_Decal and TE_WorldDecal.

## The server trace filter in C# (plan_server_native_v2 T4f): what stayed native, and follow-ups

Measured at the lane's tip (2026-10-19). T4f moved the per-candidate trace rule
to managed (TraceFilterRules over libss_fwd_server's ITraceFilter), and every
server trace with it: TraceLine/TraceHull/EntityTraceHull, WaterSurfaceBelow,
PaintDecal's traced and permanent forms, BubbleEffect and the movement hull's
MoveTrace.

- **r_visualizetraces draws nothing for server traces any more.** The native
  UTIL_Trace* inlines drew each trace when the engine's ConVar was set; the
  managed route has no debug-overlay draw. A divergence of a debug aid only; a
  managed draw over IVDebugOverlay would restore it.
- **Trace cost.** Debug, dm_lockdown, 30 s at rest: line trace mean 3.9 -> 4.7 us
  (+1 ShouldHitEntity crossing a frame). Levers if the perf pass wants it: cache
  the candidate's block pointer by handle for the trace's duration, or batch the
  candidates of one trace.
- **MovePass has no fact**: its slot (GetPlayerHandle) is [SuppressGCTransition],
  which a managed fake cannot stand in for in a test host. The live gate's move
  and jump steps cover it.
- **The native recipient filters left**: shell_usermessage_send.cpp's three
  (UserMessageSend), CWorld::DecalTrace's broadcast filter (UTIL_DecalTrace
  went in T2; see the T4d row), TE_DispatchEffect and the TE forwards'
  debug arms. The managed recipient filter object (fwd_recipient_filter.cpp) is
  the exit for each; none is a trace.
- **CSourceSharpGameRules::ShouldCollide, s_CollisionRules and
  IShimCollision::RegisterCollisionRules** have no server caller left (the
  managed rule reads the same rows); the vtable keeps them linked. Retiring the
  slot is a --reorder on a shared row, for a later lane.
- **Stock shape-clause divergences kept** (TraceFilterRules' summary): the
  transparent / MOVETYPE_PUSH / debris skips stock makes were never answered for
  this hierarchy, and a move is not where they change. The client's filter still
  reads every entity as the static-prop case (trace_filter_library.cpp).

**Owner:** the server lane; the trace cost row is the perf pass's.
**Next:** the managed recipient filter for the native filters left.

## The server's player commands in C# (plan_server_native_v2 T4g): what stayed native, and follow-ups

Measured at the lane's tip (2026-10-19). T4g moved the server's user-command
path to managed: the tick base adjust, RunCommand's prepare (clock, angle
sanity, fixangle, base-velocity fold, SetupMove), the movement loop called
directly over a per-player managed record (ServerPlayerMoves), FinishMove and
the body pitch, the move helper's decisions (ServerMoveWorld: touches, sounds,
fall damage, the surface), the 30-word PlayerSync copy (PlayerMovePublisher
writes the movement words; native writes origin, health and alive), the
movement constants (ServerMoveConstants), the view punch, the slow-movement
word and a player's base velocity. Debug server.so .text 389,111 -> 359,607
bytes; native C++ -1,094 lines; movemath and 3d-cmdgate pass before and after.

What stayed native, and why (each is an engine vtable, a struct the engine
reads by layout, or a pointer it holds):

- **PlayerCommandBegin / View / Finish** (shell_standalone_player.cpp): the
  facts a command needs from the engine-held player (v_angle, fixangle, the
  CPlayerState the engine reads by layout), SetAbsOrigin / SetAbsAngles on the
  edict the engine networks, and TouchTriggers (engine's trigger sweep).
- **MoveTraceServer**: the hull trace is IEngineTrace through the managed
  filter (T4f); native hands back the hit and the ground as handles.
- **PlayerFallDamage**: the shell's damage path (TakeDamage on the standalone
  player); its decision is managed's (ServerMoveWorld.PlayerFallingDamage).
- **PlayerSyncEngineSide / SyncToBlob**: origin, health and alive are the
  engine side's words in the blob the client reads by layout.
- **QueuedUsercmds**: the count of decoded commands waiting for the frame's
  one PlayerRunCommands crossing; the commands are managed's.
- **The native mirrors** (flags, move type, view offset, velocity, base
  velocity, ground, fall velocity), written by PlayerCommandFinish, because
  native readers remain: shell_worldquery's EntityGetState (overlaid by the
  managed route for players), standalone_relays' move type,
  shell_entityapi's PlayerSetMoveType return and EntityGetEyePosition
  (EyePosition over the view offset, which GetPlayerShootOrigin also reads),
  and shell_standalone_player_debug.cpp. Each reader is the exit: move it to the
  record and the mirror goes.

Follow-ups:

- **IMovementPlayer on the server's CStandalonePlayer.** Nothing on the server
  runs the loop over it now (MvCurrentCommandNumber answers 0). Dropping the
  base and the members only the loop read (water jump, swim/step sound
  times, surface data, ladder normal, stuck, crouch counts) needs a
  per-use audit of the shared headers the client still compiles.
- **shell_movement.cpp's server-dead parts** are behind CLIENT_DLL in the
  shared file (the client's preprocessed TU is byte-identical, __LINE__
  masked). Moving them to src/client/native is the server-dead-to-client-only
  ruling's next step.
- **The client never calls SetAreaState**: the area bits had no wire since T2;
  T4g deleted the server's copy.
- **Divergence: damage between commands no longer republishes the movement
  words** (stock's TakeDamage did not either; the old native SyncToBlob did).
  The next command publishes them.
- **A hot reload seeds the record from the published state**, so the ground
  handle is not restored until the next ground trace.

**Owner:** the server lane (movement). **Next:** the IMovementPlayer audit of
the shared headers, then shell_movement.cpp's server-dead parts to
src/client/native.

## Substrate collapse (plan_server_native_v2 T5): what stayed native, and follow-ups

Measured at the lane's tip (2026-10-19; nm U over both link edges, the
symbol in the linked .so as the liveness check). T5 deleted or moved every
definition server.so's objects no longer import, except these, each with the
reason it was left:

- **Client-only imports kept in a shared TU as a seam.** `Tier0_DevSpewV`
  (tier0_spew_port.cpp) is imported only by tier0_spew_client.cpp's
  DevWarning/DevMsg( level ), and `SourceSharp_LogStub` (shell_diagnostics.cpp)
  only by client objects' SOURCESHARP_STUB macros; both are thin bodies over the
  file's own static helpers (SpewV and DeveloperAt; SourceSharp_DiagPrint).
  Moving them means exporting those helpers: a header change, not a deletion.
- **UTIL_Tracer** (util_shared_library.cpp) is server-dead; its one client
  importer is client_util_library.cpp's UTIL_ParticleTracer, itself dropped by
  the client's --gc-sections. blood_family_move_test and
  client_entity_resolve_test pin it where it is. Delete both with the client's
  dead-tail pass, or move it beside its importer.
- **shell_movement.cpp's server-dead arms** (the T4g bullet above), including
  `SourceSharp_MovementParams`, live on the client through the TU's own caller.
- **The header-inline relays.** `StandaloneEntity_IsPlayer`,
  `StandaloneEntity_OwnerHandle` (with `CStandaloneEntity::OwnerHandleEntity`),
  `PhysicsStandaloneToucher` (with `CStandaloneEntity::AsToucher`) and
  `GameEntity_ModelIndex` have no importer, but anyentity.h's and
  physicsowner.h's inline accessors name them, so they go with an audit of
  those two headers (AnyEntity_t, PhysicsOwner_t), which the client compiles
  too. GameEntityRelayEvictionTests pins GameEntity_ModelIndex's declaration.
- **dt_send.cpp**: `SendTable::SendTable()`, `~SendTable()` and the
  `CNonModifiedPointerProxy` constructor have no importer. The proxy
  constructor is what builds `s_pNonModifiedPointerProxyHead`, a D6 FLOOR row;
  both go with the standalone shell's move (the SendPropInt/Vector/String
  bullet above), not on their own.
- **Managed-table slots with no native caller**: `IManagedHierarchy.HierarchyResolveParentNames`,
  `IManagedSoundEnt.ServerSoundEntInsert` and the `ManagedSoundEmitter()` accessor's
  interface were retired 2026-10-20 by the serialised ABI pass (docs/abi-retirements.md).
- **Kept on purpose**: alloc_check.cpp's `__wrap_*` set (the debug allocation
  checker; a wrap with no caller is the guard for the day one appears),
  exit_watch.cpp's `ExitWatch_*ForTest` (test hooks that drive the live exit
  watch), `CStandaloneEntity::operator delete( void *, int )` (the pair of its
  placement new), and `SourceSharpPlayerSync_WriteEngineSide`, a server-only
  arm in the shared standalone_player_sync.cpp.
- **mathlib_port.h's ss_mathlib:: twins with no non-test caller**
  (AngleQuaternion, Approach, BasisToQuaternion, Catmull_Rom_Spline,
  MatrixVectors, the three-argument VectorAngles, kPower2N), held to stock by
  mathlib_port_stock_test. (The client members only collisionutils.cpp reached
  went with it at C7 S2, mathlib_port_client.cpp's header.)

**Owner:** the server lane, and the client's dead-tail pass for UTIL_Tracer. **Next:** each row's
exit, as written.

## The client's sound emit path in C#: follow-ups and divergences

- **A client entity with no managed half has no speaker gender.** The client's
  `SpeakerModel` asks the client registry by index (`IClientStandalones`); a
  shell with no managed instance answers nothing, so its gendered script plays
  its GENDER_NONE wave. sound_emitter.cpp asked the slot table, which held
  every shell. Players (the gendered voices) are managed, so nothing audible
  changed; an index-addressed model-name read would close it.
- **An entity sound from a handle that names nothing, or a client-only entity,
  sounds from the world.** `ClientSounds.EmitFrom` maps `GetIndex`'s -1 to
  SOUND_FROM_WORLD; the native executor emitted from -1 (the engine's "the
  view entity") for a resolving client-only shell. Telling the two apart needs
  a resolve answer the entity API does not give yet.
- **No caption is shown on the client.** Stock dispatched a caption straight to
  the HUD's `CHudCloseCaption`, which this client does not have
  (`NoCloseCaptionHud`; sound_emitter.cpp's `GET_HUDELEMENT` found nothing
  either), and `fromplayer` is false (`ClientSoundContext.FromPlayer`: no SDK
  players). A managed caption element would take `ICloseCaptionHud` and the
  owner walk.
- **The sound patch still crosses twice.** `CSoundPatch` (shell_soundenvelope_client.cpp)
  keeps its script-or-wave choice, its filter copy and its state natively, so a
  patch's emit goes managed controller -> native patch -> managed emitter.
  Moving the patch's state to managed (the envelope's native half, C2) makes it
  one crossing.
- **The team-255 broadcast is a native game-event listener** (shell_client.cpp's
  `FireGameEvent`); a managed listener would take it, and the RPG's
  `RPGShotDown` effect (managed since C7: RpgShotDownEffect) has no sender in this
  build.

**Owner:** the client port. **Next:** not chosen; the cheapest is the
index-addressed model-name read for the speaker gender.

## Client temp entities, impacts and the schema gate in C# (lane M1-C, 2026-10-25): follow-ups

- **Dead slots for the serialised ABI pass.** Lane M1-C could only append, so
  these stay until the pass retires them: `IManagedDiagnosticsClient::Md5PseudoRandom`
  (md5_pseudorandom.cpp left client.so), `IManagedTempEntsClient::OnImpactChoice`
  with `ImpactChoicePolicy.Choose(span)` (the native impact chain is gone;
  `BulletImpactEffect` decides), `IShimTempEntBackendsClient::ClientPlayerDecalShoot`
  (empty; `PlayerSprays` decides, through `ClientPlayerLogoDecal`) with
  `IEngineEffects.ClientPlayerDecalShoot`, and `IShimStandaloneClient::ClientSchemaRefusal`
  (empty; the refusals are `ClientSchemaGate`'s, and `ShimAreaTypedCallsTests`
  waives it by name until it goes). The client `SourceSharpTe_Forward`
  (shell_tempents_client.cpp) has no caller since TE_Bubbles and TE_Smoke left.
- **FindTempEntByID's search is still native** (`SourceSharpTePool_Find`,
  ruled LOGIC): its only stock caller is TF's tf_fx_christmaslights.cpp, so
  nothing in this build reaches it, and the managed pool holds neither field
  it matches (skin and hitSound). It moves with the TF stage that brings the
  caller.
- **Divergences recorded by the lane:** the warp-shield impact particle is
  dispatched by name; an impact's control-point entity resolves from the hit's
  index; a spray checks its files before the logo material;
  `cs_cl_schema_status` counts the declared types, not the published ones.

**Owner:** the client port. **Next:** the serialised ABI pass for the slots.

## The TF2 stage-0 plan's class-script fixture does not exist

Added 2026-10-12 at stage0-core-seams 724e7547e (phase 0.2's gate).

`docs/plans/tf2/stage0_core_seams.md:103,120` names
`scripts/playerclasses/scout.txt` in `tf2_misc_dir.vpk` as the fixture whose
`"speed_max"` gates `tools/vpk-extract`. No such entry exists: the pack's
`scripts/playerclasses/` holds nine `.ctx` files (binary, not KeyValues),
and `--list '*scout*'` returns `classes/scout.res` (a UI resource:
`classNameLabel`, `#base "default.res"`) plus HWM expression files. The
loose `content/tf/scripts/items/items_game.txt` exists but holds no
`speed_max` either (0 matches). So phase 0.2's gate ran its extraction arm
against a real entry that *does* exist (`scripts/autorp.txt`, and
`--list 'scripts/items/*'`), and the `speed_max` claim is left unproved.

What is known: per-class speed in stock TF2 is not a KeyValues file at all
-- grep `../source-sdk-2013/src/game/server/tf*/tf_player_shared.cpp` /
`tf_shared.cpp` for the character table before Stage 1.2 ports classes, and
give that phase a fixture that exists. Behaviour-only test, no header.

Re-checked 2026-10-20 at 4c808928c: stage0_core_seams.md:103,120 still name
scout.txt, and stage1_shared_foundation.md:58,78 plan class facts on
`speed_max`. **Owner:** TF2. **Next:** before Stage 1.2, find the class table
in the stock TF2 source and give the phase a fixture that exists.

## The vtable trap's report can recurse before it fails the process: open

`ManagedVtables.FailFast` (src/abi/ManagedVtable.cs) writes its message
through the side's log before `Environment.FailFast`. When the trapped slot
is one that a print reaches (IManagedConsoleOutput, through the console
forwarder), the report re-enters the trap. It recursed until the stack ran
out: a SIGABRT whose core held 1,789 copies of the message, and no clean
FailFast line in any log (the nightly's hot-reload step, 10-10 and 10-11).
The cause, the forwarder staying installed across the reload gap, is fixed
(CConsoleDisplayReloadGap). The trap still needs a re-entry guard, so that
any future trapped print fails once and plainly. The fact needs an injected
fail action, because the default one kills the test host.

Re-measured 2026-10-20 at 4c808928c: `FailFast` (src/abi/ManagedVtable.cs:235-244)
has no guard; the constructor's `onTrap` argument (:228) is the injection
point a fact can use. **Owner:** none named (managed ABI infrastructure).
**Next:** a re-entry guard in FailFast and a fact that traps a print through an
injected onTrap.

## Hazard: a hosted mod that reloads its level loses every player's lease

Added 2026-10-11 from the k3s round-end run (branch client-r-k3sfix, base 2eafcb77).

On a level change the engine disconnects every client, and tf2_descent's
`PlayerService.OnClientDisconnected` takes each one as a real leave: it sends
PlayerLeft, the host releases the lease and the gateway ends the player's
Steam auth, so the client times out instead of following the change. A
hosted instance that changes map is also refused its next Booting
(`not_booting`, retried silently), after which the server turns every connect
away as "starting". tf2_descent no longer reloads (`RoundEndGate` refuses the
core's round end), so nothing triggers this today. Any hosted mod that does
reload a level (a round end, `changelevel` from the console or the dev API, a
map vote) needs, first: disconnects during a level change kept apart from
leaves, and a same-instance reload the host accepts.

**Owner:** descent and the host. **Next:** none until a hosted mod reloads a
level; then the two changes above come first.

## descent-death: P1+P2 on main; P3-P6 remain the plan's own work

Added 2026-10-05 at main 8ec65287; status re-taken 2026-10-06 at main 043862af; P2 re-taken 2026-10-08; landed on main with the todo batch 2026-10-09.

P1 landed as `bc10ca92` (host-side hooks: `IPlayerDeathHook`, `IRespawnGate`, `Corpses.Keep`).
P2 landed 2026-10-08 on todo-list (`01e24de3`..`1561f57d`, retired to `archive/descent-death-p2`):
`IHostLink.RecordDeath/LootCorpse/TravelDirection.Corpse` and `DeathService`
(keep-the-body, death screen, host told, hop-hold + respawn-gate + retry + 60 s kick,
161 descent-leg facts; the lane's plan §5 note carries the gate record).
Resume P3-P6 from main's tip following
`docs/plans/plan_descent_death.md` §5 (P3 needs host SDK 2.5.0's anchor calls; P5 is a k3s live session).

Re-checked 2026-10-20 at 4c808928c: P3 has not started (no teleporter entity
in src/tf2_descent, no PlaceAnchor call anywhere in src). **Owner:** descent.
**Next:** P3, once host SDK 2.5.0's anchor calls ship.

## Client port follow-ups (small)

Added 2026-10-05 from the client no-Valve lanes (plan_client_novalve.md), merged to main with the client port squash.

- **Server browser has no data source.** No master-server or LAN query exists yet; it opens empty (C4: Steam matchmaking over the managed flat API).
- **MOTD HTML.** HTML/URL MOTDs fall back to `motd_text` as stock does with HTML off; a web view (Steam HTML surface, already probed in C0) is the later option.
- **Client physics props: what ClientPhysicsProps still does not carry** (it ports CTempEnts::PhysicsProp and C_PhysPropClientside, 73b64446d): the respawn zones (no map prop_physics_multiplayer path), a piece's motion-disabled flag, the skin-family clamp, and prop shadows (temp entities cast none).
- **The client ammo table is written and never read** (lane M1-B, 2026-10-25). `GetAmmoDef` had no importer on client.so's link edge and is deleted, so `SourceSharp_RegisterAmmoType` (shell_client.cpp, the `RegisterAmmoType` row of `IShimStandaloneClient` in docs/engineapi-areas.tsv) fills a `CAmmoDef` nothing reads, from `ClientModule`'s ammo registration. Retire the row, the native body and the managed call together in the serialised ABI pass (rows only append in a lane); until then the slot is ruled LOGIC in its rulings file.
- **Three client LOGIC rows lane M1-B left, each blocked outside its files** (2026-10-25; rulings in docs/shim-scope/client): `UTIL_IsLowViolence` (client_util_library.cpp, stock's four violence_* convars or the engine) needs `IVEngineClient::IsLowViolence` forwarded (tools/engine-forwarders' EngineClientVtbl) and the `IShimTempEntBackendsClient` `ClientIsLowViolence` row retired, so managed's `ClientIsLowViolence` decides it; `FormatViewModelAttachment` (shell_client_orphans.cpp, stock's viewmodel FOV squash) is called per particle by the native particle renderer's local-space emitter (shell_managed_particles.cpp) and moves with that transform; `SourceSharp_RegisterAmmoType` is the dead ammo table below.
- **Game logic the ABI pass left unreferenced** (2026-10-20, docs/abi-retirements.md): `ShellLessRoutes.Find`, `PropData.RandomChunkModel`, the `Templates.Add` the native map parse used, `PhysRagdollMagnet.Best` and `C_StandaloneEntity::SetShadowCast` lost their last callers with their slots (SetShadowCast was deleted by lane M1-A, ab53190cc: it set the block's ShadowChoice, which managed reads, and stock's choice is managed's own). Each is a port of stock logic, so the next dead-code pass decides each one (port-dead-stock-arms ruling) rather than the ABI pass. The pass's census was two levels deep (a slot's wrapper, then the wrapper's callers) plus the compiler; a third-level chain the census could not see is that pass's question too.
- **Lane M1-A (2026-10-25): what moved to managed and is not yet seen live.** The standalone shell's remaining decisions left client.so (tools/shim-scope R7 7435 -> 5241 at the lane's tip): the distance fade (StandaloneFade), a broken surface's materials and plan (BreakableSurfaceDraw), the colour-correction table and the frame's weight reset (ColorLookupTable, ColorCorrections at FRAME_RENDER_START), the viewer's own body (ClientRenderPolicy's OwnerSeenRule) and the interpolation filter and delay (InterpolatedSet). No live-gate step breaks a window or stands in a color_correction volume, so the glass draw and the colour-correction fix (the weights were zeroed before every render: ClientDLL_Update runs after _Host_RunFrame_Render) are proven by facts only; a gate step for each is owed. `ViewSubject::ViewEntityOverridesTheEye` is the lane's one LOGIC row: its caller is shell_viewrender_client.cpp's SetUpView, and it moves with that port. The engine block's fingerprint moved with the OwnerSeenRule rename (0x027436fc -> 0xb4839a12): a hosted server must be rebuilt with the client.
- **Render budget headroom:** `ClientStandaloneDraw` (4-7 per client frame, the standalone entity shell's per-entity draw) is now the main driver of the `render` step; batching it is C7 render-plan work. `ClientFxSmokeFogPlan` costs a steady 1 per frame and could fold into a per-frame call.
- **IManagedRuntimeClient::MapMaterialReload (slot 8) has no native caller** (2026-10-25, lane M1-D): the spawn's material-reload window moved to managed (`MapMaterialReloadWindow`), and the hooks call the three slots appended after it (`MapMaterialSearchPath`, `MapMaterialMapAccess`, `MapMaterialReloadPlan`). Slot 8, its binding (`ClientRuntimeBinding.MapMaterialReload`, `IClientRuntimeHost.MapMaterialReloadSet`) and its facts in MapMaterialReloadTests retire with the next ABI cleanup's renumber (rows only append between lanes).
- **Two stock rules are C# with no caller yet** (2026-10-25, lane M1-D; their native headers were included by no compiled TU). `SourceSharp.Shared.BulletGlass.PassesThrough` is CBaseEntity::FireBullets' glass rule: wire it, with `HandleShotImpactingGlass`'s re-trace and one-round re-fire (ai_basenpc.cpp), into `Arsenal.FireBullets` when a managed `func_breakable` exists (none does; `func_breakable_surf` is not penetrable). `ClientCollisionSolver.SelfPenetrationSolvedByOwner` is the client CCollisionEvent::ShouldSolvePenetration ragdoll arm's rule: carrying it out needs `IPhysicsConstraintGroup::SolvePenetration` appended to the constraint group's forwarders and the ragdoll's group found by owner; until then every pair goes to the default solver, as the fork's armed build did.
- **The interpolation gate's per-frame reads are unmeasured** (2026-10-25, lane M1-D): `InterpolationGate.Open` asks cl_interpolate by name (`FindConVarValue`, one string a frame), three IVEngineClient forwarders and the net channel row once per frame at RenderStart. A ConVar watch (`IClientConVarSlots.Watch`) would drop the string; the perf pass's.
- **Stock undefined behaviour in the Int and SelectFirstIfNonZero proxies, ported as four-wide.** Added 2026-10-11 (lane c5-proxies, 37ad7f31, ported to f56bc32c). mathproxy.cpp reads a four-wide result through a three-wide `Vector`; in the recorded debug build the fourth float overruns onto the `vecSize` local, so stock writes `SetVecValue( a, 0 )`. The port writes all four components (`Vec4Temp`, `MathProxies.cs`), and the oracle replay names exactly those writes as the allowed disagreements (`TheOverrunBindsAreTheFourWideVectorResultsOnly`). A different compiler or the release build may lay the stack out differently, so the oracle's ruling holds for the recorded build only; re-record in release before relying on it there.

**Owner:** the client port; the unreferenced game logic is the next
dead-code pass's. **Next:** the server browser waits on
C4's matchmaking; the MOTD web view and the physics props' gaps have no
date; re-record the proxy oracle in release before a release build relies
on it; the render budget row is the perf pass's.

## Spectator (observer) port: what stock does that ours does not yet

Added 2026-10-11 from the playermsgs lane (`Observers.cs`, ported forward
from `archive/client-player-msgs` onto main 2eafcb77). The observer port
follows CHL2MP_Player::StartObserverMode (observe only on `spectate` /
`jointeam 1`, never on a death timeout); these parts are named as not
ported in `Observers`' remarks:

- **The team-change rate limit.** The team itself changes now: TF2 Stage 0
  (1c170dac2) made it `Teams.Join`'s (Teams.cs), which stores it through the
  PlayerSetTeam slot and fires `player_team`; the model switch and the
  side-change suicide it does not make are a docs/divergences.md row.
  HL2MP's `TEAM_CHANGE_INTERVAL` (5 s, `m_flNextTeamChangeTime`, set by
  CHL2MP_Player::ChangeTeam) is still not ported (grep at 4c808928c: no
  match in src/server), so team commands have no rate limit. Observers.cs:77
  still says the team change is not ported; that remark is stale.
- **Hide-HUD bits and life state.** Stock's observer has `HIDEHUD_HEALTH`,
  health 1 and `LIFE_DEAD`; ours keeps its own life, so `jointeam` respawns
  it rather than waiting for a button (a DIVERGENCE in `Observers.JoinTeam`).
- **mp_fadetoblack.** CBasePlayer::ClientCommand refuses `spec_mode` under
  it (player.cpp), and the dead player's screen fade; neither is ported.
- **Fog.** CheckObserverSettings copies the target's fog controller (and its
  ground and duck flags) in in-eye; only the view offset is copied.
- **spec_player by SteamID.** UTIL_PlayerByCommandArg's SteamID forms are
  not handled; `#userid`, a unique partial name and a bare userid are.
- **spec_goto** (fixed / roaming, five arguments) is not ported.

**Owner:** the server lane (HL2MP gameplay). **Next:** the rate limit is the
smallest; the rest have no order.

## A rename cannot be driven on a Steam client

Added 2026-10-11 from the playermsgs lane. `player_changename` fires from
`PlayerNames` (CTeamplayRules::ClientSettingsChanged's name half, through
`IManagedClients::OnClientSettingsChanged`). A Steam client's engine puts
its persona name back on every `name` change (measured 2026-10-05: client
2's `name` convar stayed the account's after `name x` and after a direct
convar set), so a real rename cannot be driven from a client on this box.
`cs_sv_rename` (a cheat) drives the same rule with a given name, and the
live `changename` step uses it (the client's event declarations came with
the UI lane's 1c0d494b). A client-driven rename
needs a non-Steam client or a Steam account rename.

**Owner:** the live gate. **Next:** none on this box; `cs_sv_rename` covers
the rule.

## Render plan (C7) follow-ups

Added 2026-10-11 by the C7 lane (client-r-c7, on client-novalve 7289d410).

- **ssmap marks almost no leaf `CONTENTS_TESTFOGVOLUME`**: 2 of `ss_sandbox`'s 2345 leaves, where vvis flags every leaf of every cluster that can see water. The engine's visible-fog-volume query (`R_GetVisibleFogVolume`) only searches from a flagged leaf, so from above any pool it answers -1 and stock never draws a water reflection or refraction view in this tree (measured at the sandbox's mirror pool: -1 from three poses above it, volume 1 with the eye under it). The fix is source-sdk-map-tools' `VisWater` (another repo: ask first). Until then the render plan's above-water views are held only by `RenderPlannerTests`' synthetic inputs, and the water oracle is the under-water fixture.
- **The fog volume is asked up front**: the executor gathers `GetVisibleFogVolume` at RenderView's entry, where stock asks it after `ViewSetupVisEx`. The search through `CONTENTS_TESTFOGVOLUME` leaves reads `r_visframecount` and the frustum, both set mid-frame, so once a map flags those leaves (item above) the two can disagree. Then: split the plan at DrawWorldAndEntities (re-gather the water inputs after the vis setup and plan the rest), and record an above-water fixture.
- **A clip-planed entity under fast clipping** (C7 step 2): stock's `DrawOpaqueRenderable`/`DrawTranslucentRenderable` draw a clipped depth box first when `materials->UsingFastClipping()` and the renderable has a render clip plane (`DrawClippedDepthBox`, a static in viewrender.cpp). The view program's `DrawEntity` pushes the clip plane in the other case and skips the box in this one. No entity in either map answers a clip plane; port the box (its mesh is geometry the executor can carry) before one does.
- **View programs allocate per view** (C7 step 2): `ViewSceneData.FromBytes` copies the scene's leaves and entries into arrays and `ViewDrawProgram` builds a context list per view, a few KB a world view a frame. Measure with `RenderPlanAllocationTests`' method before reusing buffers.
- **A monitor view draws no water** (C7 item 6): the render plan's monitor pass reuses the frame's water inputs, which are the main eye's, so it plans the monitor dry rather than asking GetVisibleFogVolume at the monitor's origin as stock's DrawOneMonitor does. Gather a fog-volume answer per queued monitor when a second camera can look at water.
- **The native second-camera draw is still the fallback** (C7 item 6; re-measured 2026-10-20 at 4c808928c): `cs_cl_render_plan` is gone (grep finds no reader), but ViewCameraService.Pass hands a view to native's queue (`targets.Render`, shell_view_targets.cpp's ShellViewTargets_RenderQueued from the client mode's PreRender, as VIEW_MAIN with pixel visibility off) whenever `PlannedQueue` is unset or refuses (ViewCameraService.cs:83). Next: find whether a running game ever takes that arm; if not, delete the native draw and its IShimViewClient slot.
- **Reflective glass is the one frame the plan hands back** (re-measured 2026-10-20 at 4c808928c): RenderPlanner.Plan returns null when `IsReflectiveGlassInView` answers true (false in this build), and its remarks (RenderPlanner.cs:24-26) say stock's code then draws the frame, but viewrender.cpp left client.so on 2026-10-12 (docs/client_evictions.md). A freeze frame is planned now (FreezeFrame.cs, RenderInputs.FreezeActive). Next, before any map has reflective glass: port the glass view, or say what a null plan draws today.
- **FX_AntlionImpact needs entity world bounds** (lane c5-particles, 2026-10-11 at the port-forward f40c9d78). Stock's gate reads the hit entity's bounding radius and returns without one; the client ABI carries no entity bounds, so `ShardEffects.AntlionImpact` (ported, `AntlionImpactStockTests`) is reached by no caller yet. Needs a client read of an entity's world-space bounds, then the impact route can pass the radius. — re-read 2026-10-20 against the pristine c_impact_effects.cpp (621-655): the radius is NOT needed. Stock computes vWorldMins/vWorldMaxs from `BoundingRadius()` and never reads them (the emitter's box is `spawnOffset ± 32`), so the gate is only "the trace hit an entity", and every hit of UTIL_ImpactTrace has one (the world is an entity). What stops it is the native arm: `ApplyImpactChoice`'s SS_IMPACT_ACT_ANTLION case (shell_impact_effects.cpp) has no managed call to make, and adding one is a new IManagedParticlesClient slot (beside ImpactMetalSpark: start, end, hit-an-entity), so it WAITS FOR THE ABI CLEANUP. Then ShardEffects.AntlionImpact takes a bool for the gate instead of the radius. Unreachable in this mod meanwhile: CHAR_TEX_ANTLION is an antlion model's surface, and neither map has one.
- **Numbered muzzle flashes use the local player as the view entity** (lane c5-particles). `CL_EVENT_MUZZLEFLASH*` (FireObsoleteEvent) puts the world-space flash on the client's view entity; this ABI does not read the engine's view entity, so `IViewEntity` answers the local player and a `point_viewcontrol` camera is not it. — checked 2026-10-20: stock reads `render->GetViewEntity()` (c_baseanimating.cpp:4372), an IVRenderView method no client forwarder row carries yet. The fix is one appended FORWARD row for it on the client side and an `IViewEntity` over it (the index through `IClientStandalones.GetByIndex`, as LocalViewEntity does now), so it WAITS FOR THE ABI CLEANUP. No map here has a point_viewcontrol that fires a numbered muzzle flash, so it is not reachable live.
- **The debris dust's draw order** (lane c5-particles). `ImpactSprays` hands each burst's sheets to the executor in first-use material order; stock's sheets sort within the particle manager by their own insertion. Not seen to differ live; check against a recorded multi-material burst when the sort is next touched. — re-read 2026-10-20 against the pristine particlemgr.cpp: stock draws ONE emitter's materials in first-use order (GetEffectMaterial appends a CEffectMaterial on a material group's first particle, :1033, and RenderEffect walks that list, :348), within one renderable. The port's order inside a sheet is the same, but ImpactSprays starts one effect per sheet (ImpactSprays.cs:139-140), so the order BETWEEN two materials is the leaf system's translucent sort of separate renderables sharing one sort origin, not first use. Not a few lines: the fix is one effect that draws its sheets in list order (or a per-effect sub-order key). Testable then by a planner fact, no recording needed.
- **The debris dust's wind bit** (lane c5-particles). Stock never writes `SimpleParticle::m_iFlags` on a spawn, so a puff is wind-blown whenever the reused pool memory's bit 0 is set; the port spawns 0 (no wind) by decision, and the oracle replays the recorded byte (`SimpleEmitter.cs`, Fixtures/README-smoketrail-stock.md). Decide whether any effect should be wind-blown on purpose. — 2026-10-20: a choice, not a bug (stock's bit is whatever the pool's reused memory held); nothing to fix until someone wants wind.
- **particle/particledefault is missing** from the mounted content too (lane c5-particles, 2026-10-11): the debug line and blood stream temp entities (TeParticles) draw the missing-material checker; the VMT exists only in sourcetest_pak_dir.vpk, which this mod does not mount, so stock's CTEParticleRenderer asked for the same absent material. Ship it or mount it.
- **sprites/flare6 is missing** from this content (lane c5-particles): the large funnel (`LargeFunnel`) draws the missing-material checkerboard, in stock's build and ours alike (side-by-side screenshots taken when the funnel temp entity was wired to LargeFunnel). Ship the material or pick an existing one.
- **7e: the client never sees a managed particle owner move** (measured 2026-10-11 on plain main 20ebb23a, scratch worktree, `tools/7e-particle-sound-gate`, 3 runs: 3 of 3 red, identically). The harness moves the flame's owner 400 units on the server ("the harness actually MOVED the entity" PASS), and the client's copy moves 0.0 units ("the CLIENT saw the move" FAIL), so "the system FOLLOWED the handle" and "the sort origin followed" are NOT REACHED. The sound half of the gate passes. Same result on lane client-r-particles at b4a7d443, so the lane did not cause it. Suspect: the moved origin is not networked to the client copy of a managed entity (or the harness's move bypasses the networked origin).
- **The view-model edge checker artefact** (lane c5-particles): a checker-pattern artefact along the view model's edge, seen in the lane's first-person screenshots. Its measurements were in the lane's uncommitted notes, lost with the worktree (2026-10-06), so it is unattributed: re-take a first-person screenshot pair (effects on and off) before working on it.

**Owner:** the client port (C7); the TESTFOGVOLUME fix is map-tools' (ask
first). **Next:** each row's own next step. The 7e particle-owner row was last
measured on 2026-10-11 and has not been re-run since.

## The render debug views move to Debug.Api routes (later stage)

Added 2026-10-12 by the C7 lane (client-r-c7, on client-novalve 0945d192a), from a user direction relayed by the integrator.

viewdebug.cpp's debug views are to become dev API routes (Debug.Api) rather than convar-driven overlays, at a later stage. Until then they stay as render plan ops (`DebugQuad` .. `PopRenderTargetAndViewport`, kinds 111-121): that is still the path to evicting viewdebug.cpp. The inventory, each with the convars or command that switch it:

- **Wireframe**: `mat_wireframe` (and `vcollide_wireframe` for collision models).
- **Lightmap page**: `mat_showlightmappage` (the engine's convar).
- **Shadow texture**: `cl_drawshadowtexture`, sized by `cl_shadowtextureoverlaysize`.
- **Flashlight depth**: `r_flashlightdrawdepth`.
- **Render-target panels**:
  - water: `mat_showwatertextures` / `mat_wateroverlaysize`;
  - framebuffer copy: `mat_showframebuffertexture` / `mat_framebuffercopyoverlaysize`;
  - camera target: `mat_showcamerarendertarget` / `mat_camerarendertargetoverlaysize`;
  - a named texture: `mat_drawTexture` / `mat_drawTextureScale`.
- **Material over the frame**: `cl_drawmaterial`.
- **Colour views**: `mat_yuv`, `mat_hsv`.
- **Colour test pattern**: `mat_drawColorRamp` (1 = 0..255, 2 = 0..127).
- **Overdraw test**: `cl_overdraw_test`.
- **Screen overlay**: the `r_screenoverlay` command (a material drawn over the frame).
- **3D debug overlays and the line file**: `Draw3DDebugOverlays` and `DrawLineFile` (pointfile), drawn every frame.

Re-checked 2026-10-20 at 4c808928c: RenderOpKind DebugQuad is 111 and
PopRenderTargetAndViewport 121 (src/client/interop/RenderPlanAbi.cs:704,721).
**Owner:** the client port, at a later stage (user direction). **Next:** not
scheduled.

## Pyro vision's facts move to TF2.Tests

Added 2026-10-12 by the C7 lane (client-r-c7), at the integrator's direction.

`src/tf2_descent/tests/PyroVisionTests.cs` holds the facts for TF2.Client's `PyroVision`. The condition this waited on is met: `src/tf2/tests/TF2.Tests.csproj` is on main since TF2 Stage 0 (1c170dac2; re-measured 2026-10-20 at 4c808928c). Next: move the file there, unchanged apart from its namespace and the fixture lookup.

PyroVision's `pyro_*` convars are TF2.Client's own declarations since viewpostprocess.cpp left (`PyroConVars.cs`); it reads them by name.

**Owner:** TF2.

## One managed input system: ClientInputDevices and the UI's InputSystem

Added 2026-10-11 by lane c6-input, on its port onto main 2eafcb77.

The client binds the engine's IInputSystem (`InputSystemVersion001`, the generated
`InputSystemVtbl`) twice, each acquiring it through the app-system factory on its own:

- `ClientInputDevices` (`src/client/interop/Input/ClientInputNative.cs`, the game input's
  `IInputDevices`): raw mouse accumulators, the joystick axes, rumble, the analog pad and
  the Steam Controller (over libsteam_api's flat API).
- `InputSystem` (`src/client/interop/InputSystem.cs`, `IFwdInputSystem`): only
  `ButtonCodeToString`, for the UI's key bindings form (`EngineKeyBindingStore`,
  acquired by `ClientModule.AcquireInputSystem`).

Fold them into one service registered in the client container, with the two contracts as
its faces (or `IFwdInputSystem` as one more member of the input's device surface), so
the vtable is acquired once, a missing input system is reported once, and the UI and the
game input cannot disagree about whether there is one. Pure plumbing, no behaviour change:
the facts to keep green are `InputStockOracleTests` (the device reads in the replay) and the
key bindings form's `ui-keys` live step.

Re-checked 2026-10-20 at 4c808928c: both bindings still name
InputSystemVersion001 (InputSystem.cs:20, ClientInputNative.cs:130).
**Owner:** the client port. **Next:** the fold, as written.

## C7 particles: what is still open

- **Ownerless particle effects do not cross cs_cl_reload** (2026-10-12,
  found by C7 particles phase 2.3). Muzzle flashes and impacts are lost.
  ClientParticleEffects.Restore destroys every ownerless effect, because
  nothing registers a kind in ParticleEffectKinds (the registry is empty).
  Firing an SMG through a reload on ss_sandbox, the native ownerless
  executors went 22 -> 2 on the base and 23 -> 3 with phase 2.3, so this is
  not a regression. Owned emitters (the smoke stack, steam jets, trails)
  cross intact. Fix: register `simple-sheet` (SimpleSheet, PoolEmitters'
  muzzle flashes) and the other kinds in ParticleEffectKinds, and make each
  sheet's state, its emitter rules included, restorable.

Re-checked 2026-10-20 at 4c808928c: ParticleEffectKinds is registered empty
(ClientModule.cs:1168) and nothing calls its Register. **Owner:** the client
port. **Next:** register `simple-sheet` first (Sparks.cs:338 already names it).

## New-style particle effects are handed no camera object

- **Queued (2026-10-19): the new-style effects' camera object.**
  shell_new_particles_client.cpp passes CameraObject 0 (stock: the
  C_BaseEntity at render->GetViewEntity()), so managed NewParticleEffect's
  skip-render test (a first-person view hiding its own control-point entity's
  particles) is never taken. Feeding it SourceSharp_ClientShellFromIndex(
  render->GetViewEntity() ) makes it live, with SkipObject, which is the shell
  pointer since lane ige: a behaviour change that wants its own stock
  recording first.

**Owner:** the client port (particles). **Next:** record stock's skip-render
case, then feed the view entity.

## -assertstack covers one DLL since the client's spew slot is its own (2026-10-12)

`SourceSharp_InstallAssertStacks` (shell_diagnostics.cpp) claims `-assertstack`
once per process through `SOURCESHARP_ASSERTSTACK_CLAIMED`, on the reasoning
that tier0 was shared. Since C9 tier0 both server.so and client.so own their
assert-notify slot (tier0_spew_port.cpp), so only the DLL that initialises first
prints native backtraces. Install per DLL (each has its own slot) and keep the
one-per-process claim only for the terminal line.

Re-checked 2026-10-20 at 4c808928c: the claim is still per process
(shell_diagnostics.cpp:1014-1017). **Owner:** none named (native
diagnostics). **Next:** as written.

## Simplify the usercmd wire format (user direction 2026-10-12; after C9's managed encoder)

Both ends of the usercmd codec are C# (UserCmdCodec: the server's decode, the
client's encode), and its format lives in one place (`UserCmdCodec.Format`, Write
and Read side by side), so a later commit can replace it with whatever makes the
managed code simplest, changing both ends together. Engine-side constraints, observed
in the engine's behaviour, to respect before changing it:

- clc_Move only FRAMES the payload: it sends the bit count as a 16-bit word
  (`WriteWord( m_nLength )`, so at most 65,535 bits a packet) and copies the bits;
  `CLC_Move::ReadFromBuffer` skips them. The server engine hands the buffer to
  ProcessUsercmds and disconnects the client unless the DLL consumed EXACTLY
  m_nLength bits. The client's move buffer is MAX_CMD_BUFFER bytes.
- Demos: CDemoRecorder::RecordUserInput asks the client DLL to encode one command
  (EncodeUserCmdToBuffer) into a 256-byte buffer and stores its bytes;
  playback hands them back to DecodeUserCmdFromBuffer. Nothing else reads them, but a
  format change makes demos recorded under the old format undecodable (their
  usercmds only; the rest of the demo is unaffected) -- decide whether to keep a
  reader for the stock format for those.
- SourceTV/HLTV: CHLTVClient::ProcessMove ignores moves; no relay of usercmds.
- Nothing in the engine parses the usercmd bits themselves. Verify against
  engine.so before the change: search it for any other
  caller of the three IBaseClientDLL slots and of ProcessUsercmds.

**Owner:** the client port, at the user's direction; no date. **Next:** the
engine.so search above, then the demo-reader decision.

## Perf pass: fold ViewEffects CalcShake/FadeParams into the per-frame crossing (2026-10-12)

Batch 4's daily gate measured render at 42.188 crossings a frame against a
42.138 ceiling, likely the two ViewEffects crossings the view makes about once
a frame each (ClientViewEffectsCalcShake, ClientViewEffectsFadeParams; C7,
271265e15). The user ruled that crossing-budget rises from the port are
allowed and that a perf pass runs once the port is complete. The fold then:
carry CalcShake's offset and roll and GetFadeParams' colour in an existing
per-frame crossing (ViewSetUp's out, or RenderFrame's), so screen effects cost
no crossing of their own on a normal frame; Fade and Reset stay their own rare
crossings. Measure the render row before and after, and re-record it down.

Re-checked 2026-10-20 at 4c808928c: both crossings exist
(src/abi/Crossings.cs:823, :825). **Owner:** the perf pass, after the port.

## QueueOverlayRenderView is not shipped (C7 step 3, 2026-10-12)

CViewRender::QueueOverlayRenderView kept a view that stock's RenderView then
drew after the 3D scene (before the 2D half), its FOV widened to the view's
aspect and with bloom off. Nothing in this build calls it (no caller in our
code or the fork's compiled client; the engine does not reach IViewRender),
so CShellViewRender's says SourceSharp_NotShipped instead of keeping a view
nothing would draw. When something needs it: an overlay-view arm in
RenderPlanner (the view in RenderInputs, a nested RenderView's ops after
ScreenSpaceEffects), held to viewrender.cpp's order.

**Owner:** the client port. **Next:** none until something calls it
(shell_viewrender_client.cpp:229 says NotShipped).

## Client flashlight: ported (C7 cluster 12); what it left open

Added 2026-10-12 at main c46cb63eb (lane c6-input, C9 survey; integrator
ruling); ported 2026-10-18 (lane C7 flashlight, on main e070a68e7).

flashlighteffect.cpp left client.so: the local player's flashlight is
SourceSharp.Client.Flashlight (docs/client_evictions.md has the row,
docs/client-novalve-rulings.tsv the `ported` reason). Left open, each small:

- **Other players' flashlights.** Stock's C_BaseEntity::CreateLightEffects
  gives a non-local entity with EF_DIMLIGHT a dynamic light at its origin.
  Only the local player's projected texture is ported; another player with
  the light on shows nothing on this client.
- **The debris arm of the trace filter cannot fire.** A client shell's
  collision group is not networked (C_StandaloneEntity answers NONE), so
  stock's skip of COLLISION_GROUP_DEBRIS / INTERACTIVE_DEBRIS never matches.
  It fires once the collision group reaches the client.
- **CBasePlayer::SetFlashlightEnabled** (the server's per-player "may use the
  flashlight" switch) is not ported; cs_sv_allow_flashlight is the one gate.

**Owner:** the client port; SetFlashlightEnabled is the server lane's.
**Next:** other players' lights, which need EF_DIMLIGHT's dynamic light on a
non-local entity.

## Client C9 (tier libraries): what is still open

Added 2026-10-12 at client-r-input efb6197b7 on main 432d2e9f1 (lane c6-input;
the lane stopped here on the user's instruction). Measured with lld
`--why-extract` on a relink of client.so (debug), `nm -D -u` over
out/client.so, and `tools/client-novalve-census`.

- **Lip sync wiring** (2026-10-18, lane t1): SourceSharp.Client.LipSync (Visemes, Sentence) is
  held to stock by LipSyncOracleTests but has no caller: CBaseFlex is in neither build. When a
  flexed client entity exists, it needs engine GetSentence / GetSentenceLength through the
  client FORWARD row (plan_engine_forwarders.md), a native reader of the engine's CSentence
  (runtime phonemes, emphasis samples), the entity's CMouthInfo voice sources, the .vfe files,
  and the phonemedelay/phonemefilter/phonemesnap ConVars (VisemeSettings.Stock holds their defaults).
  Then the ruling's live PROOF (a mouth moving) can be run.
- **libss_particles.so still needs libtier0 and libvstdlib** (client.so needs
  neither since fa6353435). The client's binding of vstdlib's thread pool is
  held by tier_bindings_client_test alone: no live run reaches a client job on
  that pool.

Re-measured 2026-10-20 at 4c808928c: `readelf -d` on
bin/debug/bin/linux64/libss_particles.so lists libtier0.so and libvstdlib.so;
client.so lists neither, and tools/client-novalve-census reads every
imports: row 0. **Owner:** the client port (lip sync); the particles lane
(libss_particles.so's needs). **Next:** none for lip sync until a flexed
client entity exists.

## Live gate: chat's second client exits during connect (flaky, 2026-10-12)

The daily gate at main 891126a2c ruled `chat` FLAKY: red in 1 of 2 night
passes and 0 of 3 confirm passes. The second game client exited during
`connect 127.0.0.1:<port>` ("the game process ... has exited"); the gate log
holds no backtrace or exit code for it, and the next step (spectate) started
a client 2 normally. Nothing in the batch touched chat or the second client's
connect. To do: capture the client's exit status and console on a red, and
measure a rate with `tools/live-gate --steps join,chat --passes 10`.

**Owner:** the live gate. **Next:** as written; no rate has been taken
since 2026-10-12.

## Live gate: no step covers screen shakes and fades

ViewEffects (shakes, fades) moved to C# and its native stand-in left
(d0d288632); the only live evidence is the probe session
tools/probes/sessions/c7-shake-fade.steps. Add a live-gate step that fires an
env_shake and an env_fade and checks the drawn fade colour and the view's
offset and roll, with a control frame before them.

Re-checked 2026-10-20 at 4c808928c: tools/live-gate's STEPS and RUN_ORDER
name no shake or fade step. **Owner:** the live gate.

## sdk-links follow-ups (from the 2026-10-19 reconciliation)

- **sdk-links/windows.txt** still lists bitmap.lib, tier2.lib, tier3.lib and
  vtf.lib. No CMake target on any platform links sdk::bitmap, sdk::tier2,
  sdk::tier3 or sdk::vtf, so they look dead. Only GitHub CI verifies the
  Windows build: delete them in a commit that CI then checks.

Re-checked 2026-10-20 at 4c808928c: sdk-links/windows.txt:13,19,20,22 still
list the four, and no CMakeLists.txt or cmake/ file names sdk::bitmap,
sdk::tier2, sdk::tier3 or sdk::vtf. **Owner:** none named (build; CI checks
it). **Next:** delete the four lines and let CI's Windows job prove it.

## Belt riding is not predicted; trigger_push leftovers (2026-10-20)

Found by the client bugs lane at 44b552294 (debug, ss_sandbox, `make map`).
- **trigger_push lifts a player: fixed 2026-10-20.** Root cause: a velocity
  impulse on a player went to its physics object, the shadow, which the next
  move overwrites (the player's velocity is the movement record's since T4g).
  EngineSide.EntityApplyVelocityImpulse now asks the player movement route
  first, and TriggerPush is stock's CTriggerPush::Touch arm for arm: not
  solid, MOVETYPE_PUSH/NONE, parented or noclip is skipped; push-once
  impulses and removes; VPHYSICS gets ApplyForceCenter(speed*dir*100*
  frametime) every tick; a player gets AddBaseVelocity(speed*dir) on entry
  and after each of their commands (stock's ProcessImpacts, through
  PlayerCommandTouches), never per tick: pushed per tick, the ticks between
  one batch's commands added the push up two or three times and the
  commands it was not renewed for folded it into the velocity for good
  (dm_lockdown's fan_wind flung the live gate's player). Left as
  divergences: (1) a non-player that is not VPHYSICS is not pushed, where
  stock gives it base velocity: only a player has a base-velocity channel
  here (before the fix its impulse reached no object either); (2) the
  volume's `wait` gates entry, so a toucher entering within `wait` of the
  last entry is not pushed unless the push think is already running, where
  stock's Touch has no wait. The HL2_DLL ladder hack does not apply: no
  server TU is built with HL2_DLL.
- **The sandbox's `forces` push cannot lift a player, in stock either.**
  Stock's StartGravity (gamemovement.cpp) folds a vertical base velocity
  into the velocity as base.z * frametime per command and zeroes it, so an
  upward push only beats gravity above sv_gravity (600): forces_push's 200
  holds a player about a unit off the floor (the push's unstick), measured
  at 98db7cb6f as z 10.1-12.4 over a floor at 9.25 (9.25 flat at
  c5c8df3d2). Raise its speed in Sandbox.cs if the station should show a
  lift.
- **A volume switched on ignores who is already in it.** TriggerVolume
  drops OnTouch while disabled and keeps no occupant it was off for, and
  Enable re-scans nothing, so a player standing in a trigger when it is
  enabled is not inside until they leave and come back. Stock's
  CBaseTrigger::Enable calls PhysicsTouchTriggers, which touches them at
  once. Found by the live gate's trigger step (2026-10-20), which crosses
  into dm_lockdown's fan_wind after enabling it for that reason.
- **Riders push per tick (suspected, not measured).** Riders.Ride hands a
  belt's rider AddBaseVelocity every tick, the cadence trigger_push was
  found to multiply and fold into the velocity above; stock's conveyor
  reaches the player per command. It may be part of the belt mispredictions
  below.
- **Belt prediction:** on the func_conveyor at (320, -2040), the client
  mispredicts 82 times in 4 s with the FL_BASEVELOCITY fix (b16adebd4) and 81
  with the old bit, and 0 standing beside it. The client does not replay
  the belt's push; stock's client does not either (prediction.cpp's
  CheckMovingGround conveyor arm is under `#if 0`).

**Owner:** the server lane (the trigger_push divergences); the client port
(belt prediction, a decision beyond stock). **Next:** the two trigger_push
divergences need a non-player base velocity and a wait-free touch; porting
the `#if 0` arm needs the ground entity's conveyor speed on the client, and
a ruling.

## Entity entry 264 sometimes undrawn after dm_lockdown's second map load: fixed 2026-10-20

Not a render bug, and not the reload's. Entry 264 is a weapon_shotgun pickup at
(-3722, 4082, 49.8), and the player had TAKEN it: the probe sessions teleport
the player to pose 1 (-4116, 4600) through `/player/teleport`, and the
standalone teleport (SourceSharp_EntityTeleport) moved through SetAbsOrigin,
whose funnel sweeps the trigger touch from the OLD origin (engine->SolidMoved
with a previous origin is a ray). From two of dm_lockdown's ten spawn points,
(-2739, 2563) and (-2932, 3218), the straight line to pose 1 passes within the
pickup's box (closest approach 30 and 29 units on an axis, against 32), so the
teleport collected the shotgun and its respawn hid it (EF_NODRAW for 20 s).
The second map load was only a second random spawn. Stock's TeleportEntity
moves by UTIL_SetOrigin with bFireTriggers false and touches nothing.

Measured at 4c808928c (debug, `bg-264c.steps`: map, pose 1, trace, map again,
pose 1, trace; 10 runs = 20 loads): entry 264 undrawn in 6 of 20 loads, every
one of the 6 after a spawn at one of those two points and none of the 14
others; the console said `#1 took weapon_shotgun` after the pose-1 teleport,
and the policy pass decided it with nodraw=True. Reproduced on demand by
teleporting to (-2932, 3218) and then to pose 1.

Fixed by CStandaloneEntity::TeleportTo (shell_standalone_base.cpp): the
teleport writes origin and angles once and tells the funnel not to touch
triggers; the entity's next move asks (a player's next command touches where it
stands). Facts: EntityBaseCluster.AStandaloneTeleportTouchesNoTriggerOnTheWay
(red on the pre-fix sources, green after). Live, after: the same crossing
teleport took nothing and pose 1's trace drew entity 264; the control,
a teleport onto the shotgun, still took it; live-gate pickup, move, damage,
projectile and spectate pass (2 passes).
Not carried: children of a teleported entity are moved by the hierarchy's own
setters and still sweep from their old origins (stock teleports them without
triggers too); no map here parents anything to a teleported entity.

**Owner:** the client port (C7 shadow and leaf). **Next:** as written.

## bitvec.h's two tables leave with the native PVS/PAS filters (user ruling 2026-10-20)

`firstBitLUT` and `g_StartMask` (bitvec.h, ruled CONST) are emitted into
server.so only by `ss::CRecipientFilter::AddPlayersFromBitMask`
(ss_recipient_filter.cpp), called from `AddRecipientsByPVS/PAS`
(shell_recipientfilter.cpp), which `ss::CPVSFilter`/`CPASFilter` use in
shell_te_forwards.cpp (397, 470, 481) and server_util_library.cpp (210). When
those callers move to managed (RecipientFilter.cs already ports the bit walk),
delete AddPlayersFromBitMask and the tables go with it; check with
`make globals-census` that the two ledger rows vanished. No fork edit.

**Owner:** the server lane (T6).

## Rulings and divergences recorded only here (not open work)

Kept because nothing else records them. Each was ruled or measured as
stated; none asks for work unless a new ruling does.

- **Voice bans are saved immediately** (lane ui, C3; kept, user 2026-10-05). Stock `CVoiceStatus` wrote `voice_ban.dt` once, from its destructor at client shutdown (`m_BanMgr.SaveState`, voice_status.cpp:141), so a crash lost every mute made that session. `VoiceStatus.ToggleBlocked` writes the file on each toggle instead; the bytes are stock's (`VoiceBanList.Save`, held to the CVoiceBanMgr oracle). Recorded as a divergence to keep; revisit only if a write per click ever shows up in a profile.
- **The kill feed outlines the local player's lines** (lane ui, C3; kept, user 2026-10-05). Stock HL2MP's CHudDeathNotice did not mark them (TF2's feed does); `screens/KillFeed.cs` outlines a line the local player is in with the accent. A deliberate departure from stock, not a port gap.
- **The second camera's overview omits entities outside the player's PVS** — a known limitation, by user ruling 2026-10-11 (option 4 of the PVS question, plan_client_novalve C7.6): no PVS change at all, the server adds no camera origin, so `cs_cl_overview 2` shows only what the player's own PVS sends. Measured at 74d9ee19 + lane camproto: of 37 server entities in the overview's box, 20 were absent on the client (props in the next rooms). Not to be fixed without a new ruling.
- **The debug camera** (`cs_cl_overview`, lane camproto, user ruling 2026-10-11): a debug tool, not gameplay. Cheat convars, off without `sv_cheats 1`, HUD panel only while on; driven by `/client/{slot}/viewcam[?mode=0|1|2]` (`tools/debug-api client 1 'viewcam?mode=1'`) for screenshots and debugging. Plan C7.6 has the measurements; C7 keeps its render-target mechanism generic.
- **Studio models draw black at dm_lockdown's spawn: the map's own lighting, not a regression** (investigated 2026-10-11, lane camproto). The crowbar viewmodel and the debug camera's third-person body are black silhouettes at the spawn corridor (-2651 4225, eye z 192.8, yaw -90). The ENGINE's own model lighting there is near zero: `engine->ComputeLighting` (the light cache over the BSP's leaf ambient and world lights, which no client code feeds) answers 0.004 at the eye with all six ambient-cube faces under 0.01, and under 0.015 within 64 units, against 0.67-0.97 in the lit room at -3079.5 4656.5 where the crowbar draws lit (the pose of the 2026-10-05 UI screenshots, which draws the same today). Walking toward the corridor lamp, the crowbar brightens smoothly with distance to it (red 0.9 at y 4225 to 23.5 at y 4000), so the draw path takes the light the engine offers. Not a regression either: a pixel probe (crowbar luminance under mat_fullbright 0 over 1, same pose) reads 0.008 at 797a129b and 0.065 at fbd904ed, black at both. Open question, if anyone wants it closed: whether stock HL2DM shows the same there (no stock hl2mp binaries are installed to run the comparison; the half-life 2 deathmatch dir holds content only).
- **`mat_stub 1` crashes the game, in stock and in the port alike.** Added 2026-10-11 (lane c5-proxies, measured at its 7ef99f1b and ported to f56bc32c). `CMatStubHandler` (now in `shell_material_proxy_client.cpp`, the swap decided by the managed `MatStubConVar` through `IManagedMaterialProxyClient::MatStubEnabled`) carries out stock's swap exactly; setting it to 1 exits the game, and the stock build (cl_mat_stub.cpp restored at d835d05c, same steps) exits the same way. Stock's behaviour, carried, not fixed; the cause in the engine's stub material system is not yet found.
- **The old-style particle draw's wireframe pass reuses the first pass's
  quads** (C7 particles phase 2.3). Stock asked the sim to render twice under
  mat_wireframe; the managed sims' draws are pure, so the second pass draws
  the same quads with debug/debugparticlewireframe and hands its keys to a
  second sort, as stock did. The old effects' cl_particle_stats counts
  (StatsOldParticleEffectDrawn) are not kept; phase 4 (b1350d3c6) moved the
  stats commands to ParticleCommands.cs, empty as stock's are in this tree.

## Closed entries removed 2026-10-20 that code or docs still cite

These entries were closed and left this file on 2026-10-20. Their full text,
with the measurements and rulings nothing else holds, is in
`git show 4c808928c:docs/todo.md`. Each line names the heading, who cites it,
and where its record lives now.

- **Managed code calls the engine vtables directly: ShimSlots retired** (the
  "router retirement": tools/shim-areas, src/abi/AssemblyInfo.cs,
  ShimAreaTypedCallsTests, EngineApiAreasTests, convar_replicated_flag_test):
  closed 2026-10-07.
- **Capture native console output in process** (the "console capture item":
  console_display_forward.*, Console*Binding, ConsoleTail, DebugApiServer,
  sourcesharp_gamesystem.cpp, ServerModule, ClientModule, UiComposition, the
  CMakeLists comments and their tests): closed 2026-10-09.
- **Concurrent launches stall in start-up: the KWin rule** (tools/kwin-bg-rule,
  tools/local_launch.py, tools/concurrent-start, Launcher.cs): fixed 2026-10-05;
  the before/after table of 5 x 6 games is in the old text.
- **Re-measure test timings on a quiet machine** (tools/measure-timings): the
  figures are docs/measurements/timings-quiet-20261007.tsv.
- **A game sent SIGTERM exits with 139** (exit_watch.cpp, DebugApiServer.cs,
  DebugApiExitRecordTests, live-gate's sigterm-shutdown step): fixed 2026-10-06.
- **Entity fields set in OnSpawn are lost across a hot reload**
  (OnSpawnFieldReloadTests): fixed 2026-10-06.
- **tools/concurrent-start checks sockets, not joins** (tools/concurrent-start):
  fixed 2026-10-06.
- **Client port follow-ups, closed rows**: the scoreboard's max players
  (ScoreboardMaxPlayersTests, UiLoaderTests, UiHostKit, MenuSources.cs), the
  console window's engine prints (ConsoleUiEngineFeedTests, MenuSources.cs), the
  dead tempent pool (TempEntPoolRetirementTests), the serialised ABI cleanup
  (docs/abi-retirements.md, 2026-10-20), the PostRender stage (its op and walk
  went in that pass), the crossbow bolt's ragdoll pin (e9b0c2dee; divergence:
  of two bodies on one line the nearer is pinned), ss_bolt's BoltImpact, the
  snowfall tolerance and the client trace on props: closed 2026-10-08..19.
- **Live-gate step fixes**: impact-damage's fall origin (tools/live-gate:2947),
  hot-reload's door (tools/live-gate:5808), spectate's ragdoll lifetime
  (tools/live-gate:5235), prop-hurts-player's debris square: closed
  2026-10-12..18.
- **Lane C leftovers** (PhysicsFrameTests, PhysicsCollisionImpactsTests,
  EntityPhysicsMotionTests): closed 2026-10-18.
- **Physics paths never measured in release** (CostMeter.cs,
  PhysicsCostReport.cs): closed 2026-10-19 (cost meters d495fae85); the release
  figures are in the old text.
- **C7 particles: where the work stopped** (the phase 3 spike's threading
  measurement and phase 3's divergences: iparticlelib.h,
  shell_managed_particles.cpp, Fixtures/README-particlemgr-new-stock.md):
  phases 3 and 4 are d0ec3ec8e and b1350d3c6.
- **C_StandaloneEntity's six IGameEntity slots** (shell_standalone_client.cpp,
  docs/client-shell.md 2.2): closed 2026-10-19, e93170fa1; the measurements and
  the table of fork-declared signatures kept with IGameEntity are in the old
  text.
- **The native IGameResources (g_PR)** (docs/client_evictions.md): retired
  2026-10-11; the eviction row holds the reason.
- **UiAllocationTests' negative control** (UiAllocationTests.cs): done
  2026-10-19; the control formats from 1000 up.
- **Client flashlight: hot reload forgot the shadow handle**
  (LocalPlayerFlashlight.cs, ShadowManagerFactsTests): closed 2026-10-19,
  docs/plans/c7_shadow_manager.md.
- **The client save codec** (a4254ee42) and **the client temp entity tables**
  (5f1fe84b7): the divergences each kept are listed in the old text.
- **Client C9 tier0 and vstdlib** (fa6353435): the user's 2026-10-18 ruling that
  client.so's VPROF nodes run against its own never-enabled profile is in the
  old text.
