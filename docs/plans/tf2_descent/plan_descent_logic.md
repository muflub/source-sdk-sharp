# plan_descent_logic.md: TF2 Descent game logic and gameplay

Measured at `ce143b36b` (main, 2026-11-10). Reviewed and committed that day
("commit it and start on CR").

**Status, 2026-11-14.** On main at `9175b350d`: 65 of the 66 packages (CR2 is
ruled and not built). The wave of 2026-11-13 (rulings L14 to L17) landed
CR7, CR11, RU14 with the bosses rebalanced, four defect fixes and the lamp's
two stats, and one whole run was played end to end unhosted; section 4.7
says what each gave and what the run found. Nothing has been run hosted. The
host and map sessions have the asks of section 7 and have answered them.

The design is `plan_tf2_rpg.md` (rulings R1 to R29, defaults D1 to D25,
appendices A to D). This file does not restate it. It says what to build in
today's tree, in what order, in packages a lane can pick up, and it records
where the design has changed since that plan was measured at `85a7449d1`.

The levels are the map session's (`plan_descent_mine_levels.md`). Its game
server and client rows (D1 to D9, D11, E1 to E5) are packages here, named by
their row, so the two plans do not both assign them.

---

## 0. Decisions

### 0.1 Rulings (user, 2026-11-10)

| # | Ruling | Changes |
|---|---|---|
| L1 | **Horde controller, hybrid pool.** One controller holds every robot of a level as data. Only robots near a hero are given a real entity, from a fixed pool. Distant robots cost no edict; near ones use stock traces and damage. | Replaces D19 (one `[ScriptedEntity]` per robot). Section 3. |
| L2 | Items have minimum requirements on character stats. | Confirms Appendix C.3 (requirements read Now; an unmet one makes the item inactive). |
| L3 | A level-up refills life and mana to full. | New rule in the hero sheet (RU2, HE5). |
| L4 | The descent is a mine shaft. | Replaces D14's catacombs and the church stairs. The town entrance is the mine cage; acts and themes are the mine plan's. |
| L5 | The XP bar is at the top of the HUD. | UI2. |
| L6 | Killing more of a robot type reveals more of its stats. | Appendix D row 13 (bestiary) is in v1 (RU12, UI6). |
| L7 | Repair follows Diablo 1. | Replaces D21's repair. Town repair by the Engineer is lossless and costs Australium. Field repair (the Repair Kit) restores `level + rand(level)` durability a step and lowers maximum durability by `max(maxDur / (level + 9), 1)` a step, until current reaches maximum. **Amended 2026-11-11:** a field repair never destroys an item. One that takes maximum durability to 0 leaves the item **unusable**: it cannot be equipped and grants nothing, and it can still be stashed, sold, salvaged and traded. Whether anything restores maximum durability is not ruled; nothing does until it is. |
| L8 | Escape closes the open Descent screens before it opens the main menu. | UI1. |
| L9 | Enemies push back if a door is blocked. | Robots shove a hero who stands in a doorway, so a door cannot be held against a horde (HO11). **Confirmed 2026-11-11** over the other reading (robots stepping back out of a doorway they block), which is left to ordinary steering. |
| L10 | The Town Portal is an Engineer teleporter. | Replaces D16's Eureka Effect charge. One entity serves this and the death plan's M3 teleporter (WO4). |
| L11 | **(2026-11-11)** A player's equipped items and their attributes are sent to every client, on the player's own networked state: all players see what the others have equipped. | Replaces CR9's owner-only entity. The owner's client predicts from it; every other client can show it (inspect, party frames). An equipped item's rolled attributes are therefore not secret. An unidentified item cannot be equipped (D22), so nothing hidden is exposed. |
| L12 | **(2026-11-11)** A robot is hurt by its own blast, is pushed by blasts as a player is, and can rocket jump and grenade jump as a player can. | Replaces the kept divergence (CR10). The body's motion takes the blast push stock gives a player (HO3, HO8); Soldier and Demoman robots may blast jump as a decision (HO6, HO12). |
| L13 | **(2026-11-11)** Subagent lanes of this plan run on Opus 5.5. | Section 6. |
| L14 | **(2026-11-13)** The economy retune and model land as a starting point: a gate that still fails stays a skipped fact with its numbers, and boss balance is done with it. | RU14 (branch `descent-ru14`, carrying `c5e116065` and `f7de49543` from `descent-ru-d`). |
| L15 | **(2026-11-13)** CR7 (lag compensation for opted-in entities) and CR11 (pose parameters on the server) start. | Core packages; lanes `descent-cr7`, `descent-cr11`. |
| L16 | **(2026-11-13)** The known defects are fixed first, then one whole run is played end to end; the asks of section 7 are sent to the host and map sessions. | Lane `descent-defects`; the run after it lands. The asks went that day to `source-sdk-map-tools-af`, `source-sdk-sharp-26` and `source-sdk-sharp-46` (which session makes the levels was not known). |
| L17 | **(2026-11-13, said to the mine-levels session and relayed by it)** "build the miner lamp (which can have brightness increase and range increase from skills and itmes) then remeasure". | Lamp range and lamp brightness are hero stats (lane `descent-lampstat`). Replaces Appendix D.2 row 11 ("skip the stat") for the hero's own view; robots' sight still reads only whether the lamp is lit. |

### 0.2 What the tree has that `plan_tf2_rpg.md` did not know

| The RPG plan says | The tree has (ce143b36b) | Consequence here |
|---|---|---|
| `Descent.*` assemblies, `game/mod_descent` | `TF2.Descent.{Rules,Server,Client,Client.Ui,Tests}` under `src/tf2_descent`, `game/mod_tf2_descent`, layered on the managed TF2 port (`src/tf2`) | Use these names. Commands and entities are prefixed `tf2_descent_`. |
| Phases 3 and 7: service, leases, orchestrator, gateway, MapForge | A separate repo, `../source-sdk-sharp-host`, reached through the `SourceSharp.Host.Sdk` package (`src/tf2_descent/Directory.Build.props`). `HostLink`, `HostService`, `PlayerService`, `TravelService`, `DeathService`, `LevelHop`, `LevelPrefetch` exist in `src/tf2_descent/server` | Not this plan's work. Logic calls `IHostLink` (`src/tf2_descent/server/IHostLink.cs`). Contract gaps are section 7, raised with the host's owner, never edited from here. |
| `Descent.Rules` shared by game and service | `TF2.Descent.Rules` implements the host's `IGameRules` (`Host.Contracts` 1.0.0) and references nothing else. The host loads and replays it in its own process. Today it is a port of the host's fake rules: 8 hard-coded bases, a `Sheet` of 7 fields | Rules stay engine-free and reference-free. All content tables ship inside the rules assembly (RU1). |
| `Descent.Ai`, a new off-thread AI | `SourceSharp.Ai` (`src/ai/core`) is that design, built and engine-free: sessions, pooled and inline transports, recorder and replayer, nav graph, flow fields, behaviour-tree runtime, skills, memory | Reuse it. Do not write a second AI core. Its bots are fake clients keyed by player slot, which a horde cannot be (section 3.6). |
| D20: a nav graph stitched from room fragments | Levels link to `.nav3d`; the AI derives its `NavGraph` from it (`src/ai/server/Nav/Nav3dNavSource.cs`) | Use `.nav3d`. No stitched graph. |
| `TransmitRecipients`, a field to be added | `ITransmitRule.ShouldTransmit(entity, recipientSlot)` exists (`src/shared/interface/TransmitRule.cs`), used by `src/tf2/server/Rules/TFGlowTransmit.cs` | Instanced loot needs no core work. |
| Phase 4: build the Soldier | The TF2 port has the classes, 498 item rows, the attribute system, conditions, the damage layer and the projectiles | The hero is a TF2 player with a Descent component. Phase 4 shrinks to HE1 to HE10. |
| Phase 8: build the UI | Thirteen screens exist in `src/tf2_descent/ui/screens`, all on `*.Samples.cs` data. Nothing implements the `IDescent*` sources (`src/tf2_descent/ui/DescentUiSources.cs`), and `game/mods.yaml` lists no `client_ui` for `tf2_descent` | The logic side fills the sources (track UI). |
| Phase 10: death | `plan_descent_death.md` P1 and P2 landed; P3 to P6 (teleporter, corpse loot) did not | WO4, WO5. |
| Markers are entities | Markers are `info_poi` rows compiled into the navigation and cost no edict (`src/mapgen/Descent/Markers.cs`): `descent_spawn_pack`, `descent_mark_shrine`, `_container`, `_dispenser`, `_barrel`, `_trap`, `descent_map_marker`, `arrival`, `spawn` | WO1 reads them. |

### 0.3 Measured facts this plan rests on

Source in brackets. Lanes re-measure at their own base before relying on one.

| Fact | Value |
|---|---|
| Edict limit | 2,048 (`MAX_EDICT_BITS` 11, `vendor/shared/public/const.h`) |
| Entries with no edict | 2,048 to 8,191. A `ScriptedEntityKind.Logical` entity takes one of these and no edict (`src/shared/interface/EntityVocabulary.cs`) |
| Map entities per level, depths 1 to 4 | 105 to 287, against a map budget of 1,536. The mine plan leaves **512 edicts for the run** |
| Player slots | 33. A fake-client bot costs a slot and about 5 edicts (player, `tf_player_data`, weapon, viewmodel, viewmodel attachment) [survey of `src/tf2/server`, `Arsenal.cs`] |
| AI cost, debug build, 4 bots | about 1,068 µs CPU a brain decide at 10 Hz; main-thread apply 770 µs a tick for 4 bots, because each bot's command runs the full player path [`plan_hl2mp_ai.md`] |
| Networked state per entity | one blob of at most 2,048 bytes; arrays are fixed length and scalar only [`src/generators/NetStateGenerator.cs`] |
| User message | at most 255 bytes |
| TF damage rules | run for player victims only. `IDamageModifier` has one caller, the player [`src/server/managed/GameRulesDamage.cs`, `src/tf2/server/Combat/TFDamageLayer.cs`] |
| Conditions, burn, bleed, stun | per-player components. Nothing non-player can carry them |
| Robots | none. No robot model, sound, NPC base or MvM code in `src/tf2` |
| Movement with no entity | `GameMovement` runs on plain state through `IMoveWorld` (`src/shared/interface/Movement.cs`). `SoldierMover` drives it against nav voxels with no engine (`src/ai/tf2/server/Jump/`) |
| Lag compensation | rewinds players only (`src/server/managed/LagCompensation.cs`) |
| Stock precedent for a non-player victim | `CTFBaseBoss::OnTakeDamage` calls `ApplyOnDamageModifyRules(info, this, true)` (`tf_base_boss.cpp:442` in the Valve clone) |

### 0.4 Defaults I chose (overrule by editing)

| # | Gap | Default |
|---|---|---|
| E1 | Body pool size | 48 bodies and 1 boss body. HO0 measures and may raise it; the budget in section 2 holds to 64. |
| E2 | Roster size | at most 256 robots a level |
| E3 | Content tables | JSON embedded in `TF2.Descent.Rules` as resources, so the host and the game read the same bytes |
| E4 | Per-hero state on the wire | scalars on a per-hero networked entity; item views by reliable user message, one item a message |
| E5 | Robot locomotion | **Replaced by HO0's measurement (3.11, row 8):** the movement step and the route stay on the AI workers against nav voxels; the main thread accepts each body's move with one hull trace from its last accepted position, and refuses or clips a move into a prop, a body or a hero |
| E6 | Robot gibs and death | a temp entity, no ragdoll entity |
| E7 | Bestiary thresholds | 1 kill shows the count, 15 the life numbers, 30 resistances and immunities, 60 the temperament style. Kept per character in the sheet |
| E8 | Play without the host | an in-memory item ledger behind the same seam as the hosted one, so every live proof below runs `-unhosted` (HE0) |
| E9 | Robot status effects | Descent's own, in the rules (burn, stun, slow, marked, bleed). TF conditions stay on heroes |

---

## 1. Shape

| Assembly | Exists | References | Holds |
|---|---|---|---|
| `TF2.Descent.Rules` (`src/tf2_descent/rules`) | yes | `Host.Contracts` only | every formula and roll: sheet, items, loot, inventory, skills, robot templates, temperament roll, status effects, death, furniture, automap, bestiary. `IGameRules` |
| `TF2.Descent.Ai` (`src/tf2_descent/ai`) | **new** | `SourceSharp.Ai`, Rules. No engine, no ABI | the robot roster, the far simulation, the robot deciders, the temperament to parameter map |
| `TF2.Descent.Server` | yes | TF2.Server, Abi, Rules, Host.Sdk; adds Ai | the mode, hero component, skills runtime, horde controller and bodies, senses and applier, loot, furniture, town, portal |
| `TF2.Descent.Client` | yes | TF2.Client | implementations of the UI sources, key actions, robot and loot client entities |
| `TF2.Descent.Client.Interface` | **new** | nothing | the `IDescent*` source interfaces, moved out of the UI project (UI1) |
| `TF2.Descent.Client.Ui` | yes | toolkit, interfaces | screens. Not logic |
| `src/tf2_descent/shared` | yes | compiled into both sides | net states, user messages, ids |

