# SourceSharp.Abi audit: plumbing vs. hot-reloadable code

Read-only audit of `../ss-engineapi` (branch `engineapi-vtables`). Everything cited is at **HEAD `ad7914d`**. The integrator's uncommitted SE1 edits (Entry.cs, Crossings.cs, native, ServerEntry/ClientEntry.cs) are excluded. Line numbers in Entry.cs are checked against `git show ad7914d:src/host/managed/Entry.cs`.

**Instruments.** File and type references come from a regex scan of `src/` with comments and strings stripped: every type declared in `src/abi/*.cs`, matched on word boundaries. This is not a Roslyn graph. Each pair of files records only its first hit, and a pair-level filter drops the false positives I found, such as fields named after enums (`public int MoveType;`) and shim-area field names (`ShimRef<...> Transform`). The "stay" line counts for SPLIT files are body-extent estimates. Nothing was built.

## 0. Approved, with exit criteria (the user, 2026-10-02)

"Plan is good, make sure no statics or globals are used. Should be able to run two managed game
instances side by side with no conflicts." Then: "Start executing".

1. **No static or global state in managed code.** Each instance owns its engine, registry, counters,
   caches and log sink through its own composition (DI). A `static readonly` field may hold only an
   immutable value (a constant table, a frozen collection, a string). It never holds a collection that
   is written, a singleton, or anything per-instance.
2. **Two managed game instances in one process, side by side, with no conflicts.** This is proven by a
   fact that composes two instances against two fake engines and drives both. It is ratcheted by a
   per-assembly census of static fields, read from the compiled metadata (mutable, thread-static and
   reference-typed readonly), which may only fall.

Baseline at bcfa9e6, measured by reflection over bin/debug/bin/managed:

| assembly | mutable | thread-static | readonly (reference-typed) |
|---|---|---|---|
| SourceSharp.Abi | 37 | 3 | 93 (77) |
| SourceSharp.Bootstrap | 17 | 0 | 2 (1) |
| SourceSharp.Shared | 0 | 0 | 699 (59) |
| SourceSharp.Server | 0 | 0 | 399 (391) |
| SourceSharp.Client | 0 | 0 | 29 (3) |
| SourceSharp.DevApi.Server | 0 | 0 | 20 (13) |
| SourceSharp.DevApi.Client | 0 | 0 | 3 (0) |
| the Interop assemblies, DevApi.Contracts, Mods | 0 | 0 | 0 |

Order of work: Phase 0 below (decouple inside Abi, static Engine to zero, plan_side_entries SE2/SE3),
then Phase 1 (the moves), then statics to zero across every assembly and the two-instance fact.
Native globals that hold one process's engine interfaces are out of scope here; the criterion is the
managed game instance.

## 1. Summary

- **What stays in Abi:** 53 whole files (14,302 lines), plus the staying parts of 17 SPLIT files (about 2,080 lines). That is **about 16.4k of 83.0k lines (about 20%)**.
- **What moves:** 138 whole files (56,281 lines), plus the moving parts of the SPLITs (about 10,317 lines). That is **about 66.6k lines (about 80%)**.
- **Against `docs/abi-residency.tsv`:** today the census leaves 189 files and 77.8k lines in Abi (49 `stays`, 9,215 lines; 140 `pinned`, 68,613 lines). It moves only 19 files (5,152 lines).
- **What stays, under the user's criterion:**
  - the generated boundary: `VtableLayouts.g.cs`, `EngineSide.Slots.g.cs` routing, `ShimRoot`, `ShimConsole`, `Vtable`, `NativeInterfaceFactory`;
  - the managed-vtable machinery and the per-side registry that outlives reloads;
  - the blittable mirrors that the generated routing names, and the layout types inside them: `Vector`, `QAngle`, `EntityHandle`, `Transform` (an engine-block field), `UserCmd`;
  - native-numbered value enums;
  - the module contract (`ISideModule`, `Side`);
  - the crossing counters, the build stamp, the loader diagnostics, and the `Log` sink;
  - a reduced reload carrier that holds bytes only.
- **Structural prerequisite: `SourceSharp.Shared` must reference `SourceSharp.Shared.Interop`.** Today Shared.Interop references only Abi.
  - `NetStateGenerator.cs:1181` emits `partial class X : NetState` into Shared assemblies.
  - `NetState` is pointer code (`byte* _blob`).
  - `GameAssembliesAreSafeTests.cs:54` forbids `AllowUnsafeBlocks` in game assemblies.
  - So NetState can leave Abi only for Shared.Interop, and only if Shared can see Shared.Interop.
  - The same holds for the engine surface: the `IEngine*` contracts, `EngineSide`, the adapters, and their vocabulary.
  - Shared.Interop then becomes "the reloadable engine surface", and Shared is safe game code on top of it.
  - Both reload, so Shared vs Shared.Interop only decides layering.
- **The census's own destinations would fail that test.** It sends `EngineFrame`, `EngineLevel`, `PhysicsSurfaceProps`, `SoundEmitterBase`, `PlayerCommandQueue` and `ShimProbes` to `Server`, and `EngineClient` to `Shared`. All of them are `unsafe`, so they belong in `Server.Interop` and `Client.Interop`.
- **Dead code in the Bootstrap creates three pins.**
  - `Entry.ServerOnGameRulesThink` (Entry.cs:846-853) is a private `[UnmanagedCallersOnly]` method. Native never resolves it: `sourcesharp.cpp:532-565` resolves only `LoadSideV2`, `UnloadSide` and `ReloadCheck`.
  - `Entry.Borrow(MapLoadRaw*)` (Entry.cs:742-747) has no caller.
  - `s_ClientGame` is assigned at Entry.cs:583 and cleared at Entry.cs:679, and never read.
  - These are the Bootstrap's only uses of `IServerModule`, `IClientModule`, `MapLoad` and `Engine.FromUtf8`.
  - plan_side_entries SE3 plans to *move* `ServerOnGameRulesThink` and `Borrow`. They should be deleted instead.
- **One cycle blocks most of the move.** 26 files form a single strongly connected component: EngineSide, EngineBinding, the IEngine* contracts, ScriptedEntity, EntityDirectory, EntityIo, Services, ConVar/ConCommand, ShellLess, NetSchema/EntityData/StandaloneNetStates, and others. Seven cuts break it (D3, D7, D8 below). Two of them are "static `Engine` to zero" and "static routes to injection", which the user already wants.

## 2. Rule applied

A file **stays** if at least one of these holds:

- (a) The generated boundary names it, or it is a field type of something the boundary names. The boundary is `VtableLayouts.g.cs`, `EngineSide.Slots.g.cs` routing, and the managed-vtable machinery. The Slots routing takes typed pointers to `MovementParams`, `CollisionRulesBlob`, `DamageRuleMasks`, `GameRulesPolicyBlob`, `PlayerShadowPlan/State`, `TeClassDesc/TeFilterDesc`, `GibShot`, `WeaponState`, `EntitySlot` and the `*Blob` structs. `VtableLayouts.g.cs` names `Vector` 126 times and `QAngle` 26 times.
- (b) It is a blittable mirror of a live native struct, or an enum whose numbers native interprets. These are "value contracts": editing one always needs a native rebuild, so hot reload gains nothing.
- (c) It is state that must outlive the collectible ALC: the `ManagedVtables` registry, the reload carrier, crossing counters, the build stamp.
- (d) It is the side-loading contract or the binding machinery itself: `ISideModule`, `Side`, `ShimRoot`, `Vtable`, `NativeInterfaceFactory`, the `[SlotHash]` attributes and hash-file reader.
- (e) It is the `Log` sink, or loader infrastructure only the Bootstrap uses (`ManagedDiagnostics`, `ManagedPins`, `ProcMaps`, `GitProvenance`). These could also fold into the Bootstrap; neither choice reloads.

Everything else moves. A value-contract enum that only one side uses could also follow its consumer later, at zero cost. They are marked "value contract" in the table.

**Destinations** follow the layering this produces: Abi < Shared.Interop < Shared < {Server.Interop, Client.Interop} < {Server, Client}.

- Each file goes to the lowest assembly that every one of its consumers can see.
- Pointer code goes to an `*.Interop` assembly.
- Moved files should **keep `namespace SourceSharp.Abi`** at first. Several generators match types by metadata name: `NetStateGenerator`, `ScriptedVarsGenerator`, `StringTableGenerator`, `UserMessagePackerGenerator` and `ManagedVtableGenerator` match names such as `"SourceSharp.Abi.NetFieldAttribute"`, `"SourceSharp.Abi.Vector"` and `"SourceSharp.Abi.Color32"`. `src/tf2_descent/tests/CoreAccessTests.cs:26` names `"SourceSharp.Abi.EngineServices"`.

