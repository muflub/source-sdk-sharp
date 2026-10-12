# Backlog: lane 9f-managedvtable (the reverse vtable direction)

Status: **paused, 1 of 7 steps done, not merged.** Phase 0 of `plan_sharp_repo.md`
decided not to land its code on the fork; this note is what survives of the lane.

## What it is

`plan_globals.md` G2 item 5 (and §2 for the design and measurements): the
**native -> managed** direction of the boundary, proven once before anything depends on
it.

Today managed -> native goes through bound vtables (`ishim_level.h`, `ishim_frame.h`;
`tools/vtable-layout` chain rows generate typed pointers into
`SourceSharp.Abi/VtableLayouts.g.cs`). Native -> managed does not: it goes through ~439
free `SourceSharp_*` entry points and ~629 ABI slot fields, and a free entry point has no
`this`, which is precisely why the native side needs globals to find its state. The lane
builds a managed-built vtable (`{vtbl, handle}` block) that native calls through a C++
abstract class.

## Where it stopped

| | |
|---|---|
| Branch | the lane's branch in `source-sdk-2013` |
| Base | the plan_shim integration branch @ `281d19e1b` |
| Only commit | `0848884ac` "9f-managedvtable 1/7: tools/vtable-layout gains the implemented-by-managed chain kind" |
| Files | `tools/vtable-layout` only (+194/-1) |
| Lane scratch | the lane's directory under `~/.cache/sourcesharp/lanes/` (`9f-notes.md`, `9f-run.sh`) |
| Worktree | gone; it had 0 uncommitted files when paused (2026-09-21, `~/.cache/sourcesharp/paused-lanes.md`) |

### Step 1/7, done: the `implemented-by-managed` chain kind

A chain row may say `kind='implemented-by-managed'`: native owns only the C++ abstract
class and managed writes the table. The struct keeps the same shape (field N at byte 8N)
and is emitted with `[ImplementedByManaged("<class>")]`, which the generator in
step 2 will require of any struct it fills.

The kind refuses four things, each one a layout the reverse direction cannot survive:

- a **virtual destructor**: it takes two Itanium slots, and `delete` through the pointer
  would free memory the C++ heap never allocated;
- a table that does **not end in `void Release()`**: the generator owns `Release`, and
  native takes its slot count from it;
- **any slot managed cannot spell**, which would leave a hole native calls through;
- **any policy reason against a slot**, since policy decides what managed may *call*.

