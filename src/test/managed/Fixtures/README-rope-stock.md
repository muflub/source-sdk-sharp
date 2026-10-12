# `rope-stock.tsv`: where it came from

The rope oracle for plan_client_novalve.md §5.1 (cluster 6: `c_rope`,
`rope_physics`, `simple_physics`). It holds the **native** `C_RopeKeyframe`
as it ran on a live `dm_lockdown` before any of the rope simulation moved to
C#. `RopeStockReplayTests` replays every row through the managed rope and
requires the node positions, the rope's state and every built segment to match
**bit for bit**.

Recorded at `client-c2-beams` base `cb18f0a8`, fork `66f63b2de`
(`sdk-root.lock`), debug build, with the probe described below and nothing of
the port written.

## The probe

`tools/probes/client/c2-beams/c_rope.diff` patches a copy of the stock
`game/client/c_rope.cpp` at build time. The copy replaces the original in
`client.so` only when `farm/debug/build` is configured with
`-DSS_CLIENT_PROBES=ON` (`src/client/native/CMakeLists.txt`). Nothing patched
is committed, and the fork is not touched. The probe adds logging only; every
line of the stock arithmetic runs unchanged. Three of the stock bodies
(`RopeThink`, `DrawModel` and `BuildRope`) run inside a lambda so that their
early returns still reach the dump after them.

## The rows

Every row is tab-separated. The second column is the rope
(`C_RopeKeyframe::m_nRopeIndex`). `%.9g` round-trips an IEEE-754 single.

| tag | when | what |
|---|---|---|
| `RC` | `Create`, after `FinishInit` | flags, segments, width, material, texture height, back material, and the members the constructor never initialises (`m_bConstrainBetweenEndpoints`, `m_bNewDataThisFrame`, `m_bApplyWind`, the gust timers, the wind direction, the previous impulse), as the allocator left them |
| `RS` | `RecomputeSprings` | the members `SourceSharp_RopeConfigure` wrote just before it: length, slack, width, texture scale, subdiv, scroll speed |
| `SP` | `SetStartPos` (0) / `SetEndPos` (1) | the endpoint written |
| `RT` | `RopeThink`, on entry | `frametime`, `curtime`, `MainViewOrigin()`, `GetWindspeedAtTime(curtime)`, `r_drawropes`, `rope_shake`, `rope_wind_dist`, `rope_collide`, whether physics was initialised |
| `RF` / `RV` | each `RandomFloat` / `RandomVector` the rope draws | the arguments and the value drawn: the replay's random source returns exactly these |
| `RN` | after `RopeThink` | each node's `m_vPos`, `m_vPrevPos` and `m_vPredicted` |
| `RX` | after `RopeThink` | the rest of the state: initialised, wind applied, impulse, previous impulse, gust timer, gust lifetime, time to next gust, wind direction, forced-move counter, previous and current locked points, links touching something, render bounds, spring length |
| `DM` / `CN` | `DrawModel` | whether the between-endpoints constraint is on, and (when it is) the nodes after it |
| `BR` / `BP` | `BuildRope`, on entry | view forward and origin, node count, colour modulation, length, slack, scroll, width, texture scale and height, subdiv, `rope_subdiv`, flags, `ShouldUseFakeAA`, `ScreenWidth()`, `mat_fullbright`, holiday mode and the five `rope_smooth_*` values; then each node's predicted position and light value |
| `BS` / `BE` | `BuildRope`, after it | each segment's position, colour, texture coordinate, alpha, width and back width; the segment count and the widest back width |

## How to record it again

    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON
    ninja -C farm/debug/build client_sourcesharp
    tools/probes/c0-session OUT tools/probes/sessions/c2-ropes.steps --port 27461
    tools/probes/client/c2-beams/select-ropes OUT/probe.log src/test/managed/Fixtures/rope-stock.tsv
    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=OFF && make debug

The session parks the viewer under the four shower cables, the only spans
`dm_lockdown` strings (its other five `keyframe_rope`s have no `NextKey`). All
four are inside `rope_wind_dist` of the view, so the gust wind is on. The view
looks up at them, so `BuildRope` runs. `select-ropes` keeps ropes 0 and 1 (the
450-unit span with 200 slack and the 128-unit span with 110), each from its
creation through its 240th think. That window holds the 5-second initial hang,
the first gust draw and every think after it, and every eighth build. The tool
prints what it kept and refuses a window with no draw or no build.

## What it covers, counted (this recording)

Per rope: 240 thinks (1,200 node rows), 4 `RandomFloat` and 1
`RandomVector` (the initial gust delay and one full gust), wind applied on 221 of
the 240 thinks (the first 19 ran before the viewer was parked, from 1,100
units away), 30 builds (390 segments, all of them in the FakeAA arm:
`cable/cable_back` exists and the material system's AA is off), 2 spring
configurations and 483 endpoint writes. `m_bConstrainBetweenEndpoints` is 0 on
both ropes, so `CN` is empty: that arm is covered by a hand fact, not by this
recording.
