# `dusttrail-stock.tsv`: provenance

Stock `C_DustTrail` (c_smoke_trail.cpp) and its `CDustFollower` emitter,
carried in our `src/client/native/shell_trail_sim.cpp` until lane c2-sims,
recorded live before the simulation moved to C#. Recorded as
`smoketrail-stock.tsv` was (see README-smoketrail-stock.md for the method,
the fixed seed 0x5eed and the flags byte stock never writes);
`DustTrailSimOracleTests` replays it against
`SourceSharp.Client.Effects.DustTrailSim`. Tolerance zero (bit-exact).

## How it was recorded

1. Base `f9a96bce` (branch client-c2-sims), with the extended
   `tools/probes/c2sims-trail-probe.diff` (all six trails) applied and the
   client built with `-DSS_CLIENT_PROBES=ON`. Reverted by `cp` afterwards.
   `CDustFollower` gained a logging `UpdateColor` override that calls
   `CSimpleEmitter::UpdateColor` (the probe's only window on the render).
2. `tools/probes/c0-session OUT tools/probes/sessions/c2sims-dusttrail.steps
   --port 27471 --arg=+map --arg=dm_lockdown --env SOURCESHARP_C2SIMS_FIXTURE_DIR=DIR`:
   the smoke trail's pose, then `ent_create env_dusttrail ... spawnrate 20
   lifetime 2 color "200 150 100" opacity 0.6 minspeed 4 maxspeed 12
   mindirectedspeed 10 maxdirectedspeed 30 startsize 24 endsize 48
   spawnradius 6 emittime 1.2`, then 120 frames. The emit time is short so
   that the stop-time fade (`alpha *= sqrt(...)`) runs on every puff and the
   stop itself is reached: 24 puffs, then 60-odd frames of drift.

## Rows

As the smoke trail's, except:

| tag | columns |
|---|---|
| `F` | dt, curtime, origin xyz, angles xyz, velocity xyz, frametime |
| `P` | spawnrate, lifetime, minspeed, maxspeed, mindirected, maxdirected, startsize, endsize, spawnradius, opacity, colour rgb, stopemittime, startemittime, emit |
| `D` | the tag is in column 0 as for every row; pos xyz, colour rgb, alpha, scale, roll |
