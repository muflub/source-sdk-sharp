# Fable review: scout.md, soldier.md, pyro.md (2026-11-20)

Checked against `git show 225fe2e96:<path>` (the files' base), the Valve clone, the TF2 wiki (fetched today) and
the three foundation commits that landed in this tree while the files were being written. Findings are ranked by
how much they would mislead the code lane. Verdicts at the end.

## 0. Findings common to all three files

**C1. The tree moved under the files; §3 and §4.7 of each must be re-read against HEAD.** Three p7f commits landed
at 19:32, 19:37 and 19:46 (`git log 225fe2e96..HEAD`): `3b8dacaa1` (a `ClassPlay` per class, `Classes/TF*.cs`,
`TFClassSpecs.BlockRows = 16`), `63d0b85bd` (the slot-key fix plus an `IBotItemChoice` seam) and `5e6321b26` (the kit
read: `PerceivedView.GameHud`, `TFHud`, `ITFOwnKit`, an `ITFHudMeters` chain). Consequences, by file, are under C1a–C1d.
- **C1a. The slot bug is fixed.** Soldier §3.1 "bot loadout per slot: bug" and Pyro §3.2 / §4.2 "fix the slot key"
  are done: HEAD `BotInventory.cs:177` keys a row by `definition.Slot(cls)`, a refused equip is counted and the draw
  moves on. Soldier fact 13's slot arm and Pyro fact 10 are covered by `src/tf2/tests/BotInventoryTests.cs`. Do not redo.
- **C1b. Flagged families are no longer the seeded draw's.** HEAD `BotWorksItem.Flagged` (`BotInventory.cs:239`): a
  lunchbox, drink, banner, shield, builder, PDA or `tf_wearable*` row reaches a bot only through `IBotItemChoice`.
  Soldier §3.1's "banner 4/10, Gunboats 2/10, parachute 1/10" and Scout §3.2's "the secondary is as likely a cleaver
  as a pistol" describe the base, not HEAD. This is the shape the files ask for; the archetype draw is the choice.
- **C1c. The Escape Plan is filtered out at load (Soldier, highest impact).** HEAD `BotInventory.cs:160` drops any
  row with `self_mark_for_death` (aimed at MvM's GRU; the commit message names the Escape Plan explicitly). A choice
  may only name an offered row, so **defindex 775 cannot reach a bot at HEAD**. Soldier archetypes B, C, D and E all
  list `775|6` first in melee, and rule C's "Escape Plan held while going to heal" rests on it. The Soldier lane must
  get the foundation to lift the filter for the Soldier (keep it for the GRU) or drop the Escape Plan from the set.
- **C1d. The kit read has landed, but not where the files expect.** `5e6321b26` adds per-slot defindex, item class,
  catalog kind and attribute classes *with values* (`src/tf2/server/Econ/TFOwnKit.cs:18`, `TFOwnKitItem`), and a meters
  chain, on `PerceivedView.GameHud`, not on `SelfState` (unchanged at HEAD). Scout §4.7 items 3–4, Soldier §4.7 "kit
  read", Pyro §4.7 "the kit read" are satisfied in substance; the lanes should extend `ITFHudMeters` (drink, hype,
  item meters, rage) and read `TFHud`, not ask for `SelfState` fields. `ActorConditions` is still a 6-bit byte.

**C2. Every file writes rules on the enemy's facing, and none asks the foundation for it.** Scout §4.3 "a sighted
enemy whose view points at the bot within 12°", Soldier §4.3 "Pyro … facing the bot (view direction within 30 deg)",
Pyro §4.3 A4 "target's facing dot with the bot's view > 0.8". `ActorFacts` (`PerceivedView.cs:90-100`, same at HEAD)
carries Origin, Velocity, WeaponSeen, Activity, VisibleFraction, Team, Class, Conditions, NameSlot: **no eye or facing
direction**. Plan C.3.2's Scout row points at "C.2's field list" for it, and C.2 has none either. One shared sense; add
it to each §4.7 (a client renders every player's eye yaw, so the read is honest).

**C3. Where the loadout table lives.** Plan C.3.1 (`:690-693`) makes a sub-class "data in `tf2_subclasses.json`, one
row per sub-class … a preference list per slot". All three files put the archetype table in the class's C# file
(Scout §4.2 "as a table in the Scout's file"; Soldier §4.2 "the foundation's archetype table"; Pyro §4.2 lists in
prose). Not a behaviour contradiction, but one ruling is needed before three lanes build three shapes.

**C4. "p7f, uncommitted" cites are now committed** and match: `ClassPlay` has `Hands`, `Engage`, `FightInSight`,
`OneShots`, `Weigh`, `Role`, `PlaceUtility`, `GoPlace`, `Died`, `Archetype` (HEAD `TFDecider.Classes.cs:26-67`);
blocks are 16 rows: Scout 16, Soldier 48, Pyro 112. The Scout file never names these hooks (it says only "p7f's plan
is `TFScout.cs`"); its §4.3 rules should be mapped onto them as the other two files do.

## 1. scout.md

**Repo claims.** 31 cites checked; 28 are what the file says (`TFCapturePlayerRules.cs:30,55`, `ItemTeamFlag.cs:620`,
`TFLunchboxWeapon.cs:139,172-180`, `TFJump.cs:35,87,94-99`, `TFPlayerNetState.cs:188,336,459,672`,
`TFActorFacts.cs:41,87,90`, `TFHypeDrain.cs:82,90-107`, `TFPlayerConditionEffectArms.cs:72-87`,
`TFMeleeBehaviour.cs:164-185`, `TFProjectileEntities.cs:36-53`, `TFItemMeter.cs:74-80`, `TFScattergunBehaviour.cs:44,
63-79`, `TFWeaponBehaviour.cs:726`, `TFClassTrees.cs:15`, `TFBotClassPick.cs:22,55`, `TFDecider.cs:37,46,485-494`,
`TFDecider.Soldier.cs:38,112,125-149,137-141`, `PerceivedView.cs:33-48,59-68`, `TFKnowledge.cs:69,132-142`,
`TFBotGameView.cs:60-68`, `TFNavProfile.cs:24-25,71-76`, `NavFollower.cs:979-1010`, `TFDecider.Fight.cs:136-141,
150-153`, `BotCommandApplier.cs:526-531`, `DecideSchedule.cs:12`, `IntentCommand.cs:52-67`, `TFAbilities.cs:23,50-66`,
`TFDecider.Feel.cs:128,159-172`, `TFDecider.Things.cs:101-102,172`, `Hl2mpDecider.cs:1621`, `BotInventory.cs:84-108`,
`TFLoadoutProvider.cs:369`, `Makefile:852`, plan `:718-719`; stock `tf_bot.cpp:849,864,3168-3171,3190-3193,3213,
3365-3375,4566,4593`, `tf_bot_tactical_monitor.cpp:266-281`, `tf_bot_get_health.cpp:14-16`,
`tf_player_shared.cpp:172-173,4643-4653`). Both gaps are real: `mod_mark_attacker_for_death` is in no file under
`src/tf2` while stock `OnAttack` marks for the schema's 5 s; `tf_weapon_bat_wood`'s alt-fire falls to the base class
and there is no stun-ball entity (the `CTFStunBall` comment at `TFProjectileEntities.cs:3700` is the ornament's).
Three misreads:
- **S1. Bonk's phase is 8 s, not "about 9 s".** `TFLunchboxWeapon.cs:190-193` is `DrinkSeconds` = 100/rate + 1, stock's
  fall-back *dead time for the drink taunt*. The phase itself drains at the consume rate 12.5/s from 100
  (`TFHypeDrain.cs:92`) = 8 s, as the wiki and stock say. Drop the "measure".
- **S2. The FaN's knockback gate is stock's, not the wiki's.** §2 C and §4.3 C say "over 40 damage ([FaN])". Stock is
  `flDamage > 30 && distSq < 400²` (`tf_weapon_shotgun.cpp:40-41,52`) and the port follows it
  (`TFDamageLayer.cs:140-142`). Port follows stock: 30 damage within 400 u.
- **S3.** `tf_bot_medic_heal.cpp:426-437` is `IsGoodUberTarget` (a Scout gets no Über), not heal priority. The
  direction is right; the cite supports a different sentence.
- Minor: `TFDamageModifyRules.cs:526-531` is the Scattergun's extra ×1.5 short-range bonus, not the whole ramp.

**Game knowledge.** Bonk 8 s / 30 s recharge / 25–50 % slow for 5 s: wiki, stock (`regen_rate 3.3`) and port agree.
Crit-a-Cola: the file rightly claims no +25 % speed (removed 2017) and marks the missing mark-for-death. Mad Milk,
Flying Guillotine (5.1 s, −1.5 s on a long hit, no buildings), Atomizer (no triple-jump damage since 2017-10-20, 0.7 s
deploy hold), BFB (360→520, −75 % per air jump, 4 %/damage taken) all match the wiki. One flag: the wiki says 100
damage fills the BFB meter, so the port's "450 after 200 damage" (TSV line 199) is either a divergence or a misread;
the file's **measure** is right to keep.

**Buildability.** The Bonk drink gate, the phased rules, the weapon choice and the fight band are precise and sourced.
Needs: the enemy facing (C2) for the dodge rule; "a target the bot is winning (health share over the target's
believed)" needs a believed health on `KnownActor` or a HUD read, name which; the backcap trigger "team stance has
failed for 20 s" is vague but marked measure. The fight double jump as a two-decide move over the follower's
`Rising → LetGo` model is buildable as written. Rules contradict nothing in C.3–C.5; the ×2 cap weight and the
drink gate (stock's meter 100 and the flag) follow C.3.2's row.

**Ship order.** Flanker (stock) first ✓. Bonk second is right: a cheap one-shot with a visible difference (sentry pass).
FaN third adds little play beyond a shorter band until the knockback use lands; BFB (no new sense) could go before
Milk, which waits on Medic whereabouts. Not blocking.

**Measures.** Objective time first (first cap start / first flag touch, Scout vs Soldier control) ✓; one axis per
archetype ✓; jank score ✓.

## 2. soldier.md

**Repo claims.** 38 cites checked at the base; all hold (`TFRadiusDamage.cs:22,29,32`, `BlastJumpSim.cs:296-307`,
`TFBlastApplier.cs:376,412-413`, `TFDamageModifyRules.cs:221-231`, `TFDamageLayer.cs:315,416`,
`TFMovementRules.cs:386`, `TFRageMeter.cs:36,59,63`, `TFBuffItemBehaviour.cs:72-90,94-95`, `TFBuffDeployment.cs:51`,
`TFWeaponHooks.cs:215-217`, `ITFWeaponHook.cs:739-750`, `TFMeleeEffects.cs:225-226,394`, `TFJumps.cs:231-239`,
`BlastJumpSims.cs:61-79`, `TFDecider.Soldier.cs:17,26,38,77,92-119,113,125-149,137-141`, `TFDecider.cs:46,116-117,
255,472-482`, `TFDecider.Jump.cs:64,68`, `TFDecider.Points.cs:274,307,327,404`, `TFObjectiveFeed.cs:108-109`,
`TFDecider.Feel.cs:128,131,160-173`, `TFDecider.Things.cs:23-57`, `BotSelf.cs:85-99`, `BotInventory.cs:148,152,
174-176`, `PlayerInventory.cs:120`, `TFFlameEffects.cs:631`; stock `tf_bot.cpp:3164-3166,3207-3210,3377-3390,
4539-4567`, `tf_bot_behavior.cpp:629-676`, `tf_bot_destroy_enemy_sentry.cpp:244,304,325`, `tf_obj_sentrygun.h:35`,
`tf_weaponbase.cpp:1172,1208`, `tf_weaponbase.h:301`, `tf_weapon_medigun.cpp:489-499`). Gaps confirmed: no
honorbound holster cost (only the duel; `KillCountSinceLastDeploy` absent), no melee selection anywhere in `Trees/`,
no banner row, the Beggar's `Clip <= 0` gate, the jump book from the stock launcher's script with `NoTFWeaponOwners`.
The 62 = 44/10/8 count reconciles with the TSV. The slot bug was real at the base and is fixed at HEAD (C1a).
- **So1. Escape Plan: see C1c.** Four archetypes and one rule need a filter lifted.
- **So2. Reserve Shooter rule is stale.** §2 says "mini-crits airborne for 5 s after deploy" and rule E swaps "when the
  target is airborne within 800 u (mini-crits 5 s after deploy), back after 5 s". The deploy window went in Dec 2015
  (wiki); the port's arm is `mini_crit_airborne && VictimAirborne` with no window (`TFDamageModifyRules.cs:293-296`),
  and `VictimAirborne` is `InAirDueToExplosion` (`TFDamageLayer.cs:417`). Rule: swap while the target is
  **blast-airborne**, back when grounded. The same applies to the Direct Hit's `mini_crit_airborne`: rule E's
  "airborne (|vz| > 200 …)" earns the +0.3 priority but the mini-crit lands only on a blast- or knockback-airborne
  target, not a jumper.
- **So3. Two reads with no sense behind them.** B "the Medic's beam is on the bot" (stock's HUD shows the healer; no
  field at base or in `5e6321b26`'s `TFHud`, which carries the *Medic's* patient) and D "a sentry firing within
  1100 u" (`ThingFacts` has no firing state). Ask for a healed-by flag in §4.7; make the Battalion's trigger "a sentry
  in sight within 1100 u" or team damage taken.
- **So4.** The Beggar's "a 4 s hold misfired" reading: row 730's evidence says "5 s hold 134, a 4 s hold 228 then 43",
  damage figures, not a stated misfire. Re-read before building the release rule on it. B.A.S.E. Jumper: row 1101's
  evidence ends with the server-side fix pressed 2026-11-06 (tf2ai-F129), so the file's "verify live" is a caution,
  not an open bug.
- Minor: the medigun's 450 u is the script value; `tf_weapon_medigun.cpp:489-499` only shows the script being read.

**Game knowledge.** Body, launcher, Direct Hit (1980 u/s, 112, 44 u), Liberty, Black Box, Beggar's, Air Strike,
banners (600/480, 450 u, 10 s), Gunboats −60 %, Mantreads, Escape Plan tiers, Market Gardener, Pain Train,
Half-Zatoichi all check. Pocket/roamer and the 6s notes match `Soldier_(competitive)`. Stock numbers (500 u shotgun
swap, 1250/3000, 1.1–1.5 × 1100, parachute 0.5 / 300 / 150) are exact.

**Buildability.** The most precise of the three: every rule has a number and a source, the eight beginner mistakes
map to rules. Needs C2 (enemy facing) for the Pyro rule; "within 128 + 256 u" should say 384 u or give the two terms
separately; the attack-jump program is correctly the one large build. Nothing contradicts C.3–C.5; the banner gate
(stock's full meter and flag) and the jump floors follow C.3.2.

**Ship order.** A (kit-safe stock) → C (Gunboats, jump cost by kit) → D (banners, one row) → E (Direct Hit + sentry
shelling, which then serves A–D) → B (waits on the Medic's `Ready`) → F, G, H. Cheap, high-value first ✓. At HEAD,
Gunboats and banners arrive only through the choice seam, which the archetype draw is ✓.

**Measures.** Objective time first ✓; per-archetype separators and a flat mutant ✓.

## 3. pyro.md

**Repo claims.** 46 cites checked; all hold (`TFFlameManager.cs:42-68` via `TFFlameDefs` 2450 / 0.6 / 8.5, `:374`,
`:397`, `:487-500`; `TFBurn.cs:76`; `TFShellBehaviours.cs:528-537,541`; `TFFlameEffects.cs:123,185-188,201-235,
350-397,405,437,631,654`; `TFBuffDeployment.cs:194-195`; `TFSapper.cs:224-240`; `TFInvisibility.cs:26,50`;
`TFProjectileEntities.cs:1567,2407,2587,2697,2746,3095,3405`; `TFSentryRocket.cs:34`; `TFDamageModifyRules.cs:194,
242,287,294`; `TFSharedDefs.cs:39`; `TFDecider.cs:46,437-471,485-493`; `TFDecider.Soldier.cs:38,41,112-115,125-146`;
`TFDecider.Things.cs:60,102,148-164,184`; `TFBotGameView.cs:102-103` (`CloakHidden` 0.75), `:129-132,134-150,
156-166,180-191`; `PerceivedView.cs:32-68,90-100,118-127`; `MapKnowledge.cs:44-50,93`; `SkillCurves.cs:22,340`;
`IntentCommand.cs:202`; `DecideSchedule.cs:12`; `BotInventory.cs:94-108,152-166`; `PlayerInventory.cs:120`;
`ItemDefinition.cs:300-306`; plan `:672,725-727,783-789,1492`; stock `tf_bot.cpp:45,49,3139-3156,3196-3199,
3402-3418,4083-4106,4116-4144,4180-4189`, `tf_bot_behavior.cpp:39,1355-1368`, `tf_bot_vision.cpp:264-271`,
`tf_bot_destroy_enemy_sentry.cpp:160-170`, `tf_bot_seek_and_destroy.cpp:159-167`, `tf_bot_defend_point.cpp:183-196`,
`tf_bot_attack.cpp:39-60`, `tf_player.cpp:17913-17918`). Every gap is real: `Deflect` overloads exist only for
pipe, sticky, rocket and energy ball (`TFFlameEffects.cs:273-329`), so arrows, flares, jars, cleavers, bolts, sentry
rockets and fireballs pass; no `Megaheal`/pushback-immunity check where stock has `IsImmuneToPushback`
(`tf_weapon_flamethrower.cpp:1662`); `Draw` drops a ≥ 0.75-invisible actor before reading `Burning`; `ThingFacts` has
no sapped flag; `HealPlace` filters health kinds only; pipes and flares read owner 0. Counts (66 works + the
Detonator `partial`; 43/8/15 by the slot column; 12 shotguns under primary) reconcile with the TSV.
- **P1.** The file says cites are "at HEAD (`git show HEAD:<path>`)". HEAD moved twice while it was written; the cites
  match `225fe2e96`, so say so (the `TFDecider.Soldier.cs` lines it cites no longer exist at HEAD).
- **P2.** The slot bug (§3.2, §4.2 "fix the slot key", fact 10) is done at HEAD (C1a); the kit read is landed (C1d).

**Game knowledge.** Flame reach, afterburn cap, airblast cost (20/25/50) and refire, extinguish +20, Backburner (73°
= dot 0.8, no random crits), Degreaser, Phlogistinator (300, 2.6 s), Axtinguisher (44, mini-crit and extinguish,
+35 % for 4 s, 35 % slower holster) all match the wiki; the stock numbers (250 / 100 u, 750 u swap, deflect
tolerance 0.5 as dot < −0.5, shove 250, reflect 0 / 50 / 90 / 100 %, 3 s circle-strafe window) are exact. The flame
reach arithmetic (2450/8.5 · (1 − e^−5.1) ≈ 287 u) follows the port's drag step.

**Buildability.** The reflect program (extrapolate the tracked projectile into the 0–256 u zone, press on the entry
tick, skill as a once-per-projectile draw like `dodgeChance`) is the best-specified ability in the three files. Needs
C2 (enemy facing) for A4. A2's Homewrecker rule and Spy-check reveal are honestly marked as waiting on the sapped flag
and the suspicion counter. Nothing contradicts C.3–C.5; the Spy-check row is C.3.2's verbatim, with its 9-of-10 target.

**Ship order (one real concern).** A1 (stock) first ✓. **A2 "Airblast guard" second carries three foundation
dependencies** (a Medic class that is not `Ready`, the sapped flag, the Spy-suspicion counter) while A3 "Combo" needs
only the per-class `HandsEverySeconds` and the kit read, which has landed. Either ship A3 second, or cut A2's first
slice to what exists (extinguish, reflect, shove, a leash to any sighted teammate) and let the sapper and Spy parts
follow. A4 and A5 after ✓.

**Measures.** Objective time first ✓ (ctf_2fort concede rate; CP point times). The CP map is left as "`ss_ai_tf_cp`,
or the shipped cp map the P6 gates use": `ss_ai_tf_cp` does not exist (plan P8); name cp_granary as the Soldier file
does so the three lanes share one harness. Per-archetype counters ✓; the reflect counter needs a log line at
`TFFlameEffects.cs:206-216`, correctly noted.

## Verdicts

- **scout.md: ready after these fixes.** (1) Bonk's phase is 8 s (`TFHypeDrain.cs:92`), not 9: drop the measure (S1).
  (2) FaN knockback is stock's > 30 damage within 400 u, not the wiki's 40 (S2). (3) Add the enemy-facing sense to
  §4.7 (C2). (4) Rebase §3.2 and §4.7 on HEAD: kit and meters via `TFHud`/`ITFHudMeters`, drinks via `IBotItemChoice`,
  rules onto the `ClassPlay` hooks (C1, C4). (5) Fix the `tf_bot_medic_heal.cpp` cite (S3).
- **soldier.md: ready after these fixes.** (1) The Escape Plan is filtered out at HEAD: get the filter lifted for the
  Soldier or remove 775 from B–E and rule C (C1c). (2) Reserve Shooter: no deploy window; swap on blast-airborne,
  back when grounded; the Direct Hit/Reserve mini-crit needs blast-airborne, not a jumper (So2). (3) Add the healed-by
  flag and the enemy facing to §4.7; replace "a sentry firing" with a sense that exists (So3, C2). (4) Rebase §3.1 on
  HEAD: slot fix done, flagged families via the choice, kit read landed (C1a, C1b, C1d). (5) Re-read row 730's
  evidence before the Beggar's release rule (So4).
- **pyro.md: ready after these fixes.** (1) Mark the cites as `225fe2e96` and drop §3.2/§4.2's slot-fix and kit-read
  asks as done (P1, P2, C1). (2) Add the enemy-facing sense to §4.7 (C2). (3) Ship A3 before A2, or trim A2's first
  slice to what exists (ship order). (4) Name cp_granary as the CP measure map.
