# Fable review: Demoman, Heavy, Engineer research (2026-11-20)

Reviewer: Fable 5.1. Repo cites checked with `git show 225fe2e96:<path>` (the working tree has another lane's
edits), stock cites in `../source-sdk-2013-valve/src/game/`, game numbers against the TF2 wiki (Teleporter,
Minigun, Stickybomb Launcher, Chargin' Targe pages fetched this session) and `items_working.tsv` at the base.
Findings are ranked by how much they would mislead the code lane. Nothing but this file was written.

## Verdicts

| file | verdict | fixes |
|---|---|---|
| `demoman.md` | **ready after these fixes** | X1 (Hands mechanism), D1 (750 u/s cite), D2 (hearing cues), D3 (Tide Turner never crits), D4 small cite slips |
| `heavy.md` | **ready after these fixes** | X1 (`_launcher` is not -1), H1 (ammo packs are already map knowledge), H2 (Engineer is not hit by the slot bug), H3 ("heard" Über line), small cite slips |
| `engineer.md` | **ready after these fixes** | E1 (teleporter upgrade cost is a port divergence, not only a check), E2 (plan C.3.2's 512/768 u numbers need a ruling), E3 (Wrangler trigger as written is a server read) |

All three are strong: of ~45 repo `path:line` cites spot-checked per file, every line says what the file
claims, give or take the slips listed below; every stock `tf_bot` number quoted was found at the cited line;
the archetypes are the ones people run, and the plan's C.3.1 rows are covered (Demoman: Hybrid, Trapper,
Demoknight; Heavy: Pocketed=Standard, Anchor, Ambusher, Siege; Engineer: Nest, Gunslinger, Road builder=A5).
Every file puts objective time first in its measures, as ruled.

## X. Finding shared by all three files (highest impact)

**X1. `_launcher` is never -1, and the Soldier's `Hands()` actively misbehaves for every other class.**
`heavy.md` §3 says "A Heavy has neither, so `_launcher` is -1 and the Heavy never changes weapon";
`demoman.md` A5 says "A Demoman never switches: it fights all life with what it spawned holding". Both
describe the effect but miss the mechanism, and the Heavy's premise is wrong:

- `KindNamed` (`src/ai/tf2/server/Trees/TFDecider.Soldier.cs:55-61`) scans `_map.Weapons.All`, the level's
  whole weapon table, by script name. `tf_weapon_rocketlauncher` is always in it, so `_launcher >= 0` for a
  Heavy, a Demoman and an Engineer alike (`TFDecider.cs:116-117`).
- `Hands()` (`TFDecider.Soldier.cs:125-149`): with no target sighted, `if (_launcher >= 0 && _self.ActiveWeapon
  != _launcher) { Select(_launcher); return; }` (`:137-141`) runs **every `HandsEverySeconds` (1 s) in every
  lull** for a bot whose active weapon is a minigun, grenade launcher or wrench, and returns before the lull
  reload at `:143-148`. So today a non-Soldier bot sends a `SelectWeapon` for a rocket launcher it does not own
  once a second, and **never reloads in a lull**.
- For the code lane: the Hands hook the foundation adds (`heavy.md` §4.7.3, `demoman.md` §4.7.1) must replace
  this branch by slot role, not merely add the class's weapons beside it; and a fact that a Heavy/Demoman in a
  lull reloads (and sends no `SelectWeapon` for a kind it does not hold) is red today and belongs in the first
  commit. Neither file lists that fact.

## D. demoman.md

Spot-checked and correct (line says what the file claims): `TFDecider.cs:350-362` (class table health),
`:37`, `:46`, `:116-117`, `:259-275`, `:485-494`; `TFRadiusDamage.cs:75`, `:160`, `:163`, `:173`, `:175-189`,
`:191`, `:226-256`; `TFViewVectors.cs:35`; `TFShellBehaviours.cs:23-36`; `TFClassSpecialSkill.cs:63-73`;
`TFSwordHook.cs:25-28`; `TFShieldCharge.cs:74`, `:77`, `:80-118`, `:110-115`, `:450-465`;
`TFSharedConVars.cs:100-103`; `TFMovementRules.cs:1009`, `:1040`; `TFPlayerNetState.cs:309`, `:322`;
`TFBlastJump.cs:30`, `:127`; `TFProjectileNetStates.cs:76-87` (no owner: Critical, TeamNumber, Pulsed only);
`TFProjectileEffects.cs:657`, `:704-718`, `:724-737`; `TFProjectileEntities.cs:936`, `:2920`, `:2993`;
`TFHudDemomanPipes.cs:19-21` (stale "no port to equip" note); `deferred.tsv:38` ("none ship", stale);
`BotInventory.cs:94-108`, `:148`, `:183-190`; `TFBotGameView.cs:138-139`, `:160` (owner 0), `:65-66`;
`BotCommandApplier.cs:617-624`; `Predictors.cs:83`; `Hl2mpDecider.cs:848`; `ChokeMap.cs:25`;
`BotMemory.cs:175`; `CoverSearch.cs:9`; `TFDecider.Things.cs:10`, `:102` (`AvoidsSentries => true`);
`TFDecider.Soldier.cs:17`, `:41`, `:84-85`, `:96-110`, `:112`; `TFKnowledge.cs:85-88` (no `PipebombRemote`
arm), `:121-123`; `PerceivedView.cs:33-47`; `TFDecider.Jump.cs:60`; `TFAbilities.cs:50-66`;
`TFBotClassPick.cs:22`; `TFClassTrees.cs:15`. Greps: `melee` in `src/ai/tf2/server` empty; `AltFire` in
`src/ai/tf2` empty. Counts: 61 works rows; 13/1/2 primary, 27 pipebomb (see D4) + 4 shields + 1 parachute,
7/1/3/1/1 melee; reskin groups right; Stickybomb Jumper (265) carries `cannot_pick_up_intelligence` and is
dropped by `BarsTheObjective(cols[6])`.

Stock, all found at the cited lines: `tf_bot.cpp:833-870` (Demoman 2/3/3/3), `:1212-1235`, `:3057-3091`
(Demoman 0.6 at :3090-3091), `:3212-3213` (500), `:3415-3420`, `:4522-4535`; trap `:12`, `:40`, `:86`,
`:188`, `:248`, `:260`, `:309`; sentry stickies `:14`, `:53`, `:91`, `:151`, `:167`, `:187`, `:227-240`;
`tf_bot_destroy_enemy_sentry.cpp:25`, `:51`, `:638-680` (1000 u at :674); tactical monitor `:62`, `:90`,
`:102`; `tf_bot_behavior.cpp:47`, `:265`, `:773-790`; escort `:78`, defend point `:211`, block capture
`:118`, payload guard `:119`; `tf_weapon_grenade_pipebomb.cpp:69,74`; `tf_shareddefs.h:1818`;
`tf_weapon_parse.cpp:96`.

Game knowledge checked: 175 HP, 93 % speed; sticky arm 0.8 s stock vs 0.7 s live wiki (the file says it
follows stock and gives both: right); 8 out, 4 s full charge, 120 base; sticky jump 45-114 (wiki); charge
750 u/s, 1.5 s, 12 s recharge; crit ladder meter <= 40 crit, <= 75 mini matches the wiki's "60 % / 25 %
depleted"; Scottish Resistance 14 stickies; Quickiebomb arms 0.2 s sooner.

Findings:

- **D1 (cite is wrong).** §3.1 "Movement: 750 u/s (`:41`, `TFMovementRules.cs:1009`)": `TFShieldCharge.cs:41`
  is a comment line and the file's only 750 is a screen-shake radius (`:174`). The charge speed is
  `src/tf2/shared/Movement/TFMaxSpeed.cs:160` (`MaxChargeSpeed = 750f`), applied at `:266-267` for a Demoman
  in `TFCond.ShieldCharge`. §4.3's charge-band arithmetic (950 u crit, 560 u mini) stands on the number, which
  is right, but the lane will look in the wrong file.
- **D2 (dishonest read, as written).** Detonation "when an enemy is ... seen within its reaction time or
  heard capping or stepping nearby". The core hears shots only (`src/ai/core/Memory/Blackboard.cs:44`
  `LastHeardPos`, `AmmoEstimate.cs:226-231`); there is no footstep or capture-sound sense in the view. Either
  add the sense (a client does hear a capture's announcer and footsteps) or restrict the rule to sighted
  enemies and the capture-progress HUD (the objectives feed carries `Progress`, `Objectives.cs:51-52`, which a
  player sees on the HUD). As written a lane could read the server's capture trigger.
- **D3 (design gap).** A5 Hybrid knight and the A2 alternatives list the Tide Turner (1099). The port's
  `GrantCrit` (`TFShieldCharge.cs:455-458`) gives a Tide Turner wearer a mini-crit only, never a full crit
  (`lose_demo_charge_on_damage_when_charging`), as stock does. §4.3's "*Why 560-1000 u*" crit band and fact 9
  ("the hit carries the crit flag in 8 of 10 seeds") are therefore wrong for that shield; the rule should read
  the flag (a `TideTurner` kit flag beside `HasShield`) and aim for the 560 u mini-crit band instead.
- **D4 (small slips).** "26 `tf_weapon_pipebomblauncher`" is 27 rows (the Jumper is a row, just never drawn);
  `TFSwordHook.cs:42` for the 8 %/head is `:38` (`SpeedPerHead`), `MaxHeads` at `:32`;
  `TFRadiusDamage.cs:264` for the 0.4 gravity is `:259` (`PipebombGravity`); `TFSwordHook.cs:25-42` fine.
- **D5 (A3 verified, one nuance).** The sticky's speed does read 0: `WeaponOf` takes `w.LaunchSpeed`, else the
  script's `ProjectileSpeed`, else `StockSpeed`, which has no `PipebombRemote` arm; the test script
  (`src/tf2/tests/TestData/weapons/tf_weapon_pipebomblauncher.txt`) names `projectile_pipe_remote` and no speed,
  and stock's `tf_weapon_parse.cpp:94` defaults `ProjectileSpeed` to 0. Gravity for `PipebombRemote` **is**
  already there (`TFKnowledge.cs:71`), so only the speed/charge curve is missing; `ChargeSeconds` for
  `PipebombRemote` is already `MaxChargeTime` (`:123`). Smaller job than §4.7.6c implies.
- **D6 (plan vs file).** The sticky-jump health rule (over max - 25) replaces the plan's `blastHealthFloor`
  80 for the Demoman (C.3.2) with a reason; record it as a ruling to make, not silently. The §4.2 claim that
  C.4 "moves weight to Loch and Hybrid" overstates: C.4 raises the *class* weights of Demoman/Spy/Soldier on a
  seen sentry; the archetype shift is the file's own addition (fine, but say so).
- **D7 (ship order).** A3 Trapper is cheaper than A2 Demoknight (one `InViewDet` flag over A1's trap and
  detonation, on the defend role) while A2 needs the shared melee approach, the charge rule, heads and a
  `SelfState` extension. A6 Loch-n-Load is nearly free once A1's sentry kill exists (`Shatter` = direct hits
  only). Suggest 1 Hybrid, 2 Trapper, 3 Demoknight, 4 Loch, 5 Combo, 6 Hybrid knight. The plan's stock row
  first is respected either way.
