# `ieffects-stock.tsv`: where it came from

Stock's client `IEffects`, `CEffectsClient` in `game/client/EffectsClient.cpp`,
recorded off a live `dm_lockdown` before lane c2-fx made `IEffects` a thunk onto
managed decisions (plan_client_novalve.md §2 cluster 3, §5.1).
`ClientEffectsTests` replays it: for every recorded call, the managed decision
must suppress exactly the calls stock suppressed and make exactly the `FX_*`
call stock made, with the same arguments, given the same suppression state and
the same random draw.

Recorded at `client-c2-fx` `c221416d` (rebased since as `faac6d47`) with this commit's probe files (stock `EffectsClient.cpp` compiled,
nothing of its port written), debug, with the lane's PROBE COPY of the file:
`tools/probes/client/EffectsClient.cpp.probe.diff`, applied by
`tools/probes/client/probe_copies.cmake` in a `-DSS_CLIENT_PROBES=ON` build, adds
one log line per call, one at the suppression test, one per random draw and one
per `FX_*` call, and changes nothing else. The probe commands are in
`tools/probes/client/fx_probes_debug.cpp`.

## The rows (tab-separated, in call order)

| tag | what |
|---|---|
| `call` | an `IEffects` method entered: its name and arguments (vectors as three `%.9g` floats; Sparks' direction as a present flag and three floats) |
| `suppress` | stock's `SuppressTE`: `CanPredict()`, the PVS filter's recipient count, and the ricochet-sound flag at that moment |
| `draw` | a random draw the method made (`RandomInt 20 35 <value>`, Smoke's grey) |
| `fx` | the `FX_*` routine stock called and its arguments, or `Msg-no-case <type>` for a muzzle-flash type the switch does not know |
| `arm` | a probe command that set an arm a session does not reach alone (`suppress-events 1/0`) |

Where it came from: 30 `cs_te_fire all` volleys (the networked temp-entity
players reaching `g_pEffects`), the `cs_cl_te_backend` scaffolding, each
muzzle-flash type (0, 1, 2, 3, 4, 7, -1) through `cs_cl_probe_fxmuzzle`, the
same calls under `IPredictionSystem::SuppressEvents(true)` (`CanPredict()`
false, as in a re-prediction), and ricochets with `SuppressEffectsSounds(true)`.
205 calls: 196 drew, 9 were suppressed by `CanPredict()`.

Not recorded: a recipient count of 0 (no local player slot yet). Every call in
a joined session has one; the managed rule for it is stock's `!GetRecipientCount()`
read from the source, not from this file.

## How to record it again

    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON
    ninja -C farm/debug/build client_sourcesharp
    tools/box-ready gate --wait
    tools/probes/c0-session OUT tools/probes/sessions/fx-ieffects.steps --config debug
    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=OFF && make debug

then keep `OUT/probe.log`'s `fx ie-*` lines from field 4 on, drop the `ie-`
prefix and turn spaces into tabs. The probe copy needs the stock
`EffectsClient.cpp` in the farm, so this recipe works at or before `faac6d47`'s
successor that evicts it.
