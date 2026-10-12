# `dustpuff-stock.tsv.gz`: provenance

FX_Dust, the dust puff, as stock ran it: c_func_dust.cpp's body, carried
verbatim in `src/client/native/shell_dust_render.cpp` since step 7a.
`DustPuffOracleTests` replays it against `SourceSharp.Client.Effects.DustPuff`,
bit for bit.

Recorded at `1cba7038` (client-r-particles) with the probe copy
`tools/probes/client/shell_dust_render.cpp.puff.probe.diff` and the client
built with `-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-dustpuff.steps`
on dm_lockdown: nine `c5p_ieffects dust` calls across the clamps (a size under
2 gives one puff, over 64 gives 32; a negative speed becomes 1, over 480 is
48), in light and in shadow, and two `cs_te_fire dust` (the temp entity's
64 / 32). 11 puffs, 131 particles, every draw.

| tag | columns |
|---|---|
| `dpe` | origin xyz, direction xyz, size, speed, `\|`, puffs, the clamped speed, the world light xyz |
| `dpr` | `c` and the `rand()` drawn (Vector::Random), or `f` / `i`, min, max, value (`random->RandomFloat` / `RandomInt`) |
| `dpp` | the particle: position xyz, die time, velocity xyz, rgb, start alpha, end alpha, start size, end size, roll, roll delta, material (0 particle_smokegrenade, 1 particle_noisesphere) |
