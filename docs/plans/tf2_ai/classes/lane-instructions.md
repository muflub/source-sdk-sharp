## Instructions to a class lane (P7, after the foundation at tf2ai-P7)

Your class is <C> (Scout, Soldier, Pyro, Demoman, Heavy, Engineer, Medic, Sniper, Spy).

### Files you own (edit only these, plus new files of your own)
- `src/ai/tf2/server/Classes/TF<C>.cs`: `TF<C>Class` (Ready, Abilities(), Archetypes(), Spec()) and the nested
  `TFDecider.<C>Play : ClassPlay`. Heavy's play is `HeavyPlay`, Demoman's `DemomanPlay`.
- New files of yours: `src/ai/tf2/server/Classes/TF<C>*.cs` (more nested partials of `TFDecider` holding private
  nested types, an `ITFHudMeters` reader, routes), `src/ai/tests/TF2/TF<C>*Tests.cs`.
- Never edit `TFDecider.cs`, `TFDecider.Classes.cs`, `TFDecider.Hands.cs`, `TFAbilities.cs`, `TFClassSpecs.cs`,
  `TFArchetypes.cs`, another class's file, `TFDecider.Rally.cs`/`Points.cs` (lane Groutes). If you need a hook that
  is not there, say so in your hand-back; do not add it in a shared file.
- Name everything in your play's own scope: fields of your nested `<C>Play` class cannot clash with another lane's.
  A new nested type goes inside `TFDecider` in your own file as `private sealed class <C>Something`.

### The play: what to override (`TFDecider.ClassPlay`, Trees/TFDecider.Classes.cs)
- `Hands()` weapon choice (default: kit primary in a lull, secondary when the primary is dry and the target near,
  lull reload); `SwitchSeconds` its spacing (1 s).
- `Engage()` aim and trigger (default: lead, lob for falling projectiles, feet for flat splash, melee at 72 u).
  Call `FireAtFriend(mate, at)` to hold the trigger at a teammate (a beam, a puff): the friend gate lets it go.
- `FightInSight()` footwork with a target in sight (default range band; melee closes).
- `OneShots()` runs after `Engage`, so it may replace `_action` (D's action) with an ability one-shot or a fire: a
  reflect wins over the general fire this way.
- `Weigh(branch)` a factor on a root branch (`TFDecider.Fetch`, `Defend`, `Fight`, ..., `ClassBranch`).
- `Role(dealt)` the team role it plays; `PlaceUtility()`/`GoPlace()` its own root branch "Class" (a sentry spot,
  a sight line); `AvoidsSentries`; `TakesReflex(verdict)`; `TargetBonus(target, sighted, dist)`; `HurtShare`.
- `Died()`; `Count(into)` your harness counters; `Archetype` is the archetype it plays (set for you).
- Through `D` you reach the decider's privates: `D._self`, `D._view`, `D._target`, `D._targetDist`, `D.GoTo`,
  `D.Select`, `D.Kit` (the HUD: `TryWielded(slot)`, `Meter(name)`, `Mates`), `D.HeldFacts`, `D._map.Abilities`.

### An ability row
Append to `TF<C>Class.Abilities()` (at most 16, end only): your rows sit at `TFClassSpecs.BlockOf(TFClass.<C>)`
(16 x class number) and no other row moves. Find yours by name with `D.AbilityRow("name")` once (in your play's
constructor is too early: the map is the decider's, so look it up lazily) and send
`new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(row, arg), ++seq)`. The shared rows (rocket_jump,
spin_up 60 ticks, uber, build_*, destroy_*, voice_*) stay in block 0; a longer spin is a row of your own.

### An archetype
`TF<C>Class.Archetypes()` returns `new TFArchetypeTable(TFClass.<C>, [new TFArchetype("Name", weight,
[new(TFLoadoutSlot.Primary, [defindex, ...]), ...])])`. A preferred item with no attributes stands for its reskins;
a slot you do not name is the seeded draw's; flagged families (lunchbox, drink, shield, banner, builder/PDA, worn
items, mark-for-death) are only ever yours by an archetype's choice. The Soldier's `Pocket` is the worked example.
Pin one for a run: `ai_bot_add Name hard team=2 class=<c> archetype=<Name>`.

### A counter
Override `Count(List<KeyValuePair<string,long>> into)` and add `("<c>.what", n)`; `/ai/classes` and the harness
table sum it over the pinned bots with no other change.

### A meter (own HUD reads)
A `[SideService(Side.Server, As = [typeof(ITFHudMeters)])]` class in your own file adds `TFMeter("<c>.name", v)` for
the bot's own HUD; read it in the play with `D.Kit?.Meter("<c>.name")`. The Medic's charge/patient and the healed-by
pair are `TFMedigunMeters` (shared).

### The ready flag
Flip `TF<C>Class.Ready = true` when your facts pass and the harness shows the class plays. Then also add your class's
row to `src/ai/tf2/client/TFBotClasses.cs` Playable (`TFBotSetupTests.ThePageShowsTheClassesTheServersBotsField` goes
red until you do; it is the one fact holding "the page's classes are the ready set"): a one-line append the landing
merges. Before Ready, runs field it by pin or by `ai_bot_classes_ready <c>`. Clearing `ai_bot_classes_ready`
mid-run flips every bot of that class to a ready class at its next death, one bot per class-change window (the stay bonus does not hold an unready class).

### Shared files you do not edit
- `src/ai/tests/TF2/TFField.cs`: it already carries `Hud`, `weapons:` and `plays:` for you; build your own helpers in
  your own test file.
- `src/ai/tests/TF2/TFPerceptionCensusTests.cs`: its lists of the readers of `ITFActorFacts` and of another player's
  health go red if a reader of yours (an `ITFHudMeters` reading actor facts, a target ID read) joins them. That is by
  design: do not edit the lists; name the reader and why it is honest in your hand-back, and the landing adds it.
- `docs/valve-header-new.tsv`: a shared append. A new file of yours that ports stock logic (`tf_bot_*.cpp`) starts
  with `tools/valve-header.txt` and gets one row here, appended at the end, your rows only; never reorder or edit others'.

### Recordings
A TF2 recording (format 10) carries each view's kit, meters, mates and every sighted actor's facing, so a
`LiveRecording`-style proof replays your class's play identically whatever its kit.

### The harness
`AI_SCENARIO_API_DIR=... AI_SCENARIO_TMPDIR=... AI_SCENARIO_PORT=... tools/ai-tf-class --class <c> [--archetype N]
--map cp_granary|ctf_2fort --bots 8 --pinned 4 --seconds 300` (control: 4 Soldiers on the same names and seeds;
`--control <c> --control-archetype <A>` for archetype A/B). Output: `bin/<config>/ai-class/<utc>-<tag>/`.