Rules that hold for every package:

- **Anything that can be decided without the engine is decided in Rules**,
  with facts. The server carries decisions out. The host replays Rules to
  audit what a server reports, so a formula that lives in the server is a
  formula the host cannot check.
- **Mod code is injected, never looked up.** Mod-gated analyzers SS0010 to
  SS0013 fail a static, an event or a core static in these assemblies. A gap
  in the core is closed in the core, generic and tested, in a commit before
  the mod code that uses it (track CR).
- **A registered type joins every hook chain it implements.** Give each chain
  member an explicit `IOrderedHook`, after TF2's band (999,000 to 999,999),
  from `ModStages.First`. Boot the `tf2` mod and `tf2_descent` before landing
  anything that registers a type.
- **The server is authoritative; the client draws.** An unidentified item, an
  unentered room and another player's drop never reach a client that may not
  know them.
- **Every system ships dev commands** (`tf2_descent_give`, `_setlevel`,
  `_spawn_robot`, `_horde`, `_die` and so on, cheats only) so a live proof is
  driven through `tools/debug-api`.

---

## 2. The edict budget

The mine plan reserves 512 edicts for the run. This is how they are spent.
The numbers are ceilings the code enforces, not estimates.

| Consumer | Each | Count | Edicts |
|---|---|---|---|
| Hero: player, `tf_player_data`, weapon, viewmodel, viewmodel attachment | 5 | 4 | 20 |
| Hero wearables that draw (hat, body, feet) | 3 | 4 | 12 |
| Hero Descent state entity (HE2) | 1 | 4 | 4 |
| Robot bodies, pooled (E1) | 1 | 48 + 1 | 49 |
| Robot weapons, one attached-model entity each (CR2's ruling) | 1 | 49 | 49 |
| Robot projectiles in flight, capped | 1 | 32 | 32 |
| Hero projectiles in flight | 1 | 48 | 48 |
| Loot on the floor in woken rooms, capped (WO2) | 1 | 64 | 64 |
| Furniture in woken rooms (WO3) | 1 | 32 | 32 |
| Hero bodies, teleporter pads, boss props | | | 16 |
| TF2 and engine singletons made at run time | | | 16 |
| **Total** | | | **342** |

Headroom is 170. Three things keep it true:

- **Pools are filled at level load and never grow.** Bodies, robot
  projectiles and loot entities are created once at `LevelReady`, parked, and
  reused. A level's edict count is known before the first hero arrives.
- **What a sleeping room holds is data.** Robots, floor loot and furniture in
  a room no hero is near have no entity (section 3.4, WO2, WO3).
- **A census gate** (HO2): a fact over the pools, and a live step that reads
  the entity count after a full clear of a level and fails above
  map + 512.

---

## 3. The horde controller

### 3.1 Three states of a robot

```
            room wakes, body free,                 no hero near for 10 s,
            no hero can see the spot               or a body is needed more
  ASLEEP ───────────────▶ FAR ───────────────▶ EMBODIED ───────────────▶ FAR
  data, no tick           data, 2 Hz tick      a pooled entity,          (state kept)
                          on the AI workers    full brain at 10 Hz
```

- **Asleep.** The robot is a row in the roster. Its room is asleep. It costs
  nothing.
- **Far.** Its room is awake or its pack is alerted, and it has no body. It
  is moved along the room graph by the far simulation: which room, how far
  along the edge to the next door, at its own speed. It can hear, be
  alerted, regroup and walk towards a fight. It cannot be seen or hit,
  because it is never where a hero can see (3.4).
- **Embodied.** It holds a body from the pool. Engine traces hit it, TF
  weapons damage it, it blocks movement, it runs the full brain.

### 3.2 The pieces

| Piece | Kind | Edicts | Where |
|---|---|---|---|
| `tf2_descent_horde` | `ScriptedEntityKind.Logical`; one a level. Owns the roster, the pool, the session | 0 | Server |
| `tf2_descent_robot` | a body. Model, scale, hitboxes, solid box, health bar state. 49 made at `LevelReady`, parked | 1 each | Server, shared net state, client |
| `RobotRoster` | struct-of-arrays, capacity 256, no allocation after load | none | Ai |
| `FarSimulation` | the 2 Hz room-graph tick | none | Ai |
| `HordeSession` | one AI session for the level: publishes a snapshot, takes intents | none | Server over `SourceSharp.Ai` |
| `HordeApplier` | main thread: intents to body motion, attacks, animation | none | Server |
| `EmbodimentRules` | pure: who gets a body, who gives one up | none | Ai |

The controller is the one entity L1 asks for: every robot decision goes
through it, and a robot body is a thing it lends out.

### 3.3 A roster row

Identity (index, serial, template, seed, pack, level). Rolled once at level
load from the level seed and the room's `descent_spawn_pack` markers, by
Rules: scale, life, resistances, temperament, champion affixes. Live: state,
room, position, yaw, life, status effects with their end ticks, clip, body
index or none. Carried across a demotion: target (hero seat), last known
position and tick, alert level, top threat entry.

A robot's brain memory belongs to the body's session slot while it is
embodied. On demotion the carried fields above are saved and the rest is
dropped; on promotion the brain starts from the carried fields. A robot that
loses and regains a body resumes a hunt, and forgets the detail.

### 3.4 Who gets a body

Pure, in `EmbodimentRules`, with facts.

- **Wake.** A room wakes when a hero is within two rooms of it on the room
  graph (RPG plan 5d). Its robots are embodied at their markers then, before
  any hero can see in.
- **Never in view.** A robot takes a body only at a point no hero can see
  (potentially visible set, then a trace). A robot gives a body up only when
  no hero can see it. A far robot never enters a room a hero can see into: it
  waits in the last unseen room and takes a body there, so reinforcements
  arrive through doors.
- **Priority when the pool is short.** Same room as a hero, then a pack
  alerted to a hero, then nearest by room-graph distance. A champion outranks
  an ordinary robot at the same distance.
- **Demotion.** No hero within two rooms and no sight of one for 10 s. Under
  pressure, the lowest-priority unseen body is taken for a higher-priority
  robot.
- **The boss** has its own body and is never demoted while a hero is on its
  level's arena.

### 3.5 The far simulation

Engine-free, on the AI workers, 2 Hz. A far robot holds, patrols between its
room's markers, or walks the room graph towards its pack's last contact.
Sound reaches far robots by room-graph distance (RPG plan B.3). No combat
happens far: a far robot that reaches a room a hero can see queues at the
door.

### 3.6 The AI: one session, bodies as slots

`SourceSharp.Ai` keys everything by slot: `ActorId(Slot, Serial)`
(`src/ai/core/Primitives/Ids.cs`), a session of per-slot lanes
(`src/ai/core/Transport/AiSession.cs`). A pooled body is a natural slot: the
pool is fixed, so body index is slot index, and the serial changes when the
body is lent to another robot. Heroes are actors in the snapshot, not brains.

- **Reused as is:** `AiSession`, `InlineTransport` and `PooledTransport`,
  the recorder and replayer, `NavGraph`, `FlowField`, the pathfinder and
  smoother, the behaviour-tree runtime, `BotMemory`, `SkillProfile` and the
  skill curves, `IDecider`.
- **Not reused:** `SourceSharp.Ai.Server`'s `AiBots` and
  `BotCommandApplier`. They create fake clients and turn intents into
  player commands. The horde has its own senses and applier.
- **Needed in the AI core (CR4, from its spike):** capacity is already a
  constructor argument of `AiSession` and the core names no player, so a
  horde session can be built from the core alone. What it needs is a way to
  switch callouts off (a teamed session otherwise waits on the worker pool
  at every publish), predictors made only for a foe a brain knows (a brain
  otherwise allocates one per slot each time a body is lent), and clearance
  classes by hull size (CR3).
- **Conventions the core imposes:** slot 0 means "nobody" in three fact
  fields, so the session has 54 slots, bodies at 1 to 49 and heroes at 50
  to 53. A brain holds without a word unless its session has a world, skill
  curves and ten or eleven skill levels. A recording does not write an
  actor's class, conditions, metal, name slot, things or objectives, so a
  horde decider that reads one of them will not replay byte-identical: the
  deciders read none of them.
- **Deciders:** one per robot family, in `TF2.Descent.Ai`, implementing
  `IDecider`. They read memory and write an intent. The reference for what a
  family does is stock `bot/behavior/` in the Valve clone.
- **Temperament:** Rules rolls the ten skills of B.11 from a budget of
  `round(40 × scale)`. `TF2.Descent.Ai` maps them onto `SkillProfile`
  (`StandardSkills` has ten of its own; Soloness maps to the inverse of
  Teamwork). The B.11 gates (each skill's observable is monotonic in its
  level) are facts over this map.
- **Cost.** Only embodied robots run a brain: 49 brains at 10 Hz. At the
  measured debug cost that is about 0.5 CPU-seconds a second across the
  workers. Main-thread work per tick is sense, publish and apply. HO0 sets
  the budget; the target is 1.5 ms a tick at 48 bodies, release build.
- **The main thread's cost is the engine writes, not the brains** (HO0,
  3.11 rows 7 and 10): 12 to 15 µs a body a tick in release for origin,
  angles and net state; 3.5 µs of worker CPU a decide for a trivial decider;
  24 to 41 µs a tick to publish 49 brains. The player-bot driver's 116 to
  158 µs a step (the Memory owner's number) is not the horde's path.
- **The src/ai tree is the TF2 AI sessions'.** CR3 and CR4 were agreed with
  the coordinator and the Memory owner on 2026-11-10 and come as this
  plan's own lane; each commit's sha goes to both as it lands.

### 3.7 Moving a body

A robot's motion is computed on the AI workers: `GameMovement` on a
`MoveState` against the level's nav voxels, as `SoldierMover` does. The main
thread writes the result to the body (origin, angles, sequence, pose
parameters). Knock-ups, blast pushes and falls are impulses on the same
state, so *Stomp* and *Juggle Doctrine* work on robots.

The voxels know floors and walls and nothing else: no prop, no door plug,
no hero, no other body. HO0 walked a body seven rooms that way: it stood on
the floor to within about a unit, and it was inside a prop on 3 to 8 % of
its grounded ticks. So the main thread accepts every move: one hull trace a
body a tick from the last accepted position to the worker's position (about
0.6 µs a trace in release), and a move into a prop, a body or a hero is
clipped. Steering separation still does the avoiding; the trace is what
makes a mistake impossible to see.

Scale (R16): hitboxes follow the model's scale by themselves. The hull does
not: the body sets its bounds to the scaled hull itself
(`Entities.SetBounds`), as HO0 did at 0.6, 1.0 and 1.75.

Animation is the client's. The entity block networks origin, angles, model,
skin and bounds, and no sequence, cycle, scale or pose parameter: the body's
own net state carries scale, sequence, a restart serial and the pose
parameters (36 bytes in the spike), and the client plays them. Since CR11
(`e34bd6bb2`, 2026-11-14) the server holds the same pose parameters on the
body's skeleton, so a hit is tested against the posed hitboxes; the overlay
gestures (an attack's layer) are still the client's alone.

### 3.8 Fighting

- **A hero hits a robot.** The engine trace hits the body. Damage reaches the
  body, runs TF's modify rules as stock does for `CTFBaseBoss` (crit, ramp-up
  and falloff, the attacker's attributes; CR1), then Rules (resistance,
  immunity, champion affixes, status effects), then the roster row's life.
  An immune hit reports IMMUNE. The hit is an event for the brain and a
  threat entry.
- **A robot hits a hero.** The applier carries out the intent on the main
  thread: a hull trace for melee, a line trace with the brain's aim error for
  hitscan, a pooled TF projectile armed with the robot's damage
  (`ArmedBy`) for rockets, grenades and flares. The hero is a TF2 player, so
  TF's damage layer, the hero's resistances and conditions apply unchanged.
- **Status on robots (E9).** Burn, stun, slow, marked and bleed are fields of
  the roster row with end ticks, applied and ticked by Rules. The body
  networks flags; the client draws the particles.
- **Death.** The row dies, the body is parked, a temp entity draws the gibs
  at the body's scale, the kill goes to HO10.

