# TF2 port — Stage 0: core seams (no TF2 code yet)

Status: DRAFT, uncommitted (repo rule). Part of `docs/plans/plan_tf2_port.md`;
read that file's §0 rulings, §2 "how TF2 attaches to the core" and §6 agent
contract first. Measured at main `375193a4c` (2026-10-11); re-check every
cited line with `grep -n` before editing — the citation is a pointer, not a
promise.

## Why this stage exists

The core forbids what the first draft assumed. Verified at `375193a4c`:

| The draft assumed | What the core does | Where |
|---|---|---|
| TF2 registers a movement extension | The three movement hooks are **static stubs** returning stock; nothing can register | `src/shared/managed/Movement.cs:146,175,196`; wired through `IServerModule.GetJumpScale/GetFrictionScale/GetMaxSpeedScale` (`src/server/interop/IServerModule.cs:217-231`) → `ServerModule.cs:3287-3293` and `ClientModule.cs:1832-1838` |
| `TFWeaponBase` over Arsenal | `WeaponLogic` is **sealed and data-driven**; a weapon is a `WeaponType` row in an **append-only static table** whose index is the wire format; the client predicts with the same table | `src/shared/managed/WeaponLogic.cs:142`, `WeaponTypes.cs:32,192,418,430`, `AmmoTypes.cs:30,99`, `src/client/managed/ClientArsenal.cs:30` |
| TF2 supplies its own `GameRules` | A mod **may not replace a core service** (`"a mod adds services, it never replaces one"`); `IGameRules` and `GameRules` are fixed | `src/shared/managed/SideServices.cs:183-188`; `src/server/managed/Services.cs:18`, `GameRules.cs:76`, `ServerModule.cs:1258,1345` |
| Teams exist | **No teams**: `jointeam` is refused, `IEnginePlayers.GetTeam` is read-only, no `SetTeam` on any ABI area, spawn selection is team-blind | `ServerModule.cs:2254-2266`, `Observers.cs:158-165`, `src/shared/interop/EngineAreas.Players.cs:62`, `ServerModule.SelectSpawnPoint :2004` |
| TF2 HUD screens register like Descent's | **Mod UI assemblies never reach the game**: the UI loader loads exactly three assemblies and the screen list is hand-built; Descent's screens run only in preview/lint/tests | `src/client/managed/Ui/UiLoader.cs:52`, `src/client/ui/host/UiComposition.cs:35` (`ClientUi.Build`) |
| HUD strings come from `tf_english.txt` | One language file, read from disk by path: `resource/mod_sharp_english.txt`; VPK content is unreachable to the localiser | `UiComposition.cs:31,59-64`, `toolkit/Text/UiLanguageFile.cs:30,41` |
| Hitgroups available for headshots | **No hitgroup crosses the ABI**; managed `FireBullets` is a surface trace | `Arsenal.cs:1084`, `EngineAreas.World.cs:51` (`SurfaceTraceResult`), notes at `BlastSweep.cs:83`, `PhysicsCannister.cs:184` |
| Mod effects/tempents discoverable | Effect handlers are a **fixed list** in `ClientModule`; tempent `RegisterAll` is passed the core assembly only | `ClientModule.cs:939-948,998,1026` |
| Mod handles `joinclass` | Client commands are an inline `switch` with no hook | `ServerModule.cs:~2230-2290` (`"spectate"` :2246, `jointeam` :2260) |
| TF2 can call `FiredBy`/`ThrownBy`/`Explosions` | All **internal** to `SourceSharp.Server`; `InternalsVisibleTo` names test projects only | `FlyingProjectile.cs:100`, `Grenade.cs:83`, `Entities/Env/Explosions.cs:32` |
| `make run` can start tf2 | `run` is hard-wired to `mods/test`; only `headless` has a `LAUNCH_MOD` hook | `Makefile:60,65,411,453-460` |

