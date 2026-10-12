# plan_globals.md — No static state in the server DLL

> **RE-MEASURED 2026-09-20 AT THE plan_shim INTEGRATION TIP, AND EVERY NUMBER BELOW IS STALE.**
> The figures in this document were taken at `216533fa8` — **229 commits back**, the Phase 0 tip.
> `plan_shim.md` has since run Phases 1 through 6 and deleted a great deal of what this plan
> counted.
>
> | | at `216533fa8` (this doc) | at the current tip |
> |---|---|---|
> | server objects on the link line | 143 | **108** |
> | archives | 7 | **6** |
> | symbols seen by the census | — | 12,326 |
> | mutable state, object level | 721 | **552** |
> | **mutable state, LINK level** | **619** | **469** |  *(my first pass said 468; the committed instrument says 469 and it is right — see below)*
>
> So **about 151 link-level statics — roughly a quarter — have already gone as a side effect of
> the shim close.** The remaining scope is **468**, not 619.
>
> Two further consequences before anyone executes this:
>
> 1. **The riding structure no longer applies.** This plan is written to add "one exit criterion
>    per existing `plan_shim` phase". Those phases are done. The globals work is now standalone
>    and needs its own step sequence.
> 2. **`plan_globals_rulings.tsv` (620 rows) is partitioned by `plan_shim_rulings.tsv`'s lane
>    column**, and those lanes no longer exist. Its rulings must be re-derived against the current
>    tree, not handed to lanes by name.
>
> Method for the table above (re-runnable): `python3 plan_globals_census.py src
> src/_vpc_/ninja/sdk_csharp_debug.ninja <out.tsv>`, then filter `kind` **exactly** `bss` or
> `data`, drop `category=guard` and the four `vec2_/vec4_` header constants, and count LOCAL once
> per object with UNIQUE/WEAK/GLOBAL once by name.
>
> **G0 IS DONE, AND IT FOUND FOUR BUGS IN THE SCRIPT ABOVE — I had caught one.**
> `tools/globals-census` is now committed with a 78-check self-test, a partition-based
> reconciliation, a 469-row ledger (`docs/globals-ledger.tsv`) and a gate that fails both ways.
>
> | bug | effect |
> |---|---|
> | section filter was a **substring** (`bss\|data` matches `rodata`) | 11,820 rows from a 1,017 population — the one I hit |
> | the vec exclusion keyed on the **mangled** name | matched **0 of 406**; those four constants were never actually excluded |
> | the fold key was the **demangled** name | `studio.o`'s two distinct `__executeCount` statics counted as **one** |
> | the archive `nm` filter omitted `u` | archive symbols under-counted |
>
> The second and third are why my re-measure read **468** and the instrument reads **469**.
>
> **Two environment traps, now refusals rather than wrong answers.** `lib/public/linux64/*.a` is
> **not config-qualified** — both graphs write one path, so `make debug && make release` left 476
> instances unattributed. And the sniper container's `nm` (binutils 2.35.2) **emits no DWARF**,
> which made the gate's first run read **875 against a ceiling of 469**; it now reads the symbol
> table only, verified identical on host and in container.
>
> **The reconciliation's first draft could not fail** — it defined the archive term as "in the .so
> and not in an object", making the identity true by construction. It is now a partition whose
> residue must be empty. Two more unfalsifiable checks were caught and fixed the same way.
>
> **`GONE` no longer fits as a disposition.** It meant "leaves under a `plan_shim` ruling"; all six
> phases have run and **185 of its 332 GONE rows are still on the link line**. DEAD / CONST /
> MANAGED / MEMBER / FLOOR still fit. Current ledger: DEAD 33 · FLOOR 12 · **UNRULED 424**.
>
> **§0's numbers, all re-taken:** static initialisers **103 of 108** (was 138/143), `.init_array`
> **130** (was 165), ConVar objects **87** (was 163), release **453** and still naming nothing debug
> lacks. §0.3's stock `game/server` (95) and `game/shared` (72) rows are now **zero** — everything
> left is under `src/sourcesharp/`.
>
> **The old 620-row rulings table joins 0 of 469** on `(home, name)`. It is superseded, not merged.

> *(A caution for whoever re-runs it: my first filter used `kind ~ /bss|data/`, which also matches
> `rodata` and returned 11,355 against a 1,017-row population. An implausible number against its
> own total is the tell. Match the section exactly.)*


Measured at `216533fa8` (the plan_shim integration branch, the Phase 0 tip of `plan_shim.md`), against the debug
build in the phase0 worktree (objects dated 19:40, tip committed 19:42 the same day; the census
refuses a missing object). Untracked until reviewed. Rides on `plan_shim.md`: it adds one
instrument, one scaffolding step and one exit criterion per existing phase; it opens no second
pass over files that plan is already editing.

The ask: audit every global and static in the native server project, and plan their removal so
that everything that survives is owned by **the game DLL instance**.

