# P7 research: the Medic

Lane: P7 research, Medic. Base: main `225fe2e96`.
Repo claims cite `path:line` at that commit; stock claims cite `../source-sdk-2013-valve` (`valve-sdk.lock`);
game knowledge cites URLs listed in section 5 as [W1]...[W8]. Numbers marked **(measure)** are proposals to
confirm in a scenario or live game before they become constants.

---

## 1. The Medic's job, by mode

### 1.0 What holds in every mode

Class facts: 150 health, 107 % speed (320 u/s), base regen 3 hp/s rising to 6 hp/s between 5 and 10 s
after the last hit, doubled while beaming a hurt patient (port: `src/tf2/server/Player/TFPlayerItemRegen.cs:121-135`).
The Medi Gun heals 24 hp/s on a patient hurt within 10 s, rising linearly to 72 hp/s at 15 s unhurt
(the "crit heal"), overheals to 150 % of base health, connects at 450 u and breaks at 540 u [W2]
(port: the stick rule `StickRangeMult = 1.2f`, `src/tf2/server/Medigun/TFMedigunWeapon.cs:84`, `:550`).
ÜberCharge builds at 2.5 %/s (40 s), half that on a patient at or over 142.5 % (0.95 x the buffed max),
split by the number of healers, x3 in setup [W2]; port `TFMedigunWeapon.cs:486-532` (0.95 at `:496`, healer
split `:498-500`, setup x3 `:529-530`). The release lasts 8 s (`weapon_medigun_chargerelease_rate` "8",
stock `src/game/shared/tf/tf_weapon_medigun.cpp:77`). The Medic matches a faster patient's speed (port
`src/tf2/shared/Movement/TFMaxSpeed.cs:302-318`, stock `tf_player_shared.cpp:11029-11045`).

Rules of the game a Medic plays around:
- An invulnerable or megahealed player cannot capture a point or push a cart (stock
  `tf_gamerules.cpp:16853`; port `src/tf2/server/Player/TFCapturePlayerRules.cs:52-53`), and cannot pick up
  the intelligence (port `src/tf2/server/Entities/Ctf/ItemTeamFlag.cs:609`). Kritz and Vaccinator charges
  do not stop either [W3].
- A flag carrier receives no standard charge effect (port `src/tf2/server/Medigun/TFPlayerHealing.cs:396-397`);
  the Medic cannot deploy while carrying the intelligence [W2].
- Charge is lost on death (except the Vita-Saw's up to 60 %) [W1][W4].
- A Medic beam heals a disguised enemy Spy whose disguise team is the Medic's (port
  `TFMedigunWeapon.cs:694-703`, stock `:563-599`): a human Medic is fooled the same way.

Stock TFBot's Medic (`src/game/server/tf/bot/behavior/medic/tf_bot_medic_heal.cpp`) is the numeric
reference throughout section 4. Its ranges: stop-follow 75 u, start-follow 250 u, max heal range 600 u,
call-response range 1,000 u (`:21-25`).

### 1.1 Pub and competitive, the common core

**Where he stands, what range.** Behind the patient, inside the beam's 450 u, ideally behind cover the beam
bends round [W1][W6]; "stay in the thick of combat, not the edge" so teammates can reach him [W3]; never past
the front line [W1]. Out of combat, behind the patient's facing (stock `tf_bot_medic_heal.cpp:937-947`: if the
patient has not fired for 5 s and the Medic is in front, go to 75 u behind). Under enemy sight, the nearest spot
within heal range no known enemy can see (stock `:958-1047`). He fights only when he must: syringe gun under
about 800 u when retreating, crossbow at range [W3][W6].

**Who he heals (heal priority).**
- In a fight: whoever is under fire, before whoever is merely hurt [W1][W6]; burning teammates first
  (stock `tf_bot_medic_heal.cpp:356-381`, "fire trumps").
- Before a fight: overheal everyone, in the order they will leave him (Scout, Demo first; slower classes
  last) [W1][W3]. 5CP rollout order: Demo first, then Scouts, the roaming Soldier, the pocket last [W3].
- A primary patient (the "pocket"): stock prefers Heavy = Soldier = Pyro, then Demoman, never a Medic,
  Sniper, Engineer or Spy as primary, always the flag carrier (stock `:60-74`, `:224-233`). 6s pocket is a
  Soldier [W3][W4]; Highlander's combo is Medic, Demo, Heavy, Pyro [W4].
- A human calling "Medic!" within 1,000 u overrides class rank (stock `:130-176`).
- Not one patient all the time: spread heals [W1].

**Push, hold, retreat.**
- Push when his team has Über and the enemy does not, or the enemy is down players [W3][W4]. An advantage under
  10 % is no advantage [W4].
