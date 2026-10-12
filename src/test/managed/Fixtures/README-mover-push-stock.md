# The stock push recording — plan_entity.md step 2g

Three TSVs, taken off a live `dm_lockdown` **before** any of step 2g's changes,
from a temporary probe inside `CStandaloneEntity::UpdateMovement` and
`::BlockerAt` (`tools/2g-record-push` drives the game; the probe itself is gone,
and `git diff` on `shell_standalone_base.cpp` carries no trace of it). They are
the acceptance for the managed push integrator: the C# is replayed against them
and must agree bit for bit, `%.9g` round-tripping a single.

| file | one row per |
|---|---|
| `mover-updatemovement-stock.tsv` | frame a mover stepped |
| `mover-blocker-stock.tsv` | candidate `BlockerAt` looked at |
| `mover-stopped-stock.tsv` | frame a mover's velocity fell to zero |

## Why the blocker answer is an INPUT

`blocked` in the move fixture is **the answer the engine's own partition walk
gave that frame**, carried into the row rather than recomputed. Replaying
against it is what makes the comparison managed arithmetic against *native*
arithmetic instead of against itself: a managed integrator that queried the
world differently would still produce numbers, and they would still look
plausible. The rider fixture is the other half — it holds the walk's own
per-candidate verdicts, so the rule that produced `blocked` is checked
separately from the stepping that consumed it.

## What the recording exercises, counted

Counted from the committed files, not asserted from the scenario:

| arm of `UpdateMovement` | rows |
|---|---|
| linear, stepping | 905 |
| linear, blocked (committed nothing) | 308 |
| linear, arrived — the step clamped to what was left | 8 |
| angular, stepping toward a target | 372 |
| angular, arrived | 3 |
| continuous spin (`m_angVelocity`, never arrives) | 201 |
| linear AND angular in one frame — the single write | 107 |
| parented: measured in the parent's frame (`bLocal`) | 285 |
| unparented | 1405 |

| arm of `BlockerAt` | rows |
|---|---|
| candidate is a player and is IN THE WAY | 308 |
| candidate is a player and is a RIDER, so not a blocker | 21 |
| candidate is not a player (a prop, another mover) | 1043 |

**Two arms are not in it, and neither can be.**

* `flDelta <= 0` — a paused or single-stepped server. Nothing in a live run
  reaches it; it is a fact in `EntityMotionPushTests` instead.
* `BlockerAt`'s `ent.pBase` arm. `AnyEntityOf` fills `pBase` from
  `IServerUnknown::GetBaseEntity()`, which answers NULL for everything on this
  build — so the arm compiles, has no subject, and cannot be recorded. Said out
  loud rather than left as a zero that reads like a gap.

**The first recording was wrong in two ways and both were found by counting**,
which is the whole reason the table above exists:

1. `parented` read **0** for 1512 rows. `ent_create ... parentname X` does not
   parent: a map's `parentname` is resolved once, at level activate
   (`MapSpawner`, "parents last"), so a key set on an entity created mid-game is
   never looked at. The recorder uses `cs_sv_parent` now.
2. `linear+angular same frame` read **0**. No entity in `dm_lockdown` asks for
   both at once; `func_platrot`'s `GoUp` is the only input in the build that
   does, so the recorder builds one.

## How to take it again

    src/buildsourcesharp debug          # with the probe re-applied
    DEBUG_API_DIR=/tmp/ss-2g tools/2g-record-push <prefix>

The probe is three `Msg` calls and two captured locals; the recorder's docstring
names the columns it writes.

## What MUTATING the code found that the recording could not

Fifteen mutations were applied to `MoverMath.cs`, `EntityMotion.cs` and to the
fixture itself; each was run, the check watched go red, the file restored by
copy, and the check watched go green again. Three of the first nine survived
the recording alone, and each named a real hole:

| mutation | why 1690 recorded frames could not see it |
|---|---|
| the clamp `flStep >= flRemaining` → `>` | no live frame lands with the step EXACTLY equal to what is left |
| the rotation's `fabsf(remaining) <= step` → `<` | same, for the angle — and on that boundary both branches produce the same NUMBER, so only the arrival differs |
| `AngleDiff`'s asymmetric fix-up → the obvious symmetric one | it is not a hole. `fmodf`'s sign follows the dividend, so `dest > src` gives a remainder in `[0,360)` and `dest <= src` one in `(-360,0]`; neither arm can ever reach the other's clause, and the two spellings agree everywhere |

The first two are constructed facts in `EntityMotionPushTests`. The third is a
sweep of 37,249 angle pairs asserting the agreement, so the next reader who
notices the asymmetry finds out it is inert instead of "simplifying" it and
wondering why nothing went red.
