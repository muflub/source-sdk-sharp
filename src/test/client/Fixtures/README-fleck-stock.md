# `fleck-stock.tsv.gz`: provenance

The impact flecks as stock made them: shell_impact_effects.cpp's
`CreateFleckParticles` (the sampling inside the impact block's ranges, ours
since c2-fx moved the decisions to ImpactEffectPolicy) and fx_fleck.cpp's
`CFleckParticles` (the merge list, `SimulateParticles`, `RenderParticles`),
with particle_collision.cpp's traces and draws interleaved. `FleckOracleTests`
replays it against `SourceSharp.Client.Effects.FleckSystems`/`FleckRules`,
bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with
three probe copies applied (`tools/probes/client/shell_impact_effects.cpp.probe.diff`,
`fx_fleck.cpp.probe.diff`, `particle_collision.cpp.probe.diff`) and the
client built with `-DSS_CLIENT_PROBES=ON`, over
`tools/probes/sessions/c5p-collision.steps`: 63 impacts, 689 flecks, 65
creations (merges among them), 124,086 steps. Kept: every impact's whole unit
(its inputs and block, the creation, the collision Setup and its traces, every
random draw, every fleck made), every creation, every collision Setup, every
step that traced, drew, died or stopped in solid and 1 in 40 of the rest, and
the next draw of each kept step's fleck.

| tag | columns |
|---|---|
| `fki` | trace end xyz, normal xyz, surface colour rgb, scale, throttle, block length, the block |
| `fkc` | name, centre xyz, extents xyz, `\|`, the system, merged, and (merged) the box after |
| `fke` | the system, its collision |
| `fkr` | `f` or `i`, min, max, value |
| `fkp` | the fleck, sheet, die time, size, velocity xyz, roll, roll delta, colour bytes, position xyz |
| `fks` | the system, the fleck, dt, position xyz, velocity xyz, roll, roll delta, lifetime, die time |
| `fkso` | the fleck, then `removed`, or position xyz, velocity xyz, roll, roll delta, lifetime, allsolid |
| `fkd` | the system, the fleck, position xyz, colour rgb, alpha, size, roll |
| `pc*` | as particlecollision-stock.tsv.gz |

One stock fact is not replayed: a fleck whose system's collision has no
planes reads `trace.allsolid` from an uninitialised `trace_t`; the port reads
false, and the fact counts those rows apart.
