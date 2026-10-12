# plan_sharp_repo.md: move SourceSharp into ../source-sdk-sharp

## Goal

Move every SourceSharp-authored file into a new repo at `../source-sdk-sharp`.

- The new repo tracks no Valve code or assets.
- It builds against a **root dir**: this repo, `source-sdk-2013`, on its current branch. That branch becomes the **Valve fork**. It keeps Valve's files, our edits to them, and the Valve code that was moved into `src/sourcesharp`.
- `make setup ROOT=<dir>` builds a symlink farm, a gitignored tree that reproduces today's checkout layout. The build and the tests run inside it unchanged.
- The farm's link manifest records how much of Valve's tree we still depend on. It shrinks as more code is ported to managed or shimmed, and when a Valve file is fully ported, its link is deleted.

Git history is not carried over. What counts is a clean, working build of the new repo with no Valve source in it.

## Decisions (asked and answered 2026-09-27)

| Question | Decision |
|---|---|
| How are the ~513 edited Valve files and ~21 relocated Valve files reproduced? | **Valve fork as root dir.** The edits stay in `source-sdk-2013`, and the new repo symlinks them in. No patches. |
| What is the root dir? | **This repo's current branch** (the fork's working branch once Phase 0 lands). It is pinned by commit in `sdk-root.lock`. |
| Outstanding work | Merge the plan_shim integration branch (8b-globals, 8c-ruleslayer) and lanes 9b-playerstate, 9c-worldstate, 9d-clientglobals. Lane 9f-managedvtable (1 of 7 done) becomes a backlog note. Every other branch is dropped: landed, superseded, or moved to map-tools. |
| `game/mod_sharp` | **Copied whole** into the new repo, Valve copies included, for easy testing, and stored in the repo as **`game/mod_test/`**. Only that source folder is renamed; file names and code references are unchanged. Setup links its content directly into `bin/<config>/`, which is the mod directory (see "Output layout"). It is the one exemption from the no-Valve-content audit. |
| Launcher and output | The launcher becomes a **C# NativeAOT project** named **`source#`** that reads **`mods.yaml`** (per-mod settings: assemblies to load, game dir, app ids, args, env). `bin/<config>/` **is** the game directory: no `mod_*` folder in the output. See "Output layout" and Phase 6a. |
| `src/` layout | One directory per module. A module with both managed and native code has `managed/` and `native/`; a module with one kind is flat. `abi` is SourceSharp.Abi. See "Module layout". |
| Native build | After Phase 4 proves parity through VPC, **CMake + Ninja** replaces VPC (Phase 6b). **Local** builds run on the host with clang 22 and lld, with no container and no concern for glibc age. **CI/CD** builds run the same CMake project **inside Valve's sniper SDK container** (`registry.gitlab.steamos.cloud/steamrt/sniper/sdk`), which provides the old-glibc floor. There is no sysroot. |
| Server plugin | `serverplugin_empty` is **dropped from the new repo entirely**. It is never built, from Phase 4 on. |
| Native tests | They are already gtest (294 of the 295 `.cpp` files in `src/sourcesharp/test` use `TEST`/`TEST_F`; `fake_engine_main.cpp` is a helper). The port is the **harness**: `buildcpptests` (5,326 lines of bash) becomes CMake targets plus `ctest`, and Valve's bundled gtest becomes a pinned upstream googletest (Phase 6b). |
| Valve header | Every new-repo file with Valve source or Valve-inspired source carries the attribution header (text in Phase 1), applied **before the first commit**. The scope is rule-based, decided per file, with the list reviewed by you before check-in. |
| Maps | **Split.** `make sandbox` (SourceSharp.MapGen) stays here. `make map` calls `../source-sdk-map-tools` through `MAPTOOLS_DIR`. The `hammer`, `map-tools` and `toolgame` targets, and `tools/{maptools,mapgame,steamcmd}`, are not moved. |

## Measured starting point (2026-09-27, tip `02851a196`, merge-base with Valve master `22288b919`)

| Fact | Command |
|---|---|
| 7,365 tracked files under `src/` excluding `src/sourcesharp`, and 1,902 under `src/sourcesharp` | `git ls-files src ':!src/sourcesharp' \| wc -l`; `git ls-files src/sourcesharp \| wc -l` |
| Valve files: 608 modified, 64 added, 65 deleted | `git diff --name-status 22288b919 HEAD -- src ':!src/sourcesharp'` |
| Of the 608 modified: 21 are deletion-only, 74 add only guards or comments, ~513 add real code (25,312 added lines) | `numstat` bucket script (scratchpad) |
| 36 files in `src/sourcesharp` carry a Valve copyright; about 21 are relocated Valve files (e.g. `physics.cpp` 4,310 lines, `bone_setup.cpp`, `collisionproperty.cpp`), and 6 are `shell_*_sim.cpp` splits | survey agent |
| `game/mod_sharp`: 43 tracked files, 39 of them verbatim `mod_hl2mp` copies. Only `resource/mod_sharp_english.txt` is ours. | blob compare against `master:game/mod_hl2mp` |
| `tools/`: 630 MB, of which 613 MB is untracked downloads (`node`, `.npm-cache`, `steamcmd`) | `du -sh tools/*` |
| Managed solution: `src/sourcesharp/managed/SourceSharp.slnx`, 9 csproj files | `find -name '*.csproj'` |
| 34 C# root-finder helpers; the root marker is `src/buildsourcesharp` in 21 of them | survey agent |
| Native tests assume cwd = `src/`: 1,292 `"game/…"` literals, 992 `"sourcesharp/…"` literals; `../tools/.cpptest` is their output | survey agent |
| Container mounts only the repo root, at `/my_mod`; `PROJECTDIR=/my_mod/src/<proj>` is embedded in ninja | `src/sdk_container:38-41` |

Phase 1 re-measures every number above at the frozen tip before anything uses it (memory `a-brief-scoped-from-a-handover-goes-stale`).

## The one idea that keeps this safe: the compat farm

The new repo's physical layout is new: `src/`, `game/`, `bin/`, `tools/`, `docs/`. The build does **not** see that layout. `make setup` composes `.sdk/<config>/`, a tree of real directories and symlinks that looks exactly like today's checkout:

```
.sdk/debug/                        (gitignored; one farm per configuration)
  src/game/server/baseentity.cpp   -> <ROOT>/src/game/server/baseentity.cpp    (Valve / fork, per-file link)
  src/game/server/server_csharp.vpc-> <REPO>/src/server/native/sdk/game/server/server_csharp.vpc
  src/sourcesharp/managed/SourceSharp.Server -> <REPO>/src/server/managed      (ours, dir link)
  src/sourcesharp/managed/SourceSharp.Abi    -> <REPO>/src/abi
  src/sourcesharp/game/server/entity_host.cpp -> <REPO>/src/server/native/entity_host.cpp
  src/sourcesharp/game/server/physics.cpp -> <ROOT>/src/sourcesharp/game/server/physics.cpp  (relocated Valve, per-file)
  src/buildsourcesharp             -> <REPO>/tools/build/buildsourcesharp
  src/lib/public/linux64/tier1.a   (real file: built output, never linked)
  tools -> <REPO>/tools    docs -> <REPO>/docs
  game/                            (real dir: the compat view of the old game/ layout)
    mod_sharp -> <REPO>/bin/debug    so every old `game/mod_sharp/...` path, OUTBINDIR included, lands flat in bin/debug
```

