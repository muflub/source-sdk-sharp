# TF2 port — Stage 5: bots on the event-fed AI architecture (D3), and what follows

Status: DRAFT, uncommitted. Part of `docs/plans/plan_tf2_port.md` (§0 D3,
D5). Starts only after the Stage 4 milestone. This file is the **brief for a
separate plan**, not a phase list to execute: it fixes the architecture,
the stock sources, the build order and the gates so that plan can be
written by an agent from this repo alone.

## 5.0 Where the architecture is written down (and why it is copied here)

The user's ruling D3 points at Appendix B of `plan_tf2_rpg.md` in
`~/git/sharp-fresh/docs/plans/` — a sibling clone of this repo whose plan
file is **uncommitted** and not in this repo's history (checked 2026-10-11:
`git log --all -- docs/plans/plan_tf2_rpg.md` is empty here). An agent
working from this repo cannot read it, so the load-bearing content is
restated below with `Descent.*` renamed to `TF2.*`. If the two disagree,
the sharp-fresh text is the ruling and this section is updated.

### B.1 The constraint
Engine, entities, traces, sounds and the ABI are single-threaded. The AI
thread touches **none** of them. Enforced as a build fact: `TF2.Ai` has no
reference to `SourceSharp.Abi` (a fact fails if one appears; mutation-
verified by adding one). Everything the AI knows arrives as data the main
thread published; everything it wants happens because the main thread read
a command. NextBot is the reference for *what* to perceive and react to
(`NextBot/NextBotEventResponderInterface.h`: 53 `On…` events;
`NextBotVisionInterface.h`: sight rules) — its **vocabulary**, not its
threading.

### B.2 One frame
Main thread: 1 APPLY (drain commands made from frame N-1; validate handle →
locomotion/aim/fire) → 2 SIMULATE → 3 SENSE (vision budget → sight events;
taps → sound/damage/gameplay events) → 4 PUBLISH snapshot N (latest wins)
and events (queue, never dropped), signal, carry on — **never wait**. AI
thread: wake, take newest snapshot, drain all events, remember → decide →
path → steer, write commands tagged "from N". One tick of latency.

### B.3 Inputs — perceive, never query
- **Events** (SPSC ring, never dropped; overflow is counted and fails the
  gate): `Sound{pos, radius, kind, source, team}`, `SightGained/SightLost`,
  `Damaged/AllyDied/AllyDamaged`, gameplay (`FlagTaken/Dropped/Captured`,
  `PointCapturing/Captured`, `CartMoving/Blocked`, `BuildingBuilt/Sapped/Destroyed`,
  `UberDeployed`, `Taunted`, `RoundState`), self (`Spawned{bot, class, team, seed, skill}`,
  `Removed`, `Arrived`, `Stuck`, `Blocked`, `Landed`, `ClipEmpty`, `ReloadDone`,
  `CommandRejected{seq, why}`).
- **Snapshot** (latest wins): per bot — position, velocity, health, clip,
  grounded, conditions; per player — position, velocity, state flags. A
  brain gets a **`PerceivedView`** filtered per bot: a player's kinematics
  are readable only while that bot holds `SightGained` on them. Enforced in
  the type, asserted by a fact that tries.
- **Sight** is the one sense that needs the engine: main-thread vision pass
  = range → view cone → (precomputed visibility where the nav mesh offers
  it; TF2 nav has no cell table, so start with range+cone) → a fixed
  per-frame trace budget served round-robin. Cloak/disguise are range
  multipliers.
- **Hearing** needs no engine: attenuate by path distance through the nav
  graph, not straight-line.

