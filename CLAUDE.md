# SourceSharp: rules for Claude Code sessions

SourceSharp is a .NET 10 host for the Source SDK 2013 multiplayer game. Game
logic is ported from Valve's C++ into C#, with only the native code the engine
needs. This repo holds our code only: the DLLs build from the trimmed Valve
headers in `vendor/`, and everything else Valve comes from a pristine clone of
upstream (see "The Valve tree"). Each rule carries its reason: read the reason
before you work around a rule.

## Core rules

- **Commit after every step.** One reviewable unit per commit. A replacement
  and the deletion of the thing it replaced are separate commits.
- **Test all logic: xUnit `[Fact]` for C#, gtest for C++.** One behaviour per
  fact. Never use the `Run(Action<string,bool>)` shape: it compiles and xUnit
  never runs it.
- **The build must compile and produce correct output after every change.**
- **Maximise C#, minimise C++.** The ideal C++ diff for a port is one
  `#ifndef SOURCESHARP`. When unsure what the logic should do, read the stock
  C++ in the Valve clone (`../source-sdk-2013-valve`, `valve-sdk.lock`).
- **Work autonomously through a plan.** Don't stop between phases or ask
  "should I continue?". Stop only for ambiguous requirements that change what
  gets built, destructive or outward-facing actions (push, publish, delete),
  and checkpoints the plan names.
- **Keep output small.** Pipe builds and greps through `head`/`wc -l`, or write
  them to a file. Keep state that must survive compaction in a scratch file on
  disk.
- **Plans live in `docs/plans/` and stay uncommitted until the user has
  reviewed them.** A request for a plan is a request for the plan document
  only: start implementing only when told (the client-novalve plan was
  approved with "dont start"). Grep or read every path, type and API a plan
  cites before presenting it, because plans have cited a wrong `UiLoader.cs`
  and an invented `Crossings.Get`. Record the user's rulings, dated, in the
  plan's Decisions section.
- New lessons go in this project's Claude memory directory.

## Terms and environment

- **`SendUserMessage` is the game's user-message API** (see
  `EngineApiCensusTests.cs`), not Claude's `SendMessage` tool. It has been
  misread as the tool.
- **The .NET is Microsoft's, in `~/.dotnet`**, and `make setup` provisions
  it. Ubuntu's packaged `/usr/lib/dotnet` (on PATH as `/usr/bin/dotnet`) has a
  `libcoreclr.so` that crashed: a crash under it is not evidence about our
  code.
- Packet capture has not been blocked here. Try a capture before claiming it
  needs root or sudo.

## The Valve tree

| Where | Holds (classes in `docs/move/ledger.tsv`) |
|---|---|
| this repo | `OURS`, `OURS-OVERLAY` (our files at Valve paths: `src/<module>/native/sdk/...`, `tools/build/`), `MOD-COPY` (`game/mod_test/`), and `vendor/`: the Valve headers server.so and client.so build from |
| the Valve clone (`../source-sdk-2013-valve`, `valve-sdk.lock`) | stock upstream, unedited: what the farm links, what tier1, mathlib and libss_particles build from, and what the oracles read |

- **`make setup` clones the Valve clone at `valve-sdk.lock`** (`VALVE_SDK=`
  elsewhere) and links the farm from it; it refuses a clone at another commit
  or with edits. There is no ROOT and no patch series (dropped 2026-10-27):
  nothing builds from the fork's edits. A Valve header the DLLs need changed
  is edited in `vendor/` (within the engine barrier); a stock file is never
  edited.
- **Never write to the fork** (`../source-sdk-2013` or any clone or worktree
  of it) or to the Valve clone: no commits, branches, config or worktrees
  (user ruling 2026-10-04). Builds only read the clone.
- **`sdk-links/*.txt` is the list of Valve files the farm links**, split by
  consumer (client, server, shared, libs, tests); all but tests.txt are empty.
  `make sdk-census` says which list a file belongs in. Nothing under
  `src/sourcesharp/` is ever linked from the clone.
