# P7 research: the Pyro

Research lane, 2026-11-20. Base `225fe2e96`. Repo cites are
`path:line` **at HEAD** (`git show HEAD:<path>`). This worktree also holds the foundation lane's uncommitted draft
(`src/ai/tf2/server/Classes/`, `Trees/TFDecider.Classes.cs`, `Trees/TFDecider.Hands.cs`, and edits to `TFAbilities.cs`
and `TFDecider{,.Soldier}.cs`). §4 is written against the hooks that draft declares (`ClassPlay`: `Hands`, `Engage`,
`FightInSight`, `OneShots`, `Weigh`, `Role`, `PlaceUtility`/`GoPlace`, `Died`, `Archetype`; ability blocks of 16 rows,
the Pyro's block at 16 × `TFClass.Pyro` (7) = rows 112..127, `src/tf2/shared/TFSharedDefs.cs:39`). Line numbers in
that draft will move, so I cite it by name only. Web facts carry a URL. A number with neither a URL nor a cite is
marked **measure**.

## 1. The Pyro's job, by mode

### 1.1 What every mode shares

- **Range.** The wiki gives the flame about 350 HU when the Pyro stands still, less when he walks forward
  ([Flame Thrower](https://wiki.teamfortress.com/wiki/Flame_Thrower),
  [Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy)). The port's flame manager
  launches flame points at 2450 u/s with drag 8.5/s and a 0.6 s life (+0.1 s random)
  (`src/tf2/server/Projectiles/TFFlameManager.cs:42-48`, drag applied as `speed -= dt*drag*speed` at `:374`). That
  gives 2450/8.5·(1−e^−5.1) ≈ **287 u** of travel before the point dies, plus the 12 u box (`:51`). The code lane
  measures the reach live; until then the bot assumes 280 u. Damage depends on the flame's age, not on distance:
  100 % for a new point down to 50 % at 0.5 s (`TFFlameManager.cs:57`; same on the wiki). So most of the damage lands
  well inside the reach. Stock TFBot uses 250 u as the Pyro's most range and 100 u as its wanted range
  (`../source-sdk-2013-valve/src/game/server/tf/bot/tf_bot.cpp:3139-3156`, `:3196-3199`).
- **Afterburn.** 8 a second for 3-10 s, built up by time in the flame
  ([Flame Thrower](https://wiki.teamfortress.com/wiki/Flame_Thrower)). The port caps it at 10 s
  (`src/tf2/server/Combat/TFBurn.cs:76`). Hit and run works: light the target, step back into cover
  ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy)).
- **Airblast (compression blast).** It costs 20 ammo (Degreaser 25, Backburner 50) and refires after 0.75 s. It
  reflects most projectiles as mini-crits, puts out burning teammates (+20 health to the Pyro), and pushes enemies off
  points, carts and ledges. It wastes an Über by pushing the pair apart or into the air, but not a Quick-Fix Über
  ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy),
  [Flame Thrower](https://wiki.teamfortress.com/wiki/Flame_Thrower)). In the port: cost from `tf_flamethrower_burstammo`
  20 through `mult_airblast_cost` (`src/tf2/shared/Weapons/TFShellBehaviours.cs:528-537`), refire 0.75 s (`:541`).
  It reaches a 128 u sphere centred 128 u in front of the eye (`src/tf2/server/Projectiles/TFFlameEffects.cs:185-188`,
  `:631`) and pushes players inside a 35° cone (`:368-371`, `:654`).
- **Target priority, from the sources.** The closest enemy inside flame reach first (stock fights only inside
  `GetMaxAttackRange`, `tf_bot_behavior.cpp:1355-1368`). An Übered enemy within 250 u is always shoved
  (`tf_bot.cpp:4116-4125`). A capper on a point is pushed off (`:4133-4137`). Spies: flames show cloaked Spies
  ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy)), and stock bots always see a
  burning Spy, cloaked or not (`tf_bot_vision.cpp:264-271`). Snipers and Engineers die to flares
  ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy)). The Pyro cannot kill a
  sentry from range, so stock never sends a Pyro to destroy one (`behavior/tf_bot_destroy_enemy_sentry.cpp:160-170`).
