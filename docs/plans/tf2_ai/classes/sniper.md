# P7 research: the Sniper (meta, play styles by weapon set, the design for the code lane)

Lane: P7 research, Sniper. At main `225fe2e96` (checked with
`git log --oneline -1`). Nothing was built, run or committed. Repo claims are `path:line` at that tip. Game
knowledge cites a URL from §5. "Stock" means Valve's code in `../source-sdk-2013-valve`. A number marked
**(ours)** is a design choice to tune, not a fact about the game.

**The aim rule that binds this file.** Plan Q6 (`docs/plans/plan_bots_teams_tf2.md:1651`) says P7's Sniper lane
waits on Phase 5b, the human motion model and its gates (`docs/plans/plan_hl2mp_ai.md:2121-2146`). 5b is
**not done** at this tip. No human reference recordings exist: `src/ai/tests/recordings/` holds only bot
scenarios and `live/`. No spin-bot detector exists: a grep for `SpinBot|spin-bot` over `src/ai` finds
nothing. Every aim rule below is built so that "no snap headshots a person could not make" follows from the
motion model (`src/ai/core/Motion/HumanMotion.cs`), never from a hand cap on hit rate. The Sniper's aim gate
needs Sniper reference sessions (§4.7).

---

## 1. The class's job, by mode

**What all modes share** (wiki community and competitive pages, [W1] [W2]):
- **Range and where to stand.** Behind the front line, on high ground or a gap in a wall that sees several
  enemy routes. Stock's vantage search wants at least 1,000 u to the area it watches
  (`tf_bot.cpp:52` `tf_bot_sniper_spot_min_range`). A Sniper has 125 health [W1].
- **Target priority.** Wiki community order [W1]: enemy Sniper, Medic, Demoman, Heavy; then Soldier,
  Engineer, Pyro, Spy, Scout. Scouts are worth a shot only when capping, when close, or when nothing better
  is in view. Competitive order [W2]: immediate threats first (a Scout on you, a Sniper aiming at you), then
  "the enemy Medic will always be the highest priority", then Demoman (his damage and stickies), then
  Soldier/Heavy, Engineer, Scout.
- **What stock TFBot counts as an immediate threat** (`tf_bot_sniper_attack.cpp:161-226`): an enemy Sniper
  whose eye vector points at me (dot > 0.8), and a sentry in range pointed at me.