- **D8 (buildability, fine).** The weapon-choice band, pipe lob, sticky charge search, trap, sentry kill and
  charge rules each carry a number and a source; the lane can build them. The sentry kill's "cover cell within
  1000 u the sentry's cell does not see" fits `NavSight.Range` 1024 (`NavSight.cs:32`). Measures: objective
  first, per-archetype axes named; good.

## H. heavy.md

Spot-checked and correct: `TFMaxSpeed.cs:124`, `:137`, `:229-233`, `:347-355`; `TFMinigunBehaviour.cs:24`,
`:58`; `TFWeaponHooks.cs:270-282`, `:274`, `:282`, `:294`, `:327`; `TFDamageLayer.cs:719-730`, `:959`,
`:1048`; `TFDamageAliveRules.cs:107`, `:140`, `:167`, `:190`; `TFLunchboxWeapon.cs:69`, `:86-128`, `:98`,
`:100-105`, `:104`, `:122`, `:202`; `TFLunchboxBehaviour.cs:30-37`; `TFDeathDrops.cs:305-323`;
`TFPlayerResupply.cs:302-320`; `TFTargetId.cs:157`; `WeaponBehaviour.cs:256`; `PlayerInventory.cs:120`,
`:144-147`, `:153-165` (`SlotsConsideredIdentical`: weapon slots must match exactly); `SchemaParser.cs:349-371`;
`BotInventory.cs:68-81`, `:148`, `:152`, `:183-190`; `TFDecider.Soldier.cs:38`, `:92-119`, `:129-139`;
`TFDecider.cs:37`, `:46`, `:116-117`, `:348-362`, `:370-374`, `:485-494`; `TFKnowledge.cs:118-143`;
`PerceivedView.cs:33-51`; `TFAbilities.cs:40`, `:52`; `TFDecider.Voice.cs:33`, `:42-49`, `:128-152`;
`BotCommandApplier.cs:385`, `:619-623`; `TFHudItemMeter.cs` exists (client and ui); plan `:731-734`, `:732`,
`:1509`; part G `:384`. Greps: `SpinUp` only in the row; `CanHolster` in `src/tf2` only a comment and the
cond's add/remove (`TFLunchboxWeapon.cs:104`, `TFPlayerResupply.cs:152-153`), so gap 1 is real even though
the needle "override bool CanHolster" could not match an interface default. Counts: 67 works rows; 34 minigun
+ 12 shotgun in the `primary` column, 9 secondary (7 lunchbox + 11 + 425), 12 fists; attribute lists per row
match the TSV; 1184 carries `self_mark_for_death`; 1100 carries `breadgloves_properties`.

