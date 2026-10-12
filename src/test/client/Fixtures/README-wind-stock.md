# `wind-stock.tsv`: provenance

Stock `CEnvWindShared`'s simulation (env_wind_shared.cpp), carried in our
`src/client/native/shell_effects_sim.cpp` until lane c2-sims, recorded live
before it moved to C#. `WindSimOracleTests` replays it against
`SourceSharp.Client.Effects.WindSim`. Tolerance zero (bit-exact). The wind
owns its two random streams and seeds them itself (stock's server passes 0),
so the replay runs the port's own streams (`UniformStream`) rather than
replaying draws: the fixture holds the streams to vstdlib's generator too.

## How it was recorded

1. Base `921db239` (branch client-c2-sims), with `tools/probes/c2sims-wind-probe.diff`
   applied to `shell_effects_sim.cpp` and the client built with
   `-DSS_CLIENT_PROBES=ON`; reverted by `cp` afterwards. The probe logs the
   start (`S`), each `UpdateTreeSway`'s shader vector (`V`) and each
   `WindThink`'s state at its one return (`W`).
2. `tools/probes/c0-session OUT tools/probes/sessions/c2sims-wind.steps
   --port 27471 --arg=+map --arg=dm_lockdown --env SOURCESHARP_C2SIMS_FIXTURE_DIR=DIR`:
   `ent_create env_wind ... minwind 20 maxwind 60 mingust 80 maxgust 150
   mingustdelay 0.4 maxgustdelay 0.9 gustduration 0.5 gustdirchange 30`, then
   400 thinks.

## Rows

| tag | columns |
|---|---|
| `S` | minwind, maxwind, mingust, maxgust, mingustdelay, maxgustdelay, gustdirchange, gustduration, seed, initial yaw, initial speed, start time |
| `V` | time, sway clock after the step, the shader's wind direction xyz (`Lerp( pct, prev, cur ) / 2`) |
| `W` | time, wind vector xyz, speed, average speed, direction, gusting, switch time, sim time, angle variation, speed variation, variation clock |
