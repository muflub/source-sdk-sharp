# `smoketrail-stock.tsv`: provenance

Stock `C_SmokeTrail` (c_smoke_trail.cpp) and its `CSmokeParticle` emitter,
carried in our `src/client/native/shell_trail_sim.cpp` until lane c2-sims,
recorded live before the simulation moved to C#. The emitter's simulate and
render loops are the fork's `CSimpleEmitter` (particles_simple.cpp, which the
lane may not patch); they are observed through `CSmokeParticle`'s own
overrides, which they call once per particle. `SmokeTrailSimOracleTests`
replays it against `SourceSharp.Client.Effects.SmokeTrailSim`.

## How it was recorded

1. Base `760b53fc` (branch client-c2-sims), with
   `tools/probes/c2sims-trail-probe.diff` applied to `shell_trail_sim.cpp` and
   `shell_trail_sim.h` (the file header line names it after the session,
   `c2sims-smoketrail-probe.diff`; the diff is the trail one) and the client
   built with `-DSS_CLIENT_PROBES=ON`. Reverted by `cp` afterwards.
2. `tools/probes/c0-session OUT tools/probes/sessions/c2sims-smoketrail.steps
   --arg=+map --arg=dm_lockdown --env SOURCESHARP_C2SIMS_FIXTURE_DIR=DIR`: the
   player at `setpos_exact -4066.64 5622 1.59` looking 25 degrees down, the
   recording starts, `ent_create env_smoketrail ... spawnrate 20 lifetime 2
   startcolor "200 150 100" opacity 0.6 minspeed 4 maxspeed 12
   mindirectedspeed 10 maxdirectedspeed 30 startsize 24 endsize 48
   spawnradius 6` at (-3926.0, 5622.0, 28.0), then 120 frames.
3. The random stream is the recorder's fixed one (seed `0x5eed`). Tolerance
   zero (bit-exact).

## The one byte stock never wrote

`CSimpleEmitter::AddParticle` hands back pool memory without running
`SimpleParticle`'s constructor, so a spawned puff's `m_iFlags` is whatever that
memory held. Bit 0 is `SIMPLE_PARTICLE_FLAG_WINDBLOWN`, which makes
`CSimpleEmitter::UpdateVelocity` pull the puff's x and y velocity towards the
wind (zero on dm_lockdown, which has no env_wind) at 50 units/s/s: in this
recording 40 of the 129 puffs had it. The replay injects the recorded byte; the
port spawns 0, the constructor's value, and so does not reproduce the accident.

## Rows

| tag | columns | what |
|---|---|---|
| `F` | dt, curtime, origin xyz, angles xyz, velocity xyz | `Update`'s inputs, one per frame |
| `P` | spawnrate, lifetime, minspeed, maxspeed, mindirected, maxdirected, startsize, endsize, spawnradius, opacity, startcolor rgb, stopemittime, emit | the members on entry |
| `RI` / `RF` | min, max, value | a draw during `Update` |
| `N` | material, pos xyz, vel xyz, roll, rolldelta, lifetime, dietime, colour rgb, startalpha, endalpha, startsize, endsize, flags | each spawned puff |
| `Q` | -1, then as `N` | each puff after `UpdateRoll`, inside the emitter's simulate (a puff with lifetime >= dietime is removed right after) |
| `D` | pos xyz, colour rgb, alpha, scale, roll | each puff's numbers in the emitter's render (before the near-clip fade) |