- Hold (build) while both sides build; heal teammates who damage themselves (Soldiers' rockets) to build faster [W3].
- Pop: when the team pushes, when the patient falls under half health under fire, or to save himself: popping
  early beats dying with a full bar [W1][W6] (stock rules `:631-701`).
- Trade: when the enemy pops, pop just after, so both charges fade together [W4].
- Retreat when the nearby team is dead [W6] or the pocket is about to die; find another protector [W4].
  Stock retreats to spawn when no patient is known (`tf_bot_medic_retreat.cpp`, `tf_bot_medic_heal.cpp:521-522`).

**Movement tech.** Jump, duck and strafe [W6]; damage-surf (crouch-jump into knockback) to escape [W1]; cross a
Sniper line unpredictably or while he reloads [W1]; with the Quick-Fix, ride the patient's blast jumps [W3].

**Teamwork.** The Combo: the pocket stays close and guards the Medic [W4]; Engineer nests give him cover and a
dispenser, but are a trap against an enemy Über push [W1]; he calls Spies, Snipers and flankers [W1]; he tells the
team his Über ("ÜberCharge ready", voice menu X 8 [W7]) and is told "Activate charge!" (X 7) by his patient;
stock pops on the patient's "Go!" or "Activate charge" (`tf_bot_medic_heal.cpp:833-845`).

**What beats him, and how a good Medic plays round it** [W5][W4]:
- Being focused first (everyone): stays in the middle of his team, keeps his own health high (Scouts two-shot a
  hurt Medic [W3]).
- Scouts flanking: stays by the pocket; heals himself to full with regen in lulls.
- Soldiers and Demos jumping in to bomb him: keeps under cover from above; Quick-Fix charge ignores knockback.
- Knockback (rockets, airblast) breaking the beam: re-latches at once, stays within 450 u.
- Spies: backs to a wall or a teammate, looks behind while the beam holds (the beam stays locked) [W1].
- Snipers: never stands in a long line; crosses it moving.
- Forced Über (damage on the Medic to make him pop in a bad place) [W4]: pops only when the pop does something
  (a push, saving the patient, his own life under 25 hp), not at the first damage.
- Ubersaw/Vita-Saw Medics: enemies keep out of melee [W5]; ours does not seek melee [W6].

### 1.2 By mode

| Mode | Pub | Competitive |
|---|---|---|
| CTF | Heal the flag carrier always (stock `:224`); the carrier takes no Über (`TFPlayerHealing.cs:396-397`), so Über goes on his escort. Defending: heal at the intelligence room or the nest. Never pick up the flag (no Über while carrying). | Rare in comp. |
| 5CP (symmetric) | Overheal at spawn and rollout; push mid with the pocket; Über to break a held point; no cap while invulnerable: pop to clear, then the cappers stand on it after or round the Medic. | Rollout overheal order (Demo first) [W3]; mid fight: Kritz pops fast on a loaded Soldier/Demo [W3]; Über advantage decides pushes to the next point [W4]. |
| A/D CP, attack | Build in setup (x3), pop on the choke's sentry nest with a Heavy/Soldier/Demo. | Same; trade Über with the defending Medic. |
| A/D CP, defend | Sit by the sentry and dispenser (cover from Scouts) [W3]; pop when the attack's Über arrives or the point is about to fall. | Same. |
| KOTH | Hold Über until the enemy pops or loses its Medic; pop to retake the point, the cap goes to the un-ubered. | As 5CP mid. |
| Payload, attack | Heal the cart pushers; the cart also heals, sharing build rate [W1]; an ubered player does not push, so pop to clear the defence ahead of the cart. | Same. |
| Payload, defend | Build in setup; pop at the last point or when the enemy pops. | Same. |
| Arena | No respawn: the Vita-Saw is useless [W6]. (Arena is not ported: plan Part F.) | — |

### 1.3 Beginner mistakes that make a bot read as a bot

1. Following the patient into his face, or standing in front of him; never using cover.
2. Holding a full Über until death ("dropped Über" [W4]); or popping the instant it is full on nobody.
3. Ubering a Scout, Sniper, Spy, Engineer or another Medic (stock refuses these as good Über targets,
   `tf_bot_medic_heal.cpp:428-439`), or ubering a flag carrier, or standing on a point while ubered.
4. Beam flicker: retargeting every frame (stock holds the trigger off 1-2 s to retarget, `:616-628`).
5. Fighting with the syringe gun while a hurt teammate is in reach; melee-chasing for Ubersaw charge [W6].
6. Ignoring "Medic!" calls; healing only the pocket while others die.
7. Walking straight through a Sniper's line; no glance behind for Spies.
8. Popping a Kritz on a sentry [W6], or a Vaccinator bubble of the wrong type.
9. Never saying "ÜberCharge ready" or "Spy!".

---

## 2. Play styles by weapon set

The Medic's rows in `game/mod_tf2/scripts/items/items_working.tsv` are 40, all `works`: primary 6, secondary 26,
melee 8. Attribute values below are from the shipped schema (`bin/debug/content/tf/scripts/items/items_game.txt`,
item or prefab blocks: `weapon_kritzkrieg`, `weapon_ubersaw`, `weapon_amputator`, `weapon_crusaders_crossbow`).

**Reskins (same `item_class`, no attributes), grouped under the base item:**
- Medi Gun (`tf_weapon_medigun`, def 29): 211, 663, 796, 805, 885, 894, 903, 912, 961, 970, 15008, 15010,
  15025, 15039, 15050, 15078, 15097, 15120, 15121, 15122, 15145, 15146 (23 rows with 29).
- Syringe Gun (`tf_weapon_syringegun_medic`, def 17): 204.
- Bonesaw (`tf_weapon_bonesaw`, def 8): 198, 1143.
- Ubersaw 37 = Festive Ubersaw 1003 (same attributes); Crusader's Crossbow 305 = Festive 1079 (adds
  `override_projectile_type`, visual).