The same VPC scripts, ninja graph, relative paths, cwd assumptions and test literals therefore all work on day one. Moving the tests onto the new layout is optional follow-up work and is not needed for a green build.

The farm follows these rules. Phase 3 has a test for each one.
1. **Valve and fork files are linked per file, never per directory.** So the manifest line count is the dependency metric. VPC's `*.vpc_cache`, ninja and the atomic-deploy `mv` all write into real farm directories, never into the fork.
2. **Our own directories may be directory links.** Writes into them land in our repo; gitignore the by-products (`*.vpc_cache`).
3. **Build outputs are never listed in the manifest.** `tier1.a`, `mathlib.a`, `raytrace.a`, `vgui_controls.a`, `matsys_controls.a`, the `.so` files and the launcher are always real files. Today the build overwrites Valve's `src/lib/public/linux64/*.a`; through a link it would write into the fork.
4. **A path is never supplied by both repos.** Setup exits non-zero if one is.
5. **The manifest never names a path the ledger classes as OURS or MOD-COPY.** Otherwise the new repo could silently build from the fork's stale copy.
6. **After any build or test run, `git -C $ROOT status --porcelain` is empty.** This is the gate that proves the fork is read-only.

## New repo layout

```
source-sdk-sharp/
  Makefile                  build/test/run front (ported from today's Makefile)
  SourceSharp.slnx          moved up from src/sourcesharp/managed; project paths src/<module>/managed/...
  Directory.Build.props     (and the other files at the root of src/sourcesharp/managed: .editorconfig, props, targets) moved to the repo root
  CLAUDE.md  README.md  .gitignore
  sdk-root.lock             fork commit this repo was last verified against
  sdk-links.txt             manifest: Valve/fork paths linked into the farm (the shrinking list)
  src/                      one directory per module; see "Module layout" below
    abi/                    SourceSharp.Abi, the managed base assembly           (flat)
    client/  managed/ native/
    server/  managed/ native/
    shared/  managed/ native/
    host/    managed/ native/   (managed = SourceSharp.Bootstrap)
    test/    managed/ native/
    launcher/               SourceSharp.Launcher, C# NativeAOT, publishes source#   (flat)
    mods/                   SourceSharp.Mods: mods.yaml model and parser, used by launcher + Bootstrap (flat)
    devapi/  generators/  mapgen/                                                 (flat)
    webui/                  TypeScript UI                                         (flat)
  tools/build/              build scripts the farm places at src/: buildsourcesharp, buildcpptests, buildonecpptest,
                            cpptest_one, build_check, preprocess_target, sdk_container_sharp; plus the devtools/vpc_*
                            gates and vpc_scripts/*.watermark (placed at their devtools/ and vpc_scripts/ paths)
  game/mod_test/            the repo copy of game/mod_sharp (folder renamed only; its content is linked flat into bin/<config>/) COPIED whole: all 43 tracked files, including the 39 verbatim mod_hl2mp copies and
                            dm_lockdown.bsp, so the mod can be tested standalone; plus maps/ss_sandbox.vmf source
  tools/                    kept tools (list in Phase 2), setup, devapi, devapi-proxy
  docs/                     docs/*.md + docs/plans/ (every plan*.md, tracked and untracked)
  bin/debug  bin/release    gitignored: each IS the game directory (launcher source#, gameinfo.txt, bin/linux64, bin/managed); no mod_* folder
  .sdk/                     gitignored: the farms
```

### Output layout

```
bin/debug/                      (bin/release/ has the same shape)
  source#                       the launcher: NativeAOT build of src/launcher (Phase 6a)
  mods.yaml                     linked from game/mods.yaml: which mods this launcher can start
  bin/linux64/                  real dir, SHARED by every mod: server.so client.so libmanaged_host.so
                                (built) + libsteam_api.so (link to the fork's game/bin/linux64)
  bin/managed/                  managed build output (shared)
  bin/dotnet/                   bundled runtime, provisioned by setup (shared)
  mods/                         one directory per mod, named by its mods.yaml id
   test/                        the test mod
     gameinfo.txt steam.inf     linked from game/mod_test/
     scripts/ shaders/ resource/ maps/  linked from game/mod_test/ (maps/ also takes compiled sandbox .bsp output)
     bin -> ../../bin           symlink made by setup: the engine's gamebin and the host's "<gamedir>/bin" both resolve here
     cfg/ save/ screenshots/ console.log  written by the game at runtime (gitignored with the rest of bin/)
```

How this layout works:
- The engine takes `-game <absolute path>`. The launcher passes `bin/<config>/mods/<mod>` from `mods.yaml` `game_dir` (relative to `mods.yaml`), so it no longer derives the mod folder from its own file name.
- **Binaries are shared, content and runtime state are per mod.** The engine loads `server.so` and `client.so` through gameinfo's `gamebin |gameinfo_path|bin`, and the host derives its managed and runtime dir as `<gamedir>/bin` (`sourcesharp_gamesystem.cpp:992`). A per-mod `bin -> ../../bin` symlink satisfies both with **no code change**.
  - The alternative is `gamebin |gameinfo_path|../../bin` in gameinfo plus a host change. It is not needed, and it would depend on the engine filesystem accepting `..`, which a live run would have to confirm.
- The launcher finds `libsteam_api.so` at `<its dir>/bin/linux64`, which is the shared `bin/linux64`.
- **gameinfo.txt fix:** two SearchPaths entries name the old folder relative to the engine install: `game+mod mod_sharp/custom/*` and `game+download mod_sharp/download`. In our copy they become `|gameinfo_path|custom/*` and `|gameinfo_path|download`. This is a MOD-COPY edit, listed in the Phase 2 rewritten-files list.
- The farm's compat `game/mod_sharp` link points at `bin/<config>/mods/test`. Old paths such as `game/mod_sharp/bin/managed` and `game/mod_sharp/console.log` therefore resolve through the per-mod `bin` symlink to the shared binaries and to the per-mod runtime files. The build's `OUTBINDIR` writes through the same symlink into the shared `bin/`.

Until Phase 6a, the old native launcher is still built, unchanged, for parity with the old tree. It lands as `bin/<config>/mod_sharp_linux64`, and `make run` passes `-game $(CURDIR)/bin/<config>/mods/test` explicitly. So the fork's `main.cpp` is never edited.

`#` in the launcher's file name is legal but must be handled:
- In make it starts a comment unless escaped: write `LAUNCHER := source\#`.
- In the shell it is a comment only at the start of a word, so `./source#` works; quote it anyway in every script.
- MSBuild is not trusted with it: the project's `AssemblyName` is `SourceSharp.Launcher`, and a post-publish target copies the native binary to `bin/<config>/source#`.
- A test, `launcher_name_test`, runs `./source# -help` through the Makefile, `tools/live-gate` and `devapi`, so a tool that truncates the name at `#` fails loudly.

