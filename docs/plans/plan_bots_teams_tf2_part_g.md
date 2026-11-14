# Part G of the TF2 bots plan: human feel, learning from players, and blast jumps

Status: written 2026-11-10; the user said "Ok to push and continue" the same
day, so it is started, at G0. An
addition to `docs/plans/plan_bots_teams_tf2.md` (Parts A–F), kept in a file of
its own so the main plan, which the TF2 AI session owns, is not edited under
it. When this is approved, two edits go into the main plan through that
session: a Part G row pointing here, and the 2026-11-10 rulings in §8.1.
The user has said the session that wrote this implements it; nothing is
implemented yet.

The user's requests, word for word (2026-11-10):

- "there is another session working on ai for tf2, i want you to review what
  they have done and suggest ways to make the AI better to play against and
  be more human like plus learn and grow from what other players on the map
  do. An example is that i want the solider ai to be able to rocket jump by
  identifying good places to do it and how to do it and be able to complete
  the rocket jump map"
- "for tf2 ai it makes sense to have a sqlitedb to record map and mode info
  into to improve bot responses"
- "i want you to write this up as an adition to the existing plan and tell
  the other session you are going to implment all this"

## G.0 Premises

Read this session at these commits:

| tree | commit | note |
|---|---|---|
| this repo, `main` | `98522a4e2` (2026-11-10); `690a916b5` at writing, no change under `src/ai` or `docs/plans` between them | P6 steps 1–7, 9, 10 landed; step 8 (the TF2 bots page) not |
| map-tools `../source-sdk-map-tools` | `b178845` = `maptools.lock` | read only |
| the Valve clone | `22288b91` = `valve-sdk.lock` | read only; identifiers cited, no code quoted |
| `jump_academy2_rc9a.bsp` | in the Steam TF2 install, `tf/download/maps/`, reachable as `bin/debug/content/tf/download/maps/` | entity lump read with a scratch script (LZMA, 987,213 bytes) |

The rules of the main plan's §0 bind this part, with one change the user
ruled on 2026-11-10: **TF2 map and mode experience is stored on disk in
SQLite** (G.3). Everything else stands: usercmds only, perception only for
anything live, off the main thread, 100 % managed, `.nav3d` only, human
motion, no human recordings committed, a mod-neutral core.

What Part G does **not** take over: the eight other class subtrees (P7), the
other modes' branches (P8), the TF2 bots page (P6 step 8), the item stages
(Part F). It supplies layers those call.

## G.1 The review: what is there and what it lacks

All file:line at `98522a4e2`.

**What plays.** CTF as Soldier on ctf_2fort. Every bot sends `joinclass
soldier` (`src/ai/tf2/server/TFBotClassPick.cs`), `TFClassTrees` hands every
class the Soldier tree, and `TFDecider` acts only on `ObjectiveKind.Carry`
and `Deliver` (`Trees/TFDecider.Ctf.cs:36-42`) though the feed publishes
Payload, CP and KOTH. The only play numbers are in commit bodies: 8 bots,
ten minutes, nine flags taken and none captured (`6b2e6f808`, fixed there);
12 bots, ten minutes, 2,771 stuck reports (`c0ccd6759`, fixed there, no
number after). `tools/tf2-gate`'s `bots-ctf` asserts a capture in 180 s and
has no stuck or rate assertion.

The TF2 AI session's own measurements, from its message of 2026-11-10 (ten
minute headless runs on ctf_2fort at `c0ccd6759`; not in any doc, not re-run
here): 6v6 made 3, 3 and 2 captures, with the first at 4 to 6.6 minutes,
8–12 pickups, 45–54 kills, 70–88 rocket jumps begun and 403–1,107 stuck
reports; 4v4 made no capture. Four one-bot traps remain, at (1318 1453 357),
the bridge roof (−4 −188 185), a stair landing (1232 −1527 −44) and
(24 1260 77). **The 2fort nav is about to change:** a map-tools fix
(`claude/tf2-nav-exits`, unpushed, after the navperf2 PR) stops vis-cluster
and occluder brushes walling off doorways and the bridge, and the game's
lock bump to it lands with a held fact (`archive/p6c-dryway-fact`).

**The brain against HL2MP's.** `TFDecider*.cs` is 934 lines;
`Hl2mpDecider*.cs` is 3,747. Of the fields of `DecisionParams`
(`src/ai/core/Skills/SkillCurves.cs:306`) the TF2 decider reads four:
`StrafeWeight`, `StrafeMeanSeconds`, `AimErrorDeg`, `LeadQuality`. The shared
layer still gives it reaction time, the flick and tracking model
(`HumanMotion`), recognition delay and give-way.