**The behaviour rows:**

| def | name | item_class | slot | attributes (value) |
|---|---|---|---|---|
| 29 | Medi Gun | tf_weapon_medigun | secondary | — (8 s invulnerability) |
| 35 | The Kritzkrieg | tf_weapon_medigun | secondary | `set_charge_type` 1 (crits), `mult_medigun_uberchargerate` 1.25 |
| 411 | The Quick-Fix | tf_weapon_medigun | secondary | `set_weapon_mode` 2, `mult_medigun_healrate` 1.4, `set_charge_type` 2 (megaheal), `mult_medigun_uberchargerate` 1.1, `mult_medigun_overheal_amount` 0.5 |
| 998 | The Vaccinator | tf_weapon_medigun | secondary | `set_weapon_mode` 3, `mult_medigun_uberchargerate` 1.67, `mult_medigun_overheal_uberchargerate` 0.67, `overheal_fill_rate` 0.34, `set_charge_type` 3, `medigun_{bullet,blast,fire}_resist_passive` 0.1, `..._deployed` 0.75 |
| 17 | Syringe Gun | tf_weapon_syringegun_medic | primary | — |
| 36 | The Blutsauger | tf_weapon_syringegun_medic | primary | `add_onhit_addhealth` 3, `add_health_regen` -2 |
| 412 | The Overdose | tf_weapon_syringegun_medic | primary | `mult_player_movespeed_resource_level` 1.2, `set_weapon_mode` 1, `mult_dmg` 0.85 |
| 305 | The Crusader's Crossbow | tf_weapon_crossbow | primary | `set_weapon_mode` 1, `mult_maxammo_primary` 0.25 |
| 8 | Bonesaw | tf_weapon_bonesaw | melee | — |
| 37 | The Ubersaw | tf_weapon_bonesaw | melee | `add_onhit_ubercharge` 0.25, `mult_postfiredelay` 1.2, `set_weapon_mode` 1 |
| 173 | The Vita-Saw | tf_weapon_bonesaw | melee | `ubercharge_preserved_on_spawn_max` 0.6, `add_maxhealth` -10, `set_weapon_mode` 2 |
| 304 | The Amputator | tf_weapon_bonesaw | melee | `provide_on_active` 1, `mult_dmg` 0.8, `enables_aoe_heal` 1, `add_health_regen` (wiki: +3 hp/s held [W1]) |
| 413 | The Solemn Vow | tf_weapon_bonesaw | melee | `mult_postfiredelay` 1.1, `see_enemy_health` 1 |

Today a Medic bot's secondary is a uniform draw over 26 rows, 23 of them the stock gun's behaviour, so 88 % of
Medic bots would be stock Über and 1 in 26 each Kritz, Quick-Fix and Vaccinator; the primary and melee are drawn
independently (`src/tf2/server/Econ/BotInventory.cs:94-108`, `PickIndex` per class and slot).

### 2.1 The archetypes

**A. Über (stock, the Combo's Medic).** Medi Gun (29 or reskin) + Crusader's Crossbow (305) or Syringe Gun (17)
+ Ubersaw (37) or Bonesaw (8); Vita-Saw (173) as a melee variant in respawn modes. The competitive standard
[W3]; stock fallback for every other archetype. Decisions: a primary patient (Soldier/Heavy/Pyro/Demo), build
fastest by healing the hurt, hold to the Über-advantage rule, pop to push or to save, trade on the enemy's pop.
Crossbow: heal a teammate beyond 540 u with a bolt (75-150 healing by distance [W3]) and pick at range. Ubersaw:
melee only a target already in reach (under 96 u **(measure)**) when the bar is under 75 % and the Medic is over
half health; never walk to melee [W6].

**B. Kritz (Kritzkrieg 35).** + Crossbow + Ubersaw/Bonesaw. 32 s to full [W1]; 8 s of crits on the patient, no
invulnerability. Decisions: patient must be a Soldier, Demoman or Heavy (wiki [W1]); pop fast on a patient
with an enemy group in sight, not into a sentry [W6] nor into an enemy Medic's pop (a standard Über negates it
[W3]); can cap and push while charged [W3], so a Kritz pop may go on a capper. The Medic stays further back
than with Über (he is not protected).