### 3.9 The door shove (L9)

The room graph knows every door. When a robot's path runs through a door
portal and a hero has stood inside that portal for 0.5 s, the robot's next
melee intent is a shove: no damage, an impulse along the door's normal away
from the robot, scaled by the robot's scale and reduced by the hero's
knockback resistance (Brawn). A giant clears a doorway; a runt barely moves
a Heavy. Low-Discipline robots shove at once; the numbers are data.

### 3.10 Gates for the horde as a whole

- **Pure:** a fact per rule of 3.4, with a negative for each (a body taken in
  view, a demotion in view, a far robot entering a seen room); roster rolls
  are seed-stable; a robot's life, status and carried memory survive a
  demotion and promotion unchanged; the far simulation is a pure function of
  its inputs; the pool never lends one body twice.
- **Structure:** `TF2.Descent.Ai.csproj` has no engine or ABI reference,
  proven by adding one; zero allocated bytes over 1,000 steady far ticks and
  1,000 brain ticks.
- **Determinism:** a recorded horde session replays to a byte-identical
  intent stream, inline and pooled.
- **Live, two map loads in one process:** a level of 150 robots and one hero;
  the entity count never exceeds map + 512; no robot appears or vanishes in
  the hero's view (asserted from the client's entity list against the hero's
  view each tick of a scripted walk, reading dormancy: a parked body stays
  in a client's list as dormant once it has been seen); the thread count returns to baseline
  after each level shutdown; main-thread horde time is inside HO0's budget.

### 3.11 What HO0 measured (2026-11-11)

A throwaway body on a generated level (depth 1, seed 1), one hero on a
headless listen server. The box was heavily loaded throughout, so the times
are thread CPU, taken twice (a stopwatch and the thread clock, which agreed).
The spike's code is on branch `descent-ho0-spike` and does not land.

| # | Asked | Ruling | Measured | What it means |
|---|---|---|---|---|
| 1 | Model and scale | works | The robot Soldier draws at 0.6, 1.0 and 1.75. Line traces find the head, chest, arm and leg where the scaled model has them, and miss two units outside the scaled hull. A player hull stops at 38.4, 48.0 and 66.0 from the body's centre | The hull is set by the body (3.7); hitboxes follow by themselves |
| 2 | Animation | works on the client; **port** on the server | Sequence, scale and poses reach the client only through the body's own net state. Every pose-parameter call answers nothing on the server | CR11. Poses step at the update rate (about 20 a second) unless the client eases them |
| 3 | TF damage | works | Direct rocket 107, splash 61 to 95 by distance, a crit-boosted rocket 270 with the hurt hook saying crit. The blast's push is sized from each body's bounds (540 direct; 1,000, the cap, on the runt). A shotgun's push is 19,193 and a shovel's 24,000 | HO8 clamps the bullet and melee push before motion takes it, picks a robot impact sound, and rules on the hit group |
| 4 | A weapon | works, one more edict | Its own entity, parented on the server and bone-merged on the client; it sits in the hand at all three scales | As section 2 budgets. The wearable entity is not reused for a non-player owner |
| 5 | Parking | works | A parked body is hit by no trace. 49 parked and never seen cost nothing measurable a frame. Once seen, a client keeps them as dormant | HO2 measures the client's cost of dormant bodies (about 1.3 ms a frame at 49 in release on a listen server, not attributed) before the pool grows |
| 6 | Edicts | works | 29 at load; 127 with 49 bodies and 49 weapons; 127 before, during and after 1,000 park-and-lend cycles | 98 edicts, as budgeted |
| 7 | Moving 48 bodies | measured | Release: 585 to 748 µs a tick mean for 49 bodies (p95 804 to 958), 698 with 49 weapons moved too. Debug: 1,695 mean | The 1.5 ms target holds and is tight: writes take about half. Write a sequence only when it changes |
| 8 | Off-thread motion | **mixed** | Vertical error zero at the 5th and 95th percentile, about a unit at worst. Inside a prop on 6.3 %, 3.1 % and 7.8 % of grounded ticks at scale 1.0, 0.6 and 1.75. A movement step costs 8 to 12 µs off-thread; route following 42 to 60 µs, with 11 to 27 ms spikes when it rebuilds. Three of six doors stuck once and recovered; the exit cage was never left | E5 as rewritten (0.4). Route rebuilds never run on the main thread. Props and door plugs belong in the nav (Y2) |
| 9 | Clearance by scale | works for the runt; giants are restricted | 26,013 cells: the standing class reaches 18 of 18 rooms, the runt 18 of 18, the giant 15 of 18. The giant fits every door and no stair step | HO9 places a giant only where its floor reaches the doors |
| 10 | The brain at pool size | works | Release, 49 pooled brains with a trivial decider: 24 µs mean to publish (p95 37), 3.5 µs of worker CPU a decide. 64 brains: 41 µs | Negligible beside the writes; real trees add their own |
| 11 | Assets | listed | Models for all nine classes; giant models for scout, soldier, pyro, demoman and heavy, and the sentry buster; none for a giant engineer, medic, sniper or spy. Robot footsteps, 2,635 voice lines and 1,406 giant ones, impact sounds, gib sets for five classes | RU7's giant medic has no giant model: it uses the ordinary one scaled, or goes |

**The pool stays at 48 and a boss.** In release the 1.5 ms target divides
as: writes 0.6 to 0.75 ms, the accepting traces 0.03, publish 0.04, and
about 0.6 ms left for senses and the applier, which HO0 did not measure.
Sixty-four bodies fit the edicts and about 1.0 ms of writes; the pool grows
only after HO2 has the client's dormant cost and HO5 the senses'.

Traps the spike hit, for every lane that boots a generated level: copy the
level's three files into `bin/<config>/mods/tf2_descent/pool/maps/` (the
`make descent-install` links make the setup check call them stale); a
service that names `ITransmitRule` in `As = [...]` is refused as registered
twice, so register it plain; the hero spawns at the origin; the console log
keeps earlier boots' lines.

---

## 4. Work packages

Each package is one lane's work, lands as several commits, and names what it
owns so two lanes do not meet. **Kind**: *pure* needs no game boot and suits
a local-model lane; *engine* needs live proofs; *core* changes code outside
`src/tf2_descent` and is serialised.

New type names below are proposals. A lane may rename one; it may not move
the responsibility.

### Track CR: core and TF2 prerequisites (serialised, land first)

