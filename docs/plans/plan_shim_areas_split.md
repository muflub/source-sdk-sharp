# plan_shim_areas_split.md: logic sees seams, never the native wrapper

Status: approved 2026-10-19 ("commit plan and start"); in progress.
Measured at 9fb726b (main, #11).
Main has since moved to 4cb8137 (15 commits, three retiring shim slots): the
flat classes are now 208 methods over 23 areas (server) and 288 over 29
(client), against 288/26 and 296/31 below. Every count here is re-measured at
the tip when its phase starts.

## 1. Why

Managed logic should not know that what it calls is a native wrapper. It
takes one interface per seam, with plain names, and each side's composition
root injects the implementation:

    before:  api.ClientShims.UiDrawFilledRect(x0, y0, x1, y1, rgba);
    after:   ui.FilledRect(x0, y0, x1, y1, rgba);     // IUiSurface, as named today

Logic that runs on both sides takes an interface with only what it needs, and
each side registers its own implementation at load, so nothing decides the
side at run time. Tests hold the same line: they reach every implementation
through its interface, never through its class.

## 2. What already has this shape

- **The UI seam is the model.** `IUiSurface` (`src/client/interop/
  ClientUiSurface.cs`) names its calls without the native prefix (`FilledRect`,
  `Text`, `SetClip`, ...). `ClientUiSurface` implements it over
  `api.ClientShims`, `ClientEntityFetches.AddTo` registers it
  (`services.AddInstance<IUiSurface>(...)`), and `VguiBackend` takes it from
  the registry (`services.Require<IUiSurface>()`).
- **Most callers are adapters already.** 86 interop classes are constructed with
  `EngineSide` or `ModuleEngine` and implement seam interfaces: 121 interfaces in
  all, declared in Client.Interop (57), Shared.Interop (52), Server.Interop (11)
  and the Abi (`ILog`). Of the 29 client files that call `ClientShims`, 27 are
  such adapters.
- **Logic rarely reaches the wrapper.** Outside the two composition roots
  (`ServerModule`, `ClientModule`), 11 managed files name `EngineSide` or
  `ModuleEngine`, most once or twice: `ScriptedEntity` (10), `ParticleSystemQuery`
  (3), `EntityDirectory`, `ClientReloadLedger`, `ClientParticleSims`,
  `EngineReconnect` (2 each), and `WorldNet`, `LivenessCheckCommand`, `DevCommands`
  and both sides' `Commands` (1 each). `src/client/ui`, `src/tf2` and `src/mods`
  name neither.
- **Shared code is compiled into each side already.** `src/shared/managed/**` is
  compiled into both `SourceSharp.Server` and `SourceSharp.Client`
  (`SourceSharp.Server.csproj:8`), with no `#if`. Each side loads everything but
  the Abi into its own collectible `GameLoadContext`; only the Abi is held by the
  side's non-collectible `SideRoot`. So the two sides share no managed type
  today, and a "shared" assembly is loaded twice anyway.
- **Order allows constructor injection.** `LoadSideV2` attaches the shim areas
  (`SideLoad.cs:148`) before it creates the side's module (`SideLoad.cs:235`),
  so every service is registered after its areas exist.

## 3. The gaps

1. **Adapters depend on `EngineSide`.** Each adapter takes `EngineSide api` and
   calls the flat `ShimAreas` / `ClientShimAreas` class (288 and 296 methods),
   so it depends on every area, not the one it adapts.
2. **`EngineSide` is a seam with no interface.** 421 of its own calls go to the
   shim classes, over 25 server and 20 client areas, and in 52 places it picks
   the side per call (`ServerShimsOrNull is { } s ? s.X(...) : ClientShims.X(...)`).
3. **The seams live beside their implementations**, in the interop assemblies,
   so logic that takes a seam references the assembly that holds the wrapper.
   `SourceSharp.Shared.Interop` (122 files, 40,852 lines, including `EngineSide`
   and `SideLoad`) is the one interop assembly both sides reference.
4. **Tests reach implementations by class.** 30 test files name 23 adapter
   classes directly (`EngineClient` 7, `InputMailbox`, `EngineServer`,
   `EngineEntityRender` 4 each, ...).
5. **Nothing enforces the rule.** New logic or a new test can call
   `api.ClientShims`, or `new ClientUiSurface(...)`, and nothing turns red.

## 4. The shape

Three layers, and logic and tests see only the middle one:

1. **The native wrapper, generated and internal.** `tools/shim-areas` writes
   one class per area over its forwarder table (`UiClientArea` over
   `ShimUiClientFwd`), raw types (`byte*`, `int`), native names. A server area
   derives its base (`MovementServerArea : MovementArea`): each of the ten
   server tables starts with exactly its base's slots (checked field by field
   in `SharedForwarders.g.cs`), so the base class reads either side's table.
   The containers (`ServerShimAreas`, `ClientShimAreas`) keep the all-or-nothing
   acquire and hold one instance per area.
