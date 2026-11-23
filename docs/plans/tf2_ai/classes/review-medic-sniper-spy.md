# Fable review: P7 research for the Medic, the Sniper and the Spy

Reviewer: Fable, 2026-11-20. Base checked: `git show 225fe2e96:<path>` for every repo citation (the working
tree has another lane's edits); stock at `../source-sdk-2013-valve`; wiki pages fetched this session. Brief:
`.scratch/brief-p7-review-research.md`. Findings are ranked by how much they would mislead the code lane.

## What held in all three files

- Repo citations: 30+ spot-checked per file. All but the handful listed below print the line claimed. Every gap
  claim was re-grepped at the base and reproduced: no `Uber` user outside `TFAbilities.cs` and
  `TFBotGameView.cs:130`; `chargeready`, `CallingForMedic|SaveMe`, `suspic`, `SpinBot` empty; `Reload` absent
  from `TFSniperRifleBehaviour.cs`; `SniperHold` only the enum; `src/ai/tests/recordings/` has no `human/`;
  `game/mod_tf2/maps/src/ai/` has six maps and no `ss_ai_tf_cp`; the arrow pushes `HitGroup.Generic`
  (`TFProjectileEntities.cs:1672`) and `HitGroup.Head` is read only in `TFDamageLayer.cs`.
- Stock numbers: every `tf_bot_*.cpp`, `tf_bot.cpp`, medigun, revolver, cloak, target-ID and death-notice
  citation checked says what the file says (ranges 75/250/600/1000; rosters at `tf_bot.cpp:833-872`; sniper
  400/200/5 s/10 s/2 s/25 u/0.01 rad/10°/s/0.1 s; spy 300/300/2500/5-10 s/150/40/0.99/0.25/tolerances by
  difficulty; medigun 8 s, 3 s, 75 %, 1,000 u; revolver 1.0 s).
- `items_working.tsv` at the base: works rows scout 75, heavy 67, engineer 66, pyro 66, soldier 62, demoman 61,
  spy 60, sniper 59, medic 40; only 155 and 351 `partial`; Medic 26/6/8 by slot; 30 `tf_weapon_sniperrifle`
  rows. All as stated.
- Wiki numbers (Medi Gun, Quick-Fix, Vaccinator, Sniper Rifle, Huntsman, Ambassador, Jarate) match the files,
  including the 8 s vs 9 s and 2.5 s vs 3 s discrepancies the Medic file itself flags.
- The foundation lists agree with each other: class pick and roster, the subtree hook in `Decide`, the kit
  read, the archetype draw, `SelfState` HUD fields, the facing on `ActorFacts` (Sniper and Spy), the
  friend-directed hold (Medic). A coordinator can merge them into one foundation brief without conflict.
- Ship orders start at the stock set, each later archetype changes a decision rule, and the live measures put
  objective time first.

A shared caveat: all three name a Payload or 5CP measure (cart metres per minute on pl_badwater; the Spy on
cp_granary). Payload and 5CP are P8 (`plan:1492`); in P7 only CTF on ctf_2fort has an objective feed. The
Medic file says "once P8 lands"; the Sniper and Spy files should say the same, and the Spy's first-objective
measure must run on ctf_2fort.

---

## Medic (`medic.md`)

### Findings

1. **The auto-caller channel is a flag, not a health value (honesty, high).** The coordinator ruled the honest
   reads are the heal target's health, the crosshair target ID's health, and the auto-caller at <= 75 % within
   1,000 u. Stock's auto-caller (`tf_weapon_medigun.cpp:2487-2562`, checked) draws a bubble on a teammate whose
   health ratio is at or under `hud_medicautocallersthreshold` (75) inside 1,000 u (`flDistSq < 1000000`,
   :2544), with no line-of-sight test, skipping the current heal target (:2511-2515) and including a disguised
   enemy Spy on our team (:2509). What the HUD shows is *that* a teammate is at or under 75 %, and where; not
   the number. The file reads it as a value: §4.4's opportunistic rule ranks "the most injured teammate … under
   50 % (fighting)", and fact 11 says "at 70 % within 1,000 u -> known". With only the ruled channels the bot
   knows "<= 75 %" for auto-callers and the exact ratio only for the heal target and the one under its
   crosshair. Rewrite: the channel carries `NeedsHeal` (bool, position) per teammate; the "most injured"
   ranking uses burning (rendered), the flag, and the two exact reads; the in-combat 50 % threshold applies only
   to the crosshair/heal-target reads, or the bot sweeps its crosshair to read. Fact 11 becomes "at 70 % within
   1,000 u -> flagged; its ratio is not known". Stock's `CFindMostInjuredNeighbor` (:354-381) reads server
   health for every known entity and must not be copied.
2. **The plan of record contradicts the ruling (plan, high).** `plan_bots_teams_tf2.md:617` (C.3 Medic row):
   "the brain reads the target's health only while healing them". The coordinator's three-channel ruling widens
   that. Before fact 11 is accepted as the honesty gate, the ruling goes into §8.1 Decisions dated, and the C.3
   row is amended; otherwise the plan's own text fails the fact. The file asks for the ruling (§4.3, §4.9 item
   7) but does not say the plan row has to change.
