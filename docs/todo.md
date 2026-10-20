# To do

Work that has been decided on but has no plan or owner yet. Each item says
why it's here and what is known, with the commit it was measured at.


## live-gate ui-teammenu: the team menu's click sends no jointeam the server hears

At main 760ca80fc (2026-10-19, both passes) the step's three rounds open the
UI's team menu, take the mouse and draw AUTO ASSIGN, then fail "the click sent
jointeam 0 and the server heard it"; the menu closes and gives the mouse back.
It is the one step the full live gate fails there, and the daily gate's reports
at 43171f0c1 (2026-10-16..18) failed the same three rows. TF2 Stage 0's 0.5
work rerouted jointeam (ITeams, IJoinTeamPolicy, IClientCommandHook), which is
the first place to look. Repro: make live-gate LIVE_ARGS='--steps ui-teammenu'.


## live-gate's fault check does not see across a hot reload

Every step fails on a managed EXCEPTION IN or `could not acquire "` line in
its log (c0902624e). The hot-reload step restarts the managed log, so the
step's window is re-read from the new log's start and reported "NOT ALL": the
lines between the step's start and the reload are not looked at (500 lines
read in each pass at 760ca80fc). Reading the native console feed (/console,
epoch-aware, which ConsoleFeed already pages) beside the managed log, or
marking the log before the reload, would close it.


## The client systems' PostRender stage reaches no body

Added 2026-10-19 with SFM's removal (user ruling: "SFM is dead, remove it").
CToolFrameworkClient was the one kClientSystems[] row with a PostRender body;
its row left with toolframework_client.cpp, so
SourceSharp_ClientSystemsPostRender walks the table and calls nothing. The
render plan still emits RENDER_OP_ClientSystemsPostRender (RenderOpKind 15),
and the frozen render recordings name it, so the op and the walk stayed.
Retiring both is one plan-op change plus the recordings' re-freeze.

## The tools-off stand-ins leave with their last Valve importer

shell_tools_off_client.cpp (2026-10-19) defines ToolsEnabled and
ToolFramework_RecordMaterialParams only because clientshadowmgr.cpp, a fork
TU still compiled into client.so, names them (nm -u over its object,
2026-10-18). `clienttools` and ToolFramework_PostToolMessage left with
flashlighteffect.cpp (C7 cluster 12, 2026-10-18), their only importer. When
clientshadowmgr.cpp leaves, delete the file, its test, its (x) row in
docs/shim-reasons.md and the toolframework_client.h /
toolframework/itoolentity.h links in sdk-links/client.txt.


## physics_debug.cpp's commands, managed: two small follow-ups (closed)

Added 2026-10-18 when the six commands moved to PhysicsDebugCommands.cs
(lane rebased onto main f707d5b8c).

- `physics_report_active` keeps native's name fallback (the entity's name,
  else its classname); stock's CallbackReport fell back to the MODEL name.
  Switching is one line in PhysicsReportActiveCommand if the model name is
  wanted, and changes the command's output.
- `PhysicsWorldOwner` keeps its `LIVE-API` ruling in
  tools/physics-scope/rulings.tsv, but `nm -u` over server_sourcesharp's
  objects finds no importer since physics_debug.o left: only physics.cpp's
  own SourceSharp_PhysicsUpdateOwners reaches it, through physicsowner.h's
  inline PhysicsOwnerOf. Re-classify it (5a-physics-classify.py) the next
  time the scope's totals are re-taken.

**Closed 2026-10-18:** `physics_report_active` names an unnamed owner by its
model name, as stock's CallbackReport (PhysicsActiveReport.DebugName over
IEngineEntityRender.GetModelName; a classname-fallback mutation turned
Report_names_an_unnamed_owner_by_its_model_as_stock_CallbackReport red).
`PhysicsWorldOwner` is ruled `BOUNDARY` (nm -C over server.so's 119 link-line
objects, farm/debug at 8605b6ea1: defined in physics.cpp.o, undefined in none);
LIVE-API 3/34 -> 2/28, BOUNDARY 0/0 -> 1/6, total 10/135, and
PhysicsMoveScope.EachVerdictRowIsTheClassifiersCount now holds every row of
docs/physics-move-scope.md section 2 to the classifier (4 failures with the
ruling moved and the document not).


## PhysicsMotionControllersTests.EachKindsSimulateAllocatesNothing failed once under a full managed-server leg (closed)

At 52d8eb6a4 (main 82d2df06c plus a comment-only cleanup, 2026-10-18), the
`(kind: Medium, expected: GlobalAcceleration)` case failed once in
`make test-full`'s managed-server leg: Assert.Equal, values differ. It passed
3 of 3 alone (`make managed-test FILTER='FullyQualifiedName~PhysicsMotionControllersTests'`,
24 facts), and the commit under test changes no managed code. This is the same
shape as the NameMatchesTests entry below: an allocation read around a
simulate that a JIT tier-up or a parallel leg's GC can charge. Measure the rate
over repeated legs, then warm until two consecutive simulates allocate nothing.
The fact is the client physics lane's (8ab15ceaf).

**Closed 2026-10-18:** both allocation facts measure through SteadyAllocation.LeastAllocated (src/test/common): warmed three times, then the fewest bytes across five measured calls, so a JIT tier-up inside one call no longer charges it. Mutation proof: a per-call `new object[1]` in MotionSimulation's SimulateState turned all 4 EachKindsSimulateAllocatesNothing cases red, and one in NameMatches.MoveNext turned AWalkAllocatesNothing red; both green after the restore.


## The client tail's four Studio*Count fields are unwritten (closed)

