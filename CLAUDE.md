# SourceSharp: rules for Claude Code sessions

SourceSharp is a .NET 10 host for the Source SDK 2013 multiplayer game: game
logic ported from Valve's C++ into C#, with only the native code the engine
needs. This repo holds our code only: the DLLs build from the trimmed Valve
headers in `vendor/`, and everything else Valve comes from a pristine upstream
clone (see "The Valve tree"). Each rule carries its reason: read it before you
work around the rule.

## Core rules

- **Commit after every step**, one reviewable unit each. A replacement and the
  deletion of what it replaced are separate commits.
- **Test all logic**: xUnit `[Fact]` for C#, gtest for C++, one behaviour per
  fact. Never the `Run(Action<string,bool>)` shape: it compiles and xUnit never
  runs it.
- **Every change compiles and produces correct output.**
- **Maximise C#, minimise C++.** The ideal C++ diff for a port is one
  `#ifndef SOURCESHARP`. When unsure what the logic should do, read the stock
  C++ in the Valve clone (`../source-sdk-2013-valve`, `valve-sdk.lock`).
- **Work autonomously through a plan.** Don't ask "should I continue?". Stop
  only for ambiguous requirements that change what gets built, destructive or
  outward-facing actions (push, publish, delete), and checkpoints the plan names.
- **Keep output small**: pipe through `head`/`wc -l` or into a file. State that
  must survive compaction goes in a scratch file on disk.
- **Plans live in `docs/plans/`, uncommitted until the user has reviewed
  them.** A request for a plan is for the document only; implement only when
  told ("dont start"). Grep every path, type and API a plan cites before
  presenting it (plans have cited a wrong `UiLoader.cs` and an invented
  `Crossings.Get`). Record the user's rulings, dated, in its Decisions section.
- New lessons go in this project's Claude memory directory.

## Terms and environment

- **`SendUserMessage` is the game's user-message API** (`EngineApiCensusTests.cs`),
  not Claude's `SendMessage` tool.
- **.NET is Microsoft's, in `~/.dotnet`** (`make setup` provisions it).
  Ubuntu's `/usr/bin/dotnet` has a `libcoreclr.so` that crashed: a crash under
  it is not evidence about our code.
- Packet capture is not blocked here: try one before claiming it needs root.

## The Valve tree

| Where | Holds (classes in `docs/move/ledger.tsv`) |
|---|---|
| this repo | `OURS`, `OURS-OVERLAY` (our files at Valve paths: `src/<module>/native/sdk/...`, `tools/build/`), `MOD-COPY` (`game/mod_test/`), and `vendor/`: the Valve headers server.so and client.so build from |
| the Valve clone (`../source-sdk-2013-valve`, `valve-sdk.lock`) | stock upstream, unedited: what the farm links, what tier1, mathlib and libss_particles build from, and what the oracles read |

- `make setup` clones the Valve clone at `valve-sdk.lock` (`VALVE_SDK=`
  elsewhere), links the farm from it, and refuses a clone at another commit or
  with edits. No ROOT, no patch series (dropped 2026-10-27). A Valve header the
  DLLs need changed is edited in `vendor/` (within the engine barrier); a stock
  file is never edited.
- **Never write to the fork** (`../source-sdk-2013`, any clone or worktree of
  it) or the Valve clone: no commits, branches, config or worktrees (user
  2026-10-04). Builds only read the clone.
- `sdk-links/*.txt` lists the Valve files the farm links, by consumer (client,
  server, shared, libs, tests); all but tests.txt are empty. `make sdk-census`
  says which list a file belongs in. Nothing under `src/sourcesharp/` is linked
  from the clone.
- **An oracle** (a test checking a port against stock) reads the clone
  (`src/test/oracle/`), never the farm, which holds only what tests compile.
- After any build or test run, `git -C ../source-sdk-2013-valve status
  --porcelain` must be empty: a write there means a build output was linked
  instead of built.

## Layout and build

