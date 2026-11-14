# F04: the per-entity collide rule (core seam; not for a lane)

Base: `dae055998`. Size M. Fit: **stronger model**. Depends on nothing; F05
(teammates pass through) and F07 (the visualizer) depend on it.

## Why not a lane

Stock's `CTraceFilterSimple::ShouldHitEntity` asks the touched entity
`ShouldCollide(collisionGroup, contentsMask)` and then the game rules'
`ShouldCollide`. The managed port, `TraceFilterRules.ShouldHitEntity`
(`src/server/interop/TraceFilterRules.cs:156-186`), has the pass checks and
the collision-group table (`GroupsCollide`) and **no per-entity rule**. TF2's
team pass-through lives in `CTFPlayer::ShouldCollide` (`tf_player.cpp:10159-10175`,
read this session): for `COLLISION_GROUP_PLAYER_MOVEMENT` with
`tf_avoidteammates`, and for the rocket groups, a red player is not hit by a
trace whose contents mask lacks `CONTENTS_REDTEAM`, a blue one by one lacking
`CONTENTS_BLUETEAM`; the player's solid mask adds the other team's contents
(`CTFPlayer::PlayerSolidMask`, not read in full here). The client runs the
same in `c_tf_player.cpp:7707`. And the client has no managed trace filter
at all (`grep -rl ShouldHitEntity src/client` is empty): prediction's traces
go through the engine's native filter. So the seam is three things at once:
a chained rule in the server's `TraceFilterRules`, the same rule reachable
by the client's prediction (a design question: a managed filter the client
installs the way the server's `TraceFilterBinding` does, or a contents-mask
arrangement the native filter already honours), and a `seams.tsv` row with
`SeamsLedgerTests` holding both arms. Plus a live prediction check: the
misprediction count must not rise (memory `tf2-carrier-pitfalls`). That is
judgement across the interop, prediction and the ledger: not a lane.

## What it must deliver (so F05 and F07 can be briefed against it)

- `src/shared/interface/EntityCollideRule.cs` (new): `public interface
  IEntityCollideRule { bool ShouldCollide(EntityHandle entity, CollisionGroup
  group, int contentsMask); }` as a chain (`IHooks<IEntityCollideRule>`,
  every rule must agree, like `ISpawnFragRule`), asked by
  `TraceFilterRules.ShouldHitEntity` after the pass checks and before
  `GroupsCollide`, for game entities only.
- The same rule reachable from the client's movement prediction for the
  player-vs-player case, with the fact that predicted and server positions
  agree through a pass (the `TF2ClientPredictionTests` shape).
- `docs/plans/tf2/seams.tsv`: a row for `SourceSharp.Shared.IEntityCollideRule`
  (owner_rule chain, side shared, dispatch `TraceFilterRules.ShouldHitEntity`,
  a no-owner fact `NoCollideRuleKeepsStockCollision` and an owner fact).
- Facts in `src/test/server/`: a registered rule answering false for one
  entity makes `ShouldHitEntity` false for it and nothing else; no rule
  keeps every existing `TraceFilterRulesTests` fact green.
- A live boot of the test mod and of tf2 with no rule registered:
  `MISPREDICTION` count unchanged against the previous tip's boot.

## Status (2026-11-08)

The server half landed as lane F153 (`1e2a3b410`..`83e59a36d`): `IEntityCollideRule`,
asked by `TraceFilterRules.ShouldHitEntity` with the trace's own collision group
(stock's `m_collisionGroup`, util_shared.cpp:305), and the `seams.tsv` row.

The client half has nothing to attach to yet. The client's movement traces go
through the native `CTraceFilterSimple` in `src/shared/native/trace_filter_library.cpp`,
whose `ShouldHitEntity` answers false for every entity, so client prediction
already passes through every player and entity (docs/todo.md:420). F05 therefore
registers its rule on the server only. Giving the client a filter that hits
entities is its own stage, and the collide rule joins it then.
