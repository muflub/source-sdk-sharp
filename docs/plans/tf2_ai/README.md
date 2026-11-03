# TF2 AI prework: stages a local model can implement

Status: started 2026-11-02 (user: "checked in ... for you to start working on as
well"), first wave F01, F09, F12, F17; committed earlier as "commit plan but dont start",
with `docs/plans/plan_bots_teams_tf2.md` (Part F points here). Written at main `dae055998` (2026-11-02); every stage names
that base and re-greps its citations. User ruling 2026-11-02: "prework to add
ai tf2 should be broken out as stages that local models can impl".

**What prework is:** everything a TF2 bot depends on that is not its brain:
the mod's gaps (collision, spawn rooms, roles, race rules, arrow lighting,
items), the read-only facts a bot will perceive (class, conditions,
disguise, objectives, round state), the gates (KOTH, 5CP), the stale docs,
and the item audit with its port stages. The brains (P6–P8 in the main plan)
are not here.

**How a lane gets its brief:** `cat COMMON.md F<nn>_<name>.md > brief.md`,
launched with the gateway launcher (`gw-run5.sh <worktree> brief.md <log>`),
one fresh session per stage, never resumed; a correction is a new session
with a new worktree (memory `fresh-lane-sessions`). Keep GLM to the stages
marked GLM-able (one or two commits); Qwen for the rest; the ones marked
"stronger model" are not for lanes (reasons in each file).

**Worktree convention:** `../ss-tf2ai-F<nn>` on branch
`tf2ai-F<nn>`, cut from `main` at the base sha; `make setup` inside it.
Landing: rebase onto main, `make check`, fast-forward; the ABI tail, the
ledgers and `docs/plans/tf2/deferred.tsv` are touched by one lane at a time
(the table below says who owns what).

## What changed from the house format (and why)

The TF2 stage files (`docs/plans/tf2/stage*.md`) are phase lists for an
integrator to cut briefs from. These are the briefs: one file is one
session's whole task, with the base check, the owned files, the stock lines
already read (lanes have ported a wrong premise three times when a row
misstated stock: memory `deferred-row-premises`), the type and fact names
(no design left to the lane), the exact commands, a fact the lane must see
red first, and a command-readable proof for anything live (a JPEG
description is not evidence: memory `lane-jpeg-claims-unreliable`). The
common part is in `COMMON.md` so a stage file stays short (small briefs
finish; memory `glm-text-tool-calls`).

## The stages

