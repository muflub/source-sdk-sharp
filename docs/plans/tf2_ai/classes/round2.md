# P7 round 2: the classes' second round

A plan, 2026-11-22: no code yet. It builds on round 1's plans ([README.md](README.md)) and what the round-1 lanes
and their Fable reviews found.

## 1. The guide

["The TF2 Complete Subclasses Guide"](https://steamcommunity.com/sharedfiles/filedetails/?id=1391651763), a Steam
Community guide by "Davy Mastah", posted 2018-07-17 and updated 2020-04-14.

**What it is.** It is not a base-class guide. It lists 19 *subclasses*: playstyles that players run with a given
loadout, played "differently enough" from the class they come from. Each entry gives a category (Competitive,
Gimmick, Joke, Friendly), a role (Offensive, Defensive, Support, Power, All-Rounder, Flank, Pick, Distraction), a tier
per format (regular and Medieval), the weapons, and how the subclass plays and is countered. That is the user's goal
put another way: the class plays differently with different weapon sets.

**How far to trust it.**
- Steam now shows the guide as removed for breaking its community guidelines. It was still readable on 2026-11-22.
  The page was cut at about 40,000 of 72,000 characters. Entries after Fat Scout (Boxer, Hoovy, Battle Engie,
  Ninjaneer, Battle Medic, Huntsman, Spycrab, Boxspy, Civilian) are known only from a summary of the cut part.
  They are marked *(summary)* below.
- Its tiers and counters are one author's view. As in round 1, every number a lane builds from (a range, a timing, a
  damage figure) is checked against the stock `tf_bot` code in the Valve clone or the TF2 wiki, and stock wins.

**What this plan takes from it:**
- the playstyles that change how a class decides: range, routes, targets and ability use;
- the counter notes, as rules for the classes that answer them (§5).

**What it leaves out, and why:**
- **Out of scope: playstyles about trolling or not playing.**
  - The Scunt: abusive binds, taunting after kills, and hostility to its own team.
  - The Hoovy *(summary)*: refuses to fight and feeds both teams.
  - The Spycrab and Boxspy *(summary)*: crouch or taunt instead of playing.
  - The Civilian *(summary)*: cannot attack.
  
  A bot that harasses its team or refuses to fight makes the game worse for the people in it. The user ruled out a
  friendly-bot mode too (2026-11-22, §9).
- **Cosmetic variants fold into their parent playstyle:** Demopan into the Hybrid Knight, Demorai into the
  Demoknight, Flyroshark into the Pyroshark. Their loadouts differ, but their decisions do not.

Every item the guide names that this plan uses is marked `works` in `game/mod_tf2/scripts/items/items_working.tsv`
on main at 2026-11-22: 56 of 56 checked. The exceptions are the Frying Pan (no row) and the items round-1 lanes are
marking `partial` (§3).

## 2. Where round 1 left each class (2026-11-22)

| Class | Archetypes built | Ready | Where it is |
|---|---|---|---|
| Scout | Flanker, Bonk, FaN, BFB | yes | landing batch 6 |
| Soldier | Stock, Roamer, Banner, DirectHit | yes (the default bot) | review fixes |
| Pyro | Stock, Guard, Combo, Backburner, Phlog | yes; 2fort being re-measured | the user's four airblast tactics (2026-11-22) |
| Demoman | Hybrid, Trapper, Demoknight, Hybrid knight, Loch, Combo | yes | landing batch 6 |
| Heavy | Standard, Ambusher, Anchor, Siege | yes | landing batch 6 |
| Engineer | Nest, Gunslinger, Wrangler, Rescue Ranger | no | review fixes; per-class cap in the composition |
| Medic | Stock, Combo, Kritz, QuickFix | no | done; lands unready |
| Sniper | Marksman, Jarate, Guarded | no | review fixes |
| Spy | Backstabber, Saboteur, Dead Ringer, Gunspy | no | Spy-against-Spy fix; core re-review |

**What round 1 taught that shapes round 2:**
- **Three runs per arm cannot separate 20 s on ctf_2fort.** The Soldier control alone swung from 104 s to 54 s
  between two batches. Round 2 measures with six runs per arm (§6).