Every phase below opens one seam **as a generic core feature** (rule: "find it
on the existing ABI first, then add a *generic* ABI call"). Nothing in this
stage mentions TF2 except tests that prove a *mod* can use the seam. Each phase
is its own commit (or a few), and the full `make test` must stay green with the
seam unused, because the test mod and tf2_descent ride the same core.

**Ruling D10 (user, 2026-10-11) shapes the whole stage**: core → TF2 → further
mods, each layer extended by composition. So the *generic machinery* lives
here — the game-mode proxy (U1), the per-player composite, the weapon
behaviour seam (U2), the animation state machine (U5) — and a mod only
registers into it. Every seam is designed so that two mods can register
side by side (TF2 and Descent): chains compose in a deterministic order;
single-owner hooks are for things that genuinely have one owner (movement
constants, the join policy) and must say so in their doc comment.

Phases 0.1–0.4 and 0.9 are independent of each other and can run as parallel
lanes (file ownership: 0.3 owns `Movement*`, 0.4 owns `Weapon*`/`Arsenal*`,
0.9 owns `src/client/ui/host/*`). 0.5 → 0.6 → 0.7 are sequential (0.7 reads
0.5's team service). 0.8 is independent but needs an ABI tail: serialise it
with any other ABI change.

---

## Phase 0.1 — TF2 project skeleton, test project, test leg

**Make**
- `src/tf2/shared/` directory; both `TF2.Server.csproj` and `TF2.Client.csproj`
  get `<Compile Include="..\shared\**\*.cs" Link="shared\%(RecursiveDir)%(Filename)%(Extension)" />`
  (the Descent pattern, `src/tf2_descent/server/TF2.Descent.Server.csproj`).
  Shared source compiles twice; there is no `TF2.Shared` assembly (and so no
  `[StandaloneNetState]` double-registration problem: that is how
  `src/shared/managed/*NetState.cs` already works).
- `src/tf2/tests/TF2.Tests.csproj`: copy `src/tf2_descent/tests/TF2.Descent.Tests.csproj`
  minus the host packages; reference `TF2.Server`, `TF2.Client` with
  `Aliases="client"` (shared source exists twice), `SourceSharp.Abi`,
  `src/test/support/SourceSharp.TestSupport.csproj`; `OutputPath`
  `bin/<cfg>/tf2-tests/`. Link `src/test/common/RepoLayout.cs` so facts can
  read the farm/fork the way core tests do (`src/test/server/SourceSharp.Server.Tests.csproj:73`).
- Register it everywhere the Descent project is registered, or the build/tests
  will not see it: `SourceSharp.slnx`; `SourceSharpTestGated` in root
  `Directory.Build.props`; `TestProjects` in
  `src/test/managed/TestSupportClosureTests.cs:17-24`; Makefile `TF2_TEST`
  variable + `tf2` leg in `TEST_LEGS` (model: `Makefile:732,851,905-906`) and
  a `make tf2-test FILTER=` target; `tools/leg-floor` function (model
  `floor_descent`, `tools/leg-floor:543-545,602`).
- `src/tf2/` must **not** get a `Directory.Build.props` (Descent's adds the
  host SDK; TF2 never references the host).
- Fix `src/tf2_descent/tests/ReferenceClosureTests.cs:56-65`: it checks the
  prefix `"TF2Descent"` but assemblies are `TF2.Descent.*`, so that half can
  never fail. Change to `"TF2.Descent"` and prove it red by temporarily adding
  a reference (mutation note in the commit message).

**Facts** (in `src/tf2/tests/`)
- `Tf2TestLegRuns`: a trivial fact so the leg is non-empty from the first commit.
- Structure: `TF2.Server`/`TF2.Client` reference neither `TF2.Descent.*` nor
  `SourceSharp.Host.*` (read the deployed dll references like
  `DeployedReferencesTests.cs:18-19` does).

**Gate**: `make test` lists a `tf2` leg with a declared floor; `make tf2-test
FILTER=Tf2TestLegRuns` runs exactly one test; `AnalyzerWiringTests`
(`src/test/managed/AnalyzerWiringTests.cs:21-60`) still green; the
ReferenceClosure fix is shown red-then-green in the commit message.

## Phase 0.2 — Launch plumbing and an offline VPK reader

**Make**
- Makefile: `RUN_MOD_ID ?= test`; `run`, `run-debug`, `headless` pass
  `-mod $(RUN_MOD_ID)` (today `run` snapshots `$(MOD)`=`mods/test`,
  `Makefile:60,387-411`; keep the snapshot but of `mods/$(RUN_MOD_ID)`).
  `tools/launcher-name-test` must still pass (it checks every launch path
  writes `./source\#`). Keep argv total under 512 bytes (`source#` refuses
  longer lines).
- `tools/vpk-extract` (python, no deps): read a Valve VPK v1/v2 directory file
  (`tf2_misc_dir.vpk`: signature `0x55aa1234`, version, tree size; tree =
  extension → path → filename → entry {crc, preload bytes, archive index,
  offset, length}) and extract named paths from `tf2_misc_NNN.vpk`. Usage:
  `tools/vpk-extract bin/debug/content/tf/tf2_misc_dir.vpk scripts/playerclasses/scout.txt -o <dir>`
  and `--list 'scripts/items/*'`. This is how later stages obtain test fixtures
  (class scripts, `items_game.txt`, weapon scripts) without a Steam tool; no
  python `vpk` module is installed and the install ships no vpk binary
  (checked 2026-10-11).

**Facts**
- `tools/vpk-extract --self-test`: builds a tiny VPK in a temp dir (dir +
  one archive), extracts, compares bytes and CRC. Add the self-test to
  `tools/checks.tsv` (checks leg).
- Makefile fact (in `src/test/server/Launcher/` beside `ModsConfigTests`):
  `make -n headless RUN_MOD_ID=tf2` prints `-mod tf2`, and `RUN_MOD_ID=test`
  prints `-mod test` (before ≠ after).

**Gate**: `make headless RUN_MOD_ID=tf2 MAP=ctf_2fort` reaches
`[SourceSharp/Hud] first paint` with TF2 content mounted (the console shows
`tf2_misc` search paths; `tools/debug-api` answers); `tools/vpk-extract ...
scripts/playerclasses/scout.txt` yields a KeyValues file containing
`"speed_max"`. Record both commands' output in the commit message.

## Phase 0.3 — Movement rules as an injected, side-symmetric hook

Stock parity constraint: movement is predicted, so server and client must
answer identically; the hook must be shared source resolved the same way on
both sides.

**Make** (owner files: `src/shared/managed/Movement.cs`, `src/shared/interop/Movement.cs`,
`PlayerCommandsBinding.cs`, `PredictionClientBinding.cs`, the two module
resolvers)
- `public interface IMovementRules` in `src/shared/managed/` with the three
  methods the static stubs have today (`JumpScale(in JumpRequest)`,
  `FrictionScale(int playerIndex, float friction, float speed, bool ducking, int groundMaterial)`,
  `MaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial)`)
  plus `MovementConstants? Constants { get; }` (a mod's hull/view/jump
  constants, replacing the global `Movement.Current` defaults at
  `src/shared/managed/Movement.cs:46-113` when present).
- A core `MovementRulesResolver` (both sides) that takes `IHooks<IMovementRules>`:
  zero hooks → stock (`MovementRules.Stock`, `src/shared/interop/Movement.cs:501`);
  one → it; two or more → a startup error naming both types (two mods cannot
  both own movement). `ServerModule.cs:3287-3293` and `ClientModule.cs:1832-1838`
  call the resolver instead of the statics; delete the statics.
- `Movement.Register` (`src/shared/managed/Movement.cs:118`, called at
  `ServerModule.cs:1266`, `ClientModule.cs:846`) takes the resolved constants.
- Per-player eye height: TF2 sets a per-class view offset. Find where
  `MoveState.ViewOffset` (`src/shared/interop/Movement.cs:398`) is filled from
  the player's state; if it is not a networked, predicted per-player field,
  add `ViewOffsetZ` to `PlayerNetState` (`src/shared/managed/PlayerNetState.cs`,
  `[Predicted]`) with a server setter on `SsPlayer`, and have both bindings
  feed it into `MoveState`. Default = `Movement.Current.View.Z` so nothing
  changes for the test mod.

**Facts** (`src/test/managed/` for shared, mirrored expectations server+client)
- No hook → every scale is the identity (existing movement facts stay green).
- One fake hook registered in a `ServiceContainer` (pattern
  `src/test/managed/SideServicesTests.cs:80-98`) → `MaxSpeedScale` seen by the
  server binding equals the one seen by the client binding for the same inputs.
- Two hooks → startup error, both type names in the message.
- Constants from the hook reach `Movement.Register` (RecordingEngine shows the
  mod hull, not 32×32×72).
- Source-parity fact in the style of `MoveTypePredictionParityTests.cs`: both
  bindings call the resolver (no stray static remains: grep-fact over
  `src/shared/managed/Movement.cs` for `public static float .*Scale` = 0).

**Gate**: `make managed-test FILTER='Movement'` green, prediction facts green
(`ClientPlayerPredictionTests`), `make headless` on `dm_lockdown` shows no
prediction error growth (`tools/debug-api` prediction report, before ≠ after
not required — this is a no-change gate: the error count stays at the pre-change
value you record first).

## Phase 0.4 — Weapon behaviour seam and mod-extensible weapon/ammo catalogues

Goal: a mod adds weapon types and per-type behaviour without touching
`WeaponLogic`'s stock path, and the client predicts them with the same code.

**Make** (owner files: `src/shared/managed/Weapon*.cs`, `AmmoTypes.cs`,
`src/server/managed/Arsenal.cs`, `src/client/managed/ClientArsenal.cs`)
- `WeaponCatalog` and `AmmoCatalog` services (both sides) built once at
  startup from the core rows (`WeaponTypes.All :192`, `AmmoTypes.All :30`)
  followed by every `IWeaponTypeSource` / `IAmmoTypeSource` hook's rows in a
  **deterministic order**: hooks sorted by assembly name then declaration
  order, and a fact proves server and client produce identical indices for
  the same assembly set (the index is the wire format: `IndexOf :418`,
  `ByIndex :430`, `AmmoTypes.WireIndex :99`). Replace static lookups with
  the injected catalogue (SS0001 forbids a mutable static table anyway).
- `public interface IWeaponBehaviour` in `src/shared/managed/`: the points
  where `WeaponLogic` decides — `BeforePrimaryAttack`, `PrimaryAttack`,
  `SecondaryAttack`, `Reload`, `ItemPostFrame`, `ModifyFireInterval`,
  `ModifyClipSize` — each with a stock default (`WeaponLogic.cs:172,250,326,386`).
  `WeaponType` gets a `Behaviour` key (string) resolved through an
  `IWeaponBehaviourSource` hook; unresolved → stock. `WeaponLogic` stays
  sealed; it calls the behaviour at the named points and otherwise runs
  stock. `ClientArsenal` resolves the same behaviours (prediction).
- `IWeaponArms` (`WeaponLogic.cs:26`) gains the arms a projectile/beam mod
  needs and the stock set lacks: `LaunchWith(in WeaponType, string projectile, Vector velocity, float fuse)`,
  `BeamTarget(...)` only if 0.4's test mod needs it — otherwise leave to
  Stage 2 (do not pre-build).
- Make `FlyingProjectile.FiredBy` (`:100`), `Grenade.ThrownBy` (`:83`) and
  `Explosions.Fire` (`Entities/Env/Explosions.cs:32`) public API with
  `<summary>` docs (not `InternalsVisibleTo`: a mod is not a test).

**Facts**
- Catalogue: core rows keep their indices (fact pins the first and last core
  index before/after); a test `IWeaponTypeSource` row appears after them; two
  sources in reversed registration order yield the same indices (sorted).
- Behaviour: a fake behaviour that doubles `ModifyFireInterval` changes
  `NextPrimaryAttack` by exactly that in `WeaponLogic.RunCommand`; with no
  behaviour the existing `WeaponLogic` facts are unchanged.
- Client/server parity: `ClientArsenal` and `Arsenal` resolve the same
  behaviour for the same type (one fact constructing both with the same
  container).

**Gate**: `make managed-test FILTER='Weapon|Arsenal|Ammo'` green; live:
`make headless` on `dm_lockdown`, fire each stock weapon once via
`tools/debug-api` and confirm ammo/clip behaviour unchanged (record clip
values before and after a shot — the same values as before this phase).

## Phase 0.5 — Teams: the one new ABI call, a team service, team-aware spawns

**Make**
- ABI: `SetTeam(EntityHandle player, int team)` on the players shim area
  (`IEnginePlayers`, `src/shared/interop/EngineAreas.Players.cs`; a `chain=`
  row in `tools/vtable-layout`, a row in `docs/engineapi-areas.tsv`,
  regenerate both sides). Native carries it out with `pPlayer->ChangeTeam(team)`
  (`ChangeTeam` is `CBaseEntity`'s; the standalone player shell is
  `src/server/native/shell_standalone_player.cpp`, which has no team code
  today). Native makes no decision: it does not validate the team.
- Core `ITeams` service (server): `Team(EntityHandle)`, `Players(int team)`,
  `Count(int team)`, `Join(EntityHandle, int requested)`. `Join` asks the
  single `IJoinTeamPolicy` hook (zero → stock HL2DM behaviour; one → the
  mod decides auto-assign/balance/refuse; two → startup error) and then calls
  `SetTeam`. `jointeam` at `ServerModule.cs:2260` routes to `ITeams.Join`
  instead of refusing; `Observers.JoinTeam` (`Observers.cs:165`) keeps the
  spectator arm. Fire the stock `player_team` game event (the client's
  `ChatNotices` already subscribes to `PlayerTeam`, `ClientModule.cs:1112-1129`).
- Spawn: `ISpawnSelector` hook consulted first by `SelectSpawnPoint`
  (`ServerModule.cs:2004`); null result → stock walk over
  `Pickups.SpawnPointClasses`.
- `PlayerResource` (`src/server/managed/Entities/PlayerResource.cs`) must
  publish the team per player if it does not already (the scoreboard reads
  `PlayerResourceNetState`, `ClientPlayerResource.cs:50`).

**Facts**
- RecordingEngine: `jointeam 2` with a policy hook that accepts → exactly one
  `SetTeam(player, 2)` call; a refusing policy → zero calls and the stock
  refusal text.
- Zero policies → HL2DM behaviour (the existing `Observers` facts).
- `ISpawnSelector` returning a handle wins over the stock walk; returning
  `EntityHandle.None` falls through (both arms, stimulus proven non-zero).
- `AbiParityTests` and the vtable-layout check green (new slot hashed).

**Gate**: `make debug` + `make test`; live: `make headless RUN_MOD_ID=test`,
join, `jointeam 2` via `tools/debug-api`, `/entity` shows the player's
`m_iTeamNum` = 2 and the scoreboard mirror shows team 2 (a datatable change is
verified only by a full join: write on the server, read on the client).

## Phase 0.6 — Client-command hook

**Make**: `IClientCommandHook { bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args); }`
dispatched (`IHooks<T>.Each`, first `true` wins, deterministic order by
`IOrderedHook.HookOrder`) before the inline `switch` in `ServerModule`'s
client-command handler (`~:2230`). Unclaimed commands fall through unchanged.
Also expose `ICommandSender` or reuse `IUserMessageSender` for the reply
channel mods need (`ClientPrint`-style text to one player) if not present.

**Facts**: a hook claiming `joinclass` receives `("joinclass", ["scout"])`
and `spectate` still reaches the stock arm (`Observers`); two hooks → the
lower `HookOrder` runs first and a `true` stops the chain (prove with a
recording second hook that is never called).

**Gate**: `make managed-test FILTER='ClientCommand|Observers'`.

## Phase 0.7 — Game-mode proxy, per-player composite, rules extension points (U1, D10)

A mod cannot replace `IGameRules`; the core gains the generic machinery and
mods register into it. Three parts, three commits.

**Make, part A — `GameModes` (the generic mode proxy, in `src/server/managed/Modes/`)**
- `IGameMode { string Id; IRoundFlow Rounds; IRespawnPolicy? Respawn; ISpawnSelector? Spawns; IScoring? Scoring; IModeSetup? Setup; }`
  — one interface per concern so a mode composes them; a mode may return
  `null` for a concern and the core default applies.
- `GameModes` service: collects every `[SideService(Side.Server, As=[typeof(IGameMode)])]`
  (TF2's, Descent's, the test mod's), picks the active one at level init
  through an `IGameModeSelector` chain (first non-null answer wins; the core's
  default selector answers `"null"`), exposes `Active`, and performs
  `SwitchTo(id)`: flush round state (`IRoundFlow.ResetRound`), keep players,
  run the new mode's `IModeSetup`, warmup (`sv_mode_switch_warmup`, default
  `mp_waitingforplayers_time`), then start the round (D6). Unknown id → log,
  unchanged. `GameRules`, `Players`, `SelectSpawnPoint` consult
  `GameModes.Active` and fall back to stock when a concern is `null`.
- `NullMode` (`Id = "null"`): today's deathmatch behaviour moved behind the
  interfaces, so the proxy is exercised by the test mod from day one.
- `game_mode_switch` map entity (`[ScriptedEntity]`, input `SwitchTo <id>`)
  and a dev `[ConCommand] sv_mode <id>` — generic names, in the core.
- `IRoundResettable { void RoundReset(); }` on `ScriptedEntity` (virtual
  no-op); `ResetRound` walks the registry (stock `CleanUpMap` respawns from
  the map string; we reset in place — one `docs/divergences.md` row).

**Make, part B — `PlayerComposites` (the generic per-player composite, D9/D10)**
- `IPlayerComponent { int Order; void Attach(EntityHandle player); void Tick(float now); void Detach(); }`
  and `IPlayerComponentSource { IEnumerable<IPlayerComponent> Create(EntityHandle player); }`
  (chain; sources from every mod, sorted by assembly name then `Order`).
- `PlayerComposites` service (`IClientLifecycle` + `IFrameLifecycle`):
  creates each player's components on connect, ticks them in a fixed order,
  detaches on disconnect; `T? Get<T>(EntityHandle)` so one mod's component
  can find another's by interface (Descent's component reads TF2's
  `ITfClassComponent`). TF2 registers one source (Stage 2.3); Descent later
  registers its own into the same composite.

**Make, part C — rules extension points consumed by `GameRules.cs`**
- `IRoundFlow` (from the active mode): `Think(float now)`, `RoundOver`,
  `ResetRound()`, `OnPlayerSpawned`, `OnPlayerKilled(PlayerDeath)`; `GameRules`
  delegates its frag/time-limit machine (`Think :360`, `EndRound :413`,
  `ResetRound :428`, `ScoreDeath :314`). `Intermission` stays core.
- `IDamageRule` (chain): `Decision Modify(ref DamageContext ctx)` run inside
  `GameRules.CanTakeDamage` after the stock decision; a context carries
  victim, attacker, inflictor, damage, type, hitgroup (0.8), position, force.
- `IRespawnPolicy` (from the active mode): `float Delay(EntityHandle)`, `bool WaveRelease(EntityHandle, float now)`;
  `Players.Think` (`Players.cs:273`) consults it, default `RespawnDelay` 3f.
- `ILoadoutProvider` (chain, last answer wins per slot): replaces the static
  HL2 `Loadout`/`StartingAmmo` arrays (`GameRules.cs:186,194`) when present.
- `IScoring` (from the active mode): team/player score writes to
  `PlayerResource` and the team score fields it needs (add team score to the
  resource netstate if absent).

**Facts**: `GameModes` with no mode registered → `NullMode` and every
existing `GameRules`/`Players` fact unchanged; two registered modes → the
selector chain picks deterministically and `SwitchTo` flushes once, keeps
player handles, runs warmup then the round; unknown id unchanged + log;
`game_mode_switch` input routes. `PlayerComposites`: two sources from two
fake assemblies attach in sorted order, tick in that order, detach on
disconnect (count differential), and `Get<T>` finds a component across
sources. Each rules hook: one fact with no owner and one with a fake owner
(delegation observed, stimulus non-zero). Structure fact (D10): no class in
`src/server/managed/Modes/` or `PlayerComposites` is `sealed` without an
extension interface — i.e. every behaviour-dispatching type has a hook.

**Gate**: `make managed-test FILTER='GameModes|PlayerComposites|GameRules|Players|Respawn'`;
`make test` green (the test mod gets `NullMode` and no components); live:
`sv_mode null` on `dm_lockdown` restarts the round after the warmup.

## Phase 0.8 — Hitgroup on bullet traces (serialise with other ABI work)

**Make**: `SurfaceTraceResult` (`EngineAreas.World.cs:51`) gains `Hitgroup`
(stock `trace_t.hitgroup`, `HITGROUP_GENERIC=0 … HITGROUP_GEAR=10`,
`game/shared/shareddefs.h`). Check whether the native trace used by
`TraceLineSurface` already runs with a mask/filter that hits studio hitboxes
(`MASK_SHOT`); if it hits only `MASK_SOLID`, add the mask argument rather than
a second call. `ServerWeaponArms.FireBullets` (`Arsenal.cs:1084`) passes it
into `IEngineDamage.ApplyAt` → `OnDamagedAt` → `DamageContext.Hitgroup`
(0.7). Struct layout is held by `AbiParityTests` only; update the parity
fixture.

**Facts**: parity test for the new layout; RecordingEngine fact that
`FireBullets` forwards the trace's hitgroup into the damage call; stock
`HITGROUP_*` constants pinned against `shareddefs.h` (oracle read of the
pristine clone, `src/test/oracle/`).

**Gate**: live on `dm_lockdown`: shoot a standing player in the head with
`tools/debug-api` aim help; the damage log shows `hitgroup=1`; body shows
`2`. Both values, not one.

## Phase 0.9 — Mod UI assemblies, VPK localisation, effect discovery

**Make** (owner: `src/client/ui/host/*`, `src/client/managed/ClientModule.cs` effect wiring)
- `game/mods.yaml`: a mod may list `assemblies.client_ui: [TF2.Client.Ui]`
  (defaults: empty). `src/client/managed/Ui/UiLoader.cs:52` loads the three core UI assemblies
  **plus** the mod's into the same collectible context (so `cs_ui_reload`
  still works). `ModsConfigTests` gets the new key.