| id | stage | size | fit | depends on | owns (may touch) |
|---|---|---|---|---|---|
| F01 | stale rows: `divergences.md:40`, `deferred.tsv:3,11,71`, `TFAutobalance.cs:32-35` | S (1 commit) | GLM or Qwen | — | `docs/divergences.md` (row 40 text), `docs/plans/tf2/deferred.tsv` (rows 3, 11, 40, 71), `src/tf2/server/Rules/TFAutobalance.cs` (comment) |
| F02 | TF map nav spike (`ssmap nav3d` on ctf_2fort, cp_dustbowl, koth_viaduct, pl_badwater) | S | **stronger model / coordinator** | — | none in the repo (a report) |
| F03 | bot arms in the mod: `CanBeAutobalanced`'s bot refusal, `mp_humans_must_join_team`'s bot arm | S (2 commits) | GLM or Qwen | — | `TFAutobalance.cs`, `TFJoinTeamCommand.cs`, `src/tf2/tests/Rules/TFAutobalanceTests.cs`, `TFJoinTeamPanelTests.cs` |
| F04 | the per-entity collide rule (`IEntityCollideRule`) in the server trace filter and the client's prediction | M | **stronger model** | — | `src/server/interop/TraceFilterRules.cs`, `src/shared/interface`, client prediction, `docs/plans/tf2/seams.tsv` |
| F05 | TF2 teammates pass through + push-away + spawn rules | M (3 commits) | Qwen | F04 | `src/tf2/shared/Movement/`, `src/tf2/server/Player/TFTeamCollide.cs` (new), `src/tf2/tests/TFMovementTests.cs`, `src/tf2/tests/Player/` |
| F06 | the per-client transmit rule (`ITransmitRule`) | M | **stronger model** | — | core transmit path, `seams.tsv` |
| F07 | the respawn-room visualizer: solid to the other team, sent only to it | M (2 commits) | Qwen | F04, F06 | `src/tf2/server/Entities/Rooms/FuncRespawnRoomVisualizer.cs`, `src/tf2/tests/Entities/Rooms/` |
| F08 | a body keeps the team and class it died on (TF2 proxies read the body's own) | M (2 commits) | Qwen | — | `src/tf2/client/Proxies/TFMaterialProxies.cs`, `src/tf2/server/Player/TFBurnRagdoll.cs`, `src/tf2/shared/Player/`, `src/tf2/tests/Player/` |
| F09 | `ITFRoundFacts`: setup, round state, waves, time left, read-only | S (1–2 commits) | GLM or Qwen | — | `src/tf2/server/Rules/TFRoundFacts.cs` (new), `src/tf2/tests/Rules/TFRoundFactsTests.cs` (new) |
| F10 | team roles (attackers/defenders): proxy inputs, `GetAutoTeam`'s arm, read-only | M (3 commits) | Qwen | — | `src/tf2/server/Rules/TFTeamRoles.cs` (new), `TFGameRulesProxy.cs`, `TFTeams.cs`, `CPMode.cs`, `src/tf2/tests/Rules/` |
| F11 | Payload Race rules (`tf_logic_multiple_escort`) | M (3 commits) | Qwen | — | `src/tf2/server/Modes/PayloadMode.cs`, `src/tf2/server/Entities/Payload/`, `src/tf2/tests/Payload/` |
| F12 | `ITFActorFacts`: class, conditions, disguise, `DisguiseTargetIndex`, cloak fraction, `AirDash`, by player index | S (1–2 commits) | GLM or Qwen | — | `src/tf2/server/Player/TFActorFacts.cs` (new), `src/tf2/tests/Player/TFActorFactsTests.cs` (new) |
| F13 | `ITFObjectives` + the CTF and Payload feeds, with `Progress` and `LastProgressTick` | M (3 commits) | Qwen | — | `src/tf2/server/Objectives/` (new dir), `src/tf2/tests/Objectives/` (new) |
| F14 | the CP and KOTH feeds | M (2 commits) | Qwen | F13 | `src/tf2/server/Objectives/TFObjectivesCp.cs`, `TFObjectivesKoth.cs` (new), tests |
| F15 | live gate step `koth` on koth_viaduct | M (2 commits) | Qwen | — | `tools/tf2-gate` (new `step_koth` only) |
| F16 | live gate step `cp5` on cp_badlands | M (2 commits) | Qwen | — | `tools/tf2-gate` (new `step_cp5` only); serialise with F15 |
| F17 | the item audit tool: `tools/tf2-items-audit` writes `game/mod_tf2/scripts/items/items_working.tsv` | S (2 commits) | Qwen | — | `tools/tf2-items-audit` (new), the tsv (new), `src/tf2/tests/ItemsWorkingTableTests.cs` (new) |
| F18a/b/c | the audit fill, by class group (a: Scout/Soldier/Pyro; b: Demoman/Heavy/Engineer; c: Medic/Sniper/Spy) | M each | Qwen | F17 | the tsv's rows for its classes only |
| F19 | the bots' item source behind `TFLoadoutProvider` (every working class item) | M (2 commits) | Qwen | F17 (F18 for real rows) | `src/tf2/server/Econ/BotInventory.cs` (new), `TFLoadoutProvider.cs`, `src/tf2/tests/TFLoadoutServerTests.cs` |
| F20 | the Huntsman arrow lit by a flamethrower | S–M (2 commits) | Qwen | — | `src/tf2/server/Projectiles/`, `src/tf2/shared/Weapons/`, `src/tf2/tests/Projectiles/` |
| F21–F32 | item families the sub-classes need (one file each; see below) | S–M each | Qwen | F17 | the files each names; its tsv rows |
| F33 | the long tail: the remaining families, generated from the audit | 40–60 sessions | Qwen | F18 | per generated stage |
| F37 | wearables drawn on the client (hats) | L | **stronger model** | — | `src/tf2/client/` |
| F38 | quality names (Strange, Unusual, Australium) in item names on the scoreboard and target ID | S | Qwen | — | `src/tf2/shared/Econ/TFItemStrings.cs`, `src/tf2/ui/screens/TFScoreboard.cs` text path, tests |
| F40 | `IGameMode.Teamplay` and `JoinPolicy` default members with no-owner facts | S (2 commits) | Qwen | — | `src/server/managed/Modes/GameModeContracts.cs`, `Teams.cs`, `HostSay.cs`, `src/test/server/` |

Sizes: S = one session, one or two commits; M = one session, two or three
commits; L = more than a session. Stock citations in each file were read in
this session at the clone's `valve-sdk.lock` (`22288b91`).

## Dependency graph and parallel lanes

```
F01  F02*  F03  F09  F12  F15  F16  F17  F20  F38  F40      (no dependencies: start at once)
F04* ──► F05 ──► F07 ◄── F06*
F13 ──► F14
F17 ──► F18a F18b F18c ──► F19 ──► (P6's bots page)
F17 ──► F21 … F32 (each independent of the others)
F18 ──► F33 (generated)
F08 (independent)   F10 (independent)   F11 (independent)   F37* (independent)
                                   * = not for local models
```

Six lanes at once, by file ownership, a first wave: F01 (GLM), F03 (GLM),
F09 (GLM), F12 (Qwen), F13 (Qwen), F17 (Qwen). Second wave as slots free:
F10, F11, F20, F38, F40, F15; then F18a–c, F14, F16, F21+. F05 and F07 wait
for the stronger-model seams F04 and F06. Shared files and who waits:

| shared | owner at a time |
|---|---|
| `docs/plans/tf2/deferred.tsv` | F01 first; a port stage deletes only its own rows and rebases over others |
| `docs/divergences.md`, `docs/valve-header-new.tsv` | append-only; conflicts are line-adjacent and the integrator keeps both |
| `tools/tf2-gate` | F15, then F16 (one at a time) |
| `game/mod_tf2/scripts/items/items_working.tsv` | F17 creates; F18a/b/c edit disjoint class rows; a port stage flips its own rows |
| `TFLoadoutProvider.cs` | F19 only |
| `TFJoinTeamCommand.cs`, `TFTeams.cs` | F03, then F10 |
| `PayloadMode.cs` | F11 only |
| the ABI tail (`docs/engineapi-areas.tsv`, forwarders) | none of these stages adds a row; a stage that finds it must is reported, not done |
| `tools/ai-mutations` | not touched by prework (its arms are the AI's); the item and seam facts carry their own red-first arm instead |
| `docs/plans/plan_bots_teams_tf2.md`, this directory | the coordinator only |

## Not for local models, and why

- **F02 nav spike:** needs `dotnet run --project` in map-tools, outside the
  launcher's allow-list, and the finding (LZMA lumps? prop lump version?
  height cap?) changes what the next stages are. A coordinator runs it
  (`tools/ai-nav-build`'s command with `-game game/mod_tf2`), and a map-tools
  lane follows if it fails.
- **F04 collide rule, F06 transmit rule:** core seams across the server
  interop (`TraceFilterRules`), the client's prediction (no managed trace
  filter exists on the client today: `grep -rl ShouldHitEntity src/client`
  is empty, so the client half is a design question), `seams.tsv` with
  `SeamsLedgerTests`, and a live prediction check (misprediction counts,
  memory `tf2-carrier-pitfalls`). Judgement across systems and a debugging
  loop: a stronger model.
- **F37 wearables on the client:** bone-merged rendering with visual proof;
  lanes have misdescribed images (memory `lane-jpeg-claims-unreliable`).

## The item port stages (F21–F32), ordered by the sub-classes that need them

| id | family (attribute classes, stock read site) | sub-classes unblocked | size |
|---|---|---|---|
| F21 | passive items: `provide_on_active` (`tf_weaponbase.cpp:1374`), `rocket_jump_dmg_reduction` (`tf_gamerules.cpp:7456`), the Razorback's backstab block | Soldier Roamer (Gunboats), Sniper Guarded | M |
| F22 | deploy time: `mult_switch_from_wep_deploy_time`, `mult_single_wep_deploy_time` (`tf_weaponbase.cpp:1262`) | Pyro Combo (Degreaser) | S |
| F23 | item meters: `item_meter_charge_rate` (`tf_gamerules.cpp:21901` names the hook), `kill_refills_meter`, the meter on the HUD | Scout Bonk, Pyro Combo, banners | M |
| F24 | scattergun mods: `set_scattergun_no_reload_single`, `mult_bullets_per_shot` (`tf_weapon_shotgun.cpp:302`, `tf_fx_shared.cpp:305`) | Scout Harasser (Force-a-Nature) | S |
| F25 | stickies: `add_max_pipebombs` (`tf_weapon_pipebomblauncher.cpp:382`), `set_detonate_mode` (`tf_weapon_grenadelauncher.cpp:484`), `sticky_arm_time` (`tf_weapon_grenade_pipebomb.cpp:1190`) | Demoman Trapper | M |
| F26 | buildings and sappers: `mult_dmg_vs_buildings` (`tf_obj.cpp:1927`), `set_dmg_apply_to_sapper` (`tf_weaponbase_melee.cpp:473`) | Pyro Airblast guard (Homewrecker) | S |
| F27 | sniper mods: `sniper_only_fire_zoomed` (`tf_weapon_sniperrifle.cpp:711`), the tracer and penetrate rows | Sniper Marksman variants | S |
| F28 | cloak rates: `mult_cloak_meter_consume_rate` (`tf_weapon_invis.cpp:327`), the regen rate | Spy Infiltrator (Cloak and Dagger) | S |
| F29 | medigun charge types: `set_charge_type` (`tf_weapon_medigun.cpp:821`), `add_uber_time` (`:1442`) | Medic Kritz, Quick-Fix | M |
| F30 | the mini-sentry: `wrench_builds_minisentry` (`tf_weapon_wrench.cpp:255`) | Engineer Gunslinger | M |
| F31 | banners: `set_buff_type` (`tf_weapon_buff_item.cpp:183`), rage build and the buff conditions | Soldier Banner | M–L |
| F32 | flare and extinguish: `extinguish_restores_health` (`tf_weapon_flaregun.cpp:558`), `mult_projectile_speed` (`tf_weaponbase_rocket.cpp:275`) | Pyro Combo, Soldier variants | S |

Deliberately not staged here, with the reason: swords and shields and the
charge (`is_a_sword` is commented out in stock `tf_weapon_sword.cpp:286`;
`deferred.tsv:17,20,22` hold the arms; a shield charge is movement, a
stronger-model stage), the Dead Ringer's feign (a death fake across both
sides; its own plan), the Wrangler (`tf_weapon_laser_pointer.cpp`, sentry
control across player and building: M–L, after F30), the Eureka Effect
(`teleport_instead_of_die` is not it; the teleport goes through a HUD menu,
`tf_weapon_wrench.cpp:229`: client UI + server, a later stage), the Sandman's
stun (a condition with a HUD and movement arms), Natascha's slow (the
attribute name was not found in stock by the grep this session: unverified).

**Counts** (from the schema parse named in the main plan, C.3.1): 498
class-equippable weapon-slot items, 179 distinct behaviours, 279 behaviour
attribute classes, 212 of them named nowhere in `src/tf2`. F21–F32 cover
about 22 behaviours; F33's long tail is about 110 behaviours over about 200
attribute classes, grouped by family into 40–60 lane sessions. At the
current rate (Qwen finishes most briefs; GLM about two in three) that is
three to six months of six lanes, and no bot waits on it: a bot draws only
`works` rows.