- **A test that checks a port against stock code is an oracle** and reads
  the clone (`src/test/oracle/`), never the farm: the farm holds only what the
  tests compile.
- After any build or test run, `git -C ../source-sdk-2013-valve status
  --porcelain` must be empty. A write into it means a build output was linked
  instead of built.

## Layout and build

| Path | What |
|---|---|
| `farm/<config>/` | The compat farm: the old single-repo layout as links (`make setup`). CMake compiles from `farm/<config>/src` into `farm/<config>/build`; tests run with cwd `farm/<config>/src`. Rules in `tools/setup`'s header. |
| `bin/<config>/` | The game dir: `source#`, `mods.yaml`, `bin/{linux64,managed,dotnet}`, `mods/test/` (its `console.log`). |
| `bin/<config>/cpptest`, `tools/.test-legs/` | Native test binaries; per-leg logs of `make test`. |
| `farm/<config>/build-oracle`, `bin/<config>/cpptest-oracle` | The oracle tests' own build tree (`oracle-<config>` preset) and binaries, so the native and oracle legs run at once. |
| `src/<module>/{managed,native}` | Our code by module. |
| `docs/move/` | The move ledger, header rulings, overrides. |

```
make setup                           make debug | make release
make test [CONFIG=release]           make check    (debug + test-full, before merging to main)
make test-full                       make census-test | make cpp-test | make managed-test
make live-gate (daily, tools/daily-gate) make run | make headless | make run-debug
```

- The native build is CMake + Ninja on the host (clang 22 + lld): root
  `CMakeLists.txt`, `CMakePresets.json`, `cmake/`, `src/*/native/CMakeLists.txt`,
  `src/test/native/CMakeLists.txt` (ctest). Read those, not notes. `make ci-build`
  runs the same project in the sniper container (gcc, GLIBC <= 2.31 check).
  Build one target alone: `ninja -C farm/<config>/build server_sourcesharp`.
  One native test: `farm/<config>/src/buildcpptests NAME`. What a DLL compiles
  and links is `farm/<config>/build/compile_commands.json` and `build.ninja`,
  read through `tools/cmakegraph.py` / `cmake_graph.h` / `CMakeGraph.cs`.
- `source#` reads `mods.yaml` beside it: no `-game`. In make, write
  `./source\#` (`tools/launcher-name-test` checks every launch path).
- Tests find files through `RepoLayout`: `Repo(...)` is this repo, `Sdk(...)`
  the farm (`$SOURCESHARP_SDK_TREE` overrides).
- **A managed change is deployed by `dotnet build`**, into
  `bin/<config>/bin/managed`. Native-only checks go green while the game runs
  old C#. Confirm the deploy by the HUD build stamp or `tools/debug-api status`.
- **`make check` builds debug only.** Anything reading `farm/release/build`
  is stale unless you ran `make release` in this checkout.
- `make debug GC_SECTIONS=0` must keep working. It diagnoses "a symbol
  vanished without a link error"; run it after evicting a `.cpp`. `tools/nogc-relink`
  (run by `make test`) asks the same of the normal build in a second.
- A fresh worktree needs `make setup`. It also provisions the .NET runtime;
  without it the game starts, then segfaults, which looks like a broken port.
  `ss_sandbox.bsp` is per-tree build output: never copy it between trees (a
  copied map drifted 216 changes behind its generator). Never seed
  `steam_appid.txt`: the launcher writes it.
- A target's source list is its CMakeLists.txt; the reason a file left it
  goes in docs/server_evictions.md / client_evictions.md, never in a CMake
  comment (a list names what it builds, not what it used to).
  After reordering sources, the daily gate covers the new static-init order (a
  new object order is a new one); read its next report before building on it.
- **Every native file is in one clang-format style** (`.clang-format`, no
  per-file settings; user 2026-10-26). Run `tools/clang-format --write`
  before committing C or C++; the routine `clang-format` check fails an
  unformatted file. Generators write through it, so regenerate rather than
  hand-format a generated file.