- **Push, hold, retreat.** Hold a sightline. Move after a kill (the freezecam shows the killer's spot) and
  after killing a Sniper [W1]. "Remaining in one position makes you predictable" [W2]. Charge behind cover,
  then peek out with the charge ready [W1]. Retreat from anyone close.
- **Movement tech.** Scoped walking is 27 % speed and crouched-scoped 1.33 % [W3]. The port's
  `TFMaxSpeed.Aiming` is 80 u/s (`src/tf2/shared/Movement/TFMaxSpeed.cs:130`), which is 27 % of the
  Sniper's 300. A good Sniper therefore unscopes to move, steps out, and scopes again. Zoom is refused
  while jumping (`TFSniperRifleBehaviour.cs:346`; [W3]). Crouching and moving after a shot spoils an enemy
  Sniper's aim [W1]. Strafe while retreating; jumping makes you easy to hit unless you air-strafe [W1].
- **Working with teammates.** Pubs: pairs with a Spy as a spotter, with a Pyro (lit arrows, Spy checks)
  and with a Heavy (kills the Snipers that would kill him) [W1]. Competitive: a Sniper's pick is the
  signal for the team's push, and the Sniper can clear the stickies on a final point so a Scout can back-cap
  [W2] [W4].
- **What beats them and how to play round it.**
  - Spy: backstab from behind. Counter with glances behind, a teammate nearby, Razorback, Jarate or SMG on
    a suspected cloak, and listening for the decloak [W1].
  - Scout: too fast for the scope. Counter with the SMG, Jarate, or the Kukri [W1] [W2].
  - Enemy Sniper: counter by charging behind cover, not peeking the same angle twice, moving, crouch-peeking,
    and shooting when he is busy with someone else [W1].
  - Soldier and Demoman: bombing from above. Counter by retreating to the team.
  - Pyro: reflects arrows and Jarate [W1].
- **The laser dot** shows where a scoped Sniper aims, to everyone (`src/tf2/server/Entities/SniperDot.cs:39`;
  [W3]). Players park it on a doorway, or take it off cover to bait an enemy out [W1].

**By mode**
| mode | pub | competitive | stock TFBot goal (`tf_bot.cpp:2295-2405`) |
|---|---|---|---|
| CTF (2fort) | battlements and bridge duels; cover the bridge for attackers; watch the courtyard exits on defence. On 2fort, "Scouts on the bridge roof" are worth shooting [W1] | not played | **none**: no goal entity, so spots are cleared and the bot heads for a point or the enemy spawn's threshold (`tf_bot_sniper_lurk.cpp:465-496`) |
| CP attack | long lines onto the point and its entrances, kept back from it | A/D is Highlander's; Gravel Pit's long lines at B and C [W2] | vantage areas kept 750 u *back* from the point (`tf_bot.cpp:55,2390-2394`, tolerance negated) |
| CP defend | lines onto the attackers' exits and the choke before the point; an early pick at the gates [W4] | the same | vantage up to 750 u *past* the point |
| 5CP | mid: picks across the open middle; then the next point's sightlines | 6s swaps a Scout for a Sniper on some maps; stays "far enough back to kill safely but close enough to avoid being left alone with enemy Scouts" [W2] | per point, as CP |
| KOTH | lines onto the hill from cover | "Play passively from spots that overlook the hill" [W2] | `TF_GAMETYPE_CP` arm, the hill's point |
| Payload | attack: move up with the cart, shoot the defenders on the high ground; defend: see the cart [W2] | Highlander: one Sniper per team [W5] | the cart; spots recomputed after it moves 500 u (`tf_bot.cpp:58,2329-2336`) |

**Composition.** Stock offence and defence rosters allow no Sniper below team size 5, none at Easy, and one
from Normal up (`tf_bot.cpp:841,855`; the fields are at `:822-829`). Their comp roster has none (`:870`).
The wiki [W1] says "more than two is usually excessive".

**Beginner mistakes that make a bot read as a bot**
- Snapping onto a head the frame it appears, or a perfect 180 into a headshot.
- A dot that never moves, or one that jitters every tick. Each reads as a script.
- Staying scoped with an enemy at 300 u.
- Scoping and walking in the open, or never relocating after a kill.
- Firing uncharged bodyshots at a Heavy. Firing the instant the 0.2 s gate opens, every time.
- Never looking behind; tracking through walls; switching targets every second while scoped.

Stock TFBot shows several of these: it fires after only 0.1 s of a steady yaw (`tf_bot_behavior.cpp:1391-1393`),
it charges nothing first, and its aim error is a 0.01 rad radius redrawn every 0.5-1.5 s
(`tf_bot_behavior.cpp:36,721-729`). That is the "aimbot Sniper" players complain of. We do not copy it.

---

## 2. Play styles by weapon set

The row numbers are `defindex` from `game/mod_tf2/scripts/items/items_working.tsv`. Every Sniper row there
is `works`: 59 rows, matching the brief. Group by **item_class plus attribute set**, never by item_class
alone: `tf_weapon_sniperrifle` covers 30 rows, and five of them are not reskins.

**The base groups** (each row is a defindex, then its attributes as the tsv lists them)
- **Stock rifle**, `tf_weapon_sniperrifle`, no attributes: 14, 201, 664, 792, 801, 851 (AWPer Hand), 881,
  890, 899, 908, 957, 966, 15000, 15007, 15019, 15023, 15033, 15059, 15070-15072, 15111, 15112, 15135,
  15136, 15154 (reskins and warpaints).
- **Sydney Sleeper** (230), `tf_weapon_sniperrifle`: `set_weapon_mode` (1: no crit headshot),
  `jarate_duration`, `mult_crit_chance`, `mult_sniper_charge_per_sec`.
- **Machina** (526) and **Shooting Star** (30665), `tf_weapon_sniperrifle`: `sniper_only_fire_zoomed`,
  `sniper_penetrate_players_when_charged`, `sniper_full_charge_damage_bonus`, `sniper_fires_tracer`.
- **Hitman's Heatmaker** (752), `tf_weapon_sniperrifle`: `rage_on_kill`, `rage_on_assists`,
  `set_buff_type`, `bodyshot_damage_modify`, `decapitate_type`.
- **Bazaar Bargain** (402), `tf_weapon_sniperrifle_decap`: `mult_sniper_charge_base_dummy` (heads change
  the charge rate).
- **The Classic** (1098), `tf_weapon_sniperrifle_classic`: `sniper_no_headshot_without_full_charge`,
  `sniper_crit_no_scope`, `crit_on_hard_hit`, `bodyshot_damage_modify`.
- **Huntsman**, `tf_weapon_compound_bow`: 56, 1005 (Festive), 1092 (Fortified Compound, same stats [W6]).
- **SMG**, `tf_weapon_smg`: 16, 203, 1149, 15001, 15022, 15032, 15037, 15058, 15076, 15110, 15134, 15153.
- **Cleaner's Carbine** (751), `tf_weapon_charged_smg`: mini-crits on demand.
- **Jarate**, `tf_weapon_jar`: 58, 1083, 1105.
- **Passive secondaries**: Razorback 57 (`tf_wearable_razorback`), Darwin's Danger Shield 231, and Cozy
  Camper 642 (`tf_wearable`).
- **Melee**, `tf_weapon_club`: Kukri 3/193, Tribalman's Shiv 171 (bleed), Bushwacka 232 (mini-crits become
  crits, takes 20 % more damage while held), Shahanshah 401 (stronger under half health).

**Today's draw** (`src/tf2/server/Econ/BotInventory.cs:84-109`) picks one row per slot, uniformly and on
its own: Huntsman 3/35 primaries, Jarate 3/19 secondaries, Razorback 1/19. The pairings that make an
archetype are left to chance.

