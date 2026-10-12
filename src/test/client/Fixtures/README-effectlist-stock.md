# `effectlist-stock.tsv.gz`: where it came from

Stock's client-side effect list and the effects on it, recorded off a live
`dm_lockdown` before lane c2-fx moved them to C# (plan_client_novalve.md §2
cluster 3, §5.1). The subjects, each compiled as a PROBE COPY (the stock file
with log lines added, nothing else changed):

| stock TU | what it records |
|---|---|
| `clientsideeffects.cpp` | `CEffectsList`: every add (with the count before it), every frame's draw (frametime, count), each effect's `IsActive` after its draw, every removal (the index and count, which show the swap-with-last order), the flush |
| `fx_line.cpp`, `fx_quad.cpp`, `fx_staticline.cpp`, `fx_discreetline.cpp` | each `CFX*` effect's creation parameters, and on every `Draw` its inputs (frametime; the view origin, view forward, screen width and `tracer_extra` where it reads them) and the mesh it built |
| `clientsideeffects_test.cpp` | `FX_Tracer`, `FX_PlayerTracer` and `FX_TracerSound`: their inputs, every random draw, the whiz decision (listener, distance, timer) and the sound emitted |
| `fx_tracer.cpp` | the `Tracer`, `ParticleTracer` and `TracerSound` callbacks' entry state |

The meshes are logged by `tools/probes/client/fx_probe_mesh.h`, a
`CMeshBuilder` that logs each vertex as it is handed over and forwards
unchanged. The random draws come from the engine's stream through
`FxProbeRandomFloat` (`tools/probes/client/fx_probes_debug.cpp`).

`ClientEffectListTests` replays it.

Recorded at `client-c2-fx` `72c32d2c` (rebased since as `1d66f855`) with this commit's probe files, debug, by
`tools/probes/sessions/fx-effectlist.steps`: five batteries of effects in front
of the camera (`cs_cl_probe_fxbattery`: four lines, eight quads covering every
flag combination and a zero-alpha one, four static lines with each flip, four
discreet lines clipped and not, four tracers, three player tracers; the fourth
with `tracer_extra 0`), `FX_TracerSound` for each tracer type at four distances,
the three tracer callbacks with `r_drawtracers` 1 and 0, and two
`cs_te_fire all` volleys.

## The rows (tab-separated, in event order)

| tag | what |
|---|---|
| `fxb-begin`, `fxb-end` | a battery's bounds and the view it was placed from |
| `fxe-new` | an effect created: kind, its pointer (the effect's identity in this file), name, material, parameters |
| `fxl-add` | the list took it: pointer, name, the count before |
| `fxl-draw` | a frame's `DrawEffects`: frametime (`%.17g`, a double), count |
| `fxe-draw` | one effect's `Draw` on that frame, with the inputs it read |
| `fxm-begin`, `fxm-v`, `fxm-end` | the mesh that draw built (kept on the first three draws of each effect, every tenth after, and the last) |
| `fxl-drawn` | the effect's `IsActive()` after that draw |
| `fxl-remove` | a removal: pointer, index, count before |
| `fxl-flush` | the list flushed |
| `fxr` | a random draw: `f` (float) or `i` (int), the range, the value |
| `fxt-*` | the tracer helpers' and callbacks' inputs and decisions |

## What it shows that is not obvious

`fxt-cb` reads `player=0` on every callback: `C_BasePlayer::GetLocalPlayer()`
answers NULL in this mod (the viewer is a `C_StandalonePlayer`, see
`local_recipient.h`), so stock's `Tracer` and `ParticleTracer` callbacks return
before drawing anything. That is the dead stock arm the port restores (memory:
port dead stock arms): the managed callbacks take the local player from the
engine's slot, and their draw then goes through the `FX_Tracer` and
`FX_PlayerTracer` recorded here directly.

## How to record it again

    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON
    ninja -C farm/debug/build client_sourcesharp
    tools/box-ready gate --wait
    tools/probes/c0-session OUT tools/probes/sessions/fx-effectlist.steps --config debug
    tools/probes/fx-select OUT/probe.log effectlist-stock.tsv --prefix fxl --prefix fxe \
        --prefix fxm --prefix fxr --prefix fxt --prefix fxb --mesh-every 10 --title "..."
    gzip -9 -n effectlist-stock.tsv
    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=OFF && make debug

The probe copies need the stock TUs in the farm: the recipe works until the
commit that evicts them.
