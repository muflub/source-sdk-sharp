# Plan: retire EngineSide

Status: approved 2026-10-25; PR 1 (Phases 0 to 3) done, in review. Hooks
(`IHooks<T>`) are out of scope.

## Goal

`EngineSide` (`src/shared/interop/EngineSide.cs` and its per-side partials
`src/server/interop/EngineSide.Server.cs`, `src/client/interop/EngineSide.Client.cs`)
is gone. Each thing it does today has its own home: composition holds the
attach state, every service is registered and taken by constructor, and the
entity helpers live in the binding or service that calls them. Nothing holds
"the side" as a bag of everything, for the same reason the area containers
went (`docs/plans/plan_shim_areas_split.md`): a class that took the whole side
depended on all of it, and nothing said what it used.

## Where it stands

Measured at 239be41 (PR #32, the model caches off `EngineBound`, since merged):

| measure | count | command |
|---|---|---|
| lines in the three partials | 2,189 (927 shared, 1,077 server, 185 client) | `wc -l src/shared/interop/EngineSide.cs src/server/interop/EngineSide.Server.cs src/client/interop/EngineSide.Client.cs` |
| product files naming `EngineSide` | 72 (interop and the two composition roots; game logic names it only in comments) | `grep -rlw EngineSide --include=*.cs src \| grep -v test` |
| test files naming it | 71 | same, over `src/test` and `src/tf2_descent/tests` |
| binding constructors taking it | 43 | `grep -rnE 'public Engine\w+\(EngineSide side'` |
| member reads through `E.` in the three binding files | 112, of 64 distinct members | `grep -oh '\bE\.[A-Z]\w*' src/*/interop/EngineBinding*.cs` |

What it does, by job:

1. **Attach state.** `Create`, `AttachOwnShims` (per side), the shim root
   (`Root`), the area container (`Areas`, `UseAreas`), the bound slots
   (`Bound`, an `EngineBoundSet`), the globals pointer (`BindGlobalVars`,
   `AskForGlobalVars`, `RefuseGlobalVars`, `IsInitialized`, `Initialize`),
   `ManagedDirectory`, and the side's `SideState`. `SideLoad` builds and
   attaches it before `LoadModule`; `ModuleEngine` wraps it for the roots.
2. **The side's own services.** `Log` and `Lines`, `VPhysics`, `Interfaces`
   (`NativeInterfaceFactory`), the three routes (`ShellLess`, `EntityIo`,
   `Removal`), the liveness attachment (`AttachLiveness`), and on the server
   `Motion` (`EntityPhysicsMotion`). Most are already registered by
   `EngineServices.AddTo`; bindings still reach them through the side
   (`E.ShellLess` alone is 19 reads).
3. **Routes filled after composition.** Server: `ServerSounds` (11 reads),
   `Tracer`, `PlayerMoves` (5), `PhysicsInit`, `TraceCost`. Shared:
   `Teleported`. They are settable properties the module fills once its
   services exist.
4. **About 80 entity helpers**, mostly in the server half: velocity, mass,
   forces, physics objects and environment, entity blocks
   (`GetEntityBlock`, `MarkEntityBlockChanged`, `BindEntityBlob`), entity
   factories, player shadow (`PlayerShadow*`), player commands
   (`PlayerCommand*`), movement traces (`MoveTrace`, `MovePass`), names
   (`FindEntityByName(s)`), and on the client lighting and console overlays
   (`TryGetLightForPoint*`, `ConNPrintf`). Most have one or two callers.
5. **Static helpers.** `Utf8`, `Utf8OrNull`, `FromUtf8`, `SayNoBoundary`,
   `RefuseGlobalVars`.

`EngineServices.Build` special-cases two constructor parameter types today,
`EngineSide` and `NativeInterfaceFactory`, and fills shim areas from
`side.Areas`; that special-casing goes with it.

## Phases

Each phase is green on its own, with the facts it needs and a mutation proof
for each census it adds; the phases land as three PRs ("PR grouping").
Order matters: later phases depend on earlier registrations.

### Phase 0: the census first

- `EngineSideReachCensusTests` (managed, both sides): counts the members of
  `EngineSide` and, per member, the product types that call it, from IL
  (the IL walker `EngineBoundCallerCensus` already uses). It holds the counts as a ceiling
  that each phase lowers, so a phase that puts a call back fails.
- No behaviour change.

### Phase 1: the attach state leaves

- New `SideAttachment` (interop, internal): the root, the area container,
  the bound slots, the globals pointer and its decision, `ManagedDirectory`,
  `SideState`. `SideLoad` builds it, attaches the shims to it and hands it
  to `LoadModule`. Its constructor stores; a static `Attach(...)` does the
  work (`ConstructorCensusTests`).
- `EngineServices.AddTo(services, attachment)` registers from it.
  `EngineBound` slots read the attachment's `Bound`.
- `ModuleEngine` becomes one class per side (user ruling 2026-10-25):
  `ServerModuleEngine` in Server.Interop and `ClientModuleEngine` in
  Client.Interop, each wrapping that side's `SideAttachment`.
  `ServerModule` takes the server's and `ClientModule` the client's, so a
  root cannot be composed over the other side's engine. Both keep today's
  surface (`Log`, `Crossings`, `Build`, `Reload`, `AttachLog`, `AddTo`,
  `Create`, `AttachLiveness`, `VPhysics`, `Clock`), the shared members
  delegating to one helper the shared interop compiles into both; the
  server's also has `EntityFactories`. `Side` goes in PR 3 ("No runtime
  side").
- `TestSides.Create` returns the attachment over the default areas.
- Facts: the attach order (`bindSide` before the root binds), a refused
  root and a refused area fail the load naming it, two attachments share
  nothing. Hot reload: the next load builds a fresh attachment.

### Phase 2: the side's services by constructor

- Register what is not yet registered: `NativeInterfaceFactory`, the
  liveness holder, `EntityPhysicsMotion` (server).
- Every binding and interop class that reads `E.ShellLess`, `E.EntityIo`,
  `E.Removal`, `E.Interfaces`, `E.Log`, `E.Msg`/`Warning`, `E.VPhysics`,
  `E.Motion` takes the service by constructor instead.
- `EngineServices.Build` stops special-casing `NativeInterfaceFactory`.
- Facts: the registration facts name each service; the census ceiling
  drops by these members' callers.

### Phase 3: the late routes become registrations

- `ServerSounds`, `Tracer`, `PlayerMoves`, `PhysicsInit`, `TraceCost` and
  `Teleported` become route objects in the container, as `ShellLessRoutes`
  already is: registered empty at composition, filled by the module, taken
  by constructor. No settable property on a shared object.
- Facts: a route read before the module fills it answers what the empty
  route answers today; two compositions' routes are separate.

### Phase 4: the entity helpers move to their callers

One commit per group, each moving code without changing it:

| group | helpers | goes to |
|---|---|---|
| server physics | velocity, angular velocity, mass, forces, torque, gravity, motion, sleep/wake, game flags, inertia, collide extent, physics object and environment, swap, static, trigger, init, solid flags | the server `EngineEntityPhysics` binding, or a `ServerEntityPhysics` service it and `VPhysicsEntityCalls` take |
| entity blocks | `GetEntityBlock`, `MarkEntityBlockChanged`, `BindEntityBlob` | an `EntityBlocks` service (server) taken by `NetStateServerBinding`, `ServerSkins` and the bindings |
| entity factories | `BindEntityFactories`, the factory counts and names | `EntityFactories.Bind(...)` (static factory) |
| player shadow | `PlayerShadowFacts/Adopt/Release/Commit` | `PlayerShadowBodies` |
| player commands | `PlayerCommandBegin/View/Finish`, `PlayerSyncEngineSide`, `PlayerFallDamage` | `ServerPlayerCommands` / `ServerMoveWorld` |
| movement | `MoveTrace`, `MovePass`, `MoveTraceServer` and the partial `SideMoveTrace`/`SideMovePass` | `MovementBridge` and `ServerMoveWorld`, each side's own |
| entity queries | `EntityIsValid(BySlot)`, origin, angles, eye angles/position, index, class name, model name, is-player, set origin, spawn, observer mode, player handle, `FindEntityByName(s)` | the bindings that answer them (`EngineEntities`, `EngineEntityNames`, `EnginePlayers`), over the areas they already take |
| world | `TraceLine`, `TraceLineSurface`, `TraceHull`, `EntityTraceHull`, `WaterSurfaceBelow`, `InvisibleRangeReach` | `EngineWorldQuery` (server partial) |
| client | `TryGetLightForPoint(Fast)`, `ConNPrintf`, `ConNXPrintf`, `StandaloneIsDormant` | `EngineClient` / the client render binding |
| precache | `PrecacheModel`, `OwnModelPrecache` | `EnginePrecache` (server), handed to the break models by composition |
| static text | `Utf8`, `Utf8OrNull`, `FromUtf8`, `SayNoBoundary` | a static `Utf8Text` class |

Each move keeps the existing facts green and lowers the census ceiling.

### Phase 5: delete

- `EngineSide` and its partials are deleted; the census becomes "no type
  named `EngineSide`". `EngineServices.Build` loses its `EngineSide` case.
- The 71 test files move to `TestSides` over the attachment or to
  `SideBindings.Of` (most only hand a side to a builder).
- CLAUDE.md: the rules that name `EngineSide` ("Logic reaches the engine
  only through seams", "A shim area is handed to the code that calls it",
  the injection rules) are rewritten; `ShimAreaInjectionCensusTests` and
  `SeamCensus` drop it from their lists.

### No runtime side

Each side's interop assembly binds only its own side, so the interop stops
asking which side it is. Today the shared interop source compiles into both
`Server.Interop` and `Client.Interop`, one `EngineSide` class stands for
either side, and 12 places in the interop branch on `Side` at run time
(measured at 239be41 with
`grep -rnE '(_side|side|Side|E\.Side|Api\.Side)\s*(==|!=)\s*Side\.' src/*/interop src/abi`):
the interface version strings (`EngineTrace`, `EngineModelInfoVersion`), the
string tables (`ForwardedStringTables`), the render mirror's `Recording`,
`MovementRunner`'s server flag, `OwnModelPrecache`, the liveness reroute,
the entity-factory guard, the globals agreement check, `SideLoad`'s library
name and its session's `Server` flag, and `CoreServices`' assembly list key.

- Each branch becomes what the side's composition registers: the server's
  partial or registration answers the server's value and the client's the
  client's, as the per-side partials already do for IVEngineServer members.
  `SideAttachment` and the per-side module engines carry no `Side`.
- `IEngineHost.ActiveSide` stays for logic (10 reads; `Scoreboard` and
  `GameRules` gate server-only work on it, `GlobalsCommand` prints it), but
  each side's host binding answers its own side as a constant.
- `[SideService(Side.X)]` (26 attributes) stays: it is registration metadata
  in source that compiles into both sides, not a run-time branch.
- Tests stop building the other side in one assembly: 9 server-test files
  call `TestSides.Create(Side.Client)` and 6 client-test files
  `TestSides.Create(Side.Server)`; they move to the other side's test
  project or its aliased support.
- A census fails on a `Side.Server`/`Side.Client` comparison in the interop
  assemblies.

### PR grouping

Fewer, larger PRs (user ruling 2026-10-24), three in all:

1. **Attach and services:** Phases 0 to 3 (the census, `SideAttachment`,
   the side's services by constructor, the late routes as registrations).
2. **Helpers:** all of Phase 4 in one PR, one commit per group so review can
   still go group by group.
3. **Side-free and delete:** "No runtime side" and Phase 5, with the test
   migration.

### PR 1 as built

Where PR 1 departs from the phase text above, and what it leaves for PR 2
and PR 3:

- `SideAttachment` is built by `SideAttachment.Detached(side, state)` per
  load and attached by `AttachShims(root, out why)` in each side's half,
  rather than by one static `Attach(...)`. Its constructor stores; the
  attach replaces the areas, the root and the bound set whole, and runs
  what waits on it (`WhenAttached`), which is how `EngineSide` takes up the
  attached areas while it still exists.
- `ModuleEngine` is the abstract shared surface, compiled into both
  interops, and `ServerModuleEngine` / `ClientModuleEngine` derive from it,
  rather than delegating to a separate helper. Each side's half of the
  attachment makes its own (`ModuleEngineOver`).
- Interim until PR 3: `ModuleEngine` still wraps `EngineSide` (`Api`),
  `EngineServices.AddTo` and `Build` still take the side, `TestSides.Create`
  still returns `EngineSide`, and the side keeps its globals, `Log` and
  `Lines` (registered, so consumers already take them by constructor).
  `EnginePrecache` and `EngineStringTables` still ask the side's `Side` for
  the string table container's version; "No runtime side" removes it.
- Phase 2 also moved `VPhysicsTables` onto the attachment beside the
  interface factory, gave the liveness mirror a holder (`SideLiveness`),
  and has `ManagedCaches` search `SideRoutes` (the three routes) rather
  than sides.
- Phase 3's route objects are one generic `SideRoute<T>` (public, internal
  members), registered by the server's container; the routing helpers fill
  them through the composition's container, and the player shadows' teleport
  hand-off has its own helper (`PlayerShadowBodies.RouteTeleports`).
- The reach census: 179 server and 84 client pairs after Phase 1a, 145 and
  55 at the end of PR 1.

## Risks

- **Load order.** `SideLoad` binds the server's `IVEngineServer` acquirer
  (`bindSide`) before the root binds and composes after the areas attach.
  Phase 1 must keep that order; a fact holds it.
- **Hot reload.** Today the side is rebuilt per load, so nothing survives a
  reload by accident. The attachment and every registered service must keep
  that: built per load, released by the session.
- **Test churn.** 71 test files. Phase 5 is mostly mechanical; it is
  counted per file (assertions before and after) so a scripted edit cannot
  drop facts.
- **Native.** None of this changes the ABI or a forwarder table: it is all
  managed. `make check` and the daily gate cover the load path.

## Open questions for the user

None.

## Decisions

- 2026-10-24: fewer, larger PRs (three; see "PR grouping").
- 2026-10-24: the user asked whether the side needs to be known at all;
  the plan now binds each side through its own composition and keeps no
  run-time side in the interop ("No runtime side").
- 2026-10-25: the attach object is `SideAttachment`.
- 2026-10-25: the roots keep a module engine, one class per side
  (`ServerModuleEngine`, `ClientModuleEngine`; Phase 1).
- 2026-10-25: approved ("its approved, go do it").
