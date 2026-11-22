# P7 research: the Heavy (2026-11-20, at main `225fe2e96`)

The design brief for the Heavy's code lane. Game knowledge cites a URL (list in §5). Repo claims cite `path:line` at
`225fe2e96`, and stock claims cite `../source-sdk-2013-valve` (`valve-sdk.lock`). Where a number is mine and not a
source's, it says **[pick]** and names the measurement that should settle it.

## 1. The Heavy's job, by mode

**The body.** 300 HP, 450 with overheal. He runs at 230 u/s (77%), 110 u/s with the gun spun up (37%), and 44 u/s
spun up with the Brass Beast (15%) [W-Heavy]. The port has the 110 cap at `src/tf2/shared/Movement/TFMaxSpeed.cs:124`,
and the Brass Beast's 0.4 aiming scale goes through `TFMaxSpeed.cs:137`. The minigun takes 0.75 s to spin up
(`src/tf2/shared/Weapons/TFMinigunBehaviour.cs:58`). Its damage starts at half and ramps to full over the first
second spun (`:24`, stock `tf_weapon_minigun.cpp:996-1013`), and its spread tightens over the same second. It spins
down 1.15 s after the trigger is let go [W-Minigun]. Each 0.105 s it fires 4 bullets of 9 damage, and 200 rounds last
about 20 s [W-Minigun]. Hitscan damage falls off by distance around an optimal 512 u: 150% at point blank and 50% at
1024 u and beyond (stock `tf_gamerules.cpp:6548-6566`). The wiki's figures are 475-513 dps point blank, 48-285 dps
at mid range and 48-95 dps at long range [W-Minigun].

**In every mode, what good players do** [W-Comm], [W-Basic], [W-Comp]:
- **Where he stands, at what range.** At the front of his team's group, on the objective or the choke. He fights
  inside about 512 u, where the falloff and the spread favour him. Past about 1000 u he fires short bursts only, at
  slow or still targets (Engineers, Snipers, Medics), and otherwise closes in or lets others take the fight.
- **Target priority.** First whatever threatens his Medic and him up close: Scouts, Pyros, Spies, Soldiers and
  Demomen inside his range. A Medic or Sniper in his range is free damage. He leaves sentries to Soldiers, Demomen
  and Spies (stock agrees: `CTFBotDestroyEnemySentry::IsPossible` refuses the Heavy,
  `bot/behavior/tf_bot_destroy_enemy_sentry.cpp:161-169`).
- **Push, hold, retreat.** He pushes with a Medic, and harder with overheal or an Über. He holds a choke or a point
  with the gun pre-spun when enemies are expected. He retreats early, at about 125 HP ±25 as a beginner's rule
  [W-Comm], because he is too slow to escape late.
- **Movement tech.** He spins up in the air: he jumps round a corner and lands spinning [W-Comm]. He keeps moving
  and crouch-jumps against Snipers [W-Comm]. He travels unspun, or on the GRU or the Eviction Notice. He keeps the
  gun spun only where a fight is expected, because spun he moves at 37%.
- **Teamwork.** The Heavy and Medic pair is "one of the strongest combinations" [W-Comm]. He walks in front of the
  Medic, absorbs projectiles for him, and drops his Sandvich for him. He sits by the Engineer's dispenser (ammo) and
  rides the payload cart (a moving dispenser [W-Comm]). Stock's Medic bot ranks the Heavy first among patients
  (`bot/behavior/medic/tf_bot_medic_heal.cpp:60-63`).
- **What beats him and how he plays around it.**
  - Snipers: a charged headshot kills him outright. He breaks their lines, jumps and crouches, and keeps out of long
    sightlines.
  - Spies: he checks his back before spinning up and eats with his back to a wall. Minigun spread reveals a cloaked
    Spy. The Huo-Long ring burns Spies.
  - Focused fire, crossfire and projectile spam: his size and speed make him easy to focus [W-Anti]. He backs off a
    sentry's range unless he has an Über.
  - Kiting Scouts [W-Anti]: Natascha's slow and the Family Business answer them.
  - Eating: he is "defenceless while eating" [W-Anti], so he eats behind cover only.
  - Another Heavy fresh off a spin-up: the 50% first-second penalty means a Heavy rounding a corner unspun into a
    spun Heavy loses. He pre-spins, or avoids the corner [W-Comm].

**By mode.** P8 builds the mode goals; this lists what a Heavy does in each.

| Mode | Pub | Competitive |
|---|---|---|
| CTF (2fort) | defends the intel room and the bridge; on attack, escorts a carrier or holds the enemy's courtyard exit with a Medic; never the carrier by preference (his speed) | not played |
| 5CP (symmetric) | mid fight with a Medic; holds a point he has taken; the last-point anchor | 6s: "situational"; rarely starts a push (his speed costs the mid fight); most often on **last-point defence**, swapped in for a Scout or Soldier; also a pocket instead of a pocket Soldier [W-Comp] |
| A/D CP (attack) | front of the push with a Medic; spins at the choke leading to the point; stands on the point | Highlander: the team's one Heavy is the Medic's main patient and the push's tank; Heavy plus Medic on an Über leads the push |
| A/D CP (defence) | holds the choke or the point, pre-spun, by the Engineer's nest; Brass Beast or Natascha | defence "comes to him, so he can keep his Minigun spun" [W-Comp] |
| KOTH | holds the point; the Huo-Long suits close quarters [W-Comm] | 6s and Highlander: a hold class on the point |
| Payload (attack) | pushes the cart: ammo and health from it, and a Heavy adds capture rate; Fists of Steel for escorting [W-Comm] | Highlander stopwatch: escorts the cart with the Medic |
| Payload (defence) | holds the chokes on the cart's path; Brass Beast or Huo-Long by a nest | the same |

