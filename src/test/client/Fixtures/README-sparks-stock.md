# `sparks-stock.tsv.gz`: provenance

fx_sparks.cpp as stock ran it: `FX_ElectricSpark`, `FX_MetalSpark` and
`FX_EnergySplash` (the emitters each made, every particle, every random
draw, the metal spark's impact quad), `CTrailParticles`' steps and draws, and
`CSimpleGlowEmitter`'s visibility gate, with particle_collision.cpp's setups,
traces and draws interleaved. `SparksOracleTests` replays it against
`SourceSharp.Client.Effects.SparkEffects` / `TrailRules` / `GlowGate`, bit
for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with two
probe copies applied (`tools/probes/client/fx_sparks.cpp.probe.diff`,
`particle_collision.cpp.probe.diff`) and the client built with
`-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-sparks.steps`
(`tools/probes/c0-session OUT tools/probes/sessions/c5p-sparks.steps
--arg=+map --arg=dm_lockdown`): the sparks, metalsparks and energysplash
temp entities fired in front of the player from eight angles, then bullets
into a metal drum. 48 electric sparks, 35 metal sparks (11 from impacts, the
rest from the temp entity, all at scale 1), 24 energy splashes; 947,177 trail
steps and 813,050 trail draws, all in the main view.

Kept: every metal spark's and energy splash's whole unit, one electric spark
in four whole (the others' emitters as `skx` rows and their collision setups
with their tags `x`-prefixed, so the steps that move against them replay),
every glow row, the steps that died (1 in 30), traced (1 in 100) or neither
(1 in 800), and 1 in 300 drawn trail draws with 1 in 1000 of all.

| tag | columns |
|---|---|
| `ske` | position xyz, magnitude, trail length, has direction, direction xyz |
| `skm` | position xyz, direction xyz, surface normal xyz, scale, throttle, `fx_drawmetalspark` |
| `skn` | position xyz, normal xyz, flags |
| `skc` / `skx` | emitter, `trail` / `glow` / `simple`, name, sort origin xyz, curtime |
| `skr` | `f` or `i`, min, max, value (`Vector::Random`'s three `rand()`s logged as `f -1 1`) |
| `skp` | emitter, particle, `trail`: position xyz, lifetime, die time, width, length, velocity xyz, rgba; or `simple`: position xyz, velocity xyz, lifetime, die time, rgb, start and end alpha, start and end size, roll, roll delta, material |
| `skq` | `data`, origin xyz, normal xyz, yaw, start scale (the metal spark's quad) |
| `sts` | emitter, particle, dt, flags, velocity dampen, position xyz, velocity xyz, lifetime, die time |
| `stso` | particle, then `removed`, or position xyz, velocity xyz, lifetime |
| `std` | emitter, particle, flags, view id, the model-view's 12, position xyz, velocity xyz, lifetime, die time, length, width, rgba, `\|`, view-space start xyz, scaled delta xyz, width, colour rgba, drawn, the four corners |
| `sgs` | glow, curtime, death time, `dead` / `untested` / `step` |
| `sgr` | glow, view id, tested and visible masks before, visible fraction (-1: not queried), curtime, start time, `\|`, the masks after, drawn |
| `pc*` | as particlecollision-stock.tsv.gz |

Stock ran under the engine's flush-to-zero: twice a dampened velocity
component is a denormal in the port and the signed zero stock flushed it to;
the step fact compares such a value as its zero. The scale > 1 arm of the
metal spark (every third spark flying far) was not reached by this session.

## `sparkfan-stock.tsv.gz`

`FX_SparkFan` (the ManhackSparks client effect), recorded at the same base
with `fx_sparks.cpp.probe.diff`'s `skf` row (the call's position and normal)
and its draws routed through the probe's `skr` logger, over
`tools/probes/sessions/c5p-sparkfan.steps` (`c5p_dispatch ManhackSparks` at
three places and normals). Kept: each fan's rows from its `skf` to its
thirty-fifth spark (`pcs`, the set-up's `pct` traces, `skr`, `skp`), nothing
from the later steps. `SparksOracleTests.Every_spark_fan_is_stocks` replays it.