| # | Package | Kind | Owns | Gate |
|---|---|---|---|---|
| CR1 | **Damage rules for a non-player victim.** A `ScriptedEntity` that opts in has the `IDamageModifier` fold run on damage to it, and TF's layer applies its modify rules to it as stock does for `CTFBaseBoss` (read `tf_base_boss.cpp`, `tf_gamerules.cpp` `ApplyOnDamageModifyRules`). Add entity hurt and entity killed hooks beside `IPlayerHurtHook` and `IPlayerDeathHook`. | core | `src/server/managed/GameRulesDamage.cs`, `ScriptedEntity.cs`, `src/tf2/server/Combat/TFDamageLayer.cs` | a rocket's falloff, a crit and `mult_dmg` reach an opted-in test entity with the numbers the same shot gives a player; an entity that did not opt in is unchanged (sentries keep `mult_dmg_vs_buildings`); the hooks fire once a hit and once a kill |
| CR2 | **A model drawn on a client entity with no edict of its own** (a robot's weapon, bone-merged). **Ruled 2026-11-10, not built:** it can be done, with a second `DrawModelEx` in the body's own draw (`src/client/native/shell_standalone_client.cpp`, 60 to 90 native lines), one shim row and one row appended to the managed bones table; the managed bone merge is reused unchanged. It is not built, because the existing attached-model entity does the job with no native code and the budget holds at one edict a weapon (section 2). Build it only if the budget tightens. | core | `src/client/native`, the standalone client entity path | if built: the edict count is equal before and after a body's weapon is set, and a screenshot |
| CR3 | **Clearance by hull.** `NavGraph`'s clearance classes are Stand and Crouch. Add classes by hull width and height, derived from `.nav3d` (`Nav3dNavSource.Stands(leaf, width, height)` already asks the reader). Three Descent hulls: runt, normal, giant. | core (src/ai, through the AI coordinator) | `src/ai/core/Nav/NavGraph.cs`, `src/ai/server/Nav` | a flow field never routes a giant through a door it cannot fit, on a fixture with one narrow door; existing AI facts unchanged (`make ai-test`) |
| CR4 | **A session for actors that are not players.** The spike found capacity is already a parameter (section 3.6). What is left: an opt-out for callouts on `AiSession`, default unchanged; position predictors created the first time a foe becomes known to a memory, not one a slot at `AddBot`. | core (src/ai, agreed with its owners) | `src/ai/core/Transport/AiSession.cs`, `src/ai/core/Memory/BotMemory.cs` | a teamed session with the opt-out never drains on publish, and one without it still does; a fresh memory holds no predictor until a foe is known; existing recordings replay byte-identical at 1, 2 and 4 threads; the Memory owner's cost and team-belief facts stay green |
| CR5 | **A player's item source is a seam.** `TFLoadoutProvider.OwnedFor` is private and wired to `LocalInventory`. Make the source an injected interface so a mod supplies the kit with per-instance attributes (`ItemInstance` already carries them). Change a carried weapon without a respawn. Runtime player attributes survive a kit regrant (today the manager is rebuilt in `TFLoadoutProvider`). The owner's client receives per-instance and runtime attributes, so predicted speed and fire rate match. | core (TF2) | `src/tf2/server/Econ`, `src/tf2/server/Player/TFPlayerAttributes.cs`, `src/tf2/client/Player` | the `tf2` mod's kit is unchanged (its facts and a `ctf_2fort` boot); a test source gives a launcher with `mult_dmg 1.5` and the damage and the client's predicted numbers both show it; an attribute added before a resupply is there after it |
| CR6 | **Spawn a TF projectile from mod code.** `TFProjectileEffects.Born` is private. A public, injected spawner taking class, owner, origin, direction, damage and crit. | core (TF2) | `src/tf2/server/Projectiles` | a rocket spawned with damage 37 by a non-player owner does 37 at point blank before falloff |
| CR7 | **Lag compensation for opted-in entities.** After the slice; LAN play does not need it. | core | `src/server/managed/LagCompensation.cs` | a shot at a moving opted-in entity from a client with 100 ms of latency hits where the client saw it |
| CR8 | **`tf2_descent` loads TF2's UI.** `game/mods.yaml`: `client_ui: [TF2.Client.Ui]` on the `tf2_descent` row, if a boot shows the hero needs it. **Narrowed 2026-11-10:** the Descent UI assembly cannot be listed until UI1, because the UI loader throws on a screen whose constructor it cannot fill and no `IDescent*` source has an implementation; the AI assembly does not exist until HO1. Each joins the row in the package that makes it loadable. | core | `game/mods.yaml` | a boot of `tf2_descent` with zero `EXCEPTION IN` and TF2's HUD drawn; `tf2` and `test` rows unchanged |
| CR9 | **Attributes on the owner's client** (CR5's client half, from its design note). The client builds its attribute manager from item definition indexes only (`src/tf2/client/Player/TFClientWeaponOwners.cs`), so an item's own attributes, the runtime list and a mod's standing attributes never reach it, and every predicted quantity that reads one (move speed, fire rate, reload, deploy, clip, spread, melee reach) is corrected by the server each time. Build (**L11**): on the player's own networked state, sent to every client, the providers in the server's order (kind, weapon row, a run of attribute index and value), written when a manager is built and when a runtime or standing attribute changes; every client builds that player's manager from it. Measure the player state's size first: it shares one 2,048-byte row with the 128-row weapon array. If the lists do not fit, report the sizes before choosing a second always-sent entity. Needed before items roll anything but damage, health and resistances. | core (TF2) | `src/tf2/server/Player`, `src/tf2/shared`, `src/tf2/client/Player` | joined, a launcher with `mult_postfiredelay` 0.8 and a standing `mult_player_movespeed` 1.1: the owner's predicted refire and speed equal the server's over 100 shots and 10 s of running; a second client reads the same item and attribute values for that player |
| CR11 | **Pose parameters on the server.** `IEngineEntityRender`'s pose-parameter calls reach only the client's skeletons; on the server Lookup, Set and Count answer nothing (HO0 row 2; a sentry's aim pose is a no-op there for the same reason). Port the server half into `ServerSkeletons` as `ClientSkeletons` has it, managed, with no engine change, so a posed body's hitboxes are where the client draws them. | core | `src/server/managed/ServerSkeletons.cs`, the entity render binding | a body posed with `body_pitch` −45 on the server has its head hitbox where the pose puts it (a line trace hits before, misses after, at the unposed place); a sentry's aim pose moves its hitboxes; the client's posing is unchanged |
| CR10 | **A non-player's own blast hurts it (L12).** Stock's second sweep hits any attacker; the port keeps a gate for a non-player owner (`TFBlastApplier`, a row in `docs/divergences.md`, a pinning fact). Open the gate as stock, rework the rig facts that model their shooter as a non-player (29 went red when a lane tried it), delete the divergence row and the pin. | core (TF2) | `src/tf2/server/Projectiles/TFBlastApplier.cs`, the rig facts | a non-player owner standing on its own rocket takes stock's self-damage; a player's self-damage numbers are unchanged; the divergence row is gone |

What CR1, CR5 and CR6 gave, by name (all on main):

| For | Seam | Where |
|---|---|---|
| a body that takes TF damage | `ScriptedEntity.RunsDamageRules`; `IEntityHurtHook`, `IEntityDeathHook` | `src/server/managed/ScriptedEntity.cs`, `EntityDamageHooks.cs` |
| firing a projectile | `ITFProjectileSpawner.Spawn(in TFProjectileSpawn)`: rocket, pipe, flare, energy ball, energy ring, arrow; any other class is refused by name | `src/tf2/server/Projectiles/TFProjectileSpawner.cs` |
| a hero's kit from rolled items | `ITFItemSource.ItemInLoadout(entIndex, cls, slot)`, a chain; the last opinion wins, TF2's inventories are the floor | `src/tf2/server/Econ/TFItemSource.cs` |
| a hero's stat bonuses | `ITFPlayerAttributeSource.AttributesOf(player)`, asked at every manager build; the container stays the source's, so a changed value shows at the next read | `src/tf2/server/Player/TFPlayerAttributes.cs` |
| changing a weapon mid-life | `ITFKitRefresh.Refresh(player)`: no heal, no move | `src/tf2/server/Player/TFKitRefresh.cs` |

| a horde session that never waits | `AiSession.TeamCallouts = false` | `src/ai/core/Transport/AiSession.cs` |
| routes by body size | `NavNumbers.ExtraHulls` (up to six `NavHull`), giving `ClearanceClass.Hull2` to `Hull7` on each cell | `src/ai/server/Nav/NavNumbers.cs`, `src/ai/core/Nav/NavGraph.cs` |
| a cheap brain | a memory makes a predictor only for an enemy it has lost or heard: 48,792 bytes a brain at 54 slots, from 2,518,448 | `src/ai/core/Memory/BotMemory.cs` |

| attributes on every client | nothing to call: an item's own attributes, a runtime attribute and a standing source all reach every client on the player's own state (1,764 of its 2,048 bytes; at most 16 providers and 32 values a player, a truncation counted and logged) | `src/tf2/shared/Econ/TFAttributeWire.cs` |
| a body thrown by a blast | a body that runs the damage rules is handed, in `OnDamagedAt`, the push stock gives a player, sized from the body's own bounds | `src/tf2/server/Projectiles/TFBlastApplier.cs` |
| rules content | a table is a JSON file in `src/tf2_descent/rules/Content`, a table class, and one line appended to `ContentTables.All()`; `DescentContent` holds them and their hash | `src/tf2_descent/rules/Content` |
| a hero on paper | `HeroSheet`, `HeroRules`, `HeroStats`; gear and skills feed it as `Contribution`s, and an inactive or broken source counts for nothing | `src/tf2_descent/rules/Hero` |
| a checkpoint the host can refuse | `SheetCodec` (schema 2), `CheckpointRules`, the evidence `{"xp":N,"kills":{template:count}}` | `src/tf2_descent/rules/Sheets` |
| the weapon vocabulary | `content.DamageTypes` (fire, blast, bullet, shock), `content.Families` (18), `content.TryGetFamilyOfItemClass` | `src/tf2_descent/rules/Content/WeaponTables.cs` |
| travel | `IHostLink.Travel(steamId, direction, boot, depth)`, `IBossKills` (answers none until the sheet's boss kills are wired), `WaysDown`, `LevelNames` | `src/tf2_descent/server`, `client` |

What they found that later packages must hold to:

- **A checkpoint with no evidence earns nothing** (RU13). HE5 sends the
  evidence with every checkpoint once kills pay XP, or the host refuses the
  sheet. The host's own admin edit of a character can no longer raise XP or
  level (section 7, X4). XP a kill is worth is not bounded yet: RU7's
  templates give the bound.
- **The content hash is logged by nobody.** The host contract has no member
  for it (X5); the game's line belongs to HE2.
- **The rules hand-parse their JSON.** Source-generated System.Text.Json
  emits a static field, which the no-statics analyzer refuses here.
- **A travel notice can name a different way down from the one the map
  built.** `WaysDown` follows the mine plan's table per level; the map
  generator picks a coarser exit shape per seed. One of the two gives way,
  with the map session (Y1).
- **`HudNotices` asks the container for the localiser each time it draws**,
  because the UI registers it after the mod is composed and replaces it on a
  reload. It is the one place in the mod that looks a service up.
- **Worn items' static attributes still do not reach a client while a
  player has nothing rolled** (CR9 keeps the lists empty in plain TF2).
  Sending the walk always would close it; not ruled.

- **A hull wider than the game's own cannot climb stairs** (CR3's lane,
  `src/ai/tests/ServerNav/NavExtraHullStairTests.cs`). The derivation raises
  a hull by one step only. On `ctf_2fort` a 29 by 50 hull has every route
  the game's hull has; a 49 by 83 hull has none. So: an ordinary robot is
  routed by the game's own `Stand` class, never an extra hull; a runt by a
  small extra hull; a giant by a large one, which keeps it on the floor it
  spawned on. HO9 places a giant only where its floor reaches the room's
  doors, and tags it as arena-bound otherwise (RPG plan 5d). A giant that
  follows a hero down a flight needs a derivation rule that stands a wide
  hull across several steps; that is the AI coordinator's to design and was
  put to that session on 2026-11-11.
- **A recording of a session with callouts off does not replay as it ran.**
  `AiReplayer` builds its own session with the team layer on. HO4 agrees the
  replayer's change with the AI coordinator before its record and replay
  gate can be met.

- **`tf2_descent` has no TF kit today.** Its console says
  `scripts/items/items_game.txt is on no GAME search path`, and a Soldier
  there spawns with the core's crowbar. HE1 mounts the TF item schema before
  anything else in track HE can be proven live.
- **Name a team on every projectile a robot fires** (`TFProjectileSpawn.Team`).
  A non-player has no team of its own: a teamless pipe never detonates on
  contact, and the energy ball, ring and arrow ask the player seam and come
  out teamless (HO7).
- **A non-player owner takes no damage from its own blast** (a row in
  `docs/divergences.md`). L12 overturns it: CR10 opens the gate. The push is
  the horde's: a body's `OnDamagedAt` is handed the blast's force, and HO8
  gives it to the robot's motion as stock gives it to a player
  (`CTFPlayer::ApplyPushFromDamage` in the Valve clone is the reference), so
  a robot is thrown by a rocket, juggled by a hero and lifted by its own.
  A Soldier or Demoman robot's blast jump is a decision of its family (HO6,
  HO12) over the jump recipes the TF2 AI already measures
  (`src/ai/tf2/server/Jump`).
- **`Refresh` does not clamp** ammo or health when the new kit lowers a
  maximum, and does not fill a new weapon's effect bars. HE4 does both.
- **TF2's loadout menu and equip commands read `LocalInventory` directly**,
  so for a hero a mod claims they show an inventory that changes nothing.
  HE1 keeps them closed in Descent; the Descent inventory screen is the only
  way to equip.
- **A Descent item is an `ItemInstance` over a TF schema `ItemDefinition`**;
  the weapon row comes from its `item_class` (RU3's bases name a row for
  this reason).

### Track RU: the rules (pure, parallel after RU1)

All in `src/tf2_descent/rules`, facts in `src/tf2_descent/tests`, run by
`make descent-test FILTER=...`. Each package owns its own folder under
`rules/`. Every number is data (E3). Each quotes the RPG plan section that
specifies it; that section's gate is the package's gate unless a row below
adds to it.

| # | Package | Spec | Adds to the spec's gate |
|---|---|---|---|
| RU1 | **Content pipeline.** Embedded JSON tables, a loader, a validator (unknown stat, dangling reference, out-of-range roll are load errors), a content hash both sides log. | 1b, E3 | a fact per validation rule, each proven red; the host process and the game report the same hash |
| RU2 | **Hero sheet.** Four attributes, Base and Now, class starts and caps, derived stats, XP curve, level-up (5 attribute points, 1 skill point, **refill life and mana: L3**), respec, the audit identity. | 2a, Appendix C | level-up returns a refill; the identity holds over any sequence of level-ups, spends and respecs |
| RU3 | **Items.** Item bases over TF2 item rows (`game/mod_tf2/scripts/items/items_working.tsv`; a base names a row), families, rarity, affix pools, cursed stats, unusual chance, **requirements (L2)**, the seed-stable roll, `ItemView`. Affixes that TF2 already implements are named by their TF attribute (`mult_dmg`); the rest are Descent stats the sheet reads. | 2b, 2i, R28 | every base names a row that exists and "works"; an item whose requirement is unmet is inactive and contributes nothing, and is active again when it is met |
| RU4 | **Loot.** Tables by depth, template and champion affixes; `KillRoll`, `BossDrops`; the party split. | 2c | fits `IGameRules.KillRoll`'s signature as it stands (section 7, X1) |
| RU5 | **Inventory, stash, vendor, trade.** Pure state transitions; capacity derived from skill ranks; withdraw-only overflow. | 2d | the count-in equals count-out property over 10k random sequences |
| RU6 | **Skills and spells.** `SkillDef`, the effect primitives as descriptors the server binds (projectile, hitscan, area burst, aura, buff, summon, dash, heal, passive, charge, toggle with drain, triggered passive, threat), the Soldier tree and the Mercenary tab as data, tomes and scrolls. | 2e, Appendix A, R27 | facts generated from the JSON, so a skill added without facts fails the build |
| RU7 | **Robots on paper.** Templates, scale and its stat curve, resistances and immunities, champion affixes, packs by depth, party scaling, the temperament roll, **status effects (E9)**. | 2f, B.11, R25 | a status effect's damage, duration and stacking, one fact each |
| RU8 | **Wear and repair (L7).** Wear per use and per hit; broken contributes nothing; `Repair(item)` is the Engineer's, lossless, priced; the Repair Kit is the recipe `field_repair` through `Craft`, which already carries the hero's level and a seed, with Diablo 1's steps. | 2h, L7 | a field repair's durability gained and maximum lost per step match the formula for levels 1, 15 and 30; the Engineer's repair never lowers maximum; a kit on an item at maximum is refused and not consumed; a field repair that takes maximum durability to 0 leaves the item unusable and never removes it: it grants nothing, cannot be equipped, and can be stashed, sold and salvaged |
| RU9 | **Identify, gambling, crafting.** | 2i, 2j, 2k | as specified |
| RU10 | **Death and downed.** | 2g, R26 | as specified |
| RU11 | **Furniture.** Shrines, dispensers, containers, barrels, traps, placed from the seed at the room's markers. | 2l | as specified |
| RU12 | **Automap and bestiary.** The automap model over the level's rooms. **Bestiary (L6):** kill counts per template in the sheet; `Reveal(template, kills)` says which of count, life, resistances and style a player may see (E7). | 2m, L6 | a view built for a player below a threshold does not contain the hidden field, asserted on the serialised bytes |
| RU13 | **Sheet codec 2 and checkpoint validation.** The sheet grows from 7 fields to the full hero (attributes, skill ranks, spell ranks, kill counts, belt). `ValidateCheckpoint` checks the audit identities; define what `CheckpointEvidence` carries. A schema 1 sheet still decodes. | 3c, C.3 | a forged sheet with one extra point is refused; a schema 1 sheet round-trips into schema 2 |
| RU14 | **The economy model.** Seeded simulated runs of depth 1 to 15, used by the gates of RU4, RU7, RU9 and Appendix A.6. | 2f, 2j, A.6 | the model's own facts; it is an instrument, so it has a self-test that fails on a planted imbalance |

### Track HE: the hero in the engine