**Beginner mistakes that make a bot read as a bot.** Each one breaks a rule in §4.
1. Firing at 2000 u. Today `FireReach` is 2000 (`src/ai/tf2/server/Trees/TFDecider.Soldier.cs:38`); at that range
   the gun does its floor damage and gives away his place.
2. Spinning only on sight. The first second is at half damage, so he loses the first exchange at every corner.
3. Backing off a target at 320 u. That is the launcher's self-blast rule (`TFDecider.cs:46`, `:490`) and the
   opposite of a Heavy's.
4. Eating with an enemy in sight, or at full health. Stock TFBot eats whenever the lunchbox has ammo:
   `CTFBot::OpportunisticallyUseWeaponAbilities`, `tf_bot.cpp:4587-4598`.
5. Walking spun across open ground, and stopping in Sniper lines.
6. Running out of ammo and standing there. Today nothing seeks ammo (`grep ItemKind.Ammo src/ai/tf2` finds nothing).
7. Pushing alone ahead of his Medic, or leaving him behind.
8. Spinning up with a Spy at his back.

## 2. Play styles by weapon set

**The Heavy's works rows** in `game/mod_tf2/scripts/items/items_working.tsv` (status column 8) number 67. By the
table's own slot column they are 46 primary (34 minigun and 12 shotgun), 9 secondary and 12 melee. The 12 shotguns
are misfiled for the Heavy, as explained below. Attribute values come from
`bin/debug/content/tf/scripts/items/items_game.txt` (item blocks and their prefabs, read this session). Values in
brackets are the wiki's [W-Heavy].

**Primary (`tf_weapon_minigun`)**
- **Stock group** (no behaviour attributes), 29 rows: 15 Minigun, 202 Upgradeable, 298 and 1206 Iron Curtain, 654
  Festive, 850 Deflector, the Botkillers 793 802 882 891 900 909 958 967, and the skins 15004 15020 15026 15031
  15040 15055 15086 15087 15088 15098 15099 15123 15124 15125 15147. The Deflector's MvM projectile attribute is not
  in the row, so it behaves as stock here.
- **41 Natascha.** `mult_onhit_enemyspeed` 1.0 (always slows), `mult_dmg` 0.75, `mult_minigun_spinup_time` 1.3,
  `spunup_damage_resistance` 0.80 (20% resistance below 50% HP while spun).
- **312 Brass Beast** (prefab `weapon_gatling_gun`). `mult_dmg` 1.2, spin-up 1.5, `mult_player_aiming_movespeed` 0.4,
  resistance 0.80.
- **424 Tomislav** (prefab `weapon_tomislav`). Spin-up 0.8 and `minigun_no_spin_sounds` 1. The row also names
  `mult_postfiredelay` and `mult_spread_scale` [20% slower fire, 20% more accurate].
- **811 and 832 Huo-Long Heater** (prefab `weapon_long_heatmaker`). `ring_of_fire_while_aiming` 12,
  `uses_ammo_while_aiming` 4 (4 rounds/s while spun), `mult_dmg` 0.9, `mult_dmg_vs_burning` 1.25.

**Secondary**
- **Shotgun:** 11 (`TF_WEAPON_SHOTGUN_HWG`).
- **425 Family Business** (prefab `weapon_russian_riot`): `mult_dmg` 0.85, `mult_clipsize` 1.33,
  `mult_postfiredelay` 0.85.
- **Misfiled shotguns.** The multi-class shotgun rows 199 1141 15003 15016 15044 15047 15085 15109 15132 15133 15152
  and 1153 Panic Attack carry `primary` in the table's slot column. That is the item's default `item_slot`, but
  `used_by_classes` gives the Heavy `secondary` (prefab `weapon_shotgun_multiclass`). See §3.
- **Sandvich group** (`tf_weapon_lunchbox`, mode by `set_weapon_mode`, `src/tf2/shared/Weapons/TFLunchboxBehaviour.cs:30-37`):
  42 Sandvich (mode 0), 863 Robo-Sandvich (3) and 1002 Festive (4), which all eat as the plain Sandvich.
- **159 Dalokohs Bar** (mode 1) and **433 Fishcake** (mode 7): the same arm (`TFLunchboxWeapon.cs:98`).
- **311 Buffalo Steak Sandvich** (mode 2): `energy_buff_dmg_taken_multiplier` 1.2.
- **1190 Second Banana** (mode 6): `lunchbox_healing_scale` 0.67, `charge_recharge_rate` 1.5.

