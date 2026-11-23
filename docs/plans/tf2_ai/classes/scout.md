# P7 research: the Scout (2026-11-20, base `225fe2e96`)

Every `path:line` is at `225fe2e96`. The foundation lane (p7f) is editing `TFDecider.cs`, `TFDecider.Soldier.cs`,
`TFAbilities.cs`, `TFDecider.Roles.cs` and `TFDecider.Personality.cs` in this worktree. Lines in those files were
checked with `git show 225fe2e96:<path>`, not off the working tree. Game knowledge cites a URL from §5. A number
marked **measure** is one the code lane must measure before building on it.

## 1. The Scout's job, by mode

**What the class is.** 125 HP, or 110 with the Sandman. Runs at 133 %: 400 u/s, which is the Baby Face's
Blaster page's "400 to 360". Captures points and pushes carts at twice the rate, a 2 under `mp_capstyle` 1
(`src/tf2/server/Player/TFCapturePlayerRules.cs:30`). Double jump. The smallest hitbox. Sources: [Scout], [BFB].
- **The damage curve decides his range.** The Scattergun is 10 pellets of 6: 60 at 512 u, up to 175 % (105)
  point blank, falling to 50 % at 1024 u and past it ([Damage], [Scout comp]). The port's ramp is
  `src/tf2/server/Combat/TFDamageModifyRules.cs:526-531`. The Pistol does 15 base, 22 point blank and 8 at long
  range ([Community]).
- **So the Scout fights inside ~350 u.** Past ~600 u he uses the Pistol or does not fight.

