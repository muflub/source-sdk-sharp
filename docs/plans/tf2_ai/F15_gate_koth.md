# F15: a live gate step for KOTH (`koth` on koth_viaduct)

Base: `dae055998`. Worktree `../ss-tf2ai-F15`, branch
`tf2ai-F15`. Size M, two commits. Fit: Qwen. Depends on nothing; serialise
with F16 on `tools/tf2-gate` (F15 first).

## Goal

`tools/tf2-gate --steps join,teams,koth` passes on `koth_viaduct`, proving
the KOTH mode live (today it has unit tests only: `src/tf2/tests/Entities/
KothTests.cs`, `src/tf2/tests/Modes/TFModeTests.cs:82-94`).

## What exists

- `tools/tf2-gate`: a python driver over `tools/debug-api`; steps are
  functions `def step_<name>(run, state)` (`:558` `step_join`, `:604`
  `step_teams`, `:1941` `step_ctf`, `:3075` `step_mode`); the default list
  and `--steps` at `:3741`; the report `tools/.tf2-gate/<run>/report.tsv`
  with one row per step. Read `step_ctf` whole: how it teleports a client
  (`setpos` through the dev API with cheats), reads an entity field
  (`/entity`), waits, asserts, and writes its row. Read `step_mode` for how
  the active mode id is read.
- The map: `bin/debug/content/tf/maps/koth_viaduct.bsp` (present). The
  gate's map is set per run: read how `step_changelevel` (`:2547`) changes
  the map and make `koth` change to `koth_viaduct` first (a `changelevel`
  is the map load the step needs; the second map load of a gate is
  CLAUDE.md's rule).
- KOTH rules in code: `KothLogic.cs:255-258` (`timer_length` 180,
  `unlock_point` 30 s), `KothMode.cs:31`.

## Build

1. `step_koth` (commit 1): `changelevel koth_viaduct`; both clients re-join
   on opposite teams (reuse the join/teams helpers); assert the mode id is
   `koth` (the `step_mode` read); assert the point is locked for
   `unlock_point` seconds after round start (read the point's `Locked`
   through the objective resource's net state or the entity's field: name
   which in the commit); teleport the red client onto the point after the
   unlock; assert the red capture (owner 2) and that red's timer runs (its
   `team_round_timer` value falling over 5 s, two reads); teleport blue on,
   assert blue captures and red's timer stops. Write the row with the
   numbers (unlock wait observed, seconds read). Add `koth` to the default
   step list after `mode` only if the whole default run still fits the
   gate's time budget (read how the budget is held; else leave it off the
   default and say so).
2. Red first (commit 2 is the hand-back note): run the step once with the
   assertion deliberately inverted in `.scratch/` (a copy of the gate is not
   allowed; instead run against a map that is not KOTH:
   `--steps join,teams,koth` with the changelevel target edited to
   `ctf_2fort` on a scratch branch commit you then drop with a fixup... no:
   simplest honest red: on the first run, before implementing the capture
   teleport, the step must FAIL on "owner stayed 0"; paste that FAIL row,
   then the PASS row after the implementation).

## Done condition

`tools/tf2-gate --steps join,teams,koth` → every row PASS, pasted; the
report path; `make test` all legs `Failed: 0` (the python self-test
`tools/tf2-gate --self-test` must pass if the file has one: run it).