2. **Seam interfaces, in interface assemblies.** One per seam, in managed types,
   with the names the existing seams already have (`IUiSurface.FilledRect`). A
   seam is a capability, not a native area: several seams may sit over one area.
   They move out of the interop assemblies into `SourceSharp.Server.Interface`
   and `SourceSharp.Client.Interface`, the names the dev API's assemblies give
   up in phase 1. Seams both sides use go in `src/shared/interface/`, which is
   compiled into both, as `src/shared/managed/` is into the logic: no third
   assembly. The analyzer role "interface" is assigned by project name
   (`Directory.Build.props:73`), so SS0030 (no bodies) applies. The value types
   the seams use live here too, unless the Abi's own code uses them (below).
   These assemblies load into the game context, so they reload with the game.
3. **Implementations, one per side, internal.** Each takes the area objects it
   uses, not `EngineSide`, and does the marshalling. The side's composition
   root builds it after attach and registers it under its interface.
   `src/shared/interop/` is compiled into `SourceSharp.Server.Interop` and
   `SourceSharp.Client.Interop`, like the shared logic, so each side's copy
   implements that side's copy of a shared interface.

**Value types: the Abi holds only what the Abi uses.** The Abi references
nothing and lives in the side's non-collectible root, which cannot reference an
assembly that unloads. So a type the Abi's own code uses stays there: `Vector`,
`QAngle`, `EntityHandle`, `EngineBlock`, the movement, trace and temp-ent blobs,
and the five managed types in shim signatures (`EffectData`, `FogParams`,
`DynamicLight`, `ParticleSystemInfo`, `EntitySlot`). The types only managed code
and seam signatures use (about 80: `DamageType`, `CollisionGroup`, `SolidFlags`,
`NetTierInfo`, `GameSoundParams`, ...) live in the interface assemblies, the
shared ones in `src/shared/interface/`. The Abi stays the native layout
contract, and everything else reloads.

For logic on both sides, the interface holds only what that logic calls
(`IMoveWorld`, which `EngineMoveWorld` implements for `MovementBridge`, is the
existing case). Shared logic, compiled into each side, binds to that side's copy
of the interface; the server root registers the server implementation and the
client root the client one, so the 52 run-time side choices go away.
A project that references both sides sees each shared interface twice and
tells them apart with an alias, as `src/test/managed` already does
(`Aliases="client"`, 69 files with `extern alias client`).

**Tests use the interfaces.** A test of logic takes a fake of the seam
interface, as the UI's tests already do. A test of an implementation gets it
from the side's registration, never by naming its class: it runs the side's
`AddTo(services, ...)` over `FakeShimRoot`, then `services.Require<IUiSurface>()`,
so the fact also covers the registration.

## 5. Phases

Each phase builds, passes its tests and is committed before the next.