- **A native function added or removed is a reviewed change:**
  `tools/native-symbols` lists what server.so and client.so define
  (`docs/native-symbols.tsv`); record it with `--write` in the same commit.
  Tests that read native source as text were dropped (user 2026-10-26); don't
  add new ones.
- Never edit a Valve source to satisfy clang: fix our code, or a named
  `-Wno-...` on the Valve target/source with a comment. Zero warnings.
- **The object directory is not the link line.** Ninja keeps stale `.o` files
  of evicted TUs; enumerate from the graph.
- **Many TUs sharing one class's undefined symbols usually means a missing key
  function**, not the PCH. After touching a shared header, re-run a failing
  build once before believing it: the PCH may be stale.

## Valve attribution

- A new file that ports or translates Valve logic, or native code built
  against SDK headers, starts with `tools/valve-header.txt` byte for byte. Add
  a row to `docs/valve-header-new.tsv` (`path<TAB>reason`) citing the source,
  e.g. `ports CGameMovement::FullWalkMove (gamemovement.cpp)`. Moved files are
  ruled in `docs/move/header-rulings.tsv`. Original work and behaviour-only
  tests get no header.
- `make test`'s checks leg (`tools/checks.tsv`) enforces this: `tools/valve-header check` and
  `tools/valve-text-audit`, which must stay at zero findings. The header does
  not license a copy: verbatim Valve code belongs in `vendor/` or the clone.

## Porting rules

- **When managed code cannot express something:** find it on the existing ABI
  first, then add a *generic* ABI call, else leave it out as a divergence.
  Native code may carry out a decision; it must never make one.
- **New managed→native surface is a forwarder row, never a vtable.** No
  vtable crosses between native and managed (user ruling 2026-10-18): managed
  calls a native interface through the forwarder table tools/engine-forwarders
  generates from its header (an `IShim<Area>` method is a row in
  `docs/engineapi-areas.tsv`, an engine method a `FORWARD` row), and native
  calls managed through a C table tools/managed-areas generates from
  `docs/managed-interfaces.tsv`. A Valve interface that Valve code calls and
  managed answers is a libss_fwd class over a managed table (`fwd_physics.cpp`,
  `fwd_steam.cpp`).
  - Why: MSVC and Itanium lay vtables out differently, so managed code that
    indexed one had to know the C++ ABI. A forwarder lets the C++ compiler do
    the dispatch (`docs/plans/plan_engine_forwarders.md`,
    `docs/plans/plan_forwarders_everywhere.md`).
  - A call is `_fwd->Method(_self, ...)` through the interface's table
    (`Forwarders.TryAcquire`); vphysics objects go through `VPhysicsTables`,
    injected in the constructor.
  - Slots only append, because the bind checks only a count: a method joins
    the END of its row, then run `tools/engine-forwarders --write` (and
    `tools/shim-areas --write`, `tools/managed-areas --write` for their rows)
    from the farm. `--reorder` is a deliberate break that rebuilds both sides:
    name it in the commit message.
  - Tables are per side (`libss_fwd_server`, `libss_fwd_client`, in
    `src/fwd/native`). A row's side is where its users compile. Server-only
    wrappers live in Server.Interop.
  - Never add a free `SourceSharp_*` function that finds its state through a
    global.
- **Managed code keeps no state in statics; inject it as a service.** The
  build enforces it: SS0001 (`src/analyzers`) fails a mutable static in a
  runtime assembly, SS0002 a waiver row whose static has gone. The one waiver
  list is `src/analyzers/static-state-waivers.txt` (constant tables and
  stateless null objects only). `StaticStateCensusTests` keeps the rule over
  IL as the analyzer tests' oracle.
- **Move first, measure after.** Stay native only when the engine calls a C++
  vtable, reads a struct by layout, or holds a pointer. "It might be slow"
  needs a measurement. A blown budget is a boundary-design task (batching,
  `[SuppressGCTransition]`), never a retreat to C++. The performance pass comes
  after the port; record costs with numbers.