Stock, all found: rosters `:839`, `:853`, `:868` (Heavy min team 3, 1/1/2/2); `GetThreatDanger`
`:3074-3098` (Pyro 1.0, Soldier/Heavy 0.8, Scout/Spy/Demo 0.6, Engi/Sniper 0.4, Medic 0.2);
`GetDesiredAttackRange` 500 at `:3213`; `:4587-4598` eats whenever the lunchbox has ammo;
`tf_bot_behavior.cpp:1284-1291` (3 s spin, `!IsAmmoLow`, not hurrying); `tf_bot_destroy_enemy_sentry.cpp:161-169`;
`tf_bot_medic_heal.cpp:60-63` (Heavy first); `tf_bot_get_health.cpp:14` (0.3); `tf_gamerules.cpp:6548-6566`;
`tf_weaponbase_melee.cpp:86-93`; `tf_player_shared.cpp:12276-12293`; `tf_weaponbase.h:339`; the minigun does
not override `OwnerCanJump` (only the bow does, `tf_weapon_compound_bow.h:92`).

Game knowledge checked (wiki Minigun, Heavy): 0.75 s spin-up, 1.15 s spin-down, 4 x 9 per 0.105 s, 50 %
first-second penalty, 230/110/44 u/s, 125 +-25 retreat, Sandvich 4.3 s. The wiki does say a spun Heavy
"cannot jump"; the SDK code has no such gate, so the file's "measure it, plan no jump regardless" is the right
call under "the port follows stock".