- `ClientUi.Build` (`UiComposition.cs:35`) adds every `UiScreen` found in the
  loaded UI assemblies to the layer named by its `Layer`, constructed through
  the container (data interfaces by ctor injection, as `authoring.md` §1
  promises). Core screens keep their order; mod screens follow by `Order`.
  Mod data-source adapters live in the mod's *client* assembly as
  `[SideService(Side.Client, As = [typeof(IFooSource)])]`, so the UI assembly
  still references only `Client.Interface` + Toolkit (`UiAssemblyRulesTests`).
- Localisation: `UiLanguageFile.Parse(ReadOnlySpan<byte>)` (UTF-16 with BOM,
  the existing parser body) and a loader that reads
  `resource/<name>_english.txt` through `IShimEngine.FilesReadGameFile`
  (`docs/engineapi-areas.tsv:6`), so VPK-mounted files work. `GameUiLocaliser`
  chains: mod disk file → mod VPK files (list in mods.yaml
  `localisation: [resource/tf_english.txt]`) → engine. `IUiLocaliser.Find`
  unchanged.
- Effects: `IClientEffectHandler` implementations collected from the container
  (`[SideService(Side.Client, As=[typeof(IClientEffectHandler)])]`) in
  addition to the fixed list (`ClientModule.cs:939-948`); tempent
  `RegisterAll` (`:1026`) over `LoadedAssemblies.Types(...)` like the
  standalone registry does (`ClientModule.cs:862`).