- **Push, hold, retreat.** Stock's Pyro never answers "retreat" in seek-and-destroy
  (`behavior/tf_bot_seek_and_destroy.cpp:159-167`). A Pyro defending a point goes straight for any enemy seen
  recently, by the safest route (`behavior/scenario/capture_point/tf_bot_defend_point.cpp:183-196`). Close in, stock
  circle-strafes, picking left or right every 3 s (`behavior/tf_bot_attack.cpp:39-60`). People: don't charge a group,
  ambush the weak, stay out of open ground and water, come from flanks, height and blind corners
  ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy),
  [Pyro strategy](https://wiki.teamfortress.com/wiki/Pyro_strategy)).
- **Movement tech.** Detonator and Scorch Shot flare jumps, at a health cost; the Thermal Thruster's launch
  ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy),
  [Dealing with Pyro](https://wiki.teamfortress.com/wiki/Dealing_with_Pyro): 2 charges, 15 s recharge). A Pyro has no
  jump of his own with stock weapons.
- **Teamwork.** He guards the Medic (the main competitive job, Highlander above all), the Engineer's nest (reflects,
  Spy checks, sapper removal with the Homewrecker/Maul in one hit or the Neon Annihilator in two) and the slow Heavy.
  He reflects for a Soldier and covers his close range. Two Pyros pair a shotgun with a flare. He lights a Sniper's
  arrows ([Pyro (competitive)](https://wiki.teamfortress.com/wiki/Pyro_(competitive)),
  [Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy); arrow lighting in plan
  C.3.2, `docs/plans/plan_bots_teams_tf2.md:672`).
- **What beats him, and how he plays around it.** Range: Snipers, Heavies and sentries. Water. Afterburn removed by
  packs, dispensers, Jarate and Milk. Enemy Pyros are immune to afterburn, so use the shotgun on them. Hitscan costs
  him nothing to reflect. Direct Hit rockets are hard to reflect
  ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy),
  [Dealing with Pyro](https://wiki.teamfortress.com/wiki/Dealing_with_Pyro),
  [Pyro (competitive)](https://wiki.teamfortress.com/wiki/Pyro_(competitive))). He plays around it by taking routes
  with cover and corners, using the shotgun or flare past flame range, and swapping class off a lost matchup.

### 1.2 By mode

| mode | pub play | competitive (6s / Highlander) |
|---|---|---|
| CTF (2fort) | Defence: guard the intel room and its corners, Spy-check teammates going past, airblast the carrier back. Attack: flank by the sewer or side routes, ambush at corners. The wiki names 2fort as a flank map for the Volcano Fragment + Panic Attack combo ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy)) | not played |
| CP attack/defend (Dustbowl, Gravel Pit) | Defence: sit by the Engineer's nest, reflect, check for Spies. Push cappers and Übers off the point (`tf_bot.cpp:4133-4137`). Attack: escort the Über and airblast the enemy's counter-Über; flank | Highlander: pocket the Medic; hold enclosed points such as Gravel Pit C ([Pyro (competitive)](https://wiki.teamfortress.com/wiki/Pyro_(competitive))) |
| symmetric 5CP | Flank mid; defend last | 6s: a situational swap for a Scout or Soldier at mid (a flank), then back off the class; last-point defence when behind on Übers ([Pyro (competitive)](https://wiki.teamfortress.com/wiki/Pyro_(competitive))) |
| KOTH | Ambush round the point's corners; airblast cappers off it | the same last-defence logic applies (enclosed point) |
| Payload | Defence: corners near the cart route, push the cart pushers, guard the nest. Attack: escort the cart and reflect spam. Avoid the Back Scratcher, which cuts the cart's healing ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy)) | not played much |

The AI plays CTF (`TFDecider.Ctf.cs`), control points (`TFDecider.Points.cs`) and jump courses today. Payload and
KOTH are P8 (`docs/plans/plan_bots_teams_tf2.md:1492`). P7's Pyro is built and measured on CTF and CP.

### 1.3 Beginner mistakes that make a bot read as a bot

1. **Flaming from out of range.** Holding M1 at 400 u and more. Today's bot does exactly this: it keeps 320-900 u
   (`Trees/TFDecider.cs:46`, `:485-493`) and fires at anything within 2000 u (`TFDecider.Soldier.cs:38`, `:112-115`).
2. **W+M1 straight down a corridor** into a Heavy or a sentry, without cover or a flank.
3. **Airblast spam**, which empties 200 ammo at 20 a blast. **Never reflecting**, which is just as bot-like.
4. **Flaming an Übered pair** instead of pushing it (`tf_bot.cpp:4116-4125`).
5. **Never Spy-checking**, or never putting out a burning teammate.
6. **Flaming another Pyro**, who is immune to afterburn, instead of switching to the shotgun.
7. **Chasing a Scout** across open ground; **standing in the open** against a Sniper.
8. **Instant 180° flicks and perfect reflects.** Players miss reflects; stock gives Easy 0 %, Normal 50 % and Hard
   90 % (`tf_bot.cpp:4083-4106`).

## 2. Play styles by weapon set

The Pyro rows of `game/mod_tf2/scripts/items/items_working.tsv` (cited `items:<line>`, the file's own line number):
66 `works` rows and one `partial` (Detonator). Folded by behaviour:

| base behaviour | rows (defindex) | item_class | attributes (table) |
|---|---|---|---|
| **Flame Thrower** (stock) | 21 (items:23); reskins, attribute `extinguish_restores_health` only: 208, 659, 798, 807, 887, 896, 905, 914, 963, 972, 15005, 15017, 15030, 15034, 15049, 15054, 15066, 15067, 15068, 15089, 15090, 15115, 15141, 30474; Rainblower 741 (Pyroland visuals, items:196) | tf_weapon_flamethrower | extinguish_restores_health |
| **Backburner** | 40 (items:36), Festive 1146 (items:323) | tf_weapon_flamethrower | +set_flamethrower_back_crit, mult_airblast_cost, mult_crit_chance |
| **Degreaser** | 215 (items:94) | tf_weapon_flamethrower | +mult_single_wep_deploy_time, mult_switch_from_wep_deploy_time, mult_wpn_burndmg, mult_airblast_cost |
| **Phlogistinator** | 594 (items:171) | tf_weapon_flamethrower | burn_damage_earns_rage, set_buff_type, airblast_disabled, mult_crit_chance |
| **Dragon's Fury** | 1178 (items:328) | tf_weapon_rocketlauncher_fireball | item_meter_charge_rate, mult_airblast_cost, dragons_fury_* |
| **Shotgun** | 12 (items:14) | tf_weapon_shotgun | none |
| Reserve Shooter | 415 (items:141) | tf_weapon_shotgun | mult_clipsize, mini_crit_airborne, mult_single_wep_deploy_time |
| Panic Attack and the multiclass shotgun reskins | 1153, 199, 1141, 15003, ... (items:79, 318, 327, ...) | tf_weapon_shotgun | table slot `primary`: **never reaches a Pyro**, see §3 |
| **Flare Gun** | 39 (items:35), Festive 1081 (items:302) | tf_weapon_flaregun | mult_maxammo_secondary, crit_vs_burning_FLARES_DISPLAY_ONLY |
| Scorch Shot | 740 (items:195) | tf_weapon_flaregun | mult_dmgself_push_force, or_minicrit_vs_playercond_burning, set_weapon_mode |
| Detonator (**partial**) | 351 (items:127) | tf_weapon_flaregun | blast_dmg_to_self, or_minicrit_vs_playercond_burning, set_weapon_mode |
| Manmelter | 595 (items:172) | tf_weapon_flaregun_revenge | energy_weapon_no_ammo, extinguish_revenge, ... |
| Thermal Thruster | 1179 (items:329) | tf_weapon_rocketpack | thermal_thruster, item_meter_charge_rate, ... |
| Gas Passer | 1180 (items:330) | tf_weapon_jar_gas | item_meter_charge_rate, item_meter_starts_empty, ... |
| **Fire Axe** | 2 (items:4), 192 (items:72), Lollichop 739 (items:194, Pyroland visuals only) | tf_weapon_fireaxe | none |
| **Axtinguisher** | 38 (items:34), Postal Pummeler 457 (items:155), Festive 1000 (items:291) | tf_weapon_fireaxe | attack_minicrits_and_consumes_burning, mult_dmg, mult_switch_from_wep_deploy_time |
| **Homewrecker** | 153 (items:59), Maul 466 (items:158) | tf_weapon_fireaxe | mult_dmg_vs_buildings, mult_dmg_vs_players, set_dmg_apply_to_sapper |
| Neon Annihilator | 813, 834 (items:223, 227) | tf_weapon_breakable_sign | crit_vs_wet_players, set_dmg_apply_to_sapper |
| Powerjack | 214 (items:93) | tf_weapon_fireaxe | heal_on_kill, mult_player_movespeed, mult_dmgtaken, provide_on_active |
| Back Scratcher, Volcano Fragment, Third Degree, Hot Hand | 326, 348, 593, 1181 (items:121, 125, 170, 331) | fireaxe / slap | (see table) |

The current numbers come from the wiki's [Pyro](https://wiki.teamfortress.com/wiki/Pyro),
[Axtinguisher](https://wiki.teamfortress.com/wiki/Axtinguisher), [Backburner](https://wiki.teamfortress.com/wiki/Backburner)
and [Phlogistinator](https://wiki.teamfortress.com/wiki/Phlogistinator) pages: Degreaser deploys 60 % and holsters
30 % faster, with −66 % afterburn. Backburner always crits from behind (2D dot > 0.8, about 73°) and has no random
crits. Phlogistinator: 300 fire damage fills the Mmmph meter, giving 10 s of crits minus the taunt, invulnerable during
the taunt, no airblast. Axtinguisher: 44 base, always mini-crits a burning target and puts it out, damage grows with
the afterburn left, +35 % speed for 4 s on a burning kill, 35 % slower holster. Flare Gun: 30 damage plus 60 of burn
over 7.5 s, crits on a burning target. Detonator: mini-crits burning targets, detonates on alt-fire, 50 % more blast
self-damage.

### Archetypes (pub names; competitive notes from [Pyro (competitive)](https://wiki.teamfortress.com/wiki/Pyro_(competitive)))

**A1 Ambusher / W+M1 (stock).** Flame Thrower, Shotgun, Fire Axe. It waits round a corner or comes from a flank,
closes to under 200 u, burns and airblasts. It takes the shotgun beyond flame range or against a Pyro. Decisions:
preferred range 120-200 u; high aggression; routes weighted to cover and corners (stock picks `SAFEST_ROUTE` for a Pyro
chasing, `tf_bot_defend_point.cpp:193`); circle-strafes in range. It reflects whatever comes at it.

**A2 Airblast guard / support.** Flame Thrower, Shotgun, Homewrecker (or Maul or Neon). It stays near a teammate: the
Medic, the nest or the Heavy. It reflects spam, puts out the burning, Spy-checks, removes sappers and pushes Übers.
This is the Highlander pocket role. Decisions: a leash of about 400 u to its charge (**measure**: inside one flame reach
plus an airblast step of the Medic); low chase (it gives up past 600 u from the charge); high reflect and extinguish
rate; Spy-check puffs at passing teammates.

**A3 Combo, "puff and sting".** Degreaser, Flare Gun (or Scorch Shot), Axtinguisher. Light the target, airblast it,
switch, flare it for a crit (about 90) or axe it (up to about 250 in all)
([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy)). Decisions: a short burn
(about 0.5 s, **measure** the burn-to-afterburn timing), then airblast, then switch inside 0.3 s (the Degreaser's fast
deploy), then the finisher. At mid range (300-1200 u) it harasses with flares at burning targets. It must be able to
switch weapons fast: see §3, the hands' 1 s limit.

**A4 Backburner ambusher.** Backburner, Shotgun or Flare, Axtinguisher or Powerjack. It must come from behind. It holds
an ambush spot off the enemy's route and engages once the target faces away (dot > 0.8, the crit rule at
`TFFlameManager.cs:68`, `:397`). It airblasts only when it has to, since each blast costs 50 of its 200.

**A5 Phlogistinator.** Phlogistinator, Shotgun or Reserve Shooter, Powerjack. No airblast. It builds 300 burn damage,
then taunts in safety (invulnerable for 2.6 s in stock, `tf_player.cpp:17913-17918`; the port's
`src/tf2/server/Player/TFBuffDeployment.cs:194-195`) and goes on a crit push. Decisions: it avoids spam it cannot
reflect, hunts groups once boosted, and taunts at full meter only behind cover or with no enemy within 600 u
(**measure**).

**A6 Dragon's Fury (long tail).** It fights at range, out to 526 u against about 340 for the other primaries
([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy)). It aims single fireballs
(a projectile with lead) and does triple damage to a burning target, so it pairs with a teammate who lights them.

Long tail, each needing its own subtree code: the Thermal Thruster (a route jump like the Soldier's, `JumpBook`),
the Gas Passer (a meter that starts empty, thrown on a group on a point), the Manmelter (crits from extinguishing
teammates), the Detonator (flare jumps, `partial`), and the Reserve Shooter's airblast-then-shoot combo. Reskins
(the flamethrower family's 25 + Rainblower, the axe's Lollichop/192, the Postal Pummeler, the Festive rows) fold into
their base. The archetype names the behaviour, and the reskin is a looks draw (C.3.3).

## 3. What the port and the AI can do today

### 3.1 Game side (src/tf2)

| behaviour | state | cite |
|---|---|---|
| flame stream, age falloff, burn | works | `TFShellBehaviours.cs:494-546`; `TFFlameManager.cs:42-68`; `TFBurn.cs:76-190` |
| Backburner back crit | works | `TFFlameEffects.cs:123`; `TFFlameManager.cs:397` |
| airblast reflects rocket, pipe, sticky, Cow Mangler ball | works | `TFFlameEffects.cs:201-235` |
| airblast reflects **arrow, flare, jar, cleaver, healing bolt, sentry rocket, Dragon's Fury ball** | **missing**: `AirBlast` has no arm for these classes (`TFArrow`, `TFFlare`, `TFJar`, `TFCleaver`, `TFHealingBolt`, `TFBallOfFire`; sentry rocket is its own `TFSentryRocket`). Stock reflects arrows ([Flame Thrower](https://wiki.teamfortress.com/wiki/Flame_Thrower)) | `TFFlameEffects.cs:201-237`; classes `TFProjectileEntities.cs:1567`, `:2407`, `:2587`, `:3095`, `:3405`; `src/tf2/server/Buildings/TFSentryRocket.cs:34` |
| airblast pushes enemy players (cray and old arms), puts out burning teammates (+heal) | works | `TFFlameEffects.cs:350-397`, `:405` |
| airblast on a Quick-Fix Über (Megaheal, immune to knockback) | not checked in `DeflectPlayer`: **audit** | `TFFlameEffects.cs:350-397` |
| flare: plain crits a burning target; Scorch and Detonator modes; Detonator self blast | works (Detonator row `partial`: the self multiplier is unmeasured, items:127 evidence) | `TFProjectileEntities.cs:2746`, `:2697` (`Detonate`) |
| Axtinguisher mini-crit and consume; Reserve Shooter airborne mini-crit; Neon wet crit; Scorch/Detonator burn mini-crit | works | `src/tf2/server/Combat/TFDamageModifyRules.cs:194`, `:242`, `:287`, `:294` |
| Phlogistinator: rage on burn, Attack2 taunt, crit buff, 2.6 s invulnerability | works | `TFRageMeter.cs:40-104`; `TFBuffDeployment.cs:194-195`; items:171 evidence |
| sapper removal by Homewrecker/Maul/Neon | works | `src/tf2/server/Buildings/TFSapper.cs:224-240` |
| Thermal Thruster launch (632 u at pitch −20, items:329 evidence); Gas Passer; Dragon's Fury; Manmelter extinguish | works | `TFRocketPackBehaviour.cs:39`; `TFFlameEffects.cs:437` |
| lights a teammate's arrows (F20) | **landed**: the plan's "F20 missing" row (`plan_bots_teams_tf2.md:672`) is stale | `TFFlameManager.cs:487-500` |
| a burning cloaked Spy blinks (0.85 × invisibility) | works (the renderer side) | `src/tf2/shared/Player/TFInvisibility.cs:26`, `:50` |

### 3.2 AI side (src/ai/tf2/server)

| need | state | cite |
|---|---|---|
| own play | none. The Pyro runs the Soldier's subtree | `Trees/TFClassTrees.cs:15`; `TFBotClassPick.cs:22` (every bot a Soldier) |
| range | wrong for a Pyro: it holds 320-900 u and backs off under 320 | `Trees/TFDecider.cs:46`, `:485-493` |
| fire gate | 2000 u for anything but the Soldier's shotgun; Spray holds the trigger | `TFDecider.Soldier.cs:38`, `:112-115` |
| weapon choice | only rocket launcher and shotgun kinds; other classes never switch | `TFDecider.Soldier.cs:125-146` |
| switch speed | at most one change or reload a second (`HandsEverySeconds`) | `TFDecider.Soldier.cs:41` |
| projectile tracking | yes: rockets, pipes, stickies, arrows, flares, jars, cleavers, syringes, bolts; closest-approach maths (`BlastDodge.Threatens`) used only to dodge | `Senses/TFBotGameView.cs:134-150`; `Trees/TFDecider.Things.cs:148-164` |
| projectile team/owner | the rocket's owner is read; flares, pipes and others read owner 0; arrows and syringes team 0 | `TFBotGameView.cs:156-166` |
| airblast / reflect row | none. `AltFire` exists in the core but nothing fires it for TF2 | `Knowledge/TFAbilities.cs:50-68`; `src/ai/core/Brain/IntentCommand.cs:52-67` |
| Übered enemy | the bot backs off within 900 u: wrong for a Pyro, which should push | `Trees/TFDecider.Things.cs:60`, `:184` |
| sentries | every class avoids them (`AvoidsSentries => true`): right for the Pyro | `TFDecider.Things.cs:102` |
| teammates' conditions (burning, for extinguish) | yes, `ActorFacts.Conditions` | `src/ai/core/Snapshot/PerceivedView.cs:58-68`, `:90-100`; `TFBotGameView.cs:129-132` |
| **burning or blinking cloaked Spy** | **hidden**: `Draw` drops any actor over 75 % invisible, burning or not. Stock always sees burning, jarated, blinking or bleeding Spies (`tf_bot_vision.cpp:264-271`), as a client renders the flames | `TFBotGameView.cs:102-103` |
| Spy suspicion counter (C.3) | none. Disguised "teammates" are never targets | `Trees/TFDecider.cs:298-330` |
| a sapper on a building | not in `ThingFacts` (no sapped flag) | `PerceivedView.cs:118-127`; `TFBotGameView.cs:180-191` |
| own kit (which flamethrower, flare or axe) | none: `WeaponFacts` are per script name, so Backburner, Degreaser and stock all read `tf_weapon_flamethrower`; `SelfState` lists no carried weapons | `src/ai/core/Knowledge/MapKnowledge.cs:44-50`; `Knowledge/TFKnowledge.cs:118-140`; `PerceivedView.cs:32-50` |
| meters (Mmmph rage, item meter for Thruster/Gas/Fury) | not in `SelfState` | `PerceivedView.cs:32-50` |
| ammo errand (200 lasts about 16 s, [Flame Thrower](https://wiki.teamfortress.com/wiki/Flame_Thrower)) | none: `HealPlace` looks only for health and the cabinet; `ItemKind.Ammo` exists in the core | `Trees/TFDecider.cs:437-471`; `MapKnowledge.cs:93` |
| ambush spots, chokes | the core has `ChokeMap` and `ambushWeight`; the TF2 decider uses neither | `src/ai/core/Nav/ChokeMap.cs`; `src/ai/core/Skills/SkillCurves.cs:22`, `:340` |
| decide rate | about 10 Hz (period 6), intents last 16 ticks. A rocket at 1100 u/s (`TFRadiusDamage.cs:32`) crosses the 256 u reflect zone in about 0.23 s, about 2.5 decides | `src/ai/core/Brain/DecideSchedule.cs:12`; `IntentCommand.cs:202` |

**The loadout draw today** (`src/tf2/server/Econ/BotInventory.cs:94-108`, rows keyed by the table's own slot column,
`:152-166`):

- The multiclass shotguns' table slot is `primary` (their prefab's `item_slot`), but their `used_by_classes` gives the
  Pyro `secondary` (`bin/debug/content/tf/scripts/items/items_game.txt`, prefab `weapon_shotgun_multiclass`). So 12 of
  the Pyro's 43 "primary" rows are shotguns. `PlayerInventory.Equip` refuses them (`src/tf2/server/Econ/PlayerInventory.cs:120`),
  `BotInventory` ignores the false (`:107`), and the slot falls back to the base flamethrower. The Panic Attack and
  the 11 shotgun reskins can therefore never reach a Pyro's secondary.
- Per Pyro bot: stock-behaving primary 38/43 (88 %), Backburner 2/43, Degreaser, Phlogistinator and Dragon's Fury
  1/43 each. Secondary 1/8 each, so 2/8 = 25 % of Pyro bots carry a Thermal Thruster or a Gas Passer they never use.
  A coherent Degreaser + Flare + Axtinguisher set comes up (1/43)·(2/8)·(3/15) ≈ 0.1 % of the time.

### 3.3 Gaps, by side

**Game:** airblast reflects of arrows, flares, jars, cleavers, bolts, sentry rockets and fireballs; the Quick-Fix
knockback check on the airblast push; the Detonator's `partial` flip to `works`.

**AI:**
- a Pyro play: range, fire gate, circle-strafe, no retreat, a route through corners;
- airblast as a reflect, a push and an extinguish;
- weapon choice with switching faster than 1 s;
- draw burning or blinking cloaked actors;
- a Spy-suspicion counter fed by "my flame lit a teammate" and "my airblast pushed a teammate";
- a sapped flag on buildings;
- the own kit and meters;
- an ammo errand;
- projectile owners;
- a coherent archetype draw, and fixing the slot bug.

## 4. The design for the code lane

### 4.1 Ship order

1. **A1 Ambusher (stock)**, which every Pyro gets first. It covers 88 % of today's primaries.
2. **A2 Airblast guard**: stock items plus the Homewrecker. It adds the support layer: extinguish, Spy-check, sappers,
   pocket.
3. **A3 Combo (Degreaser / Flare / Axtinguisher)**: the most-played unlock set
   ([Community Pyro strategy](https://wiki.teamfortress.com/wiki/Community_Pyro_strategy)).
4. **A4 Backburner**, then **A5 Phlogistinator**.

The long tail (Fury, Thruster, Gas, Manmelter, Detonator, Reserve Shooter juggle) comes after these.

### 4.2 Drawing an archetype, and the loadout that follows

- The bot's seed draws an archetype at spawn, weighted. Starting weights (**measure** the share in pubs; no source
  gives one): A1 0.40, A2 0.25, A3 0.20, A4 0.10, A5 0.05. On defence A2 ×1.5; on attack A1/A3 ×1.2 (C.4 roles). The
  draw holds until the class changes or the map does (C.3.1). A pin overrides it.
- An archetype is a **set**, not three draws. Each slot holds a preference list whose first allowed `works` item
  wins. Last comes the slot's stock item, because `BotInventory` answers the stock item for an empty set. A slot the
  archetype leaves open draws only from the **families its subtree reads**. A Thermal Thruster or Gas Passer is never
  drawn until a subtree reads it (C.3.1's "family" rule, `plan_bots_teams_tf2.md:783-789`).
  - A1: primary {flamethrower family}, secondary {12}, melee {2, 192, 739}.
  - A2: {flamethrower family}, {12}, {153, 466, 813, 834}.
  - A3: {215}, {39, 1081, 740}, {38, 457, 1000}.
  - A4: {40, 1146}, {12, 39, 1081}, {38, 457, 1000, 214}.
  - A5: {594}, {12, 415}, {214, 2}.
  - "Flamethrower family" means the 26 stock-behaving rows. Which reskin comes up is a looks draw.
- Fix the slot key: key the works rows by `ItemDefinition.Slot(cls)` (`src/tf2/shared/Econ/ItemDefinition.cs:300-306`),
  not the table's slot column, and treat a false from `Equip` as a fault that the fact catches.

### 4.3 Decision rules per archetype

All numbers are starting values. The source is in brackets: [wiki] the URL above it, [stock] a `tf_bot` cite, [port]
a repo cite, [measure] a value to measure live.

**Shared Pyro play** (`PyroPlay : ClassPlay`):

- `Engage`:
  - Fire the flamethrower only within `FlameReach` = 280 u [port, measured once live], with the target seen in the
    last 1 s [stock `tf_bot_behavior.cpp:1365`].
  - Hold the fire for at least 1 s [stock `tf_bot_fire_weapon_min_time`, `tf_bot_behavior.cpp:39`].
  - Lead the target by the flame's flight: about 0.12 s to 250 u [port, from the drag formula].
  - Never fire at an enemy Pyro, which is immune to afterburn; take the shotgun [wiki].
- `Hands`:
  - Primary out whenever the threat is a Soldier or a Demoman, or a projectile is tracked [stock `tf_bot.cpp:3410-3417`].
  - Secondary (the shotgun or the flare) past 750 u [stock `tf_bot.cpp:3404-3408`], or while the reserve is under
    the airblast cost.
  - Weapon switches here use 0.25 s spacing, not the general 1 s (`HandsEverySeconds`) [measure against human
    switch rates; Degreaser deploy is 60 % faster, wiki].
- `FightInSight`:
  - approach to 100-150 u [stock desired range 100, `tf_bot.cpp:3196`];
  - inside 1.1 × that, circle-strafe, flipping left or right every 3 s [stock `tf_bot_attack.cpp:47-60`];
  - no retreat while in a fight [stock `tf_bot_seek_and_destroy.cpp:161-166`], except Survive under 40 % health
    (`HurtShare`, `TFDecider.cs:37`).
- **Reflect** (`OneShots`), a new ability row:
  - For each tracked enemy rocket, pipe, sticky or (once the game reflects them) flare or arrow, take its closest
    approach from the existing `BlastDodge.Threatens` (`TFDecider.Things.cs:150-152`).
  - Fire the airblast when its predicted entry into the 0-256 u zone in front [port `TFFlameEffects.cs:185-188`]
    falls within the next decide (≤ 0.09 s).
  - The rocket must be heading at the bot: horizontal dot < −0.5 [stock `tf_bot_pyro_deflect_tolerance`,
    `tf_bot.cpp:45`, `:4180-4189`].
  - The look turns to the projectile first.
  - It succeeds by skill, the stock ladder as a curve: `reflectChance` from Easy 0 / Normal 0.5 / Hard 0.9 / Expert
    1.0 [stock `tf_bot.cpp:4083-4106`], drawn once per projectile like `dodgeChance`, plus `reflectLateMs` like
    `dodgeLateMs`.
  - A reflect replaces the dodge for that projectile.
- **Shove** (`OneShots`), within 250 u [stock `tf_bot_pyro_shove_away_range`, `tf_bot.cpp:49`]:
  - always an Übered enemy (`ActorConditions.Invulnerable`), and the `InvulnerableBackOff` reflex does not apply to a
    Pyro;
  - an enemy on a point being captured [stock `:4133-4137`];
  - an airborne enemy half the time;
  - otherwise "pushy" half the time per 3 s window [stock `:4126-4144`];
  - never when the reserve is under the cost.
- **Extinguish**: a burning teammate within 200 u in sight; airblast aimed at him [port `TFFlameEffects.cs:353-360`].
  It outranks the fight unless the target is inside 150 u.
- **Ammo**: under 60 reserve with no target, go to the nearest ammo pack or dispenser (a new errand beside
  `HealPlace`).
- **Sentries**: keep `AvoidsSentries` [stock `tf_bot_destroy_enemy_sentry.cpp:160-170`]. An A2 bot may stand by its
  own Engineer's sentry.
- **Spy check**, from C.3.2's row (`plan_bots_teams_tf2.md:672`):
  - a 0.2 s puff at a teammate passing within 128 u, at `spyCheckRate` by Alertness;
  - a "teammate" newly `Burning` within 0.3 s of the puff, or moved by the bot's own airblast, is certain: target him
    and say "Spy!" (`TFAbilities.VoiceSpy`).
  - The plan's figure of 9 of 10 seeds within 3 s at Alertness 6 stands as the fact's target.

**Per archetype** (on top of the shared play):

| | A1 Ambusher | A2 Guard | A3 Combo | A4 Backburner | A5 Phlog |
|---|---|---|---|---|---|
| place (`PlaceUtility`) | a corner cell off the enemy's route within 600 u of the objective (`ChokeMap` + sight table: sees the route, is seen by few) | within `GuardLeash` 400 u of its charge (Medic, else own sentry, else Heavy) [measure] | the A1 corner, plus a mid-range cover spot from which to flare | the A1 corner, entered only facing the route's back | none: it goes with the team's push |
| engage when | target within 280 u, or a corner contact | threat within 400 u of the charge | flares at burning targets 300-1200 u; the full combo within 280 | target's facing dot with the bot's view > 0.8 (from behind) [port `TFFlameManager.cs:68`] | meter not full: as A1; full: taunt (row below) once no enemy is within 600 u; boosted: push the nearest group |
| chase | 15 s [stock `tf_bot_defend_point.cpp:186`] | no further than 600 u from the charge | 8 s | 6 s; it breaks off to a new ambush | 15 s while boosted |
| airblast | reflect + shove | reflect (top weight), extinguish, shove Übers | as part of the combo: burn 0.5 s → blast → switch → flare/axe within 0.6 s [measure] | reflect only rockets aimed at it; cost 50 [wiki] | **disabled**: dodge as the Soldier does |
| melee | when out of ammo | Homewrecker on any sapped own building in sight (needs the sapped flag) | Axtinguisher on a burning target within 80 u | Axtinguisher on a burning target, else flames | Powerjack to move between fights |
| routes | `trafficWeight` up (C.3.2's Spy cost term), a corner prior | the charge's route | as A1 | strongest away-from-traffic bias | the team's |

### 4.4 Ability rows (the Pyro's block, rows 112..)

1. `pyro_airblast`: `AbilityRun.Buttons`, `IN_ATTACK2`, hold 2 ticks (the shape of the `Uber` row, `TFAbilities.cs:53`).
   It needs its own name so the counters and the doing record can tell the two apart.
2. `pyro_reflect`: `AbilityRun.Program`. It holds up to 12 ticks (0.18 s) and presses `IN_ATTACK2` on the tick that
   the projectile's extrapolated position (last seen origin and velocity, carried in the one-shot as the rocket jump
   carries its plan, `AbilityCall.Pack`) enters the zone. That closes the 10 Hz timing gap without new perception.
3. `pyro_spy_check`: `Buttons`, `IN_ATTACK`, 13 ticks (0.2 s at 66 Hz).
4. `pyro_phlog_taunt`: `Buttons`, `IN_ATTACK2`, 2 ticks, with a full meter (the Phlogistinator's secondary is the
   rage press, items:171 evidence).
5. Long tail: `pyro_detonate` (`IN_ATTACK2` with an own flare in flight), `pyro_thruster` (a jump program), and
   `pyro_gas` (throw).

New skill curves (`game/mod_tf2/scripts/ai/tf2_skills.json`, a Pyro section, C.5): `reflectChance`,
`reflectLateMs`, `spyCheckRate`, `shoveChance`, `guardLeash`. The core's `ambushWeight` now feeds A1/A4 place scoring.

### 4.5 Facts to write (each red with its mutant)

1. A Pyro with a target at 600 u approaches and does not fire; at 200 u it fires (mutant: today's 2000 u gate).
2. It never backs off under 320 u (mutant: `FightAtRange` kept).
3. With a Soldier in sight it holds the primary; with a target at 900 u and no projectile it holds the secondary.
4. A rocket on a collision course gets `pyro_reflect` at Expert in 10 of 10 seeds and at Easy in 0. A rocket passing
   wide (dot > −0.5) gets none.
5. An Übered enemy at 200 u is shoved, not backed away from (mutant: `InvulnerableBackOff`).
6. A burning teammate at 150 u is airblasted.
7. A Phlogistinator bot never sends `pyro_airblast` or `pyro_reflect`.
8. A Backburner bot holds off a target facing it, and engages the same target facing away.
9. Draw: over 500 seeds every A3 bot holds 215 / {39, 1081, 740} / {38, 457, 1000}. No Pyro ever draws 1179 or 1180
   until a subtree reads them. No `partial` row is drawn. Every drawn item is equipped (`Equip` true).
10. Slot fix: a Pyro's secondary set contains the Panic Attack (1153); no shotgun is keyed to a Pyro primary.
11. Senses: a cloaked Spy at 1.0 invisibility who is `Burning` is drawn; one who is not burning is not. The negative
    arm stays.
12. Spy check: a disguised Spy lit by the bot's puff becomes a target within 0.5 s; a real teammate never.
13. Game (if the lane takes it): the airblast reflects an arrow and a flare (`TFAirBlastFacts` style,
    `src/tf2/tests/Projectiles/TFAirBlastFacts.cs`).

### 4.6 Live measures

By the user's ruling, objective time and progress come first:

- **ctf_2fort, 4v4 bots, 10 min, three seeds.** Captures per team and the median time from the enemy's intel taken
  to captured, with Pyro defenders against Soldier defenders. Gate: a team with 2 Pyro defenders concedes captures at
  ≤ the Soldier rate, with the bias measured first.
- **A CP map (`ss_ai_tf_cp`, or the shipped cp map the P6 gates use).** Time to capture each point against Pyro or
  Soldier defenders. On attack: point progress per minute with an A2 guard on the Medic against none.

Then the class numbers per archetype, as counters on the decider like `Dodges`/`RocketJumps`:

- reflects sent and reflects that hit (the port logs each blast, `TFFlameEffects.cs:240`, and each push, `:395`, but
  not each deflect: a reflect needs a counter or a log line added at `:206-216`);
- shoves of Übered enemies; extinguishes (`airblast extinguished`, `:359`);
- Spies revealed;
- median engagement distance: A1 < 200 u, A3 flares 300-1200 u;
- Backburner kills from behind as a share (the crit log);
- Phlogistinator taunts and crit kills;
- the median distance from the guarded Medic for A2 against A1 (A2 ≤ 400 u, A1 several times that).

The archetypes count as playing differently when at least three of these differ beyond seed noise.

### 4.7 What the Pyro lane needs from the foundation

- **The kit read** (the foundation's step 2, `OwnKit`): per slot, the catalog kind and the defindex, plus the family
  traits the subtree reads (back crit, no airblast, airblast cost, fast switch, sapper hits, burning mini-crit).
  Also the meters a HUD shows: rage, item meter, own flares in flight.
- **The archetype draw** (step 3, `IBotItemChoice` in `BotInventory`): preference lists per slot, the family gate,
  the archetype string on `ClassPlay.Archetype`. With it, the slot-key fix (§4.2).
- **Hooks**: the `ClassPlay` set as drafted, plus:
  - a reflex override (the Pyro replaces the `InvulnerableBackOff` and the dodge with a shove or a reflect);
  - a per-class `HandsEverySeconds`;
  - a way for `OneShots` to win over the general fire within a decide (one action per decide).
- **The class pick** (step 4): `TFPyroClass.Ready` flips true only when facts 1-7 pass. Composition weights from
  stock: a Pyro only once the team has 3, no limit (`tf_bot.cpp:838`, `:852`).
- **Senses** (shared files the foundation owns):
  - draw burning, jarated or blinking actors whatever the cloak (`TFBotGameView.cs:102`);
  - a building's sapped flag in `ThingFacts`;
  - flare and pipe owners;
  - the Spy-suspicion counter on the blackboard (C.3), shared with the Spy-check walk.
- **An ammo errand** in the general tactical layer, which every class wants.

## 5. Sources

Web:
- https://wiki.teamfortress.com/wiki/Community_Pyro_strategy
- https://wiki.teamfortress.com/wiki/Pyro_strategy
- https://wiki.teamfortress.com/wiki/Pyro_(competitive)
- https://wiki.teamfortress.com/wiki/Pyro
- https://wiki.teamfortress.com/wiki/Flame_Thrower
- https://wiki.teamfortress.com/wiki/Axtinguisher
- https://wiki.teamfortress.com/wiki/Backburner
- https://wiki.teamfortress.com/wiki/Phlogistinator
- https://wiki.teamfortress.com/wiki/Dealing_with_Pyro

Two gaps in what the sources covered:
- No written Highlander-specific Pyro guide was found.
- No source gives a numeric pub share of loadouts, so the §4.2 weights are a measurement to make.

Stock tf_bot (`../source-sdk-2013-valve/src/game/server/tf/bot/`):
- `tf_bot.cpp`: 45, 49-50, 825-870 (roster limits), 1026-1075 (rosters), 3085-3100 (class threat weights), 3130-3200
  (attack ranges), 3402-3418 (weapon by threat), 4073-4200 (`ShouldFireCompressionBlast`);
- `tf_bot_vision.cpp:240-305`;
- `behavior/tf_bot_behavior.cpp:1340-1370`;
- `behavior/tf_bot_attack.cpp:30-60`;
- `behavior/tf_bot_seek_and_destroy.cpp:155-170`;
- `behavior/scenario/capture_point/tf_bot_defend_point.cpp:175-200`, `:395-405`;
- `behavior/tf_bot_destroy_enemy_sentry.cpp:160-170`;
- `../src/game/server/tf/tf_player.cpp:17895-17925` (the Phlogistinator's taunt).

Stock has no `behavior/pyro/` directory: everything the stock Pyro does is in the lines above.

Where the brief or the plan is wrong:
- `plan_bots_teams_tf2.md:672` says arrow lighting is missing (F20); it has landed (`TFFlameManager.cs:487-500`).
- C.3.1's table (`:725-727`) marks the Backburner and Degreaser "not found"; both are `works` with their attributes
  read (items:36, items:94).
- The brief's "works rows per class: pyro 66" is right, but 12 of them are shotguns keyed to the wrong slot. The
  Pyro's effective works set is 31 primary, 8 secondary, 15 melee.