Added 2026-10-18 (studio.cpp's eviction, studio 3). The render policy reads
a studio model's sequence, pose-parameter, bone-controller and IK-chain
counts from the side's managed header (ClientRenderPolicy.ReadModelCounts),
so `EngineBlockClientTail.StudioSequenceCount` .. `StudioIkChainCount`
(offsets 196..208) are neither written nor read. They were left in place
because removing them moves PolicyInputs, PolicyOutputs and HitboxSet and
shrinks the tail (224 bytes); drop them at the next re-layout of the block,
with EngineBlockTests' and engine_block_layout_test's offset facts.
— closed 2026-10-18 (lane claude/tail-skeleton-standalone): the four words
left EngineBlock.cs and the regenerated header; PolicyInputs, PolicyOutputs
and HitboxSet moved to 196, 200 and 204, the tail is 208 bytes and the block
488, and the fingerprint is 0x027436fc. ClientRenderPolicyTests'
TheBlocksOwnCountFieldsAreNotRead went with its subject.


## NameMatchesTests.AWalkAllocatesNothing failed once under a full managed-server leg (closed)
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

## The sound emit path in C# (plan_server_native_v2 T4a step 1): follow-ups

- **env_microphone's OnSoundPlayed.** Stock's `EmitSoundByHandle` let
  `CEnvMicrophone::OnSoundPlayed` hear (and swallow) every script emit; neither
  the native emitter nor the managed `SoundEmitter` that replaced it calls one.
  The emitter is managed now, so `EnvMicrophone` can be offered each emit there.
- **The `_DEBUG` "Sound %s was not precached" line.** Stock printed it from
  the emit in debug builds; the managed emitter drops it. The forwarder it
  needs, `IEngineSound::IsSoundPrecached`, exists since T4a's precache path
  (`IVtableEngineSoundServer.IsSoundPrecached`); `ISoundEmitterEngine` would
  carry it to the emit.
- **sound_emitter.cpp's client emit path is native** (moved to
  src/client/native 2026-10-19, same link position). **Closed 2026-10-19:**
  the emit path is the shared `SoundEmitterCore` (src/shared/managed), the
  server's `SoundEmitter` and the client's `ClientSoundEmitter`
  (SourceSharp.Client.Sounds) its two sides' arms, held to pristine
  SoundEmitterSystem.cpp by `ClientSoundEmitterOracleTests`
  (soundemitter_client_oracle_test's golden); the system's stages are
  `ClientSoundEmitterSystem`'s; sound_emitter.cpp left client.so and the
  repository (logic-lines 8804 -> 8229). Its follow-ups are the next section.

- **DONE 2026-10-19: the client save codec is managed** (the client save
  codec lane). IBaseClientDLL's SaveInit/SaveWriteFields/SaveReadFields forward
  into IManagedSaveRestoreClient, to the shared SaveBlockSession over
  SaveBlockCodec (src/shared/interop, the server's too); shell_savegame.cpp is
  deleted and shell_savegame.h (src/server/native) holds only the server's
  ShellSave_LoadGameNeedsPlayer. The codec is held byte for byte to stock's
  CSave::WriteFields / CRestore::ReadFields by SaveCodecOracleTests over
  savegame-stock.tsv (savegame_codec_oracle_test cuts the field path out of the
  pristine saverestore.cpp). Porting it to that oracle changed four behaviours
  on both sides, now stock's: a field with no data is not written
  (ShouldSaveField), positions are relative to GetLandmark both ways, a record
  of another width fills what both have (ReadSimple), and EmptyFields empties
  fieldSize x FIELD_SIZE of every declared type. Live, dm_lockdown at
  -maxplayers 1: 76 DECALLIST blocks out and back through the managed codec per
  save and load, and the client state file's decal section byte-identical
  (sha256 6e3c63496f180a46) before a load, after it, and after a second map
  load. DIVERGENCES KEPT, none reachable from the engine's six save maps:
  - Six field types (INTEGER, SHORT, VECTOR, TIME and POSITION_VECTOR one
    element each, CHARACTER arrays). A field of another type that has data is omitted, counted
    and named in the warning; stock would write it.
  - FTYPEDESC_SAVE is not read: every described field is a saved one (the
    engine's maps are all DEFINE_FIELD / DEFINE_ARRAY).
  - FIELD_EMBEDDED and FIELD_CUSTOM are neither written nor emptied (their own
    description or ops are not carried).
  - Hostile input: a truncated or corrupt block is refused whole and the struct
    left as it was (stock reads what it can); a zero-width record for a known
    field reads nothing (stock's ReadSimple reads the field's whole width from
    beyond the record); a token out of range resolves to no name.
  - FindField's rotating search cookie is not kept: the first case-blind match
    from the start, the same answer for a map's unique names.
- **No runtime decal reached the client state file in the live save run**
  (2026-10-19, the client save codec lane): with sv_cheats 1, an SMG burst at
  the floor and an env_explosion's scorch (spawnflags 1) left the engine's decal
  list at the map's own 76 (ClientDecalTranslate 15, ClientSaveWriteFields 76
  before and after). Either this build's impact and scorch decals are not
  engine decals or they are not placed; until that is answered the live save
  check cannot tell a reapplied decal from one the map placed again.

## Temp entities and datatables in C# (plan_server_native_v2 T4b): what stayed native, and why

Measured at the lane's tip (2026-10-19). The server plans its temp entity
tables in C# (TePropPlanner, TeSchemaRegistry, SendPropPlan, ServerTempEntEngine)
and plays them through the PlaybackTempEntity forwarder; native stores the plan
(shell_tempents_store.cpp) because the engine walks SendProp/SendTable/ServerClass
by layout. What is left, each with its exit:

- **shell_te_forwards.cpp's TE_* forwards, DispatchEffect and ss::Effects()**,
  and **SourceSharpTe_Forward** (shell_tempents.cpp), the round trip that packs
  a native caller's arguments in managed and sends them with the caller's own
  IRecipientFilter. They stay while their native callers do: shell_worldquery.cpp
  (TE_Explosion, TE_ProjectDecal, TE_BSPDecal, TE_Bubbles, TE_LargeFunnel,
  DispatchEffect "watersplash"/"Impact"), world_adapter.cpp (TE_Decal,
  TE_WorldDecal, DispatchEffect "Impact"), util_shared_library.cpp (Tracer,
  bloodimpact, ss::Effects()->Sparks), server_util_library.cpp (TE_BloodStream,
  ss::Effects()->Smoke), particle_manifest.cpp ("ParticleEffect") and
  shell_absent.cpp ("bloodspray"). Worth checking first: IShimEffectsServer's
  ExplosionEffect, FunnelEffect, ImpactEffect, TracerEffect and SplashEffect
  slots are called only by EngineSide, and the server's registered IEngineEffects
  (EffectsOverTempEnts) overrides all five in managed; if nothing holds the raw
  binding, the slots and their shell_worldquery.cpp bodies are dead, and with
  them TE_Explosion's and TE_LargeFunnel's callers (T4d's file).
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
- **The client half is done (2026-10-19, the client temp entity lane)**: the
  client's tables are planned by ClientTempEntEngine (TeSchemaRegistry,
  TePropPlanner, RecvPropPlan) and stored through
  IShimTempEntBackendsClient::TempEntBuildClient; tempent_schema.h and the
  native registration path are deleted. What stays native on the client, each
  for the engine: the receiving shell (CSourceSharpTempEnt, the
  IClientNetworkable the engine decodes into and calls PostDataUpdate on), its
  per-handle CreateEventFn table (the function carries no context), the store
  (shell_tempents_store_client.cpp: RecvProp[] -> RecvTable -> ClientClass),
  cs_cl_te_dump (prints the engine's view) and the client SourceSharpTe_Forward
  (a native TE_*() call's arguments to managed). Changed on purpose, none of it
  stock's: the native registry's stock-table checks are gone (client.so holds no
  DT_TE*/DT_BaseBeam/DT_BaseTempEntity/DT_EffectData table at ab8f56ef2, so the
  external base resolver, the "a native RecvTable is still named" shadow
  refusal, the ClientClass-name refusal and the dump's TESTOCK lines had nothing
  to answer); the reload's reshape warning names the new shape only (native no
  longer holds the old description); the close line is the server's ("45
  table(s), 43 ClientClass(es)"); the dump's TE| line is
  table|i|name|type|offset|flags|nested (the plan columns went with the native
  plan). A ClientClass name collision with a non-temp-entity class (CWorld, the
  game rules, the StdEnt tiers) is not refused; no temp entity name can take
  one of those today.

## Entity API and world in C# (plan_server_native_v2 T4d): what stayed native, and why

Measured at the lane's tip (2026-10-19). T4d moved: the five effect slots
EffectsOverTempEnts already answered, four collision compositions, a player's
respawn / shoot origin / aim, the trace mask table (TraceMasks), the decal
precache (onto IVEngineServer), the area portal records and their per-client
walk (AreaPortals), worldspawn's own keys, the default light styles, and the two
TE forwards left without callers. What is left in the lane's files, each with
its exit:

- **The traces** (shell_worldquery.cpp's TraceLineInto, TraceHull,
  EntityTraceHull, and the traced arms of PaintDecal, BubbleEffect and
  WaterSurfaceBelow): the engine calls the native ITraceFilter's ShouldHitEntity
  per candidate, mid-trace, through its vtable. P3f's managed-built filter is
  the exit; BubbleEffect and WaterSurfaceBelow also want the trace's contents,
  which no managed trace returns yet.
- **PaintDecal's permanent form** calls MakeInitMessage on the native recipient
  filter (signon data), and its traced forms need trace_t's entity, model and
  local position: P3f's filter object again. TE_BSPDecal and TE_ProjectDecal
  stay with it.
- **BloodEffect**: g_pGameRules->IsMultiplayer() (the amount x5) has no managed
  route (BloodEffectPortabilityTests says so); UTIL_BloodStream keeps
  TE_BloodStream, UTIL_Smoke and UTIL_BloodDrips keep ss::Effects()'s Smoke and
  Sparks and their TE forwards.
- **CWorld::DecalTrace and CWorld::ImpactTrace** are reached virtually from the
  native UTIL_DecalTrace / UTIL_ImpactTrace (util_shared_library.cpp,
  server_util_library.cpp); they move with those callers, and TE_Decal,
  TE_WorldDecal and DispatchEffect("Impact") with them.
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
  EngineSide members are shared and [SuppressGCTransition]), GetPlayerTeam /
  PlayerSetTeam (nothing native reads m_iTeamNum: an SsPlayer [SaveField] behind
  ArsenalPlayers would do), TemplateSpawnKeyed, EntitySetKeyValue,
  EntitiesInSphere, EntityGetState, EntityTakeDamage, EntityModelSurfaceProp (it
  wants IMaterial / IMaterialVar forwarders) and AngleVectorsFromForward (the
  client's pane split is native and the two must agree to the bit).

## The server trace filter in C# (plan_server_native_v2 T4f): what stayed native, and follow-ups

Measured at the lane's tip (2026-10-19). T4f moved the per-candidate trace rule
to managed (TraceFilterRules over libss_fwd_server's ITraceFilter), and every
server trace with it: TraceLine/TraceHull/EntityTraceHull, WaterSurfaceBelow,
PaintDecal's traced and permanent forms, BubbleEffect and the movement hull's
MoveTrace. The T4d bullets above for the traces and PaintDecal are done.

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
  (UserMessageSend), CWorld::DecalTrace's broadcast filter (with
  UTIL_DecalTrace's native callers), TE_DispatchEffect and the TE forwards'
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
- **The client's FL_BASEVELOCITY is 1u<<5** (ClientPlayerPrediction.cs:556)
  where public/const.h has 1<<24 in the multiplayer block (1<<23 in the
  other) and the server's ServerCommandRules uses 1<<24. Likely a client
  prediction bug; needs a fact against const.h.
- **The client never calls SetAreaState**: the area bits had no wire since T2;
  T4g deleted the server's copy.
- **Divergence: damage between commands no longer republishes the movement
  words** (stock's TakeDamage did not either; the old native SyncToBlob did).
  The next command publishes them.
- **A hot reload seeds the record from the published state**, so the ground
  handle is not restored until the next ground trace.
- **tools/slot-census.tsv** needs a live regeneration for the retired slots
  (PlayerRunMove, MoveTraceThrough, PlayerSetSlowMovement, ViewPunch,
  EntityAddBaseVelocity).

## The client's sound emit path in C#: follow-ups and divergences

- **CLOSED 2026-10-19: gamesound.h is dead on both sides.** Nothing defines
  or calls a `GameSound_*` function any more (`nm -C -u` over both link
  lines: none). The four server includes went in 4e8970d95; the client probe
  build repair moved the last includer (tools/probes/client/
  c2sims_probes_debug.cpp) to soundemitterbase and deleted the header.
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

## NameMatchesTests.AWalkAllocatesNothing failed once under a full managed-server leg

At f8c7e82ff (stage1-c-gameplay rebased onto main 42b718990, 2026-10-18) the
fact failed once in `make test`'s managed-server leg: expected 0 bytes, read
152. It passed 5 of 5 run alone (`make managed-test
FILTER='FullyQualifiedName~NameMatchesTests'`), and the lane does not touch
it; the fact is unchanged since 0805b2f. It warms the walk once and then reads
`GC.GetAllocatedBytesForCurrentThread()` around a second walk, so a JIT tier-up
landing inside the measured walk would charge it. The rate is unmeasured: run
the leg repeatedly to get one, then warm until two walks in a row allocate
nothing before measuring.

**Closed 2026-10-18:** both allocation facts measure through SteadyAllocation.LeastAllocated (src/test/common): warmed three times, then the fewest bytes across five measured calls, so a JIT tier-up inside one call no longer charges it. Mutation proof: a per-call `new object[1]` in MotionSimulation's SimulateState turned all 4 EachKindsSimulateAllocatesNothing cases red, and one in NameMatches.MoveNext turned AWalkAllocatesNothing red; both green after the restore.

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

## Managed code calls the engine vtables directly: ShimSlots retired, closed

Added 2026-10-04 at main 9925463d; closed 2026-10-07 at todo-list (this
lane).

The router is gone. `tools/shim-areas` now emits one typed method per slot
on the area class that serves it (`ShimAreas`, `ClientShimAreas`), with the
vtable call inline; the generated file is `src/abi/ShimAreas.g.cs`. There is
no `Slot_X` router, no `Has_X` (an area method is reached only through the
attach-state pattern-match, which is what `Has_X` tested), and no
throwing accessor. Each `EngineSide` dispatch reads
`ServerShimsOrNull is { } __attN ? __attN.X(...) : ClientShims.X(...)`.

Measured before cutting, as required: of EngineSide's 432 public wrappers,
59 carry a side dispatch and none is a bare pass-through — the rest
convert arguments (utf8 bytes, `fixed` pins, out-structs), default-check,
or log crossings — so the wrapper layer stays as the named surface and the
routing layer died. `Slot_TemplateSpawnInstance` (the generated-but-never-
called orphan P5 left) went first, with its tsv row and native body.

Instruments retargeted with it: the side-audit classifier follows IL calls
to typed area methods (a wrapper map that matched nothing would have
silently passed the audit — the chain facts' positive arms name the four
bug slots, and waivers/known lists stay at ceiling 0); the router-walk fact
`ShimSlotsTests` was killed with the router; `EngineApiAreasTests`'
shared-route fact and the convar gtest's forwarder needle read the typed
shape. `make test` rc=0, zero warnings.

## The debug API did not answer in a headless run at 7dd48e61: closed, not reproducible

Added 2026-10-04 at main 9925463d; closed 2026-10-07 at todo-list b00910bf.

In the bug lane's headless runs (worktree `ss-bugs`, base 7dd48e61, gate
flags), the game printed its debug API socket path but the API never
answered, and `setpos` sent through it had no effect. The lane guessed that
the endpoint was missing, but 7dd48e61 already has the new endpoints, so the
cause was unknown.

The `ss-bugs` worktree is gone, so the lane's logs are unrecoverable. At
today's tip the scenario was re-run (fresh `make headless` game,
`-debugapisocket`, own `DEBUG_API_DIR`, pid 1197645): `/status` answered
with full JSON, and `/command` ran `sv_cheats 1; setpos -256 128 80` with
the effect confirmed at `/entities?class=ss_player` — `origin [-256.00,
128.00, 80.00]`. The API answers in a headless run.

Best account of the original fault, from the listener's shape
(`HttpSocketServer.cs`): the accept thread is its own thread, but every
game-thread route is queued and only runs in `Pump`, with a 5 s work timeout.
A game whose main thread froze after `Start()` completes therefore prints
`listening on <sock>` and then answers no command — the report's exact
signature. A main-thread freeze in exactly that period is what the KWin
minimising rule did (docs below: games frozen at 100% in
`XGetWindowAttributes`); `tools/kwin-bg-rule` landed 2026-10-05 00:04, one
hour after that base commit. No standing regression test is added: the API
is asserted answered on every join by `tools/concurrent-start` (map census,
`ss_player` census, first-paint line, all read THROUGH the API) and by the
live gate's steps, which is the same assertion a unit test could not raise.

## Capture native console output in process, not from console.log: closed

Added 2026-10-04 at main 9925463d; closed 2026-10-09 at todo-list (this
lane).

`IManagedConsoleOutput` / `IManagedConsoleOutputClient` (declared in
`docs/managed-interfaces.tsv`, generated both ways) are the bound areas;
`CConsoleDisplayForward` (shared/native) is the one `IConsoleDisplayFunc`
per side, installed by `SourceSharp_LoadMySide` on `g_pCVar` — where both
sides have ICvar and a load's areas — and removed by
`SourceSharp_UnloadManagedSide` before the areas go. Its three methods
forward the engine's already-formatted text verbatim on the printing
thread; the area is re-read on every line, so a reload picks up the new
managed object and a NULL (outside a load, or unloading) drops the line.
Forwarding decides nothing.

Managed: the side's binding (`ConsoleDisplayBinding` /
`ConsoleDisplayClientBinding`, registered by the module's area bindings)
marks the side's `ConsoleLines` fed and hands every print to `Line`; the
dev API then subscribes its console ring to those lines and disposes the
file tail, and `/command?capture=1` answers from the ring with no echo
markers and no 3 s wait — `/console` says which feed served it
(`feed: display-func | file-tail`). `ConsoleLines.SuppressEcho` keeps a
managed print to one delivery: the engine's synchronous re-print is
consumed instead. `ConsoleTail` stays as the fallback for a side with no
display func attached (tests, no-engine runs, a game whose areas never
loaded). A print with no feed and no `-condebug` is the one thing still
lost, and the capture reply now says so instead of timing out.

Evidence: 12 `console_display_forward_test` gtests (the installed func
forwards Print/DPrint/ColorPrint verbatim, reads the area per line, stops
after remove); `ConsoleDisplay(Binding|ClientBinding)Tests`,
`ConsoleLinesEchoTests`, `DebugApiConsoleFeedTests` (engine print served
by `/console` with no console file at all), `ConsoleUiEngineFeedTests`
for the window. `make test` at the lane's tip: all legs `Failed: 0`.

## Concurrent launches stall in start-up: the KWin rule, now fixed

Added 2026-10-05 at main 8febc29c, from the hushsteam lane's rounds; cause
found and fixed the same day (concurrent-startup branch, tools/kwin-bg-rule).

Several local games started at once often never opened their dev API socket:
26 of 160 servers and 13 of 131 second-client instances in 30 rounds of 4 or
6 concurrent `-maxplayers 4` games. The stall was not the shared
`videoconfig_linux.cfg`. It was the KWin rule for `sourcesharp-bg` windows,
which minimised them on creation. A minimised window is unmapped, and SDL2's
`X11_SetWindowBordered` (libSDL2 2.32.10) loops `XSync; XGetWindowAttributes`
with no timeout until the window is viewable. The engine calls
`SDL_SetWindowBordered` while it creates its window (engine.so 0x5db298), so a
game KWin minimised first spun there forever: main thread at 100% in
`XGetWindowAttributes` under `XSync` (perf), `WM_STATE` Iconic, Map State
IsUnMapped. The console usually ends at `[CFontManager] Forcing freetype2 for
GorDIN Semibold`, but can end in videoconfig errors when the window comes up
later. The rule now keeps games below and out of the taskbar and never
minimises them. `tools/kwin-bg-rule --probe` reproduces the stall with a bare
SDL2 window (rc 1 = stalled).

Measured 2026-10-05 at dc6d34eb (debug, background, 5 rounds of 6 concurrent
`-maxplayers 4` games, each gated by `live-gate --attach --steps
second-client`; regime: load 10-36 and 2-6 foreign games before, load 23-69
and 0-4 foreign games after):

| rule | servers stalled | second instances stalled |
|---|---|---|
| minimising (before) | 11 / 30 | 3 / 19 |
| never minimise (after) | 0 / 30 | 0 / 30 |

Every stalled server had the stall's signature; the four caught live were
Iconic and unmapped, one sampled with perf spinning in `XGetWindowAttributes`.
P(0 of 30 | before rate) is 1e-6 for servers (0.005 at the hushsteam lane's
26/160) and 0.006 for second instances (0.04 at 13/131). One after-run second
instance opened its socket and then exited during `connect` with no exit
record: a separate fault, not a start-up stall.

The `videoconfig_linux.cfg` race is real but separate: the engine rewrites the
file in place during start-up, so games started together log `KeyValues
Error: LoadFromBuffer: missing {` and may read a half-written config. The
launcher's `-instance` switch (on for every Python tool and `make headless`)
now gives each game its own copy.

## Re-measure test timings on a quiet machine, and time the release build: done

Added 2026-10-04 at main 9925463d; measured 2026-10-07 at f725b434.

`tools/measure-timings` runs each once and writes
`docs/measurements/timings-<stamp>.tsv` with load1, exit code and TIP per
row. The quiet run (box otherwise idle; the first three rows are the quiet
ones — load rose as nothing else could be kept off the box):

| command | s | load1 | rc |
|---|---|---|---|
| `make debug` (warm) | 14.0 | 0.34 | 0 |
| `make test` | 25.1 | 7.30 | 0 |
| `make test-full` | 27.0 | 16.95 | 0 |
| `make release` (cold, build dir removed) | 42.1 | 18.80 | 0 |
| `make release` (warm) | 3.1 | 24.59 | 0 |

The old after-timings (load 33–70) were `make test` 19.8–31 s and
`make test-full` 35.3 s. `make test` did not get faster in peace — the old
19.8 s figure was a favourable draw, and the leg-floor's "about 18 s warm"
is optimistic at this tip. `make release` was never timed before: cold is
42 s, warm 3 s.

## A second instance exited during connect with no exit record: closed, by design

Added 2026-10-05 at main 991037cd; closed 2026-10-07 at f725b434.

The instance was the after-series round 3 g3 second client (pid 905949): the
gate's own report (`mg-after-3.json`) says `second-client: dev API stopped
answering: the game process 905949 has exited`. No record was ever expected
for that death. The record has exactly three writers, and every un-watched
death between socket-bind and join — SIGKILL, an unwatched signal, a
CoreCLR abort, an engine `_exit` — leaves none by design
(`src/server/native/exit_watch.h`: a crash is the launcher's to report, by
exit status; only SIGTERM/SIGINT/SIGHUP, SDL quit/window-close, managed
`Dispose`, and the fatal report write one). The observation was made by a
scratch harness whose reader looked in the server's socket dir, not the
instance's own `DEBUG_API_DIR` — a death that *should* have left a record
would have looked identical to "no record".

Forensics on the surviving artifacts: no `/tmp/dumps` minidump contains tid
905949 (raw stream scan of all 2026-10-05 dumps), so the death was not a
breakpad-caught crash of our module; it was an engine-side exit inside
connect, uninstrumented territory. Repro attempt: 12 games with
`tools/concurrent-start -n 6 --rounds 2 --second-client` — 24/24 joined,
0 failures, consistent with the fault's 1/30 base rate.

If it recurs, the record question is already answered (there was none to
find); the fault itself needs the instance's own console and exit status,
which the harness must collect from the INSTANCE dir before it cleans up.

## A game sent SIGTERM exits with 139 (segfault): fixed

Added 2026-10-05 at main 9004e146; fixed 2026-10-06.

Seen by the webhelper lane: a debug game sent SIGTERM exits with status 139
(SIGSEGV) instead of shutting down cleanly. The exit record (exit_watch)
chains SIGTERM to the previous handler, so the fault is in the shutdown that
follows.

The repro (`SIGTERM` to the ENGINE's own pid, after first paint) and the
coredump name the frame: the process dies inside SDL's event-watch dispatch
in the engine's last frames, jumping into `exit_watch.cpp`'s `OnSdlEvent`
AFTER `server.so` has been unmapped — there was no removal half:
`ExitWatch_Arm` installed the watch and the three signal handlers once per
process and nothing ever took them out. The record file itself is written
first (managed shutdown), so the crash lands after the record and before the
exit. Group-Terminating the launcher hides it: the launcher hard-kills the
game (rc 137), which is why the repro TERM the engine pid.

Fixed: `ExitWatch_Disarm` (native) restores the signal handlers and REMOVES
the SDL watch through `SDL_DelEventWatch` (facts kept — the record is written
after disarming), reached as an `IShimDiagnostics` slot
(`ExitWatchDisarm`) and called by `DebugApiServer.Dispose` right after
`WriteExitRecord`. A hot reload's re-arm now installs over the restored
handlers. Regression: four gtests in `exit_watch_test.cpp` (mutation-proved:
dropping the sigaction restore is red), two facts in
`DebugApiExitRecordTests` (mutation-proved: dropping the Dispose call is
red), and the live-gate's new `sigterm-shutdown` step, which is the only
check anywhere that SIGTERMs a real game — the whole gate otherwise only
ever SIGKILLs. Its negative control: reverting the Dispose call in the live
game reproduces 139 exactly, and the step goes red on it.


## Entity fields set in OnSpawn are lost across a hot reload: fixed

Added 2026-10-05 at main 2b1cd8a5; fixed 2026-10-06.

A hot reload rebuilds each managed entity without running `OnSpawn` and saves
only `[SaveField]` fields. A plain field filled once in `OnSpawn` therefore
comes back as its default. That was the frozen-mover bug: `FuncMoveLinear`'s
open, closed and target positions came back as (0,0,0), fixed by `[SaveField]`
in 070933fd.

The lane that fixed it counted about 60 other plain fields in
`src/server/managed/Entities/Brushes` with the same pattern. Likely affected
the same way:

- `MovingBrush.ClosedPosition` / `OpenPosition` (doors)
- `FuncPlat`
- `FuncDoorRotating`
- `TrainBrush`

All three landed. `OnSpawnFieldReloadTests`
(`src/test/managed/OnSpawnFieldReloadTests.cs`) loads every scripted server
entity class, runs its `OnSpawn`, drives the real save/restore round trip,
and fails on any field that comes back holding something other than what
`OnSpawn` left -- proving the stimulus worked, since unmarked fields
demonstrably DO return to their defaults. It names each loss and its waiver
exit in `bin/debug/tests/onspawn-reload-losses.txt`. The sweep found the
fields listed above and more: 53 plain fields across 39 classes are now
`[SaveField]`, each with why at its site, and the sweep line reads
`# swept 445 fields, 0 lost`. The gate arm is live-gate's hot-reload door
check (ef845f49): a `func_door` left standing open across `cs_sv_reload`
must come down on Close and go back up on Open -- an uncarried `State` or
`ClosedPosition` reads red there.

## Follow-ups found while actioning the 2026-10-10 review: closed

Added 2026-10-11 on the review-2026-10-11 integration (lanes review-physics,
review-bones, review-pcore); closed 2026-10-11 on followups-2026-10-11.

- **A disabled trigger comes back enabled after `cs_sv_reload`.**
  `TriggerVolume._disabled` and `_nextFireTime` are plain fields set in
  `OnSpawn` or by inputs and carried by no save tier. `OnSpawnFieldReloadTests`
  cannot see it: the default `StartDisabled=false` reads the same on both sides
  of the reload, the same harness gap `FuncPhysbox._startingHealth` had (a
  per-entity reload fact with a non-default value is what catches it). —
  closed: both are `[SaveField]`, as stock saves `m_bDisabled` and the wait's
  think (`TriggerStateReloadTests`).
- **`PhysicsCollisionResponse.Drain`'s POST_COLLISION arm has no producer.**
  Since plan_physics_managed B2.2, `PhysicsCollisionImpacts.PostCollision`
  answers the response in the solver callback, and nothing native records
  POST_COLLISION into the ring (`physics_event_sink_test` pins the absence).
  Only facts reach that arm: delete it, or say why it stays. — closed: deleted.
  `Drain` counts every kind and answers none, so a native record coming back
  cannot answer an impact twice; `Respond` is the one way into the response
  and the response facts drive it.
- **`Studio_SetPoseParameter`'s `floor()` overload is unverified.** Stock may
  resolve to the double overload; the port uses `MathF.Floor`. The bones
  review's float/double batch (review §10) did not cover it. — closed, no code
  change: stock's line compiled with the game's own flags calls
  `std::floor(float)` and stays single precision in debug and release, as the
  port does. `SetPoseParameter_LoopWrap_FloorIsStocksFloatOverload` pins it on
  an input where the two overloads differ.

## hot-reload's think check counted a one-shot that came due inside the reload as lost: fixed

Added and closed 2026-10-11 at main 86f4d763d. The same family as the
keyframe_rope flake (closed in aea9d298b): a transient booking near a reload.

The step's "the think schedule survived" check failed about 1 in 5-10 runs on
`item_ammo_pistol`: its one-shot 20 s respawn think came due INSIDE the
reload's span, ran, and was no longer booked. The reload had not lost it
(source-sdk-sharp-18, from `/counters?group=think`). Measured: the
playermsgs integration saw 1 failure (client-novalve 35acabce6, pass 2 of 2:
7 bookings, was 8, 8 required), with 3 of 3 green reruns on both that tip and
main 20ebb23a; the nightly at 7b2ec8cd failed 1 of 2, then 0 of 3 on confirm.
The earlier fix (the most common of nine reads, the lower of two reads per
class) narrowed the race but did not close it: a one-shot due within a second
of the second before-read counted as recurring and so as must-survive.

**Fixed:** the think counter group now says when each booking comes due
(`dueMs.<class>.<n>`, milliseconds from the read), and the step's
`think_survival` requires only the bookings due AFTER the reload's span, plus
the recurring ones inside it (a class booked within a second in both
before-reads). The gate still checks the total. Its self-test pins both
halves: a one-shot due inside the span is not counted lost, and a booking due
after it, a recurring thinker, and the 8 -> 0 shape are all still caught.

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

## tools/concurrent-start checks sockets, not joins: fixed

Added 2026-10-05 at main 8ec65287; fixed 2026-10-06.

`tools/concurrent-start` counted a game as started once its debug API socket
opened. The start-up and launch-arguments lanes had to use scratch harnesses
(`hs-mg.py`, `cs/mg.py`) to assert real joins (first paint, one `ss_player`)
and the second-client step.

The tool now gates on the join: the map census, exactly one `ss_player` (the
listen server's own client), and the `[SourceSharp/Hud] first paint` line in
that game's own console log (path from `/console`, never a shared guess). A
game whose socket opened but that never joined is NOT JOINED, names the half
it failed on, and fails the run. `--second-client` launches a real second
instance per game against the port the server actually bound and requires
two players plus the newcomer's own `ClientLevelInit`. `--self-test` pins
both decision tables (15 checks, registered in `tools/checks.tsv`; all eight
arm-break mutants caught). Live proof: `-n 2 --second-client` green at
1965e17b.

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

## descent-death: P1+P2 on main; P3-P6 remain the plan's own work

Added 2026-10-05 at main 8ec65287; status re-taken 2026-10-06 at main 043862af; P2 re-taken 2026-10-08; landed on main with the todo batch 2026-10-09.

P1 landed as `bc10ca92` (host-side hooks: `IPlayerDeathHook`, `IRespawnGate`, `Corpses.Keep`).
P2 landed 2026-10-08 on todo-list (`01e24de3`..`1561f57d`, retired to `archive/descent-death-p2`):
`IHostLink.RecordDeath/LootCorpse/TravelDirection.Corpse` and `DeathService`
(keep-the-body, death screen, host told, hop-hold + respawn-gate + retry + 60 s kick,
161 descent-leg facts; the lane's plan §5 note carries the gate record).
Resume P3-P6 from main's tip following
`docs/plans/plan_descent_death.md` §5 (P3 needs host SDK 2.5.0's anchor calls; P5 is a k3s live session).

## Client port follow-ups (small)

Added 2026-10-05 from the client no-Valve lanes (plan_client_novalve.md), merged to main with the client port squash.

- **Scoreboard max players reads 33** ("1 / 33") on a listen server — closed 2026-10-08: 33 was the net-state array bound; the header now reads the engine's `maxClients` off the live globals, the load stock's dialog makes (`ScoreboardUi`, `ScoreboardMaxPlayersTests`).
- **Console window mirrors only typed lines.** — closed 2026-10-09: the window now mirrors the side's `ConsoleLines`, so engine prints show too; its own typed echo is deduped against the engine's re-print (`ConsoleUi`, `ConsoleUiEngineFeedTests`).
- **Voice bans are saved immediately** (lane ui, C3; kept, user 2026-10-05). Stock `CVoiceStatus` wrote `voice_ban.dt` once, from its destructor at client shutdown (`m_BanMgr.SaveState`, voice_status.cpp:141), so a crash lost every mute made that session. `VoiceStatus.ToggleBlocked` writes the file on each toggle instead; the bytes are stock's (`VoiceBanList.Save`, held to the CVoiceBanMgr oracle). Recorded as a divergence to keep; revisit only if a write per click ever shows up in a profile.
- **The kill feed outlines the local player's lines** (lane ui, C3; kept, user 2026-10-05). Stock HL2MP's CHudDeathNotice did not mark them (TF2's feed does); `screens/KillFeed.cs` outlines a line the local player is in with the accent. A deliberate departure from stock, not a port gap.
- **Server browser has no data source.** No master-server or LAN query exists yet; it opens empty (C4: Steam matchmaking over the managed flat API).
- **MOTD HTML.** HTML/URL MOTDs fall back to `motd_text` as stock does with HTML off; a web view (Steam HTML surface, already probed in C0) is the later option.
- **Tempent PhysicsProp** stays a refusal under the clientside-prop eviction (ClientTePhysicsProp). — closed 2026-10-18: `ClientPhysicsProps` ports CTempEnts::PhysicsProp and C_PhysPropClientside (the model's last solid, the velocity, the skin, cl_phys_props_max, StartFadeOut's death time, Break through PropBreakableCreateAll's client half), drawn as a FTENT_PERSIST pool slot its vphysics object moves (`ITempEntDriver`), made through the ragdolls' vphysics calls; impact sounds through `ClientImpactSounds.SoundsWithoutGameData`. Live: `cs_te_fire physicsprop` (now Flags 0) then `cs_cl_physprops` showed the skull fall from z 168 to 131 and sleep, and one client impact sound. Not carried (class notes): game data and collision group, TOUCH_STATIC and respawn zones, m_fEffects, scrape sounds, the piece limits, and hot reload (the pool keeps no slots across one either). — gaps closed 2026-10-18 (lane cg): the object is owned by the prop (a handle of its own in the non-networkable range, PhysicsGameData), group PUSHAWAY / a zero-health piece NONE (ClientCollisionFacts), the splash at its object; the scrape played by the world (FrictionSounds' sound source; native's client patch create carries the world's handle out as entindex 0); CTEPhysicsProp's effects kept and inherited by pieces, EF_NODRAW drawn as kRenderNone (a temp entity has no m_fEffects; no shadow or entity lights to switch); props_break_max_pieces(_perframe) as PropBreakableCreateAll; CALLBACK_GLOBAL_TOUCH_STATIC set; the props survive cs_cl_reload (ClientTempEntPool.Adopt). Facts in ClientPhysicsPropsTests, ClientContactTouchesTests, FrictionSoundsTests, TempEntClientPool2Tests, each with a mutation. Live (headless dm_lockdown): a prop fired at z 120 settled at z 3.45 and slept; after cs_cl_reload ("41 saved, 41 restored") cs_cl_physprops still listed it at the same place, and a prop made after the reload settled too. Still not carried: the respawn zones (no map prop_physics_multiplayer path), a piece's motion-disabled flag, the skin-family clamp, prop shadows (temp entities cast none).
- **Crossbow bolts pinning ragdolls** (stock's CRagdollBoltEnumerator) needs the client ragdoll's physics objects; not reachable from managed yet. — 2026-10-18: deferred until after the publish (integrator). Reachable now: the client ragdolls are managed (ClientRagdolls), and the vphysics calls it needs are already forwarder rows (PhysicsEnvironment's CreateBallsocketConstraint, PhysicsObject's SetMass and WorldToLocal), so no ABI. Still to write: a hitbox trace by handle in ClientSkeletons (TestHitboxes reads the block by handle, `_blocks.Block(entity)`), a ragdoll's element object by physicsbone, the client world's physics object as the reference, and Weapon_Crossbow.BoltSkewer from the world; CrossbowBoltEffect then replaces its once-said refusal. — closed 2026-10-18: `RagdollBoltPin` ports CRagdollBoltEnumerator (the line 128 back along the flight, each client body's hitboxes traced by handle through `ClientSkeletons.TestHitboxes`, the element `physicsbone` names by `ClientRagdolls.ElementObject`, its mass doubled, a ballsocket to the physics system's world object, `Weapon_Crossbow.BoltSkewer`), and `CrossbowBoltEffect` calls it before the bolt is made; `RagdollBoltPinTests` and the oracle's `BoltImpactPinsABodyAlongTheFlightBeforeTheBoltIsMade` pin it. Live (dm_lockdown, `cs_cl_boltpins`): a bolt shot into a corpse pinned physicsbone 0, its ballsocket end 3.00 units from the bolt; the body slept (stock wakes nothing either), and a `cs_te_fire explosion` beside it moved the body 4.5 units while the pinned end went to 0.01 and stayed within 0.36. Divergence: of two bodies on one line, the nearer is pinned (stock: the partition's walk order).
- **ss_bolt never sends BoltImpact** — closed 2026-10-08: `Bolt.Impact` ports `CCrossbowBolt::BoltTouch`'s dispatch (body/world branches, hit sounds, `BoltImpact` tempent with stock's normal/entindex, `UTIL_ImpactTrace` via the managed `ImpactEffect`); `BoltImpactTests` pins it; the reflect/linger/sparks omissions are in `docs/divergences.md`.
- **Dead tempent pool code** (SourceSharpTePool_Reserve/Bind/Release/EjectBrass and their managed callbacks) cannot be removed without renumbering CrossingOps, which `CrossingsTests.OpValuesAreContiguousFromZero` requires to be dense; do it in a serialised ABI cleanup. — closed 2026-10-09: the four exports (nm'd dead over all 868 link-edge objects, their only caller TU long evicted) and their internals deleted; `ClientTempEntPoolAlloc`/`ClientTempEntEjectBrass` left with them in one commit (CrossingsTests' both-directions equality), one tail renumber 81..108 -> 79..106; the managed half flipped to refusal facts. `IManagedTempEntsClient`'s two callback slots stay (the area table is the ABI) and leave in the serialised pass below.
- **Snowfall ray-box tolerance** is not exercised by the precipitation oracle. — closed 2026-10-09 at c0ab6a8d: the golden's ray arm activates a box only through the 0.325 flare (hit at 0.325, miss 0.13 further, so the cases bracket the constant), and `ManagedSnowfallUsesStocksRayBoxTolerance` parses the tolerance from both source trees and asserts equality.
- **Serialised ABI cleanup** (one pass, renumbering CrossingOps once): remove the uncalled `DrawSprite`/`DrawSpriteOffset` slots and their `IEngineEffects` members, and `IManagedTempEntsClient`'s two dead callback slots with their `imanaged_areas.tsv` rows. Measured 2026-10-08 at 37c2e4a7: the DrawSprite slots ARE still called — `EngineSide.DrawSprite/Offset` (client shim slots 0/1 of IShimEffectsRenderClient) have live managed callers (`EngineBinding`, and `ClientPlasma`/`ClientSprite`/`ClientSpriteHost` through `IClientSpriteDraw`, which is the render-budget plan path and must stay). The census before cutting: which of the two vtable slots, if either, an engine-body still reaches — the engine binaries, not `nm`. (The tempent pool half closed 2026-10-09; see the row above.) Also in this pass: `IManagedConsoleOutput::ConsoleLine` and `IManagedConsoleOutputClient::ClientConsoleLine`, uncalled since console-batch (2026-10-11: the forwarder appends to its line buffer and managed drains it through `ConsoleBufferAttach`), with their rows in `docs/managed-interfaces.tsv` and the `ServerConsoleLine`/`ClientConsoleLine` CrossingOps.
- **Snowfall ray-box tolerance** is not exercised by the precipitation oracle.
- **Serialised ABI cleanup** (one pass, renumbering CrossingOps once): remove the uncalled `DrawSprite`/`DrawSpriteOffset` slots and their `IEngineEffects` members, and `IManagedTempEntsClient`'s two dead callback slots with their `imanaged_areas.tsv` rows. Measured 2026-10-08 at 37c2e4a7: the DrawSprite slots ARE still called — `EngineSide.DrawSprite/Offset` (client shim slots 0/1 of IShimEffectsRenderClient) have live managed callers (`EngineBinding`, and `ClientPlasma`/`ClientSprite`/`ClientSpriteHost` through `IClientSpriteDraw`, which is the render-budget plan path and must stay). The census before cutting: which of the two vtable slots, if either, an engine-body still reaches — the engine binaries, not `nm`. (The tempent pool half closed 2026-10-09; see the row above.)
- **The second camera's overview omits entities outside the player's PVS** — a known limitation, by user ruling 2026-10-11 (option 4 of the PVS question, plan_client_novalve C7.6): no PVS change at all, the server adds no camera origin, so `cs_cl_overview 2` shows only what the player's own PVS sends. Measured at 74d9ee19 + lane camproto: of 37 server entities in the overview's box, 20 were absent on the client (props in the next rooms). Not to be fixed without a new ruling.
- **The debug camera** (`cs_cl_overview`, lane camproto, user ruling 2026-10-11): a debug tool, not gameplay. Cheat convars, off without `sv_cheats 1`, HUD panel only while on; driven by `/client/{slot}/viewcam[?mode=0|1|2]` (`tools/debug-api client 1 'viewcam?mode=1'`) for screenshots and debugging. Plan C7.6 has the measurements; C7 keeps its render-target mechanism generic.
- **Studio models draw black at dm_lockdown's spawn: the map's own lighting, not a regression** (investigated 2026-10-11, lane camproto). The crowbar viewmodel and the debug camera's third-person body are black silhouettes at the spawn corridor (-2651 4225, eye z 192.8, yaw -90). The ENGINE's own model lighting there is near zero: `engine->ComputeLighting` (the light cache over the BSP's leaf ambient and world lights, which no client code feeds) answers 0.004 at the eye with all six ambient-cube faces under 0.01, and under 0.015 within 64 units, against 0.67-0.97 in the lit room at -3079.5 4656.5 where the crowbar draws lit (the pose of the 2026-10-05 UI screenshots, which draws the same today). Walking toward the corridor lamp, the crowbar brightens smoothly with distance to it (red 0.9 at y 4225 to 23.5 at y 4000), so the draw path takes the light the engine offers. Not a regression either: a pixel probe (crowbar luminance under mat_fullbright 0 over 1, same pose) reads 0.008 at 797a129b and 0.065 at fbd904ed, black at both. Open question, if anyone wants it closed: whether stock HL2DM shows the same there (no stock hl2mp binaries are installed to run the comparison; the half-life 2 deathmatch dir holds content only).
- **Render budget headroom:** `ClientStandaloneDraw` (4-7 per client frame, the standalone entity shell's per-entity draw) is now the main driver of the `render` step; batching it is C7 render-plan work. `ClientFxSmokeFogPlan` costs a steady 1 per frame and could fold into a per-frame call.
- **The client's trace misses props:** a bullet impact on a metal prop takes no custom effect, because the client trace used for impacts doesn't hit props (seen by c5-particles, older than the particle work). — closed 2026-10-09: the props tier sends `m_nSolidType`/`m_nSolidFlags`, the client prop answers collisions with the server twin's solidity, stock's gate and the twin's hitbox trace (`client_prop_collision_test.cpp`, `StandaloneNetTable`); an impact on a metal prop now takes its custom effect.
- **`mat_stub 1` crashes the game, in stock and in the port alike.** Added 2026-10-11 (lane c5-proxies, measured at its 7ef99f1b and ported to f56bc32c). `CMatStubHandler` (now in `shell_material_proxy_client.cpp`, the swap decided by the managed `MatStubConVar` through `IManagedMaterialProxyClient::MatStubEnabled`) carries out stock's swap exactly; setting it to 1 exits the game, and the stock build (cl_mat_stub.cpp restored at d835d05c, same steps) exits the same way. Stock's behaviour, carried, not fixed; the cause in the engine's stub material system is not yet found.
- **Stock undefined behaviour in the Int and SelectFirstIfNonZero proxies, ported as four-wide.** Added 2026-10-11 (lane c5-proxies, 37ad7f31, ported to f56bc32c). mathproxy.cpp reads a four-wide result through a three-wide `Vector`; in the recorded debug build the fourth float overruns onto the `vecSize` local, so stock writes `SetVecValue( a, 0 )`. The port writes all four components (`Vec4Temp`, `MathProxies.cs`), and the oracle replay names exactly those writes as the allowed disagreements (`TheOverrunBindsAreTheFourWideVectorResultsOnly`). A different compiler or the release build may lay the stack out differently, so the oracle's ruling holds for the recorded build only; re-record in release before relying on it there.
- **vtf, bitmap and proxyentity stay linked into client.so until C7.** Added 2026-10-11 (lane c5-proxies, measured at its 75f41432 with nm -u over the client objects and tools/client-novalve-census; re-measured at f56bc32c: imports:bitmap 1, imports:vtf 2). bitmap's `TGAWriter` is still called by view.cpp and viewpostprocess.cpp, vtf's `CreateVTFTexture`/`DestroyVTFTexture` by view.cpp, and proxyentity.cpp stays because viewpostprocess.cpp's MotionBlur proxy derives `CEntityMaterialProxy`. All three leave with C7's render-plan port of view.cpp/viewpostprocess.cpp.

## Spectator (observer) port: what stock does that ours does not yet

Added 2026-10-11 from the playermsgs lane (`Observers.cs`, ported forward
from `archive/client-player-msgs` onto main 2eafcb77). The observer port
follows CHL2MP_Player::StartObserverMode (observe only on `spectate` /
`jointeam 1`, never on a death timeout); these parts are named as not
ported in `Observers`' remarks:

- **Team changes.** `spectate` and `jointeam N` do only the observer half;
  the team itself never changes (no SetTeam reaches the standalone player),
  so `player_team` is never fired and teamplay's chat filters see no teams.
  With it goes HL2MP's `TEAM_CHANGE_INTERVAL` (5 s, `m_flNextTeamChangeTime`,
  set by CHL2MP_Player::ChangeTeam): there is no rate limit on team
  commands until ChangeTeam is ported.
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

## Server skeleton flip (physics_bones_native_zero 1.2): three follow-ups, two open

Added 2026-10-11 at the bones 1.2 lane (worktree-agent-a52447138edc9bfa5).

- **The shared IShimModels bone rows.** StandaloneBoneMatrices,
  StandaloneIsPoseable, StandaloneStudioName, StandaloneLookupAttachment,
  StandaloneGetAttachment, StandaloneBoneCount and StandaloneSetSequence are
  `both` rows in docs/engineapi-areas.tsv; the server answers them in managed
  code now and its native bodies only warn (StandaloneBones_Unreached). When
  the client's skeleton flips too, the rows go (both sides at once); before
  then they could become client-only rows in one serialised ABI pass. The
  server-only EntitySetSequence and IShimHierarchy's LookupAttachmentAny are
  the same shape.
  — the seven shared rows: closed 2026-10-18 (lane claude/tail-skeleton-standalone):
  none of them is in docs/engineapi-areas.tsv any more (grep, at main
  8605b6ea1). EntitySetSequence and EntityLookupAttachmentAny: closed
  2026-10-18 (same lane). Managed stopped calling them first (EngineBinding's
  empty-route fallbacks and the EngineSide wrappers went); then the rows,
  ishim_areas.h's two methods, the native bodies in shell_scripted.cpp and
  shell_standalone_hierarchy.cpp and StandaloneBones_Unreached went, with a
  deliberate `tools/engine-forwarders --write --reorder` that drops
  ShimModelsServer slot 4 and ShimHierarchy slot 7 (both the last slot; no
  other slot moved). EngineApiCensusTests' server-only count 160 -> 158;
  animating_parallel_arm_test's TheRetiredSetSequenceBodyStaysGone holds the
  body's absence.
- **studio_sequence_timing.cpp is on server.so**: closed 2026-10-12 (lane
  agent-a77ed344, "studio timing" commits). The descriptor blob dropped each
  sequence's duration (format v3); ModelDescriptors takes it from
  StudioSequenceTiming over the side's StudioModels, and the file left both
  DLLs and the repository. Live parity before the cut (dm_lockdown, load +
  changelevel, both sides): max difference 0 over 1010 sequences.
  model_descriptor.cpp stays: ModelDescriptors lives in Shared.Interop, which
  cannot reach the managed StudioHdr, and moving the rest of the walk would
  retire the IShimModels ModelDescriptor slot (a serialised ABI change).
- **Hot reload and the model scale.** A skeleton this composition has not
  seen takes its sequence clock from the engine block, but the scale has no
  block word: after cs_sv_reload a scaled entity's skeleton is built at 1
  until the scale is set again (on the server only gibs scale, and they are
  short-lived). Carry it through the ReloadStore, or give it a block word.
  — closed 2026-10-12 by lane C3 (plan_physics_bones_perf): ServerSkeletons
  is an ISaveCustom; each skeleton whose scale is not 1 rides the
  ReloadStore and the first ask after the reload takes it, with the clock
  still from the block (ServerSkeletonsTests' three reload facts).
## Render plan (C7) follow-ups

Added 2026-10-11 by the C7 lane (client-r-c7, on client-novalve 7289d410).

- **ssmap marks almost no leaf `CONTENTS_TESTFOGVOLUME`**: 2 of `ss_sandbox`'s 2345 leaves, where vvis flags every leaf of every cluster that can see water. The engine's visible-fog-volume query (`R_GetVisibleFogVolume`) only searches from a flagged leaf, so from above any pool it answers -1 and stock never draws a water reflection or refraction view in this tree (measured at the sandbox's mirror pool: -1 from three poses above it, volume 1 with the eye under it). The fix is source-sdk-map-tools' `VisWater` (another repo: ask first). Until then the render plan's above-water views are held only by `RenderPlannerTests`' synthetic inputs, and the water oracle is the under-water fixture.
- **The fog volume is asked up front**: the executor gathers `GetVisibleFogVolume` at RenderView's entry, where stock asks it after `ViewSetupVisEx`. The search through `CONTENTS_TESTFOGVOLUME` leaves reads `r_visframecount` and the frustum, both set mid-frame, so once a map flags those leaves (item above) the two can disagree. Then: split the plan at DrawWorldAndEntities (re-gather the water inputs after the vis setup and plan the rest), and record an above-water fixture.
- **A clip-planed entity under fast clipping** (C7 step 2): stock's `DrawOpaqueRenderable`/`DrawTranslucentRenderable` draw a clipped depth box first when `materials->UsingFastClipping()` and the renderable has a render clip plane (`DrawClippedDepthBox`, a static in viewrender.cpp). The view program's `DrawEntity` pushes the clip plane in the other case and skips the box in this one. No entity in either map answers a clip plane; port the box (its mesh is geometry the executor can carry) before one does.
- **View programs allocate per view** (C7 step 2): `ViewSceneData.FromBytes` copies the scene's leaves and entries into arrays and `ViewDrawProgram` builds a context list per view, a few KB a world view a frame. Measure with `RenderPlanAllocationTests`' method before reusing buffers.
- **A monitor view draws no water** (C7 item 6): the render plan's monitor pass reuses the frame's water inputs, which are the main eye's, so it plans the monitor dry rather than asking GetVisibleFogVolume at the monitor's origin as stock's DrawOneMonitor does. Gather a fog-volume answer per queued monitor when a second camera can look at water.
- **The native second-camera draw stays for the stock path** (C7 item 6): with cs_cl_render_plan 0, shell_view_targets.cpp still draws the queued view through RenderView in the client mode's PreRender, as VIEW_MAIN with pixel visibility switched off around it. It goes when stock's viewrender does.
- **Two frames the plan hands back to stock**: a freeze frame (`test_freezeframe`; stock keeps that state in CViewRender's private members, the executor mirrors its time through the FreezeFrame slot) and reflective glass in view (`IsReflectiveGlassInView`, false in this build). Port them with step 2 if either becomes reachable.
- **FX_AntlionImpact needs entity world bounds** (lane c5-particles, 2026-10-11 at the port-forward f40c9d78). Stock's gate reads the hit entity's bounding radius and returns without one; the client ABI carries no entity bounds, so `ShardEffects.AntlionImpact` (ported, `AntlionImpactStockTests`) is reached by no caller yet. Needs a client read of an entity's world-space bounds, then the impact route can pass the radius.
- **Numbered muzzle flashes use the local player as the view entity** (lane c5-particles). `CL_EVENT_MUZZLEFLASH*` (FireObsoleteEvent) puts the world-space flash on the client's view entity; this ABI does not read the engine's view entity, so `IViewEntity` answers the local player and a `point_viewcontrol` camera is not it.
- **The debris dust's draw order** (lane c5-particles). `ImpactSprays` hands each burst's sheets to the executor in first-use material order; stock's sheets sort within the particle manager by their own insertion. Not seen to differ live; check against a recorded multi-material burst when the sort is next touched.
- **The debris dust's wind bit** (lane c5-particles). Stock never writes `SimpleParticle::m_iFlags` on a spawn, so a puff is wind-blown whenever the reused pool memory's bit 0 is set; the port spawns 0 (no wind) by decision, and the oracle replays the recorded byte (`SimpleEmitter.cs`, Fixtures/README-smoketrail-stock.md). Decide whether any effect should be wind-blown on purpose.
- **particle/particledefault is missing** from the mounted content too (lane c5-particles, 2026-10-11): the debug line and blood stream temp entities (TeParticles) draw the missing-material checker; the VMT exists only in sourcetest_pak_dir.vpk, which this mod does not mount, so stock's CTEParticleRenderer asked for the same absent material. Ship it or mount it.
- **sprites/flare6 is missing** from this content (lane c5-particles): the large funnel (`LargeFunnel`) draws the missing-material checkerboard, in stock's build and ours alike (side-by-side screenshots taken when the funnel temp entity was wired to LargeFunnel). Ship the material or pick an existing one.
- **CClientSystemPerFrame::PreRender may run twice a frame** (lane c5-particles, measured 2026-10-11 at 5aeeef63). The retired native dust volume's PreRender tick ran 3,630 times against 1,814 particle-manager steps over one recording, its frametime summing to 2x real time (dust-stock.tsv.gz's provenance; commit "cs_cl_dust_report counts ticks and steps"). Its caller was not found by grep (`SourceSharp_ClientSystemsPreRender` has no caller in this repo or the farm). Every remaining PreRender system (sprites, ropes, world text, the executor's own system, standalone interpolation) may double-step the same way: count one system's PreRender against `gpGlobals->framecount` live.
- **7e: the client never sees a managed particle owner move** (measured 2026-10-11 on plain main 20ebb23a, scratch worktree, `tools/7e-particle-sound-gate`, 3 runs: 3 of 3 red, identically). The harness moves the flame's owner 400 units on the server ("the harness actually MOVED the entity" PASS), and the client's copy moves 0.0 units ("the CLIENT saw the move" FAIL), so "the system FOLLOWED the handle" and "the sort origin followed" are NOT REACHED. The sound half of the gate passes. Same result on lane client-r-particles at b4a7d443, so the lane did not cause it. Suspect: the moved origin is not networked to the client copy of a managed entity (or the harness's move bypasses the networked origin).
- **The view-model edge checker artefact** (lane c5-particles): a checker-pattern artefact along the view model's edge, seen in the lane's first-person screenshots. Its measurements were in the lane's uncommitted notes, lost with the worktree (2026-10-06), so it is unattributed: re-take a first-person screenshot pair (effects on and off) before working on it.

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

## Pyro vision's facts move to TF2.Tests

Added 2026-10-12 by the C7 lane (client-r-c7), at the integrator's direction.

`src/tf2_descent/tests/PyroVisionTests.cs` holds the facts for TF2.Client's `PyroVision`. Main has no test project for `src/tf2` yet. The TF2 lane's stage0 branches add `src/tf2/tests/TF2.Tests.csproj`, and once that lands on main the file moves there, unchanged apart from its namespace and the fixture lookup.

PyroVision's `pyro_*` convars are TF2.Client's own declarations since viewpostprocess.cpp left (`PyroConVars.cs`); it reads them by name.

## One managed input system: ClientInputDevices and the UI's InputSystem

Added 2026-10-11 by lane c6-input, on its port onto main 2eafcb77.

The client binds the engine's IInputSystem (`InputSystemVersion001`, the generated
`InputSystemVtbl`) twice, each acquiring it through the app-system factory on its own:

- `ClientInputDevices` (`src/client/interop/Input/ClientInputNative.cs`, the game input's
  `IInputDevices`): raw mouse accumulators, the joystick axes, rumble, the analog pad and
  the Steam Controller (over libsteam_api's flat API).
- `InputSystem` (`src/client/interop/InputSystem.cs`, `IVtableInputSystem`): only
  `ButtonCodeToString`, for the UI's key bindings form (`EngineKeyBindingStore`,
  acquired by `ClientModule.AcquireInputSystem`).

Fold them into one service registered in the client container, with the two contracts as
its faces (or `IVtableInputSystem` as one more member of the input's device surface), so
the vtable is acquired once, a missing input system is reported once, and the UI and the
game input cannot disagree about whether there is one. Pure plumbing, no behaviour change:
the facts to keep green are `InputStockOracleTests` (the device reads in the replay) and the
key bindings form's `ui-keys` live step.

## phys_thruster scale and physics_cannister thrust: stock's compute-once shape: closed

Closed 2026-10-12 by lane C4 (plan_physics_bones_perf). phys_thruster is CPhysForce's
ForceOn / ForceOff / ScaleForce: SetupForces runs at each ForceOn (not at attach), the
answer is kept (`_linearSave`/`_angularSave`, stock's m_linearSave/m_angularSave, from
SetForceAtPoint's return) and `scale` scales it without decomposing again, switching the
force on first if it is off; Activate on a running thruster does nothing.
physics_cannister computes its thrust once at the light in its own frame
(CalcThrust; SetForceAtPoint with localSpace) and its think only moves the jet.
Facts: ThrusterStockTests (4 new or rewritten), CannisterStockTests
`The_thrust_is_computed_once_in_the_cannisters_own_frame`. Left as it was: phys_torque
takes its axis at attach rather than at each ForceOn and keeps `_scale` across an
off/on, both smaller than the thruster's difference and not seen by any map here.


plan_physics_bones_native_zero 2.4 made phys_thruster / phys_torque stock's
CPhysForce: an acceleration, the frame taken at activation, the answer in the
object's frame under SF_THRUST_LOCAL_ORIENTATION. Two small differences remain:
a non-local thruster's `scale` input re-decomposes at the attached object's
CURRENT pose (stock scales the activation answer, ScaleConstantForce), and
physics_cannister still re-issues a world force every think where stock's
CThrustController::CalcThrust computes once in the object's frame. The
force-at-a-point setter now converts a local push, so both can move to
stock's shape.

## phys_convert converts nothing: port stock's CreateSimplePhysicsObject (done 2026-10-12)

Done: SourceSharp.Server's PhysicsConversion is stock's InputConvertTarget body,
CreateSimplePhysicsObject and TransferPhysicsObject (PhysicsConversionTests); the
native stub and its EntityConvertToPhysics row are gone. The object moves between
shells through EntitySwapPhysicsObject. Seen live since 2026-10-12: ss_sandbox's
physbrush station converts a hanging func_brush and a hanging prop_dynamic, and
tools/live-gate's phys-convert step checks the fall, the removal and the name.

## Static props are unowned on the solver's side, where stock owns them by the world: closed

Found by plan_physics_bones_perf lane B (its branch adds this entry too: whichever lane
lands second keeps this closed form). Stock's PhysCreateWorld hands the world entity to
`CreateVPhysicsRepresentations` (physics.cpp:422); ours passes NULL, so every static
prop was an unowned side, ShouldCollide answered 1 before any rule and
ShouldSolvePenetration never ledgered a pair with one (lane B's dump at 532c1a078:
10,444 of 15,827 asks had an unowned side). Closed 2026-10-12 by lane C8:
PhysicsSolver.OwnerOf treats a static object with NULL game data as the world's (an
object that is not static stays unowned, as stock's NULL entity); the game data itself
is unchanged, so no other reader of it moves. Facts: PhysicsSolverTests' static-prop
four (a not-solid entity and a push mover refused, a crate kept, a non-static NULL
owner still unowned). `/counters?group=physics` gains `collideUnowned`.


Closed 2026-10-12 by lane C2/C6 (plan_physics_bones_perf). The movetype of a
non-player entity holding an object is managed's now: EntityPhysicsCreation writes
the engine block's MoveType word (VPHYSICS for a moveable object, as
VPhysicsInitNormal; NONE beside a static one, as VPhysicsInitStatic leaves a
prop_dynamic's), PhysicsConversion writes VPHYSICS on the copy it hands an object to,
and standalone_movetype.h reports that word (gtest
`StaticObjectKeepsTheEntitysOwnMoveType`, red before). phys_convert's IsStatic
workaround went back to stock's plain movetype test.


Found 2026-10-12 by the phys-convert gate step: standalone_movetype.h reports
VPHYSICS for any non-player entity holding an object, static or not, while stock's
prop_dynamic keeps MOVETYPE_NONE beside its static/shadow object. PhysicsConversion
now refuses only a moveable object (55b9e7e81); the report itself, and its other
readers (the collision rules' MoveType checks), still see a solid prop_dynamic as
simulated. Moving the rule to managed with stock's movetypes is the fix.


SourceSharp_EntityConvertToPhysics (shell_physicsobjects.cpp) answers 0: its
SDK arm was always NULL and had no standalone arm, so a map's phys_convert
fires OnConvert and leaves the target alone. The user's ruling is that a dead
stock arm is PORTED, not deleted. Stock (physobj.cpp CPhysConvert /
CreateSimplePhysicsObject / TransferPhysicsObject) makes a
simple_physics_brush or simple_physics_prop (both already managed classes),
moves the physics object over or recreates it, applies the mass override,
moves the name, pose parameters and children, and removes the original. Most
of it is existing managed calls; the object transfer between shells is the
one native step to design. Found by plan_physics_bones_native_zero 2.4.

## PhysicsCollisionResponse.Drain has no caller: closed

Closed 2026-10-12 by lane C5 (plan_physics_bones_perf): `Drain`, `Drains`, the
`drains` lines of `cs_sv_physics_events` and `/counters?group=physics` and the
three facts that only drove `Drain` are gone; the kind-count facts drive
`Respond`, and `The_response_has_no_drain_entry` pins the absence.


Added 2026-10-11 at plan_physics_bones_native_zero 2.3 (the event ring's
deletion). `PhysicsCollisionResponse.Drain` and its `Drains` counter were fed by
native's ring through `IServerModule.OnPhysicsEvents`; both are gone, so
`cs_sv_physics_events` and the debug counter group print `drains 0` forever.
Delete `Drain`, `Drains`, the two print sites and the five
`PhysicsCollisionTests` facts that drive `Drain` directly (their kind-count
halves belong on `Respond`).

- **hot-reload's door probe flakes on main** (2026-10-12): "the door rose to
  stand open before the reload (timeout in 399 of 399 ticks)" -- the func_door
  never moves. A/B at fafe611f1 (main) vs 8be875b6d (physics 2.3/2.5),
  alternating 5 x 3 passes of join,move,jump,trigger,push,hot-reload on one
  box: 2/15 failing passes each side; with the lane's own runs 3/33 vs 7/33
  (Fisher p ~0.3). Not caused by the physics lanes; the door's open never
  starts, so look at the step's use/input path before the reload.
  — closed 2026-10-12 by lane C7 (plan_physics_bones_perf). Cause, from
  failure-only diagnostics: the door is built by ent_create at the client's
  LAST-SENT aim, which drifts, and in the failing passes its origin stood
  56.4 / 67.7 units from the player (75.6 in passing ones), so the brush
  overlapped them; blocked at once, the wait -1 door bounced OnOpen /
  OnBlockedOpening / OnClose / OnBlockedClosing and never rose. Two fixes:
  MovingBrush no longer reverses a blocked door with a negative wait (stock's
  CBaseDoor::Blocked; BrushTests), and the step moves the player clear of the
  door before the Open (reload_player_clear_of, self-test rows). Rates on
  this lane, 6 x 3 passes of join,move,jump,trigger,push,hot-reload each:
  before 4 of 18 passes red on the door, after 0 of 18 (4 of the 18 in the
  failing geometry, all green).

- **C7 particles: where the work stopped (2026-10-18), and what is next.**
  Landed:
  - phase 1: ParticleSphereProxy is C#'s; particle_proxies.cpp left client.so;
  - 2.1: the stock old-style manager, recorded (particlemgr-stock.tsv.gz);
  - 2.2: ParticleBinding, held to that recording;
  - 2.3: the ABI commit. ClientParticleManager owns the old-style bindings,
    and the native executor is its own renderable.
  - phase 3: the new-style effects are managed's. The stock recording
    (particlemgr-new-stock.tsv.gz), NewParticleEffect held to it, and the ABI
    commit: NewParticleManager decides each frame (IManagedParticlesClient::
    NewEffectsFrame, one crossing) and each draw (NewEffectDraw); the library
    steps every listed collection in one batch call (ISSParticleLib::
    CollectionSimulateBatch, reached through SSFwd_ParticleLib). The native
    shell is CShellNewParticleEffect (shell_new_particles_client.cpp), a
    renderable over the library's own CParticleCollection: CNewParticleEffect
    is no longer constructed. User ruling 2026-10-18: the library keeps all
    simulation, operators and rendering; it is never ported.

  Phase 3's divergences from stock (all recorded in the ABI commit):
  - a removed effect leaves the leaf system at once and is deleted when its
    last CSmartPtr lets go (stock deleted it at once, whoever held it);
  - a retirement's replacement (cl_particle_retire_cost, a cheat, off by
    default) is started after the frame and steps from the next one (stock
    replaced mid-frame and stepped it in the same frame);
  - r_threaded_particles is not read: the batch is single-threaded (the
    spike's measurement below);
  - Release's finished test is made at the next frame, before anything steps
    (stock made it at the release; nothing simulates in between);
  - the tool framework's recording messages are not sent (no recording here);
  - cl_particle_stats_trigger_count's spew is not run (stock's stats
    functions are empty in this tree).

  The phase 3 spike's decision record:
  - The particle library has no game-side reach. Neither
    libss_particles.so nor particles.a names CNewParticleEffect,
    IClientRenderable or C_BaseEntity (`strings`: 0 hits).
    CParticleCollection declares no virtual method, so the library never
    dispatches into the derived class.
  - The only game code that reads a collection is our IParticleSystemQuery
    (shell_managed_particles.cpp). It reads m_ControlPoints[n].m_Position,
    and m_pObject only as a non-null test. Both are library-owned fields.
  - Threading: measured, not assumed (ebe389584, debug -O0). 30 env_fire
    systems with about 1,090 particles simulate in about 0.1 ms a frame
    single-threaded (median 102 us, p95 152 us). ParallelProcess is no faster
    (median 97 to 141 us) and its first frames spike to 15.6 ms. Decision:
    managed builds the sim list and makes one single-threaded batch call
    into the library. No Parallel.For.

  - phase 4: particlemgr.cpp and particles_new.cpp left client.so. What the
    client still called of CParticleMgr (the PMaterial table, the draw
    state, the library's setup) is shell_particle_mgr_client.cpp; the live
    convars are ParticleConVars.cs's and the commands ParticleCommands.cs's
    (cs_cl_particle_report reads the dev API's per-system rows, which do not
    carry the highest control point stock printed, nor a cull replacement or
    a system no definition answers). Dropped as unread:
    particle_simulateoverflow, r_threaded_particles, particle_sim_alt_cores.
    tier1's utlsymbol left client.so with particlemgr.cpp.

  Next: nothing in C7 particles. The particle library (libss_particles.so)
  keeps all simulation, operators and rendering (user ruling 2026-10-18).

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
- **The old-style particle draw's wireframe pass reuses the first pass's
  quads** (C7 particles phase 2.3). Stock asked the sim to render twice under
  mat_wireframe; the managed sims' draws are pure, so the second pass draws
  the same quads with debug/debugparticlewireframe and hands its keys to a
  second sort, as stock did. The old effects' cl_particle_stats counts
  (StatsOldParticleEffectDrawn) are not kept; those commands move with
  particlemgr.cpp in phase 4.

- **hot-reload loses spectate's ragdoll** (2026-10-12, client-r-c7 b19a8c48e,
  client-only diff). In `tools/live-gate --steps
  join,move,render,render-plan,render-plan-sandbox,view-camera,spectate,hot-reload
  --passes 2`, spectate runs just before hot-reload and its death leaves an
  ss_ragdoll. The reload then fails 2 of 2 passes:
  - census 477 against 478, "less 0 gone on their own schedule";
  - "1 lost (first 397, a ss_ragdoll)";
  - the report shows 476 of 476 carried.

  The ragdoll goes during the reload without being counted as leaving on its
  own schedule, or is not carried. `join,move,hot-reload --passes 3` (no
  death first) shows no loss. Server-side: check the ragdoll's fade/removal
  across `reload` and the step's own-schedule count.
  Closed 2026-10-18 (lane oi1): the reload lost nothing. The body's 15 s
  Lifetime (SsRagdoll) ran out between the step's census read and its think
  reads, 1.5-2 s of game apart, so it removed itself BEFORE the reload and was
  in no booking the allowance read. Reproduced at 43171f0c1 1 of 2; with the
  fix the step reads the think heap with the census
  (reload_vanish_allowance_over) and logged the corpse due 90 ms and 240 ms
  after that read in 2 of 3 passes, 3 of 3 green. Self-test rows, mutation-
  checked (only the last read: 1 failed).

## impact-damage: the dropped crate read no hurt once: closed

Closed 2026-10-18 (lane oi1). Rate at 6fc08d40e: 3 of 30 passes red
(`tools/live-gate --steps join,impact-damage --passes 3`, 10 runs). The cause
is the step: the player is teleported 120 up with no settle and is falling
when the aim is read, so the crate made over the aim followed it down. The
crate was made at z 189.0-214.9; the three made under 199 (189.0, 198.72,
198.72) landed unhurt, every one over 204 was hurt: too slow a landing for
the damage table's first step, which stock refuses too. The step now puts
the player's fall back on the crate's height (fall_drop_origin; /player/aim's
"eye" is the player's origin): 30 of 30 green after, the crate made at z
216.0 every pass (the player read at 111.8-120 when the aim was taken; a run
with the field misread as the eye saw it at 93-110). Self-test rows,
mutation-checked.

Added 2026-10-11 from the integrator's scratch list. The nightly gate at
main 7b2ec8cd failed `impact-damage` 1 of 2 at night ("the crate dropped
from height was hurt by landing: []", an empty hurt list), then passed 0 of
3 red on confirm, so it was ruled FLAKY and not bisected. No other report
since. Wanted: a rate (`tools/live-gate --steps join,impact-damage --passes
10` on main) and, on a red, the crate's landing speed and what it landed on;
the gibs-in-the-path history of this step and knock-loose is in
docs/plans/gate_flakes_findings.md.

## view-camera: monitor passes and views drawn disagreed by one too many once: closed

Closed 2026-10-12 by `live-gate view-camera: monitor passes and views drawn read in
one snapshot` (client-novalve 3eff2b3de, carried in batch 4): the plan's monitor passes
and native's drawn count now come from one game-thread snapshot
(`/client/{slot}/viewcam`) and must be equal, with no tolerance. The gap was the
window skew between the two routes; 3 live passes read all 18 checks equal.


Added 2026-10-12 from client batch 3's re-gate, at cn-batch3-squash
362fc63d0 (landed as main 466d9e12c). The daily gate (scratch state) failed
`view-camera` on night pass 2: "round 3, cs_cl_overview 2: the render plan
drew them as monitor passes (34 passes, 27 drawn)". The check allows
`abs(passes - drawn) <= max(4, drawn // 4)` (tools/live-gate, the two
counters are read through two routes a few frames apart), so 27 drawn
allows 6 and this read 7. Then 0 of 3 red on confirm, and 0 of 6 red in
`tools/live-gate --steps view-camera --passes 6` (31 of 34 drawn there):
1 red in 11 passes at that sha, against 2 of 2 green at cf35c0924, the
squash before the post-processing commits. Not bisected. Wanted: a rate
over more passes, and whether the gap is the window skew the tolerance
assumes (read both counters on one route, or in one frame) or monitor
passes the plan queued but did not draw.
## The native IGameResources (g_PR) has no reader on the client link line
## The native IGameResources (g_PR) has no reader on the client link line: retired

Retired 2026-10-11 by lane c6-input: no importer on the link line and no engine binary names the interface; shell_playerresource.cpp/.h left client.so (docs/client_evictions.md).

Added 2026-10-11 by lane c6-input, at d40df095 (client Steam to managed).

`CSourceSharpGameResources` (`src/client/native/shell_playerresource.cpp`) still
publishes `g_PR` every frame from the managed player resource's bytes, but no object on
client.so's link line reads `g_PR` or `GameResources()` (grep over the compiled sources:
its own file only; the VGUI panels it was kept for are not compiled). Its `GetPlayerName`
now returns the engine's name UNFILTERED, since the Steam filter is managed's
(`IPlayerNames`). Confirm no reader through the vtable (the engine does not ask for
`IGameResources`), then retire the object and its per-frame system, or record why it
stays.

## C_StandaloneEntity's six IGameEntity slots: drop them when the last relay site leaves (CLOSED 2026-10-19)

Added 2026-10-11 by lane c6-input (plan_client_novalve C8), owner "physics and bones".

`C_StandaloneEntity` still derives from `IGameEntity` and carries its six slots
(`ShouldCollide`, `Touch`, `StartTouch`, `EndTouch`, `VPhysicsUpdate`,
`GetCollisionGroup`) only because `game_entity_relays.h` still has client call sites
(docs/client-shell.md 2.2). At e6c78c1ec the sites left are:

- `game/client/ragdoll.cpp`: three `GameEntity_ModelIndex` calls (fork file);
- `src/shared/native/physics_shared.cpp`: one `GameEntity_ModelIndex` call;

and the relay definitions in `shell_standalone_client.cpp`. Whoever removes the last
site drops the slots and the `IGameEntity` base from `C_StandaloneEntity`, the relay
definitions, and the header from the client (the server compiles it too).

- **Relays done, slots still open** (2026-10-18, the client physics leftovers): the
  last two sites went (fork ragdoll.cpp in Phase 3, physics_shared.cpp's in 2.6, fork
  physics.cpp's PhysicsOwnerOf/VPhysicsUpdate with the client physics flip); the
  three client relay definitions and the header's client include are deleted
  (GameEntityRelayEvictionTests). The six slots and the `IGameEntity` base stay for
  a different reason than the one above: the client's native code still uses
  `IGameEntity` as its entity type -- `CEffectData::GetEntity`, the impact effects'
  `GetImpactDecal`/`PlayImpactSound`/`ParseImpactData`, the entity list's
  `SourceSharp_ClientSdkEntityFromIndex/Handle`, the tempent pool's resolve, and
  `GetGameEntity()` itself. Dropping the base means retyping those to
  `C_StandaloneEntity` or `IClientEntity` first. VPhysicsUpdate, Touch, StartTouch
  and EndTouch have no client caller left (physics.cpp's active-object loop and
  touch dispatch were the last); ShouldCollide and GetCollisionGroup are asked by
  native's collision paths through ICollideable/IGameEntity.

- **Inventory, 2026-10-18 (lane claude/tail-skeleton-standalone): the base needs
  fork edits.** Fork code on client.so still uses `IGameEntity` as the
  entity type, so dropping the base would compile but answer NULL there:
  icliententity.h's `GetGameEntity()` and iclientunknown.h's `GetBaseEntity()`;
  util_shared.h's `GameEntityFromEntityHandle` and c_baseentity.h's
  `SDKEntityFromRenderable`; flashlighteffect.cpp's trace filter (it asked
  `GetCollisionGroup`; the file left client.so 2026-10-18, and the managed
  flashlight's filter resolves the hit by handle through the slot table,
  flashlight_trace_filter.h, with no IGameEntity); clientleafsystem.cpp (`GetClassname`); and particles_new.cpp
  (the owner, the control-point entity and the view-entity walk). Fork
  interfaces also fix our implementations' types: DispatchParticleEffect,
  CTakeDamageInfo/CMultiDamage, KillDeadBeams, ClientProjectile, the te
  forwards, CEffectData::GetEntity. Ours to retype to `C_StandaloneEntity *`
  once those go: `SourceSharp_ClientSdkEntityFromIndex/Handle`, the impact
  effects (GetImpactDecal, SetImpactControlPoint, PlayImpactSound,
  ParseImpactData, DispatchNewParticleImpact) and the new particles'
  Set/GetControlPointEntity. These last are ca's particles lane's files. The base
  can go when particles_new.cpp, flashlighteffect.cpp and clientleafsystem.cpp
  leave client.so, or after a user-made fork retype. (2026-10-18: the first
  two have left. clientleafsystem.cpp's one use, `SDKEntityFromRenderable` /
  `GetClassname` in AddRenderableToLeaf, is inside `#ifdef
  DUMP_RENDERABLE_LEAFS`, which nothing defines: the TU preprocessed with its
  compile_commands.json line holds neither name in its own text, nor does any
  of the other seven fork TUs client.so compiles name IGameEntity,
  SDKEntityFromRenderable or GameEntityFromEntityHandle. sdk_player_cast_test
  still counts that site, because its guard stack does not know the macro is
  undefined. The client's compiled IGameEntity users are ours:
  trace_filter_library.cpp's GameEntityFromEntityHandle.)

- **CLOSED 2026-10-19 (lane ige, base main 6b0be9239): the base and the six
  slots are gone.** `class C_StandaloneEntity : public IClientEntity, public
  ICollideable`; GetGameEntity() is IClientEntity's NULL; a static_assert in
  shell_standalone_client.cpp refuses the base (shown failing before the drop),
  and client_entity_resolve_test's TheClientShellIsNoGameEntityAndTheBuildSaysSo
  pins it. GetCollisionGroup stays, ICollideable's. What was measured first:
  - With the base dropped and nothing else changed, the client build had TWO
    errors (precipitation's ShouldCollide, SourceSharp_ClientShellOf); every
    other user would have compiled and answered NULL. nm over client.so's 141
    link-edge objects plus libss_particles and libss_fwd_client found the
    GetGameEntity() readers: TraceGameEntity (shell_movement.o, the client
    ground; shell_impact_effects.o, the impact control points), the client
    partition (beam render, the entity list's SDK arms, the particles' and
    takedamageinfo's CHandle<IGameEntity>::Get, clientshadowmgr.o's EHANDLE).
    Preprocessed with their compile_commands.json lines, the eight fork TUs
    client.so compiles name none of IGameEntity, GetGameEntity,
    TraceGameEntity, GameEntityFromEntityHandle, SDKEntityFrom* in their own
    text; clientshadowmgr.cpp's three EHANDLEs are m_hTargetEntity, never set
    (cpp_alias_t5_test's FlashlightTargetVacuity).
  - The two LIVE readers were retyped first, onto the slot table:
    SourceSharp_ClientShellFromTraceHit (handle_cross.h's
    SourceSharp_CrossHandleToClientShell; handle_cross_test's ClientShellCross,
    mutation-proved) serves movement_ground.h's client arm and the impact
    effects' three SetImpactControlPoint calls. The control-point owners became
    a CBaseHandle from the shell's GetRefEHandle(), which fixed a defect: the
    EHANDLE was assigned from an IGameEntity *, which networkvar.h's
    SourceSharp_EntityHandle refuses (Warning, AssertMsg, invalid handle) for
    every managed entity a bullet hit.
  - SourceSharp_ClientSdkEntityFromIndex/Handle were deleted, not retyped: they
    asked GetBaseEntity(), NULL from every client implementor, so they answered
    NULL for every entity. The brief's premise that they reached the real
    entity was false at 6b0be9239; their callers now say NULL themselves.
    SourceSharp_ClientShellOf had no caller and went.
  - **Fork-declared signatures our files implement, kept with IGameEntity and
    decided per caller** (no client caller passes a client entity to any):

    | signature (declared in) | ours | client callers | decision |
    |---|---|---|---|
    | CEffectData::GetEntity (effect_dispatch_data.h) | shell_effect_dispatch_client.cpp:176 | ParseImpactData | returns NULL (the SDK arm it asked was NULL for every entity) |
    | ParseImpactData (fx_impact.h) | shell_impact_effects.cpp:787 | shell_impact_effects.cpp:1085, return discarded | returns GetEntity()'s NULL |
    | GetImpactDecal (fx_impact.h) | shell_impact_effects.cpp:543 | none (nm: defined, imported by no client object) | entity ignored, as before |
    | PlayImpactSound (fx_impact.h) | shell_impact_effects.cpp:722 | shell_impact_effects.cpp:1157 passes NULL | entity ignored, as before |
    | DispatchParticleEffect x2 (particle_parse.h) | particle_manifest.cpp:352,383 | shell_effects_list_client.cpp (NULL), shell_impact_effects.cpp (no entity) | entity ignored; the effect's entity is cleared on this side |
    | C_TempEntsSystem::Fizz, ::ClientProjectile (itempents.h) | shell_te_forwards_client.cpp:287,317 | none | empty bodies |
    | CTempEnts::ClientProjectile (c_te_legacytempents.h) | shell_legacy_tempents_client.cpp:504 | none (nm: no importer) | the existing refusal |
    | IViewRenderBeams::KillDeadBeams(IGameEntity *) (iviewrender_beams.h) | shell_beam_render.cpp:257 | none; the handle overload is the live one | empty |
    | IClientUnknown::GetBaseEntity (iclientunknown.h) | shell_standalone_client.h:325, shell_detail_props.cpp:111, shell_tempents_client.cpp:136 | the fork's SDKEntityFromUnknown | NULL, as before |
    | CTakeDamageInfo / CMultiDamage (takedamageinfo.h) | takedamageinfo_library.cpp:130-232 | ClearMultiDamage's NULLs only (nm: no client importer of the entity forms) | unchanged; ApplyMultiDamage's entity arm is server-only |
    | CGameRules::ShouldUseRobustRadiusDamage (gamerules.h) | shell_gamerules.h:208 | none on the client | returns false |
    | CSaveRestoreData::m_pCurrentEntity (saverestore.h, engineabi) | the layout | none on the client | a held pointer's type in an engine layout; untouched |

    Shared declarations that name the type and compile on the client without
    being used there: physicsowner.h, physicslink.h, imovementplayer.h's
    MvGetObserverTarget, and the `#ifndef CLIENT_DLL` arms of
    movement_ground.h and trace_filter_library.cpp. The fork headers keep
    using IGameEntity (c_baseentity.h's EHANDLE, CHandle<IGameEntity>::Get,
    SDKEntityFromUnknown; util_shared.h's TraceGameEntity), all of which now
    answer NULL for every client object -- retyping them is a fork edit.
- **Queued (2026-10-19): the new-style effects' camera object.**
  shell_new_particles_client.cpp passes CameraObject 0 (stock: the
  C_BaseEntity at render->GetViewEntity()), so managed NewParticleEffect's
  skip-render test (a first-person view hiding its own control-point entity's
  particles) is never taken. Feeding it SourceSharp_ClientShellFromIndex(
  render->GetViewEntity() ) makes it live, with SkipObject, which is the shell
  pointer since lane ige: a behaviour change that wants its own stock
  recording first.

- **Two m_oldOrigins where stock keeps one** (found 2026-10-12 by the
  damage-message diagnosis): native's m_vecOldOrigin is set per usercmd and
  PlayerShadowRecovery keeps its own last-valid memory; neither updates the
  other. Stock's CBasePlayer::VPhysicsShadowUpdate stuck branch
  (player.cpp:8455-8470) falls back to the one m_oldOrigin. Unify them on the
  managed side.
  — closed 2026-10-12 by lane C1 (plan_physics_bones_perf): PlayerMoveMemory
  holds the one m_oldOrigin; PlayerShadowBodies writes it after each command
  and the plan reads it, PlayerShadowRecovery reads and writes the same
  instance and carries it across a reload; native's m_vecOldOrigin and the
  facts' vecOldOrigin are gone.

## -assertstack covers one DLL since the client's spew slot is its own (2026-10-12)

`SourceSharp_InstallAssertStacks` (shell_diagnostics.cpp) claims `-assertstack`
once per process through `SOURCESHARP_ASSERTSTACK_CLAIMED`, on the reasoning
that tier0 was shared. Since C9 tier0 both server.so and client.so own their
assert-notify slot (tier0_spew_port.cpp), so only the DLL that initialises first
prints native backtraces. Install per DLL (each has its own slot) and keep the
one-per-process claim only for the terminal line.

## Probe build in the daily gate (proposed 2026-10-12, for the Native Server Plan session)

`SS_CLIENT_PROBES=ON` (tools/probes/client) stopped linking when Steam left
client.so's native side and nothing noticed until C7 needed it (fixed in lane
input's "probes: the html probe goes" commit). A configure-and-link of the probe
build in its own build tree, once a day in tools/daily-gate, would catch the
next one. It is a full client compile, so not per commit.

— closed 2026-10-19, in the full tier instead of the daily gate: it rotted
again (13 undefined symbols at 4e8970d95, when tier1/vstdlib,
sound_emitter.cpp and particles_new.cpp left client.so), and
`tools/client-probe-link` is now a `full` row of tools/checks.tsv (make
test-full). It configures farm/debug/build-probes (deploying into the tree,
never bin/debug), links client_sourcesharp, and needs every probe command the
SS_CLIENT_PROBES block's sources define in the linked client.so. Cost on this
32-core box: cold 4.1 s wall / 100 s CPU (189 build steps), warm 0.1 s; the
full checks tier's slowest row stays live-gate's self-test (4.2 s). A header
change recompiles the probe tree with the normal one, so a test-full after
one pays the cold cost again.

- **The last native physics decisions** (plan_physics_bones_native_zero Phase 4
  census, 2026-10-12): CStandaloneEntity::UpdatePhysics's asleep-pose skip and
  StandaloneEntity_Velocity's solver-or-member pick could move with a batched
  design; StandaloneMoveType_Report's VPHYSICS-for-any-object (a stock
  divergence) and the player move-trace's touched-physics-object check belong
  to the movement lane.
  — closed 2026-10-12 by lane C2 (plan_physics_bones_perf): the owner update
  writes every active object as stock's PhysFrame (the native asleep-twice
  skip went; vphysics lists an object asleep only on its freeze frame); the
  ground's velocity is a held value managed's PhysicsFrame hands over in the
  owner batch (shell_velocity_home.h deleted); the movetype is the block word
  managed writes (VPHYSICS only beside a moveable object); and stock's
  CBasePlayer::Touch over the move's touch list is
  PlayerShadowBodies.TouchedMoveablePhysics (native records the list).

- **Lane C leftovers** (2c29c2af3): a SetVelocity on a sleeping object reaches
  the movement fill's ground read at the next physics step (one tick at most)
  where the old native pick read the solver live; impacts and stress still read
  static props as unowned (C8 changed only the solver's owner lookup).
  (a) closed 2026-10-18 (lane oi1): stock's ground read is the member
  (`ground->GetAbsVelocity()`, gamemovement.cpp:3847) and its velocity set,
  Teleport, writes the member and the object together (baseentity.cpp:4947,
  :4970), so a rider sees it the same tick. EntityPhysicsMotion.SetVelocity
  now writes the entity's held velocity beside the object's
  (EntityPhysicsMotionTests.SettingASimulatedVelocityHoldsItForTheGroundReadAtOnce).
  (b) closed 2026-10-18 (lane oi1), per reader against stock: PreCollision's
  held-object raise now counts a static object with NULL game data as the
  world, an entity that is no player (physics.cpp:1836), so a carried object
  striking a static prop is raised as against the world; PostCollision
  already names a static prop's side as it names the world's (no entity,
  static), fact pinned; the stress walk puts every unmoveable contact in the
  static slot before it reads an owner (physics_impact_damage.cpp:541), so a
  static prop is the world floor there whoever owns it: no code, fact pinned.
  Not changed, and not static-prop specific: our world is no entity in the
  contact-touch pairs and friction, where stock's is CWorld. Closed 2026-10-18:
  the contact-touch owners name the world's side (a static object with NULL
  game data: the world's own object or a static prop's) the world entity,
  found by classname (PhysicsWorldEntity), and EndTouch's contact count uses
  the same rule (PhysicsCollisionEventsTests; red 3 under the old rule).
  Friction needs nothing: vphysics raises it only for an active object, so the
  world is never its subject and is only the hit surface. Reachable live only
  by an object with CALLBACK_GLOBAL_TOUCH_STATIC, which nothing in this build
  sets (stock's crate on the floor touches nothing either; the contact-touch
  step measured A(395) touching only B(396)). — closed 2026-10-18 (lane cg):
  the flag is set wherever stock sets it on an entity this build has. The
  client's C_PhysPropClientside (physpropclientside.cpp:271) is
  ClientPhysicsProps, which sets it; C_Gib (c_gib.cpp:76) has no managed
  twin because nothing here can make one: stock HL2MP compiles c_gib.cpp and
  hl2/fx_antlion.cpp, but its only caller is the "AntlionGib" client effect
  (fx_antlion.cpp:334), which only CNPC_Vortigaunt::HandleAnimEvent sends
  (npc_vortigaunt_episodic.cpp:921; npc_antlion's "AntlionGib" is a particle
  system), and this build has no vortigaunt, so it waits for an NPC port;
  on the server none of grenade_tripwire, combine_mine, grenade_brickbat,
  npc_rollermine or npc_attackchopper exists in src/server/managed, so the
  server's world-as-CWorld path still has no live subject. The client's own
  StartTouch/EndTouch slots, which only counted, now run stock's client path
  (ClientContactTouches: the same world rule, buffered to UpdateTouchEvents,
  StartTouch once per held pair; the prop's StartTouch/HitSurface with its
  0.1 s limit and PROPINTER_WORLD_BLOODSPLAT's blood decal). Live (headless
  dm_lockdown, cs_te_fire physicsprop at z 120): SSCLPHYSTOUCH starts=18
  world_starts=18 dispatched=10 by_world=10 ends=14 held=2, prop
  surface_hits=7 -- every reported pair had the world entity as the toucher.
- **Physics paths never measured in release** (user 2026-10-12: noted, not
  queued): the managed physics frame per tick (0.161 vs 0.137 ms, debug, at
  physics 2.3/2.5), the player shadow apply (1.8 vs ~0.2 us, debug, near the
  instrument's resolution), and client ragdolls in game after warm-up (9-12 us
  per ragdoll-frame including JIT; lane B's bench says ~3.9 us per body).
  Closed 2026-10-19 (lane rel-physics-measure, at 75852d9e7): none of the
  three is significant in release. Instruments: `cs_sv_physics_cost`
  (physics.frame / .simulate / .managed = frame less its solves,
  shadow.step / .apply, timer.empty) and `cs_cl_ragdolls`' clragdoll.step /
  .write rows, all on CostMeter (two Stopwatch timestamps a call; the
  timer's floor read 20 ns median, at 1 ns ticks). Load: release, listen
  server, dm_lockdown with 124 prop_physics (8 crates dropped every 2 s),
  the player strafing; then a death every 2 s for ~4 client corpses.
  40 s warm-up, five 10 s windows per phase; figures are the quiet runs
  (load average under 3.2, one idle peer game), as the median of the
  windows' medians / p95s, and the overall mean:
  - physics frame per tick: 132-182 us median (p95 252-295), of which the
    vphysics solve 96-139 us and managed's own share 36-43 us median (p95
    51-58, mean 36-43): 0.24-0.29% of the 15 ms tick. Native comparator
    (pre-switch tree 2f4a07f1d, release, same load, clock_gettime around
    CPhysicsHook::FrameUpdatePostEntityThink less its Simulate): 31 us
    median, 33 us mean. Managed adds ~5-10 us a tick (~0.05% of it).
  - player shadow, per command: step (probe, plan, apply) 0.8-1.0 us
    median, mean 2.0-2.7 us; apply alone 0.23-0.28 us median, mean
    0.9-1.0 us. Native-era tree (native probe and apply behind a managed
    plan): step 0.35-1.2 us median, mean 1.4-1.5; apply 0.08-0.21 us
    median, mean 0.24-0.59. About 1 us more a command, ~0.01% of a tick.
  - client ragdolls: 1.28 us per body-frame for the step (4.4 us a frame at
    4 bodies) and 1.23 us median (mean 1.41) per bone write: ~2.7 us per
    ragdoll-frame against debug's 9-12; 4 bodies are ~11 us of a 16.7 ms
    frame (0.07%). No native comparator (the pre-switch tree is the
    server's; the client bodies' switch is another commit).
  Under a busy box (load 15-32, runs at 05:29-05:51) both sides inflate
  alike: managed's frame share up to 84 us median, native's to 68 us.

- **Client sequence transitions are not blended** (found 2026-10-12, when
  fork sequence_Transitioner.cpp was found gc-dropped from client.so): stock
  C_BaseAnimating::StandardBlendingRules (c_baseanimating.cpp ~1828-1860) runs
  m_SequenceTransitioner (CheckForSequenceChange, UpdateCurrent, then each
  queued previous sequence accumulated at its fading weight), so a sequence
  change blends the old one out. ClientBoneDriver has no transitioner, so a
  client-side sequence change pops. A dead stock arm: port it to managed
  (CSequenceTransitioner in game/shared/sequence_Transitioner.cpp), with facts
  from stock, per the 2026-10-01 ruling.
  Closed 2026-10-12 by lane seq-transitions: SequenceTransitioner
  (src/shared/managed/Bones), one per BoneEntityState, run by
  ClientBoneDriver between the base sequence and the layers; parity is
  SequenceStarts, the no-interp frame the block's InterpolationFrame; facts
  in BonesSequenceTransitionerTests. Prediction never builds bones here,
  so the in-prediction arm is ported but passed false.

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

## QueueOverlayRenderView is not shipped (C7 step 3, 2026-10-12)

CViewRender::QueueOverlayRenderView kept a view that stock's RenderView then
drew after the 3D scene (before the 2D half), its FOV widened to the view's
aspect and with bloom off. Nothing in this build calls it (no caller in our
code or the fork's compiled client; the engine does not reach IViewRender),
so CShellViewRender's says SourceSharp_NotShipped instead of keeping a view
nothing would draw. When something needs it: an overlay-view arm in
RenderPlanner (the view in RenderInputs, a nested RenderView's ops after
ScreenSpaceEffects), held to viewrender.cpp's order.


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
- **Hot reload forgets the shadow handle.** LocalPlayerFlashlight holds the
  shadow manager's handle in managed state, and the module's Shutdown does
  not destroy it (whether the shadow manager is still up when the module
  shuts down at process exit was not established). Read from the code, not
  run: a `cs_cl_reload` with the light on would make a second effect and
  leave the first projected texture alive until level shutdown (the shadow
  manager frees every shadow there). The fix is a reload carry of the
  handle, as ClientBoneBatchCarry carries the bone table.
- **The debris arm of the trace filter cannot fire.** A client shell's
  collision group is not networked (C_StandaloneEntity answers NONE), so
  stock's skip of COLLISION_GROUP_DEBRIS / INTERACTIVE_DEBRIS never matches.
  It fires once the collision group reaches the client.
- **CBasePlayer::SetFlashlightEnabled** (the server's per-player "may use the
  flashlight" switch) is not ported; cs_sv_allow_flashlight is the one gate.


## Client C9 (tier libraries): what is still open

Added 2026-10-12 at client-r-input efb6197b7 on main 432d2e9f1 (lane c6-input;
the lane stopped here on the user's instruction). Measured with lld
`--why-extract` on a relink of client.so (debug), `nm -D -u` over
out/client.so, and `tools/client-novalve-census`.

- **tier1.a** (imports:tier1 2, 2026-10-18 C7 detail; was 7 at lane t1, 15 before) is down to one member linked into
  client.so (user ruling 2026-10-18: retire their importers, never port CUtlBuffer/CUtlSymbolTable).
  `utlbuffer.cpp.o` left with `game/client/detailobjectsystem.cpp` (C7 cluster 10, 2026-10-18: the detail lump
  is read by managed's DetailLumpReader); public/sentence.cpp left client.so 2026-10-18 (its arm is
  SourceSharp.Client.LipSync). What is left:
  - `utlsymbol.cpp.o`, pulled only by `game/client/particlemgr.cpp`
    (CUtlSymbolTable). It leaves when particlemgr.cpp does (C7 particles).
- **Lip sync wiring** (2026-10-18, lane t1): SourceSharp.Client.LipSync (Visemes, Sentence) is
  held to stock by LipSyncOracleTests but has no caller: CBaseFlex is in neither build. When a
  flexed client entity exists, it needs engine GetSentence / GetSentenceLength through the
  client FORWARD row (plan_engine_forwarders.md), a native reader of the engine's CSentence
  (runtime phonemes, emphasis samples), the entity's CMouthInfo voice sources, the .vfe files,
  and the phonemedelay/phonemefilter/phonemesnap ConVars (VisemeSettings.Stock holds their defaults).
  Then the ruling's live PROOF (a mouth moving) can be run.
- **tier0 and vstdlib: DONE 2026-10-18** (imports:tier0 7 -> 0, imports:vstdlib
  2 -> 0, needed-forbidden 2 -> 0): the client links neither (client.dll's
  arm, on server.dll's STATIC_TIER0/SS_BIND_IMPORT/LNK4217 pattern with
  GetProcAddress behind loaded_symbol.h, is CI-verified only).
  `Plat_FloatTime` is ported (tier0_thread_client.cpp, ss_clock.h);
  `CommandLine_Tier0`, `g_pMemAlloc`, the CThreadEvent/CThreadSyncObject/
  CThreadRWLock/CThreadSpinRWLock members, `g_pThreadPool` and
  `DestroyThreadPool` are bound to the engine's loaded copies
  (tier_bindings_client.cpp); the target is NO_WRAP like the server's.
  **Divergence (user ruling 2026-10-18):** the VPROF macros in client.so's fork
  TUs (clientleafsystem.cpp) run against client.so's own never-enabled profile
  (tier0_vprof_client.cpp), so the client's nodes no longer show in the
  engine's `vprof` report or budget panel. Still open: libss_particles.so
  still has DT_NEEDED on both (particles lane); and no live run has reached a
  client job on vstdlib's pool (the fork's threaded leaf/shadow branches are
  hard-wired off, and the mod's content defines no new-style effect the
  threaded particle branch would simulate), so that binding is held by
  tier_bindings_client_test alone.
- **flashlighteffect.cpp** was ruled `port`; it is ported and left client.so
  2026-10-18 (C7 cluster 12; entry above: "Client flashlight").

## Live gate: chat's second client exits during connect (flaky, 2026-10-12)

The daily gate at main 891126a2c ruled `chat` FLAKY: red in 1 of 2 night
passes and 0 of 3 confirm passes. The second game client exited during
`connect 127.0.0.1:<port>` ("the game process ... has exited"); the gate log
holds no backtrace or exit code for it, and the next step (spectate) started
a client 2 normally. Nothing in the batch touched chat or the second client's
connect. To do: capture the client's exit status and console on a red, and
measure a rate with `tools/live-gate --steps join,chat --passes 10`.

## Live gate: no step covers screen shakes and fades

ViewEffects (shakes, fades) moved to C# and its native stand-in left
(d0d288632); the only live evidence is the probe session
tools/probes/sessions/c7-shake-fade.steps. Add a live-gate step that fires an
env_shake and an env_fade and checks the drawn fade colour and the view's
offset and roll, with a control frame before them.

## UiAllocationTests' negative control needs a cold number-string cache (2026-10-18)

`The_measure_sees_an_allocation_in_describe` counts the strings `(n++).ToString()`
allocates for n from 60 up; .NET caches the decimal strings of small numbers
(0..299) once formatted, so any earlier test in the same process that formats
those numbers makes the control see too few bytes and fail. Found by lane t1:
LipSyncVisemesTests' `"c" + i` (i 0..383) turned it red 3 of 3 in the client
assembly, green with the LipSync facts filtered out; the facts now format in
hex. To do: make the control allocate something no cache can serve (e.g. a
fresh `new string('x', 8)` a frame, or numbers past 300).
DONE 2026-10-19 (C7 cluster 12 L2): LeafRegistryOracleTests formats the
golden's small integers and turned it red again in the full run; the control
now formats from 1000 up.


## The prop-hurts-player live-gate flake: measured, and fixed in the step

Added 2026-10-18. It failed once in a 3-pass gate on the physics 2.6 lane
("the player started at 95 health and the reading came back as None").
Measured so far: `tools/live-gate --passes 3 --steps join,prop-hurts-player`,
3 runs at 63f681f9b (lane po, before the forwarders-everywhere rebase):
9 of 9 passes PASS, 0 failures. Nine passes cannot tell a 1-in-3 rate from
zero (P(0 fails in 9 | p = 1/3) = 0.026, | p = 1/10 = 0.39), so the target
is still at least 30 passes at main's tip, each run with its own TMPDIR,
DEBUG_API_DIR and DEBUG_API_UI_PORT. Deferred until after the publish
(integrator, 2026-10-18).

Closed 2026-10-18 at 71d2be26d (debug): `tools/live-gate --steps
join,prop-hurts-player --passes 3`, 11 runs, own TMPDIR, DEBUG_API_DIR and
DEBUG_API_UI_PORT, each after `tools/box-ready gate --wait`: 33 of 33 passes
PASS, every one reading health 100 -> 95 (the player table's first step).
P(0 fails in 33 | p = 1/3) = 1.5e-6, | p = 1/10 = 0.031, so a rate of one in
ten or more is ruled out at this tip. Not reproduced, so nothing to fix. What
this subset cannot see: the original failure started at 95 health, a state
an earlier step of the full gate left, which `join,prop-hurts-player` never
reaches (join puts the player at 100 each pass). If it recurs, read the
health the step's own line prints first.

Reproduced the same day in the full 3-pass gate at 72abbb1a2 (steps join,
shoot, die, respawn, prop-hurts-player, impact-damage, hot-reload,
map-reload): pass 2 FAILED with the step's own debris check naming prop
401 at the player's spot and health 100 -> None. The cause is the step's
precondition, not the game: its clear_debris circle is 160 around the AIM
POINT, the player stands about 290 from it, and a gib of the previous pass's
impact-damage crate lay at the player's spot. Fixed in the step:
`clear_player_spot` clears the whole square the debris check reads (70
around the spot, the crate itself spared); three self-test rows, red with
the radius at 48 and with the crate not spared. The subset
join,prop-hurts-player,impact-damage did not reproduce it in 12 passes
before the fix or 12 after (the gibs' landing depends on the full gate's
state), so the evidence is the full-gate log and the self-test.