| Path | What |
|---|---|
| `farm/<config>/` | The compat farm: the old single-repo layout as links (`make setup`). CMake compiles from `farm/<config>/src` into `farm/<config>/build`; tests run with cwd `farm/<config>/src`. Rules in `tools/setup`'s header. |
| `bin/<config>/` | The game dir: `source#`, `mods.yaml`, `bin/{linux64,managed,dotnet}`, `mods/test/` (its `console.log`). |
| `bin/<config>/cpptest`, `tools/.test-legs/` | Native test binaries; per-leg logs of `make test`. |
| `farm/<config>/build-oracle`, `bin/<config>/cpptest-oracle` | The oracle tests' build tree (`oracle-<config>` preset) and binaries, so native and oracle legs run at once. |
| `src/<module>/{managed,native}` | Our code by module. |
| `docs/move/` | The move ledger, header rulings, overrides. |

```
make setup                           make debug | make release
make test [CONFIG=release]           make check    (debug + test-full, before merging to main)
make test-full                       make census-test | make cpp-test | make managed-test
make live-gate (daily, tools/daily-gate) make run | make headless | make run-debug
```

- Native build: CMake + Ninja on the host (clang 22 + lld): root
  `CMakeLists.txt`, `CMakePresets.json`, `cmake/`, `src/*/native/CMakeLists.txt`,
  `src/test/native/CMakeLists.txt` (ctest). Read those, not notes. `make
  ci-build` builds in the sniper container (gcc, GLIBC <= 2.31 check). One
  target: `ninja -C farm/<config>/build server_sourcesharp`; one native test:
  `farm/<config>/src/buildcpptests NAME`. What a DLL compiles and links is
  `compile_commands.json` and `build.ninja`, read through `tools/cmakegraph.py`
  / `cmake_graph.h` / `CMakeGraph.cs`.
- `source#` reads `mods.yaml` beside it (no `-game`). In make write
  `./source\#` (`tools/launcher-name-test` checks every launch path).
- Tests find files through `RepoLayout`: `Repo(...)` is this repo, `Sdk(...)`
  the farm (`$SOURCESHARP_SDK_TREE` overrides).
- **A managed change deploys by `dotnet build`** into `bin/<config>/bin/managed`;
  native-only checks go green while the game runs old C#. Confirm by the HUD
  build stamp or `tools/debug-api status`.
- **`make check` builds debug only**: `farm/release/build` is stale unless you
  ran `make release` in this checkout.
- `make debug GC_SECTIONS=0` must keep working: it diagnoses "a symbol vanished
  without a link error". Run it after evicting a `.cpp`; `tools/nogc-relink`
  (in `make test`) asks the same of the normal build in a second.
- A fresh worktree needs `make setup`, which also provisions .NET (without it
  the game starts, then segfaults, which looks like a broken port). Never copy
  `ss_sandbox.bsp` between trees (a copied map drifted 216 changes behind its
  generator). Never seed `steam_appid.txt`: the launcher writes it.
- A target's source list is its CMakeLists.txt; why a file left it goes in
  docs/server_evictions.md / client_evictions.md, never a CMake comment. After
  reordering sources, read the daily gate's next report (a new static-init
  order) before building on it.
- **One clang-format style for every native file** (`.clang-format`, no
  per-file settings; user 2026-10-26, 2026-10-29): `char* p`, `f(a)`, `if (x)`.
  Run `tools/clang-format --write` before committing C or C++; the routine
  check fails an unformatted file. Generators write through it: regenerate,
  never hand-format a generated file.
- **server.so and client.so compile in unity batches of 32** (`SS_UNITY_BATCH`,
  cmake/SourceSharp.cmake; user 2026-11-01). A file-local name (static,
  anonymous-namespace member) is shared with its batch, and a file-scope
  using-directive reaches later files: give file-local names a file-specific
  spelling. `tools/unity-check` (routine) compiles each DLL as one TU so a
  cross-batch clash fails early. An object is a batch, so per-object
  attribution tools were removed; `-DSS_UNITY_BATCH=0` gives one TU per source
  (`tools/include-min`). Tests that read native source as text were dropped
  (user 2026-10-26): don't add new ones.
- Never edit a Valve source to satisfy clang: fix our code, or add a named
  `-Wno-...` on the Valve target/source with a comment. Zero warnings.
