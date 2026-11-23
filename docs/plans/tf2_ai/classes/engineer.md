# P7 research: the Engineer

Lane: P7 research, Engineer. Base `225fe2e96`.
No code, builds or games were run. Repo claims cite `path:line` at that base; game knowledge cites a URL (list in §5).
Stock TFBot numbers cite `../source-sdk-2013-valve/src/game/server/tf/bot/...` (abbreviated `bot/`).

## 1. The class's job, by mode

**What the class is.** 125 health (150 with the Gunslinger), normal speed, 90 % while hauling a building, 200
metal at most ([Engineer]). Sentry 130 metal, dispenser 100, teleporter 50 a side; upgrades 200 a level for the
sentry and dispenser (mod: `src/tf2/server/Buildings/TFObjectDefs.cs:42-47`; [Sentry Gun] gives 130 / 330 / 530 in
total). The mod's teleporter `UpgradeCost` is 100 (`TFObjectDefs.cs:45`) while [Engineer] lists 50 / 250 / 450, which
is 200 a level. **Check this against the shipped `objects.txt` before relying on it** (it may be a divergence, or the
shared pool of the two teleporter ends). Sentry range 1,100 u (`TFSentryGun.cs:80`). Build times in the mod: sentry
10 s, dispenser and teleporter 20 s (`TFObjectDefs.cs:42-47`). A wrench hit while it builds speeds construction
(`TFBaseObject.cs:912-924`, window 1 s; [Engineer]: 2.5x).

The Engineer **denies an area**: the sentry kills or stalls whatever comes near it, the dispenser keeps the team
supplied where it stands, and the teleporters cut walking time to the front. His measure under the user's ruling
(objective time and map progress) shows **on the other bots**. When he attacks: how soon his team's slow classes
reach the point (the teleporter). When he defends: how long the enemy takes to cap (the sentry). His own kills are
a side number.

### By mode (pub and competitive)

