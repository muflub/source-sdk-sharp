# plan_engineapi_vtables.md: retire `EngineApi` on the server, both directions through vtables

Measured at `54e8ea3` (ss-novalve worktree). Untracked until reviewed.

## The ask, as read

1. **Managed → native.** The server stops handing managed a `SourceSharpEngineApi`
   struct of function pointers. Native exposes **C++ abstract classes** instead. Managed
   holds a raw `this` plus its vtable and calls slot N through a typed function pointer,
   using the `IShimLevel`/`IShimFrame` pattern that already exists.
2. **Native → managed ("vice versa").** `SourceSharpManagedApi` is replaced the same way,
   built from the other side. Managed allocates `{ vtbl, GCHandle }` blocks. Native holds
   them as ordinary `IManaged<Area> *` and calls virtuals on them.
3. **An internal factory in each direction** hands out the raw pointers, each with its
   entry count:
   - native → managed: the existing `SourceSharp_InternalFactory`, promoted to `IShimRoot::Acquire`;
   - managed → native: a new `IManagedRoot::Acquire`.

   Nothing else crosses at bootstrap.
4. **Slots grouped by function into interfaces** (§1.5; the full slot → interface
   table is `docs/engineapi-areas.tsv`, moved there at A1).
5. **Slots nothing uses are removed, not ported** (Phase 0).
6. **Managed code never calls the engine through a global.** Every caller takes a
   managed contract by constructor injection from its side's container. The vtable (or,
   until Phase C, the struct) is visible only inside that contract's sealed
   implementation (§1.6, Phase B).
7. **None of this managed code lives in `SourceSharp.Abi`.** Each piece goes in
   `SourceSharp.Shared` (both sides) or `SourceSharp.Server` (server only), and so does
   the `abi` code that calls the engine today. `Abi` keeps the ABI itself (§1.7,
   Phase R).

