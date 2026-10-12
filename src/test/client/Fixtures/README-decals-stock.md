# `decals-stock.tsv`: provenance

Stock `game/shared/decals.cpp` (`CDecalEmitterSystem`), recorded live through
the `decalsystem` pointer every client caller uses, before lane c2-sims
replaced it. `ClientDecalsOracleTests` replays it against the managed port.

## How it was recorded

1. Base `cb18f0a8` (client-novalve), worktree `ss-client-c2-sims`, nothing else
   changed. Probe: `tools/probes/client/c2sims_probes_debug.cpp`,
   `cs_cl_probe_decals`, compiled into a debug `client.so` configured with
   `-DSS_CLIENT_PROBES=ON`.
2. `tools/probes/c0-session OUT tools/probes/sessions/c2sims-decals.steps
   --env SOURCESHARP_C2SIMS_FIXTURE_DIR=DIR`: `map dm_lockdown`, wait for the
   HUD's first paint, run the probe, quit.
3. The probe swaps the client's `random` (VEngineRandom001) for a forwarder
   that logs every `RandomFloat(min, max) -> value` while the picks run, then
   puts it back. No other draw kind was taken (console: `0 non-float draws`).

Console line of the run: `[c2-sims probe] decals: 40 groups, 217 entries,
210 picks, 0 non-float draws, max index 205`.

## Rows

| tag | columns | what |
|---|---|---|
| `S` | group, entry, weight | the script `scripts/decals_subrect.txt` as stock's KeyValues read it (`%.9g`) |
| `M` | material key, group | its `TranslationData` section (`-` for an empty value) |
| `P` | entry, index | `effects->Draw_DecalIndexFromName`, what stock's `LevelInitPreEntity` stores |
| `I` | group, answer, draws | `GetDecalIndexForName`, five per group plus an unknown and an empty name; draws are `min,max,value;...`, `-` for none |
| `N` | index, name | `GetDecalNameForIndex` for -2 .. max+2 (`-` for `""`) |
| `T` | name, material, answer | `TranslateDecalForGameMaterial` for five names over all 256 materials; `=` when the answer was the caller's own pointer, `-` for `""` |
