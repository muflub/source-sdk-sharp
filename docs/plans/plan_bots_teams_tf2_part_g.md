# Part G of the TF2 bots plan: human feel, learning from players, and blast jumps

Status: written 2026-11-10 and started the same day on the user's "Ok to push
and continue". **On main: G0, G1 (ten of its eleven pieces) with its cost
cut, G3, and of G5 the simulator, the pilot, the recipe search and the
place-finding pass. Running: G2.** G.14 has where each phase stands and
what the lanes found. An addition to
`docs/plans/plan_bots_teams_tf2.md` (Parts A–F), in a file of its own because
the main plan is the TF2 AI session's; that plan points here and carries the
2026-11-10 rulings in its §8.1.

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
the push was put at roughly 690 u/s here; it is 1,000, the cap, in every
recorded flight, because the blast measures a TF2 player by HL2's box (G.14).
About a quarter of it is against the run. Then the
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
   body through the `.nav3d`'s free space (built: over `INavSource.Leaf`'s
   bounds and floor heights, since `FitsAt` answers only at a voxel's centre), and `BlastJumpSim` in `SourceSharp.Ai.TF2`, which
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
| a nav for a map this tall | `jump_academy2_rc9a` does not build at map-tools `b178845`: "the map is 32704 units tall, 2044 voxels of 16; a .nav3d column holds at most 255" (G.13). Measured there with taller columns, a 16 u `.nav3d` of it is 126 million leaves and a 4.5 GB image: not viable in any column format | **allowed by the user 2026-11-10** and built by the map-tools session (PR #152, merged at `3551fa8`, MapFormats 1.0.7 / MapTools 1.0.5): a new file beside the `.nav3d`, `<map>.navhull` (`ssmap nav3d <bsp> -hull`), boxes of free space carrying per built width (32 and 48 for TF2) the lowest solid overhead, so `FitsAt` stays exact; jump_academy2 is 3.9 M boxes, 27 MB, 68 s to build. `.nav3d` version 4 (16-bit leaf heights) landed with it for maps that fit. The game's side is a lane of this part after the TF2 AI session's lock bump: an `INavSource` over `NavHullReader` with a pseudo-leaf a (box, column), the loader preferring a map's `.navhull` |
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

- "Yes map tools should handle jump map" (asked whether map-tools may be
  asked to build a navigation for a map as tall as `jump_academy2`). Q-G6's
  first item is allowed; the map-tools session confirmed it with the user and
  built a hull navigation file for it, `.navhull` (G.7).

### Rulings (user, 2026-11-13)

Asked four things on 2026-11-12 (G.15's "still to do"); two were ruled and
two were sent back for a fuller account.

- **Prefire may spend the rockets kept back for a jump** ("yes"). The clip
  rule of `TFDecider.PrefireAllowed` (a bot that rocket jumps prefires only
  with `RocketJumpRockets + 1` loaded) goes: one rocket loaded is enough.
  Lane Gp2.
- **Pogo, and a jump that takes the bot under its health floor:** "should be
  a planned jump to a health pack near by for fast mobility". Read as: a
  planned jump may leave the bot under `RocketJumpHealthFloor` (90) when it
  lands near a health pack the bot believes is there, or a cabinet it may
  use, and the bot takes the pack next; such jumps are for getting
  somewhere fast, not only for reaching a height. A jump that lands
  nowhere near health keeps the floor. A faster-firing launcher is not
  asked for. Lane G9.
- The rooms proposal (Q-R1 to Q-R4 in
  `plan_bots_teams_tf2_part_g_rooms.md`) and whether the Course branch
  stays on ordinary control-point maps: "explain more". Explained the same
  day, and then ruled:
- **Control-point maps:** "bots should do both on control point maps, jump
  to objectives some times and fight with team other times". The Course
  branch's send-to-the-nearest-point stays, and a bot on such a map also
  spends part of its time with its team's fight, changing between the two.
  Lane Gc.
- **The rooms proposal:** Q-R1, "can we make a nav3d file per sealed space
  and load it on demand?"; Q-R2 (a room is built when a bot first gets
  there), "sure"; Q-R3 (the two big courses on demand), "we need to find a
  way to simplify the nav mesh for these sealed sections"; Q-R4 (this plan
  may edit the idle TF2 AI session's files), "yes". Q-R1 and Q-R3 change
  the proposal: lane Gr measures a file per sealed space and ways to make a
  big room's navigation smaller, and the proposal is revised from its
  numbers before anything is built into the game.

### Open questions, each with a recommendation (taken as recommended 2026-11-10; Q-G6's first item since allowed)

| | question | recommendation |
|---|---|---|
| Q-G1 | does the store learn from every player's state as the server has it, or only from what bots perceived? | every player's, for map knowledge (as watching a demo); anything live stays perceived |
| Q-G2 | HL2MP too? | no: TF2 only, as ruled |
| Q-G3 | on by default? | on for a played server, off for every tool launch |
| Q-G4 | ship the native SQLite library? | yes |
| Q-G5 | the jump map's promise | tiers 1 and 2; tier 3 reported, not promised |
| Q-G6 | map-tools, two items: a navigation for a map taller than 255 voxels, and a brush hull trace only if the voxels prove too coarse for the simulator | the first **allowed 2026-11-10 and done there** (`.navhull`, G.7); the second not needed so far (the simulator matches the game on `ss_ai_tf_jump`, G.14) and asked for only if a real map shows it |
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

## G.14 Where each phase stands, and what the lanes found (2026-11-10; the table's last rows 2026-11-11)

| phase | state | on main at | notes |
|---|---|---|---|
| G0 | landed, but for the ctf_2fort baseline, which waits for the map-tools lock bump | `d84babc87` | G.13 |
| G1 | landed: ten of the eleven pieces of G.2.1, each lifted to the core and used by TF2; **prefire not started** | `1367ecdd7` (24 commits) | HL2MP's recordings untouched; ai leg 2,303 → 2,479 facts |
| G3 | landed | `811e9cb94` (8 commits) | the store, its feed and the tools' switch; no decider reads it yet |
| G5 step 3 | landed | `67177cd88` (8 commits) | the simulator and its flights fixture |
| G5 steps 2 and 4, the pure half | landed | `05b70a850` (9 commits) | the recipe, the pilot, the search, the place-finding pass; nothing in the game calls them yet |
| G1p | landed | `2bb63c854` (6 commits) | a decide's cost found and cut to about a sixth, with a cost fact |
| G2 | landed 2026-11-11 | `f9cfd3a9e`, call sites `dc5a59665` | G.15 |
| G5c-1 (the jump in the game, without the graph), the `.navhull` adapter | landed 2026-11-11 | `2b1e7bff0` (20 commits), `d30d0c343` (5 commits) | G.15; the fizzles are lane G5d's |
| the recording format 7 → 8 | next | | announced to the TF2 AI session before it starts |
| G4, G5 steps 5–7 (edges in the graph, G5c-2), G6, G7 | not started | | G7's graph has to be a room's (G.15) |

**G1, the feel layer.** Lifted: `BodyKnowledge` (an unseen actor's believed
speed is its class's run; HL2MP's stays exactly `190f`), `AimHeightHold.Hold`,
`AimWander`, `TargetScore`, `KnownActor.BelievedAt`, `PathGlance`,
`FriendLine`, `Streaks`, `RetreatRule`, `SoundSearch`. TF2's use is in
`TFDecider.Feel.cs`; the owner's three files took call sites only. What it
changed that a player will meet: TF2 bots' settled miss is about 0.83 of
`aimErrorDeg` RMS where the uniform draw's was 0.58, and 2.5 times that on
first sight, so hit rates will read lower (not measured in a game yet); a
target is the sighted carrier of the bot's flag first, then anyone sighted
(nearness, rank, grudge, a Medic +0.35, the held target × 1.25); a duck is
followed down over a few decides, not held (TF2's hull middle moves 10 u,
under `AimHeightHold`'s 14); a Search branch (0.4, or 0.55 for a defender
within 1,200 u of its flag).

**G1 raised the decide's cost.** `/ai/counters` every 10 s for 240 s, debug,
12 bots on ctf_2fort (the nav at maptools.lock), one run each, before
(`d84babc87`) and after (`1367ecdd7`): `ai.brain.cpu.us` 1,188 → 2,986 a
decide (medians), `ai.brain.us.max` 31,178 → 99,300, the pool's busy time
144,496 → 380,392 µs a second over four threads, steps in flight at most
6 → 39, none skipped, stuck reports 254 → 449. The after run had the busier
box, so the main thread's rows (`ai.main.us` 392 → 811) are not yet
separated from load. The cost does not grow with the graph (the same on
`ss_ai_tf_jump`'s 15,679 cells as on 2fort's 164,799). Lane G1p finds and
cuts it and leaves a cost fact.

**What the cost was (G1p).** Not the feel parts: the bots spread a belief of
where their own **teammates** are, which nothing in TF2 reads. Timed offline
by part over a recorded 12-bot session (`tools/ai-replay cost`, thread CPU
time, debug), the predictor's spread was 99 % of a step before and after G1
(1,031 of 1,041 µs at 190 u/s, 2,183 of 2,201 at class runs); five hops in
six were a teammate's; `perf` puts 92.6 % of samples in
`PositionPredictor.Hop`. G1's own share is the class run, 1.57 times the
hops; every feel part together is 5 to 10 µs. The cut (`BotMemory`'s Lost
and Heard begin a belief for an enemy only; a teammate's last seen and heard
place is still remembered): offline 2,201 → 342 µs a step, live on ctf_2fort
with 12 bots `ai.brain.cpu.us` 2,221 and 2,469 → 377 and 428 (two runs each,
the walled-off nav), under the pre-G1 1,188; release 80 µs a step. It is
also why TF2 cost seven times HL2MP before G1. `TFDecideCostTests` bounds a
decide at 600 µs of thread CPU (it reads about 177; red at 826 to 1,109 with
the memory as it was), and `TeamBeliefTests` holds the rule by hop counts.
Settled with it: the main thread's rows had risen with the box's load, not
with G1 (the same code read `ai.main.us` 332 at a load of 19 and 761 at 54;
`ai.main.cpu.us` is the row to read); and the stuck reports' rise could not
be tied to G1 (the same code gave 548 and 280; 62 to 87 % of a run's reports
are one or two wedged bots repeating, so first-time reports are what to
count). Tools left: `tools/ai-cost live|record`, `tools/ai-replay cost`.

**G3, the store: what it chose where G.3 did not say.**

- A snapshot is one (map, bsp checksum, mode); the checksum is the first 16
  hex of the bsp's SHA-256; `context` is (stage, role, class), with TF2's
  role the team number, its class the `TFClass`, its stage the control points
  Blue owns.
- The feed reads every living player from the engine and has hurt and death
  hooks of its own, because the brains' snapshots hold only each bot's own
  view. It is the one reader of all players' truth, hands it only to its own
  worker thread and never to a brain (census entries in
  `PerceptionCensusTests` and `TFPerceptionCensusTests`, accepted by the TF2
  AI session). This is Q-G1 as recommended.
- **A worker thread of its own, not the brains' pool**: a store call can
  wait out the busy timeout, `ai_sync 1` has no pool, and the pool drops its
  queue at stop, where the last flush must run. Nothing runs on the game
  thread (`ai.experience.main_thread_work` 0 in a live run).
- It samples every 7 ticks, only living players on a side, only while a
  round is played.
- Hazard is deaths over seconds present; every count is a real, sums not
  means; a row unwritten for ten half-lives is deleted; a kill counts 1.0 in
  `sight_line` whoever made it.
- `ai_experience_db default` is the default file; the launcher marks every
  `-background` or `-instance` game `-toolgame` (`tools/local_launch.py` and
  `make headless` carry it), and under it the default file is off while a
  named path is used. `make run` passes neither, so a played game learns.
- A file that is newer, foreign, unreadable or has no `meta` is refused with
  a line and the game keeps its experience in memory for the process.
- SQLite's native library is resolved from the mod's managed directory; it
  loads inside the game on Linux (a live run wrote 191 `cell_stat` and 262
  `look` rows). Microsoft.Data.Sqlite 10.0.12.

**G5 step 3, the simulator.** `VoxelColumns` and `VoxelMoveWorld` (a hull
sweep exact against the nav's column boxes, which are up to a voxel fat
sideways and a voxel low overhead), `SoldierMover` (`GameMovement` under the
mod's own rules), `BlastJumpSim` (the mod's own blast functions, none
copied). The differential: 13 flights recorded from the bot
(`src/ai/tests/recordings/ss_ai_tf_jump.airec`, 300 s, debug), each replayed
from its takeoff state with its own commands, agree to 0.0001 u in apex and
landing place and to the tick in air time; halving the push or zeroing the
muzzle offset goes red (the latter moves the landing a median 140 u). One
core does about 6,600 to 7,300 flights a second in release (medians of five runs, by thread CPU and by wall clock) and about 810 in debug
(`tools/ai-tf-jump sim`). **What it does not show:** walls inside a voxel on
a real map; the test map is built on the nav's own grid.

**G5 steps 2 and 4, the pure half (G5b).** `JumpRecipe` (takeoff, landing,
run-up, pitch, the view's yaw against the way at the shot, the shot's delay
against the jump, ducked); `JumpPilot.Step`, a pure function a command from
the recipe, its own small state and the body to that command's buttons, view
and move (in the air it scans wish directions through the game's own
`MoveMath.AirAccelerate`; a bot's moves are analog in the view's frame, so it
turns the view only for the shot); `JumpSearch` (at most 160 simulated
flights, coarse then refined, with robustness over 20 perturbed trials);
`JumpEnvelope` (the reach, asked of the simulator at run time); `JumpPlaces`
(the pass). All under today's physics, the blast at its cap, which F231
lowers, so none of it is pinned by a fact:

- **The rocket must be behind.** With the view down and forward, as
  `TFAbilities`' row has it, a standing jump goes 384 to 576 u backwards in
  the simulator; with the view 180° off the way it goes forward. That is why
  G.13's jump lost to the walk.
- **A forward jump beats the walk.** On the run's 1,200 u stretch: 1.785 s
  in the air against 5.010 s walked, 36 % (45 % from standing at the
  run-up's start), where C.3.2 asks for under 70 %. The recipe: a 160 u
  run-up, the view 35° down and 180° off the way, the trigger 3 commands
  before the jump, crouched; it lands in 19 of 20 trials and costs 28.7
  health.
- **The pass on `ss_ai_tf_jump`:** 12 candidates, a recipe for all 12 (the
  three ledges, both gap platforms, the slab's top, the gap both ways, four
  hops between ledges), each landing in 19 or 20 of 20 trials, 2.1 to 2.9 s
  and 28 to 38 health; nothing under the slab. 1,738 flights; 2.3 to 3.6 s
  of one core in debug, 0.8 to 1.2 s in release for a first pass (a fresh
  process flies about 1,500 to 2,300 flights a second, not the 6,600 a warm
  one does); an estimate of 9 to 14 s in release for a map ten times the
  size.
- **Its stated limits:** one candidate a pair of level stretches; takeoff
  only straight out from the middle of an edge; no landing along a stair's
  side; no flat-run jump for speed; the pilot does not know where a ledge's
  face is, so a takeoff close under a wall is fragile.

**A TF2 port bug the simulator's recording showed**, reported to the TF2 port
session and taken into its lane F231: a TF2 player's bounds are HL2's
32 × 32 × 72 box (`SsPlayer.ShadowHulls` reads `MoveConstantDefaults.Stock`),
so `DamageForce` sees a volume 2.56 times too small and a Soldier's own blast
is always pushed at the 1,000 u/s cap. That, not the formula, is the 928 u
jump of G.13; stock is not yet compared. With it: the launcher reads the
shooter's crouch one command late. When F231 lands, the flights fixture is
recorded again (`tools/ai-tf-jump record`) and G.13's jump numbers measured
again.

**Follow-ups the lanes left**, each to be done or ruled:

| item | where | state |
|---|---|---|
| `AiRecordFormat` 7 → 8, one serialised change: what a TF2 session needs to replay through TF2's own tree as the game decided (class, objectives, team zones, abilities, bodies: without them a TF2 replay has no flag to go for and believes 190 again), the experience snapshot's hash and the tick its slot is replaced at, a ground flag and a self-blast event (G.6) | `src/ai/core/Record/AiRecordFormat.cs` | agreed with the TF2 AI session as one change; announce before starting |
| `PositionPredictor.Hop` is the brains' cost that is left, and in debug every vector operator in it is a call | `src/ai/core/Predict/PositionPredictor.cs` | a scalar rewrite, or the core built optimised in debug: a build-policy call |
| the pool's main cost is now the drive steps (`ai.drive.us` 116 to 158 µs, 66 a second a bot) | `src/ai/server/Apply/` | measure before the next cost work |
| the two facts that run the whole place-finding pass take over 2 s in debug | `tools/test-tiers.tsv` | `tools/test-tiers measure --write` |
| `tools/ai-tf-jump sim` deletes every json under `bin/<config>/ai-jump/` | `tools/ai-tf-jump` | the pass's reports come back when its facts run; narrow the delete |
| the Descent session makes a memory's predictors lazy (one made when a foe is first known) | `src/ai/core/Memory/BotMemory.cs` | done, `fc5a752f2` |
| a bot's own blast ends its commitment | `src/ai/server/Senses/CombatTap.cs` | done with G2 (`7a60bf4d9`, call site `fd7d9d717`) |
| "not while glancing" in the jump's rule | `JumpPays`, the TF2 AI session's until G5 reworks it | done with G5c-1 (`JumpAllowed` in `TFDecider.Jump.cs`) |
| the package carries `e_sqlite3` for 22 platforms, 33 MB | `bin/<config>/bin/managed/runtimes/` | trim to the two the game runs on |
| the store's native library on Windows | the load fact in the Windows CI | written, unverified |
| CLAUDE.md's launch paragraph does not name `-toolgame` | CLAUDE.md | the user's to allow |
| 34 mutation arms were stale before G1 (their text is gone or not unique) | `tools/ai-mutations` | list with the G1 lane's `g1-arms.py`; re-point or retire |
| the skill files' notes name `Hl2mpDecider.AimSettleExtra` | `hl2mp_skills.json:14`, `tf2_skills.json:14` | now `AimWander.SettleExtra`; check the files are not hashed first |
| HL2MP's `SurviveUtility` reads a never-hurt tick 0 as a hit at tick 0 | `Hl2mpDecider.cs` | a fix changes replays; left |
| the believed class comes only from sight | `CombatTap`'s scoreboard row has no class | P7's enemy-class learning will want it |
| a brief's test command | lane briefs said `make managed-test`; AI facts are `make ai-test FILTER=...` | fixed in later briefs |

## G.15 What landed on 2026-11-11, and what it found

Everything below is on local main; nothing is pushed.

| what | on main at | notes |
|---|---|---|
| the simulator under the TF2 hull | `4dd689e38` | F231's physics, the flights fixture recorded again, one rule fixed |
| the feel switches | `0ea7e4ec0` | six cheat convars, for A/B runs |
| G2 | `f9cfd3a9e`, call sites `dc5a59665` | personality, roles, things in view, voice, dominations |
| the `.navhull` adapter | `d30d0c343` (5 commits) | a map's `.nav3d` first; `jump_academy2` does not fit (below) |
| G5c-1, the jump in the game | `2b1e7bff0` (20 commits) | the pilot flies it; faster than the walk; three in ten fizzle on ctf_2fort |
| the jump map by room, measured (lane Groom) | `681473095` (3 commits) | a region source the derivation runs on unchanged; `tools/ai-replay navrooms`; nothing in the game calls it |
| G5d, the fizzles | `065ad9fac` (2 commits) | the cause is the mod's rocket, not the AI; the jump's record and tally say when a blast was felt |
| G5c-2a, blast links | `49a67813c` (10 commits) | jumps as links beside the graph, found on a worker, cached, searched under a reach and a health gate; no bot has the reach |
| G5c-2b, the Soldier takes a link | `35075b8db` (12 commits) | the flow field, the follower and the tree learn the links; the 250 u ledge by a link in 10 of 10 tries; only the Soldier has the reach |
| G4a, the experience's first two uses | `082d91263` (6 commits) | a route costs more where the bot's team died, by its Cunning; a bot with nothing to look at looks where its team looked; an empty store changes nothing |
| G5e, a link's results, and chains | `cd030b14c` (6 commits) | each try of a link is written to its `link` row; a link under 30 % after five tries is left out at the next snapshot; a route's own link is not spaced from the jump before it, so two in a row work; the skip after a reload found and fixed |
| G7a, a course of our own | `9be3e5946` (9 commits) | `ss_ai_tf_course` (seven stages, six jumps, the highest +350 u) and a Course branch of the tree; one Soldier finishes it from its spawn with no errand, 5 of 5 and 5 of 5 after a reload; it heals for a jump it cannot pay for |
| G4b, the experience's other uses | `f55f7441c` (6 commits) | a bot checks the place its side keeps dying from; a defender holds where players held (fed from what players do); an attacker chooses its door into the flag room by what worked; an empty store changes nothing |
| G6a, the store learns players' jumps | `c60c9008c` (4 commits) | a person's jump the navigation has no way for is seen command by command, judged off the game's thread and written as two `link` rows with no identity; nothing reads them yet |
| G7b, a second rocket in the air | `810defc31` (12 commits) | a recipe may fire a second rocket at a floor the flight passes over; the search, the pass, the cache and the store carry it; `ss_ai_tf_double`, a course only it finishes; a teleport back from a ledge over the start is a reset |
| Gp, prefire | `52a6bcbdb` (4 commits) | HL2MP's prefire is the core's (`src/ai/core/Brain/Prefire.cs`), its replays unchanged; a TF2 Soldier puts one rocket at the floor of the opening an enemy it lost is believed behind (`ai_tf_feel_prefire`); a rocket at a place the store says kills the side, after the feed says it just did again |
| G6b, a bot flies a jump a player showed | `8363e9918` (5 commits) | each Soldier blast jump of the snapshot is fitted by the search (the person's shot only a hint) and joins the level's links after the shape links; kept by the bots' own tries; `ai_tf_links_skip` for the proof |
| G7c, a wall jump | `eaa8efe55` (6 commits) | a Wall family: a jump up whose second rocket is fired at a wall beside the flight; cache revision 3, store recipe format 5; `ss_ai_tf_wall`, a course only it finishes; a link's start is walked onto to within 4 u; the second rocket's deadline shot is gone |
| Ge, a course's ends, the friend line, the gate's step | `bed4d5221` (10 commits) | a capture area whose point the level does not place is a course's end (`TFCourseEnds`), reached for a life, easiest title first; `ss_ai_tf_ends`; `FriendLine` judges a teammate's body where the shot passes it, HL2MP's replays unchanged; `tools/tf2-gate --steps bots-jump` |
| G8, the practice tool, a shown wall jump | `8547149f8` (7 commits) | `tools/ai-jump-practice` searches a level's weak and missing jumps offline and writes `link` rows of source Practice; the learn pass flies each in the level's simulator before it joins the links (`JumpPractice`); a shown jump past one rocket's rise is asked a wall jump too; `tools/ai-tf-jump`'s tries start in a running round |
| Gp2, prefire's clip rule | `cade08806` (2 commits) | a bot that rocket jumps prefires with one rocket loaded (user, 2026-11-13); one decide is one clip refusal in the counters, not two |
| Gr, a file per sealed space and a smaller room (offline) | `3ae8d86c7` (9 commits) | a hull navigation cut into a file per room and a room index; a derived graph with a teleport's cells left out and flat floor merged in blocks; tools `ai-replay navsplit` and `navcells`. Nothing in the game calls it |
| G9, a jump to a health pack | `7b3cc109c` (15 commits) | a planned jump may leave the bot under the floor of 90 when a pack or a cabinet it may use is within 384 u on foot of the landing, it lands with 50 or more, and the place pays the rest; the bot takes the pack next; `ai_tf_feel_jumphealth`; `ss_ai_tf_pack`, the gate's sixth jump map. Pogo measured and left out |
| Gc, both on a control-point map | `9e7e450ef` (20 commits) | with a teammate on a level of control points and no flag, a bot holds the points or the team for a stretch; the team stance gathers at a rally called where the team is and pushes the point together (the rally and push of the main plan's C.6.1); `/ai/points`; `cp_granary` joins `TF_NAV_MAPS` |

**The physics changed under the jump (F231, the TF2 port's lane).** A TF2
player now carries TF2's hull (48 × 48 × 82, 62 ducked), not HL2's box, so a
crouched Soldier's own blast pushes at about 650 u/s and no longer at the
1,000 u/s cap. A jump peaks near 462 u where G.13 measured 928; the reach the
simulator states is a rise of 580 u and a range of 1,040 u. The flights
fixture was recorded again and showed one wrong rule in the simulator: the
muzzle used the command before's duck (`BlastJumpSim.Fire()` now reads the
mover's duck after the move). The differential agrees to 0.0001 u again.
G.13's and G.14's jump numbers are the old physics'.

**The feel switches.** `ai_tf_feel_carrier`, `_search`, `_aim`, `_retreat`,
`_friendgate` and `_glance` (cheats, default 1, read when a bot's decider is
made) turn one feel part off for a run. Two arms on ctf_2fort (carrier-first
targeting off, the Search branch off) each took 3 captures in 560 s, as the
all-on runs do. **Withdrawn:** this plan's coordinator had told the TF2 AI
session that carrier-first targeting cost captures; that session's all-on runs
took 3, 3 and 1, and the cause of the drop was the old HL2 hull wedging bots
after their jumps.

**G2.** Personality (leanings that weigh the root's scores), the role
allocator (`RoleSplit` → `RoleCounts`, dealt by the TF2 AI session's
`CtfRoleSplit`), the things in view (projectiles and buildings), voice lines
as ability one-shots, a nemesis from the HUD's domination mark, and a bot's
own blast no longer ending its commitment. Proof runs with the call sites in,
ctf_2fort, 560 s, debug: captures 3, 3, 4 at 6v6 and 5, 3, 4 at 4v4, against
5, 3, 4 and 4, 4, 4 before them. Voice in for a caller who is not the carrier
waits for a "go to a teammate" branch. With it the Descent session made a
memory's predictors lazy (`fc5a752f2`), keeping G1p's cost fact green.

**The `.navhull` adapter.** map-tools writes a `.navhull` (boxes with a
ceiling per built width; PR #152, MapFormats 1.0.7), which holds a map too
tall for a practical `.nav3d`. `NavHullNavSource` answers the derivation's
questions from it, with no change to the derivation; `NavLoader.Order` takes a
map's `.nav3d` first and its `.navhull` only when no `.nav3d` binds;
`tools/ai-nav-build --hull`, `make ai-nav-tf HULL=1`; `NavHullDifferentialTests`
compares the two graphs on the maps a tree has both of; `tools/ai-replay
navsize` says what a load costs step by step. Found on the way and fixed in
map-tools PR #153 (not merged at this writing; navhull v2, MapFormats 1.0.8):
`FitsAt` on sloped brushes. When it merges the lock bump is the TF2 AI
session's, v1 files are refused, and the differential is run again on
ctf_2fort before the order is switched to prefer `.navhull`.

**`jump_academy2_rc9a` does not load as one graph.** Its `.navhull` builds in
84 s and 2.5 GB (26.9 MB; 3.9 M boxes). Read through the adapter it is 12.7 M
pseudo-leaves; derived, 6.9 M cells and 55.7 M edges in 7,594 connected parts
(the largest 793,849 cells; ctf_2fort is 164,799 in all), 62 to 65 s of CPU
and 11.9 GB at the peak in release, a 1.1 GB cache file, and 51 s more for the
sight table (`tools/ai-replay navsize`, `d30d0c343`). The derivation runs on
the game's thread at level load. So G.7's nav row is not met by a file format
alone: the graph has to be a room's, made off the game's thread when a bot
gets there. That is a proposal of its own (measured first: how many rooms,
how big), since it reaches the derivation's callers, which are the TF2 AI
session's.

**Measured (lane Groom, `tools/ai-replay navrooms`, release, at
`4f3041e73`).** A room is a region of connected free space in the `.navhull`
(`NavHullRegions`, 0.36 s). The map has 369; 28 hold a spawn, a teleport, a
destination, a cabinet or a capture area: a hub, 20 courses, 6 small chambers
and a closet. **A region is a course of 4 to 15 jumps, not one jump**: of 264
teleports, 188 land in their own region (a jump's reset) and 66 in another (a
course's end). `NavRegionSource` shows one region of a source and
`NavGraphFromNav3d.Derive` runs on it unchanged. The first three courses and
the hub derive in 0.09 to 2.5 s of CPU and are each at or below ctf_2fort's
size; pogo takes 11.1 s and 3.8 GB at the peak, hard sync 21.5 s and 2.4 GB.
On ctf_2fort one region holds every spawn. The proposal that follows from it
(the level holds one room's graph and swaps it when a teleport carries the
bot) is `docs/plans/plan_bots_teams_tf2_part_g_rooms.md`, awaiting the user's
review: nothing of it is started.

**G5c-1, the jump in the game.** A third way to run an ability, a
**program** (`AbilityRun.Program`: the applier steps it a command at a time
with the bot's own state, and the view still goes through `HumanMotion`).
TF2's is `RocketJumpProgram`, which flies a recipe with `JumpPilot.Step`: it
runs up, turns the view to the shot's line, stands for a view that is late or
calls the jump off, and launches. Where a recipe comes from: `JumpDesk` works
on a worker thread, never in a decide; `JumpBook` is what the decider asks
(`TFDecider.Jump.cs`: nobody in sight, health over the floor after the jump's
cost, a rocket to spare, not carrying, not mid-glance, the stretch ahead along
the route long and straight enough with the headroom, and the simulator's
recipe faster than the walk). `/ai/jumps` and `ai.jump.*` in `/ai/counters`
say what each try did.

- On `ss_ai_tf_jump`, today's physics, debug: the jump leg 3.93 s against the
  walk's 5.11 s (77 %), 14 of 14 jumps landed, the simulator's landing within
  0.1 u of the game's. Forward jumps are planned at `ReachShare` 0.7 of the
  reach; 0.9 would give about 60 % of the walk at an apex of 385 u for 40.5
  health. Left at 0.7: a ruling.
- On ctf_2fort, 560 s, debug, at `4c6cb719c` (one run each): 4v4 5 captures,
  113 stuck reports; 6v6 4 captures, 138. No collapse and nobody wedged after
  a takeoff, which was the TF2 AI session's gate.
- **Only one stretch of ctf_2fort gets a plan** (takeoff near
  (-794, -1476, 256), 1.23 s in the air against 3.07 s walked). The refusals
  in a 6v6 run: the route turns 2,752, too short 2,038, no route 799, no
  recipe 86, a ceiling 6. Jumps up to a ledge wait for the graph's jump edges
  (G5c-2).
- **Three in ten fizzle there.** Of 28 jumps in the two runs: 19 landed within
  0.2 u of the prediction, 1 landed 484 u off, 8 fizzled, nearly all with a
  rise of exactly 72.2 u against the plan's 158, which is a crouch jump with
  no blast. The program is not told whether the launcher can fire. Lane G5d
  has it.
- **That reading was wrong (lane G5d).** The bot fires on the command the
  recipe asks for every time. In the TF2 mod a rocket explodes on the first
  teammate it touches; stock passes teammates for a rocket's first 0.25 s
  (`tf_weaponbase_rocket.cpp:354-369`, `tf_player.cpp:10159-10178`,
  `baseprojectile.cpp:212-230`), and the port's `TFRocket` has no team rule
  (its pipebomb and stickybomb do). Every jump on ctf_2fort is a BLU Soldier
  leaving its spawn with a teammate behind it, where the recipe fires the
  rocket: it goes off 26 to 37 u from the eye, the push is level, and only
  the crouch jump lifts the bot. A fizzle costs 31 to 81 health against a
  jump's 27. ctf_2fort, debug, 4v4, 1,500 s with a round restart every 40 s,
  at `932dae47c`: 15 of 47 launched jumps fizzled, each with a blast felt 1
  or 2 commands after the trigger; with stock's rule patched into the mod
  (not committed), 0 of 39. The three older runs hold 11 fizzles in 33 jumps
  (the 8 of 28 above is two of them). Reported to the TF2 port's session;
  nothing is fixed on the AI's side, because nothing there is wrong. What
  landed is evidence: a jump's record carries the command its blast was felt
  on and the leap it made (`felt_after_shot`, `leap` in `/ai/jumps`), and
  `ai.jump.fizzled.felt` counts the fizzles that felt one.

**Two things the landings taught about the lanes' own facts.** A voice line
and a rocket jump are both ability one-shots, so a fact that counts
`ActionKind.Ability` counts either (`TFField.BeginsJump` reads the row's
name); and a game launch runs `make debug`, so a branch that stops compiling
after a rebase starts no game, which reads as a failed run and not as a build
error.

**G5c-2a, blast links.** G.5's step 5 says an edge type inside the graph.
Built otherwise, and why: the finder is TF2's and the graph is generic, so
an edge in the graph would make the graph's hash change with TF2's physics,
rebuild every map's cache and put the pass on the level load. The links are
a set **beside** the graph, as the choke map is (`NavLinks`, `NavLinkSlot`
in `src/ai/core/Nav/NavLinks.cs`): found by `JumpPlaces` on a worker after
the level loads (`JumpLinkWork`, started by `TFJumps`), cached in a file
named after the graph's (`.navgraph.blast1`, refused on another revision,
graph or inputs), and published through a slot. `NavPathfinder` takes them
only for an agent whose `NavObstacleState` has `JumpReach.Blast` and a
`LinkHealth` that covers the link; without the reach a search is the old one
exactly. A link costs its seconds, 0.03 s a hit point and (1 − robustness)
× 4 s (`NavLinkCosts`). Nobody has the reach yet, and a fact holds that.

- **The place finder was wrong on a real map, and is fixed.** Its first run
  on ctf_2fort gave 21 links, 16 of them taking off where no player stands
  (clip and cliff tops, the river), two landing hundreds of units below where
  the flight came to rest. Now it proposes takeoffs only where the graph
  leads from a spawn, goes on in rounds from where its links land, links only
  to a cell at the rest's height, and tries up to three takeoffs a pair of
  levels.
- **Numbers** (`tools/ai-replay jumps`, one thread's CPU, at `955f3526f`):
  ctf_2fort 49 candidates, 8 links, 1,263 flights, 2.11 s debug and 1.05 s
  release; `ss_ai_tf_jump` 11 candidates, 11 links, 1.36 s and 0.81 s. In a
  running game (debug, at `974f22107`): 8 and 11 links found, 2.6 s and 1.9 s
  of a worker.
- **What it is worth on ctf_2fort: little.** Of 56 routes between the spawn
  rooms, flags and capture zones (`tools/ai-replay jumps --routes`, a Soldier
  with 110 health to spend), 18 take a link, all one of two jumps on the blue
  side at x = 8, the best saving 5.4 s of 67.7. The battlements-to-roof jump
  is found on both sides, and the roof is a dead end in the graph. On
  `ss_ai_tf_jump` 9 of 11 landings are reached only by a link.
- **What the next lane has to know:** `NavFollower` routes by a `FlowField`,
  not by `NavPathfinder`, so the field has to learn the links; and a TF2 map's
  entity lump is LZMA-packed, which `MapEntities.Read` does not unpack, so in
  the game `LoadedNav.Entities` is empty on shipped TF2 maps (the finder's
  seeds come from `ITFLevelEntities`).

**G5c-2b, the Soldier takes a link.** G.5's step 6, built as follows.

- **Who may.** The level's links ride on each player's obstacle state
  (`NavObstacleState.Links`, beside `Reach` and `LinkHealth`), set by the
  game's profile (`INavProfile.LinksFor`). TF2 gives them to the Soldier
  only, with its health less the jump's floor of 90 to spend, refreshed each
  drive step. The fact `OnlyTheSoldierHasTheBlastReach` replaced the one
  that said nobody has it.
- **The route.** `FlowField` relaxes the links into a cell after its edges
  and names the link a cell steps over (`LinkFrom`, `NavEdgeType.Link`,
  which no graph holds). An agent without the reach builds the old field
  exactly (0 cells differ on ctf_2fort). A build over 162,712 cells costs
  the same with and without links within the box's noise (26.9, 25.5 and
  26.1 ms of thread CPU, debug, `tools/ai-replay jumps ctf_2fort --routes`,
  at `d405c72ab`).
- **The follower only reports.** It leads the bot to the link's start (the
  recipe's run-up start), holds within 48 u of it and says so
  (`NavLinkStep`). The tree begins the jump, under `JumpAllowed`'s rule
  unchanged plus "health after the blast is over the floor"; the drive sees
  the one-shot and takes its hands off until the bot is down.
- **A bot never waits at a takeoff.** No begin within 66 ticks, a jump that
  fails, or health that no longer covers the link: the link is left out of
  that bot's route for 533 moves and the route goes round. A goal given up
  while its links were left out is tried again when they are back.
- **The report crosses on the main thread** (`AiBots.TakeDrives` to
  `INavLinkReports`, which is `TFJumps`), stamped with the tick the drive
  step was handed on; a decide reads the newest stamped before its own tick,
  so the same report whatever the thread timing.
- **Proof** (`tools/ai-tf-jump links --tries 10 --others --reload`, debug,
  one bot, at the lane's tip `17384775c`, tag `archive/tf2ai-G5c2b-pre-land`):
  the 250 u ledge 10 of 10 by a link, median 294.5 ticks, landing 0.8 u from
  the simulator's (worst 1.6); the seven other link-only landings 7 of 7;
  after a `changelevel` (links from the cache) 10 of 10. Again at the
  landing, 5 tries each: 5 of 5 and 5 of 5 (`971149fb2`).
- **ctf_2fort, 560 s, debug, one run each (a sample, not a rate).** 4v4: 5
  captures and 119 stuck reports (before: 5 and 113), 17 jumps of which 8
  links, none fizzled. 6v6: 3 and 4 captures, 270 and 141 stuck reports
  (before: 4 and 138), 20 and 26 jumps of which 20 and 16 links. 123 of the
  270 were four bots under a flag dropped 180 u over the floor, where no
  link is.
- **Open.** The first try after a `changelevel` skips the ledge's link at
  its start (the book advises nothing there) and goes round by another:
  seen in both full runs, cause not found. The jumps' 6 s spacing refuses
  the second link of a chain, so that route goes round. `ai_tf_errand` (a
  cheat command) sends a bot to a place by its own tree; it is how the tool
  proves a link, and a recording made under it would need its goals.

**Other sessions' changes under this part, same day.** The service-free
navigation moved to `src/ai/nav/` (`SourceSharp.Ai.Nav`, `f8f9fd1a1`,
`df9240fc0`; pure renames, namespaces unchanged; the ctf_2fort and
`ss_ai_tf_jump` graph hashes are the same before and after, derived fresh).
The recording format is 8 (`a108dbc72`: a header flags byte and a bot's home
point, for Descent), so this part's TF2 replay step is 9. `AiEventKind` ends
at 61.

**G4a, the experience's first two uses** (G.4's rows "route cost" and
"looks"; 2026-11-12).

- **Route cost.** One field a team (`NavCellHazard`, 4 bytes a cell): a
  cell's value is its bin's deaths per second present × 20 s. Built on a
  task when a snapshot is loaded (`ExperienceRoutes.Build`) and published
  with the snapshot, never before it (`ExperienceRouteSlot`). It rides on a
  player's obstacle state as the links do (`INavProfile.HazardFor`), and
  `FlowField` costs a step into a cell at its own cost × (1 + min(3, weight
  × hazard)): a cost, never a wall, at most four times the walk. The weight
  is the bot's Cunning over 10. On the facts' two-route graph the choice
  flips at weight 0.375: Cunning 4 and up go round.
- **G.4's text was out of date:** it put the field on `NavPathfinder`. A
  bot's route is the follower's flow field, so the field is there.
- **Looks.** `LearnedLook`: in periods of 0.6 to 1.5 s drawn from the bot's
  seed, a bot on its way with a goal, nobody in sight, no jump under way and
  no glance held looks along its bin's most looked-along yaw (at least 2
  counted seconds, a share of 0.3, within 100° of its way), with a chance
  from its glance rate (Easy about 0.3, Hard 0.65). The view moves through
  the motion layer. Alertness reaches a TF2 tree only as the glance rate; a
  curve of its own is a follow-up.
- **With no experience nothing changes:** an empty store, a weight of 0 or
  another graph's field gives the field of before, cell for cell, and no
  look intent; HL2MP keeps no store.
- **What it costs** (debug, ctf_2fort, 162,712 cells, thread CPU of the
  build task, at `a3b0e1257`, the lane's tip): 20 to 35 ms a snapshot, 1.3 MB
  for both teams.
- **What it is worth so far: little, as expected from two bot runs.** With
  a private store filled by a 560 s and a 420 s run of 12 bots (about 62 bot
  deaths; a bot counts a quarter), a Cunning 8 bot's walk from the red spawn
  to the blue flag goes from 51.96 s to 52.76 s; the most any of the 12
  spawn-and-flag routes changes is 1.2 s. Over the 420 s run, 3,815 decides
  looked along a learned yaw.
- **ctf_2fort, 560 s, debug, store empty, one run each (a sample):** 4v4 4
  captures and 114 stuck reports (before: 5 and 119); 6v6 4 and 156 (before:
  3 and 4, 270 and 141).
- **Follow-ups.** Each decider builds its own table of looks when the
  snapshot changes (one a team, built with the routes, would be better);
  `ITFObjectives`' capture zones sit at the origin and a flag's row is where
  the flag is now, so the route tool measures between spawns and flag homes.

**G5e, a link's results, and chains** (G.5's step 7 and what G5c-2b left
open; 2026-11-12).

- **A try.** A link's jump whose rocket went. Landed within 64 u of the
  link's touch: a try and a success, with its seconds and the recipe's
  health. Any other end after the launch: a failed try. Nothing is added for
  a jump called off before the launch, for a fizzle felt 0 to 2 commands
  after the trigger (the mod's rocket going off on a teammate), or for a
  body the game moved over 256 u in one command (`JumpEnd.Moved`: a round's
  restart in mid-flight, which the tool had counted as a landing).
- **The row.** The Soldier's class, the bins of the recipe's start and
  landing, kind Blast, source `Solver` (who found the link; the tries are
  always a bot's), and the recipe's numbers as 42 bytes (format 1), so a
  link found again with another recipe starts a row of its own and G.6 has a
  recipe to fit. The key comes from the recipe, not from cells.
- **Leaving a link out.** `LinkTrust`: five or more tries and under 30 % of
  them successes, in the snapshot's decayed rows. The mask (`NavLinkMask`,
  stamped with the link set's hash) is built with G4a's routes on the same
  task and published with the snapshot; it rides on
  `NavObstacleState.LinksShut` and `NavLinks.Admits` refuses a shut link, so
  the field, the pathfinder and the follower all leave it out. With an
  empty store the field is today's, cell for cell.
- **Chains.** A link the route takes is begun under every part of the jump
  rule but the 6 s spacing and a held glance; a stretch jump keeps the whole
  rule. What stops a chain jumping a bot to death is the health, checked per
  link as the bot stands at it (at least 90 after the blast).
- **The skip after a reload** was the spacing too: after a `changelevel`
  the bot is not frozen, rocket-jumps out of its spawn by its own tree, and
  the spacing from that jump (and a glance) held back the link it then
  reached until the follower's 66-tick wait ran out. In the first level the
  bot is frozen until try 1, so only a reload showed it.
- **Measured** (debug, `tools/ai-tf-jump`, the lane's tip `4882cf044` or as
  said): the 250 u ledge by its link in 10 of 10 tries, and 10 of 10 after
  the reload, whose first try took 291 ticks (before: 1,409); two links in a
  row to the same ledge in 12 of 12 tries, median 570 ticks. With a private
  store a run wrote one `link` row at 9 tries and 9 successes. With one
  link's shot spoiled in a test build it fizzled 13 times, the next
  snapshot left out 1 of 11 links, and in 12 more tries the bot began it
  zero times.
- **ctf_2fort, 560 s, debug, store empty, one run each (a sample):** 4v4 5
  captures and 115 stuck reports (before: 4 and 114); 6v6 4 and 153
  (before: 4 and 156).
- **The brief was wrong about a place that needs three links:** the place
  at (1624 1240 366) is reached by one link; the old 1,680 ticks was a
  detour forced by refusals.
- **Open.** In 6v6 a link fizzled three times with the blast felt 11 to 18
  commands after the trigger; those count against the link and are not
  explained. A decide at a link's start is refused while the launcher
  reloads after the jump before (15 and 6 decides in the two runs; no wait
  ran out because of it). Counters: `/ai/jumps` `ai.jump.link.refused.*`,
  `ai.jump.link.tries`, `successes`, `unwritten`.

**G7a, a course of our own** (G.7's "the course as objectives", "failing"
and "health and ammo", on our own map; 2026-11-12).

- **The map.** `ss_ai_tf_course`, generated (`TFAiTestMaps`), one 2,048 u
  room: seven 256 u stages on pillars over a pit, laid as a U, tops at z
  656, 756, 956, 956, 1256, 1256 and 1606. Six jumps: rises of 100, 200,
  300 and 350 u over a 128 u gap, and two level gaps of 400 and 560 u. Each
  stage has a cabinet's zone (team 0, in a bay off the walking line on the
  middle stages), a control point that needs the one before, and a
  `trigger_teleport` under its jump back to the takeoff stage. The pit is
  640 u under the first stage, past the pass's cut, so no link leaves it.
- **+350 is the single rocket's limit.** At +400 the pass proposes the jump
  and the search finds no recipe that lands in 60 % of its trials. The
  pass's reach is the simulator's envelope × 0.9 (rise 522 u, line out 936
  u), not G.5's 600 and 400.
- **The pass went on only four rounds from the spawns** (`MaxRounds` 4), so
  a course's fifth stage had no link. It is 32 now, and a pass still stops
  at the first round that plays no new cell; `ss_ai_tf_jump` (11 links) and
  ctf_2fort (8) come out the same, field for field. The course: 8 links
  found for the 6 it needs (the two gaps also back the other way), 1.14 s
  of thread CPU, debug.
- **What tells a bot it is a course:** control points and no flag, read
  from the HUD feed. `TFObjectiveFeed` now gives a point's teams that may
  act as the teams the rules let capture it now
  (`IControlPointRules.TeamMayCapturePoint`, the padlock). The `Course`
  branch (`TFDecider.Course.cs`, weight 0.5, under a fight) runs for a bot
  that rocket jumps; its goal is the nearest point its team may take now,
  and the links and the jump rule do the rest. **It therefore also runs on
  any control-point map**, where it sends a Soldier to the nearest point
  it may capture: the main plan's control-point phase should take it over
  or rename it.
- **Health.** A field now says the least health of a link it left out for
  the bot's health alone (`FlowField.HealthShort`), the follower reports
  "no way, and what it needs" (`NavLinkStep.NoWay`, `Need`), and lets a
  link it dropped for health back in as soon as the health covers it. The
  branch then waits in a cabinet's zone (its team's or team 0) or walks to
  the nearest zone or pack on its own floor. A route with no way for any
  other reason for 20 s: the bot says it cannot go on and leaves the course
  for 60 s.
- **A fall** leaves the goal where it was. A body the game puts back at the
  jump's start ends the jump as `JumpEnd.Reset`, a failed try in the store.
  **The one fall seen in the game ended `Moved` instead; the cause is not
  found** (lane G7b has it).
- **Measured** (debug, `tools/ai-tf-jump course --runs 5 --reload`, one
  Soldier, no errand, at `9c4d1289c`, the lane's tip): 5 of 5 finished in
  2,218 to 2,351 ticks, and 5 of 5 after a `changelevel` in 2,210 to 2,414.
  Every jump landed first try in all ten runs but one fall. Health at the
  takeoffs of a typical run: 200, 173, 140, 200 (after a bay), 160, 200.
  A private store got 6 `link` rows, every try a success.
- **Controls** (same build): the 250 u ledge 5 of 5 and 5 of 5 after a
  reload; ctf_2fort 560 s, 8 bots, one run: 5 captures, 14 pickups, 92
  stuck reports (before: 5, 12, 115).
- **The gate's `bots-jump` step** should run `tools/ai-tf-jump course
  --runs 5 --reload` on a built `ss_ai_tf_course`; it exits 1 unless 4 of 5
  finish before and after the reload.
- **What `jump_academy2_rc9a` needs beyond this** (from an entity dump in
  a scratch file, not a committed tool: a lead): its 19
  `trigger_capture_area` name points that do not exist, and it has one
  `team_control_point`, at the hub, so the feed shows one point where the
  map has 19 course ends; the ends would be read from the capture areas'
  volumes. Its 29 cabinets have team 0 or none, which the tree's own heal
  rule (`HealPlace`) skips and the Course branch accepts.

**G4b, the experience's other uses** (G.4's rows "danger places", "spots"
and "outcomes"; 2026-11-12). Every TF2 bot is a Soldier, so nothing here
waits on another class.

- **Danger places.** `ExperienceDangers`, once a snapshot from a side's
  sight lines: a bin's danger place is the killer place (a 3×3×3
  neighbourhood of bins) with at least 2 counted deaths and 0.4 of the
  area's, at least 256 u away. A bot with no target, a goal, no jump under
  way and no sound being looked into draws once on entering such a bin
  (not the same place again within 8 s) and holds a corner-check look on
  it: how often and how long by Alertness (`dangerLookChance` 0.2 to 0.95,
  `dangerLookSeconds` 0.5 to 1.4). It comes before the glance and the
  learned look.
- **The shot was not built.** The plan's "fires at a place its team died to
  before it sees who is there": the tree knows of a teammate's death only
  from the kill feed in its memory and knows where only if it saw the
  teammate lately.
- **Spots.** Fed on the game's thread by `HoldTracker`: a hold is a player
  staying within 128 u for at least 4 s (cut at 30 s), with the kills made
  and the death taken while holding, kept only near the side's own
  objective (`IExperienceHolds`; TF2: within 1,200 u of its flag's home or
  a point it owns), written as `spot` rows of kind Hold at the feed's
  weight (a bot a quarter). A defender re-picking its spot takes a learned
  one within 1,024 u of its flag at its `spotTrust` chance (Discipline,
  0.1 to 0.95), drawn by seconds × (1 + kills) / (1 + deaths), and faces
  the yaw held there.
- **Outcomes.** A route family is a door of the enemy flag's room
  (`RouteFamilies`: a search back from the flag bounded at 1,200 u walked;
  3 doors a flag room on ctf_2fort). A try is an attacker's run at the flag
  at home: 1 for a pickup, 0.5 for a death within 512 u of the flag, 0.25
  for a death past a door, 0 before any; written in full as `outcome` rows.
  With rows for its side a bot draws a door once a run at its `familyTrust`
  chance (Cunning) by seeded Thompson sampling, goes to the door, then to
  the flag: one more field build a run, none a decide.
- **With an empty store every intent is the one of before**, decide by
  decide, in the facts. Each use draws from a stream of its own.
- **In the game** (debug, ctf_2fort, 480 s, 12 bots, private store,
  `/ai/experience/use`, at `6dfaf78e4`, the lane's tip): with hand-written
  sight rows added to a store two runs had filled, 200 danger checks, 82
  learned-hold picks and 14 door draws over 39 runs. **Bot-only play makes
  almost no danger place** (0 after 108 kills, 2 for one side after 242):
  the thresholds want the same death from the same place, which is what
  people do. The "failed door is drawn less" effect in the game was 2 of 6
  draws against 4 of 8: no evidence either way; the facts hold it (the
  door that worked is drawn 27 to 30 times in 30).
- **ctf_2fort, 560 s, debug, store off, one run each (a sample):** 4v4 5
  captures, 12 pickups, 167 stuck reports (before: 5, 12, 115); 6v6 3, 9
  and 156 (before: 4, 16, 153). With the store off none of the three acts.
- **Left.** A `sight_line` row counts 1 whoever died, so bots dying to bots
  teach the danger table in full. Each decider builds its own tables once a
  snapshot (one a side, built with the routes, is the follow-up, as for the
  looks). A decider's list of finished tries grows for the session.

**G6a, the store learns players' jumps** (`c60c9008c`, 2026-11-12; the
gate at that tip: `make check` rc=0, 14 legs; the ledge 3 of 3 and 3 of 3
after a reload; three boots with kills and no fault lines):

- **Seen.** `ShownJumpFeed` (the generic server, hook order First + 915)
  hands every command of a person, with the body as the command finds it
  and the game's word on blasts (`IExperienceJumps`; TF2's reads
  `SsPlayer.State` and `TFBlastJumpState`, no engine call but one at a
  landing), to a pure tracker, `JumpWatch`. A bot is never watched. HL2MP
  registers no `IExperienceJumps`, so nothing is watched there. With the
  store off, learning off or no round being played: one check a command.
- **Kept for judging:** 0.4 to 6 s in the air, begun by the player's own
  doing (a jump press in the 3 commands before, or its own blast), landing
  dry and walking and not on a player. **Thrown away, each counted:** short,
  long, a fall, pushed (hurt or blasted by another), kicked (a leap in speed
  in the air nothing of its own explains: an airblast, a shove), moved by
  the game, died, wet, a ladder, on a player.
- **Judged on the experience worker** (`ShownJumpJudge`): no cell within
  24 u of the feet; the same cell or bin; one of the level's links already
  goes there (landing within 1 bin, start within 3); a walk of no more than
  2.5 times the straight line, or one that saves under 128 u; else learned,
  as "no way" or "long walk". The plan's "no walk, jump or drop link to the
  landing bin" was read as a route on the graph, not a direct edge.
- **The rows** (source `Human`): a count row keyed by class, takeoff bin,
  landing bin and kind (blast 0, sticky 16, double 17, crouch 18, plain 19)
  with the one-byte recipe 4, each sighting a try and a success with its
  seconds and health; and, the first time, an exemplar row (recipe format
  3, counts zero): the two points, four velocities, the air commands, the
  health and the player's commands from 0.5 s before the takeoff through
  the landing, 7 bytes each (the server does get them:
  `IPlayerCommandObserver`), at most about 3.1 KB. `LinkTrust` matches exact
  keys, so it never reads a human row. The reader is
  `ExperienceView.ShownJumps()`. A link row's recipe formats are now 1 (a
  solver's), 2 (with a second rocket), 3 and 4.
- **In the game** (debug, `ss_ai_tf_jump`, `5a9b0df1c`, a private store): a
  bot standing in for a person (`ai_experience_jump_standin`, a cheat, off
  by default) flew the level's link to the 250 u ledge three times: 3
  learned with the level's links left out of the judging, the count row at
  3 tries and 120 health, the exemplar 1,063 bytes with the takeoff and
  landing within 8 u of the finder's own link; with the switch off nothing
  was watched; judged against the level's links, 3 "known" and no row.
- **Cost** (debug, ctf_2fort, thread CPU on the game's thread, `22deec1b1`):
  1.0 to 1.25 µs a watched command (13 and 25 players watched); the
  existing feed's cost unchanged.
- **Left for the fit.** The row has no weapon, no health at takeoff and no
  positions per tick. On ctf_2fort 52 of 147 watched landings had no cell
  (crouch hops onto props and ledge edges) and learned jumps were rare (2
  in 120 s of 24 bots). An airblast or a shove gives no hurt and no blast
  state: it is caught only as a kick. An exemplar row is never touched
  again, so it is forgotten after ten half-lives while its count row lives;
  the next session's first sighting writes another.

**G7b, a second rocket in the air** (`810defc31`, 2026-11-12; the gate at
that tip, with G6a under it: `make check` rc=0, 14 legs; the ledge 3 of 3
and 3 of 3 after a reload; `ss_ai_tf_course` 3 of 3 and 3 of 3;
`ss_ai_tf_double` 3 of 3 and 3 of 3; three boots with kills and no fault
lines):

- **The simulator was measured first and needed no fix** (debug,
  `0169cbc51`, a test build firing a second rocket on every stretch plan of
  `ss_ai_tf_jump`): 14 of 14 landed with two rockets in the game and in the
  simulator, the landing gap median 0.0 u and worst 6.2 u; the recorded
  commands flown again through the simulator, 11 flights, 0.00 u.
- **Where a second rocket pays.** Not over open floor: at the launcher's
  0.8 s refire a jump up is 450 to 580 u over its floor, far outside the
  121 u a self blast reaches, and off the takeoff floor it adds no range.
  It pays **at a floor the flight passes over** (a beam, a ledge's top).
  So a recipe's second shot (`JumpSecondShot`) is a place, not a time: the
  pilot presses again once the launcher is ready and the body has come over
  the place the found flight fired from, or at a deadline 12 commands
  later, never from under that floor and only with the view within 3° of
  straight down and behind.
- **The search and the pass.** A `Double` family, run only when asked and
  only when no single jump is worth keeping; the envelope gains a `Lift`
  (what a blast 32 u over a floor adds: 247 u) and the pass proposes up to
  0.9 × (580 + 247) = 744 u, anything past the single cut of 522 searched
  for two rockets alone. The links' cache is revision 2; a two-rocket
  recipe's store row is format 2 (70 bytes); a two-rocket link is begun
  with three rockets loaded, and its health is both blasts (78 on the
  course).
- **Decision (the implementer's, 2026-11-12, on the lane's evidence): a
  two-rocket jump is searched under a tighter error model than a single.**
  Under the singles' (start ±16 u, view ±3°, trigger ±2 commands) no beam
  layout of about 300 probed reached the 0.6 a link needs; the first shot's
  pitch alone lost 18 of 40. A link is flown standing, placed within 8 u,
  the trigger on the pilot's own command, so a two-rocket jump's first shot
  is modelled at start ±8 u, view ±1°, trigger exact, and the program holds
  that shot to 1° for every bot. In the game the jump then landed 10 of 14
  times it was launched and not cut (debug, `0acf42c3c`); a fall is put
  back, retried and written to the store. All 4 failures pressed the second
  trigger at the deadline, the first blast never having carried the body
  over the place: firing at the deadline never paid, and dropping it is a
  follow-up.
- **`ss_ai_tf_double`:** S0 at 656, S1 at 848 (+192 over a 128 u gap, one
  rocket), S2 at 1,424 (+576), with a beam 48 u wide and 192 u long at
  1,232 on the way, narrower than the pass's shortest landing and with a
  reset over its top. A second map because `ss_ai_tf_course`'s room has no
  height left over S6. The lane's runs (debug, `0acf42c3c`,
  `tools/ai-tf-jump course --map ss_ai_tf_double --runs 5 --reload`): 5 of
  5 and 5 of 5; landing gaps to the simulator 0.0 to 1.6 u.
- **The reset.** G7a's "fall that ended Moved" was the tool's own
  `mp_restartgame` moving the bot in the middle of a jump; the tool now
  counts a move apart from a fall. A real case was new: a failed second
  rocket comes down on the beam and is put back at S1 from above the
  start, which the rule "fell below the start's floor" missed. A move to
  the start is now a reset when the body fell below its floor or is still
  moving where it was put (a teleport keeps the body's speed; a restart's
  spawn is at rest). A test build whose second rocket never fired: 16
  tries, 14 resets, the store row at 16 tries and 0 successes.
- **Nothing else moved** (debug, `0acf42c3c` against `f55f7441c`):
  `ss_ai_tf_jump`'s pass 11 links and 1,379 flights both, its thread CPU
  within 2 %; ctf_2fort 49 candidates → 58 (9 two-rocket, none flown: no
  standing start), the same 8 links, the pass about 1 % slower; ctf_2fort
  560 s with 8 bots 5 captures, 11 pickups, 120 stuck reports (a sample).
- **Corrections to earlier notes.** One rocket's limit is about 580 u of
  rise, not +350: onto a ledge across the line singles land +400 at
  robustness 0.85 to 0.90; the course's +400 failure was its own geometry.
  G7a's table put S6 at 1,656; it is built at 1,606.
- **Found outside the lane, not fixed** (`src/server`): at a round's
  restart the bot's move back to its spawn touched a reset trigger on the
  way and ended on S1, as if the game sweeps the body through triggers when
  it moves it. Unproven; the map was changed to keep S0 off that line.

**Gp, prefire** (`52a6bcbdb`, 2026-11-12; the gate at that tip: `make
check` 14 legs, the ledge 3 of 3 and 3 of 3 after a reload, both courses 3
of 3 each side, boots with 18, 18 and 26 kills and no fault lines).

- **The lift.** `LineAsks` (the bot's one channel for asking the game about
  a line), `PrefireBelief.Of` (seen within 2.5 s, heard within 1.5 s, or a
  habit spot), `PrefireOpening` (the nearest place to the belief the sight
  table says the bot sees) and `PrefireRun` (the state machine) name no
  game. HL2MP calls them; its 27 replay facts pass with no recording
  re-made, and halving the chance in the core turns four red.
- **TF2's rules.** A launcher in hand; no enemy in sight; a target lost that
  the core believes; the branch Fight, Defend, Guard or Search; no jump under
  way; a rocket for it and, for a bot that rocket jumps, two kept back; the
  place 320 to 1,500 u off, within 60 degrees of the view, the belief still
  inside its seconds when the rocket lands; only down a line the game
  answered clear; one rocket, then a wait of 2 s. The friend gate gets the
  place level with the eye as its aim (see "found" below).
- **The danger shot.** The kill feed names a teammate dead; the bot heard
  the scream within 1 s or saw the teammate within 2 s; the place is in or
  next to a bin the store has a killer place for: one chance in 8 s to fire
  at that place's floor, also on the way to the flag.
- **In the game** (debug, ctf_2fort, 8 bots, 560 s, store off, one run an
  arm, so spread and not effect): on, 5 captures, 11 pickups, 133 stuck
  reports; off, 4, 8, 102. On: 16 chances, 5 aims, 1 rocket, 0 hits; decides
  refused by the clip rule 239, far 87, near 40, turn 34, old 25, blocked 3.
  **The clip rule is what limits it**: whether prefire may spend the rockets
  kept for a jump is the user's to rule. The danger shot on a seeded store
  (480 s): 69 teammate deaths in the feed, 14 placed, 3 at a danger place, 1
  chance, 0 rockets: it works and almost never fires between bots.
- **Found, not fixed there.** `FriendLine` judges a teammate's body at the
  shooter's eye height, so a shot aimed down can pass a teammate unjudged
  (lane Ge has it). The kill feed's event carries no weapon. `tools/ai-mutations`
  had 9 stale anchors at the base. The feel switches are seven now: a
  faithful replay records them.

**G6b, a bot flies a jump a player showed** (`8363e9918`, 2026-11-12; the
gate at that tip, both lanes stacked: `make check` 14 legs, the ledge 3 of
3 and 3 of 3, both courses 3 of 3 each side, a show-then-learn run on a
fresh store 5 of 5 by the learned link, boots with 19, 16 and 26 kills and
no fault lines).

- **Where it enters.** `TFJumps` reads the experience's snapshot each frame;
  a new one of the level's map, once the shape links are published, starts a
  learn pass on a task (`JumpLearning`, `ShownJumpFit`).
- **How.** Each Soldier blast jump, most seen first, is an ask of the jump
  search from the exemplar's takeoff to its landing; the person's shot is a
  `JumpHint` whose 3 x 3 neighbourhood is flown before the grids and kept
  only as a grid's find is. The bot never flies the person's numbers.
  Verdicts: Fitted, Kept, Few, Capped (12 a pass), NoCell, Same, Known,
  NoStand, NoRecipe (under 0.6), Rests, Twin.
- **Where it lives.** After the shape links in the same published set, so a
  shape link's index never moves; marked `JumpLink.Shown`; not cached (each
  jump is searched once a level); its tries are ordinary `Solver` rows, so
  `LinkTrust` shuts a bad one.
- **In the game** (debug, `ss_ai_tf_jump`): with the pass told to skip the
  250 u ledge (`ai_tf_links_skip`, its own cache key) and a store from a
  session that showed the jump, 10 of 10 tries reached the ledge by the
  learned link, the landing 0.9 u (worst 2.6) from the simulator's. Empty
  store: no link, 0 of 3. Switch off: all three shown jumps Known, none
  learned. A learned link whose plan was spoiled (a test build) failed 8
  tries and the next session left it out.
- **Cost.** 139 and 160 flights, about 120 ms of a worker's CPU a jump,
  once a level; the publish 2 us under the lock; the shape pass unchanged.
- **Open.** A round restart's tries reach the snapshot one round late. The
  practice tool is designed, not built (lane G8 has it). The slow first try
  is the tools', see G7c below.

**G7c, a wall jump** (`eaa8efe55`, 2026-11-12; the gate at that tip: `make
check` 14 legs, the ledge 3 of 3 each side of a reload, `ss_ai_tf_course` 3
of 3 each side, `ss_ai_tf_double` and `ss_ai_tf_wall` 5 runs each side with
rc 0, a show-then-learn run 5 of 5 by the learned link, three boots with no
fault line).

- **What a wall gives.** In the simulator a first shot at a wall adds little
  over the Up family (711 u against 677 at 64 u from the wall), so no
  first-shot family was built. A second rocket at a wall beside the flight
  lifts an Up jump's apex from 580 u to 980, 920, 830 and 733 u with the wall
  32, 48, 64 and 96 u off the line; with no wall the best is 530.
- **The family.** `JumpFamily.Wall`: the Double's recipe with
  `JumpSecondShot.Wall` set and the same pilot rule. The pass asks
  `Double | Wall` for a rise past 0.9 of one rocket's; the search tries the
  floor double first. Grid: 144 cells, at most `WallMaxFlights` 480. Two
  blasts' health (71 to 87), three rockets loaded. Cache `Revision` 3, store
  recipe format 5 (3 and 4 are a person's rows).
- **Why doubles failed.** Every failed two-rocket jump of lanes G7b, G6b and
  G7c (17 of 17) launched 7.0 to 8.0 u past its start: the placing stopped at
  the 8 u slack's edge. `RocketJumpProgram.PlaceNear` (4 u) walks the body
  nearer before it stops; after it, doubles 10 of 10 first try. The second
  trigger's deadline shot was not the cause; it is gone anyway
  (`JumpPilot.SecondNow` fires only as the body comes over its place).
- **The slow first try** is not the jump code and not the map change: the
  preround freeze (about 560 ticks), TF2's waiting-for-players restart in the
  middle of a try, and the bot carrying the flag between `mp_restartgame` and
  the try. The tools should wait for the round; not done.
- **Pogo: designed, not built.** One blast straight down 32 u over a floor
  while falling adds 630 to 680 u/s for 45 to 50 damage; a chain needs about
  0.81 to 0.85 s of air against the 0.8 s refire, and the tree's health floor
  of 90 leaves one extra blast. It needs a faster refire or a health budget
  under the floor for a planned link: a ruling, not a search.
- **Open.** `ShownJumpFit` does not ask the Wall family. The pass's
  two-rocket cut still uses the beam's lift (744 u), though a wall lifts
  more. `FindTwo` keeps a refined hit's second view as the first.

**Ge, a course's ends, the friend line, the gate's step** (`bed4d5221`,
2026-11-12, stacked on G7c with one commit joining them; the gate at that
tip: `make check` 14 legs, `tools/tf2-gate --steps bots-jump` PASS with the
ledge, `ss_ai_tf_course`, `ss_ai_tf_double`, `ss_ai_tf_wall` and
`ss_ai_tf_ends` each 5 of 5 before a reload and 5 of 5 after, three boots
with no fault line).

- **Ends.** `jump_academy2_rc9a` has 19 `trigger_capture_area`s whose
  `area_cap_point` is a course's title and names no entity, and one locked
  point at the hub. `TFCourseEnds.Of` reads such areas from the level's
  placements; a level with one is a course of ends and its points are none.
  The bot takes the easiest end by its title's word (basic, easy, medium,
  hard, expert), then the nearest; an end is reached when the body's box
  overlaps its volume, remembered for a life (a death, or a respawn by a
  round's restart); no way for 20 s sets an end aside for 60 s. A level with
  no end runs the code it ran. `/ai/course` says it per bot.
- **Proof.** `ss_ai_tf_ends`: a hub and three courses (a 150 u ledge, a
  250 u ledge, stairs and a 400 u gap). 5 of 5 each side of a reload, the
  three ends in 1,553 to 2,427 ticks; with the ends handed as none, 0 of 2
  and the bot never left the floor.
- **The friend line.** `FriendLine.AngleToBody` takes the body's point
  nearest the shot's line, not the point at the shooter's eye height, and a
  crouched teammate's lower top when the caller gives one. HL2MP is on the
  new rule and its 27 replay facts pass with no recording re-made. Gp's
  level-point work-round is gone. On ctf_2fort (8 bots, 560 s, one run) fire
  held for a teammate went from 23 of 1,436 shots to 100 of 2,110
  (`/ai/friendgate`, new). One mutation is told apart by no fact: handing
  every aim level with the eye.
- **The gate.** `tools/tf2-gate --steps bots-jump`, named only: it runs
  `tools/ai-tf-jump` on its own port for each of the five maps and needs 4
  of 5 each side of the reload, read from the run's JSON (the tool's exit
  code does not check the ledge's reload side). `tools/tf2-gate` is the TF2
  port session's; the edit is named in its commits.
- **What the real map still needs.** Its navigation and its teleports as
  links (every course is entered and left through a `trigger_teleport`), a
  route distance for "nearest", the techniques most rooms want, and ports of
  `trigger_catapult`, `func_nogrenades` and `filter_tf_class`. One area has
  `team_cancap_2 0`, which the rule does not read.

**G8, the practice tool and a shown wall jump** (`8547149f8`, 2026-11-12;
the gate at that tip: `make check` 14 legs, `tools/tf2-gate --steps
bots-jump` PASS with the five maps each 5 of 5 before a reload and after,
a show-then-learn run 5 of 5 by the learned link, the practice tool's dry
run on that run's store rc 0, three boots with no fault line).

- **The tool.** `tools/ai-jump-practice <map> [--db] [--below 0.8]
  [--dry-run]` (the replay program's `practice` verb): no game. It opens the
  store (never makes one), runs the shape pass, the game's `ShownJumpFit`
  and `LinkTrust` over the snapshot, and re-searches the shown jumps judged
  NoRecipe or Rests, the links the bots gave up or under `--below`, and the
  pass's candidates with no link, with `JumpPracticeTool.Rich` (grids of
  12 by 5 and 9 by 7 against the game's 5 by 3 and 4 by 3, 40 trials, up to
  3,200 flights). It writes a row only when the game's own reader would
  take it. A write while a game has the store open is held by facts.
- **The reader.** `JumpPractice`, in the learn pass, before the shown jumps'
  fit: each Soldier blast row of source Practice is read back
  (`TFLinkExperience.TryRecipe`), flown unperturbed (it must make its
  landing and rest on a cell), then 40 perturbed trials (0.6 to keep; 20
  trials put a 0.775 recipe at 0.6 on ctf_2fort). Verdicts: Taken, Unread,
  Spoiled, Rests, NotBetter (a level link the bots have not given up costs
  no more), Twin, Capped (12 a pass). Taken links join after the shape
  links and before the shown ones; a shown jump a practice link already
  makes is `Practiced`, not searched. It is in the learn pass, not the
  shape pass, because the shape pass starts before a snapshot exists and
  its cache holds no store. With no practice rows the pass is the one of
  before.
- **What it gained.** At 0.8 no jump of `ss_ai_tf_course` or ctf_2fort is
  improved: ctf_2fort's 0.6 link's practice recipe costs more than the
  level's own link to the same landing, and `ss_ai_tf_course`'s one
  unlinked candidate is an 832 u gap with no recipe. At a named 0.95 the
  course's 0.9 link onto the top was found again at 0.975 (2.07 s against
  2.145 s planned), taken by the game and flown 5 of 5; the control store
  copied before the tool's run has no practice link.
- **A shown wall jump.** `ShownJumpFit` asks `Both | Double | Wall` past 0.9
  of one rocket's rise. On `ss_ai_tf_wall`, with `ai_tf_links_skip` over
  S2's landing: shown 3 times, fitted as a Wall link (robustness 0.7, 369
  flights), 5 of 5 by it; an empty store, 0 of 5. No new recipe byte.
- **The tools' first try.** `tools/ai-tf-jump links` and `trust` wait for
  `RoundRunning` and hold the bot with an errand to its own place between
  tries: the first try 555 ticks → 336, the restart's two `Moved` ends gone.
  Steady tries read about 45 ticks longer (they start standing now).
- **A replay needs** the snapshot's practice rows, as it needs the shown
  jumps' rows.

**Gp2, prefire's clip rule** (`cade08806`, 2026-11-14; the gate: `make
check` 14 legs at the landing tip; at the lane's base the five jump maps 5
and 5 of 5 each side of a reload, boots with 20, 12 and 28 kills and no
fault lines).

- **The rule.** `TFDecider.PrefireAllowed` asks for one rocket loaded, for
  every bot. At a link's start with too few, the jump waits for the reload.
- **The counter.** Both of a decide's asks (the belief shot and the danger
  shot) counted a clip refusal; a decide is one now. Lane Gp's 239, and the
  base's counts here, were up to twice what happened.
- **Measured** (debug, ctf_2fort, 8 bots, store off, three runs of 300 s
  before and three after, sums): chances 24 → 50, aims 10 → 16, rockets
  1 → 4, hits 1 → 3, self hurt 1 → 0. Every run after fired at least one;
  no run before fired more than one. Near and far refusals rose (44 → 131,
  66 → 192): the reach is what limits prefire now. Captures, pickups, kills
  and stuck reports are inside the spread.
- **No jump went short.** A link refused for the clip or for a reload: 0 in
  all six runs. Link tries 13 → 23, landed 13 → 20; the three that failed
  were two blasts that lifted too little and one called off before its
  shot, and a link is begun only with its rockets loaded. No rule was added.
- **Found.** `tools/ai-mutations` names no TF2 assembly among its subjects,
  so a TF2 rule's mutation cannot be one of its rows (the proofs are the
  lane's own logs). Nothing counts a rocket fired at a teammate; only the
  friend gate's holds are counted (0 in all six runs).

**Gr, a file per sealed space and a smaller room** (`3ae8d86c7`,
2026-11-14; `make check` 14 legs at the landing tip). Offline only: the
user's Q-R1 and Q-R3 answers to the rooms proposal, measured. The numbers
and the design they changed are in `plan_bots_teams_tf2_part_g_rooms.md`
(R.1a, R.2), which waits for the user's go-ahead.

- **A file per room works, cut on our side** (`NavHullSplit`,
  `NavRoomIndex`): 28 room files, 22.1 MB together, the same graph bit for
  bit as the region of the whole file; the first course opens in 0.31 s and
  131 MB against 5.3 s and 762 MB. The index is 262 KB.
- **A big room made small** (`NavGraphReduce`, `BrushModelBoxes`): the cells
  inside teleport-back brushes left out and flat floor merged 4 by 4 take
  pogo from 1.11 M cells to 236,000 and hard sync from 865,000 to 230,000.
  Neither lowers the first derive; a 32 u voxel does (1.9 s, 778 MB), with
  floors snapped to 32 u, unmeasured against the simulator.
- **Found.** The sight table is rebuilt at every load, never cached.
  `MapEntities.Read` cannot read a compressed entity lump.
  `MapEntities.Hurt` takes a trigger's volume from its model's bounds, so a
  `trigger_hurt` of several brushes is overstated on any map (a fix changes
  those maps' graphs: to be measured per map with the rooms build).

**G9, a jump to a health pack** (`7b3cc109c`, 2026-11-14; the gate at the
lane's base: `make check` 14 legs, the six jump maps 5 and 5 of 5 each side
of a reload, a show-then-learn run 5 of 5 by the learned link, boots with
20, 26 and 24 kills and no fault lines; at the landing tip `make check`
again, the pack course 3 and 3 of 3, two boots).

- **The rule** (`JumpHealthRule`, the user's ruling in G.11). Plain: health
  less the blast is 90 or more. By health: a pack the bot does not believe
  gone, or a cabinet of its team or of every team, within 384 u along the
  graph of the landing; landing health (after the blast and the worst fall)
  50 or more; that and what the place gives back 90 or more. A search
  spends on such a link `min(cost, cost + fall - min(pays, 40))` against a
  budget of health less 90, which is exactly the two rules. The bot takes
  the place next, in the air too.
- **Proof.** `ss_ai_tf_pack` (four +350 jumps, a small pack by the third's
  landing): on, 5 of 5 and 5 of 5 after a reload, the third jump begun at
  116, landed at 74, the pack taken 0.81 s later; off, 0 of 2, the bot
  says it cannot go on. A hurt bot going for a pack: jumps, 3.7 to 4.8 s;
  off, walks, 5.9 to 7.0 s.
- **In play it is inert so far.** ctf_2fort, 8 bots, three runs of 300 s
  each way: no link lands by health and no jump by health was begun. The
  rule allows; nothing seeks. That is the follow-up (lane G9b).
- **Pogo: measured, left out.** A chain of two blasts covers 2,205 to 2,296
  u in 4 s against a run's 960 and the best single jump's 1,339, at 145 to
  156 health, and lands where planned 0.05 to 0.20 of the time under the
  search's own error (0.8 is asked). Not flyable at the stock refire.
- **Found and fixed, each with a fact.** A heal place within 16 u of a map
  pack is that pack (after a changelevel the placed entities are 1.3 u
  off). A link may begin during a reload with its rockets loaded. A heal
  that broke off starts its patience again (every TF2 bot). Two dev routes
  shared a hook order, which failed every dev API request; the hook-order
  fact covers those routes now.
- **A replay needs** each bot's `Feel.JumpHealth`, the published spends (or
  the heal places as the placed entities gave them), each stretch plan's
  heal places, and the item beliefs with `ExpectedBack`.

**Gc, both on a control-point map** (`9e7e450ef`, 2026-11-14; the gate at
that tip: `make check` 14 legs, the six jump maps 5 and 5 of 5 each side of
a reload, boots on cp_granary, ctf_2fort and the duel map with 20, 16 and 8
kills and no fault lines). The user's ruling in G.11: "do both ... jump to
objectives some times and fight with team other times".

- **Where.** A level of control points and no flag, with no course ends,
  for a bot with a teammate on its scoreboard. A bot alone, or a level
  with ends, runs the Course branch as before (the courses 3 and 3 of 3).
- **The points** (`TFDecider.Points.cs`): an own point being taken, or one
  with an enemy seen on it; else the nearest point the team may take, by
  the Course way; standing on it.
- **The team** (`TFDecider.Rally.cs`): gather, then push. A bot calls a
  rally (the core's callout) at the middle of the teammates it can place,
  moved toward the point while out of the enemy's last-seen sight; one that
  places nobody calls none and takes the points. When half the team's
  players (at least 2) are within 384 u, or the rally's time is up, the
  caller calls the push and they go for the point together, a rocket jumper
  by the route's jumps, each waiting for one who falls behind. After a
  push a bot takes the points; one that dies in a push rejoins it.
- **The stance** is held 30 to 60 s, drawn from the bot's own stream, the
  chance leaning by Aggression and Teamwork and capped when half the placed
  teammates stand on a point; an own point being taken pulls a team-stance
  bot in. Nothing reads another bot's stance or the server.
- **Measured** (debug, cp_granary, 8 bots, store off, three runs of 300 s;
  base, first pass, second pass, final): the points' share of bot-seconds
  -, 0.71 to 0.81, 0.23 to 0.30, 0.54 to 0.60; team-stance bot-seconds with
  two or more teammates within 600 u (the base over all bots) 0.35 / 0.49 /
  0.28, 0.25 / 0.24 / 0.17, 0.29 / 0.58 / 0.43, 0.77 / 0.18 / 0.62;
  captures 21, 12, 10, 14; rounds won 2, 2, 1, 0 (in 900 s: 3, 0, 2, 1);
  the share of gathering spent on the way, second pass 0.75, final 0.23 to
  0.41. **Rounds are the miss**, on small counts; the spread between runs
  is wide.
- **Found.** cp_gorge does not play with bots in the port (two bots off the
  navigation in their spawn; a team frozen after a forward respawn). A
  5-point map's spawns keep the team of level start, so "the spawn's side"
  is not a side. C.6.1's call gate of Teamwork 4 stops every "normal" bot
  (Teamwork 3) from calling.
- **Left of C.6.1 for the main plan:** the no-progress trigger and "a
  winning team never regroups", the cooldown and the lowest-slot caller,
  the choke-map spot, the class rules (the Medic's charge, Spies and
  Snipers), humans' pushes, and its scripted facts.
- **A replay needs** nothing new: the say slot, the callouts and the
  seeded stance stream are recorded; to check, that the view carries every
  point's `MayAct` and state at every decide.

**Still to do, in order:** the recording format 8 → 9, after the user's
ruling on the rooms proposal, since a room change would be a record of its
own (what each lane said a faithful replay needs is in the coordinator's
notes); the jump map's rooms, its navigation and its teleports, once the
proposal is revised from lane Gr's numbers. The rows of G.4 that need an
Engineer, a Sniper, a Spy or a class pick wait for the main plan's P7.

**Ruled 2026-11-13** (G.11) and landed, above: prefire's clip rule (Gp2),
a jump to a health pack (G9), a file per sealed space measured (Gr), both
behaviours on a control-point map (Gc). Under way: making a jump to health
sought, not only allowed (lane G9b). Next: the rooms build, on the user's
go-ahead.

**Ownership, from 2026-11-11.** The TF2 AI session has not run since 07:18
that day. G.9's serialised files have no second session to be serialised
against: this part's edits to them are announced to the user and named in
their commits.

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
