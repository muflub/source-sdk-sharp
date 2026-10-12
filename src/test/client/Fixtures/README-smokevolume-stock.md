# `smokevolume-stock.tsv`: provenance

Stock `C_FuncSmokeVolume` (c_func_smokevolume.cpp, carried in our
`src/client/native/shell_smokevolume_sim.cpp` until lane c2-sims), recorded
live before the simulation moved to C#. `SmokeVolumeSimOracleTests` replays it
against `SourceSharp.Client.Effects.SmokeVolumeSim`.

## How it was recorded

1. Base `02efa8a2` (branch client-c2-sims), with
   `tools/probes/c2sims-smokevolume-probe.diff` applied to
   `shell_smokevolume_sim.cpp` and the client built with
   `-DSS_CLIENT_PROBES=ON` (the recorder,
   `tools/probes/client/c2sims_probes_debug.cpp`). The diff was reverted by
   `cp` afterwards.
2. `tools/probes/c0-session OUT tools/probes/sessions/c2sims-smokevolume.steps
   --arg=+map --arg=dm_lockdown --env SOURCESHARP_C2SIMS_FIXTURE_DIR=DIR`: the
   player at `setpos_exact -4066.64 5622 1.59` looking 25 degrees down, the
   recording starts, `ent_create func_smokevolume ... model *4 Color1 "120 120
   140" Color2 "220 210 200" Material particle/particle_smokegrenade
   ParticleDrawWidth 64 ParticleSpacingDistance 48 DensityRampSpeed 0.5
   RotationSpeed 20 MovementSpeed 15 Density 0.7` at the aim point
   (-3926.0, 5622.0, 28.0), then 60 frames.
3. **The one input the probe changes.** Stock fills the grid from the brush's
   model-space bounds (`WorldAlignMins`), which only lands in the map for a
   brush compiled without an origin, and every brush model in dm_lockdown has
   one (`*4`'s bounds are -225..225 x -25..25 x -125..125 about its origin).
   So, for the recording only, the factory moves the bounds by the entity's
   origin and `VolumeWorldSpaceAABB` does not move them a second time. The
   algorithm is untouched; the replay feeds the same world-space box and a zero
   origin, which gives the same bounds. The port keeps stock's model-space rule.
4. The random stream is the recorder's fixed one (seed `0x5eed`), installed as
   `random` and as vstdlib's stream (client.so's `rand()`, so `FRand`).
   Tolerance zero (bit-exact). The light here is dim, so `LinearToTexture`'s
   gamma taken as `2.2f` and as `2.2` give the same bytes; the port uses stock's
   `2.2f`.

## Rows

| tag | columns | what |
|---|---|---|
| `V` | created, mins xyz, maxs xyz, origin xyz, drawwidth, spacing, ramp, rotspeed, movespeed, colour1 rgb, colour2 rgb, spawnflags, reduceparticles, material | `ApplyUpdate`'s inputs |
| `N` | x, y, z counts, spacing radius, particle radius, density | the grid `FillVolume` sees |
| `C` | x, y, z, contents, cell centre xyz | each cell's `GetPointContents` |
| `RI` before the first `F` | min, max, value | a fill draw (colour interpolant, spin factor, start spin) |
| `L` | rgb | the cell's `GetLightForPoint(pos, true)` |
| `T` | plane, fraction, normal xyz, plane dist | each of six fade traces |
| `K` | colour rgb, fade, colour interpolant, spin factor, start spin | the filled cell and its puff |
| `F` | dt, origin xyz, angles xyz, density on entry, target density | `Update`'s inputs, one per frame |
| `B` | min xyz, max xyz | `SetBBox` |
| `RI` after an `F` | min, max, value | a trade draw (three `rand()` per idle cell, a duration per new trade) |
| `G` | cell, trade index, clock, duration, puff pos xyz, fade, rgb, spin factor, spin, colour interpolant | every filled cell after `Update` |
| `S` | dt | `SimulateParticles` ran |
| `D` | pos xyz, colour rgb, alpha, size, spin, drawn | each puff's numbers in `RenderParticles` (before the distance fade) |