- **The ABI is generated tables; there is no version number** (dropped
  2026-10-01). Shim areas and managed-built interfaces are rows in
  `docs/engineapi-areas.tsv` and `docs/managed-interfaces.tsv`; regenerate both
  sides from them together. A forwarder table only appends (managed refuses
  one with fewer slots than its struct reads); native validates each
  managed-built slot by hash; struct layouts are held only by the parity
  tests. After an ABI merge, check
  brace balance, per-parent line counts, `<summary>` balance and
  `AbiParityTests`: a keep-both splice eats lines.
- **`*.Interface` and `*.Interfaces` assemblies hold only interfaces, enums
  and pure data structs** (the engine seams' `SourceSharp.Server.Interface`
  and `SourceSharp.Client.Interface`, with `src/shared/interface` compiled into
  both; the dev API's `Debug.Api.Server.Interfaces` and
  `Debug.Api.Client.Interfaces`). No default interface bodies, no static
  initializers: a seam type's behaviour goes in the interop as extension
  methods or a static class beside it. UI types, implementations and logic go
  in an assembly of their own: putting UI types in the client interface forced
  a plan to be restructured.
- **Logic reaches the engine only through seams, and tests hold seams.**
  Game logic (the server, the client, the UI, the mods) never names
  `EngineSide`, `ModuleEngine`, a shim area class, a forwarder struct or an
  implementation of a seam interface. The two composition roots
  (`ServerModule`, `ClientModule`) are the exception: they build the
  implementations over their own engine and register them. A test reaches an
  implementation through its side's registration (`SideBindings.Of`, a root's
  `Services`) and holds the interface. `SeamCensusTests` and each test
  project's `SeamCensus*Tests` hold both rules over IL
  (`src/test/common/SeamCensus.cs`). Why: an adapter that took the whole side
  depended on every area, and a test that named one could not see the
  registration break (`docs/plans/plan_shim_areas_split.md`).
- **A shim area is handed to the code that calls it; nothing holds the areas
  as a set.** A binding or adapter takes the `*Area` classes it calls by
  constructor, and `EngineServices.Build` fills them from the side's
  registration (`SideAttachment.Areas`), which only composition reads (a static
  factory such as `For`, `ModuleEngine`); a constructor never looks one up. `EngineSide` keeps only the
  areas its own services read (its log, globals, model names, break models and
  motion host). `ShimAreaInjectionCensusTests` holds all three. Why: the
  containers (`ShimAreas`, `ClientShimAreas`) let every binding reach every
  area through its side, so nothing said what each one depended on.
- **Consumers hold a shim area by its generated interface (`IEngineArea`,
  ...), and nothing checks whether the engine is attached** (user ruling
  2026-10-20). No `ShimsAttached`, no `IsInitialized` guard, no null area:
  the game composes only after the attach, and a side before it throws on a
  call, naming the area. A test with no engine builds its side with
  `TestSides.Create` (and a registry with `TestEngines.Server()`), over the
  generated default areas (`src/test/support/ShimAreaDefaults.g.cs`, from
  `tools/shim-areas`), which answer good defaults; a fake root's unserved
  slots answer the same (`src/test/common/DefaultSlots.cs`). Why: the guards
  made every binding take its side only to ask a question production always
  answers yes, and hid in tests what a missing engine really does.