This is `plan_globals.md` §2 ("one root, two tiers"; native → managed "built from the
other side") and the paused `backlog-9f-managedvtable.md`, scoped to the server and put
in execution order. It does not repeat §2's reasoning or its benchmark: a
`{vtbl, GCHandle<TSealed>}` crossing measured 4.07 ns against 5.72 ns for today's
static + thread-static route.

The client keeps its `EngineApi` table until a follow-up wave that reuses everything built
here (§6). It loses its static access in Phase B like the server: after B, the struct
is read only inside injected `Native<Contract>` instances.

---

## 0. Where it stands (this session)

| what | number | instrument |
|---|---|---|
| `EngineApi` function-pointer fields | 462 | awk over `src/abi/Abi.cs`, `EngineApi` struct |
| … wired server-only / client-only / both | 272 / 137 / 53 | `#ifdef` walk over the fill in `sourcesharp_gamesystem.cpp:1174-` |
| **slots the server fills** | **325** | same walk (server + both) |
| … carrying `[SuppressGCTransition]` | 43 (all sides) | `grep -c` in `Abi.cs` |
| `ManagedApi` function-pointer fields | 169 | awk over `Abi.cs` |
| `ABI_VERSION` | 251 | `sourcesharp.h:139` |
| managed readers of `EngineApi` | `Engine.cs` 455 calls; `Movement`, `ClientReplay` 4 each; 3 other files 1 each | grep `Api(For\w+)?\.X(` |
| native readers of `ManagedApi` | 111 distinct slots, 42 non-test `.cpp`; `managed_runtime_server.h` alone 28 | grep `GetManagedApi()->` |
| `ManagedApi` slots named by server/shared native code (X-macros included) / client only / **no native reader** | 123 / 20 / **26** | `grep -rlw` per slot over `src/{server,shared}/native` vs `src/client/native` |
| server `EngineApi` slots **no managed code reads** | **7** | grep `(Api\|api\|ApiFor…).X` over non-test `.cs`, then `grep -rlw` over all `.cs` (it found 2 read through a lowercase `api` local and 1 named only in a doc comment) |
| `[UnmanagedCallersOnly]` in `Entry.cs` | 172 | grep |
| gtest call sites of free `SourceSharp_*` | 969 | grep over `src/test/native/*.cpp` |
| bound shim interfaces today | `IShimFrame` (19), `IShimLevel` (22) | `tools/vtable-layout:494,514` |
| per-slot `SuppressGCTransition` in `tools/vtable-layout` / `VtableLayouts.g.cs` | 0 / 0 | grep |

Three facts shape the plan.

- **The managed side is already funnelled.** Every `EngineApi` read except 11 goes
  through `Engine.cs`'s private `Api` ref (`Engine.cs:46`, side chosen by `t_Active`).
  The 455 call sites change in one file, and none of their callers change.
- **The generator cannot yet carry `[SuppressGCTransition]`.** Moving a suppressed slot
  into a vtable without it adds a GC-mode switch to every hot read (health, origin,
  time), and `SuppressionAuditTests` would stop seeing the slot. The generator gains this
  before any suppressed slot moves (step A2).
- **Many wrappers null-check their slot** (`Api.EntityGetHealth != null ? … : 0`,
  `Engine.cs:720`), because one side leaves it unfilled. A vtable has no empty slots. The
  "not on this side" answer moves from a null field to "this side does not serve that
  interface". That is the `PerSide<T>` + `InternalFactory` NULL path `EngineBound.cs`
  already uses, and the answer the client gets today does not change.

---

## 1. Design

### 1.1 Bootstrap: one pointer each way

```
host ── LoadSideV2( side, IShimRoot *root, int32 rootEntries,
                    void **ppManagedRoot, int32 *pManagedEntries ) ──▶ managed
         managed: checks rootEntries against the generated layout, builds its root,
                  returns { IManagedRoot *, entry count }
native: checks the count against MemberOf( &IManagedRoot::Release ).slot + 1, refusing with both numbers
```

- `IShimRoot` (native, implemented by `CShellGameDll`, the root that `plan_globals` G2
  names) has three members:
  - `Acquire( const char *name, int32 *pnEntries ) -> void *`: the internal factory. Its
    body is today's `SourceSharp_InternalFactory` switch, which becomes a thin wrapper
    over it for the engine-kind switch in `shell_vtable_interfaces.cpp`;
  - `AbiVersion()`;
  - `Release()`, which does nothing but is the count anchor.
- `IManagedRoot` (managed-implemented) has `Acquire( name, &n )`, which returns
  managed-built areas the same way, plus `Release()`.
- **Why a new `LoadSideV2` rather than changing `LoadSide`.** The client still calls
  `LoadSide( side, EngineApi*, ManagedApi* )` until its own wave. The host resolves each
  by name (`sourcesharp.cpp:530`), and two entry points is the honest shape during
  migration. `LoadSide` is deleted in the client wave.
- The ABI version check moves into `IShimRoot::AbiVersion()`. `struct_size` has no
  successor: the entry counts replace it, per interface.

### 1.2 Managed → native: `IShim<Area>` interfaces

- **The 318 live server slots split by function into 38 classes, bound as 26 pointers
  (§1.5), not one 318-slot class.** The split lets later `plan_globals` work make each interface's
  implementor the subsystem that owns its state. A single class could never be split
  across two owners without renumbering.
- **A both-sides slot goes into a shared base; server-only slots go into a derived
  interface.** Where a function family has slots on both sides, the family is two
  classes: `IShim<Area>` (shared, header under `src/shared/native/`) and
  `IShim<Area>Server : public IShim<Area>`, which appends the server-only methods. The
  base layout is then identical in both DLLs, and the client later implements the base
  alone. `tools/vtable-layout` already generates multi-level chains
  (`chain=['IAppSystem', 'IDataCache']`), so the server row is
  `chain=['IShim<Area>', 'IShim<Area>Server']` and managed binds one pointer. The client
  does not *serve* the bases until its wave: its `InternalFactory` arm is compiled but
  not wired.
- **Implementors are stateless singletons now, and the root's subsystems later.** Each
  area is `static CShim<Area> g_Shim<Area>;` beside the code it wraps, with an
  `<Area>_Acquire( &n )` row, exactly as `shell_level.cpp:901-917` does. They hold no
  state, so moving them into `m_pDll`/`m_pLevel` is `plan_globals` G2's job and is not
  a prerequisite here.
- **Method bodies are the free functions, renamed in place, never wrapped.**
  `int SourceSharp_EntityGetHealth( uint32 h ) { … }` becomes
  `int CShimEntities::GetHealth( uint32 h ) { … }` in the same file, with the same body.
  A forwarding layer would add C++; renaming in place does not.
  - The 969 gtest calls to free functions move to calling through the acquired interface
    (`ShimEntities()->GetHealth(…)`), via a test-only helper that goes through
    `IShimRoot::Acquire`.
  - Count `EXPECT_`/`ASSERT_` per file before and after each scripted rewrite (CLAUDE.md,
    "after a scripted edit").
  - `tools/entrypoint-decls` stops deriving from the struct. The class declaration *is*
    the declaration.
- **Managed call site: an injected contract, never a static (§1.6).**
  `tools/vtable-layout` gains a `chain=[…]` row per class and generates
  `Shim<Area>Vtbl` into `VtableLayouts.g.cs`. The only managed code that touches a
  vtable is the production implementation of a managed contract: a sealed
  `Native<Contract>` that holds `(self, vt)` pairs as readonly instance fields, handed
  to it by its constructor. Game code sees the contract alone.
  - `Engine.EntityGetHealth(h)` (static, `Api.EntityGetHealth` inside) becomes
    `_entities.GetHealth(h)` on an injected `IEngineEntities`. Its implementation calls
    `_shim.Vt->GetHealth(_shim.Self, h.Value)`.
  - A null-check wrapper disappears. On a side that does not serve an interface, the
    container holds the contract's "not on this side" implementation, which returns
    what the null check returned.
- **`[SuppressGCTransition]` travels with the slot.** The area TSV's `suppress` column
  feeds a `suppress=[…]` field on the chain row. The generator emits
  `delegate* unmanaged[SuppressGCTransition]<void*, …>`. `SuppressionAuditTests` switches
  from reading `Abi.cs` fields to reading the generated vtable structs.
  - The audit's native half still reads the method body, which is now a member function.
    The `EntitySetOrigin` → `TouchTriggers` lesson in `Abi.cs` stays attached to its slot
    comment.
- **No thread-static side for engine calls.** Each side has its own container and its
  own `Native*` objects, bound to that side's root. A server object cannot reach the
  client's engine, because it was never given it. `t_Active` survives only for what is
  still static, and Phase E removes the last of that.

### 1.3 Native → managed: `IManaged<Area>`, managed-built

This is the 9f backlog design, resumed here in this repo.

- A fifth generator, `src/generators/ManagedVtableGenerator.cs`, takes a C# interface
  marked `[ImplementedByManaged("IManagedX")]` and emits:
  - the `NativeMemory` `{vtbl, GCHandle}` block;
  - the `[UnmanagedCallersOnly]` thunks, wrapped in the existing try/catch-to-`Fail`;
  - recovery through `GCHandle<TSealed>`, never `(IFoo)handle.Target`;
  - `Release()`, which frees the handle.

  Diagnostics are `SSVT001…`. It refuses to emit the raw-address form.
- The `tools/vtable-layout` chain kind `implemented-by-managed` is re-applied by hand from
  fork commit `0848884ac` (+194/-1). It refuses a virtual destructor, a table that does
  not end in `Release()`, an unspellable slot, and a slot policy objects to.
- **First area: `IManagedRuntimeServer`**, which is already a C++ abstract class. Only its
  implementor changes:
  - the adapter `managed_runtime_server.cpp` (28 forwards in the `.h`) is deleted;
  - its slots move off `ManagedApi`;
  - `CShellGameDll::m_pManagedRuntime` holds the managed-built pointer.
- **The generator's input is the managed contract**, for example `IServerGlobalState`.
  Its implementation is whatever the side's container already resolves, so
  `GlobalStateTable` is exposed to native without knowing it. `IManagedRoot::Acquire`
  (the managed internal factory) looks the name up in the side's container and returns
  that service's generated binding. The `GCHandle` targets the container's instance, and
  no static `s_Server` exists.
- **Then the rest of the server's `ManagedApi`**, area by area in the order the 1.2
  areas land. Each native caller of `SourceSharp_GetManagedApi()->X` becomes
  `ManagedSide()-><area>->X`.
  - `ManagedSide()` is the one accessor route 3 of `plan_globals` §2 allows. It replaces
    `SourceSharp_GetManagedApi`, so the accessor count does not rise.
  - A gtest pins the files that call it.
- **Lifetime.** `UnloadSide` / `cs_sv_reload` Release every managed area before the
  collectible ALC unloads. A strong `GCHandle` keeps the ALC alive, and thunk stubs die
  with it. Native nulls its pointers before `Release`: a freed handle slot is reused, so
  a late call would land on a different live object. The reload path re-acquires through
  `IManagedRoot::Acquire`, replacing the "hand the same `g_EngineApi` again" logic at
  `sourcesharp_gamesystem.cpp:303-318`.

### 1.4 What stays, and why

- `ModelInfoForPrecache`, `AcquireInterface` and the engine-kind factory switch are not
  `EngineApi` concerns once `IShimRoot::Acquire` exists. `AcquireInterface` becomes a
  method on the root.
- The client's `EngineApi` fill and struct stay, marked client-only, until §6.

### 1.5 The interfaces, grouped by function

The machine-checked form is `docs/engineapi-areas.tsv` (regenerated at A1 without the
slots Phase 0 retired: 315 EngineApi and 121 ManagedApi rows), with columns `slot`,
`struct`, `interface`, `kind`, `base`, `side` and `suppress`. It has one row per server
`EngineApi` slot (325) and per `ManagedApi` slot the server names or that nothing
names (149). The script that wrote it checked three things against the census:

- every server slot appears exactly once;
- every shared-base slot is filled on both sides;
- every derived-class or server-only slot is filled only on the server.

In the tables, `*n` is the number of `[SuppressGCTransition]` slots, 39 in all. Method
names drop the prefix the interface already says: `EntityGetHealth` becomes
`IShimEntitiesServer::GetHealth`. A `→` row is a derived interface that appends to the
row above it.

**Managed → native (`IShim*`, implemented in C++): 318 slots in 38 classes, bound as
26 pointers.** The root is not counted in the 26. The last column of the second table
in §1.6 names the managed contract each class serves.

| interface | n | slots |
|---|---|---|
| `IShimRoot` | 1 | `AcquireInterface` (joined by `Acquire`, `AbiVersion`, `Release` from §1.1) |
| `IShimEngine` (shared) | 2 *1 | `GetCurrentTime`, `GetGlobalVarsPtr` |
| → `IShimEngineServer` | 4 | `CommandLineHasParam`, `CommandLineGetParamValue`, `FilesGameFileSize`, `FilesReadGameFile` |
| `IShimConsole` (shared) | 10 | `Msg`, `Warning`, `RegisterConCommand`, `RegisterConVar`, `Set/Get/FindConVarValue`, `SetNamedConVarValue`, `ConsoleSnapshot`, `ConsoleSnapshotEntry` |
| → `IShimConsoleServer` | 3 | `ExecuteServerCommand`, `SendClientCommand`, `GetCommandClientHandle` |
| `IShimEvents` (shared) | 6 | `RegisterUserMessage`, `ListenForGameEvent`, `EventGetInt/Float/Bool/String` |
| → `IShimEventsServer` | 1 | `SendUserMessage` |
| `IShimStringTables` (shared) | 4 | `Find`, `FindStringIndex`, `GetString`, `GetNumStrings` |
| → `IShimStringTablesServer` | 2 | `Create`, `AddString` |
| `IShimNetwork` (shared) | 4 | `NetHierarchyDeclare`, `NetPublishType`, `NetTierInfo`, `WorldNetDeclare` |
| → `IShimNetworkServer` | 7 *1 | `EntityGetDataBlob`, `EntityBindBlob`, `EntityMarkStateChanged`, `EntityMarkBlockChanged`, `EntityPayloadAgreement`, `EntityGetEngineBlock`, `EntitySetTransmitAlways` |
| `IShimModels` (shared) | 11 | `ModelDescriptor`, `EntityGetModelName`, `EntitiesInvalidateMdlCacheAll`, `Standalone{StudioName, IsPoseable, BoneCount, BoneMatrices, LookupAttachment, GetAttachment, SetSequence, SetModelScale}` |
| → `IShimModelsServer` | 6 *2 | `EntitySetModel`, `EntitySetSequence`, `EntityGet/SetTextureFrame`, `ModelBreakPieceCount`, `ModelBreakPiece` |
| `IShimPrecache` | 5 | `PrecacheModel/Sound/Decal/Material/ParticleSystem` |
| `IShimMovement` (shared) | 6 | `MoveTrace`, `MovePointContents`, `MoveSurface`, `MoveEffect`, `CurrentGravity`, `RegisterMovementParams` |
| → `IShimMovementServer` | 13 | `PlayerRunMove`, `PlayerInitVCollision`, `PlayerShadowProbe/Apply`, `PlayerSetMoveType`, `PlayerForceButtons`, `PlayerGetButtons`, `PlayerSetButtonsDisabled`, `PlayerSetLaggedMovement`, `PlayerSetAllowAutoMovement`, `PlayerSetSlowMovement`, `PlayerSetMovementConstraint`, `PlayerSetFrozen` |
| `IShimWorld` (shared) | 2 | `PointContents`, `SurfacePropGameMaterial` |
| → `IShimWorldServer` | 16 | `TraceLine`, `TraceHull`, `TraceLineSurface`, `TracePlayerAim`, `AngleVectorsFromForward`, `WaterSurfaceBelow`, `SurfacePropIndex`, `EntityModelSurfaceProp`, `InvisibleRangeReach`, `EngineLightStyle`, `SetSkyboxParams`, `WorldChapterTitle`, `AreaPortal{Create, Destroy, SetOpen, SetWindow}` |
| `IShimEntities` (shared) | 4 *2 | `EntityGetIndex`, `EntityGetOrigin`, `EntityGetAngles`, `EntitySetSkin` |
| → `IShimEntitiesServer` | 25 *11 | identity and lookup: `IsValid`, `IsPlayer`, `GetClassName`, `GetName`, `GetState`, `FindEntityByClassname`, `FindEntityByName`, `FindBaseEntityByName`, `EntitiesInSphere`, `SetKeyValue`. Spawn data: `GetSpawnOrigin/Angles/Flags`, `SetSpawnFlags`. Vitals: `Get/SetHealth`, `Get/SetArmor`. Presentation and ownership: `SetVisible`, `SetOwner`, `MakePickup`, `SetPickupEnabled`, `DetectInSkybox`, `Entities{Sdk,Standalone}PlayerSkyboxArea` |
| `IShimEntityLifecycle` | 18 | `CreateEntity`, `EntitySpawn`, `EntityRemove`, `DestroyShell`, `MapSpawnCreate`, `TemplateCapture`, `TemplateSpawnInstance`, `EntitiesPurgeMapRefs`, `EntitiesCleanupDeleteList`, `RegisterScriptedEntity`, `RegisterWeaponEntity`, `RegisterClassOps`, `EntityDispatch`, `EntityFactory{Count, NameAt, RefusalCount, RefusalNameAt, RefusalTimesAt}` |
| `IShimTransform` | 18 *5 | `SetOrigin`, `SetAngles`, `Teleport`, `Get/SetVelocity`, `AddBaseVelocity`, `Get/SetAngularVelocity`, `GetEyePosition`, `GetEyeAngles`, `GetWorldSpaceCenter`, `MoveToPosition`, `MoveToAngles`, `StopMoving`, `Get/SetGravity`, `SetFriction`, `SetBounce` |
| `IShimHierarchy` | 8 | `SetParent`, `GetParent`, `GetParentAttachment`, `Set/GetLocalTransform`, `ParentProbe`, `GetAttachmentFrame`, `LookupAttachmentAny` |
| `IShimCollision` | 15 *1 | `GetBounds`, `GetBoundsSize`, `SetBounds`, `GetSolidType`, `SetSolidity`, `GetSolidFlags`, `SetNotSolid`, `SetBrushSolidity`, `SetSolidToWorld`, `Get/SetCollisionGroup`, `SetPairCollisions`, `BecomePhysicsTrigger`, `OverlapReport`, `RegisterCollisionRules` |
| `IShimPhysics` | 14 *2 | `InitPhysics`, `ConvertToPhysics`, `DestroyPhysics`, `Get/SetMass`, `EnableMotion`, `SetPhysicsStatic`, `PhysicsWake`, `PhysicsSleep`, `ApplyVelocityImpulse`, `ApplyForceCenter`, `ApplyForceOffset`, `ApplyTorque`, `SetForceServerRagdoll` |
| `IShimConstraints` | 6 *2 | `Create`, `Destroy`, `IsValid`, `SetMotor`, `SetLinearMotor`, `Count` |
| `IShimSprings` | 7 *2 | `Create`, `Destroy`, `IsValid`, `SetConstant`, `SetDamping`, `SetLength`, `Count` |
| `IShimMotionControllers` | 13 *1 | `Create`, `Destroy`, `Attach`, `Detach`, `SetActive`, `SetForce`, `SetForceAtPoint`, `SetForceIsAcceleration`, `SetMotor`, `SetUpright`, `SetMedium`, `RampLinearLimit`, `Count` |
| `IShimDamage` | 7 | `ApplyDamage`, `EntityTakeDamage`, `RadiusDamage`, `ExplosionDamage`, `EntitySetTakeDamageMode`, `EntityIgnite`, `EntityDissolve` |
| `IShimGameRules` | 3 | `RegisterGameRulesPolicy`, `RegisterDamageRules`, `EndRound` |
| `IShimPlayers` | 16 *4 | `GetPlayerHandle`, `GetPlayerUserId`, `GetPlayerTeam`, `AddPlayerScore`, `PlayerIsAlive`, `PlayerRespawn`, `PlayerPlaceAtSpawn`, `Get/SetObserverMode`, `SetViewEntity`, `SetFov`, `SnapEyeAngles`, `GetPlayerShootOrigin`, `PlayerSetSoundscape`, `Clients{Standalone,Sdk}PlayerEarPosition` |
| `IShimWeapons` (shared) | 2 | `RegisterAmmoType`, `RegisterWeaponType` |
| → `IShimWeaponsServer` | 14 *5 | `GetState`, `Idle`, `SetNextPrimaryAttack`, `SetNextSecondaryAttack`, `SetClip`, `SetInReload`, `TakeAmmo`, `TakeSecondaryAmmo`, `PlaySound`, `SendAnim`, `AnimDuration`, `FireBullets`, `MuzzleFlash`, `AddViewKick` |
| `IShimInventory` | 10 | `GivePlayerItem`, `GivePlayerAmmo`, `GetPlayerActiveWeapon`, `PlayerGetActiveWeaponEntity`, `PlayerGetWeaponEntity`, `PlayerGetViewModelEntity`, `PlayerDropActiveWeapon`, `PlayerHolsterActiveWeapon`, `PlayerDeployLastWeapon`, `PlayerRemoveAllItems` |
| `IShimSound` | 8 | `EntityEmitSound`, `EntityStopSound`, `EmitSoundAtPosition`, `EmitAmbientSound`, `SoundPatch{Emit, Stop, State, Free}` |
| `IShimEffects` (shared) | 1 | `TempEntRegister` |
| → `IShimEffectsServer` | 21 | `TempEntPlayback`, `EffectDispatchIndex`, `ExplosionEffect`, `ScreenShake`, `ScreenFade`, `ViewPunch`, `Spark/Impact/MuzzleFlash/Tracer/Bubble/Funnel/Dust/Splash/BloodEffect`, `BloodDecal`, `PaintDecal`, `ShootGib`, `FireEffectCreate/Scale/Enable` |
| `IShimDebugOverlay` | 5 | `DrawWorldText`, `DrawEntityOverlay`, `AddLineOverlay`, `OverlayCounts`, `OverlayResetCounts` |

Rulings a reviewer may want to move:

- the blob and dirty-mark slots sit under `IShimNetworkServer`, not `IShimEntitiesServer`,
  because they are the networking contract;
- `PlayerRunMove` and the shadow slots sit under `IShimMovementServer`, next to the
  movement they serve;
- `TracePlayerAim` sits under `IShimWorldServer`, with the other traces;
- `RegisterCollisionRules` sits under `IShimCollision`, while `RegisterDamageRules` sits
  under `IShimGameRules`.

Constraints, springs and motion controllers are three classes, not one, because the
managed side already has three contracts for them (`IEngineConstraints`,
`IEngineSprings`, `IEngineMotionControllers`). `IShimEntitiesServer` (25) is the
largest. It splits later if `plan_globals` gives vitals and lookup different owners.

**Native → managed (`IManaged*`, implemented in C#): 123 slots in 18 interfaces.**
Everything the server's native code names. The client-only 20 wait for §6.

| interface | n | slots |
|---|---|---|
| `IManagedRuntimeServer` (exists; changes implementor) | 22 | `System{Start}`, `Game{Start, PostStart, Stop}`, `MapLoad{Transition, SaveGame, Background, NewGame, PreEntity}`, `MapUnload`, `MapUnloadPreEntity`, `Activate`, `GameFrame`, `PreClientUpdate`, `Think`, `CreateNetworkStringTables`, `TickInterval`, `Hibernation`, `InvalidateMdlCache`, `SteamGameServer{Activated, Shutdown}`, `WorldKeyValue` |
| `IManagedServerBrowser` | 7 | `GetGameDescription`, `ShouldHideServer`, `BrowserMapOverride`, `BrowserGameData`, `IsManualMapChangeOkay`, `QueryCvarValueFinished`, `StatusLines` |
| `IManagedFrame` (shared) | 6 | `Frame`, `FramePreThink`, `FramePostThink`, `LevelInit`, `LevelShutdown`, `Shutdown` |
| `IManagedCommands` (shared) | 3 | `ConCommandDispatch`, `ConVarChanged`, `OnClientCommand` |
| `IManagedEvents` | 2 | `OnGameEvent`, `OnUserMessage` |
| `IManagedClients` | 13 | `OnClientConnect`, `OnClientPutInServer`, `OnClientActive`, `OnClientGamePrecache`, `OnRespawn`, `OnPlayerSpawn`, `OnPlayerSpawned`, `OnPlayerKilled`, `OnSelectSpawnPoint`, `GetPlayerRelationship`, `CanTakeDamage`, `SkyboxAreaForClient`, `EarPositionForClient` |
| `IManagedPlayerCommands` (shared) | 6 | `MoveProcessMovement`, `OnStandalonePlayerCommand`, `OnPlayerRunCommand`, `PlayerRunCommands`, `PlayerDecodeCommands`, `OnPlayerSurfaceChanged` |
| `IManagedEntities` | 8 | `Dispatch`, `EntityRemoveRequested`, `OnEntityIgnite`, `OnFireAddHeatInRadius`, `OnRagdollMagnetForce`, `ShellLessFindByName`, `ShellLessFindByClassname`, `TemplatesAddNamed` |
| `IManagedHierarchy` | 11 | `ParentMoved`, `ChildMoved`, `SetParent`, `GetParentAttachment`, `SetParentName`, `SetParentAttachmentName`, `EntityRemoved`, `ResolveParentNames`, `ToLocal`, `ToWorld`, `SetLocal` |
| `IManagedNetState` (shared) | 2 | `FieldOffset`, `FieldBits` |
| `IManagedSaveRestore` | 4 | `ReloadSave`, `ReloadRestore`, `SaveCollectEntities`, `SaveRestoreEntities` |
| `IManagedGlobalState` | 13 | `Add`, `IndexOf`, `Count`, `Get/SetState`, `Get/SetCounter`, `AddToCounter`, `GetName`, `Get/SetMap`, `EnableUpdates`, `Dump` |
| `IManagedSoundEnt` | 4 | `Init`, `Shutdown`, `Insert`, `ResetClientSound` |
| `IManagedSoundController` | 13 | `Create`, `Destroy`, `Play`, `PlayEnvelope`, `ChangePitch`, `ChangeVolume`, `GetPitch`, `GetVolume`, `FadeOut`, `CommandAdd`, `CommandClear`, `Reset`, `Shutdown` |
| `IManagedResources` | 5 | `DecalIndexForName`, `DecalNameForIndex`, `DecalTranslateForGameMaterial`, `PropDataRandomChunkModel`, `ModelPrecacheComponents` |
| `IManagedTempEnts` (shared) | 2 | `OnTempEntity`, `OnNativeTempEntity` |
| `IManagedPhysicsEvents` | 1 | `PhysicsEvents` |
| `IManagedDiagnostics` | 1 | `CrossingsReport` |

The two one-slot interfaces are deliberate. Each has a single owner, so folding them into
a neighbour would give that neighbour two owners.

**Removed, not ported (Phase 0): 33 slots.**

- `EngineApi`, 7, which no managed code reads:
  - `FireEffects`
  - `EntityGetPropData`
  - `CreateExplosion`
  - `SoundscapeGetIndex`
  - `HostSteamGameServer{Reacquire, Clear, RequestGameplayStats}`
- `ManagedApi`, 26, which no native code names on either side:
  - `AllowFlashlight`, `CanRespawn`, `GetRespawnDelay`, `OnGameRulesThink`,
    `OnPlayerTakeDamage`
  - `OnEntityTouched`
  - the 13 `OnScripted*` slots (`Spawn`, `KeyValue`, `Input`, `Think`, `Removed`,
    `Damaged`, `Killed`, `MoveDone`, `Blocked`, `EndTouch`, `PhysicsCollision`,
    `ResolveOutput`, `FilterPasses`)
  - `OnWeapon{ItemPostFrame, PrimaryAttack, Reload}`
  - `PlayerCommands`
  - `Prediction{DescribeOffset, RecordCompare, TypePredictedFields}`

### 1.6 Managed side: contracts by dependency injection, never a global

**The rule.** Managed code never calls the engine through a static method. It declares
the contract it needs as a constructor parameter and receives it from its side's
container. That holds for game code, for the engine-facing code that moves out of
`abi` (§1.7) and for `Bootstrap`, which makes no engine call at all. The vtable is an
implementation detail of one sealed class per contract.

**What is there today** (`54e8ea3`):

- **Contracts and container.** ~44 contracts already exist (`IEngine*`, `IServer*`),
  and there is a `ServiceContainer` with `AddInstance`/`Require` (`src/abi/Services.cs`).
  `ServerModule` owns one and resolves from it (`ServerModule.cs:28, 480, 885`).
- **The contracts are backed by globals.** Their implementations in `EngineBinding.cs`
  are stateless forwarders to static `Engine.X`, 434 of them. `Engine.X` reads a static
  struct chosen by a thread-static side.
- **Default container.** `EngineServices.Defaults` is a static container, with 15
  non-test readers.
- **Static callers.**
  - The contracts cover only part of the tree: 1,459 static `Engine.X` references
    remain outside `Engine.cs`, in 85 files.
  - By area: `abi` about 1,211, `host` 226 (of which `SetActiveSide` 161), game code 16.
    Game code is already nearly clean.
  - `Engine.cs` has 469 static members.

**Target shape, per side:**

```
LoadSideV2(side, IShimRoot *root, n)                         ← Bootstrap: the one static entry. Loads the
  │                                                            side's ALC, then passes raw nints on
  └─ ISideModule.Compose(root, n) → (managedRoot, n)         ← Abi: the one cross-ALC interface
       ServerComposition.Build(root)                         ← Server: the composition root; no statics
       shims  = NativeShims.Acquire(root)                    ← 26 (self, vt) pairs, counts checked
       services = new ServiceContainer()
       services.Add<IEngineEntities>(new NativeEntities(shims.Entities, shims.EntitiesServer,
                                                        shims.Transform, shims.Hierarchy, …))
       services.Add<IEngineSound>(new NativeSound(shims.Sound, shims.Players))
       …                                                     ← one sealed Native<Contract> per contract
       module = new ServerModule(services)                   ← game objects take contracts by ctor
       managedRoot = ManagedRoot.Bind(services)              ← IManagedRoot: Acquire(name) → a
                                                               generated binding of a resolved service
```

- **The contract is the managed-shaped API.** It takes `EntityHandle`, `Vector`,
  `string` and spans, never `byte*` or `uint`. Marshalling (UTF-8 buffers, vector
  packing, the handle's `.Value`) lives in `Native<Contract>`. Today it is in the bodies
  of `Engine.cs`'s static methods, which move there as instance methods reading
  `_shim.Vt->X(_shim.Self, …)` instead of `Api.X(…)`.
- **One contract may read several native classes.** Measured by chaining
  `EngineBinding.cs` → `Engine.X` → `Api.Slot` → §1.5:

  | managed contract | native classes it reads (slots) |
  |---|---|
  | `IEngineConsole` | Console 10, ConsoleServer 2 |
  | `IEngineEntities` | EntitiesServer 17, Transform 13, Hierarchy 8, EntityLifecycle 6, Collision 6, Entities 3, Physics 2, NetworkServer 1, Models 1 |
  | `IEngineEntityPhysics` | Physics 11, Collision 8, Transform 3 |
  | `IEngineEntityRender` | Models 9, ModelsServer 4, EntitiesServer 2, Entities 1, NetworkServer 1 |
  | `IEngineBrushMovement` | Transform 4 |
  | `IEngineCollisionRules` | Collision 1 |
  | `IEngineConstraints` / `IEngineSprings` / `IEngineMotionControllers` | Constraints 6 / Springs 7 / MotionControllers 13 |
  | `IEngineDamage` | Damage 7, Physics 1, EffectsServer 1, GameRules 1 |
  | `IEngineEffects` | EffectsServer 17, Effects 1, Movement 1, Collision 1 |
  | `IEngineHud` | DebugOverlay 5, EffectsServer 3 |
  | `IEngineFiles` / `IEngineHost` | EngineServer 2 / EngineServer 2, GameRules 1 |
  | `IEngineGameEvents` | Events 5, Engine 1 |
  | `IEngineGameRulesPolicy` | GameRules 1 |
  | `IEngineNetworking` | NetworkServer 4, Network 4, EntitiesServer 1, Events 1, EventsServer 1 |
  | `IEnginePlayerMovement` | MovementServer 9, Movement 1, Players 1 |
  | `IEnginePlayers` | Players 14, Inventory 10, EntitiesServer 2, MovementServer 1, ConsoleServer 1 |
  | `IEnginePrecache` | Precache 5 |
  | `IEngineScripted` | EntityLifecycle 4, Root 1 |
  | `IEngineSound` | Sound 8, Players 1 |
  | `IEngineStringTables` / `IEngineStringTable` | StringTables 4, StringTablesServer 2 / Console 2 |
  | `IEngineVisibility` | WorldServer 7, EntitiesServer 2 |
  | `IEngineWeapons` | WeaponsServer 14, Weapons 2, EntityLifecycle 1 |
  | `IEngineWorldQuery` | WorldServer 8, World 2, EntitiesServer 1 |

  This covers the slots reached through a contract today. The rest are reached only
  by static `Engine.X` from `abi`/`host`. Phase E either assigns each of those to an
  existing contract or rules that a new one is needed. A new contract is named for what
  game code does with it, not after the native class.
- **The reverse direction mirrors this.** `IManaged*` is generated from a managed
  contract (`IServerGlobalState`, `IServerSoundController`, `IServerSoundEnt`,
  `IServerDecals`, `IServerPropData`, `IServerTemplates`, `IServerListing`,
  `IServerClients`, `IServerLifetime`, …). §1.5's reverse table therefore takes each
  existing contract's name where one exists. D0 confirms each match by chaining
  `Entry.cs` thunk → the object it forwards to → its contract, the same way.
- **"Not on this side" is a registration, not a null.** The client container
  registers a contract's absent-side implementation (`NoClientReplayEngine` is the
  existing precedent). It answers exactly what today's null check answered, so no call
  site keeps a guard.
- **Composition happens once, in one place.** `ServiceContainer.Require` is called only
  in composition roots (`ServerComposition`/`ClientComposition`, the module constructors). A type that
  receives the container itself and resolves from it later is a service locator, which
  is a global in disguise, and an analyzer refuses it (E-last).
- **Objects native creates** (entities through `Dispatch`, temp ents) are built by a
  factory the composition root constructs. The factory captures the contracts, so an
  entity's constructor receives them like anything else. No entity reaches for a static.
- **Tests get this for free.** A fact constructs the subject with fakes
  (`RecordingEngine` and `UnstubbedEngineCallException` already exist) instead of
  stubbing a static. A new fact becomes writable: two containers in one process, where a
  server object's call never lands on the client's shims.

### 1.7 Where the code lives: Shared and Server, not Abi

**Why this layout holds.** Bootstrap loads `SourceSharp.Abi` once, in the default ALC,
for both sides (`GameLoadContext.cs:27`). It loads `SourceSharp.Shared` and
`SourceSharp.Server`/`Client` into **each side's own collectible `GameLoadContext`**
(`Entry.cs:719`). So code in Shared and Server is per side by construction, and code in
Abi is shared by both sides by construction. Three consequences:

- **A static in Abi is a cross-side global.** That is why `Engine.cs` needs `t_Active`
  and `NativeInterfaceFactory` keys its cache on the side. The same class in Shared has
  one copy per side, and neither mechanism has anything left to do.
- **Generated thunks unload with their side.** Thunks in Server/Shared die with the
  side's ALC, which is exactly the lifetime rule of §1.3. Thunks in Abi would outlive
  the side they call into.
- **Game code can be kept from the vtables.** Server references Shared. If `Native*`
  types are `internal` to the assembly that composes them, game code cannot reach a
  vtable even by accident.

**What goes where.**

| code | assembly | notes |
|---|---|---|
| `ShimRef<TVtbl>` (`self`, `vt`), acquire + count check (from `NativeInterfaceFactory.cs`), `Vtable.cs` | Shared | The side key is gone: one copy per side. |
| `ManagedVtable.cs` (the `{vtbl, GCHandle}` block, `Release`) and use of `ManagedVtableGenerator` | Shared | Shared and Server already reference `SourceSharp.Generators`. |
| Generated layouts: engine interfaces (`IVEngineServer`, …) and the `IShim*` shared bases | Shared | `tools/vtable-layout` writes `src/shared/managed/VtableLayouts.g.cs`. |
| Generated layouts: server chains (`IShim*Server`, server-only classes) | Server | a second output, `src/server/managed/ShimLayouts.g.cs` |
| Contracts used by both sides (`IEngineConsole`, `IEngineGameEvents`, `IEngineStringTables`, `IEngineNetworking`, `IEnginePlayerMovement`, …) and their `Native*` | Shared | A shared contract's `Native*` is built by each side's composition from that side's root. |
| Server-only contracts (`IEngineConstraints`, `IEngineDamage`, `IEngineWeapons`, …), their `Native*`, `ServerComposition`, `IManagedRoot` and every `IManaged*` binding | Server | The generator emits the reverse thunks into Server. |
| `IServer*` contracts (today 2 files in `abi`) | Server | They are the reverse direction's inputs. |
| The engine-facing code in `abi` (`Engine.cs` and `EngineBinding.cs` are gone by R, deleted in B; also `EngineAreas.*`, `Movement.cs`, `PlayerShadow.cs`, `ScriptedEntity.cs`, `EntityDirectory.cs`, `Hierarchy.cs`, …) | Shared, or Server when only server code reaches it | 74 of `abi`'s 166 files, 54,747 of its 79,419 lines, touch the engine at `54e8ea3`. Phase R decides each file's destination by census. |
| Blittable ABI types and enums, `ISideModule`, value types that cross ALCs (`EntityHandle`, …) | **Abi**, and only these | `EngineApi`/`ManagedApi` stay until the client wave deletes them. The client's `Native*` reads the struct from Client/Shared. |
| `Entry.cs` | Bootstrap | It keeps `LoadSideV2` and the ALC work. Its 226 `Engine.X` references go: failures return codes and native prints them, and every forward goes through `ISideModule`. |

**When.** Phase R runs after C. B and C edit code where it already lives, so each of
their diffs is a behaviour change with no relocation mixed in. R is then a pure move of
code that is already injected and vtable-backed, and `Engine.cs` no longer exists to be
moved. Phase D follows R, so the generated reverse thunks are emitted straight into
Server and never into Abi.

**Visibility.** `Abi` grants internals only to Bootstrap and the tests
(`AssemblyInfo.cs`), so R moves a file together with any `Abi`-internal member it reads.
Shared and Server each gain `InternalsVisibleTo("SourceSharp.Tests")` for the facts that
need it. From B until R, the `Native*` classes are public in Abi, and the fence fact in
B1 stands in for `internal`.

---

## 2. Steps

Each step is one commit and runs `make check`. Steps marked **(live)** also run
`make live-gate` (memory `live-gate-cadence`). Agents append ABI slots and never bump the
version. The integrator bumps once at the end of Phase C and once at the end of Phase E.

**Why injection comes before vtables.** Phase B turns every static `Engine.X` into an
instance method of a `Native<Contract>` that is injected where it is used. At first
those methods read the struct through a pointer held as an instance field. Phase C then
changes only what is inside each `Native<Contract>`, from struct to vtable. Callers
change once and never see the swap.

The server passes both the struct and the root while Phase C runs, so every contract
switches independently. That makes a transitional legacy bridge class unnecessary, and
it lets the client lose its statics in this plan (in B) even though its vtables wait
for §6.

### Phase 0: remove what nothing uses

- **0a.** Retire the 7 unused server `EngineApi` slots (§1.5).
  - Mark each `Abi.cs`/`sourcesharp.h` field `SS_RETIRED` in place. The client still
    reads the struct by position, so no field moves.
  - Delete the fill line and the `Engine.cs` wrapper, if any.
  - Delete the native body when it has no other native caller. A gtest-only caller goes
    with it.
  - Add a row to `docs/abi-retirements.md`.
- **0b.** Retire the 26 `ManagedApi` slots that no native code names.
  - Mark them the same way and delete their `Entry.cs` thunks and `pOut->X =` lines.
  - Delete the managed method a thunk forwarded to when nothing else calls it.
- **Instrument before deleting.** The two unused-slot searches become a `[Fact]`, so
  that an unused slot appearing later is caught:
  - managed: no reader of the slot outside `Abi.cs`;
  - native: no `grep -w` hit in `src/{server,shared,client}/native`.

  Break-it proof: add a dummy unread slot and see red.
- **A slot with a reader only in tests is unused.** Delete the test with the slot.
  Count asserts before and after.

### Phase A: the managed→native machinery, proven on one class

- **A0. Instrument.** Add `tools/engineapi-census`. It reads the fill (`#ifdef` walk)
  and `Abi.cs`, and fails on any slot that is in the struct but not in the fill. A
  `[Fact]` pins 318 live server slots, 7 retired, and 272 / 137 / 53 by side, each
  re-measured at the tip first.
- **A1. Commit `docs/engineapi-areas.tsv`**, moved from `plan_engineapi_vtables_areas.tsv`
  here. The census requires every live server slot to have exactly one row, and every
  row's `side` to match the fill.
- **A2. Generator: per-slot suppression.** `tools/vtable-layout` accepts `suppress=[…]`
  and emits `unmanaged[SuppressGCTransition]`.
  - Self-test arms: an arm is emitted for a slot on the list, and no arm for a slot off
    it.
  - Mutation proof: drop the list and see red.
- **A3. `IShimRoot` + `LoadSideV2`.** Add the header and the `CShellGameDll`
  implementation (`Acquire` wraps the `InternalFactory` rows, so
  `IShimFrame`/`IShimLevel` are reachable through it).
  - Add the managed `Entry.LoadSideV2(side, root, n, EngineApi* legacy, …)`. Here
    `legacy` is the struct while any contract still reads it, then NULL.
  - gtest: `Acquire` of an unknown name returns NULL with count 0.
  - gtest: the count equals `MemberOf(&IShimRoot::Release).slot + 1`.
- **A4. `IShimConsole` end to end.** It has 10 slots, is called from everywhere, and
  carries no entity semantics.
  - Rename the bodies in place and add the chain row.
  - A `[Fact]` and a gtest call through the vtable. The managed half (`ShimRef`, the
    acquire and count check, the console binding) is written in Abi beside the code it
    replaces, and Phase R moves it.
  - A negative control plants a count mismatch and must be refused, naming both numbers.

### Phase B: injection, and no static `Engine` (both sides)

- **B1. `ServerComposition` (Server) and `ClientComposition` (Client).** Each side
  builds its own `ServiceContainer` from its own root and constructs its module with it.
  Until Phase R, both call Abi's public `NativeContracts.Register(container, root, legacy)`,
  because the `Native*` classes still live in Abi.
  - `EngineServices.Defaults` (static, 15 non-test readers) is replaced by the per-side
    container.
  - `Native<Contract>` classes replace the `EngineBinding.cs` forwarders. Each holds the
    side's `EngineApi*` as a readonly instance field, so no side switch is involved.
  - Until R they sit in Abi and cannot be `internal` to their composer. A fence fact
    refuses any reference to a `Native*` type from the Server, Shared, Client, DevApi or
    MapGen assemblies, with a break-it proof. R retires the fence.
  - A `[Fact]` builds two containers in one process and shows a server object's call
    never reaches the client's table. This fact is impossible to write today.
- **B2…Bn. One contract per commit.**
  - Move that contract's `Engine.cs` static bodies into its `Native<Contract>` as
    instance methods (the bodies are unchanged except `Api.` → `_api->`).
  - Rewrite the static callers of those members (in `abi`, `host` and game code) to take
    the contract by constructor.
  - Replace null checks with an absent-side implementation registered on the side that
    lacks the slot.
  - Where static `Engine.X` has no contract today, A1's TSV says which contract it joins;
    a new contract is added only when none fits.
  - Lanes are split by contract, and each owns its `Native<Contract>` file. `Engine.cs`
    shrinks from the ends, and conflicts in it are resolved by taking both deletions.
- **B-log.** `Engine.Msg`/`Engine.Warning` (127 uses) go to an injected `IEngineConsole`.
  A type that only logs takes that contract alone.
- **B-host.** Bootstrap stops calling `Engine`. `LoadSideV2` hands raw pointers to
  `ISideModule.Compose`, one new method on an Abi interface with blittable arguments.
  Failures come back as return codes, and native prints them. `Entry.cs`'s 172 thunks
  reach their side's objects through the
  `GCHandle` of the side object, not through `s_Server`/`s_Client`. The generated thunks
  of Phase D replace most of them anyway, so this step covers only what Phase D does not.
- **B-last.** Delete `Engine`'s static members and `EngineServices`. Then:
  - `SetActiveSide`/`t_Active` go when their last reader does (161 writes in `host`
    today);
  - the analyzer from §1.6 lands: no static member access to an engine contract, and
    `ServiceContainer.Require` only in composition roots;
  - mutation proof: re-add one static call and see the analyzer go red.

  **(live)** at B1 and at B-last, and at each B step that moves entity, movement,
  player or sound bodies.

### Phase C: each server contract switches from struct to vtable

- **C1…Cn. One native class per commit** (§1.5 order: Movement, Players, Weapons,
  Inventory, Entities and their satellites, StringTables, Sound, World, Precache and
  Models, Effects, the rest).
  - Rename the native bodies in place and add the chain row and suppress bits.
  - Swap that class's `Native<Contract>` fields from `_api->X` to `_shim.Vt->X(_shim.Self, …)`.
    The server now acquires the class through the root, and the client still reads the
    struct.
  - Rewrite the gtests for the renamed functions, counting asserts per file.
  - Lanes are split by native `.cpp` owner. `vtable-layout` rows and the TSV go through
    the integrator.
- **C-last.** No server `Native<Contract>` reads `_api` any more, and the server passes
  `legacy = NULL`.
  - The server fill is deleted, and the server half of `EngineApi` is marked
    `SS_RETIRED`, listed in `abi-retirements.md`.
  - The integrator bumps `ABI_VERSION`. **(live)**

### Phase R: move the engine-facing code out of Abi

By now every engine call is an injected contract backed by a vtable (server) or by an
instance-held struct pointer (client), and `Engine.cs` is gone. Phase R changes where
code lives and nothing about what it does.

- **AS RUN (the user, 2026-10-01): R is partial before the client plan.** At
  123febe the Bootstrap, which may reference only Abi, still names ~20 of the
  census's moving files (Engine, Log, Crossings, ShimRoot, ShimConsole,
  MovementBridge, StandaloneNetStates, UserCmdCodec, BuildInfo, ModInfo ...),
  because the client still runs on Entry.cs thunks. R0 below found 58
  engine-facing files and 134 moving in all. The order is:
  1. split the mixed files first. Abi.cs and Entity.cs hold pure ABI types
     beside engine code, and their whole-file ruling drags the closure;
  2. re-measure;
  3. move every file the Bootstrap does not pin, leaves first;
  4. R-last's fact allows a NAMED residual (the pinned files), which the client
     plan empties.
  R1's premise is stale: the registration helper is EngineServices
  (EngineBinding.cs), not NativeContracts.Register.
- **R0. Census, re-measured at the tip of C.** `tools/abi-residency` builds a reference
  graph over `src/abi` types
  (Roslyn, run in the test project). It rules each file:
  - **stays**: blittable ABI types, `ISideModule`, cross-ALC value types;
  - **moves**: touches the engine, or depends on a file that moves;
  - destination: Shared when a Client or Shared consumer reaches the file, else Server.

  It writes `docs/abi-residency.tsv`. A `[Fact]` pins the count of engine-facing `abi`
  files: 74 at `54e8ea3`, measured by `grep` per file for `Engine.X`/`Api.X`,
  `delegate* unmanaged<void*`, `VtableLayouts`, `EngineServices` and
  `NativeInterfaceFactory`. The count is re-measured at C's tip, with the needles
  changed to contract references, `ShimRef` and `Native*`, because `Engine.X` no longer
  exists by then. It may only fall, and it ends at 0.
  - Break-it proof: add a contract reference to a staying file and see red.
- **R1. The registration helper dissolves.** During B and C, Abi's public
  `NativeContracts.Register(container, root, legacy)` built the `Native*` objects for
  the composition roots. Its body splits into `ServerComposition` and the Shared helper,
  and the `Native*` classes become `internal` to their new assembly.
- **R2…Rn. Move files in dependency order, leaves first.**
  - A file and the `Abi`-internal members it reads move together. `Abi`'s
    `InternalsVisibleTo` covers only Bootstrap and the tests.
  - Each commit is a `git mv` with the namespace unchanged, so history follows the file.
    The namespace rename to `SourceSharp.Shared.*`/`SourceSharp.Server.*` is the next
    commit.
  - Tests that used `Abi`'s internals switch to the new assembly's
    `InternalsVisibleTo`. Count facts per file before and after.
- **R-layouts.** `tools/vtable-layout` writes the Shared and Server outputs of §1.7.
  `AbiParityTests` and every test that reads a layout path follow in the same commit.
- **R-legacy (the user, 2026-10-01).** The two struct mirrors, `EngineApi` and
  `ManagedApi`, move out of Abi.cs into `src/abi/LegacyStructs.cs`, marked client-only:
  - after E-last only the client still fills or reads them (§6);
  - the client plan deletes them with `LoadSide` V1;
  - a fact pins that no server-side code (Server, the server's composition, server
    bindings) names either type;
  - Abi.cs keeps the types R0 rules as staying, so the legacy is visible as legacy
    rather than as most of Abi.cs (3,716 lines at 8d08e1d, ~2,500 of them the two
    mirrors).
- **R-last.** Abi holds only what R0 rules as staying. An assembly-level fact checks
  it: no type in `SourceSharp.Abi` implements an engine contract, holds a
  `delegate* unmanaged` outside the two retiring structs, or references
  `SourceSharp.Shared`. **(live)**, since an ALC change is a load-order change.
  - The B–C fence fact (no game assembly names a `Native*` type) is retired here, because
    `internal` now enforces it.

### Phase D: the native→managed machinery (backlog 9f steps 1-5)

- **D1.** Re-apply the `implemented-by-managed` chain kind by hand (5 refusal arms, each
  with its mutation proof).
- **D2.** `ManagedVtableGenerator` plus `src/abi/ManagedVtable.cs`. The generator's input
  is a managed contract, and its output binds whatever instance the container resolves.
  - Snapshot facts cover its output.
  - Each diagnostic has a fact that feeds it bad input. Feed synthetic source, not the
    tree's own declarations, or the test cannot fail.
- **D3.** Native accept check: pointer and count, refused on a mismatch. Prove it by
  planting a mismatch.
- **D4.** `IManagedRoot` is returned from `LoadSideV2`. `Acquire(name)` resolves from
  the side's container.
- **D5.** `IManagedRuntimeServer` is managed-built **(live)**. Delete the adapter in the
  commit after, not the same one.
- **D6.** Behaviour facts in `Tests/ManagedVtableTests.cs` (`DisableParallelization`,
  run memory-capped). Each gets a mutation that turns it red:
  - a GC compaction moves the object and the call still lands;
  - a released binding fails loudly;
  - an exception never escapes into native frames;
  - two sides' containers in one process are independent.
- **D0 (before D5).** Confirm §1.5's reverse grouping against the chain `Entry.cs`
  thunk → object → contract, and rename `IManaged*` to match the existing `IServer*`
  contracts where they correspond.

### Phase E: the server's `ManagedApi` moves

- **E1…En.** One `IManaged<Contract>` per commit, in §1.5's reverse table order.
  - Rewrite the native callers of those slots to `ManagedSide()-><area>`, and delete the
    `Entry.cs` thunks those slots used.
  - Mark the slots retired for the server.
  - Every caller lands on the new route in the same commit, because a NULLed slot is not
    a removed slot (backlog 9f, item 4).
- **E-last.** No server `ManagedApi` slot remains, and `LoadSideV2` stops taking
  `ManagedApi *` for the server. The integrator bumps the version. **(live)**, with
  `cs_sv_reload`.

---

## 3. Gates

- **Per step:** `make check`, zero warnings, and a clean fork
  (`git -C ../source-sdk-2013 status --porcelain` empty).
- **Retention halves** (doing less than asked cannot satisfy these):
  - unused slots: 7 + 26 → 0, pinned by the Phase 0 fact;
  - static `Engine.X` references outside `Engine.cs`: 1,459 in 85 files → 0 by B-last,
    as a ratchet `[Fact]` that may only fall, then `Engine`'s statics are deleted;
  - `EngineServices.Defaults` readers: 15 → 0;
  - engine-facing files in `src/abi`: 74 at `54e8ea3`, re-measured at C's tip, → 0 by
    R-last, pinned by the R0 fact and the assembly-level fact;
  - server `Native<Contract>` fields reading `_api`: all → 0 by C-last. This is measured
    by the census, not by a grep for the struct name;
  - server `ManagedApi` slots named by native code: 123 → 0 by E-last;
  - `managed_runtime_server.cpp` absent from the server graph (`tools/cmakegraph.py`).
- **Behaviour:** `make live-gate`, with both map loads, at each **(live)** step and
  before merge. `cs_sv_reload` runs at D5 and E-last, because it is the lifetime test.
- **Cost:** record ns per crossing for one suppressed and one unsuppressed slot at three
  points, with `tools/bench-managed-vtable` taken from `2f67ee4ff`:
  - before (static + struct);
  - after B (instance + struct);
  - after C (instance + vtable).

  The numbers are recorded, not gated (memory `move-first-measure-after`).

## 4. Risks

| risk | control |
|---|---|
| A suppressed slot moves without its attribute: silently slower, and invisible to the audit | A2 lands first. The census fails when a TSV row's `suppress` disagrees with the generated struct. |
| A suppressed method grows a path back into managed (the `EntitySetOrigin` class of bug) | The audit keeps reading native bodies, now member functions. Its mutation proof is re-run at A2. |
| A null-check guard is replaced by an absent-side implementation that answers differently | Each B step keeps a `[Fact]` on the absent side's answer, taken from the null check it replaces, before and after. `SideAuditTests` waivers are re-read at B1. |
| An object gets the other side's contract (a container crossed) | The B1 two-container fact. Composition is per side and has no shared static to leak through. |
| A service locator creeps back (`Require` called outside a composition root) | The B-last analyzer, with its mutation proof. |
| Renumbering during parallel lanes | Native classes are new interfaces, so nothing inside `EngineApi` moves; retirements mark fields in place. Chain rows and the TSV go through the integrator. |
| A late call to a released managed area lands on another object | Native nulls before `Release`. D6 fact plus a `cs_sv_reload` live run. |
| A contract type crosses sides through Abi (Shared is loaded twice, so each side's `IEngineSound` is a distinct type) | Only `ISideModule` and blittable types cross, as today. The R-last fact refuses any Abi member typed as a Shared type. |
| A moved file changes static-init or load order | R-last runs the live gate. Each R step is a `git mv` with no edits, so any breakage is attributable to the move. |
| A scripted rewrite (gtests, or static callers → injected) drops asserts | Count asserts per file before and after each scripted edit. |

## 5. Decisions taken here (flag any to overturn)

1. **Function-grouped native classes (38 classes, 26 bound pointers), not one
   server-sized class.** They are split into a shared base plus a server extension
   where a family has both-sides slots.
2. **The native grouping follows the managed contracts where one exists.** Constraints,
   springs and motion controllers are three classes because three contracts exist. A
   contract may still read several native classes (`IEngineEntities` reads nine),
   because native is grouped by owner and managed by use.
3. **Injection before vtables** (B before C). Callers change once. There is no legacy
   bridge class, because the server passes both the struct and the root while C runs.
   The client's statics go in this plan even though its vtables do not.
4. **Stateless singleton implementors on the native side.** Folding state into
   `m_pDll`/`m_pLevel` stays `plan_globals` G2's job.
5. **Unused slots are retired first** (Phase 0), so nothing is ported only to be deleted.
6. **Relocation (R) after C.** B and C change behaviour in place, and R only moves
   code. The cost is that each `Native*` file is written in Abi and moved once, which is
   a `git mv`. For that window the fence fact stands in for `internal`.
7. **The reverse direction (D, E) after R**, so the generated thunks land directly in
   Server and each direction is proven separately.

## 6. Not in this plan

- **The client's vtables:**
  - the 137 client-only slots and the client's `ManagedApi`;
  - wiring the shared bases' client arms;
  - deleting `LoadSide` V1, then the `EngineApi`/`ManagedApi` structs themselves.

  After Phase B the client already reaches the struct only from inside its
  `Native<Contract>` classes, so its wave is Phase C and Phase E again, with A2, A3 and
  D1–D4 unchanged.
- Moving native area state into root-owned subsystems (`plan_globals` G2/G3).

---

## 7. As run (2026-09-29/30)

What changed from the plan above while executing it, and why. Numbers are in
the commits that made them.

- **Phase 0 retired 38 slots, not 33.** Comment-stripped scans found three more
  unused ManagedApi slots (named only in comments), and server-novalve's work
  made FindEntityByName and the two break-piece slots unread. One removal pass,
  ABI 251 -> 252.
- **The shim root owns every area by value** (A4 onward), so the DLL gains one
  static for all areas, not one per area.
- **C0 ran before B finished.** It touches only native files and EngineSide, which
  the managed-di merge did not. tools/shim-areas generates the 26 server areas
  (304 slots) from docs/engineapi-areas.tsv. EngineSide reaches every area slot
  through a generated Slot_X / Has_X helper, and docs/engineapi-switched.txt says
  which areas route through their vtable.
- **Forwards, not renames** (a deviation from §1.2): the generated classes
  forward to the existing free SourceSharp_* bodies, because 969 gtest calls and
  tools/entrypoint-decls depend on them. The console (A4), whose bodies were
  file-static, was renamed in place.
- **Log stays static** (the owner's ruling, 2026-09-30, over injecting a logger
  into 190 files). It is the one permitted static, and it prints through the
  calling side's own engine console binding (B4).
- **The static Engine's last forwarders go with Phase D, not at B-last.** After
  B1-B5, what still calls them is mostly statics that native reaches through
  Entry.cs thunks (MovementBridge, PlayerShadow, PhysicsSurfaces,
  StandaloneNetStates, ScriptedVars.Now, ...). They become container services
  when Phase D replaces the thunks with managed-built vtables. The forwarder
  ratchet stands at 110 and falls to 0 there.
- **The client may implement the shared vtable areas too** (the owner, 2026-09-30).
  Shared areas then switch on both sides at once in Phase C, with no dual
  struct/vtable path for their slots.
- **C-last ran as two commits.** C6b (`c756cba`) has the server pass a NULL
  struct to LoadSideV2. C6c removes the 262 server-only fields from both
  structs (ABI 252 -> 253), makes the fill client-only, and drops the server's
  `g_EngineApi`. A server-only `Slot_X` has no struct fallback: it throws if
  called before the shims attach, and `Has_X` is false until then. The slot
  table became the server's source of truth. tools/entrypoint-decls reads the
  server prototypes from it and from ishim_areas.h, and the census and side
  audit read ishim_areas.h/ishim_console.h instead of the fill. The fill can no
  longer tell Both (one body) from Dual (one body per side), so the side audit
  compares them as one set.
- **D runs before R** (the owner, 2026-09-30). R assumed that Engine.cs and
  Bootstrap's direct calls into Abi were gone after C, but those moved to D.
  Entry.cs names types in 56 Abi files (about 21 of them engine-facing), which
  cannot leave Abi while Bootstrap calls them. D replaces those thunks, and R
  then moves every engine-facing file in one pass.
- **CommandLineHasParam/GetParamValue retired** (b0b3c56), once server-novalve
  P4.5 made the process's own command line readable; the server reads
  ILocalCommandLine.
- **D5 reshaped IManagedRuntimeServer, and the adapter left in the same
  commit.** The interface the plan called "already a C++ abstract class" broke
  three of the managed-implemented kind's rules: a protected virtual
  destructor, MapLoadInfo passed by value, and OnSystemStop, which unloads the
  managed side and so cannot be managed. It lost the destructor and the stop
  (DLLShutdown calls SourceSharp_UnloadManagedSide), its map loads take
  `const SourceSharpMapLoad *`, and Release was appended. The adapter could not
  compile against that, so it could not wait a commit. The shell keeps no
  pointer: it calls ManagedRuntimeServer(), which the shim root holds and every
  load and reload acquires again. Boundary code marks the side through
  Abi.SideEntry, fenced to *Binding.cs, until E deletes the thread-static.
