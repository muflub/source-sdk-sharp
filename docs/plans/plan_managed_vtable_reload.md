# Managed-built objects that survive a reload

Status: reviewed. L0 is taken up BEFORE `plan_engineapi_vtables.md` E-last,
because E-last deletes the ManagedApi fields tools/managed-areas reads its
signatures from. L1-L6 follow E-last AND that plan's Phase R (the user,
2026-10-01): Phase R shrinks Abi first, and the lifetime runtime here (block
registry, patching, call depth, slot hashes) then lands in an Abi already in
its final shape. It holds slot addresses as `nint`, never
`delegate* unmanaged`, so it passes Phase R-last's assembly fact as written.

The phases are named L ("lifetime") so that "R" means only
plan_engineapi_vtables' Phase R.

## Why

Today every managed-built object (the root, `IManagedRuntimeServer` and the
generated `IManagedX` areas) lasts for exactly one managed load:

- native acquires each one from the root on every load
  (`ManagedRoot_Accept` / `ManagedRoot_AcquireAreas`);
- native holds them in `static CShimRoot g_ShimRoot`
  (`shell_internalfactory.cpp`);
- native releases them in `ManagedRoot_Release()` before `UnloadSide`;
- callers re-read `ManagedX()` at the point of use and get NULL between
  loads.

Three things are wrong with that:

1. **The blocks leak.** `ManagedVtable.ReleaseBlock` tombstones its block and
   never frees it. That is one block per object per reload.
2. **The tombstone does not protect what it claims to.** The block's slots
   point at `[UnmanagedCallersOnly]` thunks inside the outgoing assembly, so a
   call after unload jumps into unloaded code before it ever reads the
   tombstone.
3. **The ownership is a free-standing static.** It is not the game DLL
   object, which is the one static the engine requires.

## The design

### 0. Every managed-built interface is generated, from its own spec

tools/managed-areas generates an `IManagedX` only from existing ManagedApi
slots: it reads their signatures from `SourceSharpManagedApi` in
sourcesharp.h, and their grouping from docs/engineapi-areas.tsv. Two
interfaces cannot be generated that way, so they are hand-declared:

- `IManagedRuntimeServer` (D5, sourcesharp_gamesystem.h) predates the
  generator, and its methods were never one-for-one slots. The map loads take
  `const SourceSharpMapLoad *`, and E1/E11 added methods that were never
  slots.
- `IManagedRoot` (D4, imanaged_root.h) is the bootstrap that hands out the
  rest, and was never a slot.

E-last deletes the ManagedApi fields, which leaves the generator with no source.
So the generator moves to a spec of its own: `docs/managed-interfaces.tsv`, one
row per method (interface, method, C return type, C parameters), seeded from
today's signatures. Every managed-built interface, the root and the runtime
included, is generated from it. `HAND_WRITTEN` goes.

From then on, L1's fixed slots (Release, `Validate`, the slot hashes) are
emitted by one tool and cannot be forgotten on any interface.

### 1. Abi owns the blocks, and a reload patches them in place

SourceSharp.Abi loads once, into the default context, and never unloads. It
owns:

- every block;
- a registry keyed by contract name, holding for each contract its block, its
  slot count and its slot signatures.

On the first load, Abi allocates a block per contract. On every load after
that, the side's generated code hands Abi a new target and new thunk
addresses, and Abi writes them into the **existing** block: word 1 gets the
new GCHandle, and the vtable gets the new thunks. The old target's handle is
freed after the new one is written.

`ManagedVtable` and the `Create`/`Target`/`Release` runtime move from
SourceSharp.Shared into Abi. The per-side `ManagedBindings` keeps only the
builders.

Native acquires each object **once per process** and holds a plain
`IManagedX *` for the rest of it. There is no NULL between loads.

### 2. Native ownership lives on `CShellGameDll`

The managed root, the runtime and every generated area become members of the
game DLL object (`s_gameShellDll`, `shell_gamedll.cpp`), not a separate
static:

- `ManagedX()` reaches them through it, as `ShellGameDll_TransmitCapture*`
  already does;
- the generated `shell_managed_areas.inc` changes to match;
- `CShimRoot` keeps only the native-side shim areas and the console.

### 3. Release is slot 0, Validate is slot 1

Every managed-built interface starts with two generated, fixed slots:

```cpp
virtual void    Release() = 0;                                                  // slot 0
virtual int32_t Validate( const uint32_t *pSlotHashes, int32_t nSlots ) = 0;   // slot 1
```

- **Release at slot 0** is a fixed index, like a destructor.
- **Validate at slot 1** describes the object, so native can check it on
  acquire. Native passes its compiled per-slot hashes. Managed compares them
  against its own and answers -1 if they match, or the first slot that
  differs. A count mismatch reports as index `min(native, managed)`.
  `ManagedAccept` refuses with "IManagedX slot N differs". The out-of-band
  counts (`Acquire`'s `&nEntries`, `LoadSideV2`'s `nManagedEntries`) go.

**What a slot hash covers:**

- its ABI shape: return type, parameter count, and each parameter's ABI type;
- never its name, so a method can be renamed freely.

**One source for the hash.** tools/vtable-layout already maps each C++ slot to
its canonical `delegate* unmanaged<...>` spelling. The hash (FNV-1a, 32-bit)
is taken over that string:

- tools/managed-areas emits it into the C++ header as a
  `static const uint32_t k<Interface>SlotHashes[]`;