- **The object directory is not the link line**: ninja keeps stale `.o` files
  of evicted TUs. Enumerate from the graph.
- **Many TUs sharing one class's undefined symbols usually means a missing key
  function**, not the PCH. After touching a shared header, re-run a failing
  build once before believing it: the PCH may be stale.

## Valve attribution

- A new file that ports or translates Valve logic, or native code built
  against SDK headers, starts with `tools/valve-header.txt` byte for byte, plus
  a row in `docs/valve-header-new.tsv` (`path<TAB>reason`, e.g. `ports
  CGameMovement::FullWalkMove (gamemovement.cpp)`). Moved files are ruled in
  `docs/move/header-rulings.tsv`. Original work and behaviour-only tests get no
  header.
- `make test`'s checks leg (`tools/checks.tsv`) enforces it: `tools/valve-header
  check` and `tools/valve-text-audit`, at zero findings. The header does not
  license a copy: verbatim Valve code belongs in `vendor/` or the clone.

## Porting rules

- **When managed code cannot express something**: find it on the existing ABI,
  else add a *generic* ABI call, else leave it out as a divergence. Native code
  may carry out a decision, never make one.
- **New managed→native surface is a forwarder row, never a vtable** (user
  2026-10-18). Why: MSVC and Itanium lay vtables out differently, so managed
  code indexing one had to know the C++ ABI (`docs/plans/plan_engine_forwarders.md`,
  `docs/plans/plan_forwarders_everywhere.md`).
  - Managed calls native through the forwarder tables `tools/engine-forwarders`
    generates from headers (an `IShim<Area>` method is a row in
    `docs/engineapi-areas.tsv`, an engine method a `FORWARD` row); native calls
    managed through C tables `tools/managed-areas` generates from
    `docs/managed-interfaces.tsv`. A Valve interface that Valve code calls and
    managed answers is a libss_fwd class over a managed table (`fwd_physics.cpp`,
    `fwd_steam.cpp`).
  - A call is `_fwd->Method(_self, ...)` through `Forwarders.TryAcquire`;
    vphysics objects go through `VPhysicsTables`, injected in the constructor.
  - Slots only append (the bind checks only a count): add at the END of the
    row, then run `tools/engine-forwarders --write` (and `tools/shim-areas
    --write`, `tools/managed-areas --write` for their rows) from the farm.
    `--reorder` deliberately rebuilds both sides: name it in the commit message.
  - Tables are per side (`libss_fwd_server`, `libss_fwd_client`, in
    `src/fwd/native`); a row's side is where its users compile. Server-only
    wrappers live in Server.Interop.
  - Never add a free `SourceSharp_*` function that finds its state through a
    global.
- **Managed code keeps no state in statics; inject it as a service.** SS0001
  (`src/analyzers`) fails a mutable static in a runtime assembly, SS0002 a
  waiver whose static has gone. Waivers (constant tables, stateless null
  objects only): `src/analyzers/static-state-waivers.txt`.
  `StaticStateCensusTests` is the IL oracle.
- **Move first, measure after.** Stay native only when the engine calls a C++
  vtable, reads a struct by layout, or holds a pointer. "It might be slow"
  needs a measurement; a blown budget is a boundary-design task (batching,
  `[SuppressGCTransition]`), never a retreat to C++. The performance pass
  comes after the port; record costs with numbers.
- **The ABI is generated tables, with no version number** (dropped 2026-10-01).
  Regenerate both sides from `docs/engineapi-areas.tsv` and
  `docs/managed-interfaces.tsv` together. Managed refuses a forwarder table
  with fewer slots than its struct reads; native validates each managed-built
  slot by hash; struct layouts are held only by parity tests. After an ABI
  merge, check brace balance, per-parent line counts, `<summary>` balance and
  `AbiParityTests`: a keep-both splice eats lines.
- **`*.Interface` and `*.Interfaces` assemblies hold only interfaces, enums and
  pure data structs** (`SourceSharp.Server.Interface` and
  `SourceSharp.Client.Interface`, with `src/shared/interface` compiled into
  both; `Debug.Api.Server.Interfaces`, `Debug.Api.Client.Interfaces`). No
  default interface bodies or static initializers: behaviour goes in the
  interop as extension methods or a static class. UI types, implementations
  and logic get an assembly of their own (UI types in the client interface
  forced a plan to be restructured).
- **Logic reaches the engine only through seams, and tests hold seams.** Game
  logic (server, client, UI, mods) never names `EngineSide`, `ModuleEngine`, a
  shim area class, a forwarder struct or a seam implementation; only the
  composition roots (`ServerModule`, `ClientModule`) build and register those.
  A test reaches an implementation through its side's registration
  (`SideBindings.Of`, a root's `Services`) and holds the interface.
  `SeamCensusTests` and each project's `SeamCensus*Tests` hold this over IL
  (`src/test/common/SeamCensus.cs`). Why: an adapter that took the whole side
  depended on every area, and a test naming one couldn't see the registration
  break (`docs/plans/plan_shim_areas_split.md`).
- **A shim area is handed to the code that calls it; nothing holds the areas
  as a set.** A binding takes the `*Area` classes it calls by constructor;
  `EngineServices.Build` fills them from `SideAttachment.Areas`, which only
  composition reads (a static factory such as `For`, `ModuleEngine`).
  `EngineSide` keeps only the areas its own services read (log, globals, model
  names, break models, motion host). `ShimAreaInjectionCensusTests` holds it.
  Why: the containers (`ShimAreas`, `ClientShimAreas`) let every binding reach
  every area, hiding what each depended on.
- **Consumers hold a shim area by its generated interface (`IEngineArea`, ...),
  and nothing checks whether the engine is attached** (user 2026-10-20): no
  `ShimsAttached`, `IsInitialized` guard or null area. The game composes after
  the attach; a side before it throws on a call, naming the area. A test with
  no engine uses `TestSides.Create` (registry: `TestEngines.Server()`) over the
  generated default areas (`src/test/support/ShimAreaDefaults.g.cs`, from
  `tools/shim-areas`); a fake root's unserved slots answer the same
  (`src/test/common/DefaultSlots.cs`). Why: the guards made bindings take the
  side only to ask a question production always answers yes, and hid in tests
  what a missing engine does.
- **Engine interfaces (`IFwd*`, `IUniformRandom` and slices of the same
  objects) are injected, never null** (user 2026-10-20).
  `EngineServices.AddForwarders` acquires each through the side's bound slot
  and registers it before the area bindings; a side composes inside the game
  DLL's Init, so a refused interface fails composition there, by name.
  Consumers take the interface by constructor: no `Func<IFwdX?>`, null branch,
  public `Acquire` answering null, or `EngineBound.X(side)` outside the
  acquisition layer (`EngineBoundCallerCensus`). An `IVEngineServer` member is
  the server partial's; the client partial answers it as a client engine does.
  Test sides hold defaults that answer nothing
  (`src/test/support/DefaultForwarders.cs`); a fake root serves every engine
  interface and table with default slots, and a fact refuses one with
  `FakeShimRoot.Refuse`. Only the acquisition layer (internal `Acquire`, bound
  slots) sees null (`NullableEngineInterfaceCensus`). A diagnostic reporting a
  refusal asks a probe in the interop (`ServerEngineInterfaces`,
  `IFwdEngineClientProbe`).
- **An interop constructor saves its parameters into fields and does nothing
  else** (Abi, Server.Interop, Client.Interop; user 2026-10-20). Lookups,
  acquisition and building happen once in a static factory (`For`, `Of`,
  `Create`, `Acquire`). Also allowed: the object's own empty state (an empty
  collection, array or scratch block; in a parameterless constructor a lambda
  or a constant-built layer object), chaining to another constructor with a
  default, and argument checks that throw. Value types, attributes and
  exceptions are exempt. `ConstructorCensusTests` reads the IL. Why: a
  constructor that reached into the side hid its dependencies.
- **The invalid entity handle is `0xFFFFFFFF`, never 0** (entry 0 is the
  world). `EntityHandle` stores the raw handle inverted, so
  `default(EntityHandle)` is `EntityHandle.None`; name raw handle 0 on purpose.
  Hand native `.Value`, never a pinned run of handles (`EntityHandle.CopyRaw` /
  `RawView` + `DecodeRawInPlace`).
- A client-side engine read wired only on the server returns plausible zeros:
  check `SideAuditTests`' waiver list.
- For a member of a shared `.cpp`, check both `baseentity.h` and
  `c_baseentity.h`: `virtual`, `const` and defaults can differ.
- The engine requests some interfaces by name only, invisible to `nm`: search
  the engine binaries for the version string before porting or deleting an
  exposed interface.
- Sandbox coverage (`Sandbox.cs`) is one pass per porting batch, after the
  ports land.
- **UI screens** (`src/client/ui/`): `docs/ui/authoring.md` and the `ui-screen`
  skill.

## Tests and mutation proofs

- Run tests through make, which owns the MSBuild settings (don't set node or
  server options by hand). Between commits run what the change touches: `make
  managed-test FILTER='FullyQualifiedName~X'` (all three projects:
  `src/test/server`, `src/test/client`, cross-side `src/test/managed`; helpers
  in `src/test/common`), `make cpp-test` or `farm/<config>/src/buildcpptests
  NAME`, `make census-test`. `make check` is needed only before merging to
  main. A hanging run is a bug in the tests: fix it, never work around it.
  Judge by `Failed: N` (`[FAIL]` lines appear only on a TTY). Zero warnings
  before a phase is complete.
- **Two tiers; each leg declares its size** (`tools/leg-floor`: a leg far
  smaller than usual was not measured). `make test` (~18 s warm): checks
  (`tools/checks.tsv`, via `tools/run-checks`), managed, managed-server,
  managed-client, tf2_descent, native, ui. `make test-full` (~40 s) adds
  runner, checks-full, oracle-managed, oracle-native and census (native tests
  labelled `ss_census()`). Both build the solution once. A slow check that
  changes only with its own subject goes in the full tier; nothing leaves both.
  One `make test` per worktree at a time (a concurrent run reports `LOG LOST`).
- **`make test-fast`** is `make test` minus the slow tests in
  `tools/test-tiers.tsv` (a fact or binary of 2 s+, a class totalling 20 s+);
  `make test-slow` runs them alone. Locally neither is a gate. CI splits the
  tiers (`make managed-legs-fast|slow`, `make check NATIVE_TIER=fast`, `make
  cpp-test-slow`), covering every test. Regenerate with `tools/test-tiers
  measure --write`; a fast run names any test that grew past the threshold.
- Every bug fix lands with a regression test that fails before the fix.
- **A check that cannot fail is not evidence** (more than fifty were found):
  - Break the subject and see red.
  - A missing, stale or unreadable input is a failure, never a pass (only
    per-tree build output such as the sandbox map may skip).
  - Prove the setup took effect: before ≠ after; a refusal test first shows
    the unpatched input is accepted.
  - Anchor needles on whole constructs; a substring can't see text added
    around it.
  - Verify "X did not change" by diffing extracted values, not a negative grep.
  - Require a non-zero in the positive arm, so the stimulus reaches the subject.
- **Exit statuses die in wrappers** (`| tail`, `; echo`): use `cmd > log 2>&1;
  rc=$?` or `pipefail`. Stamp logs another check reads with `TIP=$(git
  rev-parse HEAD)` and the exit code.
- **A count cannot tell restored from rebuilt**: gate persistence and caching
  on a differential, with a negative control that a plain rebuild fails.
- After a scripted edit over many files, count what must survive (assertion
  descriptions) per file, before and after: a converter once deleted 149
  asserts and the suite went green.
- **Mutation harnesses:** snapshot with `cp` and restore from it in a
  `finally`/`trap`, never `git checkout --` (it deleted uncommitted work).
  Restore with a fresh mtime (no `copy2`, `cp -p`, `cp -a`) or MSBuild/ninja
  skip the rebuild; clear `__pycache__`; confirm the rebuild happened. Hash
  before and after to prove the mutation landed. Target by line; never key
  expected results on a population count.
- Committed harnesses find their root from their own location, never an
  absolute worktree path.

## Live gates and running the game

- **The live gate runs once a day** (user 2026-10-04): `tools/daily-gate`, from
  the systemd user timer in `tools/systemd/` (04:17; install commands in the
  service file). It gates main's tip in its own worktree, confirms a red step
  over 3 passes (under 2 of 3 is FLAKY), and reports the range it went red in
  without searching it (user 2026-11-02; by hand: `tools/daily-gate --bisect
  GOOD BAD --steps <id>`). Reports: `~/.local/state/sourcesharp/daily-gate/`
  (`tools/daily-gate --status`). Not per commit or merge: run `make live-gate`
  only when the user asks, or to diagnose a daily failure (`tools/live-gate
  --steps <ids> --passes 3`, `tools/3f-gatebisect --steps <id> --rounds 3`).
- `make live-gate` loads `dm_lockdown` (475 entities / 62 classes), then
  changes and reloads maps in one process. **Keep the second map load in any
  gate**: reload bugs are invisible to single-load gates.
- **Zero `[SourceSharp]` lines and a death in swapchain creation**: the GPU
  offload variables are missing (every launch sources `tools/gpu-offload`), or
  the NVIDIA driver was upgraded under a running box (only a reboot fixes it).
- **Concurrent games** each need their own `TMPDIR`, `DEBUG_API_DIR`,
  `DEBUG_API_UI_PORT` and `-port` (no lock: the user rejected serialising
  runs). The engine silently binds a higher port when its port is taken; read
  the real one from `/proc`.
- Every local launch carries `+sv_lan 1 -hushsteam +fps_max 60` (Makefile
  `LOCAL_GAME_ARGS`, `tools/local_launch.py`; `STEAM_AUTH=1` drops the first
  two): a LAN-only listen server. Joins need no lease (2026-10-05 at
  8febc29c: 30 rounds of 4 or 6 concurrent games, 249 joins, no Steam ticket
  failure). Assert the join before trusting any client reading.
- A game that never opens its dev API socket, with its window Iconic and its
  main thread spinning in `XGetWindowAttributes`, was minimised by KWin under
  `SDL_SetWindowBordered`: `tools/kwin-bg-rule` must never minimise (`--probe`
  checks the installed rule; docs/todo.md).
- Tools and `make headless` pass `-instance`, a private game dir under
  `/tmp/ssi-<user>/<pid>` (never TMPDIR), so games never share the engine's
  rewritten `videoconfig_linux.cfg`.
- libtier0 exits when argv[0] plus all arguments pass 512 bytes (`command line
  too long, 512 max`, no socket); `source#` refuses such a line itself, naming
  the longest arguments, and logs `engine command line: N of 512 bytes`. Our
  flags (`-debugapi`, `-assertlog`, ...) travel in `SOURCESHARP_ARGS`, an
  instance's `+commands` in its `cfg/sslaunch.cfg`. `@file` is no way round
  it: the engine makes the whole file one parameter.