| mode | where he builds, what he does | sources |
|---|---|---|
| **CP attack/defend, defending** | His strongest mode. He builds during setup: the sentry at the last or the next point, at a choke the enemy must cross, with a dispenser behind it. He falls back a point when one is lost: haul a level 3 if the way is short, else rebuild. | [Competitive]: "Attack/Defend … his strongest mode"; [Community]: "pick chokepoints near the enemy's approach" |
| **CP attack/defend, attacking** | Teleporter entrance at spawn during setup, exit forward behind cover. A level 2 sentry near the point the team is taking is enough ([Community]: "On offense, a Level 2 Sentry is often enough"). Moves the exit up as points fall. | [Community], [Guide] |
| **Symmetric 5CP** | Pub: a sentry at his own second or last point, sometimes forward on mid. 6s: an off-class, played only to hold the last point ("it is best to play when contesting your team's last point"). | [Competitive] |
| **KOTH** | A nest on his side of the hill, covering the point, on cells his team reaches before the enemy (stock drops every area the enemy reaches first, `bot/behavior/engineer/tf_bot_engineer_move_to_build.cpp:150-155`). Teleporter to the hill. | stock; [Community] gives no KOTH advice |
| **Payload, BLU** | Teleporter to the cart. Gunslinger minis beside the cart's path; Short Circuit against projectiles at the cart. Relocates as the cart passes checkpoints. | [Community]: "Gunslinger mobility suits BLU's push. Short Circuit protects the cart" |
| **Payload, RED** | A nest in front of the next checkpoint. When the cart gets more than a sentry's range past it, he moves up (stock: `bot/behavior/engineer/tf_bot_engineer_building.cpp:225-245`, the tolerance is `SENTRY_MAX_RANGE`). | stock |
| **CTF** | Pub: a sentry in or at the door of his own intelligence room, a dispenser for the defenders, a teleporter out to mid for the attackers. Stock builds at the capture zone (`tf_bot_engineer_move_to_build.cpp:68-79`, with Valve's note "should defend location of our team's flag"). | stock; [Community] has little on CTF |
| **Highlander** | A staple on defence (sentry, Wrangler or Rescue Ranger, a nest at the choke). On attack, Gunslinger minis or a teleporter "helper" with the Jag. | [Competitive] (it covers 6s and names the items; Highlander specifics were not found in a source I could read: the tf2center thread answered 403) |

### What good Engineers do

- **Where they stand.** In the nest, between the sentry and the dispenser, close enough to swing at both, crouched
  behind the buildings (stock: within 75 u of both, crouch inside 90 u, `tf_bot_engineer_building.cpp:85, 128-147`).
  A good player stands **beside or behind** the sentry, never in front of a level 3 while it fires: its rockets hurt
  him ([Community]).
- **Range.** The sentry fights out to 1,100 u. He fights with the shotgun up close and the pistol at middle range. He
  does not chase: the gun pulls him off his buildings.
- **Target priority.** (1) Spies at the nest, always ("Engineers hate Spies", stock `tf_bot_engineer_build.cpp`'s
  ShouldAttack). (2) The sapper: kill the Spy first, then hit the sapper ([Community]). (3) Whoever is damaging the
  sentry from range (Demoman stickies, Soldier rockets, Sniper): he answers by repairing between their shots or by
  wrangling. (4) Anything else only if it is close and the sentry does not have it. Stock never attacks within
  100 u of its own sentry except a Spy: it keeps the sentry alive (same ShouldAttack).
- **Push, hold, retreat.** He holds a spot as long as it covers the objective. He relocates when the front moves
  (stock checks every 3-5 s; a sentry out of position is kept only while it is still firing: >10 s without a shot
  and it is taken down, `tf_bot_engineer_building.cpp:326-364`). When the nest is falling to an über or several
  enemies, **he lives**: he hauls what can be saved and retreats ("it is most important that the Engineer makes it out
  alive", [Guide]). When the sentry is destroyed, stock goes after the attacker if it is within 750 u and retreats
  otherwise (`tf_bot_engineer_retaliate_range`, `tf_bot_engineer_building.cpp:28`).
- **Movement tech.** Hauling (+attack2 on a building; 90 % speed, no weapon). Rescue Ranger lift from range. Eureka
  teleport home. Wrangler jump (the sentry's rockets under his feet: [Community]). None needs the blast-jump nav
  class beyond what exists. Wrangler jumps are long tail.
- **With teammates.** The dispenser is the team's: the Medic builds über behind it and slow classes refill there
  ([Competitive]). A Pyro guards the nest against Spies and reflects at it. Two Engineers overlap their sentries' fire
  but never build side by side, so one explosive cannot take both ([Community]). The rocket blast radius is 146 u
  (`src/tf2/server/Projectiles/TFRadiusDamage.cs:22`). He calls "Sentry ahead" and "Spy". He builds a teleporter for
  the team on attack.
- **What beats him, and how good players play around it.**
  - A Demoman's stickies from out of sight. He builds where pipes cannot be lobbed in (not in pits) and wrangles.
  - Soldiers out-ranging the sentry. He builds where approaches end inside 1,100 u, behind a corner, out of long
    lines, and wrangles at range.
  - Snipers: two charged shots kill an unwrangled level 3 ([Competitive]). He keeps the sentry off long lines.
  - Spy and sapper. He spy-checks with the shotgun and kills the Spy before the sapper.
  - Übers. He moves the most valuable building out of the way and retreats ([Community]); a Pomson drains über.

### Beginner mistakes that make a bot read as a bot

1. Building in the open, or in the middle of a corridor, where the sentry is seen from far beyond its range ([Guide]).
2. Standing in front of the sentry or on the teleporter exit (telefrags) ([Guide]).
3. Hitting his own buildings when an enemy is in his face ([Guide]: the swing prefers the building).
4. Dying with the nest instead of retreating and rebuilding ([Guide]).
5. Never moving the nest after the front moves (stock's own fault without its out-of-position check).
6. Running into the fight with the shotgun and leaving the sentry unrepaired.
7. A sentry facing a wall. The blueprint's yaw is the bot's view (`TFBuilderPlacement.cs:580`). Stock aims it toward
   where enemies enter (`tf_bot_engineer_build_sentrygun.cpp`, `m_needToAimSentry`).
8. A dispenser out of reach of the team, or one swing from both buildings, so it serves only him ([Community]).
9. Building the teleporter entrance far from spawn, or an exit next to the entrance. Stock refuses an exit under
   3,000 u of travel from the entrance (`tf_bot_min_teleport_travel`, `tf_bot_engineer_move_to_build.cpp:30`) and an
   entrance over 1,500 u from spawn (`tf_bot_max_teleport_entrance_travel`, `tf_bot_engineer_build_teleport_entrance.cpp:18`).
10. Spinning on the spot while the blueprint refuses, retrying forever. Stock tries 5 times
    (`tf_bot_engineer_building.cpp:34`, `tf_bot_engineer_build_sentrygun.cpp` `m_sentryTriesLeft = 5`), then picks a
    new spot.

## 2. Play styles by weapon set

The items are from `game/mod_tf2/scripts/items/items_working.tsv` (status `works`, class engineer: 66 rows). The
reskins (same `item_class`, no attribute, or only cosmetic ones) count as their base item:

- **Wrench** (`tf_weapon_wrench`, no attributes): 7, 197, 662, 795, 804, 884, 893, 902, 911, 960, 969, 15073, 15074,
  15075, 15114, 15139, 15140, 15156, and 169 Golden Wrench (`set_turn_to_gold;cannot_trade`: cosmetic).
- **Shotgun** (`tf_weapon_shotgun`): 9, 199, 1141, 15003, 15016, 15044, 15047, 15085, 15109, 15132, 15133, 15152.
- **Pistol** (`tf_weapon_pistol`): 22, 23, 160, 209, 294, 1202, 15013, 15018, 15035, 15041, 15046, 15056, 15060,
  15061, 15100, 15101, 15102, 15126, 15148, and 30666 The C.A.P.P.E.R (`ragdolls_become_ash`: cosmetic).
- **Wrangler** (`tf_weapon_laser_pointer`): 140, 1086 Festive, and 30668 Giger Counter (`is_giger_counter`: cosmetic).
- **Frontier Justice** (`tf_weapon_sentry_revenge`): 141 and 1004 Festive (the same attributes).
- Always the same: the builder 28 (`tf_weapon_builder`), the destroy PDA 26. The build PDA (25) has no row, so
  `ItemInLoadout` answers the stock one (`src/tf2/server/Econ/BotInventory.cs:98-99`).
- **Distinct behaviours:** 142 Gunslinger, 329 Jag, 589 Eureka Effect, 997 Rescue Ranger, 527 Widowmaker,
  588 Pomson 6000, 1153 Panic Attack, 528 Short Circuit.
- **Excluded:** 155 Southern Hospitality is `partial` and is never drawn.

**Today's draw is not coherent.** Each slot is drawn on its own, uniformly (`BotInventory.cs:183-190`). Melee has 22
works rows, so the Gunslinger comes 1 in 22, and so do the Jag and the Eureka. Primary has 18 rows: the Rescue Ranger
1 in 18. Secondary has 24 rows: a Wrangler 3 in 24. So a "Gunslinger with a Rescue Ranger" (a poor pair, per
[Community]) is as likely as any other set, and no bot plays any of them differently.

### The archetypes

| # | archetype | loadout, by preference (defindex) | how it plays | what changes in the decisions |
|---|---|---|---|---|
| A1 | **Nest** (stock) | shotgun 9, pistol 22, wrench 7 | sentry to level 3, dispenser behind it, teleporters; repairs, spy-checks, holds | defensive; leaves the nest only to fetch metal or to relocate; target rule as in §1 |
| A2 | **Wrangler nest** (Highlander defence) | shotgun 9, or Rescue Ranger 997 / Wrangler 140 / wrench 7, or Jag 329 | A1, plus manual aim when the sentry is out-ranged or under siege: 2/3 of damage absorbed, about double the fire rate, no range limit ([Sentry Gun]); the sentry is off for 3 s after the Wrangler is put away (`TFSentryGun.cs:151`) | wrangles when an enemy shoots the sentry from beyond 1,100 u, or the sentry is under 50 % health with an enemy sighted. While wrangling, the bot's look is the sentry's aim: it targets the enemy hurting the sentry. It stops 3 s before it must use the wrench (the reboot), or once nothing has been sighted for 2 s |
| A3 | **Rescue Ranger nest** (mobile defence) | Rescue Ranger 997, pistol 22 or Wrangler 140, wrench 7 or Jag 329 | repairs from range with bolts (60 health a bolt at 4 health a metal, [Rescue Ranger]; mod: `TFProjectileEntities.cs:1698`); lifts a building from 250-5,500 u for 100 metal ([Rescue Ranger]; mod `TFBuildingPickup.cs:90-93`), and is marked for death while hauling | stands back from the nest (250-600 u, in cover), repairs by bolt while the sentry is shot; lifts the sentry away from a lost fight instead of dying beside it; relocates more readily (lower stay threshold) |
| A4 | **Gunslinger battle Engineer** (attack, Payload BLU, 6s) | Gunslinger 142, Frontier Justice 141, or Widowmaker 527, or shotgun 9 / pistol 22, or Wrangler 140 | mini-sentries for 100 metal, quick to build, 150 health for him ([Sentry Gun], [Community]); drops them behind corners, near health packs or as decoys; fights with his gun | aggression up, stays near the front with the team, never upgrades (a mini cannot be), destroys and rebuilds rather than hauls (the wiki's rule unless the way is short), rebuilds within 10 s of a mini's death; with Frontier Justice he banks revenge crits and spends them on the next fight |
| A5 | **Teleporter builder** (attack "helper") | shotgun 9, pistol 22, Eureka Effect 589, or Jag 329 | exit forward first, entrance at spawn; Eureka: teleport home for metal and halved teleporter cost (`mod_teleporter_cost`), 20 % less metal from pickups, slower construction ([Community]); Jag: 30 % faster construction, less damage | builds the teleporters before the sentry; a level 2 sentry at most; moves the exit with the front; with the Eureka, teleports home (`eureka_teleport`, `src/tf2/server/Player/TFEurekaTeleport.cs:52`) when out of metal and more than 2,000 u from a source |
| tail | Short Circuit 528 (`mod_ammo_per_shot`, metal ammo): orb kills projectiles at the nest. Pomson 588: drains über and cloak at range. Widowmaker in A1. Panic Attack 1153 is a shotgun row with its own pattern | the Short Circuit fires its orb at a rocket or sticky sighted within 400 u of a building (a measurement to make), at 65 metal a shot ([Community]); the Pomson prefers a Medic with charge as a target | |

**Pairs to avoid** (a draw never makes them): the Gunslinger with the Rescue Ranger or the Jag ("pairs poorly"
[Community]: a mini is repaired in one punch); the Wrangler with the Gunslinger is allowed ([Community] lists it as a
good pair).

## 3. What the port and the AI can do today

### The game side (`src/tf2/`): almost everything an Engineer needs exists

| behaviour | status | where |
|---|---|---|
| `build N M` / `destroy N M` verbs | yes | `TFBuildCommands.cs:45,141` |
| blueprint placement: walk, rotate (alt-fire), place (fire), metal paid, the refusals (no-build, respawn room, fit, sight, other building) | yes | `TFBuilderPlacement.cs:124-306, 376-470` |
| no-build volumes | yes | `src/tf2/server/Entities/Rooms/FuncNoBuild.cs` |
| wrench: construction boost, repair, upgrade, refill, sapper removal | yes | `TFBaseObject.cs:912-996, 1010, 1069`; `TFSentryGun.cs:427`; `TFSapperUse.cs:221-252`; `TFWrenchBehaviour.cs:110` |
| sentry levels 1-3, rockets, range 1,100 | yes | `TFSentryGun.cs:80`; `TFSentryRocket.cs` |
| Wrangler (laser aim, shield, 3 s fade) | yes | `TFSentryWrangle.cs`; `TFSentryGun.cs:150-156` |
| Gunslinger mini-sentry | yes | `TFBaseObject.cs:271-284`; `TFBuilderPlacement.cs:157` (`MakeMiniFor`) |
| dispenser, teleporter (levels, recharge) | yes | `TFDispenser.cs`, `TFTeleporter.cs` |
| haul (+attack2), Rescue Ranger lift | yes | `TFBuildingPickup.cs:19-29, 48-103` |
| Rescue Ranger bolt repair | yes | `TFProjectileEntities.cs:1698` |
| Eureka teleport | yes | `TFEurekaTeleport.cs:52` |
| Frontier Justice revenge crits | yes | `src/tf2/server/Weapons/TFRevengeHook.cs:32` |
| Short Circuit, Pomson, Widowmaker | yes (works rows) | `items_working.tsv` evidence column |
| the HUD's building panel (exists, building, sapped, built, level, progress, health, ammo, kills), per player | networked | `src/tf2/shared/Buildings/TFBuildingResourceNetState.cs:62-130` |
| Southern Hospitality | partial | `items_working.tsv` |

**Game-side gap:** none blocks the archetypes. One to check: the teleporter upgrade cost (`TFObjectDefs.cs:45` says
100, while [Engineer]'s totals imply 200).

### The AI side (`src/ai/tf2/server/`)

| need | today | where |
|---|---|---|
| ability rows: build and destroy each building | yes, the eight rows exist, unused | `Knowledge/TFAbilities.cs:54-61` |
| facts about the buildings (cost, build time, upgrade cost, health by level) | yes | `Knowledge/TFKnowledge.cs:19-20, 152-159`; sentry range at `:196` |
| own metal | yes, read off the HUD | `Senses/TFBotGameView.cs:65-66`; `src/ai/core/Snapshot/PerceivedView.cs:48` |
| buildings in sight (type, team, builder slot, level, health for its own team) | yes, sighted things only; placing and carried ones are hidden | `TFBotGameView.cs:180-191, 202-217`; `PerceivedView.cs:122-131` |
| own buildings **out of sight** (the HUD panel: sapped, building, built, progress, ammo) | **no** | `SelfState` has only `Metal` (`PerceivedView.cs:33-48`) |
| the blueprint's validity (the player sees it red or green) | **no** | `ThingFacts` drops placing buildings (`TFBotGameView.cs:184`) |
| a class subtree | **no**: every class runs the Soldier's | `Trees/TFClassTrees.cs:15` |
| weapon handling | Soldier-only names (rocket launcher and shotgun) | `Trees/TFDecider.Soldier.cs:17, 92-155` |
| class pick | always Soldier | `TFBotClassPick.cs:22` |
| use of dispensers or teleporters by any bot | **no** (`grep Dispenser\|Teleporter src/ai/tf2/server/Trees` is empty) | |
| map knowledge for spots: choke map, sight table (1,024 u, nodes of 128 u), nav cells with footprints, flow fields, spawns, respawn and resupply zones, objectives (points, flags, cart) | yes | `src/ai/core/Nav/ChokeMap.cs:25-66`; `src/ai/nav/Nav/NavSight.cs:30-60`; `src/ai/core/Nav/ICellSight.cs:8-18`; `src/ai/core/Nav/NavGraph.cs:96-110`; `src/ai/core/Nav/FlowField.cs:15, 92`; `Knowledge/TFMapKnowledge.cs:33-54`; `src/ai/core/Knowledge/Objectives.cs:51`; `src/ai/core/Memory/BotMemory.cs:11` (Sight, Chokes) |
| no-build zones in map knowledge | **no**: `ZoneKind` has only RespawnRoom and Resupply | `src/ai/core/Knowledge/MapKnowledge.cs:112-119`; `TFMapKnowledge.cs:48-54` |
| learned spots (the experience store) | the kinds are reserved: `TFSpotKind.Sentry, Dispenser, TeleporterIn, TeleporterOut`; only `Hold` is gathered | `Nav/TFExperienceHolds.cs:7-16`; `src/ai/core/Experience/ExperienceView.cs:129`; the Defend pick `Trees/TFDecider.Holds.cs:33-46` is the pattern to copy |
| the sentry-shy logic (avoid enemy sentries) | yes, enemy-team only, so an Engineer will not flee its own sentry | `Trees/TFDecider.Things.cs:132-172` |
| long sight lines (the Sniper table, to keep buildings off them) | **no**: C.3.2's `LongLine` pass is not built | plan C.3.2 |
| the objective front (next checkpoint, the point being contested) | the objectives' states and positions only; no "the front" helper | `Objectives.cs:51` |

**The biggest AI gaps:**
1. No sense of the bot's own buildings off-sight.
2. No sense of its blueprint's validity.
3. No sentry-spot finder.
4. No Engineer subtree, and the hands are hardwired to the Soldier's weapons.
5. No coherent loadout.
6. No team use of dispensers or teleporters, so the Engineer's value to its team never shows in objective time
   until the other classes walk to a dispenser and step on a teleporter.

## 4. The design for the code lane

### 4.1 Ship order

1. **A1 Nest** with stock items: the build loop, maintenance, the spot finder. This is most of the work.
2. **A4 Gunslinger** (only the loop's numbers change: mini, no upgrades, aggression).
3. **A2 Wrangler nest**.
4. **A3 Rescue Ranger**.
5. **A5 Teleporter builder** (it needs the other classes to use teleporters, §4.8).
6. The tail (Short Circuit orb, Pomson target preference) after these.

### 4.2 The archetype draw and the loadout

- The archetype is drawn by seed at spawn (the foundation's draw), weighted by mode and role:
  - defending, or KOTH: A1 0.4, A2 0.3, A3 0.2, A4 0.1, A5 0.
  - attacking (A/D, Payload BLU, or CTF with the role Attack): A4 0.35, A5 0.3, A1 0.2, A3 0.15.
  - The weights are a starting guess, to be tuned by the outcome store (Part G.4 "class and sub-class picks").
- The loadout is the archetype's per-slot preference list (§2's table): the first allowed item, then the stock item.
  Reskins of the base item are drawn uniformly by seed **within** the archetype's choice, so looks vary and behaviour
  does not. A slot the archetype does not name keeps today's seeded draw, restricted to items that are not a
  forbidden pair (§2).
- Each preference list is held as data in the Engineer's file. A fact: no drawn set is ever a forbidden pair, and
  every archetype's set is legal under `ItemIsAllowed`.

### 4.3 How a bot finds spots on a map it has a navigation for

All of this is **map knowledge** (plan §5.0: a bot knows the layout before it plays). It is computed once per level
and per defended target on the AI pool, from the graph (`NavGraph`), the sight table (`ICellSight`), the choke map
(`ChokeMap`), the voxel space (`IVoxelSpace.Free`, `NavSight.cs:8-17`), the objectives and the spawns. It is cached
per target id, like the choke map's cache (`src/ai/server/Nav/AiNav.cs:355-373`). Nothing in it reads a live entity.

**Step 0: the inputs per team.**
- `own[c]` and `enemy[c]`: the travel cost from each team's spawn to cell c. Build a `FlowField` (`FlowField.cs:92`)
  to each team's spawn cell, with one field per spawn group, taking the minimum. This is stock's "incursion distance".
  The field is costed toward its objective, so on one-way drops it differs slightly from cost from the spawn. Accept
  that, or build it on reversed edges.
- `T`, the defended target: the control point the team must hold (or the next one it attacks); the next checkpoint
  ahead of the cart (Payload); the own capture zone or flag home (CTF); the hill (KOTH). This is the same choice as
  stock's (`tf_bot_engineer_move_to_build.cpp:68-126`). A helper `Front(team)` reads `ObjectiveKnowledge`
  (§4.9 asks the foundation for it).

**Step 1: the sentry candidates.** A cell c is a candidate when all of these hold:
- it admits standing (`NavCell.Admits`), with a footprint of at least 48 x 48 u. The building box is about 40 u plus
  the 4 u padding (`TFBuilderPlacement.cs:85`); confirm the hull from `TFObjectModels.cs`.
- it is not in a respawn room, and not in a no-build zone (this needs `ZoneKind.NoBuild`, §4.9);
- (CP) it is not on the capture area, and not more than 150 u below T (stock `:164-173`);
- its distance to T is at most 1,100 × 1.1 u (stock `:175-178`);
- there is a clear line of fire from c + 60 u to T + 70 u (stock's heights, `:182-185`), by a voxel ray;
- `own[c] < enemy[c]` (KOTH: stock `:150-155`; everywhere as a preference, as a hard rule only in KOTH).

**Step 2: the score.** For each candidate:

`S(c) = w1·Cover(c) + w2·Range(c) − w3·Exposure(c) − w4·LongLine(c) + w5·Height(c) + w6·Learned(c) − w7·Crowd(c)`

| term | what it is | source |
|---|---|---|
| `Cover(c)` | the sum of `ChokeMap.ScoreOf(k)` over cells k with `enemy[k] < enemy[T]` (the enemy's side of the target), visible from c (`ICellSight.Visible`) and within 1,100 u: it sees where the enemy comes from | plan C.3.2 ("sees a choke between the enemy and the objective") |
| `Range(c)` | the share of those approach cells whose distance from c is 600-1,100 u: enemies are hit near the gun's range | [Community]: "shot near the Sentry's maximum range" |
| `Exposure(c)` | `ICellSight.Exposure(c)`, the share of places in range that see c | [Community]: avoid the open |
| `LongLine(c)` | the share of enemy-side cells at 1,100-2,500 u from which a voxel ray reaches c + 40 u: out-ranged by Soldiers and Snipers. Sample at most 64 cells (the top enemy-side chokes beyond range) | [Competitive]: Soldiers and Snipers out-range it |
| `Height(c)` | `clamp((floor(c) − mean floor of the visible approach cells) / 128, −1, 1)`: high ground is good, pits are bad | [Community]: "High ground"; avoid pits |
| `Learned(c)` | normalised uses and kills of experience spots of kind `Sentry` within 64 u of c (`ExperienceView.Spots`, `ExperienceView.cs:129`); 0 with an empty store | Part G.4 "spots" |
| `Crowd(c)` | 1 if a friendly sentry (sighted, or called out) is within 300 u (twice the 146 u blast radius) | [Community]: not side by side |

The weights start at w1 1, w2 0.5, w3 0.75, w4 1, w5 0.3, w6 by Discipline (`SpotTrust`, the hold pick's rule), and
w7 2. They are measured on cp_granary and ctf_2fort by the gate (§4.7) and recorded with their commit.

**Step 3: the pick.** Draw from the top 8 by `exp(S/τ)` with the bot's seeded stream (τ 0.25), so two Engineers and
two lives differ. Stock draws by surface area to avoid the mesh's bias (`:270-287`); cells are not uniform here, so
weight by footprint as well. Hold the spot for the life, and keep it across lives while the sentry stands (stock
`:340-358`).

**Step 4: the facing.** The facing is the yaw from the spot to the score-weighted mean of the approach cells it
covers. This is stock's "orient sentry towards where enemies enter" (`tf_bot_engineer_build_sentrygun.cpp`, the
invasion areas).

**The other spots:**
- **Dispenser.**
  - Nest (A1, A2): 75 u behind the sentry, along its facing (stock `tf_bot_engineer_build_dispenser.cpp`:
    `mySentry->GetAbsOrigin() - 75 * BodyDirection2D()`).
  - Team-serving variant (A3, or two Engineers): 150-300 u behind, on a cell with `Exposure` under the sentry's, on
    the team's way to T (the own flow field's next cells), and not seen from `LongLine` cells.
  - Never more than 500 u from the sentry (stock `tf_bot_engineer_building.cpp:370`).
- **Teleporter entrance.**
  - Within `own[c] ≤ 1,500` (stock `tf_bot_max_teleport_entrance_travel`) of the spawn's exit, on the own team's way
    out (the flow field toward T).
  - On flat ground: stock wants a normal z of at least 0.99 (`tf_bot_teleport_build_surface_normal_limit`). The nav
    has no normal, so take cells whose footprint corners lie within 2 u of one floor height.
  - Built "as you leave spawn" ([Guide]) and during setup.
- **Teleporter exit.**
  - The entrance is at least 3,000 u of travel away (stock `tf_bot_min_teleport_travel`), else don't build.
  - Within 300 u of the sentry on defence (stock `:460`). On attack, within 768 u of T's approach (plan C.3.2) and
    `≤ 2,500` travel from T (stock `tf_bot_max_teleport_exit_travel_to_point`).
  - Minimum `Exposure`, not visible from the enemy-side chokes.
  - Facing along the team's flow toward T ([Guide]: "face away from walls").
- **Gunslinger mini (A4).** The same finder with T = the bot's current fight front (the team's lead, C.6.1). The radius
  falls to 600 u, w4 and w5 are halved, and a bonus goes to cells within 256 u of a health pack (`MapKnowledge`
  items) or behind a corner (`Exposure` under 0.2) ([Community]).

**Placement through the player's path.** The bot walks to stand at `spot − forward × d`, faces the spot's yaw, sends
the row (`build_sentry`), and fires when the blueprint shows valid. `d` is the build distance the blueprint walks,
read from the walk (`TFBuilderPlacement.cs:473-474`, `PlacementSpot.BuildDistance`; the build box is
`TFObjectModels.Hull` plus 4 u, `:467-470`). On an invalid blueprint it does
stock's search: a 0.1-0.25 s step in a random direction and a re-aim, at most 5 tries
(`tf_bot_engineer_build_sentrygun.cpp`). Then it marks the spot failed for the level and takes the next best.

### 4.4 The Engineer's decisions (A1 as the base; the other archetypes override numbers)

Phases (a state in the Engineer's file, scored into the root as one branch, `Build`, beside Fight and Survive):

1. **Setup or first life on attack:**
   - A1, A2, A3: entrance first if the exit rule can be met, then the sentry spot.
   - A5: entrance, then the exit forward.
2. **Go to the spot.** Pick up metal on the way when `Metal < cost` (stock suspends for ammo,
   `tf_bot_engineer_build_sentrygun.cpp`, `CB_NEED_RESOURCES`). Ammo packs come from `MapKnowledge` items and the
   own resupply.
3. **Build the sentry.** Hit it while it builds (the boost).
4. **The dispenser** once the sentry stands, is above 50 % health and nothing has hurt the bot for 1 s (stock: not
   under attack, retry every 10 s).
5. **Maintain**, by stock's priority list (`tf_bot_engineer_building.cpp:154-171`):
   1. sentry sapped;
   2. dispenser sapped;
   3. sentry hurt, or damaged in the last 1 s;
   4. sentry building;
   5. dispenser building;
   6. sentry level under 3 (only with a metal source within 2,000 u: stock `IsMetalSourceNearby`, `:181-207`);
   7. dispenser hurt;
   8. dispenser level under the sentry's.

   Stand within 75 u of both, crouched; hold the wrench (`SelectWeapon`) and fire at the target building's centre
   (`LookKind.Point`).
6. **The teleporter exit:** every 30 s if missing (stock `:425-465`), when not under attack.

**Fight overrides inside the nest:**
- A Spy, or anyone sapping, comes first, with the shotgun.
- Within 100 u of its own sentry the bot does not take up a target other than a Spy; it keeps repairing (stock
  ShouldAttack).
- An enemy within 400 u that the sentry cannot see (no sentry kill in 2 s and the target in the bot's sight): the
  shotgun.
- The sentry destroyed: fight if the killer is within 750 u, else retreat to the dispenser or the nearest cover and
  rebuild (stock retaliate range).

**Relocate:**
- Every 4 s, check whether T moved (a point capped, the next checkpoint changed) and the sentry is out of position.
  Out of position means more than 2,500 u of travel from T (stock `tf_bot_engineer_max_sentry_travel_distance_to_point`),
  or, on Payload, the cart is more than 1,100 u past the sentry along the track (stock `:243`).
- Then:
  - if the sentry has not fired for 10 s (stock `:338`), haul it (`IN_ATTACK2` facing it) when level ≥ 2 and the new
    spot is within 1,500 u. The 1,500 u is a measurement to make: a haul at 90 % speed against a rebuild's 10-30 s.
  - otherwise destroy and rebuild.
  - A4 always destroys and rebuilds; A3 lifts from range.
- Plan C.3.2's fact: rebuilt within 1,024 u of the new front within 90 s.

**Retreat:** an über seen coming (the `Invulnerable` condition on an enemy within 1,500 u), or 3 or more enemies
sighted near the nest with the sentry under 50 %. Then haul the sentry if it is in reach and level 3, else leave, and
rebuild at the next spot back.

**Wrangler (A2):**
- Draw the Wrangler when the sentry has taken damage in the last 1 s from an enemy at more than 1,100 u from it, or
  the sentry is under 60 % health with an enemy sighted.
- Look and fire at the target (the sentry fires where the bot aims).
- Put it away after 2 s with nothing sighted, or when the sentry is under 25 % and needs the wrench, bearing the 3 s
  shield fade.

**Class change:** none while the bot owns a sentry or a teleporter exit (stock `CanChangeClass`, `tf_bot.cpp:1156-1163`).

### 4.5 Ability rows needed

Existing: `build_sentry`, `build_dispenser`, `build_teleporter_entrance`, `build_teleporter_exit`, the four
`destroy_*` (`TFAbilities.cs:54-61`), `voice_sentry_ahead` and `voice_spy`.

New, in the Engineer's table:
- `pickup` (`IN_ATTACK2`, 2 ticks: the haul; the same press places a carried building's rotation, so the applier
  presses only when facing an own building within 150 u, `TFBuildingPickup.cs:96`);
- `rotate_blueprint` (`IN_ATTACK2`, 2 ticks while placing, `TFBuilderPlacement.cs:309`);
- `eureka_teleport 0` / `eureka_teleport 1` (spawn and exit, `TFEurekaTeleport.cs:52`; check its argument);
- `voice_need_teleporter` / `voice_move_up`: optional (stock speaks MOVEUP on relocation, `tf_bot_engineer_building.cpp:361`).

Placement and repair are not rows: they are `Fire` with a `Point` look and a `SelectWeapon`.

### 4.6 Facts to write (each red once against a mutant)

1. Spot finder on a synthetic graph (a choke, a long corridor, a pit): the top spot sees the choke and is not on the
   corridor's long line; mutants with `LongLine` or `Cover` dropped pick the corridor or the pit. Red.
2. Candidate rules: no candidate in a respawn room, a no-build zone or on the point; none farther than 1,210 u; KOTH
   candidates all satisfy `own < enemy`.
3. The facing points within 30° of the approach mean.
4. Dispenser: the A1 spot is 75 u behind the sentry; the team variant is within 500 u and less exposed than the sentry.
5. Teleporters: no exit within 3,000 u of travel of its entrance; the entrance within 1,500 of spawn.
6. Maintenance priority: given panel states (sapped, hurt, building, level), the work target matches stock's order,
   one fact per rung.
7. Relocation: after a scripted capture (the objective feed changes), the bot's next spot is near the new T; mutant
   (no check) red. Plan C.3.2.
8. Fight override: a disguised-then-revealed Spy at the nest is targeted; a Soldier at 900 u is not chased while
   repairing.
9. Loadout: each archetype's draw holds its items or the stock fallback; no forbidden pair over 500 seeds.
10. Archetypes differ: A4's mean distance to the team's lead is under A1's by at least 2x (a monotonic style fact,
    C.3.1).
11. No class change with an own sentry standing.

### 4.7 Live measures (by the foundation's A/B harness)

User ruling first:
- **(a) Defending:** the enemy's time to the first capture, and the captures per 10 min against them, Engineer bot
  (A1) against a Soldier in its slot.
- **(b) Attacking:** own team's time to the first capture, and the teleporter uses per minute, A5 against A1 against
  a Soldier in its slot.
- **(c) Map progress:** Payload cart distance at 5 min.

Then the class numbers (counters):
- seconds to the first sentry after spawn;
- sentry uptime share (built and not sapped), level-3 seconds;
- sentry kills (the panel's `Kills`, `TFBuildingResourceNetState.cs:126`);
- dispenser heal and metal given (a counter to add);
- teleports through the bot's teleporter;
- relocations, with the distance to the new front at 90 s;
- placement refusals per build;
- deaths within 200 u of the nest.

Archetype differences:
- A4 has the most kills by the player, and the fewest level-3 seconds;
- A2 has the most damage dealt at range by the sentry;
- A3 has the highest sentry survival under siege;
- A5 has the most teleports.

Numbers are reported with the commit and map, over 3 runs.

### 4.8 What the Engineer needs other classes to do

His value reaches objective time only if teammates:
- step on his teleporter entrance when it is ready and the exit's way to T is shorter than walking (cost of walking
  from the entrance minus cost from the exit, more than the recharge's wait);
- use a dispenser as a heal and ammo source in `Survive`.

These are shared rows, not the Engineer's. The foundation or the Medic/Heavy lanes should own them; without them,
measure (b) cannot move.

### 4.9 What the code lane needs from the shared P7 foundation

1. **The Engineer subtree file** with its own branch scored into the root (`Build`), and the general Fight with a
   per-class override hook ("may this target be taken now?" for the 100 u rule and the Spy priority).
2. **The kit read:** defindex, `item_class` and attribute classes per slot (to tell the Gunslinger, Wrangler, Rescue
   Ranger and Eureka apart), and the weapon-kind index of each slot for `SelectWeapon`.
3. **The archetype draw and a coherent loadout** (§4.2), with forbidden pairs.
4. **The class pick:**
   - composition (stock defence roster: one Engineer per 4 players, at least 1, at most 1/2/3/3 by difficulty;
     attack: only from team size 5, at most 1; `tf_bot.cpp:833-857`);
   - the Engineer ready flag;
   - no change while it owns a sentry or an exit.
5. **New senses, honest by the HUD:**
   - (a) the bot's own building panel off-sight, from `TFBuildingResourceNetState`'s row for the bot's slot (exists,
     building, sapped, built, level, progress, health, max health, ammo, kills) as a per-bot HUD block. A
     `SelfState` extension or a binding-side read.
   - (b) whether its own blueprint is valid (the builder sees it red or green), from `ITFBuilderPlacement`'s walk.
   - (c) whether it is carrying a building (`ITFBuildingPickup.Carried`).
6. **Map knowledge:** `ZoneKind.NoBuild` from `func_nobuild` (`TFMapKnowledge.cs:48-54`), and a `Front(team)` helper
   over `ObjectiveKnowledge` (the point to hold or take, the next checkpoint, the flag home). The spot finder itself
   can live in the Engineer's files; its cache wants a home in the nav layer beside the choke cache.
7. **The experience feed for `spot` rows** of kinds Sentry, Dispenser, TeleporterIn and TeleporterOut from **every**
   player's buildings (human engineers teach bots, as G.3's feed does for holds): bin, yaw, seconds standing, kills.
   This is the long-term source of good spots on a known map.
8. **Shared team behaviours** (§4.8): take a teleporter, heal at a dispenser.
9. **Harness counters** (§4.7) from the building panel and the teleporter.

## 5. Sources

Web:
- [Community] Community Engineer strategy: https://wiki.teamfortress.com/wiki/Community_Engineer_strategy
- [Guide] Basic Engineer strategy: https://wiki.teamfortress.com/wiki/Engineer_guide
- [Competitive] Engineer (competitive): https://wiki.teamfortress.com/wiki/Engineer_(competitive) (it says the
  competitive scene changes often, and some of it may be dated)
- [Engineer] https://wiki.teamfortress.com/wiki/Engineer
- [Sentry Gun] https://wiki.teamfortress.com/wiki/Sentry_Gun
- [Rescue Ranger] https://wiki.teamfortress.com/wiki/Rescue_Ranger
- Not readable: https://forums.tf2center.com/topic/775-playing-engineer-in-highlander/ (403). Highlander-specific
  claims above rest on [Competitive] and [Community] only.

Stock TFBot (`../source-sdk-2013-valve/src/game/server/tf/bot/`):
- `behavior/engineer/tf_bot_engineer_build.cpp` (the first action, ShouldHurry, ShouldAttack)
- `behavior/engineer/tf_bot_engineer_move_to_build.cpp` (CollectBuildAreas, SelectBuildLocation, the teleporter
  travel cvars)
- `behavior/engineer/tf_bot_engineer_building.cpp` (maintenance order, out of position, rebuild timers, retaliate
  range)
- `behavior/engineer/tf_bot_engineer_build_sentrygun.cpp`, `_build_dispenser.cpp`, `_build_teleport_entrance.cpp`,
  `_build_teleport_exit.cpp`
- `tf_bot.cpp:825-880` (rosters), `:1156-1163` (CanChangeClass), `:2655-2662` (IsAmmoFull: metal ≥ 200)