**C. Quick-Fix (411).** + Crossbow or Overdose + Amputator or Bonesaw. 33.6 hp/s heals, 125 % overheal cap, an
8 s megaheal that triples heals and ignores knockback and stuns [W1][W3]. Mirrors the patient's blast jumps and
shield charge [W3]. Decisions: the patient is a jumper (Soldier or Demo) or a Scout; follow his route closely
(stop-follow 75 u, not 250); pop when the patient is under half health and hurt, or when knockback would break
the beam (Pyros, Soldiers); never the Über-advantage push (no invulnerability, cannot cap while charged [W3]).

**D. Vaccinator (998).** + Crossbow + Ubersaw/Amputator. Passive 10 % resist of one type on patient and Medic;
reload cycles bullet -> blast -> fire (port `TFMedigunWeapon.cs:220-228`, `:255`); a pop spends one quarter
(`ResistChunkSize`, `:91`, `:730`) for a bubble of 75 % of that type (wiki: 2.5 s [W3]; stock code default
`weapon_vaccinator_resist_duration` "3", `tf_weapon_medigun.cpp:89`; the port uses 3, `TFMedigunWeapon.cs:92`).
Builds 67 % faster on hurt patients, much slower on overhealed ones [W1]. Decisions: keep the resist matched to
what is hitting the patient (bullet by default [W1]); pop a chunk when the patient or Medic took damage of
that type within 1 s (stock `tf_bot_medic_heal.cpp:635-647`); stay where patients are hurt (builds on hurt
targets [W6]). The hardest archetype; ships last.

**E. Battle Medic (long tail).** Blutsauger (36) or Overdose (412) + Medi Gun + Amputator (304) or Solemn Vow
(413). Pub only. Decisions: fights more (syringes at under 800 u), Blutsauger hits heal 3; Overdose held while
travelling for up to 20 % speed at a full bar (port measured 320 -> 384 u/s, `items_working.tsv` row 412);
Amputator taunt (aura heal 25-75 [W1]) with two or more hurt teammates in 450 u and no enemy in sight; Solemn
Vow reads enemy health and the enemy Medic's Über [W1] (an honest read once that HUD exists).

---

## 3. What the port and the AI can do today

### 3.1 Game side (`src/tf2/`)

| Behaviour | Status | Where |
|---|---|---|
| Beam: hold-to-heal, find by aim trace, stick to 1.2 x range, recheck each 1 s | works | `TFMedigunWeapon.cs:469-596` |
| Autoheal toggle | reads the client's `tf_medigun_autoheal`, so a bot (no client convars) holds the trigger | `TFMedigunWeapon.cs:189` |
| Charge build, 0.95 crawl, healer split, setup x3, item rates | works | `TFMedigunWeapon.cs:486-532` |
| Über pop (`IN_ATTACK2`), Kritz crits, Quick-Fix megaheal, Vaccinator bubbles | works | `TFMedigunWeapon.cs:711-784`; charge rows `TFPlayerHealing.cs:332-340`; Kritz pressed live (tsv row 35) |
| Vaccinator resist cycle on reload | works | `TFMedigunWeapon.cs:220-228`, `:255-280` |
| No Über on a flag carrier; ubered cannot cap / take the flag | works | `TFPlayerHealing.cs:396-397`; `TFCapturePlayerRules.cs:52-53`; `ItemTeamFlag.cs:609` |
| Medic regen, speed match, Overdose speed | works | `TFPlayerItemRegen.cs:121-135`; `TFMaxSpeed.cs:302-330` |
| Crossbow healing bolt, Blutsauger, Ubersaw, Vita-Saw, Amputator aura, Solemn Vow target ID | works (tsv evidence) | `items_working.tsv` rows 36, 37, 173, 304, 305, 413 |
| Heal target networked | yes | `src/tf2/shared/Player/TFPlayerNetState.cs:403` (`HealTargetIndex`) |
| Quick-Fix mirrors the patient's blast jump | **missing**: stock `server/tf/tf_player.cpp:9797-9829` gives the Quick-Fix healer the jumper's velocity; no arm in the port (`grep -rn "PipeJumping\|QuickFix" src/tf2/server` finds only the speed and decay arms) | game gap G1 |
| "ÜberCharge ready" auto line when the bar fills | **missing**: stock speaks `MP_CONCEPT_MEDIC_CHARGEREADY` and `MP_CONCEPT_HEALTARGET_CHARGEREADY` at the threshold (`tf_weapon_medigun.cpp:1357-1362`); `grep -rni chargeready src/tf2` is empty | game gap G2 |
| "Medic!" caller state and bubbles (`IsCallingForMedic`, `CreateSaveMeEffect`, auto-callers) | **missing**: `grep -rn "CallingForMedic\|SaveMe" src/tf2` empty; stock auto-callers `tf_weapon_medigun.cpp:2487-2562` (threshold 75 %, 1,000 u, `:203-204`) | game gap G3 (HUD only; the voice sound already reaches bots) |
| Teammate health on the client | yes: the core player resource networks every slot's health (`src/shared/managed/PlayerResourceNetState.cs:106`), as stock's auto-callers and target ID read | the honest source for 4.3 |