Findings:

- **X1** above: "`_launcher` is -1" is false; the real behaviour is a failed `SelectWeapon` each lull second and
  no lull reload. Fix §3 "Weapon swaps" and add the fact.
- **H1 (gap overstated).** §1 mistake 6 "nothing seeks ammo (`grep ItemKind.Ammo src/ai/tf2` finds nothing)":
  the grep hits `src/ai/tf2/server/Knowledge/TFMapKnowledge.cs:25-27`, where ammo packs become
  `ItemKind.AmmoSmall/Medium/Large` map items. The conclusion stands (no `ammo` in
  `src/ai/tf2/server/Trees`; Survive heads only for health), but §4.7.7 asks the foundation for "ammo packs ...
  in `MapKnowledge`" which already exist; only friendly dispensers and the cart are missing. The lane's ammo
  rule (§4.3) can go straight to `MapKnowledge.Items`.
- **H2 (slot bug: right, with one wrong class).** Verified: the 12 multi-class shotguns carry `primary` in the
  TSV's slot column with `soldier,heavy,pyro,engineer`, `BotInventory.cs:152` buckets by that column,
  `Equip` (`PlayerInventory.cs:120`) refuses because `Slot(heavy)` is secondary, and `BotInventory.cs:105`
  (not `:107`) drops the bool. The Engineer is **not** hit by these rows (his shotgun slot is primary); Soldier
  and Pyro are. The regression fact in §4.5.1 is right and red today.
- **H3 (dishonest read).** §4.3 Pocket: "with the Medic's target-ID Über at 100 % (or 'ÜberCharge ready'
  heard)": the target ID is honest only while the bot looks at the Medic (say so: a glance rule); nothing in
  the view carries voice lines, so "heard" needs a sense or should go. §4.3 Target priority "x1.3 for a target
  attacking his Medic" needs its honest definition (an enemy facing within N degrees of the Medic, or the
  Medic's health falling while that enemy is the only one in sight).
