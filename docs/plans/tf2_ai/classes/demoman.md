# P7 research: the Demoman

Base `225fe2e96`. Repo cites are `path:line` at that commit. Stock TFBot
cites are in the Valve clone (`../source-sdk-2013-valve`, `src/game/server/tf/bot/...`), with real line numbers.
Game knowledge cites a URL (section 5). Where the wiki and stock code disagree on a number, the port follows stock
code, and this file gives the port's number with the wiki's beside it.

## 1. The class's job, by mode

**Body.** 175 health and 93 % speed (wiki *Demoman*). The class table gives the port's numbers, and the decider reads
its health off that table (`src/ai/tf2/server/Trees/TFDecider.cs:350-362`). The Demoman has no hitscan weapon. He wins
at medium range and loses long duels to Snipers and Scouts, and loses close fights to anyone who closes on him (wiki
*Community Demoman strategy*, *Demoman match-ups*).

**Weapons.**
- **Pipes** (grenade launcher, primary). 1200 u/s (`src/tf2/server/Projectiles/TFRadiusDamage.cs:75`, stock
  `TF_GRENADE_LAUNCER_MIN_VEL`; the wiki measures about 1216). They fall at 0.4 × 800 = 320 u/s² (`TFRadiusDamage.cs:264`,
  `src/tf2/shared/Movement/TFViewVectors.cs:35`). A pipe that hits the world first becomes a "roller" on a 2.0 s fuse
  (`TFRadiusDamage.cs:163`; the wiki says about 2.3 s). A direct hit does 100, a pipe's splash about 60, and the blast
  radius is 146 (`:160`).
- **Stickies** (stickybomb launcher, secondary). Holding fire charges the shot; the bomb leaves on release, or by
  itself after 4 s (`src/tf2/shared/Weapons/TFShellBehaviours.cs:23-36`). Launch speed is 900 to 2400 u/s by charge
  (`TFRadiusDamage.cs:226-256`; the wiki measures 805 uncharged). A sticky arms after 0.8 s (`:173`), and at most 8 are
  out (`:191`). Damage is 120 base, 60-124 by range (wiki *Stickybomb Launcher*). The secondary trigger detonates them
  from any weapon in hand (`src/tf2/server/Player/TFClassSpecialSkill.cs:63-73`).
- **Melee.** A bottle, or a sword with 72 u reach, 128 u while charging (`src/tf2/shared/Weapons/TFSwordHook.cs:25-28`).

### 1.1 Pub play by mode (wiki community pages; stock TFBot's own choices noted)

| mode | where he stands, at what range | what he does |
|---|---|---|
| **CTF** (ctf_2fort) | Defence: out of sight of the intel room's doors and stairs, 300–800 u from his trap. Offence: the bridge and battlements, lobbing at 400–1100 u. | Defence: traps the doors and the intel, detonates on sight or sound. Offence: spams the enemy's sentry spots from around corners, sticky-jumps to the battlements. The carrier's escort traps the chase route behind him (wiki: "leave stickies behind you when fleeing"). |
| **CP attack** (A/D) | Behind the front line, 500–1000 u from the sentry nest, at a corner the sentry cannot see | Kills sentries with stickies from cover (stock `tf_bot_destroy_enemy_sentry.cpp:638-680`) and pushes with an Über. Once the point falls, he carpets it so the defenders cannot retake it. |
| **CP defence** (A/D, last points) | Behind the point, out of the attackers' line, about 1000 u back | Traps the chokes and the point (stock `scenario/capture_point/tf_bot_defend_point.cpp:211`, `tf_bot_defend_point_block_capture.cpp:118`) and detonates on the capture alert (wiki). Falls back to the next point when this one is lost. |
| **5CP** (cp_granary, cp_badlands) | First to mid, at the choke facing the enemy's entrance | Sticky-jumps the rollout. Locks the enemy's mid entrance, spams it, carpets the point, and goes on the offensive when they retreat (wiki *Mid fight*). |
| **KOTH** | The point's flanks and the stairs onto it | Carpets the point and traps the flanks. A Kritz target. |
| **Payload** | Attack: beside the cart, 400–900 u behind the front. Defence: ahead of the cart. | Attack: kills sentries and lobs pipes over the cart. Defence: traps the cart's path ahead of it (stock `scenario/payload/tf_bot_payload_guard.cpp:119`). |

**Competitive (6s, Highlander).** He is one of the two classes that seldom change, and leagues allow one (wiki *6s*,
*Mid fight*). In 6s he and the Medic, with a Soldier, are "the combo"; he is the usual Kritzkrieg target, gets to mid
first and locks a choke (*Mid fight* gives per-map sticky-jump routes on Granary, Badlands and Freight). The standard
6s kit is the Iron Bomber, the stock stickybomb launcher and the bottle; the Scottish Resistance is the defensive pick
and the Tide Turner the aggressive one (wiki *Demoman weapons (competitive)*). Highlander has more sentries and
Heavies, so the Demoman is the sentry-buster, and the defending Demo runs the Scottish Resistance on last.