### 3.2 AI side (`src/ai/tf2/server/`, `src/ai/core/`)

| Need | Today | Gap |
|---|---|---|
| Medic subtree | every class runs the Soldier's (`Trees/TFClassTrees.cs:15`) | A1 |
| Class pick | every bot is a Soldier (`TFBotClassPick.cs:22`, `:55-58`) | A2 (foundation) |
| Über one-shot | the row exists, `Uber` = `IN_ATTACK2` x 2 ticks (`Knowledge/TFAbilities.cs:20`, `:53`); nothing uses it (`grep -rn Uber src/ai/tf2/server` hits only that file and the condition read) | A3 |
| Hold the beam on a teammate | the hands fire only at an enemy target (`Trees/TFDecider.Soldier.cs:92-119`), and the friend gate cancels any `Fire` with a teammate on the line (`Trees/TFDecider.Feel.cs:205-218`): a heal would always be held | A4 (foundation) |
| Weapon choice | `Hands` selects the Soldier's launcher whenever it is not in hand (`TFDecider.Soldier.cs:137-140`, kinds from names at `TFDecider.cs:116-117`): a Medic would ask for a weapon he does not own once a second | A5 (foundation: the kit read) |
| Own charge %, heal target, resist type | `SelfState` has none (`src/ai/core/Snapshot/PerceivedView.cs:33-48`); the HUD read adds class, conditions, metal only (`Senses/TFBotGameView.cs:60-68`); the HUD has the meter (`src/tf2/interface/TFUiSources.cs:654-666`) | A6 |
| Teammate health | `ActorFacts` carries no health on purpose (`PerceivedView.cs:80-100`) | A7: the TF2 client does see teammates' health (3.1); needs a ruling and a channel |
| Who another Medic is healing, an enemy Medic's full bar | not in `ActorFacts`; the beam and the charged glow are rendered (`src/tf2/client/Weapons/TFMedigunBeam.cs`, `docs/divergences.md:135-136`) | A8 |
| "Medic!" calls heard | yes: `Senses/TFVoiceTap.cs:28` maps `*.Medic*`/`*.HelpMe*` to `CalloutKind.Help`; bots call under 50 % health (`Trees/TFDecider.Voice.cs:33`) | "Activate charge"/"Go" lines not mapped (A9) |
| Patient hurt cue | `AiEvent.AllyDamaged` exists (`src/ai/core/Events/AiEvent.cs:219`) but only tf2_descent produces it | derive from the health read (A7) |
| Invulnerable seen | yes (`TFBotGameView.cs:130`, `ActorConditions.Invulnerable`) | crit-boost and megaheal not drawn as conditions |
| Damage type taken (Vaccinator) | `AiEvent.Damaged(..., damageType)` (`AiEvent.cs:200`) for the bot itself | the patient's damage type is not known: infer from the shooter's class/weapon seen |
| Cover from enemy sight | the follower and the fight's cover (`LoadInCover`) exist for the Soldier | a "hidden spot within R of the patient" query (4.4) |
| Retreat, health | Survive branch (`TFDecider.cs:369-374`) | medic-specific thresholds |

---

## 4. The design for the code lane

### 4.1 Archetypes to ship, in order

1. **Über, stock kit** (Medi Gun 29, Syringe Gun 17, Bonesaw 8): the fallback and the first gate.
2. **Über, competitive kit** (Medi Gun, Crossbow 305, Ubersaw 37): crossbow heals and Ubersaw rules on top of 1.
3. **Kritz** (35 + 305 + 37/8).
4. **Quick-Fix** (411 + 305 + 8): the follow-the-jumper rules; its jump mirroring waits on game gap G1.
5. **Vaccinator** (998 + 305 + 37).
6. Long tail: Battle Medic (36/412 + 304/413), Vita-Saw swaps.

### 4.2 The draw and the loadout

A Medic bot draws the archetype at spawn from its seed, weighted, then the loadout follows from the archetype's
preference list per slot (a coherent set), with reskins substituted uniformly by the same seed (looks, C.3.3).
Initial weights (pub): Über 55 (stock kit 25, comp kit 30), Kritz 15, Quick-Fix 15, Vaccinator 10, Battle 5
**(measure: a design choice, not a source)**. A row whose item is not `works`, or refused by
`ItemIsAllowed`, falls back to the stock item. The archetype lives in `tf2_subclasses.json` per plan C.3.1
(the Medic rows there: Über, Kritz, Quick-Fix, Vaccinator, plan line 738-741). Kept until class change or map
change (C.3.1). The bot's archetype is read from its own kit at spawn (the HUD's weapon list), never a label
passed in, so a human-equipped bot or a mode restriction gives the right behaviour.

