# `firetrail-stock.tsv`: provenance

Stock `C_FireTrail` (c_smoke_trail.cpp) and its `CSmokeParticle` emitter,
carried in our `src/client/native/shell_trail_sim.cpp` until lane c2-sims,
recorded live before the simulation moved to C#, by the method of
README-smoketrail-stock.md (fixed seed 0x5eed, tolerance zero) at base
`87390d60` with the extended `tools/probes/c2sims-trail-probe.diff`.
`FireTrailSimOracleTests` replays it against
`SourceSharp.Client.Effects.FireTrailSim`.

The probe moves the C++ trail each frame before `Update` (origin +
(30 * (frame & 1), 9 * ((frame >> 1) & 1), 0); the follow sweep puts it back,
so offsets do not accumulate) so that the flame gap fill runs; the `F` row is
the origin `Update` saw. Because it moves every frame, the clamp of the flame
count up to one is not exercised. Session:
`tools/probes/sessions/c2sims-firetrail.steps`, `--port 27471 --arg=+map
--arg=dm_lockdown`.

Rows: `F` dt, curtime, origin xyz, angles xyz, velocity xyz, frametime; `P`
m_flLifetime, last position xyz; `N`/`Q`/`D` as the smoke trail's, material
in stock's FTRAIL order (0, 1 smoke; 2..6 flamelet1..5). The emitter is made
at Start, so frame 1 is not stepped.
