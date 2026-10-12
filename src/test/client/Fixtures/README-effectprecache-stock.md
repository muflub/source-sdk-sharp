# `effectprecache-stock.tsv`: where it came from

Stock's client effect precache system (`game/client/clienteffectprecachesystem.cpp`,
`CClientEffectPrecacheSystem`), recorded off a live game before lane c2-fx
moved its level-time decisions to C# (plan_client_novalve.md §2 cluster 3,
"precache lists"). `ClientEffectPrecacheTests` replays it.

Recorded at `client-c2-fx` `8f1d1b1e`, debug, through the lane's PROBE COPY
(`tools/probes/client/clienteffectprecachesystem.cpp.probe.diff`,
`probe_copies.cmake`) driven by `tools/probes/sessions/fx-precache.steps`: six
map loads alternating `dm_lockdown` and `ss_sandbox`, then quit.

## The rows

| tag | what |
|---|---|
| `pc-reg` | a `CLIENTEFFECT_REGISTER` object registered at static init, by index (17) |
| `pc-ev` | a system event and the registry size: `init` (LevelInitPreEntity), `shutdownpre`, `shutdownpost`, `shutdown` |
| `pc-cache` | `IClientEffect::Cache( precache )` on registration index i (1 precache, 0 release) |
| `pc-used` | `materials->CacheUsedMaterials()` |
| `pc-handles` | `FX_CacheMaterialHandles()` |

252 rows: 17 registrations, 6 level starts and ends, 204 cache calls.

## How to record it again

As `README-ieffects-stock.md`, with `fx-precache.steps`, then
`tools/probes/fx-select OUT/probe.log effectprecache-stock.tsv --prefix pc --title "..."`.
The probe copy needs `clienteffectprecachesystem.cpp` in the farm: the recipe
works until the commit that evicts it.