The engine sees the mod directory name as the mod id (`test`, at `mods/test`), not `mod_sharp`. Before Phase 6:
- Grep game, host and managed code for gamedir-name comparisons: `GetModDir`, `COM_GetModDirectory`, `IsGameDir`, and the 13 `mod_sharp` literals in `src/sourcesharp/game`.
- Make every one of them independent of the folder name, each with a test. The folder is now named by the mod id, `mods/test`.

### Module layout

Every module is one directory under `src/`.
- A module with both managed and native code has `managed/` and `native/` subdirectories.
- A module with only one kind is **flat**: its project or sources sit directly in `src/<module>/`.

| Module | Shape | Managed (was `src/sourcesharp/managed/…`) | Native (was `src/sourcesharp/…`, plus our files at Valve paths) |
|---|---|---|---|
| `abi` | flat | `SourceSharp.Abi` (159 files; referenced by every other assembly) | — |
| `client` | split | `SourceSharp.Client` (123) | `game/client` (88, OURS only), plus `sdk/game/client/*` (7 added files incl. `client_csharp.vpc`) |
| `server` | split | `SourceSharp.Server` (345) | `game/server` (113, OURS only), plus `sdk/game/server/*` (8 added files incl. `server_csharp.vpc`) |
| `shared` | split | `SourceSharp.Shared` (116) | `game/shared` (89, OURS only), plus `sdk/game/shared/*` (17) and `sdk/public/*` (4: `igameentity.h`, `entity_funcslot.h`, `chain_state_notify.h`, `bullet_glass.h`) |
| `host` | split | `SourceSharp.Bootstrap` (3) | `host/` (5) and `sourcesharp.vpc` (builds `libmanaged_host.so`) |
| `test` | split | `SourceSharp.Tests` (401) | `test/` (309 gtest files) |
| `launcher` | flat | **new** `SourceSharp.Launcher` (NativeAOT, Phase 6a). It replaces the native `launcher_main_mod_sharp.vpc` and `exepath.h`, which are not moved. | — |
| `mods` | flat | **new** `SourceSharp.Mods`: the `mods.yaml` model and parser. It is AOT-safe, and both the launcher and Bootstrap reference it. | — |
| `devapi` | flat | `SourceSharp.DevApi` (25) | — |
| `generators` | flat | `SourceSharp.Generators` (7, Roslyn) | — |
| `mapgen` | flat | `SourceSharp.MapGen` (9) | — |
| `webui` | flat | 100 TypeScript files, was `src/sourcesharp/webui` | |

File counts are at `02851a196` and are re-measured at T in Phase 1.

Rules:
- In a split module, `native/sdk/` mirrors Valve-tree paths. It holds our files that must sit inside Valve directories (the 64 "added outside `src/sourcesharp`" files), and the farm links each one to the same path under `.sdk/<config>/src/`. So every file lives with its module.
- **Not moved:** `src/sourcesharp/public/*` (5) and `common/*` (2) are all relocated Valve code (VALVE-DERIVED). They stay in the fork and get no module. The same goes for any VALVE-DERIVED file inside `game/{client,server,shared}` (e.g. `physics.cpp`, `shell_*_sim.cpp`). That is why a module's `native/` holds OURS files only, and why the farm's `src/sourcesharp/game/<side>` is a real directory of per-file links from both repos rather than one directory link.
- **The farm's `src/sourcesharp/…` view is assembled from the modules** by a single map in `tools/setup`, `module-map.tsv` (farm path → repo path). It covers `managed/SourceSharp.X` → `src/<module>/managed` or `src/<module>` for flat modules, `game/<side>` → `src/<side>/native`, `host` → `src/host/native`, `test` → `src/test/native`, `webui` → `src/webui`, and `sourcesharp.vpc` → `src/host/native/sourcesharp.vpc`. Rule 4 (no path supplied twice) is checked against it.
- ProjectReferences between assemblies are rewritten for the new relative paths. Count them before and after; the solution must load every project (9 moved + 2 new).

Debug and release get separate farms and separate `bin/<config>` directories. This removes today's "both configurations deploy to the same place" hazard (Makefile:22).

---

## Phase 0: land outstanding work on the fork

Work in a dedicated worktree of `source-sdk-2013` whose branch starts at the fork's working-branch tip (memory `worktree-agents-start-on-master`: reset to the tip as step 0).

1. Merge the plan_shim integration branch. One conflict is known, in `src/buildcpptests` (both sides add `build_and_run` entries; keep both). Check the merged file's entry count equals the sum of both sides' counts (memory `abi-merge-splice-eats-a-line`).
2. Merge lanes 9b-playerstate, 9c-worldstate and 9d-clientglobals in that order, resolving and re-verifying after each.
3. Write lane 9f-managedvtable's state (1 of 7 done, its brief) to a backlog note for `docs/plans/`. Do not merge its code.
4. **Gate:** cold `make debug`, `make release` and `make test`; zero warnings; a live gate with two map loads (memory `one-map-load-hides-a-whole-bug-class`). Quote test counts before and after.
5. Hand over a fast-forward of the fork's working branch and say so (memory `peer-owns-integration-branch`). Do not push.
6. **Freeze:** record the tip as `T`. Any later fork commit that touches an OURS path must be replayed into the new repo in Phase 9.

## Phase 1: classify every file at T (the move ledger)

`tools/move-classify` writes `docs/move-ledger.tsv`, one row per tracked file at `T` (plus the untracked `plan_*` files), with exactly one class:

| Class | Meaning | Goes to |
|---|---|---|
| `VALVE` | identical to Valve upstream `22288b919` | stays in fork; linked if needed |
| `VALVE-EDITED` | a Valve path we modified | stays in fork; linked |
| `VALVE-DERIVED` | under `src/sourcesharp` but relocated or split Valve code | stays in fork at its path; linked per file |
| `OURS` | SourceSharp-authored | new repo, via the path map |
| `MOD-COPY` | every tracked file under `game/mod_sharp` (decided 2026-09-27: copied whole for easy testing) | new repo `game/mod_test/` (folder renamed, file names unchanged); removed from the fork at cut-over |
| `OURS-OVERLAY` | SourceSharp-authored at a Valve-tree path | new repo `src/<module>/native/sdk/<same path>`, or `tools/build/` for build scripts and gates (see Module layout) |
| `DROP` | dead tools, lane probes, stale output (Phase 2 list) | nowhere |

**The `valve_header` column.** Every OURS, OURS-OVERLAY and MOD-COPY row that is a source file (`.cs`, `.cpp`, `.h`, `.ts`, `.tsx`, `.vpc`) also gets `valve_header` = `yes`/`no`, with a one-line **reason**. It is `yes` if any of the following holds:
1. The file carries a Valve copyright header today (35 C++ files in `src/sourcesharp` at `02851a196`).
2. The file **ports or translates Valve logic**. This is judged per file from its content: it names the Valve file or function it reproduces, or its logic mirrors one. The reason cites that source, e.g. `ports CGameMovement::FullWalkMove (gamemovement.cpp)`. A grep (`.cpp` / `stock` / `ported from` / Valve) only nominates candidates: about 767 of the 1,167 `.cs` files hit it, many of them tests that merely compare against stock, so it decides nothing.
3. It is **native code built against Valve SDK headers**: it includes `cbase.h`, `tier*/`, `public/…` and similar (172 non-test native files at `02851a196`).

