# `sprite-stock.tsv.gz`, `spritetrail-stock.tsv.gz`: where they came from

The sprite decisions still native in our shells, recorded off a live game
before lane c2-fx moved them to C# (plan_client_novalve.md §2 cluster 4, the
coordinator's sprite item): `C_SpriteRenderer::DrawSprite`, `GetSpriteAxes`,
`StandardGlowBlend` and `DrawSpriteModel` (`src/client/native/shell_sprite_leaf.cpp`),
the managed sprite's scale and aspect (`SourceSharpDrawSpriteCore`) and the
`env_spritetrail` ribbon (`CSourceSharpSpriteTrail::UpdateTrail`,
`UpdateBoundingBox`, `DrawModel`, `src/client/native/shell_sprite_render.cpp`).

Recorded at `client-c2-fx` `341a0188`, debug, through PROBE COPIES of those two
shells (`tools/probes/client/shell_sprite_*.cpp.probe.diff`,
`ss_client_probe_copy_ours` in `probe_copies.cmake`), driven by
`tools/probes/sessions/fx-sprites.steps`: `dm_lockdown` with two `cs_te_fire
all` volleys and `r_drawsprites` 2 and 0, then `ss_sandbox`'s sprite station.
There the ribbon is moved in a circle (`cs_cl_probe_trailwiggle`) and, in
view, put through four parameter sets (`cs_cl_probe_trailparams`: a width
jitter, texture and tail fade; no end width; a zero lifetime; back to the
map's); then the station is seen from five more places, and the
`cs_cl_probe_spriteaxes` battery is asked against three views.

## Rows

Each row is the probe line after its `<ns> <pid> fx ` prefix: a tag, then the
probe's `key=value` groups (a vector is three numbers after its key). `%.9g`
round-trips a float.

`sprite-stock.tsv.gz` (5,555 draws, 78 skips, 792 axis questions):

| tag | what |
|---|---|
| `spc` | a managed sprite's draw (`SourceSharpDrawSpriteCore`): owner, world-space flag, scale asked, model width and height, the scale handed down, glow proxy size, aspect, offset |
| `spr-in` | `DrawSprite` entered: renderer id, `r_drawsprites`, `ShouldDrawInWireFrameMode()`, the sprite's orientation, extents (left right up down) and size, origin, angles, attached, render mode and fx, alpha, colour, scale, HDR scale, the render blend, the view's forward right up, the frame |
| `spr-skip` | `DrawSprite` declined before reading the sprite |
| `spg-vis`, `spg-dist`, `spg-ret` | `StandardGlowBlend`: the query (position, proxy, aspect) and the fraction the engine answered; the sight distance; the blend returned and the scale it left |
| `spr-blend` | the blend set on the renderer, the colour and scale after it |
| `spr-axes` | `GetSpriteAxes`'s forward right up, and the effect origin |
| `spb` | `DrawSpriteModel` set the blend to 1 |
| `spq` | the quad: wireframe material, scale, colour bytes, HDR scale, texture range, four corners |
| `spr-ret` | `DrawSprite`'s answer |
| `spa-view`, `spa` | the battery: the view, then each `GetSpriteAxes( type, origin, angles )` with outputs pre-filled 777/787/797 |

`spritetrail-stock.tsv.gz` (1,258 ticks, 458 draws, 11,135 segments):

| tag | what |
|---|---|
| `trl-upd` | the ribbon's parameters, at creation and at each override |
| `trl-tick` | a frame's tick: curtime, followed origin, ring count, first, next update time |
| `fxr` | `RandomFloat( -variance, variance )` and what it drew |
| `trl-box` | after the tick: ring, next update time, the relative box |
| `trl-draw` | `DrawModel`: curtime, origin, ring, visible, wireframe |
| `trl-seg` | one `CBeamSegDraw` segment: position, colour, alpha, width, texture coordinate |
| `trl-end` | the ring after the draw retired the dead points |

## How to record them again

    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON
    ninja -C farm/debug/build client_sourcesharp
    tools/box-ready gate --wait
    tools/probes/c0-session OUT tools/probes/sessions/fx-sprites.steps --config debug --port N
    tools/probes/sprite-select OUT/probe.log src/test/client/Fixtures
    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=OFF && make debug

The probe diffs apply to the shells as they were at `341a0188`; they stop
applying in the commit that turns the shells into executors.
