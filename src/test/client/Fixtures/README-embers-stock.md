# `embers-stock.tsv`: provenance

Stock `C_Embers` and its `CEmberEmitter` (c_embers.cpp), carried in our
`src/client/native/shell_effects_sim.cpp` until lane c2-sims, recorded live
before the simulation moved to C#, by the method of
README-smoketrail-stock.md (fixed seed 0x5eed, tolerance zero) at base `16017ee4`
with `tools/probes/c2sims-embers-probe.diff` (logs each simulate's inputs,
each spawn, each step after `UpdateRoll`, the manager's step `T` and the
draws the wander takes). `EmbersSimOracleTests` replays it against
`SourceSharp.Client.Effects.EmbersSim`, lining the port's embers up in
stock's step order first. Session: `tools/probes/sessions/c2sims-embers.steps`
(`ent_create env_embers ... model *4 density 40 lifetime 3 speed 40
spawnflags 1`), `--port 27471 --arg=+map --arg=dm_lockdown`.

Two findings: the spawn uses the frame time but the particle step is the
manager's own time delta (`T`, logged per ember), which differs in the last
bits; and stock drew none of the embers, because brush *4's model bounds put
them round the world origin, out of the player's view, so there are no `D`
rows. `m_clrRender` is white: nothing in this build sets it.

Rows: `F` frametime, curtime, angles xyz, origin xyz; `P` density, lifetime,
speed, emit, mins xyz, maxs xyz, colour rgb; `N`/`Q` as the smoke trail's;
`T` the step's time delta.