### Pub play, every mode
- **Where he stands.** On the flanks, never the front line ([Basic]: "the frontlines … multiple attackers make
  dodging hard"). He comes from above or behind, or wherever the target is not looking ([Community]).
- **Range.** Scattergun from a few feet out, two shots kill most classes at point blank. Pistol at medium range
  and for finishing someone running away ([Community], [Basic]).
- **Target priority.** Medics first ([Community], [Scout comp]); then lone or hurt players: Spies, Snipers,
  Engineers away from their sentry, enemy Scouts ([Community]). Avoid Heavies head-on, Pyros up close, Sentries,
  and Soldiers or Demomen in tight spaces ([Community], [Scout comp]).
- **Push, hold and retreat.** Hit and run: close in, fire a few shots, leave ([Scout comp]). Keep 2 or more shells
  loaded before engaging ([Community]). Retreat when low: against another Scout, at under 75 HP ([Community]).
  Take health packs, because the class has no sustain of its own.
- **Movement tech.** Strafe all the time, and fire when the crosshair crosses the target rather than tracking
  ([Community]). Mid range: stay grounded and strafe; very close: jump, so shots fired downward miss ([Basic]). The
  double jump takes the held movement key's direction, and with no key it kills horizontal speed; just before
  landing it negates fall damage; crouch-jumping shrinks the hitbox ([Community]).
- **With teammates.** Protect the Medic, who should not spend Über on a Scout ([Community]: "Scouts are low on
  healing priority"; stock agrees, `behavior/medic/tf_bot_medic_heal.cpp:426-437`). Distract for a Heavy or a
  Sniper, lead enemies into a Demoman's traps, give an Engineer early warning ([Community], co-op section).
- **What beats him, and how he plays around it.**
  - Sentries: perfect accuracy cancels the dodge. Approach from the side or behind, or chip with the Pistol from
    outside its range ([Community]). Buildings take no falloff ([Damage]).
  - Snipers: jump arcs are predictable, so learn the sightlines.
  - Soldiers and Demomen: splash punishes double jumps in tight spaces ([Scout comp]).
  - Pyros: airblast. Back-pedal, since a Scout outruns a Pyro ([Community]).
  - Heavies: avoid them. Natascha's slow is deadly to a Scout ([Basic]).

### By mode
- **CTF.** The ideal Intelligence carrier ([Community]). BFB or plain speed on the run, and a teammate escorts.
  Bonk cannot pick up the flag (`src/tf2/server/Entities/Ctf/ItemTeamFlag.cs:620`) or be drunk with it
  (`src/tf2/server/Weapons/TFLunchboxWeapon.cs:172-180`).
- **Control points, 5CP.**
  - Mid: comp Scouts arrive second, after the Demoman. They push the flanks so enemy Scouts cannot backcap, and
    clean up the damaged ([Scout comp]).
  - Pubs: stand on an unguarded point to double-cap; a backcap locks the enemy out of your point ([Community]).
- **Attack/Defend.**
  - Attack: rush points and backcap, and pass sticky traps with Bonk. The comp wiki gives Gravel Pit A as the
    example ([Scout comp]).
  - Defence: comp teams usually swap the Scout out, for an Engineer, Sniper or Heavy ([Scout comp]). In a pub he
    stays as a flanker who picks the Medic out of the push.
- **KOTH.** Rush the point early, pressure Engineer setups, and target Medics and Demomen ([Scout comp]).
- **Payload.**
  - Attack: push the cart at ×2 rate. Crouch behind the cart to hide from sentries. The Flying Guillotine is good
    on slow cart pushers ([Community]).
  - When the cart stalls: play flank or pocket ([Scout comp]).
- **Competitive 6s.** Two Scouts ([6s], [Scout comp]):
  - **The combo or "passive" Scout** stays with the Medic, Pocket Soldier and Demoman. He denies enemy bombers and
    follows up the Soldier's bomb. With the Boston Basher he self-hits to build Über where leagues allow it.
  - **The flank or "aggro" Scout** holds and pushes the flank, picks stragglers, and backcaps when the enemy is
    busy. He waits briefly during an Über push so the flank is not contested. If the combo Scout dies, he fills in.
  - Almost every comp Scout runs the stock Scattergun and Pistol, since most unlocks are banned ([Scout comp]).
- **Highlander.** One Scout per team ([Highlander]). His role mixes pocket and flank: he helps on the flank,
  takes overheals, and stays alive for captures (search summary in §5).

### Beginner mistakes that make a bot read as a bot
- **Fighting wrong:** standing still while firing, or strafing in one rhythm; the Scattergun at 1,000 u or more,
  and backing away from a close target, the Soldier's rule (the port's bot does both today, §3.2); a Heavy face to
  face; turning instantly to anyone who shoots from behind.
- **Moving wrong:** the main choke, or chasing into a sentry; double-jumping at once on every jump, with no air
  control left to dodge; bunny-hopping in a corridor against a Soldier.
- **Items wrong:** reloading the FaN with one shell left; drinking Bonk in the open and walking slowed into the
  enemy; swinging the Boston Basher at air (all [Basic]).
- **Objective wrong:** not standing on the point, and so throwing away the ×2 cap.

## 2. Play styles by weapon set

The item rows are `game/mod_tf2/scripts/items/items_working.tsv`: 75 Scout rows, all `works`. Reskins are the same
`item_class` with no attributes (or only `ragdolls_*`), grouped under their base item. Defindex `item_class`
(attributes). "TSV line N" below is the line of that file, header = line 1.

| group (base) | items |
|---|---|
| Scattergun, stock behaviour | 13, 200, 669, 799, 808, 888, 897, 906, 915, 964, 973, 15002, 15015, 15021, 15029, 15036, 15053, 15065, 15069, 15106, 15107, 15108, 15131, 15151, 15157 `tf_weapon_scattergun` (none) |
| Force-a-Nature | 45, 1078 `tf_weapon_scattergun` (set_scattergun_has_knockback, mult_postfiredelay, mult_bullets_per_shot, mult_dmg, mult_clipsize, set_scattergun_no_reload_single) |
| Back Scatter | 1103 `tf_weapon_scattergun` (closerange_backattack_minicrits, mult_clipsize, mult_spread_scale, mult_crit_chance) |
| Shortstop | 220 `tf_weapon_handgun_scout_primary` (provide_on_active, mult_reload_time_hidden, damage_force_reduction, airblast_vulnerability_multiplier) |
| Soda Popper | 448 `tf_weapon_soda_popper` (mult_reload_time, mult_postfiredelay, mult_clipsize, set_scattergun_no_reload_single, hype_on_damage) |
| Baby Face's Blaster | 772 `tf_weapon_pep_brawler_blaster` (mult_player_movespeed, boost_on_damage, hype_resets_on_jump, lose_hype_on_take_damage, mult_clipsize) |
| Pistol, stock behaviour | 22, 23, 160, 209, 294, 1202, 15013, 15018, 15035, 15041, 15046, 15056, 15060, 15061, 15100, 15101, 15102, 15126, 15148, 30666 `tf_weapon_pistol` (none; shared with the Engineer) |
| Winger | 449 `tf_weapon_handgun_scout_secondary` (mult_dmg, mult_clipsize, mod_jump_height_from_weapon) |
| Pretty Boy's Pocket Pistol | 773 `tf_weapon_handgun_scout_secondary` (provide_on_active, add_onhit_addhealth, mult_postfiredelay, mult_clipsize) |
| Bonk! Atomic Punch | 46, 1145 `tf_weapon_lunchbox_drink` (none: weapon mode 0) |
| Crit-a-Cola | 163 `tf_weapon_lunchbox_drink` (set_weapon_mode, mod_mark_attacker_for_death) |
| Mad Milk | 222 `tf_weapon_jar_milk` (extinguish_reduces_cooldown); 1121 Mutated Milk (+override_projectile_type) |
| Flying Guillotine | 812, 833 `tf_weapon_cleaver` (desc_cleaver_description, mult_crit_chance) |
| Bat, stock behaviour | 0, 190, 660, 30667 `tf_weapon_bat`; 221, 999 `tf_weapon_bat_fish` (Holy Mackerel: stock damage); 572 Unarmed Combat `tf_weapon_bat_fish` (fish_damage_override) |
| Sandman | 44 `tf_weapon_bat_wood` (set_weapon_mode, add_maxhealth) |
| Wrap Assassin | 648 `tf_weapon_bat_giftwrap` (set_weapon_mode, mult_dmg, effectbar_recharge_rate) |
| Boston Basher / Three-Rune Blade | 325 / 452 `tf_weapon_bat` (bleeding_duration, hit_self_on_miss) |
| Candy Cane | 317 `tf_weapon_bat` (drop_health_pack_on_kill, mult_dmgtaken_from_explosions) |
| Sun-on-a-Stick | 349 `tf_weapon_bat` (or_crit_vs_playercond, mult_dmg, mult_dmgtaken_from_fire_active) |
| Fan O'War | 355 `tf_weapon_bat` (mark_for_death, mult_dmg, minicrits_become_crits) |
| Atomizer | 450 `tf_weapon_bat` (air_dash_count, mult_single_wep_deploy_time, mult_dmg_vs_players) |

**What a bot must read off its own kit.**
- **`item_class` alone does not tell the items apart.** FaN, Back Scatter and Scattergun share one; Bonk and
  Crit-a-Cola share one; the Atomizer, Basher, Fan O'War and others share `tf_weapon_bat`.
- **The bot needs the defindex, or the attribute classes plus `set_weapon_mode`.** Bonk is mode 0 and Cola mode 2
  (`TFLunchboxWeapon.Drink`, per the TSV line 65 evidence).

### The archetypes, and what changes in the bot's decisions

**A. Flanker (stock: 13 / 23 / 0).** The comp and pub default ([Scout comp]).
- **Range:** Scattergun at 0–350 u, best under 200; Pistol at 600–1,500 u.
- **Routes:** a flank route, not the main choke.
- **Targets:** Medic, then the hurt, then a lone player.
- **Retreat:** with the clip at 1 or less and no Pistol ammo, or on low health.
- **Movement:** double-jump dodges close in; grounded strafing in mid range.
- **Objective:** caps when a point is open (×2).

**B. Bonk runner (Scattergun / Bonk 46 / Bat).** The way past sentries, sticky traps and chokes ([Community],
[Scout comp]). Also distracts sentries and Snipers and body-blocks doorways.
- **What Bonk does.** 8 s of near-total damage immunity. No attacking, no switching weapons, no capping, no
  flag ([Bonk]).
- **Its cost.** A 5 s slow after, 25 % rising to 50 % at 200 or more damage absorbed. Recharges in 30 s ([Bonk]).
- **Changes in the bot:**
  - A drink decision tied to a seen sentry or trap on the route.
  - Arrives after the phase ends, so it can cap.
  - Goes to cover or a health pack during the slow.
  - Otherwise plays the Flanker without a Pistol: an empty Scattergun means melee or retreat.

**C. Force-a-Nature brawler (FaN 45 / Pistol 23 / Bat 0).**
- **The weapon.** 2 shells of 12 pellets × 5.4. Knockback only on hits over 40 damage, once per target per 1.5 s.
  Firing in the air pushes the Scout opposite his aim, which gives a third jump ([FaN]).
- **The port's shove, measured (TSV line 41):** 272 u at 150 u, 108 u at 60 u, none at 500 u.
- **Changes in the bot:**
  - Shorter range: under 200 u, so the knockback lands.
  - Fire both shells, then reload; never reload with 1 in the clip ([Basic]).
  - Pistol to finish.
  - Uses the knockback to push a capper off the point, or an enemy off a ledge.
  - The force jump as a mobility one-shot: phase 2.

**D. Milk sustain / combo Scout (Scattergun / Mad Milk 222 / Boston Basher 325, or Bat).** The pub sustain set and
the comp "passive Scout".
- **Mad Milk.** Lasts 10 s, cut to 2.5 s on a target being healed. 60 % of damage dealt to a milked enemy comes
  back as health. Splash radius 200 u, recharge 20 s. It extinguishes teammates for a 20 % refund and shows cloaked
  Spies ([Milk]).
- **Changes in the bot:**
  - Stays within ~600 u of its own Medic or combo (**measure** the band).
  - Throws Milk on a group or a duel target before a fight it may lose, and on burning teammates.
  - Swings the Basher only at a certain hit: a miss is 18 self-damage plus bleed ([Community]).
  - Lower flank share and fewer backcaps.

**E. BFB runner (BFB 772 / Pistol or Winger / Bat), for CTF and Payload.**
- **The weapon.** 360 u/s base, rising to 520 at full boost. Boost is +1 per point of damage dealt, −4 per point
  taken, and −75 % per air jump ([BFB]).
- **The port, measured live:** 360, then 450 after 200 damage (TSV line 199). **measure** the 520 cap against it.
- **Changes in the bot:** no air dashes, except to dodge a rocket or for a nav link. Builds boost on chip damage
  before a run. A high weight on carrying the flag.

**F. Soda Popper aerialist (Soda Popper 448 / Winger 449 / Atomizer 450).**
- **The weapon.** Hype at 350 damage gives 5 air dashes (`src/tf2/shared/Movement/TFJump.cs:35`) for about 10 s:
  the meter drains at 0.75 × 12.5/s (`src/tf2/server/Player/TFHypeDrain.cs:82`).
- **Changes in the bot:** activates hype on alt-fire when the meter is full and a fight within 800 u is starting
  or a long gap lies ahead. Fights airborne against Soldiers and Demomen. 2-shell clip.

**G. Sentry and Sniper poker (Shortstop 220 / Winger 449 / Bat).**
- **The weapons.** The Shortstop: 4 shots and a tighter spread, good at longer range, with a shove alt-fire
  ([Community], [Basic]). The Winger: 5 rounds and +25 % jump height while held.
- **Changes in the bot:** a longer fight band, up to 600 u. Chips a lone sentry from outside its range. Shoves a
  Pyro or a capper.

**H. Cleaver harasser (Scattergun / Flying Guillotine 812 / Bat), for Payload.**
- **The weapon.** 50 damage at any range, plus 40 bleed over 5 s. Recharge 5.1 s, 1.5 s less after a long hit
  ([Cleaver]; `src/tf2/shared/Weapons/TFItemMeter.cs:77`). It lobs: aimed level it flies over a Heavy at 300 u, and
  10° down it hits (TSV line 222 evidence).
- **Changes in the bot:** throws at cart pushers and fleeing targets at 300–1,500 u. Never at buildings.

**Long tail (not first).**
- **Atomizer trickster.** Triple jump, but only with the bat deployed for at least 0.7 s
  (`src/tf2/shared/Movement/TFJump.cs:94-99`).
- **Back Scatter ambusher.** Mini-crits from behind at close range ([Community]).
- **Fan O'War marker.** Marks one enemy for 15 s.
- **Candy Cane** (healthpack on kill, +25 % explosive damage taken) and **Sun-on-a-Stick** (crits on burning;
  pairs with a Pyro).
- **Wrap Assassin.** A bauble that bleeds.
- **Pretty Boy's Pocket Pistol.** +3 HP per hit, for flank sustain.
- **Sandman harasser:** blocked on the game side (§3.1).
- **Crit-a-Cola assassin:** its drawback is not in the game (§3.1).

## 3. What the port and the AI can do today

### 3.1 Game side (`src/tf2/`)

| behaviour | state | evidence |
|---|---|---|
| ×2 capture | yes | `src/tf2/server/Player/TFCapturePlayerRules.cs:30`; phased players cannot cap, `:55` |
| double jump, Atomizer triple, Winger height, hype dashes | yes | `src/tf2/shared/Movement/TFJump.cs:71-100` (dash count `:87`, Atomizer's 0.7 s hold `:94-99`), `:35` |
| `AirDash` networked | yes | `src/tf2/shared/Player/TFPlayerNetState.cs:188`; on the AI facts, `src/tf2/server/Player/TFActorFacts.cs:41,87` |
| Bonk phase, 8–9 s, refill, after-slow | yes | `TFLunchboxWeapon.cs:139` (Drink), `:190-193` (100/12.5 + 1 s); `TFHypeDrain.cs:90-107` (drain/refill at 12.5 and 3.3/s, stock `tf_player_shared.cpp:172-173`); `TFPlayerConditionEffectArms.cs:72-87` (5 s slow); no flag pickup `ItemTeamFlag.cs:620` |
| drink meter networked | yes | `TFPlayerNetState.cs:336` (`EnergyDrinkMeter`) |
| Crit-a-Cola mini-crits | yes | TSV line 65: EnergyBuff 9 s |
| **Crit-a-Cola's mark for death on attacking** | **missing** | stock `CTFPlayerShared::OnAttack` (`tf_player_shared.cpp:4643-4653`, `mod_mark_attacker_for_death`); that name is in no file under `src/tf2` (grep this session). The row says `works` and is stronger than stock |
| **Sandman's ball (stun / slow)** | **missing** | `tf_weapon_bat_wood`'s alt-fire falls to the base class skill (`src/tf2/shared/Weapons/TFMeleeBehaviour.cs:164-185`: only `BatGiftwrap` throws); there is no `tf_projectile_stun_ball` (`src/tf2/server/Projectiles/TFProjectileEntities.cs:36-53`); deferred in `docs/plans/tf2_ai/README.md:148`. The row says `works` only for its attributes |
| Wrap Assassin bauble | yes | `src/tf2/server/Projectiles/TFProjectileEffects.cs:133`; 10 s bar `TFItemMeter.cs:78` |
| FaN knockback and self-shove | yes | TSV line 41 (TFScattergunBehaviour, TFDamageLayer); the victim's movement stun `TFScattergunBehaviour.cs:44` |
| Soda Popper hype | yes | `src/tf2/shared/Weapons/TFScattergunBehaviour.cs:63-79`; meter `TFPlayerNetState.cs:459` |
| BFB boost | yes | TSV line 199 (TFMaxSpeed.cs:100, TFHype) |
| Shortstop shove | yes | `src/tf2/shared/Weapons/TFWeaponBehaviour.cs:726` |
| Mad Milk, Guillotine | yes | TSV lines 97 (milk) and 222 (cleaver); bars `TFItemMeter.cs:74-80` |
| per-slot item meters networked | yes | `TFPlayerNetState.cs:672` (`ItemMeters`) |
| Scattergun ramp, falloff | yes | `TFDamageModifyRules.cs:526-531` |

**Missing on the game side, for the Scout:**
1. The Sandman's stun ball, with its HUD and movement arms: a stronger-model stage (F33).
2. The Crit-a-Cola's mark for death on attack: small, one attribute hook on attack.

Until both land, the Sandman and Crit-a-Cola rows should not be drawn for bots. Flip them to `partial`: that is
the TSV owner's call, not this lane's.

### 3.2 AI side (`src/ai/tf2/server/`, core `src/ai/core/`)

| need | state | evidence |
|---|---|---|
| a Scout subtree | **no**: every class runs the Soldier's | `Trees/TFClassTrees.cs:15`; class pick always Soldier, `TFBotClassPick.cs:22,55` |
| fight band | **wrong for a Scout** | `Trees/TFDecider.cs:46`, `FightNear 320 / FightFar 900`; `FightAtRange` backs off inside 320 u (`Trees/TFDecider.cs:485-494`): a Scout retreats from the targets it should close on |
| fire reach | **wrong** | `Trees/TFDecider.Soldier.cs:38`, `FireReach 2000`: a Scattergun is fired at 2,000 u; only the Soldier's shotgun has a shorter reach (`:112`) |
| weapon choice | **Soldier-only** | `Hands` (`TFDecider.Soldier.cs:125-149`) knows the launcher and the Soldier's shotgun. In a lull a Scout asks every 1 s for a rocket launcher it does not own (`:137-141`); an empty Scattergun never switches to the Pistol (stock does: `tf_bot.cpp:3365-3375`) |
| own kit | **no** | `SelfState` (`src/ai/core/Snapshot/PerceivedView.cs:33-48`) carries the active weapon's catalog kind, clip and reserve. A kind is a weapon script (`Knowledge/TFKnowledge.cs:132-142`), so FaN, Back Scatter and Scattergun are one kind, and so are Bonk and Cola |
| own meters | **no** | none of AirDash, EnergyDrinkMeter, HypeMeter or ItemMeters reaches `SelfState`; `TFBotGameView.Hud` adds class, conditions and metal only (`Senses/TFBotGameView.cs:60-68`) |
| conditions | **too few** | `ActorConditions` is a byte with 6 bits used (`PerceivedView.cs:59-68`): Burning, Stunned, Invulnerable, Disguised, Cloaked, Taunting. No Phase, EnergyBuff, MadMilk, Bleeding, MarkedForDeath or SodaHype. `Ubered` is `IsInvulnerable` and does not include Phase (`TFActorFacts.cs:90`). Conditions are not recorded (no `Conditions` under `src/ai/core/Record`), so widening the type breaks no recording |
| double jump on routes | **yes** | `Nav/TFNavProfile.cs:71-76` (Scout → `JumpReach.Double`); the follower's second press, `src/ai/nav/Nav/NavFollower.cs:979-1010` |
| double jump in a fight | **no** | the fight hops once (`Trees/TFDecider.Fight.cs:136-141`); the applier presses `IN_JUMP` on a `Jump` intent (`src/ai/server/Apply/BotCommandApplier.cs:526-531`); decide period 6 ticks (`src/ai/core/Brain/DecideSchedule.cs:12`), so a release and second press over 2–4 decides fits in the 0.36 s rise (289/800) |
| alt-fire, ability one-shots | **yes**, generically | `ActionKind.AltFire` and `Ability` (`src/ai/core/Brain/IntentCommand.cs:52-67`); `AbilityRun.Command/Buttons/Sequence/Program` (`src/ai/core/Brain/Abilities.cs`); rows `Knowledge/TFAbilities.cs:50-66`; no Scout row |
| lob aim for jars and cleavers | **partly** | `AimLead` and `WeaponFacts.ProjectileGravity` (`src/ai/core/Knowledge/MapKnowledge.cs:44-46`); the TF2 table gives gravity per projectile (`TFKnowledge.cs:69`); not exercised for a thrown weapon |
| Medic first | **yes** | `MedicBonus 0.35` in `TakesTarget` (`Trees/TFDecider.Feel.cs:128,159-172`) |
| sentries | **avoid only** | every class keeps out (`Trees/TFDecider.Things.cs:101-102,172`, `SentryRange` + 100). No pass-through, so Bonk cannot override it |
| flank routes | **partly** | `ChokeMap` (`src/ai/core/Nav/ChokeMap.cs`, `BotMemory.Chokes`) and HL2MP's flank waypoint (`src/ai/hl2mp/server/Trees/Hl2mpDecider.cs:1621`, `FlankReach 512`); the TF decider uses CTF route families (`Trees/TFDecider.Families.cs`) but no flank waypoint |
| capture | **yes** | the Points branch stands on points (`Trees/TFDecider.Points.cs`); it does not weigh the ×2 |
| loadout | **per-slot uniform draw** | `src/tf2/server/Econ/BotInventory.cs:84-108`: 25 of 31 primaries are stock-behaviour Scatterguns, and the secondary is as likely to be a cleaver as a pistol. Nothing ties the slots together |

**Missing on the AI side:** the Scout's subtree, fight band and weapon choice; the kit read; own meters and the
extra conditions; the fight double jump; the drink, throw and alt-fire uses; a sentry pass while phased; a flank
route; the ×2 cap weight.

## 4. The design for the code lane

### 4.1 Archetypes to ship, in order
1. **Flanker** (stock). It fixes the general play first: band, reach, pistol, dodge.
2. **Bonk runner**, 3. **FaN brawler**, 4. **Milk combo**: the most-played alternatives in pubs, and the comp
   "passive Scout".
5. **BFB runner** (CTF), 6. **Cleaver harasser** (Payload), 7. **Soda aerialist**, 8. **Shortstop/Winger poker**.
9. The long tail. The Sandman waits on its ball and Crit-a-Cola on its mark.

### 4.2 Drawing an archetype, and the loadout that follows
- **The draw is per bot seed, at spawn or class change** (C.3.1). Weights by mode and side, as a table in the
  Scout's file. Start at Flanker 40, Bonk 20, FaN 20, Milk 20 (no source: a starting point; **measure** by
  objective time and tune).
  - CTF: BFB gets weight once it ships.
  - Payload attack: Cleaver gets weight.
  - A respawn after the bot itself saw an enemy sentry (`TFDecider.Things.cs` `Sentries`) raises Bonk by ×1.5,
    under C.4's change cooldown.
- **The loadout follows from the archetype.** Each archetype names its defindex per slot. A slot it does not name
  takes the **stock base group** of that slot, never a uniform draw over all works rows: a Flanker must not
  find a cleaver in its secondary.
- **Reskins for looks.** Within a base group, a seeded draw (§2's groups) picks the look, so C.3.3's looks can vary
  without changing play.
- **The pin.** `archetype=` pins it.
- **The game still decides what is allowed.** `ItemIsAllowed` binds (`src/tf2/server/Econ/TFLoadoutProvider.cs:369`; the plan cites `:198`, stale).
- **The fallback.** An archetype whose item is refused falls back to Flanker.

### 4.3 Decision rules (every number names its source)

**Common to every Scout archetype** (the Scout's fight, weapon choice and objective say):
- **Fight band.**
  - Scattergun-family primaries (Scattergun, FaN, Soda, BFB, Back Scatter): close in when over 350 u; hold 120–350
    u; never back off for self-blast.
    - Why: 175 % ramp under 512 u and 50 % at 1024 u ([Damage]); 105 point blank ([Community]).
    - 350: a **measure**, the distance where two shots stop killing a 200-HP Soldier at the port's ramp. Fit it
      with `tf_damage_disablespread` 1 in a fact over `TFDamageModifyRules`.
  - FaN: hold under 200 u. Knockback needs over 40 damage ([FaN]); the port's shove fades to none by 500 u
    (TSV line 41).
  - Shortstop: hold 200–600 u ([Basic]: reliable at longer ranges).
- **Fire reach.**
  - Scattergun family: 900 u. Past that a shot does under 30.
  - Pistol: 1,500 u.
  - Stock bots fire bullet weapons at any range "so they don't look dumb" (`tf_bot.cpp:3168-3171`); we do not.
- **Weapon choice.**
  - Primary when the target is within 600 u and the clip is above 0.
  - Pistol (or Winger, PBPP) when the target is over 600 u, or the primary clip is 0 and the Pistol has ammo.
    Stock switches on an empty primary at any range (`tf_bot.cpp:3365-3375`); 600 u is stock's 500 u bullet
    "desired range" (`tf_bot.cpp:3213`) plus a margin, a **measure**.
  - Reload in a lull, the Pistol first when both are down ([Community]).
  - Bat when both guns are dry and the target is within 100 u (stock melee desired range, `tf_bot.cpp:3190-3193`).
- **Retreat.**
  - To reload: with the primary clip at 1 or less and the Pistol empty or the target within 300 u. Stock retreats
    to reload a "barrage" weapon (the Scattergun included) at clip ≤ 1 on Hard and Expert
    (`behavior/tf_bot_tactical_monitor.cpp:266-281`, `tf_bot.cpp:3622-3640`).
  - For health: in a fight under 0.3 of max, out of one under 0.8, packs within 1,000 u
    (`behavior/tf_bot_get_health.cpp:14-16`, `tf_bot_tactical_monitor.cpp:296-307`). Today's `HurtShare` is 0.4
    (`TFDecider.cs:37`); the Scout's own pair replaces it.
  - Disengage below 50 HP against a target over 60 % (two Scattergun hits; [Community]'s 75 HP Scout duel line).
- **Targets.** Today's score plus:
  - Medic +0.35 (kept).
  - Sniper and Demoman +0.2 (comp main targets, [Scout comp]).
  - A target with no teammate within 600 u +0.15 ([Community]).
  - Below −0.3: a Heavy within 600 u facing the bot, or a Pyro within 350 u.
  - All but the Medic bonus are starting values to **measure** by kills per death.
- **Dodging (C.3.2's Scout row).**
  - With a sighted enemy whose view points at the bot within 12°, inside 1,200 u, the fight step's reversal
    becomes a double jump: a jump along the current side, a release decide, then a second press toward the
    other side on the first decide after the rise stops. Use the follower's `Rising → LetGo` test
    (`NavFollower.cs:993-1006`) as the model.
  - Hop share: 0.6 × step weight under 250 u; 0.1 at 250–600 u, grounded ([Basic]: close favours jumping, mid
    stays grounded).
  - Scale by Agility through the existing curve rows (`strafeWeight`, `strafeMeanSeconds`), plus a new `jumpUse`.
  - Never double-jump against a Soldier or Demoman inside 400 u in a corridor ([Scout comp]). A corridor is a
    cell with `SideRoom` walls on both sides (`TFDecider.Fight.cs:150-153`).
- **Routes.**
  - A flank waypoint, with chance `routeAltWeight` per approach: HL2MP's (`Hl2mpDecider.cs:1621`, 512 u aside),
    weighted away from `ChokeMap`'s top cells.
  - Avoid the Sniper long lines once the sight table has them (C.3.2).
- **Objective.**
  - The Points branch's weight ×1.3 for a Scout, from the ×2 cap: a **measure**, tuned by first-cap time.
  - Backcap: a lone Scout heads for an enemy-owned point the HUD shows as unguarded, when the team stance has
    failed for 20 s (**measure**).
  - Payload attack: stand on the cart (P8 owns the cart branch).
  - CTF: carry. A Scout's Fetch weight ×1.3 (**measure**).

**B. Bonk runner.**
- **Drink** (`scout_drink`) when all of these hold:
  - the drink meter is at 100 (stock `tf_bot.cpp:4593`);
  - the bot is not carrying the flag (`tf_bot.cpp:4566`; the game refuses it too, `TFLunchboxWeapon.cs:172-180`);
  - no target is within 300 u that the bot is winning (health share over the target's believed);
  - and one of:
    - a remembered enemy sentry's range (`SentryRange`) cuts the next 1,500 u of the route, the route's run through
      it is under 8 s × 400 u/s × 0.9 = 2,880 u, and the bot stands within 200 u of the range's edge;
    - a stickybomb thing lies within 300 u of the route's next choke;
    - an escape: health under 0.4, an enemy in sight within 900 u, and no cover within 300 u.
- **While phased:**
  - no fire and no weapon change (the game refuses both);
  - the sentry avoidance is off, so the route runs through the range;
  - no standing on a point it means to cap: it walks past or waits beside it until the phase ends, since phased
    players cannot cap (`TFCapturePlayerRules.cs:55`).
- **The slow after** (5 s; [Bonk], `TFPlayerConditionEffectArms.cs:84`): go to the nearest cover from the last
  sighted enemy or sentry, or to a health pack within 800 u.
- **Timing.** Phase about 9 s here (`TFLunchboxWeapon.cs:190-193`), 8 s per [Bonk]; **measure** it in a fact.
  Recharge 30 s at 3.3/s.

**C. FaN.**
- Fire only within 250 u.
- Reload only at a clip of 0, or in a lull at 1 (the lull's reload wastes the shell, but at no risk).
- Against an enemy within 150 u standing on a point, or between the bot and a drop of over 264 u (the safe fall,
  `TFNavProfile.cs:24-25`), fire for the knockback (**measure** the push direction in a fact).
- The force jump is phase 2: a `scout_force_jump` sequence for a nav class not built yet.

**D. Milk combo.**
- **Keep within 600 u of the team's Medic, when one is known** (B.4 teammate whereabouts). Flank share low:
  `routeAltWeight` × 0.3.
- **Throw Milk** (`scout_throw`, slot 2, lob) when the item meter is full (`ItemMeters`) and one of:
  - 2 or more enemies in sight within 200 u of each other, inside 900 u ([Milk]: 200 u splash);
  - the duel target inside 600 u while the bot's health is under 0.7;
  - a burning teammate within 600 u.
- **Basher.** Swing only at a target within 64 u, and never at a strafing Scout (hit_self_on_miss).

**E. BFB.**
- No air dash, unless a blast dodge (`TFDecider.Things.cs`) or a nav link needs one. The BFB loses 75 % of boost
  per air jump ([BFB]).
- Chip-damage the nearest enemy before a flag run, when the boost is under 50 and a target is inside 600 u.
- Fetch weight ×1.5 in CTF (**measure**).

**F. Soda.** Alt-fire (`ActionKind.AltFire`) on the Soda Popper when the HypeMeter is at 100 and either an enemy is
in sight within 800 u or the route's next link is a double-jump edge longer than one dash.

**G. Poker.**
- Chip a sentry: remembered, no Engineer seen within 10 s, line of sight from outside `SentryRange` + 100. Fire the
  Pistol or Winger (buildings take no falloff, [Damage]).
- Shove (`AltFire` on the Shortstop) a Pyro within 120 u, or an enemy on a point within 120 u.

**H. Cleaver.**
- Throw at targets at 300–1,500 u: a cart pusher, or a fleeing target with health under 0.5.
- Lob: aim from the projectile's gravity with `AimLead`. The game's evidence is a 10° pitch-down at 300 u (TSV
  line 222).
- Never at a building (the cleaver does no damage to buildings, [Cleaver]).

### 4.4 Ability rows (the Scout's block)
| row | run | what |
|---|---|---|
| `scout_drink` | Sequence | select the secondary (the lunchbox kind) → hold `IN_ATTACK` 2 ticks after the deploy. The game taunts, drinks and hands back (`TFLunchboxWeapon.Drink`). Refused by the game at a meter under 100 or with the flag |
| `scout_throw` | Sequence | select the secondary (milk or cleaver) → aim the lob → `IN_ATTACK` 2 ticks → reselect the primary |
| `scout_alt` | Buttons `IN_ATTACK2`, 2 ticks | Soda hype, Shortstop shove, Wrap Assassin and (later) Sandman ball, on the weapon in hand. `ActionKind.AltFire` may do without a row: the class lane should prefer `AltFire` if the counters can tell uses apart |
| `voice_*` | existing | the Medic call and the sentry callout already exist (`TFAbilities.cs:23`) |

The fight double jump is a move, not an ability: it is decided across decides in the Scout's file and needs no
core change. If the foundation wants it in the core, it would be `MoveKind.DoubleJump` with a second direction.

### 4.5 Facts to write (one behaviour each, each red once with a mutant)
- **Kit:** FaN 45 reads as FaN, not stock Scattergun; Bonk 46 is not Crit-a-Cola 163; each archetype's loadout
  holds its defindices, and its unnamed slots come from the stock group; an archetype whose item is refused falls
  back to Flanker.
- **Fight band:** a Scout with a Scattergun closes on a target at 500 u and does not back off at 200 u (red with the
  Soldier's band); it does not fire the Scattergun at 1,200 u.
- **Weapon choice:** an empty Scattergun with a target at 400 u selects the Pistol; a target at 900 u selects the
  Pistol; in a lull it never asks for a weapon it does not own (red with today's `Hands`).
- **Dodge:** watched within 12° at 300 u, a fight's reversals include double jumps, a second `IN_JUMP` press while
  airborne seen in the applied buttons; the share rises with Agility (flat mutant red); the hop share at 450 u is
  lower than at 200 u.
- **Bonk:** meter 99, no drink; carrying the flag, no drink; a remembered sentry across the route, a drink at the
  range's edge; phased, no fire and the sentry avoidance off; never standing on the point it means to cap while
  phased.
- **FaN:** it never reloads with 1 shell while a target is in sight.
- **Milk:** two enemies 150 u apart, thrown; one enemy at full bot health, not thrown; a burning teammate, thrown.
- **Cleaver:** the lob's pitch for 300 u, against the projectile's gravity; never thrown at a building.
- **Objective.** A Scout on a points level chooses the points stance more often than a Soldier of the same skills
  (the ×1.3 weight; flat mutant red).

### 4.6 Live measures (the foundation's harness: cp_granary, ctf_2fort; Scout pinned against a Soldier control)
1. **Objective time first** (user ruling 2026-11-15):
   - cp_granary: seconds from setup's end to the first friendly capture start, and to the first capture.
   - ctf_2fort: seconds to the first touch of the enemy flag, and captures per 10 min.
   - Expect the Scout earlier than the Soldier: 400 against 240 u/s is 0.6 of the walk time, before fights.
2. **Map progress:** points capped per 10 min, and the cap share the Scout bots contribute.
3. **Class numbers:** median engage distance (Scattergun fire under 350 u); the Pistol's share of shots past 600 u;
   hits taken per enemy shot (`src/ai/server/Senses/AimMeter.cs`), dodging on against off; double jumps per fight
   minute; deaths to sentries.
4. **By archetype, each should differ from Flanker on its own axis:** Bonk, drinks, sentry ranges entered phased
   and left alive, and first-cap time on a sentry-held point; FaN, knockbacks and median fire distance under 200 u;
   Milk, health healed by milk and median distance to the own Medic; BFB, flag carry seconds and mean speed;
   Cleaver, hits per throw.
5. **The jank score** (`tools/ai-replay jank`) stays inside the human envelope with the double jump on.

### 4.7 What the Scout needs from the P7 foundation
1. **A class file and a ready flag.** The Scout's own file (p7f's plan is `src/ai/tf2/server/Classes/TFScout.cs`)
   holds its weapon choice, fight band, fire reach and one-shots, a Survive threshold pair, an objective-weight
   say, a route-cost or flank hook, and the class's ready flag.
2. **Override points in the general play.** The Soldier constants that today bind every class, settable per class:
   - `FightNear/FightFar` (`TFDecider.cs:46`) and `FireReach` (`TFDecider.Soldier.cs:38`);
   - the launcher re-select in `Hands` (`TFDecider.Soldier.cs:137-141`);
   - `AvoidsSentries` (`TFDecider.Things.cs:101-102`), per decide, so a phased Scout can pass.
3. **The kit read.** Per slot: defindex, `item_class`, attribute classes, and `set_weapon_mode`'s value. Plus a way
   to select a slot by its kit row, not only by catalog kind (`Select` takes a kind,
   `TFDecider.Soldier.cs:151-155`).
4. **The HUD's own numbers in `SelfState`:**
   - `AirDash` (on `TFActorFacts` already: `TFActorFacts.cs:41`);
   - `EnergyDrinkMeter` and `HypeMeter` (`TFPlayerNetState.cs:336,459`);
   - the per-slot item meters (`:672`);
   - secondary-slot clip and reserve, since the Pistol's ammo is needed while holding the Scattergun.
5. **Conditions widened past the byte** (`PerceivedView.cs:59-68`; not recorded, so safe):
   - own: Phase, EnergyBuff, SodaHype, MarkedForDeath, Bleeding;
   - drawn on others: MadMilk, Jarate, Bleeding, Marked, Phase (Bonk's visual), Slowed;
   - a client draws all of these.
6. **The archetype draw and a coherent loadout.** The stock base group fills an unnamed slot, so a Scout's
   secondary is the Pistol and not a uniform pick (`BotInventory.cs:101`).
7. **The class pick.** A Scout weight in the composition by mode: attack CP, Payload and KOTH 1–2 per 12; CTF 2;
   defence 0–1 (comp swaps Scouts out on defence, [Scout comp]). Stock for comparison: offence Scout limit 3 at
   every difficulty (`tf_bot.cpp:849`); comp roster 0 at Easy and Normal, 2 at Hard and Expert (`:864`); the class
   lists at `tf_bot.cpp:1026-1070` put a Scout 6th of 12 on offence and 5th on defence.
8. **Harness counters.** Drinks, throws, alt-fires, double jumps, and the median engage distance per weapon.

### 4.8 Where the brief and the plan are out of date
- **The plan's sub-class status column is stale.** C.3.1's Scout rows say Bonk and the Sandman/FaN are "not
  found" (`docs/plans/plan_bots_teams_tf2.md:718-719`). Today Bonk and FaN work. The Sandman's attributes work but
  its ball does not exist.
- **`works` overstates two Scout items.** The Sandman (no ball) and Crit-a-Cola (no mark for death) are `works` in
  the TSV.
- **The F12 prework is only half done.** C.3.2's Scout row needs it (`AirDash` on the actor facts): the game side
  has it (`TFActorFacts.cs:41`), but it never reaches the core's `ActorFacts` or `SelfState`.
- **The plan's evade map does not exist.** `ss_ai_tf_evade` is not in `TF_LINKED_MAPS` (`Makefile:852`).

## 5. Sources

**Web** (fetched 2026-11-20), all `https://wiki.teamfortress.com/wiki/` unless a full URL is given:
[Scout] `Scout` · [Community] `Community_Scout_strategy` (both halves: weapons, modes, co-op) ·
[Basic] `Basic_Scout_strategy` · [Scout comp] `Scout_(competitive)` · [6s] `6s` (search summary) ·
[Highlander] `Highlander`, plus a search over https://www.pokecommunity.com/threads/1-9-highlander-scout.522213
(the truetf2 wiki pages answered 403 and were not read) · [Damage] `Damage` · [Bonk] `Bonk!_Atomic_Punch` ·
[Cola] `Crit-a-Cola` · [Sandman] `Sandman` (2–7 s slow, moonshot 0.8 s, 10 s recharge, −15 HP) ·
[FaN] `Force-a-Nature` · [BFB] `Baby_Face%27s_Blaster` · [Atomizer] `Atomizer` (no self-damage on the triple jump
since 2017-10-20) · [Milk] `Mad_Milk` · [Cleaver] `Flying_Guillotine`.

**Stock TFBot** (`../source-sdk-2013-valve/src/game/server/tf/bot/`):
- `tf_bot.cpp`: rosters `:849,864,1026-1070`; threat danger `:3075-3100`; attack ranges `:3128-3215`; weapon choice
  `:3253-3375`; hitscan `:3520-3555`; barrage `:3622-3640`; item use `:4512-4612` (drink gate `:4593`, flag
  `:4566`, ball `:4602-4609`).
- `behavior/tf_bot_tactical_monitor.cpp:266-307`; `behavior/tf_bot_get_health.cpp:14-17`;
  `behavior/tf_bot_scenario_monitor.cpp:160` (Scouts wander in raid); `behavior/medic/tf_bot_medic_heal.cpp:426-437`;
  `tf_bot_vision.cpp:158-217`. There is no `behavior/scout/` directory: stock has no Scout behaviour of its own.

**Stock shared** (`../source-sdk-2013-valve/src/game/shared/tf/`): `tf_player_shared.cpp:113,172-174,4643-4653`;
`tf_weapon_bat.cpp:107-108,196`; `tf_weapon_lunchbox.h:32`; `tf_weaponbase.h:628-656`; `tf_weapon_jar.h:59,120`;
`tf_weapon_bat.h:91`.