### 4.3 Honest reads (the Medic's senses)

- **Own HUD:** charge 0..1, releasing, heal target slot, Vaccinator resist type (A6).
- **Teammate health** (A7), proposed rule, all of it a client's: the heal target's health at all times (the
  heal-target panel); a teammate the bot aims at within the target-ID range (the target ID shows teammate
  health); and an "auto-caller" for any teammate at or under 75 % within 1,000 u (stock's
  `hud_medicautocallers` rule, `tf_weapon_medigun.cpp:2487-2562`; a player setting a Medic may turn on, the
  bot is ruled to have it on). Everyone else reads "unknown, assumed full". This needs the user's ruling
  against `ActorFacts`' no-health rule; the channel is TF2-binding data beside the view, not an `ActorFacts`
  field.
- **Patient hurt:** the heal target's health fell since the last decide, or an enemy in sight is firing at him.
- **Enemy Über estimate:** per enemy Medic, 0 at his death (kill feed) or at a seen pop (`Invulnerable` on an
  enemy), +2.5 %/s while alive (x3 in setup, x1.25 if seen holding a Kritz), 1.0 when his gun's charged glow is
  in sight (A8). Comp's "assume Über after about 40 s" [W1].

### 4.4 Decision rules (Über; others are deltas in 4.5)