1. **Rename the dev API's interface assemblies.** `SourceSharp.Server.Interface`
   becomes `Debug.Api.Server.Interfaces` and `SourceSharp.Client.Interface`
   becomes `Debug.Api.Client.Interfaces`: project, assembly and namespace (15 and
   16 files), the folders moved beside the dev API (`src/debug_api/interfaces`,
   `src/debug_api_client/interfaces`), and every reference (about 70 files each:
   the DebugHooks, the dev API, the UI host and their tests), the interface role
   list in `Directory.Build.props`, `DebugApiClosureTests`, and CLAUDE.md's
   `*.Interface` rule. Nothing else changes: the dev API's closure rule (System.*
   only) stays with the renamed assemblies.
2. **Generator.** `tools/shim-areas` writes the per-area classes and the
   container properties, and keeps the flat methods as one-line members that
   call the area, so no caller changes. Self-test arms per class shape (base,
   derived, shared); `ShimAreaTypedCallsTests` gains a fact per shape over
   `FakeShimRoot`.
3. **`Shared.Interop` compiled into each side.** `src/shared/interop/` is
   compiled into `SourceSharp.Server.Interop` and `SourceSharp.Client.Interop`
   and its project goes, with its references (the two interops, `TestSupport`,
   which names none of its types) and the Abi's
   `InternalsVisibleTo("SourceSharp.Shared.Interop")`. Namespaces stay. The
   bootstrap is untouched: it enters through `ServerSideEntry` and
   `ClientSideEntry`, which already live in each side's interop. No behaviour
   changes: each copy still holds the run-time side choices until phase 7.
4. **Seam interface assemblies.** Create `SourceSharp.Server.Interface` and
   `SourceSharp.Client.Interface`, and `src/shared/interface/` compiled into
   both, and move the 121 seam interfaces into them (the 52 declared in
   Shared.Interop to the shared folder). The managed-only value types their
   signatures use move with them (about 80, from the interop and managed
   assemblies; the count is a scan by name, re-measured at the start of the
   phase); the types the Abi uses stay in the Abi. A class in a seam signature
   (`Angles`, `ExplosionEffect`) is looked at on its own: it becomes data, or
   the call becomes its own seam. Namespaces stay, so callers recompile
   unchanged.
5. **Adapters take their areas.** The 86 adapters swap `EngineSide api` /
   `ModuleEngine engine` for the area objects they use, become `internal`, and the
   composition roots pass the areas. One commit per seam group (UI, FX, effects
   render, particles, input, physics, the rest).
6. **Tests through the interfaces.** The 30 test files that name adapter classes
   get each implementation from its side's registration and hold it as the
   interface; the test projects lose any `InternalsVisibleTo` they used only to
   reach adapters.
7. **`EngineSide`'s seams.** Each side compiles its own `EngineSide` since phase
   3, so the 52 side choices become per-side partial files (one per side, no
   `#if`, as the shared logic has none). Each group of `EngineSide` members that
   logic or interop calls becomes a seam interface with a server and a client
   implementation, registered by each root. Shared consumers take the minimal
   interface.
8. **The last logic.** The 11 logic files of section 2 take interfaces; only the
   composition roots name `EngineSide` or `ModuleEngine`.
9. **Delete.** The flat members of the containers and the `EngineSide` members
   with no caller left.
10. **Enforce.** Two census facts over IL, in the style of
   `StaticStateCensusTests`:
   - no logic assembly (`SourceSharp.Server`, `SourceSharp.Client`, the UI, the
     mods) references `EngineSide`, `ModuleEngine`, an area class, a forwarder
     struct or a seam implementation, except the two composition roots, listed
     by name;
   - no test assembly references a seam implementation class: implementations
     are reached through the registration and held as interfaces.
   A planted reference must turn each red.

## 6. Checks

- Phase 1 renames without changing a type: the renamed assemblies' public
  types, compared by name less the namespace and by member signature, are the
  same before and after, and `DebugApiClosureTests` passes on the new names.
- Phase 2 changes no caller: a differential shows every flat member still calls
  the same forwarder slot.
