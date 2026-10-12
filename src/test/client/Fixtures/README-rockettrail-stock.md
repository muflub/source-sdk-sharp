# `rockettrail-stock.tsv`: provenance

Stock `C_RocketTrail` (c_smoke_trail.cpp) with its three emitters, carried in
our `src/client/native/shell_trail_sim.cpp` until lane c2-sims, recorded live
before the simulation moved to C#, by the method of
README-smoketrail-stock.md (fixed seed 0x5eed, tolerance zero) at base
`87390d60` with the extended `tools/probes/c2sims-trail-probe.diff`.
`RocketTrailSimOracleTests` replays it against
`SourceSharp.Client.Effects.RocketTrailSim`.

## What the probe changed besides logging

- env_rockettrail has no map keys, so the trail would sit still and fill no
  gap. The probe moves the C++ trail each frame before `Update`:
  origin + (40 * (frame & 1), 15 * ((frame >> 1) & 1), 0). The managed follow
  sweep puts it back every frame, so the offsets do not accumulate; the `F`
  row is the origin `Update` saw.
- `m_bDamaged` is forced on from frame 61, so the ember path runs.
- The muzzle flash and ember emitters are made as logging subclasses of
  `CSimpleEmitter` and `CEmberEffect` (the probe's `CC2SimsProbeEmitter`),
  whose overrides call the stock ones.

Session: `tools/probes/sessions/c2sims-rockettrail.steps` (`ent_create
env_rockettrail` at the smoke trail's pose), `--port 27471 --arg=+map
--arg=dm_lockdown`.

## Two findings the replay encodes

- A new emitter's first frame is not simulated. The flash and embers come
  from an emitter stock makes fresh each frame, so they are drawn as spawned
  and stepped from the next frame (the flash then dies at once: 0.01 s). The
  puff emitter lives from Start, so only frame 1 goes unstepped.
- Stock steps an emitter's particles in the order its last render's depth
  bucket sort left them, and the ember step draws three `rand()`s per
  particle. The port steps in its own order; the replay reorders the port's
  embers to stock's (matched on the unchanging roll speed) before each step.

## Rows

`F`: dt, curtime, origin xyz, angles xyz, velocity xyz, frametime.
`P`: spawnrate, lifetime, minspeed, maxspeed, startsize, endsize,
spawnradius, opacity, startcolor rgb, stopemittime, emit, damaged, flarescale,
last position xyz. Particle rows as the smoke trail's, by emitter: `N`/`Q`/`D`
the puffs (material 0/1), `NF`/`QF`/`DF` the flash (material 1..4,
muzzleflash<i>n</i>), `NE`/`QE`/`DE` the embers (1..4, flamelet<i>n</i>).