Patient choice, each decide (stock `tf_bot_medic_heal.cpp:249-295`):
- Keep the patient while releasing, or while ready with a good Über target (`:260-269`).
- Candidates: sighted or remembered teammates, not Medic/Sniper/Engineer/Spy unless carrying the flag (`:224-233`).
- A candidate already beamed by another Medic is skipped (`:110-128`; another Medic's patient is visible, A8).
- A human "Medic!" call within 1,000 u wins (`:130-176`; travel distance under 1,500 u by the graph).
- Rank Heavy = Soldier = Pyro (0) > Demoman (3) (`:60-74`, `:180-188`); same rank: switch only if 300 u closer
  (`:193-194`); better rank: switch if within 750 u (`:200-204`).
- Payload attack: a candidate on the cart's push zone ranks first **(measure)**; CTF: the carrier first (stock).

Momentary heal target (opportunistic, `:589-604`): when the patient is stable (unhurt 3 s, at or over max
health, not burning, `:302-321`) and no release runs, heal the most injured teammate within 0.9 x 450 = 405 u
in line of fire, burning first, if under 50 % (patient fighting, fired in the last 1 s) or under 100 % (not
fighting); with only the honest reads of 4.3.

Beam discipline: aim at the target's centre and hold `IN_ATTACK` (A4). When the gun is on someone else, let go
for 1-2 s (random) to retarget, else keep building on the wrong one (`:609-629`).

Weapon: the medigun out unless (no Über ready, not ubered, not actively healing) and (an enemy nearer than the
patient, the patient beyond 1.1 x 600 = 660 u, or the patient's eyes out of line of fire) (`:735-756`); then the
primary at the threat (crossbow over 800 u, syringe under). Comp kit: a crossbow bolt at a teammate over 540 u
away known under 75 % **(measure: arc and lead from the bolt's `WeaponFacts`)**.

Position (`:758-782`, `:902-1047`):
- Ready, ubering, or the beam lost: close to within 75 u and sight of the patient.
- Else, every 0.5-1 s or when visible to an enemy: not visible -> in range (250 u and seen) stay, behind the
  patient (75 u back) if he is quiet 5 s and the bot is in front; out of range -> go to him. Visible -> the
  nearest hidden point on rings from 75 u (+ up to 100 u random) by 100 u to 600 u (250 u if the patient moved
  over 200 u in the last 3 s, or Über ready), 16 directions, from the patient's centre, no lower than a step.
  The visibility test uses only enemies the bot knows of.

Pop (stock `:631-701`, then ours):
- Not in setup; bar at least the gun's minimum (1.0; 0.25 Vaccinator) (`:412-424`).
- Pop when: the patient is under 50 % of max health and not already invulnerable/megahealed (`:650-658`); the
  Medic is under 50 health and hurt in the last 1 s (`:660-667`, threshold `tf_bot.cpp:4968-4978`); under 25
  health always (`:669-673`); the patient said "Go!" or "Activate charge!" (`:833-845`).
- Ours, from [W3][W4]: pop with the team's push (C.6.1's rally push) when our Über leads the enemy estimate by
  10 % or more, or the enemy has no live Medic; pop within 1 s **(measure)** of an enemy Über seen within
  1,500 u heading for us (the trade); hold otherwise.
- Never: on a flag carrier patient (gets nothing) unless to save the Medic; while standing to cap or push.
  After a pop on a point, the bot does not stand in the capture zone; ubered cappers leave it to others.
- Skill: a delay before the pop by Reaction (stock has `GetUberDeployDelayDuration`, `tf_bot.cpp:4981-4992`).

Survive and retreat:
- A Medic does not take the Survive branch while beaming in combat; his regen covers lulls.
- Retreat to the nearest teammate, else spawn, when no teammate is known within 1,000 u (stock retreat) or the
  last teammate in sight died within 3 s **(measure)** [W6]; with a bar over 50 % the threshold to break off
  any fight drops (he carries the team's push).
- Glance behind for Spies every 3-6 s while beaming **(measure)** (the beam holds without aim, [W1]: it checks
  the stick rule only, `TFMedigunWeapon.cs:550-557`, aim rechecked once a second).

Class keep: a Medic over 25 % charge does not change class on respawn (stock `tf_bot.cpp:1164-1171`).
Composition: a Medic once the team has 4, one per 4 (stock `tf_bot.cpp:826`, `:840`).

### 4.5 Per-archetype deltas

| Archetype | Changes |
|---|---|
| Über comp kit | crossbow heal bolts (above); Ubersaw melee when an enemy is under 96 u, bar under 75 %, health over 75 **(measure)**; prefer healing self-damaging Soldiers to build (W3) |
| Kritz | good Über target = Soldier/Demo/Heavy only; pop when the patient sees 2 or more enemies within 900 u and no enemy sentry in sight and no enemy Über seen in the last 8 s; may pop on a capper; hold 100-150 u further back than Über **(measure)** |
| Quick-Fix | patient preference Soldier/Demo/Scout; stop-follow 75 u, start-follow 150 u **(measure)**; pop under 50 % patient health, or on a knockback/airblast seen on the patient; never the advantage push |
| Vaccinator | keep the resist matched: bullet default; blast if the last hurt (self `Damaged` type, or the shooter seen at the patient is a Soldier/Demo) was blast; fire if a Pyro or burning; one reload press per step, at most once a second; pop a chunk on hurt-by-that-type within 1 s (stock `:635-647`); stays with hurt patients |
| Battle | fights in reach 800 u when no teammate under 75 % is known; Overdose out while travelling over 600 u with no enemy known; Amputator taunt rule (2 hurt in 450 u, no enemy, bar under 100 %) |

### 4.6 Ability rows needed

- `uber` exists (`TFAbilities.cs:53`); used by every pop.
- `heal_hold`: `IN_ATTACK` held at a friend, exempt from the friend gate (or a new `ActionKind`/`FireStyle`
  the gate skips), A4.
- `vacc_cycle`: one `IN_RELOAD` press (the existing `ActionKind.Reload` may do it; the medigun has no clip).
- `voice_uber_ready` (`voicemenu 1 7`) and `voice_activate_charge` (`voicemenu 1 6`) [W7], beside the four in
  `TFAbilities.cs:23-31`.
- `taunt` (Amputator aura) as the mod's `taunt` command, long tail.

### 4.7 Facts to write (each red once with a mutant)

1. A Medic bot with a hurt sighted Soldier in 450 u holds `IN_ATTACK` aimed at him; mutant: friend gate on -> red.
2. Primary patient: Heavy beats Demo within 750 u; same rank keeps current unless 300 u closer; flag carrier
   always; Sniper never primary.
3. A human "Medic!" within 1,000 u wins over class rank; at 1,200 u it does not.
4. Opportunistic: stable patient + burning teammate in 405 u -> burning one healed; unstable patient -> not.
5. Pop: full bar + patient at 40 % health -> pop; at 60 % -> no pop; in setup -> no pop; self 20 hp -> pop.
6. No pop on a flag-carrier patient with the Medic healthy.
7. Über advantage: our 1.0 vs enemy estimate 0.95 -> hold; vs 0.5 with a rally push -> pop.
8. Trade: enemy `Invulnerable` seen at 1,200 u closing, our bar full -> pop within 1 s.
9. Position: visible to a known enemy -> the goal is a point no known enemy sees, within 600 u of the patient.
10. Weapon: patient at 700 u and an enemy at 300 u -> primary out; patient back at 200 u -> medigun.
11. Honesty: a teammate at 80 % health, not aimed at, not the heal target -> reads unknown; at 70 % within
    1,000 u -> known.
12. Kritz: no pop with an enemy sentry in sight; pop with two enemies in 900 u.
13. Vaccinator: blast damage taken -> resist cycles to blast in at most 2 presses; bubble pops on blast hurt.
14. Class keep: Medic at 30 % charge does not change class at death; at 20 % may.
15. Archetype draw: same seed same archetype; loadouts are coherent (a Kritz bot holds 35); the distribution
    over 1,000 seeds matches the weights within 3 %.

### 4.8 Live measures (objective first)

Scenario pairs on the same map and seeds, a team with a Medic bot of each archetype vs the same team with a
Soldier in its place:
1. **Objective time and progress** (user ruling 2026-11-15): time to first capture (5CP/KOTH point, flag
   capture on `ctf_2fort`), points or flags per 10 min; payload cart distance per minute once P8 lands.
2. Ubers built (time 0 -> 1 per life), popped, dropped (died with the bar at or over 0.9), pop outcomes
   (patient alive 8 s later; kills by patient during the charge; Kritz crit kills).
3. Healing per minute (the round's healing stat, `TFPlayerResourceNetState` healing column), share of alive
   time with a beam up, share of time within 450 u of the primary patient, share of time visible to an enemy.
4. Medic deaths per 10 min and the share of them from behind (Spy) or by headshot.
5. Archetype difference: Kritz pops mostly with 2+ enemies in sight; Quick-Fix average distance to patient
   under Über's; Vaccinator resist changes per minute over 0 and matched to the damage taken over 60 %
   **(measure)**.

### 4.9 What the code lane needs from the P7 foundation

1. The class pick and composition (A2): `joinclass medic` by the team's roster target, the 25 % keep rule.
2. Class subtree dispatch in `TFClassTrees` and a hook in `TFDecider.Decide` (`TFDecider.cs:188-206`) where
   a class's subtree owns the hands, the look and the movement goal for a decide (A1); the Soldier's
   `Engage`/`Hands`/`RocketJump` must not run for a Medic.
3. The kit read (A5): the bot's own weapon kinds by slot from its HUD, replacing the name-bound
   `_launcher`/`_shotgun`, and the archetype read from it.
4. The archetype draw (4.2) in place of the per-slot draw (`BotInventory.cs:94-108`).
5. A friend-directed hold action the friend gate skips (A4), with the look layer allowed an actor that is a
   teammate.
6. `SelfState` (or a TF2 HUD extension) with the charge meter, releasing, heal target and resist type (A6).
7. The teammate-health channel and its ruling (A7), and another Medic's beam target and charged glow (A8).
8. Voice lines in (A9): "Go!", "Activate charge!" and "ÜberCharge ready" heard as callouts with the speaker.
9. A hidden-spot query near an actor (rings of points tested against known enemies' eyes), shared with the
   Engineer and Sniper lanes.

Seen in this worktree while writing (uncommitted foundation work, not at `225fe2e96`, so not cited above):
`src/ai/tf2/server/Classes/TFMedic.cs` (a `TFMedicClass` with `Ready = false`, an empty `Abilities()` block, a
`MedicPlay : ClassPlay` stub) and `Classes/TFClassSpecs.cs` (16 ability rows per class block). Items 2 and 6 of
the list above are what `MedicPlay` must be able to override; the rows of 4.6 go in the Medic's own block.

Game-side prework the Medic lane does not own: G1 (Quick-Fix jump mirroring, before archetype C's jump-follow
row), G2 (charge-ready speech), G3 (caller bubbles; only needed if the HUD's bubbles become the honest source).

---

## 5. Sources

Web:
- [W1] https://wiki.teamfortress.com/wiki/Community_Medic_strategy
- [W2] https://wiki.teamfortress.com/wiki/Medi_Gun
- [W3] https://wiki.teamfortress.com/wiki/Medic_(competitive)
- [W4] https://wiki.teamfortress.com/wiki/Combo_(competitive)
- [W5] https://wiki.teamfortress.com/wiki/Dealing_with_Medic
- [W6] https://wiki.teamfortress.com/wiki/Medic_strategy
- [W7] https://wiki.teamfortress.com/wiki/Voice_commands
- [W8] https://wiki.teamfortress.com/wiki/6s (format background; the Highlander page holds no Medic detail)

Notes: the wiki pages warn they may be outdated ([W3] last edited April 2023). [W2] gives the release as 8 s in
its infobox and 9 s in its text; stock code says 8 (`weapon_medigun_chargerelease_rate`). [W3] gives the
Vaccinator bubble as 2.5 s; stock code's default is 3 (`tf_weapon_medigun.cpp:89`).

Stock files read (`../source-sdk-2013-valve`):
- `src/game/server/tf/bot/behavior/medic/tf_bot_medic_heal.cpp` (all), `tf_bot_medic_retreat.cpp`
- `src/game/server/tf/bot/tf_bot.cpp:820-870` (class selection), `:1160-1171` (Medic keeps class), `:4968-4992`
  (Über health threshold 50, deploy delay)
- `src/game/shared/tf/tf_weapon_medigun.cpp:74-89` (convars), `:203-205`, `:1355-1362`, `:2487-2562`
- `src/game/server/tf/tf_player.cpp:9797-9829` (Quick-Fix jump share)
- `src/game/shared/tf/tf_gamerules.cpp:16835-16860`, `:16953-16984` (capture and block rules)
- `src/game/shared/tf/tf_gamemovement.cpp:490-506` (Quick-Fix shield-charge follow)