**Melee (`tf_weapon_fists`)**
- **Stock group:** 5, 195 and 587 Apoco-Fists (`crit_kill_will_gib` only: gibs on a crit kill).
- **43 Killing Gloves of Boxing (KGB):** `add_onkill_critboost_time` 5, `mult_postfiredelay` 1.2.
- **239 GRU** (prefab `weapon_gru`; `set_weapon_mode` 2, `mult_switch_from_wep_deploy_time` 1.5, and per the row
  `mult_player_movespeed_active` and `mod_maxhealth_drain_rate`) [+30% speed active, max HP drains to a floor of
  100, holsters 50% slower]. Its reskins are 1084 Festive GRU and 1100 Bread Bite (plus
  `breadgloves_properties`, named nowhere in `src/tf2`).
- **1184 GRU MvM:** carries `self_mark_for_death`. Never draw it for a bot (§4.2).
- **426 Eviction Notice:** `mult_dmg` 0.4, `mult_postfiredelay` 0.6, `speed_boost_on_hit` 3; active speed and drain
  per the row.
- **310 Warrior's Spirit:** `mult_dmg` 1.3, `mult_dmgtaken` 1.3, `heal_on_kill` 50.
- **331 Fists of Steel:** `dmg_from_ranged` 0.6, `dmg_from_melee` 2.0, holster 2.0, and per the row overheal and
  healer penalties.
- **656 Holiday Punch:** a crit makes the victim laugh, crits from behind always, and crits do no damage.

**The archetypes, and what each changes in the bot's decisions** (names from [W-Comm], [W-Comp] and the plan's
C.3.1 rows, `docs/plans/plan_bots_teams_tf2.md:731-734`):

| # | Archetype | Kit (primary / secondary / melee) | Why people run it | What changes in the bot |
|---|---|---|---|---|
| A | **Standard** (the plan's "Pocketed") | stock group / Sandvich group (Shotgun as the stock fallback) / stock or KGB | the reliable default; the Sandvich is a 300 HP self-heal and a drop for the Medic | baseline rules (§4.3); eats behind cover; drops food for a hurt Medic |
| B | **Ambusher** | Tomislav / Sandvich or Family Business / KGB or GRU | 20% faster spin-up and no spin sound: corner ambushes; "use it on slow targets rather than fast Scouts" [W-Comm] | pre-spins at every believed-enemy corner (silent, so no giveaway); spins down sooner between fights; fights a little further out (tighter spread); prefers slow targets |
| C | **Anchor** | Natascha / Sandvich or Shotgun / stock or Fists of Steel | slows the target, which also stops charges and jumps; 20% resistance under half health; "support" [W-Comm] | holds a choke near the Medic; ranks mobile targets (Scout, Soldier mid-jump, a Demoknight) first; fights a little further out because the slow lands at range; never chases far |
| D | **Siege** | Brass Beast / Sandvich or Shotgun / stock | +20% damage; best "defending chokepoints near a Dispenser" [W-Comm] | picks a spot (point, choke, dispenser) and stays there; spins long; travels unspun; Shotgun for targets past its spun reach |
| E | **Steak brawler** | stock or Tomislav / Buffalo Steak / GRU, Eviction Notice, Warrior's Spirit or KGB | 16 s of mini-crits and +30% speed, melee-locked; early-round rushes [W-Comm] | eats the Steak at a push or before a reachable close target, then charges on melee for 16 s; never against a sentry or a spun Heavy |
| F | **Mobile** (GRU Heavy) | stock or Tomislav / Dalokohs or Sandvich / GRU or Eviction Notice | gets to the fight 30% faster | travels on the melee when nobody has been seen for a while; switches to the gun on contact (the slow holster counts) |
| G | **Burner** | Huo-Long / Sandvich / stock | the ring of fire burns Spies and close attackers; "near Dispensers"; KOTH and Payload [W-Comm] | stays near ammo (dispenser, cart, packs); spins only with an enemy expected, because it costs 4 rounds/s |

The long tail, after A-G: Holiday Punch (punish an Über's healing), Fists of Steel escort (a Payload push under
fire), Second Banana (alone behind lines), Family Business plus GRU (roaming), Warrior's Spirit with a Kritzkrieg,
and Panic Attack. They need the same hooks.

## 3. What the port and the AI can do today

**Game side (`src/tf2/`): almost everything the archetypes need is ported.**
- **Minigun.** The spin state machine, damage and spread ramps, dry fire, item spin-up, spun drain and ring of fire
  are all in `src/tf2/shared/Weapons/TFMinigunBehaviour.cs:13-300`, with the hooks at `TFWeaponHooks.cs:270-282`.
  Measured live 2026-11-10 in the minigun row's evidence (the tsv's row 15). The aiming speed is at
  `TFMaxSpeed.cs:124,229-233`.
- **Natascha.** The slow is at `src/tf2/server/Combat/TFDamageLayer.cs:719-730`. The spun resistance
  (`spunup_damage_resistance`) is at `src/tf2/server/Combat/TFDamageAliveRules.cs:167`.
- **Huo-Long.** The bonus against burning targets is at `TFDamageAliveRules.cs:140`. The ring is at
  `TFWeaponHooks.cs:282` and `src/tf2/server/Combat/TFBurn.cs`.
- **Lunchbox.** Eat through the taunt (`src/tf2/server/Weapons/TFLunchboxWeapon.cs:69`), the bite's modes
  (`:86-128`) and the throw as a health kit (`:202`). The Steak's mini-crits and speed are at
  `TFLunchboxWeapon.cs:100-105` and `TFMaxSpeed.cs:347-355`; its damage taken is at `TFDamageAliveRules.cs:107`.
  The Banana's scale is applied in `TFLunchboxWeapon.Bite` (`TFLunchboxWeapon.cs:122`). The HUD's item meter
  (`src/tf2/client/Ui/TFHudItemMeter.cs`) shows the recharge.