3. **The raw source must be gated (honesty, medium).** §3.1 names `PlayerResourceNetState.cs:106` (every
   slot's health) as "the honest source". It is the raw array; what is honest is the three HUD reads. The
   channel in the TF2 binding must expose only those, and a census fact should hold that nothing in
   `src/ai/tf2/server` reads `PlayerResourceNetState.Health` directly. (Also: stock's auto-caller reads
   `pPlayer->GetHealth()` off the player entity, :2520, not the resource; harmless, but the sentence "as stock's
   auto-callers … read" is not what stock does.)
4. **"Medic!" calls: humans only (design, medium).** §1.1, §4.4 and fact 3 keep stock's "only attend to HUMAN
   calls" (`tf_bot_medic_heal.cpp:131`). Our bots call for a Medic under 50 % (`TFDecider.Voice.cs:33`), the
   live measures run all-bot teams, and a player-equivalent Medic answers any teammate's call. Keep the 1,000 u
   rule but take any teammate's call (a human's may rank first). Otherwise the bot shows the file's own
   beginner mistake 6 against its own team.
5. **The advantage pop has no P7 trigger (buildability, medium).** §4.4 pops "with the team's push (C.6.1's
   rally push)". C.6.1 is P8 (`plan:1492`). Name a P7 trigger on CTF: the patient advancing past the front
   line, or the patient engaging two or more enemies, so the Über-advantage rule fires in the gated map.
6. **Stock's `IsGoodUberTarget` returns false for everyone (citation, low).** `tf_bot_medic_heal.cpp:428-440`
   ends `return false;` for every class, so stock's "keep the patient while ready with a good target"
   (:265-269) never fires. Cite the wiki for the Scout/Sniper/Spy/Engineer/Medic exclusion, not stock.
7. Citation drift: the speed match is `tf_player_shared.cpp:11005-11026` (the file's `:11029-11045` is the
   bonesaw and resource-level block). `TFVoiceTap.cs:28` holds the names; the mapping is `:58-69`.
8. Game knowledge: all checked numbers right. The Vaccinator's "much slower on overhealed" is -33 % (wiki) /
   `mult_medigun_overheal_uberchargerate 0.67` (tsv): say the number.

### Verdict: ready after these fixes
Fix 1 (flag, not value) and 2 (plan row + Decisions entry) before the code lane writes the channel; 4 and 5
before the decision rules; 3, 6, 7 are text.

---

## Sniper (`sniper.md`)

### Findings