**Target priority** (wiki match-ups; stock threat danger 0.6 for a Demoman, `tf_bot.cpp:3057-3091`): (1) a sentry
that blocks the objective, with stickies, never by walking into its range; (2) a Medic and the patient beside him (the
splash takes both; multiple stickies break an overhealed pair); (3) a Heavy at range (his damage falls off, the
Demoman's does not); (4) an enemy standing on a trap; (5) a Sniper, only by a jump or charge on a route he is not
watching. He avoids Scouts and Pyros up close.

**Push, hold, retreat.** He holds at chokes, and pushes behind an Über or once the enemy retreats. He retreats when
caught at close range (a close-range threat, or health under about 40 % with no Medic): he backs off and lays two
stickies at his own feet (wiki match-ups: "lay pairs of Stickybombs near you to deter a direct rush"). He reloads in
cover: 0.6 s a pipe, 0.67 s a sticky after the first.

**Movement tech.** The sticky jump: one sticky at the feet, jump and crouch, detonate. It costs 45–114 health (wiki), so
he jumps only at full health or overhealed. The pipe jump (38–73) is a trick, not a route (plan C.3.2 rules it out);
the Loose Cannon's jump costs 64–73. Then the parachute (B.A.S.E. Jumper) after a jump, and the demoknight's charge.

**With teammates.** The Medic pockets him and heals him in spawn before a jump. Quick-Fix overheal makes a jump safe
and lets the Medic follow (wiki). The Kritz is popped on a grouped enemy. Stickies go on a sentry while a Spy saps it.
He carpets a point his Scouts are capping.

**What beats him, and how good players play around it.**

| threat | how a good Demoman plays around it |
|---|---|
| Hitscan at range | Breaks line of sight and plays off corners, using lobs over cover. |
| Scout dives and close Soldiers | Keeps two stickies at his feet and detonates when they arrive. Keeps a teammate beside him. |
| Pyro airblast (reflected pipes mini-crit him, and stickies get scattered) | Spreads his stickies so one blast cannot clear them all, and uses charged stickies, which are hard to reflect. |
| Spies (cloak passes over stickies) | Traps the entrances. The Scottish Resistance can spy-check without spending the whole trap. |
| Bullets, melee and certain explosions destroy stickies (wiki *Stickybomb Launcher*) | Hides stickies on ceilings, behind doors and on ammo packs. |

**Mistakes that read as a bot:** (1) pipes fired in a straight line that fall short, never an arc; (2) detonating at
nothing, or before the stickies arm (0.8 s); (3) all 8 stickies on one spot in the open, laid standing still; (4)
walking onto his own trap, or splashing himself inside 146 u; (5) stickies only for traps, never in a fight; (6)
holding range with a sword (this port does it today: §3); (7) charging into walls (stock charges whenever 100 u ahead
is open, `tf_bot.cpp:4522-4535`); (8) a trap laid with a half-empty clip; (9) forgetting stickies are out, so a ninth
detonates the oldest; (10) staying in a sentry's range.

## 2. Play styles by weapon set

The Demoman's works rows are in `game/mod_tf2/scripts/items/items_working.tsv`:
- **61 rows, all `works`.** One is never drawn: the Stickybomb Jumper (265) has `cannot_pick_up_intelligence`, and
  `src/tf2/server/Econ/BotInventory.cs:148` never draws an item that carries it.
- **Primary:** 13 `tf_weapon_grenadelauncher`, 1 `tf_weapon_cannon`, 2 boots (`tf_wearable`).
- **Secondary:** 26 `tf_weapon_pipebomblauncher`, 4 `tf_wearable_demoshield`, 1 `tf_weapon_parachute`.
- **Melee:** 7 `tf_weapon_sword`, 1 `tf_weapon_katana`, 3 `tf_weapon_bottle`, 1 `tf_weapon_stickbomb`, 1
  `tf_weapon_shovel`.

**Reskins** (same `item_class`, no attributes) go under their base item:

| base item | reskins |
|---|---|
| Grenade Launcher 19 | 206, 1007, 15077, 15079, 15091, 15092, 15116, 15117, 15142, 15158 |
| Stickybomb Launcher 20 | 207, 661, 797, 806, 886, 895, 904, 913, 962, 971, 15009, 15012, 15024, 15038, 15045, 15048, 15082, 15083, 15084, 15113, 15137, 15138, 15155 |
| Bottle 1 | 191, 609 |
| Eyelander 132 (the same attributes) | 266 Headtaker, 482 Nessie's Nine Iron, 1082 Festive Eyelander |
| Chargin' Targe 131 | 1144 Festive Targe |
| Ali Baba's Wee Booties 405 | 608 The Bootlegger (the same attributes) |

| # | archetype | primary | secondary | melee | how it plays, and what changes in the bot's decisions |
|---|---|---|---|---|---|
| A1 | **Hybrid** (stock; the plan's C.3.1 "Hybrid") | 19 GL | 20 Sticky | 1 Bottle | **Range:** pipes in the fight at 400–1100 u; stickies at chokes, and at his feet when rushed. **Trap:** in a lull. **Sentry:** stickies from cover. Holds with the team. The default, and the fallback for every row. |
| A2 | **Demoknight** (C.3.1) | 405 Booties (`add_maxhealth; mult_player_movespeed_shieldrequired; kill_refills_meter; charge_turn_control`), or 608 | 131 Targe (`mult_dmgtaken_from_fire; mult_dmgtaken_from_explosions`), or 406 Splendid Screen (`charge_recharge_rate; charge_impact_damage`) | 132 Eyelander (`is_a_sword; add_maxhealth; decapitate_type`) or its reskins; alternatives 327 Claidheamh Mòr (`mod_charge_time; kill_refills_meter; mult_dmgtaken`) and 404 Persian Persuader (`ammo_gives_charge; charge_meter_on_hit`) | **No launchers.** Melee range only, with the charge as the gap-closer. **Routes:** flanks. **Targets:** Snipers, Spies, Medics and lone players; he avoids Heavies, Pyros (airblast stops a charge) and sentries. **Ability:** a charge whose swing lands with the meter at 40 or under, for a crit (§4.3). Heads make him faster (+8 % a head, at most 4: `TFSwordHook.cs:42`, stock `tf_shareddefs.h:1818`) and more careful. A poor Medic pocket (plan C.3.1). |
| A3 | **Trapper** (C.3.1) | 19 GL, or 996 Loose Cannon | 130 Scottish Resistance (`set_detonate_mode; stickies_detonate_stickies; add_max_pipebombs; sticky_arm_time; mult_postfiredelay; mult_maxammo_secondary`) | 1 Bottle | **Defence only** (wiki: "useless in offense"). Up to 14 stickies (wiki) in two or three separate clusters. **Detonation:** only the cluster in view goes (`TFProjectileEffects.cs:704-718`), so he looks at the cluster the enemy is on and detonates it. Arms later (+0.8 s). Holds a spot that sees his clusters. Drawn only on a defending role. |
| A4 | **Combo / competitive** | 1151 Iron Bomber (`fuse_mult; grenade_no_bounce; mult_explosion_radius`) | 20 Sticky, or 1150 Quickiebomb (`sticky_arm_time; stickybomb_charge_rate; stickybomb_charge_damage_increase; stickies_detonate_stickies; mult_clipsize`) | 1 Bottle | The 6s kit. **Pipes:** they do not roll, so they stay where they land as short traps at a choke. **Mobility:** sticky-jumps forward to mid and to the point. **Teammates:** stays by his Medic; the first Kritz target. **Quickiebomb:** detonates in the air in a fight (it arms 0.2 s sooner and charges faster), and clears enemy traps. |
| A5 | **Hybrid knight** | 19 GL, or 1151 | 131 Targe, or 1099 Tide Turner | 132 Eyelander, or 327 | **Range:** pipes up to mid range, then the charge to finish. No sticky trap and no sticky sentry kill. |
| A6 | **Sentry buster** | 308 Loch-n-Load (`mult_clipsize` 3; `mult_dmg_vs_buildings`; `mult_projectile_speed`; `set_detonate_mode` shatter) | 20 Sticky | 1 Bottle | **Direct hits only:** the pipes break on the world, so no rollers and no banks. Kills a building in two direct hits (wiki match-ups). Drawn against a seen sentry (plan C.4). |

**Long tail**, each its own small rule set after A1–A6: 996 Loose Cannon (double donk: hold, then release so the ball
hits and explodes within 0.5 s); 1101 B.A.S.E. Jumper (opens after a jump; wiki: weak indoors); 307 Ullapool Caber (one
blast on the first hit, 124 to the enemy and 52 to himself, the row's evidence); 154 Pain Train
(`add_player_capturevalue`: a capper on CP and Payload); 357 Half-Zatoichi (`honorbound`: no holster without a kill, so
never for a bot that switches weapons); 172 Skullcutter (`mult_dmg`, `mult_player_movespeed`).

**The independent draw today breaks these sets.** `BotInventory.cs:94-108` draws each slot on its own (`PickIndex`,
`:183`):
- About 4 of 31 secondaries are shields: that bot has no stickybomb launcher, and its primary is a grenade launcher 13
  times in 16.
- About 2 of 16 primaries are boots: that bot has no primary weapon, and about 27 times in 31 it has stickies, not a
  shield, so the boots' speed (`mult_player_movespeed_shieldrequired`) never applies.
- Swords and the katana are 8 of 13 melees, so most bots carry a sword with no charge.

## 3. What the port and the AI can do today

### 3.1 The game side: present

| behaviour | where |
|---|---|
| Sticky charge and release, auto-launch at 4 s | `src/tf2/shared/Weapons/TFShellBehaviours.cs:23-36`; speed `src/tf2/server/Projectiles/TFRadiusDamage.cs:226-256` |
| Detonate from any weapon (attack2); arm skip; Scottish in-view mode; stickies destroy stickies | `src/tf2/server/Player/TFClassSpecialSkill.cs:63-73`; `src/tf2/server/Projectiles/TFProjectileEffects.cs:698-721`; tray `src/tf2/server/Projectiles/TFProjectileEntities.cs:2920`, `:2993` |
| 8 stickies, arm time 0.8 s, pipe fuse 2.0 s, blast radius 146 | `TFRadiusDamage.cs:191`, `:173`, `:163`, `:160` |
| Shield charge | Start `src/tf2/server/Player/TFShieldCharge.cs:80-118`: only at meter 100, for 1.5 s (`:74`). Meter: drains over 1.5 s, refills at 8.3/s, about 12 s (`src/tf2/shared/ConVars/TFSharedConVars.cs:100-103`). Movement: 750 u/s (`:41`, `src/tf2/shared/Movement/TFMovementRules.cs:1009`). Yaw cap: `TFMovementRules.cs:1040`. Crit ladder: 40 or under crits, 75 or under mini-crits (`TFShieldCharge.cs:450-465`). Bash reaches 48 u (`:77`). |
| Swords: reach, the heads' speed and health | `src/tf2/shared/Weapons/TFSwordHook.cs:25-42`; `src/tf2/server/Player/TFSwordHeads.cs:44` |
| The meter and the heads on the net state (what a HUD reads) | `src/tf2/shared/Player/TFPlayerNetState.cs:309` (Decapitations), `:322` (ChargeMeter) |
| The sticky-jump state (game event `sticky_jump`) | `src/tf2/server/Player/TFBlastJump.cs:30`, `:127` |
| Each unlock pressed live | the TSV's evidence column: Loose Cannon donk, Quickiebomb, Iron Bomber, Loch-n-Load, the Targe's bash, the Eyelander's heads, the B.A.S.E. Jumper's server condition (2026-11-08) |

### 3.2 The game side: missing or partial

- G1. **Charge turn control is deferred.** `charge_turn_control` waits for the Tide Turner, Booties and Bootlegger
  (each row's evidence; `docs/plans/tf2/deferred.tsv`), so every charge steers at the base cap. The Tide Turner reads as
  a Targe until it is ported.
- G2. **A shield's charge time.** A `mod_charge_time` worn on the shield waits on the wearables feeding the manager
  (`TFShieldCharge.cs:110-115`). The Claidheamh Mòr's, on the sword, works.
- G3. **The sticky's net state has no owner** (`src/tf2/shared/Projectiles/TFProjectileNetStates.cs:76-84`). Stock
  networks `m_hLauncher` (`src/game/shared/tf/tf_weapon_grenade_pipebomb.cpp:69,74`), so a client knows its own
  stickies. The server entity has `OwnerIndex` (`TFProjectileEntities.cs:936`): the AI reading it is honest, and the net
  state should carry it for parity.
- G4. **The HUD does not draw the charge meter.** Its sub-mode is missing (`src/tf2/ui/screens/TFHudDemomanPipes.cs:19`);
  the note there says shields have no port, which is stale. UI only: the bot reads the net state, as it reads metal.
- G5. **The Caber's re-arm at resupply was not seen** (row 307's evidence).
- G6. **Stale note.** `deferred.tsv:38` says no shield items ship. They do now.

### 3.3 The AI side: what exists

| what | where |
|---|---|
| The view lists pipes and stickies as things with their team | `src/ai/tf2/server/Senses/TFBotGameView.cs:138-139` |
| The applier taps the second trigger and holds the first for a Spray style, so a held fire charges a sticky | `src/ai/server/Apply/BotCommandApplier.cs:617-624` |
| Ability programs take an argument | `src/ai/core/Brain/Abilities.cs` (`AbilityCall.Pack`, the `Program` run) |
| The lob, which HL2MP's frag uses | `src/ai/core/Predict/Predictors.cs:83` (`AimLead.Lob`); `src/ai/hl2mp/server/Trees/Hl2mpDecider.cs:848` |
| The choke map and cover search | `src/ai/core/Nav/ChokeMap.cs:25` via `BotMemory.Chokes` (`src/ai/core/Memory/BotMemory.cs:175`); `src/ai/core/Motion/CoverSearch.cs:9`, used by `TFDecider.Fight.cs:58-90` |
| Seen sentries remembered | `src/ai/tf2/server/Trees/TFDecider.Things.cs:10`, `:24-31` |
| The CTF, points and course branches; Survive at 40 %; blast dodging | `TFDecider.cs:259-275`, `:37`; `TFDecider.Things.cs` |

### 3.4 The AI side: missing (each blocks the Demoman reading as a Demoman)

- A1. **Every bot is a Soldier.** The pick is fixed (`src/ai/tf2/server/TFBotClassPick.cs:22`), and every class runs
  the Soldier's subtree (`src/ai/tf2/server/Trees/TFClassTrees.cs:15`). The foundation lane owns this.
- A2. **No arc.** `Splash` is only a projectile with no fall (`TFDecider.Soldier.cs:84-85`), and `Engage` aims with
  `AimLead.Point` and no gravity (`:96-110`). A pipe at 900 u falls about 90 u short (½ × 320 × 0.75²). The bot never
  lobs, banks or aims at the feet with a pipe.
- A3. **The sticky's speed reads 0.** `StockSpeed` has no `PipebombRemote` arm (`src/ai/tf2/server/Knowledge/TFKnowledge.cs:85-88`),
  so `WeaponOf` (`:121-123`) takes the script's speed. The test copy of the script names none
  (`src/tf2/tests/TestData/weapons/tf_weapon_pipebomblauncher.txt`), so the shipped script needs checking. With 0 the
  bot aims a sticky like a hitscan gun. It needs the charge curve, which `TFRadiusDamage.StickySpeed` has.
- A4. **Melee is treated as a gun.** The fire reach is 2000 for anything but the shotgun (`TFDecider.Soldier.cs:112`)
  and the fight holds 320–900 u (`TFDecider.cs:46`, `:485-494`), so a demoknight stands off and swings at the air. No
  melee approach exists anywhere (`grep -i melee src/ai/tf2/server` is empty).
- A5. **Weapon choice is the Soldier's.** `_launcher` and `_shotgun` name the Soldier's items (`TFDecider.Soldier.cs:17`,
  `TFDecider.cs:116-117`). A Demoman never switches: it fights all life with what it spawned holding.
- A6. **The bot's state has nothing a Demoman needs.** No stickies out, no charge meter and no heads in `SelfState`
  (`src/ai/core/Snapshot/PerceivedView.cs:33-47`). Metal is the only TF counter (`TFBotGameView.cs:65-66`).
- A7. **The bot cannot tell its own stickies.** The sticky's owner reads 0 in the view (`TFBotGameView.cs:160`).
- A8. **No sticky jump.** The jump is the Soldier's (`TFDecider.Jump.cs:60`), the simulator flies rockets only
  (`src/ai/tf2/server/Jump/BlastJumpSim.cs`: "a Soldier firing at his own feet"), and the blast links are for rockets
  too (plan C.3.2 `JumpReach.Blast`).
- A9. **No sentry kill.** `AvoidsSentries` is true for all (`TFDecider.Things.cs:102`), so a Demoman waits at a
  sentry's edge instead of killing it.
- A10. **No Demoman ability rows**, and nothing in TF uses `ActionKind.AltFire` (`src/ai/tf2/server/Knowledge/TFAbilities.cs:50-66`).
- A11. **The loadout is drawn per slot** (`BotInventory.cs:94-108`; §2's broken sets). The foundation owns this.
- A12. **No Payload or KOTH branch** (that is P8). The traps there wait on P8's objective branches.

## 4. The design for the code lane

### 4.1 Order to ship

1. **A1 Hybrid**, at stock: pipes with an arc, stickies at the feet when rushed, sticky traps on the defending role,
   stickies on sentries, weapon choice and detonation. The bulk of the work, and the fallback for every row.
2. **A2 Demoknight:** the melee approach, the charge rule and heads. Melee is shared with every class's melee: offer it
   to the foundation.
3. **A3 Trapper:** the Scottish Resistance's in-view detonation, on a defending role.
4. **A4 Combo:** the Iron Bomber, the Quickiebomb and the sticky jump. It needs stickies in the simulator (A8), the
   longest item, so it ships after 1–3.
5. **A5 Hybrid knight**, then **A6 Loch-n-Load**, then the long tail.

### 4.2 The archetype draw and the loadout

**The archetype table** (the foundation's type). Each row has a weight per role, then a preference list per slot.

| row | weight per role (attack / defend / symmetric) | primary | secondary | melee |
|---|---|---|---|---|
| Hybrid | 0.40 / 0.30 / 0.35 | 19 | 20 | 1 |
| Demoknight | 0.20 / 0.10 / 0.20 | 405, then 608 | 131, then 406, then 1144 | 132, then its reskins 266, 482 and 1082, then 327 and 404 |
| Trapper | 0 / 0.30 / 0.10 | 996, then 19 | 130 | 1 |
| Combo | 0.25 / 0.15 / 0.30 | 1151 | 20, then 1150 | 1 |
| Hybrid knight | 0.10 / 0.05 / 0.05 | 19, then 1151 | 131 | 132 |
| Loch | 0.05 / 0.10 / 0 | 308 | 20 | 1 |

**The weights are a starting point to tune, not a measurement.** Their sources:
- the plan's C.3.1 for the rows;
- the competitive page's "Iron Bomber most common" for the Combo row;
- the wiki's "defence only" for the Trapper;
- the plan's C.4 rule that a sentry the team has seen moves weight to Loch and Hybrid (stock `tf_bot.cpp:1212-1235`
  picks the Demoman first against a sentry).

**How the draw works.**
- Each slot takes the first item `ItemIsAllowed` passes, else the slot's stock item (C.3.1).
- Reskins are cosmetic: the looks draw (C.3.3) may swap a base item for one of its reskins, but play reads only the
  base item.
- The draw is by seed, at spawn and on each class change. It holds for the map; `ai_bot_reroll_on_map` redraws it.
- Composition cap: 1 a team in 6s and Highlander (wiki *6s*). Stock's pub limit is 2–3 a team by difficulty
  (`tf_bot.cpp:833-870`); keep stock's 2 at Easy and 3 above it.

**The brain reads the kit, not the archetype name.** The foundation's kit read hands it each slot's `item_class` and
attribute classes, and the class file derives:

| flag | from |
|---|---|
| `HasStickies` | secondary `tf_weapon_pipebomblauncher` |
| `HasShield` | secondary `tf_wearable_demoshield` |
| `InViewDet` | `set_detonate_mode` on the secondary |
| `NoRoll` | `grenade_no_bounce` |
| `Shatter` | `set_detonate_mode` on the primary |
| `Mortar` | `grenade_launcher_mortar_mode` |
| `Sword` | `is_a_sword` |
| `Honorbound` | `honorbound` |

A pinned or hand-picked loadout therefore plays right, whatever its archetype's name.

### 4.3 Decision rules

Each number names its source: **S** stock TFBot, **W** the wiki, **P** the port's code, **M** a measurement to make.

**Weapon choice** (every decide; a change at most once a second, as `HandsEverySeconds`, `TFDecider.Soldier.cs:41`).

| situation | weapon |
|---|---|
| target within 350 u (M) and closing | the stickies: lay up to 2 at the feet (W), back off |
| target at 350–1100 u (M; S's desired range is 500, `tf_bot.cpp:3212-3213`) | the pipes |
| target past 1100 u, or not in sight | stickies charged to reach (W: "charge … against Snipers"), or no fire |
| primary dry, target in sight | the stickies |
| under 120 u, both clips dry | melee (with a sword and a shield, the melee rules below) |

Never fire a pipe or sticky whose point of impact is within 146 u of the bot (P `:160`). This replaces the 160 of
`SelfBlastUnits`.

**Pipe aim (A2).**
- Aim along `AimLead.Lob(eye, led target, 1200, 320)`, the low arc.
- Out of the lob's reach, or with a flight over the 2.0 s fuse (P), bank a roller off the floor 100 u short of a
  grounded target, or hold the shot.
- A target on the ground: aim at its feet (W: "feet shots give knockback"). An airborne one: its body.
- The aim error is the existing curve.

**Sticky aim (A3).**
- Aim along `Lob` with speed `StickySpeed(c)`. Find the least charge `c` for which the lob exists, and hold fire for
  `c` seconds plus a tick. "Better to overshoot": S holds 1.1 × the charge (`tf_bot_stickybomb_sentrygun.cpp:187`).
- Under 800 u, no charge (S `tf_bot_sticky_base_range`, `:14`).
- Reload to a full clip before a trap or a sentry (S `tf_bot_prepare_stickybomb_trap.cpp`, `m_isFullReloadNeeded`).
- No auto-reload in a fight, so the stickies go out fast (S `tf_bot_behavior.cpp:265`).

**Detonation.** Every 0.3–1.0 s, faster at high Alertness (S `tf_bot_tactical_monitor.cpp:62`; the scale is M), the bot
checks its own armed stickies:
- **It detonates when** an enemy is within 150 u of one (S `:102`), seen within its reaction time or heard capping or
  stepping nearby. Buildings are skipped and left to the sentry rule (S `:90`).
- **Armed** means 0.8 s after launch (P), or 0.6 s for the Quickiebomb, from its attribute. Each bot keeps its own
  stickies (`ThingFacts` with its own owner, once A7 is fixed) and their launch times.
- **In-view detonation** (`InViewDet`): first look at the cluster nearest the enemy (within the game's 0.975 dot,
  `TFProjectileEffects.cs:657`, `:724-737`), then detonate.
- **Air detonation** (Quickiebomb, Combo): an armed sticky in flight within 100 u of a target (M) is detonated. An
  airborne sticky's radius is 85 %, ramping to full over 2 s (P `TFRadiusDamage.cs:175-189`).

**Sticky trap** (stock's `CTFBotPrepareStickybombTrap`, on honest inputs).

- **When**, every condition: the role is defend, or escort of the own flag's carrier (S `tf_bot_escort.cpp:78`), or
  the bot is on or near an own point it holds (S `tf_bot_defend_point.cpp:211`) or a cart it guards (P8); no enemy it
  knows of within 500 u (S `:188`); not hurt in the last 1.0 s (S `:86`); fewer than 8 out (S `:12`; the Trapper's
  count from `add_max_pipebombs`, 14 per the wiki).
- **Where:** the top three to five `ChokeMap` cells within 1000 u of the objective on the enemy's side, the objective
  itself last; 1–2 stickies a spot (W: "spread so one blast can't clear them"; S density 0.0001/u², at least 1,
  `:248`); the Trapper's in two or three clusters at least 300 u apart (M).
- **How:** 2 s at most to aim each sticky and 0.15 s after each launch (S `:260`, `:40`); no shooting while laying (S
  `ShouldAttack` NO, `:309`), but a sighted enemy within 500 u ends the trap. Then hold a cover cell that sees the trap
  or is within 1200 u of it (M) and that the enemy's main approach does not see (`CoverSearch` against the choke cell).
- **Re-laid** when a sticky is taken (destroyed, or the count fell with no detonation of the bot's), after a
  detonation, and after setup ends.

**Sentry with stickies** (stock's `CTFBotStickybombSentrygun` and its corner spot). This replaces `AvoidsSentries` for a
Demoman with `HasStickies`.
- **The spot.** For a remembered sentry, go to a cover cell within 1000 u (S `:674`) that the sentry's cell does not see
  (`NavSight`), from which a lob lands within 75 u of the sentry's centre (S `:91`).
- **The lob search.** Over pitch −85° to 85° and yaw ±30° (S `:227-240`), keeping the least charge. Never searched past
  1500 u (S `tf_bot_max_sticky_launch_at_sentry_range`). On the way, if the sentry fires on him, he fires back with
  pipes (S `:657-659`).
- **Lay.** Reload in full, then lay 3 stickies, or 4 on a level 3 (S `:151`; 3 for health 216 is M). It is "hurrying":
  no dodge, no retreat (S `ShouldHurry` / `ShouldRetreat`).
- **Detonate** once all are at rest and the sentry is alive. At rest is S `m_bTouched` (`:167`), here a speed under
  30 u/s (P `TFStickybomb.RestSpeed`).
- **Give up** if hurt, or if no aim is found in 3 s (S `:53`).

**Demoknight** (`HasShield`).

*Approach.* A flank route: cells off the `ChokeMap` top set, unseen from a remembered sentry. Never inside a sentry's
range.

*Charge* (attack2) when every condition holds:
- the meter is 100 (the net state's `ChargeMeter`, once A6 is fixed; P `TFShieldCharge.cs:113`);
- the target is in sight, within 5° of the eye (M; the yaw cap is P `TFMovementRules.cs:1040`), on a clear walk along
  the eye;
- it is 560–1000 u away.

*Why 560–1000 u.* The charge runs at 750 u/s (P). The meter at the swing sets the crit (P `GrantCrit`): a crit at 40
or under, after 0.9 s of a 1.5 s charge; a mini-crit at 75 or under, after 0.375 s. The swing lands 0.2 s after the
press (stock `SmackDelay` default, `tf_weapon_parse.cpp:96`), at 128 u of reach (P). So the start distance is
128 + 0.2 × 750 + 0.9 × 750 ≈ 950 u for a crit and about 560 u for a mini-crit. Measure it (M) and tune the band.

*Swing:* press attack when the target is within 128 + 0.2 × (750 + its speed toward the bot) u.

*Escape:* charge to cover, or to a health pack the route reaches in a straight line, at health under 40 % with the
meter at 100.

*Never charge* into a Pyro, at a spun-up Heavy facing him, or toward a sentry. (Stock keeps a Pyro's flamethrower out
against a Demoman, `tf_bot.cpp:3415-3420`.)

*Targets*, in order: Sniper, Medic, a revealed Spy, an Engineer away from his sentry, then anyone hurt.

*Heads:* from 3 heads (W: "risky if you die") the Survive threshold rises from 0.4 to 0.55 (M).

*Persian Persuader:* seeks ammo packs, which give charge (`ammo_gives_charge`).

**Sticky jump** (C.3.2; A4 needs it).
- **When:** on a route over 1200 u (the plan's `blastRouteUnits`), with no enemy in sight, a full clip and nothing
  carried. Never as the carrier away from its escort.
- **Health:** over max − 25, or under it with a pack in reach of the landing (the Soldier's `JumpHealthRule`). This is
  stricter than the plan's floor of 80, because one sticky costs 45–114 (W: "only jump at full health or overheal").
- **How:** the simulator's recipe, with the sticky kind added to `BlastJumpSim`. As a program row: look down and tap a
  sticky at minimum charge, wait 0.8 s for it to arm, jump and crouch, detonate on the jump's tick, and fly. The wait
  for the arm makes this row unlike the rocket's.
- **On 5CP:** the first jump of a life is offered only in the setup and rollout window, at spawn.

**Positions by role** (all M): attack 300–700 u behind the team's foremost teammate; defend a cover cell 600–1100 u
behind the objective (stock defends near the point); a demoknight off the main route, 0–600 u ahead of the team.

**Retreat** at health under 40 % (the existing `HurtShare`), or with two enemies within 500 u and the clip under 2: lay
2 stickies at the feet when it has them, then the Survive branch.

### 4.4 Ability rows (the Demoman's own table, appended, so indices hold)

| row | run | what |
|---|---|---|
| `demo_sticky_lob` | Program | Argument: the charge in ticks, with the aim packed beside it. Hold attack that many ticks, then release. It is a program because the release is the shot. |
| `demo_detonate` | Buttons | `IN_ATTACK2`, 2 ticks: the tap (P `TFClassSpecialSkill.cs:63-73`). AltFire Tap would also do; a row keeps it out of the hands' state. |
| `demo_charge` | Buttons | `IN_ATTACK2`, 2 ticks, with a shield worn (P: the same key, with no launcher). |
| `sticky_jump` | Program | Flies a sticky recipe. |
| `demo_cannon_cook` | Program | Argument: the hold in ticks before the release (Loose Cannon, long tail). |
| `voice_incoming`, `voice_go` | Command | Optional, for the calls: `voicemenu 0 2` / `0 3` by `voicecommands.txt`, checked as the Soldier's rows were. |

### 4.5 Facts to write (one behaviour each; each red under its mutant)

1. **The lob reaches.** A pipe at a target 900 u away on flat ground lands within 60 u of it, and the flat aim misses
   by more. Mutant: gravity 0 in the aim.
2. **The sticky's charge.** Its speed fact is 900 at no charge and 2400 at full. The charge chosen for 1500 u is between
   0 and 4 s.
3. **The weapon band.** Pipes at 700 u. Stickies at the feet at 250 u when the target closes. Stickies when the primary
   is dry.
4. **No self-blast.** No shot whose impact is within 146 u.
5. **Detonation.** It detonates when a seen enemy is within 150 u of an own armed sticky. Negatives: not for an unarmed
   sticky (0.5 s old), a teammate, an enemy's sticky or a building.
6. **In-view looks first.** With the Scottish Resistance, the view comes onto the cluster before the detonate goes.
7. **The trap.** Laid only with no enemy within 500 u and no hurt within 1 s; at most 8 stickies, over at least 2 cells
   at least 150 u apart; the clip full first; ended by a sighted enemy within 500 u.
8. **The sentry kill.** From a cell the sentry does not see, it lays 3 and detonates only when all 3 are at rest. It
   never enters the sentry's range. Mutant: `AvoidsSentries` back on.
9. **Charge timing.** It charges only at meter 100, with the target within 5° and 560–1000 u away, and swings at reach.
   On a scripted target, the hit carries the crit flag in 8 of 10 seeds.
10. **No melee at range.** A demoknight's fire is never sent past 128 u. This is the mutant of A4 today.
11. **The kit read.** A Booties, Targe and Eyelander kit plays the knight. A plain kit with a reskin plays the Hybrid. A
    sword with no shield never charges.
12. **The draw.** No drawn Demoman has boots without a shield; boots come only in the Demoknight archetype; over 500
    seeds the archetype shares are within 5 points of the weights.
13. **Sticky jump.** Never under max − 25 without a pack in reach, never with an enemy in sight; on `ss_ai_tf_jump` a
    2000 u route takes under 70 % of the walk's time (the plan's C.3.2 fact).

### 4.6 Live measures (the user's ruling first: time to the objective and map progress)

**The setup.** Each pinned class and archetype runs on the foundation's harness, three runs of 300 s each, on
cp_granary and ctf_2fort.

| measure | control | target |
|---|---|---|
| Attack: time to the first point touched or flag taken | an all-Soldier team | Hybrid and Combo no slower than the control. Combo with the sticky jump at least 15 % faster to the first point (M). |
| Defence: the enemy's captures, and caps prevented while a Trapper holds | a Hybrid on the same role | fewer captures than that Hybrid |
| Defence: detonations with a kill; trap uptime (the share of alive time with 4 or more stickies out) | | |
| Enemy sentries destroyed per sighted sentry, and seconds from sight to kill | the Soldier | a higher share for the Demoman |
| Self-damage (own blast ÷ damage taken; jumps counted apart) | | under 25 % (M) |

**Each archetype must play differently** (the jank tool's "how each actor plays"):

| archetype | measure |
|---|---|
| Demoknight | median engagement range under 200 u; over 1 charge a minute; crit share of melee kills (M) |
| Hybrid | median engagement range 400–1000 u |
| Trapper | median distance from his kills to his stickies under 200 u; distance travelled per minute under a third of a Hybrid's |
| Combo | over 1 sticky jump a life |

**Counters** for the harness hook: `stickies_laid`, `detonations`, `det_kills`, `sentries_killed`, `charges`,
`charge_crits`, `heads_max`, `sticky_jumps`, `self_damage`.

### 4.7 What the code lane needs from the P7 foundation

1. **The class subtree hooks** in its own file: weapon choice (replacing the Soldier's `_launcher`/`_shotgun` pair); a
   fight-band override per weapon; the hands' aim, so the class can lob (or a shared `Engage` that lobs whenever
   `ProjectileGravity > 0`, which every lobbing class wants); a say in `AvoidsSentries`; a lull hook, where the trap
   runs; the role and objective positions.
2. **The kit read**, with attribute classes per slot (§4.2's flags).
3. **The archetype draw**, and a coherent loadout through the seam to `BotInventory`.
4. **Per-class ability tables**, appended.
5. **The class pick**, with a Demoman cap of 2 (Easy) or 3, from stock's rosters. Ready flag: not ready until A1's
   facts pass.
6. **Shared-file changes**, not the Demoman's own:
   - a. `SelfState`, or a TF self extension, carrying `StickiesOut`, `ChargeMeter` and `Heads`. Read them as metal is
     read (`TFBotGameView.cs:65-66`, from `TFPlayerNetState.cs:309,322`; stickies out from the tray, which the HUD counts).
   - b. The sticky's `OwnerSlot`, filled from `TFStickybomb.OwnerIndex` (`TFBotGameView.cs:160`), with G3 for parity.
   - c. `TFKnowledge`'s sticky speed and charge curve (A3).
   - d. A shared melee approach, for every class with melee.
   - e. The sticky kind in `BlastJumpSim`, and blast links for the Demoman. These are the jump lane's files.

## 5. Sources

**Web**
- TF2 Wiki: [Community Demoman strategy](https://wiki.teamfortress.com/wiki/Community_Demoman_strategy) ·
  [Demoman](https://wiki.teamfortress.com/wiki/Demoman) ·
  [Demoman match-ups](https://wiki.teamfortress.com/wiki/Demoman_match-ups) ·
  [Mid fight](https://wiki.teamfortress.com/wiki/Mid_fight) ·
  [6s](https://wiki.teamfortress.com/wiki/6s) ·
  [Demoman weapons (competitive)](https://wiki.teamfortress.com/wiki/Demoman_weapons_(competitive)) ·
  [Stickybomb Launcher](https://wiki.teamfortress.com/wiki/Stickybomb_Launcher) ·
  [Grenade Launcher](https://wiki.teamfortress.com/wiki/Grenade_Launcher) ·
  [Basic Demoman strategy](https://wiki.teamfortress.com/wiki/Demoman_guide) ·
  [Loose Cannon](https://wiki.teamfortress.com/wiki/Loose_Cannon) ·
  [Iron Bomber](https://wiki.teamfortress.com/wiki/Iron_Bomber) ·
  [Loch-n-Load](https://wiki.teamfortress.com/wiki/Loch-n-Load)
- [Steam discussion naming the hybrid knight's primaries](https://steamcommunity.com/app/440/discussions/0/1471967615852344151)

**Valve clone** (`../source-sdk-2013-valve/src/game/`)
- The trap: `server/tf/bot/behavior/demoman/tf_bot_prepare_stickybomb_trap.cpp` (`:12`, `:40`, `:86`, `:188`, `:248`,
  `:260`, `:309`).
- The sentry kill: `server/tf/bot/behavior/demoman/tf_bot_stickybomb_sentrygun.cpp` (`:14`, `:53`, `:91`, `:151`,
  `:167`, `:187`, `:227-240`), and `server/tf/bot/behavior/tf_bot_destroy_enemy_sentry.cpp` (`:25`, `:51`, `:638-680`).
- Detonation: `server/tf/bot/behavior/tf_bot_tactical_monitor.cpp:58-110`.
- Aim and reload: `server/tf/bot/behavior/tf_bot_behavior.cpp` (`:47`, `:262-266`, `:773-790`: the ballistic elevation,
  0.01°/u capped at 45°).
- Picks and choices, `server/tf/bot/tf_bot.cpp`: `:833-870` the rosters; `:1212-1235` a sentry makes the pick a
  Demoman; `:3057-3091` threat danger; `:3179-3213` the desired range; `:3357-3363` the primary; `:3415-3420` a Pyro
  against a Demoman; `:4224` the splash target; `:4512-4535` the shield charge.
- The scenario triggers: `server/tf/bot/behavior/tf_bot_escort.cpp:78`,
  `scenario/capture_point/tf_bot_defend_point.cpp:211`, `tf_bot_defend_point_block_capture.cpp:118`,
  `scenario/payload/tf_bot_payload_guard.cpp:119`.
- Shared: `shared/tf/tf_weapon_grenade_pipebomb.cpp:69,74` (`m_hLauncher` networked),
  `shared/tf/tf_weapon_sword.cpp:295-309`, `shared/tf/tf_shareddefs.h:1818`, `shared/tf/tf_weapon_parse.cpp:96`.
