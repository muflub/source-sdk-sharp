# `sporeexplosion-stock.tsv`: provenance

Stock `C_SporeExplosion` (c_smoke_trail.cpp) and its `SporeEffect` emitter,
carried in our `src/client/native/shell_trail_sim.cpp` until lane c2-sims,
recorded live before the simulation moved to C#, by the method of
README-smoketrail-stock.md (fixed seed 0x5eed, tolerance zero) at base
`87390d60` with the extended `tools/probes/c2sims-trail-probe.diff`, plus
`m_bEmit = true` forced in the probe's `Update` wrapper (as a map's Enable
input would raise it; stock's Spawn clears it). `SporeEffect` gained a logging
`UpdateRoll` override that calls `CSimpleEmitter::UpdateRoll`, and its
`UpdateVelocity` arms the draw recorder, because its wander draws three
`rand()`s per spore per step. `SporeExplosionSimOracleTests` replays it
against `SourceSharp.Client.Effects.SporeExplosionSim`, lining the port's
spores up in stock's step order first (stock steps in the order its last
render's depth sort left; see README-rockettrail-stock.md). Session:
`tools/probes/sessions/c2sims-sporeexplosion.steps` (`spawnrate 30`),
`--port 27471 --arg=+map --arg=dm_lockdown`.

The view stayed within `cl_sporeclipdistance` (512) of the cloud, so the
half-rate branch is not in the recording; a plain fact covers it.

Rows: `F` as the dust trail's; `P` spawnrate, previous (running) spawnrate,
lifetime, startsize, endsize, spawnradius, emit, MainViewOrigin xyz,
cl_sporeclipdistance; `N`/`Q`/`D` as the smoke trail's, material 0 the spores
(`particle/fire`), 1 the smoke (the second dust puff).