- **Melee.**
  - KGB: `src/tf2/server/Player/TFDeathDrops.cs:305-323`.
  - GRU and Eviction drain: `src/tf2/server/Player/TFPlayerResupply.cs:302-320`.
  - Eviction speed on hit: `TFDamageLayer.cs:959`.
  - Warrior's Spirit heal on kill: `TFDamageLayer.cs:1048`.
  - Fists of Steel: `TFDamageAliveRules.cs:190`.
  - Holiday Punch: `TFWeaponHooks.cs:294,327`.
  - Holster time: `TFWeaponHooks.cs` (`mult_switch_from_wep_deploy_time`).
- **Medigun, Über and overheal.** `src/tf2/server/Medigun/`. The friendly Medic's ÜberCharge line on the target ID
  is in `src/tf2/ui/screens/TFTargetId.cs:157`.

**Game-side gaps**
1. **The Steak's melee lock is not enforced.** `TF_COND_CANNOT_SWITCH_FROM_MELEE` is added
   (`TFLunchboxWeapon.cs:104`), but stock enforces it in `CTFWeaponBaseMelee::CanHolster`
   (`tf_weaponbase_melee.cpp:86-93`), and no TF melee behaviour overrides `CanHolster`
   (`grep -rn "override bool CanHolster" src/tf2` finds nothing; the core's default `true` is at
   `src/shared/managed/WeaponBehaviour.cs:256`). A Steak player can go back to his minigun with mini-crits. The bot
   lane should not rely on this, and it needs a port stage of its own.
2. **`breadgloves_properties`** (1100 Bread Bite) is named nowhere in `src/tf2`. The row says `works`; if the
   attribute carries behaviour, the row overstates.
3. **Spun-up jump.** The wiki says a spun Heavy cannot jump [W-Minigun]. This SDK's `CTFPlayer::CanJump`
   (`tf_player_shared.cpp:12276-12293`) asks only for taunting, the weapon's `OwnerCanJump` (the minigun keeps the
   base `true`, `tf_weaponbase.h:339`) and `no_jump`. I found no stock gate. Measure it in the port before the AI
   assumes either way. The design below plans no jump while spun regardless.
4. The TF2 gate's `classes` step is already red with `heavy_speed=unmeasurable` (`plan_bots_teams_tf2.md:1509`).
   This is not the Heavy lane's to fix, but the Heavy's live speed numbers should not be trusted until it is green.

**The loadout draw, today** (`src/tf2/server/Econ/BotInventory.cs`)
- **Slot bug.** The draw buckets rows by the table's slot column (`BotInventory.cs:152`, `cols[3]`), which is the
  item's default slot, not the class's. For the Heavy, the 12 multi-class shotguns land in the primary pool. When one
  is drawn, `PlayerInventory.Equip` refuses it (`src/tf2/server/Econ/PlayerInventory.cs:120`, the class's slot is
  secondary), `BotInventory.cs:107` ignores the refusal, and `ItemInLoadout` falls back to the base minigun
  (`PlayerInventory.cs:144-147`). So 12 of 46 primary draws are silently stock, and those 12 shotguns, Panic Attack
  among them, can never be drawn as the Heavy's secondary. The same bug hits the Soldier, Pyro and Engineer. The
  fix is to bucket by `ItemDefinition.Slot(cls)` (the schema's `used_by_classes`, `src/tf2/shared/Econ/SchemaParser.cs:349-371`).
- **Today's odds** (uniform over rows, `BotInventory.cs:183-190`):
  - Primary: Natascha, Brass Beast and Tomislav 1/46 each, Huo-Long 2/46, stock behaviour 41/46.
  - Secondary: a lunchbox 7/9, of which Sandvich behaviour 3/9, Dalokohs 2/9, Steak 1/9, Banana 1/9; Shotgun 1/9;
    Family Business 1/9.
  - Melee: 1/12 each, the MvM GRU among them.
- **The plan's flagged-family rule is not built.** The plan says "a flagged family a brain does not understand is
  not drawn for that bot until its subtree reads it" (`plan_bots_teams_tf2.md:777-781`), but today every lunchbox
  is drawn while no brain reads one.
- **Stale plan cell.** The plan marks Natascha "not found" (`plan_bots_teams_tf2.md:732`); row 41 is `works`.

**AI side (`src/ai/tf2/server/`): no Heavy play at all.**
- **Class pick.** Every bot is a Soldier (`src/ai/tf2/server/TFBotClassPick.cs:22`), and every class runs the
  Soldier's subtree (`src/ai/tf2/server/Trees/TFClassTrees.cs:15`).
- **Spin-up row.** `spin_up` exists: `IN_ATTACK2` for 60 commands (`src/ai/tf2/server/Knowledge/TFAbilities.cs:40,52`;
  0.9 s at 66.7 ticks/s, short of Natascha's 0.975 s and the Brass Beast's 1.125 s). No code reads it
  (`grep -rn SpinUp src/ai/tf2` finds only the row).
- **Fire.** The hands hold `Fire/Spray` on a sighted target within 2000 u (`TFDecider.Soldier.cs:92-119`). For a
  Heavy this means a spin-up on every first sight, a spin-down 1.15 s after losing sight, and fire at any range.
- **Range.** The fight ranges are the launcher's (`FightNear` 320 and `FightFar` 900, `TFDecider.cs:46`), and
  `FightAtRange` backs off inside 320 (`TFDecider.cs:488-498`).
- **Weapon swaps.** The swap logic names the Soldier's launcher and shotgun by script name (`TFDecider.cs:116-117`,
  `TFDecider.Soldier.cs:129-139`). A Heavy has neither, so `_launcher` is -1 and the Heavy never changes weapon: no
  Shotgun when the minigun is dry, no melee, no lunchbox.