- **The all-Soldier control mostly measures walking speed for the faster classes.** The Pyro beat it by exactly the
  ratio of 300 to 240 u/s. Round 2 weighs damage, deaths and the class's own job beside objective time.
- **A support or defending class needs a team measure,** not first-on-objective. The captures column read 0 until
  lane `cap` (in progress) credits point and flag captures.
- **Most of what each lane could not build was a shared hook.** So round 2 starts with them (§4).
- **Never measured live,** for lack of the other class: the Bonk sentry pass, the Demoman's sentry kill, the
  Saboteur, the Pyro's Über shove. With the Engineer and Medic fielded, round 2 measures these.

## 3. Game-side gaps round 2 needs

| Gap | Blocks | State |
|---|---|---|
| The Buffalo Steak's melee lock is never enforced (no TF melee `CanHolster`) | Heavy Boxer | Heavy lane marked 311 `partial` |
| Huntsman arrows hit as `HitGroup.Generic`: no headshot | Sniper Huntsman | not marked (Descent's tests need the row) |
| Hitman's Heatmaker Focus never starts (the rifle has no reload arm) | Sniper | Sniper lane marked 752 `partial` |
| The Sandman's ball; Crit-a-Cola's mark-for-death on attack | Scout | Scout lane marked 44 and 163 `partial` |
| No mini-crit on a reflected flare or arrow; rockets and the energy ball turn before they retarget | Pyro | in docs/divergences.md |
| Captures counted only on payload | every measure | lane `cap`, in progress |
| A pit or fall death is credited to the world, not to the player who pushed (stock credits the last airblaster) | Pyro env kills | open (the Pyro lane, 2026-11-22) |
| cp_foundry's nav build refuses it (5.99M leaves, over `ai_nav_build_max_leaves` 4M) | live pit pushes | open |
| The Quick-Fix does not mirror its patient's blast jump | Medic | open |
| Grenades (pipe, sticky, jar, milk, gas, cleaver) flew a flat, spin-free arc; jars and cleavers had no fuse, latch or contact rules | Demoman, Sniper Jarate, Scout cleaver | fixed in batch 11 (stock's arc to 0.01 u in an A/B) |
| Grenades bounce off players physically, and meet them on the bounding box, where stock's pass through (ShouldCollide) and hit hitboxes | Demoman, Sniper Jarate | in docs/divergences.md; needs TF collision groups (an ABI vocabulary change, or a managed pair-veto service in src/server/managed): to schedule |
| Buildings took model bounds after each model change and trapped the Engineer | Engineer | fixed in batch 11 |
| `func_nobuild` and `trigger_hurt` not read into map knowledge | Engineer, Pyro (S12) | fixed in batch 11 |

Each is a small port with a fact, owned by the class whose game code it is. A shared game base (`CanHolster`, the
arrow's hitbox) goes to the shared lane.

## 4. Phase 0: the shared hooks

Ownership: one shared lane per row group, serialised on the files they share (`TFDecider*.cs`, the core applier,
`PerceivedView`). Each hook is done when a fact holds it and a class has a caller.

| # | Hook | Who uses it |
|---|---|---|
| S1 | The jump program hands the bot its hands in flight: fire, swing, open the parachute, use a thruster | Soldier Paratrooper, Trolldier, Market Gardener; Pyro Thermal Thruster |
| S2 | The route planner reads the bot's own reach, jump cost and floor from its kit, not its class (`TFNavProfile.ReachOf` keys on the class today) | Scout Bonk Boy (Atomizer triple jump), BFB (no double jump); Soldier Rocket Jumper, Gunboats, Roamer; Demoman sticky jumps |
| S3 | A sticky jump kind in `BlastJumpSim`; a client's own stickies in the view (stock networks the launcher) | Demoman Combo, Hybrid |
| S4 | Enemy conditions a player sees, in the view: Jarated, milked, wet, marked for death, blast-airborne (a recording format bump) | Sniper Bushwacka; Scout Milkman; Pyro Pyroshark (Neon Annihilator crits the wet); Soldier Direct Hit, Reserve Shooter |
| S5 | A shared ammo errand: the Heavy's packs, cabinet and seen-dispenser code, lifted for every class | every class; Heavy first |
| S6 | No class change with a sentry or teleporter exit up | Engineer (lane building it now) |
| S7 | A building's sapped flag, as its team sees it | Pyro Pybro, Homewrecker; Engineer unsap order; Spy Saboteur |
| S8 | Teammates take a teleporter and stand at a dispenser | Engineer Battle Engie, Teleporter builder |
| S9 | The applier holds Fire at a teammate through a one-shot (the Medic lane wrote the exact change) | Medic beam |
| S10 | `ClassPlay.BacksOffDry`, so a class with a loaded secondary does not back off on its first dry decide | Scout |
| S11 | Respawn glows as teammate places for 10 s (`tf_spawn_glows_duration`) | Medic seek |
| S12 | A lethal-drop and hurt-zone query (the Pyro lane is building a TF2-side one for cliff shoves) | Pyro; any class that pushes or is pushed |
| S14 | Carriers and guards stuck in shared leaves on ctf_2fort: a Deliver carrier stood 142 s at (180, 2573, 153) and 76 s at (442, 2550, -192); Guard stands of 15-40 s at mid bridge (0, 500, 0); Escort stands of 11-17 s. Seen in every class's arms and the Soldier controls (the Spy lane's diagnosis, 2026-11-22) | every class: the objective time itself |
| S13 | The route planner's floor and cost per bot (the Soldier review: it budgets with the stock floor) | Soldier, Roamer |

## 5. Phase 1: each class

Each class lane, as in round 1:
1. finishes its round-1 leftovers;
2. builds the new archetypes in order, each with its rules, facts and a same-run A/B against the class's stock
   archetype;
3. adds its counter rules.

Weights start low for Gimmick-tier styles and are raised only on measured evidence.

### Scout
- **Leftovers:** the review's C5 (a stale dodge stage; hops starting at wall turns) and C6 (a double jump onto the
  route's objective cell). The one dry decide, through S10.
- **Milkman** (guide: Gimmick; Defensive/Support; tier B). Shortstop 220, Force-a-Nature 45 or Soda Popper 448;
  Mad Milk 222; Holy Mackerel 221 or a bat.
  - Plays safer and stays with the team: no pistol, so "he will most likely die if left behind".
  - Throws milk into two or more enemies in splash, onto a burning teammate to put him out, and at a teammate the
    bot suspects is a Spy (the Spy lane's suspicion marks).
  - Measure: milk throws, teammates extinguished, its team's deaths against the Flanker's.
- **Bonk Boy** (guide: Competitive; Offensive/Flank; tier S). Soda Popper 448, Winger 449, Atomizer 450.
  - "Jumps much more than a normal Scout"; "takes more risks and alternate routes": a corridor bias from lane
    Groutes' route spread.
  - Atomizer triple jumps on routes (S2); the Soda Popper's hype jumps.
  - Avoids Heavies and Engineers' sentries, which the guide names as its counters.
  - Measure: jumps a minute, the share of off-corridor routes, time to objective against the Flanker.
- From research: the Cleaver (812), with Payload in P8; the Shortstop as its own style.

### Soldier
- **Leftovers:** the current review round, S2 and S13 for the Roamer, the Beggar's Bazooka as a row or removed.
- **Paratrooper** (guide: Gimmick; Offensive/Power; tier C). Air Strike 1104, B.A.S.E. Jumper 1101, a melee.
  - "Keeps the high ground longer than a Soldier"; opens the parachute after a jump (S1).
  - Picks Scouts and Medics from a safe range; avoids Heavies and Snipers, which shut it down.
  - Measure: the share of its life above its target, its kills on Scouts and Medics, deaths to hitscan.
- **Soldierai** (guide: Gimmick; Offensive/Power; tier B). Black Box 228 or Direct Hit 127, Concheror 354,
  Half-Zatoichi 357.
  - Rockets at range, the katana up close.
  - Answers a katana with the katana: stock's Half-Zatoichi duel rule, read in the Valve clone, decides it.
  - The Concheror for its health on hit.
  - Measure: melee kills, katana duels won, Concheror pops.
- **Trolldier** (guide: Joke; Distraction/Pick; tier D). Rocket Jumper 237, Mantreads 444 or B.A.S.E. Jumper,
  Market Gardener 416.
  - Lives in the air and kills only by an airborne Market Garden crit (S1). It gives up all ranged damage.
  - Pin-only, at weight 0: it would cost a team a slot. It still has a use: jump maps and showing what the jump
    planner can do.
- From research: the Cow Mangler; the Escape Plan, once a rule drops it when an enemy is in sight; the Pocket,
  with the Medic.

### Pyro
- **Leftovers:**
  - the user's four airblast tactics (2026-11-22): shove off objectives, split an Über'd Medic from his patient,
    push enemies off cliffs, reflect what is predicted to arrive;
  - the reflected flare and arrow mini-crit;
  - stock's capture-point shove.
- **Pybro** (guide: Competitive; Defensive/Support; tier B). Flamethrower or Degreaser 215, Scorch Shot 740 or a
  shotgun, Homewrecker 153. Round 1's Guard, aimed at an Engineer.
  - Picks a teammate Engineer's nest and stays within about 400 u of it.
  - Spy-checks teammates near the nest with a flame puff.
  - Takes sappers off with the Homewrecker (S7), and puts out burning teammates.
  - The guide: "Spies can hardly take down an Engie nest with a Pybro nearby."
  - Needs the Engineer fielded.
  - Measure: the guarded nest's sentry uptime against an unguarded one, sappers removed, Spies killed within
    600 u of the nest.
- **Pyroshark** (guide: Gimmick; Defensive/Flank; tier C). Any flamethrower, a shotgun, Neon Annihilator 813.
  - On a map with water (ctf_2fort), waits in water beside an enemy route and strikes wet enemies with the Neon
    Annihilator, which crits the wet (S4, and water places in TF2 map knowledge).
  - Leaves the water to fight with fire or the shotgun when it must.
  - Weight low, and only where the map has water.
  - Measure: kills in water, time in water, the route's enemy traffic.
- From research: the Thermal Thruster (S1), the Gas Passer and Dragon's Fury (an item meter), the Manmelter, the
  Detonator, the Reserve Shooter's airblast-then-shoot.

### Demoman
- **Leftovers:**
  - the sticky jump and own-sticky view (S3);
  - the review's small items;
  - the sentry kill, shown live against a fielded Engineer.
- **The knights, played as the guide plays them.** The Demoknight and Hybrid knight are built.
  - **Routes:** they "stay off teammates and hang on flank routes": the corridor bias away from the team's way.
  - **Trimping** (a charge up a slope launching the knight). Read the port's shield charge: if it carries a slope
    launch, this is a movement program; if not, it is a game gap.
  - **Counters:** never charge into a spun Heavy. The guide names Heavies with Pyros and sentries as the knight's
    three counters; the first two are built.
  - **Loadouts:** the Tide Turner 1099 gets a row. The Hybrid knight prefers the Iron Bomber 1151, as the guide
    says.
  - **Measure:** the share of time on flank corridors, kills on Scouts, Spies and Snipers.

### Heavy
- **Leftovers:**
  - the Buffalo Steak's melee lock (§3);
  - a defending role on ctf_2fort (the Heavy holds the intel room and bridge there);
  - fire-to-fire target switches that drop the spin.
- **Fat Scout** (guide: Gimmick; All-Rounder/Flank; tier C). Tomislav 424, a shotgun, GRU 239 or KGB 43.
  - Moves with the GRU out, fights with the shotgun, and takes the minigun only "as a last resort" or to escape.
  - Runs when surrounded. This is round 1's planned Mobile archetype.
  - Measure: the GRU-out share, the shotgun's share of damage, time to objective, which should be nearer a
    Scout's.
- **Boxer** *(summary)* (Gimmick; Defensive/Flank; tier D regular, B Medieval). KGB 43 or Fists of Steel 331, the
  Buffalo Steak.
  - Melee first, ambushing light classes.
  - Needs the Steak's lock (§3). Weight low.

### Engineer
- **Leftovers:**
  - the Teleporter builder (S8);
  - from round 1's last measure (2026-11-22): the gun stalls at level 2, because no dispenser is built while he
    walks for metal; his sentry killed no one on ctf_2fort; and the enemy still made its first capture on granary
    sooner than against Soldiers (70 s against 83 s), though it took fewer points overall (3 against 6);
  - the composition knows only the symmetric stance, so the cap uses stock's defence roster.
  
  The per-class cap and S6 landed with round 1.
- **Battle Engie** *(summary)* (Competitive; All-Rounder/Support; tier A).
  - More aggressive than the Nest: a teleporter close to the front (S8).
  - Fights beside its sentry with its primary and secondary: Frontier Justice 141 or Widowmaker 527, Pomson 588 or
    Short Circuit 528.
  - Measure: teleporter uses by teammates, the time teammates take to return to the front after a death, its own
    damage.
- **Ninjaneer** *(summary)* (Competitive; Defensive/Flank; tier B).
  - Builds behind the enemy's lines on a flank route: the sentry-spot finder scored for the enemy's side and a
    hidden approach.
  - "High risk, high reward": weight low.
  - Measure: the nest's lifetime, kills from behind.
- From research: the Short Circuit's projectile deletion, the Pomson.

### Medic
- **Leftovers:**
  - S9 (the beam through a one-shot) and S11 (spawn glows);
  - the Vaccinator's `ResistType`;
  - Ready, on a six-run team measure.
- **Battle Medic** *(summary)* (Gimmick; Support/Flank; tier B regular, S Medieval). Crusader's Crossbow 305,
  Blutsauger 36.
  - Heals less and fights more; crossbow heals at range.
  - The guide calls it "riskier and less efficient", so weight low.
  - Measure: damage against healing, the team's deaths.

### Sniper
- **Leftovers:**
  - the current review round;
  - S4's Jarated mark for the Bushwacka;
  - the long-line sight pass, after a build-time and memory measure;
  - the human-aim core, which waits on the user's recorded Sniper sessions.
- **Huntsman** *(summary)* (Gimmick; All-Rounder/Pick; tier B regular, S Medieval). Huntsman 56.
  - First the arrow headshot (§3).
  - Then mid-range bow play: a charged draw, leading the target by the arrow's speed and drop, and moving with the
    team instead of holding a sight line.
  - Prefers Heavies, Soldiers, Demomen and Pyros; avoids Scouts. The guide: good against the first four, weak
    against Scouts.
  - Measure: headshot share, kills by class, time to objective.

### Spy
- **Leftovers:**
  - Spy against Spy;
  - the stuck lines;
  - the core re-review's fixes;
  - Ready;
  - the Saboteur measured against a fielded Engineer.
- **From round 1's last review (2026-11-22), accepted with Ready on:**
  - a re-dress in plain sight keeps the suspicion mark, as stock does;
  - two Spies who borrow one name get a drawn row each.
- **The guide has no playing Spy subclass** (only the Spycrab and Boxspy, out of scope). Round 2's Spy styles come
  from round 1's research: Conniver's Kunai 356, Your Eternal Reward 225 (silent kills, a disguise on the kill),
  the Cloak and Dagger.

## 6. Counter rules across classes

From the guide's counter notes, each a small rule in the answering class's own file, with a fact:
- **A charging knight:**
  - a Pyro airblasts the charge (the user's airblast work);
  - any class steps out of a charging knight's line;
  - an Engineer's sentry is the knight's counter, so the knight avoids it (built).
- **A Bonk Boy or Scout flanker:** Heavies and Engineers are its counters. The Heavy's Anchor already raises Scout
  priority; the Engineer's sentry covers flank routes.
- **A Paratrooper or Trolldier in the air:** hitscan classes (Heavy, Sniper, Scout) raise an airborne Soldier's
  priority.
- **A Pybro by a nest:** an enemy Spy treats a nest with a Pyro near it as guarded.

## 7. Measures and harness

- **Six runs per arm** on cp_granary and ctf_2fort. Archetype A/Bs get a same-run control of the class's stock
  archetype.
- **Captures,** once lane `cap` lands: the enemy's first capture, and the team's captures, beside first-on-objective.
- **The team measure for support and defending classes:** one pinned class among Soldiers against all Soldiers.
  Report points taken and lost, the team's deaths, and the class's own job (heals, sentry uptime, sappers, picks).
- **Every arm on one build,** deployed once, with no edit during a batch (round 1's notice 1).
- **Each new playstyle's own counter,** named above, so a table shows it playing differently.

## 8. Order, lanes and what is open

1. Land round 1: batch 6 (Scout, Demoman, Heavy), then the rest as their fixes and reviews close.
2. Phase 0: two or three shared lanes, by file:
   - S1, S2 and S13 together (the jump and route files);
   - S4 and S9 (the view and the applier; S4 is a recording format bump);
   - S5, S7, S8 and S12 (buildings, map knowledge, the tactical layer).
   
   Then the shared game gaps (`CanHolster`, the arrow headshot).
3. Phase 1: one lane per class from §5, each reviewed by Fable at hand-back, as in round 1.

**Started 2026-11-22** (the user: "Can start round 2 hooks"), beside round 1's last landings. Four lanes cut from
landing batch 6's tip:
- `r2j`: S2, S13, S3's sticky kind;
- `r2h`: S9, S10, S11;
- `r2b`: S5, S7, S8;
- `r2g`: the game gaps of §3.

Held until the lane editing their files lands:
- S4 (Spy and Pyro);
- S1 (Soldier);
- S6 (the Engineer lane builds it);
- S12 (the Pyro lane builds it).

**Wave 2, 2026-11-24** (the user: "yes start"): S1, S4 and S14 with the Engineer, Medic and Sniper lanes, landed
as batch 11 (`597fd806a`, 120 commits). Open after it:
- **Engineer** (Ready): the dispenser stand's stall under a ledge (no patience starts outside 96 u); the granary
  teleporter entrance off the Soldiers' walks (they pass 369-377 u from it); a stand's hold surviving decides that
  are not the stand's; the pad clock running through a repair; no list of given-up pad spots; a parented or
  team-filtered `trigger_hurt` read as everyone's fixed pit; the sentry covering flank routes.
- **Medic** (not Ready: granary's blue captures fell outside the control's spread): the Pocket; the class keep.
- **Sniper** (not Ready: kills, damage and first-on-objective well under the Soldier control): the Bushwacka (S4 is
  in); draws far outnumber arrows; the human-aim core waits on the user's recorded sessions.
- **S1:** the Market Gardener's air swing shown only in facts (0 live swings); the parachute's live repeat; the
  Thermal Thruster.
- **S14:** the Survive dither on red's 2fort stairs (a heal-place latch); bridge-end and spawn-door hop hot spots.
- **Grenades:** the Scout bot never throws the Flying Guillotine, so no live cleaver hit is shown; the generic
  `ITFProjectileSpawner.Spawn` pipe is unfaced and unspun; the stock-ab render tracer no longer compiles.

## 9. Decisions

- **2026-11-22, user:** "can you review this doc and plan a second round for each class
  https://steamcommunity.com/sharedfiles/filedetails/?id=1391651763".
- **2026-11-22, user** (round 1, the Pyro lane): "pryos should also use their air blast to push people of objectes,
  push ubered medics away from the person they are healing and also push people off the cliffs to their death when
  posible. Pryos should also refelect projectiles and try and guess when they are in bound".
- **2026-11-22, user:** "Can start round 2 hooks".
- **2026-11-22, user:** "I would skip friendly ai". No friendly or joke bots, not even as an opt-in mode: the
  Hoovy, Spycrab, Boxspy, Civilian and Scunt stay out.
- **2026-11-22, coordinator:**
  - the guide's trolling and non-playing subclasses are out of scope;
  - the Trolldier is pin-only at weight 0;
  - the guide's tiers and counters are advice, and stock's and the wiki's numbers win.
- **2026-11-24, user:** "yes start" (round 2 wave 2: S1, S4, S14 and the Engineer, Medic and Sniper lanes).
- **2026-11-24, coordinator:**
  - the Ninjaneer is pin-only at weight 0, the Battle Engie 0.05;
  - grenades that lost their contact with players in the move to physics bodies get stock's ray and contact rules
    now, not a todo row (a regression from the same lane and a divergence from stock).
