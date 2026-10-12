# Plan: Port Team Fortress 2 to SourceSharp (C#)

Status: DRAFT v2 — awaiting user review. Uncommitted per CLAUDE.md.
Dates: v1 2026-10-10; v2 2026-10-11 (rewritten after measuring the core at
main `375193a4c` and the fork at `sdk-root.lock`'s pin). Scope: base TF2
(`src/tf2`, `game/mod_tf2`, Steam appid 243750). **tf2_descent is out of
scope** (user ruling 2026-10-10), though it loads on top of TF2
(`game/mods.yaml`: its assemblies list `TF2.Server`/`TF2.Client` first).

This file is the index: rulings, corrected ground truth, how TF2 attaches
to the core, the stage map, the per-task contract and the agent brief
template. The work itself is in **`docs/plans/tf2/stage{0..5}_*.md`**, one
file per stage, each written so an agent with no conversation context can
execute it phase by phase (`exec-plan` shape: phases in order, a gate after
each, a commit per phase). `plan_tf2_port_tasks.md` (v1) was deleted on the
user's ruling U7 (2026-10-11).

## 0. User rulings (2026-10-10/11; unchanged in v2)

- **D1 UI: YES** — TF2 HUD/menus re-implemented in the `src/client/ui` C# toolkit; no stock vgui.
- **D2 Economy: as much as can work without Steam** — schema, attributes, wearables, local
  inventory, loadout. No GC/Steam inventory/matchmaking/store/trading (divergence rows).
- **D3 Bots: use the new AI plan** — Appendix B of `~/git/sharp-fresh/docs/plans/plan_tf2_rpg.md`
  ("the robot AI: off the main thread, fed by events, answering with commands"): `TF2.Ai` with
  **no `SourceSharp.Abi` reference** (build fact), main thread publishes events (SPSC ring,
  never dropped) + a latest-wins snapshot, brains keep per-bot blackboards, decide on
  behaviour trees, path via flow fields with clearance classes, output intent commands
  (`MoveCommand`, `expiresTick` ~250 ms) applied+validated on the main thread;
  `IAiTransport` threaded + inline (`tf2_ai_sync 1`); record/replay determinism gates.
  NextBot is ported as *vocabulary*, not its threading. **That file is not in this repo's
  history** (sharp-fresh is a sibling clone with the plan uncommitted), so
  `stage5_bots.md` §5.0 restates Appendix B for agents working here.
- **D4 Milestone modes: Payload + CTF** (`pl_badwater`, `ctf_2fort`); other modes later.
- **D5 Dead-code list: remove** (duel minigame, training annotations, bot_npc, replay,
  STAGING_ONLY, dead `TF2`/`TF2_DLL` arms, workshop import UI, sixense, saxxy) — all get
  `docs/divergences.md` rows.
- **D6 Game mode system: map-switchable on the fly.** Modes are selected by a proxy via
  dependency injection; a map entity can change the mode at runtime. **On switch the round
  restarts after a warmup** (2026-10-11); no live state handover.
- **D7 Payload cart: stock mechanics** (2026-10-11) — `path_track`s + `team_train_watcher`
  ported faithfully, no spline shortcut.
- **D8 TF2 movement: plugin/extension over base movement, NOT inheritance.**
- **D9 TFPlayer: composed feature classes, not one god class.**
- **D10 Composition layering** (2026-10-11): **TF2's managed files are extensions of
  core services and entities by composition, and they must themselves be extensible
  the same way by further mods such as tf2_descent.** Three layers, one rule: a layer
  adds hooks, components and registrations to the layer below; it never subclasses or
  replaces it. So the generic machinery (mode proxy, player composite, weapon
  behaviour seam, HUD slots) lives in the core; TF2 registers modes, components,
  behaviours and panels into it; and every TF2 service exposes its own hook/component
  interfaces so Descent can register into TF2 as TF2 registers into the core.

## 1. Ground truth (measured 2026-10-11; v1's numbers corrected)

**Stock (fork, read-only, `../source-sdk-2013/src/game/`).** Counted with one
Python pass over `*.cpp`/`*.h` (per-file tables are in the stage files where used):

| Directory | .cpp | lines | .h | lines | Notes |
|---|---|---|---|---|---|
| `shared/tf` | 169 | 149,616 | 152 | 26,472 | 67 `tf_weapon_*.cpp` = 45,185 lines; `tf_gamerules.cpp` 22,489 (322 methods); `tf_player_shared.cpp` 14,813 (397); `tf_weaponbase.cpp` 7,026; `tf_gamemovement.cpp` 3,493; `entity_capture_flag.cpp` 3,064 (**the flag logic**; v1 placed it in `tf_mapitems.cpp`) |
| `server/tf` (top) | 84 | 72,069 | 78 | 10,453 | `tf_player.cpp` 23,131 (412 methods); `tf_obj*.cpp` 9,735; `tf_mapitems.cpp` 3,127 |
| `server/tf/bot` | 6 (+134 in `behavior/`, 20 in `map_entities/`) | 30,833 total | | | behaviour library 20,439 lines |
| `server/tf/nav_mesh` | 5 | 3,952 | 4 | 667 | |
| `server/tf/{halloween,player_vs_environment,bot_npc,raid,workshop,minigames}` | 84+48+12+2+2+2 files | 39,290 | | | post-milestone or D5 |
| `client/tf` (top) | 147 | 90,738 | 104 | 12,223 | 73 `tf_hud_*.cpp` = 46,701 lines |
| `client/tf/vgui` | 102 | 64,127 | 95 | 9,679 | D1: layout/behaviour only |
| `shared/econ` | 24 | 29,013 | 30 | 10,957 | `econ_item_schema.cpp` **7,192** (v1 said 2,680 — that is `tf_item_schema.cpp`) |
| `client/econ` | 18 | 19,106 | 19 | 2,866 | store/backpack UI: D2 drop |
| **Not TF2 but required and not in managed:** `shared/teamplayroundbased_gamerules.cpp` 3,781, `teamplay_round_timer.cpp` 1,543, `server/team_control_point{,_master,_round}.cpp` 2,851, `trigger_area_capture.cpp` 1,247, `team_train_watcher.cpp` 1,572, `team_objectiveresource.cpp` 573 | | **13,571** | | | none is in `sdk-links/*.txt`; Stage 2 ports them |

**The `TF2`/`TF_DLL` hash-define findings (the original ask).** There is no live
`TF2`/`TF2_DLL`/`TF2_CLIENT_DLL` define: `h_cycler.cpp:108` (`TF2_DLL`, defined
nowhere), `econ_item_inventory.cpp:1741` (`TF2` + appid 520, legacy), and the
commented sample in `serverplugin_empty.cpp:32`. The live arms are `TF_DLL` /
`TF_CLIENT_DLL`: **459 sites in 118 files outside `tf/` and `econ/`**
(`docs/plans/tf2/tfdll_sites.tsv`, generated 2026-10-11). The ones that are
logic to port, by consumer: `teamplayroundbased_gamerules.cpp` 36 +
`teamplay_round_timer.cpp` 19 + `multiplay_gamerules.cpp` 9 +
`team_control_point.cpp` 7 (Stage 2.1/2.4); `basecombatweapon_shared.cpp` 12
(crit bucket, Stage 1.9); `baseentity.cpp` 7 + `baseentity_shared.cpp` 3 +
`baseprojectile.cpp` 7 + `player.cpp` 4 + `item_world.cpp` 9 (Stage 2.3/2.7/2.8);
`multiplayer_animstate.cpp` 7 + `c_baseanimating.cpp` 6 + `c_baseviewmodel.cpp` 6
(Stage 3.5); `cdll_client_int.cpp` 17 + `clientmode_shared.cpp` 11 +
`viewrender.cpp` 11 + `c_baseentity.cpp` 9 (Stage 3.3/3.6); `hud_vote.cpp` 46 +
`vote_controller.cpp` 10 (votes: deferred); `steamshare.cpp` 12, `replay/*`,
`sixense/*` (D5). Under `SOURCESDK` the build prunes GC matchmaking
(`DISABLE_GC_CONNECTION`) and uses `INVENTORY_VIA_WEBAPI`; `REPLAY_ENABLED` is
never defined; `STAGING_ONLY` is dev-only. `BuildGroup.cpp:919`'s `-game tf`
tolerance is irrelevant (no stock vgui).

**TF2 content.** Mounted from the local Steam install: `bin/<cfg>/content ->
…/Team Fortress 2` (`tools/setup find_tf2()`, `tools/setup:286`);
`game/mod_tf2/gameinfo.txt` mounts `tf2_misc`, `tf2_textures`,
`tf2_sound_misc`, `tf2_sound_vo_english` VPKs + `../test`. Class stats,
weapon scripts, `items_game.txt`, HUD `.res` layouts and `tf_english.txt` are
**inside the VPKs**; no VPK tool is on the box (Stage 0.2 adds
`tools/vpk-extract`). Managed code reads game files through
`IShimEngine.FilesReadGameFile` (`docs/engineapi-areas.tsv:5-6`;
`WeaponScriptDatabase.cs` is the model) and parses KeyValues with
`src/shared/managed/KeyValuesText.cs`.

**This repo today.** `src/tf2/{server,client}/TF2.*.csproj` exist with **zero
`.cs` files**; `game/mods.yaml` already lists the `tf2` mod and
`tf2_descent` on top of it; `ModsConfigTests.cs:370-378` pins that. The
Descent mod (`src/tf2_descent/`) is the worked example of every mod
mechanism (registration, hooks, messages, entity, UI project, test project).

## 2. How TF2 attaches to the core (the architecture v1 lacked)

Verified at `375193a4c` and binding for every stage:

1. **A mod adds services; it never replaces one** (`SideServices.cs:183-188`).
   So `IGameRules`, `GameMovement`, `WeaponLogic`, `Arsenal`, the UI host
   stay core, and TF2 owns **hooks** the core dispatches: single-owner hooks
   (`IMovementRules`, `IRespawnPolicy`, `ILoadoutProvider`, `IScoring`,
   `IJoinTeamPolicy`, `ISpawnSelector`) and chains (`IDamageRule`,
   `IClientCommandHook`, `IWeaponTypeSource`, `IWeaponBehaviourSource`,
   `IAmmoTypeSource`, `IPlayerComponentSource`). Zero owners = stock; two
   owners of a single-owner hook = startup error. Stage 0 adds them; the
   ratchet `docs/plans/tf2/seams.tsv` lists them.
   **Game modes are a core service** (U1): `GameModes` in `src/server/managed`
   holds `IGameMode` registrations (`[SideService(As=[typeof(IGameMode)])]`),
   resolves the active one, and performs the switch-with-warmup (D6); the
   core ships a `NullMode` (today's deathmatch rules). TF2 registers `CtfMode`
   and `PayloadMode`; Descent can register its own or decorate TF2's.
   **Composition layering (D10)**: every TF2 service that owns behaviour
   exposes a hook or component interface of its own (`ITfPlayerComponent`,
   `ITfWeaponHook`, `ITfDamageHook`, `ITfModeHook`, HUD slots on `TfHud`), so
   a further mod extends TF2 exactly as TF2 extends the core. A structure
   fact per stage checks that no TF2 class that dispatches behaviour is
   `sealed` without such an interface.
2. **Registration is attribute scan**: `[SideService(Side.X, As=[…])]` and
   `ISideServiceRegistrar.Register(ISideServiceRegistry)` (`SideServices.cs:24-66`),
   picked up at `ServerModule.cs:1382` / `ClientModule.cs:793`. Entities
   (`[ScriptedEntity]`), client mirrors (`[ClientStandaloneEntity]`),
   commands (`[ConCommand]`), convars (`[ConVar]`) and user messages
   (`[UserMessage]`, key `TF2:<id>`) are already discovered across loaded
   mod assemblies. Shared TF2 source compiles into both `TF2.Server` and
   `TF2.Client` (Descent's `<Compile Include="..\shared\**\*.cs">`); there is
   no `TF2.Shared` assembly.
3. **Predicted state decides**: anything movement or weapons read on both
   sides (class, conditions, air-dash count, cloak meter) lives on the
   per-player carrier `tf_player_data` (`[StandaloneNetState]` with
   `[Predicted]` fields), never in a server-only service (U3). The core
   player entity `SsPlayer` is not subclassed; the core gains a generic
   per-player composite (`PlayerComposites` + `IPlayerComponentSource`,
   Stage 0.7) and `TfPlayer` is TF2's set of components in it (D9); Descent
   adds components to the same composite (D10).
4. **Weapons are behaviours, not classes** (U2): a TF weapon is a `WeaponType`
   row (from the stock script + schema) plus an `IWeaponBehaviour` that
   `WeaponLogic` calls at its decision points; the client predicts with the
   same behaviour. Crit rolls use the prediction seed (stock does too). TF
   behaviours are themselves composed: `TfWeaponBehaviour` runs an
   `ITfWeaponHook` chain (attribute modifiers, crit policy, Descent's
   item effects) at the same points, so a further mod never subclasses a
   TF weapon either.
5. **Native carries out, never decides**: the only new ABI calls are
   `SetTeam` (Stage 0.5) and the trace `Hitgroup` field (0.8); both are
   generic. Everything else is managed.
6. **No statics** (SS0001/SS0010 — mods get no waivers): catalogues and
   tables are injected services built at startup.
7. **UI**: TF2 screens live in `TF2.Client.Ui` (toolkit + `Client.Interface`
   only), are loaded with the core UI assemblies (Stage 0.9) and read data
   through `ITf…Source` interfaces implemented by adapters in `TF2.Client`.

## 3. Stage map

| Stage | File | Phases | Depends on | Parallel lanes |
|---|---|---|---|---|
| 0 Core seams | `tf2/stage0_core_seams.md` | 0.1 projects+test leg · 0.2 launch+vpk tool · 0.3 movement hook · 0.4 weapon seam+catalogues · 0.5 teams (ABI) · 0.6 client-command hook · 0.7 game-mode proxy, player composite, rules hooks · 0.8 hitgroup (ABI) · 0.9 mod UI/localisation/effects · 0.10 MultiplayerAnimState (core, shared; may trail the bones lanes) | — | 0.1, 0.2, 0.3, 0.4, 0.9, 0.10 independent; 0.5→0.6→0.7; 0.8 serialised with 0.5 (ABI tail) |
| 1 Shared foundation | `tf2/stage1_shared_foundation.md` | 1.1 enums (oracle) · 1.2 class data · 1.3 convars · 1.4 schema · 1.5 attribute hooks · 1.6 inventory/loadout · 1.7 conditions · 1.8 movement rules · 1.9 weapon base + milestone weapons | 0 | {1.1,1.2,1.3} · {1.4,1.5,1.6} · {1.7,1.8,1.9} |
| 2 Server core | `tf2/stage2_server_core.md` | 2.1 team-round rules · 2.2 TF2 modes into the core proxy · 2.3 TfPlayer components+carrier · 2.4 map entities+CP+resources · 2.5 CTF · 2.6 Payload · 2.7 damage · 2.8 projectiles · 2.9 medigun · 2.10 buildings · 2.11 flames · 2.12 spy · 2.13 wearables | 1 | {2.1,2.2} · 2.3 · 2.4 first; then 2.5/2.6; 2.7–2.13 by directory |
| 3 Client | `tf2/stage3_client.md` | 3.0 wiring · 3.1 HUD panels · 3.2 menus · 3.3 mirrors · 3.4 prediction proof · 3.5 TF2 animation layer (full, over 0.10) · 3.6 effects | 0.9, 0.10, 2 carriers | 3.3 before 3.1 adapters; 3.4–3.6 independent; 3.5 waits for 0.10 |
| 4 Milestone | `tf2/stage4_milestone.md` | 4.1 targets · 4.2 `tools/tf2-gate` · 4.3 daily-gate leg · 4.4 human acceptance | 3 | one lane |
| 5 Bots and after | `tf2/stage5_bots.md` | brief for its own plan (Appendix B restated, stock sources, build order, gates) | 4 | — |

Stage exits are stated at the end of each file; Stage 2's exit is a
playable server with the HL2 HUD, Stage 3's a playable client, Stage 4's the
D4 milestone.

## 4. Per-task contract (every phase in every stage)

- **Base**: the brief names a base sha; the agent's first command is
  `git log --oneline -1` to confirm it. Work in a dedicated worktree cut from
  main (`make setup` there; never in the main tree). Commit after every
  phase; stage explicit paths; never push.
- **Stock is read-only**: `../source-sdk-2013` is never written; after any
  build/test `git -C ../source-sdk-2013 status --porcelain` is empty. A
  needed fork change stops the phase and goes to the user.
- **Where code lands**: `src/tf2/shared` (both sides), `src/tf2/server`,
  `src/tf2/client`, `src/tf2/ui`, `src/tf2/tests`; core seams in
  `src/{shared,server,client}/…` only in Stage 0 or when a stage file says
  "add to the core". Shared infra edits get their own commit and facts.
- **Attribution**: a file that ports Valve logic starts with
  `tools/valve-header.txt` byte for byte and gets a `docs/valve-header-new.tsv`
  row (`path<TAB>ports <Method> (<file>)`); original work and behaviour-only
  tests get none. Every deliberate omission is a `docs/divergences.md` row
  (`| Port | Divergence | Stock file:line | Note |`). Deferred items are rows
  in `docs/plans/tf2/deferred.tsv`. Small follow-ups go to `docs/todo.md`.
- **Tests**: xUnit `[Fact]`, one behaviour per fact, in `src/tf2/tests`
  (run `make tf2-test FILTER=…`; `make managed-test` runs the core projects
  only). Every bug fix lands with a test that fails before it. A check must
  be able to fail: stimulus non-zero, before ≠ after, missing input = failure.
  Oracle facts read the pristine clone (`src/test/oracle/` conventions),
  never the farm. Fixtures from the VPK are committed trimmed, with a README
  naming source path and date.
- **Build**: `make debug` compiles with zero warnings; a managed change is
  deployed by `dotnet build` (check the HUD stamp or `tools/debug-api status`
  before trusting a live reading). Between commits run the touched facts;
  `make check` only before merging to main.
- **Live checks**: `make headless RUN_MOD_ID=tf2 MAP=…` with `tools/debug-api`;
  own `TMPDIR`/`DEBUG_API_*`/`-port` per game; kill by PID after `ps -o args=`;
  a datatable change is proven only by a full join (write on the server,
  read on the client). Quote every number with its command and sha.
- **Difficulty** (per phase, in the stage files): E ≤ a day, M ≤ a week,
  H open-ended.

## 5. Agent brief template (copy into the lane brief; fill the brackets)

```
Base: main <sha> (check with `git log --oneline -1`). Worktree: ../ss-tf2-<lane>.
Plan: docs/plans/plan_tf2_port.md (§2, §4) and docs/plans/tf2/stage<N>_*.md, phases <a>–<b>.
You own: <directories/files>. Do not touch: <other lanes' files>; never ../source-sdk-2013.
Read first (stock, read-only): <file:line list from the phase>.
Read first (ours): docs/plans/tf2/seams.tsv, <core files the phase cites>.
Deliver per phase: code + facts + valve-header rows + divergence/deferred rows, one commit,
message "tf2 <stage.phase>: <what>" ending with the gate's command and its numbers.
Gate per phase: <the phase's Gate paragraph>. Stop only for: a needed fork edit, an
ambiguity that changes what gets built (name it and propose a default), or a destructive/
outward action. "No code needed" is an acceptable finding if measured.
Report: relay the measured numbers; the user does not see your transcript.
```

## 6. Decisions (all resolved)

v1: D1–D9 (§0); Q3 AI plan = Appendix B; Q4 mode switch = restart after
warmup; Q5 cart = stock mechanics.

v2, ruled by the user 2026-10-11:

- **U1 Mode proxy: generic, in the core** (`GameModes` service, Stage 0.7);
  TF2 registers modes into it (Stage 2.2). Rejected: a TF2-owned proxy.
- **U2 Weapons: the behaviour seam** on the sealed `WeaponLogic` with
  mod-extensible catalogues (Stage 0.4), TF behaviours composed from an
  `ITfWeaponHook` chain (Stage 1.9).
- **U3 Player state: a per-player `tf_player_data` entity** with a predicted
  netstate (Stage 2.3), components in the core's generic player composite.
- **U4 Spy: full** disguise + cloak + backstab in the milestone (Stage 2.12).
- **U5 Animation: full** `MultiplayerAnimState` port — generic in the core
  (Stage 0.10, shared, both sides) with the TF2 class/weapon layer composed
  on top (Stage 3.5). No Tier A stop-gap ships as the milestone.
- **U6 Stage 0 lands on main alone before Stage 1.**
- **U7 `plan_tf2_port_tasks.md` deleted.**
- **D10 (§0)** is the general form of U1–U5: core → TF2 → further mods, each
  layer extended by composition, never by subclassing or replacement.

## 7. Open questions

None.
