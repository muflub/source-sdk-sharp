# P7 research: the Soldier, its meta and its play styles by weapon set

Base `225fe2e96` (checked: `git log --oneline -1`). Repo cites are `path:line` at that base (`git show 225fe2e96:path`;
the foundation lane is editing `TFDecider.cs`, `TFDecider.Soldier.cs` and `TFAbilities.cs` in this tree) unless marked
**(p7f, uncommitted)**: the foundation lane's work in this tree as read on 2026-11-20 (`src/ai/tf2/server/Classes/`,
`Trees/TFDecider.Classes.cs`). Valve cites are `../source-sdk-2013-valve/src/game/...`. Web cites are in §5 by tag
[W1]..[W9]. "Measure" marks a number this file does not have and the code lane must take from the simulator or a run.

## 1. The class's job, by mode

**Body.** 200 health (300 overheal), 80 % speed (240 u/s); launcher 90 base, clip 4, reserve 20; shotgun 60 (10x6),
clip 6; shovel 65 [W1]. Rocket 1100 u/s, blast radius 146, self radius for jumps 121
(`src/tf2/server/Projectiles/TFRadiusDamage.cs:22,29,32`). A launcher jump from the floor costs 44 (200 -> 156) and 17
with Gunboats (200 -> 183), measured live (`items_working.tsv` row 133's evidence).

**Everywhere.** The Soldier is the front-line damage class: a team without one is "offensively weak" [W2]. Good
players:
- **Stand on height** and fight from above; rockets from above are hard to dodge [W2]. Range: the launcher's best is
  mid range, roughly 300-1250 u (stock TFBot's desired attack range 1250, max 3000: `server/tf/bot/tf_bot.cpp:3164-3166,
  3207-3210`); nearer than the blast, swap to shotgun or melee [W2].
- **Aim at the feet** of grounded targets, lead slow rockets, splash the floor or a wall when a direct line is
  doubtful; juggle (a feet hit lifts the target) and follow up [W2]. Stock TFBot: feet with lead for a grounded
  target, the ground under an airborne one within 200 u, the centre or eyes when the target is 30 u above, eyes inside
  150 u (`server/tf/bot/behavior/tf_bot_behavior.cpp:629-676`).
- **Target priority:** Medic, then fragile high-value classes (Sniper, Scout, Engineer and his buildings, dispenser
  first when the Engineer repairs the sentry) [W2]; sentries from outside their 1100 u range
  (`server/tf/tf_obj_sentrygun.h:35`; stock TFBot picks an attack spot between 1.1 and 1.5 x that range with a clear
  line: `tf_bot_destroy_enemy_sentry.cpp:244,304,325`).
- **Ammo discipline:** reload in every lull and in the air, keep a rocket in hand, switch to the shotgun rather than
  reload mid-fight (stock TFBot: an empty launcher with a loaded secondary and a threat inside 500 u swaps,
  `tf_bot.cpp:3377-3390`) [W2].
- **Movement tech:** rocket jump (crouch-jump then fire at the feet; wall-floor corner for distance), air-strafe while
  up, reload while airborne; jump to height, to flank, to escape, never at low health without a pack ahead [W2].
- **Teamwork:** leads pushes with the Medic and Demoman; is a common Uber target because he swaps between defence and
  offence fast; his self-damage builds the Medic's Uber [W3]. A Buff Banner/Backup is popped with the team, inside
  450 u of it (`src/tf2/shared/Player/TFRageMeter.cs:59`).
- **What beats him and how he plays round it:** Pyro airblast (reflects rockets: deflection radius 128 u,
  `src/tf2/server/Projectiles/TFFlameEffects.cs:631`) -> fire at the Pyro's feet from outside reach, vary the rate,
  shotgun up close, Bison/shotgun are unreflectable [W2]; Scouts at close range -> shotgun; Snipers shoot him out of
  the air -> don't jump across a Sniper's line; sentries -> out-range them; hitscan punishes a predictable jump arc
  [W4]; reload windows and slow ground speed are his weakness [W4].

**By mode** (pub play first, then competitive 6s [W3][W5]; Highlander had no source this session: marked "HL, unverified").

| Mode | Attack / push | Hold / defend |
|---|---|---|
| CTF (2fort) | jump to the battlements and the enemy's balcony, shell the courtyard, escort the carrier; Rocket Jumper/Pain Train cannot carry (attribute, below) | spam the bridge and the courtyard doors from the battlements; chase the carrier |
| 5CP (symmetric) | rollout: jump to mid (6s pockets use Escape Plan at self-damaged low health to run there with the Medic; roamers jump to mid ahead of him) [W3]; mid fight: pocket spams and guards the Medic, roamer takes height early (Gunboats) or jumps the enemy Medic [W3]; after a win: roamer flanks and clears, Scout caps [W3] | on own last: height over the point, block caps by standing on it, spam chokes; roamer ambushes the Medic [W3] |
| A/D CP | lead the push behind Uber, kill sentries from range, the attacker jumps to pick key targets and a sentry [W3] | deny chokepoints with spam [W2], jump to perches and onto the point to block [W3] |
| KOTH | in 6s both Soldiers play as aggressive roamers, often suicidal to push [W3] | same: hold height round the point |
| Payload | push the cart (it is the objective and a dispenser), clear sentries ahead from range, Battalion's against sentry/Kritz pushes [W2] | spam the cart, flank its pushers, Backup when the attackers pop |

Pubs: one Soldier rarely has a pocket; he roams more, jumps more, and the banners are common. 6s: two Soldiers,
**pocket** (stock or Original + Shotgun + Escape Plan, stays on the Medic, "the combo", 300 overheal) and **roamer**
(Gunboats, rocket-jumps ahead, picks, expendable) [W3][W5]. Black Box suits the roamer; Direct Hit is "allowed but
avoided" because it cannot damage groups [W6]. HL, unverified: one Soldier, usually a roamer-pocket hybrid beside the
Heavy/Medic.

**Beginner mistakes that read as a bot** (each is a rule in §4):
1. Holding fire with a target inside the blast: today's Engage simply stops firing inside 160 u
   (`Trees/TFDecider.Soldier.cs:113`, `TFDecider.cs:46`) and never swaps to shotgun or shovel then.
2. Emptying the clip into a Pyro at airblast range; reloading mid-fight instead of swapping.
3. Jumping in straight, repeated arcs; jumping at low health with no pack where it lands; jumping across a Sniper.
4. A full banner never popped, or popped alone with nobody in 450 u.
5. Standing on flat ground below an enemy who has height; never using height at all.
6. Walking into a sentry, or walking away from one forever instead of shelling it from 1100+ u.
7. Chasing a Scout across a map with rockets.
8. A Direct Hit fired at feet (its 44 u blast misses), a Beggar's never loaded, a Market Gardener swung on the ground.

## 2. Play styles by weapon set

Items by `items_working.tsv` (`game/mod_tf2/scripts/items/items_working.tsv`; defindex, item_class, attribute classes).
Every row below is `works` (the table's status), except where noted. **Reskins** (same `item_class`, no attributes)
fold into their base: launcher 18 = {18 205 658 800 809 889 898 907 916 965 974 15006 15014 15028 15043 15052 15057
15081 15104 15105 15129 15130 15150} (23 rows); shotgun 10 = {10 199 1141 15003 15016 15044 15047 15085 15109 15132
15133 15152} (12); shovel 6 = {6 196}; Black Box 228 = {228 1085}; Buff Banner 129 = {129 1001}.

| defindex | item | item_class | attribute classes (the table's) | wiki numbers [W1] |
|---|---|---|---|---|
| 18 | Rocket Launcher | tf_weapon_rocketlauncher | - | 90, clip 4 |
| 513 | The Original | tf_weapon_rocketlauncher | centerfire_projectile | fires from crosshair centre |
| 127 | The Direct Hit | tf_weapon_rocketlauncher_directhit | mult_explosion_radius; mult_projectile_speed; mult_dmg; mini_crit_airborne | +80 % speed (1980), +25 % dmg (112), -70 % radius (44), mini-crits blast-airborne targets |
| 228 | The Black Box | tf_weapon_rocketlauncher | add_health_on_radius_damage; mult_clipsize | +20 hp per hit (60 per clip), clip 3 |
| 237 | Rocket Jumper | tf_weapon_rocketlauncher | mult_dmg; mult_crit_chance; no_self_blast_dmg; mult_maxammo_primary; cannot_pick_up_intelligence | 0 dmg, no self dmg, 60 reserve; **never drawn for a bot** (`src/tf2/server/Econ/BotInventory.cs:148`) |
| 414 | The Liberty Launcher | tf_weapon_rocketlauncher | mult_projectile_speed; mult_dmg; mult_clipsize; rocket_jump_dmg_reduction | +40 % speed, -25 % dmg (68), clip 5, -25 % jump cost |
| 441 | The Cow Mangler 5000 | tf_weapon_particle_cannon | energy_weapon_no_ammo; energy_weapon_charged_shot; energy_weapon_no_hurt_building; crits_become_minicrits; mult_crit_chance | no ammo, alt-fire charged shot: mini-crit, burn 6 s, disables a building 4 s; 20 % vs buildings |
| 730 | The Beggar's Bazooka | tf_weapon_rocketlauncher | mult_postfiredelay; auto_fires_full_clip; can_overload; mult_reload_time_hidden; mult_clipsize; projectile_spread_angle; no_primary_ammo_from_dispensers; mult_explosion_radius | hold to load up to 3, release fires a burst, past 3 misfires, 3 deg spread, -20 % radius |
| 1104 | The Air Strike | tf_weapon_rocketlauncher_airstrike | mult_explosion_radius; mult_dmg; rocket_jump_dmg_reduction; rocketjump_attackrate_bonus; mini_rockets; clipsize_increase_on_kill | -15 % dmg (76), faster fire while blast-jumping, clip 4 -> 8 with kills, -15 % jump cost; row evidence says "partial for" the reload and clip ceiling |
| 10 | Shotgun | tf_weapon_shotgun | - | 60, clip 6 |
| 415 | The Reserve Shooter | tf_weapon_shotgun | mult_clipsize; mini_crit_airborne; mult_single_wep_deploy_time | clip 4, 20 % faster switch, mini-crits airborne for 5 s after deploy |
| 1153 | Panic Attack | tf_weapon_shotgun | mult_dmg; mult_single_wep_deploy_time; mult_spread_scales_consecutive; mult_bullets_per_shot; fixed_shot_pattern | 15 pellets, 50 % faster deploy, 108 point blank; **never drawn for a Soldier today** (§3) |
| 442 | The Righteous Bison | tf_weapon_raygun | energy_weapon_no_ammo; energy_weapon_penetration; energy_weapon_no_hurt_building; energy_weapon_no_deflect | piercing, unreflectable, no ammo, 1200 u/s [W2] |
| 129 | The Buff Banner | tf_weapon_buff_item | set_buff_type | 600 dmg to fill, 10 s mini-crits for the team in 450 u |
| 226 | The Battalion's Backup | tf_weapon_buff_item | set_buff_type; add_maxhealth | +20 max hp, 10 s: crit immunity, -35 % dmg, -50 % sentry dmg |
| 354 | The Concheror | tf_weapon_buff_item | set_buff_type; add_health_regen | regen to +4/s, 480 dmg to fill, 10 s speed + 35 % heal-on-hit |
| 133 | The Gunboats | tf_wearable | rocket_jump_dmg_reduction | -60 % jump cost |
| 444 | The Mantreads | tf_wearable | damage_force_reduction; airblast_vulnerability_multiplier; boots_falling_stomp; mod_air_control_blast_jump | -75 % knockback/airblast, stomp 3x fall dmg, +200 % air control when blast-jumping |
| 1101 | The B.A.S.E. Jumper | tf_weapon_parachute | parachute_attribute; set_buff_type | parachute while airborne |
| 6 | Shovel | tf_weapon_shovel | - | 65 |
| 775 | The Escape Plan | tf_weapon_shovel | provide_on_active; set_weapon_mode; mult_healing_from_medics; self_mark_for_death | 240 -> 384 u/s as health drops (measured: row 775), marked for death while held + 3 s, -90 % Medic heal |
| 128 | The Equalizer | tf_weapon_shovel | provide_on_active; set_weapon_mode; mult_healing_from_medics | 33-107 by missing health, -90 % Medic heal |
| 416 | The Market Gardener | tf_weapon_shovel | mult_postfiredelay; crit_while_airborne; mult_crit_chance | crits while blast-jumping, 20 % slower swing |
| 447 | The Disciplinary Action | tf_weapon_shovel | mult_dmg; speed_buff_ally; melee_range_multiplier; melee_bounds_multiplier | hit an ally: you +speed 3 s, them 2 s; -25 % dmg; +70 % range |
| 154 | The Pain Train | tf_weapon_shovel | add_player_capturevalue; mult_dmgtaken_from_bullets | +1 cap rate, +10 % bullet dmg taken |
| 357 | The Half-Zatoichi | tf_weapon_katana | is_a_sword; decapitate_type; restore_health_on_kill; mult_crit_chance; honorbound | kill heals 50 %, holster costs 50 hp without a kill |

**The archetypes** (names are the community's [W3][W5][W7]; "Trolldier" from general knowledge, as the plan's C.3.1
already says). For each: the loadout (preference per slot, base item last), and what changes in the bot's decisions.

- **A. Stock all-rounder** (18, 10, 6): today's play: launcher at mid range, feet shots, shotgun when dry up close,
  rocket jumps on the route. The control for every A/B.
- **B. Pocket** (18|513, 10|1153, 775|6) [W3][W5]: stays within 300 u of its Medic, in front of it on the side the enemy
  is; jumps only when the Medic can follow (the jump lands within 450 u, the beam's script range, stock
  `shared/tf/tf_weapon_medigun.cpp:489-499`) or under Uber; holds the fight rather than chasing; peels for the Medic
  (an enemy within 500 u of the Medic outranks a nearer one). Needs a ready Medic class; until then plays as A.
- **C. Roamer** (18|228, 133, 775|6) [W3][W5]: jumps more (Gunboats cut the cost to ~17), flanks off the main route,
  takes height early at the contested point, hunts Medic and Sniper first, leaves at low health by Escape Plan;
  Black Box variant fights longer before Survive (heals 20 per hit).
- **D. Banner** (18|127|414, 129|226|354, 775|6): builds rage (600, Concheror 480) fighting with the team, pops it on
  a push or a teamfight with teammates in 450 u. Buff Banner = offence (pop on push), Backup = defence (pop when the
  enemy pushes, Ubers or a sentry fires on the team), Concheror = mobility (pop to push or to retreat with the team).
  No shotgun: when the launcher is dry it falls back to melee or retreats to reload.
- **E. Direct Hit** (127, 415|10, 775|6): long range and picks: body/head-height aim with long lead (1980 u/s), not
  feet; airshots on juggled or jumping targets (mini-crit); the sentry killer (stock: two DH rockets kill an
  unattended level 3 vs three for stock [W2]); prefers 600-1500 u. Reserve Shooter pairs for airborne mini-crits.
- **F. Beggar's ambusher** (730, 10, 6|447): loads 3 out of sight, waits at a corner or chokepoint, releases the burst
  at a target inside ~600 u; the burst at the floor ahead of a chaser [W2]; no dispenser refill so it reloads at packs.
- **G. Trolldier / airborne** (1104|414|18, 133|1101|444, 416) [W7][W8]: rocket-jumps at a target and hits it with the
  Market Gardener while still blast-jumping (crit 195); Air Strike fires faster while blast-jumping and grows its
  clip; B.A.S.E. Jumper extends the air time. In pubs the iconic Soldier; a pick tool, not a holder.
- **H. Cow Mangler** (441, 10|1153, 6): spam without ammo pickups, charged alt-fire on a sentry/dispenser (4 s
  disable) before the team pushes it, and on a group; no random crits. Banned in 6s [W6], common in pubs.
- Long tail (one rule each, §4.3): Bison (vs Pyro and through groups), Mantreads (stomp, airblast resistance), Equalizer
  (low-health melee), Disciplinary Action (speed a teammate to the front), Pain Train (capper), Half-Zatoichi (see §3,
  not drawn until its holster cost is ported).

## 3. What the port and the AI can do today

### 3.1 Game side (src/tf2)

| behaviour | state | cite |
|---|---|---|
| rocket, splash, self radius, jump push | works | `server/Projectiles/TFRadiusDamage.cs:22-32`; `src/ai/tf2/server/Jump/BlastJumpSim.cs:296-307` mirrors it |
| jump-cost reduction (Gunboats, Liberty, Air Strike), Rocket Jumper's no-self-damage | works | `server/Projectiles/TFBlastApplier.cs:412-413` |
| Black Box heal on hit | works | `TFBlastApplier.cs:376` |
| crit while blast-jumping (Market Gardener) | works | `server/Combat/TFDamageModifyRules.cs:221-231`, airborne = `InAirDueToExplosion` (`server/Combat/TFDamageLayer.cs:416`) |
| Mantreads stomp | works | `server/Combat/TFDamageLayer.cs:315` |
| Escape Plan speed tiers | works | `shared/Movement/TFMovementRules.cs:386`; row 775 measured 240/264/336/384 |
| banners: rage, deploy, pulses, 450 u, 1.2 s cond | works | `shared/Player/TFRageMeter.cs:36,59,63`; `shared/Weapons/TFBuffItemBehaviour.cs:72-90`; `server/Player/TFBuffDeployment.cs:51-85`; rage networked `shared/Player/TFPlayerNetState.cs:439,448` |
| Beggar's load / overload | works | `shared/Weapons/TFWeaponHooks.cs:215-217`, `ITFWeaponHook.cs:739-750` |
| Cow Mangler / Bison energy, charged shot | works (row 441/442 live evidence) | `shared/Weapons/TFEnergyBehaviour.cs` |
| B.A.S.E. Jumper | row says works; its evidence first found the server never opened the chute, then a fix via AirJump | row 1101 evidence; **verify live before an archetype relies on it** |
| Air Strike | works with "partial for: the single-shell reload answer and the kill-grown clip ceiling" | row 1104 evidence |
| Half-Zatoichi honorbound **holster cost** | **missing**: stock hurts 50 on holster without a kill (`shared/tf/tf_weaponbase.cpp:1172,1208`); the port has the duel only (`server/Weapons/TFMeleeEffects.cs:225-226,394`), no kill-since-deploy count (grep `KillCountSinceLastDeploy` in src/tf2: empty) | game-side gap |
| bot loadout per slot | **bug**: the table's `slot` column is the item's default slot, so 12 multi-class shotguns (the 11 rows of 10's group other than 10 itself, and 1153) sit under Soldier **primary** (`BotInventory.cs:152`); `PlayerInventory.Equip` refuses them there (`server/Econ/PlayerInventory.cs:120`), so 12 of 44 primary draws fall back to the stock launcher and the Soldier's secondary set is 10 rows with only 2 shotguns: Panic Attack is never drawn, a banner is drawn 4/10 | game side (or the foundation's archetype draw) |
| bot secondary/melee draws today | 4/10 banner, 2/10 Gunboats/Mantreads, 1/10 parachute, 1/10 Bison, 2/10 shotgun; melee 1/8 each of 6,196,128,154,357,416,447,775 | counted from the table (62 Soldier rows: 44 primary, 10 secondary, 8 melee) |

### 3.2 AI side (src/ai/tf2/server)

| need | today | cite |
|---|---|---|
| know its own kit | **no**: `SelfState` carries only the active weapon's kind, clip, reserve, class, conditions, metal (`src/ai/core/Snapshot/PerceivedView.cs:33-48`); the TF HUD read adds class, conditions, metal (`Senses/TFBotGameView.cs:60-68`). The applier knows the held list (`src/ai/server/Apply/BotSelf.cs:85-99`) but the decider does not. **p7f step 2 is building it** | foundation |
| the launcher by kit | `HoldingLauncher` is the stock item_class by name (`Trees/TFDecider.Soldier.cs:17,77`; `Trees/TFDecider.cs:116-117`): Direct Hit, Air Strike and Cow Mangler never count as the launcher, so those bots **never rocket-jump** (`Trees/TFDecider.Jump.cs:64`) and a lull's Hands asks every second for a stock launcher they don't hold (`TFDecider.Soldier.cs:137-141`; a no-op, `BotSelf.cs:88-96`) | Soldier lane |
| the shotgun by kit | `_shotgun` is `tf_weapon_shotgun_soldier` by name (`TFDecider.Soldier.cs:17`, `TFDecider.cs:117`); a banner/Gunboats/parachute/Bison bot has none, so dry up close it only backs off (`TFDecider.cs:472-482`) | Soldier lane |
| weapon facts by item | facts are per catalog kind (item_class), attributes not applied (`Knowledge/TFKnowledge.cs:117-140`): Black Box/Liberty/Beggar's read as the stock launcher (clip 4, 1100 u/s); Liberty's lead is wrong by 40 % | foundation (kit read) or Soldier lane |
| feet aim, lead, self-blast hold | yes, `TFDecider.Soldier.cs:92-119` (feet when vertical speed < 60 and range < 1500) | - |
| melee at all | **no** weapon choice ever selects melee (grep `Melee|Shovel` in Trees: none) | Soldier lane |
| banner use | **no**: no rage read, no row; rage is networked to the owner (`TFPlayerNetState.cs:439`) but not on `SelfState` | foundation (HUD meter) + Soldier lane |
| Beggar's | **broken**: `Engage` refuses to press with `Clip <= 0` (`TFDecider.Soldier.cs:113`) and the Beggar's clip is empty until the trigger is held, and `LauncherDry` keeps it in "back off and load" (`TFDecider.cs:255,472`) | Soldier lane |
| Cow Mangler | energy as `Clip`? stock's `Clip1()` answers energy for an energy weapon (`shared/tf/tf_weaponbase.h:301`); what our `SelfState.Clip` reads for it: **measure**. No alt-fire use | Soldier lane |
| jump cost by kit | **no**: the book is built once per level from the stock launcher's script (`Jump/TFJumps.cs:231-239`) and its simulated Soldier has no wearables (`Jump/BlastJumpSims.cs:61-79`, `NoTFWeaponOwners`), so a Gunboats bot is charged 44 for a 17 jump and an Air Strike/Liberty one 44 for ~33-37; Direct Hit/Air Strike radii differ (measure) | Soldier lane (cost scale) |
| jump at an enemy (Market Gardener, stomp, airshot) | **no**: jumps are planned to nav goals only, refused with a target in sight (`TFDecider.Jump.cs:68`) | Soldier lane (new program) |
| parachute | no | Soldier lane (stock rule exists) |
| sentries | sighted, remembered, avoided (`Trees/TFDecider.Things.cs:23-57`); never attacked from outside range | Soldier lane |
| target priority | Medic bonus 0.35, carrier 1 (`Trees/TFDecider.Feel.cs:128,131,160-173`); no Sniper/Engineer/Pyro weighting | Soldier lane via a foundation hook |
| airborne target read | an `ActorFacts` velocity only; no "blast-jumping" cue though a client draws the smoke trail (`src/tf2/client/Player/TFBlastJumpSmoke.cs`); `ActorConditions` has two free bits (`PerceivedView.cs:59-68`) | foundation |
| modes | CTF and control points are read; KOTH (`Hold`) and Payload (`Push`) map in the feed (`Knowledge/TFObjectiveFeed.cs:108-109`) but no branch reads them (`Trees/TFDecider.Points.cs:274,307,327,404` read `Capture` only) | P8 (plan row "P8 the other modes") |
| ability rows | rocket_jump only for the Soldier (`Knowledge/TFAbilities.cs:50-51`); the Soldier block is empty **(p7f, uncommitted)** `Classes/TFSoldier.cs` `Abilities() => []`, block at `BlockOf(Soldier)` = 48 (`TFClassSpecs.cs`) | Soldier lane |

**Missing, by side.** Game: the Half-Zatoichi holster cost; the item table's per-class slot (or the draw must use
`Definition.Slot(cls)`); B.A.S.E. Jumper and Air Strike want a live recheck. AI, foundation: the kit read (defindex,
item_class, attribute classes per slot, plus the rage meter and an energy meter as HUD numbers), the archetype draw,
a blast-jumping/airborne bit on `ActorFacts`, hooks for Survive's threshold, target priority and the in-flight phase.
AI, Soldier lane: launcher/secondary/melee by kit, melee use, banner, Beggar's, Cow Mangler alt, jump cost by kit,
the attack-jump program, parachute, sentry shelling, Pyro handling, pocket/roam positions.

## 4. The design for the code lane

### 4.1 Ship order

1. **A Stock** (today's play, made kit-safe) and the kit fixes every archetype needs: launcher/secondary/melee by
   slot, melee when dry or at point blank, no Select of a weapon not held.
2. **C Roamer** (Gunboats / Black Box) - jump cost by kit; flank and pick.
3. **D Banner** (three banners) - one row, the pop rules.
4. **E Direct Hit** - aim rules and sentry shelling (sentry shelling then also serves A-D).
5. **B Pocket** once a Medic class is ready (its file's `Ready`).
6. **F Beggar's**, **G Trolldier**, **H Cow Mangler**; then the long tail.

### 4.2 The draw and the loadout

Archetype first, loadout from it (C.3.1), through the foundation's archetype table and seam into `BotInventory`.
Weights for an unpinned Soldier (pub feel, no source gives pub shares: **a choice to revisit with data**): A 30,
C 20, D 20, E 10, F 5, G 10, H 5; B replaces 15 of A when the team has a Medic. Per slot a preference list, the base
item last; reskins are interchangeable members of their base's group (draw among the group by seed so the looks
vary, behaviour does not). Rules for the table:
- Rocket Jumper (237) never (barred by `BotInventory.BarsTheObjective`, `BotInventory.cs:148,174-176`).
- Half-Zatoichi (357) never until the holster cost is ported (else the bot is unpunished and plays wrong).
- B.A.S.E. Jumper only in G and only after a live check that the server opens the chute.
- A row whose slot does not fit the class (`Definition.Slot(cls)`) is never offered: fix the draw's key, so the
  shotgun group and 1153 reach the Soldier's secondary.
- Mode bias: Pain Train in a CP/Payload attacker's G/A melee list; not in CTF (it does not bar the flag, it just does
  nothing there).

### 4.3 Decision rules per archetype

All sit in `Classes/TFSoldier.cs`'s `SoldierPlay` overrides (`Hands`, `Engage`, `FightInSight`, `OneShots`,
`Weigh`, `Role`, `PlaceUtility`/`GoPlace`, `Died`) **(p7f, uncommitted)**. "Kit" means the foundation's read.

**Common (all archetypes)**
- *Weapons by slot.* Launcher = the primary slot's weapon whatever its item_class (18 group, 127, 228, 414, 441, 513,
  730, 1104); secondary gun = the secondary slot when it is a gun (10 group, 415, 1153, 442); melee = the melee slot.
  `HoldingLauncher`, `JumpRefusalNow` and Hands read these, never the stock names.
- *Point blank.* Target inside `SelfBlastUnits` (160, `TFDecider.cs:46`) and in sight: swap to the secondary gun if
  loaded, else melee, instead of holding fire (mistake 1). Back to the launcher once the target is past 300 u
  (FightNear 320) or after 1.5 s.
- *Dry.* Launcher empty and target inside 500 u (stock's `closeSoldierRange`, `tf_bot.cpp:3384`; ours is 600,
  `TFDecider.Soldier.cs:38`: take 500 to match stock) -> secondary gun if loaded, else melee if inside 150 u, else back
  off and reload. A Gunboats/banner/parachute bot has no secondary gun: the melee or the back-off arm.
- *Melee.* Swing when the target is inside 100 u (stock desired melee range, `tf_bot.cpp:3190-3193`) and no gun is
  loaded, or the archetype says so (G, Equalizer below).
- *Pyro.* A sighted enemy whose rendered class is Pyro, facing the bot (view direction within 30 deg; measure),
  within 128 + 256 u (deflection radius, `TFFlameEffects.cs:631`, plus one rocket-flight reaction at 1100 u/s,
  measure): no rockets; shotgun or Bison if held, else back off to 500 u and rocket the feet.
- *Sentry shelling.* A remembered enemy sentry (`TFDecider.Things.cs`) with the bot's launcher loaded and health
  over 50 %: go to a spot 1.1-1.5 x 1100 u from it with a line to it (stock `tf_bot_destroy_enemy_sentry.cpp:244,304,
  325`; ours has the sight tables), fire at its body (not feet). Weigh it over Course/Points while the sentry is the
  thing between the bot and the objective; stop after 15 s with no hit (measure) or when hurt (Survive).
- *Height.* In FightInSight, prefer a step that gains height when one is within 200 u (the graph's cells): measure the
  gain on cp_granary; never stand lower than an enemy who has height for over 2 s when a higher cell is within reach.
- *Air.* Reload during a rocket jump (the program's flight; the clip refills to the jump's need on landing).
- *Targets.* Medic +0.35 (kept), Sniper +0.25, Engineer +0.2 near his buildings, Scout -0.15 beyond 600 u (don't
  chase): numbers are a choice to tune by the A/B, sourced only as an order from [W2].

**A. Stock** - the common rules; jumps as today (floor 90, `TFDecider.Soldier.cs:26`).

**B. Pocket** - `Weigh(Course|Roam) x0.5`, a new place: within 300 u of the Medic's position, between it and the
nearest believed enemy; jump only when the plan lands within 450 u of the Medic or the Medic's beam is on the bot
(Uber: `ActorConditions.Invulnerable` on self); target bonus +0.5 for an enemy within 500 u of the Medic.

**C. Roamer** - jump cost from the kit (Gunboats x0.4: stock's attribute, measured 17 vs 44); health floor 70
(measure: cp_granary, the share of roamer jumps that land under 50 health must stay under 10 %); route bias off the
main route (the Spy row's traffic prior of C.3.2 if built, else the ChokeMap's betweenness); target bonus Medic +0.6,
Sniper +0.4; Survive at 35 % health (Black Box: 30 %, since a clip returns up to 60; [W1]); Escape Plan held while
going to heal with nobody in sight and health <= 120 (speed tier 96 %+, [W1]; marked for death: drop it the moment an
enemy is sighted within 1000 u, since the mark lasts 3 s after the swap).

**D. Banner** - row `soldier_banner` (§4.4). Pop when rage is 100 (`TFRageMeter.FullMeter`) and not carrying the flag
(stock's gate, `tf_bot.cpp:4567`) and:
- Buff Banner 129: a push is called or under way (`TFTeamPlay.Push`, `TFDecider.Rally.cs`) or a sighted enemy within
  1250 u with at least one teammate within 450 u; else hold.
- Battalion's 226: an enemy shown Invulnerable or a sentry firing within 1100 u, or own team's point is being capped,
  with a teammate within 450 u; or own health under 40 % in a fight.
- Concheror 354: a push or a gather moving to a point with a teammate within 450 u; or retreating hurt with a
  teammate.
- Fallback: full for more than 30 s with a teammate within 450 u -> pop (pubs; stock pops at once, so this is the
  middle way; measure pops per life).
Rage fills at 600 dealt (480 Concheror): the archetype fights at the front with the team (`Weigh(Fight) x1.2`).

**E. Direct Hit** - aim body for grounded targets (radius 44: a feet shot misses), feet only when the target stands
on a floor within 30 u of the bot's height and inside 400 u (measure); lead at 1980 u/s (the item's
`mult_projectile_speed`, not the kind's 1100); airborne targets (|vz| > 200 or the blast-jumping bit) get priority
+0.3 and a direct aim (mini-crit); FightNear 500, FightFar 1500 (stock max 3000); sentry shelling weight x1.5.
Reserve Shooter: swap to it when the target is airborne within 800 u (mini-crits 5 s after deploy), back after 5 s.

**F. Beggar's** - in a lull, hold `IN_ATTACK` until `Clip == 3` and release at once (never past 3: misfire; the row's
press shows a 4 s hold misfired, `items_working.tsv` row 730); carry the load; on a target inside 600 u release a
tap (fires the held burst: measure the press shape on the live weapon); Engage must not refuse on `Clip == 0` for an
auto-fire weapon; hold a corner within 300 u of a choke on the enemy's side of the objective (`Weigh(Defend) x1.3`);
reload at packs or resupply (no dispenser ammo).

**G. Trolldier** - a new **attack jump** (ability row `soldier_attack_jump`, a program): with the target sighted,
grounded, 400-900 u away (measure the band in `BlastJumpSim`), health over the floor and the melee a Market
Gardener: plan a jump whose apex passes within 120 u above the target's predicted spot, swap to melee in flight,
swing when within 90 u (melee range, measure); Mantreads: aim to land on the target (stomp); Air Strike: while
blast-jumping fire at targets below (the faster rate); B.A.S.E. Jumper per stock (`tf_bot.cpp:4539-4560`: open when
falling, health >= 50 %, not burning and ground over 300 u below; close under 150 u or burning or under 50 %). No
attack jump across a sighted Sniper's line or into a sentry's range.

**H. Cow Mangler** - never reloads at a pack for ammo (no ammo); alt-fire (`IN_ATTACK2` held, charged shot ~2 s per
the row's evidence: "right-click fires itself at 2 s") at a sentry or dispenser in sight within 1100 u before a push,
or at a group of 2+ within 300 u of each other; normal shots otherwise; buildings take 20 % so shelling a sentry
uses the charged shot, then normal shots only if teammates are on it.

**Long tail.** Bison: secondary vs Pyro and through 2+ enemies in a line. Mantreads: jump floors -10 (knockback),
stomp attempts when an enemy is under the landing. Equalizer: melee with health under 70 and target inside 100 u.
Disciplinary Action: hit a teammate ahead within 80 u while both walk to the front (no enemy sighted, goal over
1000 u). Pain Train: `Weigh(Points) x1.3` on an attacker. Half-Zatoichi: not drawn (§4.2).

### 4.4 Ability rows (Soldier block, `BlockOf(Soldier)` = 48 **(p7f)**; append only)

| row | run | what |
|---|---|---|
| `soldier_banner` | Program | select the secondary (the buff item), wait for its deploy, `IN_ATTACK` once, the game switches back (`TFBuffItemBehaviour.cs:94-95`); refuse when not full |
| `soldier_load` | Buttons | hold `IN_ATTACK` for N ticks (Beggar's load; N from the clip read, released on `Clip == 3`) |
| `soldier_charged_shot` | Buttons | hold `IN_ATTACK2` (Cow Mangler) |
| `soldier_attack_jump` | Program | the rocket-jump program with a moving target goal, a melee swap and a swing |
| `soldier_parachute` | Buttons | `IN_JUMP` airborne (open/close) |

Rocket jump stays the shared row 0 (`TFAbilities.cs:51`): recordings name it.

### 4.5 Facts to write (each red once against a mutant; `src/ai/tests/TF2/`, beside `TFDeciderTests.cs`)

1. A Direct Hit bot counts its primary as the launcher: it begins a rocket jump on the fixture where a stock bot does
   (mutant: name check back -> no jump).
2. No Select for a weapon the kit lacks: a banner bot in a lull sends no select of a shotgun (mutant red).
3. Point blank swaps: target at 120 u, launcher held -> select secondary or melee, never holds fire (mutant: today's
   rule).
4. Dry inside 500 u with no secondary gun -> melee at 100 u, back off beyond.
5. Banner pops only full, only with a teammate in 450 u or a push, never carrying (one fact per arm, per banner type).
6. Beggar's loads to 3 and releases (never a 4th tick held); fires with Clip 0 at load time (mutant: today's `Clip <= 0`
   gate).
7. Jump cost by kit: a Gunboats bot with 70 health takes a jump a stock bot at 70 refuses (mutant: cost x1).
8. Direct Hit aims body at a grounded target; stock launcher aims feet (both arms on one fixture).
9. Lead by the item's speed: Liberty leads at 1540, Direct Hit 1980 (mutant: kind speed).
10. Sentry shelling picks a spot 1210-1650 u from the sentry with a line; none inside 1100.
11. Pyro within reach facing the bot: no rocket; shotgun if held.
12. Pocket keeps within 300 u of a scripted Medic over 60 s (> 80 % of ticks); a Roamer's mean distance is over 3x
    (C.3.1's monotonic style fact).
13. Archetype draw: same seed, same archetype and loadout; no 237, no 357; every drawn item fits its slot
    (`Definition.Slot(cls)`); the shotgun group reaches the secondary.
14. Market Gardener swing only while blast-jumping (attack-jump fact on the simulator).
15. Parachute open/close per stock's three conditions.

### 4.6 Live measures (the A/B harness, cp_granary and ctf_2fort; 8 bots, 3 runs of 10 min, a fixed seed set)

First, the user's measure: **time from round start to first on the point / first touch of the enemy flag, captures
per 10 min, and the furthest point held** for each archetype vs A (control) on the same seeds. Then:
- rocket jumps per life, jump deaths (died within 3 s of a jump's landing), share of jumps landing under 50 hp
  (C target < 10 %);
- damage dealt per life, kills, deaths, KD; shots, hits, feet shots, airshots (E);
- banner pops, pops with >= 2 teammates in 450 u, and team damage during the 10 s (D);
- sentries destroyed and time from first sight to destruction (E, H); sentry deaths;
- Beggar's bursts and misfires (F, target 0 misfires); attack jumps begun, MG crits landed (G);
- melee swaps at point blank, self-blast damage taken (all; should fall vs today);
- stuck lines (the harness's).
"Plays differently" is shown when the archetypes' numbers separate: C's distance from team and jumps per life over A;
E's mean engagement range over A's; D's team damage during a pop over the same team's without; a flat mutant
(archetype ignored) puts them all on A's.

### 4.7 What the code lane needs from the foundation

- **Kit read** (p7f step 2): per slot defindex, item_class, attribute classes; and live meters: rage (0-100,
  `TFPlayerNetState.cs:439`), the energy of an energy weapon, the Beggar's load (the clip is enough if it reads 0..3).
- **Weapon facts by item**, not by kind: speed, clip, radius with the item's attributes (`TFKnowledge.WeaponOf`), or
  the kit hands the attribute values and the Soldier computes them.
- **Archetype draw** seam into `BotInventory` with the slot-fit fix (`Definition.Slot(cls)`), and the per-class table
  with reskin groups.
- **Hooks** beyond `ClassPlay`'s current members **(p7f)**: a Survive threshold (`HurtShare`, `TFDecider.cs:37`) per
  class/archetype; a target-priority bonus per actor (`TakesTarget`, `TFDecider.Feel.cs:168-169`); the jump rule's
  health floor and a cost scale (`TFDecider.Soldier.cs:26`, `JumpHealthRule`); an in-flight callback while a jump
  program has the bot (melee swap, parachute, air fire).
- **ActorFacts**: a blast-jumping bit (a client sees the smoke trail, `TFBlastJumpSmoke.cs`) and, later, a buffed bit
  (the banner's glow): `ActorConditions` has bits 6 and 7 free.
- **Composition**: the Soldier is ready; Pocket waits on the Medic file's `Ready`.

## 5. Sources

Web (read 2026-11-20):
- [W1] https://wiki.teamfortress.com/wiki/Soldier - stats of every weapon above.
- [W2] https://wiki.teamfortress.com/wiki/Community_Soldier_strategy - aiming, jumping, matchups, per-weapon advice
  (first 100k characters read).
- [W3] https://wiki.teamfortress.com/wiki/Soldier_(competitive) - pocket/roamer, 5CP, A/D, KOTH.
- [W4] https://wiki.teamfortress.com/wiki/Anti_Soldier_strategy - weaknesses.
- [W5] https://wiki.teamfortress.com/wiki/Pocket_(competitive) (search snippet: combo, Gunboats roamer).
- [W6] https://wiki.teamfortress.com/wiki/Soldier_weapons_(competitive) - allowed/banned/avoided (the page warns it may
  be outdated).
- [W7] https://gamersdecide.com/articles/team-fortress-2-best-soldier-loadouts - Rocket Jumper/Mantreads/Market
  Gardener trio, Gunboats (a weak source; used only for pub loadouts).
- [W8] https://wiki.teamfortress.com/wiki/Market_Gardener - crit while blast-jumping, 20 % slower swing.
- [W9] https://wiki.teamfortress.com/wiki/Gunboats.
Not found: any Highlander-specific Soldier source; pub archetype shares.

Valve clone (`../source-sdk-2013-valve`, read only): `src/game/server/tf/bot/tf_bot.cpp` (:1026-1068 rosters,
:3164-3210 ranges, :3377-3390 shotgun swap, :4539-4586 parachute and banner), `bot/behavior/tf_bot_behavior.cpp:629-676`
(rocket aim), `bot/behavior/tf_bot_destroy_enemy_sentry.cpp:159-199,244,304,325`, `bot/behavior/tf_bot_use_item.cpp:20-66`,
`bot/behavior/tf_bot_get_health.cpp:14-17` (health ratios 0.3/0.8), `src/game/server/tf/tf_obj_sentrygun.h:35`,
`src/game/shared/tf/tf_weaponbase.cpp:1172,1208` (honorbound), `tf_weaponbase.h:301` (energy clip),
`tf_weapon_medigun.cpp:489-499`.