- `tools/ui-preview-assemblies.txt` gains `TF2.Client.Ui=game/mod_tf2` when
  Stage 3 creates it (not now).

**Facts**
- A test-only UI assembly with one `UiScreen` (`src/test/client/Ui/`): after
  `ClientUi.Build` it is in the HUD layer when `Layer == Hud`, the menu layer
  otherwise; preview (`PreviewRun.AllSamples`) and game see the same list
  (compare names).
- `UiLanguageFile.Parse` on bytes equals `Load` on the same file written to
  disk; a token present only in the VPK file resolves through the chain
  (RecordingEngine answers `FilesReadGameFile`).
- A `[SideService]` effect handler is dispatched for its `EffectName`
  (`ClientEffectDispatch.cs:27` order: managed first).

**Gate**: `make ui-lint` 0 findings, `make managed-test FILTER='Ui'` green,
`cs_ui_reload` in a running game still reloads (`tools/debug-api log` shows
the reload line) with a mod UI assembly listed.

## Phase 0.10 — `MultiplayerAnimState` in the core (U5: full animation, generic first)

The user ruled full animation, not a stop-gap, and D10 puts the generic
machine in the core. This phase is independent of 0.1–0.9 and may trail
them: it depends on the bones lanes (`docs/plans/plan_bones_phase_bc.md`,
`bone_cpp_removal.md`) for pose-parameter and layer plumbing on the client;
read their current state first and name the exact members this phase needs.

