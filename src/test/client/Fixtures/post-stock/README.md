# `post-stock/*.txt.gz`: where they came from

Stock's post-processing, one frame each. They were recorded for C7 step 3
before viewrender.cpp and view.cpp left client.so. The user ruled on
2026-10-12: "freeze recordings". No stock path and no stock build option
remain, so these files and `../render-stock/` are the reference for good.
They feed the post-process port's ops and its managed "engine_post" and
"MotionBlur" proxies.

Recorded at `client-r-c7` on `0945d192a` plus the commit that lands them. The
build was debug, 1280x720 windowed, with every frame drawn by stock
(`cs_cl_render_plan 0`). The session was `tools/probes/sessions/c7-post.steps`:

    tools/probes/c0-session OUT tools/probes/sessions/c7-post.steps --no-probe-build
    for f in OUT/post-*.txt; do gzip -9nc $f > src/test/client/Fixtures/post-stock/$(basename $f).gz; done

Each file is a render trace, in `../render-stock/README.md`'s format. It also
carries:
- **`cvar` lines:** every convar's value at the frame's start, by name.
- **`env` lines:** the engine's answers by name:
  - realtime, curtime, frametime;
  - HDR type, DX levels, the map's HDR lighting;
  - the sRGB and ps2.0 caps;
  - the linear tonemap scale;
  - the viewport, the video mode height and the game directory;
  - the map's tonemap overrides.
- **`q material.var` lines:** after every screen-space draw, each shader
  parameter of its material as bound. The type comes first: `f` float, `i`
  int, `vN` vector, `s` text.

Because the inputs are keyed by name, a port that later reads a new input still
finds it here. A struct's bytes would not survive the change.

## What the frames show

- **ss_sandbox has HDR lighting and runs as HDR_TYPE_INTEGER.** Each frame
  does the following:
  - draws the histogram's dev/lumcompare query;
  - sets the tonemap goal;
  - draws the bloom chain: dev/downsample_non_hdr into _rt_SmallFB0, then
    dev/blurfilterx_nohdr and dev/blurfiltery_nohdr;
  - adds the result through dev/bloomadd. This is the engine-post fallback,
    because this renderer has no engine_post shader, so dev/engine_post is
    never drawn.
- **dm_lockdown has no HDR lighting and runs as HDR_TYPE_NONE.** Its bloom
  amount is 0, so it draws no post pass unless bloom is forced.
- **The recorded config has `mat_colorcorrection 0`**, so the `nocolorcorrection`
  frame matches the default one. It also has `mat_queue_mode -1`, under which
  stock refuses `mat_dump_rts`. `PostProcessPlannerTests` covers both arms with
  synthetic inputs.
- **Software AA is X360-only in stock.** It changes nothing here.
- **Pyro vision draws `___error`,** because dev/pyro_post is missing from this
  game, and draws random sides.

| file | the setting changed from default |
|---|---|
| `post-sandbox-default` | none |
| `post-sandbox-debug-quadrants` | `mat_debug_postprocessing_effects 1` |
| `post-sandbox-debug-centre` | `mat_debug_postprocessing_effects 2` |
| `post-sandbox-halfscreen` | `mat_debug_process_halfscreen 1` |
| `post-sandbox-nocombine` | `mat_postprocessing_combine 0` |
| `post-sandbox-softwareaa` | `mat_software_aa_strength 1` |
| `post-sandbox-softwareaa-quality` | that and `mat_software_aa_quality 1` |
| `post-sandbox-histogram` | `mat_show_histogram 1` |
| `post-sandbox-debugbloom` | `mat_debug_bloom 1` |
| `post-sandbox-nobloom` | `mat_disable_bloom 1` |
| `post-sandbox-nocolorcorrection` | `mat_colorcorrection 0` |
| `post-sandbox-forcetonemap` | `mat_force_tonemap_scale 1.5` |
| `post-sandbox-pyro` | `localplayer_visionflags 1` |
| `post-sandbox-fullbright` | `mat_fullbright 1` |
| `post-sandbox-motionblur` | `mat_motion_blur_enabled 1`, turning (`cl_mouselook 0`, `+right`) |
| `post-lockdown-default` | none (at the openFloor pose: (-3843, 2894, 0), angles (25, 90, 0)) |
| `post-lockdown-softwareaa` | `mat_software_aa_strength 1` |
| `post-lockdown-debug-quadrants` | `mat_debug_postprocessing_effects 1` |
| `post-lockdown-forcebloom` | `mat_force_bloom 1` |
| `post-lockdown-motionblur` | `mat_motion_blur_enabled 1`, turning |

The auto exposure's frame-by-frame state is held by `../tonemap-stock.tsv.gz`,
and motion blur's by `../motionblur-stock.tsv`.
