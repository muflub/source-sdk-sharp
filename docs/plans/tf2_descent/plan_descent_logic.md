# plan_descent_logic.md: TF2 Descent game logic and gameplay

Measured at `ce143b36b` (main, 2026-11-10). Reviewed and committed that day
("commit it and start on CR").

**Status, 2026-11-11.** On main at `d6e9ebec9`: CR1, CR3, CR4, CR6 and
CR5's server half (item source, standing attributes, kit refresh). Ruled and
not built: CR2. Not started: CR7, CR8, CR9 and every other track.

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
| L7 | Repair follows Diablo 1. | Replaces D21's repair. Town repair by the Engineer is lossless and costs Australium. Field repair (the Repair Kit) restores `level + rand(level)` durability a step and lowers maximum durability by `max(maxDur / (level + 9), 1)` a step, until current reaches maximum. A field repair that takes maximum durability to 0 destroys the item, as in Diablo 1. That last clause contradicts D21's "never destroyed by wear" only for this deliberate act; strike it at review to floor at 1 instead. |
| L8 | Escape closes the open Descent screens before it opens the main menu. | UI1. |
| L9 | Enemies push back if a door is blocked. | Read as: robots shove a hero who stands in a doorway, so a door cannot be held against a horde (HO11). The other reading, robots stepping back out of a doorway they block, was offered and not chosen; say so if it was meant. |
| L10 | The Town Portal is an Engineer teleporter. | Replaces D16's Eureka Effect charge. One entity serves this and the death plan's M3 teleporter (WO4). |

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
| E5 | Robot locomotion | off the main thread, `GameMovement` against nav voxels. HO0 measures the fidelity; the fallback is main-thread hull traces |
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
- **Drive steps, not brains, are the pool's main cost** since `0b9817e34`
  (the Memory owner's measurement: 116 to 158 µs a step, 66 steps a second
  a bot, 12 bots). The horde does not use that driver as it stands (3.7);
  HO0 reads `tools/ai-cost live` before the pool is sized.
- **The src/ai tree is the TF2 AI sessions'.** CR3 and CR4 were agreed with
  the coordinator and the Memory owner on 2026-11-10 and come as this
  plan's own lane; each commit's sha goes to both as it lands.

### 3.7 Moving a body

A robot's motion is computed on the AI workers: `GameMovement` on a
`MoveState` against the level's nav voxels, as `SoldierMover` does. The main
thread writes the result to the body (origin, angles, sequence, pose
parameters). Knock-ups, blast pushes and falls are impulses on the same
state, so *Stomp* and *Juggle Doctrine* work on robots.

What the voxels do not know (doors, props, heroes, other robots) is handled
by steering separation and nav obstacle flags. HO0 measures how far a
voxel-moved body sits from where an engine hull trace would put it; if that
shows, E5 falls back to main-thread hull traces for embodied robots.

Scale (R16): the body's hull and hitboxes follow its model scale
(`ServerSkeletons.SetModelScale`). HO0 proves that at 0.6, 1.0 and 1.75.

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
  view each tick of a scripted walk); the thread count returns to baseline
  after each level shutdown; main-thread horde time is inside HO0's budget.

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
| CR9 | **Attributes on the owner's client** (CR5's client half, from its design note). The client builds its attribute manager from item definition indexes only (`src/tf2/client/Player/TFClientWeaponOwners.cs`), so an item's own attributes, the runtime list and a mod's standing attributes never reach it, and every predicted quantity that reads one (move speed, fire rate, reload, deploy, clip, spread, melee reach) is corrected by the server each time. Build: one owner-only networked entity a player holding the providers in the server's order (kind, weapon row, a run of attribute index and value), written when a manager is built and when a runtime or standing attribute changes; the client applies it on top of what it builds today. One edict a hero. Needed before items roll anything but damage, health and resistances. | core (TF2) | `src/tf2/server/Player`, `src/tf2/shared`, `src/tf2/client/Player` | joined, a launcher with `mult_postfiredelay` 0.8 and a standing `mult_player_movespeed` 1.1: the client's predicted refire and speed equal the server's over 100 shots and 10 s of running; a second client receives no attribute row |

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

What they found that later packages must hold to:

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
  `docs/divergences.md`). That suits robots; a design that wants a robot hurt
  by its own rocket reopens it.
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
| RU8 | **Wear and repair (L7).** Wear per use and per hit; broken contributes nothing; `Repair(item)` is the Engineer's, lossless, priced; the Repair Kit is the recipe `field_repair` through `Craft`, which already carries the hero's level and a seed, with Diablo 1's steps. | 2h, L7 | a field repair's durability gained and maximum lost per step match the formula for levels 1, 15 and 30; the Engineer's repair never lowers maximum; a kit on an item at maximum is refused and not consumed; the destroy-at-zero clause, one fact |
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
| HO0 | **Measure before building.** A throwaway body on a generated level: robot model (`models/bots/...` from the TF2 mount) at scales 0.6, 1.0, 1.75; hull and hitboxes follow; sequences and pose parameters; hit by a TF rocket; cost of 48 bodies moved each tick; voxel-moved position against an engine hull trace; edicts before and after. | engine | none | nothing kept | a table in this file: each capability ruled works, port, or generic slot; the per-tick budget; the pool size (E1); E5 confirmed or replaced |
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
- `src/ai` belongs to the TF2 AI coordinator's lanes; `src/mapgen` and
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
| X3 | host | Field repair fits `Craft("field_repair", ...)` as the contract stands; confirm the host has no fixed recipe list that would refuse it. |
| X4 | host | What `CheckpointEvidence` may carry, and its size limit, for RU13. |
| Y1 | map session | What a linked level ships that the server can read at load: rooms and their bounds, door links, marker positions. Whether the server should rebuild the layout from Rules (`GenerateLayout` needs the library's room list) or read a sidecar. WO1 waits on this. |
| Y2 | map session | Door portals as data (position, normal, width) for the far simulation's queue points and the door shove. |
| Y3 | AI coordinator | CR3 and CR4: agree the shape before a lane touches `src/ai/core`. |

---

## 8. Open

1. **L7's destroy-at-zero clause** and **L9's reading**: both flagged in 0.1.
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
