# `smokefog-stock.tsv`: where it came from

Stock's smoke fog overlay (`game/client/smoke_fog_overlay.cpp`,
`DrawSmokeFogOverlay`), recorded off a live `dm_lockdown` before lane c2-fx
moved its decision to C# (plan_client_novalve.md §2 cluster 3).
`ClientSmokeFogOverlayTests` replays it.

Recorded at `client-c2-fx` `bb3033cd`, debug, through the lane's PROBE COPY
(`tools/probes/client/smoke_fog_overlay.cpp.probe.diff`, `probe_copies.cmake`)
driven by `tools/probes/sessions/fx-smokefog.steps`. Nothing in dm_lockdown
puts the viewer in smoke, so the probe's `cs_cl_probe_smokefog` lays a sweep
over the two globals before each draw: the alpha runs -0.5 .. 1.5 in
twentieths (0 included) and the colour's red -0.25 .. 1.25, its green 0.5 and
its blue 2.0, so every clamp arm is reached. The frames either side of the
sweep are stock's own (alpha 0).

## The rows

| tag | what |
|---|---|
| `sf-in` | a draw entered: `g_SmokeFogOverlayAlpha`, `g_SmokeFogOverlayColor`, whether the material is held |
| `sf-skip` | it returned before drawing |
| `sf-draw` | the quad: its colour and alpha after the clamps, and the depth it is drawn at |

401 draws: 292 quads, 109 skips.

## How to record it again

As `README-ieffects-stock.md`, with `fx-smokefog.steps`, then
`tools/probes/fx-select OUT/probe.log smokefog-stock.tsv --prefix sf --title "..."`.
The probe copy needs `smoke_fog_overlay.cpp` in the farm: the recipe works
until the commit that evicts it.
