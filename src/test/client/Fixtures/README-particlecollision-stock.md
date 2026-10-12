# `particlecollision-stock.tsv.gz`: provenance

Stock `CParticleCollision` (particle_collision.cpp with its base
`CBaseSimpleCollision`), as the client's impact flecks and explosion debris
used it, recorded before the collision moved to C#.
`ParticleCollisionOracleTests` replays it against
`SourceSharp.Client.Effects.ParticleCollision`, bit for bit.

## How it was recorded

1. Base `a9d8f9fd` (client-novalve), in a scratch worktree, with
   `tools/probes/client/particle_collision.cpp.probe.diff` applied to a probe
   copy of the fork's particle_collision.cpp (`ss_client_probe_copy` in
   probe_copies.cmake) and the client built with `-DSS_CLIENT_PROBES=ON`. The
   copy routes its three `UTIL_TraceLine` calls and its one
   `random->RandomFloat` through loggers, and logs `Setup`'s inputs and the
   planes it found, and each `MoveParticle`'s state on entry and exit.
2. `tools/probes/c0-session OUT tools/probes/sessions/c5p-collision.steps
   --arg=+map --arg=dm_lockdown --port 27646`: SMG bursts into the floor and
   walls at three places and two `env_explosion`s. 65 Setups, 127,703 moves.
3. Kept: every Setup with its traces, every move that traced or drew (4,000
   or so: the bounces, the stops, the plane crossings), and 1 in 40 of the
   moves that did neither. Each move is a pure function of its object's
   planes, its inputs and its recorded traces and draws, so the sampling
   loses no state.

## Rows (tab-separated; floats %.9g; `|` separates a trace's inputs from its answer)

| tag | columns |
|---|---|
| `pcs` | object, origin xyz, has direction, direction xyz, angular spread, min speed, max speed, gravity, dampen |
| `pcp` | object, plane count, then per plane: normal xyz, dist |
| `pct` | start xyz, end xyz, `\|`, fraction, endpos xyz, normal xyz, dist, allsolid, startsolid |
| `pcr` | min, max, value: `random->RandomFloat` |
| `pcm` | object, origin xyz, velocity xyz, has roll, roll delta, time delta, gravity, dampen, plane count |
| `pcmo` | object, origin xyz, velocity xyz, roll delta, bounced, the trace's fraction and allsolid after the call |

A Setup is `pcs`, its `pct` rows, `pcp`; a move is `pcm`, its `pct` and `pcr`
rows, `pcmo`. Objects are addresses: a reused address with no Setup of its
own, or an object never set up, is a constructor's empty plane list.