- **Ammo.** No ammo-seeking. Survive heads only for health and resupply (`TFDecider.cs:372-377`, `HealPlace`). Health
  works out: `HurtShare` 0.4 of 300 is 120 HP (`TFDecider.cs:37`, `:348-362` reads the class table's 300), which
  matches the wiki's 125±25.
- **The kit.** The bot cannot read its kit. `WeaponKnowledge` is per script kind (`tf_weapon_minigun`), built
  without item attributes (`src/ai/tf2/server/Knowledge/TFKnowledge.cs:118-143`), so Natascha and the stock gun are
  the same kind. `SelfState` has no item, no spin state and no item meter (`src/ai/core/Snapshot/PerceivedView.cs:33-51`).
- **Medic awareness.** There is an inference only: health rising in small steps with a friendly Medic within 540 u
  (`src/ai/tf2/server/Trees/TFDecider.Voice.cs:42-49,128-152`), used for "Thanks!". Nothing reads Über percent,
  though the target ID would show it (`TFTargetId.cs:157`).
- **What exists and can be reused.**
  - The Medic call under 50% (`TFDecider.Voice.cs:33`).
  - The danger places, for Sniper lines (`TFDecider.Danger.cs`).
  - The prefire's belief of where a lost enemy is (`TFDecider.Prefire.cs`), the source for a pre-spin.
  - The rally and push (`TFDecider.Rally.cs`).
  - The plan's line "Heavies spin up at the corner" (C.6.1) is not built.
  - `ActionKind.AltFire` held is pressed as is (`src/ai/server/Apply/BotCommandApplier.cs:619-623`). With an Actor
    or CornerCheck look it waits for the eye (`:385`), so a pre-spin should use a path or point look.

**Missing, by side**

| Need | Game | AI |
|---|---|---|
| spin before contact, hold the spin, spin down | done | a spin policy (none) |
| a fight range by weapon (inside 512 u, bursts past it) | done | ranges are the launcher's |
| ammo: seek packs, the dispenser, the cart; Shotgun when dry | done | none |
| eat, throw the plate, the Steak, the Dalokohs | done, except the Steak lock (gap 1) | select-and-fire on a lunchbox; a kit read; the meter |
| know the kit (defindex, attributes, spin time) | schema has it | no kit read |
| a coherent loadout per archetype | slot bug, draw per slot | no archetype draw |
| Medic pocket: whose heals, Über % | target ID, beam | a heal-source read, an Über read |
| GRU travel, melee switching, KGB crits | done | no melee logic for a non-Soldier |

## 4. The design for the code lane

### 4.1 Ship order
1. **A Standard.** The stock primary, the Sandvich group, and stock fists or the KGB. This is the base the other
   archetypes override.
2. **B Ambusher** (Tomislav).
3. **C Anchor** (Natascha).
4. **D Siege** (Brass Beast).
5. **E Steak brawler.** It needs melee play and an answer to game gap 1.
6. **F Mobile.**
7. **G Burner.**
8. The long tail.

Steps 1-4 differ mainly in primary and spin policy over one set of hooks, which is why they go first.

### 4.2 Drawing an archetype, and the loadout from it
- **Per bot, at spawn as a Heavy,** draw an archetype by the seed (`BotInventory.SeedOfName`,
  `BotInventory.cs:68-81`) from weights by mode and role (C.4, C.6) **[pick]**:
  - attack: A 4, B 3, C 2, E 2, F 2, D 1, G 1;
  - defence: A 3, D 3, C 3, G 2, B 2, E 0, F 0.
  - Then scale by skill. Ambusher weight ×(1 + Cunning/10). Anchor weight ×(1 + Teamwork/10). C.5 makes Nerve the
    Heavy's favourite, so Siege is ×(1 + Nerve/10).
- **The draw holds** until the class or the map changes (C.3.1).
- **The archetype names a preference list per slot** (C.3.1's `tf2_subclasses.json`), each a set of defindexes from
  §2's groups. Within a group the draw is by seed across the reskins (looks only), so 29 stock miniguns still show
  variety. A slot the archetype leaves open is drawn from that slot's works set as today, minus the exclusions.
- **Exclusions from every bot draw:**
  - `self_mark_for_death` (1184), alongside `BarsTheObjective` (`BotInventory.cs:148`);
  - `crit_does_no_damage` (656) unless the archetype names it;
  - families no subtree reads yet. The lunchbox stays excluded until step 1's eat lands; then Standard names it.
- **The draw goes through the player's path** (`TFLoadoutProvider`, `ItemIsAllowed`). `BotInventory` must check
  `Equip`'s answer and bucket by `Definition.Slot(cls)` (the slot bug in §3). That is its own commit with a
  regression fact (§4.5).

### 4.3 Decision rules (Standard), with sources
- **Spin hold.** Hold the spin (`AltFire`, held) while any enemy was seen within the last 3 s and ammo is above 25%
  (stock: `bot/behavior/tf_bot_behavior.cpp:1284-1291`, `spinTime = 3.0f`, `!IsAmmoLow()`, not hurrying). Don't
  hold it while carrying the flag or on a "hurry" leaf (Deliver, Course, GoHeal).
- **Pre-spin.** Pre-spin when the route's next corner is within 300 u **[pick]** and the bot believes an enemy
  within 800 u **[pick]** past it (the prefire's belief), or on entering a danger place from `TFDecider.Danger.cs`.
  Start at least `spinUpTime(item)` before the corner: 0.75 s × the item's `mult_minigun_spinup_time`.
- **Spin-down.** Spin down after 1.5 s **[pick]** with nobody seen and no belief within 800 u, so he travels at
  230 rather than 110.
- **Fire.** Fire full inside 1024 u (the falloff floor, `tf_gamerules.cpp:6548-6566`). Between 1024 and 1600 u
  **[pick]** fire bursts of 0.5 s every 1.5 s **[pick]** at targets moving under 100 u/s (a Sniper, Engineer, Medic
  or a Heavy). Past 1600 u don't fire. Measure the hit rate by range with an `ai-aim-range`-style run (its TF2 form
  is a measurement to make).
- **Range.** The preferred band is 250-550 u (stock's desired attack range for spray weapons is 500,
  `tf_bot.cpp:3214`). Close in when further than 550 and spun or near cover. **Never back off a player target**
  for being near (replaces `FightNear` for the Heavy). Back off a sentry seen within its range (`TFKnowledge.SentryRange`)
  unless invulnerable (stock: the Heavy doesn't take on sentries, `tf_bot_destroy_enemy_sentry.cpp:161-169`).
- **Target priority.** Score sighted enemies by `threat × reach`:
  - the invulnerable first (stock danger 1.0, `tf_bot.cpp:3074-3076`);
  - then by class, stock's `GetThreatDanger` (`tf_bot.cpp:3078-3097`): Pyro 1.0, Soldier 0.8, Heavy 0.8, Scout 0.6,
    Demoman 0.6, Spy 0.6, Engineer 0.4, Sniper 0.4, Medic 0.2;
  - × 1.5 for a target within 512 u, × 1.3 for a target attacking his Medic **[pick]**.
- **Ammo.** Under 50 of 200 rounds (25%) with nobody sighted for 1 s, go for the nearest ammo pack, friendly
  dispenser or resupply within 1500 u **[pick]**. Under that and dry in a fight, switch to the Shotgun or Family
  Business if carried, else to melee. When the fight ends, switch back to the minigun (once a second, as `Hands` does).
- **Retreat.** Keep `HurtShare` 0.4 (120 HP; [W-Comm] says 125±25). A pocketed Heavy (below) retreats at 0.3 instead
  (stock's critical ratio, `bot/behavior/tf_bot_get_health.cpp:14`).
- **Eat (Sandvich group).** Eat when health is under 55% **[pick]**, nobody has been seen for 1.5 s, nobody is
  believed within 600 u, and the item meter is full. First go to the nearest cover cell within 256 u, back to the
  wall **[pick]** ([W-Comm]: "eat behind cover", "back to a wall"). It is two decides: `SelectWeapon(lunchbox)`, then
  `Fire/Tap`. The taunt holds the bot about 4.3 s [W-Heavy]. Never at full health (this beats stock's
  `tf_bot.cpp:4587-4598`).
- **Dalokohs and Fishcake.** Eat below 85% or before a push (overheal to 350, recharge about 10 s) [W-Heavy].
- **Second Banana.** Eat below 50% (it heals 200).
- **Throw.** Throw the plate (`AltFire/Tap` with the lunchbox held) to a friendly Medic in sight within 300 u under
  60% health, when own health is above 70% **[pick]** ([W-Comm]: "drop food for them").
- **Pocket.** A Heavy is pocketed while the being-healed read (lifted from `TFDecider.Voice.cs:128-152`) holds for
  2 s or more. While pocketed:
  - he weights Fight and the objective up by 0.1 **[pick]**;
  - he keeps 100-250 u ahead of the Medic toward the threat **[pick]** and never moves further than 500 u from him
    (beyond the beam's 540 u, `TFDecider.Voice.cs:49`);
  - he waits at a rally for the Medic rather than pushing alone (`TFDecider.Rally.cs`);
  - with the Medic's target-ID Über at 100% (or "ÜberCharge ready" heard), he holds at the rally until the pop,
    then leads the push.
- **Spy check.** Before a pre-spin with no teammate behind, turn to look back once per 8 s **[pick]** (the glance
  machinery, `TFDecider.cs:204`). A "teammate" bumping him from behind is suspect (C.3's honesty rule).

### 4.4 Overrides per archetype (on top of 4.3)
- **B Ambusher (Tomislav).** Pre-spin at any corner with a belief within 1000 u **[pick]** (silent). Spin down after
  1.0 s **[pick]**. Full fire to 1200 u (spread ×0.8). Prefer non-Scout targets (score Scout ×0.7). Travel by the
  route family that is not the team's main one (`routeAltWeight` up by 0.2) **[pick]**.
- **C Anchor (Natascha).** The goal is a hold spot within 400 u of the choke or point **[pick]**, not the front.
  Chase only to 300 u past the hold. Mobile targets get ×1.4 (Scout, Soldier and Demoman airborne). Fire full to
  1200 u (the slow still lands). Stay within 400 u of his Medic. Below 50% health while spun he has the 20%
  resistance, so retreat at 0.3 instead of 0.4.
- **D Siege (Brass Beast).** The goal is a spot within 300 u of the objective or a friendly dispenser, chosen once a
  life and held. Spin on any enemy seen within 6 s **[pick]**. Never move spun beyond the spot (44 u/s). Travel only
  unspun. Use the Shotgun for a target past 1024 u that stays in sight 2 s or more. On attack he follows the push
  late, rather than leading it.
- **E Steak brawler.** Eat the Steak when the team's push is called (`TFDecider.Rally.cs`'s Push), or when a target
  is believed within 900 u and no sentry or spun Heavy is known within 1200 u. Then switch to melee and chase for
  16 s at up to 310 u/s (`TFMaxSpeed.cs:347-355`), with no retreat until under 25% health. A KGB kill brings 5 s of
  crits: switch back to the minigun if a target is within 600 u. Check which weapons the port's boost reaches
  first (`TFDeathDrops.cs:305-323`); it is a measurement to make.
- **F Mobile.** Hold the GRU or Eviction Notice when nobody has been seen for 2 s and the front or rally is more
  than 1000 u away **[pick]**. Switch to the minigun at first sight or belief within 1200 u. The holster is 50%
  slower, so switch early. Watch the max-health drain to the floor of 100 [W-Heavy], and switch to the gun 3 s
  before arriving to let it regenerate.
- **G Burner (Huo-Long).** Stay within 1200 u of a known ammo source **[pick]**. Spin only with an enemy seen within
  1.5 s (4 rounds/s spun, `TFWeaponHooks.cs:274`). Ammo-seek below 40%. Prefer burning targets (×1.25 damage) and
  close targets (the ring's reach is a measurement to make).

### 4.5 Ability rows and facts
- **Rows.** Keep `spin_up` (`TFAbilities.cs:52`) for a timed pre-spin, but set `HoldTicks` from the kit's spin-up
  time rather than 60. The continuous spin is a held `AltFire` from the decider (no row). Eating and throwing are
  `SelectWeapon` plus `Fire/Tap` or `AltFire/Tap`. If two decides prove too slow, add a `Sequence` row `eat` (select
  slot 2, attack), measured against the two-decide way.
- **Facts.** One behaviour per fact, each red with its arm removed. Inline simulation unless marked.
  1. `BotInventory`: a Heavy's primary draw never names a shotgun, and the 12 multi-class shotguns are drawn as his
     secondary. This is red today (§3).
  2. `BotInventory`: 1184 is never drawn.
  3. The archetype draw: the same seed gives the same archetype and kit; Siege names 312; Anchor names 41; and so on.
  4. Spin hold: AltFire is held for 3 s after the last sighting, and released after it.
  5. Spin-down: no spin while Deliver, Course or GoHeal is chosen.
  6. Pre-spin: with a belief 600 u past a corner 200 u ahead, AltFire starts at least the spin-up time before the
     corner. Tomislav starts at 1000 u.
  7. No fire past 1600 u. Bursts only between 1024 and 1600 u at slow targets.
  8. The Heavy never backs off a player inside 320 u, unlike the Soldier.
  9. The Heavy backs off a sentry within range when not invulnerable.
  10. Ammo below 50 sends him to the nearest pack; when dry in a fight he switches to the Shotgun.
  11. He eats only under 55%, with nobody seen for 1.5 s and the meter full; never at full health.
  12. He throws to a hurt Medic within 300 u.
  13. Pocket: with the heal read on, he stays within 500 u of the Medic and waits at the rally for him.
  14. Siege: he stays within 300 u of his chosen spot for the life, excluding Survive.
  15. Steak: after the bite he selects melee, and chases at the Steak's speed.
  16. GRU: he holds the GRU in a quiet transit and switches to the gun at first sight.
  17. Burner: he spins only with an enemy seen within 1.5 s.
- **Mutation proofs.** One per fact. Snapshot with `cp` and restore with a fresh mtime, per the common brief.

### 4.6 Live measures
- **Objective first** (user ruling 2026-11-15). On `ctf_2fort` and `ss_ai_tf_cp`, with a mixed team holding one to
  two Heavies:
  - captures per 10 min, and seconds from round start to the first capture, against the all-Soldier baseline
    (G0's instrument, `docs/plans/plan_bots_teams_tf2_part_g.md:384`);
  - the points level's seconds to cap;
  - Payload cart progress per minute, once P8 lands.
  - The Heavy must not slow his team: compare time to the objective with the Heavy against a Soldier in his slot.
- **Class numbers** per Heavy life:
  - damage per life and kills per life;
  - deaths per minute;
  - the share of first sightings met already spun (pre-spin working);
  - seconds spun against seconds firing;
  - median engagement range (target: under 600 u for A, B and E; higher for C and D);
  - ammo-empty events per life;
  - eats per life and HP healed per eat (never above 90% health at the eat);
  - plates thrown to Medics;
  - seconds within 500 u of a healing Medic.
- **Archetypes play differently.** One run per archetype pinned, three seeds. The fact holds when the archetypes
  separate on their own axis:
  - Siege: distance moved per minute at least 40% below Standard;
  - Anchor: Natascha slows applied per minute above zero, and lower chase distance;
  - Ambusher: the pre-spun share at least 1.5× Standard's;
  - Steak: the melee kill share above 30%;
  - Mobile: spawn-to-front seconds at least 20% below Standard;
  - Burner: burning targets set per minute above zero.
  - Each margin is a **[pick]** for the first measurement to confirm.

### 4.7 What the code lane needs from the P7 foundation
1. **Class pick.** `TFBotClassPick.Class` (`TFBotClassPick.cs:22`) becomes C.4's roster pick, with stock's Heavy
   rows as a reference: minimum team size 3, limits 1/1/2/2 by difficulty (`tf_bot.cpp:839,853,868`).
2. **Subtree dispatch.** `TFClassTrees.TreeFor` (`TFClassTrees.cs:15`) answers the Heavy.
3. **Per-class hooks in the decider.** At least:
   - `Engage` (fire gate, reach and range band);
   - `Hands` (weapon choice by slot role: primary, secondary, melee, lunchbox; not the Soldier's script names,
     `TFDecider.cs:116-117`);
   - `FightAtRange` (the band);
   - a per-class "hold the second trigger" output beside the fire;
   - Survive's targets, extended with ammo.
4. **The kit read.** The bot's own loadout: per slot the defindex, the item's attribute values (spin-up scale,
   damage, resistance, meter) and the weapon kind. Its own spin state: TF_COND_AIMING, or the bot's own clock from
   the kit's spin-up. The HUD item meter: lunchbox ready. A player knows all of this, so it is honest.
5. **The archetype draw** beside `BotInventory`, with the slot fix and the exclusions (§4.2).
6. **Team reads.** A heal-source read (who is healing me: the beam's Medic, or the Voice inference lifted) and a
   friendly Medic's Über % from his target ID.
7. **Ammo knowledge.** Ammo packs, friendly dispensers and the payload cart as ammo sources in `MapKnowledge`/things.

## 5. Sources
- [W-Heavy] https://wiki.teamfortress.com/wiki/Heavy (stats, speeds, every weapon's numbers)
- [W-Minigun] https://wiki.teamfortress.com/wiki/Minigun (damage, ramp, spin times, the first-second penalty, no jump)
- [W-Comm] https://wiki.teamfortress.com/wiki/Community_Heavy_strategy (per-weapon play, modes, counters, 125 HP retreat)
- [W-Basic] https://wiki.teamfortress.com/wiki/Heavy_strategy
- [W-Comp] https://wiki.teamfortress.com/wiki/Heavy_(competitive) (6s last-point, pocket swap, A/D holds)
- [W-Anti] https://wiki.teamfortress.com/wiki/Anti-Heavy
- https://wiki.teamfortress.com/wiki/Highlander_(Competitive) (one Heavy a team; KOTH, PL stopwatch, A/D)
- https://wiki.teamfortress.com/wiki/Heavy_weapons_(competitive) (6s whitelist: Natascha banned; seen via search
  only, not read)
- Stock, in `../source-sdk-2013-valve/src/game/`:
  - `server/tf/bot/tf_bot.cpp` (rosters `:822-875`, `:1020-1080`; `GetThreatDanger` `:3057-3100`;
    `GetMaxAttackRange` and `GetDesiredAttackRange` `:3128-3216`; `IsAmmoLow` `:2603`;
    `OpportunisticallyUseWeaponAbilities` `:4512-4600`);
  - `server/tf/bot/behavior/tf_bot_behavior.cpp:1284-1291` (the Heavy's 3 s spin);
  - `server/tf/bot/behavior/tf_bot_use_item.cpp:20-73`;
  - `server/tf/bot/behavior/tf_bot_destroy_enemy_sentry.cpp:161-169`;
  - `server/tf/bot/behavior/medic/tf_bot_medic_heal.cpp:57-73,180-195`;
  - `server/tf/bot/behavior/tf_bot_get_health.cpp:14-17`;
  - `shared/tf/tf_gamerules.cpp:6545-6566` (falloff);
  - `shared/tf/tf_weaponbase_melee.cpp:86-93`;
  - `shared/tf/tf_player_shared.cpp:12276-12293`;
  - `shared/tf/tf_weapon_minigun.cpp`.
- Values: `bin/debug/content/tf/scripts/items/items_game.txt` (the items above and the prefabs `weapon_gatling_gun`,
  `weapon_tomislav`, `weapon_gru`, `weapon_long_heatmaker`, `weapon_russian_riot` and `weapon_shotgun_multiclass`).
- Caveats:
  - The wiki's competitive pages warn that they may be out of date. The 6s and Highlander rows above are their
    claims, not measured.
  - Mode play for CTF and the pub rows of §1's table come from the strategy pages plus general play knowledge where
    the pages are silent (CTF especially). The reviewer should treat those cells as the weakest.
