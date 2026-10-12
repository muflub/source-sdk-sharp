# `shelleject-stock.tsv`: where it came from

Stock's three shell-eject effects, `ShellEject`, `RifleShellEject` and
`ShotgunShellEject` (`game/client/fx_shelleject.cpp`), recorded off a live
`dm_lockdown` before lane c2-fx moved them to C# (plan_client_novalve.md §2
cluster 3). `ClientShellEjectTests` replays it.

Recorded at `client-c2-fx` `8ca6aad3` with this commit's probe files, debug,
through the lane's PROBE COPY (`tools/probes/client/fx_shelleject.cpp.probe.diff`,
`probe_copies.cmake`) driven by `tools/probes/sessions/fx-shelleject.steps`:
`cs_cl_probe_fxshells` dispatches each name for four owners (the local
player, the world, an empty slot, none), eighteen times.

## The rows

| tag | what |
|---|---|
| `fxs-call` | a callback entered: the owner's entity index (`entindex()`, -1 for none), the origin, the angles, whether the owner has a renderable, and its render angles |
| `fxs-eject` | the brass type `tempents->EjectBrass` was called with (0 pistol, 1 rifle, 2 shotgun); absent when stock ejected nothing |

216 calls, 108 ejects.

## How to record it again

As `README-ieffects-stock.md`, with `fx-shelleject.steps`, then
`tools/probes/fx-select OUT/probe.log shelleject-stock.tsv --prefix fxs --title "..."`.
The probe copy needs `fx_shelleject.cpp` in the farm: the recipe works until
the commit that evicts it.