| finding | where | consequence |
|---|---|---|
| bots of a team decide alike | utilities are constants (`TFDecider.cs:356-382`, `TFDecider.Ctf.cs:11-21`); the role is `Slot % DefendEvery` for the slot's life (`TFDecider.cs:113`) | herding; the live fixes so far are distance thresholds (`AttackerStopsFor`, `AttackerChaseReach`) |
| aim error is drawn fresh and uniform each decide | `TFDecider.Soldier.cs:106-110` | jitter; HL2MP's error wanders, is worse on first sight and has a smaller vertical share (`Hl2mpDecider.cs:2538-2640`) |
| the eye is origin + 64 and a body is origin + 48, for every class | `TFDecider.Soldier.cs:96-97` | wrong for a crouched or short target; `TFKnowledge` has the class eye heights; HL2MP has `AimHeightHold` |
| the tree never reads `PerceivedView.Things` | `grep Things src/ai/tf2/server/Trees` is empty | no rocket dodge, no sentry avoidance, though `TFBotGameView` publishes projectiles and buildings |
| the target is the nearest enemy in sight | `TFDecider.cs:275-316` | the enemy carrying the bot's flag, a Medic, a grudge get no weight |
| none of the learned state is read | `grep -E 'Habits|Predict|ChokeMap|Heard' src/ai/tf2/server/Trees` is empty | `BotMemory` records habits for TF2 bots and nothing uses them; no prefire, flank, cover, glance, search by hearing |
| an unseen enemy is believed to move at 190 u/s | `BotMemory.OpponentSpeed` (`src/ai/core/Memory/BotMemory.cs:57`), HL2MP's run | wrong for every TF2 class (Scout 400, Heavy 230) |
| any damage ends a commitment | `NoteInterrupts` (`TFDecider.cs:257-265`) | **unverified:** whether afterburn ticks make a burning bot thrash |
| defend spots are random points in a circle | `GoDefend` (`TFDecider.Ctf.cs:149-172`) | no sight line, no cover; `ChokeMap` is not asked |
| roaming walks to an enemy spawn | `GoRoam` (`TFDecider.cs:475-510`) | a bot with nothing to do heads for the spawn-room door |