An unknown kind is refused rather than read as the default. No row uses the kind yet, so
`VtableLayouts.g.cs` stays byte-identical (33 interfaces, 971 slots at the lane's base).
Self-test went from 41 to 49 arms, 0 failed. The mutation proof disabled one rule at a time:
no-dtor -> 1 red, no-release -> 2 red, no-typing -> 1 red, no-policy -> 1 red, no-kind-check
-> 1 red. The arm "every implemented-by-managed row carries the attribute" is 0 == 0 today
and cannot fail until a row exists. It starts to bite at step 3.

## What remains (steps 2-7)

The lane numbered its work N/7, but only step 1 was committed, so the exact split of 2-7
was never recorded. Below are the brief's remaining items, ordered as the lane's own notes
(`9f-notes.md`) had them:

1. **A fifth generator in `SourceSharp.Generators`** (`ManagedVtableGenerator.cs`, beside
   NetState, ScriptedVars, StringTable and UserMessagePacker). Given a C# interface it emits:
   - the `{vtbl, handle}` block over `NativeMemory`;
   - `[UnmanagedCallersOnly]` thunks, wrapped in today's try/catch-to-`Fail`;
   - **`GCHandle<TSealed>` recovery**, the measured form (never an untyped handle cast to
     the interface);
   - `Release()`.

   Planned support file `Abi/ManagedVtable.cs` (attributes, block, helpers) and generator
   diagnostics `SSVT001..011`. **The raw-address form must be a generator input the
   generator refuses to emit**, not a runtime hope.
2. **Native accept check**: native receives the pointer **and** the count, and refuses on a
   mismatch with `VTableHook::MemberOf(&Release).slot + 1`. This is what turns layout drift
   into a loud failure instead of a wild call. Planned: an engine-free accept check in
   `managed_runtime_server.h`, and a `VTABLE_LENGTH_TEST` in the agreement test. **Prove it
   fires by planting a mismatch.**
3. **First area: `IManagedRuntimeServer`** changes implementor. The lane's design notes:
   - it loses its virtual destructor (it becomes protected and non-virtual) and gains
     `OnSystemStop` (today that is native `SourceSharp_UnloadManagedSide`);
   - `OnMapLoad_*` take `const MapLoadInfo*`;
   - `Release()` goes last, for 26 slots;
   - `UnloadManagedRuntime(p)` becomes `p->Release(); SourceSharp_UnloadManagedSide();`;
   - in `shell_gamedll.cpp`, the `OnSystemStop` call site moves to `UnloadManagedRuntime`,
     and the later `UnloadManagedRuntime` is dropped;
   - `CShellGameDll::m_pManagedRuntime` holds the managed-built pointer;
   - **the adapter in `src/sourcesharp/game/server/managed_runtime_server.cpp` is deleted**
     (355 lines at fork `02851a196`).

   Managed side: `Bootstrap/ServerRuntime.cs` (the interface, a sealed implementation and a
   `Game` field) plus an Entry acquire thunk. ABI: **append**
   `ManagedApi.ServerRuntimeAcquire(int32_t* pnEntries) -> void*`, add it to
   `SHELL_REQUIRED` and to the native test's `kAfterShellBlock`, and bump the ABI version
   **once, at the end** (memory `abi-version-bumps-collide`). Rewrite sections 1-3 of
   `managed_runtime_server_test.cpp`.
4. **Retire the 25 `ManagedApi` lifecycle slots** that the new route replaces:
   `SS_RETIRED` markers, `docs/abi-retirements.md`, the REQUIRED list, Entry thunks
   deleted, and `Shell`/`ManagedSlotWiringTests` updated. **Every caller must land on the
   new route in the same commit**, because a NULLed slot is not a removed slot. A nulled
   slot once returned false for every prop on every map, and neither suite nor the live
   gate saw it.
5. **Behaviour facts** in `Tests/ManagedVtableTests.cs`, in a `DisableParallelization`
   collection:
   - GC compaction moves the object and the call still lands;
   - a released binding is unusable, and fails loudly;
   - a late call reaches the heir;
   - an exception does not escape into native frames;
   - two bindings are independent;
   - the generator diagnostics SSVT001..011.

   Each fact needs a mutation that turns it red. For a generator, beware a test that reads
   the same declarations the generator reads: that check cannot fail.
6. **The bench branch.** A benchmark branch at `2f67ee4ff` measured that a managed-built
   vtable with a `GCHandle` context costs less than today's static-plus-TLS route (5.72 ->
   4.07 ns per crossing, `plan_globals.md` §2). The brief said to merge
   `tools/bench-managed-vtable` and turn its three GC modes into `[Fact]`s **over the
   generated code**. A benchmark is not a test, so no timing loop goes into the suite.
   `plan_sharp_repo.md` drops this branch from the fork, so whoever resumes takes the tool
   from that sha.

## Constraints for whoever resumes

- `9e-scaffolding` (the rest of G2: `IShimRoot`, two lifetime tiers, 24 subsystem classes)
  also touches `tools/vtable-layout`. 9e adds a chain **row**, 9f adds a chain **kind**.
  Keep them separate.
- Collector-provoking facts must run memory-capped
  (`~/.cache/maptools/bin/run-capped 4G ...`), with `~/.dotnet/dotnet` and
  `MSBUILDDISABLENODEREUSE=1`.
- The lane's baseline, at `281d19e1b`: native 308 entries / 4,151 tests / 0 failures;
  managed 6,432 passed / 0 warnings; ABI 251. **Re-measure at the resume base.** These
  numbers are only true at the lane's own base (memory
  `a-lanes-evidence-is-only-true-at-its-base`).
- In the new repo, `tools/vtable-layout`, `SourceSharp.Generators` and
  `managed_runtime_server.*` are OURS paths. Resume there, not on the fork, once Phase 2
  lands; re-apply `0848884ac`'s diff to `tools/vtable-layout` by hand.