It is `no` for original work: `webui`, `devapi`, `generators`, `mapgen`, the new `launcher` and `mods`, tools, build scripts, and tests that only check behaviour. A test that embeds ported Valve logic, such as a reference implementation or a fixture generator, is `yes` under rule 2.

The per-file judgement for rule 2 is fanned out to agents by module, one ledger slice each (memory `always-use-subagents`). Every `yes` or `no` for a candidate the grep nominated carries its reason.

The header, byte for byte (kept in `tools/valve-header.txt`):

```
//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
```

VALVE-DERIVED is decided by measurement, not by header. A file is VALVE-DERIVED if it has **≥ 12 consecutive non-trivial lines** (trivial means blank, a brace, `#endif`, `return;` and similar) that appear verbatim in any file of `git ls-tree -r 22288b919`. The threshold is written into the tool with its reasoning.
- Files with only a copied boilerplate Valve header (4 tests, `standalone_*.h`) come out OURS. Their old header block is replaced by the attribution header when they move.
- Borderline files (overlap below threshold but present) are listed for a ruling.

**Gate:**
- Every file has exactly one class, checked by a second independent count (`git ls-files | wc -l` against the ledger row count).
- The tool's self-test passes. A planted verbatim Valve block must flip a file to VALVE-DERIVED, or the check cannot fail (memory `the-check-that-cannot-fail`).
- Per-class counts go into the ledger header, stamped with `T` (memory `a-check-log-has-no-provenance`).

## Phase 2: create the new repo and copy OURS

`../source-sdk-sharp` exists as an empty repo on `main`. Copy files per the ledger and the path map above. Nothing is `git mv`'d, since history is not kept.

Tools triage (from the survey; re-check references at T):
- **Keep, needed by the Makefile, build or a committed test:**
  - `get-node`, `worktree-setup` (becomes the base of `tools/setup`), `run-test-legs`, `leg-floor`, `live-gate-lease`
  - `entrypoint-decls`, `convar-ref-census`, `entity-ratchet`, `entity-census`, `entity-census-patterns`, `entity-gates`, `entity-gates-allowlist`
  - `live-gate` and its `.tsv` and scenario files, `gpu-offload`, `build-stamp`, `vtable-layout` and `vtable-policy.json`
  - `dead-conditionals` and its ledger, `shim-orphan-reach`, `movemath-gate`, `8f-movetype-gate`, `8f-probe-movetypes`, `3d-cmdgate`, `client-only-probe`, `physics-scope/`, `4f-component`, `p4-exit-probe-skybox`
  - the 8 probes that `physics_event_sink_test` opens
  - the `globals-*` tools arriving in Phase 0
- **Keep as useful:**
  - `shim-census`, `shim-header-census`, `header-consumers`, `reached-by-name`, `vtable-dump`, `shim-facade-audit`, `shim-policy-audit`, `abi-slot-census.py`, `variant-oracle.c`, `mutate-managed`, `mutate-native`, `finish-line`
  - the live-gate "live half" scripts (`7a-effect-gate`, `7c-view-gate`, `7e-particle-sound-gate`, `fx-impact-gate`, `defect-teleport-gate`, `8e-entfire-gate`, `3f-shadow-ab`)
  - `devapi`, `devapi-proxy`
- **Drop:**
  - the lane probes `2f-*` … `8f-mutate`, the `c8-*` gates, `crate-sleep-*`, `p4*`, `sub-*` and the rest of the survey's DROP list
  - `src/f-srvshim-objs-*.txt`, `src/tmp`, every `__pycache__`, `home/`, logs, `.test-legs`
  - `maptools`, `mapgame`, `steamcmd`
- A test that merely *names* a dropped tool in a message is edited. A test that *opens* one keeps that tool. The phase ends with zero dangling `tools/…` opens (grep of committed tests against the file list).

Also:
- `docs/` → `docs/`. Every `plan*.md` / `plan_*` (tracked and the untracked `plan_globals*`, `plan_shim*`) → `docs/plans/`.
- `.claude/skills/exec-plan` → `.claude/skills/`.
- A new `.gitignore` covering `bin/`, `.sdk/`, `tools/node`, `.npm-*`, `*.vpc_cache`, `obj/` and `sdk-root.lock.local`.

**Headers are applied before the first commit.**
- `tools/valve-header apply` takes the ledger's `valve_header=yes` files.
- If a file's first comment block contains `Copyright Valve`, that block is **replaced**; any `Purpose:` text in it is kept below the new header. Otherwise the header is **prepended**, followed by one blank line.
- The header's `//` comments are valid in every file type it applies to (C#, C++, TS/TSX, `.vpc`).
- It runs once over the copied tree. It is idempotent: a second run changes nothing, which is checked by hash.

**Checkpoint, needs your review before anything is checked in:**
- `tools/valve-header review` writes `docs/valve-header-review.md` on demand (it is not committed): every `yes` and every grep-nominated `no`, grouped by module, each with its reason, plus the counts per rule.
- Nothing in the new repo is committed until you have reviewed that list. The review can flip rows, and the flips are recorded in the ledger.

**Gate:**
- Every OURS and OURS-OVERLAY ledger row exists at its mapped path with identical bytes (sha256 comparison, except for the rewritten headers, which are listed).
- No DROP or VALVE-* file is present (`tools/valve-text-audit`, Phase 8, run early).
- Every `valve_header=yes` file starts with the exact header, and no `no` file has it (`tools/valve-header check`).
- The header count equals the ledger's `yes` count, measured twice with independent commands (the tool, and a `grep -lF` of the header's third line).
- Commit.

## Phase 3: `make setup`, the farm and the game root

`tools/setup` (run as `make setup ROOT=../source-sdk-2013`; `ROOT` is remembered in the untracked `sdk-root.lock.local`):
1. Checks that `ROOT` is a git checkout whose `HEAD` equals `sdk-root.lock`, or is a descendant of it with `--accept-root`, which rewrites the lock. It refuses on mismatch and names both commits.
2. For each configuration, builds `.sdk/<config>/` from `sdk-links.txt` (ROOT paths, per-file links) plus the fixed OURS map (dir links). It uses absolute link targets and removes stale links, so the result is idempotent.
3. Builds `bin/<config>/` (see "Output layout"):
   - Shared real directories `bin/linux64`, `bin/managed` and `bin/dotnet`, plus a link `bin/linux64/libsteam_api.so` to the fork's `game/bin/linux64/libsteam_api.so`.
   - One directory per mod in `mods.yaml` under `bin/<config>/mods/`, today just `mods/test/`, containing:
     - per-file links to that mod's content in the repo (`game/mod_test`: gameinfo, scripts, shaders, resource, `dm_lockdown.bsp`); nothing of the mod comes from the fork
     - a real `maps/` directory (its files are links, and compiled `.bsp` output lands beside them)
     - the `bin -> ../../bin` symlink
   - The farm's compat `game/` directory (`mod_sharp` → `bin/<config>/mods/test`, `bin` → `bin/<config>/bin`).
