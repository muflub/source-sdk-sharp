# P7 research: the Spy

Research lane, 2026-11-20, read at `225fe2e96`.
No code, builds or games. Repo claims cite `path:line` at that commit. Game knowledge cites a URL (§5). Stock
TFBot cites `../source-sdk-2013-valve/src/game/server/tf/bot/...` (written `tf_bot/` below). A number marked
**measure** is a guess for the code lane to replace with a measurement.

**The gate.** The plan says this lane's Spy work is gated on the Spy honesty fact
(`docs/plans/plan_bots_teams_tf2.md:1491`). The fact is at `:619-634` (with no cue, a bot never targets a disguised
Spy, and suspicion comes only from cues a player gets). Its sequel is at `:636-656` (a Spy wearing the bot's own
name is identified at once, and there are four graded name cues). §3.3 shows the view does not yet meet the fact in
three places: `NameSlot` leaks, the kill feed ignores the silent kill and the feign, and the actor id is the real
identity. Those come first, in §4.1.

---

## 1. The Spy's job, by mode

**What the Spy is.** 125 health, 107 % speed (320 u/s). The revolver holds 6 + 24 rounds. The knife does 40, or a
backstab of 6× the victim's current health ([wiki Spy](https://wiki.teamfortress.com/wiki/Spy),
[Backstab](https://wiki.teamfortress.com/wiki/Backstab)). A disguise takes 2 s, or 0.5 s when already disguised
(`TFSpy.cs:56,59`). The Invis Watch drains 10/s and refills 3.3/s (stock `tf_player_shared.cpp:166-167`). A decloak
fades over 2 s and the Spy cannot attack for 2 s after it (`:116`, `:168`).

**The common rule in every mode:**
- **Where he stands.** Behind the enemy's front, on routes the enemy doesn't use. Range is 0–60 u for the knife and
  300–1200 u for the gun.
- **What he hunts.** He picks people whose attention is elsewhere, and he shapes the fight his team is having.
- **Pub target order.** Medic first, then a lone scoped Sniper, then an Engineer at his nest, then a spun-up Heavy,
  then Demo or Soldier. He avoids Pyros and Scouts. With a group, he takes the Medic first, except when a
  Heavy+Medic pair is alone, when the Heavy goes first. With several victims in a line, he starts from the
  rear-most ([Community Spy strategy](https://wiki.teamfortress.com/wiki/Community_Spy_strategy)).
- **6s order.** Demoman, then Medic ([Spy (competitive)](https://wiki.teamfortress.com/wiki/Spy_(competitive))).
- **Timing.** He stabs when the enemy has engaged his team's push. He kills the Medic before Über is used.

| mode | pub | competitive |
|---|---|---|
| **CTF** (2fort) | Go round to the intel room by a side route and kill the defenders near the intel and sentry. Sap the sentry before a teammate's run. A Spy may carry the intel, but carrying removes the disguise (`ItemTeamFlag.cs:690`) and forbids the cloak (`TFSpy.cs:295`, stock `CanGoInvisible`). Hand the intel to faster classes unless alone. | not played |
| **CP attack** (A/D) | Sap the sentries guarding the point before the team pushes, and kill the Medic. He cannot capture while stealthed or disguised as the enemy (`TFCapturePlayerRules.cs:43-64`), so he decloaks and drops the disguise to cap. | Swapped in for a Scout to break a sentry-backed hold; A/D defence almost never runs Spy ([competitive](https://wiki.teamfortress.com/wiki/Spy_(competitive))). |
| **CP defend** | Lurk near the attackers' spawn exit, hidden from it, and stab people leaving. Stock TFBot does this: a hiding spot within 2,500 u of an enemy spawn exit, not potentially visible from it, waited at 5–10 s (`tf_bot/behavior/spy/tf_bot_spy_infiltrate.cpp:128-135,223-262`). | rarely |
| **5CP symmetric** | Flank mids and pick off the backline. | Played on last and second-to-last in stalemates, to kill the Demo or Medic so the team can push ([competitive](https://wiki.teamfortress.com/wiki/Spy_(competitive))). |
| **KOTH** | Like 5CP mid: flank, sap sentries on or near the point, kill the Medic before a push. | Highlander: one Spy; info and picks. |
| **Payload attack** | Sap the sentries along the track ahead of the cart, and stab defenders who face the cart. | Highlander only |
| **Payload defend** | Get behind the push. Pushers face the cart, so their backs are offered. Stab the Medic, then the Heavy. A Dead Ringer Spy can hold the cart area ([Community Spy strategy](https://wiki.teamfortress.com/wiki/Community_Spy_strategy)). | Highlander only |

**Push, hold and retreat.**
- *Push* (go for the stab) when the victim faces away, or is engaged with the Spy's team, with no enemy in sight
  facing the Spy within ~600 u (**measure**).
- *Hold* (disguised or cloaked, still, in a low-traffic cell) while the enemy has an unengaged group of three or
  more.
- *Retreat* (cloak away, or feign) on cover blown. Stock calls cover blown when a threat aims at him (dot over 0.99)
  and fired within 0.25 s, the Spy was hurt within 1 s, or he burns, is Jarated, bleeds or blinks
  (`tf_bot/behavior/spy/tf_bot_spy_attack.cpp:165-178`).

**Movement tech.** Cloak out of sight, around a corner; double back past pursuers; drop off ledges behind the
victim (height is ignored by the stab); the stair-stab; circle-strafe a victim to reach his back (stock: within
250 u, to the side the cross product says, not closing under 100 u while not behind, `tf_bot_spy_attack.cpp:190-221`).
Disguised speed is clamped to the disguise class's (`TFMaxSpeed.cs:108-109`). Soldier is 80 % and Heavy 77 %, so
slow disguises cost travel time ([competitive](https://wiki.teamfortress.com/wiki/Spy_(competitive))).

**Teamwork.** Pair with the team's push: the stab lands as the enemy turns to fight. A sap is worth most just
before a teammate's assault on the nest (an Engineer can unsap with two wrench hits). In Highlander, call out
enemy positions.

**What beats him, and how good players play around it.**

| threat | how good Spies handle it |
|---|---|
| Pyro (flame spy-checks, airblast) | Avoid; use the revolver ([Community](https://wiki.teamfortress.com/wiki/Community_Spy_strategy)) |
| Scout | Hardest to stab; use the gun |
| Sentry | Never stab in front of an unsapped sentry. A sentry does not shoot a disguised Spy until the disguise drops. |
| Razorback Sniper | Blocks the stab and locks the Spy for 2 s ([Backstab](https://wiki.teamfortress.com/wiki/Backstab)); shoot him |
| Teammates walking into him | Bumping him gives him away |
| Jarate, Milk, bleed, fire | Each reveals him |

**Beginner mistakes that make a bot read as a bot** ([Community](https://wiki.teamfortress.com/wiki/Community_Spy_strategy),
[Anti-Spy](https://wiki.teamfortress.com/wiki/Anti-Spy_strategy)):
- cloaking, decloaking or disguising (the smoke) in view; bumping enemies;
- walking against the flow of the disguise's team, or out of their spawn the wrong way;
- a disguise that doesn't act its class: a "Medic" who never heals, a "Scout" who walks slowly, a Heavy or Soldier
  disguise used to run;
- staring at the victim (stock's Hard+ Spies look where the victim's team would come from instead,
  `tf_bot/tf_bot.cpp:2898-2906`);
- front-stabbing (40 damage); stabbing in front of a sentry; standing still in a corridor; running out of cloak in
  the open;
- disguising as a class the enemy doesn't field, or whose real player is in sight: the borrowed name is then wrong
  (§3.3).

---

## 2. Play styles by weapon set

Spy works rows at `225fe2e96`: 60 (`items_working.tsv`, `awk '$5~/spy/'`): secondary 20, melee 26, pda2 7, building
7. All are `works` (none partial). Attribute values are read from
`bin/debug/content/tf/scripts/items/items_game.txt` by prefab.

**Reskins**, the same `item_class` with no attributes, grouped under their base item:
- **Revolver 24** (`tf_weapon_revolver`): 161, 210, 1142, and 15011 … 15149 (warpaints).
- **Knife 4** (`tf_weapon_knife`): 194, 638, 665, 727, 794, 803, 883, 892, 901, 910, 959, 968, and 150xx.
- **Invis Watch 30** (`tf_weapon_invis`): 212, 297, 947, 1205. 297 and 1205 carry only `cannot_trade`.
- **Sapper 735** (`tf_weapon_builder`): 736, 1080, 1102, and 933 (Ap-Sap: voice lines only).
- **Festive Ambassador 1006** is the Ambassador 61.
- **Wanga Prick 574** is Your Eternal Reward 225.
- **Promo Red-Tape 831** is the Red-Tape Recorder 810.

| item | defindex, `item_class` | attributes (value) | what it changes for a bot |
|---|---|---|---|
| Invis Watch | 30 | none | ~9–10 s of cloak, refilled by ammo and dispensers |
| Dead Ringer | 59, `tf_weapon_invis` | `set_weapon_mode` (feign), `mult_cloak_meter_consume_rate` 0.6, `cloak_consume_on_feign_death_activate` 0.5, `mult_cloak_meter_regen_rate` 1.5, `mod_cloak_no_regen_from_items` | `IN_ATTACK2` arms the watch instead of cloaking; the arm needs a full meter (`TFSpy.cs:338-342`). The next hit feigns: a fake death notice, a body and an ammo pack (`TFSpy.cs:372-420`), 75 % less damage for the first 3 s, then 20 %. Ammo doesn't refill it. |
| Cloak and Dagger | 60 | `set_weapon_mode` (motion cloak), regen 2.0, `NoCloakWhenCloaked`, `ReducedCloakFromAmmo` 0.65 | Drains with speed and refills standing still (`TFSpy.cs:211-238`); cloaked indefinitely while still |
| Ambassador | 61, `tf_weapon_revolver` | `revolver use hit locations`, damage 0.85, fire-rate penalty 1.2, `crit_dmg_falloff` | Headshot crits only when >1.0 s has passed since the last accuracy check (stock `tf_weapon_revolver.cpp:85`). The headshot is not a crit past 1,200 u ([Ambassador](https://wiki.teamfortress.com/wiki/Ambassador)). |
| L'Etranger | 224 | `add_cloak_on_hit` 15, damage 0.8, consume 0.6 | Each hit refills 15 % cloak; 40 % longer cloak |
| Enforcer | 460 | `damage bonus while disguised` 1.2, `mod_pierce_resists_absorbs`, fire rate 1.2 | The opener from disguise hits harder; good against the Vaccinator and the Razorback |
| Diamondback | 525 | `sapper_kills_collect_crits`, damage 0.85 | One stored crit per sapper kill or backstab; spend the crits on the Engineer |
| Your Eternal Reward | 225 | `set_disguise_on_backstab`, `set_silent_killer`, consume 1.33, `mod_disguise_consumes_cloak` | A stab re-disguises the Spy as the victim, and the victim's team gets no feed line. A manual disguise needs, and spends, a full meter. |
| Conniver's Kunai | 356 | `sanguisuge`, `add_maxhealth` −55 | 70 max health; a stab takes the victim's health, up to 210 (`TFSpy.cs:470-475`) |
| Big Earner | 461 | `add_cloak_on_kill` 30, `add_maxhealth` −25, `speed_boost_on_kill` 3 | 100 health; a stab gives +30 cloak and 3 s of speed, which suits chains (`TFSpy.cs:851-858`) |
| Spy-cicle | 649 | `freeze_backstab_victim`, `melts_in_fire` 15, `become_fireproof_on_hit_by_fire` 10 | The first fire contact gives fireproofing and the knife is gone for 15 s (`TFSpy.cs:952`); for Pyro-heavy enemies |
| Red-Tape Recorder | 810, `tf_weapon_sapper` | `sapper_degenerates_buildings` 0.5, `mult_sapper_damage` 0 | Downgrades buildings instead of damaging them; better against level-3 nests |

**Archetypes.** These are the plan's C.3.1 rows (`plan_bots_teams_tf2.md:745-749`), made coherent and ordered by
how often they are played:

| # | archetype | primary (2nd) | melee | watch | sapper | why players run it |
|---|---|---|---|---|---|---|
| A1 | **Backstabber** (stock) | 24 | 4 | 30 | 735 | the baseline |
| A2 | **Dead Ringer** ("Amby-Knife-DR") | 61 | 4 | 59 | 735 | Called "extremely powerful" and the common competitive set ([Community](https://wiki.teamfortress.com/wiki/Community_Spy_strategy)). Plays openly and survives a burst. |
| A3 | **Saboteur** | 525 | 4 | 30 | 810 | The anti-nest set: crits from saps, downgrades for level 3s. Drawn more often after a sentry is seen (C.4's counter). |
| A4 | **Infiltrator** | 224 | 4 (225 later) | 60 | 735 | Ambush from stillness; L'Etranger's on-hit cloak offsets the C&D's drain ([Community](https://wiki.teamfortress.com/wiki/Community_Spy_strategy)) |
| A5 | **Gunspy** (the plan's "Ambassador") | 61 (460 vs. Vaccinator) | 461 | 30 | 735 | Picks at 300–1,200 u, with the knife as an opportunist |
| tail | YER silent (225 + 30), Kunai gambler (356 + 59), Spy-cicle against Pyros (649) | | | | | |

**What changes in the bot's decisions, per archetype.** Each number names its source or says **measure**.

- **A1 Backstabber.**
  - Range: the knife (≤ 60 u). The gun only when cover is blown.
  - Routes: low-traffic, per C.3.2 (`plan:669`).
  - Cloak to cross: start only when the meter is ≥ 40 and the crossing is ≤ 4 s at 10/s; decloak by meter 15 (a
    margin, **measure**).
  - Decloak ≥ 2.5 s before the stab, out of the victim's sight (fade 2 s + no-attack 2 s,
    `tf_player_shared.cpp:116,168`).
  - Disguise again after every reveal, out of sight (0.5 s).
- **A2 Dead Ringer.**
  - Never cloaks to cross. It walks openly in disguise.
  - Arms the watch whenever an enemy is sighted within 1,500 u or it is in the enemy half (**measure**), and
    re-arms when the meter is full.
  - Aggression is higher: it accepts a stab with one enemy facing it.
  - After a feign, it runs for the 3 s speed window (`TFSpy.cs:83`) away from the hit's direction, to cover or a
    health pack. It decloaks only out of sight, and does not re-engage until the meter is ≥ 60 (**measure**). Ammo
    does not refill the meter.
  - Ambassador: once cover is blown and the target is 300–1,200 u away, a head shot every ≥ 1.05 s (stock's 1.0 s
    plus a tick margin).
- **A3 Saboteur.**
  - The target order is buildings first: sentry, then teleporter exit, then dispenser. Within 300 u of the first
    sap it saps the rest (stock `tf_bot_spy_sap.cpp:126-142`).
  - When the Engineer is within 150 u and between the Spy and the building, it stabs him first (`:70-78`).
    Otherwise it saps, then stabs or shoots the Engineer as he comes to unsap. Diamondback crits are kept for him.
  - The route aims for sentry spots it has seen (the experience) and the `ChokeMap`'s defended cells.
- **A4 Infiltrator (C&D).**
  - Moves cloaked only while the meter is ≥ 25, then stands still on a perch (C.3.2's perch table, `plan:669`)
    until 80 (**measure**).
  - Ambushes at a choke the enemy uses, stabbing the last of a passing group. Stalls by design.
  - Pistol hits refill the meter.
- **A5 Gunspy.**
  - Engages at 300–1,200 u with the gun. The knife is used only on a back offered within 250 u.
  - Big Earner: after a stab it chains to a second victim within 400 u if the speed boost is up (**measure**).

---

## 3. What the port and the AI can do today

### 3.1 The game side (`src/tf2/`): almost everything exists

| behaviour | status | where |
|---|---|---|
| Disguise / last disguise commands | yes. Team −1 is the enemy and −2 own (stock client numbers). | `server/Player/TFSpyCommands.cs:27-63,70-94`; `TFSpy.cs:448,675` |
| Disguise target (the borrowed name) | Yes. It prefers a player of the disguise team **and class**, else anyone on the team, else −1. | `TFSpy.cs:567-597` |
| Disguised speed clamp | yes | `shared/Movement/TFMaxSpeed.cs:108-109` |
| Disguise weapon shown | yes | `server/Player/TFDisguiseWeapon.cs:38` |
| Removal on attack, on the flag | yes (a backstab with YER keeps it) | `server/Weapons/TFKnifeServerHook.cs:79-98`; `Ctf/ItemTeamFlag.cs:690` |
| Cloak on `IN_ATTACK2` | Yes, through the weapon's secondary to `DoClassSpecialSkill`. The `cloak` verb's comment saying the button path is deferred is **stale**. | `server/Player/TFClassSpecialSkill.cs:58-61`; `TFSpyCommands.cs:96-100` |
| Cloak meter, C&D motion cloak, rate attributes | yes | `TFSpy.cs:195-260`, `:314-358` |
| Dead Ringer feign: body, notice, pack, buffs | yes (live-pressed 2026-11-05, tsv evidence) | `TFSpy.cs:372-420`; `TFFeignRagdoll.cs:28` |
| Backstab: stock's three dot tests, 2× health crit, Razorback | yes. The stab lands at the swing (the plan's divergence row, `plan:522`). | `TFSpy.cs:716-770,800-831` |
| Kunai, Big Earner, YER, Spy-cicle | yes | `TFSpy.cs:470-475,851-858,907,952` |
| Sapper: `sap` verb | Yes. It needs an enemy building within 160 u in a 0.766 view cone, and refuses an already-sapped one. | `Buildings/TFSapperCommands.cs:21`, `TFSapperUse.cs:79-130` |
| Red-Tape, Ap-Sap | yes | `TFSapper.cs:126-215` |
| Cap and flag rules for Spies | yes | `TFCapturePlayerRules.cs:43-64`; `ItemTeamFlag.cs:615` |
| Disguised enemies collide (the walk-check's cue) | Yes. The collide rule reads the real team through the roster. | `server/Rules/TFTeamCollide.cs:30-32` |
| Silent kill (YER) in the death notice | yes, per tsv evidence (`set_silent_killer TFDeathNotice.cs:130`) | `Combat/TFDeathNotice.cs` |
| Ambassador headshot gate | yes | `shared/Weapons/TFRevolverBehaviour.cs:36,48`; `TFDamageModifyRules.cs:349` (crit falloff) |

**Game-side gaps for the Spy:**
- A disguise kit (27) and its menu do not matter to a bot: the verb is enough.
- The PDA placement preview is deferred (`TFSapperCommands.cs:6-10`). The verb stands in.
- Nothing else blocks the archetypes A1–A5.

### 3.2 The AI side (`src/ai/`): perception is half there, the brain is absent

- **Class pick: Soldier only.**
  - `TFBotClassPick.cs:21-22` sets the class, and `TFClassTrees.cs:15` runs the Soldier's tree for every class.
  - The Soldier's hands know only the launcher and shotgun (`TFDecider.Soldier.cs:17,125-149`). A Spy bot today
    would hold its revolver, fire at enemies (which removes its disguise), and never disguise, cloak, stab or sap.
- **Ability rows.** There are none for the Spy (`Knowledge/TFAbilities.cs:50-66`). Rows are fixed strings
  (`core/Brain/Abilities.cs:146`), so each disguise class needs its own row.
- **The rendered Spy (`Senses/TFBotGameView.cs:100-126`).**
  - Other teams see the disguise's team, class, weapon and borrowed name.
  - Cloak scales `VisibleFraction`, and at ≥ 0.75 invisible he is not drawn (stock's number, `tf_bot_vision.cpp:282`).
  - A Spy sees building health (`:216-217`; stock `tf_hud_target_id.cpp:483`).
- **Targeting skips anyone drawn as a friend** (`Trees/TFDecider.cs:311-315`). The fact
  `AnActorDrawnOnTheBotsTeamIsNoTarget` (`src/ai/tests/TF2/TFDeciderTests.cs:213-221`) holds today's honesty
  negative.
- **"Spy!" callout** for an enemy drawn as a Spy (`TFDecider.Voice.cs:137-138`).
- **No suspicion counter** of any kind (`grep -rni suspic src/ai` is empty).

### 3.3 Honesty holes found (each is a leak a fact should catch)

1. **`NameSlot` sets a Spy apart.**
   - `SnapshotBuilder.cs:114` builds every actor with `NameSlot = 0`, and only a disguised enemy gets
     `NameSlot = DisguiseTargetIndex` (`TFBotGameView.cs:116`; the doc at `core/Snapshot/PerceivedView.cs:87-88`
     calls 0 "its own").
   - So `f.NameSlot != 0` is true exactly for a disguised enemy Spy. No decider reads it yet, but the first cue
     written on it would read the truth.
   - The borrowed index can also be −1 (no target, `TFSpy.cs:592-594`). A client then shows no name.
2. **The actor id is the real identity.**
   - A disguised Spy is sighted under his real `ActorId` (`SnapshotBuilder.cs:114`, `sight.TargetOf(t)`), and the
     board row keeps his frags, `BoardTeam` (the scoreboard's real team, `BotMemory.cs:388-394`), his grudge and
     his habits (`Blackboard.cs:73-91`).
   - `Reclassified` flips him to "friend" on sight (`BotMemory.cs:239-243,826-830`), but the row's history and
     `BoardTeam` still name him. `a.BoardTeam != f.Team` would be an instant, dishonest detector.
   - A player sees "a BLU Pyro named Bob", not entity 7.
3. **The kill feed is not the TF feed.**
   - `CombatTap.Died` sends every bot a `KillFeed` for every engine death (`server/Senses/CombatTap.cs:108-117,146-157`).
   - Stock hides a silent kill (YER backstab) from the victim's team (`client/tf/tf_hud_deathnotice.cpp:695-702`).
   - Stock shows a Dead Ringer feign in everyone's feed as a kill. Here the feign never reaches the bots, because it
     does not run the death hooks (`TFFeignRagdoll.cs:20`; the notice is fired by `TFSpy.cs:415`).
   - So a bot knows a YER victim died, and is never fooled by a feign. Both are wrong.
4. **A dishonest read the plan allows: none.** The Spy bot's own target ID shows enemy health
   (`client/tf/tf_hud_target_id.cpp:380`, the `bSpy` arm). `ActorFacts` forbids health on purpose
   (`PerceivedView.cs:81-84`). A Spy-only "health under the crosshair" read is honest but needs a ruling (§4.7).

**Missing on the perception side.** All of these are honest, because a client draws them.

| missing | what a client shows | used by |
|---|---|---|
| a player's facing (model yaw) | the model's yaw | The backstab test, "is he looking at me", cover blown. Today no facing field exists (`PerceivedView.cs:90-100`). |
| `ActorConditions` Disguising | the smoke | a reveal cue |
| Jarated, Milked, Bleeding | particles | reveal cues |
| cloak Blink | the flicker when bumped or hit | a reveal cue |
| an `ActorActivity` PlacingSapper flag | the placing animation | a reveal cue; plan `:627` names it |
| `SelfState` cloak meter | own HUD | every watch decision; plan F12 `CloakMeter` (`TFPlayerNetState.cs:136`), not passed by `Hud()` (`TFBotGameView.cs:60-68`) |
| `SelfState` feign-ready bit | own HUD | Dead Ringer arming (`TFPlayerNetState.cs:168`) |
| own disguise class and team | own HUD (`TFHudSpy`) | re-disguising |
| `ThingFacts` Sapped flag on buildings | the sapper model and sparks | picking the next building (`PerceivedView.cs:122-131` has no such field) |

---

## 4. The design for the code lane

### 4.1 First: the honesty fix and its facts (they gate the lane)

**Rendered identity** (foundation or core). For a viewer who is not on the Spy's team, a disguised Spy appears as an
**alias actor**:
- an `ActorId` from a reserved alias range, stable for one disguise of one Spy, renewed at every new disguise or
  reveal, held per viewer in the binding;
- `NameSlot` = the borrowed slot.

Every other actor's `NameSlot` = its own slot, so the field means the same thing for everyone.
- The applier resolves an alias to the entity for aim, and only there. The brain never sees the mapping.
- When the disguise drops in sight, the alias row is told "this was a Spy, now actor X". That is honest: the
  player saw it happen.
- Out of sight, nothing links the two.

**The cues, all from the view** (`plan:624-629`, `:646-650`; stock `tf_bot.cpp:1445-1465`, `tf_bot_vision.cpp:258-302`):

| cue | strength | trigger |
|---|---|---|
| name is own slot | certain | `NameSlot == self` |
| name is a player sighted elsewhere this tick | certain | another sighted actor has the same `NameSlot` |
| name is dead per the bot's kill feed | strong | |
| class differs from the name's last-seen class | weak, scaled by Alertness | |
| a bump (a solid "teammate", the walk-check, `plan:673`) | certain, as stock `RealizeSpy` on touch | |
| hurt by a friend-drawn actor (`Damaged`) | certain | |
| the bot's own shot or flame damaged a friend-drawn actor | certain | `DealtDamage`, `CombatTap.cs:138-141` |
| burning, Jarated or bleeding (stock always notices these) | strong | |
| seen disguising (smoke) or placing a sapper | certain | |
| a teammate's "Spy!" | strong | |
| walking out of the enemy spawn | weak | |

- Suspicion is a per-row counter with a half-life. Stock forgets after 5 s (`tf_bot.cpp:60-61`, the forget
  cooldown); keep 5 s as the default half-life (**measure**).
- Above the threshold the row is an enemy, and the bot calls "Spy!" (the existing `VoiceSpy` row).

**Facts** (the honesty gate; each red once against a mutant):
1. `ADisguisedSpyWithNoCueIsNeverTargeted`. The negative arm reads the server's real team and goes red. This
   extends today's fact (`TFDeciderTests.cs:213`) with a real disguise through `TFBotGameView.Draw`.
2. `ADisguisedSpysFactsMatchARealTeammatesInKind`. A census over the `ActorFacts` of a disguised Spy and a real
   teammate of the same class: `NameSlot` is a slot in both, and the ids are in no range a brain can tell apart.
   Mutant: today's `NameSlot = 0`, red.
3. `ASpyWearingMyNameIsAnEnemyAtOnce`, after the recognition delay (`Sight.cs:30`).
4. `ASpyWearingANameISeeElsewhereIsAnEnemy`.
5. `ASpyWearingADeadMansNameIsSuspected`.
6. `AClassMismatchRaisesSuspicionByAlertness`. Monotonic over Alertness; a flat mutant goes red.
7. `TheSameSpyInAnotherLivingNameIsNotIdentified` (the negative).
8. `AYERKillIsNotInTheVictimTeamsFeed`.
9. `AFeignIsInEveryFeedAsAKill`.
10. `ABumpedDisguisedSpyIsAnEnemy`.
11. `ABurningFriendDrawnActorIsSuspected`.
12. `SuspicionDecaysWithItsHalfLife`.

Live check (`plan:652-653`): a scripted Spy taking the bot's own name is shot on sight; the same Spy in another
living name is not.

### 4.2 Archetypes to ship, in order

1. **A1** Backstabber (stock).
2. **A3** Saboteur. Stock TFBot's own top behaviour is the sap (`tf_bot_spy_infiltrate.cpp:66-79`), and the class
   pick already prefers Spy against sentries (`tf_bot.cpp:1206-1222`).
3. **A2** Dead Ringer.
4. **A4** Infiltrator. It needs the perch table from C.3.2.
5. **A5** Gunspy.
6. The tail.

**The draw.** A1 0.40, A2 0.25, A3 0.15, A4 0.10, A5 0.10 (**measure**; no source gives pub shares).
- ×3 on A3 once the team has seen an enemy sentry (C.4's counter, `plan:859-861`).
- Cunning raises A1/A4, Accuracy raises A5, low Nerve raises A2. Each weight is linear in the skill over 0–10
  (**measure**).
- The loadout is the archetype's row (§2), each slot falling back to the stock item when its defindex isn't allowed
  (`ItemIsAllowed`, `plan:762`). Reskins count as their base. A slot the row doesn't name keeps today's seeded
  draw, but **never** a watch or knife that changes the archetype: the pda2 and melee slots are always named.

### 4.3 Decision rules

These are the Spy subtree's branches over the root utility selector (`TFDecider.cs:259-275`), in the Spy's own file.

**Disguise** (`disguise <class> -1`).
- When: alive, not disguised, and not in the sight of any enemy (out of sight, or cloaked first: cloak hides the
  smoke, [Disguise Kit](https://wiki.teamfortress.com/wiki/Disguise_Kit)).
- Class weight = `seen(C)` (1.0 if the bot has seen or heard of an enemy C this life, else 0.3) × speed factor
  (Pyro, Engineer, Sniper, Medic 1.0; Scout 0.6; Demo 0.5; Soldier 0.3; Heavy 0.2;
  [competitive speeds](https://wiki.teamfortress.com/wiki/Spy_(competitive))).
- Weight 0 for a class whose only known enemy player is in the bot's sight now. `FindDisguiseTarget` would borrow
  that very name (`TFSpy.cs:567-583`).
- Re-roll every 60–120 s out of sight, so the class isn't a pattern ([Community](https://wiki.teamfortress.com/wiki/Community_Spy_strategy)).
- Stock picks a random enemy's class (`tf_bot.cpp:4411-4424`). Ours is that plus the two filters.

**Look.** While disguised and an enemy is sighted, Hard+ (Cunning ≥ 6) looks along the disguise team's flow, not at
the victim, until the stab is set (stock `tf_bot.cpp:2898-2906`).

**Route.**
- Path cost adds `trafficWeight × traffic` (C.3.2, `plan:669`): `OpponentHabits` (`core/Memory/OpponentHabits.cs`)
  plus `ChokeMap` betweenness (`core/Nav/ChokeMap.cs`).
- The route weights already take a per-bot table (`Nav/TFRouteWeights.cs:11-37`); the Spy's traffic weight is a
  second column.
- Never walk against the disguise team's flow within 600 u of an enemy (**measure**).

**Cloak (A1, A3, A5).**
- *Press `IN_ATTACK2`* when all hold: off the own spawn; the path ahead passes within 1,000 u of an enemy sighted in
  the last 3 s (stock "area in combat", `tf_bot_spy_infiltrate.cpp:57-64`); meter ≥ 40; no enemy facing the bot
  within 1,500 u.
- *Decloak* when all hold: no enemy sighted within 1,200 u for ≥ 1 s, or the meter is ≤ 15; behind cover. Never
  decloak within 2.5 s of the stab (fade 2 s + no-attack 2 s).
- **A4 (C&D)**: move only at meter ≥ 25; at < 25 stop on the nearest perch until 80.
- **A2**: never cloak to cross. *Arm* (`IN_ATTACK2` at a full meter) when an enemy is sighted within 1,500 u or the
  bot is past mid. After the feign, flee for 3 s.

**Choose a victim** (the Spy's override of `TakesTarget`, `TFDecider.cs:320`).
- Priority, pub: Medic 1.0, Sniper 0.9 (×1.3 while zoomed), Engineer 0.8 (×1.3 near his own building), Heavy 0.7,
  Demo 0.6, Soldier 0.5, Scout 0.3, Pyro 0.2. Source: [Community](https://wiki.teamfortress.com/wiki/Community_Spy_strategy)
  order; the numbers are a ranking, **measure**.
- Competitive: Demo 1.0, Medic 0.95 ([competitive](https://wiki.teamfortress.com/wiki/Spy_(competitive))).
- × facing away (needs facing), × engaged (firing in a direction more than 60° from the bot), × rear-most of a group.
- Change victim when a new one is more than 300 u nearer (stock `tf_bot_spy_change_target_range_threshold` 300,
  `tf_bot_spy_attack.cpp:18`).
- A victim deeper into the Spy's side than the Spy is, measured as route distance from his own spawn, counts as
  "got behind" (stock's incursion test, `tf_bot_spy_infiltrate.cpp:93-109`).

**Stab.**
- Behind test on the rendered facing: `dot(victimForward, toVictim) > tol`. The tolerance by Cunning interpolates
  stock's per difficulty: Easy 0.9, Normal 0.7071, Hard 0.2, Expert 0.0 (`tf_bot_spy_attack.cpp:131-138`; we don't
  copy Easy's "always thinks it is behind", `:148-151`).
- Swing (`ActionKind.Fire` with the knife) only when the game's own test would pass on what the bot sees: within
  ~48 u, plus stock's three dots (`TFSpy.cs:763-764`).
- Within 250 u and not behind: circle-strafe to the side stock's cross product says, and don't close under 100 u
  (`tf_bot_spy_attack.cpp:190-221`).
- Knife when the victim is within 300 u (stock `tf_bot_spy_knife_range`, `:17`). Else the gun if cover is blown.
- Never stab in front of an unsapped sentry that sees the bot (stock retreats from sentry fire, `:91-97`).

**Cover blown** (stock `:165-178`).
- The trigger: the victim faces the bot (dot > 0.99) and fired within 0.25 s, and the bot was hurt within 1 s; or
  the bot burns, is Jarated or bleeds.
- Then: switch to the gun, or knife-spam within 100 u if the victim is believed at ≤ 40 % health (**measure**).
- With an Ambassador, aim the head (`AimPoint.Head`) and fire only when ≥ 1.05 s have passed since the last shot and
  the range is ≤ 1,200 u.
- Then escape.

**Escape.**
- After a stab, chain if another victim faces away within 400 u and no enemy faces the bot (Big Earner: ×2 while
  speed-boosted).
- Otherwise: cloak (meter ≥ 20) and take the lowest-traffic route out, or with YER walk off like the victim
  ([Community](https://wiki.teamfortress.com/wiki/Community_Spy_strategy)).

**Sap** (the `sap` verb).
- Walk disguised to within 160 u of the building with it in the 0.766 cone (`TFSapperUse.cs:79-82`).
- Stock closes to within 80 u before switching (`2 × sapRange 40`, `tf_bot_spy_sap.cpp:83-104`). Ours can send at
  ≤ 140 u, facing the building.
- If the Engineer is within 150 u and between the bot and the building: stab first.
- After the sentry: the next unsapped building within 300 u, then the Engineer. Wait behind the sentry on the
  Engineer's side.
- Stock `CTFBotSpySap::IsHindrance` stops avoiding people within 300 u of the target (`:208-219`).

**Objective.**
- The Spy's say in the role: `Fetch`/`Escort`/`Points` utilities ×0.3 while disguised (he can't cap disguised as
  the enemy or cloaked).
- Payload or CP defence: lurk at the enemy spawn's hide spot (stock's 2,500 u / not-visible rule, waiting 5–10 s).
- C.6's rally excludes the Spy (`plan:989`).

### 4.4 Ability rows (the Spy's own table)

Each is one `AbilityRun.Command` or `Buttons` row (`core/Brain/Abilities.cs:6-20`):
- `spy_disguise_<class>` × 9 (`disguise 1..9 -1`), one row per class because rows are fixed strings;
- `spy_lastdisguise`;
- `spy_cloak`: `Buttons IN_ATTACK2`, `HoldTicks` 2, the key a player presses (the Dead Ringer's arm is the same
  key);
- `spy_sap` (`sap`).

The friendly-team disguises (`… -2`) come later: setup-time baiting.

### 4.5 Facts to write (beyond §4.1)

- **Abilities:** each row is applied once as the right command or buttons; the disguise never goes on in the sight
  of an enemy; the cloak never starts with the meter < 40; the decloak is never within 2.5 s before a swing.
- **The stab:** no knife swing while the victim faces the bot (needs facing); the swing only within the game's
  three-dot test; the circle-strafe side matches the cross product.
- **The disguise:** never a class whose only enemy is in sight; class weights are monotonic in speed.
- **Targets:** a Medic is chosen over a Soldier at equal range; the victim changes only at > 300 u nearer.
- **Sap:** the Engineer is stabbed first when he is within 150 u and between; otherwise the sap comes first.
- **Archetype differences** (each with a flat mutant red): A2 never presses cloak to travel; A4 stands still at a
  meter under 25; A5's gun shots outnumber its swings on a scripted 600 u field.
- **The draw:** the same seed gives the same archetype and loadout; the pda2 and melee slots always come from the
  archetype; A3's weight rises after a sentry sighting.

### 4.6 Live measures (objective first, per the user's ruling)

**Objective.** On cp_granary and ctf_2fort, teams of 6 bots with one Spy against one Soldier in the same slot,
running the harness from the foundation brief.
- The team's time to first cap or pickup, and its captures over 10 minutes.
- **Spy-specific objective**: sentries destroyed while sapped, and the time from the first sentry sighting to its
  sap.

**Class numbers.** Backstabs per life; the share of backstabs on Medic, Sniper or Demo; facestab attempts (swings
without a backstab), which should be near 0; deaths while undisguised and uncloaked in the enemy half; reveals by
cue type; time disguised in the enemy half; cloak-time share; feigns per life (A2); saps per life (A3).

**Distinct archetypes** (targets to **measure**):
- A2 feigns ≥ 0.5 per life, against 0 for A1;
- A4 cloak share > 60 % against A1's 20–40 %;
- A3 saps ≥ 1 per life on a map with a sentry;
- A5 gun hits outnumber knife swings.

**Honesty, live.**
- Enemy bots shoot a scripted Spy in their own name at once, and do not shoot the same Spy in another living name
  with no cue (`plan:652-653`).
- Pyro bots set a disguised Spy burning (`plan:672`).

### 4.7 What the code lane needs from the shared P7 foundation

1. **A Spy file** with overrides of victim choice (`TakesTarget`); the hands (`Engage`/`Hands`, the Soldier's
   today, `TFDecider.Soldier.cs:92-149`: the Spy must be able to hold fire while disguised); the look while
   disguised; an extra route-cost column; and its weights on the objective utilities.
2. **Per-class ability tables** composed by position-stable append (the foundation brief's step 1).
3. **The kit read.** The watch's mode (`set_weapon_mode` 1 = feign, 2 = motion) and rate attributes; the knife's
   attributes (`set_disguise_on_backstab`, `sanguisuge`, `add_cloak_on_kill`, `melts_in_fire`); the revolver's
   (`revolver use hit locations`, `sapper_kills_collect_crits`, `add_cloak_on_hit`); the sapper's
   (`sapper_degenerates_buildings`).
4. **The archetype draw and the loadout seam** over `BotInventory.cs:84-120`, with the pda2 and melee slots bound to
   the archetype.
5. **The class pick.** A Spy "ready" flag; stock's roster limits (Spy only on teams of ≥ 5, at most 0/1/2/2 Spies
   by difficulty, `tf_bot.cpp:842,856`: stock never fields an Easy Spy; map that to our skill tiers); the
   sentry-seen raise (`:1206-1222`).
6. **Perception, honest** (§3.3, core or binding): the alias identity and `NameSlot` for everyone; the TF kill feed
   (silent kill, feign); actor facing; Disguising, Jarate/Milk/Bleed and Blink conditions; PlacingSapper activity;
   `SelfState` cloak meter, feign-ready and own disguise; the `ThingFacts` sapped flag.
7. **A ruling needed** (raise it with the user): may a Spy bot read the health shown in its own target ID
   (`tf_hud_target_id.cpp:380`)? It is honest, but it is the one health read `ActorFacts` forbids.
8. **Harness counters:** backstabs (`TFDamageCustom.Backstab`), feigns, saps, sapped kills, reveals by cue.

---

## 5. Sources

**Web:** https://wiki.teamfortress.com/wiki/Community_Spy_strategy,
https://wiki.teamfortress.com/wiki/Spy_(competitive), https://wiki.teamfortress.com/wiki/Anti-Spy_strategy,
https://wiki.teamfortress.com/wiki/Backstab, https://wiki.teamfortress.com/wiki/Disguise_Kit,
https://wiki.teamfortress.com/wiki/Ambassador, https://wiki.teamfortress.com/wiki/Spy; and the plan's own Spy
sources (`plan_bots_teams_tf2.md:840`): https://wiki.tf2.com/wiki/Cloak, https://pcgamesn.com/tf2/relearning-play-spy.

**Stock TFBot, read** (`../source-sdk-2013-valve/src/game/server/tf/bot/`): `behavior/spy/tf_bot_spy_attack.cpp`
(knife range 300, retarget 300, behind tolerances, cover blown, circle-strafe); `tf_bot_spy_infiltrate.cpp` (cloak
on combat, hide spot 2,500 u, incursion); `tf_bot_spy_sap.cpp` (sap range 40, Engineer-first within 150 u);
`tf_bot_spy_lurk.cpp`, `tf_bot_spy_hide.cpp` (750 u, 3,500 u), `tf_bot_spy_escape.cpp`;
`behavior/tf_bot_scenario_monitor.cpp:127-131` (a Spy starts in infiltrate);
`tf_bot.cpp:60-61,833-875,1206-1222,1445-1465,2898-2906,3744-3900,4411-4424`; `tf_bot_vision.cpp:258-302`.

**Other stock files read:** `shared/tf/tf_player_shared.cpp:115-181`, `tf_weapon_revolver.cpp:85,191-196`,
`tf_weaponbase_gun.cpp:210-213`, `tf_gamerules.cpp:12833-12839,16844-16869`,
`client/tf/tf_hud_deathnotice.cpp:695-702`, `tf_hud_target_id.cpp:246,380`.