- Phase 3 moves code without changing it: each side's interop, after, holds
  every public type of its old self plus every type of the old Shared.Interop,
  by full name and member signature; a live load of each side still reaches
  `SideLoad.Load`.
- Phase 4 moves types without changing them: each moved interface's members
  and each moved value type's fields, compared by signature (less the
  assembly) before and after, are identical; the Abi's own types are untouched.
- Each seam: a fact over the implementation, reached through its registration
  over `FakeShimRoot` (the slot is reached with the marshalled arguments); the
  logic's existing facts keep using a fake of the interface.
- Both sides: for each shared interface, the server root and the client root
  each register an implementation, and a client-only side resolves the client
  one.
- Mutation proofs: point a derived area at the wrong base table; register the
  server implementation on the client; plant an `EngineSide` reference in a logic
  type; plant `new ClientUiSurface(...)` in a test. Each must turn a fact red.
- `make test` at each phase against the known baselines; Windows CI on the PR.

## 7. Costs

- **Interface dispatch.** A seam call becomes an interface call over the
  forwarder hop. The UI already pays this per draw call today. CLAUDE.md's rule
  is to move first and measure after: phase 7 measures one per-frame seam
  (entity velocity) before and after, in the style of plan_engine_forwarders 8a,
  and the numbers go in this plan. A blown budget is a boundary-design task
  (batching), not a reason to drop the interface.
- **Restart only for the Abi.** The Abi does not reload, so its own types
  (`Vector`, `EntityHandle`, the blobs) change only with a restart, as today.
  The seams and their value types live in the game context and reload.
- **Compiled twice.** Shared.Interop's 40,852 lines are compiled once per side,
  as the shared logic already is; the build pays for it, the run does not (each
  side loaded its own copy already).
- **Call sites.** About 70 files per renamed assembly in phase 1; about 620
  references to the flat classes across phases 5 and 7, and 30 test files in
  phase 6. The compiler checks each.
- **Moving a slot between areas** in `docs/engineapi-areas.tsv` changes only the
  implementation that uses it, never the logic or its tests.
- **Generated code** gains one class header per area (55: the engine base and
  the console are shared) and loses the 584 flat members (288 + 296).

## 8. Decisions

- 2026-10-19 (user): one interface per seam; managed logic must not know it calls
  a native wrapper.
- 2026-10-19 (user): logic that runs on both server and client takes an interface
  with the minimum it needs, and the right implementation is injected at run
  time.
- 2026-10-19 (user): the existing seams keep their names (`IUiSurface.FilledRect`
  stays).
- 2026-10-19 (user): the seam interfaces live in interface assemblies.
- 2026-10-19 (user): tests use the interfaces, not the sealed implementation
  classes.

- 2026-10-19 (user): the value types the seams use live in the Abi assembly.
  Superseded the same day, below.
- 2026-10-19 (user): the dev API's assemblies are renamed
  `Debug.Api.Server.Interfaces` and `Debug.Api.Client.Interfaces`, so
  `SourceSharp.Server.Interface` and `SourceSharp.Client.Interface` can hold the
  seams.

- 2026-10-19 (user): shared seam interfaces are compiled into the server and
  client interface assemblies, as shared logic is; no shared interface
  assembly. `Shared.Interop` is compiled into each side's interop to match.
- 2026-10-19 (user): the Abi keeps only the value types its own code uses; the
  others live in the interface assemblies.
- 2026-10-19 (user, review of phase 5): the adapters' areas are not nullable,
  because nothing in the game composes before the attach (LoadSideV2 fails the
  load first). Tests that compose a client module do not reach the shim at
  all: they mock the seams the adapters implement, handed in through
  `IClientEngineAdapters`.

Open: none.

## 9. As built

