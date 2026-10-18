# plan_forwarders_everywhere.md: no vtable crosses between native and managed

Status: approved for execution 2026-10-18 (see Decisions). Follows
plan_engine_forwarders.md, whose phase 6 left `src/abi/VtableLayouts.g.cs`
holding only our own tables.

## 1. Why

The user's ruling (Decisions, 2026-10-18): every native<->managed call goes
through generated forwarders and never through a vtable. plan_engine_forwarders
did that for the engine and vphysics. What still crosses as a vtable, at
8b35a67 (counted from tools/vtable-layout's emitted rows):

| Kind | Tables | Who builds the table | Who calls it |
|---|---|---|---|
| shim areas (`ishim_areas.h` 35, `ishim_areas_client.h` 19) | 54 | native (`CShim*`) | managed, through `ShimRef<TVtbl>` in `ShimAreas.g.cs` |
| hand-written shims: root, console, frame, level | 4 | native | managed (`ShimRoot.cs`, `ShimConsole.cs`, `EngineFrame.cs`, `EngineLevel.cs`, `EngineSide.cs`) |
| the particle library (`iparticlelib.h`) | 1 | libss_particles | managed (`ParticleLibrary.cs`) |
| managed-built areas (`imanaged_areas.h` 18, `imanaged_areas_client.h` 26) | 44 | managed (`ManagedVtableGenerator`) | native, as C++ virtual calls |
| managed root, two runtimes, Steam callback target | 4 | managed | native, as C++ virtual calls |
| vphysics callbacks (`vphysics_interface.h` 5, `vcollide_parse.h` 1) | 6 | managed, in Valve's layout | vphysics, as C++ virtual calls |

None of these is ABI-dependent any more (phase 6 refuses a table MSVC orders
differently), but each is still a layout that two compilers, or a compiler and
a C# struct, must agree on slot by slot, held by `vtable_index_agreement_test`,
`[SlotHash]` and the count checks. A forwarder table is a plain array of
function pointers whose order is a generated list, on both sides.

## 2. The three shapes

### 2a. Native builds, managed calls: forwarders (as plan_engine_forwarders)

For each shim interface, tools/engine-forwarders writes one forwarder per slot,

    static float SSFwd_ShimEngine_GetCurrentTime( IShimEngine *self )
        { return self->GetCurrentTime(); }

into the side's forwarder library (libss_fwd_server / libss_fwd_client), and
the C# struct `ShimEngineFwd` beside the engine ones. The native classes stay
as they are (`CShim*` over the `SourceSharp_*` bodies); only managed's way in
changes. `self->GetCurrentTime()` is a virtual call compiled by the same
compiler that compiled the class, so no layout crosses.

- **Every slot is forwarded**, not only the ones managed calls: the shim
  interfaces exist for managed, and their slots already only append
  (tools/shim-areas' span check). A row says `every=True` instead of a method
  list; the append-only refusal applies as for engine rows.
- **Managed's call shape does not change.** `ShimRef<T>` holds `Self` and the
  table; `ShimAreas.g.cs` (tools/shim-areas) keeps writing
  `EngineBase.Vt->GetCurrentTime(EngineBase.Self)`, now over `ShimEngineFwd`.
- **The table is acquired by name** through the root, as the engine tables are
  (`SSFwd_<Table>` through `SourceSharp_InternalFactory`, which `SSFwd_Serve`
  answers), with `Forwarders.TryBind`'s count check.
- **The root is the exception that is handed over**: managed needs a table to
  call anything, so `SourceSharp_LoadMySide` passes the root object and the
  root's `SSForwarderTable *` to `LoadSideV2` in place of today's entry count.
- **The particle library** is a native object served by libss_particles; its
  forwarders go in libss_fwd_client over `iparticlelib.h`.

### 2b. Managed builds, native calls: native forwarders over a managed table

Managed keeps building the block it builds now (`ManagedVtable`: word 0 points
at the table, the header follows), but native stops calling it as a C++
object. tools/managed-areas writes, from `docs/managed-interfaces.tsv`, per
interface:

    struct SSManagedResourcesTable          // the table managed writes
    {
        void ( *Release )( IManagedResources *self );
        int32_t ( *Validate )( IManagedResources *self, const uint32_t *, int32_t );
        void ( *Precache )( IManagedResources *self, const char *a0 );
    };

    class IManagedResources : public IManagedObject   // no virtual anywhere
    {
    public:
        void Precache( const char *a0 ) { return Table()->Precache( this, a0 ); }
        ...
    };

`IManagedObject` holds the one data member, the table pointer managed wrote at
word 0, and its `Release` / `Validate` read the first two fields every table
starts with. Native call sites (`pArea->Precache( "x" )`) do not change, and
the inline methods are the generated forwarders of this direction. The order
is the TSV's, on both sides; `Validate` and the per-slot hashes stay as the
check that native and managed were generated from the same rows. Managed's
C# table struct is written by tools/managed-areas from the same TSV rather
than parsed back out of a C++ header by tools/vtable-layout.

Reload is unchanged: the block stays where native holds it and the rebuilt
module rewrites its word 0 (plan_managed_vtable_reload.md L4).

### 2c. Valve calls, managed implements: a native class over a managed table

vphysics calls `IPhysicsCollisionEvent` and the rest through its own C++
vtable, so something native must be that object. As the Steam callback is
(plan_engine_forwarders 8c), libss_fwd_<side> builds a class per interface,
deriving the Valve one, whose overrides call a managed-built table of shape
2b: `SSPhysicsCollisionEvent::PreCollision( e ) { m_pTarget->PreCollision( e ); }`.
Managed allocates the object (`SizeOf`, `Construct`, as for Steam) and hands
vphysics the native pointer. The six become ordinary managed-built interfaces
(`IManagedPhysicsCollisionEvent`, ...).

## 3. What goes, at the end

- `src/abi/VtableLayouts.g.cs`, `Vtable.cs`'s shim acquire, `ShimRef<TVtbl>`'s
  vptr read, and tools/vtable-layout's emitter (its header walk stays as
  tools/engine-forwarders' input for the engine rows).
- `vtable_index_agreement_test`'s length arms and the generated
  `shim_areas_layout.inc` / `managed_areas_layout.inc`; the forwarder
  libraries' tests take over (each table's count and a call through it).
- `VTableHook::MemberOf( ... ).slot + 1` counts handed to managed.
- `[ImplementedByManaged(ValveLayout = true)]` and `ReservedWords`.
- CLAUDE.md's porting rule "New managed->native surface is a bound interface
  (`IShim<Area>` on the bound-vtable pattern, a `chain=` row in
  tools/vtable-layout)" is rewritten to name forwarder rows.

## 4. Phases

Each phase builds, passes its tests and is committed before the next.

1. **Spike: IShimFrame through forwarders.** tools/engine-forwarders learns a
   native-implemented row with `every=True` over one of our headers; the
   server library serves `SSFwd_ShimFrame`; `EngineFrame`, `EngineSide`'s
   frame read and `ShimProbes` call through `ShimFrameFwd`. Facts: the table's
   count and a call through it in the native fwd tests; the managed binding
   over a fake table.
2. **The generated shim areas** (54): tools/shim-areas switches `ShimRef` to the
   forwarder structs; the rows are generated from the TSV's interfaces.
3. **The other native-built tables**: console, level, the particle library,
   and the root (the `LoadSideV2` change on both sides).
4. **Managed-built tables** (48), shape 2b: tools/managed-areas writes the
   tables and the non-virtual classes; the hand-declared ones (root, both
   runtimes, Steam target) move to the TSV or are written the same way;
   native fakes in tests build tables.
5. **vphysics callbacks** (6), shape 2c.
6. **Delete** what section 3 lists; CLAUDE.md's rule.

## 5. Checks

- The forwarder generator's append-only refusal covers shim rows as it does
  engine rows; tools/managed-areas keeps the TSV's order and the hashes.
- A managed bind refuses a table native serves with fewer slots than the
  struct reads (`Forwarders.TryBind`), naming both counts.
- No mutation proof is "the build still passes": each phase breaks its
  subject (drop a slot, swap two rows) and shows the red.

## 6. Costs

A shim call gains one direct call and return (the forwarder, then the
virtual). The engine forwarders' spike measured that shape at +0.5 to +1 ns a
call (plan_engine_forwarders.md 8a, GetModelType, 50M calls x 5 rounds).
Shim calls are per frame and per entity, so phase 1 re-measures a hot one
(IShimFrame's TickCount) the same way, before and after, and records the
numbers here.

## 7. Decisions

- 2026-10-18 (user, relayed by a peer session): every native<->managed call
  goes through the generated forwarders and never through vtables. The user
  acted on it in this session by asking for this plan.
- 2026-10-18 (user): "Write as plan, commit and start executing" -- this plan
  is committed and executed without the usual review pause.