4. Provisions files git does not carry, replacing today's `worktree-setup` copies:
   - `bin/<config>/bin/dotnet`: the bundled runtime from `~/.dotnet` (memory `ubuntu-dotnet-package-miscompiles`: never `/usr/bin/dotnet`), or a pinned download
   - `steam_appid.txt`
   - node via `get-node`
5. `--check` reports without changing anything. Every launch target runs it first, as `require-worktree-files` does today.

The initial `sdk-links.txt` holds **every** fork file the old checkout had that is not OURS or OURS-OVERLAY: all VALVE, VALVE-EDITED and VALVE-DERIVED paths, minus build outputs. It is deliberately correct first, not minimal. Phase 7 minimises it.

**Gate:**
- gtest/pytest-style self-tests for rules 1 to 5, including the negative cases: a planted OURS path in the manifest, a path supplied by both repos, a manifest dir entry, and a built lib listed. Each must be refused, and each was seen red.
- Running setup twice produces a byte-identical `find -printf '%p %l\n'` listing.
- Commit.

## Phase 4: build

- **Container:** write our own runner, `tools/build/sdk_container_sharp`; the fork's `sdk_container` is VALVE-EDITED and stays untouched.
  - It bind-mounts the new repo and `ROOT` **at their host paths**, so absolute farm links resolve identically inside and outside the container.
  - `/my_mod` is kept as a second mount of the repo only if a test needs it. Three test files mention `/my_mod`; check them.
  - It keeps the ccache mount.
- **`serverplugin_empty` is not built.** `buildsourcesharp` passes ninja an explicit target list (the client, server, `sourcesharp`, launcher and the Valve libraries) instead of `all`, so there is no fork edit. `utils/serverplugin_sample` is left out of `sdk-links.txt` from the start.
- `buildsourcesharp` runs VPC and ninja with cwd `.sdk/<config>/src`. `OUTBINDIR` (`$SRCDIR\..\game\$GAMENAME\bin`) then lands in `bin/<config>/bin` through the farm's compat `game/mod_sharp` link. Only our launcher `.vpc` changes (name and `OUTBINDIR`; see "Output layout").
- **Managed build:**
  - Root `SourceSharp.slnx`.
  - the root `Directory.Build.props` sets `SourceSharpBinDir = $(RepoRoot)bin/$(Configuration)/bin/managed/`.
  - Intermediate and output paths go under `bin/<config>/obj` (the artifacts layout), so nothing is written into `src/`.
  - `build-stamp` is called from `tools/`.
- **Makefile:** port the targets. `debug`/`release` call `setup --check` first, then build their own configuration. `run` launches `bin/debug/mod_sharp_linux64 -game $(CURDIR)/bin/debug/mods/test` until Phase 6a replaces it with `source#`.
  - `test`, `check`, `cpp-test`, `live-gate`, `ui`, `ui-watch`, `sandbox` and the entity/globals targets are kept.
  - `map` calls `$(MAPTOOLS_DIR)` (default `../source-sdk-map-tools`) and fails with a clear message if that directory is missing.
  - `clean` removes `bin/<config>` and `.sdk/<config>/src/_vpc_`.

**Gate ("compiles and produces correct files"):**
- A cold `make debug` and `make release` from a **fresh clone** plus `make setup`, with `pwd` shown and the ninja graph removed first (CLAUDE.md "Verify Before Claiming").
- Compare the output file set against an old-tree build of `T` in the same configuration, through an explicit old→new path map: `game/mod_sharp/bin/X` → `bin/<config>/bin/X` and other `game/mod_sharp/X` → `bin/<config>/mods/test/X`, `game/mod_sharp_linux64` → `bin/<config>/mod_sharp_linux64` (until Phase 6a). Every old output must exist at its mapped path, and nothing new may appear unexplained. The one expected difference is that the old tree's `game/bin/linux64/serverplugin_empty.so` has no counterpart.
- For each `.so`, the defined dynamic symbols (`nm -D --defined-only` with a symbol-type filter; memory `nm-without-symbol-type-inflates`) and the section sizes must match the old build.
- Any difference is explained, not waived.
- Rule 6: `git -C $ROOT status --porcelain` is empty after both builds.
- Commit.

## Phase 5: tests

1. Replace the 34 C# root-finders with one `RepoLayout` helper in the test project.
   - It knows `RepoRoot` (walks up to `SourceSharp.slnx`) and `SdkTree` (`$SOURCESHARP_SDK_TREE`, else `RepoRoot/.sdk/<config>`). It never walks past `RepoRoot` (memory `a-test-can-read-the-wrong-checkout`).
   - Existing `Path.Combine(root, "src", …)` calls resolve against `SdkTree`, so the old-layout paths keep working.
   - Mechanical edit: count call sites before and after (memory `count-before-and-after-a-mechanical-edit`).
2. Native tests run inside the container with cwd `.sdk/<config>/src`, unchanged.
   - `OUT=../tools/.cpptest` becomes `bin/<config>/cpptest`.
   - The two `/proc/self/exe`-strip-3 root finders (`orphan_reach_test.cpp:96`, `entity_gates_test.cpp:505`) are updated to the new depth.
3. Code run from the repo root rather than from the farm cannot see the compat `game/mod_sharp` link. That is `tools/` (46 files mention `mod_sharp`), the Makefile and `devapi`. Their `game/mod_sharp/...` paths are rewritten: `bin/...` → `bin/$(CONFIG)/bin/...`, and `console.log`, `maps/`, `cfg/` → `bin/$(CONFIG)/mods/test/...`. The launcher name changes in Phase 6a.
   - This is a mechanical edit: count references before and after.
   - The only `mod_sharp` references left are the compat link in `tools/setup` and the gamedir-name tests.
4. Tests that assert the *absence* of Valve files (evicted-TU tests) must read the farm, where an evicted file has no link. They must not read the fork, which still holds the file. Each such test is mutation-checked: plant a link, and the test must go red.
5. **Gate:**
   - The full `make test` (every leg) and the old tree at `T` report the same discovered and passed counts per leg.
   - Zero warnings.
   - Rule 6 holds.
   - Commit.

## Phase 6: live run

- `make run` (the old native launcher with an explicit `-game`) and `make live-gate` on the NVIDIA card (memory `launch-game-on-the-nvidia-card`).
- A per-lane devapi socket dir (memory `devapi-socket-dir-is-machine-wide`).
- Two map loads, a `devapi ents` check, `sv_cheats 1` for `ent_fire` (memory `ent-fire-needs-cheats`).
- `make release` then a live run from `bin/release`.

**Gate:**
- The live gate passes in both configurations.
- `[SourceSharp]` lines are present in `bin/<config>/mods/test/console.log`.
- The engine's reported game dir is `bin/<config>/mods/test`.
- Runtime writes (`cfg/`, `save/`, `screenshots/`, `console.log`) land only under `bin/<config>/mods/test/`: `find bin/<config> -maxdepth 1` and `bin/<config>/mods` list nothing the game wrote.
- No `mod_*` path exists under `bin` except the old launcher binary before Phase 6a: `find bin -name 'mod_*'`.
- Commit.