- **Phase 1** (2026-10-19). The server's dev API contract moved whole, to
  `src/debug_api/interfaces/` as `Debug.Api.Server.Interfaces`. The client's
  assembly held two things: the dev API's 16 hook files and the UI's data
  contracts (`Ui/`, namespace `SourceSharp.Client.Ui`), which the screens, the
  mod UI, the Descent UI and UiPreview take and the dev API never names. Only
  the dev API's files moved, to `src/debug_api_client/interfaces/` as
  `Debug.Api.Client.Interfaces`; `Ui/` stays in `SourceSharp.Client.Interface`,
  where it is already a seam in this plan's sense, so no UI project's
  references changed and a screen still sees only that assembly and the toolkit
  (`UiAssemblyRulesTests`). The renamed and remaining assemblies' public
  surface, stripped of namespaces and assembly names, is identical to the old
  two (3,775 members, 71 interfaces, a reflection dump of each).
- **Phase 2** (2026-10-19). `tools/shim-areas` writes 50 area classes at
  4cb8137's table (23 server areas and 29 client, less the engine base and the
  console both sides share): `EntitiesServerArea : EntitiesArea`, the derived
  class handing its base its own table read as the base's. The containers hold
  one instance per area and keep all 496 flat members (208 server, 288 client)
  as one-line calls. A differential over the old and new generated file shows
  each flat member calls the same slot, with the same signature, casts and
  arguments, through the table its old field took. Tests that read the
  forwarder type from a container field now read it from the area class's
  constructor (`ShimAreaTypes.FwdOf`).
- **Phase 3** (2026-10-19). `src/shared/interop` is compiled into
  `SourceSharp.Server.Interop` and `SourceSharp.Client.Interop` and its project
  is gone; each interop carries the grants the shared one gave (the suites, the
  oracle tests, its side's test doubles and its side's logic). A reflection dump
  of each side's interop equals its old public surface plus the old
  Shared.Interop's (5,785 and 7,691 member lines). What the change asked of the
  rest:
  - The test doubles are built per side (`SourceSharp.TestSupport.Server` and
    `.Client`, one source); a suite driving both sides references the client's
    under its `client` alias, with the client's interop. `RecordingEngine.Default()`
    holds `Mock()`'s stubs so each build has them, and `ClientDoubles` (common)
    hands out the client's doubles, unattached engine and `Require`.
  - Facts in the cross-side suite that drove only the client moved to the client
    suite (twelve files, three of them halves split off a mixed file); facts that
    drive both name the client's copies explicitly, and the temp entity round
    trips run the server's writer on the server's own double and registry.
  - `EngineEffectsForwarder`, a pure forwarder the audit treats as pass-through,
    is shared source, so the client's temp entity facts can decorate the
    client's effects area.
  - The side audit compiles the shared interop alone in memory
    (`SourceSharp.SharedInteropSource`, granted by the Abi) and holds it to the
    both-sides slots, as it does the shared source.
  - A runtime check against the other side's copy compiles and reads nothing,
    so the cross-side suites were scanned for every `as`, `is`, `typeof`,
    `IsAssignableFrom` and attribute lookup naming a shared interop type. One
    had gone vacuous (`ReloadPinTests` asked client types for the server's
    `IRouteBinder`), and others failed loudly (`HookOrderTests` read every
    client hook's order as 0, the ConVar declaration facts found no client
    declarations); each now asks the right side.
