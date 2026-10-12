# `flight-trajectory-stock.tsv` — provenance

Thirty frames of `CStandaloneEntity::UpdateFlight`, **recorded from the native
integrator before step 2f changed anything**, one row per flying entity per
frame. `EntityMotionTests` replays them through `MoveMath.AdvanceFlight` and
`MoveMath.ResolveFlight` and requires bit-exact agreement, so the comparison is
managed arithmetic against the C++ arithmetic it replaces and not against
itself.

## How it was recorded

1. `1a14675f`, in lane 2f's worktree, with nothing else
   changed. A temporary probe (`SS2F_FlightProbe`) was added to `UpdateFlight`
   and printed one `%.9g` TSV row per call — `%.9g` because that is what
   round-trips an IEEE-754 single.
2. `src/buildsourcesharp debug`.
3. `tools/2f-record-flight <out.tsv>` — launches `dm_lockdown` headless with
   `-condebug`, waits for the player, `sv_cheats 1`, teleports to the
   scenario file's `openFloor`, `impulse 101`, and fires three weapons:

   | weapon | projectile | what it covers |
   |---|---|---|
   | `weapon_crossbow` | `ss_bolt` | gravity scale 0.15 — the falling arm, and a terminal impact on a wall |
   | `weapon_rpg` | `ss_rocket` | gravity scale 0 — the flat arm, and a terminal impact |
   | `weapon_ar2` (alt) | `ss_energyball` | bounces 8 — the reflect-and-lift arm |

   The AR2 is aimed **down at the floor** and the player is moved off the spawn
   first: `ss_energyball` ends its flight on touching anything that is not its
   shooter, and `dm_lockdown` wraps its spawn points in `trigger_multiple`, so
   a ball fired from a spawn is over after one frame and never reaches a
   surface to come off.
4. The probe was removed and the file restored by copy (`cp` + `touch`, never
   `git checkout --`).

Rows: 10 `ss_bolt`, 16 `ss_rocket`, 4 `ss_energyball`. All three branches of
`UpdateFlight` appear:

| branch | rows |
|---|---|
| clear step (`fraction >= 1`) | 27 |
| blocked, no bounces left (flight ends) | 2 — one `ss_bolt`, one `ss_rocket` |
| blocked with bounces (reflect, lift, count down) | 1 — `ss_energyball`, 8 → 7 |

## The columns

Everything up to and including `hit` is an INPUT to the frame — the state
`UpdateFlight` was entered with, plus the whole trace answer it was handed.
Everything from `rx` is what it produced.

| column | what |
|---|---|
| `class` | the entity's classname |
| `dt` | `gpGlobals->frametime` |
| `gravity` | `GetCurrentGravity()` |
| `gravityScale` | the entity's `m_flGravityScale` |
| `ox oy oz` | origin on entry |
| `vx vy vz` | velocity on entry, **before** gravity was added |
| `bounces` | `m_nBouncesLeft` on entry |
| `nx ny nz` | the destination the step computed |
| `fraction` | `trace_t::fraction` |
| `ex ey ez` | `trace_t::endpos` |
| `pnx pny pnz` | `trace_t::plane.normal` |
| `hit` | 1 if `trace_t::m_pEnt` named anything |
| `rx ry rz` | origin on exit |
| `wx wy wz` | velocity on exit |
| `flying` | `m_bFlying` on exit |
| `bouncesOut` | `m_nBouncesLeft` on exit |

## Re-recording it

`tools/2f-record-flight` is kept so the recording is repeatable, but it needs
the probe put back — it is a recorder run by hand against an instrumented
build, and nothing in `make check` runs it. Re-record only against a build of
the NATIVE integrator; re-recording against the managed one would turn this
file into a replay of the code it is supposed to be checking.
