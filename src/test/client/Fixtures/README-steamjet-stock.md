# `steamjet-stock.tsv`: provenance

Stock `C_SteamJet` (c_steamjet.cpp, carried verbatim in our
`src/client/native/shell_steamjet_sim.cpp` until lane c2-sims), recorded live
before the simulation moved to C#. `SteamJetSimOracleTests` replays it against
`SourceSharp.Client.Effects.SteamJetSim`.

## How it was recorded

1. Base `65257834` (branch client-c2-sims), with
   `tools/probes/c2sims-steamjet-probe.diff` applied to
   `shell_steamjet_sim.cpp` and the client built with `-DSS_CLIENT_PROBES=ON`
   (which compiles `tools/probes/client/c2sims_probes_debug.cpp`, the
   recorder). The diff was reverted by `cp` afterwards.
2. `tools/probes/c0-session OUT tools/probes/sessions/c2sims-steamjet.steps
   --arg=+map --arg=dm_lockdown --env SOURCESHARP_C2SIMS_FIXTURE_DIR=DIR`:
   the player is put at a fixed pose (`setpos_exact -4066.64 5622 1.59`,
   `setang_exact 0 0 0`), the recording starts, and
   `ent_create env_steam ... Speed 120 SpreadSpeed 15 StartSize 10 EndSize 25
   Rate 26 JetLength 80 RollSpeed 8` makes a jet at the aim point
   (`created env_steam #407 at (-2752.0, 5622.0, 93.6)`), facing yaw 180.
   120 frames from its `Start`.
3. The random stream is FIXED: while recording, the recorder is both the
   client's `random` and vstdlib's installed stream (which client.so's `rand()`,
   and so `FRand`, draws from), and it draws from its own
   `CUniformRandomStream` seeded `0x5eed`. Every draw the jet's `Update` takes is
   a row, so the replay is handed the same numbers in the same order; the
   tolerance is zero (bit-exact).

## Rows

| tag | columns | what |
|---|---|---|
| `L` | at xyz, light rgb | `WorldGetLightForPoint` inside `UpdateLightingRamp` (the five before the first `F` are `Start`'s) |
| `F` | dt, wasDrawnPrevFrame, 0, origin xyz, angles xyz | `Update`'s inputs; one per frame |
| `P` | spread, speed, start, end, rate, length, emit, faceleft, type, spawnflags, rollspeed, r, g, b, a, m_Lifetime | the members on entry to `Update` |
| `B` | min xyz, max xyz | `SetBBox`, when `Update` had puffs to emit |
| `RI` / `RF` | min, max, value | a `RandomInt` (via `rand()`/`FRand`) / `RandomFloat` draw during `Update` |
| `S` | dt | `SimulateParticles` ran past its early return |
| `Q` | removed, pos xyz, vel xyz, roll, rolldelta, lifetime, dietime, startsize, endsize | each puff after its step, in the particle manager's order |
| `D` | pos xyz, colour rgb, alpha, size, roll | each puff's quad numbers in `RenderParticles` |

Counts: 120 `F`, 118 `S`, 1727 `Q`, 1692 `D`, 104 `RI`, 104 `RF`, 5 `L`.