**The rocket jump as built is probably slower than walking** (calculated
from the mod's own formula, **not measured**). The row
(`Knowledge/TFAbilities.cs:36-42`) looks down 70° for 40 commands, then
jumps, ducks and fires, with the yaw left where it was
(`BotCommandApplier.cs:314-319`), so the rocket lands in front of the bot.
The blast pushes from the rocket's centre, 10 u low, toward the victim's
centre (`TFRadiusDamage.PushDirection`, `:801`): up and backwards. With the
muzzle 23.5 u ahead and 12 u right (`RocketMuzzleForward`, `RocketMuzzleRight`)
the push is roughly 690 u/s, about a quarter of it against the run. Then the
follower sends `MoveIntent.Hold` while the bot falls
(`NavFollower.cs:362-368`), so nothing steers the flight. `JumpPays`
(`TFDecider.Soldier.cs:177-197`) checks the floor ahead with `WalkableLine`
and never the headroom. It costs about 45 health. A person's speed jump
fires behind them and steers in the air.

## G.2 Human feel: the shared layer, personality, reacting to what it sees

**G.2.1 Lift the feel pieces out of the HL2MP decider, so both deciders call
them.** Today they are private members of `Hl2mpDecider`. Each moves to
`src/ai/core/` as a small class that names no game, with HL2MP's recordings
replaying byte-identical after each move (P5's gate), then `TFDecider` calls
it.

| piece | HL2MP today | TF2 gains |
|---|---|---|
| wandering aim error, first-sight penalty, vertical share | `Hl2mpDecider.cs:2538-2640` | no jitter; reads `AimErrorDeg` as now |
| `AimHeightHold`, per-class eye and aim heights from `TFKnowledge` | `core/Motion/AimHeightHold.cs`, `Hl2mpDecider.cs:2574` | crouched targets, the Soldier's own eye |
| target scoring: stickiness, grudge, rank | `Hl2mpDecider.cs:1628-1642` | plus TF2 terms: the flag carrier, a Medic, a healed target, a sentry in sight |
| where an unseen enemy is (`Where`, `PositionPredictor`) with a per-class speed | `Hl2mpDecider.cs:1693` | `OpponentSpeed` becomes a number the game's knowledge gives per actor class |
| search by hearing (`TryBestInvestigation`) | `Hl2mpDecider.cs:1743, 2026` | bots turn to rockets and footsteps |
| glances off the path (`GlanceRate`) | `Hl2mpDecider.cs:2460` | no more staring along the route |
| prefire and corner checks (`PrefireChance`, `HabitSpot`) | `Hl2mpDecider.cs:552-647` | rockets into a choke an enemy was last seen at |
| the friend gate | `FriendGate` (`Hl2mpDecider.cs:441`) | no rocket through a teammate's back at close range |
| streaks reaching the motion layer | `Hl2mpDecider.cs:1251-1320` | `TFDecider` fills the `DoingRecord` streak fields it leaves at zero (`TFDecider.cs:186`) |
| retreat on health by skill (`RetreatHealthPct`, `GiveUpHealth`) | `Hl2mpDecider.cs:1715` | replaces the fixed `HurtShare` 0.4 |

**G.2.2 Personality in the utilities.** Each root score becomes
`base × trait × (1 + seeded noise)`: Aggression on Fight and Fetch, Tenacity
on ChaseCarrier, Discipline on Defend and Guard, Teamwork on Escort, Nerve
on Survive. The role is no longer the slot's remainder: a team allocator in
the team layer (plan B.4's slot-order merge, deterministic) hands out
attack, defend and escort from the flag's state and from what the humans on
the team are doing, re-dealt on a flag event with a hold time. Fact: eight
bots given one view split across at least three branches; mutant (traits
ignored) red.

**G.2.3 Reacting to things.** `TFDecider` reads `PerceivedView.Things`:

- a rocket or pipe whose path passes within its blast radius of the bot: a
  sidestep or a jump, after the bot's reaction time, by Agility;
- a sentry in sight: out of its range unless the bot is a class that kills
  it (P7), and its place remembered for the life of the round;
- an Übered enemy: back off; a burning teammate, a called Medic: P7's.

**G.2.4 The voice menu, both ways.** The mod has the commands
(`src/tf2/server/Voice/TFVoiceMenuCommand.cs`). A bot calls "Medic!" when
hurt, "Sentry ahead!" on first sight of one, "Spy!" on an identified Spy,
thanks after a heal, each through its client command with a per-bot rate
limit by Teamwork. A human's voice command a bot hears is an input: "Help!"
and "Medic!" raise Escort and (P7) the Medic's heal target, "Sentry ahead!"
marks the place for teammates as a callout does (plan B.4).

**G.2.5 Dominations as grudges.** `src/tf2/server/Combat/TFDominations.cs`
has the state; a domination the bot's HUD shows multiplies its grudge, and a
revenge kill clears it.

**G.2.6 The TF2 reference recording and jank.** `ai_record human` already
works in the TF2 mod (`src/ai/server/Senses/AiRecording.cs:102`, generic).
`src/ai/replay/JankTool.cs` passes HL2MP weapon reaches and falls back to
`Hl2mpDecider`; the tactics measures key on a leaf named "ambush". The game
supplies its reaches and leaf names through its binding, and
`make ai-jank` takes the mod. This overlaps P9's "TF2 jank run": agreed with
the TF2 AI session before starting (G.9). The user's TF2 sessions stay
local, as HL2MP's do.

## G.3 The experience store (SQLite)

**What it is.** One SQLite file per server holding what players did on each
map in each mode, as counts with no player identity. A bot reads an
immutable snapshot of it; nothing in it says where anyone is now.

**Where the code lives.**

| assembly | holds |
|---|---|
| `SourceSharp.Ai` (core), `src/ai/core/Experience/` | `MapExperience` (the immutable snapshot a decider reads), `ExperienceDelta` (what a round added), `IExperienceStore`, the accumulator; names no game and no SQLite |
| `SourceSharp.Ai.Store` (new), `src/ai/store/` | `SqliteExperienceStore` over `Microsoft.Data.Sqlite`; the schema and its migrations |
| `SourceSharp.Ai.Server` | the feed from the server's snapshot, the convars, `/ai/experience` routes |
| `src/ai/tests/` | `MemoryExperienceStore` for every fact that is not about SQLite; the SQLite facts write to a temp file |

`MemoryWorld` (`src/ai/core/Memory/BotMemory.cs:9`) gains an `Experience`
member, never null: an empty one when the store is off, so no decider has a
null branch.

**Keys.** A row is keyed by the map (name and the bsp's checksum, so an
edited map starts clean), the mode (the id `GameModeCatalog` names it by),
the stage (the active objective's index; 0 in CTF), and where it matters the
team role and the class. **Places are world bins, not nav cells**: 64 u
square, 72 u tall. The nav is rebuilt often (`NavGraphFromNav3d.Revision` is
5 already) and cell ids change with it; bins survive, and the snapshot maps
bins to cells at load.

**Tables.**

| table | a row | read for |
|---|---|---|
| `map`, `context` | the dimensions above | — |
| `cell_stat` | bin, source (human or bot): seconds present, deaths, kills, damage taken and dealt | traffic and danger: route cost, flanks, Spy paths |
| `sight_line` | victim bin, killer bin, weapon class: count | where death comes from: prefire, cover, sentry places |
| `look` | bin, yaw bucket (16), pitch bucket (3): seconds | where people face from here |
| `spot` | kind (sentry, dispenser, teleporter in/out, sniper hold, sticky trap, hold), bin, yaw: uses, seconds held, kills, deaths | where to build, camp, defend |
| `link` | class, from bin, to bin, kind, recipe blob, source (human, solver, practice): tries, successes, mean seconds, mean health cost | movement the nav lacks (G.5, G.6) |
| `outcome` | kind (route family, class pick, sub-class, push timing), key: tries, score sum | what worked here |
| `round` | mode, seconds, winning role, humans, bots, composition | composition and timing priors |
| `meta` | schema version | migrations |

**Decay.** Each row carries the round count it was last written at; a read
or a merge scales it by 0.5 per `ai_experience_half_life` rounds played on
that map and mode (default 20). Nothing is rewritten on a timer.

**The feed.** The accumulator runs on the AI worker pool over the server's
published snapshot (every player's origin, eye, velocity, class, team, and
the combat taps' damage and kills), not over bots' views. This is the
demo-watching reading of "learn from what other players do" (open question
Q-G1). Humans count 1.0 and bots 0.25 in `cell_stat` and `look`, so bots do
not teach each other their own stuck spots; a bot's own results feed
`outcome` and `link` in full. Writes are batched on a worker at round end,
map change and every 60 s, as additive upserts in WAL mode with a busy
timeout, so two game processes sharing a file merge.

**The snapshot.** Loaded on a worker beside the nav when a map starts,
refreshed only at a round start, so a decide is a function of its views, its
seed and one snapshot. A recording writes the snapshot's hash at session
start and the replay tool refuses another (`AiRecordFormat.Version` 7 → 8);
HL2MP has no store and its recordings are unaffected.

**Convars.** `ai_experience_db` (a path; default
`~/.local/share/sourcesharp/ai-experience.db`, `%LOCALAPPDATA%` on Windows;
empty = off), `ai_experience_half_life`, `ai_experience_learn 0|1` (read
without writing). **Every tool launch passes an empty path or a private
one** (`tools/local_launch.py`, the gates, lane boots, `make headless`), so
no gate depends on what this box has played. An unreadable file logs one
refusal line, counts in `/ai/counters`, and the game runs memory-only.

**How strongly a bot uses it** is read from its existing skills, no new
axis (the 60-point pool stands): Cunning for routes and flanks, Discipline
for spots, Alertness for looks, Agility for the tightness of a link it will
try. An Easy bot barely adapts.

**The dependency.** The game ships no SQLite today
(`plan_host_sdk.md:532`). `Microsoft.Data.Sqlite` brings a prebuilt native
`e_sqlite3` per platform; it is a NuGet binary, not hand-written native
code. map-tools already depends on it. The risk is its load from a mod's
assembly load context (G.10).

## G.4 Using the experience

| use | how | fact (red first) |
|---|---|---|
| route cost | `NavPathfinder.Find` takes a per-cell cost field beside its obstacles; a cell's added cost is its hazard (deaths per second present) × the bot's weight | on a two-route test map with deaths seeded on one route, a Cunning 8 bot takes the other in ≥ 80 % of runs and a Cunning 1 bot in ≤ 60 %; mutant (field ignored) red |
| Spy paths, flanks | traffic directly: C.3.2's "avoid common paths" gets real data | P7's Spy fact reads the field |
| looks | travelling with no target and no prefire spot, the look is the bin's dominant learned yaw, changed every 0.6–1.5 s, blended with the path | a bot crossing a seeded bin faces within 25° of the seeded yaw for ≥ 40 % of its crossing; with an empty store it looks along the path |
| spots | `MapExperience.Spots(kind, near, radius)` for P7's Engineer and Sniper and for Defend, falling back to `ChokeMap` | a defender holds a seeded spot; with none, a choke |
| danger places | `sight_line` rows give prefire and splash targets (a sentry place a team died to) | a Soldier fires at a seeded sentry bin before it sees the sentry, only after a teammate's death there this round |
| outcomes | seeded Thompson sampling over route families (by the chokes they cross), class and sub-class picks (multiplying C.4's composition weights), push timing | a route family that failed five times is chosen less than one that worked; same seed, same store, same choice |

## G.5 Blast jumps that work

In dependency order.

1. **Measure first.** `ss_ai_tf_jump`: a generated test map
   (`game/mod_tf2/maps/src/ai/`, built as `LINKED_MAPS` are for the test
   mod), with a 2,000 u flat run, ledges at 150, 250 and 350 u, a 500 u gap,
   and a low-ceiling corridor. Today's jump is timed against walking on it.
   The plan's C.3.2 fact (a 2,000 u route in under 70 % of a walking bot's
   time) is the target the measurement is read against.
2. **A closed-loop jump controller, not a fixed row.** `AbilityStep`
   (`src/ai/core/Brain/Abilities.cs:26`) holds buttons, ticks and a pitch.
   A new `AbilityRun.Program` row runs a controller in the applier, a few
   float operations a command beside `HumanMotion`: **align** (turn to the
   takeoff yaw and pitch through `HumanMotion`, wait until the view's error
   is under a tolerance by skill, give up after a timeout), **fire** (jump
   and duck, then attack at the recipe's offset), **fly** (steer toward the
   landing with the strafe keys and a turn, under TF2's air rules:
   `AirAccelerate` 10, `AirSpeedCap` 30, `TFViewVectors.cs:35-44`),
   **land** (release, hand the view back). `ai.apply.us` is measured before
   and after.
3. **A simulator.** `GameMovement` (`src/shared/interop/Movement.cs:660`)
   already runs without the engine against an `IMoveWorld`
   (`src/shared/interface/Movement.cs:35`); the tests do it with fake
   worlds. New: `VoxelMoveWorld`, an `IMoveWorld` whose hull sweep steps the
   body through the `.nav3d`'s free space (`INavSource.FitsAt`,
   `IVoxelSpace.Free`), and `BlastJumpSim` in `SourceSharp.Ai.TF2`, which
   flies the rocket from the muzzle at `RocketSpeed` to the first solid,
   applies the self blast with the mod's own functions (`AtDistance`,
   `SelfDamageScale`, `DamageForce`, `PushDirection`, `Push` in
   `TFRadiusDamage.cs`), and steps the movement to the landing. Its error is
   a measured number: a differential fact flies the same recipes live on
   `ss_ai_tf_jump` and in the simulator and holds the landing gap under a
   tolerance set from the first run.
4. **Finding places.** A derivation pass after the graph is built, on the
   pool, cached with the graph. Candidates: a standable cell A and a cell B
   within 600 u and up to 400 u higher (plan C.3.2's reach), where walking
   from A to B costs more than 2.5 × the straight line or cannot be done.
   For each, a search over the recipe (pitch, yaw offset from the way,
   fire delay, run-up speed, strafe side), coarse then refined. A jump is
   kept when it lands on B and still lands in at least 60 % of perturbed
   trials (± 3°, ± 2 commands, ± 16 u at takeoff). The share is its
   **robustness**. Two recipe families here: **up** (a standing or slow
   vertical jump) and **forward** (a running jump with the rocket behind).
5. **The edge class.** `NavEdgeType.Blast` and `JumpReach.Blast`, as plan
   `:663` names them; the recipe in a side table by edge index, since
   `NavEdge` (`NavGraph.cs:89`) has no payload; `Revision` → 6. An edge
   costs its air time, plus its health cost in seconds, plus a penalty for
   low robustness against the bot's Agility. `NavObstacleState` carries the
   health a search may spend, so a hurt bot's routes leave the edges out.
6. **The follower and the decider.** On a blast edge `NavFollower` hands the
   controller its recipe and keeps its hands off the flight (its
   `_falling` hold stays for drops). `JumpPays`'s flat-run rule goes: the
   route decides where, and the decider only says whether a jump is allowed
   now (nobody in sight unless escaping, health over the floor, a rocket to
   spare, not a carrier leaving its escort).
7. **Live results feed back.** Each attempt writes tries and successes to
   `link`; an edge under 30 % after five tries is left out of that server's
   searches until it decays back.

Facts: the C.3.2 route fact on `ss_ai_tf_jump`; a bot reaches the 250 u
ledge in ≥ 8 of 10 tries; never jumps under the health floor, in the
low-ceiling corridor, or with an enemy in sight; a mutant with the edge
class off is red. Demoman sticky jumps reuse 2–7 with their own recipe
family once P7's Demoman exists.

## G.6 Learning movement from players

- **Detect.** In the accumulator: a player off the ground for 0.4 s or more
  who lands in a bin the graph has no walk, jump or drop link to from the
  takeoff bin. `AiActorFrame` gains a ground flag and the taps record a
  self blast as its own event (`ShotTap.cs:40` drops self hits today).
- **Store.** A `link` row: class, kind (blast, double jump, crouch jump,
  other), takeoff state, and the player's commands through the landing
  (about 4 KB), with no identity.
- **Fit, never replay.** The simulator fits a controller recipe that
  reproduces the landing. A bot never plays back a person's mouse; its view
  still goes through `HumanMotion`.
- **Trust.** A fitted link joins the graph as a candidate and earns its
  place by the bots' own tries (G.5 step 7).
- **Practice.** `tools/ai-jump-practice <map>` refines low-robustness
  recipes in the simulator offline and writes them back as source
  `practice`.

Fact: a scripted player performs a jump on `ss_ai_tf_jump` that the
derivation was told to skip; after one round a Soldier bot's route uses it.

## G.7 Jump maps

**The target, in tiers.** `jump_academy2_rc9a` has 264 `trigger_teleport`,
200 `info_teleport_destination`, 29 `func_regenerate`, 26 `func_nogrenades`,
19 `trigger_capture_area`, 11 `trigger_catapult` and `filter_tf_class`, with
rooms named for pogo, strafe, sync, bounce and speed shots.

| tier | techniques | promise |
|---|---|---|
| 1 | single jumps for height and distance | G.5; the gate's first arm |
| 2 | wall jumps, a second rocket in the air, pogo | committed here: two more recipe families and a multi-rocket controller |
| 3 | sync, speed shots, bounces, strafe-heavy rooms | not promised; attempted through practice (G.6) and reported with numbers |

**What it needs.**

| item | where | note |
|---|---|---|
| `trigger_catapult`, `func_nogrenades`, `filter_tf_class` | the TF2 mod; stock `game/server/tf/trigger_catapult.cpp`, `func_nogrenades.cpp`, `tf_filters.cpp:324` | `grep` finds none of the three in `src/tf2`; ports, with stock classnames |
| teleports as nav links | `trigger_teleport` to its destination (`src/server/managed/Entities/Triggers/TriggerTeleport.cs` exists) | a dynamic edge, or a fail reset when its filter and place say so |
| a nav for a map this tall | map-tools' play space is the vis clusters joined to the spawns **and the teleport destinations** (`Nav/NavPlayableSpace.cs`), so platforms nobody can walk to are covered. But `jump_academy2_rc9a` does not build: "the map is 32704 units tall, 2044 voxels of 16; a .nav3d column holds at most 255" (`ssmap nav3d`, map-tools `b178845`, G.13). Its rooms are spread over the whole height of the world | **map-tools change, needs the user's go-ahead (Q-G6)**: a navigation per play-space region, or a taller column. A larger voxel is no answer (128 u) |
| mounting the map | `game/mod_tf2/gameinfo.txt` mounts `../../content/tf`, not `tf/download` | one search-path row; no map is copied |
| the course as objectives | the CP feed already publishes capture areas | a Jump branch: the next uncaptured area is the goal |
| failing | a teleport back is a failed try on that link | the follower re-locates each call already (`NavFollower.cs:352`) |
| health and ammo | `func_regenerate` is a `ZoneKind.Resupply` already | before a link, health must cover its cost; else the nearest cabinet |

**The gate.** `tools/tf2-gate` step `bots-jump`, by name only: a Soldier bot
finishes the graded stages of `ss_ai_tf_jump` inside a time limit, across a
second map load. A second arm runs the tier 1 rooms of `jump_academy2`, and
fails when the map is absent.

## G.8 Phases

Each phase ends as the main plan's §6 says: `make test` green with the `ai`
leg, one reviewable commit a step, a red-first fact or a mutation arm for
each rule, `make check` before the merge. Worktrees `../ss-tf2ai-G<n>`.

| phase | ships | depends on | size (honest) |
|---|---|---|---|
| **G0 measure** | `ss_ai_tf_jump`; today's jump timed against walking; a ctf_2fort baseline of captures and stuck reports per ten minutes with 12 bots, three runs; the afterburn-interrupt check; cell and candidate counts for G.5 step 4 | the ctf_2fort baseline waits for the map-tools lock bump (G.1): before it, it measures a walled-off map. The jump map and its timing do not wait | 3–4 days |
| **G1 the feel layer** | G.2.1 piece by piece (a move commit, then TF2's use), per-class belief speed and aim heights | G0's baseline | 2 weeks |
| **G2 personality and things** | G.2.2–G.2.5 | G1 | 2 weeks |
| **G3 the store** | G.3: the assembly, schema, accumulator, snapshot, convars, the tools' empty path, the recording's hash | — (a lane beside G1) | 2 weeks |
| **G4 using it** | G.4's rows, one a commit | G1, G3 | 2 weeks |
| **G5 blast jumps** | G.5 steps 2–7 | G0 | 4 weeks |
| **G6 learned movement** | G.6 | G3, G5 | 2 weeks |
| **G7 jump maps** | the three entities, teleport links, the Jump branch, tier 2's families, the gate step; `ss_ai_tf_jump` grows the graded stages first, which need no map-tools change | G5, G6; the map-tools go-ahead for `jump_academy2` itself | 4–6 weeks, tier 3 open-ended |
| **G8 TF2 recording and jank** | G.2.6 | agreement with P9's owner; the user's TF2 sessions | 1 week |

Order: G0 → {G1, G3, G5 in parallel lanes} → G2, G4 → G6 → G7; G8 when the
user has recorded.

## G.9 File ownership against the TF2 AI session

P7 and P8 will rewrite the same decider this part touches, so the split is
by file. The TF2 AI session agreed to the table below on 2026-11-10: both
sides announce before touching a serialised file. Its next work, none
started: P6 step 8 (the TF2 bots page, new files), lengthening `bots-ctf`'s
180 s window, then P7 in `TFDecider*.cs`, `TFClassTrees.cs` and
`TFBotClassPick.cs`.

| files | owner |
|---|---|
| new: `src/ai/core/Experience/`, `src/ai/store/`, `src/ai/tf2/server/Jump/`, `src/ai/tests/Experience/`, `src/ai/tests/Jump/`, `tools/ai-jump-practice`, `game/mod_tf2/maps/src/ai/` | Part G |
| new partials `TFDecider.Feel.cs`, `TFDecider.Things.cs`, `TFDecider.Jump.cs` | Part G |
| `TFDecider.cs`, `TFDecider.Ctf.cs`, `TFDecider.Soldier.cs`, `TFClassTrees.cs`, `TFBotClassPick.cs`, class subtrees, mode branches | the TF2 AI session; Part G's edits there are small commits handed over or rebased daily |
| `Hl2mpDecider*.cs` (the lifts of G.2.1) | Part G, one piece at a time, recordings byte-identical |
| `NavGraph.cs`, `NavGraphFromNav3d.cs`, `NavFollower.cs`, `BotCommandApplier.cs`, `Abilities.cs`, `AiRecordFormat.cs` | serialised: one session at a time, announced |
| `tools/tf2-gate` | serialised (the main plan's rule); Part G adds `step_bots_jump` only |
| `docs/plans/plan_bots_teams_tf2.md`, `docs/plans/tf2_ai/` | the TF2 AI session |
| map-tools | its own session, after the user's go-ahead |

## G.10 Risks

| risk | why it is real | what limits it | size |
|---|---|---|---|
| the voxel world is too coarse to predict a landing | the `.nav3d` is free-space voxels, not brushes | G.5 step 3's differential fact gives the error early; robustness margins absorb some; the fallback is a brush hull trace in map-tools | could add 2–3 weeks |
| the native SQLite library does not load in the mod's load context | mods load through their own contexts (`src/shared/interop/SideLoad.cs`); nothing native rides one today | a load fact in G3's first commit, on Linux and in the Windows CI | small if found first |
| learning makes bots worse | feedback loops; a bad round teaches a bad route | bots count 0.25, decay, weights bounded by skill, `ai_experience_learn 0`, and G0's baseline re-run with a warm store | medium |
| a gate reads this box's history | a default path shared by every tree | the tools' empty path, held by a fact over each launcher | small |
| two sessions in one decider | P7 and P8 restructure `TFDecider` | G.9; new partials; daily rebase | medium |
| tier 3 never lands | expert-human technique | not promised; reported with numbers | accepted |
| the lifts change HL2MP's play | moved code, same logic | byte-identical replays per move | small |
| candidate search is too slow on a big map | cells × targets × recipes | G0's counts; pruning to ledges; cached with the graph; off the main thread | medium |

## G.11 Decisions

### Rulings (user, 2026-11-10)

- TF2 map and mode information is recorded in a SQLite database to improve
  the bots. This overturns "memory in memory only, never on disk" (main plan
  §8.1) **for TF2 map and mode data**; per-opponent habits stay in memory.
- The Soldier finds places to rocket jump, performs the jump, and the aim is
  to complete the rocket jump map (G.5–G.7).
- This part is an addition to the main plan, and the session that wrote it
  implements it.

- "Ok to push and continue" (after the review and the open questions were
  put to the user). Read as: the TF2 AI session's pending map-tools push
  (`claude/tf2-nav-exits`) may go, relayed to that session to carry out
  under its own permissions; and Part G starts. The open questions below are
  taken as recommended, **except Q-G6**: new map-tools work is asked for
  again when G7 reaches it.

### Open questions, each with a recommendation (taken as recommended 2026-11-10, Q-G6 held)

| | question | recommendation |
|---|---|---|
| Q-G1 | does the store learn from every player's state as the server has it, or only from what bots perceived? | every player's, for map knowledge (as watching a demo); anything live stays perceived |
| Q-G2 | HL2MP too? | no: TF2 only, as ruled |
| Q-G3 | on by default? | on for a played server, off for every tool launch |
| Q-G4 | ship the native SQLite library? | yes |
| Q-G5 | the jump map's promise | tiers 1 and 2; tier 3 reported, not promised |
| Q-G6 | map-tools, two items: a `.nav3d` for a map taller than 255 voxels (`jump_academy2` is 2,044; without it no bot can play that map), and a brush hull trace only if G.5 step 3 finds the voxels too coarse | **open, the user's to give**: asked when G7 reaches it; its own session's lane |
| Q-G7 | mount `tf/download` so Steam's downloaded maps load | yes |
| Q-G8 | a new skill for learning | no: read from Cunning, Discipline, Alertness, Agility |
| Q-G9 | who does the TF2 jank wiring (G8 against P9) | the TF2 AI session would leave it in P9 and does not hold that strongly; leave it there, and drop G8, unless the user wants it sooner |

## G.13 G0's measurements (2026-11-10, branch `tf2ai-G0`)

**The rocket jump, measured.** Instrument: `tools/ai-tf-jump measure
--seconds 300`, the game built at `5daaf62cf` (no game code has changed on the
branch since), the map from `c3b622456`, the track read again by the tool at
`6817f882b` (`read`, after its restart guard), debug, one headless tf2 game on `ss_ai_tf_jump`, one red
Soldier bot (Medium), its brain deciding; one run of each arm. The bot plays
CTF alone: east along the 1,800 u run for the flag, back carrying, east
under the low lane's slab to capture, back under it. Times are over each
lane's 1,200 u middle stretch, by server ticks.

| leg | may jump (health held at 200) | no jump (health held at 85) |
|---|---|---|
| run, east, fetching | 6.18 s median, n=11, 0.64 jumps a crossing | 5.09 s, n=11 |
| run, west, carrying | 5.00 s, n=8 | 5.03 s, n=8 |
| low lane, east, carrying | 5.08 s, n=3 | 5.20 s, n=2 |
| low lane, west, fetching | 5.01 s, n=3, no jump | 4.81 s, n=2 |

- **A leg with the jump is 21 % slower** (6.18 s against 5.09 s), where the
  main plan's C.3.2 asks for under 70 % of the walk's time.
- **Each flight** (12 in the arm; 13 ability sequences begun): 3.17 s in the
  air, 400 u over the floor in that time against the 761 u a walk covers,
  peak 928 u over the floor (medians). The first flight in the track drifts
  288 u sideways into the lane's wall: the muzzle is 12 u right of the eye.
- **928 u is not checked against stock.** The mod's force is its port of
  `ApplyPushFromDamage` (the force damage is the fallen-off damage times the
  Soldier's 0.6, then × 10, capped at 1,000 u/s); by that formula a crouched
  point-blank jump is near the cap. A stock A/B is the TF2 port session's
  instrument, not run here.
- **The low lane measured nothing about headroom.** `JumpPays` compares the
  straight line to the goal with `RocketJumpRoute` (1,200 u), and from the
  capture zone the flag is 400 u away through a wall, so the bot never
  considered a jump there. Two findings: the route's length is not what is
  compared, and the headroom question is still open (by reading, nothing
  checks it).
- **A frozen bot thinks it is stuck.** In the preround freeze after each
  round the follower reports stuck and hops in place (32 stuck reports in
  the run, all at the spawn): the bot does not know the round has not
  started (main plan C.7's setup time).

**Own damage ends a commitment.** By reading: `CombatTap.Hurt`
(`src/ai/server/Senses/CombatTap.cs:134-144`) raises `Damaged` for any hurt
whose victim is a bot, with no test for the attacker being the bot itself or
for the damage type, and `NoteInterrupts` treats any new damage tick as an
interrupt. So a bot's own rocket jump interrupts its branch. The hurt hook
is raised from the player's one damage path (`SsPlayer.cs:620`), so
afterburn ticks reach it unless burning bypasses that path; not run live.

**The nav's grain.** `ss_ai_tf_jump` (one 2,048 u room): 15,679 cells and
119,523 edges (the game's own load line), over 16 u voxels
(`nav_voxel_size 16`, the rooms pipeline's limit of 128 voxels a room is why
the run is 1,800 u and not 2,000). About 267 square units a cell: cells are
close to single voxel columns. So G.5 step 4 cannot search cell pairs (a
cell has some 4,000 others within 600 u); candidates are made per ledge
run, with one representative takeoff and landing each. 2fort's count waits
for the lock bump with its baseline.

**`jump_academy2_rc9a` builds no nav.** `ssmap nav3d` on it (map-tools
`b178845`, `-game` the tf2 mod, 3.5 s): "no navigation: the map is 32704
units tall, 2044 voxels of 16; a .nav3d column holds at most 255: build it
with a larger voxel." Its play space takes in every room a teleport reaches,
and they stand over the whole height of the world. This is the map-tools
item G.7 names.

**Fixture notes.** The tf2 mod runs no teams on a map without an objective
(`TFGameModeSelector`), and its spawn walk refuses a spawn point whose hull
starts in the floor: the map carries a flag pair and a capture zone, and its
spawns stand 16 u up.

## G.12 What could not be verified this session

- The rocket jump's cost was calculated here and then measured in G0
  (G.13); its height against stock is not checked.
- Whether afterburn re-triggers `NoteInterrupts`, live (G.13 has the reading).
- The `.nav3d` voxel size on the stock TF maps (the rooms pipeline's is 16 u,
  G.13), and so the simulator's likely error.
- Whether `e_sqlite3` loads from a mod's load context.
- The room structure of `jump_academy2` beyond its entity counts and names;
  which rooms are tier 1 was not walked.
- What the TF2 AI session has planned for `TFDecider` in P7 that would
  change G.9's split.