| # | Package | Kind | Depends | Builds | Gate |
|---|---|---|---|---|---|
| HE0 | **Item ledger seam and the unhosted ledger (E8).** One interface for mint, reveal, claim, drop, move, with the hosted implementation over `IHostLink` and an in-memory one over Rules. | engine | RU3 | `src/tf2_descent/server/Items` | the same fact suite passes against both; `-unhosted` boots, joins and holds an inventory across a respawn |
| HE1 | **The mode.** `DescentMode : IGameMode` and its selector: no rounds, heroes on one team, robots on the other, no friendly fire, the respawn gate `DeathService` already answers, no TF objectives. `RoundEndGate` folds in. | engine | CR8 | `src/tf2_descent/server/Mode` | a boot on a generated level and on the town: a hero joins as Soldier with no round timer and no team menu; `tf2` mod boot unchanged |
| HE2 | **Hero component and its networked state.** An `IPlayerComponent` per hero holding the live sheet, and one networked entity a hero: life max, mana, mana max, level, XP, XP to next, points, downed state (public), with owner-only fields behind `ITransmitRule` or a second owner-only state if the split needs it. | engine | RU2, RU13, HE1 | `src/tf2_descent/server/Hero`, `shared/HeroNetState.cs` | join, read mana on the client through `tools/debug-api`, write on the server, read again; second map load |
| HE3 | **Sheet to stats.** Derived stats become TF attributes on the player through CR5's surviving provider (max health, move speed, damage by family, crit, resistances) and, where TF has no attribute, a Descent `IDamageModifier` ordered after TF's layer. | engine | CR5, HE2 | `src/tf2_descent/server/Hero` | damage on a target equals the Rules prediction within rounding for three sheets; a broken or inactive item contributes nothing live |
| HE4 | **Equipment.** The equipped items are the hero's kit through CR5's item source; equip and unequip mid-life; requirements refuse; wearables for hat, body, feet. | engine | CR5, HE0, RU3, RU5 | `src/tf2_descent/server/Hero/Equipment.cs` | `tf2_descent_give` a rolled launcher, equip, the `deployed` line names it and its affix shows in damage; an unmet requirement leaves it inactive |
| HE5 | **XP and levels.** A kill pays XP to heroes in range (D10); level-up applies RU2 and **refills (L3)**; a checkpoint follows a level-up. | engine | HE2, CR1 | `src/tf2_descent/server/Hero/Experience.cs` | kill a test robot at a set level, XP matches Rules; level-up at 1 HP leaves full life and mana |
| HE6 | **Skills runtime.** Key actions for skill slots and the belt (`[KeyAction]`, as `src/tf2/client/TFKeyActions.cs`), the client command, mana spend and refusal, cooldowns, and a binder from each RU6 primitive to the engine: CR6 projectiles, traces, radius sweeps, TF conditions for hero buffs, `TFBlastJumpState` for the triggered passives. | engine | RU6, CR6, HE2 | `src/tf2_descent/server/Skills`, `client/KeyActions.cs` | casting with no mana is refused and says so; a cooldown holds; one fact per primitive binder against a fake world |
| HE7 | **The Soldier's first six skills.** *Patriot Missile*, *Marksmanship*, *Jump Training*, *Death From Above*, *Battle Orders*, *EMP Charge* (RPG plan 4c). | engine | HE6 | data plus any binder gap | each skill live against a test robot, numbers from Rules |
| HE8 | **Potions, belt, scrolls, tomes.** | engine | HE6, HE0 | `src/tf2_descent/server/Hero/Belt.cs` | drink, cooldown, count falls by one; a sixth tome is refused and kept |
| HE9 | **Wear.** Weapons wear on fire, armour on hits taken; broken fires as Stock; deltas to the owner. | engine | HE4, RU8 | | a launcher worn to 0 does Stock damage; repair restores |
| HE10 | **The miner's lamp** (mine plan D7, E5) and **hazards** (D8). | engine | HE1 | | every hero's lamp is seen by the others; lava burns as fire through the hero's resistance |

### Track HO: the horde

| # | Package | Kind | Depends | Builds | Gate |
|---|---|---|---|---|---|
| HO0 | **Measure before building. Done 2026-11-11: section 3.11.** A throwaway body on a generated level: robot model (`models/bots/...` from the TF2 mount) at scales 0.6, 1.0, 1.75; hull and hitboxes follow; sequences and pose parameters; hit by a TF rocket; cost of 48 bodies moved each tick; voxel-moved position against an engine hull trace; edicts before and after. | engine | none | nothing kept | a table in this file: each capability ruled works, port, or generic slot; the per-tick budget; the pool size (E1); E5 confirmed or replaced |
| HO1 | **Roster, embodiment rules, far simulation.** Section 3.3 to 3.5, engine-free. | pure | RU7 | `src/tf2_descent/ai` | 3.10's pure and structure gates |
| HO2 | **The controller, the body and the pool.** `tf2_descent_horde`, `tf2_descent_robot` with its net state and client entity, the pool, parking, the census. | engine | HO0, HO1, CR1 | `src/tf2_descent/server/Horde`, `shared/RobotNetState.cs`, `client/Robots` | 49 bodies at load, parked ones never transmitted; lend and return 1,000 times with the edict count flat; second map load |
| HO3 | **Moving a body.** Section 3.7. | engine | HO2, CR3 | `src/tf2_descent/ai/Move`, `server/Horde/HordeApplier.cs` | a body walks a three-room route through doors; a giant refuses a door it cannot fit; a knock-up lands |
| HO4 | **The horde session.** Section 3.6: snapshot, publish, intents, inline and pooled, record and replay, stop and join at level shutdown. | engine | HO2, CR4 | `server/Horde/HordeSession.cs` | 3.10's determinism gate; an induced 1 s AI stall leaves main-thread frame time unchanged and robots holding |
| HO5 | **Senses.** Sight with a per-tick trace budget (the existing `VisionPass` if its slots can be body indices, else the same budget rule rebuilt), sound and damage taps, the gameplay events of B.3. | engine | HO4 | `server/Horde/Senses` | a robot facing away does not see; a gunshot two rooms off is heard as far; no event is dropped over a stall |
| HO6 | **Three families.** Melee rusher, rocket soldier, medic: deciders and their data. | pure, then engine | HO1, HO4 | `src/tf2_descent/ai/Families` | inline simulation facts per family; live, each kills an idle hero |
| HO7 | **Robot attacks.** Melee, hitscan, pooled projectiles (cap 32), flame. Section 3.8. | engine | HO3, CR6 | `server/Horde/Attacks` | each attack's damage on a hero equals Rules after the hero's resistance; the 33rd projectile is not fired |
| HO8 | **Robots take damage.** CR1 to Rules to the roster; IMMUNE; status effects with their flags on the body. | engine | HO2, CR1, RU7 | `server/Horde/RobotDamage.cs` | a fire-immune champion shows IMMUNE to a flare and dies to a rocket; burn ticks stop at their end tick |
| HO9 | **The spawn director.** Markers to packs by depth, seed and party size; clearance fits the rolled scale; the half-immune rule. | pure, then engine | WO1, RU7, HO1 | `src/tf2_descent/ai/Director` | no generated pack breaks the rules over 10k seeds; live, a level's roster matches its replay |
| HO10 | **Kill to reward.** XP (HE5), `KillRoll`, the ledger's reveal, the loot entity (WO2), Australium split, the bestiary count. | engine | HO8, HE0, HE5, WO2 | `server/Horde/Rewards.cs` | a scripted clear's XP and drops equal a seeded Rules replay; a second client's entity list never holds the first client's drop |
| HO11 | **The door shove (L9).** Section 3.9. | pure, then engine | HO3, HO6 | | a hero in a doorway is pushed out along the door's normal; a hero beside it is not |
| HO12 | **Champions and the remaining families.** Sniper, spy, pyro, demoman, heavy, scout, sentry buster; champion affix behaviour. | engine | HO6 to HO8 | | per family, the HO6 gate; B.11's temperament gates |
| HO13 | **Bosses.** The boss body, phase-scripted trees, the three bosses (D13), the Tank's track (mine plan D11), the boss exit opening on the kill (D6), the boss kill recorded for the camps (D4). | engine | HO12, WO7 | | each boss can be killed and drops its table; the exit is closed before and open after |

### Track WO: the world

| # | Package | Kind | Depends | Builds | Gate |
|---|---|---|---|---|---|
| WO1 | **The level as data.** At `LevelReady`: the rooms, their doors and neighbours, their bounds, and every marker's position and kind, from what the level ships (`.nav3d`, `.map2d`, the layout from Rules). Arrival points for a party (mine plan D1), the town's `tf2_descent_arrival` spawns (D2), markers handed to the director and furniture (D5). Start by asking the map session what a linked level carries (section 7, Y1). | engine | Y1 | `src/tf2_descent/server/Level` | on three seeds of depth 1, the room count equals the layout's, every room has its markers, and a party of four arrives at four distinct arrival points |
| WO2 | **Loot on the floor.** One `tf2_descent_loot` a drop, with up to four seats (one a party member), shown to a seat's owner only (`ITransmitRule`). Pooled, 64; loot in a room that falls asleep becomes data and returns when the room wakes. Pick up with use; refuse when full. | engine | HE0 | `server/Loot`, `shared/LootNetState.cs`, `client/Loot` | client B never receives client A's seat; 65 drops in one room keep 64 entities and lose no item; sleep and wake a room and the count is unchanged |
| WO3 | **Furniture.** Shrine, dispenser, container, barrel, trap entities at markers, made when a room wakes. Each emits the events B.3 expects. | engine | RU11, WO1, CR1 | `server/Furniture` | each kind used once live matches its Rules replay; a barrel kills a robot |
| WO4 | **The teleporter (L10, death plan M3).** A Town Portal scroll places an Engineer teleporter entrance on the level and an exit pad in town; using it travels (`IHostLink.Travel`, `TravelDirection.Up`) and returns once to the same live instance. The model and effects are TF2's (`src/tf2/server/Buildings/TFTeleporter.cs` is the reference for the look, not a base class). | engine | HE8 | `server/Portal` | place, go up, come back to the same level and the same pad; a second return is refused |
| WO5 | **Loot your own body** (death plan M4). Use within 96 units takes all that fits; `IHostLink.LootCorpse`. | engine | HE0 | `server/DeathService` area | die, return, recover, counts conserved |
| WO6 | **Downed and revive (R26).** | engine | RU10, HE2 | `server/Hero/Downed.cs` | the RPG plan's Phase 10 gate, first half |
| WO7 | **Travel.** The picked depth, not always 1 (mine plan D3, `HostLink.Travel.cs`); camps and boss kills (D4); notices per way down (D9); the level's name on arrival (E4). | engine | none | `server/TravelService` area | from town, pick depth 3 and arrive on depth 3 |
| WO8 | **Town vendors.** A use-able NPC entity at each `info_target shop_<name>`; use opens its screen (`VguiMenu` to a screen that claims the panel); buy, sell, buy-back, identify, repair, craft, crate through the ledger. | engine | HE0, RU5, RU8, RU9, UI4 | `server/Town` | the RPG plan's Phase 9 gate |
| WO9 | **Party.** Create, invite, accept over the host's parties; party frames' data; shared automap. | engine | HE2 | `server/Party` | two clients form a party and descend together |

### Track UI: wiring the screens to the game

The screens are done and are not this plan's to redesign. Each package
implements sources in `TF2.Descent.Client` and the server messages that feed
them, and replaces a screen's sample data in game.

| # | Package | Depends | Gate |
|---|---|---|---|
| UI1 | **Plumbing.** Move the `IDescent*` interfaces to `TF2.Descent.Client.Interface` (interfaces and data only). Screens claim panels. **Escape closes the top Descent screen; the main menu opens only when none is open (L8).** The item-view cache on the client, fed by one-item messages, with a resync on join. | CR8 | a screen opens from a server `VguiMenu`; Esc with two screens open closes one, then the other, then opens the menu |
| UI2 | **HUD.** Vitals, belt, **XP bar at the top (L5)**, party frames, boss bar, cast bar, gear warning. | HE2, UI1 | live screenshot; mana on the orb equals the server's |
| UI3 | **Inventory, character sheet, skill tree, spellbook** and their actions as client commands. | HE3, HE4, HE6, UI1 | spend a point on the sheet, the server's sheet changes; an item that fails a requirement shows it |
| UI4 | **Vendor, stash, craft bench, crate, depth picker (mine plan E1).** | WO7, WO8 | the Phase 9 gate, driven through the screens |
| UI5 | **Automap (mine plan E2).** Outlines from the level's `.map2d`, revealed room by room by the server. | WO1, RU12 | an unentered room is absent from what the client holds |
| UI6 | **Robot health bars.** Name, style, resist icons, IMMUNE, each gated by the bestiary (L6). | HO8, RU12 | a first kill shows the count only; the thirtieth shows resistances |
| UI7 | **Downed.** | WO6 | live screenshot of the bleed-out and the revive bar |

### 4.7 What landed after `ff7ce14ae`, and what it found

Seams by name, for the packages that build on them; then what did not hold.