### B.4 Memory — per bot, fixed size, no allocation after spawn
Known entities `{handle, lastSeenPos/Vel/Tick, lastHeardPos/Tick, confidence}`
(decays; a lost target is predicted along `lastVel`, then a place to search,
then forgotten); threat table per enemy (damage from, healing by, proximity;
`Taunted` pins); investigations (unexplained sounds by loudness/recency);
self (current intent, seq, last feedback). Per team a **shared blackboard**:
a sighting is "called out" after a delay to teammates in earshot at reduced
confidence (replaces NextBot's `TFBotVision` sharing).

### B.5 Deciding, pathing, steering
Decide at 10 Hz staggered: a behaviour tree per role whose leaves only read
memory and write an intent; every threshold is read from the bot's **skill
parameters** (B.11), never a constant. Path: per objective cell and per
clearance class one Dijkstra **flow field** over the nav graph, rebuilt
when the objective moves; individual A* with funnel smoothing for the odd
trip (sniper post, retreat), time-sliced. Steer: separation, corner
clearance, strafing/range-keeping per weapon family.

### B.6 Output — intents with a lifetime
`MoveCommand{bot, fromTick, seq, expiresTick, move: Hold|Follow(corners≤4, speed)|Strafe|Flee|Jump, face: None|MoveDir|Point|Entity(h), action: None|Fire|Melee|Ability(id)|Reload|Taunt (+oneShotSeq)}`.
Two channels: latest-wins slot per bot (move/face) and a sequenced queue for
one-shots (neither lost nor repeated). The applier trusts nothing: handle
serial (the invalid handle is `0xFFFFFFFF`, never 0 — `EntityHandle.None`),
newer `seq`, not stunned/airborne, ability off cooldown; refusals return as
`CommandRejected`. Locomotion, collision, animation and weapons stay
main-thread code in the bot's player entity.

### B.7 When things go wrong
Late/stalled AI: commands carry `expiresTick` (~250 ms); bots finish, then
hold; events queue and all process on recovery. Bot died between publish
and apply: handle check drops the command (counted). GC: a brain tick
allocates **nothing** in steady state (fact with
`GC.GetAllocatedBytesForCurrentThread`). Lifecycle: thread starts at level
load with the immutable nav graph, is **stopped and joined at level
shutdown** before the graph is released (teardown order is a named risk —
the gate loads two maps). One AI thread per server process.

### B.8 Determinism and debugging
A brain is a pure function of `(seed, snapshot stream, event stream)`;
the thread is only transport. `IAiTransport` has **threaded** and **inline**
(`tf2_ai_sync 1`) implementations so any bug is A/B'd between logic and
hand-off. A server can **record** its input streams; a test **replays** a
recording through a fresh brain and demands the identical command stream.
`tf2_ai_dump <bot>` prints a blackboard through the dev API.

### B.9 Build order
1. `TF2.Ai` skeleton, data types, inline transport, the no-Abi fact.
2. Skill roll (`tf_bot_difficulty` → parameter curves) so every later step
   reads parameters; memory and hearing driven by synthetic event streams.
3. Threat, team board, alert propagation.
4. Flow fields with clearance classes, steering.
5. Behaviour-tree runtime and the first roles (soldier attacker, heavy
   defender, medic pocket).
6. Main-thread half in `TF2.Server`: sound/damage taps, vision pass,
   command applier, bot player entities (`tf_bot` players through the
   core's fake-client path — check `IEnginePlayers` for a bot-add call; if
   absent it is a generic ABI row).
7. Threaded transport, stall/teardown handling, recorder/replayer.
8. Remaining roles (engineer, spy, sniper, scout, demo, pyro), scenario
   trees (CTF, PL, CP), then MvM/Halloween as their own plans.

### B.10 Gates
Pure facts per memory rule; hearing by path vs wall; threat/taunt; alert
delay/range; flow field never routes a bot through a gap it cannot fit;
`PerceivedView` cannot yield an unseen player. Structure: no-Abi reference
(mutation-verified); zero bytes over 1,000 steady-state ticks. Determinism:
recorded session replays byte-identical, inline and threaded. Hand-off:
events in = events processed across an induced 1 s stall with main-thread
frame time unchanged; one-shots applied exactly once; a command for a bot
killed that frame is dropped and counted. Live: 24 bots on `cp_dustbowl`
with main-thread AI cost inside a budget measured first; two map loads
with `/proc/<pid>/task` back to baseline; the Stage 4 gate passes
identically with `tf2_ai_sync` 0 and 1.

### B.11 Skills → parameters
Stock `tf_bot_difficulty` 0–3 maps to a parameter set (aim error, reaction
time, hearing range, retreat health, cover preference, …); the structure
allows a per-bot roll later. Facts: each parameter drives a brain knob
(fact per parameter: knob moves when the parameter moves).

## 5.1 Stock sources (fork, read-only) — sizes at 2026-10-11

- `server/tf/bot/`: 6 cpp / 6,803 lines top level (`tf_bot.cpp` 8,328 in
  the first draft's count included headers; use these numbers:
  `tf_bot.cpp`, `tf_bot_vision.cpp`, `tf_bot_body.cpp`, `tf_bot_locomotion.cpp`,
  `tf_bot_manager.cpp`, `tf_bot_squad.cpp`), `bot/behavior/` 134 files /
  20,439 lines (`scenario/capture_the_flag` 8 files 981, `scenario/payload`
  6/703, `scenario/capture_point` 6/1,044, `engineer` 22/3,241 incl.
  `mvm_engineer`, `medic` 4/1,314, `spy` 16/1,831, `sniper` 4/956,
  `demoman` 4/748, `squad` 2/391, `missions` 6/614, `nav_entities` 6/420,
  `training` 2/187 (D5 drop), `scenario/raid` 10 and `creep_wave` 2 (dead
  modes, drop)), `bot/map_entities/` 20 files / 2,082 (`bot_hint_*`,
  `bot_generator`, `bot_roster`, `bot_action_point`).
- `server/tf/nav_mesh/`: 5 cpp / 3,952 lines (`tf_nav_mesh.cpp`,
  `tf_nav_area.cpp`, `tf_path_follower.cpp` — **not ported**: flow fields
  replace it, divergence row). The base `server/nav_mesh.cpp` family loads
  `.nav` files; check whether the managed core has a nav loader
  (`grep -rn "nav_mesh\|\.nav" src/server/managed`); if not, the `.nav`
  reader is a generic core task (the file format is documented in the
  stock `nav_file.cpp`).
- `game/server/NextBot/*` (vocabulary only): `NextBotEventResponderInterface.h`,
  `NextBotVisionInterface.h`, `NextBotBodyInterface.h`.

## 5.2 What this stage's plan must contain

A `docs/plans/tf2/stage5_bots_plan.md` written against the Stage 4 tip
with: the `TF2.Ai` csproj (no Abi; in `SourceSharpModGated`), the data types
of B.3/B.6 as `readonly record struct`s, the nav loader decision
(`check-first` with both arms), the eight build-order phases as exec-plan
phases each with its B.10 gate, a per-role table mapping the stock
`behavior/` files to tree definitions, and the live budget measured before
the threaded transport lands.

## 5.3 After bots (each its own plan, not started here)

- MvM: `server/tf/player_vs_environment/` 48 files / 15,931 lines
  (population manager, populators, tank, upgrades) — depends on bots.
- Halloween bosses: `server/tf/halloween/` 84 files / 12,476 lines.
- Other modes: 5CP/KOTH/Arena/TC/ADR/SD/PD — mode plugins over Stage 2.2
  (`tf_logic_koth`, `tf_logic_arena`, `tf_logic_player_destruction.cpp` 468,
  `tf_logic_robot_destruction.cpp` 1,614, `entity_capture_flag` variants).
- Dropped (D5, D2 — one `docs/divergences.md` row each): replay
  (`REPLAY_ENABLED` never defined), `bot_npc/` (12 files; OBSOLETE/raid),
  `raid/`, `STAGING_ONLY` arms, dead `TF2`/`TF2_DLL` arms
  (`h_cycler.cpp:108`, `econ_item_inventory.cpp:1741`), workshop
  (`server/tf/workshop/`, `client/tf/workshop/`), sixense, saxxy, vgui
  `-game tf` res hook, `tf_duel`, training annotations, GC/matchmaking/
  store/trading/quests/achievements (`achievements_tf_*.cpp` ≈ 15k lines,
  `tf_quest_restriction.cpp` 3,040, `tf_gc_server.cpp` 4,402,
  `tf_passtime_*`, `tf_voteissues.cpp` 1,815 — votes may return later as
  their own small plan).