- **H4 (small slips).** The flagged-family quote cited at plan `:777-781` is at `:793-795`; `tf_bot.cpp:3214`
  is `:3213`. `BotCommandApplier.cs:385`'s eye wait applies to any `AltFire` with an Actor/CornerCheck look,
  held included, so the file's advice (pre-spin on a path or point look) is right.
- **H5 (buildability, fine).** Every rule carries a source or a `[pick]` with the measurement that settles it;
  the spin hold/pre-spin/spin-down, bursts past 1024 u, eat and throw rules are concrete. Ship order (A-D as
  spin-policy variants over one hook set) is cheap-first. Measures: objective first, archetype axes named with
  `[pick]` margins; good.

## E. engineer.md

Spot-checked and correct: `TFObjectDefs.cs:42-47`; `TFSentryGun.cs:80`, `:150-156`, `:427`;
`TFBaseObject.cs:271-284`, `:912-924`, `:1010`, `:1069`; `TFBuilderPlacement.cs:85`, `:157`, `:309`,
`:467-470`, `:473-474`, `:580`; `TFBuildingPickup.cs:19-29`, `:90-93` (250-5500), `:96` (150);
`TFProjectileEntities.cs:1698`; `TFEurekaTeleport.cs:52`; `TFRevengeHook.cs:32`;
`TFBuildingResourceNetState.cs:62`, `:126`; `TFSapperUse.cs:221-252`; `TFWrenchBehaviour.cs:110`;
`FuncNoBuild.cs`, `TFSentryWrangle.cs`, `TFDispenser.cs`, `TFTeleporter.cs`, `TFSentryRocket.cs`,
`TFObjectModels.cs` exist; `TFAbilities.cs:54-61`; `TFKnowledge.cs:19-20`, `:152-159`, `:196`;
`TFBotGameView.cs:65-66`, `:180-191` (`:184` drops placing/carried), `:202-217`; `PerceivedView.cs:33-48`,
`:122-131`; `TFClassTrees.cs:15`; `TFBotClassPick.cs:22`; `ChokeMap.cs:25-66` (`ScoreOf` `:63`);
`NavSight.cs:8-17`, `:30-34`; `ICellSight.cs:8-18`; `NavGraph.cs:96-110` (`Admits`); `FlowField.cs:15`, `:92`;
`TFMapKnowledge.cs:33-54` (`ZoneOf`: RespawnRoom, Resupply only); `MapKnowledge.cs:112-119`;
`Objectives.cs:51`; `BotMemory.cs:11`; `TFExperienceHolds.cs:7-16`; `ExperienceView.cs:129`;
`TFDecider.Holds.cs:33-46`; `TFDecider.Things.cs:132-172`; `AiNav.cs:355-373`; `BotInventory.cs:98-99`;
`TFRadiusDamage.cs:22`. Grep `Dispenser|Teleporter` in `src/ai/tf2/server/Trees` empty. Counts: 66 works;
melee 22, primary 18, secondary 24; wrench 21 (18 plain + Golden + Jag + Eureka), pistol 20, Wrangler 3,
shotgun 13 with the Panic Attack; 155 is `partial`; the build PDA (25) has no row.

Stock, all found: `tf_bot_engineer_move_to_build.cpp:30` (3000), `:68-79`, `:150-155`, `:164-173`,
`:175-178`, `:182-185`, `:270-287`, `:340-358`; `tf_bot_engineer_building.cpp:28` (750), `:34` (5), `:85`,
`:128-147`, `:154-171` (the priority list, rung for rung), `:181-207`, `:225-245` (`:243`), `:326-364`
(`:338`, `:361`), `:370`, `:425-465` (`:460`); `..._build_teleport_entrance.cpp:18` (1500), `:19` (0.99);
`..._build_sentrygun.cpp` `m_sentryTriesLeft = 5`, `m_needToAimSentry`, `CB_NEED_RESOURCES`;
`..._build_dispenser.cpp:131` (75 u behind); `..._build.cpp:107-111` ("Engineers hate Spies", 100 u);
`tf_bot.cpp:835`, `:857` (defence 1 per 4, min 1, max 1/2/3/3; offence team >= 5, max 1), `:1156-1163`,
`:2655-2662`.