- **Phase 4** (2026-10-19). Re-measured at the start: 93 interfaces that an
  adapter (a class built over `EngineSide` or `ModuleEngine`) implements, 94
  with the interfaces their signatures pull in (ILog stays in the Abi), and 57
  value types their signatures use, all from the interops (none from the
  managed assemblies). 148 types moved: 66 to `src/shared/interface`, 4 to the
  new `SourceSharp.Server.Interface`, 78 to `SourceSharp.Client.Interface`
  beside its `Ui/`. Fourteen files moved whole; 43 were split, the interop
  keeping the implementations. Namespaces stayed. A reflection dump of each
  side's interop plus interface after equals the interop before, member for
  member, but for the changes below.
  - Nine value types in seam signatures carried behaviour, each looked at on its
    own. Seven became data, their behaviour staying in the interop as extension
    methods or a static class (`NativeConsoleEntryReading`,
    `PayloadAgreements`, `TeFilters`, `MoveConstantDefaults`,
    `StandaloneBonesReading`, `ParticleViewReading`); `TeClass` lost a lookup
    nobody called. Two could not: `NameMatches` (a ref struct enumerator whose
    `using` needs its own `Dispose`) and `OverlapPair` (unordered equality
    through `IEquatable`). Their calls became seams of their own,
    `IEngineEntityNames` and `IEngineOverlaps`, which stay in the interop with
    their types; `EngineServices` finds areas in both assemblies.
  - Extension methods rather than C# 14 extension members: the side audit and
    the analyzer harness compile the shared code with Roslyn 4.14.
  - Ten default interface bodies went (SS0030, and the metadata fact forbids
    them). Each implementer that leaned on one now says the same answer: the
    null objects for the three client lists, null or the GAME path in the
    doubles, and `FxFlashAttachments` for the two flash reads the real binding
    answers the engine's way (the oracle doubles answer them from recorded runs,
    so they stay members). `EngineClients.AvgLatency` answers null as its
    default did; it never forwarded the latency.
  - `IEngineStringTables` is internal (it carries table ids), so the interface
    assemblies grant their internals to what reached it in the interop.
  - A screen still references only `SourceSharp.Client.Interface` and the
    toolkit, and uses nothing in it but `Ui/`: `UiAssemblyRulesTests` reads that
    from each screen's metadata, and the interface's new reference to the Abi
    does not reach a screen that never names an engine type.
  - The cross-side suites reference the client interface under the `client`
    alias, as they do its interop. The side audit follows calls into each
    side's interface assembly from the logic (it found the server's
    `SetSkin` call again through it) and compiles the shared seams alone in
    memory for the shared audits; the interop's own audits do not follow them,
    as they never followed the seams when those were the interop's own types.
- **Phase 5** (2026-10-19). Measured at the start (a Roslyn scan of every
  class whose constructor takes `EngineSide` or `ModuleEngine`, and what it
  reads off it): 89 classes, of which 34 client adapters reach the engine
  through shim areas alone plus the attach guard. Those 34 take the areas they
  call (`ClientUiSurface(UiClientArea uiClient)`), handed over by the
  client's root from `EngineSide.ClientShims`, in six commits (UI, FX,
  particles, effects render, physics, the rest); all are internal.
  - No side detaches its shims and every game composition runs after the
    attach, so an adapter captures its areas at construction, and they are
    not nullable (review ruling, Decisions). The registration
    (`ClientEntityFetches`) reads `ClientShims` once and refuses an engine
    that never attached, before it builds any adapter.
  - `ClientModule` registers the adapters through the `IClientEngineAdapters`
    it was handed (the game's constructor hands `ClientEntityFetches`). A
    test composing a module hands `FakeClientEngineAdapters`, mocks of the
    seams the composition resolves, so it never reaches a shim area.
  - `ClientPhysicsSlots` also takes the side's `VPhysicsTables`. The soundscape
    audio's server branch went: a client `EngineSide` attaches only client
    areas, so it could never run.
  - `ClientAdapterRegistrationTests` holds each one through the client's
    registration over a `FakeShimRoot`, driving one slot (never a
    `SuppressGCTransition` slot, which cannot call a managed callback), plus
    the unattached refusal. Withholding an area from the root turns that
    adapter's fact red. `ClientModuleAdapterSeamTests` holds that the root
    asks the adapters it was handed and that the game's constructor composes
    the real ones.
  - The rest wait for phase 7, because what they call is `EngineSide`'s own:
    the `Engine*` bindings (34), the input host and devices (the engine's
    interfaces), `ClientModelNames` (EngineBound's model info), the server's
    native wrappers, and the shared `EngineMoveWorld` and `PhysicsSurfaces`,
    which choose a side at run time.