- **Engine interfaces (`IFwd*`, `IUniformRandom` and the slices the same
  objects answer) are injected, never null** (user ruling 2026-10-20).
  `EngineServices.AddForwarders` acquires each through the side's bound slot
  and registers the object the slot holds, before the area bindings. A side
  composes after its root is attached, inside the game DLL's Init, which
  holds the engine's factories by then, so one the engine refuses fails the
  composition there, naming it. A consumer takes the interface by
  constructor: no `Func<IFwdX?>`, no null branch, no public `Acquire`
  answering null, and no `EngineBound.X(side)` lookup outside the
  acquisition layer (`EngineBoundCallerCensus` reads the IL). A binding's
  constructor names the interfaces it calls; an `IVEngineServer` member is the
  server partial's, and the client's partial answers it as a client engine
  does. A test side holds defaults that answer nothing
  (`src/test/support/DefaultForwarders.cs`); a fake root serves every engine
  interface and forwarder table with default slots, as it serves the areas,
  and a fact refuses one with `FakeShimRoot.Refuse`.
  Only the acquisition layer (each wrapper's internal `Acquire`, the bound
  slots) sees a null; `NullableEngineInterfaceCensus` holds that over both
  sides' assemblies. A diagnostic that reports a refusal asks through a probe
  in the interop that answers a line (`ServerEngineInterfaces`,
  `IFwdEngineClientProbe`).
- **An interop constructor saves its parameters into fields and does nothing
  else** (Abi, Server.Interop, Client.Interop; user ruling 2026-10-20). What
  it would look up, acquire or build is handed to it, and a static factory
  (`For`, `Of`, `Create`, `Acquire`) does that work once, where a reader can
  see it. Allowed beside the stores: the object's own empty state (an empty
  collection, an array, a scratch block, and in a parameterless constructor
  a lambda or a layer object made from constants), chaining to another
  constructor with a default, and argument checks that throw. Value types,
  attributes and exceptions are exempt. `ConstructorCensusTests` reads every
  constructor's IL. Why: a constructor that reached into the side hid what
  the class depended on, the same way the area containers did.
- **The invalid entity handle is `0xFFFFFFFF`, never 0**, because entry 0 is the
  world. `EntityHandle` stores the raw handle inverted, so `default(EntityHandle)`
  IS `EntityHandle.None`; the raw handle 0 (entry 0) must be named on purpose.
  Its bits are not the raw handle: hand native `.Value`, never a pinned run of
  handles (`EntityHandle.CopyRaw` / `RawView` + `DecodeRawInPlace`).
- A client-side engine read that is wired only on the server returns
  plausible zeros. Check `SideAuditTests`' waiver list.
- For a member of a shared `.cpp`, check both `baseentity.h` and
  `c_baseentity.h`: they can differ in `virtual`, `const` and defaults.
- The engine requests some interfaces by name only; `nm` cannot see those
  consumers. Search the engine binaries for the version string before porting
  or deleting an exposed interface.
- Sandbox coverage (`Sandbox.cs`) is one pass per porting batch, after the
  ports land.
- **UI screens** (`src/client/ui/`): `docs/ui/authoring.md` and the `ui-screen` skill.

## Tests and mutation proofs

- Run tests through make. `make check` (debug + `make test-full`, every leg)
  is needed only before merging to main. Between commits, run the tests the change touches:
  `make managed-test FILTER='FullyQualifiedName~X'` for managed ones (it runs
  all three projects: `src/test/server` holds the server's facts,
  `src/test/client` the client's, `src/test/managed` the cross-side ones, with
  helpers in `src/test/common`; `make test` runs each as its own leg),
  `make cpp-test` or `farm/<config>/src/buildcpptests NAME` for native ones
  (`make census-test` for the census tier).
  The Makefile owns the MSBuild settings, so don't set node or server options
  by hand. A run that hangs is a bug in the tests: fix it, never work around
  it. Judge a run by its `Failed: N` summary: `[FAIL]` lines appear only on a
  TTY. Zero warnings before a phase is complete.
