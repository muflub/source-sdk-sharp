# P7: the TF2 bots' class plans

P7 of [plan_bots_teams_tf2.md](../../plan_bots_teams_tf2.md) ("the other eight classes"). One plan per class: what
the class does in each mode, its archetypes by weapon set, what the port and the AI could do at the start, and the
rules a code lane builds from. Each plan was written by a research lane at main `225fe2e96` (2026-11-20), then
reviewed by Fable against the code, the stock `tf_bot` code and the TF2 wiki. **Where a review and its plan
differ, the review's fixes win.**

| Class | Plan | Review |
|---|---|---|
| Scout | [scout.md](scout.md) | [review-scout-soldier-pyro.md](review-scout-soldier-pyro.md) |
| Soldier | [soldier.md](soldier.md) | [review-scout-soldier-pyro.md](review-scout-soldier-pyro.md) |
| Pyro | [pyro.md](pyro.md) | [review-scout-soldier-pyro.md](review-scout-soldier-pyro.md) |
| Demoman | [demoman.md](demoman.md) | [review-demoman-heavy-engineer.md](review-demoman-heavy-engineer.md) |
| Heavy | [heavy.md](heavy.md) | [review-demoman-heavy-engineer.md](review-demoman-heavy-engineer.md) |
| Engineer | [engineer.md](engineer.md) | [review-demoman-heavy-engineer.md](review-demoman-heavy-engineer.md) |
| Medic | [medic.md](medic.md) | [review-medic-sniper-spy.md](review-medic-sniper-spy.md) |
| Sniper | [sniper.md](sniper.md) | [review-medic-sniper-spy.md](review-medic-sniper-spy.md) |
| Spy | [spy.md](spy.md) | [review-medic-sniper-spy.md](review-medic-sniper-spy.md) |

The code lanes build on the P7 foundation (branch `tf2ai-P7`): a file per class
(`src/ai/tf2/server/Classes/TF<Class>.cs`), its ability-row block, its archetype table, its HUD meters and its
`Ready` flag. [lane-instructions.md](lane-instructions.md) is the foundation's guide to those files;
[lane-notice-1.md](lane-notice-1.md) corrects it (item 1 replaces its "A meter" section) and adds the rules
learned from the first hand-backs.

Line citations (`path:line`) in the plans and reviews are at `225fe2e96`; later commits move them.

## The request

User, 2026-11-20: "spin up sub agents working on AI for each character make sure to research meta classes and play
styles for tf2 and ensure the AI can use them and play differently with different weapon sets. Use fable to
review".

Standing ruling, 2026-11-15: "A measure of how good they are is how long they take to get to the objective and
progress in the map". Earlier class ruling, 2026-11-02 (C.4): "bots should evaluate changing class on respawn but
have a global cool down for all bots to not thrash".

## Decisions

### User

- **2026-11-21:** "Make sure the plan for each class is committed to main". These plans are committed ahead of the
  user's own review of them, at the user's request.

### Coordinator, from the Fable reviews (2026-11-20 and 2026-11-21)

- **Archetype tables are C#** in each class's own file, not plan C.3.1's `tf2_subclasses.json`.
- **Senses are a player's.**
  - Exact health is readable only for the bot's heal target and the player under its crosshair. A Spy may read the
    health of the enemy under its crosshair; a disguised Spy shows its disguise's health.
  - A teammate calling for a Medic (75 % or less, within 1,000 u) is a flag and a place, not a number.
  - Nothing else reads another player's health; a census fact holds that only `TFHudReader` does.
- **Stock over the plan's numbers:** where the stock `tf_bot` code and a plan differ, stock wins (for example, the
  Engineer's building distances).
- **Game-side gaps:** an item marked `works` that is not is ported if it is small and in the class's own game
  code, with a fact. Otherwise it is marked `partial` in `game/mod_tf2/scripts/items/items_working.tsv`, with the
  evidence, so bots never draw it.
- **Ship orders:**
  - Demoman: Hybrid, Trapper, Demoknight, Loch-n-Load, Combo, Hybrid knight.
  - Pyro: the airblast-and-flare combo second.
  - The Sniper splits rifle aim (it waits on recorded human Sniper sessions) from its positions and Huntsman play.
- **The Medic** answers any teammate's call, not only its pocket's.
- **The Spy's disguise** is an alias owned by the core: one per disguise, shared by every viewer it fools.
- **A class is `Ready`** (the composition may field it) once its stock archetype is at least as fast to the
  objective as the all-Soldier control on cp_granary, and on ctf_2fort where the class has a CTF job. A support
  or defending class uses its plan's own objective measure.
- **Shared code:** a class lane does not edit shared files. It asks, with the exact change, at hand-back. The
  coordinator granted these exceptions:
  - the Scout: the navigation follower's double jump;
  - the Soldier: the jump planner;
  - the Pyro: the projectile owner and the airblast's reflect list;
  - the Spy: the core's alias identity, the kill feed, aim resolution, the sapped flag, and (2026-11-21) the
    suspicion and bump cue in `TFDecider.ChooseTarget` and `TFDecider.Voice.cs`;
  - the Medic: its crosshair target ID;
  - the Heavy (2026-11-21): the applier holding a minigun's spin while a shot waits for the eye.
- **A class file that restates stock `tf_bot` logic** carries the Valve header and a row in
  `docs/valve-header-new.tsv` (2026-11-21).
- **The class harness** (`tools/ai-tf-class`, on the foundation's branch until it lands) counts the first arrival on an objective from the start of the round
  it happened in. A round that restarts straight back to Preround (waiting for players, about 31 s in) is not
  counted (2026-11-21).

## Where it stands, 2026-11-21

The foundation is in its landing gate. The Scout and Demoman lanes are reviewed and fixed, and wait for the next
landing. The Heavy is fixed, and its shared applier change is under review. The Spy's `Ready` flag is off until its
door deadlock and bump cue are fixed. The Soldier, Pyro, Engineer, Medic and Sniper lanes are building. Each
landing records its numbers in plan_bots_teams_tf2.md.