Game knowledge checked (wiki Engineer, Sentry Gun, Teleporter, Rescue Ranger): 125/150 HP, 200 metal,
sentry 130, dispenser 100, teleporter 50, upgrades 200 a level, Wrangler 66 % shield and 3 s fade, mini 100
metal, Rescue Ranger 100 metal lift and bolts.

Findings:

- **E1 (port divergence, promote from "check" to "fix").** The teleporter's `UpgradeCost` 100
  (`TFObjectDefs.cs:44-45`) against the wiki's 200 a level ("200 metal to upgrade the set to Level 2, and an
  additional 200", Teleporter page). `TFObjectDefs.cs:22-23` says the rows are "transcribed from the shipped
  `objects.txt`"; the Valve clone is `src/` only and ships no `objects.txt`, so the lane must open the shipped
  game file (the TF2 VPK's `scripts/objects.txt`) and either fix the row or record the divergence. Note
  `TFKnowledge.Buildings()` (`:152-159`) reads the same table, so the bot's belief tracks the port either way;
  only the live metal budget is at stake.
- **E2 (contradicts the plan of record, unrecorded).** Plan C.3.2's Engineer row says "teleporter entrance
  within 512 u of own spawn's exit, exit within 768 u of the sentry". The file uses stock's 1500 u of travel for
  the entrance, 300 u of the sentry on defence and 768 u of T's approach on attack, and never names the plan's
  numbers. Both are defensible; the lane needs a dated ruling in the plan's Decisions, else the plan's fact and
  the file's fact disagree.
- **E3 (dishonest read, as written).** §4.4 Wrangler: "Draw the Wrangler when the sentry has taken damage in
  the last 1 s from an enemy at more than 1,100 u from it". The building panel (the honest sense §4.9.5a asks
  for) carries health, not the attacker; the attacker's range is known only when sighted. Rewrite as "sentry
  health fell in the last 1 s and no enemy is sighted within 1,100 u of the sentry (or the one sighted is
  beyond it)". Same for §4.4 "An enemy within 400 u that the sentry cannot see (no sentry kill in 2 s ...)":
  use `ICellSight.Visible(sentryCell, targetCell)` or the sentry's own line of fire, which a player judges by
  eye, rather than the kill feed.
- **E4 (cite path).** `TFBuildCommands.cs:45,141` has no directory in the file (nor in the plan); it is
  `src/tf2/server/Buildings/TFBuildCommands.cs` (the `Verb` constants, lines right). The file's other cites
  name their directories.
- **E5 (buildability, fine).** The spot finder is the most complete design of the three: inputs, candidate
  rules, a scored formula with per-term sources, a draw, facing, the dispenser/teleporter spots, and the
  placement loop through the player's path, all on map knowledge and the bot's own HUD (honest). Two things
  the lane should know: `FlowField.Build` takes one objective cell (`FlowField.cs:92`), so "one field per spawn
  group, minimum" is several builds per team, once per level (fine); `LongLine`'s 64-cell voxel-ray sample is
  the only term with a cost worth timing. Facts 1-11 are one behaviour each with named mutants; fact 7 matches
  C.3.2's. Measures: objective first (a, b, c), and the honest note that (b) cannot move until other classes
  take teleporters (§4.8) is exactly the dependency the foundation must own.
- **E6 (ship order, fine).** A1 Nest then A4 Gunslinger (numbers only) then A2/A3/A5 is cheap-first; A5 is
  rightly last because of §4.8.

## What each lane needs from the foundation (reconciled across the three files)

The three files agree on: a per-class Hands/Engage/FightAtRange hook that replaces the Soldier's script-name
pair (X1 makes this the first commit, with its red fact); the kit read by slot with attribute classes; the
archetype draw with a coherent loadout and the slot-column fix (H2); per-class ability tables appended; the
class pick from stock's rosters (Demoman 2/3, Heavy 1/1/2/2 min team 3, Engineer 1 per 4 on defence);
`SelfState` extensions (stickies out, charge meter, heads; spin state and item meter; the building panel,
blueprint validity, carrying). Only the Heavy asks for a heal-source and Über read; only the Engineer asks for
`ZoneKind.NoBuild`, a `Front(team)` helper and the experience feed of sentry/dispenser/teleporter spots.
