# F02: the TF map nav spike (not for a lane)

Base: `dae055998`. Size S. Fit: **coordinator or a stronger model**, because
it runs map-tools' `ssmap` through `dotnet run --project` (not on the
lane launcher's allow-list), and its finding decides whether a map-tools
lane follows. Depends on nothing; **P6 and every TF2 nav stage depend on
it**.

## Goal

Answer, with numbers, whether `ssmap nav3d` builds a `.nav3d` from the
stock TF maps, with TF content mounted, within the format's limits.

## What to run

From the game checkout, with map-tools at `maptools.lock` (`38446d1`):

```
for m in ctf_2fort pl_badwater koth_viaduct cp_dustbowl; do
  /usr/bin/time -v tools/ai-nav-build-tf $m    # does not exist: see below
done
```

`tools/ai-nav-build` (`:84`) runs `ssmap nav3d <bsp> -out <nav> -game <mod>`
over `bin/<config>/mods/test/maps/<map>.bsp`. TF maps are at
`bin/debug/content/tf/maps/<map>.bsp` and the TF mod's game dir is
`game/mod_tf2` (its `gameinfo.txt:15-19` mounts `../../content/tf`, never a
Steam appid). So the spike is the same command by hand, once per map:

```
ssmap nav3d bin/debug/content/tf/maps/ctf_2fort.bsp -out .scratch/ctf_2fort.nav3d -game game/mod_tf2 -threads 8
ssmap nav3d -check ... (the same, with -check; it prints elapsed)
```

(`ssmap` = `dotnet run --project ../source-sdk-map-tools/src/SourceSharp.MapCompile -c Release --`.)

## What to record (a table in the report, one row per map)

| map | exit code | seconds, threads, box load | bytes, leaves | sha256 at 8 and at 1 thread (byte-identical?) | warnings (missing prop models: count) | `-check` missing floors / standable | refusal text if any |

Refusals the format has: the 255-voxel height (`LevelNavFromBsp.cs:229`,
"a .nav3d column holds at most 255: build it with a larger voxel"), "no
world model" (`:200`). A refusal on height is retried with `-voxel 24` and
`-voxel 32` and both recorded. BSP versions 19–21 are read; TF maps' LZMA
lumps are decompressed on load per `BspData.cs:23-27` (unverified for a TF
map before this spike: say whether the load succeeded).

## Done condition

A report file `docs/plans/tf2_ai/F02_report.md` with the table, the exact
commands, the map-tools commit and `nproc`/load. If any of the four fails,
the report names the failing step's message and the map-tools class (a
map-tools lane's brief starts from it). If all four build, P6's nav work
starts from these files' sizes (the derive cost scales with leaves: dm_lockdown
156,159 leaves derive in 207–305 ms, `plan_hl2mp_ai.md` §12.3).
