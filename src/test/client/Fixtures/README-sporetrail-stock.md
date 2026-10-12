# `sporetrail-stock.tsv`: provenance

Stock `C_SporeTrail` (c_smoke_trail.cpp), carried in our
`src/client/native/shell_trail_sim.cpp` until lane c2-sims, recorded live
before the simulation moved to C#, by the method of
README-smoketrail-stock.md (fixed seed 0x5eed, tolerance zero) at base
`87390d60` with the extended `tools/probes/c2sims-trail-probe.diff`, plus
`m_bEmit = true` forced in the probe's `Update` wrapper: nothing a map or a
player can do raises it (see EnvSporeTrail's remarks), so a stock spore trail
neither spawns nor draws. `SporeTrailSimOracleTests` replays it against
`SourceSharp.Client.Effects.SporeTrailSim`. Session:
`tools/probes/sessions/c2sims-sporetrail.steps`, `--port 27471 --arg=+map
--arg=dm_lockdown`.

Its particles are kept in the trail's own space and drawn through
`m_mAttachmentMatrix`, which our follow sweep fills from the entity. Stock drew
only on the first frame: the effect's bounds are computed from those
trail-space positions (a box at the world origin), and are culled after. The
two `D` rows are that frame's, as world positions (`Vector3DMultiplyPosition`
of the matrix the `P` row carries). The port draws world positions with world
bounds, so a forced-on trail draws every frame.

Rows: `F` as the dust trail's; `P` emit, then the attachment matrix's three
rows (12 floats); `N` spawned (positions trail-space); `Q` after the drift
and age step (no roll, no velocity rule), before the removal; `D` world
position, colour (white), alpha 1, size 4, roll 0. The effect is made at
Start, so frame 1 is not stepped.