**Archetypes** (C.3.1's names where they exist, `docs/plans/plan_bots_teams_tf2.md:742-744`)

| # | archetype | loadout (primary / secondary / melee) | why people run it [W1] [W6] | what changes in the bot |
|---|---|---|---|---|
| A | **Marksman** (stock) | stock rifle / SMG / Kukri or Shiv | the simplest consistent damage, and it hides your position (6s whitelist: SMG "a popular choice") | long lines; headshots on light classes; charge for heavy ones; SMG under 750 u; relocates after kills |
| B | **Marksman + Jarate** (pub default) | stock rifle / Jarate / Bushwacka or Kukri | Jarate: mini-crits on a group, Spy reveal, extinguish; Bushwacka turns those mini-crits into crits. Banned in 6s [W6]; a pub staple | as A, plus jar throws on groups, Spies and burning teammates; the Bushwacka swing only on a jarated target |
| C | **Huntsman** | Huntsman / Jarate or Darwin's / Shiv or Kukri | mobile, medium range, no dot; pre-draw at corners; fights near the front [W1] | 300-1,500 u fights, moves with the team, draws before corners, lobs with drop; asks a Pyro for fire |
| D | **Guarded** | stock rifle / Razorback / Shahanshah or Kukri | blocks one backstab; for when Spies are a problem [W1] | fewer checks behind; on the Razorback's break sound, turns and fights; picks it by the enemy's Spies |
| E | Sydney Sleeper support | Sleeper / SMG or Jarate / Kukri | body shots coat targets in Jarate; good on buildings; banned in 6s [W1] [W6] | aims at the chest; values a charged shot into a group; stays near the team |
| F | Machina / Shooting Star | Machina / SMG / Kukri | full-charge shots pierce lines of players; tracer gives you away; banned in 6s [W1] [W6] | fires only zoomed and full; prefers corridors where targets line up; moves after every shot |
| G | Bazaar Bargain | Bargain / SMG or Razorback / Kukri | heads speed the charge; early heads on light classes [W1] | light classes first; more careful (a death loses the heads) |
| H | Classic | Classic / SMG / Kukri | charges unzoomed; headshot only at full charge [W1] | charges while walking; fires full charges only |
| I | Heatmaker | Heatmaker / Razorback / Kukri | Focus: fast charge, no unscope after a shot [W1] | **blocked**: Focus never activates in the port (§3) |
| — | modifiers | Cozy Camper 642 (campers), Darwin's 231 (vs Pyro, with the Huntsman), Carbine 751 | [W1] | Cozy Camper raises hold time and lowers relocation; Darwin's lowers the flee range against Pyros |

---

## 3. What the port and the AI can do today

**Game side** (`src/tf2/`)
| behaviour | state | where |
|---|---|---|
| zoom, charge, unzoom, rezoom; headshot crit with the 0.2 s gate | works | `src/tf2/shared/Weapons/TFSniperRifleBehaviour.cs:59-74,330-349,354-400`; `src/tf2/server/Combat/TFDamageLayer.cs:263-285` |
| charge 50/s from 0 to 150 after the 0.3 s zoom, damage max(charge, 50) | works | `TFSniperRifleBehaviour.cs:59-63,95`; full charge 3.3 s from the zoom [W3] |
| laser dot (server, client) | works | `src/tf2/server/Entities/SniperDot.cs:39`, `src/tf2/client/Entities/ClientSniperDot.cs` |
| Sleeper's jarate on hit, Machina's zoomed-only fire, penetration, tracer; Classic; Bazaar's head rate | works | tsv evidence rows 230, 526, 1098, 402; `TFDamageLayer.cs:181,205-207` |
| Huntsman draw, release, tired shot at 5 s, speed 1,800-2,600 | works | `src/tf2/shared/Weapons/TFBowBehaviour.cs:58,68`; `src/tf2/server/Projectiles/TFRadiusDamage.cs:296-301` |
| **Huntsman headshot** | **missing** | the arrow's hit always pushes `HitGroup.Generic` (`src/tf2/server/Projectiles/TFProjectileEntities.cs:1672`); stock tests `pBox->group == HITGROUP_HEAD && CanHeadshot()` and crits with `TF_DMG_CUSTOM_HEADSHOT` (`tf_projectile_arrow.cpp`, about :446-500). The tsv's `works` on row 56 does not cover it |
| **Heatmaker Focus activation** (IN_RELOAD with the meter full → `ActivateRageBuff`) | **missing** | stock `tf_weapon_sniperrifle.cpp:420-431`; the port's rifle has no Reload arm (grep `Reload` in `TFSniperRifleBehaviour.cs` is empty); row 752's evidence says `set_buff_type` is "grep empty". The meter fills; the buff can never begin |
| Jarate (throw, splash, mini-crits, extinguish refund, 20 s meter) | works | tsv row 58 evidence; `src/tf2/server/Projectiles/TFProjectileEffects.cs:544` (`JarSpeed` 1000, `TFRadiusDamage.cs:564`) |
| Razorback (blocks one backstab, recharges) | works | row 57: `TFSpy.cs:545`, fact `ARazorbackBlocksOneBackstab` |
| Cozy Camper, Darwin's, Bushwacka, Shiv, Shahanshah, Carbine | works | tsv rows 642, 231, 232, 171, 401, 751 |

**AI side** (`src/ai/tf2/server/`, `src/ai/core`, `src/ai/server`)
| need | state | where |
|---|---|---|
| play a Sniper at all | **no**: every bot is a Soldier | `TFBotClassPick.cs:22`; `Trees/TFClassTrees.cs:15` answers Soldier for every class |
| per-class fight | **no**: `Engage`/`Hands` are the Soldier's (launcher, shotgun, feet aim, rocket jump) | `Trees/TFDecider.Soldier.cs:92-149`; ranges `TFDecider.cs:46` (320/900); the tree `TFDecider.cs:254-272` |
| own zoom state, charge %, Jarate/Razorback meter, heads, Focus | **no**: `SelfState` has no such field; own `Conditions` are only burning, stunned, invulnerable, disguised, cloaked, taunting | `src/ai/core/Snapshot/PerceivedView.cs:33-67`; `Senses/TFBotGameView.cs:60-67,129-132` |
| see an enemy zoomed, jarated or bleeding; see an enemy Sniper's dot | **no**: `ActorConditions` is a byte with 6 bits used; `ThingKind` is projectile or building; `TFThingType` has no dot | `PerceivedView.cs:59-67,98-108`; `Senses/TFBotGameView.cs:16-32` |
| an enemy's facing (is that Sniper aiming at me?) | partial: `ActorFacts` has no eye direction | `PerceivedView.cs:88-98` (C.2 lists the facing as still to come) |
| head aim point | partial: `AimPoint.Head` exists, but `TFKnowledge.AimHeight(Head, class)` takes the *viewer's* class's eye and the decider's `AimHeight` holds the body only | `src/ai/core/Brain/IntentCommand.cs:38-49`; `Knowledge/TFKnowledge.cs:162-170`; `Trees/TFDecider.Feel.cs:80-93` |
| a flick to a head lasts as long as a head is small | **no**: Fitts' W is a constant 4° (a body at mid range) | `src/ai/core/Motion/HumanMotion.cs:91-94,210-211` |
| a scoped view turns slower (the mouse's zoom scaling) | **no**: one `TurnPeakDegPerSec`, 300-900°/s | `HumanMotion.cs:29-32`; stock client scales sensitivity by `localFOV/defaultFOV × zoom_sensitivity_ratio` (`client/view.cpp:544-549`), so 20/75 to 20/90 when scoped |
| vision narrows when scoped | **no**: the cone is the client's 90° | `src/ai/core/Perception/Sight.cs:11-14`; `src/ai/server/Senses/VisionPass.cs:95`; stock re-reads the player's FOV each update (`tf_bot_behavior.cpp:116`) |
| fire only when settled | partial: the hands wait out the reaction and the first 70 % of a flick past 400 u; nothing waits for the crosshair to *stay* still | `src/ai/server/Apply/BotCommandApplier.cs:382-401`; `HumanMotion.cs:126-129` |
| zoom as a toggle | **hazard**: `AltFire` Tap toggles the button every other tick (`BotCommandApplier.cs:616-619`); with the rifle's 0.3 s re-arm (`TFSniperRifleBehaviour.cs:330-349`) that would zoom in and out on a cycle | needs a one-shot ability row like `uber` (`Knowledge/TFAbilities.cs:50`) |
| projectile lead with drop (bow, jar) | exists: `AimLead.Lob` | `src/ai/core/Predict/Predictors.cs:83`; `Engage` calls only `AimLead.Point` (`TFDecider.Soldier.cs:103`, `Predictors.cs:97`) |
| aim error by Accuracy, drifting | exists: `aimErrorDeg` 11.2° down to 0.12° | `game/mod_tf2/scripts/ai/tf2_skills.json` (`parameters.aimErrorDeg`); `Trees/TFDecider.Feel.cs:105-120` |
| long sightlines | **no**: the sight table reaches 1,024 u; the `LongLine` pass (C.3.2, 2,048 u) is not built | `src/ai/nav/Nav/NavSight.cs:32` |
| sniper spots learned | **no**: `TFSpotKind.SniperHold` exists, but nothing writes or reads it (its only use is the enum) | `src/ai/tf2/server/Nav/TFExperienceHolds.cs:12-21` |
| target priority | partial: a Medic bonus of 0.35, the carrier, the rank | `Trees/TFDecider.Feel.cs:128,160-173` |
| retreat to cover, glances, prefire | exists (shared) | `Trees/TFDecider.Fight.cs:165`, `TFDecider.Feel.cs:187-193`, `TFDecider.Prefire.cs` |
| a test map with long lines | **no**: `ss_ai_tf_cp` (plan C.3.2) is not among `game/mod_tf2/maps/src/ai/` (course, double, ends, jump, pack, wall) | |

**Missing, by side**
- Game: Huntsman headshots; Heatmaker Focus activation. Both are ports, not bot work. Each tsv row should drop
  to `partial` until its port lands, so a bot cannot draw an item it cannot use.
- AI, perception: own zoom, charge and meters; enemy zoomed, jarated and bleeding conditions, the dot and
  the facing; the narrow scoped vision cone.
- AI, motion (core, generic): Fitts' W per look; a turn-rate scale per look; a settled-fire style.
- AI, brain: the Sniper subtree; the archetype draw; the long-line table; SniperHold spots; ability rows.

---

## 4. The design for the code lane

### 4.1 Ship order
1. **A Marksman** (stock rifle, SMG, Kukri) on CTF 2fort and Payload badwater. These are the gated maps
   (plan Q8, `plan_bots_teams_tf2.md:1653`).
2. **B Marksman + Jarate.** The most-played pub alternative: one ability row and one throw rule over A.
3. **D Guarded** (Razorback). Passive: only the check-behind rate and a break reaction change.
4. **C Huntsman**, after the arrow headshot port (a game lane, size S). The bow ships body-shot-only
   meanwhile only if the user asks for that.
5. Long tail, each on A's machinery plus a rule: E Sleeper, F Machina/Shooting Star, G Bazaar, H Classic,
   then I Heatmaker after its Focus port.

### 4.2 Drawing the archetype, and the loadout from it
- **The draw.** At spawn, as Sniper, a bot draws an archetype from `tf2_subclasses.json` (C.3.1,
  `plan_bots_teams_tf2.md:690-704`) by its seed. Weights (ours): A 40, B 30, D 10, C 10, E-H 10 shared.
  The weights are multiplied by C.4's reactions: Spies seen or a backstab death raise D ×2; enemy Snipers
  seen raise A ×1.5. The draw holds until a class change or a map change.
- **The loadout follows.** Each archetype names a preference list per slot. A slot names a **group**
  (§2), and the draw inside the group is uniform over its rows: reskins and warpaints for looks (C.3.3).
  A slot the archetype leaves free draws only from **neutral** groups (ours: SMG, Kukri, Shiv for A), never
  a sidegrade that changes play. Today any of 30 rifle rows can come up: the Sleeper cannot crit a
  headshot, and the Machina cannot fire unscoped.
- **The kit read.** The decider learns its archetype and per-item flags from its own drawn loadout (a
  player knows their loadout), not from `WeaponFacts`. One kind covers the stock rifle, the Sleeper, the
  Machina and the Heatmaker (`Knowledge/TFKnowledge.cs:118-125`). The flags: `NoCritHeadshot`
  (set_weapon_mode 1), `OnlyFireZoomed`, `PenetratesWhenFull`, `HeadshotNeedsFull`, `ChargesUnzoomed`,
  `HeadsSpeedCharge`, `Focus`, `BlocksBackstab`.
- **The port's path.** Through `TFLoadoutProvider`/`ItemIsAllowed` (C.3.1, `plan_bots_teams_tf2.md:697-701`):
  a mode's restriction binds a bot too. `BotInventory.ForBot` keeps its seed hash and is handed the
  archetype's groups.

### 4.3 Decision rules, archetype A (the base the others change)

**Spots** (C.3.2, `plan_bots_teams_tf2.md:670`)
- At load, a long-line table: candidate cells (the `ChokeMap` top set, `src/ai/core/Nav/ChokeMap.cs:34`,
  plus the objective feed's points, cart route and flag rooms). Rays to 3,000 u (ours: 2fort's courtyard
  lines pass C.3.2's 2,048). Vision itself reaches 6,000 u (`Sight.cs:11`, as stock's `tf_bot_vision.cpp:467`).
- A vantage scores by:
  - what it sees: chokes, objectives, the route the enemy must take;
  - minus how many cells see it back;
  - range to what it watches of at least 1,000 u (`tf_bot.cpp:52`);
  - not in the enemy's half beyond 750 u of the objective when attacking, up to 750 u past it when
    defending (`tf_bot.cpp:55,2390-2402`).
- CTF has no stock goal (`tf_bot.cpp:2321-2325`). Ours: the enemy flag room's exits on attack, our flag's
  routes on defence.
- Payload: recompute after the cart moves 500 u (`tf_bot.cpp:58`).
- **Claiming.** A spot is claimed by a callout (B.4) so two Snipers spread. With all taken, a Sniper
  doubles up (stock `tf_bot_sniper_lurk.cpp:376-389`).
- **Learned spots.** Once Part G's store writes `SniperHold` (`TFExperienceHolds.cs:18`), they are drawn by
  Discipline like `PickLearnedHold` (`Trees/TFDecider.Holds.cs:33-46`), with the long-line table as the
  fallback.
- **Getting there.** Unscoped, by the safest route (stock `SAFEST_ROUTE`, `tf_bot_sniper_lurk.cpp:215`).
  It shoots opportunistic targets on the way (`:31,126-149`).

**Holding**
- At the spot (within 25 u, `tf_bot_sniper_lurk.cpp:161`): zoom, and look at the watched line's far end.
- **Boredom.** No enemy for 10 s ×U(0.9,1.1) (`tf_bot_sniper_lurk.cpp:29,39`) → a new spot. If the old
  spot had targets, the new one is within 500 u of it (`:321-347`).
- **After a kill.** Change spot within 15 s, in 8 of 10 seeds (C.3.2's fact). Ours: leave after a hold
  drawn from U(2,8) s.
- **After being hit from range** (a `Damaged` with no attacker seen, or by a Sniper): unscope, break line
  at once, and do not re-peek the same angle. Stock retreats if hurt within 1 s (`tf_bot_sniper_attack.cpp:79-82`).
- **Checks behind.** A glance behind every 6-14 s (ours) while scoped, scaled by Alertness, ×3 while a Spy
  is suspected (C.3's counter), ×0.3 for D. A glance costs an unzoom only if it turns more than 60° (ours).
  Players spy-check by moving side to side while reloading [W1].
- **The dot.** It never jitters: aim noise rides `AimWander`'s low-frequency drift
  (`Trees/TFDecider.Feel.cs:105-120`). While waiting, the bot parks the dot on a doorway or an edge of the
  watched line (ours: one of the long-line's three far cells, re-chosen every 3-8 s).

**Close threats** (stock numbers)
- An enemy within 750 u → SMG (`tf_bot.cpp:3394-3398`).
- Within 400 u → retreat to cover (`tf_bot_sniper_attack.cpp:19,74-77`).
- Within 200 u → melee, giving up past 250 u (`tf_bot_sniper_lurk.cpp:117-121`).
- A Scout at any range under 750 u: never the scope [W1].

**Losing the target.** Keep the aim on it for 2 s (`tf_bot_sniper_lurk.cpp:30`). Stay scoped on its last
point for 5 s ×U(0.75,1.25) (`tf_bot_sniper_attack.cpp:21,85`), then go back to the watched line.

**Target choice while scoped**
- Order: immediate threats, then the wiki's tiers [W1] [W2]. Bonus (ours): Sniper 0.5, Medic 0.4, Demoman
  0.25, Heavy 0.25, on top of today's `TakesTarget` (`Trees/TFDecider.Feel.cs:160-173`). Scouts score
  only within 750 u or near an objective.
- **Immediate threats** (`tf_bot_sniper_attack.cpp:161-226`):
  - an enemy Sniper facing the bot with cos > 0.8. This needs the facing on `ActorFacts`; until then, an
    enemy Sniper the bot sees zoomed;
  - a sentry aimed at it.
- **No switching** while zoomed more often than every 3 s (`tf_bot_vision.cpp:16`
  `tf_bot_sniper_choose_target_interval`), unless the bot is hit.

**When to fire** (the human rule; this is where the "aimbot" is avoided)
- Zoomed for at least 0.2 s (the headshot gate: `TFSniperRifleBehaviour.cs:74`, `TFDamageLayer.cs:270-281`).
- **The charge needed for a kill.** Damage is max(charge, 50); a headshot is ×3 (`TFSniperRifleBehaviour.cs:95`;
  [W3]). The bot reads its own charge off the HUD (foundation, §4.6). It assumes the target's class max
  health from the class table, ×1.5 if it sees a Medic within 450 u of the target (overheal; ours). That
  gives a headshot's needed charge of hp/3:
  - Scout, Engineer, Sniper, Spy (125) and Medic (150): no charge. Fire once settled.
  - Pyro and Demoman (175): 59, which is 1.17 s of charge.
  - Soldier (200): 67, 1.33 s.
  - Heavy (300): 100, 2.0 s.
  - Overhealed Heavy (450): 150, 3.0 s.
  A body-shot kill needs charge ≥ hp (125 → 2.5 s). The bot fires early if the target is about to leave
  its sight (moving toward cover in its prediction), or if the target is aiming at it.
- **Settled crosshair.** The view is in `Pursuit` (not `Flick`/`Correct`, `HumanMotion.cs:54-67`). It has
  been within the head's angular radius of the intended point for `steadyMs`. `steadyMs` is a Sniper curve
  by Accuracy, ours: 450 ms at level 0 down to 120 ms at level 10. Stock's 0.1 s with yaw under 10°/s
  (`tf_bot_behavior.cpp:37,129,1393`) is the floor no level goes below.
- **The intended point**, by `headAimShare` (Accuracy, ours, after stock's split at
  `tf_bot_behavior.cpp:745-765`):
  - the head at Hard/Expert levels;
  - two thirds of the way to the head from the chest at Normal;
  - the chest at Easy.
  The error is the drifting `aimErrorDeg`. **The hit is never decided by the brain**: the head hitbox and
  the bot's own error decide it.

**What makes the aim human** (core, generic; each is a 5b metric)
- **Fitts' W.** A look carries the target's angular size: 2·atan(r/d), with the head's r measured from the
  mod's hitboxes (a measurement to make). A 10° flick to a head at 1,500 u (W about 0.4°) then takes
  0.08 + 0.06·log2(1+25) ≈ 0.36 s against 0.17 s to a body (`HumanMotion.cs:91,210`). The constant
  `TargetWidthDeg` (`:94`) becomes the default.
- **Zoomed turn rate.** While zoomed, `TurnPeakDegPerSec` × (zoomFov / defaultFov): 20/90 = 0.22 to
  20/75 = 0.27 (`client/view.cpp:544-549`; `ZoomFov` 20, `TFSniperRifleBehaviour.cs:68`), so 65-245°/s
  scoped. A fast-crossing Scout at 400 u (≥ 40°/s of yaw) is near the limit of a low-skill hand. That is
  why players unscope; it is honest, not a rule.
- **Vision while zoomed.** The cone narrows to the zoom's 20° (stock `tf_bot_behavior.cpp:116`), so a
  scoped bot misses the flank as a person does.
- **Reaction and recognition.** Unchanged (`Sight.cs:26-32`, `tf2_skills.json` `reactionMs` 100-240). A
  first shot therefore comes at least recognise + react + flick + steady after first sight: about 0.55 s
  at the top and more than 1.2 s at the bottom (ours, from the numbers above). Measured against the
  human sessions (§4.7).

### 4.4 Rule changes by archetype (over A)
- **B Jarate.** Throw when the jar is ready (the HUD meter, §4.6) and one of these holds (ours):
  - ≥ 2 enemies within 200 u of a point in 300-1,000 u (the splash radius is 200 u [W7]);
  - a suspected or decloaking Spy within 600 u (Jarate reveals cloak [W7]);
  - a burning teammate within 600 u (extinguish; cooldown 20 s → 16 s [W7]), or itself burning.
  Aim with `AimLead.Lob` at `JarSpeed` 1000 (`Predictors.cs:83`, `TFRadiusDamage.cs:564`), at the feet
  [W1]. Throw from cover. Never at a Pyro facing the bot (airblast [W1]). With the Bushwacka: switch to it
  only for a jarated target within 150 u (ours; the 20 % damage taken [W1]). Otherwise as A.
- **C Huntsman.**
  - Range 300-1,500 u (ours); the drawn walk is 53 % speed [W8].
  - Pre-draw at the corner a prefire predicts (`TFDecider.Prefire.cs`) [W1].
  - Release at charge ≥ 0.95 (stock `tf_bot_behavior.cpp:1382`) or, under 400 u, at once. Never hold past
    4.5 s (the tired shot at 5 s, `TFBowBehaviour.cs:68`): lower with the second trigger.
  - Aim with `AimLead.Lob` at the charged speed and gravity (`TFKnowledge.cs:69-91`); at the head from
    Normal up once arrow headshots exist.
  - Routes and distance: with the team, not on a long line; its distance to the team front is under
    800 u (ours) against A's 1,500+.
  - SMG/Darwin's fallback as A.
  - Stands by a Pyro to be lit when one is near (C.3.2's Pyro row, F20).
- **D Guarded.** Check-behind rate ×0.3. On the Razorback's break (a sound tap), turn at once (a damage
  turn, `plan_hl2mp_ai.md` §3.4.1 "Turns"), switch to melee, and call "Spy!" (`TFAbilities.VoiceSpy`).
  Shahanshah: switch to it under 50 % health for a close fight.
- **E Sleeper.**
  - `headAimShare` → chest at every level (no crit headshot, `TFDamageLayer.cs:181`).
  - Fire at 60 % charge or more (ours), when the jarate lasts long enough (`TFSniperRifleBehaviour.cs:110`
    remaps 50-150 charge to its duration).
  - Prefers targets in a group, and buildings.
  - Holds a spot within 1,000 u of the team (ours).
- **F Machina/Shooting Star.** Never unzoomed fire (`OnlyFireZoomed`). Waits for full charge. A target
  scores +0.3 per enemy within 64 u of the shot line beyond it (ours). Relocates after every shot (the
  tracer, [W1]).
- **G Bazaar.** Light classes' bonus +0.3 until 2 heads (ours; [W1]). Retreat floor raised to 60 % health
  (ours: a death loses the heads).
- **H Classic.** Charges while walking unzoomed (`ChargesUnzoomed`). Fires only at full charge for a
  headshot (`HeadshotNeedsFull`, `TFDamageLayer.cs:205`). Does not jump with a charge [W1].
- **Cozy Camper modifier.** Boredom 15 s instead of 10. Leaves 4-12 s after a kill instead of 2-8 (ours).

### 4.5 Ability rows (`Knowledge/TFAbilities.cs`)
| row | run | why |
|---|---|---|
| `zoom` | Buttons `Attack2`, `HoldTicks` 2 | a one-shot edge: the rifle toggles on the press and re-arms after 0.3 s (`TFSniperRifleBehaviour.cs:330-349`). The decider sends it only when its zoom state (HUD) differs from what it wants, so `AltFire` Tap's cycling never reaches the rifle |
| `bow_lower` | Buttons `Attack2`, `HoldTicks` 2 | cancels a draw (`TFBowBehaviour` :28-35 summary) |
| `jarate` | Program: select the jar's slot, wait the deploy, aim (Lob), `Attack` one tap, reselect the primary | like `RocketJumpProgram` (`TFAbilities.cs:47`); the view stays the motion model's |
| `focus` | Buttons `Reload`, `HoldTicks` 2 | Heatmaker only, after its port (stock `tf_weapon_sniperrifle.cpp:423`) |
| `voice_spy`, `voice_thanks` | exist | the Razorback break; thanks for a lit arrow |
| `voice_incoming` (menu 1, item 0 in `voicecommands.txt`, to verify) | Command | a callout of an enemy Sniper's spot (B.4) |

### 4.6 What the code lane needs from the shared P7 foundation
1. **The class pick and composition** (C.4): `TFBotClassPick.Class` (`TFBotClassPick.cs:22`) becomes the
   roster target. The Sniper uses stock's limits: none under team size 5, none at Easy, 1 above
   (`tf_bot.cpp:841,855`), with a cap of 2 [W1]. The global class cooldown as ruled.
2. **The subtree hook**: `TFClassTrees.TreeFor` answers `Sniper` (`TFClassTrees.cs:15`).
   - `Engage`/`Hands`/the fight sequence are per class: today they are wired into `Decide`
     (`TFDecider.cs:197-201`) and the tree (`:254-257`) as the Soldier's.
   - Per-class fight ranges replace `FightNear/FightFar` (`:46`).
   - A class branch slot (the Sniper's "spot") goes in the root (`:259-272`), with per-class branch
     weights (`Weigh`).
3. **The kit read and the archetype draw** (§4.2): the drawn archetype and item flags reach the decider
   at spawn, and `BotInventory` draws by group.
4. **HUD reads on `SelfState`**, generic in the core: a `Charge` 0-1 (the rifle's or bow's meter, from
   `WeaponNetState.BehaviourMeter`) and an `ItemMeter` 0-1 (Jarate, Razorback, Focus,
   `TFItemMeter`/`TFHudItemMeter`); own `Zoomed`. `ActorConditions` widens from `byte` to `ushort` with
   `Zoomed` (the scoped pose and the dot, drawn for all), `Jarated` (drawn yellow) and `Bleeding`.
   `ActorFactsShapeTests` holds the member list (`PerceivedView.cs:80-86`).
5. **The facing on `ActorFacts`** (C.2's open item) for "a Sniper is aiming at me". The dot as a
   `ThingKind` of its own (`TFThingType.SniperDot`) so other classes can react to it.
6. **Core motion, generic and mod-neutral:**
   - the `LookIntent` carries a target angular width (W) and a turn scale (the zoom);
   - `VisionParams.FovDeg` follows the bot's own FOV each tick;
   - a `FireStyle.Settled` the applier enforces from the motion model (Pursuit plus `steadyMs` inside W),
     beside `HandsWaitForTheEye` (`BotCommandApplier.cs:382-401`).
   These are core changes: HL2MP's replay facts must stay byte-identical, so a look with no W keeps 4°.
7. **The nav tables:** the long-line pass (`NavSight.cs:32` is 1,024 u), and Part G's `SniperHold` spot
   writing.
8. **Per-class curves** (C.5): a `sniper` section in `tf2_skills.json` with `steadyMs`, `headAimShare`
   and a Sniper `aimErrorDeg` (below).
9. **A test map** `ss_ai_tf_lines`: two long lines of 1,500 u and 2,500 u onto a point, a choke, and a
   flank corridor for a scripted Spy.

### 4.7 The aim's human gate (Q6 on 5b), Sniper specific
- **Reference sessions.** The user plays a Sniper on `ss_ai_tf_lines` and on ctf_2fort with
  `ai_record start` (5b step 3's rules: anonymised, local, committed only on the user's word). The target is
  ≥ 300 scoped flicks and ≥ 50 engagements.
- **The metrics** (§3.4.4 of the HL2MP plan, scoped):
  - scoped peak turn rate per flick;
  - time from `SightGained` to the shot;
  - steady time before a shot;
  - overshoot;
  - headshot share of hits by range band (under 1,000, 1,000-2,000, over 2,000 u);
  - snaps over 90° per minute.
- **The gate.** KS D ≤ 0.25 against the reference at the matching preset (plan ceiling). The spin-bot
  detector must also be green.
- **Mutants that must go red:** W fixed at 4°; no zoom turn scale; steadyMs 0; the head always the point.
- **Until the references exist,** the Sniper's `aimErrorDeg` floor is held at or above stock's
  0.01 rad = 0.57° radius (`tf_bot_behavior.cpp:36`), drawn as a radius. Today's general curve reaches
  0.12° at level 10 (`tf2_skills.json`); that is too tight for a scoped head until a person shows it.

### 4.8 Facts to write (one behaviour each, each red once)
- **Draw and kit**
  - The archetype draw is the same for a seed.
  - A slot an archetype leaves free never draws a sidegrade group.
  - Only `works` rows are drawn (C.3.1); with rows 56 and 752 at `partial`, no C and no I.
- **Zoom and fire**
  - The zoom row is sent only on a state change: over 5 s held, the rifle zooms once (mutant: `AltFire`
    Tap → several toggles).
  - No shot within 0.2 s of zooming.
  - No shot while the motion is `Flick`/`Correct`, nor before `steadyMs` (mutant `steadyMs` 0 red).
  - A Heavy is not fired at before charge 100, a Scout is fired at uncharged (two facts).
- **Aim**
  - A head flick takes longer than a body flick at the same angle (W).
  - The scoped peak turn rate ≤ unscoped × 20/defaultFov.
  - The scoped vision cone is 20°: an enemy at 30° off the view is not seen when scoped and is seen
    unscoped.
- **Close threats**
  - SMG within 750 u; retreat within 400 u; melee within 200 u.
- **Spots and holding**
  - The top three spots on `ss_ai_tf_lines` see the point or a choke and are seen by under 20 % of the
    cells that see the point (C.3.2).
  - The Sniper is at a top-five spot > 60 % of its alive time.
  - It changes spot within 15 s of a kill in 8/10 seeds.
  - After 10 s with no enemy seen it moves.
  - Hit from range, it unzooms and leaves the line within 1 s.
  - While scoped, it does not switch targets inside 3 s unless hurt.
- **Archetypes**
  - Jarate: thrown at a group of 2 in its splash, not at one lone enemy; thrown at a burning teammate.
  - Guarded: fewer checks behind than A over the same seeds (flat mutant red).
  - Huntsman: never holds a draw past 4.5 s; its mean engagement range is under A's.
  - Sleeper: aims at the chest.
  - Machina: never fires unzoomed or under full charge.
- **Honesty**
  - Target health is never read: the charge rule uses only the class table and seen Medics (a mutant
    reading the server's health goes red).

### 4.9 Live measures
**Objective time and map progress first** (user ruling). Same seeds, same skills, on ctf_2fort and
pl_badwater:
- teams of 6 bots with one Sniper, against teams with that slot a Soldier;
- record caps per 10 min, and the time the enemy takes to cap or push;
- payload cart metres per minute, attack and defence;
- the Sniper team should defend at least as long; on attack, enemy deaths in the 20 s before each push
  rise.

**Then class numbers** (per 10 min, by archetype):
- kills and deaths; headshot share of rifle kills (`TFDamageCustom.Headshot`, a counter to add);
- share of alive time scoped and at a spot;
- relocations after kills;
- first-shot delay from sight;
- scoped peak turn rates against the envelope;
- hit rate by range band (`AimMeter`, `src/ai/server/Senses/AimMeter.cs`);
- Jarate throws and targets coated; Razorback blocks; backstab deaths (D lower than A);
- Huntsman mean engagement range and distance to the team front (C lower than A).

**The proof of different play.** Distinct archetype distributions in `tools/ai-replay jank`'s per-actor
block: A's range median > 1,200 u, C's < 1,000 u; B's jar throws > 0; D's checks behind < A's.
Screenshots are opened, not trusted.

---

## 5. Sources
- [W1] TF2 Wiki, Community Sniper strategy (positioning, priorities, every unlock, matchups):
  https://wiki.teamfortress.com/wiki/Community_Sniper_strategy
- [W2] TF2 Wiki, Sniper (competitive) (6s role, priorities, mode positioning):
  https://wiki.teamfortress.com/wiki/Sniper_(competitive)
- [W3] TF2 Wiki, Sniper Rifle (damage, 3.0 + 0.3 s charge, 0.2 s headshot gate, 27 % scoped speed, the dot):
  https://wiki.teamfortress.com/wiki/Sniper_Rifle
- [W4] TF2 Wiki, Community competitive play (gate picks, last-point stickies, 6s vs Highlander maps):
  https://wiki.tf2.com/wiki/Comp_play
- [W5] TF2 Wiki, Standard community competitive lineup / Highlander (one of each class):
  https://wiki.teamfortress.com/wiki/6s and https://wiki.tf2.com/wiki/Comp_play
- [W6] TF2 Wiki, Sniper weapons (competitive) (6s whitelist: Machina, Sleeper and Jarate banned; SMG popular):
  https://wiki.teamfortress.com/wiki/Sniper_weapons_(competitive)
- [W7] TF2 Wiki, Jarate (20 s / 16 s recharge, 10 s / 5 s, 200 u splash, reveals cloak):
  https://wiki.teamfortress.com/wiki/Jarate
- [W8] TF2 Wiki, Huntsman (50-120, crit 150-360, 1 s charge, 5 s fatigue, 1,800-2,600 u/s, 53 % speed):
  https://wiki.teamfortress.com/wiki/Huntsman

**Stock files read** (`../source-sdk-2013-valve/src/game/`):
- `server/tf/bot/behavior/sniper/tf_bot_sniper_attack.cpp` (all: flee 400, melee 200, linger 5 s,
  immediate threats);
- `server/tf/bot/behavior/sniper/tf_bot_sniper_lurk.cpp` (all: patience 10 s, linger 2 s, opportunistic
  fire, hints, home search);
- `server/tf/bot/tf_bot.cpp:44-58` (sniper convars), `:822-870` (rosters), `:2295-2513` (spot accumulation),
  `:3055-3100` (threat danger), `:3150-3215` (attack ranges), `:3394-3398` (SMG under 750 u);
- `server/tf/bot/behavior/tf_bot_behavior.cpp:36-37,115-147` (aim error, steady rate, FOV),
  `:681-770` (bow lead and elevation, rifle aim points by difficulty), `:1370-1420` (fire gates);
- `server/tf/bot/tf_bot_vision.cpp:16,467` (3 s target interval, 6,000 u range);
- `shared/tf/tf_weapon_sniperrifle.cpp:420-431` (Heatmaker Focus on IN_RELOAD);
- the arrow's hitbox headshot (`server/tf/tf_projectile_arrow.cpp`, the block at about :446-500);
- `client/view.cpp:536-549` (zoom sensitivity scaling).