- `tools/debug-api log` is managed-only; native output is `tools/debug-api
  console` (game started with `-condebug`) or `bin/<config>/mods/test/console.log`.
  `ent_fire` needs cheats and a `CBasePlayer`, so it can report ok and do
  nothing: use `/entity/input`. A datatable change is verified only by a full
  join (`make headless`, wait for `[SourceSharp/Hud] first paint`, write on the
  server, read on the client).
- Before deleting a binding or registration, read the teardown path: static
  instruments called the only thing that destroyed an object "unused".
- **Killing processes:** by PID after checking `ps -o args=`, never `pkill -f`
  or a class-wide pattern. Count with `pgrep -x NAME | wc -l`. Don't wrap
  `tools/debug-api run` in `timeout`.
- **Intermittent faults need numbers:** trials, regime and P(0 | old rate).
  Attribute crashes by commit, not by worktree path.

## Git, worktrees and lanes

- **main is linear, no merge commits**: rebase work onto main and `git merge
  --ff-only`; update a lane by rebasing, never by merging main into it. main's
  root is GitHub's first commit (`9e051b63`), so a push is a fast-forward.
  Pre-2026-10-04 history is under the tag `archive/pre-squash`, where older
  shas resolve.
- Multi-phase or delegated work happens in a dedicated `git worktree`. Commit
  before any rebase. Stage explicit paths, never `git add -A`. Never push.