- ManagedVtableGenerator bakes it into the generated `Validate`.

The two can disagree only when native and managed were built from different
headers, which is the skew this check exists to catch.

**Rules the tools enforce:**

- tools/vtable-layout requires Release at slot 0 and `Validate`, with exactly
  that signature, at slot 1. If slot 1's own shape could change, native would
  mis-call the check before it learned anything.
- SSVT005 checks the same on the managed side.
- The hand-declared `IManagedRuntimeServer` and `IManagedRoot` are reordered to
  match.

The count stays **exact**: no spare capacity, because a grown contract is
useless to a native build compiled without it.

**What it cannot see.** A slot whose shape is unchanged but whose meaning
changed (two `int` parameters swapped) hashes the same. Only review catches
that.

### 4. A reload must match the hashes already registered

On the first acquire, Abi records each contract's slot hashes: native's array,
which `Validate` matched. On every reload, Abi compares the new build's
generated hashes against those, **exactly**. Count and every slot must match,
and anything else refuses the reload, naming the contract and the first slot
that differs. The old build keeps running, and adopting the new one takes a
native rebuild and a restart.

The refusal happens **before** the old side unloads. It is decided from the new
assembly's generated tables, read before `UnloadSide`, so a refused reload
costs nothing.

### 5. There is no gap

A reload is one synchronous call at a frame boundary
(`SourceSharp_ServiceReloadRequest` from GameFrame): save, unload, load,
patch, restore. Nothing else runs in between.

The rule is still checked rather than trusted. For the duration of the swap
every slot points at a **trap table** (native stubs that fail loudly, naming
the contract and slot). A call that reaches one is a bug in the reload's own
ordering, and it says where.

### 6. No reload from inside a managed call

Abi keeps a managed-call depth: the generated thunks raise it on entry and
lower it on exit. It is a plain int, because every crossing is on the engine
thread. A reload requested at non-zero depth is refused loudly. Reloads are
requested from native (`cs_sv_reload`'s native body, the file watcher at a
frame boundary), so this should never fire. The check exists so that it can.

### 7. Release happens once, at shutdown

Native calls slot 0 on each object exactly once, at the final unload. That is
the only place a block is freed (`NativeMemory.Free`). The managed safety net
releases whatever native did not, tracked in Abi's registry rather than read
from a block that may already be freed.

## Phases

Each phase is one commit, with make check passed and, where runtime behaviour
changes, the live gate (two passes, with the reload steps) passed.

| | Phase | Exit evidence |
|---|---|---|
| L0a | tools/managed-areas reads `docs/managed-interfaces.tsv`, seeded from the ManagedApi signatures; every existing generated interface comes out byte-identical | tools/managed-areas' self-test; the regenerated `imanaged_areas.h`, `.inc`, layout test and `ManagedAreas.g.cs` diff empty; VtableLayouts.g.cs unchanged; a spec row changed by hand turns the check red |
| L0b | `IManagedRuntimeServer` and `IManagedRoot` get spec rows and are generated; `HAND_WRITTEN` goes | each generated declaration is slot-for-slot what the hand-written one was (the layouts diff empty); make check; gate |
| L0c | the hand-written declarations are deleted (sourcesharp_gamesystem.h, imanaged_root.h) | build; nothing references the old declarations; make check |
| L1 | Release at slot 0, generated `Validate` at slot 1, slot hashes from one canonical spelling, `ManagedAccept` through `Validate`, both hand-declared interfaces, out-of-band counts removed | tools' self-tests (Release and `Validate` pinned to slots 0 and 1); generator facts (SSVT005 refuses either out of place); `Validate` accepts its own hashes, refuses one changed hash at its index and a short array at its length, and accepts a rename with an unchanged shape; each refusal fact first shows the unchanged input accepted |
| L2 | Native ownership onto `CShellGameDll`; `g_ShimRoot` keeps the shim areas only | accessor facts; the destructor asserts every holder is empty; gate |
| L3 | Abi registry and runtime; `ManagedVtable` moves from Shared; blocks freed at release | facts: allocate, release and free (allocation count back to zero); the safety net after a native release touches nothing; a second release is refused |
| L4 | Patch in place on reload; acquire once per process; the trap table during the swap | facts: patch rewrites word 1 and every slot; the old handle is freed; a slot reached mid-swap fails loudly; gate: the reload steps call through every area after the reload, and no old context stays pinned |
| L5 | Reload refusal: the new build's hashes must equal the registered ones, checked before unload | facts: an identical build patches; a changed slot hash or count refuses, naming the contract and slot, and the old build still answers; the identical-build case runs first (the refusal test's control) |
| L6 | Call-depth counter and reload refusal | facts: a reload at depth 1 refuses; the counter is back to 0 after a throwing slot; the cost per crossing is measured and recorded |

## What has to fail if this is done wrong

- **A reload that never patches.** The live gate's second map load and its
  reload step call every area. A block still pointing at the old target pins
  the old context, and f45693d's unload check fails the gate.
- **A patch that misses a slot.** A trap stub or an old thunk is reached, and
  either fails loudly.
- **A leak that comes back.** The L3 fact counts allocations to zero across
  create, release and free.

## Not in scope

- Hot-reloading native (server.so). Nothing here makes a grown contract usable
  without a restart.
- The client. Its managed side still uses `SourceSharpManagedApi`. The same
  design applies once the client's areas are managed-built.
