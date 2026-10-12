# SourceSharp

SourceSharp runs a Source SDK 2013 multiplayer mod with its game logic in C#.
The engine loads a thin native server and client DLL. They host the .NET runtime
and hand almost everything to managed assemblies: entities, movement, physics
responses, temp entities, networking state. The goal is all-managed code, with
native C++ only where the engine demands it.

This repository holds only SourceSharp's own code. It builds against a checkout
of the Valve SDK fork, [`source-sdk-2013`](../source-sdk-2013), which keeps
Valve's files, SourceSharp's edits to them, and Valve code that was relocated but
not yet ported.

> **Not yet public: the build needs a Valve SDK checkout.** The fork and
> tf2_descent's host SDK (`source-sdk-sharp-host`) are not published yet. Use
> the `make sdk-upstream` route (see "Two ways to get ROOT"): it clones Valve's
> public [source-sdk-2013](https://github.com/ValveSoftware/source-sdk-2013)
> and applies the patch series in `patches/source-sdk-2013/`. The tf2_descent
> mod cannot be built without the host SDK.

## Prerequisites

- **The fork**: a checkout of `source-sdk-2013` on its working branch,
  beside this one (`../source-sdk-2013`).
- **Steam**, with **Source SDK Base 2013 Multiplayer** (app 243750) installed.
  The engine runs from there, and the mod takes the HL2 / HL2:DM content from it.
- **.NET 10 SDK** at `~/.dotnet`. Do not use Ubuntu's `/usr/bin/dotnet`: its
  runtime aborts under concurrent tiered compilation.
- **cmake** (3.25+), **ninja**, **clang 22** and **lld 22** (`clang++-22`,
  `ld.lld-22`) for the native build, which runs on the host; and the **zlib**
  headers for the NativeAOT launcher.
- Only for `make ci-build` (CI's build, reproduced): **podman** with Valve's
  sniper SDK image (`registry.gitlab.steamos.cloud/steamrt/sniper/sdk`). If
  podman breaks after a VS Code snap update, run it with
  `XDG_DATA_HOME=$HOME/.local/share`.

`tools/setup --check` reports anything it can see is missing.

## Building and running

```bash
make setup ROOT=../source-sdk-2013   # once, and again after the fork moves
make debug                           # build everything, debug
make release                         # build everything, split debug info
make ci-build                        # CI's native build in the sniper container
make test                            # every test leg, reported separately
make check                           # debug + test: what to run before a commit
make run                             # build debug, launch, live-reload the web UI
make live-gate                       # load a map twice in one process, gate both
make run-release                     # the release build
make help                            # the rest
```

`CONFIG=release` selects the release build for `run`, `live-gate`, `test` and the
gates. Maps: `make sandbox` regenerates `game/mod_test/maps/ss_sandbox.vmf`, and
`make map MAP=ss_sandbox` compiles it with the map tools from
[`source-sdk-map-tools`](../source-sdk-map-tools) (override with `MAPTOOLS_DIR=`).

`make test` runs six legs: `runner` (the standalone checkers and gates),
`managed` (xUnit), `native` (gtest), `oracle` (ports checked against the
pristine upstream Valve SDK, see below), `entity` (the entity census and
ratchet) and `ui` (the web UI's vitest suite).

## Layout

```
Makefile  SourceSharp.slnx  Directory.Build.props
sdk-root.lock      the fork commit this repo was last verified against
patches/source-sdk-2013/  the fork's edits as a patch series on stock upstream (make sdk-upstream)
valve-sdk.lock     the upstream Valve commit the oracle tests read (see below)
sdk-links/         the fork files the farm still links in, by consumer (see below)
src/
  abi/             SourceSharp.Abi, the managed base assembly
  client/  server/  shared/  host/  test/
                   managed/ (C#) and native/ (C++); native/sdk/ mirrors Valve
                   paths for our files that must sit inside Valve directories
  launcher/        source#, the launcher (C# NativeAOT)
  mods/            the mods.yaml model and parser
  debug_api/ debug_api_client/ debug_api_shared/  the debug API (Debug.Api.*)
  generators/ mapgen/ webui/
game/
  mods.yaml        the mods the launcher can start
  mod_test/        the test mod's content (gameinfo, scripts, maps, ...)
tools/             setup, the build scripts (tools/build/), the gates, debug-api
docs/              design notes and ledgers; docs/plans/ holds the plans
bin/<config>/      gitignored: the game directory (below)
farm/<config>/     gitignored: the compat farm (below)
```

### The game directory

`bin/debug/` and `bin/release/` are each a complete game directory:

```
bin/<config>/
  source#            the launcher
  mods.yaml          -> game/mods.yaml
  bin/linux64/       server.so client.so libmanaged_host.so, libsteam_api.so
  bin/managed/       the managed assemblies
  bin/dotnet/        the bundled .NET runtime
  mods/test/         the test mod: its content linked in, bin -> ../../bin,
                     and everything the game writes (console.log, cfg/, save/)
```

The engine rewrites `cfg/`, so it is never linked: `make setup` copies the
content's `cfg-seed/` files (`game/mod_test/cfg-seed/settings.scr`) into
`mods/<id>/cfg/` when they are absent, and never over a file the game wrote.

`source#` finds the engine through Steam and starts it on the mod `mods.yaml`
names (`-mod <id>` picks another). It also does what Valve's `hl2.sh` used to
do: sets the library path, the file limit and the environment, and restarts the
game when it exits with code 42. Bootstrap loads the assemblies the mod's entry
lists.

### The farm, and why the link count matters

The native build and many tests were written against the old single-repo layout,
where SourceSharp's files sat inside Valve's tree. `make setup` rebuilds that
layout under `farm/<config>/` as a tree of links:

- every Valve file the build needs is linked in from the fork, one link per
  file, as listed in `sdk-links/*.txt`;
- every SourceSharp file is linked in from this repo, at its old path, per
  `tools/module-map.tsv`.

The build and the tests run inside the farm unchanged. The farm only ever reads
the fork, and after any build or test run the fork's working tree is clean.
`tools/setup` enforces the rest of the rules (listed in its header), and its
`--self-test` shows each rule refusing a bad farm.

`sdk-links/` is the list of Valve code SourceSharp still depends on, split by
who uses each file: `client.txt` (only client.so's build), `server.txt` (only
server.so's), `shared.txt` (both), `libs.txt` (only the Valve static
libraries) and `tests.txt` (only test targets, or a traced test run). Porting a
Valve file to C#, or replacing it, ends with deleting its line, so the counts
at the top of each list are the progress metric. `make sdk-census` fails on a
line nothing uses or a line in the wrong list; `tools/sdk-census --write`
regenerates the lists. Nothing under `src/sourcesharp/` is ever linked from the
fork: that tree is this repo's (`tools/module-map.tsv`).

Tests that check a port against the Valve code it replaced (oracles) do not read
the farm: they live in `src/test/oracle/` and read a pristine clone of upstream
`source-sdk-2013` at the commit in `valve-sdk.lock`, which `make setup`
provisions at `../source-sdk-2013-valve` (`VALVE_SDK=` overrides). `make test`
runs them as the `oracle` leg; a missing clone fails them.

## Two ways to get ROOT

ROOT is a `source-sdk-2013` tree with our edits to Valve files. There are two:

```bash
make setup ROOT=../source-sdk-2013      # the fork (sdk-root.lock pins its commit)
make sdk-upstream [SDK_DIR=../source-sdk-2013-upstream]   # stock upstream + our patches
```

`make sdk-upstream` clones Valve's repository fresh into `SDK_DIR` (or reuses a
clean clone it made itself, marked `sourcesharp.sdkupstream` in its config and
sitting at the pinned base or result; anything else, including the fork and
the pristine oracle clone, is refused untouched), checks out the upstream
commit pinned in `patches/source-sdk-2013/UPSTREAM`, applies the series in that
directory with `git am`, and runs `make setup ROOT=$(SDK_DIR)`. The series holds
only the fork's edits to the files the build links (`sdk-links/*.txt`), without
comments. `git am` runs with a fixed committer, the patches' own dates and
fixed settings, so it makes the same commit on every machine: `UPSTREAM`'s
`result=`. `tools/setup` accepts that commit as ROOT with no flag and leaves
`sdk-root.lock` alone, so both ROOTs work side by side. A run that lands on any
other sha fails: after regenerating the series on purpose, re-pin it with
`tools/sdk-upstream --write-result DIR` and commit `UPSTREAM` with the series.

## When the fork moves

Edits to Valve files, and to relocated Valve code, are made in the fork. Then:

```bash
make setup SETUP_ARGS=--accept-root     # or: tools/setup --accept-root
```

`tools/setup` refuses a fork whose `HEAD` is not the commit in `sdk-root.lock`.
`--accept-root` re-pins the lock when the new `HEAD` descends from it. Commit
the new `sdk-root.lock` together with the change that needed it.
`--repin-root` pins an unrelated tree instead (one that does not descend from
the lock, such as a different patched upstream), as long as it holds the Valve
base in `valve-sdk.lock`; its report says it switched trees. `--accept-root`
never does, so a ROOT moved backwards or sideways by accident is still refused.

## Valve attribution

Files that port or translate Valve logic, or native code built against the SDK
headers, start with the attribution header in `tools/valve-header.txt`. The
per-file rulings live in `docs/move/header-rulings.tsv`, and the move ledger that
classified every file is `docs/move/ledger.tsv`. `make test` checks the headers
(`tools/valve-header check`) and that no Valve text, copyright or asset is in the
repo (`tools/valve-text-audit`). The named exceptions are `game/mod_test/`,
which is copied whole so the mod can be tested standalone, and Valve's own
`LICENSE-VALVE-SDK` and `thirdpartylegalnotices.txt` (see "License").

## License

SourceSharp's own code is licensed under the MIT license (`LICENSE`).

Files that carry the Valve attribution header (`tools/valve-header.txt`), and
everything under `game/mod_test/`, are derived from Valve's Source SDK and are
distributed under the Source 1 SDK License (`LICENSE-VALVE-SDK`): free of
charge, together with `thirdpartylegalnotices.txt` and Valve's copyright notice.
Both files are Valve's own, copied unchanged from the Source SDK 2013 release
this repo is audited against (`valve-sdk.lock`). The probe patches,
`tools/probes/**/*.probe.diff`, quote Valve SDK code in their context and
removed lines and are likewise distributed under the Source 1 SDK License.