- **Anchor a tip before rewinding it** (`git tag archive/<name> <sha>`) for
  every commit a rebase or rewind makes unreachable: the reflog expires and is
  local-only (the 2026-10-10 bones lane lost commits to it).
- A branch checked out in a peer's worktree can't be moved from here: rebase
  onto their tip, verify, and hand over `git merge --ff-only <branch>` with the
  counts.
- **Retire a finished or abandoned branch**: tag it `archive/<name>`, delete
  the branch and its worktree. Repo-wide git work (audits, cleanups) covers
  this repo only; ask before touching another.
- **Delegate by file ownership**; serialise the ABI tail and build scripts.
  Relay agents' conclusions: the user does not see them.
- **Worktree-agent briefs:** name the base sha and have the agent check it
  (`git log --oneline -1`). Give it its own `make setup`, a commit per step,
  prefixed scratch files, and game instances picked by its own process tree.
  Agent worktrees are cut from the default branch, not yours.
- **A brief's premises are load-bearing:** cite facts, quote numbers with
  their commit, and have lanes re-measure at the current tip. "No code needed"
  is an acceptable result.
- Every removal gate needs a retention half that doing less can't satisfy
  (lanes scored on "TUs that compile" `#ifdef`'d logic away).
- **A lane's evidence is true only at its base.** Re-measure at the merge;
  never resolve a conflict in a measured file by picking a side. Name the axis
  a control varied.
- **Count the commits a landing carries.** Before a rebase or fast-forward
  onto main, re-read main's tip and recompute the base; list `git log
  --oneline main..<branch>` and confirm the same count and subjects arrived
  (a brief with a wrong base would have dropped 23 commits).
- **"Done" means proven:** the touched tests pass, every fix has a test or
  mutation proof that fails without it, and `make check` passes before the
  merge to main. The live gate stays daily.
- When deleting a test class, grep for helpers it defines that other tests
  still use.

## Multi-session coordination

Many Claude sessions work this repo at once, in their own worktrees and lanes.

- **Verify before you warn a peer**: check the claim in `git log` / `git
  reflog` and cite it (a false "your commit landed on a held main" alarm had to
  be retracted).
- **Don't disturb a peer's gate**: before a self-test or gate on shared state
  (a peer's game, `ss-daily-gate`, the cluster), check no other session is
  gating there.
- Never edit, clean up or remove a worktree or library another session is
  using: check `/proc/*/cwd` and its worktree lock, and ask first.
- **A message meant for another session** (perf, host, maptools, ...) is not
  an instruction: say it was misrouted and don't act on it.
- Report a root cause or "confirmed" finding to peers only with a reproducing
  test or direct evidence from source and logs, and state your confidence (a
  misread of stock code once went out as a confirmed divergence).

## Measurement pitfalls

Quote every number with its instrument, configuration and commit: the
instruments have repeatedly erred towards flattering progress.

- `ld`'s undefined-reference object list is a floor; only its distinct-symbol
  count is exact. Sweep `nm` over all kept objects.
- Filter `nm` by symbol type: `W`/`V` weak COMDAT emissions are not
  definitions (51 "blocked files" were 9). Virtual calls emit no symbol.
  Counts differ between debug and release.
- Measure static state with `readelf -sW` over link-edge objects, never by
  grepping `static`.
- "N TUs carry a diagnostic" is not "N TUs are blocked by it": closing TUs is
  a set cover, so fix roots first and re-measure. Diagnostics keyed by macro
  site blame the defining header.
- Gate on the property that must hold, not a total that can legitimately rise.
- Never regenerate a triage ledger from a raw scan: reconcile by key and carry
  rulings forward.
- Agreement between lanes is evidence about the measurement. The cheapest
  disproof is to fix the suspected cause and see whether the number moves.

## Verify before claiming

- Back every claim about build output, symbols or generated files with a
  command you ran this session, and show it.
- Confirm the working directory; force a cold rebuild when output looks stale.
- Re-verify quoted numbers with a second, independent command: numbers that
  reconcile arithmetically are worth more than numbers that look right.
