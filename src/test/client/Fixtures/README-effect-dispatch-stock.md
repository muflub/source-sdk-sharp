# `effect-dispatch-stock.tsv`: where it came from

Stock's client effect dispatch table, recorded off a live `dm_lockdown` before
lane c2-fx moved the table to C# (plan_client_novalve.md §2 cluster 4, §5.1).
The subject is `game/client/c_te_effect_dispatch.cpp`: the
`CClientEffectRegistration` list every `DECLARE_CLIENT_EFFECT` adds itself to at
static init, and `DispatchEffectToCallback`, which walks that list from its head
and runs the first registration whose name matches with `Q_stricmp`.

`ClientEffectDispatchTableTests` replays it: the managed table built from the
`R` rows must pick, for every `L` row, the registration stock picked.

Recorded at `client-c2-fx` base `cb18f0a8` (stock `c_te_effect_dispatch.cpp`
compiled, nothing of the port written), debug, with the lane's probe
`tools/probes/client/fx_probes_debug.cpp` (`-DSS_CLIENT_PROBES=ON`).

## The rows

| tag | what |
|---|---|
| `R` | one registration, in stock's walk order (`s_pHead` first): walk index, name |
| `L` | one lookup: the walk index stock's `DispatchEffectToCallback` ran, or -1 when it found none, then the name asked |
| `X` | a registration that ran for real during two `cs_te_fire all` volleys (the positive arm: the trampolines saw live traffic) |

The `L` names are every `DECLARE_CLIENT_EFFECT` name in the fork's `game/`
tree and in ours (82, registered in this client or not), each asked five ways:
as written, upper case, lower case, with an `x` appended, and with its last
character dropped; plus six more (`NoSuchEffect`, `impact`, `IMPACT`,
`Impact_`, `a`, `zz`). 397 lookups: 74 resolve, 323 do not.

The lookups do not run the effect. `cs_cl_probe_fxlookup` puts the probe's
per-registration trampolines in dry mode for the call, so the walk is stock's
own and nothing is drawn.

## How to record it again

    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON
    ninja -C farm/debug/build client_sourcesharp
    tools/box-ready gate --wait
    tools/probes/c0-session OUT tools/probes/sessions/fx-dispatch.steps --config debug
    cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=OFF && make debug

then turn `OUT/probe.log`'s `fx fxreg`, `fx fxlookup` and `fx fxrun` lines into
`R`, `L` and `X` rows (fields 5 onward, tab-separated). The steps file names
every lookup, so a re-recording asks the same 397 questions.