## Phase 6a: port the launcher to C# NativeAOT (`source#`) with `mods.yaml`

The native launcher is `src/launcher_main/main.cpp`, a VALVE-EDITED fork file, plus our `exepath.h`. Its POSIX path does the following:
1. Reads `-nomessagebox`.
2. Resolves its directory from `/proc/self/exe`.
3. `dlopen`s `<dir>/bin/linux64/libsteam_api.so` and writes `steam_appid.txt` (`MOD_APPID`, 243750) in the cwd.
4. Calls `SteamAPI_Init`, then `ISteamApps::BIsAppInstalled(243750)` and `GetAppInstallDir`, then `SteamAPI_Shutdown`.
5. `execvp`s `<install>/hl2.sh`, appending `-game <dir>/<exe name minus _linux64>` unless `-game` was given.
6. Sends every failure through `LauncherError`: stderr with the prefix `[Source Mod Launcher]`, plus a dialog unless `-nomessagebox`. An `execvp` failure is reported, not swallowed.

`hl2.sh` is **Valve's**: it ships with the Steam install of Source SDK Base 2013 Multiplayer (app 243750) and is in neither repo. Its 114 lines do the following:
- choose 32- or 64-bit by the presence of `<game>/bin/linux64/server.so`
- set `LD_LIBRARY_PATH=<install>/bin/linux64`
- `ulimit -n 2048` and `__GL_THREADED_OPTIMIZATIONS=1`
- `cd <install>`, and set `ENABLE_PATHMATCH` if `pathmatch.inf` exists
- run `hl2_linux64`, **restarting while the exit code is 42**, with an optional `GAME_DEBUGGER=gdb` wrapper

**The port does both jobs in C#.** It stops depending on Valve's script, which has already disappeared once under a Steam update (see the comment at `main.cpp` ~705). Because of the exit-42 restart loop, the launcher **spawns and waits** instead of `exec`ing.

**`src/mods`: `SourceSharp.Mods` (AOT-safe library)**
- It parses `mods.yaml` with YamlDotNet's source-generated static context: no reflection, so there are no trimming or AOT warnings.
- It rejects unknown keys, a `default` that names no mod, a mod with no `game_dir`, and relative paths that escape the file's directory.
- Schema (first cut; decide the rest in the phase, from what the code needs):

```yaml
default: test                    # used when no -mod <id> is given
mods:
  test:                          # the id is also the dir name: bin/<config>/mods/<id>/
    title: SourceSharp test mod
    game_dir: mods/test          # relative to mods.yaml (bin/<config>/mods/test); must contain gameinfo.txt; passed as -game <abs>
    content: game/mod_test       # repo dir setup links into game_dir (setup reads this too)
    steam_appid: 243750          # written to steam_appid.txt (was MOD_APPID)
    engine:
      appid: 243750              # the Steam app whose install holds the engine (Source SDK Base 2013 MP)
      exe: hl2_linux64           # run from the engine install dir (replaces hl2.sh)
      restart_exit_code: 42      # restart the game while it exits with this code (hl2.sh behaviour)
      fd_limit: 2048             # RLIMIT_NOFILE (hl2.sh behaviour)
    assemblies:                  # loaded by Bootstrap into each side's load context, in order
      server: [SourceSharp.Server]
      client: [SourceSharp.Client]
    managed_dir: bin/managed     # relative to game_dir (resolves through its bin -> ../../bin link)
    args: []                     # default extra engine args, placed before the user's own
    env: {}                      # extra environment for the game process
```

**`src/launcher`: `SourceSharp.Launcher` (NativeAOT, publishes `bin/<config>/source#`)**
- Mod selection: `-mod <id>`, which is stripped before the engine sees argv, else `default`. An unknown id is an error that lists the known ids.
- **Steam:** load `<launcher dir>/bin/linux64/libsteam_api.so` with `NativeLibrary.Load`. Call its flat exports, all present in the tracked `libsteam_api.so` (checked with `nm -D`): `SteamAPI_Init`, `SteamAPI_SteamApps_v008`, `SteamAPI_ISteamApps_BIsAppInstalled`, `SteamAPI_ISteamApps_GetAppInstallDir`, `SteamAPI_Shutdown`. There is no vtable walking, and the error messages are ported string for string.
- **Environment:** the game process gets:
  - `hl2.sh`'s environment (`LD_LIBRARY_PATH`, `__GL_THREADED_OPTIMIZATIONS`, `ENABLE_PATHMATCH`)
  - the mod's `env`
  - `SOURCESHARP_MODS_YAML=<abs path>` and `SOURCESHARP_MOD=<id>`, which is how Bootstrap learns the assembly list
- **argv:** `-game <abs game_dir>` unless the user gave `-game`, then the mod's `args`, then the user's arguments in their original order. `-nomessagebox` is honoured as before.
- **Error dialog:** stderr always. When a dialog is allowed, `SDL_ShowSimpleMessageBox` from the engine install's `libSDL2`, falling back to stderr only if it cannot load.
- **Seams for tests:** Steam, the process spawner, rlimit and the message box are behind small interfaces, so every branch is an xUnit `[Fact]` with fakes, one behaviour per fact.
- **Build:** `dotnet publish -r linux-x64 -p:PublishAot=true` on the host.
  - Publishing needs `clang` (or `gcc`) and zlib dev headers; `tools/setup --check` reports if they are missing.
  - The published binary is copied to `bin/<config>/source#`.
  - Zero IL2xxx/IL3xxx warnings, alongside the zero-warnings rule.

**Bootstrap (`src/host/managed`)**
- `Entry.cs:691` stops hardcoding `"SourceSharp.Server"` / `"SourceSharp.Client"`.
- It reads the mod's `assemblies.<side>` through `SourceSharp.Mods`, from `SOURCESHARP_MODS_YAML` / `SOURCESHARP_MOD`, falling back to `<mod dir>/mods.yaml` and its `default` when the game was started without the launcher.
- It loads the listed assemblies in order into the side's `GameLoadContext`. Exactly one `ISideModule` implementation must exist across them; zero or more than one is a loud error naming the assemblies.
- A missing or invalid `mods.yaml` fails the load with the parser's message. It never silently falls back to the old names.

**Switch-over: the native launcher stays in use until the managed one has passed**
- Throughout 6a both launchers are built side by side: `bin/<config>/mod_sharp_linux64` (native, unchanged) and `bin/<config>/source#` (managed).
- The Makefile selects between them with `LAUNCHER=native|managed`, **default `native`**. The same variable applies to `run`, `run-debug`, `headless`, `live-gate`, `tools/live-gate` and `devapi`, so the managed one can be exercised everywhere without disturbing anyone else's runs.
- The default flips to `managed` only after every item of the gate below has passed with `LAUNCHER=managed`. The native build and the `LAUNCHER` switch are then removed in a separate final commit of 6a.
- That keeps a known-good launcher one variable away until the retirement commit, and makes the retirement a single revertable step.

