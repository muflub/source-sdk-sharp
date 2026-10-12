# `motionblur-stock.tsv`: where it came from

DoImageSpaceMotionBlur's decisions (`game/client/viewpostprocess.cpp`), recorded
off a live game before C7 step 2 moved them to C# (`MotionBlur.cs`). Recorded at
`client-r-c7` on `471678aeb`, debug, 1280x720 windowed, every frame drawn by
stock (`cs_cl_render_plan 0`), through the PROBE COPY
(`tools/probes/client/viewpostprocess.cpp.probe.diff`, `probe_copies.cmake`)
driven by `tools/probes/sessions/c7-motionblur.steps`:

    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON && ninja -C farm/debug/build client_sourcesharp
    tools/probes/c0-session OUT tools/probes/sessions/c7-motionblur.steps --no-probe-build
    grep ' fx mb' OUT/probe.log | sed -E 's/^[0-9]+ [0-9]+ fx mb\t//'

On dm_lockdown with `mat_motion_blur_enabled 1` and `cl_mouselook 0`: turning
and walking at the open floor, looking down and up while turning, jumping; then
the same with `mat_motion_blur_strength 2` and
`mat_motion_blur_forward_enabled 1`; then a teleport and a turn. 614 frames,
realtime 3.14 to 13.39 s; every one of the four values is non-zero in some
frames (346, 91, 5 and 192 of them).

The probe build needed `tools/probes/client/shell_probes_debug.cpp` left out of
`client_sourcesharp` locally: at `471678aeb` it fails to link on Steam symbols.

## Rows

One row per call, logged just before stock stores the frame for the next one,
so the values are what the MotionBlur proxy binds that frame. Floats are `%.9g`:
they read back to stock's bits.

| column | what |
|---|---|
| realtime | `gpGlobals->realtime` |
| origin | the blur view's origin |
| angles | the blur view's angles (unwrapped) |
| fov, aspect | `viewBlur.fov`, `viewBlur.m_flAspectRatio` |
| forward_enabled | `mat_motion_blur_forward_enabled` |
| falling_min, ... | `mat_motion_blur_falling_min`, `_falling_max`, `_falling_intensity`, `_rotation_intensity`, `_strength` |
| values | `g_vMotionBlurValues[0..3]` |

The rows start at the game's first frame, so the function's statics start at
zero, as `new MotionBlur()` does. The recording runs at a steady 60 fps: the
15 fps cut-off, the 30-50 fps fade and the teleport hold are held by
`MotionBlurTests`' synthetic facts instead.