- **Two tiers, and each leg declares its size** (`tools/leg-floor`); a leg
  far smaller than usual was not measured. `make test` is the routine tier
  (about 18 s warm): checks (`tools/checks.tsv`, run at once by
  `tools/run-checks`), managed, managed-server, managed-client, tf2_descent,
  native, ui. `make test-full` (about 40 s) adds runner and
  checks-full (the instruments' self-tests), oracle-managed, oracle-native
  and census (the native tests labelled by `ss_census()`, 2-15 s each).
  Both build the solution once and run the managed legs from the built
  assemblies. At 3375b841f: native 133 binaries / 1,652 gtests / 7
  must-not-compile, census 3 / 32, managed 2,164 + server 8,355 + client
  3,555, tf2_descent 172, tf2 3, oracle 231 + 37 binaries / 157, ui 490,
  checks 31 + 3. A check that is slow and changes only with its own subject goes in
  the full tier; nothing leaves both. One `make test` per worktree at a time:
  a concurrent run makes the other's legs report `LOG LOST`.
- **A quick local loop: `make test-fast`**, which is `make test` without the
  slow tests that `tools/test-tiers.tsv` lists (a managed fact or native
  binary of 2 s or more, a class whose facts add up to 20 s). `make test-slow`
  runs them alone, so the two together are `make test`. Locally neither is a
  gate: `make test` and `make check` run everything. CI runs each tier in jobs
  of its own (`make managed-legs-fast|slow`, `make check NATIVE_TIER=fast`,
  `make cpp-test-slow`), which between them run every test. Regenerate the list with
  `tools/test-tiers measure --write`; a fast run names any test that grew past
  the threshold.
- Every bug fix lands with a regression test that fails before the fix.
- **A check that cannot fail is not evidence.** More than fifty were found.
  - Break the subject and see red.
  - A missing, stale or unreadable input is a failure, never a pass. The only
    exception: per-tree build output such as the sandbox map may skip.
  - Prove the setup took effect: before ≠ after, and a refusal test first shows
    the unpatched input is accepted.
  - Anchor needles on whole constructs; a substring can't see text added
    around it.
  - Verify "X did not change" by diffing extracted values, not by a negative
    grep.
  - Make sure the stimulus reaches the subject: require a non-zero in the
    positive arm.
- **Exit statuses die in wrappers** (`| tail`, `; echo`). Use
  `cmd > log 2>&1; rc=$?` or `pipefail`. Stamp logs another check will read with
  `TIP=$(git rev-parse HEAD)` and the exit code.
- **A count cannot tell restored from rebuilt.** Gate persistence and caching on
  a differential, with a negative control that a plain rebuild fails.
- After a scripted edit over many files, count what must survive (assertion
  descriptions) per file, before and after. A converter once deleted 149
  asserts and the suite went green.
- **Mutation harnesses:**
  - Snapshot with `cp` and restore from the snapshot in a `finally`/`trap`.
    Never restore with `git checkout --`: it deleted uncommitted work.
  - Restore with a fresh mtime (never `copy2`, `cp -p`, `cp -a`), or
    MSBuild/ninja skip the rebuild. Clear `__pycache__`. Confirm the rebuild
    happened.
  - Hash before and after to prove the mutation landed. Target by line, and
    never key expected results on a population count.
- Committed harnesses find their root from their own location, never from an
  absolute worktree path.

## Live gates and running the game

- **The live gate runs once a day** (user ruling 2026-10-04):
  `tools/daily-gate`, from the systemd user timer in `tools/systemd/` (04:17;
  install commands in the service file). It gates main's tip in its own
  worktree, confirms a red step over 3 passes (under 2 of 3 is FLAKY), and
  bisects a confirmed one from the last green sha, naming the first bad
  commit in `~/.local/state/sourcesharp/daily-gate/` (`tools/daily-gate
  --status`). Not per commit, not before a merge: an agent runs `make
  live-gate` by hand only when the user asks, or to diagnose a failure a
  daily report shows (`tools/live-gate --steps <ids> --passes 3`, or
  `tools/3f-gatebisect --steps <id> --rounds 3`).
- `make live-gate` loads `dm_lockdown` (485 entities / 63 classes), then
  changes and reloads maps in one process. **Keep the second map load in any
  gate**: reload bugs are invisible to single-load gates.
- **Zero `[SourceSharp]` lines and a death in swapchain creation** means the GPU
  offload variables are missing (every launch sources `tools/gpu-offload`) or
  the NVIDIA driver was upgraded under a running box (only a reboot fixes it).
- **Concurrent games** each need their own `TMPDIR`, `DEBUG_API_DIR`,
  `DEBUG_API_UI_PORT` and `-port`. No lock: the user rejected serialising runs.
  The engine silently binds a higher port when its port is taken; read the real
  one from `/proc`. Every local launch carries `+sv_lan 1 -hushsteam +fps_max
  60` (Makefile `LOCAL_GAME_ARGS`, `tools/local_launch.py`; `STEAM_AUTH=1`
  drops the first two for the Steam-auth features): a LAN-only listen server
  with no Steam game server. Joins need no lease: 30 rounds of 4 or 6
  concurrent games (2026-10-05, at 8febc29c), 249 joins with and without
  `-hushsteam`, logged no Steam ticket failure. A game that never opens its
  dev API socket, with its window Iconic and its main thread spinning in
  `XGetWindowAttributes`, was minimised by KWin under
  `SDL_SetWindowBordered`: `tools/kwin-bg-rule` must never minimise, and
  `tools/kwin-bg-rule --probe` checks the installed rule (docs/todo.md).
  Tools and `make headless` pass `-instance`, a private game dir under
  `/tmp/ssi-<user>/<pid>` (never TMPDIR), so games never share the engine's
  rewritten `videoconfig_linux.cfg`. libtier0 adds up the lengths of argv[0]
  and every argument and exits past 512 (`command line too long, 512 max`,
  no socket). `source#` refuses such a line itself, naming the longest
  arguments, and logs `engine command line: N of 512 bytes`. Our own flags
  (`-debugapi`, `-assertlog`, ...) reach the game in `SOURCESHARP_ARGS`, an
  instance's `+commands` in its `cfg/sslaunch.cfg`. `@file` is no way round
  the limit: this engine makes the whole file one parameter.
  Assert the join before trusting any client reading.
- `tools/debug-api log` is managed-only. Native output is
  `tools/debug-api console` (`/console`, for a game started with `-condebug`)
  or `bin/<config>/mods/test/console.log`. `ent_fire` needs cheats and a
  `CBasePlayer`, so it can report ok and do nothing: use `/entity/input`. A
  datatable change is verified only by a full join (`make headless`, wait for
  `[SourceSharp/Hud] first paint`, write on the server, read on the client).
- Before deleting a binding or registration, read the teardown path: static
  instruments said "unused" for the only thing that destroyed an object.
- **Killing processes:** by PID after checking `ps -o args=`. Never `pkill -f`
  or a class-wide pattern. Count with `pgrep -x NAME | wc -l`. Don't wrap
  `tools/debug-api run` in `timeout`.
- **Intermittent faults need numbers:** state trials, regime and P(0 | old
  rate). Attribute crashes by commit, not by worktree path.

## Git, worktrees and lanes

- **main is linear: no merge commits.** Land work by rebasing it onto main and
  fast-forwarding (`git merge --ff-only`). To update a lane, rebase it; never
  merge main into it. main's root is GitHub's first commit (`9e051b63`), so a
  push is a fast-forward. The pre-2026-10-04 history is under the tag
  `archive/pre-squash`, and shas quoted from before then resolve there.
- Multi-phase or delegated work happens in a dedicated `git worktree`. Commit
  before any rebase. Never `git add -A`; stage explicit paths. Never push.
- **Anchor a tip before rewinding it:** tag (`git tag archive/<name> <sha>`)
  or otherwise pin every commit a rebase/rewind makes unreachable, before the
  rewind. The reflog is not persistence — it expires and is local-only (the
  2026-10-10 bones lane lost blend-facts/purge/trim commits to exactly that).
- A branch checked out in a peer's worktree can't be moved from here: rebase
  onto their tip, verify, and hand over `git merge --ff-only <branch>` with the
  counts.
- **Retire a finished or abandoned branch** by tagging it `archive/<name>` and
  deleting the branch and its worktree. Don't leave stale branches or
  worktrees behind. Repo-wide git work (audits, cleanups) covers this repo
  only; ask before touching another repo.
- **Delegate by file ownership**, and serialise the ABI tail and build scripts.
  Relay agents' conclusions: the user does not see them.
- **Worktree-agent briefs:** name the base sha and have the agent check it
  first (`git log --oneline -1`). Give it its own `make setup`, commit after
  each step, prefix its scratch files, and pick game instances by its own
  process tree. Agent worktrees are cut from the default branch, not yours.
- **A brief's premises are load-bearing:** cite facts, quote numbers with their
  commit, and have lanes re-measure at the current tip. Let "no code needed"
  be an acceptable result.
- Every removal gate needs a retention half that doing less cannot satisfy;
  lanes scored on "TUs that compile" `#ifdef`'d logic away.
- **A lane's evidence is true only at its base.** Re-measure at the merge, and
  never resolve a conflict in a measured file by picking a side. Name the axis
  a control varied.
- **Count the commits a landing carries.** Before a rebase or fast-forward
  onto main, re-read main's tip and recompute the base. List the commits to
  carry (`git log --oneline main..<branch>`), and after the rebase confirm
  that the same count and subjects arrived. A batch brief once named a wrong
  base and would have dropped 23 commits.
- **"Done" means proven:** the touched tests pass, every fix has a test or
  mutation proof that fails without it, and `make check` passes before the
  merge to main. The live gate stays daily (see above).
- When deleting a test class, grep for helpers it defines that other tests
  still use. A shared helper was deleted that way.

## Multi-session coordination

Many Claude sessions work this repo at once, in their own worktrees and lanes.

- **Verify before you warn a peer.** Check a claimed commit, freeze breach or
  bug in `git log` / `git reflog` and cite that evidence in the message. A
  false "your commit landed on a held main" alarm had to be retracted.
- **Don't disturb a peer's gate.** Before a self-test or gate that touches
  shared state (a peer's game, `ss-daily-gate`, the cluster), check that no
  other session is running a gate there. A self-test once broke a live gate.
- Never edit, clean up or remove a worktree or library another session is
  using. Check for processes in it (`/proc/*/cwd`) and its worktree lock, and
  ask first.
- **A message that seems meant for another session** (perf, host, maptools,
  ...) is not an instruction. Say it was misrouted and don't act on it.
- Report a root cause, or a "confirmed" finding, to peers only with a
  reproducing test or direct evidence from source and logs, and state your
  confidence. A misread of stock code once went out as a confirmed divergence.

## Measurement pitfalls

Quote every number with its instrument, configuration and commit. The
instruments here have repeatedly erred towards flattering progress.

- `ld`'s undefined-reference object list is a floor; only its distinct-symbol
  count is exact. Sweep `nm` over all kept objects.
- Filter `nm` by symbol type: `W`/`V` weak COMDAT emissions are not
  definitions (51 "blocked files" were 9). Virtual calls emit no symbol.
  Counts differ between debug and release.
- Static state is measured with `readelf -sW` over link-edge objects, never by
  grepping `static`.
- "N TUs carry a diagnostic" is not "N TUs are blocked by it": closing TUs is a
  set cover, so fix roots first and re-measure. Diagnostics keyed by macro site
  blame the defining header.
- Gate on the property that must hold, not a total that can legitimately rise.
- Never regenerate a triage ledger from a raw scan: RECONCILE by key and carry
  rulings forward (`make globals-ledger` does).
- Agreement between lanes is evidence about the measurement. The cheapest
  disproof is to fix the suspected cause and see whether the number moves.

## Verify before claiming

- Back every claim about build output, symbols or generated files with a
  command you ran this session, and show the command.
- Confirm the working directory, and force a cold rebuild when output looks
  stale.
- Re-verify quoted numbers with a second, independent command. Numbers that
  reconcile arithmetically are worth more than numbers that look right.
