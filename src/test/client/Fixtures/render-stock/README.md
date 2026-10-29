# `render-stock/*.txt`: where they came from

One frame each of stock's viewrender (plan_client_novalve.md C7 step 1, the
render plan's oracle), recorded off a live `dm_lockdown` at three fixed poses
with `cs_cl_render_trace`, driven by `tools/probes/sessions/c7-render.steps`:

    tools/probes/c0-session OUT tools/probes/sessions/c7-render.steps --no-probe-build

| file | pose (`/player/teleport`) |
|---|---|
| `dm_lockdown-pose1.txt` | (-4116, 4600, 3), angles (0, -90, 0): the live gate's renderStand |
| `dm_lockdown-pose2.txt` | (-3843, 2894, 0), angles (25, 90, 0): the gate's openFloor |
| `dm_lockdown-pose3.txt` | (-3800, 2500, 0), angles (-20, 30, 0) |
| `ss_sandbox-spawn.txt` | the spawn the client joins `ss_sandbox` at (no teleport), from `c7-render-sandbox.steps`: 1150 calls, two world views whose lists hold 25 translucent renderables that are not entities (particle systems) |
| `ss_sandbox-underwater.txt` | (-828, 2048, 8), angles (-30, 90, 0): standing in the sandbox's mirror pool (Sandbox.cs, MirrorWater), the eye under the surface; the 3D sky is in view through the hall's skylight |

`ss_sandbox-underwater.txt` comes from `tools/probes/sessions/c7-render-water.steps`
on a tree whose `ss_sandbox.bsp` was built from this commit's Sandbox.cs
(`make map MAP=ss_sandbox`). No pose above the water finds it: ssmap flags 2 of
the map's 2345 leaves `CONTENTS_TESTFOGVOLUME`, so the engine's visible-fog-volume
query answers -1 from outside the pool and stock draws no reflection or
refraction view anywhere in either map (docs/todo.md). Those branches are held
by `RenderPlannerTests` over synthetic inputs.

Recorded at `client-r-c7` on `4c26080b` plus the uncommitted executor that
lands with these files (debug, 1280x720 windowed), every frame drawn by stock
(`cs_cl_render_plan 0`).

Re-recorded 2026-10-11 for C7 step 2 at `client-r-c7` on `b4eec29fb` plus the step's
uncommitted scene recorder: each frame now also carries every world view's scene
(`scene`/`scenebytes`: `SourceSharpViewScene_t` and its leaves and entries in hex, noted
when the view's renderables list is built) and the `q` answers of
`LeafContainsTranslucentSurfaces`, and `RenderInputs` grew to 48 convar slots.

Re-recorded 2026-10-12 for C7 step 3, at `client-r-c7` on `0945d192a` plus the commit
that lands them. This is the last recording: the user ruled "freeze recordings", the
stock path leaves client.so, and these files are the reference for good.
Each frame now also carries:
- `cvar` lines: every convar's value at the frame's start;
- `env` lines: the engine's answers by name;
- `q material.var` lines: each screen-space draw's bound material parameters.

The live gate no longer holds a live frame to the `dm_lockdown` ones (user ruling
2026-10-28): they carry the world our server built on 2026-10-12, and items that
now fall and settle as stock's do, and a prop's skin, changed that world, so a
live frame asks other questions. `RenderPlanFixtureTests` still holds the plan
to every frame here over the frame's own inputs.

A later port reads its inputs from those by name. `RenderInputs` may only grow
at its end: `RenderTrace.Inputs` reads a shorter `inbytes` with the missing
fields as zero, and no frame can be recorded again. The post-processing frames are in `../post-stock/`.

Each line is one call client.so made on the engine's render interfaces, in
order, as `shell_render_trace_client.cpp` writes it: the view setups pushed,
render targets, clip heights, fog, world-list builds and draws, every model,
static-prop and brush draw. `q` lines are queries, `~` lines are the shadow
manager's own work, and `inbytes` is RenderView's inputs as the executor
gathered them (`SourceSharpRenderInputs_t`'s bytes in hex; a struct change
makes the fixture unreadable and `RenderPlanFixtureTests` says to re-record).

`RenderPlanFixtureTests` plans each frame from its inputs and requires the
plan's calls to be the recording's, in order, with only the in-view draws as
gaps; and, from each view's scene and stock's `q` answers, makes each view's
draws over its lists (`ViewDrawProgram`) and requires those calls too, with only
an entity's own draw as a gap (one that may not hold a colour, a static-prop
batch, a translucent leaf or the depth texture's flag).

The stock path left client.so's frame at C7 step 3 (cs_cl_render_plan and the
same-run compare are gone), so the `tools/probes/sessions/c7-*.steps` that
recorded these frames by switching to it no longer can; that is the ruling
("freeze recordings"), not an oversight. The live gate's render-plan step holds
plan frames to these files at their poses (render/compare?mutate=frozen:PATH).
