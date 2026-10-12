# `dust-stock.tsv.gz`: provenance

The dust motes as stock ran them: `CSourceSharpDustVolume` in
`src/client/native/shell_dust_render.cpp`, which is stock's `CDustEffect`
and `C_Func_Dust`'s mote half (c_func_dust.cpp) with the back-pointer struck
out. `DustOracleTests` replays it against
`SourceSharp.Client.Effects.DustVolumeSim`, bit for bit.

Recorded at `5aeeef63` (client-r-particles) with the probe copy
`tools/probes/client/shell_dust_render.cpp.probe.diff` and the client built
with `-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-dust.steps` on
`ss_sandbox` (`make map MAP=ss_sandbox` in the same tree): the weather
station's func_dustmotes (flags 3: on, scaled motes) and func_dustcloud
(flags 1), both under the map's env_wind, seen from five stands. The run wrote
4.5 million rows; this file keeps every update, the first 800 ticks with every
draw they made, every 150th simulate frame, every 250th render pass and the
first six render passes that culled a mote (pointers name the volume).

| tag | columns |
|---|---|
| `dmu` | volume, created, rgba, spawn rate, size min, size max, speed max, lifetime min, lifetime max, distmax, fall speed, flags, origin xyz, volume mins xyz, maxs xyz |
| `dmt` | volume, frametime, origin xyz, frozen batch owed |
| `dmr` | the `rand()` drawn |
| `dmp` | volume, the test point xyz, its contents |
| `dmm` | volume, the mote made: position xyz, velocity xyz, die time, size |
| `dmn` | volume, the spawner's events this tick |
| `dmb` | volume, the box handed to the particle manager: mins xyz, maxs xyz |
| `dmw` | volume, the step's dt, frametime, the wind xyz, flags |
| `dmi` | volume, a mote before the step: position xyz, velocity xyz, lifetime, die time |
| `dmo` | volume, `removed`, or the mote after: position xyz, velocity xyz, lifetime |
| `dmv` | volume, the model-view matrix's three rows, flags, distmax, rgba |
| `dmd` | volume, a mote drawn: position xyz, lifetime, die time, size, `\|`, view-space xyz, alpha, size, sort key |
| `dmc` | volume, a mote past distmax: position xyz, lifetime, die time, size, `\|`, view-space xyz, sort key |

Not reached here, and held by DustVolumeSimTests instead: a test point the
brush is not solid at (both brushes are boxes, so the first point always
lands), a frozen volume, and a volume switched off.
