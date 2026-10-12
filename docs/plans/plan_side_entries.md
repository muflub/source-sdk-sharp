# plan_side_entries: one entry per side, no `int side` across the boundary

The user (2026-10-01): "split [LoadSideV2(int side, ...)] into a method for client and a method
for server and then home them in the right server & client modules ... basically i dont want to see
`int side` being passed around". Shaped by source-sdk-sharp-48's P5 (plan_side_isolation.md §2.3,
user-approved), which builds on this: per-side root contexts, each with its own SourceSharp.Abi.

## Where it stands (ad7914d)

- Native exports `SourceSharp_LoadSideV2(SourceSharpSide side, ...)`, `SourceSharp_UnloadSide(side)` and
  `SourceSharp_ReloadCheck(side, ...)`. The host resolves three Bootstrap entries (`Entry.LoadSideV2`,
  `Entry.UnloadSide` and `Entry.ReloadCheck`), each taking `int side`.
- `Entry.cs` (917 lines) holds both sides' state in pairs (`s_Server`/`s_Client`, the vtables, ALCs and
  shim roots) and switches on `side` throughout. It owns each side's `ManagedVtables` registry, binds
  the shim root, initialises `Engine`, builds the managed root, counts crossings and sets the active
  side.

## Constraint

The LOAD entry stays in the Bootstrap. Native resolves it before any side assembly exists, and the
side assemblies are collectible: a function pointer into one dangles after a reload. Everything past
"make the context and load the assemblies" belongs to the side.

## Steps (each one commit, make check; (live) = live gate + headless cs_cl_reload/cs_sv_reload)

- **SE1. Per-side exports and entries (live).** Native exports `SourceSharp_LoadServer`/`LoadClient`,
  `UnloadServer`/`UnloadClient` and `ReloadCheckServer`/`ReloadCheckClient`, with no `SourceSharpSide`
  parameter. The host resolves six Bootstrap entries on two types, `ServerEntry` and `ClientEntry`.
  The shared gamesystem calls its side's export through one per-side macro. Inside the Bootstrap, the
  two types call the existing core with a constant side; nothing crosses the boundary as a number.
- **SE2. The side owns its load (live).** Each side's Interop assembly declares one fixed-name
  `[UnmanagedCallersOnly]` load, unload and reload-check entry. That entry binds the shim root,
  initialises the side's engine, creates the module and builds the managed root. ServerEntry and
  ClientEntry create the game context, load the side assembly, resolve the entry fresh on each load,
  and call it through a function pointer.
  - Raw pointers only: the side's `ManagedVtables` registry survives a reload as an opaque handle
    the Bootstrap keeps but never types.
  - The Bootstrap stops naming SourceSharp.Abi types.
- **SE3. Dispatch into the side.** What is left in `Entry.cs` that dispatches into a side moves into
  the side's Interop: `ServerOnGameRulesThink`, `Borrow(MapLoadRaw*)`, crossing counts and
  `SetActiveSide`.
- **SE-last. Facts.**
  - No boundary function takes a side parameter (native header, host delegates, Bootstrap entries).
  - The Bootstrap assembly references no SourceSharp.Abi type (metadata).
  - Entry.cs is handed to source-sdk-sharp-48.

## Not in this plan

48's P5 owns the per-side root context and the per-side Abi copy: ActiveSide/SideEntry go, and the
in-managed `Side` switches (`Engine.For(side)` and the rest) go with it. The user's Abi audit ("move
anything that is not binding plumbing, since it needs hot reload") and the static `Engine` to zero come
after this plan.