It covers **both directions of the boundary**, because a global on one side is what a
context-free call on the other side forces: managed → native moves onto bound `IShim<Area>`
vtables whose `this` owns the state, and native → managed moves onto managed-built
`IManaged<Area>` objects whose `self` carries a `GCHandle` to the instance (§2; measured cheaper
than today's static lookup, and safe under a compacting GC). Steps G2 and GM.

The per-symbol rulings live in `plan_globals_rulings.tsv` (one row per link-level symbol, six
read-only lanes partitioned by `plan_shim_rulings.tsv`'s lane column, so a row can be handed to
the lane that already owns its file). This document is the plan over that table.

---

## 0. What was measured

### 0.1 The instrument

`plan_globals_census.py` (Appendix A; becomes `tools/globals-census` in step G0). It reads the **link
edge** of `sdk_csharp_debug.ninja` — 143 objects, 7 archives — never the objs directory
(`plan_shim.md` §0.3; memory `objs-directory-is-not-the-link-line`). For each object: `readelf
-sW` for section, binding and size of every `OBJECT`/`TLS`/`GNU_UNIQUE` symbol, `nm -l` for the
DWARF file:line of the definition, and the source text of that line. Source text is never the
instrument — a macro (`BEGIN_DATADESC`, `IMPLEMENT_SERVERCLASS`, `CON_COMMAND`) hides the
`static` it expands to. The rule this plan enforces already exists in prose ("no file-scope
statics in SourceSharp's C++", `shell_gamedll.cpp:441`; 22 files under `src/sourcesharp/test`
mention it, `grep -rli 'file-scope static\|file-static' | wc -l`) and nothing measures it today.
Something once did: `tools/statics-audit`, a source scanner with a 306-row baseline under
`docs/statics-audit/`, went out with `b6c429e1a` ("Remove docs, plans, reports and the port's
one-off script apparatus"); 31 files under `src/` still cite it (`git grep -l statics-audit --
src | wc -l`). It scanned for **file-scope** `static` variables — so a global moved inside a
function passed it, which is how §0.3's accessors came to be.

A second, independent instrument reads the linked `server.so` by section and must reconcile.

### 0.2 The numbers

| | symbols | bytes |
|---|---|---|
| every static-storage symbol in the 143 objects | 16,401 | — |
| … `.rodata` (constants; not state) | 14,485 | 72,324 |
| … `.data.rel.ro` (vtables, typeinfo, const tables of pointers; not state) | 583 | 33,952 |
| … **`.bss` + `.data`: mutable** | **1,333** | 2,137,395 |
| minus `vec2_origin`/`vec2_invalid`/`vec4_origin`/`vec4_invalid`, header-defined, one runtime-constructed copy per TU | −544 | |
| minus guard variables | −68 | |
| = state, object level | 721 | |
| = **state, link level** (UNIQUE/WEAK/GLOBAL counted once, LOCAL once per object) | **619** | 1,009,344 |

Reconciliation with `server.so` (`.bss` 0x106080 = 1,073,280 bytes, `.data` 0xb5b0 = 46,512):
714 mutable symbols after the same exclusions = 619 − **51 the linker garbage-collects** + **146
from outside the objects** (133 attributed by name to the archives: tier1 41, mathlib 28,
particles 21, tier3 19, tier2 12, dmxloader 12, choreoobjects 0; 13 are crt/libgcc). 619 − 51 +
146 = 714. The two instruments agree.

Also: **138 of 143 objects carry a static-initialiser function**; `server.so`'s `.init_array`
has 165 entries. Every one of them runs before `DLLInit`, in link order, with no engine.

### 0.3 Where the 619 sit

| tree | link-level rows (object-level) | bytes (object-level) |
|---|---|---|
| `src/sourcesharp/game/server` | (382) | 1,460,251 |
| `src/sourcesharp/game/shared` | (145) | 331,298 |
| stock `game/server` rows | (95) | 312,047 |
| stock `game/shared` rows | (72) | 9,195 |
| `public`, `sourcesharp/public`, `common` | (26) | 17,557 |

By `plan_shim` ruling of the defining file: DELETE 86 · MOVE 40 · KEEP 164 · SPLIT 306 ·
unruled (stock headers `plan_shim` never ruled) 23. **126 rows need no plan at all — they leave
with their file.** The other 493 are what the lanes ruled.

By shape: 258 object-level rows are **function-local statics**, and the largest things in the
DLL are among them — `PhysicsShell()::s_Shell` (266,392 bytes), `SourceSharp_SlotTable()::
s_SlotTable` (81,936), `SourceSharp_NetTypes()::s_Types` (43,016), `PhysicsAskLedger()::s_Ledger`
(32,864). The project's rule ("no file-scope statics in SourceSharp's C++",
`shell_gamedll.cpp:441`) was met in many places by moving the global inside an inline accessor.
That satisfies a source grep and changes nothing: it is a global with lazy construction, no
destruction order, and no reset. The census counts them; the rule, restated in §1, names them.

### 0.4 The rulings, summed

Six read-only lanes ruled all 619 rows against the tip (`plan_globals_rulings.tsv`, 619 rows × 19
columns, 0 unmatched on the merge; each lane's row count equals its input).

| disposition | rows | bytes | |
|---|---|---|---|
| GONE | 332 | 143,220 | leaves under a `plan_shim` ruling already made (139 of the 163 ConVar/ConCommand objects are here) |
| MEMBER | 125 | 825,730 | stays native, becomes instance-owned (§2) |
| DEAD | 79 | 9,351 | no live reader; deletable now |
| MANAGED | 30 | 3,075 | native copy of, or native detour around, something managed already owns |
| FLOOR | 28 | 7,157 | stays static — 22 of them beyond argument, 6 contested (below) |
| CONST | 25 | 20,811 | becomes `.rodata` |

A cross-check the lanes did not know about: all **51 symbols the linker drops** were ruled DEAD
(17) or GONE (34) — no lane argued to keep state the link already discards.

Release objects (same worktree, `sdk_csharp_release.ninja`, 143 objects) carry 599 link-level
rows against debug's 619, and **zero names that debug lacks**; the 544 `vec2/vec4` copies are an
`-O0` artefact and vanish. The ledger keys on debug (the superset); the gate runs on both.

**The floor, in full (28).** The game DLL instance `s_gameShellDll`; 9 `InterfaceReg` nodes
(`IServerGameDLL`, `IServerGameClients` ×3 versions, `IServerGameEnts`, `IServerGameTags`,
`IPlayerInfoManager`, `IServerDLLSharedAppSystems`, `IPluginHelpersCheck`); `g_pServerClassHead`
and `s_pNonModifiedPointerProxyHead` (stock static-init chains the engine walks); 8 Steam SDK
inline-accessor cells; 2 `_DEBUG`-only cells in `saverestoretypes.h`. That is 22. **Contested,
6:** five native ConCommands that `tools/live-gate` and devapi drive (`ss_boundary_probe_report`,
`ss_boundary_probe_reset`, `cs_sv_payload_agree`, `cs_sv_poke_payload`, `cs_sv_playerinfo`) — 75
managed `[ConCommand]`s already exist, so these are FLOOR only until `plan_shim` Phase 5 rules
them — and `g_WorldFactory`, which lane A kept and lane B showed is the *only* static-init
installer left in the entity factory dictionary (no `LINK_ENTITY_TO_CLASS` expands in any linked
TU): install `worldspawn` from `DLLInit` and both it and the dictionary's function-local static
go. **Target floor: 22.**

Nothing else is reached by symbol. In particular **none of the ~50 engine interface pointers is a
floor** (lane A, by `nm -u` over the 143 objects and the seven archives): the archives import
only their own tier globals (`g_pCVar`, `g_pFullFileSystem`, `materials`, `g_pMDLCache`,
`mdlcache`), which live in the archives and are counted in the 133, not the 619.


---

## 1. The test, restated

**State** is any symbol in `.bss`, `.data`, `.tbss` or `.tdata` of an object on the server link
line. A function-local static is state. A class static is state. A static inside an inline
accessor is state. What the source calls it does not matter; the symbol table decides.

Every row gets one of six dispositions, in order of preference:

| | meaning | work |
|---|---|---|
| GONE | its file, or its half of a SPLIT file, already leaves under `plan_shim` | none here; the row is a check on that phase's exit |
| DEAD | no live reader on the link line (51 of these the linker already drops) | delete, now |
| CONST | written only by its initialiser | `constexpr`/`const`, lands in `.rodata`; no static-init function |
| MANAGED | game state or policy; nothing in `plan_shim` §1's a–h forces it native | an **instance field** reachable from the managed `ServerGameDll` — never a C# `static` |
| MEMBER | must stay native, with its a–h letter and the engine consumer named | a member of a subsystem object owned by `CShellGameDll`, with a lifetime tier |
| FLOOR | the engine or a static library reaches it by symbol, or it *is* static-init registration | stays; listed in the ledger with who reads it |

"It was already a singleton", "the accessor hides it" and "it is only a counter" are not
dispositions.

---

## 2. The target: one root, two tiers

```
s_gameShellDll : CShellGameDll        THE static.  (c): EXPOSE_SINGLE_INTERFACE_GLOBALVAR takes an
 │                                    address at static-init.  shell_gamedll.cpp:1634, 6,360 bytes.
 ├─ engine binding                    plain members, as today: m_pEngine, m_pCVar, m_pGlobals,
 │                                    m_pServerClasses, m_SendProxies, m_Refusals, m_StepOrder …
 ├─ m_pDll   : CServerDllState *      new in DLLInit   · delete in DLLShutdown
 │     EntitySlots · NetTables · TempEnts · Precache · Players · Sound · Diagnostics · …
 └─ m_pLevel : CServerLevelState *    new in LevelInit · delete in LevelShutdown
       Physics (environment, collision event, pair hashes) · per-map caches · per-map latches
```

**Why two heap blocks and not by-value members.** A by-value member of a static object is
constructed at static-init: it is still one of the 165 `.init_array` entries, it still runs
before the engine, the allocator hooks and the managed runtime exist, and it is never reset.
`new` in `DLLInit` puts construction where `CShellGameDll` already sequences everything else
(`shell_gamedll.cpp:154-381`) and unwinds in reverse (`:436-467`). `delete m_pLevel` in
`LevelShutdown` makes reset **structural**: a level-tier field cannot leak into the next map,
because the object it lived in no longer exists. Every live gate loads one map (memory
`one-map-load-hides-a-whole-bug-class`); the reload leaks the lanes found (§3) are the class this
removes by construction rather than by a list of `= 0` somebody has to remember.

**How code reaches the instance — no accessor on the managed→native path.**

1. *Inside a subsystem:* by reference, taken at construction (`CPhysicsState( CEngineBinding & )`).
2. *Managed → native: through a bound vtable, and `this` is the context.* (Owner direction,
   added after the first draft, and it removes the accessor that draft kept.) The tree already
   has the pattern end to end, and new shim interfaces follow it exactly:
   - an `abstract_class IShim<Area>` in its own header, blittable arguments only
     (`game/server/ishim_level.h:43`, `ishim_frame.h:71` — 22 and 19 methods);
   - a `chain=[...]` row in `tools/vtable-layout` (:494, :514), which generates the typed struct
     in `SourceSharp.Abi/VtableLayouts.g.cs`;
   - the entry count from the compiler — `VTableHook::MemberOf( &IShimLevel::LoadSaveGameState )
     .slot + 1` (`shell_levelshim.cpp:212`) — handed over with the pointer and checked once per
     acquire by `NativeInterfaceFactory`; nothing per call;
   - acquisition by name through the `SOURCESHARP_FACTORY_INTERNAL` arm
     (`shell_internalfactory.cpp:58`).

   What changes is **who implements the interface**. Today the two areas are themselves statics
   (`static CShimLevel g_ShimLevel;` `shell_levelshim.cpp:198`, and `g_ShimFrame`) whose methods
   call free functions that read globals. Under this plan the **subsystem object is the
   implementor**: `CPhysicsState : public IShimPhysics`, owned by `m_pLevel`; the method body
   reads `m_…`. A free `SourceSharp_*` entry point has no `this` and therefore needs a global to
   find its state; a vtable method cannot need one.

   **Bootstrap without a global.** The root already hands itself to the loader —
   `LoadManagedRuntime( this )` as `IManagedRuntimeEnvironment` (`shell_gamedll.cpp:260`). The
   engine block gains exactly one pointer-plus-count pair, `IShimRoot`, filled from that `this`;
   `IShimRoot::Acquire( name, &nEntries )` replaces the free `SourceSharp_InternalFactory`, and
   every other area is acquired from it. Level-tier areas are re-acquired on `OnLevelInit`
   (their implementor is a new object each level) — managed holds them in a level-scoped
   object, which is the managed half of the same structural reset.

   **What this does to the slot table.** `EngineApi` has 472 function-pointer fields today
   (`tools/abi-slot-census.py EngineApi`; ≈168 are `Entity*`/`Standalone*`/`Player*`), and every
   one of them is a free function. `plan_shim` 3g already retires the *passthrough* slots to
   bound **engine** vtables, and stops at entity and physics ops for a stated reason: binding
   `IPhysicsObject` directly would give up ids-with-serials, and entity ops resolve
   handle → shell through the slot table's single writer. A **shim** interface keeps both:
   `IShimPhysics::ApplyForce( uint32 id, … )` still takes the id-with-serial, and
   `IShimEntities` still resolves through the one `EntitySlots` member. Only the dispatch
   changes — a function pointer in a struct becomes a slot in a vtable whose `this` is the
   owner. So 3g's conclusion is amended, not its reasoning: **the slot table ends as the
   bootstrap pair, not as "entity and physics ops"**. The cost is one extra pointer argument
   per call; it is recorded, with numbers, at the perf pass and not before (memory:
   `move-first-measure-after`).
3. *Engine → native with no context:* the engine's own vtable calls (letters a, b) arrive on a
   member and already have `this`. What is left is C function pointers with no user-data
   argument — the spew hook and `FnChangeCallback_t` (lane C counted three in the shared tree).
   For these only, one accessor — `ShellSide()`, declared in one shared header, defined once per
   DLL beside the root — and a gtest that pins the calling files. ConCommands that stay native
   use tier1's `ICommandCallback` object form, and string-table callbacks already carry a
   `void *object`.

**Native → managed: the same pattern, built from the other side.** (Outside the native census,
but it is the same boundary and the same defect mirrored.) `SourceSharp.Bootstrap/Entry.cs` has
191 `[UnmanagedCallersOnly]` statics (194 in the tree); `ManagedApi` has 187 fields. C# requires
those methods to be `static`, and with no context argument each one finds its target through
static **state**: `s_Server`/`s_Client`/`s_ServerGame`/… (`Entry.cs:20-45`), plus an ambient
thread-static side, `Engine.SetActiveSide(...)` (`Engine.cs:106`), flipped at 169 entry points
because one runtime serves both DLLs on a listen server. Natively, `managed_runtime_server.cpp`
(354 lines) is a hand-written adapter from the C++ `IManagedRuntimeServer` onto that block.

The cure is a **managed-implemented C++ interface**: managed allocates
`{ void **vtbl; void *gcHandle; }`, fills `vtbl` with `&Thunk` pointers, and hands native the
address — which native uses as an ordinary `IManaged<Area> *`. `this` is the first integer
argument on SysV and MSVC x64 alike, so each thunk is
`static void Frame( ManagedObject *self, float dt ) => Target( self ).Frame( dt );` with
`Target` a `GCHandle.FromIntPtr( self->gcHandle ).Target` cast. The method is still `static`; the
**state** is not — the instance arrives in `self`, and because a side's object *is* its side,
`SetActiveSide` has nothing left to say.

- One header is the contract in both directions: `abstract_class IManaged<Area>` beside the
  `IShim<Area>` headers, no virtual destructor (Itanium emits two slots for one; lifetime is an
  explicit `Release()` that frees the handle), blittable arguments, no by-value struct returns
  (MSVC orders `this` and the hidden return slot differently for members — the same rows
  `tools/vtable-policy.json` already refuses).
- `tools/vtable-layout` gains an `implemented-by-managed` chain kind; a generator in
  `SourceSharp.Generators` (four exist) emits the thunks, the try/catch-to-`Fail` wrapper every
  entry carries today, and the vtable fill from a C# interface. Nobody hand-writes a thunk.
- The count check runs in reverse: managed hands over pointer **and** count; native compares
  with `VTableHook::MemberOf( &IManagedFrame::Last ).slot + 1` and refuses, naming both numbers.
- The root already has the slot for it: `CShellGameDll::m_pManagedRuntime`. It becomes the
  managed-built pointer; the 354-line adapter is deleted — net C++ goes **down**.
- What stays static in managed is the mirror of `CreateInterface`: `Entry.LoadSide`, the one
  symbol the host resolves by name. It constructs the side object and returns it.
- Tests: a two-side `[Fact]` (two side objects in one process; a thunk entered with A's `self`
  never touches B — impossible to write today); a count-mismatch refusal fact; generator
  snapshot facts; and a gtest that calls a hand-built table through the C++ interface pointer,
  pinning the layout assumption per compiler.
- **Measured** (`tools/bench-managed-vtable`, a benchmark branch at `2f67ee4ff`; a C
  caller looping over real native→managed transitions, .NET 10.0.12, pinned core, 15 × 20M calls
  per variant interleaved, reproduced on a second core; medians, ns per crossing):

  | shape | ns | over floor |
  |---|---|---|
  | transition floor — flat pointer **and** virtual call (the extra `this` is unmeasurable) | 3.68 / 3.67 | — |
  | **today**: static field + thread-static side + interface call | 5.72 | +2.0 |
  | … today without the `SetActiveSide` write | 4.99 | +1.3 |
  | `{vtbl, gcHandle}`, `GCHandle.Target` + castclass to the interface | 5.92 | +2.2 |
  | `{vtbl, gcHandle}`, `GCHandle.Target` + `Unsafe.As<T>` | 4.25 | +0.6 |
  | **`{vtbl, gcHandle}`, `GCHandle<T>.Target` on a sealed type** | **4.07** | **+0.4** |

  The proposed shape is ~1.6 ns **cheaper** than today's per crossing, provided the generator
  emits the typed handle against the sealed implementor; the naive untyped-plus-interface-cast
  form is 0.2 ns dearer. So the generator rule is: `GCHandle<TSealed>`, never `(IFoo)handle.Target`.
  A micro-benchmark of one hot monomorphic site on a box at load 6 — real entries do real work and
  the difference disappears into it; Windows/MSVC not measured.
- **The GC may move the object; native never holds its address.** Native holds two things, and
  neither is GC memory: the `{vtbl, handle}` block and the vtable (`NativeMemory.Alloc`), and the
  thunk addresses (`&Method` of an `[UnmanagedCallersOnly]` method is a stable entry stub — tiered
  re-JIT replaces what it jumps to, not the stub). `handle` is a `GCHandle` of type **Normal**: an
  index into the runtime's handle table, which the GC rewrites when it relocates the target. No
  pinning — a pinned side object would sit in the heap for the life of the DLL and fragment it.
  Proven by `--gc` (object relocated twice under forced compacting collections, handle bit-equal,
  13,000 of 13,000 calls land), by `--stress` (50M calls across 512 concurrent compacting GCs,
  exact counts), and by the negative control `--gc-raw`, which hands native the raw address the
  way a careless implementation would: **1,000 of 13,000 calls land and nothing crashes** — the
  other 12,000 write into whatever now lives at the old address. That silence is why the
  generator owns this and nobody writes a thunk by hand.
  Three rules fall out: (1) a managed-implemented interface never returns or stores a pointer
  into a managed object — spans and arrays cross as `fixed` for the duration of one call, or live
  in native memory, as the entity block already does; (2) `Release()` frees the handle and native
  must drop the pointer first — a freed handle's slot is reused, so a late call lands on a
  *different live object*, which is the same silent failure; the level-tier areas make this real
  every map change, and the G2 `[Fact]` "a level-scoped binding is unusable after
  `OnLevelShutdown`" is its gate; (3) the game assemblies load in a collectible
  `GameLoadContext` (`Entry.cs:43`): a strong handle keeps that context alive and thunk stubs die
  with it, so `UnloadSide` releases every area before it unloads — `cs_sv_reload` is the test.
- First area: the lifecycle interface, because `IManagedRuntimeServer` already exists as a C++
  abstract class — it only changes implementor. It lands in G2 beside `IShimRoot`, so both
  directions are proven before a lane depends on either. Then frame, then entities and physics
  events with their `IShim` twins in 2a/3b. A `ManagedApi` field and a vtable slot coexist
  during migration, as on the other side.

**The other engine-resolved interfaces** (`ServerGameClients`, `ServerGameEnts`,
`PlayerInfoManager`, …) become members of the root, exposed with `EXPOSE_INTERFACE_FN` whose
create function returns the member's address. The `InterfaceReg` node stays static — it is the
registration, 24 bytes, no state — and the object it names stops being one. Two constraints
lane A measured: the engine resolves `VServerDllSharedAppSystems001` **before** `DLLInit`, so
the root itself can never be created there, and anything resolved that early is a by-value
member of the root, not of `m_pDll`; and `CServerGameClients`/`CServerGameEnts` carry member
ConVars, so they stay by-value (static-init constructed) until Phase 5 moves those ConVars.

**Shared files** compile into both DLLs and the client's root is a different class. Lane C's
proposal, adopted: one `CShellSharedSide` struct embedded by value in both `CShellGameDll` and
`CShellClientDLL`. A shared area's shim interface is implemented by that struct on each side, so
managed binds the same `IShim<Area>` layout against either DLL; `ShellSide()` exists only for
route 3's context-free callbacks.

**Managed side.** MANAGED rows land as instance fields. A managed static audit beyond the
boundary (step GM) is not part of this plan; a crude grep finds ≈24 mutable `static` field lines across `SourceSharp.Server` and
`SourceSharp.Shared`, so the managed tree is already mostly instance-shaped, and G-close adds
the analyzer that keeps it so.

---

## 3. What the lanes found

Rulings are in the table; these are the findings that change a step, fix a bug, or correct
`plan_shim`. File:line at `216533fa8`.

### 3.1 Corrections to `plan_shim` line ranges — **time-sensitive: Phase 1 lanes are open**

| where | what the range gets wrong | lane |
|---|---|---|
| `shell_tempents.cpp` delete range 909–1120 (1f) | spans the live `SourceSharpTe_Forward`, which fills `g_TeScratch`; and misses `g_iTeScratchClass` :796, `g_TeShiftedField` :881, `FindField` :883–908 | F |
| `shell_sentences.cpp` `fSentencesInit` ruled delete | `SENTENCEG_Init` in the stay half reads and sets it (:142, :146) | F |
| `shell_diagnostics.cpp` DELETE range 476–1231 | contains the spew hook the kept half installs (:1355) | C |
| `shell_standalone_player.cpp` #move(3) | the range covers `SourceSharpPlayerShadowEnabled` (:1317, gates the shadow build at :1417 — not instrumentation), and `g_nSourceSharpPlayerShadowsLive` is also read in `game/server/physics.cpp:376-408,:1008` where it **detects shadows outliving their environment** | B |
| `shell_decals.cpp` KEEP | cites a reader that is off the link line; one live call remains, `world_adapter.cpp:764` | F |
| `shell_playerlocaldata.cpp` `DT_Local` kept as "wire" | no reference anywhere on the link line; 4 rows DEAD (live check: a join with the table removed) | A |
| `developer` ConVar KEEP (H2) | it is the engine's own cvar; no link-line file names the DLL's copy | D |
| 2b's `physics_globals.cpp` (30 lines, six interface pointers) | **relocates** the six globals; under this plan they are members of the Physics subsystem and the file is never created | E |
| `g_pForceAttachEdict` #stay ("fold into the world adapter") | write-only on the link line; its reader was stock `baseentity.cpp`; delete | B |

### 3.2 State that survives a map change (39 rows answer `reset_today = NO`)

Most are cumulative counters in GONE files. The ones that matter:

- **`g_Collisions`** (`physics.cpp`): `CCollisionEvent::LevelShutdown` :4025 stops the four
  friction patches and nothing else. Penetrate, fluid, damage-inflictor, remove-object, touch and
  trigger lists cross into the next map; penetrate and fluid rows are `curtime`-stamped, so they
  persist until the new clock passes the old stamp (:1931, :3496). `CPhysicsHook::
  LevelShutdownPostEntity` returns early when `physenv` is NULL (:560) and skips all cleanup. The
  ctor (:1408) relies on `.bss` zero for `m_current[].patch` — **moving it to the heap needs an
  explicit initialiser**, and that is the regression test.
- **`PhysicsShell()::s_Shell`** (266,392 B: 1024×100 B event ring, 4095×32 B id table + 8192×4 B
  hash, 120 B rule table): cleared piecemeal from four files; the rule table never returns to
  defaults; three copies of the environment pointer. Lane F filed it DLL-tier, lane E LEVEL.
  **Ruled LEVEL**: every id in it names an object in an environment that dies with the level.
- **`s_ShellPlayerInfoManager.m_Orphans[]`** (`shell_playerinfo.cpp:408`) is never cleared.
- **`CSaidOncePerId` tables** (1,024 handles, 4,100 B each) are never cleared: a re-used handle on
  the next map stays silenced, and past 1,024 ids they warn on every call.
- The native sound-patch table and `g_AreaPortals` have no native level sweep; they depend on
  managed `OnRemoved` arriving for every entity.
- Client side, same shared TU: `g_StringTableGameRules` is never cleared and its callback never
  re-installed after map 1 (unconfirmed effect — needs a two-map join; §7).

### 3.3 Pointers the engine keeps after we are gone, and a NULL window

- The spew hook (`shell_diagnostics.cpp:1355`) and the ConVar change callback
  (`sourcesharp_gamesystem.cpp:1504`) are installed and never removed: the engine holds pointers
  into an unloaded DLL. As DLL-tier members their removal is the subsystem's destructor.
- The global `engine` is NULL from `DLLInit` entry until `LoadManagedRuntime` writes it
  (`managed_runtime_server.cpp:306`, called at `shell_gamedll.cpp:260`), while `m_pEngine` has
  been valid since :171. Two copies, one late. `serverenginetools` is never assigned, so
  `mapentities.cpp:623` tests a constant NULL.
- `s_SteamAPIContext` is never `Init()`ed (stock: `gameinterface.cpp:582`); `GetSteamUtils` is NULL
  on a listen server.
- `networkvar.h:711`'s refusal latch cannot fire because `pBase` is NULL at its only producer —
  which means `CTakeDamageInfo::Init` stores NULL attacker/inflictor handles on the server.
  Not a globals problem; handed to the damage lane (3f) as found.
- First-registration-wins: ammo types (`shell_shared.cpp:117`) and managed ConVars
  (`sourcesharp_gamesystem.cpp:1531`) — `cs_sv_reload` silently ignores edited values.

### 3.4 Native copies of what managed owns (the MANAGED rows)

- Tables: `g_AmmoDef`, `s_Movement`, `s_DamageRules`, `s_Policy`, `g_SubscribedEvents`,
  `g_SkyboxParams` (a staging copy of `SkyCamera.cs`). `g_TeRegistry` (232,472 B) is a native deep
  copy of the schema `TempEntRegistry.cs` declares; the engine reads the ServerClass chain and the
  heap SendTables in `g_TeBuilt`, never the registry. After 1c its only reader is Playback (:733):
  it becomes a DLL-tier member built at `DLLInit`, and may shrink to a temporary freed at Close.
- Managed asking native for a value managed declared: `sv_alternateticks` (`ConVars.cs:159` →
  `ServerGameDll.cs:850` → `shell_framesteps.cpp:136` → engine registry), the four `violence_*`
  refs, the precache gate, `SourceSharpPlayerShadowEnabled`. **The value arrives as an argument on
  the crossing.** Lane D's warning stands as a rule: replacing a native ConVar with a
  `CManagedConVarRef` static swaps one static for another and the census will say so.

### 3.5 Shapes worth one step each

- **ACCESSOR (31 rows).** The accessor function is *deleted*, not re-pointed at the instance.
  Re-pointing keeps every caller reaching through a global name.
- **LATCH (70 rows; 46 GONE).** A once-latch is per-id, LEVEL-tier, owned by Diagnostics — or it is
  a managed refusal count. One `bool` for all classes (`shell_standalone.cpp:1063`,
  `shell_standalone_base.cpp:1931` — the second guards a write past an allocation) silences every
  class after the first for the life of the process; those two are bugs, fixed in 3b.
- **Transit globals are parameters**: `s_pPendingPlayerEdict`, and the dead `g_pForceAttachEdict`
  and `g_CurrentActivator` (write-only: its sole reader `CurrentActivator()` :160 has no caller).
- **Two duplicate indexes of the slot table**: `g_Standalones` (7 TUs through `StandaloneEntities()`)
  and `s_StandalonePlayers[]` (16 TUs). `g_EntitySlots.ActiveAt` + `SlotTable.Resolve` answer
  both. `g_EntitySlots` (163,856 B, the block managed holds a pointer into) and `s_SlotTable`
  (81,936 B, entry → shell + serial) are two tables, not one twice; the engine reads neither; both
  need only a stable address for the DLL's life → EntitySlots, DLL tier, cleared per level as today.
- **The game-system list has no static registration left** on the server: `igamesystem.cpp` is off
  the link line and the mechanism is the constant table `kServerSystems[]`
  (`shell_serversystems.cpp:321`). `s_pHead`, `g_pPredictionSystems`, the empty
  `DT_SourceSharpGameRules` table and the `GameMovement001` node (0 askers — agrees with
  `plan_shim` §1) are dead registrations.

---

## 4. Steps, and the `plan_shim` phase each rides

No step opens a file group that a `plan_shim` lane is not already opening. The rulings table's
`shim_lane` column is the hand-off: a lane's brief gains its slice of the ledger and one exit
criterion — **zero non-FLOOR rows whose home is in your file group.** And one rule of
construction: a lane that turns state into a MEMBER moves the `SourceSharp_*` entry points that
touch it onto that subsystem's `IShim<Area>` in the same commit series (§2 route 2) — it does
not leave free functions reaching the member through an accessor. The slot retires under
`plan_shim` §11's mark-now-remove-once rule; the integrator owns the `tools/vtable-layout`
chain rows as it owns the ABI version.

| step | rides | rows | owner |
|---|---|---|---|
| **G0** instrument + ledger + gate | now, beside Phase 1 (touches no shim file) | — | one lane |
| **G0.1** the §3.1 corrections delivered to the open Phase 1 lanes and into `plan_shim_rulings.tsv` | now | 9 | integrator |
| **G1** DEAD | one lane after Phase 1 merges ("1g"), split by file | 79 | one lane |
| **G2** scaffolding | after Phase 1, before Phase 2/3 fan out | — | integrator |
| **G3** MEMBER · CONST · MANAGED | inside 2a–2d and 3a–3f | 180 | the lane that owns the file |
| **GM** native → managed onto `IManaged<Area>` | generator + lifecycle area in G2; the rest inside the same lanes as G3, area by area | 187 `ManagedApi` fields, 31 native files | generator: integrator · areas: the lane that owns the caller |
| **G4** ConVar/ConCommand objects | Phase 5's ledger | 139 GONE + 24 | Phase 5 lane |
| **G5** static initialisers | after G3 | 138 objects | one lane |
| **G-close** ratchet, archives, client, managed analyzer | Phase 6 | — | integrator |

### G0 — instrument first

- `tools/globals-census`: the Appendix A script, parameterised by project (`server_sourcesharp`
  now, `client_sourcesharp` for Phase 4) and config. It **shares the link-edge reader with 1a's
  `tools/shim-census`** — one module, two reports; whichever lands second imports the first.
- `docs/globals-ledger.tsv`: key (`home`, demangled `name`) + disposition + owner + reason, seeded
  from `plan_globals_rulings.tsv`. `make globals-census`; a `globals` test leg beside `entity`.
- **The gate is set membership, never a count** (memory: `guard-count-is-not-an-invariant`,
  `armed-site-count-is-not-monotonic`): every mutable symbol on the link line must have a ledger
  row; a symbol with no row fails; a ledger row with no symbol is pruned in the same commit. At
  close every surviving row is FLOOR. It exits 2 — not 0 — on a missing object, an object older
  than its source, or a symbol with no DWARF line it cannot otherwise attribute (142 object-level
  rows have none today; the ledger keys those on the TU), and stamps its output with the tip
  (memory: `a-check-log-has-no-provenance`, `gitignored-build-output-decides-a-verdict`).
- Hermetic gtest `globals_census_test` over a fixture object: section kinds; UNIQUE counted once
  and LOCAL once per object; function-local detection; the refusal on a missing object.
- Mutation proof, recorded in the commit: add `static int s_x;` to one shim file → red; a
  function-local static inside an inline header accessor → red; remove → green. `copy`+`utime`
  restore, caches cleared, never `git checkout --`.

### G1 — DEAD (79)

17 of them the linker already drops. Each deletion commits its 0-importer proof (`nm -u` over the
link line, per `plan_shim` §9). By lane: S2 37 · N2 10 · H1 9 · S1 5 · H2 5 · N3 5 · unruled
headers 4 · N1 2 · S3 1 · S4 1. Includes nine engine interface pointers nobody reads, the seven
`r_*View*` vehicle ConVars (no vehicle file in either vpc), `g_GlobalTextRing`/`g_nGlobalTextSlot`,
`g_PostSimulationQueue`, `g_pShadowEntities`, `g_flServerCurTime`, `g_fGameOver`, `skill`,
`g_StudioBoneCache`, the X360-only `g_ModelSoundsSymbolHelper` (~60 lines behind `IsX360()`), the
debug-select and server-seed `XRef()` pairs, `g_RecipientFilterPredictionSystem`.
`g_pPlayerResource` was ruled from stale objects; re-measured at the tip: 0 importers over 143
objects — DEAD stands.

### G2 — scaffolding (integrator, one commit series, before the fan-out)

1. `CServerDllState` / `CServerLevelState` and `m_pDll` / `m_pLevel` on the root, created and
   destroyed in `DLLInit`/`DLLShutdown`/`LevelInit`/`LevelShutdown` where the shell already
   sequences. One **empty** subsystem class per owner in the ledger, each in its own header, so
   Phase 3 lanes fill their own class and never edit the root: EngineBinding, EntitySlots,
   EntityShells, EntityFactories, NetTables, TempEnts, Players, Movement, Physics, Overlaps,
   Sound, Precache, StringPool, StringTables, UserMessages, GameRules, GameEvents, GameSystems,
   Visibility, World, BoneSetup, Console, ManagedBridge, Diagnostics (24). Lanes chose the names
   independently; the integrator may merge the one-row owners (World, GameEvents, GameSystems,
   GameRules) into a neighbour when it creates the classes. The root's member list is the
   integrator's file, like `sourcesharp.h`'s version.
2. `IShimRoot` (one `abstract_class`, one `tools/vtable-layout` chain row, one pointer+count in
   the engine block, filled from the `this` that `LoadManagedRuntime` already receives) and its
   managed binding; `SourceSharp_InternalFactory` becomes `IShimRoot::Acquire`. `g_ShimLevel` and
   `g_ShimFrame` become members acquired through it — the first two areas on the new route, and
   the proof it works before any lane depends on it. `CShellSharedSide` + `ShellSide()`, the
   latter pinned by gtest to route 3's callers.
3. Secondary interfaces behind `EXPOSE_INTERFACE_FN`; early-resolved ones by value (§2).
4. **EngineBinding**: the 15 live interface pointers become one member. `engine`, `gpGlobals`,
   `s_factories` are deleted as duplicates of existing members (closing the NULL window of §3.3);
   `filesystem`, `soundemitterbase`, `datacache` readers point at the tier symbol. One stock edit:
   an `#ifndef SOURCESHARP` in `public/edict.h` around the inline that names
   `g_pSharedChangeInfo` — the ideal-sized C++ diff (memory: `maximise-csharp-minimise-cpp`).
5. **The reverse direction, proven once before anyone depends on it** (design and measurements
   in §2):
   - `tools/vtable-layout` gains the `implemented-by-managed` chain kind; a fifth generator in
     `SourceSharp.Generators` emits, from a C# interface, the `{vtbl, handle}` block
     (`NativeMemory`), the `[UnmanagedCallersOnly]` thunks with today's try/catch-to-`Fail`
     wrapper, `GCHandle<TSealed>` recovery (the measured form — never an untyped handle cast to
     the interface), and `Release()`. Native receives pointer **and** count and refuses on a
     mismatch with `VTableHook::MemberOf(...).slot + 1`.
   - First area: `IManagedRuntimeServer`. It is already a C++ abstract class; it changes
     implementor. `CShellGameDll::m_pManagedRuntime` holds the managed-built pointer and
     `managed_runtime_server.cpp`'s 354-line adapter is deleted.
   - `tools/bench-managed-vtable` (a benchmark branch, `2f67ee4ff`) merges here, and
     its three GC modes become `[Fact]`s over the *generated* code: the object moves and the
     call lands; the raw-address control is a generator-input the generator refuses to emit;
     a released binding is unusable.
6. Tests, each red first: two-instance independence for every subsystem class (the pattern
   `shell_entitystatics_test.cpp` already uses); level-tier state is fresh after a
   `LevelShutdown`/`LevelInit` pair; `engine` is non-NULL at the first line of `DLLInit`.

### G3 — the rows that ride Phase 2 and 3

| `plan_shim` step | lane | MEMBER | CONST | MANAGED | note |
|---|---|---|---|---|---|
| 2a/2b physics | N1 | 9 | 1 | 2 | Physics subsystem; `physics`/`physcollision`/`physprops` DLL tier, `physenv`/`g_PhysWorldObject`/`g_EntityCollisionHash`/`g_Collisions`/the shell LEVEL; fixes §3.2's leak; no `physics_globals.cpp` |
| 2c map load, vars | N2 | 2 | 2 | 0 | `g_GameStringPool` → StringPool/LEVEL; replicated ConVars per §7 |
| 2d leaf math | N3 | 4 | 5 | 0 | bone-setup scratch → BoneSetup |
| 3a engine binding | S2 | 33 | 6 | 7 | after G2.4 most of this is filling EngineBinding, NetTables, Players |
| 3b entity shell | S1 | 23 | 1 | 1 | the two one-bool latches; the duplicate slot indexes; transit globals → parameters |
| 3c physics shell, effects | S3 | 12 | 3 | 3 | `g_TeRegistry` → TempEnts; `g_SkyboxParams` → managed |
| 3d sound | S4 | 2 | 0 | 1 | the sound-patch table gets a native level sweep by being LEVEL tier |
| 3e shared systems | H1 | 34 | 1 | 8 | through `CShellSharedSide`; spew hook and ConVar callback removed in a destructor |
| 3f shared libraries | H2 | 6 | 3 | 6 | the `XRef()` headers; values-as-arguments |
| unruled stock headers | — | 0 | 3 | 2 | 4 GONE with their call sites; `bitvec.h` the same way |

Every MEMBER move lands with a two-instance fact and, for LEVEL tier, a two-map fact; every
MANAGED move lands with a `[Fact]` on the managed owner and the datatable check on a live join if
the file is both-sides (memory: `shared-file-virtual-differs-per-side`).

### GM — native → managed areas

Today native reaches managed through one block and one global accessor: `g_ManagedApi` (1,488 B,
ledger row MEMBER/ManagedBridge) behind `SourceSharp_GetManagedApi()`, called from **31 files**
under `game/server` and `game/shared` (43 with the client). On the managed side the 187 fields
are filled from 191 `[UnmanagedCallersOnly]` statics in `Entry.cs` that find their target through
14 static fields and `Engine.SetActiveSide` (171 non-test references to `SetActiveSide`; 17 other
`ActiveSide` references; 13 non-test files name either). Both halves go together — removing `SourceSharp_GetManagedApi()` natively *is*
removing `s_Server` managed-side, because the pointer native now holds is the instance.

| area (by `ManagedApi` field prefix; ≈ fields) | native holder | rides |
|---|---|---|
| lifecycle — `IManagedRuntimeServer` (exists) | root | **G2** |
| `Frame*`, `Level*`, `Save*`, `Reload*`, `Shutdown` (≈10) | root / `m_pLevel` | G2, straight after lifecycle |
| `Server*` (≈64 — the largest; entity think/touch/input/spawn dispatch) | EntityShells · EntitySlots | 3b |
| `OnStandalone*`, `OnEntity*`, `Hierarchy*` (≈20) | EntityShells | 3b |
| `OnPlayer*`, `Player*`, `OnRespawn`, `OnWeapon*` (≈15) | Players | 3a / 3b |
| `Move*`, `Prediction*`, `Anim*` (≈16; shared TU) | `CShellSharedSide`::Movement | 3e |
| `OnScripted*` (≈13) | EntityShells (scripted) | 3b |
| `OnTemp*`, `Net*`, `Con*`, `OnGame*`, `Get*`, `Can*`, `Is*`, `Allow*` (≈20) | TempEnts · NetTables · Console · GameRules | 3a, 3c, 3e |
| `OnClient*`, `Client*`, `OnHud*` (≈9) | the client root | Phase 4 |
| `Benchmark*`, `Shell*` probes (≈4) | — | deleted with `plan_shim` 1c's instruments |

Rules of construction, the mirror of G3's:

- The native **caller** decides the area: the subsystem that makes the call holds the
  `IManaged<Area> *` as a member, handed to it at construction. No subsystem calls
  `SourceSharp_GetManagedApi()`; the function is deleted when its last caller moves, and the
  ledger row `g_ManagedApi` goes with it.
- An area pairs with its `IShim<Area>` twin and lands in the same commit series, so a lane
  converts one subsystem's whole boundary once, not twice.
- Per-entity callbacks keep their ids: `self` is the *side's* dispatcher, and the entity handle
  stays an argument. One handle per area, not one per entity — 8,192 GC handles would be a
  different design with a different cost, and nothing here needs it.
- LEVEL-tier areas are built in `OnLevelInit` and `Release()`d in `OnLevelShutdown`; native nulls
  its pointer **before** calling `Release()` (§2's rule 2).
- `Engine.SetActiveSide` is removed per area, not at the end: once an area's thunks are
  generated they do not write it, and its 17 other references are converted to take the side from the
  object they are already a member of. The last reader going is a G-close ratchet.
- `ManagedApi` slots retire under `plan_shim` §11's mark-now-remove-once rule, like `EngineApi`'s.

### G4 — ConVars

163 ConVar/ConCommand objects: 139 GONE, 11 DEAD (G1), 4 MANAGED, 4 MEMBER, 5 contested FLOOR.
Phase 5's `docs/convar-ledger.tsv` decides every one of them; this plan adds only that the
census, not a source grep, is what proves "no native ConVar outside the keep list" — a
`CON_COMMAND` macro hides its object from grep and not from `readelf`. If the replicated-ConVar
experiment (`plan_shim` §10) fails on the client half, the fallback is one native table of
22 × 144 B = 3,168 B, entered in the ledger as FLOOR with the experiment's output as its reason.
The server half is already proven (`ConVarFlags.Replicated`, `ConVar.cs:53`; five live
precedents in `GameVars.cs`).

### G5 — static initialisers

138 of 143 objects run one; `.init_array` has 165 entries. After G3 the ledger's FLOOR rows name
the only objects entitled to one. Same gate shape: the **set** of link-line objects defining
`_GLOBAL__sub_I_*` must be a subset of an allowlist; stock headers that drag an initialiser into
every TU (`vec2/vec4` at `-O0`, `vprof.h`) are fixed at the include, not by forking mathlib.

### G-close (Phase 6)

- Ratchet gtest: the ledger has only FLOOR rows and the census agrees, debug and release.
- Archives (133 symbols; library-internal, recorded, not owned): `particles.a` 21, `tier2.a` 12,
  `tier3.a` 19, `dmxloader.a` 12 leave with the libraries Phase 0f/6 already plans to drop;
  `tier1.a` 41 and `mathlib.a` 28 stay.
- The client DLL gets the same census as the entry instrument for Phase 4 (lanes C1–C3).
- Both ABI structs end as the bootstrap: `EngineApi` = version + `IShimRoot`; `ManagedApi` =
  what `Entry.LoadSide` returns. `tools/abi-slot-census.py` reads 472 and 187 today and is the
  instrument. `Entry.cs`'s 14 static fields reduce to the load-context bookkeeping `LoadSide`
  itself needs; `SetActiveSide`/`ActiveSide` have zero references (a `[Fact]` over the
  assemblies' IL, not a grep).
- Managed: a reflection `[Fact]` over `SourceSharp.Server`, `SourceSharp.Shared` **and
  `SourceSharp.Bootstrap`** — every
  static field is `readonly`/`const` or on an allowlist — so MANAGED rows cannot land as C#
  statics.

---

## 5. Verification

`plan_shim` §9's table applies unchanged. Additions:

| gate | command | pass |
|---|---|---|
| globals ledger | `make globals-census` on a cold build, debug and release | every symbol has a row; exit 2 if it cannot measure |
| reconciliation | the `.so` instrument (Appendix A) | objects − linker drops + archives = `.so`, to the symbol |
| two maps, one process | `tools/live-gate` two passes | green; and `changelevel` back to the first map for anything LEVEL tier |
| crossing cost | `tools/bench-managed-vtable/run.sh`, and `tools/live-gate-budget.tsv` before/after each area | recorded in the commit; not gating (memory: `perf-pass-comes-after-the-port`) |
| GC safety | the three generated-code `[Fact]`s (moves / raw-address refused / released binding) + `cs_sv_reload` on a live map | green; reload leaves no live handle into the unloaded context |
| unload | DLL unload under the dedicated server, then a spew and a cvar change | no call into freed text (§3.3) |

---

## 6. What this plan does not do

- It does not add a context argument to the existing 472 slots. Slots move to bound shim
  interfaces area by area, inside the lane that owns the area; an unmoved slot keeps working.
- It does not fork stock headers to remove a static an include can avoid.
- It does not audit the client DLL, or managed statics beyond the boundary's own
  (`Entry.cs`, `Engine.ActiveSide`); it leaves the instruments that will.
- It does not allocate a GC handle per entity, and it never pins.
- It does not trade design for speed: an accessor call at the boundary instead of a direct global
  read is a cost to record at the perf pass, with numbers (memory: `perf-pass-comes-after-the-port`).

---

## 7. Open questions, and the experiment that settles each

| question | experiment | blocks |
|---|---|---|
| Is `DT_Local` really unreferenced on the wire? | remove the table, live join, datatable recipe | G1 |
| Does vphysics keep the name pointer (`s_bNameProbeDone`)? | the `[SS-PHYSNAME] … same=` line in any past `console.log` | G1 |
| Is `g_nInvisibleRangeReach` ever non-zero? | two-map devapi run | G1 vs 3b |
| Does the client lose game-rules string-table updates after map 1? | two-map join, change a rule on map 2 | 3e |
| Can `CServerGameClients`/`Ents` move into `m_pDll`? | only after Phase 5 removes their member ConVars | G-close |
| Does any engine path call the spew hook or cvar callback after `DLLShutdown`? | dedicated server, `plugin_unload`-style unload, then spew | 3e |
| `g_TeRegistry`: member, or a temporary freed at Close? | after 1c, count Playback reads on a live map | 3c |
| Replicated ConVar, client half | `plan_shim` §10's experiment, unchanged | G4 |
| Does a level-tier area re-acquired each `OnLevelInit` cost anything measurable, and does managed ever hold one across a level? | G2's `IShimLevel` conversion, two-pass live gate; a managed `[Fact]` that a level-scoped binding is unusable after `OnLevelShutdown` | G2 |
| Can `tools/vtable-layout` type every argument the entity/physics slots use today (`float const*`, id structs)? | run it over a draft `IShimEntities` with the 20 most-called slots; exit 2 names the unmapped type | 3b, 2a |
| Does the measured per-crossing saving (§2: 5.72 → 4.07 ns) hold on MSVC x64, and with hot reload's collectible context in play? | `tools/bench-managed-vtable/run.sh` ported to a `.bat`; a variant loading the callee in a collectible ALC | never blocks |
| Does any managed entry re-enter the other side mid-call (what `SetActiveSide` papers over today)? | assert in the generated thunk, debug only: entering side B while side A's thunk is on this thread's stack logs once; two-pass listen-server live gate | G2 |

---

## 8. Execution

As `plan_shim` §11: worktrees per lane reset to the integration branch's tip, scratch files prefixed
with the lane id, the integrator owns the root's member list alongside `sourcesharp.h`'s version
and the vpc rows. A lane re-measures its ledger slice against its own tip before editing (memory:
`a-brief-scoped-from-a-handover-goes-stale`) — the census takes 1.4 s. G0 and G0.1 can start
today; nothing else here precedes Phase 1's merge.

---

## Appendix A — how the measurements were taken

```
# the link edge, never the directory
awk '/^build [^:]*server[^:]*\.so[^:]*: /{p=1} p{print} p&&!/\$$/{exit}' sdk_csharp_debug.ninja | tr ' ' '\n' | grep -c '\.o$'   # 143

# per object: section/binding/size, and the DWARF line of the definition
readelf -SW X.o; readelf -sW X.o        # OBJECT|TLS|GNU_UNIQUE in .bss* .data* .tbss* .tdata*  (.data.rel.ro* and .rodata* counted apart)
nm -l --defined-only X.o                # file:line; container prefix /my_mod/src/ mapped to the worktree
c++filt                                 # one batch

# link level: LOCAL counts once per object, everything else once per name
# second instrument: the linked .so
readelf -SW server.so | grep -E ' \.(bss|data|init_array) '     # .bss 0x106080  .data 0xb5b0  .init_array 0x528 (165 entries)
readelf -sW server.so                   # .symtab, same section filter -> 714 after the same exclusions
# archives: nm -C --defined-only lib/public/linux64/<a>.a | type in [BbDdu], joined by name (LC_ALL=C)

# static initialisers
for o in <link line>; do nm $o | grep -q '_GLOBAL__sub_I_' && echo $o; done | wc -l                # 138

# release
globals_census.py src sdk_csharp_release.ninja out.tsv          # 599 link-level; comm -13 debug release -> 0 names

# a reader count is `nm -u` over the 143 objects, not grep: `engine` and `random` overmatch as words
```

Beside this file, untracked with it: `plan_globals_census.py` (the instrument, 176 lines; G0
moves it to `tools/globals-census`), and `plan_globals_rulings.tsv`. The
six lanes' working notes (every lane read `216533fa8`) were not kept; the rulings table's `note`
column summarises their per-row reasoning.


