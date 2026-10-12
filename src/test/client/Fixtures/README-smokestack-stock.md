# `smokestack-stock.tsv`: provenance

Stock `C_SmokeStack` (c_smokestack.cpp, carried in our
`src/client/native/shell_smokestack_sim.cpp` until lane c2-sims), the
non-episodic arms this build compiled, recorded live before the simulation
moved to C#. `SmokeStackSimOracleTests` replays it against
`SourceSharp.Client.Effects.SmokeStackSim`.

## How it was recorded

1. Base `9ae33bfb` (branch client-c2-sims), with
   `tools/probes/c2sims-smokestack-probe.diff` applied to
   `shell_smokestack_sim.cpp` and the client built with `-DSS_CLIENT_PROBES=ON`
   (the recorder, `tools/probes/client/c2sims_probes_debug.cpp`). The diff was
   reverted by `cp` afterwards.
2. `tools/probes/c0-session OUT tools/probes/sessions/c2sims-smokestack.steps
   --arg=+map --arg=dm_lockdown --env SOURCESHARP_C2SIMS_FIXTURE_DIR=DIR`: the
   steam jet's fixed pose (`setpos_exact -4066.64 5622 1.59`, `setang_exact 0 0
   0`), the recording starts, then `ent_create env_smokestack ...
   SmokeMaterial particle/SmokeStack.vmt BaseSpread 20 SpreadSpeed 15 Speed 30
   StartSize 10 EndSize 15 Rate 40 JetLength 180 Twist 20 Roll 10 WindAngle 30
   WindSpeed 20 rendercolor "200 180 160" renderamt 200` (twist, roll and wind
   non-zero so every arm runs): `created env_smokestack #407 at (-2752.0,
   5622.0, 93.6)`. 150 frames from its `Start`.
3. The random stream is the recorder's fixed one (`CUniformRandomStream`
   seeded `0x5eed`, installed as `random` and as vstdlib's stream for the free
   `RandomInt` that `rand()`/`FRand` draw from). Tolerance zero (bit-exact).

## Rows

| tag | columns | what |
|---|---|---|
| `F` | dt, wasDrawnPrevFrame, alwaysSimulate, curtime, frametime, alwaysSimulateUntil, origin xyz, angles xyz | `Update`'s inputs, one per frame |
| `P` | spread, speed, start, end, rate, length, rollspeed, basespread, twist, emit, wind xyz, r, g, b, a, m_InvLifetime, m_iMaxFrames | the members on entry |
| `RI` / `RF` | min, max, value | a draw during `Update` (`RandomInt(0, m_iMaxFrames)`, the `FRand`s' `rand()`, the roll's `RandomFloat`) |
| `T` | on, m00, m01, m10, m11 | the twist matrix `Update` left |
| `S` | dt, m_bInView, drawn | `SimulateParticles` ran past its early return |
| `Q` | 0, pos xyz, vel xyz, accel xyz, lifetime, angle, rolldelta | each surviving puff after its step |
| `D` | pos xyz, alpha x alphascale, size, angle (radians) | each puff's numbers in `RenderParticles` (the sphere renderer lights it) |