1. **The lane is two lanes (ship order, high).** The file binds every aim rule to Phase 5b work (Fitts W per
   look, a zoom turn scale, `FireStyle.Settled`, a 20° scoped cone, Sniper reference sessions, a KS gate) and
   says 5b is not done (true: no `human/` recordings, no spin-bot detector in `src/ai`). Those are core changes
   with a byte-identical HL2MP replay constraint and a human-session dependency the code lane cannot satisfy
   itself. Make the split explicit in §4.1: lane 1 ships A's spots, holding, boredom, relocation, close threats,
   the `zoom` row, target choice and the charge-for-kill table under today's motion model with the error floor
   held at stock's 0.57° (§4.7's own fallback); lane 2 (core, 5b's owner) ships W, the turn scale, the settled
   style and the cone, gated on the user's sessions. Without the split the first archetype waits on a human.
2. **Wrong lines (citation, medium).** `TFDamageLayer.cs:181` and `:205-207` are cited for the Sleeper's
   no-crit-headshot and the Classic's full-charge rule; the arms are `:276` (`set_weapon_mode != 1`) and
   `:277-278` (`sniper_no_headshot_without_full_charge`), inside `:263-285`. `TFSpy.cs:545` for the Razorback is
   the tsv's stale evidence string; the block is `TFSpy.cs:810` and the fact is `TFKnifeTests.cs:73`.
   `TFAbilities.cs:47` is the destroy word; the rocket-jump row is `:51`.
3. **`TFKnowledge.AimHeight` misread (gap size, medium).** §3 says it "takes the *viewer's* class's eye".
   `TFKnowledge.cs:162-170` takes whatever class it is handed; `TFDecider.Feel.cs:91` hands the drawn target's
   class for `Body`, and the friend gate hands `Undefined` for `Head` (`:214`). A head aim point is one call with
   `AimPoint.Head, (TFClass)drawn.Class`; the gap is the decider's call site, not the knowledge table.
4. **Stock's immediate threats include every Medic (game knowledge, low).** `tf_bot_sniper_attack.cpp:218-222`
   returns true for a Medic ("always try to kill these guys first"); §1 lists only the aiming Sniper and the
   sentry. The design's Medic bonus covers it; the citation should say so.
5. **Honesty of the aim design: sound.** No health read (fact: "a mutant reading the server's health goes
   red"); class max health from the table and a seen Medic for the overheal guess; the hit decided by the
   hitbox and the drifting error; the zoomed turn rate from the client's own sensitivity scaling
   (`view.cpp:544-549`, checked); the 20° cone from stock's FOV re-read (`tf_bot_behavior.cpp:116`, checked);
   "the target is aiming at it" waits on the facing. The `AltFire` Tap hazard (`BotCommandApplier.cs:616-619`
   toggles every other tick) is real and the `zoom` one-shot row is the right fix. One soft spot: `aimErrorDeg`
   at level 10 is 0.12° (`tf2_skills.json` levels `[11.2 … 0.12]`, checked), and the file holds the Sniper
   floor at 0.57°; say which curve the Sniper section inherits for levels 0-9 so Easy is not also 0.57°.
6. Arithmetic: the body-flick Fitts example is 0.08 + 0.06·log2(1 + 10/4) = 0.19 s, not 0.17 s.
7. Measures: cart metres and pl_badwater defence need P8 (above). The Huntsman's `works` row 56 and the
   Heatmaker's 752 should drop to `partial` (the file says so; row 752's evidence already reads "grep empty:
   set_buff_type"), so the draw cannot hand a bot an item it cannot use: that flip is a game-lane commit.
8. Game knowledge otherwise right (27 %, 1.33 % crouched, 0.3 + 3.0 s, 0.2 s gate, 50-150 ×3, Huntsman 50-120 /
   1 s / 5 s / 1,800-2,600 / 53 %, Jarate 200 u / 20 s, Ambassador 1,200 u). The competitive page's Medic
   "always the highest priority" with immediate threats first is as quoted.

### Verdict: ready after these fixes
Fix 1 (the split) before the lane is cut; 2 and 3 before the code is written; the rest are text.

---

## Spy (`spy.md`)

### Findings

1. **The alias identity is right in principle, large in scope, and needs an owner (honesty/buildability,
   high).** The three leaks are real at the base: `TFBotGameView.cs:116` sets `NameSlot` only on a disguised
   enemy (everyone else 0 from `SnapshotBuilder.cs:115`), so `NameSlot != 0` is a Spy detector; the sighted id is
   the real entity (`sight.TargetOf(t)`, `:115`) and the row keeps `BoardTeam`, frags and grudge
   (`BotMemory.cs:388-394`, `Blackboard.cs:73-91`), so `a.BoardTeam != f.Team` is a detector; the kill feed
   carries the real id (`CombatTap.cs:147-157`). The plan's fact (`plan:619-634`) forbids a server-truth read
   and says the borrowed name "is the only part of the disguise that names a person" (`:654-656`); it says
   nothing about ids, so the alias is an extension of the plan, not a contradiction. Record it in §8.1 when
   ruled. Scope: `ActorId` ranges in the core, `SnapshotBuilder`, the blackboard's row key, `KillFeed`, the
   applier's aim resolution: core work with HL2MP's replay constraint. Two simplifications the file should
   take: the alias is per (Spy, disguise instance), shared by every viewer (a per-viewer alias buys nothing a
   player has and costs a table per bot); and the first commit is the smaller honest step, `NameSlot` = own
   slot for everyone (`TFBotGameView.Draw` and `SnapshotBuilder`) plus fact 2, before the id work.
2. **The feed and the feign (honesty, medium, confirmed).** `CombatTap` is an `IPlayerDeathHook`; the feign
   fires `DeathNotice.Fire` (`TFSpy.cs:415`) and skips the death hooks (`TFFeignRagdoll.cs:19-22`), so no bot
   sees the fake kill; `CombatTap.Died` sends every bot every death with no `silent_kill` check. Both holes are
   as stated. The TF feed should be built from the `player_death` notice (`TFDeathNotice.cs:145-153` sets
   `silent_kill`), per viewer team.
3. **The cloak button path (claim confirmed).** `TFWeaponBehaviour.cs:756-767` (base secondary) and `:683-688`
   (busy frame) call `ClassSpecialSkill`, which reaches `TFClassSpecialSkill.cs:58-61` →
   `ActivateInvisibilityWatch`. The comment at `TFSpyCommands.cs:96-100` is stale as the file says; a bot may
   press `IN_ATTACK2` on the revolver or knife to cloak or arm the Dead Ringer.
4. **Wrong lines (citation, medium).** The Kunai's heal is `TFSpy.cs:835-846` (not `:470-475`, which is the
   disguise reset). `crit_dmg_falloff` is `TFDamageModifyRules.cs:382-387` (`:349` is `set_nocrit_vs_nonburning`).
   The silent kill is `TFDeathNotice.cs:145-153` (`:130` is the tsv's string). The "-1 no target" arm is after
   `TFSpy.cs:598`, not `:592-594`.
5. **Dead Ringer numbers (game knowledge, low).** Wiki: the triggering hit does 75 % less; resistance then starts
   at 65 % and fades to the cloaked 20 % over 3 s; speed boost about 33 % for 3 s; the meter drops to 50 %;
   ammo and dispensers do not refill it. The file's "75 % less damage for the first 3 s, then 20 %" conflates
   the hit and the fade. Not load-bearing for the rules, but the code lane will quote it.
6. **Wiki attributions (low).** The Community page does not say "extremely powerful" (it says "vastly improved
   survivability"); the competitive page does not rank Demoman over Medic (it calls both vital). Keep the
   ordering as a design choice marked **measure**, not as a quote.
7. **A3 drifts from the plan's row (ship order, low).** `plan:749` Saboteur is "Sapper first" on stock items;
   the file's A3 is Diamondback + Red-Tape Recorder. Ship A3 on stock items first (the ordering rule is the
   behaviour), with the two unlocks as a later delta; otherwise the second archetype needs two kit reads
   before it exists.
8. **Honest reads otherwise.** Every cue in §4.1 is a rendered or heard thing; the Spy's own target-ID health
   read is correctly raised as a ruling (stock `tf_hud_target_id.cpp:380`, checked). The disguise-class filter
   "weight 0 for a class whose only known enemy is in sight" predicts the server's `FindDisguiseTarget`; a player
   learns that rule, so it is fair, but note the bot can also read its own disguise name off its HUD after
   disguising (listed as a missing `SelfState` field) and should.
9. Measures: cp_granary is 5CP (P8). The objective measure in P7 runs on ctf_2fort.

### Verdict: ready after these fixes
Fix 1 (scope, owner, the two simplifications, the Decisions entry) before the lane is cut; 4 before the code
is written; 7 and 9 in the ship order and measures; 5, 6 are text.

---

## Summary table

| file | repo citations | gaps real | game knowledge | buildable | ship order | measures | verdict |
|---|---|---|---|---|---|---|---|
| medic | 30 checked, 1 drift | yes (G1-G3, A1-A9 reproduced) | right | yes, after the flag/value fix and a P7 pop trigger | stock first, deltas real | objective first; Payload is P8 | ready after fixes 1-5 |
| sniper | 35 checked, 4 wrong lines | yes (arrow headshot, Focus, SniperHold, maps, recordings) | right (+ stock's Medic threat) | yes, once split from 5b | stock first, deltas real | objective first; Payload is P8 | ready after fixes 1-3 |
| spy | 35 checked, 4 wrong lines | yes (NameSlot, id, feed, feign; cloak button confirmed working) | right (DR numbers soft) | yes, with the alias scoped and owned | stock first; A3 should start on stock items | objective first; cp_granary is P8 | ready after fixes 1, 4, 7, 9 |