**Stock**: `game/shared/Multiplayer/multiplayer_animstate.cpp` (+ `.h`,
`base_playeranimstate.cpp`): `Update(eyeYaw, eyePitch)`, `ComputePoseParam_MoveYaw`
/`_AimPitch`/`_AimYaw`, `ComputeSequences`, `ComputeMainSequence`,
`ComputeGestureSequence`, `DoAnimationEvent(PlayerAnimEvent_t)` (the 30-odd
`PLAYERANIMEVENT_*`), gesture slots (`GESTURE_SLOT_*`), `UpdateLayerSequenceGeneric`,
`m_bForceAimYaw`, `EstimateYaw`, `CalcMovementPlaybackRate`; the `TF_DLL`
arms (7 sites) stay out — TF2's layer adds them in Stage 3.5.

**Make** (`src/shared/managed/Anim/`, shared, both sides): `MultiplayerAnimState`
as a pure state machine over an `IAnimTarget` (pose parameter set, sequence
lookup, layer control — implemented on the server by the player entity's
engine arms and on the client by `ClientAnimationLayers`/`ClientPoseParameters`),
configured by `AnimStateParams` (the stock `MultiPlayerMovementData_t`),
with an **`IAnimStateLayer` chain** (D10) called at `ComputeMainSequence`,
`ComputeGestureSequence`, `DoAnimationEvent` so a mod layers class/weapon
rules without subclassing. The core's `PlayerAnimation.cs` posture mapping
becomes the HL2 layer. Server and client run the same code (prediction
of the local player's animation as stock does).

**Facts**: pose parameters for a scripted velocity/eye stream match a
recorded stock-derived table (`move_x/move_y/body_yaw/body_pitch`) — derive
the table by hand from the stock formulas, cite each; gesture slot playback
and interruption rules; `DoAnimationEvent(Jump)` → jump sequence then land;
a fake `IAnimStateLayer` overrides the main sequence and is consulted in
chain order; zero allocation per `Update` over 1,000 frames.

**Gate**: `make managed-test FILTER='AnimState'`; live on `dm_lockdown` the
HL2 players animate as before (screenshots before/after: run, crouch, jump).

---

## Stage 0 exit

- Every seam has a no-owner fact (behaviour unchanged) and an owner fact
  (delegation observed), and `make check` is green with `test` and
  `tf2_descent` registering nothing new.
- `make headless RUN_MOD_ID=tf2 MAP=ctf_2fort` loads TF2 content with the
  core game (HL2 weapons on a TF2 map — that is the expected, ugly state).
- Ratchet file for later stages: `docs/plans/tf2/seams.tsv` listing each hook
  interface, its owner rule (single/chain), the core file that dispatches it,
  and the fact that proves the no-owner arm. Stage 1+ agents consult it
  instead of re-deriving the core.