**Retired, in 6a's final commit**
- The native launcher project: `launcher_main_mod_sharp.vpc` and `exepath.h` are not moved.
- The launcher leaves `buildsourcesharp`'s explicit ninja target list, so there is no fork edit here. The fork's `[$CSHARP]` VPC entries are retired together in 6b.
- `exepath_test.cpp` goes with its subject: `Environment.ProcessPath` has no unterminated-`readlink` bug. Its behaviour, "the launcher's own dir is exact whatever the stack holds", is kept as a launcher fact.
- `vpc_removal_gate_test.cpp`'s `launcher_main` expectations are updated.
- The Makefile's `run`, `run-debug`, `headless` and `live-gate` targets, `tools/live-gate` and `devapi` launch `bin/<config>/source#` with **no** `-game`, which proves `mods.yaml` supplies it.

**Gate:**
- The launcher and mods xUnit facts pass: parse, reject, select, argv, env, restart-on-42, Steam failure messages, and missing `libsteam_api.so`.
- Each fact is mutation-checked for its red path.
- AOT publish has zero warnings.
- `./source# -help` works through make, the shell and `devapi`.
- A live run of `bin/debug/source#` and of `bin/release/source#`, with two map loads each.
- `[SourceSharp]` shows Bootstrap loaded exactly the assemblies `mods.yaml` lists. Test this with a mod entry that lists an extra assembly: it must appear in the log, and removing it must remove it.
- A second mod entry in a scratch `mods.yaml`, started with `-mod`, reaches `gameinfo.txt` in its own `game_dir`.
- The old launcher's output is gone from `bin/`.
- Commit.

## Phase 6b: CMake + Ninja replaces VPC and the container

This is done after Phases 4 to 6a are green, so a VPC-to-CMake difference can never be mistaken for a move or launcher difference. By then the launcher is the C# `source#` (6a), so **no native launcher target is ported**, and `serverplugin_empty` was never built (Phase 4). **Measured at `02851a196`:** the release graph has 533 translation units. 4 of them are not ported: `serverplugin_empty` (3, never built by the new repo) and the native launcher (1, replaced by the C# launcher in 6a). That leaves 529 across the targets below:

| Target | TUs |
|---|---|
| `client_sourcesharp` | 253 |
| `server_sourcesharp` | 108 |
| Valve libraries rebuilt from source | 167 (`vgui_controls` 77, `tier1` 44, `mathlib` 22, `matsys_controls` 21, `raytrace` 3) |
| `sourcesharp` (`libmanaged_host`) | 1 |

The host has gcc 15.2, glibc 2.43, **cmake 4.2.3, ninja 1.13.2 and clang 22.1.8**, all installed 2026-09-27 and checked with `--version`. lld 22 is installed as `/usr/bin/ld.lld-22`; `/usr/bin/lld` is only the generic driver, and there is no unversioned `ld.lld`. The toolchain file therefore links with `--ld-path=/usr/bin/ld.lld-22` (a cache variable, overridable), not `-fuse-ld=lld`.
   - lld's stricter handling of `--start-group` / archive order and of `--gc-sections` is checked by the symbol-parity gate below.
   - If the parity gate can't be made to agree, GNU ld (`-DSS_LINKER=bfd`) is the fallback, and the fallback is recorded. Today's outputs need at most `GLIBC_2.29`, and libstdc++ is linked statically.

1. **Generate, then own.**
   - Export VPC's per-TU compile commands (`ninja -t compdb`, both configurations).
   - Generate `CMakeLists.txt` per target from them: sources, `-D` defines, `-I` paths and flags from `source_posix_ninja.vpc` (`-std=c++11`, `-march=core2`, the warning set, `-static-libgcc`, the link groups and prebuilt libs).
   - From then on they are hand-owned files. Target files live with their module: `src/<module>/native/CMakeLists.txt`, and the Valve-library targets in `cmake/sdk/`.
   - Sources are compiled **from the farm**, so `sdk-links.txt` stays the single list of Valve files we depend on.
2. **Presets** (`CMakePresets.json`):
   - `debug` and `release` build locally with **host clang 22**, with no container and no sysroot. glibc age does not matter locally.
   - gcc 15 stays a supported local fallback (`-DCMAKE_CXX_COMPILER=g++`) but is not gated.
   - `ci-debug` and `ci-release` run **inside Valve's sniper SDK container**, the image used today, through `tools/build/ci-build` (`podman`/`docker run` with the repo and `ROOT` mounted at their host paths).
     - The container provides glibc 2.31 and its own toolchain. There is no sysroot tarball and nothing extra to pin beyond the image.
     - The compiler is whichever one in the image builds this code with zero warnings: its clang if it has a usable one, otherwise its gcc. The pick is recorded in the preset.
     - If CI's compiler differs from the local clang, both still pass the same symbol-parity and test gates.
     - A local developer can run `make ci-build` to reproduce CI exactly. It is the only target that needs podman. **Gate for CI only:** `objdump -T` shows no `GLIBC_` newer than 2.31 in any output.
   - `tools/setup --check` reports missing `cmake`/`ninja`/`clang` and names the install command.
3. **Compiler drift.** The container builds with its gcc; the new build uses clang 22. Two consequences:
   - Clang warns differently and is stricter than the container's gcc on 2013 Valve code.
   - `__clang__` / `__GNUC__` paths in Valve headers change. Grep them first and list the ones our targets reach.
   - Each new diagnostic is fixed in **our** code, or silenced by a named `-Wno-…` in the Valve-library or SDK-source target with a comment on why.
   - A Valve source is never edited to satisfy the compiler unless you rule it a fork edit.
   - The warning list is part of the gate: zero warnings.
4. **The Valve libraries.** The tracked `src/lib/public/linux64/{tier1,mathlib,vgui_controls,matsys_controls,raytrace}.a` are Valve's prebuilts, byte-identical to upstream (`git diff 22288b919 HEAD -- src/lib/public/linux64` is empty). Building them from source costs 167 TUs.
   - **`tier1` must be rebuilt while `tier1/interface.cpp` carries our 17-line edit.** That edit is `Interface_SetMissHook`, a diagnostic hook in `CreateInterface` that `shell_diagnostics.cpp:97` sets to log interfaces asked of our DLL that it does not export. **Open decision, see handover.**
   - For the other four libraries, try Valve's prebuilts. Keep them only if the symbol comparison and the live gate agree. Some VALVE-EDITED public headers (`iclientrenderable.h`, `networkvar.h`, …) may change class layouts that those libraries were compiled against, and only a real run catches that.
5. **Native tests: the harness moves to CMake and ctest.**
   - Each `build_and_run` entry in `buildcpptests` becomes an `add_executable` with the same sources, flags and stubs, registered with `gtest_discover_tests`.
   - `buildonecpptest` and `cpptest_one` become `ctest -R`.
   - Swap Valve's bundled `thirdparty/protobuf-2.6.1/gtest` (a 2013-era copy in the Valve tree) for a **pinned upstream googletest**, which also removes it from the manifest. Either `FetchContent` with a hash, or a vendored copy under `third_party/` with its license. Old-gtest-only macros are ported mechanically, counted before and after.
   - Output goes to `bin/<config>/cpptest` (was `tools/.cpptest`).
   - Tests keep running with cwd `.sdk/<config>/src`, set per test through `WORKING_DIRECTORY`.
