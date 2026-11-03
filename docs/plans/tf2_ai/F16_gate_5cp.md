# F16: a live gate step for 5CP (`cp5` on cp_badlands)

Base: the main tip after F15 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F16`, branch `tf2ai-F16`. Size M, two
commits. Fit: Qwen. Depends on F15 only for the file (`tools/tf2-gate` is
one lane at a time); read F15's `step_koth` as the shape.

## Goal

`tools/tf2-gate --steps join,teams,cp5` passes on `cp_badlands` (in
`game/mod_tf2/maplist.txt:3`'s list; the bsp is at
`bin/debug/content/tf/maps/cp_badlands.bsp`): the 5CP mode runs live
(today unit tests only: `src/tf2/tests/Entities/ControlPointTests.cs`,
`AreaCaptureTests.cs`, `src/tf2/tests/Modes/TFModeTests.cs`).

## What the step asserts (each a numbered read through the dev API)

1. After `changelevel cp_badlands` and both clients on opposite teams, the
   mode id is `cp` (`src/tf2/server/Modes/CPMode.cs:30`).
2. The middle point (index 2 of 5) is the only one unlocked for both at
   round start; the second points are locked for the enemy (read the
   objective resource's `Locked`/`CapLayoutInHud` state or the entity
   fields: name which).
3. The red client on the middle point captures it (owner 2) within the
   capture time (`TriggerAreaCapture`'s vars give it; one Scout counts
   double: use a Soldier or Heavy to keep the arithmetic simple), and
   red's next point unlocks.
4. Blue standing on a point red is capturing blocks it (`IsCapturing`
   false while both stand; `PointsMayAlwaysBeBlocked` is Payload's flag,
   not CP's: read `CPMode`).
5. Red capturing the last blue point ends the round for red (`TeamWin` in
   the round state; `tf2_dev_win` exists as a shortcut in `step_ctf`, but
   this step must earn the win by captures with the clients teleported
   point to point, so the capture rules are what is proven).

Write the row with the capture seconds observed per point.

## Red first

Run the step before step 3's teleport exists: it must FAIL on "owner stayed
0"; paste that FAIL row and the PASS row after.

## Done condition

`tools/tf2-gate --steps join,teams,cp5` → every row PASS, pasted with the
report path; `tools/tf2-gate --self-test` passes; `make test` all legs
`Failed: 0`. Do not add `cp5` to the default list unless the default run
stays within its budget (say which you did).
