# C7 step 2: viewpostprocess.cpp's decisions to C#

**Reviewed by the user 2026-10-12; completed: landed in 283add399, on main in batch 4 as 49388fff1.**

Drafted 2026-10-12 on client-r-c7 471678aeb.

## What it is

viewpostprocess.cpp, 3005 lines in the fork. The render plan's executor calls
two of its entry points:
- `DoEnginePostProcessing` (lines 2219-2660);
- `DoImageSpaceMotionBlur` (2706-3005).

Stock viewrender calls them too, plus `DumpTGAofRenderTarget`. shell_worldrender
imports the custom auto-exposure/bloom globals, and shell_clientdll imports
`ResetToneMapping`.

The decisions inside:
- **Auto exposure.** `CLuminanceHistogramSystem` issues occlusion queries per
  luminance bucket each frame, reads them back frames later, and finds the
  target tonemap scale (percent-bright-pixels, the min/max range, the manual
  rate). It keeps state across frames.
- **Tonemap scale.** `SetToneMapScale`, `ResetToneMapping`: the eased target
  and the per-frame rate.
- **Bloom.** `GetBloomAmount` / `GetCurrentBloomScale`, the 8-bit bloom
  texture passes and the debug boxes.
- **Software AA and the post material.** `CEnginePostMaterialProxy`'s values,
  the full-viewport / partial-viewport UV transforms, and the combine switch.
- **Pyro vision.** Vignette and side shapes, randomised over time; TF2-only
  in effect.
- **Motion blur.** The previous frame's view, the blur vector from the
  angular and positional deltas, the time-scaled falloff, and the proxy.
- **Debug.** `mat_debug_postprocessing_effects` quadrants, histogram display,
  `mat_dump_rts`.

## Shape

The same as the view programs: C# decides, native carries out.

1. **Motion blur first.** It is self-contained: a stateful C# class over the
   frame's view, computing the four blur values `CMotionBlurMaterialProxy`
   binds. The executor op draws the blur material with them. Facts come from
   stock's arithmetic over recorded view sequences, with a live check that
   `mat_motion_blur_enabled 1` draws.
2. **Tonemap and auto exposure.** The histogram's query issue and readback
   stay native: the engine's occlusion queries are called by pointer. The bin
   ranges, the target scalar, the eased scale and the exposure range are C#.
   One crossing per frame hands the readbacks over and gets the next frame's
   query rectangles and the scale back. This is the stateful core and needs
   the most facts.
3. **Bloom and the engine-post material.** The amount and scale are C#; the
   passes are executor ops (render-target pushes, screen-space rectangles,
   material vars). The proxy values come from C#.
4. **Pyro** (TF2 vision) is ported for parity or ruled out. It needs a ruling.
5. **Debug modes** go to plan ops like the debug overlays.
6. **The globals other TUs import** (the g_bUseCustom* set, ResetToneMapping,
   DumpTGA) move to a shell TU while viewrender.cpp still links them. They go
   with step 3 (the stock fallback).

## Oracle

A stock recording of the post-processing calls already exists: the render
traces include the ctx calls (SetToneMappingScaleLinear, SetGoalToneMappingScale,
the render-target copies and the DrawScreenSpaceRectangle calls). The tonemap
state needs a multi-frame recording: the same pose over N frames, with the
histogram readbacks noted as q lines, so the C# tonemap reproduces stock's
scale frame by frame.

## Size

Roughly 1500-2000 lines of decisions to C#, and a few hundred lines of executor.
Several days of work, in six commits.

## Status (2026-10-11)

- Item 1 (motion blur), decisions: done, 03c853307 (MotionBlur.cs; motionblur-stock.tsv).
- Item 2 (tonemap and auto exposure), decisions: done, 0945d192a (Tonemap.cs; tonemap-stock.tsv.gz on ss_sandbox).
  dm_lockdown has no HDR lighting, so stock never tonemaps there: any live check of
  the tonemap must run on ss_sandbox.
- Not wired: both need DoEnginePostProcessing as plan ops, and managed "MotionBlur" and
  "engine_post" proxies in place of stock's EXPOSE_INTERFACE ones. The RenderInputs
  additions (RealTime and the motion blur cvars) and the trace's $MotionBlurInternal
  q line wait for that wiring, because they force a re-record of render-stock.
- Next: item 3 (bloom passes, the engine-post proxy's values and the pass sequence)
  as executor ops, with the render traces as the oracle. Then the wiring.
- 2026-10-12: items 2-6 done and viewpostprocess.cpp evicted: 3b5a3ff8a (planner), 26ccbd1bd (crossing),
  3a4533e20 (pyro vision in TF2.Client), 032ab9c36 (mod convar duplicates), dfe61cc4d (eviction).
  Left: shell_viewpostprocess_client.cpp (x) goes with viewrender.cpp (step 3).
