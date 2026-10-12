# C7: the case for retiring the stock render fallback (viewrender.cpp, view.cpp)

**Ruled 2026-10-12 by the user: "Freeze recordings" (option 3 below).**
- viewrender.cpp and view.cpp leave client.so outright.
- `cs_cl_render_plan 0` goes, with no stock build option.
- The five render-stock fixtures and the synthetic facts are the permanent reference.

Everything the remaining ports need from the stock path was recorded before
the eviction:
- the five frames, re-recorded;
- 20 post-processing frames (`src/test/client/Fixtures/post-stock/`).

Each frame carries every convar and the engine's answers by name, and each
screen-space draw's bound material parameters. `RenderTrace.Inputs` reads an
older, shorter RenderInputs, so the struct may grow at its end. The motion
blur and tonemap state is held frame by frame in `motionblur-stock.tsv` and
`tonemap-stock.tsv.gz` (probe recordings).

Prepared 2026-10-12 for the integrator, to put to the user. Measured on
client-r-c7 471678aeb (client-novalve 63dc07889 + the debug-overlay commit).

## What goes

Retiring viewrender.cpp and view.cpp from client.so removes the stock render
path, which is `cs_cl_render_plan 0` and a view whose program is declined. With
it goes the live same-run compare: render-plan, render-plan-sandbox and the
debug-overlay round each record a frame from each path.

## What the offline oracle covers today

There are five recorded stock frames: dm_lockdown poses 1-3, ss_sandbox
underwater, and the ss_sandbox spawn. The plan is checked against them by
RenderPlanFixtureTests: the frame's top-level calls, plus each world view's
draws over its lists (the view programs).

The 85 top-level op kinds and the ~25 program kinds, by how the fixtures hold
them:

| | kinds | how |
|---|---|---|
| exact recorded calls | 38 top-level | every argument printed by the recorder (views, clears, fog, world lists, world draws, targets, ...) |
| program ops, exact | SetBlend, SetColorModulation, DrawStaticPropsBatch, TranslucentSurfaces, UpdateFullScreenDepthTexture, FullScreenDepthTextureValidity, DrawEntity (as a restricted gap) | EachViewsDrawsOverItsListsAreStocksCalls |
| gaps (calls not compared offline) | 18 top-level | ClientSystems Pre/PostRender, DrawOpaque/Translucent/NoZ when no scene, PixelVis*, Glow*, Precipitation, ClientEffectsDraw, RenderPlayerSprites, PostScreenSpaceEffects, UnderwaterOverlay, SmokeFogOverlay, ScreenOverlay, EnginePostProcessing, ScreenSpaceEffects |
| make no recorded call | 15 | AllowCurrentViewAccess, SaveRenderTarget, area bits, the current setup, ... |
| in no fixture | ~20 top-level | water reflection/refraction above the surface, CopyRefractionTarget, clip planes, Upscale, MotionBlur, monitors (ViewerSeen*, ViewTargetDrawn), the debug overlays |

The ones in no fixture are held by RenderPlannerTests' synthetic inputs: rules,
not recordings. Above-water water views can't be recorded in this tree at all
(ssmap's CONTENTS_TESTFOGVOLUME, docs/todo.md), with or without the stock path.

## What only the live compare catches

1. **The insides of the gap ops**: the calls a helper makes, in order, in a
   real frame. Both paths call the same native helper, so a difference there is
   placement or arguments, which the live compare sees and the fixtures don't.
2. **The debug overlays and the monitor pass against stock.** No fixture
   carries them; the live round with four overlays on is the only stock
   comparison.
3. **Engine answers mid-frame on a live map**: radial vis, fog colour read
   back, leaf translucency, render-target sizes. Fixtures hold only the frames
   recorded.
4. **Regressions in the world or entities**: a new map feature, entity kind
   or renderable list shape (the non-entity renderables were found this way).

Not lost:
- Crossing and time costs: the render rows measure the plan path alone.
- The view programs: offline, exact.
- The A/B noise machinery's own facts.

## How fixtures get re-recorded without a stock path

1. **Keep stock behind a build option, not in the shipping client.** For
   example `make debug SOURCESHARP_STOCK_VIEW=1` compiles viewrender.cpp,
   view.cpp and the fallback seam for re-recording only (the c7-render*.steps
   sessions). The shipping client links neither. The daily gate could build it
   weekly and re-run render-plan as an oracle leg.
   - Cost: the fork files stay buildable and sdk-links keeps their lines,
     moved to a test list.
2. **The pristine-SDK oracle build** (`oracle-<config>`, src/test/oracle) is a
   test build of upstream TUs, not a playable client. It has no engine
   session, so it can't record a frame. It could host a unit-level oracle of
   CViewRender's decision functions (Determine water info, the fog math, the
   skybox setup) fed from a recording's inputs. That is unit coverage, not a
   frame.
3. **Freeze the five recordings** as the oracle for good, and rule that a
   change to the render plan's output re-derives against stock code by reading
   it (`farm/.../viewrender.cpp` in the fork).
   - Cost: a new map feature has no stock recording.

Recommendation (not taken; the ruling is 3): 1. It keeps a stock A/B available on demand, at the price of
keeping two fork TUs compiling in a non-shipping build.
