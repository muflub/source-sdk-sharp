# Plan: HL2MP AI — a player-equivalent, event-fed, off-thread AI (generic core + HL2MP binding)

Status: DRAFT, written 2026-11-01 at main `6ba2676f4`; amended the same day
(tip `ad7e68a59`) with the user's rulings and six further features (§12.1).
APPROVED by the user 2026-11-01 ("Plan is good, commit then start
implementing"); committed at main `5c3f6bada`. Paths and numbers in §1 are
true at `6ba2676f4`; each phase re-measures at its own base.

## 0. Goal and shape

Bots for the HL2MP mod (mod id `test`: `bin/debug/mods.yaml` `default_mod:
test`, content `game/mod_test`, server assemblies `[SourceSharp.Server]`; HL2MP
is the core itself) on the architecture of Appendix B of
`~/git/sharp-fresh/docs/plans/plan_tf2_rpg.md` (restated in
`docs/plans/tf2/stage5_bots.md` §5.0; ruling D3 in `plan_tf2_port.md` §0).
HL2MP first because it needs no 3D nav mesh and no class system; TF2 reuses
the core and writes its own binding (§1.3).

The hard constraints, each a build fact or a test, not a convention:

1. **A bot acts only as a client can.** Its single output is a user command
   (`src/abi/UserCmd.cs`: `ForwardMove/SideMove/UpMove`, `Buttons`, `Impulse`,
   `WeaponSelect`, `Pitch/Yaw/Roll`), queued into the same
   `PlayerCommandQueue` (`src/server/interop/PlayerCommandQueue.cs`) and run by
   the same movement loop and weapon frame as a human's packet. No teleport,
   no entity write, no server-only shortcut (§3).
2. **A bot perceives only what a client at its eyes would be sent.** Sight is
   cone + PVS + trace from the bot's eyes; hearing is membership in the
   sound's recipient filter; the rest is its own HUD. Enforced by assembly
   references (the AI core cannot name the engine, the entity directory or
   the ABI) and by negative facts (§4). That includes an opponent's ammo: a
   bot counts the shots and reloads it heard or saw, never the real clip
   (§4.5, §5.3).
3. **It remembers and predicts.** Per-opponent memory with decaying
   confidence, learned route priors over the nav graph, ammo and reload
   estimates, and predictors for "where are they now" and "what will they do
   next" that the decision layer reads (§5). Deterministic under
   record/replay (§2.6).
4. **It moves and shoots like a human** (ruling 2026-11-01): a motion model
   with reaction delay, ballistic flicks, pursuit lag, drift and recoil error,
   human shooting cadence and human-looking movement, **measured** against
   recorded human sessions and gated by distribution distance, with a
   spin-bot detector (§3.4, §8.2, §9 Phase 5b).
5. **100 % managed** (ruling 2026-11-01): perception, nav sampling, memory,
   brains, the inspector and the bot-setup UI are C#; zero hand-written
   C++. The one thing managed cannot do — ask the engine for a client slot
   — is a generated forwarder row over an existing engine function, marked
   for the user's ruling in §3.1.

AI work runs on a **pool of one or more worker threads, never on the main
game thread** (ruling 2026-11-01; §2.7); the main thread never waits on it;
a brain step is a pure function of `(seed, decide tick, that tick's
snapshot, the events since its last step)`, so intents are identical at any
thread count (§2.6).

Two UI features ride on it (ruling 2026-11-01): a **per-bot inspector**
in the web UI only (memory, skill allocation, what it is doing and
planning; §7.2) and **bot
setup when creating a server** (an Easy/Medium/Hard slider and a custom bot
built from a point pool, with save/load and history; §3.5).

Navigation comes from the **`.nav3d` file the map-tools repo writes when a
map is compiled** (ruling 2026-11-01; §6), loaded through its managed
reader; a missing or mismatched file is a refusal. Purpose-built **`ss_ai_*`
test maps** (ruling 2026-11-01; §6.6) are the first nav inputs and each
feature's scripted scenario.

## 1. Ground truth at `6ba2676f4` (every path below was read this session)

### 1.1 What exists

| Need | What the repo has | Where |
|---|---|---|
| Usercmd intake | Native `CServerGameClients::ProcessUsercmds` hands the packet to managed in one crossing (`IManagedPlayerCommands.PlayerDecodeCommands`, `docs/managed-interfaces.tsv:80`); managed decodes, plans and queues per player; native counts pending (`CStandalonePlayer::QueuedUsercmds`, `shell_gameclients.cpp:356`, `shell_standalone_player.cpp:283`) | `src/server/native/shell_gameclients.cpp:314`, `src/server/interop/PlayerCommandQueue.cs` (`Queue` :161, `DecodeAndQueue` :263, `MaxMoveMagnitude` :239) |
| Usercmd run — **the loop is managed** | Each frame `CServerFrame::StandalonePlayerCommands` → `RunAllQueuedCommands` (gated by `m_nPendingCmds` and `m_nSimulationTick`, saves/restores `curtime`/`frametime`) → one crossing `PlayerRunCommands(hPlayer, n)` (`managed-interfaces.tsv:79`) → `PlayerCommandsBinding.PlayerRunCommands` (`src/server/interop/PlayerCommandsBinding.cs:85`: `ServerPlayerCommands.AdjustTickBase`, then `PlayerCommandQueue.Run`) → `Run` (`PlayerCommandQueue.cs:417`) calls the managed stages (`ServerPlayerCommands.Stage`, `src/server/interop/ServerPlayerCommands.cs:158,183`) which reach the engine only through the existing shim rows `PlayerCommandBegin/View/Finish` (`docs/engineapi-areas.tsv:50-52`; native reads facts and applies the result, `shell_standalone_player.cpp:399,424`), and the module's phases (`ServerModule.OnStandalonePlayerCommand` :2215, `OnPlayerRunCommand` :4010) | `src/server/native/shell_frame.cpp:72`, `shell_standalone_player.cpp:289-330` |
| Player entity | `SsPlayer` (sealed, `src/server/managed/Entities/Players/SsPlayer.cs`), created on the engine's `ClientPutInServer` → `ServerModule.OnClientPutInServer(entIndex, name)` (:3592) → `IManagedStandaloneClient.OnStandaloneCreate` | `docs/managed-interfaces.tsv:59,98` |
| Fake-client tolerance | `RegisterClientMessage`: "A bot has no channel" (null `GetPlayerNetInfo` is handled) | `src/server/native/shell_gameclients.cpp:119` |
| Traces | `ITraceQueries.Line/Hull(start, end, [mins, maxs], mask, pass)` | `src/server/interop/TraceQueries.cs:23` |
| PVS | `IEnginePlayers.BoxInPvs(player, mins, maxs)` from the player's eyes (engine `GetClusterForOrigin/GetPVSForCluster/CheckBoxInPVS` FORWARD slots) | `src/server/interop/EngineServer.cs:197`, `src/server/managed/PvsCheckClient.cs` |
| Sounds | `IWeaponSounds.Emit(owner, soundName)` → `ServerSounds.EmitPredicted` (PAS by the sound's level, owner excluded by prediction rules; `src/server/managed/ServerSounds.cs:140-153`); `PlayStepSound` from the movement loop (`Movement.cs:2562,2710` → `MovementBridge.cs:292`); `IEngineSound` (`src/shared/interface/EngineAreas.World.cs:168`); recipient rules `SoundRecipients` (`SoundLevelToAttenuation` `RecipientFilter.cs:170`, `PasAttenuation` :219-228) / `ISoundListeners.TryEars` | `src/server/managed/IWeaponSounds.cs`, `RecipientFilter.cs:137` |
| Reload sounds | Weapon-specific, as stock: shotgun, SMG1, RPG and pistol play `Arms.Sound(ReloadSound)` on both sides (`src/shared/managed/Hl2mpWeaponBehaviours.cs:190,454,971,1046`); the generic `DefaultReload` path plays `ClientSound` only — the owning client's, nothing on the server (`WeaponLogic.cs:766-768`, `Hl2mpWeaponBehaviours.cs:642-644`; `IWeaponArms.ClientSound` default is empty, `WeaponLogic.cs:30-37`), which is stock's `#ifdef CLIENT_DLL` around `WeaponSound(RELOAD)` (`…valve/src/game/shared/basecombatweapon_shared.cpp:2042-2045`) | |
| Reload animation | `PlayerAnimEvent.Reload/ReloadLoop/ReloadEnd` (`src/shared/managed/Anim/PlayerAnimEvent.cs:30-32`) drive the networked player animation state a watching client renders (`MultiplayerAnimState.cs:1328-1343`) | |
| Weapon definitions | `WeaponType.ClipSize`, `ReloadSound` from `scripts/<weapon>.txt` via `WeaponScriptDatabase` (`src/shared/managed/WeaponScript.cs:68,299`); fire cadence and reload length are the behaviours' constants and animation durations (`Hl2mpWeaponBehaviours.cs:191,231,1002`) | |
| Damage | `SsPlayer.OnDamagedAt(attacker, damage, type, position, force, hitGroup)` (:424); `IDamageRule`/`DamageContext` (`GameRulesDamage.cs:30,71`); `IPlayerDeathHook`/`PlayerDeath` (`PlayerDeaths.cs:12,21`); `IRespawnGate` | `src/server/managed/` |
| Hooks and DI | `IGameLifecycle` (`OnLevelInitPreEntity/PostEntity`, shutdown; `GameLifecycle.cs:64`), `IFrameLifecycle` (`OnPreThink/OnThink/OnPostThink/OnPreClientUpdate`; `FrameLifecycle.cs:57`), `IHooks<T>` (`CoreServices.cs:364`), `[SideService]`, `ISideServiceRegistrar`, `ISideServiceRegistry.AddHooks<T>` (`src/shared/managed/SideServices.cs`), `IEngineFrame.TickCount()` (`EngineAreas.Frame.cs:38`) | |
| Random | `IUniformRandom.RandomFloat/RandomInt` | `src/shared/interop/UniformRandomStream.cs:24` |
| Game events | `PlayerDeathArgs`, `PlayerConnectClientArgs(... IsBot)` | `src/shared/managed/GameEvents.cs` |
| Movement constants | `sv_stepsize` 18 (`MoveVars.cs:250`), `StockJumpHeight` 21 (`Movement.cs:504`), hulls `MoveConstantDefaults.Stock` (`SsPlayer.ShadowHulls` :193), tick 0.015 s (`Movement.cs:3566`), `MaskPlayerSolid` (`Movement.cs:585`), ladders `MovementUseableLadders.cs` | `src/shared/interop/` |
| Analyzers | mods: SS0010–SS0013 (`ModCoreAccessAnalyzer.cs`: no static fields, no events, no `Engine`/`EngineServices`/process statics) for the `SourceSharpModGated` list (`Directory.Build.props:72`); SS0001 statics; SS0030 interface shape | `src/analyzers/` |
| Reference facts | `LoadedAssemblyTests.EveryScannedAssemblyReferencesTheAbi` (`src/test/server/LoadedAssemblyTests.cs:80`) is the `GetReferencedAssemblies` precedent | |
| Native census | `tools/native-symbols` compares the build with `docs/native-symbols.tsv` (4,406 rows; module, source, function) and is a routine check (`tools/checks.tsv:37`) | |
| Dev API + web UI | `DebugApiServer` routes (`/map/geometry`, `/entities`, `/events`, `/counters`, `/player/aim`, …; `src/debug_api/DebugApiServer.cs:1069,1335`), `IDebugServerHooks` (log, build, catalog, status, world, counter groups; `src/debug_api/interfaces/IDebugServerHooks.cs`), web UI map tab with a hand-written WebGL wireframe renderer (`src/webui/src/map/viewport/renderer.ts`), graph pane, inspector; `tools/debug-api` subcommands | |
| Client UI toolkit | `UiScreen` screens in `src/client/ui/screens/` built by DI from data interfaces (`src/client/interface/Ui/UiSources.cs`; a mod's interfaces live in its UI assembly, its adapters in its client assembly — `src/tf2_descent/ui/DescentUiSources.cs`, `IDescentSheetActions.Stage/Apply/Reset` :214-218 is the point-spend precedent, with golden `charsheet-mid-spend-l18` in `src/test/client/Ui/UiGoldenTests.cs:41`); parts: `ui.Dialog`, `ui.FormGrid`, `ui.Tabs`, `ui.VirtualList`, `ui.Meter`, `ui.Button`, forms with `ui.Slider(key, value, min, max, step)` (`src/client/ui/toolkit/Forms/UiFormWidgets.cs:17`), `ui.Choice`, `ui.EditText`; menus open by name (`cs_ui_open scoreboard|teammenu|motd|console|serverbrowser|mouseoptions|keybindings`, `src/client/managed/Ui/UiRuntimeContract.cs:108`); the loop and goldens in `docs/ui/authoring.md` §2 and `.claude/skills/ui-screen` | |
| Client → engine commands | `InputHost.ClientCmdUnrestricted(string)` (`src/client/interface/Input/InputHost.cs:35`); the server browser's Connect button is the model (`src/client/ui/screens/ServerBrowser.cs:82`) | |
| Server → client data | networked entity state (`[NetState]`, `StandaloneNetStates.Discover`, `ServerModule.cs:232`; client classes via `IClientStandalones`, e.g. `ScoreboardUi` `src/client/managed/Ui/MenuSources.cs:14`); string tables (the MOTD's `InfoPanel`, `MenuSources.cs:204`); user messages (`[UserMessage]`, `src/shared/managed/UserMessages.cs`, `IUserMessageOutbox`/`IUserMessageDispatch`; stock ones are a few bytes, `Size = 13` at most) | |
| Client files | `EngineBaseFileSystem` (shared interop, both sides): `FileExists`, `ReadAll`, `Open(name, options, pathId)`, `Write`, `Close` (`src/shared/interop/EngineBaseFileSystem.cs:147-223`; the `BaseFileSystemVtbl` row carries `Write`, `tools/engine-forwarders:183`) | |
| Client input is managed | `ClientInputService.CreateMove` builds the usercmd in C# (`src/client/managed/Input/ClientInputService.cs:86`) | |
| In-game overlay | `IFwdDebugOverlay` (`src/shared/interop/EngineDebugOverlay.cs`, e.g. `TextAtPosition`) | |
| Offline PNG | `src/tools/UiPreview` renders with SkiaSharp (`SkiaBackend.cs`); `src/tools/StockAb` compares shots | |
| Test legs | `MANAGED_TEST_LEGS` (`Makefile:867`): managed, managed-server, managed-client, tf2_descent, tf2; each a `dotnet test` of a built DLL with `tools/leg-floor`; `TF2.Tests.csproj` is the mod-leg model (`OutputPath bin/$(Config)/tf2-tests`) | |
| Test support | `src/test/support/TestSides.cs` (`TestSides.Create`), `server/TestEngines.cs`, `DefaultForwarders.cs`, `ShimAreaDefaults.g.cs`, `src/test/common/FakeShimRoot.cs`, `IlCallCensus.cs` | |
| Threads precedent | `src/tf2_descent/server/SdkThread.cs:26`, `src/debug_api/HttpSocketServer.cs:135` (`new Thread`, background, named) | |
| BSP readers (C#) | `src/debug_api/BspFile.cs` (entities, vertexes, faces, edges, surfedges, models), `src/mapgen/BspBrushContents.cs` (nodes, leafs, models, leafbrushes, brushes) | |
| Map tools (`../source-sdk-map-tools`, tip `83bbf57`, read-only) | `ssmap link <level.yaml>` writes `<map>.nav3d` beside the linked bsp (`docs/nav3d-format.md:59`, README:997-999; `-no-nav`/`-require-nav`, README:486): a 3D clearance grid (16 u voxels in 256 u cells) answering any agent box, per clip class, with floors, water/ladder flags, costs, **jump links up to `jumpHeight` 56 (a crouch jump) and 100 u reach** (format §9.2), dynamic obstacles (doors, movers, toggles, breakables, physics, props; format §10), points of interest, and a level id bound to worldspawn `ss_level_id` (format §12). Reader `Nav3dReader` in `src/SourceSharp.MapFormats/Nav/` (`Open`, `MatchesMap`, `FindLeaf`, `Neighbours`, `Standable`, `StepUp`, `Jumps`, `Component`, obstacle leaves; allocation-free after `Open`; format §14 is the mod-side sample). Builders in `src/SourceSharp.MapTools/Nav/`: `NavClearanceBuilder.Build(NavGeometry, region, settings)` (:67,77) over `NavGeometry.FromBsp(BspData, modelBounds)` (`NavGeometry.cs:114-117`, brushes from the bsp via `NavBrush.FromBspIndexed`, `src/SourceSharp.MapFormats/Nav/NavBrush.cs:289`, obstacles and ladders from the entity lump) — **the builder already takes a compiled bsp's brushes**. `ssmap nav <map.nav3d \| level.yaml> [--obj] [--floor] [--agent]` prints and exports a navigation (`NavCommand.cs:32-33`). | |
| `ss_sandbox` | a generated VMF (`make sandbox` → `src/mapgen` → `game/mod_test/maps/ss_sandbox.vmf`, `Makefile:563-564`) compiled per tree by `make map MAP=ss_sandbox` through `ssmap all` (`Makefile:640-652`; `MAPTOOLS_DIR` beside this repo, :526-540); bsp only, never copied between trees | |
| Live gate | `tools/live-gate` `STEPS` (:9352): join, move, jump, shoot, damage, pickup, …, second-client, map-reload, map-change | |

### 1.2 What is missing (each is "new" below)

- **Bot creation.** `IVEngineServer::CreateFakeClient` and `CreateFakeClientEx`
  are stubbed in the vendor header (`vendor/shared/public/eiface.h:211,385`
  `unused_…`; also `unused_SetFakeClientConVarValue` :295) by
  `tools/vendor-strip --stub`. No FORWARD row (`grep FakeClient
  src/server/interop/ServerForwarders.g.cs src/shared/interop/SharedForwarders.g.cs`:
  0 hits), no shim row, nothing on `IEnginePlayers`
  (`src/shared/interface/EngineAreas.Players.cs`). Stage 5's "check
  `IEnginePlayers` for a bot-add call" finds none. The live gate's
  second-client step and `make join` use a second game process. §3.1.
- **Running a bot's commands needs no native change** (corrected from the
  first draft, which planned a shim row): the run loop is managed
  (`PlayerCommandQueue.Run`, §1.1); only its entry from the frame is native.
  A managed caller can run a bot's queue itself (§3.2).
- **Nav data for the maps we have.** No Valve `.nav` anywhere
  (`hl2mp_pak_dir.vpk` of Source SDK Base 2013 Multiplayer has 0 `.nav`
  entries; the HL2DM install here has no `maps/` on disk; `dm_lockdown.bsp`'s
  pakfile has none), and nothing managed reads one; the stock 2025 HL2MP
  bots need `TheNavMesh` (`hl2mp_bot_manager.cpp:196`). The nav source is
  map-tools' `.nav3d` (ruling 2026-11-01), but **no map in this repo has one
  today**: `ssmap link` is the only writer (README:997; `MapCompiler.cs`,
  the `vbsp`/`all` path, has no nav output — `grep -i nav3d` is empty), and
  `ssmap nav` takes a `.nav3d` or a `level.yaml`, not a bsp
  (`NavCommand.cs:32-33`). dm_lockdown is a stock Valve-compiled bsp with no
  VMF anywhere on this machine and no `.nav3d`; `ss_sandbox` is a VMF compiled
  through `ssmap all`, so it has none either. The builder already reads a
  compiled bsp's brushes (`NavGeometry.FromBsp`), so the gap is one
  command and one binding field: §6.4 lists the map-tools items, approved
  by the user on 2026-11-01.
- **The map-tools reader is not referenced here.** Nothing in this repo or
  its branches references `SourceSharp.MapFormats` or `Nav3dReader`
  (`git log --all -S"SourceSharp.MapFormats"` finds only this plan's
  commit and the repo's first); the `host-maptools*` worktrees are
  map-tools clones. The README (:141, format §14) says "the mod references
  this assembly" and the assembly has no dependencies, but names no package
  or path. §6.1 plans the reference.
- **A dev-API route hook for a mod.** `IDebugServerHooks` is a fixed set of
  core providers; a mod cannot add routes today. §7.4.
- **A create-server screen.** None on main: the managed menus are the seven
  `cs_ui_open` names above; "Create Server" is still the engine's GameUI
  dialog (`src/client/ui/host/UiRuntime.cs:276`: "GameUI's windows and the
  HUD are not the viewport's"), and `plan_client_ui.md:399` lists "C4's
  GameUI dialogs (options, create server, player list)" as forms to come.
  The TF2 lane (`../ss-tf2`, off main) has class/build/disguise menus and
  no create-server screen either. §3.5 plans the smallest one that fits.
- **A map list.** Enumerating `maps/*.bsp` needs `IBaseFileSystem`
  `FindFirst/FindNext`, which are not forwarded; §3.5 takes a typed map name
  checked with `FileExists` instead, and leaves the list for later.
- **Appendix B's "no `SourceSharp.Abi` reference" has a consequence here:**
  `Vector`, `EntityHandle` and `UserCmd` live in `SourceSharp.Abi`
  (`src/abi/Entity.cs:40,154`, `src/abi/UserCmd.cs`). The core therefore
  defines its own primitives (§1.3), and the binding translates.
- **No MapForge, no room graph, no cell visibility table** (B.3 assumes them).
  PVS and traces replace the visibility table; recipient filters replace
  path-distance hearing (§4.2, a deliberate change: it is what a client gets).
- **No human reference recordings.** Nothing records a human's usercmd
  stream today; §3.4 adds a core hook and the recorder captures it.

### 1.3 Assemblies (all new)

| Project | Path | References | Role |
|---|---|---|---|
| `SourceSharp.Ai` | `src/ai/core/` | BCL only. **No `SourceSharp.Abi`, no `SourceSharp.Server`, no `Debug.Api.*`.** | The generic core: data types (`AiVector`, `ActorId`, `CellId`, events, snapshot, `PerceivedView`, `IntentCommand`), memory, predictors (position, intent, ammo), nav graph + flow fields + A*, behaviour-tree runtime, skills (`SkillProfile`, `SkillPool`, curves), the human motion model (§3.4), the brain, the worker pool and inline transport, recorder/replayer, `EditHistory` (§3.5). Pure, allocation-free in steady state. Referenced by both sides (it is BCL-only), so the client's editor and the server's `ai_bot_add` validate a profile with one class. |
| `SourceSharp.Ai.Hl2mp` | `src/ai/hl2mp/server/` | `SourceSharp.Ai`, `SourceSharp.Abi`, `SourceSharp.Server`, `Debug.Api.Server.Interfaces`, **`SourceSharp.MapFormats`** (map-tools' reader package, §6.1) | The HL2MP server binding: bot players, sense taps, perception builder, command applier, the `.nav3d` loader and graph derivation, skill presets, dev-API routes, the `ai_bot_*` commands. A mod assembly in `SourceSharpModGated` (SS0010–13 apply) and `SourceSharpStaticStateGated`. |
| `SourceSharp.Ai.Hl2mp.Client` | `src/ai/hl2mp/client/` | `SourceSharp.Ai`, `SourceSharp.Abi`, `SourceSharp.Client`, `SourceSharp.Ai.Hl2mp.Ui` | The client binding: `[SideService(Side.Client)]` adapters that feed the UI's data interfaces (profile store over `IFwdBaseFileSystem`, the create-server actions over `ClientCmdUnrestricted`). Mod-gated. |
| `SourceSharp.Ai.Hl2mp.Ui` | `src/ai/hl2mp/ui/` | `SourceSharp.Client.Ui.Toolkit` (as `TF2.Descent.Client.Ui`) | The screens (`CreateServer`, `BotEditor`; no in-game inspector, §7.2) and their data interfaces (`AiUiSources.cs`), samples and goldens. Listed in `mods.yaml` `assemblies.client_ui` (`src/mods/ModsConfig.cs:51`). |
| `SourceSharp.Ai.Tests` | `src/ai/tests/` | all above + `SourceSharp.TestSupport.Server` + `.Client` (aliased, as `TF2.Tests.csproj` does) | Its own leg `ai` (§8.1). |
| `SourceSharp.Ai.Replay` | `src/tools/AiReplay/` | `SourceSharp.Ai`, SkiaSharp (as `UiPreview`) | Offline: replay a recording, render PNG frames, compute the human-vs-bot distributions (§3.4.4), assert golden images (§7). |

No `SourceSharp.Ai.Interface` project: the core's public types are already
data-only and behaviour-free by construction, and nothing reloads across a
context boundary here. UI types live in the UI assembly, never in a
`*.Interface` assembly (CLAUDE.md).

Wiring: `bin/debug/mods.yaml` `mods.test.assemblies` becomes `server:
[SourceSharp.Server, SourceSharp.Ai.Hl2mp]`, `client: [SourceSharp.Client,
SourceSharp.Ai.Hl2mp.Client]`, `client_ui: [SourceSharp.Ai.Hl2mp.Ui]` (a mod
restates each list). Listing is the registration: `[SideService]` classes are
scanned; hook interfaces the binding defines are registered by an
`ISideServiceRegistrar` with `AddHooks<T>`. TF2 later lists its own binding
assemblies; the core is untouched by that (gate in §9 Phase 1: the core has
no string, type or constant naming HL2MP).

Generic vs HL2MP-specific, drawn now so TF2 needs no rework:

| Generic (core) | HL2MP-specific (bindings) |
|---|---|
| Event, snapshot and command types; `PerceivedView`; memory, decay, predictors (position, intent, ammo); nav graph, flow fields, clearance classes; BT runtime and node library; skills, pool, profiles, history, curves; human motion model; worker pool, inline transport; recorder/replay; distribution metrics | Which entities are opponents, items, hazards; the weapon table (clip, cadence, reload length, ranges, projectile speeds); the HL2MP behaviour trees; the `.nav3d` loader and the player-hull graph derived from it (stand/crouch boxes, jump vs crouch-jump by HL2MP's movement numbers); sense taps on HL2MP's sound/damage/animation paths; `UserCmd` encoding of an intent; presets; which map entities seed the graph; the three screens |

## 2. Architecture (Appendix B, mapped onto this repo)

### 2.1 One tick

```
 main thread, tick N (IFrameLifecycle)                       worker pool (1..n threads)
 ─────────────────────────────────────────────────────       ──────────────────────────────
 OnPreThink   APPLY: read intent slots + one-shot queue   ◀── slots (latest per bot) + ring
              validate (ActorId serial, seq, expiry,
              alive, not observer) → motion model →
              one UserCmd per bot → PlayerCommandQueue
              .Queue → IPlayerCommandRunner.RunQueued
              (the managed loop, §3.2: the bot's command
              runs exactly as a client's)
 OnPostThink  SENSE: taps already pushed sound/damage/
              gameplay events this tick; vision pass
              (cone → PVS → trace budget) → sight events
              PUBLISH snapshot N into the ring (latest   ───▶ a BrainStep(bot, N) job for each
              wins for readers; ring keeps 8) + events        bot whose decide tick is N:
              enqueue jobs; never wait                         remember → predict → decide →
                                                               path → steer → write intent
                                                               "from N" into the bot's slot
```

Steady state a bot acts on N+1 on what was true at N: 15 ms, below any
reaction time in §10.

### 2.2 Inputs

**Events** (SPSC ring per worker fan-in, never dropped; on overflow the
producer increments a counter the gate reads, and does not overwrite). Core
types, HL2MP values:

| family | event | produced by (binding) |
|---|---|---|
| sound | `SoundHeard { bot, kind: Gunfire/Reload/Explosion/Footstep/Pain/Death/Pickup/Door/Physics, pos, loudness, sourceActor?, weaponKind?, tick }` | the tap on `IWeaponSounds.Emit` (fire and the both-sides reload sounds), the step-sound bridge, the explosion/pickup/door emits — **only for bots in the sound's recipient set** (§4.2) |
| sight | `SightGained { bot, target, pos, vel, visibleFraction, tick }`, `SightLost { bot, target, lastPos, lastVel, tick }`, `SeenFire { bot, shooter, weaponKind, tick }`, `SeenReload { bot, actor, phase: Start/Loop/End, tick }` (only while the actor is sighted, §4.5) | the main-thread vision pass (§4.1) and the animation-event tap |
| hurt | `Damaged { bot, attacker?, amount, dirFromBot, type, tick }`, `Killed { bot, attacker?, tick }`, `AttackerSeenKill { bot, victim, attacker }` (only if the bot saw it) | `OnDamagedAt` tap, `IPlayerDeathHook` |
| self | `Spawned { bot, seed, skill }`, `Removed`, `Respawnable`, `Landed`, `Stuck`, `ClipEmpty`, `WeaponChanged`, `PickedUp { item }`, `CommandRejected { seq, why }` | the bot player, the applier |
| gameplay (HUD) | `ScoreboardChanged { rows: (actor, frags, deaths) }`, `MatchState` | `PlayerResource` (`src/server/managed/Entities/PlayerResource.cs`) |

**Snapshot** (a ring of the last 8 ticks; readers take the one for their
decide tick): per bot — origin, velocity, eye angles, health, armour,
clip/reserve per weapon, active weapon, grounded, ducking, on-ladder,
in-water; per *other* actor — origin, velocity, weapon model, animation
activity (what a client renders: reloading, crouched, firing), and only
readable while the bot holds sight (§4.3); **never health, clip or
reserve**. Per item spawn — position, kind, visible "available" flag only
when seen.

### 2.3 Memory (§5), decide/path/steer

Decide at 10 Hz per bot, at ticks where `(tick + slot) % 6 == 0`; every
threshold read from the skill parameters (§10), never a constant. Path: one
Dijkstra flow field per (objective cell, clearance class) over the nav graph,
rebuilt when the objective moves cells, owned by the requesting bot (no
shared mutable cache, §2.7); A* with funnel smoothing for the odd trip,
time-sliced ≤ 0.5 ms per bot per decide. Steer every snapshot: follow
smoothed corners, separation from other bots, strafe/range-keep per weapon
family, with everything expressed as an intent (§2.4), never a velocity.

### 2.4 Output: intents with a lifetime

```
IntentCommand { bot: ActorId, fromTick, seq, expiresTick (fromTick + 16 ≈ 240 ms),
  move  : Hold | Follow(corners[≤4], speed: Walk|Run|Sprint) | Strafe(dir) | Retreat(corners) | Jump | CrouchJump | Crouch(bool) | Pause(ticks),
  look  : Path | Point(p) | Actor(id, aimPoint: Body|Head, leadSeconds) | Glance(p, ticks) | CornerCheck(p),
  action: None | Fire(style: Tap|Burst|Spray) | AltFire | Reload | SelectWeapon(slot) | Use | (oneShotSeq),
  reason: DecisionReason { nodeId, utility, scoreboardTerm, ammoTerm, ... } }
```

The applier (binding, main thread) turns one intent into one `UserCmd` per
tick through the **motion model** (§3.4): it resolves `look: Actor(id)`
against the target's *current* snapshot only if the bot still has sight of
it this tick (else against the last intent-resolved point), runs the aim
state machine (reaction delay, flick or pursuit, drift, recoil error),
converts the corner into `ForwardMove/SideMove` in the bot's view frame,
sets `Buttons` (`IN_ATTACK/IN_ATTACK2/IN_JUMP/IN_DUCK/IN_USE/IN_RELOAD/IN_SPEED/IN_WALK`),
`WeaponSelect` (an entity index: the binding maps slot → the bot's own
inventory entry via `IEnginePlayers.GetWeaponEntity(player, slot)`, which is
the bot's own HUD knowledge), `Impulse` (flashlight only), `TickCount`,
`CommandNumber` monotonic per bot, `RandomSeed` from the bot's seeded stream.
Two channels: a latest-wins slot per bot (move/look) and a sequenced one-shot
queue (`Fire(Tap)` on a semi-auto weapon, `SelectWeapon`), each applied
exactly once. The applier trusts nothing: `ActorId` serial, newer `seq`, not
expired, player alive and not observer; refusals go back as
`CommandRejected` and a counter. `reason` is for the inspector only; the
applier ignores it.

### 2.5 Failure modes

Late pool: intents expire; bots finish their corners and hold; counted. A
step whose snapshot has left the ring is skipped and counted
(`ai.steps.skipped`), never run on a newer snapshot (§2.6). Bot died between
publish and apply: dropped, counted. GC: a brain step allocates nothing in
steady state (fact with `GC.GetAllocatedBytesForCurrentThread()` over 1,000
steps; pooled arrays sized at spawn). Lifecycle: the pool starts in
`OnLevelInitPostEntity` with the immutable nav graph, is drained, stopped and
**joined** in the `IGameLifecycle` stage that precedes entity teardown (read
`GameLifecycle.cs` before coding) before the graph is released; the gate
loads two maps and checks `/proc/<pid>/task` count returns to baseline.

### 2.6 Determinism, transports, recordings

`IAiTransport` has `PooledTransport` (§2.7) and `InlineTransport` (steps run
on the caller inside `OnPostThink`; convar `ai_sync 1`). A brain step is
`Brain.Step(botSeed, tick, snapshot[tick], events(sinceLastStep..tick)) →
intent`; the transport is only the hand-off, and the inputs of a step are
fixed by its decide tick, not by when a thread got to it. So the intent for
(bot, tick) is the same at 1 or 16 threads; only its arrival time varies.

`AiRecorder` writes the input streams (snapshots + events + seeds + skill
parameters + nav graph hash) and, per player, the **usercmd stream** (view
angles, moves, buttons, weapon select per tick: bots' and **humans'**, §3.4.4)
to `bin/<config>/mods/test/ai-recordings/<map>-<utc>.airec`; `AiReplayer`
feeds a recording to fresh brains and demands a byte-identical intent stream
(fact), inline and pooled, and **at 1, 2 and 4 worker threads** (fact). The
recording carries the skill parameters so a retune does not invalidate old
fixtures silently: a mismatch names the parameter hash.

### 2.7 The worker pool (ruling 2026-11-01: one or more threads, never the main game thread)

`AiWorkerPool(threads)`, `ai_threads` default `min(4, ProcessorCount − 1)`,
minimum 1; one pool per server process. Jobs:

| job | runs on | reads | writes |
|---|---|---|---|
| `BrainStep(bot, tick)` | a worker | the tick's snapshot (immutable), the bot's event range, the bot's own blackboard, the immutable `NavGraph`/`MapKnowledge`, the bot's own flow-field cache | the bot's blackboard, the bot's intent slot, the bot's trace ring |
| `FlowFieldBuild(bot, objective, class)` | a worker, before the step that needs it, or inside it | `NavGraph` | the bot's own cache (ring of 16) |
| `NavLoad` + `GraphDerive` | the `.nav3d` read on the main thread at `OnLevelInitPostEntity` (a file read, no engine); the graph derivation (§6.2) as a pool job, after which the pool's brains start | the file bytes, `MapKnowledge` | the immutable `NavGraph` |
| `NavValidate` (debug/test only, §6.3) | **the main thread**: it spot-checks the loaded nav against the live world with engine hull traces, which are allowed only there | engine | a report |
| `Vision`, `SnapshotBuild`, `Apply` | the main thread (engine reads and the command queue) | engine | events, snapshots, `UserCmd`s |

Rules that make the count irrelevant to the result: a bot's steps are
serialised (step N+1 cannot start before N completes); no mutable state is
shared between brains (each has its own blackboard, RNG stream seeded
`hash(matchSeed, botSlot)`, scratch arrays and flow-field cache; the shared
graph and map knowledge are immutable; SS0001 already forbids statics);
intents are merged into the slot array in **bot-slot order** by the
applier's read, never by completion order; the trace ring is per bot. HL2MP
has no team board; TF2's will be a per-step delta merged in slot order
between steps, not a shared structure.

## 3. The action boundary: a bot is a client

### 3.1 Creating a bot player: generated forwarder rows (ruling 2026-11-01: "can expose native methods to managed code to make bots work")

Stock: `NextBotCreatePlayerBot<T>` (`…valve/src/game/server/NextBot/Player/NextBotPlayer.h:36`)
calls `engine->CreateFakeClientEx(name, bReportFakeClient)` (:51); the engine
allocates the client slot, sets `FL_FAKECLIENT` (`vendor/shared/public/const.h:86`)
and calls `ClientPutInServer`, which is how the player entity is made;
`hl2mp_bot.cpp:223` and `hl2mp_bot_manager.cpp:302` use it.

**Search for a managed route (ruling 2026-11-01, "100 % managed").**
Nothing managed can allocate an engine client slot: `IEnginePlayers`
(`src/shared/interface/EngineAreas.Players.cs`) has no add; the generated
forwarders have no `FakeClient` slot (§1.2); the live gate's second-client
step and `make join` (`Makefile:502`) start a second game process; the engine
owns the slot table. A player that is not an engine client is not a player
(no slot, no scoreboard row, no kill feed, no netcode), which breaks
constraint 1 of §0.

**The native surface, ruled accepted** (hand-written C++: **none**; every
row generated by `tools/engine-forwarders` from the header, append-only at
the END of the `EngineServerVtbl` row (`tools/engine-forwarders:234`, side
`server`), `--write` from the farm, `tools/native-symbols --write` in the
same commit; never a hand-written shim, never a free `SourceSharp_*`
function). Each row, with its use on the bot path:

| row (all `FORWARD`, `EngineServerVtbl`) | vendor declaration | use |
|---|---|---|
| `CreateFakeClientEx(const char *netname, bool bReportFakeClient)` → `edict_t*` | restored from `unused_CreateFakeClientEx` at `vendor/shared/public/eiface.h:385` (same slot, layout unchanged — `tools/vendor-layout` proves it; `"CreateFakeClientEx"` added to `KEEP` in `tools/vendor-strip:59`; memory `vendor-headers-editable`) | the engine's only client-slot allocator; it sets `FL_FAKECLIENT` and calls `ClientPutInServer` itself (stock `NextBotPlayer.h:47-51`) |
| `IndexOfEdict(const edict_t*)` → `int` | already declared (`eiface.h:116`), not yet forwarded | the returned edict's entity index, so the pending-name claim (step 2 below) is by index, not by scanning `PEntityOfEntIndex` |
| `SetFakeClientConVarValue(edict_t*, const char *cvar, const char *value)` | restored from `unused_SetFakeClientConVarValue` at `eiface.h:295`; `KEEP` row | a fake client has no client convars; stock HL2MP bots set `cl_playermodel` and `name` this way (`hl2mp_bot.cpp:256-257`, `hl2mp_bot_manager.cpp:336-337`), and our server reads `cl_playermodel` through the forwarded `GetClientConVarValue` (`src/server/managed/PlayerNames.cs:24`; `cl_defaultweapon`, `SpawnWeapon.cs:40`) — without the row every bot has an empty model and no default weapon |

Re-checked and **not** needed: the put-in-server path (the engine calls
`ClientPutInServer`; `OnClientPutInServer` is already a managed table row,
`managed-interfaces.tsv:59`); kick (`ServerCommand`, forwarded);
`GetClientConVarValue` (forwarded); `LockNetworkStringTables`
(`eiface.h:208`, stubbed) — stock's `CreateFakeClient` callers do not lock
(`tf_bot_temp.cpp:337`, `NextBotPlayer.h:51`), so neither do we; if a live
run shows a string-table refusal on bot add, that row joins the list the
same way, generated, in its own commit. The headless-client alternative of
the first draft is dropped (§12.4).

After the rows exist, everything is managed:After the slot exists, everything is managed:

1. **Wrapper:** `EngineServer.CreateFakeClient(string name) → int entIndex`
   in `Server.Interop`, exposed to the binding as a new generic core service
   `IBotPlayers.Add(name) → EntityHandle` in `SourceSharp.Server` (mods reach
   the core only by injection; the core gets the service first, with its own
   fact).
2. **Join flow:** the engine's `ClientPutInServer` reaches
   `ServerModule.OnClientPutInServer` and `OnStandaloneCreate("player")` as
   for a human; `IBotPlayers` records the pending name and claims the handle
   on put-in-server (NextBot's `s_PlayerEdict` trick, in managed).
   `PlayerConnectClientArgs.IsBot` already exists. A fact proves the bot
   player carries `FL_FAKECLIENT` through the existing entity flag read, and
   that `RegisterClientMessage` tolerates it (`shell_gameclients.cpp:119`).
3. **Convars for the fake client:** `IBotPlayers.Add` sets `name`,
   `cl_playermodel` (from the profile's model choice or the HL2MP default
   list) and `cl_defaultweapon` through `SetFakeClientConVarValue` right
   after the slot exists, as stock does; a fact reads them back through
   `GetClientConVarValue`.
4. **Console:** `[ConCommand] ai_bot_add <name> [easy|medium|hard|expert |
   profile=<name> | skills=a,b,c,d,e,f,g,h,i,j]`, `ai_bot_kick <name>`,
   `ai_bot_count`, `ai_bot_roster` (§3.5). Managed console commands are
   allowed (memory `managed-console-commands-allowed`). `-hushsteam +sv_lan 1`
   launches need no Steam identity for a fake client. Kick: the engine's
   `ServerCommand("kickid …")` is already a FORWARD slot.

### 3.2 Running a bot's command: the managed loop, no native change

The engine never calls `ProcessUsercmds` for a fake client, so native's
`m_nPendingCmds` stays 0 and `RunQueuedCommands` does nothing for a bot
(`shell_standalone_player.cpp:306-330`: it checks `m_nSimulationTick`,
saves `curtime`/`frametime`, crosses to `PlayerRunCommands`, restores them).
That gate is only the native entry into a loop that is already managed
(`PlayerCommandQueue.Run`, `PlayerCommandQueue.cs:417`; its stages reach the
engine through the existing rows `PlayerCommandBegin/View/Finish`), so a
bot skips it:

1. New generic core service `IPlayerCommandRunner` in `SourceSharp.Server`
   (own fact): `RunQueued(EntityHandle player)` = what
   `PlayerCommandsBinding.PlayerRunCommands` does
   (`PlayerCommandsBinding.cs:85-93`: `ServerPlayerCommands.AdjustTickBase`
   then `PlayerCommandQueue.Run` with the module sink), with the managed
   queue's own count as `nativeCount`, and the frame's `curtime`/`frametime`
   restored afterwards as the native batch restored them (a fact reads them
   before and after through `IEngineFrame`).
2. The applier queues the bot's one `DecodedUserCmd` (`WeaponSubtype` 0,
   `RandomSeed` from the bot's stream) through `PlayerCommandQueue.Queue`
   and calls `RunQueued` from the binding's `IFrameLifecycle.OnPreThink`,
   at the frame position where native runs humans' queues (`shell_frame.cpp:72`
   is called from the frame's standalone stage; the fact in step 3 pins
   that a bot's command and a human's run in the same frame stage order).
3. Everything downstream is the human path: movement (`Movement.cs`,
   including `MaxMoveMagnitude` clamping and the usercmd move-magnitude
   convar), `PlayerUse`, `Arsenal.OnPlayerCommand` (fire/reload/select),
   impulses, step sounds. A fact proves it by running the same recorded
   human command through `RunQueued` for a bot and through
   `PlayerRunCommands` for a human and diffing `PlayerCommandQueue.Counters()`
   and the movement result.

### 3.3 What is forbidden, and how it is held

- The applier writes only `UserCmd` fields. It never calls
  `IEnginePlayers.PlaceAtSpawn`, `PlayerSnapEyeAngles`, any `ScriptedEntity`
  setter, `/player/teleport`, or `ent_fire`. Fact: an IL census over
  `SourceSharp.Ai.Hl2mp` (`src/test/common/IlCallCensus.cs` is the precedent)
  allows exactly the listed calls into `SourceSharp.Server` and the rows
  named in this plan; any other callee is red.
- **Zero hand-written native code** (ruling 2026-11-01; exposure only by
  generated rows). Fact: `git diff --stat <plan base>..HEAD --
  src/server/native src/client/native src/shared/native src/fwd/native`
  is empty for every AI commit except the generated-rows commit of §3.1,
  whose diff is exactly the files `tools/engine-forwarders --write`
  regenerates; and the `docs/native-symbols.tsv` diff over the lane is
  **exactly** the generated forwarder symbols of the three §3.1 rows
  (`CreateFakeClientEx`, `IndexOfEdict`, `SetFakeClientConVarValue`, as the
  tool names them), listed in the fact as an allow-list, and nothing else.
  Mutant: any other native symbol added (a one-line function in a server
  `.cpp`, or a fourth row) turns `tools/native-symbols`
  (`tools/checks.tsv:37`) and this fact red.
- View angles move as a human's do (ruling 2026-11-01: "as fast as a human
  would, ± by skill"; expanded into the motion model of §3.4): a flick has
  a peak angular speed, an acceleration and deceleration, and an
  overshoot/settle, not a constant-rate turn. The parameters come from the
  skill curves (§10) inside a human envelope: peak 300–900°/s, easy to
  expert, the cap enforced by the applier whatever the curve says. Facts:
  from a `look: Point` 180° away, no tick's yaw delta exceeds
  `peak × 0.015`; the time to settle within 2° rises monotonically as skill
  falls; a curve outside the envelope is clamped (mutant: remove the clamp
  → red).
- Aim is resolved on the main thread **only against targets the bot has sight
  of this tick** (§4.3); a `look: Actor` for an unseen actor resolves to the
  memory's predicted point from the intent, not the truth.

### 3.4 The human motion model (ruling 2026-11-01: "feel human, no spin bots, no always-quick snapping")

Lives in the core (`HumanMotion`, pure, per bot, seeded), driven by the
applier each tick; every number below is a skill-curve parameter (§10) and
every rule has a fact (§8.2). Speed and acceleration of the body come from
the real movement code (it is a usercmd): no extra rules there.

**3.4.1 Aim.**

- *Reaction delay* `reactionMs` (skill) before any aim starts on a new
  target or a new stimulus; no shot on the first tick of recognition.
- *Flick* (new target): a ballistic move whose duration follows Fitts' law,
  `t = a + b·log2(1 + D/W)` with `D` the angular distance and `W` the
  target's angular size, so peak speed scales with distance; an
  undershoot/overshoot of `flickOvershootPct` (skill, signed, random per
  flick), then a slower corrective phase at ≤ 1/3 of the flick's peak; the
  per-tick delta never exceeds `turnPeakDegPerSec × 0.015`, and
  `turnPeakDegPerSec` is capped by the envelope whatever the curve says.
- *Pursuit* (tracking a sighted moving target): a smooth follow with lag
  `trackingLagMs` and an error proportional to the target's angular speed
  (`pursuitGainErr`), so a fast strafer is harder to hold.
- *Drift and noise*: a low-frequency wander (`driftAmpDeg`, 0.3–1.5 Hz) and
  per-tick noise (`aimNoiseDeg`); the crosshair is never perfectly still nor
  perfectly on target.
- *Recoil*: the bot compensates a fraction `recoilControl` of the weapon's
  view punch, with lag; the rest stays.
- *Crosshair placement*: while moving with nobody in sight, the look target
  is head height toward where an enemy is expected — the next corner on the
  path, a door, the arg-max cell of a prediction (§5.3) — so a good bot
  needs small corrections, not snaps; `placementSkill` scales how well.
- *Only perceived targets*: aim resolves only against sighted actors or the
  memory's predicted point; the type makes anything else unreachable (§4.3).
- *Turns*: no 180° snap unless reacting to damage from behind (`Damaged`
  with `dirFromBot` behind), and then still a flick with the envelope; a
  sound behind produces a glance, not a snap.

**3.4.2 Shooting.**

- Style by weapon and range: `Tap` (pistol, 357, crossbow, RPG; and SMG1/AR2
  past `tapBeyond` units), `Burst` (SMG1/AR2 mid, `burstLength` rounds,
  pauses of `burstPauseMs`), `Spray` (SMG1/AR2/shotgun close). The
  cadence respects the weapon's own refire (the usercmd's buttons; the
  weapon code enforces the rest).
- Fire-control: no fire before recognition + reaction; `fireRangeGate`
  (Discipline) refuses shots beyond effective range at low skill less
  often.
- Lost target: firing stops after an overrun of `overrunShots` (1–3) at the
  last seen point, then the bot aims where it predicts the target went.
- Mistakes, scaled by skill: firing at where the target *was* while it
  moved (`staleAimPct`), missing while moving (`movingAimPenalty` added to
  the error while `ForwardMove|SideMove` ≠ 0), reloading in the open at low
  Cover.

**3.4.3 Movement.**

- The look is not locked to the path: `look: Path` holds the next corner at
  head height, with `Glance` intents at sounds and `CornerCheck` intents at
  the corners the path passes (frequency and duration by Alertness).
- Paths are smoothed: funnel over the cell corridor, then a Catmull-Rom
  pass at 0.5 cells, never a grid zigzag (fact: heading changes along a
  straight corridor are ≤ 5°).
- Strafing is not periodic: a Poisson-timed direction change with mean
  `strafeMeanMs`, jittered ±40 %, and no change while mid-air.
- Hesitation: `Pause(ticks)` intents at doors and before peeking a corner
  (`hesitationPct`, lower with skill); `Walk` speed (`IN_WALK`) when
  sneaking (an ambush, a search near a predicted enemy).
- Jumps and crouch-jumps only on nav edges typed `Jump`/`CrouchJump`
  (§6.2), never for show.

**3.4.4 Measured against humans.**

A phase step (§9 Phase 5b) records **human reference sessions**: the user
plays dm_lockdown with `ai_record start`; the recorder captures every
player's usercmd stream (a new generic core hook `IPlayerCommandObserver.
OnCommand(player, in UserCmd, tick)` called from the module sink inside
`PlayerCommandQueue.Run`, so humans and bots are recorded by the same tap),
plus sight events for the human computed by the same vision pass (so
"time to acquire" has a reference stimulus). Storage: local only,
`bin/<config>/mods/test/ai-recordings/human/<utc>.airec`, outside git;
anonymised at write time (no names, no Steam ids, no chat: a per-session
random actor id). Reference sets are committed under
`src/ai/tests/recordings/human/` only when the user says so, and are the
user's own sessions on this box.

From human and bot recordings `SourceSharp.Ai.Replay metrics` computes, per
session and per actor, the distributions of:

| metric | definition |
|---|---|
| angular velocity | yaw/pitch speed per tick, and the peak per flick (a flick = a turn > 15° completed within 400 ms) |
| time to acquire | from `SightGained` (or the first `Damaged`) to the crosshair within 2° of the target |
| overshoot ratio | flick's extremum beyond the target over the flick's length |
| flick vs tracking share | ticks in flicks / ticks in pursuit while a target is sighted |
| turn-direction reversals | yaw sign changes per minute |
| view-vs-move divergence | angle between view yaw and velocity heading while moving |
| strafe interval | time between `SideMove` sign changes |
| shots per engagement | `IN_ATTACK` presses between `SightGained` and `SightLost` |
| reaction time | `SightGained` to the first `IN_ATTACK` or first aim movement toward the target |

Gates: for each metric, the two-sample Kolmogorov–Smirnov statistic between
the bot's distribution (at the preset matching the human's apparent skill,
and at every preset for the envelope metrics) and the human reference is
below a threshold recorded in §12.3 (plan ceiling `D ≤ 0.25` until measured;
the reference must hold ≥ 500 flicks and ≥ 50 engagements, or the gate
refuses to run rather than pass). A **spin-bot detector** fact: no tick's
delta above the envelope; no sustained same-direction rotation > 360° within
1 s; snaps over 90° per minute ≤ the human ceiling (plan ceiling 6/min until
measured; §12.3). Mutants that must turn these red: remove the flick model
(aim snaps instantly); remove the reaction delay; lock the look to the path
direction.

### 3.5 Adding bots: the create-server screen, presets, custom profiles (ruling 2026-11-01)

**Where bots are added.** On the server, always: `ai_bot_add` (§3.1.3) or
the roster convar at map start. The client's screen only produces commands.

**The screen.** None exists on main (§1.2), so the smallest one that fits:
`CreateServer`, a menu screen opened by `cs_ui_open createserver`
(and a `#SS_UI_CreateServer` entry wherever the core's menu list is
composed, `src/client/ui/host/UiComposition.cs`), built on `ui.Dialog` +
`ui.FormGrid` like `MouseOptions`:

- *Map*: an `EditText` defaulting to `dm_lockdown`, validated live by
  `IFwdBaseFileSystem.FileExists("maps/<name>.bsp", "GAME")` (a list waits on
  `FindFirst` rows, §1.2).
- *Max players*: `ui.Slider` 2–16, step 1.
- *Bots*: a count `ui.Slider` 0–15 (bounded by max players − 1) and the
  **difficulty slider** with three stops `#SS_UI_Bots_Easy / Medium / Hard`
  (`ui.Slider` with `step` 1 over 0..2, labelled by stop) mapping onto the
  presets `easy / normal / hard` of §10. **Expert** is not a stop: it is
  reachable only as a custom bot (load the built-in read-only "Expert"
  profile as a starting point; coordinator's suggestion, adopted).
- *Custom bots*: a list of named profiles (`ui.VirtualList`) with Add /
  Edit (opens `BotEditor`) / Remove, each row showing its spent points and
  style name.
- *Start* (primary): issues, through `InputHost.ClientCmdUnrestricted`
  (`src/client/interface/Input/InputHost.cs:35`), `maxplayers <n>`, then
  `ai_bot_roster "<spec>"`, then `map <name>`.

**The roster.** `ai_bot_roster` is a server `[ConVar]` (string) the binding
reads in `OnLevelInitPostEntity` and clears after use; its spec is a
semicolon list of `easy|normal|hard|expert` or `name=<n>:skills=a,b,…`. On
a listen server the client sets it before `map` (same process, same
console). A dedicated server sets it in `server.cfg` and names profiles
that are on **its** disk (`profile=<name>` → `cfg/ai/bots/<name>.json` in
the server's game dir); a client cannot upload a profile to a dedicated
server in v1, but an inline `skills=` spec works everywhere, including
rcon. A spec that overspends or exceeds a cap is **refused by the server**
with a console line naming the skill, whatever the UI did (fact).

**The custom editor** (`BotEditor`, a dialog page under `CreateServer`,
`UiPageStack`): ten rows (one per skill, §10) each with the skill's name, a
`ui.Meter` of its level, `−`/`+` buttons (shift-click ±5, as the Descent
sheet), the derived parameters beside it (aim error, reaction, …, from the
curves: the same numbers the inspector shows), a points-left plate (pool −
spent), the style name (top two skills), **Undo/Redo**, **Save**, **Save
as**, **Load**, **Revisions**. The action interface
(`IBotEditorActions.Stage/Undo/Redo/Save/Load/Restore`) follows
`IDescentSheetActions`; the model is the core's `SkillProfile` +
`EditHistory` (both pure; validation is one class on both sides).

**History** (interpretation stated, as asked): (1) **undo/redo** of point
edits while editing — a ring of 64 edits in `EditHistory`, pure, with
facts; (2) a **revision list per saved profile** — every Save appends a
revision (time, allocation, note), capped at 32 (oldest dropped), and
Restore makes the chosen revision current and records a new revision
"restored #n", so nothing is lost by restoring. No other reading fitted
"history + save and load" better; a global edit log across profiles is not
planned.

**Storage.** Client side, in the mod's game dir through
`IFwdBaseFileSystem.Open(path, "wb", "MOD")` + `Write` + `Close`
(`EngineBaseFileSystem.cs:204-223`; the forwarder row has `Write`; memory
`d6-and-file-handles-rulings` lifted the no-handles rule; the client
interop acquires the same shared `EngineBaseFileSystem`):
`cfg/ai/bots/<name>.json`, one file per profile, `<name>` restricted to
`[A-Za-z0-9_-]{1,32}` (refused otherwise, never path-joined). Format,
versioned:

```json
{ "format": 1, "name": "Deadeye", "pool": 60,
  "skills": { "accuracy": 8, "alertness": 7, "aggression": 6, "cover": 6, "tenacity": 6,
              "cunning": 6, "discipline": 7, "agility": 5, "nerve": 5, "headhunter": 4 },
  "revisions": [ { "at": "2026-11-01T10:00:00Z", "note": "first", "skills": { … } } ] }
```

`format` other than 1 is refused with the file named; an unknown skill
key, a level outside 0–10, or a sum above `pool` is refused on load (the
file is a hand-editable input and may be wrong); a `pool` other than the
current one is accepted only if the sum fits the current pool (a profile
from a smaller pool loads; one from a larger pool is refused). The
built-in presets are read-only profiles the store answers without a file.
`System.Text.Json` as `src/tf2_descent/rules/TF2DescentRules.cs` uses.

## 4. The perception boundary: a bot is a client

### 4.1 Sight (main-thread vision pass, in `OnPostThink`)

For each bot and each candidate (other players; items and hazards in the
item list; projectiles), in order and stopping at the first failure:

1. **Range:** ≤ `sightRange` (skill; stock HL2MP bot 6,000 u
   `hl2mp_bot_vision.cpp:151`; NextBot default 2,000 u
   `NextBotVisionInterface.h:215`).
2. **Cone:** angle between the bot's eye forward and the target centre ≤
   `fovDeg / 2` (stock default 90° `NextBotVisionInterface.h:142`; our cap is
   the client's `default_fov` 90 so a bot never sees wider than a player's
   screen; skill may narrow it).
3. **PVS:** `IEnginePlayers.BoxInPvs(bot, target mins, maxs)` — the engine's
   own answer to "would this client be sent this entity".
4. **Trace budget:** `ITraceQueries.Line(eye, point, mask, pass: bot)` to
   sample points on the target (head, chest, pelvis, and the two shoulders
   for players; centre and top for items). The mask is stock's
   `MASK_BLOCKLOS_AND_NPCS|CONTENTS_IGNORE_NODRAW_OPAQUE`
   (`NextBotVisionInterface.cpp:704`; `MASK_BLOCKLOS` =
   `SOLID|MOVEABLE|BLOCKLOS`, `bspflags.h:118`), a new `TraceMask` value
   beside `TraceMask.BlockLos` (`src/shared/interop/TraceMasks.cs:41`).
   **What blocks sight** (ruling 2026-11-01: "consider things that block
   line of sight of other things") is everything that blocks a player's
   view: world brushes, closed doors and `func_brush`/`func_wall`, physics
   and dynamic props (`CONTENTS_MOVEABLE`), `CONTENTS_BLOCKLOS` brushes,
   and **other players' bodies**. Stock NextBot ignores actors
   (`NextBotTraceFilterIgnoreActors`, `:726`); we do not: a player standing
   in front of another hides them, as on a screen. What does not block
   sight: windows and grates (`CONTENTS_WINDOW`/`GRATE` are not in the
   mask), the target itself, the bot itself. Partial occlusion is real:
   the share of sample points that are clear is the target's
   `visibleFraction`, and recognition (5) and aim (§3.3) use it; a target
   whose clear points are only a shoulder is seen as a shoulder. A fixed budget of `ai_vision_traces_per_tick` (default 32) served
   round-robin over pairs that passed 1–3; a pair not served this tick keeps
   its last answer (so a bot's sight has at most `pairs/32` ticks of lag,
   which the recognition delay in §10 already exceeds).
5. **Recognition:** sight becomes `SightGained` only after
   `recognizeSeconds` of continuous clear traces (stock 1.0/0.5/0.3/0.2 s by
   difficulty, `hl2mp_bot_vision.cpp:128-131`); `SightLost` after
   `lostSightSeconds` of failures.

Lighting: not modelled (ruling 2026-11-01: skip lighting). The occluders
above are modelled instead.

**Moving occluders.** Doors open and close, props get knocked about, and
players move, so perception never reads a precomputed visibility answer.
The static LOS bitsets of §6.2 are world-only, used only for planning
(cover/vantage scores), and are refreshed for cells next to a door or a
moved prop when that entity's state changes (a `DoorMoved`/`PropMoved`
gameplay event).

**Negative facts (one each)** also cover the occluders: an opponent behind
a closed door, behind a stack of `prop_physics` crates, and behind another
player is never in `PerceivedView`; an opponent behind a window is; an
opponent half behind a crate has `visibleFraction` < 1 and > 0. And: an
opponent behind a wall (PVS says yes, trace
says no) is never in `PerceivedView`; an opponent outside the cone is not,
even at 10 u; one outside PVS is not even when the trace would be clear (a
`FakeShimRoot` whose PVS slot answers false); the trace budget starved for
100 ticks never produces a sight the budget did not serve; a target seen for
less than `recognizeSeconds` yields no event.

### 4.2 Hearing (taps on the main thread; no engine call)

A client hears a sound when it is in the emit's recipient filter
(`SoundRecipients`, `RecipientFilter.cs`: PAS from the sound origin, then
soundlevel attenuation) — so a bot hears exactly when its player index is in
that set. The tap sits where the filter is final: a new generic core hook
`ISoundEmitHook.OnEmitted(in EmittedSound { origin, soundLevel, soundName,
owner, recipients })` called by the server's sound emit path
(`SoundEmitterSystem`/the engine-sound binding, `ServerSounds.EmitPredicted`)
and by `PlayStepSound`'s bridge (`MovementBridge.cs:292`) and
`IWeaponSounds.Emit`. The binding classifies by sound name/script into
`SoundHeard.kind` (gunfire, reload, footstep, explosion, pain, pickup, door,
physics) and `weaponKind` (from the script name, `Weapon_SMG1.Single` →
SMG1), hands position with a positional error that scales with distance and
`hearingAccuracy` (skill), and a loudness from soundlevel and distance.
Hearing range is the engine's attenuation; `hearingMultiplier` (skill) can
only *shrink* it. This replaces Appendix B's room-graph path distance: PAS
already occludes through solid world, and the result is by construction
what a client would be sent.

Fact: a gunshot whose recipient set excludes the bot produces no event; one
that includes it produces one with the right kind; a bot's own footsteps are
never heard (owner excluded, like a predicting client).

### 4.3 Self and HUD, and the `PerceivedView` type

A bot always knows (its HUD): own health, armour, ammo per weapon, active
weapon, position, velocity, view angles, ducking, on-ladder, in-water, the
scoreboard rows (`PlayerResource`), time, and its own death/respawn state.
It knows about other actors **only through events and the sighted subset of
the snapshot**: `PerceivedView` is built per bot on the main thread and is
the only argument a brain's senses receive. Its `TryActor(id, out
ActorFacts)` answers false unless `SightGained` is held for `id` this tick,
and `ActorFacts` has **no health, clip or reserve fields** (the type cannot
express them). The raw snapshot type is `internal` to the transport and
never reaches a brain (compile-time), and a fact tries reflection-free: it
constructs a `PerceivedView` with one sighted and one unsighted actor and
asserts the unsighted one is unreadable.

### 4.4 Structural enforcement (build facts)

- `SourceSharp.Ai.csproj` has no `ProjectReference` to `SourceSharp.Abi`,
  `SourceSharp.Server`, `*.Interop`, `Debug.Api.*`: a fact over
  `GetReferencedAssemblies()` (precedent `LoadedAssemblyTests.cs:80`),
  mutation-verified by adding one in a cp-snapshot harness.
- `SourceSharp.Ai.Hl2mp` never references `IEntityDirectory`
  (`src/server/managed/IEntityDirectory.cs`), `ScriptedEntity` readers for
  other players, `Arsenal`'s per-player weapon state (clip/reserve of
  another player), or `IEngineEntities` position reads of other actors
  except inside the two classes `VisionPass` and `SnapshotBuilder` — an IL
  census fact names the two and fails any other type in the assembly that
  calls them; and `SnapshotBuilder` itself never reads another player's
  clip or reserve (a second IL fact over its methods' callees).
- The worker pool touches nothing of the engine: the core cannot (no
  reference); the pool's job entry is `Brain.Step` only, and a fact runs
  the pooled transport with a `FakeShimRoot` that refuses every slot and
  asserts no slot was called from any worker thread (thread-id tagging on
  the fake).

### 4.5 Shots and reloads, perceived as a player would (ruling 2026-11-01)

A bot may count an opponent's shots and reloads only from what reached it:

| what | counts when | event |
|---|---|---|
| a shot | the gunfire sound reached the bot (§4.2: the bot is in the `IWeaponSounds.Emit` recipient set, `ServerSounds.cs:152`), **or** the shooter is sighted at the fire tick (a client sees the muzzle flash/tracer, sent by PVS: `EffectsOverTempEnts.cs:212-213,355-358`; we use "sighted shooter + fire this tick" from the fire tap, not the temp entity) | `SoundHeard{Gunfire, weaponKind}` / `SeenFire{shooter, weaponKind}` |
| a reload | the reload sound reached the bot — only shotgun, SMG1, RPG and pistol emit one on the server (`Hl2mpWeaponBehaviours.cs:190,454,971,1046`); the generic path is the owner's client alone (`WeaponLogic.cs:766-768`, stock `basecombatweapon_shared.cpp:2042-2045`), so a 357, AR2 or crossbow reload is **never heard** — **or** the reloader is sighted while its animation state is in `PlayerAnimEvent.Reload/ReloadLoop/ReloadEnd` (`PlayerAnimEvent.cs:30-32`; the client renders that gesture, `MultiplayerAnimState.cs:1328-1343`) | `SoundHeard{Reload, weaponKind}` / `SeenReload{actor, phase}` |
| a weapon switch | the opponent is sighted and its weapon model changes | `SightGained`'s `weaponSeen` update |

Anything not perceived stays unknown: the estimate widens (§5.3.4), it
never snaps to the truth. Facts: a shot fired outside the bot's recipient
set by an unsighted shooter changes no estimate; a 357 reload out of sight
changes nothing; a sighted SMG1 reload resets the estimate; the shots the
bot heard reduce it by the number heard.

## 5. Memory and prediction

### 5.0 What a bot knows before it plays

Ruling 2026-11-01: a bot has learnt the map before playing. On spawn its
blackboard starts with the map's static knowledge, read once per map from
the entity lump and the nav graph and handed to the core as a `MapKnowledge`
record (immutable, shared by all bots):

- the nav graph itself (layout, routes, ladders, doors, hazards, §6);
- every static item spawn: `weapon_*`, `item_ammo_*`, `item_box_*`,
  `item_healthkit`, `item_battery`, `item_suitcharger`/`item_healthcharger`,
  with position, kind and value (dm_lockdown: 22 healthkits, 5 batteries,
  18 weapons, read this session from the entity lump);
- the player spawn points (`info_player_deathmatch`, 9 on dm_lockdown);
- the static cover/vantage tables (§6.2).

And the **game's weapon knowledge** (ruling 2026-11-01, like the respawn
times): per weapon kind, clip size (`WeaponType.ClipSize`, from
`scripts/<weapon>.txt` through `WeaponScriptDatabase`, `WeaponScript.cs:299`),
fire cadence (the behaviour's refire: `FastestRefire` 0.1 s pistol
`Hl2mpWeaponBehaviours.cs:1002`, the primary animation's duration :231),
reload length (the reload animation's duration, `Send(attack,
WeaponAnim.Reload)` :191, shell-by-shell for the shotgun :174-197), whether
its reload is audible (§4.5), projectile speed, effective range. The binding
builds a `WeaponKnowledge` table once per map from the same classes the
weapons run on (no second copy of the numbers; a fact diffs the table
against the scripts), and the core reads it as data.

What it does not know in advance is anything dynamic: whether an item is
there now, where anyone is, what anyone has in the clip. Those come only
through perception (§4). Items a player drops or that spawn mid-round are
not map knowledge; a bot learns them by seeing them. Fact: the
`MapKnowledge` built for dm_lockdown lists exactly the item spawns in the
entity lump (count and positions diffed against `BspFile.Read`), and
contains no entity that is not in the lump.

### 5.1 Per-bot blackboard (fixed size, no allocation after spawn)

| store | contents | bound |
|---|---|---|
| **known actors** (per opponent) | `lastSeenPos/Vel/Tick`, `lastHeardPos/Tick/kind`, `confidence ∈ [0,1]` (decays with half-life `memoryHalfLifeS`), `healthEstimate` (full on sight at spawn-time ping; minus damage the bot *saw* dealt — its own hits via `Damaged`-to-them events it caused, deaths), `weaponSeen` (from the weapon model), `lastKnownCell`, `timesEngaged`, `damageTakenFrom` (threat), `scoreboardRank` (from the HUD rows) | max players (`ss::kMaxPlayers`) |
| **ammo estimate** (per opponent; ruling 2026-11-01) | `weaponSeen`; `clipLeft` as a **range** `[min, max]` plus a small histogram over 0..clipSize: starts at `[0, clipSize]` on first sight (unknown), narrows to `[clipSize − shotsPerceived, clipSize − shotsPerceived]` after a perceived reload or weapon switch, each perceived shot shifts it down by one, and every second of *not* perceiving the opponent while it is plausibly fighting (heard gunfire of its kind elsewhere, or out of sight) widens the range by the weapon's cadence × gap (uncertainty grows with gaps); `reloadInProgress` with `reloadEndTick` = perceived start + the weapon's reload length (shotgun: per shell, open-ended until `ReloadEnd` seen or heard); `lastShotTick`, `shotsSinceReload` | max players |
| **habits** (per opponent, learned) | **route prior**: visit counts over nav cells, decayed; **camp spots**: cells where the opponent was seen stationary ≥ 3 s, with counts; **favourite pickups**: which item spawns they were seen at; **engagement range** histogram (4 bins); **retreat tendency**: ratio of `SightLost` events that followed the opponent taking damage; **aggression**: ratio of approaches vs retreats on contact; **reload style** (ruling 2026-11-01): mean `shotsSinceReload / clipSize` at perceived reloads (reloads early vs runs dry) and a histogram of what it did while reloading (retreated out of sight / held / kept closing), from the sight events in the reload window | per opponent: cells × 1 byte saturating counters; 8 camp spots; 8 pickups; fixed bins |
| **investigations** | unexplained sounds ranked by loudness × recency | 8 |
| **items** | **known before playing** (§5.0): every static item spawn's position and kind; **observed**: present/absent per spawn and the tick it was seen; respawn interval per kind seeded from the game's rules (`sv_hl2mp_item_respawn_time` 30 s, `sv_hl2mp_weapon_respawn_time` 20 s, `src/server/managed/PlacedObjects.cs:13,17`), refined from observed reappearances | item count on the map |
| **self** | current intent, seq, last feedback, stuck counter, last damage dir, own reload state (from the HUD: clip, reserve, `InReload`) | 1 |

Decay: confidence `c ← c · 2^(−Δt / halfLife)`; a counter decays by halving
every `habitHalfLifeS`. Everything is integer or float with fixed update
order, so replay is bit-exact.

### 5.2 Persistence tiers

| tier | what | lifetime |
|---|---|---|
| life | investigations, current intent, sight set, the ammo estimates (an opponent met again after the bot's death starts unknown) | reset on death |
| round/map | known actors (position/velocity decays to zero confidence; health estimate reset on their respawn, which the bot learns from the kill feed/scoreboard; ammo estimate reset to unknown on their respawn) | reset on map change |
| opponent (across lives and map loads) | habits keyed by opponent identity (user id / name), route priors keyed by `(map content hash, opponent)`, reload style | in memory for the server process only (ruling 2026-11-01); nothing is written to disk; lost when the server exits. Bounded: route priors LRU over 64 maps |

### 5.3 Predictors (core, pure; each a fact with numbers)

1. **Where is an unseen opponent now?** A particle filter would be
   overkill; v1 is a *cell-probability map* per opponent over the nav graph:
   at `SightLost` the mass sits at `lastKnownCell` with velocity bias along
   `lastVel`; each decide tick the mass diffuses along nav edges with the
   opponent's movement speed, weighted by the route prior (habits) and by
   "where a player would go": towards the nearest health/weapon pickup when
   `healthEstimate` is low, towards the bot's last known position when the
   opponent was aggressive, away from it when retreating. Hearing collapses
   the mass to the heard cell's neighbourhood; a clear sight of an empty cell
   zeroes it (negative evidence: the bot *looked* and nobody was there).
   Output: the arg-max cell and the entropy (how sure).
2. **What will they do next?** A small intent classifier over habits and
   the last 2 s of observation: `{Approach, Retreat, Camp, GoForPickup(kind),
   Flank, Reloading}` with scores from the habit ratios, health estimate,
   distance and the ammo estimate; the top intent and its score feed the
   tree.
3. **Aim lead:** `leadSeconds × lastSeenVel` for projectile weapons (RPG,
   crossbow, grenade), clamped by `leadQuality` (skill); hitscan uses none.
4. **Ammo and reload window** (ruling 2026-11-01): from the ammo estimate,
   `P(empty)` = the histogram mass at 0, `P(reloading now)` =
   `reloadInProgress && tick < reloadEndTick` (or, when a reload was not
   perceived but the opponent is probably empty and has gone quiet for ≥
   the weapon's reload length, a prior from its reload style), and the
   **window**: `[now, reloadEndTick]`. The push test: nav distance to the
   opponent's predicted cell ÷ the bot's run speed ≤ `reloadEndTick − now`
   (minus a margin by skill).

Facts with numbers (recorded synthetic streams): a predictor fed an opponent
who always walks corridor A then B puts ≥ 70 % of mass on B's cells 2 s after
losing sight at A's end (vs ≤ 35 % with a flat prior: before ≠ after); a
heard shot moves the arg-max within one decide; negative evidence from an
empty seen cell drops that cell to 0; retreat tendency 0.8 vs 0.2 flips
`Retreat` over `Approach` at equal health; lead on a 300 u/s crossing target
at 100 u/s bolt speed lands within 8 u of the analytic intercept. Ammo: after
hearing 17 of an SMG1's 45 rounds the range's max is 28; a 4 s gap at SMG1
cadence widens it back toward unknown; a sighted reload sets it to full at
`reloadEndTick`; `P(empty)` after 45 heard shots and no reload is ≥ 0.9.
**Accuracy against a baseline** (over scenario recordings whose truth the
test reads and the brain cannot): the estimate's range contains the true
clip ≥ 80 % of ticks while the opponent is within hearing, and its width is
≤ half the clip ≥ 60 % of those ticks; the baseline that ignores ammo (range
always `[0, clip]`) is correct by construction but never narrower, so the
second number is the evidence; the push decision fires inside the true
reload window ≥ 70 % of the times it fires, vs ≤ 35 % for a baseline that
pushes on health alone.

### 5.4 How predictions reach decisions

The tree's conditions and utilities read only the blackboard and predictor
outputs: `Hunt` goes to the arg-max cell when entropy is low and the bot is
healthy; `Ambush` picks a cell on the opponent's most-used route with a
vantage over it (cover score: cells from which the predicted path is visible
by nav-graph line-of-sight precomputed at build, see §6.3) when `Cunning` is
high; `Search` sweeps the highest-mass cells when entropy is high; `Deny`
goes to the pickup the opponent is predicted to want (`GoForPickup`) when
reachable sooner; `Retreat` when `healthEstimate(self)` is below
`retreatHealthPct` and the opponent is predicted `Approach`; aim leads by
§5.3.3.

**Push / hold / retreat on ammo** (ruling 2026-11-01). Engaged with an
opponent: `Push` when `P(empty) + P(reloading) ≥ pushTrust` and the push
test of §5.3.4 passes (the bot arrives before the reload ends, by nav
distance and its own speed), weighted up by the opponent's reload style
("runs dry" and "holds while reloading" favour a push; "retreats to cover"
favours a flank instead); `Hold` (keep cover, keep range) when the opponent
probably has a full clip and the bot is low or worse-armed; `Retreat` when
both are true and the opponent is predicted `Approach`. **Discipline** is
the skill that drives `pushTrust` and the margin: how much the bot trusts
and uses the estimate (at 0 it ignores ammo; at 10 it pushes on a 0.6
probability with a 0.2 s margin). The bot's own reloads: reload in cover
when the clip is below `reloadBelowPct` (Cover) and nobody is sighted;
never mid-fight unless empty, or unless the opponent is predicted reloading
too. The decision's `reason` carries `ammoTerm`, the window, and the
nav-time comparison for the inspector.

**Headhunter** (ruling 2026-11-01; the trait, §10): target priority gains
`headhunter/10 × rankBonus(opponent)` where `rankBonus` is from the
scoreboard rows only (HUD knowledge; rank 1 = 1.0, falling linearly to 0 at
the bottom); with nobody in sight, `Hunt` targets the **leader's** predicted
cell (its own §5.3.1 map) rather than the nearest or most likely opponent;
the path-length cap and the "easier target nearer" discount are both
weighted down by the trait, so a Headhunter walks further and passes up
easy kills. The `reason` carries `scoreboardTerm`.

## 6. Navigation from the map tools' `.nav3d` (ruling 2026-11-01)

The nav source is the `.nav3d` file `../source-sdk-map-tools` writes when a
map is compiled (§1.1, "Map tools"). The HL2MP binding loads it, managed
only, through map-tools' own reader, derives the AI's graph for the player
hull from the clearance grid, and caches the derived graph by the bsp and
`.nav3d` hashes. A missing or mismatched file is a refusal naming the map
and the expected file, never a fallback (§6.3). The first draft's runtime
hull-trace sampler is **dropped** as a nav source; a small trace-based
validator stays as evidence only (§6.3).

**No stock nav-mesh logic** (ruling 2026-11-01: "map tools does write
nav3d, which must be used for nav mesh; do not use stock's nav mesh
logic"). The `.nav3d` is the only nav input, and everything built over it
is ours: the player-hull graph derivation, A*, the flow fields, path
smoothing, cover and vantage. Nothing is ported, translated or modelled on
stock nav code: no `.nav` files or loader, no `CNavMesh`/`CNavArea`/
`TheNavMesh` (`src/game/server/nav_*.cpp`), no `nav_generate`, no NextBot
path code (`NextBotPath*`, `PathFollower`, `ChasePath`), and no stock
nav-area concepts (hiding spots, encounter spots, sniper spots, place
names), where these come from the `.nav3d`'s own data or our derivation
instead. Today `.nav3d` is written by `ssmap link` (`LevelNavFromPack.cs:114`
in map-tools calls `Nav3dWriter.Write`), so the `ss_ai_*` maps built through
`link` (§6.6) have nav from the start; M1 extends the same writer to
`ssmap vbsp`/`all` for the recompiled dm_lockdown (§6.4). A fact holds the
rule: no file under `src/ai/` has a `docs/valve-header-new.tsv` row citing a
stock nav or NextBot path source (`nav_*.cpp`, `nav_*.h`, `NextBotPath*`),
and `SourceSharp.Ai*`'s IL names no type with `NavMesh`/`NavArea` in its
name (mutant: add one → red).

### 6.1 Getting the reader into this repo

`Nav3dReader` lives in `src/SourceSharp.MapFormats/Nav/` of the map-tools
repo, an assembly with no dependencies (format §14: "the mod references
`SourceSharp.MapFormats`"). Nothing names how (§1.2). The house pattern for
a sibling repo's assembly is a **package from a local feed with a pinned
version**: `TF2.Descent.Rules.csproj` restores `SourceSharp.Host.Contracts`
`$(HostContractsVersion)` from `$(HOST_ROOT)/bin/nupkg` (`Makefile:296`).
So: a `MapFormatsVersion` property in `Directory.Build.props`, a
`PackageReference` in `SourceSharp.Ai.Hl2mp.csproj` and in
`SourceSharp.Ai.Tests.csproj`, restored from `$(MAPTOOLS_DIR)/bin/nupkg`
(the Makefile already knows `MAPTOOLS_DIR`, :526-540), and a map-tools item
to pack it (§6.4 item M0). A project reference across repos is rejected:
CI and a fresh worktree have no sibling checkout, and a version pin is what
makes "the nav I built matches the reader I run" checkable (the package
version is written into the derived graph's cache header). Memory
`host-sdk-rev-every-drop` applies: each map-tools drop gets a new version.

### 6.2 What the file gives, what the binding derives

**Given by the file** (format `docs/nav3d-format.md`, version 2, §1, §9,
§10, §11): free-space leaves per voxel column (16 u voxels in 256 u cells)
with a clearance record per clip class answering any agent box; per leaf
the floor height and whether it is walkable, water and ladder flags, a cost
multiplier; `stepHeight` and `jumpHeight` (56 = a crouch jump) in the
header; **jump links** between walkable floors up to 56 u apart and 100 u
away, bidirectional (up by jumping, down by dropping; format §9.2);
dynamic obstacles (`func_door`, `func_door_rotating`, movers, `func_brush`
toggles, breakables, `prop_physics*`, `prop_dynamic*`) as open space tagged
with the entity's name, with `ObstacleLeaves(o)` naming the leaves a change
affects (format §10); points of interest; the level id. Neighbours and
components are derived at `Open` by the reader itself (format §8).

**Derived by the binding at load** (`NavGraphFromNav3d`, a pool job, pure
over the reader's answers; cached):

| derived | how |
|---|---|
| the two clearance classes | `Standable(leaf, z, 32, 72, Player, shut)` for `Stand`, `(32, 36)` for `Crouch` (the HL2MP hulls, `MoveConstantDefaults.Stock`); a leaf standable only crouched is a `Crouch`-only cell |
| one AI cell per standable leaf column | the AI's 2D graph node; its floor point is the leaf's floor |
| `Walk` edges | reader `Neighbours` with `StepUp ≤ stepHeight` (the file's 18 matches `sv_stepsize`; a fact diffs them) |
| `Jump` vs `CrouchJump` edges | the file's jump links split by rise against HL2MP's numbers: rise ≤ `StockJumpHeight` 21 + step 18 → `Jump`, else (≤ 56) → `CrouchJump`; the thresholds are pinned by a fact against the managed movement code — the file does not know which agent clears which arc (format §9.2: "its own clearance's answer at run time") |
| `Drop` edges | the down direction of a jump link, and any down-neighbour whose fall is within the safe-fall height (from the movement constants, quoted in the fact) |
| `Ladder` edges | leaves with the ladder flag, joined bottom to top |
| `Door` edges and the obstacle table | each `DYNO` door becomes a passable edge with cost + wait and a `blocking[o]` flag the binding keeps current from the door entity's state (a `DoorMoved` event; the entity found by the obstacle's name, format §10); props and breakables likewise (`PropMoved`) |
| water, hazards | the water flag → cost 2 (the file's) ; `trigger_hurt` volumes from the entity lump → forbidden (the file does not mark them) |
| static LOS bitsets for cover/vantage (`cover(cell, from)`, `vantage(cell, path)`, read by §5.4) | a voxel ray through the file's solid/free columns between cell pairs within 1,024 u — **no engine traces**: the grid is the world at 16 u, good enough for planning (perception never reads it, §4.1) |
| `MapKnowledge` items and spawns | from the entity lump as before (§5.0); the file's points of interest, where the map's author set them, add hints |

Cache: `bin/<config>/mods/test/ai-nav/<map>-<sha256(bsp)[:16]>-<sha256(nav3d)[:16]>-v<MapFormatsVersion>.navgraph`,
rebuilt on any hash or version mismatch; a fact mutates one byte of the
`.nav3d` and sees a rebuild. The derivation's cost is measured in Phase 2
(§12.3); the first draft's sampler numbers are gone.

### 6.3 Refusal, binding and the validator

Loading: `maps/<map>.nav3d` beside the bsp, read through `IFwdBaseFileSystem`
(`ReadAll`, pathId `GAME`). **Missing → refusal**: `[SourceSharp/Ai] no
navigation for dm_lockdown: expected maps/dm_lockdown.nav3d` on the console,
no bot is added (`ai_bot_add` answers the same line), and the live gate's
bots step fails. **Mismatched → refusal**: the file's level id must match
the bsp's worldspawn `ss_level_id` (reader `MatchesMap`, format §12); for a
stock bsp that carries no such key the file must instead name the bsp's
checksum (`BspMapChecksum`, the binding map-tools already uses for `.map2d`;
§6.4 item M2 adds it to the nav header), and a file naming another bsp is
refused by name. No silent fallback of any kind (CLAUDE.md: a missing input
is a failure).

**Validator** (kept, small, evidence only): `ai_nav_validate [n]` (debug
command) and a slow-tier fact run through the live scenario harness: for `n`
(default 200) seeded random standable cells, an engine `Hull` trace of the
standing box at the cell's floor must be clear, and for `n` random solid
voxels adjacent to a cell a trace must hit; both from the main thread. It
reports mismatches as a count and a list, and the fact fails above 0.5 %.
Its purpose is to catch a `.nav3d` built from geometry that drifted from the
shipped bsp (a stale build, a wrong tool version) — §6.4 — not to build anything.

### 6.4 Map-tools items (approved 2026-11-01: "can update map tools repo to make nav3d from bsp files"; still a plan, no writes there yet)

Verified read-only at map-tools tip `83bbf57`. One real item (M1) plus two
thin ones it needs (M0, M2); the earlier decompile route is gone (§12.1).

| id | item | what exists | what is new (files in `../source-sdk-map-tools`) |
|---|---|---|---|
| M0 | **Package `SourceSharp.MapFormats`** into `bin/nupkg` with a version | the project, no dependencies | a pack target (the host's pattern) and the version bump rule; `src/SourceSharp.MapFormats/SourceSharp.MapFormats.csproj` (package metadata), a `Makefile`/script target |
| M1 | **`ssmap nav3d <map.bsp> [-out <map.nav3d>] [-game <dir>] [-nav-codec <codec>]`: build the navigation straight from a compiled bsp** (named like `ssmap map2d <map.bsp>`, README:496, which builds the other per-map file from a bsp) | `NavGeometry.FromBsp(BspData, modelBounds)` (`src/SourceSharp.MapTools/Nav/NavGeometry.cs:114-117`: brushes via `NavBrush.FromBspIndexed`, `src/SourceSharp.MapFormats/Nav/NavBrush.cs:289`; obstacles, `info_ladder`/`func_useableladder` ladders and `prop_*` hulls from the entity lump, a prop's hull from `modelBounds`); `NavClearanceBuilder.Build(geometry, region, settings)` (`NavClearanceBuilder.cs:67,77`); `Nav3dWriter.Write`; `LevelNavFromPack.Plan` (`LevelNavFromPack.cs`) is the rooms-only plan type; `Assets/` reads studio models and `.phy` (README:141) for `modelBounds` | `src/SourceSharp.MapTools/Nav/LevelNavFromBsp.cs`: a plan type for a **non-rooms map** — a cell grid (256 u cells) laid over worldspawn's model bounds, every cell "placed", one `NavClearanceBuilder` region per cell (or one region and a split), `NavSettings` from the command line with the link's defaults (`stepHeight` 18, `jumpHeight` 56, 100 u reach, voxel 16), `modelBounds` answered from the game dir's models through `Assets/` so **static props' hulls** are solid, **displacements** read from the bsp's disp lumps into `NavGeometry` (check how `FromBsp` treats them today and add them if it does not — the only open point inside M1), **playerclip** brushes as the player clip class (`CONTENTS_PLAYERCLIP` from the brush lump, format §5.5); `src/SourceSharp.MapCompile/Nav3dCommand.cs` (the command, `Program.cs` table entry, README section); **tests** in `src/SourceSharp.Tests/MapCompile/`: a small bsp compiled in the test from a tiny VMF (a room with a step, a ledge, a ladder, a door, a prop, a clip brush) → `ssmap nav3d` → leaves, floors, jump links, obstacles and ladders equal the expected set; obstacle names and ladder count equal the entity lump's; and the **determinism fact: the same bsp twice → byte-identical `.nav3d`** (sha256), as map-tools' own fact |
| M1' | **`ssmap all` (and `vbsp`) write `.nav3d` through the same path** | `MapCompiler.cs` has no nav output today | after vrad, `MapCompiler` calls `LevelNavFromBsp` on the bsp it just wrote; `-no-nav` / `-require-nav` as `link` has (README:486); a thin call, no second builder |
| M2 | **Bind a `.nav3d` to a bsp by checksum** (a stock bsp has no worldspawn `ss_level_id`, format §12) | `BspMapChecksum` (`Bsp/`, README:141) is the checksum `.map2d` binds to | a `bspChecksum` header field (format version bump, `docs/nav3d-format.md` §3.2/§12/§16, `Nav3dFormat.cs`, `Nav3dWriter.cs`, `Nav3dReader.cs`: `MatchesBsp(checksum)` beside `MatchesMap`), written by M1 for every bsp-built nav (and by `link`, harmlessly); the format's `levelId` stays for linked maps |
| M3 | Player-hull movement links | jump links up to 56 u / 100 u are in the file (format §9.2); which agent clears them is run-time | nothing in map-tools: the binding splits links by HL2MP's numbers (§6.2) |

M1 is the only sizeable item; the user's go-ahead covers it. It also
serves any other stock bsp later with one command.

#### 6.4.1 `make` builds dm_lockdown's `.nav3d` per tree (the commit-policy choice)

**Per tree, not committed**, the way `ss_sandbox.bsp` is built: `make map`
gains a `nav` arm (`Makefile:640-652`) and a `NAV_MAPS := dm_lockdown` list
beside `GENERATED_MAPS` (:579) and the `LINKED_MAPS` of §6.6; `make debug`
runs `ssmap nav3d game/mod_test/maps/dm_lockdown.bsp -game game/mod_test`
through the pinned ssmap (`$(SSMAP)`, `Makefile:539-540`) once per tree,
writing `game/mod_test/maps/dm_lockdown.nav3d` (gitignored, like the
compiled sandbox, never copied between trees — CLAUDE.md) with a stamp
`dm_lockdown.nav3d.stamp` (bsp sha256, map-tools commit, `MapFormatsVersion`)
so a rebuild is a no-op until the bsp or the tools change. Why not commit
it: the input (the stock bsp) is already committed and the output is a pure
function of it and the pinned tools, so a committed copy would only drift;
and the clean-tree daily gate already runs `make setup` + `make debug`,
which already compiles the generated maps, so one nav build per tree is the
same shape of cost (measured in Phase 2, §12.3; if it exceeds 2 minutes on
dm_lockdown the choice is revisited to a committed file with provenance,
and the plan says so rather than hiding the number). **Pinning:** map-tools
gets a lock like `valve-sdk.lock` — `maptools.lock` (a commit sha) that
`make setup` checks `MAPTOOLS_DIR` against and refuses on a mismatch, so
"the nav I built matches the reader I run" is a checked fact, not a hope;
`MapFormatsVersion` (§6.1) is bumped in the same commit as the lock.
**Refusal stays:** a missing or stale `.nav3d` (checksum mismatch, M2) is
refused at level load naming the map and the expected file (§6.3); `make`
is what produces it, `source#` never builds one.

**Nothing here blocks the bot work**: the core and the replay harness run
on hand-built graphs (Phase 1–2), and the `ss_ai_*` maps (§6.6) come out of
the rooms pipeline, which writes `.nav3d` today, so Phases 3–5 have real
nav before M1/M2 land. `ss_sandbox` gets a `.nav3d` once M1' lands.

### 6.5 Target maps and their nav, in order

| map | source | nav today | nav route | when |
|---|---|---|---|---|
| `ss_ai_*` (§6.6) | room library VMF + `level.yaml`, committed | — | `ssmap link` (writes `.nav3d` today) | Phase 2 onwards |
| `dm_lockdown` | stock bsp, no VMF | none | `ssmap nav3d` (M1) run by `make` per tree, bound to the bsp's checksum (M2) | Phase 2 (as soon as M1 lands), live bot work on it from Phase 3 |
| `ss_sandbox` | generated VMF, `ssmap all` | none | M1' (`ssmap all` writes it) | after M1'; not needed by the bots |

### 6.6 Purpose-built test maps (ruling 2026-11-01: "can also make new maps to test bot features")

Made through the **rooms pipeline** (`ssmap link`, the one path that writes
`.nav3d` today): each map is a small room library VMF plus a `level.yaml`
under `game/mod_test/maps/src/ai/` (committed sources; the authoring plan's
`src/mapgen` generator can emit the library VMF the way it emits
`ss_sandbox.vmf`, so brushes are code, not hand-drawn — memory
`map-authoring-plan`), compiled **per tree** like `ss_sandbox` (never
copied between trees, CLAUDE.md) by `make map MAP=ss_ai_sight`, which gains
a `link` arm beside the `all` arm (`Makefile:640-652`) and a `LINKED_MAPS`
list; `make debug` builds them the way `GENERATED_MAPS` are built. Our own
fixtures, so `ss_` names (memory `stock-classnames-ruling`); the entities
inside use stock classnames.

| map | exercises | scripted scenario (`tools/ai-scenario <map>`: headless game, `ai_sync 1`, fixed seed, bots + a scripted "human" driven by the same command path) | gate that can fail |
|---|---|---|---|
| `ss_ai_sight` | walls, a `func_door` that opens and closes, a `prop_physics` crate stack, a window, a grate, a second player as an occluder, half cover | the target walks a fixed route past each occluder; the bot stands at a fixed post | the sight event stream equals the expected per-occluder answers (seen / not seen / `visibleFraction` band); any deviation is red |
| `ss_ai_hearing` | rooms at distances behind walls | footsteps, each weapon's fire and both-sides reloads at scripted distances | heard / not heard per sound equals the recipient-filter truth; a 357 reload is never heard |
| `ss_ai_nav` | steps, a ledge at jump reach, one at crouch-jump reach, a drop, a `func_useableladder`, a crouch-only crawlspace, a door, water, a `trigger_hurt`; `item_healthkit`/`weapon_*` spawns with respawns (items and map knowledge, merged here) | the bot is sent to each pickup in turn | every objective reached within its time budget, the edge types used match the expected list, no `Stuck`, the items table shows the respawns at the expected ticks |
| `ss_ai_duel` | a small arena with two covers | the scripted opponent fires N rounds then reloads at fixed times | the push fires inside the true reload window and never outside; hold/retreat verdicts at scripted health/arming |
| `ss_ai_routes` | three routes between two bases, a camping balcony, a flank | the scripted opponent repeats a route, then switches; a second scripted opponent leads the scoreboard | route-prior mass and search cells match the expected sequence; a Headhunter passes the near opponent for the leader |
| `ss_ai_range` | a firing range: targets appearing at varied angles and distances | the targets' appearance schedule | the human-feel metrics of §3.4.4 computed on this map against the human reference recorded **on this map** (the user plays the same schedule once), so flick/tracking compare like with like |

Merged from the suggested set: "items and map knowledge" into `ss_ai_nav`
(the pickups are on the way to the nav objectives, and respawns need only
time); nothing dropped. Six maps.

Wiring: `tools/ai-scenario` runs one map's scenario and writes its
recording; the **scenario facts** of §8.2 replay those recordings (fast,
deterministic, in the `ai` leg); the live runs happen in the lane whenever
the feature changes, and three of them (`ss_ai_sight`, `ss_ai_nav`,
`ss_ai_duel`) join the **daily live gate** as steps after `bots`. A map
whose compile fails fails `make debug`, so a scenario never runs on a stale
map.

## 7. Visualisations and the per-bot inspector

### 7.1 Views

| view | content | live | offline |
|---|---|---|---|
| **Nav graph** | the derived cells coloured by clearance, edges by type (walk/jump/crouch-jump/drop/ladder/door), obstacle state, item cells; a selected objective's flow-field arrows; the underlying `.nav3d` leaves as a faint layer | web UI map tab layer from `GET /ai/nav?map=` (and `/ai/flow?cell=&class=`), drawn by `renderer.ts` beside the wireframe | `tools/ai-replay nav <map>` PNG via Skia; map-tools' own `ssmap nav <map.nav3d> --obj` (free leaves or floors as OBJ, `NavCommand.cs:32`) is the source-of-truth view of the file and is reused as is, not duplicated |
| **Perception** | each bot's eye cone (fov, range), lines to sighted actors (green) and to candidates refused at each stage (grey: range, yellow: cone, orange: PVS, red: trace), last heard positions as rings, perceived shots/reloads as ticks on a timeline | `GET /ai/bot/<id>/perception` polled per tick; in-game `IFwdDebugOverlay` lines/text behind `ai_debug_draw 1` | scrubber frame |
| **Belief map** | per opponent the cell-probability heatmap, arg-max marker, entropy, habit overlays (camp spots, route-prior heat) | `GET /ai/bot/<id>/memory` | scrubber frame; `ai-replay heat` writes PNG per N ticks |
| **Behaviour-tree trace** | the tree with the last decide's visited nodes, condition values, utilities, the chosen intent and its seq/expiry, the `reason` terms; `CommandRejected` reasons | `GET /ai/bot/<id>/trace` (ring of the last 64 decides) | scrubber panel |
| **Aim trace** (§3.4) | view-angle path (yaw/pitch over time) against the target's angular position, flicks and pursuit segments coloured, reaction delay marked, the envelope drawn as a band | `GET /ai/bot/<id>/aim` (ring of the last 600 ticks) | scrubber panel; `ai-replay aim` PNG |
| **Human-vs-bot distributions** (§3.4.4) | one histogram per metric, human reference over bot, with the KS statistic | — | `ai-replay metrics <human.airec> <bot.airec>` PNG set + JSON |
| **Record/replay scrubber** | step a recording tick by tick (or decide by decide), all of the above at that tick, intent diff live-vs-replay highlighted | `ai_record start/stop` convars; live scrubbing = `ai_sync 1` + `ai_step` | the whole point: the web UI loads a `.airec` through `POST /ai/replay/load` served by the dev API (file read, no game needed beyond the API process) |

### 7.2 The per-bot inspector (ruling 2026-11-01): web UI only

One inspector per bot with three parts:

- **Memory:** known actors with confidence, last seen/heard (where, when),
  health estimate, the **ammo estimate** (range, histogram sparkline,
  reload-in-progress and its end tick, as uncertainty: a wide bar for a
  wide range), the learned habits (route-prior heat, camp spots, favourite
  pickups, engagement-range histogram, retreat/aggression, reload style),
  the items table (known / seen present / seen absent / expected respawn),
  investigations, and the current predictions per opponent (arg-max cell +
  entropy, next intent + score, `P(empty)`, reload window).
- **Skill allocation:** the ten skills (§10) with points spent on each
  (`ui.Meter` + number), the pool and the remainder, the preset or profile
  name, and the derived parameters (aim error, reaction, tracking lag,
  turn peak, hearing, memory half-life, push trust, …).
- **Doing and planning:** the active behaviour-tree path with each chosen
  node's utility and why (the `reason` terms: `ammoTerm`, `scoreboardTerm`,
  cover, distance), the current intent and its expiry, the goal (cell,
  actor or item), the path corners and the flow-field target, the
  push/hold/retreat verdict and its window arithmetic, and a scrolling list
  of the last 64 decisions.

**Where it lives: the web UI only** (ruling 2026-11-01: "bot inspector
needs no in game ui"). The **web UI** (`src/webui`, an `Ai` tab with a bot
list and the three parts, over `/ai/bot/<id>` which merges
`memory|skills|trace|aim`) is the inspector: it needs no second window on
the game machine, it draws heatmaps and the aim trace, and it works
**offline** from a recording (the scrubber of §7.1 shows the same three
parts at any tick). It reads the `BotInspectRecord` the core builds per
decide, served by the server binding's dev-API routes, so nothing about a
bot's mind is networked to clients: no carrier entity, no user message, no
client-side reader, no in-game screen.

Facts (§8.2): `/ai/bot/<id>` returns the record's three parts with the
values a scripted brain step produced (live through the dev API, offline
through `POST /ai/replay/load`, the same values at the same tick); the ammo
uncertainty bar width equals the range; the decision list shows the
`scoreboardTerm` and `ammoTerm` of the chosen node; web UI tests for the
`Ai` tab over a recorded sample (a full memory, an empty one) in the web
UI's own test harness.

### 7.3 The web UI and replay tool

Plumbing (all new): a generic core hook `IDebugRouteProvider` in
`Debug.Api.Server.Interfaces` (data-only: `IReadOnlyList<DebugRoute
{ method, path, description }>` + a `Handle(request) → HttpReply`
implementation interface in `Debug.Api.Server`) that `DebugApiServer`
consults after its own switch; the binding registers one. `tools/debug-api`
gains `ai-bots`, `ai-bot <id> [memory|skills|trace|aim|perception]`,
`ai-nav`. The web UI (`src/webui`, Vitest tests beside `__tests__`) gets an
`Ai` tab (bot list, the inspector's three parts, the scrubber) and nav,
cone, heatmap and aim layers on the map tab, with golden JSON facts as
`DebugApiGolden*` do. `SourceSharp.Ai.Replay` (SkiaSharp, like `UiPreview`)
renders frames, computes the §3.4.4 metrics and is the oracle for golden
PNGs in the `ai` leg.

## 8. Tests

### 8.1 Leg and shape

`src/ai/tests/SourceSharp.Ai.Tests.csproj`, output `bin/$(Config)/ai-tests/`,
leg `'ai:tools/leg-floor ai && $(AI_LEG)'` in `MANAGED_TEST_LEGS`
(`Makefile:867`), `tools/leg-floor` derivation added (count of `[Fact]`s in
`src/ai/tests`), `SourceSharpTestGated` in `Directory.Build.props`. Slow
facts (≥ 2 s) go into `tools/test-tiers.tsv` via `tools/test-tiers measure
--write`. xUnit `[Fact]` only, one behaviour per fact; never the
`Run(Action<string,bool>)` shape. UI screens take the client UI tests'
loop too: `UiSourceUseTests`, `UiAllocationTests`, `UiNarrowTests`, lint,
and goldens (`UI_GOLDEN_WRITE=1 make managed-test
FILTER='FullyQualifiedName~UiGoldenTests'`, `docs/ui/authoring.md` §2).

### 8.2 Kinds (every one can fail; the negative arm is named)

- **Pure core facts:** memory rules (decay numbers, forget, negative evidence), predictors (§5.3 numbers, ammo included), flow fields (§6.3), BT runtime (a leaf runs once per decide; a failed child aborts a sequence; utilities pick the max; ties are seed-stable), skill curves and the pool (§10), steering (separation keeps ≥ 40 u between bots on a flat 10×10 graph; path heading changes ≤ 5° along a straight corridor).
- **Boundary facts:** §4.1 negatives, §4.2 recipient facts, §4.3 `PerceivedView` (no health/clip fields: a fact over the type's members), §4.4 reference/IL censuses with cp-snapshot mutants, §4.5 unperceived shots/reloads change nothing, §3.3 applier allow-list, envelope and the zero-native diff fact with its added-symbol mutant.
- **Ammo facts (§4.5, §5.3.4):** shots heard reduce the estimate by the number heard; shots not perceived do not; a perceived reload resets it; the push fires inside the reload window and not outside it; the bot never reads the truth (the `SnapshotBuilder` IL fact and the `ActorFacts` type fact); the accuracy numbers against the ignore-ammo baseline.
- **Headhunter facts (§5.4):** with the trait at 10 a bot picks the scoreboard leader over a closer low scorer; at 0 it picks the closer one; with nobody in sight the Hunt target is the leader's arg-max cell (a fixture where the nearest opponent's cell differs); the ranking reads only the scoreboard snapshot (the §4 no-truth IL fact covers the ranking code); the inspector record carries the `scoreboardTerm` of the chosen node.
- **Human-motion facts (§3.4):** no shot on the recognition tick; reaction delay ≥ `reactionMs` before aim moves; a flick's duration grows with distance (Fitts); overshoot occurs with the configured sign distribution; pursuit error grows with target angular speed; the look leaves the path direction at a corner (`CornerCheck`); strafe intervals are not periodic (autocorrelation at the mean interval < 0.3); fire stops within `overrunShots` of `SightLost`; a 180° reaction to damage from behind is a flick inside the envelope. Mutants: instant snap, no delay, path-locked look → red. **The spin-bot detector** fact over every scenario recording and the live recording.
- **Human-vs-bot metrics:** over the committed human reference set (if absent the fact **fails** — a missing reference is not a pass), every §3.4.4 metric's KS statistic is below the §12.3 threshold for the matched preset; the instant-snap mutant turns the angular-velocity gate red.
- **Thread-count determinism (§2.6):** one recording replayed at `ai_threads` 1, 2 and 4 gives byte-identical intents and traces; a mutant that lets two brains share a scratch array turns it red (the 4-thread arm diverges).
- **Hand-off facts:** events in = events processed across an induced 1 s stall (pooled transport with a sleeping brain); main-thread `OnPostThink` time unchanged during the stall (measured, not asserted zero); intents expire and bots hold; a step whose snapshot left the ring is skipped and counted, never run on a newer one; one-shot applied exactly once under slot re-application; a command for a bot killed that tick is dropped and counted; the pool is joined at shutdown (thread count before = after).
- **Scenario facts on recorded streams:** fixtures in `src/ai/tests/recordings/*.airec` (small, synthetic, committed) and the `ss_ai_*` scenario recordings of §6.6 (written by `tools/ai-scenario`, committed per map with the map source's hash in the header, so a changed map fails the fact rather than replaying stale geometry) — "bot hears a shot behind it, glances and investigates", "bot loses sight at a corner and searches the predicted side", "bot at 20 HP with an approaching opponent retreats to the nearest health", "bot hears an SMG1 run dry and pushes", "Headhunter passes a low scorer to hunt the leader". Each asserts the intent stream's shape (which move/look/action within N decides), not pixel positions.
- **Determinism:** a recorded live session (Phase 6 produces the first; kept under `recordings/live/`) replays byte-identical, inline and pooled; a mutant that reads `DateTime.UtcNow` in the brain turns it red.
- **Prediction accuracy with numbers:** over the committed scenario recordings, the position predictor's arg-max is within 2 cells of the truth ≥ 60 % of decides while unseen for ≤ 3 s (truth is in the recording's snapshot; the test reads it, the brain cannot); the baseline flat prior scores ≤ 40 % on the same recordings (so the number is evidence, not a tautology). Ammo: §5.3's numbers.
- **Nav facts (§6):** the derivation over a hand-written `.nav3d` (written with map-tools' `Nav3dWriter` in the test, a flat floor with a 18 u step, a 30 u ledge, a 50 u ledge, a 100 u pit, a 40 u ceiling, a door obstacle) yields exactly the expected cells and edge types (`Walk`, `Jump`, `CrouchJump`, `Drop`, `Door`, `Crouch`-only); the stand/crouch classes come from the reader's `Standable` with the HL2MP boxes; a missing file is a refusal naming the map and the expected path and `ai_bot_add` adds nothing; a file whose level id (or bsp checksum, once M2 lands) names another map is refused by name; cache hash refusal and a one-byte `.nav3d` mutation rebuilds; the reader package version is in the cache header; the validator fact (§6.3, slow tier) over `ss_ai_nav` and dm_lockdown reports ≤ 0.5 % mismatches and a mutant that shifts every floor by 32 u reports > 0.5 %; the nav's obstacle names and ladder leaves correspond one-to-one to the bsp entity lump's `func_door*`/`prop_physics*`/`func_useableladder` entities (count and names, `BspFile.Read`); `make map MAP=dm_lockdown` rebuilds the `.nav3d` when the bsp's hash changes and is a no-op when it does not (stamp facts, differential); a `maptools.lock` mismatch is a `make setup` refusal (its self-test).
- **Bot-setup facts (§3.5):** the pool: overspend refused, per-skill cap refused, exact spend accepted — in `SkillPool` (core) and again by `ai_bot_add`/`ai_bot_roster` on the server (a fact runs the command with an overspent `skills=` and sees the refusal line and no bot); save/load round trip of a profile with revisions (bytes → object → bytes identical); undo/redo (a sequence of 10 edits, 3 undos, 2 redos lands on the expected allocation; redo after a new edit is cleared); restore of revision n makes it current and appends "restored #n"; a `format: 2` file, an unknown skill, a level 11, a name with `/` are each refused by name; the three slider stops produce exactly the §10 preset allocations (and Expert is not reachable from the slider: a fact over the screen's stop list); the roster spec the Start button issues for "2 easy + 1 custom" is the expected string; UI goldens for `createserver` (default, with custom bots) and `boteditor` (mid-edit with points left, full spend, revisions open) at the preview sizes.
- **Inspector facts (§7.2):** live and offline routes agree at a tick; uncertainty bar = range; reasons shown; web UI tests for the `Ai` tab.
- **Mutation proofs** for every rule the plan quotes a number for: flatten the skill curve, drop the PVS stage, drop the recipient check, zero the route prior, read the true clip, ignore the scoreboard, instant snap — each named mutant must turn its fact red, run from a cp snapshot with fresh mtimes (CLAUDE.md "Mutation harnesses"); snapshot copies carrying a Valve header are deleted before the final `make test` (memory `mutation-snapshots-trip-checks`).
- **Live (gate, later):** a `bots` step after `second-client` in `tools/live-gate` `STEPS`: `ai_bot_add` ×4 on dm_lockdown, 60 s, assert ≥ 1 bot kill and ≥ 1 bot death from the kill feed, zero `CommandRejected` of kind `handle`, main-thread AI cost counters within budget (§9), the spin-bot detector over the step's recording, then `map-change` keeps the second load and the thread-count check. Listen server only (ruling 2026-11-01). Daily only (memory `live-gate-cadence`).

## 9. Phases, commit-sized steps, gates

Each phase ends with `make test` green (the `ai` leg included from Phase 1),
one reviewable commit per step, ported-logic files with
`tools/valve-header.txt` and a `docs/valve-header-new.tsv` row (the BT
vocabulary and vision rules port NextBot's; the rest is original and gets no
header). Before merging to main: `make check`. Lanes delegate by file
ownership; the generated-rows commit of §3.1 is serialised. Every AI commit
passes the zero-native diff fact (§3.3) except that one.

### Phase 0 — measure before building (no code lands except counters)

1. Measure the main-thread cost of 16 `BoxInPvs` + 32 `ITraceQueries.Line`
   per tick on dm_lockdown with one human (`make headless`, a temporary
   counter group on `/counters?group=ai-probe`), 300 ticks. Record the
   numbers in §12.3 with commit and config.
2. Measure the validator's trace rate (§6.3): 2,000 `Hull` traces in a loop
   at level load, wall time. Record. (The first draft's sampler is gone.)
3. Decide the per-tick budgets from the numbers (§9 budgets below are the
   *plan's* ceilings; the measured ones replace them in §12.3).

Gate: the two numbers are in this file with their instrument and commit.

### Phase 1 — core skeleton, inline transport, replay harness, the build facts

1. `src/ai/core` project, primitives (`AiVector`, `ActorId` with serial,
   `CellId`, `Tick`), event/snapshot/`PerceivedView`/`IntentCommand` types as
   `readonly record struct`s; `Brain.Step` that outputs `Hold`; the snapshot
   ring; the decide-tick rule (§2.3).
2. `InlineTransport`, `AiRecorder`/`AiReplayer`, the `.airec` format
   (versioned, length-prefixed records; memory `length-prefixed-buffers`),
   including the per-player usercmd record (§3.4.4).
3. `src/ai/tests` project, leg, leg-floor row, test-gated list; the reference
   fact (no Abi) with its mutant harness; the `PerceivedView` facts; the
   zero-native diff fact with its mutant.
4. `src/ai/hl2mp/server` project in `SourceSharpModGated`, mods.yaml listing,
   an empty `[SideService]` that logs on `OnLevelInitPostEntity`; boot live
   once (`make headless`) to prove the mod composes (memory
   `tf2-carrier-pitfalls`: `make test` cannot see a mod that fails to
   compose).
5. `SkillProfile`, `SkillPool`, the ten skills and the pool numbers (§10),
   `EditHistory` (undo/redo, revisions) in the core with their facts — so
   every later step reads parameters, never constants.

Gate: `make test` green with the new leg counted; the no-Abi and
zero-native mutants are red; a replay of an empty recording yields an empty
intent stream, and of a one-event recording the `Hold` intent with the
right `fromTick`; the pool facts green (overspend refused).

### Phase 2 — nav: the reader, the derived graph, flow fields, the first test maps

1. `NavGraph` + `FlowField` + A*/funnel + Catmull-Rom smoothing in the core,
   with the flow-field facts (§6.2: a `Stand` field never routes through a
   `Crouch`-only cell, a door costs, costs strictly decrease) and the heading facts on
   hand-built graphs (no map-tools dependency yet).
2. **Map-tools M0** (needs the user's go-ahead in that repo): pack
   `SourceSharp.MapFormats`; `MapFormatsVersion` + `PackageReference` here;
   a fact that `Nav3dReader.Open` reads a `Nav3dWriter`-written fixture.
3. `NavGraphFromNav3d` in the binding (§6.2): the derivation over the
   reader's answers, the `.nav3d` fixture facts (§8.2), the door/prop
   obstacle flags from entity state, the voxel-ray LOS bitsets; the refusal
   paths (missing, mismatched) with their facts; the cache with its hash
   facts; the validator command and its slow-tier fact.
4. **First test maps** (§6.6): `ss_ai_nav` and `ss_ai_sight` as room
   libraries + `level.yaml` under `game/mod_test/maps/src/ai/`, the
   `make map` `link` arm and `LINKED_MAPS`, built by `make debug`; measure
   the derivation time on `ss_ai_nav` (record in §12.3).
5. **Map-tools M1 + M2** (approved; a lane in `../source-sdk-map-tools`,
   in parallel with steps 1–4): `ssmap nav3d`, the checksum binding, the
   small-bsp tests and the determinism fact there; then here
   `maptools.lock`, `MapFormatsVersion` bump, the `make map` `nav` arm and
   `NAV_MAPS := dm_lockdown` (§6.4.1); dm_lockdown's `.nav3d` built per
   tree; its build time measured (§12.3); the validator run on it at
   `n = 2000` and the obstacle/ladder-vs-lump fact.
6. `IDebugRouteProvider` in the core (own facts), `/ai/nav`, `/ai/flow`; map
   tab layers; `tools/ai-replay nav` PNG + golden.

Gate: `ss_ai_nav`'s derived graph joins its spawn to every pickup by the
expected edge types, and dm_lockdown's joins every
`info_player_deathmatch` to every weapon spawn by a `Stand` route (facts
over the per-tree graphs, which skip only if the per-tree map or nav is
absent — the one allowed skip); the refusal facts green; the validator
count on dm_lockdown ≤ 0.5 %; the PNG golden matches; derivation and nav
build times recorded. Step 5 may land after step 6; Phase 3's live walk
then runs on dm_lockdown, else on `ss_ai_nav`.

### Phase 3 — the bot is a player: creation + command path (the generated-rows commit)

1. Vendor declarations restored + KEEP + the three `EngineServerVtbl` rows
   (`CreateFakeClientEx`, `IndexOfEdict`, `SetFakeClientConVarValue`);
   `tools/engine-forwarders --write`, `tools/native-symbols --write` (one
   commit, its message quoting the generated rows; no other native diff;
   ruled 2026-11-01, no wait).
2. `IBotPlayers` core service with facts (pending-name claim, `IsBot`,
   `FL_FAKECLIENT`, the fake client's `name`/`cl_playermodel`/
   `cl_defaultweapon` read back).
3. `IPlayerCommandRunner` core service (§3.2) with the clock-restore fact
   and the "same command, same outcome" fact; `IPlayerCommandObserver`
   core hook (§3.4.4) with a fact that a human's and a bot's commands both
   reach it.
4. `BotCommandApplier` over a first `HumanMotion` (reaction delay, envelope,
   flick/pursuit skeleton; the rest in Phase 5b), buttons, move frame,
   weapon select by slot, seeds; `ai_bot_add/kick/count` with the `skills=`
   and preset forms and the server-side pool refusal fact.
5. Live, on dm_lockdown (or `ss_ai_nav` if Phase 2 step 5 is still in
   flight): a bot that holds `Follow` along a flow field to a weapon spawn
   walks there (`make headless`, screenshot via `tools/debug-api`), picks
   it up (`PickedUp` event), and a human can kill it.

Gate: the facts of steps 2–4; the applier IL allow-list fact; the
zero-native fact over every commit but step 1; live walk observed and
logged (`[SourceSharp/Ai] bot 1 arrived cell …`).

### Phase 4 — senses on the main thread, memory, prediction, inspector data

1. `VisionPass` (range → cone → PVS → budgeted traces → recognition) with the
   §4.1 negatives; `SnapshotBuilder` + `PerceivedView` filter; IL census
   naming the two classes and the no-clip fact.
2. `ISoundEmitHook` in the core (own fact: the hook sees the final recipient
   set) + the taps (fire, both-sides reloads, steps, explosions, pickups,
   doors); `SoundHeard` classification with `weaponKind`; §4.2 facts.
3. `SeenFire`/`SeenReload` from the fire tap and the animation-event tap
   (§4.5), with the unperceived-changes-nothing facts.
4. Damage/death taps; HUD/scoreboard events with ranks.
5. `WeaponKnowledge` table (§5.0) diffed against the scripts; blackboard
   stores (§5.1, ammo estimate and reload-style habits included) with decay
   facts; habits; investigations.
6. Predictors (§5.3, including ammo/reload window) with the numbered facts
   and both baselines (flat prior; ignore-ammo).
7. `BotInspectRecord` in the core (the three parts as data), `/ai/bot/<id>`
   routes (`memory|skills|perception`), the web `Ai` tab's memory and
   skills parts, map-tab cone and heatmap layers, `ai-replay heat`.
8. **Maps:** `ss_ai_hearing` (§6.6) with its scenario; the `ss_ai_sight`
   scenario (map from Phase 2) now that the vision pass exists;
   `tools/ai-scenario` itself (headless game, `ai_sync 1`, seed, the
   scripted opponent driven through the same command path) and the two
   scenario recordings committed with their facts.

Gate: all §4 negatives and §5.3 numbers green; mutants (drop PVS stage, zero
route prior, read the true clip) red; per-tick main-thread cost of the
vision pass for 4 bots × 3 others ≤ budget from Phase 0 (counter
`ai.sense.us`, 300-tick mean, a fact on the inline transport in a scenario,
and the live counter).

### Phase 5 — deciding: BT runtime, skills, the HL2MP trees, the inspector

1. BT runtime (sequence, selector, utility-selector, condition, action,
   decorator: cooldown/once/timeout) with per-node trace ring and `reason`
   terms; facts.
2. Skill curves (§10): JSON `game/mod_test/scripts/ai/hl2mp_skills.json`
   (read via `IBaseFileSystem` on the main thread, handed to the core as
   data); the four presets as read-only profiles; per-parameter "knob
   moves" facts; Headhunter's `rankBonus` curve.
3. HL2MP trees: `Survive` (retreat/heal/armour), `Fight` (engage by weapon
   family; **Push/Hold/Retreat on ammo**, own-reload management, §5.4),
   `Hunt` (Headhunter-aware), `Search`, `Ambush`, `Deny`, `Arm` (collect
   weapons/ammo by value), `Idle roam`, each reading predictions as §5.4;
   the stock HL2MP bot behaviours (`hl2mp/bot/behavior/`: attack,
   melee_attack, get_ammo, get_health, get_prop, move_to_vantage_point,
   retreat_to_cover, seek_and_destroy) are the vocabulary reference, ported
   as tree shapes, not code.
4. Steering (separation, smoothed corners with clearance, non-periodic
   strafe/range per weapon, glances and corner checks) with facts.
5. Scenario recordings (§8.2, the ammo and Headhunter ones included) and
   their facts; `/ai/bot/<id>/trace`; the web inspector's "doing and
   planning" part and the BT trace panel.
6. **Maps:** `ss_ai_duel` (reload windows, push/hold/retreat) and
   `ss_ai_routes` (memory, prediction, Headhunter) with their scenarios and
   recordings; the `ss_ai_nav` scenario (every pickup in turn) now that
   trees exist.

Gate: each §10 monotonicity fact green with its flattened-curve mutant red;
scenario facts green; Headhunter and ammo facts green; zero allocated bytes
over 1,000 steady-state brain steps (fact); AI cost per bot per decide
≤ 2 ms (plan ceiling; measured value recorded); the web inspector's tests
green over the recorded samples.

### Phase 5b — human motion: the model, the reference sessions, the metric gates

1. `HumanMotion` complete (§3.4.1–3.4.3): Fitts flicks with overshoot and
   correction, pursuit lag/error, drift/noise, recoil fraction, crosshair
   placement, fire styles and overrun, mistakes, glances, pauses, walking;
   facts for each; `/ai/bot/<id>/aim` and the aim-trace views.
2. `ai-replay metrics` (§3.4.4) over any two recordings; the metric
   definitions have facts on synthetic streams (a known flick yields the
   known peak; a known reaction yields the known delay).
3. **`ss_ai_range`** (§6.6) with its target schedule, then the **human
   reference sessions**: the user plays `ss_ai_range`'s schedule once and
   dm_lockdown freely with `ai_record start` (several sessions, ≥ 500
   flicks and ≥ 50 engagements in total); anonymised recordings land
   locally; the ones the user approves are committed under
   `src/ai/tests/recordings/human/`.
4. Bot sessions at each preset against the same map (`make headless`, 4
   bots, 5 min each) recorded; the KS statistics computed and the
   thresholds set from the numbers (§12.3); the metric gates and the
   spin-bot detector facts land with their mutants.
5. Tune curves until the gates pass at every preset; each retune is a
   commit with the metric table in its message.

Gate: every §3.4.4 metric within its recorded threshold for every preset
whose reference exists; the spin-bot detector green on every recording;
the instant-snap, no-delay and path-locked mutants red; a missing human
reference fails the leg rather than skipping.

### Phase 5c — bot setup when creating a server (the screens)

1. `SourceSharp.Ai.Hl2mp.Client` + `.Ui` projects, mods.yaml `client` and
   `client_ui` lists, `AiUiSources.cs` interfaces; boot live to prove the
   client composes.
2. Profile store over `IFwdBaseFileSystem` (`cfg/ai/bots/<name>.json`,
   §3.5): save/load round trip, refusal facts, name validation.
3. `ai_bot_roster` server convar + `OnLevelInitPostEntity` spawn, with the
   server-side refusal fact and the dedicated `profile=` arm.
4. `CreateServer` screen (map, max players, bots count, difficulty slider
   with three stops, custom list, Start) + samples + lint + goldens; the
   issued-commands fact.
5. `BotEditor` page (ten rows, meters, ±, derived parameters, points left,
   style name, undo/redo, save/save as/load, revisions/restore) + samples +
   goldens; the history facts.
6. Live: from the screen, start dm_lockdown with 2 easy + 1 custom bot;
   the three appear in the scoreboard with the expected skills (web inspector).

Gate: all §8.2 bot-setup facts green; goldens committed with the PNG paths
looked at; `make ui-lint` 0 findings; live start observed.

### Phase 6 — the worker pool, stall/teardown, record/replay live, gate

1. `AiWorkerPool` + `PooledTransport` (§2.7: per-bot serialisation,
   slot-order merge, per-bot seeds, flow-field jobs), `ai_threads`; the
   hand-off facts (§8.2) and the 1/2/4-thread determinism fact with its
   shared-scratch mutant.
2. `ai_sync`, `ai_record`, `ai_step`; first live recording committed under
   `recordings/live/`; determinism fact inline vs pooled vs recording.
3. Replay scrubber in the web UI (`/ai/replay/*`) with a golden JSON fact.
4. dm_lockdown's nav is already per-tree build output (Phase 2 step 5);
   here the gate's clean-tree run proves `make setup` + `make debug` build
   it (a gate step that fails on a missing or stale `.nav3d`, the §6.3
   refusal line being the evidence).
5. Live-gate `bots` step (§8.2), run by hand once (`tools/live-gate --steps
   bots,map-change --passes 3`) and reported; then daily; the three
   `ss_ai_*` steps (§6.6) added beside it.

Gate: the determinism facts over the live recording at 1, 2 and 4 threads;
the stall fact; two map loads with thread count back to baseline (gate
step); the live counters within the §12.3 budgets over 3 passes.

### Phase 7 — tuning pass and the TF2 hand-off (not in this plan's scope to execute)

1. Perf pass with numbers (CLAUDE.md: after the port): batching the vision
   traces into one crossing if `ai.sense.us` dominates; `[SuppressGCTransition]`
   only where the audit in memory `suppressgctransition-reentry-crash` allows.
2. Write `docs/plans/tf2/stage5_bots_plan.md` as a binding-only plan over this
   core: class/role trees, TF2's `.nav3d` (never stock `.nav`, §6) loader, TF2 taps, Soloness back
   as a team trait. The core must need no change for it; if it does, that
   change lands here first.

### Budgets (plan ceilings, replaced by Phase 0/2/4/5/5b measurements in §12.3)

| what | instrument | ceiling |
|---|---|---|
| main thread, sense + publish + apply, 8 bots, 66 tick | `ai.main.us` counter group on `/counters`, 300-tick mean | ≤ 1,000 µs/tick (6.6 % of 15 ms) |
| of which vision traces | `ai.sense.traces` count, `ai.sense.us` | ≤ 32 traces, ≤ 400 µs |
| worker pool per bot per decide (10 Hz), any thread count | `ai.brain.us` per bot | ≤ 2,000 µs |
| worker pool, 8 bots, total CPU | `ai.pool.us` per second | ≤ 25 % of one core |
| `.nav3d` read + graph derivation, `ss_ai_nav` then dm_lockdown, debug | log line `[SourceSharp/Ai] nav derived N cells from M leaves in T ms` | unmeasured; the derivation is a pool job, so only the read blocks the load |
| nav cache load | same line | ≤ 100 ms |
| validator, 2,000 traces | `ai_nav_validate` report | unmeasured (Phase 0 step 2) |
| steady-state brain allocation | `GC.GetAllocatedBytesForCurrentThread` over 1,000 steps | 0 bytes |
| human-vs-bot distance | KS statistic per §3.4.4 metric | ≤ 0.25 until measured |
| snaps > 90° per minute | spin-bot detector | ≤ 6 until measured |

## 10. Skill and difficulty: ten skills, one point pool (rulings 2026-11-01)

**The ten skills.** Appendix B's ten, with **Soloness replaced by
Headhunter** (ruling 2026-11-01): in free-for-all HL2MP Soloness drove only
`avoidBotsWeight`, a knob nobody would spend points on; Headhunter is a real
trade-off a player recognises, and keeping ten keeps the pool arithmetic,
the editor's rows and the inspector's meters at ten. TF2's binding restores
Soloness as its team trait beside these (its pool then has eleven rows; the
core's `SkillProfile` is a named vector, not a fixed ten). Each skill is a
level 0–10 and maps through a JSON curve to the parameters the brain reads:

| skill | parameters it drives | observable (fact: monotonic in level, mutant: flat curve) |
|---|---|---|
| Accuracy | `aimErrorDeg`, `aimNoiseDeg`, `trackingLagMs`, `pursuitGainErr`, `flickOvershootPct`, `recoilControl`, `leadQuality`, `staleAimPct` — **flick accuracy and tracking lag, never raw turn speed** | mean angular error ↓, time-to-acquire ↓ (inside the envelope at every level) |
| Alertness | `fovDeg` (≤ 90), `sightRange`, `hearingAccuracy`, `recognizeSeconds`, `reactionMs`, `glanceRate`, `placementSkill` | detection delay ↓ |
| Aggression | `preferredRange` per weapon family, `chaseSeconds`, `hesitationPct` ↓ | mean engagement distance ↓ |
| Cover | `coverWeight`, `reloadRetreat`, `reloadBelowPct`, `peekSeconds` | share of time in cells unseen by the target ↑ |
| Tenacity | `memoryHalfLifeS`, `searchSeconds` | search time ↑ |
| Cunning | `routeAltWeight`, `ambushWeight`, `predictionDepthS` | share of non-shortest approaches ↑ |
| Discipline | `fireRangeGate`, `burstLength`, `burstPauseMs`, `targetPriority`, **`pushTrust`**, **`pushMarginS`** (how far the ammo estimate is trusted, §5.4) | shots beyond effective range ↓; pushes inside the true reload window ↑ |
| Agility | `strafeWeight`, `strafeMeanMs`, `dodgeWeight`, `jumpUse` | projectiles dodged ↑ |
| Nerve | `retreatHealthPct`, `fleeOnDeathNearby` | retreats ↓ |
| **Headhunter** | `rankBonus` weight, `pathCapMultiplier`, `easyTargetDiscount` ↓ (§5.4) | share of engagements against the scoreboard leader ↑; mean path length to target ↑ |

**The pool (ruling 2026-11-01: a point pool replaces "no budget").** Pool
**60** points, per-skill cap **10**, ten skills (100 possible), so even a
full pool leaves 40 on the table: nobody is good at everything. Presets
and custom bots sit on one scale: a preset is a profile that spends part of
the pool; a custom bot may spend up to all of it; Expert spends it all,
which is why Expert is not a slider stop (§3.5).

| preset | spend | Acc | Ale | Agg | Cov | Ten | Cun | Dis | Agi | Ner | Head | stock recognition it matches |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Easy (slider) | 20 | 2 | 2 | 3 | 1 | 2 | 1 | 2 | 2 | 3 | 2 | 1.0 s (`hl2mp_bot_vision.cpp:128`) |
| Medium = `normal` (slider) | 35 | 4 | 4 | 4 | 3 | 3 | 3 | 4 | 3 | 4 | 3 | 0.5 s |
| Hard (slider) | 50 | 6 | 6 | 5 | 5 | 5 | 5 | 6 | 4 | 5 | 3 | 0.3 s |
| Expert (custom only) | 60 | 8 | 7 | 6 | 6 | 6 | 6 | 7 | 5 | 5 | 4 | 0.2 s |

(Sums checked: 2+2+3+1+2+1+2+2+3+2 = 20; 4+4+4+3+3+3+4+3+4+3 = 35;
6+6+5+5+5+5+6+4+5+3 = 50; 8+7+6+6+6+6+7+5+5+4 = 60.) The Alertness curve
passes through stock's four recognition times at levels 2/4/6/7, so a
preset bot recognises as its stock namesake did. Headhunter sits at 2–4 in
the presets: a visible preference for the leader without a Hard bot
ignoring a kill in front of it.

**Facts:** every preset's sum equals its spend and fits the pool; a custom
vector above 60 or with a level above 10 is refused by `SkillPool` and by
the server; the style name is the top two skills (ties by table order); each
skill's observable is monotonic in its level with everything else held at
the Medium vector (the inline simulation of §8.2), and flattening that
skill's curve turns the fact red; `turnPeakDegPerSec` at Accuracy 10 is
still inside the envelope.

Turning (§3.3, §3.4) is a skill-driven curve inside a human envelope:
Accuracy sets `flickOvershootPct`, `trackingLagMs` and the correction
phase; `turnPeakDegPerSec` (300 easy → 900 expert) follows the preset's
overall level and the applier clamps any curve to the envelope. Reaction
time is applied in the brain as a delay on acting on a new `SightGained`,
not by delaying the event (the recording stays truthful).

## 11. File layout (new)

```
src/ai/core/          SourceSharp.Ai.csproj  Primitives/ Events/ Snapshot/ Memory/ (Blackboard, AmmoEstimate, Habits) Predict/ (Position, Intent, Ammo) Nav/ Bt/ Skills/ (SkillProfile, SkillPool, Curves, EditHistory, Presets) Motion/ (HumanMotion, Flick, Pursuit) Brain/ Transport/ (AiWorkerPool, PooledTransport, InlineTransport) Record/ (AiRecorder, AiReplayer, Metrics) Inspect/ (BotInspectRecord)
src/ai/hl2mp/server/  SourceSharp.Ai.Hl2mp.csproj  BotPlayers/ Senses/ (VisionPass, SnapshotBuilder, SoundTap, FireTap, ReloadTap, DamageTap) Apply/ (BotCommandApplier) Nav/ (Nav3dLoader, NavGraphFromNav3d, NavCache, NavValidator) Knowledge/ (MapKnowledge, WeaponKnowledge) Trees/ Skills/ (Presets, CurveFile) DevApi/ Commands/ (ai_bot_add, ai_bot_roster, ai_record, ai_sync)
src/ai/hl2mp/client/  SourceSharp.Ai.Hl2mp.Client.csproj  Profiles/ (ProfileStore over IFwdBaseFileSystem) Setup/ (CreateServerActions over ClientCmdUnrestricted)
src/ai/hl2mp/ui/      SourceSharp.Ai.Hl2mp.Ui.csproj  AiUiSources.cs  CreateServer.cs (+Samples) BotEditor.cs (+Samples) resource/ (tokens)
src/ai/tests/         SourceSharp.Ai.Tests.csproj  Core/ Boundary/ Handoff/ Scenario/ Motion/ Setup/ Inspect/ recordings/ (synthetic, live/, human/)
src/tools/AiReplay/   SourceSharp.Ai.Replay.csproj  (nav, heat, aim, metrics)
game/mod_test/scripts/ai/hl2mp_skills.json
game/mod_test/maps/src/ai/   ss_ai_{sight,hearing,nav,duel,routes,range}.vmf (room libraries) + .level.yaml (committed sources; bsp + .nav3d are per-tree build output)
tools/ai-scenario     runs one ss_ai_* scenario headless and writes its recording
maptools.lock         the map-tools commit make setup checks MAPTOOLS_DIR against (like valve-sdk.lock)
game/mod_test/maps/dm_lockdown.nav3d (+ .stamp)   per-tree build output of `make map MAP=dm_lockdown` (ssmap nav3d), gitignored
docs/ai/              format of .airec and .navgraph, the dev-API routes, the profile JSON, the test maps' scenarios (written in Phase 1/2/5c, not before)
Makefile              LINKED_MAPS + NAV_MAPS, the `link` and `nav` arms of `make map`; MapFormatsVersion restore from $(MAPTOOLS_DIR)/bin/nupkg
```

Core changes this plan asks of `SourceSharp.Server`/`Debug.Api` (each a
generic service with its own fact, landed before the binding uses it):
`IBotPlayers`, `IPlayerCommandRunner`, `IPlayerCommandObserver`,
`ISoundEmitHook`, `IDebugRouteProvider`, and the three generated forwarder
rows of §3.1 (ruled). Hand-written native code: none.

## 12. Decisions

### 12.1 User rulings (2026-11-01)

- Build the TF2 AI plan, **starting with HL2MP** (simpler: no 3D nav mesh).
- The AI may **only do what a player can do** and **see what a player can
  see**.
- It has **memory**, and **predicts** what a player has done before and what
  they might do now, and uses that in its choices.
- **Own assembly, own tests, own visualisations.**
- **Full plan before execution** (this document; nothing is implemented).
- **Turning:** as fast as a human would, ± by skill (§3.3, §10).
- **Opponent memory is in memory only**, never on disk (§5.2).
- **Lighting is skipped**, but everything that blocks a player's line of
  sight blocks the bot's: doors, props, other players (§4.1).
- **Crouch-jump edges are in v1** (§6.2: the file's jump links split by HL2MP's reach).
- **The live gate's bots step is listen-server only**; no dedicated-server
  step (§8.2).
- **Bots know the map before playing:** static item spawns, spawn points and
  the layout are known up front; anything dynamic is perceived (§5.0).
- **Per-bot debug UI** showing what it remembers, its skill allocation, and
  what it is doing/planning (§7.2): the web inspector (live and offline),
  over one record. **No in-game inspector UI** (ruling 2026-11-01: "bot
  inspector needs no in game ui"): nothing about a bot's mind is networked.
- **Bot setup when creating a server:** an Easy/Medium/Hard slider and a
  custom bot whose skill points the user spends, with history and
  save/load (§3.5, §10). Interpretations chosen: the three stops map to
  stock's easy/normal/hard presets and **Expert is reachable only as a
  custom bot** (the coordinator's suggestion); **history = undo/redo while
  editing plus a saved revision list per profile with restore**; profiles
  are stored client-side under the mod's game dir through
  `IFwdBaseFileSystem`; on a listen server the choices reach the server as
  console commands (`ai_bot_roster`, `ai_bot_add`), on a dedicated server
  by profile name on its disk or an inline `skills=` spec; the server
  validates the pool itself.
- **AI work may use more than one worker thread, never the main game
  thread** (§2.7): a pool, split by bot (brains are independent), flow
  fields as jobs, the nav graph derived off-thread from the `.nav3d`
  (only the trace validator stays on the main thread); determinism by decide-tick-keyed inputs, per-bot seeds,
  no shared mutable state and slot-order merge; a 1/2/4-thread replay fact.
- **The bot is 100 % managed code** (§3): perception, nav, memory, brains,
  inspector and setup UI are C#; hand-written C++ is zero. The command loop
  needed no native change after all.
- **Native methods may be exposed to managed code to make bots work**
  (§3.1, later the same day): the generated `FORWARD` rows
  `CreateFakeClientEx`, `IndexOfEdict` and `SetFakeClientConVarValue` are
  accepted, per CLAUDE.md's forwarder rules (append-only, `--write`, vendor
  declarations restored and kept); exposure is by generated rows only,
  never a hand-written shim or a free `SourceSharp_*` function; the
  native-symbols fact allows exactly those rows' symbols (§3.3); Phase 3
  waits on nothing.
- **Track enemies' shots and reloads to know when to push, hold or
  retreat** (§4.5, §5.1, §5.3.4, §5.4): perceived only as a player could
  (heard through the recipient filter or seen while sighted; some reloads
  are inaudible by stock's own rules), weapon clip/cadence/reload lengths
  known up front, estimates kept as ranges with uncertainty, reload-style
  habits learned, Discipline drives how much the estimate is trusted; the
  inspector shows the estimate and the verdict.
- **A trait that hunts the top of the leaderboard** — **Headhunter**,
  replacing Soloness in HL2MP's ten (§5.4, §10): scoreboard-only ranking,
  Hunt toward the leader's predicted cell, longer paths and passed-up easy
  targets by weight, preset values 2–4.
- **No stock nav-mesh logic; the map-tools `.nav3d` is the nav mesh**
  (§6): no `.nav`, no `CNavMesh`/`CNavArea`, no NextBot path code, no stock
  nav-area concepts; the graph, pathing and flow fields over `.nav3d` are
  ours, held by a header-row and IL fact.
- **The nav mesh is built from the nav data the map-tools repo produces
  when compiling a map** (§6): the `.nav3d` file, read through map-tools'
  `Nav3dReader` (packaged, pinned), the player-hull graph derived at load,
  cached by bsp + nav3d hashes; a missing or mismatched file is a refusal;
  the runtime hull-trace sampler is dropped as a source and kept only as a
  spot-check validator. Map-tools work: §6.4 (M0 package, M1 `ssmap nav3d`
  from a bsp, M1' the same from `vbsp`/`all`, M2 checksum binding, M3 none).
- **Map-tools may be updated to build `.nav3d` from bsp files instead of
  decompiling them** (§6.4, M1; the user's go-ahead for that repo's work).
  This **supersedes** two earlier rulings of the same day, kept for
  history: "can recompile dm_lockdown" and "decompile maps with BSPSource
  (`https://github.com/ata4/bspsrc`)" — superseded 2026-11-01 because the
  builder already reads a compiled bsp's brushes, so no decompile, no
  third-party tool, no Java and no Valve-derived VMF are needed; the stock
  bsp stays in the game. Commit policy: dm_lockdown's `.nav3d` is
  **per-tree build output of `make`** through the pinned ssmap
  (`maptools.lock`), never committed (§6.4.1); a missing or stale file is
  still a refusal.
- **New maps may be made to test bot features** (§6.6): six `ss_ai_*`
  room-library maps compiled per tree through `ssmap link` (which writes
  `.nav3d` today), each with a scripted scenario and a gate that can fail;
  three join the daily live gate; they unblock the bot work before
  dm_lockdown's nav lands.
- **Bots feel human: move and shoot like humans, no spin bots, no
  always-quick snapping** (§3.4): a motion model (reaction delay, Fitts
  flicks with overshoot and correction, pursuit lag, drift and noise,
  recoil error, crosshair placement, human fire styles and overrun,
  mistakes, free look, smoothed paths, aperiodic strafing, hesitation,
  walking, jumps only where a player would), **measured** against recorded
  human sessions (the recorder captures humans' usercmds too; local,
  anonymised; committed only with the user's say-so) with KS-distance
  gates and a spin-bot detector; skills map to flick accuracy and tracking
  lag, never raw turn speed, and every preset stays inside the human
  envelope.

### 12.2 Open questions

None. The §3.1 exposure and the map-tools nav work were ruled on
2026-11-01 (§12.1); the one open point inside M1 (whether
`NavGeometry.FromBsp` already takes displacements) is settled by reading
that code when M1 starts, and changes nothing here.

### 12.3 Measurements (filled in by Phase 0/2/4/5/5b; empty = not measured)

| what | value | instrument | commit/config |
|---|---|---|---|
| 16 BoxInPvs + 32 Line per tick, main thread | mean 165.3 / 122.0 / 120.4 µs, p50 136.2 / 116.3 / 119.2 µs, p95 321.0 / 175.5 / 160.0 µs, max 600.2 / 476.9 / 486.2 µs (three runs of 300 ticks, one human, listen server) | a temporary `ai_probe` console command (`Stopwatch` around each tick's batch, one batch per frame through `IFrameScheduler.Delay(0)`): 16 `IEnginePlayers.BoxInPvs(player 1, 32×32×72 box)` at seeded random points ±2,000 u, then 32 `ITraceQueries.Line` from `GetShootOrigin` to seeded points 1,500 u away (mask `MASK_BLOCKLOS`+`CONTENTS_MONSTER`, pass = the player); 4,800 PVS asks answered true 754 times, 9,600 lines hit 9,304 times; removed after measuring, never committed | base `1ca008249` + the probe, debug, `make headless` dm_lockdown, 2026-11-01 |
| 2,000 Hull traces (validator) at load | 1.88 / 1.67 / 1.30 ms wall (0.94 / 0.83 / 0.65 µs per trace) | the same probe after its 300 ticks: 2,000 `ITraceQueries.Hull` of the standing box (±16, ±16, 0–72) swept 1 u up from seeded points ±1,500 u around the player, 64 u below its eyes, mask `MASK_PLAYERSOLID`; 1,547 of 2,000 clear. Run from a console command after the load, not inside it: the trace cost does not depend on when it is asked | as above |
| Phase 0 budget decision | vision: keep `ai_vision_traces_per_tick` 32 and the 400 µs `ai.sense.us` ceiling (measured 16 PVS + 32 lines ≈ 120–165 µs mean, ≤ 600 µs worst tick, debug); validator: `n = 2,000` costs ≈ 2 ms, so it may run at level load on the main thread in debug builds | §9 Budgets | from the two rows above |
| `.nav3d` read + derivation, ss_ai_nav / dm_lockdown | — | | |
| vision pass 4 bots | — | | |
| brain decide per bot | — | | |
| pool CPU, 8 bots, 4 threads | — | | |
| human reference: flicks / engagements recorded | — | | |
| KS threshold per §3.4.4 metric (9 rows) | — | | |
| human snaps > 90°/min ceiling | — | | |

### 12.4 Choices made in this plan (small, stated)

- Hearing = recipient-filter membership + soundlevel, not room-graph path
  distance (§4.2): it is literally what a client gets, and we have no room
  graph.
- The AI's graph is derived from the `.nav3d` leaves (one cell per
  standable column, two clearance classes, six edge types), with jump vs
  crouch-jump split by HL2MP's numbers because the file leaves that to the
  agent (§6.2); LOS bitsets by voxel ray through the file, not by traces.
- The reader arrives as a versioned package from map-tools' local feed,
  not a cross-repo project reference (§6.1).
- dm_lockdown's nav built per tree by `make` from the committed stock bsp through `ssmap nav3d`, bound by checksum; nothing new committed (§6.4.1).
- Test maps through the rooms pipeline, per tree, sources committed (§6.6).
- Respawn intervals are seeded from the game's convars (a player who knows
  the game knows them), then refined by observation (§5.1).
- Other players block sight, unlike stock NextBot (§4.1).
- Core primitives instead of `SourceSharp.Abi`'s `Vector`/`EntityHandle`
  (§1.2), so the no-Abi build fact holds.
- Position predictor = cell-probability map over the nav graph (§5.3), not a
  particle filter; ammo estimate = range + small histogram, not a scalar.
- Skills: stock's four presets as profiles over a 60-point pool with cap
  10; Headhunter replaces Soloness (§10).
- No `SourceSharp.Ai.Interface` assembly (§1.3); UI types in
  `SourceSharp.Ai.Hl2mp.Ui`, never in a `*.Interface` assembly.
- A headless client process per bot (a 100 %-managed, zero-ABI shape) was
  not taken: an engine process per bot, perception rewritten client-side,
  and no bots from a dedicated host's create-server flow; three generated
  rows cost nothing of the kind (§3.1).
- A create-server screen with a typed map name; the map list waits on
  `FindFirst/FindNext` rows (§3.5).
- The decide tick, not the thread, fixes a step's inputs (§2.6).
- `.airec`/`.navgraph` are our own binary formats, versioned, length-prefixed;
  profiles are JSON with `format: 1`.
- **The zero-native fact reads per AI commit, not `base..HEAD`** (found in
  Phase 1, 2026-11-01): §3.3's `git diff --stat <plan base>..HEAD` over the
  native dirs holds only while the lane is alone; once it lands, main's other
  lanes change native code after the base and the fact would go red for work
  that is not the AI's. `ZeroNativeTests` (src/ai/tests/Boundary) takes an
  AI commit to be one in `1ca008249..HEAD` that touches `src/ai/` (and the
  uncommitted tree, when it does), and holds both rules over those: no native
  path, and no `docs/native-symbols.tsv` row beyond the three §3.1 slots.
  `tools/ai-mutations` arms `native-line` and `native-symbol` turn it red.
- **The derivation reads the file through `INavSource`** (Phase 2,
  2026-11-01): `NavGraphFromNav3d` (src/ai/hl2mp/server/Nav) is pure over a
  small interface shaped on §1.1's description of `Nav3dReader` (leaves with
  floor, walkable, water, ladder, cost; `Standable`; `Neighbours`; `StepUp`;
  `Jumps`; obstacles and their leaves), so it and its facts landed before the
  `SourceSharp.MapFormats` package; the adapter over the real reader, and the
  facts over a `Nav3dWriter` fixture, land with the package (step 2, M0).
  The thresholds come from the movement code (`Hl2mpNavNumbers.Stock`:
  step 18, jump rise 21 + 18 = 39, the file's 56, safe drop
  526.5² / (2 × 600) ≈ 231 u).
- **The funnel alone does not straighten a grid path** (Phase 2,
  2026-11-01; §2.3, §3.4.3 assumed funnel + Catmull-Rom was enough): on a
  4-connected grid many cell paths tie for cheapest, and A* or a flow field
  returns one that runs along a side, so the funnel through it bends (11° on
  a 3 × 12 corridor). `PathSmoother.Shortcut` cuts walked corners where
  `NavGraph.WalkableLine` says a straight line is walkable (each crossed cell
  admits the class and is reached by a passable Walk/Door edge); forced
  corners (jump, drop, ladder) are kept. The heading fact holds after it.
- **Two §1.1 citations are off at this base** (the movement row): `sv_stepsize`
  is `src/server/managed/MoveVars.cs:250` (not under `src/shared/interop/`),
  and `StockJumpHeight` is `GameMovement.StockJumpHeight`,
  `src/shared/interop/Movement.cs:567` (not :504). The derivation reads the
  numbers from `SourceSharp.Shared.Movement.Current` and
  `MoveMath.PlayerMaxSafeFallSpeed` instead of restating them.

## 13. Stock references (read-only, `../source-sdk-2013-valve`)

- `src/game/server/NextBot/Player/NextBotPlayer.h` — `NextBotCreatePlayerBot` (:36–60), `_NextBot_BuildUserCommand` (:558), `PhysicsSimulate` (:577): how a stock bot synthesises its usercmd.
- `src/game/server/tf/tf_bot_temp.cpp:695` `RunPlayerMove` — the older usercmd-per-think bot path.
- `src/game/server/NextBot/NextBotEventResponderInterface.h` — 53 `On…` events (vocabulary), `OnInjured` :67, `OnSight/OnLostSight` :71–72, `OnSound` :74, `OnWeaponFired` :76.
- `src/game/server/NextBot/NextBotVisionInterface.h` — FOV 90 (:142), range 2,000 (:215), recognition (:183); `.cpp` for the LOS mask.
- `src/game/server/hl2mp/bot/` — `hl2mp_bot.h:201` difficulties; `hl2mp_bot_vision.cpp:122–151` recognition times and 6,000 u range; `behavior/` 27 files for tree vocabulary; `hl2mp_bot_manager.cpp:196` (nav-mesh dependency we replace).
- `src/game/shared/basecombatweapon_shared.cpp` — `WeaponSound` recipient rules (:1915–1942: PAS by soundlevel, prediction rules exclude the owner, `play_to_owner_only`); `DefaultReload`'s `WeaponSound(RELOAD)` under `#ifdef CLIENT_DLL` (:2042–2045), which is why most reloads are inaudible to others.