6. **Retire VPC in the new repo.**
   - `buildsourcesharp`, `build_check`, `preprocess_target` and `sdk_container_sharp` are removed or reduced to `cmake --build`.
   - The VPC gates (`vpc_removal_gate.sh`, `vpc_watermark_gate.sh`, `*.watermark`, `expected_noop_removals.txt`) and every test that reads a `.vpc` or the VPC ninja file (about 12 read `_vpc_/ninja/…`, and 10 or more read `.vpc` prose, per memory `vpc-prose-is-load-bearing`) are listed. Each is either re-pointed at the CMake source of truth (for example "file X is not compiled" becomes a query of CMake's `compile_commands.json`), or retired with a reason. Count before and after.
   - `*_csharp.vpc` and `sourcesharp.vpc` leave the repo. The farm stops linking `devtools/bin/vpc`, `vpc_scripts/` and the `*.vpc` files.

**Gate:**
- **TU parity:** the CMake `compile_commands.json` and VPC's compdb list the same source set per target, the same `-D` set, and equivalent `-I` directories. The diff is empty, or each line is explained.
- **Output parity:** per `.so`, the defined dynamic symbols (type-filtered) match the Phase 4 build.
- **Tests:** ctest reports the same per-binary test counts as `buildcpptests` did, all passing; the full `make test` is green with zero warnings.
- **Live:** a live run of `bin/<config>/source#` in both configurations.
- **No native launcher:** there is no launcher target in any `CMakeLists.txt`.
- **Local:** a cold local build with `podman` absent from `PATH` succeeds.
- **CI:** the `ci-release` preset's GLIBC ceiling check passes.
- **Commit.** Retiring the `[$CSHARP]` VPC entries in the fork is a fork commit (re-pin `sdk-root.lock`); hand it over as a fast-forward.

## Phase 7: shrink the manifest to what is actually used

`tools/sdk-census` computes the needed set as the union of:
- ninja depfiles (`ninja -t deps`) and the graph inputs (memory `objs-directory-is-not-the-link-line`: enumerate from the graph, not from `objs/`) for both configurations
- the `.vpc`/`.vgc` files VPC opens
- prebuilt libraries on the link lines
- every file a test opens, measured with `strace -f -e trace=openat` over one full `make test` into a log, not guessed from literals
- runtime game content opened during the Phase 6 live run

`sdk-links.txt` is rewritten to that set, sorted, and grouped by top-level dir with a count header.

**Gate:**
- The full Phases 4 to 6 gates pass again on the minimised farm.
- The link count before and after is quoted, measured twice with independent commands.
- `make sdk-census` is added. It prints the link count and fails if the manifest lists a path the census did not see. That makes a stale link a failure, and it is how the list keeps shrinking as ports land.
- Commit.

## Phase 8: prove that no Valve code is in the repo

`tools/valve-text-audit`, run over every tracked file of the new repo against `ROOT`'s Valve upstream tree (`22288b919`):
- No run of ≥ 12 consecutive non-trivial lines matches Valve text.
- **Headers:** a `Copyright Valve Corporation` line may appear only as part of the exact attribution header, at the top of a file the ledger marks `valve_header=yes`. Any other Valve copyright form fails, and so does a `yes` file without the header.
- The header marks attribution. It does **not** license verbatim Valve text, and the ≥ 12-line rule above applies to every file whether or not it has the header.
- No Valve binaries or assets: no `.so`/`.dll`/`.bsp`/`.vpk`/`.vtf`/`.mdl` tracked, and no `game/` file whose blob exists in `ROOT`.
- **One named exemption: `game/mod_test/`.** It is copied whole on purpose so the mod can be tested standalone. The audit reads the exemption from one list with the reason, reports its file count, and fails if a Valve-matching file appears anywhere outside it.
- The audit has a self-test with a planted violation for each rule. It runs as a `make test` leg, so this stays true.

**Gate:** zero findings, stamped with the commit. Commit.

## Phase 9: docs, CLAUDE.md, cut-over

1. **README.md**
   - What SourceSharp is.
   - Prerequisites: the fork checkout, Steam HL2MP (app 243750) for content, .NET 10 at `~/.dotnet`, podman with the sniper image (and the XDG_DATA_HOME snap workaround).
   - `make setup ROOT=…`, `make debug|release|test|check|run`.
   - The layout.
   - How the farm and manifest work, and why the link count is the progress metric.
   - How to update `sdk-root.lock` when the fork moves.
   - Replaces Valve's README text; nothing of it is kept.
2. **CLAUDE.md**
   - Today's CLAUDE.md rules (commit per step, xUnit `[Fact]`/gtest, maximise C#, working style, worktrees, verify-before-claiming, long-running sessions).
   - Plus **every memory entry folded in** as a rule with its *why*, grouped by topic: build & container, testing & mutation proofs, live gates, git & lanes, measurement pitfalls, machine.
   - Entries tied to the old layout are rewritten for the new one: `.test-legs`, "main checkout", `-game` paths, the old working-branch name.
   - Map-tools-only entries are dropped and listed in the handover.
   - A rule: a new file that ports or translates Valve logic gets the attribution header from `tools/valve-header.txt`, and its ledger row says why. The `valve-header check` leg of `make test` enforces it.
   - A new section, "Two repos": which repo an edit belongs in (the ledger classes), the rule that fork edits are made in `source-sdk-2013`, and that `sdk-root.lock` is re-pinned with them.
3. **docs/**: `docs/plans/` for all plans, `docs/move-ledger.tsv`, and fixes to dangling doc links (`docs/README.md` is referenced but missing today).
4. **Replay:** any fork commits after `T` touching OURS paths are replayed through the path map (`git format-patch T..` rewritten by the ledger map, then `git am`). Rerun the Phase 4 and 5 gates.
5. **Checkpoint (needs your approval; destructive on the fork):** delete OURS, OURS-OVERLAY and MOD-COPY paths from the fork's working branch in one commit, leaving the fork as Valve plus edits plus derived files. Then run `make setup --accept-root`, and a cold build, the tests and a live run of the new repo **against the cleaned fork**. This proves there is no hidden dependency on the fork's copies. Hand over as a fast-forward; do not push.

## Out of scope and follow-ups

- Moving tests off old-layout literals (`"src/sourcesharp/…"`) onto `RepoLayout` properties. This can happen gradually; the farm makes it unnecessary for correctness.
- Retiring VALVE-DERIVED files. Each is ported to C# or re-derived clean-room, and its link is deleted. Tracked by `make sdk-census`.
- Rebasing the fork onto newer Valve upstream (`origin/master` `b8cfb12`, 35 files).
- Lane 9f-managedvtable: 6 steps remain (backlog note from Phase 0).
- A LICENSE for the new repo: your choice. Valve's SDK license covers the fork, not our code. The ~508 C# translations of Valve logic are a derivative-work question the audit in Phase 8 does not answer.
