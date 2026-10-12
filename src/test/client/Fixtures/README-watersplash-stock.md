# `watersplash-stock.tsv.gz`: provenance

fx_water.cpp as stock ran it: `FX_GunshotSplash` (its inputs, the light
`FX_GetSplashLighting` read and what it made of it, every random draw, the
sixteen drops and the eight-particle gout, the ripple's water trace, the
sound), `RippleCallback`'s lighting, and `CSplashParticle`'s steps and its
clipped alpha. `WaterSplashOracleTests` replays it against
`SourceSharp.Client.Effects.WaterSplashes` / `WaterMath` / `SplashRules`,
bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copy `tools/probes/client/fx_water.cpp.probe.diff` (with the
fx_sparks.cpp and particle_collision.cpp probes) and the client built with
`-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-water.steps`: the
watersplash, gunshotsplash and waterripple client effects dispatched by
cs_sv_te_dispatch (scale 1, so the splash's scale is 1/8) in front of the
player from six angles. Every `w*` row is kept: 24 splashes, 384 drops, 192
gout particles, 1,224 gout steps (every one ends clipped at the surface),
1,224 alphas, 36 ripple traces (none found water: dm_lockdown has none
there) and 12 ripple lightings.

| tag | columns |
|---|---|
| `wse` | origin xyz, normal xyz, scale, `cl_show_splashes` |
| `wsl` | the world light xyz, `\|`, the splash colour xyz, luminosity |
| `wsc` | emitter, `trail` or `splash` (and its cull radius) |
| `wsr` | `f` or `i`, min, max, value (`RandomVector` and `Vector::Random` logged per component) |
| `wsp` | drop: particle, position xyz, lifetime, die time, width, length, velocity xyz, rgba |
| `wss` | gout: particle, position xyz, velocity xyz, lifetime, die time, rgb, start and end alpha, start and end size, roll, roll delta |
| `wre` | origin xyz, scale, has colour, colour xyz, lifetime, alpha, `\|`, the trace's fraction, end xyz, normal xyz |
| `wsd` | the splash sound's origin |
| `wrc` | origin xyz, effect scale, the world light xyz, `\|`, colour xyz, luminosity |
| `wst` | emitter, particle, dt, clip on, clip height, position xyz, velocity xyz, lifetime, die time, roll, roll delta, start size, end size |
| `wsto` | particle, then `clipped`, `removed`, or position xyz, velocity xyz, lifetime, roll, roll delta |
| `wsa` | position z, clip height, start alpha, start size, end size, lifetime, die time, `\|`, scale, alpha |

Not reached here, and held by WaterSplashTests instead: a ripple on water,
the slime splash (its live arm is a named particle system), the effects'
slime flag, cl_show_splashes off, and the scale's clamp at 4.
