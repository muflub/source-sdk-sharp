# `teparticles-stock.tsv.gz`: provenance

The legacy temp entities' particles as stock ran them: `CTEParticleRenderer`
(c_te_particlesystem.cpp, carried verbatim in
`src/client/native/shell_te_particle_render.cpp`) and the two effects that
build one, `TE_BloodStream` and `C_TEShowLine::PostDataUpdate` (carried in
`shell_te_backends_client.cpp`). `TeParticleOracleTests` replays it against
`SourceSharp.Client.Effects.TeParticles`, bit for bit.

Recorded at `832b0d4e` (client-r-particles, on main 375193a4) with the probe
copies `tools/probes/client/shell_te_particle_render.cpp.tep.probe.diff` and
`shell_te_backends_client.cpp.tep.probe.diff`, the client built with
`-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-teparticles.steps` on
dm_lockdown: four blood streams (three through `cs_cl_te_backend`, one at
sv_gravity 300) and two debug lines. The C0 probe file
(`shell_probes_debug.cpp`) no longer links on this main (its vgui panels), so
it was left out of the probe build for this recording, and c0-session's
check was pointed at `c5particles_probes_debug.cpp` in a local copy.

This file keeps every generation row, every 25th simulate frame plus every
frame in which a particle died, and every 25th render pass (pointers name the
renderer and the particle).

| tag | columns |
|---|---|
| `tbe` | renderer, origin xyz, direction xyz, r, g, b, a, amount |
| `tse` | renderer, start xyz, end xyz |
| `tpr` | `f` or `i`, min, max, value (`random->RandomFloat` / `RandomInt`) |
| `tpp` | a particle made: renderer, particle, position xyz, velocity xyz, lifetime, type, ramp word, rgba |
| `tpw` | renderer, the step's dt, `GetCurrentGravity()` |
| `tpi` | a particle before the step (columns as `tpp`) |
| `tpo` | renderer, particle, `removed`, or the particle after (columns as `tpp`) |
| `tpv` | renderer, the model-view matrix's three rows, the particle size |
| `tpd` | renderer, position xyz, rgba, `\|`, view-space xyz, colour xyz, alpha |