**Hero.**
- HE0: `IItemLedger` (`InMemoryItemLedger` unhosted, `HostedItemLedger`),
  `IItemLedgerHook`, `IHostItems`. HE2: `HeroSheets`, `HeroComponent`, the
  `tf2_descent_hero` entity, `IHeroStates` on the client. HE3: `HeroStats`,
  `HeroAttributes`, `HeroDamage`, `HeroLeech`, `HeroRegen`,
  `IHeroContributionSource`, `IAttackerLevel`, `tf2_descent_dummy`.
- HE4 (`TF2.Descent.Server.Gear`): `HeroEquipment`, `HeroKitSource`,
  `HeroKitSettle`; TF2 gained `ITFFreshWeapons`. HE5: `IRobotKillHook` (the
  one seam that tells a robot's death once), `HeroExperience`, the class gate
  over TF2's `ITFClassChangeGate`.
- HE6 and HE7: `SkillRuntime`, the binders, `ISkillHits`, `IRobotStatusSink`
  (its only registration answers NotAvailable until HO8), keys 4 to 9, the
  user message `TF2.Descent:skill_bar`. The rules follow-ups put the skill
  bar on the sheet (schema 3), made `damage_weapon_pct` a damage kind every
  weapon hit carries, and moved a family's damage kinds, the class words and
  the starting kits into content.
- Found: about seventeen Soldier and Mercenary skills do nothing, or less
  than their tooltip, in play. Most wait on HO8 (stun, slow, knock-up, marks,
  threat); the rest on stat readers nobody wrote (spread, reload, fire rate,
  projectile speed, crit damage, damage taken, prices, potions, item find).
  Gear's bonus ranks are never read. Heavy and Engineer have no shotgun in
  their starting kits, because the shotgun bases are Soldier's and Pyro's.

**Horde.**
- HO1 and HO2: `RobotRoster`, `EmbodimentRules`, `FarSimulation`,
  `HordeController`, `HordeService`, `RobotBodies`, `RobotBody`, the hit chain
  `IHooks<IRobotHitSink>` (`RobotHitTally` 0, `HordeHitSense` First+515,
  `RobotBlastPush` First+520), `HeroSight`, the census.
- HO3: `RobotMotion` (`IRobotMotion`, `IRobotImpulse`,
  `IRobotMotionReports`), `IMotionWorld`, `IMotionStepper`
  (`InlineMotionStepper` ships; `WorkerMotionStepper` is proven only over a
  box world), `DoorRoute`, the dev drive. The motion is handed its
  `IHordeActors` at `Begin`: the session answers that seam and is itself
  given the motion, a cycle no constructor pair can build.
- The fix after the join: robots are one team. `RobotsPassRobots` (an
  `IEntityCollideRule`, First+530) lets a trace that passes a robot body miss
  other robot bodies, and the motion pushes overlapping bodies apart with the
  port of stock's teammate pushaway. "Blocked by Robot" now means held up in
  a crowd for 33 ticks.
- HO4 and HO5: `HordeSession` (`IHordeBrainHost`, `IHordeActors`) over
  `HordeBrains`, 54 slots, `HordeWorkers`, `HuntDecider`,
  `HordeDeciders.Register(family, factory)`, `IRobotActionSink` (its only
  registration counts and drops until HO7), sight (32 lines a tick,
  round-robin), hearing through `HordeHearingTap`, `HeroLandings`.
  Recordings replay byte-identical inline and at 1, 2 and 4 threads.
- HO10: `LootRewards`, a member of the kill chain at First+310, after
  `HeroExperience` (+300).
- **Section 3.7 did not hold.** The Descent server cannot reference
  `SourceSharp.Ai.Server` or `SourceSharp.Ai.TF2`: the loader sweeps the
  reference closure and registers every `[SideService]` in it, so TF2's bot
  services would join every Descent game. So robots route door to door
  through the room graph, straight inside a room, and the motion steps on the
  main thread with engine traces. Release cost: 634 us for 34 stepped bodies
  (HO3), 797 us mean for 24 with the pushaway on; the session's own tick is
  157 us mean for 48 brains. The 1.5 ms budget does not hold at 48 walking
  bodies until the steps leave the main thread.
- What unblocks it (on main since `f8f9fd1a1`, by the user's ruling of
  2026-11-11): `SourceSharp.Ai.Nav`, a library with no service in it, holding
  the nav graph derivation, the follower, the voxel columns and
  `VoxelMoveWorld`. It needs the Abi, so `TF2.Descent.Server` references it,
  not `TF2.Descent.Ai`. The same day `src/ai` gained the event kinds Arrived,
  AllyDamaged, AllyDied and HeroDowned, record format 8 (a header flag for a
  session with the team layer off, a home point on a brain) and a drain that
  allocates nothing. A horde lane adopts these after HO6 to HO8 land.
- Found: a pack is lent on one marker, inside itself (HO9 should place a
  pack's members apart); a hull of scale 1.9 does not fit the exit cage; a
  static prop answers a trace as World.

**World.**
- WO1: `ILevelData`, `LevelAdapters`, `ArrivalSpawns`; the level-to-horde
  join (`LevelHordeSource`).
- WO2: `tf2_descent_loot`, a pool of 64 made at level ready (the census went
  123 to 187), `LootFloor`, `LootTransmit`, `ILootRooms`, `ILootParty`,
  `ILootOnFloor` on the client; `IItemLedger.CollectAustralium` and
  `PotionDrop`. Seats stay private because the entity carries a drop number
  only: each seat's contents go to its owner in a reliable user message. A
  65th drop folds the oldest to data and loses nothing.
- Found: Australium is split into per-hero shares at the drop, where the
  design asked for one pickup split on collection, which would show every
  client the others' seats. Hosted, the ledger refuses floor Australium and
  potion drops: the host has no credit on pickup and no potion mint.

**UI.**
- UI1: `TF2.Descent.Client.Interface` holds every `IDescent*` contract;
  every source has a client implementation (live or an honest empty one);
  `mods.yaml` lists `TF2.Descent.Client.Ui`; windows claim `descent_<name>`;
  keys C, I and K (they take TF2's `+voice_menu_3`, `showmapinfo` and
  `cl_decline_first_notification`); Escape closes one window a press (a fix
  in the toolkit: a closed window stayed on top, and one press closed two).
- UI2: vitals, XP at the top, party and cast bar on live data. TF2's HUD
  gained two named elements a mod HUD may take (`tf.playerstatus`,
  `tf.teamstatus`).
- Found: the client cannot reference `TF2.Descent.Rules` (it brings
  Host.Contracts), so it has no skill or item names; UI3 decides the route.
  The cast bar needs the charge's length on the wire. A party of two reaches
  one row into TF2's chat.

**Landed 2026-11-11 night to 2026-11-12 (`7ef3fecbf` to `a04ae8d77`).**
- HE8 and HE9: `HeroBelt` and the client verb `tf2_descent_belt <1-4>`
  (numpad keys), `HeroConsumables` (`tf2_descent_use`), `ITownPortal` (its
  stand-in refuses until WO4), `HeroWear` batched to one ledger write a
  point lost, `IItemLedger.Consume` / `Wear` / `RepairWorn`, the owner-only
  messages `TF2.Descent:belt` and `TF2.Descent:gear_wear`, and on the client
  `IBeltState` and `IGearWearState`, which the HUD's belt and gear warning now
  read. Content: `consumables.json`. Potions are instant, stack four to a
  slot, share one 3 s cooldown and refill from the backpack; a drink at a
  full pool is refused; the gear warning starts at 25 %. Found: pending wear
  is lost when a death moves the items to the corpse.
- HO8: a skill's hit is its declared damage type; an immune hit takes
  nothing and tells its attacker `#Descent_Robot_Immune`, once a second;
  `RobotStatusEffects` (the status sink over the roster: stun, slow,
  knock-up, mark, burn and bleed as hits by the hero who applied them, a
  debuff's stats, a taunt), `RobotAttackerLevel`, the content table
  `robot_conditions.json`, `StatusBook`. Found: `on_direct_hit` and
  `on_knock_airborne` are never raised by the skills runtime; a taunt is told
  to a brain as a hit of 0. Not done from the plan's HO8 row: the push clamp
  for bullets and melee, the impact sound, the hit-group ruling, stock's
  on-hit walk for a boss victim.
- HO7 and HO6: `RobotAttacks` (the action sink: melee, hitscan, projectiles,
  flame, the medic's heal; interval, windup, clip), the content table
  `robot_attacks.json`, the deciders in `src/tf2_descent/ai/Families` (the
  rusher, the rocket soldier, the medic) registered by `HordeFamilies`, and
  a view that carries the pack, the gun's look and the line of fire. **Robot
  projectiles are capped at 32 by count, not pooled**: TF's rockets and
  pipes delete themselves, so section 2's "nothing created in play" does not
  hold for them; the worst census seen was 165 of 537. Release, 48 fighting
  bodies: the horde tick is 1.42 ms mean, 1.74 ms p95. Found: a robot's kill
  reads "suicided" in the kill feed (a TF2 seam, with WO6); the other
  families still only hunt (HO12); the medic's heal has no beam.
- Two rules of the join: a weakened robot deals that much less (the hands
  read the debuff from the row), and **a robot's attack hurts the heroes and
  itself, never another robot** (L12 read as "like a player": a player is not
  hurt or pushed by a teammate's rocket; with every blast hurting every
  robot a mixed horde killed 37 of its own in a minute).
- UI3: the client embeds the rules' own content JSON (linked, byte-equal,
  with a fingerprint both sides check), because Rules may reference nothing
  but Host.Contracts and the client may not reference Rules.
  `OwnerItemSender` and the client caches `IItemViews`, `IHeroSheetNumbers`,
  `IHeroSkillRanks`; the client verbs `tf2_descent_spend`,
  `tf2_descent_item_equip` / `_unequip` / `_move` / `_drop`; the skill bar's
  message carries the charge's length. Not there: a control to drop or move
  an item, a control to assign a skill to a slot; a refusal's notice is
  hidden under an open window.
- WO3: `FurnitureService` over the rules' `Furnish` (the level's seed),
  `tf2_descent_furniture` in a pool of at most 32, `IRoomActivity` (lifted
  from the loot floor's seam), `IItemLedger.ContainerDrop`,
  `IFurnitureHazardHook` (nothing implements it), `IFurnitureNear` on the
  client. A marker is filled by its kind's chance: depth 1 seed 1 has 11
  pieces on 17 markers and no dispenser. A shrine lasts the level. TF2 ships
  no explosive barrel: a Badlands barrel stands in.
- Hosted play refuses: equip, belt, consume, wear and repair writes, floor
  Australium, potion drops, container drops (row X6).

**Landed 2026-11-12 (`a04ae8d77` to `f5b423323`).**
- HO9: `SpawnDirector.Plan` (`src/tf2_descent/ai/Director`) decides a
  level's roster; `RobotRoster.Load(LevelPlan, HordeLevel)` only writes rows.
  Every robot has its own post (`RobotRoster.Post`, `PostYaw`), which is its
  home for the brains and the far simulation. Marker clearances are measured
  on the server at level ready (`IMarkerClearances`); the party is the
  clients connected then (`IHordeParty`). `tf2_descent_director` prints the
  roster's digest beside a replay's. Found: the tables allow immune
  champions from depth 2 where the design says depth 11.
- HO11: `DoorShove` over `DoorShoveRules`; numbers in `horde.json`. **3.9's
  trigger never fires in play**: a brain halts its robot at fighting range,
  so the motion never reports a hero as its blocker. A robot within melee
  reach of a hero standing in a doorway shoves him instead.
- The horde's navigation: `HordeNav` derives the level's graph and voxel
  columns from its `.nav3d` on a thread of its own at level ready (depth 1
  seed 1: 26,013 cells, 142 ms, ready 59 ticks in), probes the bsp's static
  props into it, and hands it to the brains; `IHordePaths` (paths computed
  in the decider from recorded inputs, the graph's epoch in the view, at
  most two searches a tick); `VoxelMotionWorld` puts the worker stepper over
  `VoxelMoveWorld`, with the accept sweep and the separation still on the
  main thread. Release, 48 fighting bodies, thread CPU: the horde tick is
  909 to 1,057 us p95 on the worker against 2,038 to 2,239 us inline. **The
  1.5 ms target is met.** The session records `TeamLayerOff`, gives a brain
  its home, and tells `Arrived`, `AllyDamaged`, `AllyDied` and `HeroDowned`.
  Found: hulls of scale 1.6 and up cannot reach three of the level's 18
  rooms (the one-step stairs limit); the graph is derived each load, because
  the cache lives in `Ai.Server`.
- WO6, WO5, UI7: `DownedDamage` (a lethal hit with another hero up leaves
  him down at 1 life; alone, or as the last one up, he dies), a 20 s window
  that shortens 5 s a later down to a floor of 5 s, revive by holding use
  within 96 units for 3 s to 30 % life, `tf2_descent_corpse` (a gravestone
  holding everything; only its owner recovers it; a second death merges onto
  it), `IItemLedger.Corpse`, `ICorpseState`, `IHeroDownedListener`, the live
  `DownedSource`. A death costs no XP and no durability (D21). The kill
  feed names a robot through TF2's new `ITFKillerNames` and the client's
  `IKillFeedKillerNames`: the mod answers a localisation token.
- WO4: `TownPortals` (`ITownPortal`), `tf2_descent_teleporter`,
  `IPortalParty`, `IPortalRecords`, `IHostAnchors`. **Unhosted, the return
  loads the level anew** (one process holds one map): the robots, the loot
  and the furniture start over, and the player is told. Hosted returns to
  the live instance through the SDK's anchor; proven over a fake link only.
  Ask of the map session: an `info_target` named `tf2_descent_portal` in the
  town.
- UI5 and UI6: `AutomapService` (a hero's own map, a room sent to its owner
  when he first stands in it; `IAutomapReveal`, which the sight shrine now
  calls), the client's `AutomapRoomCache`, the key M; `RobotViewSender`
  (what a hero may know of a robot goes to him alone: life at 15 kills of
  its template, resistances and affixes at 30, style at 60; a hidden part
  has no bytes), `IDescentRobotBars`, IMMUNE over the robot.
- Found by playing the stack, and given to the polish lane: a hero who dies
  alone respawns unarmed at the arrival point among the robots that killed
  him; an unarmed hero's view shows a stray sprite; recovered items come
  back unequipped; the corner minimap covers TF2's kill feed; a refusal's
  notice is hidden under an open window. `god` does nothing through the
  debug route, so a proof keeps its hero alive by level and refill.

**Landed 2026-11-12, later (`f5b423323` to `8aa183777`).**
- HE10: the miner's lamp rides the hero's networked state (`LampOn`,
  `LampPitch`) and every client makes the light itself (`IDynamicLights`), so
  a lamp costs no edict; `lamp.json`; key L; `IHeroVisibility` (a neutral
  stand-in until the sight lane). Hazards: `HazardService` over brush
  entities named `tf2_descent_hazard_<kind>` and `hazards.json` (lava is
  fire and hurts robots; slime is unresisted and does not), bites through
  the hero damage path, `HazardKillerNames`. No generated level has a dark
  room or a hazard volume yet: both are asks of the map session.
- WO8 and UI4: `TownService` stands eight stalls at the town's `shop_*`
  markers (`tf2_descent_vendor`); use opens a window for its hero alone; the
  verbs `tf2_descent_town_buy`, `_sell`, `_buyback`, `_identify`, `_repair`,
  `_repair_all`, `_craft`, `_crate`, `_stash_in`, `_stash_out`, `_descend`
  go through the ledger by the rules, and a refusal shows on the window's
  own status line. `BuyerPrices` is the first reader of `price_buy_pct` and
  `price_sell_pct`. Stock restocks hourly, per hero. In town the way down
  opens the depth picker. The Phase 9 gate ran through the windows unhosted,
  but for trade between two clients, the reconnect step and a hosted run.
  Asks of the map session: `shop_engineer` and `shop_bench` markers.
- The polish lane: a respawn has a grace (`death.json`
  `respawnGraceSeconds` 6, ended by a shot, a swing or a cast) and the
  brains forget the dead (`AiEvent.ActorSpectating`); the core's
  `IWeaponColumnOwner` and TF2's `TFLoadoutProvider.OwnsWeapons`, so an
  empty TF kit is no weapons (an unarmed hero had been handed HL2's crowbar
  and gravity gun); an empty melee slot swings the class's stock melee,
  which is no item; a recovered corpse puts gear and potions back where
  they were (`InventoryRules.Recover`); pending wear is written before a
  death. HUD: the minimap hangs under TF2's kill feed, the party frames sit
  top left, a six-slot skill strip (`SkillSlotsStrip`), a status row in the
  sheet, the skill tree and the inventory (`IDescentWindowStatus`), Equip /
  Move / Drop on the bag, slot assignment from the skill tree. **Still
  open:** a robot that sees the respawned hero kills him when the grace
  ends (three deaths in 27 s in one boot): the sight lane's target.
- HO12: deciders for the scout, pyro, demoman, heavy, shotgunner, sniper,
  spy and sentry buster, so no template is left on the bare hunt; every
  champion affix has its behaviour (`server/Horde/Champions`:
  `IRobotDamageGuard`, `IHordeTicker`, `IRobotAttackShaping`); nine of
  B.11's ten temperament gates are facts (Cover is not built). Release, 48
  bodies of every family: the horde tick is 991 us mean, 1,266 us p95. The
  buster is in no depth's packs yet.
- WO9: `IParties` (`InMemoryParties` unhosted, `HostedParties` over
  `IHostParties`), `IPartyHook`, `IPartyRoster`; the client verbs
  `tf2_descent_party_invite`, `_accept`, `_decline`, `_leave`, `_kick`,
  `_disband`; key P. Every "all heroes are one party" stand-in is replaced:
  one downed machine a party (a hero in no party is solo), the portal, the
  loot seats and the XP of a hero's kill, the director's party size, skill
  auras and party-wide shrines; a party shares one automap. Asks of the
  host: decline, disband, a notice when a party changes elsewhere, the
  party on `BootInfo`.
- Descent's keys now take six of TF2's binds: C, I, K, M, L and P.

**Landed 2026-11-13 (`8aa183777` to `6875df092`).**
- HO13: `bosses.json` (Sir Nukesalot at depth 5, Major Crits with the Tank
  at 10, Captain Punch at 15: phases by life, a telegraphed special a phase,
  adds once a phase); `BossDecider` (`src/tf2_descent/ai/Boss`) wraps the
  boss's family decider; `BossEncounter`, `BossAbilities` (the chain
  `IHooks<IRobotAbility>`), `IBossAdds`, `IBossExit` (the way down is held
  by the server until the kill: the levels ship no exit brush), `BossFallen`
  (kill chain First+1300: the kill on each paid hero's sheet, read by the
  town's destinations), the `boss_bar` message and the live `IDescentBoss`.
  The boss resets after 10 s with no living hero in its arena. The Tank
  loops the arena's markers, drills at stations and brings the roof down.
  Release, a boss beside 42 bodies: the horde tick is 466 us mean, about
  600 us p95. Not seen live: Punch's slam, the Tank's roof. **Balance:** a
  solo hero of level 30 (305 life) dies to one volley pipe (398) or one
  fist (955); the bosses hold 26.8k, 75.4k and 431k life for one hero. Asks
  of the map session: a Tank track and stations, a boss spot, an exit brush,
  pillars (the arenas are placeholders).
- How robots come to know of a hero (`descent-sight`): a hero in his
  respawn grace is veiled from every brain's senses (`RespawnGraces`, a
  passive board the downed state writes and the session reads;
  `HordeHeroes.Gather` is unchanged, so the bodies round him do not thrash);
  a robot that watched him through the grace stays blind to him until it
  loses view of him once, for at most `death.json` `respawnBlindSeconds`
  (60); his first act ends both. A death forgets him for good
  (`RobotRoster.ForgetSeat`, `HordeBrains.HeroGone`: carried targets, pack
  alerts, far packs' convergence, queued sounds). **The death loop is
  closed:** die alone, respawn, do nothing: alive a minute later, twice in
  the coordinator's boot (before: three deaths in 27 s). `HeroVisibility`
  folds the lamp and the depth's darkness into a robot's sight range
  (`lamp.json` `sight`: dark from depth 11; lamp off is a quarter of the
  range in the black); `tf2_descent_horde_sight` says why a robot does or
  does not see a hero. Found: a robot back at its post faces along its last
  path, not its post's yaw; one robot sat with a stalled step count while
  following and not blocked; an explosion carries no seat, so a far pack it
  alerts keeps the contact 30 s after the hero's death.
- A hook chain is no constructor need: `Hooks<T>` collects its members the
  first time it is asked. The composition fact that guards the session and
  the downed state against a constructor cycle stops at a chain.

**Landed 2026-11-13 and 14 (`8ca876d53` to `9a51b5088`, then `fe9337f46`).**

- CR7 (`9da21f59a`, `8ca876d53`): a `ScriptedEntity` opts into the lag
  compensation (`LagCompensated`, `LagCompensationMaxSpeed`,
  `RestartLagHistory`; `ILagCompensatedEntities`), is recorded and rewound as
  a player is, and `ILagCompensationTargets` is asked with
  `LagCompensationTarget.OptedIn`. A robot body opts in while lent and alive
  and starts a fresh history at each lend and park. Recording 49 entities
  costs 9 to 17 us a tick over the fake world (release, a scratch bench) and
  allocates nothing. Live: a robot crossing the hero's view was rewound 46
  units for the probe and put back. Not shown live: a fair hit count with and
  without it (a lagged client's shot could not be timed). Left for the
  performance pass: `Collect` walks every live managed entity a frame.
- CR11 (`e34bd6bb2` and the Descent commits after it): the pose-parameter
  calls answer on the server with stock's rules, the bone cache's key holds a
  pose generation (a rewind that changed only the poses was traced against
  stale bones before), poses ride a hot reload, and a robot body sets its four
  dials on the server as it writes them to its net state. Live: a body leaned
  45 degrees moves its head hitbox to where the client draws it (the old
  place hits the wall, the new one the body). One posed setup is about 67 us
  (release, managed-only bench). **Not measured:** the horde's release tick
  with posed bodies (the disk was full). Found: a built sentry's turret does
  not follow its aim, because `TFBaseObject` never plays `ACT_OBJ_RUNNING`
  (the TF2 port session has it) and the core's model change does not reset
  the sequence to 0 as stock's `SetModel` does (no owner; asked of the user).
- The four defects (`c94691f02` to `b615d6fa5`). A robot idle at its post
  turns to its post's yaw. A corpse whose level is gone goes to a Lost and
  Found (the design's R5 / D6: reclaim in town for 25 % of sell value;
  `IItemLedger.CorpsesLost`, `tf2_descent_town_reclaim`; the host has the
  same in SDK 2.6.0, not yet called). Giants: `IHullReach` keeps a robot over
  scale 1 off a floor its hull cannot reach, and one hunting off its floor
  holds at the nearest place (`PathAnswer.Nearest`); the cut on depth 1 is
  the wide drift rooms' solid floor rails and posts, which the mine-levels
  session is making passable. Stalls: a body sat on a ledge lower than a step
  (the accept sweep now tries across and down), and a hero last seen in the
  air was never searched for (arrival is measured on the floor plane); a
  robot that follows a path and does not move for 5 s is logged once as
  `STALLED FOLLOWER`. **Found joining it and fixed:** the place a hit from
  out of sight is looked for at was worked out again at every decide, so it
  kept ahead of the robot (a pack hurt once walked 5,000 units; on main the
  same fault stood robots still, 48 of 58 path searches unreachable), and a
  goal off the floor was never searched. Open: a third kind of stall at floor
  height in the wide drift rooms; a far robot walked home keeps its walking
  yaw until it has a body; boss adds are not asked the reach; a reclaim the
  hero cannot pay for says nothing in the console.
- RU14 (`7c20dd57b` to `9a51b5088`): the held retune and the economy model
  carried onto today's content, the bosses in the model as `bosses.json`
  scripts them, and three families of gates. Passing: threat per XP 1.176
  (band 1.25), a solo hero clears every depth and boss, the Sergeant beats a
  fourth Artillery (un-skipped), crates dearer than play, no boss hit over
  half a hero's life (the largest is a tenth), every build wins each boss
  alone, a party of four is not trivial. **Skipped with their numbers, not
  loosened:** the first three builds within 25 % (1.458), a Stock weapon
  costing the Gunman most, and the depth-10 solo fight's length (M14's 10 to
  20 minutes is longer than a solo hero survives). Numbers changed beyond the
  carried retune: Sir Nukesalot life 1600 and damage 24, Major Crits 8000 and
  12, Captain Punch 12600 and 60, the Tank slower with longer stops, and **a
  level gives 18 life, not 2, for all nine classes** (solo deaths a run fell
  from 28-81 to 8-27). One live boss fight said nothing about the boss: the
  hero died in seconds to the arena's pack and his own rockets. The model
  fights the pack and then the boss; in the game they stand together.
- The lamp's stats (L17, `fe9337f46`): `lamp_radius_pct` and
  `lamp_brightness_pct` over `lamp.json`'s light, each with a floor and a
  ceiling there (range 360 to 1,080 units round 720; brightness x0.5 to x2);
  two affixes on hats and trinkets (Radiant, of Light) and a five-rank skill,
  Lamplighter, on the Mercenary tab (52 ranks now). Every number is
  provisional. On every client by the hero's own state. Brightness scales
  the world's light only (a second dlight keeps models at the base, so the
  view model is not whitened further); range is the radius for both. The
  mine-levels session measured that range is the lever and brightness is
  nearly spent at the base, and will recommend a dimmer base lamp to the
  user.
- The box: the disk filled on 2026-11-13 (349 GB of lane worktrees). 44
  spent lane worktrees were retired (each tip tagged `archive/<branch>`).

**The whole run (2026-11-14, `9f6de1cbe` to `9175b350d`).** A lane played
one process for an hour and three quarters over twelve map loads (the town
four times, depths 1 to 5), with a script that walks the room graph, aims and
fires. No load threw. By play: the town and its eight stalls, depth 1 by
fighting (35 of 48 robots for about 35 deaths), a real level-up's refill
(L3), loot on the hero's own seat, equip, a belt potion, a skill learnt and
cast, wear, a shrine, a barrel, the automap, the lamp, the bestiary (L6),
death and recovery, the Lost and Found, and with a second player a party, a
revive and both lamps. With dev help: the portal and most of the way down.
Not reached: an unmet requirement (L2: every base with one is item level 6
or more), a field repair (L7: a kit needs 6 metal and one salvage gave 1),
the door shove (L9: none all run), depths 10 and 15.

- Fixed in the lane: unhosted, a level's cage down and up did nothing (now
  the process changes level, down to the next installed depth and up to the
  town); Esc over the depth picker opened the main menu; a refused town verb
  with no window open showed nothing; an empty stash's words.
- Blocks a run, open: the Town Portal blueprint is in no vendor's stock and
  no drop table; a potion picked up or bought never reaches the belt (only
  the dev command calls `ToBelt`); a hero of level 1 to 3 dies 5 to 25 s
  after meeting an awake pack, and the depth 5 boss (level 9) was not
  beaten by the run's level 3 hero in 661 s and 21 deaths (the script
  skipped depths 3 and 4, so this is the early game's harshness more than
  the boss's numbers). Closed by the mine-levels session at `218175fc0`: a
  level's cage button pressed once per load.
- Wrong, open: another client draws the host's weapon at a stale origin
  after an in-place kit refresh (TF2 or the core); the path on depth 1 runs
  through room 8's pillar, and depths 1 and 2 logged 15 and 11 stalled
  followers; `tf2_descent_spawn_robot` from the server console with two
  players reads a stale command client (the core's); closing a town window
  on the client does not tell the server; an Engineer repair of the starting
  launcher costs 27 Australium against 26 for a new one.
- The Descent leg's `HostLinkTests` held two facts that waited on a
  `Task.Delay` continuation under a wall-clock bound; each failed one
  whole-leg run on a busy box (the pool gave the continuation no thread for
  5 and for 10 s). Both now complete their step from a thread of the test's
  own.

**For every lane.** A wall-clock bound in a fact fails on a loaded box: the
horde's stall fact failed three landings at 255 to 440 ms against 250 and
now holds the decide at a gate instead. Two lanes that each register a
stand-in for the other's seam must be booted joined: each one's live proof
ran over the other's stand-in, and the pack deadlock showed only on the join.

---

## 5. Order

```
wave 0   CR8  HO0  RU1          spikes: CR2 CR4          ask: X1 Y1
wave 1   RU2..RU14 (pure, in parallel)   CR1 CR5 CR6 CR3 CR4 (serialised)   HO1   HE0 HE1   WO7
wave 2   HE2 HE3 HE4 HE5 HE6    HO2 HO3 HO4 HO5    WO1 WO2    UI1 UI2
wave 3   HE7 HE8 HE9    HO6 HO7 HO8 HO9 HO10    WO3 WO4 WO5 WO6    UI3 UI5 UI6
wave 4   HO11 HO12    WO8 WO9    UI4 UI7    HE10    CR7
wave 5   HO13, the other eight classes (RPG plan Phase 12), balance and soak (Phase 13)
```

**Milestones.**

- **Paper game** (end of wave 1): every RU package green. A Soldier can be
  rolled, levelled, geared and killed in facts, and the economy model runs.
- **First blood** (wave 2 plus HO6, HO7, HO8, HO10): `-unhosted`, depth 1.
  A Soldier with a live life and mana HUD meets a pack of rushers lent
  bodies by the horde, kills them with a rocket that crits and falls off as
  TF2's does, gains XP, levels and is refilled, sees a rolled launcher drop
  for him alone, picks it up, equips it and does more damage.
- **A level** (wave 3): hosted. Arrival, automap, furniture, the way down to
  depth 2, death and the walk back, the teleporter home.
- **The town** (wave 4): vendors, stash, identify, repair, craft, crate, a
  party of two.
- **Act I** (wave 5): depths 1 to 5 and the first boss.

---

## 6. Rules for lanes

`CLAUDE.md` governs. What bites here in particular:

- A subagent lane runs on Opus 5.5 (L13).
- A dedicated worktree, cut from the named base, `make setup` first. Check
  the base (`git log --oneline -1`) before the first edit.
- One commit a step, each with its fact. A fact that cannot fail is not
  evidence: break the subject and see red.
- Facts: `make descent-test FILTER=...` for `src/tf2_descent`,
  `make ai-test FILTER=...` for `src/ai` (`make managed-test` runs neither).
- Live proofs boot `make dedicated` (`tf2_descent`, unhosted) and a joiner,
  each with its own `TMPDIR`, `DEBUG_API_DIR`, port. Assert the join before
  trusting a client reading. Load two maps.
- Before landing a package that registers a type: boot `tf2` on `ctf_2fort`
  and `tf2_descent`, and read both console logs for `EXCEPTION IN`.
- Never write to `../source-sdk-sharp-host`, `../source-sdk-map-tools`, the
  fork or the Valve clone. A gap there is reported, not patched.
- `src/ai` has no single owner since 2026-11-11 (Part G is one session's;
  the core's and the nav's owner is gone): ask the sessions with lanes there
  before touching it. `src/mapgen` and
  `src/tf2_descent/rules/Layout` to the map session; `src/tf2_descent/ui`
  screens to the UI work. A package that needs a change there asks.
- New files that port Valve logic start with `tools/valve-header.txt` and
  get a row in `docs/valve-header-new.tsv`.
- TF names are spelt `TF`, never `Tf`. Stock classnames stay stock; Descent's
  own entities are `tf2_descent_*`.

---

## 7. Asked of other repos and sessions

Nothing here is done by a lane of this plan.

| # | Of | Question or request |
|---|---|---|
| X1 | host | `KillRoll(instanceSeed, killSeq, robotTemplate, depth, partySize)` has no player and no magic-find input. R4 rolls items once per eligible player, and *Scavenger* raises drop chance. Until answered, RU4 rolls once per (kill, player) by advancing `killSeq` per roll, and *Scavenger* affects Australium found only through the split. |
| X2 | host | `RoomInfo` carries no door sockets (already noted in the mine plan). |
| X3 | host | Field repair. **Answered 2026-11-13:** the host keeps no recipe list, so `Craft("field_repair", ...)` would pass, but a craft terminates its inputs and mints a new item (a new id). A field repair must change the same item, so it needs a repair kind on `IGameRules.Repair` (in place, `Items.Mutate`) with the kit as a terminated input. Not a recipe. |
| X4 | host | RU13 made an empty `CheckpointEvidence` earn nothing, so the host's admin edit of a character (which passes none) can no longer raise XP or level. Either the admin path passes evidence, or the contract tells a checkpoint from an edit. The evidence is at most 16 KiB. |
| X5 | host | No `IGameRules` member reports the rules' content hash; the host can log it only by reaching into the concrete type. |
| Y1 | map session | What a linked level ships that the server can read at load: rooms and their bounds, door links, marker positions. Whether the server should rebuild the layout from Rules (`GenerateLayout` needs the library's room list) or read a sidecar. WO1 waits on this. |
| Y2 | map session | Door portals as data (position, normal, width) for the far simulation's queue points and the door shove. |
| Y3 | AI coordinator | CR3 and CR4: agree the shape before a lane touches `src/ai/core`. Done. The six later asks (the service-free nav library, the drain, the pool, the team-layer flag, four event kinds, a brain's home) landed 2026-11-11 (`70a734e52` to `a108dbc72`). |
| X6 | host | Equip, belt use and consume are not in the SDK, so hosted play cannot change a kit, drink or read a tome. No credit of Australium on pickup, no potion mint, no report of items left on a floor. A hosted `KillDrop` that completes after a level change leaves an item on the new level's ledger floor. |
| Y4 | map session | A level's `.map2d` checksum is stale after `tools/descent-face-sides`; props are not in the nav; a pack's members share one marker. |
| Y5 | map session | A light level per room in the level data the server reads (`ILevelData`'s rooms carry bounds, doors and markers, no light): the preview's lightmap mean over the room's luxels would do, on the mine plan's scale (level 1's mean is 100). Until then the horde's sight reads the depth's darkness from lamp.json's `sight.darkness` (lit to depth 10, black at 15) in place of the room's (2026-11-12, the sight lane). |

---

## 8. Open

1. **Whether anything restores an item's maximum durability** (L7 as
   amended): nothing does today, so an item a field repair has made unusable
   stays so.
2. **What CR1 left for the horde** (landed 2026-11-11; the opt-in is
   `ScriptedEntity.RunsDamageRules`, the hooks `IEntityHurtHook` and
   `IEntityDeathHook`). HO8 must: keep the body's engine health in step with
   the roster row's life, or the hurt hook misreports health left; refuse
   hits on a dead body, or the death hook fires again; rule on the hit group
   itself, since a non-player victim gets no headshot arm. Stock's on-hit
   walk for a boss victim (heal on hit, ammo refund, rage and meters) is not
   run for a non-player victim yet: it is a row in
   `docs/plans/tf2/deferred.tsv`, and HO8 ports it before a Black Box can
   heal off a robot.
3. **The pool at 48** may make large rooms feel thin in a party of four
   (D11 adds 25 % pack size a member). HO0's numbers decide whether 64 fits
   the tick budget; the edict budget already allows it.
4. **Player bots as party members** (the TF2 AI's fake-client bots filling a
   party) is not planned here and was not asked for.
5. **Unusual effect particles on wearables** do not exist in the TF2 port;
   an Unusual item's visual waits on that.
6. **Halloween spells** are not ported; RU6's tomes are built from HE6's
   primitives, with the stock spellbook
   (`src/game/shared/tf/halloween/tf_weapon_spellbook.cpp` in the Valve
   clone) as the reference for what each does.
7. Appendix D's undecided rows: quests and story (2), elixirs (7), named
   uniques (10).
8. **Waiting on the user** (2026-11-14). Ruled 2026-11-13 and gone from
   this list: the retune (L14), CR7 and CR11 (L15). Still open from before:
   whether plain TF2 always sends attribute lists; giants for the classes
   with no giant model (Engineer, Medic, Sniper, Spy): scale the ordinary
   model or drop them; Descent's keys taking six of TF2's binds (C, I, K, M,
   L, P); Australium split at the drop instead of on collection; whether one
   robot's blast should hurt another robot (it does not); the class's stock
   melee as an unarmed hero's fallback; the 60 s respawn-blind ceiling. New
   from this wave: the core's model change resetting the sequence to 0 as
   stock does (it reaches every entity; no session owns that code); a level
   giving 18 life for all nine classes; the boss fight bands (depth 5 one to
   four minutes, depth 15 two to eight) and what to do about depth 10's (M14
   asks ten to twenty minutes, which a solo hero does not survive); the two
   skipped build gates; whether the first reclaim from the Lost and Found
   is free (a hero with no Australium cannot pay the fee on a starting kit);
   Lamplighter as a new skill (52 Mercenary ranks) and robots' sight
   ignoring the lamp's stats; the base lamp's strength (the mine-levels
   session recommends a dimmer base with the range bought back). From
   the whole run (2026-11-14): whether a potion gained goes to the first
   free belt slot (the default proposed: Diablo's rule); where the Town
   Portal blueprint is sold; the early game's balance, which a person should
   play before anyone tunes it; whether a lost corpse's own Australium may
   pay its Lost and Found fee.
9. **Robot traces pass robots** (the team rule above) for every trace a body
   makes, attacks included: a robot's rocket or bullet will not stop on
   another robot, as a teammate's does not in TF2. L12 (a robot is hurt by
   its own blast) still holds through the blast path.

