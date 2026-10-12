# `simple3d-stock.tsv.gz`: provenance

particle_simple3d.cpp's CSimple3DEmitter as stock ran it: every step (the
life, the spin, the move against the collision with its traces and draws,
the settling towards flat) and every draw (the view's origin and forward,
the sort key, the facing colour, the fade, AngleVectors' normal, width and
height). `Simple3DOracleTests` replays it against
`SourceSharp.Client.Effects.Simple3DRules`, bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copies `tools/probes/client/particle_simple3d.cpp.probe.diff` and
`particle_collision.cpp.probe.diff` and the client built with
`-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-simple3d.steps`: the
shattersurface temp entity's glass shards (cs_te_fire) in front of the
player, from four angles, stepped until they settle. 159,869 steps and
158,333 draws; kept: every collision setup, 1 in 3 deaths, 1 in 40 traced
steps, 1 in 30 struck (the trace's fraction under 1) and 1 in 500 of the
rest, and 1 in 120 draws.

| tag | columns |
|---|---|
| `s3s` | emitter, particle, dt, position xyz, velocity xyz, angles xyz, spin, life remaining |
| `s3so` | particle, then `removed`, or position xyz, velocity xyz, angles xyz, spin, life remaining, the trace's fraction |
| `s3d` | particle, view origin xyz, view forward xyz, position xyz, angles xyz, life remaining, front rgb, back rgb, size, `\|`, sort key, colour xyz, fade, normal xyz, width xyz, height xyz |
| `pc*` | as particlecollision-stock.tsv.gz |

Stock ran under the engine's flush-to-zero: a dampened component the port
leaves denormal is compared as the signed zero stock flushed it to.
