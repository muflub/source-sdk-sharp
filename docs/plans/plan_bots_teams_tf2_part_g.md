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

**Still to do, in order:** G5c-2 in two lanes (a: blast links a route search
can take, found for a whole map off the game's thread and cached, running
since `065ad9fac`; b: the follower, the decider's rule, the applier telling
the decider a program runs); the recording format 7 → 8, after the user's
ruling on the rooms proposal, since a room change would be a record of its
own; G4 (the experience's cost field on the pathfinder); G6; the jump map's
rooms, when approved; the prefire lift.

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