## 3. Per-file table (208 files)

Abbreviations:

- `census: pinned via X:L (T)` is the census's pin chain, with the line in X that names type T.
- "must stop naming" lists references that would break the layering once the file moves; decouple them first.
- "uses static Engine (N)" counts `Engine.X` sites that must become injected services first.
- SPLIT sizes are "total (~staying)".

| file | verdict | destination | why | blocker | lines |
|---|---|---|---|---|---|
| AmmoType.cs | MOVE | Shared.Interop | DMG_* bits + AmmoType record: game vocabulary; IEngineDamage/EngineSide signatures name DamageType | census: pinned via EngineSide.cs:2360 (DamageType) | 196 |
| AnimListTables.cs | MOVE | Shared | wire codec for the anim-name crossing; no production caller (tests only, Entry.cs:873 comment) -> or delete | - | 108 |
| AssemblyInfo.cs | PLUMBING-STAYS | - | InternalsVisibleTo list; shrinks as internals leave | - | 19 |
| BreakModelText.cs | MOVE | Shared.Interop | parser for a model's "break" KeyValues block: game logic | census: pinned via BreakModels.cs:41 (BreakKey) | 319 |
| BreakModels.cs | MOVE | Shared.Interop | per-level break-piece cache (a service) over EngineBound model info | census: pinned via EngineBinding.cs:110 (BreakModels); uses static Engine (1 sites) | 191 |
| BreakPiece.cs | MOVE | Shared.Interop | managed-only data now (native producer shell_breakpieces.cpp deleted; SourceSharpBreakPiece left only in sourcesharp.h); named by IEngine* contracts | census: pinned via EngineAreas.Core.cs:219 (BreakPiece) | 83 |
| BuildInfo.cs | PLUMBING-STAYS | - | build stamp the Bootstrap writes and the client HUD reads (user: build/stamp info stays) | Bootstrap names it: Entry.cs:461-507 | 210 |
| BuildStamp.cs | PLUMBING-STAYS | - | stamp hashing the Bootstrap/GameLoadContext use; folds into the Bootstrap once SE2 hands the stamp over as data | Bootstrap names it: Entry.cs:445,469; GameLoadContext.cs:46 | 180 |
| ClientFrameStage.cs | PLUMBING-STAYS | - | value contract: ClientFrameStage_t numbers passed through unchanged | - | 78 |
| ClientPlayerViewBlob.cs | PLUMBING-STAYS | - | blittable mirror of SourceSharpClientPlayerView; EngineSide.Slots.g.cs routes it | - | 30 |
| ClientRenderGroup.cs | PLUMBING-STAYS | - | value contract: RenderGroup_t, COMPILE_TIME_ASSERTed at the shell call site | - | 41 |
| ClientReplay.cs | MOVE | Client.Interop | IClientReplayEngine/NoClientReplayEngine/MoveFrameConstants -> ShI (IEngineEntities names the interface); ClientReplayEngine (unsafe impl, L147) -> ClI | census: pinned via EngineAreas.Entities.cs:54 (IClientReplayEngine); uses static Engine (8 sites) | 227 |
| ClientShadowFlag.cs | PLUMBING-STAYS | - | value contract: SHADOW_FLAGS_* bits asserted at the shell call site | - | 22 |
| CollisionRules.cs | SPLIT | Shared.Interop | EngineCollisionGroup (engine positions) stays; CollisionRules rule table (L90-213) is game policy | - | 213 (~48 stay) |
| Color32.cs | MOVE | Shared.Interop | RGBA value type: HUD calls pass loose ints, not this layout; generators match it by name (keep namespace) | census: pinned via EngineSide.cs:2001 (Color32) | 34 |
| ColorCorrectionOp.cs | PLUMBING-STAYS | - | value contract: SourceSharpColorCorrectionOp | - | 18 |
| ConCommand.cs | MOVE | Shared | [ConCommand] attribute + ConCommandRegistry: a service | census: pinned via Services.cs:97 (IConCommand) | 330 |
| ConVar.cs | MOVE | Shared | [ConVar] system + registry -> Sh; ConVarFlags/ConVarFlagNames/EngineConVarBackend -> ShI (IEngineConsole and EngineSide name them) | census: pinned via Engine.cs:309 (ConVarFlags); uses static Engine (3 sites) | 791 |
| ConsoleEntry.cs | PLUMBING-STAYS | - | blittable mirror of SourceSharpConsoleEntry; Slots routes it | - | 22 |
| Constraint.cs | MOVE | Server | physics constraint objects: server game logic | uses static Engine (5 sites) | 281 |
| CoreServices.cs | MOVE | Shared | ILog, GameThread, FrameScheduler, IModInfo/ModInfo: services | Bootstrap names it: Entry.cs:533 (ModInfo.Publish) | 347 |
| Crossings.cs | PLUMBING-STAYS | - | crossing counters (user: plumbing); per side under P5 | Bootstrap names it: Entry.cs:91,255,295,849 | 834 |
| DataUpdateType.cs | PLUMBING-STAYS | - | value contract: DataUpdateType_t passed as int32 | - | 21 |
| Decisions.cs | PLUMBING-STAYS | - | value contract: Decision.UseStockRule<0 is read by native; CommandResult/PlayerRelationship numbers cross | - | 54 |
| Diagnostics.cs | PLUMBING-STAYS | - | process-wide crash handlers the Bootstrap installs: loader infra (could fold into Bootstrap) | Bootstrap names it: Entry.cs:201-202,828 | 130 |
| DynamicLight.cs | PLUMBING-STAYS | - | blittable mirror of SourceSharpDynamicLight; Slots routes it | - | 78 |
| DynamicLightFlags.cs | MOVE | Shared | networked DLIGHT_* vocabulary (a NetState field type), not a boundary layout | - | 26 |
| EffectData.cs | PLUMBING-STAYS | - | blittable mirror of SourceSharpEffectData_t; Slots routes it | - | 146 |
| Engine.cs | SPLIT | (delete) | static forwarders: the user's deletion target. Residue: t_Active thread-static (until P5) stays as ActiveSide; RefuseGlobalVars -> Globals; Utf8 helpers -> ShI | Bootstrap names it: Entry.cs:123-124,197,215,220,743-748,850 | 413 (~12 stay) |
| EngineAreas.Core.cs | MOVE | Shared.Interop | IEngine* contracts game code injects: services, not plumbing | census: pinned via CoreServices.cs:21 (IEngineConsole) | 336 |
| EngineAreas.Entities.cs | MOVE | Shared.Interop | IEngine* contracts | census: pinned via ScriptedEntity.cs:591 (IEngineEntities) | 682 |
| EngineAreas.Files.cs | MOVE | Shared.Interop | IEngineFiles contract | census: pinned via EngineBinding.cs:173 (IEngineFiles) | 53 |
| EngineAreas.Frame.cs | MOVE | Server.Interop | IEngineFrame: server frame steps contract | - | 213 |
| EngineAreas.Level.cs | MOVE | Server.Interop | IEngineLevel/ILevelFiles: server map-load steps contract | - | 250 |
| EngineAreas.Physics.cs | MOVE | Shared.Interop | IEngineConstraints/Springs/MotionControllers contracts | census: pinned via EngineBinding.cs:1256 (IEngineConstraints) | 116 |
| EngineAreas.Players.cs | MOVE | Shared.Interop | IEnginePlayers/Weapons/Hud contracts (+TextSize) | census: pinned via EngineSide.cs:2800 (TextSize) | 314 |
| EngineAreas.StringTables.cs | MOVE | Shared.Interop | [StringTable] attribute + IEngineStringTable(s) + handle | census: pinned via EngineBinding.cs:145 (IEngineStringTables) | 334 |
| EngineAreas.World.cs | MOVE | Shared.Interop | IEngineWorldQuery/Sound/Effects/Visibility contracts | census: pinned via EngineBinding.cs:1006 (IEngineWorldQuery) | 638 |
| EngineBaseFileSystem.cs | MOVE | Shared.Interop | IBaseFileSystem binding over its vtable: pointer code, not the binding machinery | census: pinned via EngineBinding.cs:182 (IFwdBaseFileSystem) | 161 |
| EngineBinding.cs | MOVE | Shared.Interop | EngineX adapters (L25-1345) -> ShI; EngineServices composition (L1347) -> Sh; ClientInfo()/hosting adapter over SteamGameServerFlat/EngineBound.Server -> SvI | census: pinned via ScriptedEntity.cs:591 (EngineEntities); uses static Engine (32 sites); must stop naming: ClientReplayEngine (ClientReplay.cs->Client.Interop) at L333; EngineClock (GameServices.cs->Shared) at L1379; IClientInfo (ServerHostingContracts.cs->Server) at L1464; InterpolationBlocks (InterpolationBlocks.cs->Client.Interop) at L321 | 1517 |
| EngineBlock.cs | PLUMBING-STAYS | - | engine block layout; native EngineBlock_t is generated from it | - | 1199 |
| EngineBlockFingerprint.cs | PLUMBING-STAYS | - | fingerprint of the engine block layout (boundary drift check) | - | 196 |
| EngineBlockMarks.cs | MOVE | Shared.Interop | which block writes announce StateChanged(offset): write policy used by game code | census: pinned via TeleportDiscontinuity.cs:74 (EngineBlockMarks) | 159 |
| EngineBound.cs | MOVE | Shared.Interop | PerSide<T> + model info -> ShI; EngineBound.Server (IFwdEngineServer) -> SvI; re-acquired per load | census: pinned via EngineBinding.cs:76 (EngineBound); must stop naming: IFwdEngineServer (EngineServer.cs->Server.Interop) at L102 | 137 |
| EngineClient.cs | MOVE | Client.Interop | IVEngineClient binding over its vtable | uses static Engine (4 sites) | 201 |
| EngineEffectsForwarder.cs | MOVE | Shared.Interop | decorator base for IEngineEffects | - | 177 |
| EngineEnvironment.cs | MOVE | Server.Interop | IEngineEnvironment + binding: server DLLInit environment | uses static Engine (1 sites) | 138 |
| EngineFrame.cs | MOVE | Server.Interop | IEngineFrame over IShimFrame | uses static Engine (2 sites) | 232 |
| EngineHierarchyWorld.cs | MOVE | Shared | ParentProbe bits -> ShI (EngineSide answers it); EngineHierarchyWorld (IHierarchyWorld over 21 static Engine calls) -> Sh | census: pinned via Engine.cs:351 (ParentProbe); uses static Engine (21 sites) | 319 |
| EngineLevel.cs | MOVE | Server.Interop | IEngineLevel over IShimLevel | - | 271 |
| EngineModelInfo.cs | MOVE | Shared.Interop | IVModelInfo binding over its vtable | census: pinned via EngineBinding.cs:565 (IFwdModelInfo) | 225 |
| EngineNetworkStringTables.cs | MOVE | Server.Interop | INetworkStringTableContainer binding (server) | - | 134 |
| EngineServer.cs | MOVE | Server.Interop | IVEngineServer binding (server) | census: pinned via EngineBinding.cs | 267 |
| EngineSide.Slots.g.cs | PLUMBING-STAYS | - | ShimRef/ShimAreas/ClientShimAreas + Slot_X/Has_X routing: generated plumbing (user example). tools/shim-areas must emit the routing as its own class (e.g. ShimSlots, public or IVT) instead of an EngineSide partial (L239), so EngineSide.cs can leave | Bootstrap names it: Entry.cs:134 (ShimAreas/ClientShimAreas.Count in a log line); stays but names moving: EngineSide (EngineSide.cs) at L239 | 1175 |
| EngineSide.cs | MOVE | Shared.Interop | per-side engine API: slot calls mixed with decisions (shell-less routing, liveness mirror, factory binding) | census: pinned via Engine.cs:30 (EngineSide); must stop naming: EntityFactories (EntityFactories.cs->Server.Interop) at L115 | 9168 |
| EngineStateScope.cs | MOVE | (delete) | test scope over static routes; goes when the routes become injected | census: pinned via ShellLess.cs:331 (ScopedBindings) | 69 |
| Entity.cs | SPLIT | Shared.Interop | EntityHandle/Vector/QAngle stay (VtableLayouts.g.cs names Vector 126x, QAngle 26x; ISideModule names EntityHandle); Angles basis math moves | census: pinned via Engine.cs:182 (EntityHandle); uses static Engine (7 sites) ; staying EntityHandle calls static Engine at Entity.cs:41-57 (IsValid/Index/ClassName/Health/Origin) | 246 (~144 stay) |
| EntityData.cs | MOVE | Shared.Interop | entity field layout computation (EntityLayout/NetworkedLayout): reloadable netcode | census: pinned via NetSchema.cs:367 (EntityLayout) | 532 |
| EntityDirectory.cs | MOVE | Server | OverlapPair/OverlapReader -> ShI (IEngineEntities names OverlapPair); SavedConnection/SavedIo -> Sh; EntityDirectory -> Sv | census: pinned via EngineSide.cs:4041 (OverlapPair) | 2204 |
| EntityDirectoryCore.cs | MOVE | Shared | shared directory storage both sides derive from | census: pinned via ShellLess.cs | 809 |
| EntityEffects.cs | MOVE | Shared | EF_* vocabulary for a block field | - | 66 |
| EntityFactories.cs | MOVE | Server.Interop | classname factory ownership table: server logic fed by 5 native slots | census: pinned via EngineSide.cs:115 (EntityFactories) | 373 |
| EntityIo.cs | MOVE | Server | EntityNameMatch -> Sh (EntityDirectoryCore uses it); EntityIoRoutes static route -> an injected interface in ShI; queue/connections -> Sv | census: pinned via ManagedCaches.cs:47 (EntityIoRoutes) | 1127 |
| EntityLayoutRegistry.cs | MOVE | Shared | per-assembly layout registry + fingerprint: netcode | - | 203 |
| EntityOp.cs | SPLIT | Server | EntityOp enum stays (C# half of entity_op.h; Crossings names it); EntityOpMask (L120) names ScriptedEntity members -> Sv | census: pinned via Crossings.cs:492 (EntityOp) | 279 (~91 stay) |
| EntityRef.cs | MOVE | Server | EntityOpArgs -> SvI (EntitiesBinding.cs:71 decodes into it); EntityRef (exposes ScriptedEntity) -> Sv | census: pinned via IServerModule.cs:124 (EntityOpArgs) | 144 |
| EntityRemoval.cs | MOVE | Shared.Interop | RemovalMark + EntityRemovalRoutes: the static route becomes an injected interface EngineSide is handed | census: pinned via ManagedCaches.cs:46 (EntityRemovalRoutes) | 209 |
| EventCatalog.cs | MOVE | Shared | animation-event table: game data | - | 219 |
| FogParams.cs | PLUMBING-STAYS | - | blittable mirror of SourceSharpFogParams; Slots routes it | - | 52 |
| FrameLifecycle.cs | MOVE | Shared | IFrameLifecycle service hook | - | 102 |
| GameEvent.cs | MOVE | Shared | [GameEvent] registry/reader: services | uses static Engine (5 sites) | 261 |
| GameLifecycle.cs | MOVE | Shared | IGameLifecycle service hook | - | 190 |
| GameServices.cs | MOVE | Shared | IGameClock/EngineClock -> Sh; IEntityDirectory (returns ScriptedEntity, L22) -> Sv | census: pinned via ScriptedEntity.cs:2047 (IEntityDirectory); uses static Engine (1 sites); must stop naming: ScriptedEntity (ScriptedEntity.cs->Server) at L26 | 129 |
| GitProvenance.cs | PLUMBING-STAYS | - | build provenance the Bootstrap reads (loader infra; folds into Bootstrap after SE2) | Bootstrap names it: Entry.cs:473-474,493 | 127 |
| GlobalState.cs | MOVE | Server.Interop | env_global state + IServerGlobalState: server game logic | census: pinned via IServerModule.cs:380 (GlobalState) | 164 |
| Globals.cs | PLUMBING-STAYS | - | CGlobalVarsRaw layout mirror + its pointer reader | - | 219 |
| HeardSound.cs | MOVE | Server.Interop | CSoundEnt record: managed-only now (SourceSharpHeardSound only in sourcesharp.h) | census: pinned via ServerInterfaces.cs:589 (HeardSound) | 23 |
| Hierarchy.cs | MOVE | Shared | parenting model: registry, composition, cycle refusal | census: pinned via EngineHierarchyWorld.cs:92 (IHierarchyWorld); uses static Engine (2 sites) | 1553 |
| HierarchyDriver.cs | MOVE | Server | parenting front door: game logic | uses static Engine (1 sites) | 481 |
| IClientModule.cs | MOVE | Client.Interop | client module hooks: a contract between Client.Interop bindings and the module; Bootstrap only casts it (Entry.cs:583), never calls it | Bootstrap names it: Entry.cs:48,583 (cast only; never called) | 293 |
| IConVarHost.cs | MOVE | Shared | module-owns-registry hook | - | 10 |
| INetTiers.cs | MOVE | Shared.Interop | size-tier cache interface (state) | census: pinned via NetSchema.cs:100 (INetTiers) | 72 |
| IServerModule.cs | MOVE | Server.Interop | server module hooks: Server.Interop bindings call it; Bootstrap use is the dead ServerOnGameRulesThink (Entry.cs:847-853) | Bootstrap names it: Entry.cs:47,578,851 (851 is in dead ServerOnGameRulesThink) | 401 |
| ISideModule.cs | PLUMBING-STAYS | - | module contract the Bootstrap loads (until SE2 makes the entry raw pointers) | Bootstrap names it: Entry.cs:21-22,157,555-570,666; GameLoadContext.cs:19 | 124 |
| ITempEntBlobs.cs | MOVE | Server.Interop | server temp-ent forward hook used by TempEntsBinding | census: pinned via IServerModule.cs:10 (ITempEntBlobs) | 24 |
| InputButtons.cs | PLUMBING-STAYS | - | IN_* bits: the meaning of UserCmd.Buttons (value contract) | - | 45 |
| Interpolated.cs | PLUMBING-STAYS | - | InterpolationKind + [Interpolated] decorate EngineBlockHead fields | - | 75 |
| InterpolationBlocks.cs | MOVE | Client.Interop | IInterpolationBlocks/NoInterpolationBlocks -> ShI (IEngineEntities names it); unsafe impl (L76) -> ClI | census: pinned via EngineAreas.Entities.cs:31 (IInterpolationBlocks); uses static Engine (2 sites) | 118 |
| KeyValuesText.cs | MOVE | Shared | KeyValues text parser: game logic | - | 418 |
| LifecycleStages.cs | MOVE | Shared | LifecycleStage/FrameStage ordering: services | - | 337 |
| ListingBuffer.cs | MOVE | Server.Interop | writes managed answers into native char* buffers for Server.Interop thunks | - | 101 |
| LivenessMirror.cs | MOVE | Shared.Interop | managed record of live handles: a cache EngineSide consults | census: pinned via EngineSide.cs:436 (LivenessMirror); uses static Engine (1 sites) | 386 |
| LoadedAssemblies.cs | MOVE | Shared.Interop | mod assembly/type discovery | census: pinned via ConVar.cs:571 (LoadedAssemblies) | 261 |
| LocalCommandLine.cs | MOVE | Shared | process argv reader: helper | - | 73 |
| Log.cs | SPLIT | Shared.Interop | Log sink stays (Bootstrap names Log 20x); EngineLimits -> ShI; LogCapture -> Sh; the sink must stop naming IEngineConsole | Bootstrap names it: Entry.cs (20 calls, e.g. 167,266,345); uses static Engine (4 sites) | 133 (~60 stay) |
| ManagedCaches.cs | MOVE | (delete) | exists only because caches live in Abi; empty once they leave (Bootstrap Entry.cs:697) | Bootstrap names it: Entry.cs:697 | 171 |
| ManagedPins.cs | PLUMBING-STAYS | - | ALC pin finder the Bootstrap runs on a failed unload (loader infra; could fold into Bootstrap) | Bootstrap names it: Entry.cs:623 | 306 |
| ManagedVtable.cs | PLUMBING-STAYS | - | managed-built vtable machinery + per-side registry that outlives reloads | Bootstrap names it: Entry.cs:28-29,158-160,259,639 | 521 |
| ManagedVtableAttributes.cs | PLUMBING-STAYS | - | [ImplementedByManaged]/[ManagedVtable]/[SlotHash]: boundary contract metadata | - | 60 |
| ManagedVtableHashFiles.cs | PLUMBING-STAYS | - | reload check reads slot hashes from disk (Bootstrap Entry.cs:263) | Bootstrap names it: Entry.cs:263 | 98 |
| MapLoad.cs | MOVE | Server.Interop | ref-struct API over MapLoadRaw; Bootstrap use is the dead Borrow (Entry.cs:742) | Bootstrap names it: Entry.cs:742 (dead Borrow) | 73 |
| MapLoadRaw.cs | PLUMBING-STAYS | - | blittable mirror of SourceSharpMapLoad (user example) | Bootstrap names it: Entry.cs:742 (dead Borrow) | 37 |
| ModelDescriptor.cs | MOVE | Shared.Interop | parsed model sequence/attachment table: game data | census: pinned via ScriptedEntity.cs | 367 |
| ModelDescriptors.cs | MOVE | Shared.Interop | per-level model descriptor cache (service) | census: pinned via ScriptedEntity.cs:1744 (ModelDescriptors); uses static Engine (2 sites) | 139 |
| MotionController.cs | MOVE | Server | motion controller objects (13 static Engine calls) | uses static Engine (13 sites) | 231 |
| MoveEffectKind.cs | PLUMBING-STAYS | - | value contract: SourceSharpMoveEffect | - | 27 |
| MoveMath.cs | MOVE | Shared | gamemovement.cpp arithmetic: game math | census: pinned via Movement.cs:963 (MoveMath) | 904 |
| MoveStateBlob.cs | PLUMBING-STAYS | - | blittable SourceSharpMoveState (user example) | - | 141 |
| MoveSurfaceBlob.cs | PLUMBING-STAYS | - | blittable SourceSharpMoveSurface | - | 15 |
| MoveTraceBlob.cs | PLUMBING-STAYS | - | blittable SourceSharpMoveTraceResult | - | 24 |
| Movement.cs | SPLIT | Shared | layout blobs stay (MovementParams, DamageRuleMasks, JumpRequest, GameRulesPolicyBlob, CollisionRulesBlob, MoveTrace, MoveSurface: Slots/VtableLayouts name them); GameMovement (~3000 lines), MoveState, MoveVec, IMoveWorld -> Sh; MovementBridge (unsafe, L3965) -> ShI | census: pinned via EngineSide.Slots.g.cs:377 (MoveTrace); uses static Engine (8 sites) | 4370 (~422 stay) |
| MoverMath.cs | MOVE | Shared | pushing-brush arithmetic | - | 266 |
| NameBuffer.cs | MOVE | Server.Interop | identifier-answer buffer convention for Server.Interop thunks | - | 70 |
| NameMatches.cs | MOVE | Shared.Interop | name lookup enumerator (named by IEngineEntities.Named) | census: pinned via ShellLess.cs:279 (NameMatches) | 113 |
| NativeConsole.cs | MOVE | Shared.Interop | engine convar/command listing over the console slots | census: pinned via Engine.cs:314 (NativeConsoleEntry); uses static Engine (3 sites) | 111 |
| NativeDocAttribute.cs | PLUMBING-STAYS | - | C++ comments for the generated native engine block | - | 44 |
| NativeInterfaceFactory.cs | PLUMBING-STAYS | - | acquire-by-version binding machinery | - | 332 |
| NetArray.cs | MOVE | Shared | networked fixed array type + attribute (generator matches by name) | - | 134 |
| NetEnums.cs | MOVE | Shared.Interop | enum schema for networked fields | census: pinned via NetSchema.cs:189 (NetEnums) | 273 |
| NetPayloadReader.cs | MOVE | Server | live payload decode for devapi | uses static Engine (3 sites) | 74 |
| NetSchema.cs | SPLIT | Shared.Interop | NetTierInfo stays (VtableLayouts/Slots name it); schema/oracle/tiers -> ShI | census: pinned via Engine.cs:329 (PayloadAgreement) | 1451 (~16 stay) |
| NetState.cs | SPLIT | Shared.Interop | EntitySlot stays (Slots routes EntitySlot**); NetState base, NetBlobView, exceptions -> ShI (Shared must then reference Shared.Interop) | census: pinned via Engine.cs:147 (EntitySlot); uses static Engine (2 sites) | 976 (~27 stay) |
| NetStateBinding.cs | MOVE | Shared | [NetState] field wiring | census: pinned via ManagedCaches.cs:33 (NetStateBinding) | 152 |
| NetStateSource.cs | MOVE | Shared | INetState/LiveNetState for devapi | - | 66 |
| NetStateTransfer.cs | MOVE | Shared | by-name networked state transfer across reload | census: pinned via ScriptedEntity.cs:717 (NetStateTransfer) | 188 |
| NetTable.cs | MOVE | Shared.Interop | NetField attributes/kinds/layout | census: pinned via NetSchema.cs:240 (NetFieldKind) | 329 |
| NetTraffic.cs | MOVE | Server | bandwidth sampler: diagnostics | - | 366 |
| OpArgs.cs | PLUMBING-STAYS | - | blittable SourceSharpOpArgs; Slots routes it | - | 74 |
| PhysicsEvents.cs | SPLIT | Server.Interop | PhysicsEvent struct + PhysicsEventKind stay (mirror of physics_shell.h, crosses as a span); PhysicsSurfaces helper (unsafe Of, L273) -> SvI | census: pinned via IServerModule.cs:171 (PhysicsEvent); uses static Engine (2 sites) | 292 (~233 stay) |
| PhysicsId.cs | MOVE | Server | managed id over native physics handles | - | 120 |
| PhysicsSurfaceProps.cs | MOVE | Server.Interop | IPhysicsSurfaceProps binding (server) | - | 56 |
| PlayerCommandQueue.cs | MOVE | Server.Interop | usercmd queue + run loop (unsafe) | uses static Engine (1 sites) | 639 |
| PlayerShadow.cs | SPLIT | Server.Interop | PlayerShadowPlan/State/Advance stay (Slots routes them); the state machine (L62-800) is game logic | census: pinned via EngineSide.Slots.g.cs:393 (PlayerShadowState); uses static Engine (5 sites) | 912 (~133 stay) |
| PrecacheAttribute.cs | MOVE | Shared | [Precache] + registry | - | 222 |
| Predicted.cs | MOVE | Shared.Interop | [Predicted] tolerance attribute | census: pinned via StandaloneNetStates.cs:558 (PredictedAttribute) | 53 |
| PredictionErrors.cs | MOVE | Client | prediction error analysis/ring | - | 572 |
| PredictionJudge.cs | MOVE | Client | prediction verdict | - | 127 |
| PredictionRing.cs | MOVE | Client | prediction blob ring | - | 114 |
| ProcMaps.cs | PLUMBING-STAYS | - | /proc/self/maps reader the Bootstrap stamps native with (loader infra) | Bootstrap names it: Entry.cs:466 | 207 |
| ProtocolManifest.cs | MOVE | Shared | protocol list hashes | - | 96 |
| RagdollState.cs | PLUMBING-STAYS | - | value contract: SourceSharpRagdollState bits | - | 29 |
| RandomSeeds.cs | MOVE | Shared | per-container seed source (service) | - | 55 |
| ReadableMemory.cs | MOVE | (delete) | dead: no caller (StaticStateWaivers.cs:149 rules it Dead) | - | 228 |
| RecordingEngine.cs | MOVE | Server.Interop | test double implementing every IEngine* area incl. server-only ones (tf2 tests use it); SvI or a test-support assembly | - | 1263 |
| ReloadRecord.cs | MOVE | Shared | non-entity reload record | census: pinned via ScriptedEntity.cs:801 (ReloadRecord) | 245 |
| ReloadRecordText.cs | MOVE | Shared | record flatten codec | census: pinned via ScriptedEntity.cs:821 (ReloadRecordText) | 128 |
| ReloadStore.cs | SPLIT | Shared | the carrier must outlive the ALC, so it stays, but as BCL-typed bytes per side; today it holds SavedEntity/SavedIo/ShellLessEntity/ReloadRecord objects, which pins all of them | census: pinned via ScriptedEntity.cs:3823 (ReloadStore) | 600 (~120 stay) |
| RenderPolicyBlocks.cs | MOVE | Client.Interop | IRenderPolicyBlocks/NoRenderPolicyBlocks -> ShI (IEngineEntities names it); unsafe impl (L67) -> ClI | census: pinned via EngineAreas.Entities.cs:43 (IRenderPolicyBlocks); uses static Engine (2 sites) | 110 |
| SaveCustom.cs | MOVE | Shared | ISaveCustom tier + SaveGraph | census: pinned via ScriptedEntity.cs:778 (SaveGraph); uses static Engine (2 sites) | 871 |
| SaveField.cs | MOVE | Shared | [SaveField] catalog | census: pinned via ManagedCaches.cs:34 (SaveFieldCatalog) | 337 |
| SaveTiers.cs | MOVE | Shared | marked/custom tier walk | - | 338 |
| SavedEntity.cs | MOVE | Shared | saved entity record (after ReloadStore carries bytes) | census: pinned via ScriptedEntity.cs:689 (SavedEntity) | 648 |
| SavedEntityCodec.cs | MOVE | Shared | SavedEntity <-> bytes | - | 803 |
| ScriptedEntity.cs | MOVE | Server | enums (BrushSolidity, SolidFlags, SolidType, CollisionGroup, FadeStyle, ScriptedEntityKind; L44-340) -> ShI with the contracts; ScriptedEntity base + ScriptedEntityRegistry (~4400 lines) -> Sv | census: pinned via Engine.cs:344 (ScriptedEntityKind); uses static Engine (30 sites) | 4941 |
| ScriptedVars.cs | MOVE | Shared | [ScriptVars] + parse | census: pinned via ScriptedEntity.cs:623 (ScriptedVarSet); uses static Engine (1 sites) | 575 |
| ServerHostingContracts.cs | MOVE | Server | IClientInfo/IChat/... hosting services | census: pinned via EngineBinding.cs:1464 (IClientInfo) | 97 |
| ServerInterfaces.cs | MOVE | Server.Interop | IServerLifetime/Level/... game-DLL subject contracts the Server.Interop bindings call | census: pinned via IServerModule.cs:275 (IServerLifetime) | 867 |
| ServiceActivator.cs | MOVE | Shared | DI activator | census: pinned via Services.cs:75 (ServiceResolutionException) | 262 |
| Services.cs | MOVE | Shared | ServiceContainer/IServiceLocator: DI | census: pinned via CoreServices.cs:311 (IServiceLocator) | 697 |
| ShadowCast.cs | PLUMBING-STAYS | - | value contract: ShadowType_t (+-1) | - | 25 |
| ShadowCastChoice.cs | PLUMBING-STAYS | - | value contract: StandaloneShadowChoice_t | - | 24 |
| ShellLess.cs | MOVE | Shared.Interop | ShellLessEntity/IShellLessSide (EngineSide reads them); ShellLessRoutes static becomes an injected IShellLessSide | census: pinned via Engine.cs:183 (ShellLessRoutes); must stop naming: EntityDirectory (EntityDirectory.cs->Server) at L483 | 581 |
| ShimConsole.cs | PLUMBING-STAYS | - | first area through the root; the Log fallback before a side composes | Bootstrap names it: Entry.cs:107,132-133 | 66 |
| ShimProbes.cs | MOVE | Server.Interop | cs_vtable probe lines (unsafe static class, so not Server: GameAssembliesAreSafeTests) | uses static Engine (1 sites) | 122 |
| ShimRoot.cs | PLUMBING-STAYS | - | shim root binding (user example) | Bootstrap names it: Entry.cs:98,132,186-187 | 80 |
| Side.cs | PLUMBING-STAYS | - | side enum of the module contract | Bootstrap names it: Entry.cs (28 uses) | 8 |
| SideEntry.cs | MOVE | (delete) | sets the thread side for bindings; P5 deletes it (plan_side_isolation P5) | uses static Engine (1 sites) | 16 |
| SideServices.cs | MOVE | Shared | [SideService] registration | - | 221 |
| SkyboxParams.cs | PLUMBING-STAYS | - | blittable mirror of SourceSharpSkyboxParams; Slots routes it | - | 44 |
| SoundEmitterBase.cs | MOVE | Server.Interop | ISoundEmitterSystemBase binding (server) | - | 45 |
| SoundType.cs | MOVE | Server.Interop | CSoundEnt SOUND_* vocabulary: server game | census: pinned via ServerInterfaces.cs:589 (SoundType) | 97 |
| Spring.cs | MOVE | Server | spring objects (6 static Engine calls) | uses static Engine (6 sites) | 87 |
| StageUniqueness.cs | MOVE | Shared | lifecycle stage collision check | census: pinned via CoreServices.cs:323 (StageUniqueness) | 101 |
| StandaloneBones.cs | MOVE | Shared.Interop | bone cache answer (named by IEngineEntityRender) | census: pinned via EngineSide.cs:4806 (StandaloneBones) | 145 |
| StandaloneMirror.cs | MOVE | Shared.Interop | client animation-clock mirror: a cache | census: pinned via EngineBinding.cs:495 (StandaloneMirror); uses static Engine (2 sites) | 323 |
| StandaloneNetStates.cs | MOVE | Shared.Interop | [StandaloneNetState] attribute + layout publish (3 static Engine calls) | census: pinned via ManagedCaches.cs:32 (StandaloneNetStates); uses static Engine (3 sites) | 940 |
| SteamGameServerFlat.cs | MOVE | Server.Interop | libsteam_api flat C binding (server) | census: pinned via EngineBinding.cs:80 (SteamGameServerFlat) | 121 |
| StringTableRegistry.cs | MOVE | Shared | [StringTable] declaration registry | - | 271 |
| TeForward.cs | PLUMBING-STAYS | - | value contract: one value per shell_te_forwards entry point (tempent_forwards.h) | - | 65 |
| TeleportDiscontinuity.cs | MOVE | Shared.Interop | teleport -> interpolation reset policy | census: pinned via EngineSide.cs:3782 (TeleportDiscontinuity) | 161 |
| TempEntDeclarationReader.cs | MOVE | Shared.Interop | temp-ent declaration decode (InlineVectors is unsafe, L246) | - | 293 |
| TempEntDeclarationWriter.cs | MOVE | Shared.Interop | temp-ent declaration encode (InlineVectors is unsafe, L230) | - | 273 |
| TempEntLife.cs | SPLIT | Client | TempEntLife struct stays (Slots); TempEntFlags/TempEntPriority policy -> Cl | census: pinned via EngineSide.Slots.g.cs:941 (TempEntLife) | 185 (~79 stay) |
| TempEntReader.cs | MOVE | Shared.Interop | temp-ent blob reader | - | 283 |
| TempEntRegistry.cs | MOVE | Shared.Interop | temp-ent registry | - | 381 |
| TempEntSpawn.cs | SPLIT | Shared.Interop | TempEntSpawn struct stays (Slots), minus its defaulting initialiser that names TempEntFlags/BreakGroup/RenderMode; BreakGroup/ModelType -> ShI (IEngine* contracts name ModelType) | census: pinned via EngineSide.Slots.g.cs:951 (TempEntSpawn); must stop naming: TempEntFlags (TempEntLife.cs->Client) at L239 | 342 (~283 stay) |
| TempEntState.cs | MOVE | Shared.Interop | temp-ent declaration base class | - | 66 |
| TempEntStatics.cs | PLUMBING-STAYS | - | blittable SourceSharpTempEnt statics; Slots routes it | - | 125 |
| TempEntTransform.cs | PLUMBING-STAYS | - | blittable temp-ent transform; Slots routes it | - | 91 |
| TempEntWriter.cs | MOVE | Shared.Interop | temp-ent blob writer | - | 251 |
| TempEntity.cs | SPLIT | Shared.Interop | TeClassDesc/TeFieldDesc/TeFilterDesc stay (Slots routes them); attributes, TeClass/TeField, TeClassBuilder -> ShI | census: pinned via EngineSide.Slots.g.cs:827 (TeClassDesc) | 795 (~42 stay) |
| TextMessage.cs | PLUMBING-STAYS | - | blittable SourceSharpTextMessage; Slots routes it | - | 40 |
| ThinkScheduler.cs | MOVE | Server | think scheduling: game logic | census: pinned via ScriptedEntity.cs:1635 (ThinkScheduler) | 330 |
| ThirdPersonCamera.cs | MOVE | Client.Interop | third-person camera policy (orbit, clamp) | - | 256 |
| ThirdPersonCameraBlob.cs | PLUMBING-STAYS | - | blittable SourceSharpThirdPerson* blob | - | 43 |
| Transform.cs | SPLIT | Shared.Interop | Transform stays (EngineBlock.cs:532/976 lay it out in the block); TemplatePlacement/TemplateSpawn/KeyedSpawn records -> ShI | census: pinned via EngineAreas.Core.cs:321 (TemplatePlacement) | 277 (~209 stay) |
| UniformRandomStream.cs | MOVE | Shared.Interop | IUniformRandomStream binding over its vtable | census: pinned via EngineBinding.cs | 96 |
| UserCmd.cs | PLUMBING-STAYS | - | blittable SourceSharpUserCmd (user example; ISideModule names it) | - | 36 |
| UserCmdCodec.cs | SPLIT | Shared.Interop | DecodedUserCmd stays (mirror of SourceSharpDecodedUserCmd); UserCmdCodec (L55) codec -> ShI | - | 184 (~14 stay) |
| UserMessage.cs | MOVE | Shared | [UserMessage] registry/reader/writer (generator matches by name) | uses static Engine (1 sites) | 542 |
| UserMessageKey.cs | MOVE | Shared | message key naming | - | 78 |
| UserMessageSchema.cs | MOVE | Shared | payload schema used by generated packers | - | 116 |
| Utf8Names.cs | MOVE | Shared.Interop | native UTF-8 name cache for binding code | - | 126 |
| VariantValue.cs | MOVE | Shared.Interop | variant_t parse/value: game logic | census: pinned via EntityIo.cs:157 (VariantParse) | 1141 |
| Vtable.cs | PLUMBING-STAYS | - | vtable acquire + marshalling helpers (binding machinery) | - | 356 |
| VtableLayouts.g.cs | PLUMBING-STAYS | - | generated vtable layouts (user example) | - | 6262 |
| WorldQuery.cs | SPLIT | Shared.Interop | GibShot/WeaponState stay (Slots routes pointers to them), SurfaceMaterial stays (JumpRequest.GroundMaterial field, Movement.cs:262); trace enums/records, weapon/blood/render vocab -> ShI | census: pinned via Engine.cs:389 (HullResult) | 729 (~147 stay) |
| WorldRenderState.cs | PLUMBING-STAYS | - | value contract: SourceSharpWorldRenderState | - | 27 |

## 4. Move plan (each batch compiles on its own)

**Ordering rule:** a file can leave Abi only when no file still in Abi names it, because Abi cannot reference outward. So the files that name others move first, and the vocabulary everyone names moves last. The levels below come from that "who names whom" graph over the moving files, after the Phase 0 cuts. Each step is one commit plus `make check`. Steps that change runtime behaviour (D5, D6, D7, D8) also need the live gate with its second map load and a `cs_sv_reload`/`cs_cl_reload`.

### Phase 0: decouple inside Abi (nothing changes assembly yet)

- **D1. Delete dead Bootstrap code.**
  - Remove `Borrow` (Entry.cs:742-747), `ServerOnGameRulesThink` (Entry.cs:846-853), and the `s_ServerGame`/`s_ClientGame` fields (Entry.cs:47-48, 578, 583, 673, 679).
  - Delete `ReadableMemory.cs`: it has no caller, and `StaticStateWaivers.cs:149` already rules it `Dead`.
  - This unpins `IServerModule`, `IClientModule` and `MapLoad`, and drops one use of `Engine.FromUtf8`.
- **D2. Change the project graph.**
  - Add `SourceSharp.Shared.csproj` → `SourceSharp.Shared.Interop.csproj`. There is no cycle: Shared.Interop references only Abi and Generators today.
  - Give Shared.Interop `InternalsVisibleTo` for `SourceSharp.Tests` and `SourceSharp.OracleTests`, as Server and Client already have.
- **D3. Split files so each one has a single destination.** These are pure declaration moves inside Abi:
  - out of `ScriptedEntity.cs` (L44-340): the entity vocabulary enums (`CollisionGroup`, `SolidType`, `SolidFlags`, `BrushSolidity`, `FadeStyle`, `ScriptedEntityKind`). This cuts BreakModelText:22, BreakModels:75, BreakPiece:64, EngineAreas.Core:218, EngineAreas.Entities:303 and EngineSide:3494 away from ScriptedEntity;
  - out of `EntityDirectory.cs`: `OverlapPair`/`OverlapReader` (EngineSide:4041, EngineAreas.Entities:250);
  - out of `EngineBinding.cs`: the `EngineServices` composition (L1347-1517) and `ClientInfo()` (L1464), away from the adapters;
  - out of `ConVar.cs`: `ConVarFlags`/`ConVarFlagNames` (named by IEngineConsole L42, NativeConsole L41, EngineSide L272);
  - from the client fetch files (ClientReplay, InterpolationBlocks, RenderPolicyBlocks): interface and null implementation separated from the unsafe implementation;
  - from `EngineBound`: the server part (L102), separated;
  - from the other SPLIT rows: Movement (blobs / GameMovement / MovementBridge), PlayerShadow, WorldQuery, TempEntity, TempEntLife, TempEntSpawn (including its defaulting initialiser at L239), NetState (`EntitySlot`), NetSchema (`NetTierInfo`), Transform, UserCmdCodec, CollisionRules, PhysicsEvents, Entity.cs (`Angles`), Log.cs (`EngineLimits`, `LogCapture`), EntityOp.cs (`EntityOpMask` names `ScriptedEntity.OnInput` etc., L178-180), GameServices (`IEntityDirectory`, L22, returns `ScriptedEntity`), EntityRef (`EntityOpArgs`, decoded at Server.Interop `EntitiesBinding.cs:71`), EntityIo (`EntityNameMatch`, used by EntityDirectoryCore:478), EngineHierarchyWorld (`ParentProbe`).
- **D4. Change the generator.** `tools/shim-areas` should emit the Slot_X/Has_X routing as its own class (for example `ShimSlots`, holding the `ShimAreas`/`ClientShimAreas` and `AttachShims`), public or visible through `InternalsVisibleTo` to Shared.Interop. Today it emits a `partial class EngineSide` (EngineSide.Slots.g.cs:239). EngineSide then holds a `ShimSlots`. This is the only change that lets `EngineSide.cs` leave while the routing stays.
- **D5. Detach the Log sink from the engine.**
  - `Log` (Log.cs:36) should hold a two-method `ILogSink` declared in Abi, instead of `IEngineConsole`.
  - Before a side composes, it falls back to `ShimConsole`, not `Engine.For(side).Msg` (Log.cs:52-62).
  - The side's composition attaches an adapter and detaches it in `Shutdown`, so the sink cannot pin the ALC.
  - Move the thread-static active side out of `Engine` (Engine.cs:28-50) into a 10-line `ActiveSide` in Abi. P5 deletes it later.
- **D6. Make the reload carrier carry bytes.** `ReloadStore.Store` (ReloadStore.cs:46-52) holds `SavedEntity`, `SavedIo`, `ShellLessEntity` and `ReloadRecord` *objects*. That pins all four, and through them `ScriptedVars`, `EntityDirectory` and `ShellLess`.
  - Make it a per-side `Dictionary<string, byte[]>` plus tallies.
  - Encode with `SavedEntityCodec`, which already writes these records for `.sav`.
  - Regression: the reload restores the same 472/473 entities.
- **D7. Turn static routes into injected interfaces.** These static routes exist so that Abi-resident code can call up into the server registry:
  - `ShellLessRoutes` (EngineSide.cs:433, EngineBinding.cs:373);
  - `EntityIoRoutes` (EngineBinding.cs:374, ScriptedEntity.cs:1933);
  - `EntityRemovalRoutes` (EngineSide.cs:2849);
  - the `EntityFactories.Native*` delegates (EngineSide.cs:115 `BindEntityFactories`; this binding moves to the server composition);
  - `PlayerShadow.Switch` (PlayerShadow.cs:242);
  - the `LivenessMirror.Shared` and `StandaloneMirror.Shared` singletons (LivenessMirror.cs:82, StandaloneMirror.cs:182).

  Each becomes a constructor-injected interface or a container-registered instance. `EntityDirectory.IsInvisibleEntry` and `EntryIndex`, called at ShellLess.cs:483, move onto `EntityHandle`. After that, `ManagedCaches.Forget` (Entry.cs:697) and `EngineStateScope` have nothing left to do, so delete both. The test scopes become per-test containers.
- **D8. Static `Engine` to zero.**
  - Delete `EngineServices.Defaults` and the defaults that follow whichever side is active: `ScriptedEntity.cs:591` `= new EngineEntities()`, `ConCommand.cs:159` `EngineServices.Defaults.Require<IEngineConsole>()`, and `GameServices.cs:122`, whose `EngineClock` is built over `EngineSide`.
  - Replace every `Engine.X` site (the counts are in the table: ScriptedEntity 30, EngineHierarchyWorld 21, MotionController 13, ClientReplay 8, Movement 8, Spring 6, ...) with an injected area or the side's `EngineSide`. Then delete `Engine.cs`'s forwarders.
  - The Bootstrap's own `Engine` calls (Entry.cs:123-124, 197, 215, 220) go through `ShimSlots` and `ActiveSide` until SE2 moves them into the side's entry.
  - This step gates moving EngineSide: once EngineSide is in Shared.Interop, any forwarder left in Abi cannot see it.
  - Three files that **stay** in Abi also call `Engine`, and must be retargeted:
    - `EntityHandle`'s convenience members (Entity.cs:41-57: `IsValid`, `Index`, `ClassName`, `Health`, `Origin`) are game conveniences on a boundary type. Move them to extension members in Shared.Interop over an injected `IEngineEntities`, or migrate their callers. Until then, `EntityHandle` is a hidden engine dependency.
    - `ManagedVtable.cs:47` (`Engine.SetActiveSide`) moves to `ActiveSide`.
    - `NativeInterfaceFactory.cs:204,265` (`Engine.AcquireInterface`, `Engine.ActiveSide`) move to `ShimSlots` and `ActiveSide`.
- **D9 (SE2).** The Bootstrap stops naming Abi; see section 5.

### Phase 1: moves, files that name others first

- **M1 (level 0: nothing left in Abi names them).**
  - Shared: AnimListTables (or delete), KeyValuesText, MoverMath, LocalCommandLine, RandomSeeds, PrecacheAttribute, ProtocolManifest, UserMessage + UserMessageKey (they form a cycle, so they move together), UserMessageSchema, GameEvent, GameLifecycle, FrameLifecycle, IConVarHost, SideServices, StringTableRegistry, SaveTiers, SavedEntityCodec, NetArray, NetStateSource, EntityLayoutRegistry, EntityEffects, DynamicLightFlags.
  - Shared.Interop: Utf8Names, EngineEffectsForwarder, the UserCmdCodec codec, the CollisionRules rule table, TempEntDeclarationReader.
  - Server: Constraint, Spring, MotionController, HierarchyDriver, NetPayloadReader, NetTraffic.
  - Server.Interop: PhysicsSurfaceProps, SoundEmitterBase, EngineNetworkStringTables, ListingBuffer, NameBuffer, PlayerCommandQueue, ShimProbes, RecordingEngine (or a test-support assembly), the PhysicsEvents helper.
  - Client.Interop: EngineClient, ThirdPersonCamera, IClientModule.
  - Client: PredictionErrors, PredictionRing, the TempEntLife policy.
  - Deleted: SideEntry (after D5), Engine.cs (after D8).
- **M2 (levels 1-3).**
  - Shared: CoreServices (the `ModInfo` hand-off needs D9 or must stay as data; see section 5), EventCatalog, LifecycleStages, StageUniqueness.
  - Shared.Interop: the temp-entity declaration stack (TempEntState, TempEntDeclarationWriter, TempEntReader, TempEntWriter, TempEntRegistry).
  - Server.Interop: IServerModule, ServerInterfaces, ITempEntBlobs, GlobalState, HeardSound, SoundType, MapLoad, EngineFrame, EngineLevel, EngineEnvironment, EngineAreas.Frame/Level, the PlayerShadow state machine.
  - Server: PhysicsId.
  - Client: PredictionJudge.
- **M3, the server entity cluster, moves to Server as one commit (internally cyclic).** ScriptedEntity (base and registry), EntityDirectory, EntityIo (queue and connections), EntityRef, ThinkScheduler, `IEntityDirectory`, ServerHostingContracts, `EntityOpMask`. Needs D3, D7 and D8.
- **M4, DI and the declared-state services, move to Shared as one commit.** Services, ServiceActivator, ConCommand, ConVar (the Shared part), the EngineServices composition, `IGameClock`/`EngineClock`, EntityDirectoryCore + `EntityNameMatch`, SavedEntity, SavedIo/SavedConnection, SaveCustom, SaveField, ReloadRecord(Text), NetStateBinding, NetStateTransfer, Hierarchy, EngineHierarchyWorld, LogCapture.
- **M5, the engine surface, moves as one commit per assembly.**
  - Server.Interop and Client.Interop first: EngineServer, SteamGameServerFlat, EntityFactories, `EngineBound.Server`; the ClientReplay, InterpolationBlocks and RenderPolicyBlocks implementations.
  - Then the Shared.Interop cluster. It is cyclic: contracts, vocabulary and binding name each other.
    - Bindings and caches: EngineSide.cs, the EngineBinding adapters, EngineAreas.Core/Entities/World/Files/Physics/Players/StringTables, EngineBound (shared part), EngineModelInfo, EngineBaseFileSystem, UniformRandomStream, EngineBlockMarks, NativeConsole, NameMatches, ShellLess, EntityRemoval, LivenessMirror, StandaloneMirror, StandaloneBones, TeleportDiscontinuity, ModelDescriptor(s), BreakModels/BreakModelText/BreakPiece.
    - Netcode: EntityData, NetSchema, INetTiers, NetTable, NetEnums, NetState, StandaloneNetStates, Predicted, LoadedAssemblies.
    - Vocabulary: WorldQuery, the entity enums, ConVarFlags, AmmoType, the Transform records, VariantValue, ScriptedVars, ParentProbe, MovementBridge, the client fetch interfaces.
  - Shared: MoveMath and GameMovement.
- **M6 (last: named by almost everything).** Color32 and NetEnums to Shared.Interop, if they did not go in M5.

## 5. What pins files in Abi, and how to break each pin

| # | pin | where | how to break |
|---|---|---|---|
| 1 | The Bootstrap names 22 Abi files | `ad7914d:Entry.cs` (lines in the table's "Bootstrap names it" cells); `GameLoadContext.cs:19,46` | Dead code first (D1: IServerModule, IClientModule, MapLoad, Engine.FromUtf8). **SE2** (side owns its load) moves into each side's Interop entry: ShimRoot.TryBind (98), ShimConsole.TryAcquire (107), the `ShimAreas.Count` log line (134), `Engine.*` (123-124, 197, 215, 220), `BuildManagedRoot`/`ManagedVtables` (158-160; the registry becomes an opaque handle), `Crossings.Count`, `Log`. `ModInfo.Publish` (533) becomes strings passed through the raw entry. BuildInfo, BuildStampBuilder, GitProvenance, ProcMaps, ManagedDiagnostics and ManagedPins become Bootstrap-internal, with the stamp handed to the side as data. `ManagedCaches.Forget` (697) is deleted after D7. None of these is reloadable either way: they are loader plumbing. |
| 2 | `EngineSide.Slots.g.cs` is a partial of `EngineSide` | EngineSide.Slots.g.cs:239 | D4: a generator emits a standalone `ShimSlots` |
| 3 | Static `Engine` | 41 Abi files use `Engine.X`, plus EngineSide.cs via `using static`; tests 348 hits; Bootstrap Entry.cs:123-124, 197, 215, 220. Staying files too: Entity.cs:41-57 (EntityHandle convenience members), ManagedVtable.cs:47, NativeInterfaceFactory.cs:204,265 | D8: Engine to zero; per-side instances injected; EntityHandle loses its engine-calling members |
| 4 | `Log` names `IEngineConsole` and falls back to `Engine.For` | Log.cs:36, 52-62 | D5: `ILogSink` in Abi, with a ShimConsole fallback |
| 5 | `ReloadStore` holds typed objects | ReloadStore.cs:46-52 | D6: bytes via SavedEntityCodec |
| 6 | Static routes and singletons that Abi code calls up through; `ManagedCaches` and `EngineStateScope` exist only to clear and scope them | EngineSide.cs:115, 433, 2849; EngineBinding.cs:373-374; ScriptedEntity.cs:1933; PlayerShadow.cs:242; LivenessMirror.cs:82; StandaloneMirror.cs:182; ManagedCaches.cs; EngineStateScope.cs | D7: inject them, then delete both helper files |
| 7 | `EntityOpMask` names ScriptedEntity members | EntityOp.cs:178-180 | Move EntityOpMask to Server; the `EntityOp` enum stays (`Crossings` names it) |
| 8 | Transitional fallbacks that follow the active side | ScriptedEntity.cs:591, ConCommand.cs:159, GameServices.cs:122 | D8 |
| 9 | Layering: Shared.Interop cannot see Shared | NetStateGenerator.cs:1181 (`: NetState` emitted into Shared); GameAssembliesAreSafeTests.cs:54 | D2: Shared → Shared.Interop |
| 10 | Generators and tests match `SourceSharp.Abi.X` by metadata name | src/generators/*.cs; CoreAccessTests.cs:26 | Keep the namespace when moving files; rename later in one sweep |
| 11 | `ISideModule` carries dispatch hooks with defaults (`OnStandalonePlayerCommand`, `OnClientPredictionUpdate`, `ReloadSave/Restore`, `SaveCollect/Restore`) | ISideModule.cs | After SE2, shrink it to load/unload; move the hooks to IServerModule/IClientModule in the Interop assemblies |
| 12 | `AbiResidency.Measure` rules on "touches the engine", not on "is plumbing" | src/test/managed/AbiResidency.cs:43-52, 120-150 | Rewrite its seeds to the criterion in section 2 (generated boundary, plus closure, plus the explicit plumbing list) and regenerate the TSV. Its "pinned by the Bootstrap" fixpoint is exactly the coupling D1/SE2 removes. |

Per-side Abi (plan_side_isolation P5) makes Abi's statics per side, but does not change which files count as plumbing. Abi still needs a process restart to change.

## 6. Counts

| | files | lines |
|---|---|---|
| PLUMBING-STAYS (whole file) | 53 | 14,302 |
| SPLIT (part stays) | 17 | 12,397 total: ~2,080 stay, ~10,317 move |
| MOVE (incl. 5 deleted: Engine forwarders via SPLIT, EngineStateScope, ManagedCaches, ReadableMemory, SideEntry) | 138 | 56,281 |
| **Stays in Abi** | 53 + parts of 17 | **~16.4k (19.7%)** |
| **Leaves Abi** | 138 + parts of 17 | **~66.6k (80.3%)** |

Lines that leave, by main destination (a file split across destinations is counted under its main one):

| destination | files | lines |
|---|---|---|
| Shared.Interop | 55 | 27,101 |
| Shared | 46 | 19,035 |
| Server | 14 | 10,671 |
| Server.Interop | 25 | 6,782 |
| Client.Interop | 6 | 1,205 |
| Client | 4 | 919 |
| deleted | 5 | 885 |

**Against `docs/abi-residency.tsv` (same 208 files):**

| census ruling | files | lines | this audit |
|---|---|---|---|
| `moves` | 19 | 5,152 | all 19 MOVE. Destinations differ for 8 unsafe files: Server.Interop/Client.Interop, not Server/Shared. UserMessageKey goes to Shared, because UserMessage names it and they form a cycle. |
| `stays` | 49 | 9,215 | 6 STAYS; 2 SPLIT (CollisionRules, UserCmdCodec); **41 MOVE** (8,607 lines). These are game logic or helpers that touch no engine: KeyValuesText, MoverMath, the TempEnt codecs, PredictionErrors/Judge/Ring, SaveTiers, SavedEntityCodec, LifecycleStages, ThirdPersonCamera, ... |
| `pinned` | 140 | 68,613 | 47 STAYS (14,091 lines); 15 SPLIT (12,000 lines); **78 MOVE** (42,522 lines). Nearly every pin traces to the Bootstrap, Engine.cs, EngineSide.cs, EngineBinding.cs, ScriptedEntity.cs or ManagedCaches.cs, the couplings in section 5. |

## 7. Caveats

- References come from a regex scan, not a compile. Each pair of files records only its first hit. Expect a few edges to appear in the D3 splits that this missed; the compiler will name them.
- I did not check `internal` member access per site. Moved code that uses Abi internals such as `ShimRef`, `NativeInterfaceFactory.AcquireFn` or `ScopedBindings` needs those made public or covered by `InternalsVisibleTo` on the Interop assemblies.
- Value-contract enums (ClientFrameStage, DataUpdateType, ClientRenderGroup, ClientShadowFlag, ColorCorrectionOp, Decisions, InputButtons, MoveEffectKind, RagdollState, ShadowCast, ShadowCastChoice, TeForward, WorldRenderState: about 450 lines) are kept as the user's "enums that cross by value". The generated boundary types them as `int`, so they could follow their consumers if Abi should shrink further.
- The blittable mirrors could also move to Shared.Interop: native cares about their layout, not their type identity. That would need the Slots generator to emit `void*` instead of typed pointers. I kept them because the user named them as plumbing.
- The integrator's uncommitted SE1 already adds ServerEntry/ClientEntry and renames CrossingOps (`git status` during the audit). Entry.cs line numbers will shift once that lands.
