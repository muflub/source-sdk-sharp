# `tonemap-stock.tsv.gz`: where it came from

viewpostprocess.cpp's tonemap decisions, recorded off a live game before
C7 step 2 moved them to C# (`Tonemap.cs`):
- CLuminanceHistogramSystem's bucket ranges, queries and readbacks;
- GetTargetTonemapScalar;
- SetToneMapScale's moving average;
- GetBloomAmount;
- GetExposureRange.

Recorded at `client-r-c7` on `03c853307`, debug, 1280x720 windowed. Every frame
was drawn by stock (`cs_cl_render_plan 0`), through the PROBE COPY
(`tools/probes/client/viewpostprocess.cpp.probe.diff`, `probe_copies.cmake`),
driven by `tools/probes/sessions/c7-tonemap.steps`:

    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON && ninja -C farm/debug/build client_sourcesharp
    tools/probes/c0-session OUT tools/probes/sessions/c7-tonemap.steps --no-probe-build
    grep -E ' fx t[a-z0-9]*\t' OUT/probe.log | sed -E 's/^[0-9]+ [0-9]+ fx //'

The session runs on ss_sandbox, built from this commit's Sandbox.cs. dm_lockdown
has no HDR lighting lump, so the engine reports HDR_TYPE_NONE there and stock
never runs the tonemap. On ss_sandbox it is HDR_TYPE_INTEGER. The session:
- turns, looks up at the hall's skylight and down;
- goes under the mirror pool;
- then changes the cvars:
  - `mat_autoexposure_max 1.2`, `mat_tonemap_percent_target 45`;
  - `mat_bloomamount_rate 0.2`, `mat_bloomscale 0.5`;
  - `mat_hdr_uncapexposure 1`;
  - `mat_force_tonemap_scale 1.5`;
  - `mat_tonemap_algorithm 0` and back.

The recording has 1806 frames: 1683 on the new algorithm and 123 on the original
one. It has 1828 readbacks (some of them -1), 1766 averaged goals and 31 forced
resets.

As with motion blur, the probe build needed
`tools/probes/client/shell_probes_debug.cpp` left out locally, because it fails
to link on Steam symbols.

## Rows

Each frame starts at its `tb` row. Floats are `%.9g`, so they read back to
stock's bits.

| tag | what |
|---|---|
| `tr` | ResetToneMapping(value): at level init, before the first frame |
| `tb` | GetBloomAmount:<br>- DX level, HDR type, mat_hdr_level, MapHasHDRLighting, mat_force_bloom, mat_disable_bloom<br>- mat_fullbright<br>- the sRGB fallback that turns bloom off<br>- rate, mat_bloomscale, the map's custom scale (-1 when none), the non-HDR factor, the scalar<br>- the amount |
| `tx` | GetExposureRange:<br>- the map's custom min (on, value) and max (on, value)<br>- the cvars' min and max, mat_hdr_uncapexposure<br>- the range<br>- HDR type, flashlight |
| `tu` | CLuminanceHistogramSystem::Update: the query frame before the increment, mat_tonemap_algorithm |
| `trb` | a readback: the frame, the bucket, the pixels (-1: not finished) |
| `tiq` | IssueQuery:<br>- the frame, the bucket's state before<br>- its luminance range and its screen fractions<br>- the viewport, the flashlight<br>- the four centre-region cvars<br>- the rectangle drawn (x0, y0, x1, y1)<br>- $C0_Z, the bucket's pixel count |
| `tts` | GetTargetTonemapScalar on the new algorithm:<br>- ideal-for-debug<br>- bright-pixels percent, target percent, min average luminance<br>- the engine's last linear scale<br>- the target |
| `tts0` | the original algorithm's target |
| `tt` | DoPreBloomTonemapping: the target, its clamp, the exposure range, mat_dynamic_tonemapping, mat_show_histogram |
| `tsm` | SetToneMapScale: the new value, the range, mat_force_tonemap_scale, mat_fullbright, the samples held |
| `tsr` / `tsa` | its forced reset, or its averaged goal (neither: the goal was the new value alone) |

These paths are held by `TonemapTests`' synthetic facts, because no ss_sandbox
frame takes them:
- the mod fallback to the original algorithm;
- a map's exposure range;
- fullbright;
- a non-finite target;
- old hardware;
- LDR bloom.

The float-HDR branch of DoEnginePostProcessing (HDR_TYPE_FLOAT) never runs
under this engine's GL renderer, and is not ported here.
