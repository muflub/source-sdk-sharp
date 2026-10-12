# `beam-stock.tsv`: where it came from

The beam oracle for plan_client_novalve.md §5.1 (cluster 5: `view_beams`,
`beamdraw`). It holds the **native** `CViewRenderBeams` and beamdraw.cpp's
mesh builders as they ran on a live `dm_lockdown` before any of the beam code
moved to C#. `BeamStockReplayTests` replays it through the managed beams and
requires every beam's state and every strip segment and mesh vertex they emit to
match **bit for bit**.

Recorded at `client-c2-beams` `165a43b3` (the rope port landed, nothing of the
beam port written), fork `66f63b2de`, debug build.

## The probes

`tools/probes/client/c2-beams/view_beams.diff` and `beamdraw.diff` patch
build-time copies of the two stock TUs (`-DSS_CLIENT_PROBES=ON` only; nothing
patched is committed, the fork is untouched). They add logging, and one probe
command, `c2_beam_probe_extra`, that makes the beam shapes no temp entity in
this build reaches (a halo, a halo beam, sine noise with both shades, a fade-in
untiled beam, a solid fade-out noise-once beam, a disk, a cylinder, a tesla and
a laser between points). The command builds them through the stock creation
paths, so what is recorded is stock code.

Two loggers wrap stock objects to see what reaches the renderer. `C2SegDraw`
derives `CBeamSegDraw` and logs each `NextSeg`. `C2MeshBuilder` derives
`CMeshBuilder` and logs each vertex at `AdvanceVertex`. Both forward
everything.

## The rows

Tab-separated, `%.9g` (round-trips a float). The second column of a beam row is
the probe's beam id (assigned at `BeamAlloc`).

| tag | what |
|---|---|
| `BI` | a `BeamInfo_t` creation call: the kind and every field (the endpoint entities as entry index and whether they have a model), with curtime and frametime |
| `BR` / `BS` | the beam it made (-1 for none) and its whole state |
| `BA` / `BF` | an allocation (renderable or not) and a free |
| `MD` | a model read: index, whether it resolves, its frame count, its name |
| `UF` | a frame's `UpdateTempEntBeams`: curtime, frametime |
| `UB` / `UBT` | a beam's state after its update and bounds, and its trail nodes (sampled) |
| `CB` | a `CullBeam`: the box, pvsOnly, and the engine's `IsBoxVisible` and `CullBox` answers |
| `EP` | an entity endpoint position: entry, attachment, hitbox flag, success, position |
| `KD` | a `KillDeadBeams` for an entity entry |
| `DM` | a draw kept by the selection: beam id, type, and 1 when its output rows were kept (0: inputs only) |
| `DV` | the view the draw ran in: current origin, forward, right, up and angles, the main view's up and right, the world-to-screen matrix, curtime, frametime, `r_DrawBeams` |
| `DB` / `DBT` / `DA` / `DAT` | the beam's state (and trail) before and after the draw |
| `PV` | a halo's pixel-visibility answer |
| `RB` | a ring's box: the engine's `IsBoxVisible` and `CullBox` answers |
| `SC` | a follow beam's debug-overlay screen positions |
| `ST` / `HD` | a sprite texture bind (model name, frame, render mode, wireframe) and an `$hdrcolorscale` set |
| `SG` / `SN` | a `CBeamSegDraw` start (segments, material) and each segment it was handed |
| `MB` / `MV` | a mesh begin (primitive, count) and each vertex: colour kind (0 float, 1 rgb bytes, 2 rgba bytes), position, colour, texture coordinate |
| `DE` | the draw's end |

## How to record it again

    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON
    ninja -C farm/debug/build client_sourcesharp
    tools/probes/c0-session OUT tools/probes/sessions/c2-beams.steps --port 27461
    tools/probes/client/c2-beams/select-beams OUT/probe.log src/test/managed/Fixtures/beam-stock.tsv
    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=OFF && make debug

The session parks the viewer in dm_lockdown's shower room and fires every beam
temp entity (`cs_te_fire`). It moves the player while the follow beam trails
it, then runs `c2_beam_probe_extra`. Each beam lives two seconds in view.
`select-beams` keeps every input row (so every frame and every state-changing
draw replays) and samples the output rows. It refuses a selection that lost an
arm.

## What it covers, counted (this recording)

16 creations (13 beams; the two entity-to-entity beams and the ring are refused
at creation because the local player entity has no model in this port, which
is the stock arm `CreateBeamEnts` takes and the replay checks), 309 frames, 812
culls, 190 entity positions, 351 draws replayed (55 compared whole: 28 points
in five flag sets, 4 disks, 4 cylinders, 7 follows, 4 ring points, 4 lasers,
4 teslas, halo beams among the points), 636 strip segments and 790 mesh
vertices compared.

Not covered by this recording (no creation path in this build reaches them):
`TE_BEAMSPLINE` (`DrawSplineSegs`) and the entity-to-entity ring. They are
pinned by hand facts in `BeamDrawArmTests`.
