# `movement-processmovement-stock.tsv` — where it came from

Thirty-four commands of the **native** `CGameMovement::ProcessMovement`,
recorded off a live `dm_lockdown`, before `plan_entity.md` step 3e changed
anything about how movement runs. It is the acceptance for that step:
`MovementTests.TheManagedLoopReproducesEveryRecordedCommand` replays every row
through the managed loop and requires the state it produces to match, **bit for
bit**.

## Why the trace answers are in it

Each command carries the state the loop was handed, the state it produced, and
**every world query it made in the order it made them, with the answer the
engine gave**. Feeding those answers back is what makes the replay a comparison
of managed arithmetic against the C++ it replaces, rather than against itself. A
fixture of positions to reproduce would agree forever and prove nothing.

The replay also checks each query's own ARGUMENTS against the recorded ones
before handing back the answer. That is the other half: a loop that traced from
the wrong place, with the wrong hull, or in the wrong order would still produce
numbers, and only the argument check catches it.

## How to record it again

    cp src/game/shared/gamemovement.cpp /tmp/pristine.cpp
    tools/3e-probe-movement                      # the probe, applied
    src/buildsourcesharp debug
    tools/3e-record-movement /tmp/raw.tsv        # ~4100 commands, ~50MB
    cp /tmp/pristine.cpp src/game/shared/gamemovement.cpp
    touch src/game/shared/gamemovement.cpp
    git diff --numstat src/game/shared/gamemovement.cpp   # MUST be empty
    tools/3e-select-movement /tmp/raw.tsv <this file>

`tools/3e-select-movement` is what makes a recording into a fixture: it keeps a
bounded subset that still reaches every arm the recording reached, each command
whole, and it PRINTS the arm counts so a fixture that lost an arm is visible
rather than merely smaller.

`%.9g` throughout, which round-trips an IEEE-754 single.

## What it covers, counted

From 4,100 recorded commands, 34 kept:

| arm | in the fixture | recorded |
|---|---|---|
| ground | 18 | 3361 |
| ground, no input at all | 9 | 2377 |
| walking | 9 | 984 |
| air, rising | 6 | 152 |
| air, falling | 4 | 424 |
| leaving the ground | 6 | 17 |
| landing | 4 | 19 |
| landing hard enough to punch the view | 4 | 4 |
| landing hard enough to take damage | 3 | 3 |
| jump held | 5 | 186 |
| ducked | 6 | 661 |
| ducked AND moving | 6 | 101 |
| the duck transition | 6 | 27 |
| noclip | 6 | 163 |
| a trace that did not go all the way | 18 | 3515 |
| a step climbed | 7 | 93 |
| surface friction ≠ 1 | 6 | 127 |

## What it does NOT cover, and why

Stated rather than left to be discovered. Each of these is **zero** commands in
the whole 4,100-command recording, and `MovementTests` has a fact that says so —
if a later recording DOES reach one, that fact goes red and this table has to be
rewritten, which is the point.

- **Water, at any level.** `dm_lockdown` has no water in it. The water arms are
  covered by hermetic facts in `MovementTests` instead, and those are not
  parity: nothing native produced their numbers.
- **Ladders.** The map's three ladders are `func_useableladder`, which is
  `CHL2GameMovement`'s mechanism and not this build's. Nothing ever set
  `MOVETYPE_LADDER`, and the `climbable` flag was 0 on all six surfaces the
  recording touched.
- **Duck-jump.** `CheckJumpButton` sets `m_flJumpTime` and `m_bInDuckJump` only
  when `gpGlobals->maxClients == 1`, so the whole duck-jump family —
  `StartUnDuckJump`, `FinishUnDuckJump`, `CanUnDuckJump` — is unreachable in a
  multiplayer build. A recording with `-maxplayers 1` was attempted and the game
  segfaulted before the scenario ran, on a tree with nothing of this step in it.
- **A moving ground.** No recorded command had a non-zero base velocity;
  `dm_lockdown` has nothing a player can ride (the live gate's `lift` step
  records the same thing).
- **`MOVETYPE_OBSERVER` and the two flying types.** The managed loop **refuses**
  those three and the stock body runs them, so there is nothing to compare.

## The one knob that is not stock

Steps 6 and 7 of the scenario set `sv_gravity` to 1600 and 2800 for two falls
each, and that is not cosmetic. The yard has a roof about 87 units above the
floor, so a player teleported 400 units up falls the same short distance however
big the number is: a first recording's maximum fall velocity was 315, under
`PLAYER_FALL_PUNCH_THRESHOLD` (350), and `PlayerRoughLandingEffects` never fired
— its punch constant could be mutated with the fixture still green. The same
87-unit drop at 1600 reaches 527 and at 2800 reaches 698, which is over
`PLAYER_MAX_SAFE_FALL_SPEED`. Every command records the gravity it ran under, so
the replay is fed it.
