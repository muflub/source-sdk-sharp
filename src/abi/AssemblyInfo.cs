using System.Runtime.CompilerServices;

// The Bootstrap names no Abi type (plan_side_entries SE2): each side's own
// entry, in SourceSharp.Shared.Interop below, binds and initialises its engine.

// The test project exercises Bind/NotifyChanged directly.
[assembly: InternalsVisibleTo("SourceSharp.Shared.Tests")]
[assembly: InternalsVisibleTo("SourceSharp.Server.Tests")]
[assembly: InternalsVisibleTo("SourceSharp.Client.Tests")]

// The oracle tests build a managed entity registry the way SourceSharp.Tests
// does (TestServices, compiled into both), which binds every engine contract
// through EngineServices.Bindings().
[assembly: InternalsVisibleTo("SourceSharp.OracleTests")]

// TF2.Descent's tests load both sides' copies of the mod's shared source in one
// process, so a protocol-order fact registers one side's declarations through
// the registries' type-list entry points (plan_side_isolation §2.4, until the
// test split puts each side in its own project).
[assembly: InternalsVisibleTo("TF2.Descent.Tests")]

// plan_abi_hotreload D4 / docs/todo.md router retirement: the generated area
// classes stay in the ABI as plumbing, and the engine surface that calls
// through them (src/shared/interop) reaches them through this. Since
// plan_shim_areas_split phase 3 that source is compiled into each side's
// interop, granted below; this name is the same source compiled alone, which
// SideAuditTests holds to the slots both sides fill.
[assembly: InternalsVisibleTo("SourceSharp.SharedInteropSource")]

// plan_abi_hotreload Phase 1: the server's engine-interface wrappers
// (EngineNetworkStringTables, PhysicsSurfaceProps, SoundEmitterBase) moved to
// Server.Interop and read the internal generated vtable layouts, and
// PlayerCommandQueue takes the internal EngineSide.
[assembly: InternalsVisibleTo("SourceSharp.Server.Interop")]

// Lane side-audit-2 (2026-10-02): EngineClient moved from Shared.Interop to
// Client.Interop, because IVEngineClient exists only on the client, and reads
// its internal generated forwarder struct and the side's proxy api.
[assembly: InternalsVisibleTo("SourceSharp.Client.Interop")]

// plan_abi_hotreload Phase 1, run 2: RecordingEngine moved to the test-only
// SourceSharp.TestSupport (the user's ruling) and implements the internal
// IEngineStringTables, which it reached as a file of this assembly. Since
// plan_shim_areas_split phase 3 the doubles are built once per side, over that
// side's copy of the engine areas.
[assembly: InternalsVisibleTo("SourceSharp.TestSupport.Server")]
[assembly: InternalsVisibleTo("SourceSharp.TestSupport.Client")]

// plan_abi_hotreload Phase 1, run 3: the server entity cluster (ScriptedEntity
// and its registry, EntityDirectory, ThinkScheduler, EntityIoQueue; plan M3)
// moved to Server and keeps the internal seams it used inside Abi
// (EventAction.ParseFloat, ScriptedVarSet's owner wiring, NameMatches, the
// reload caches). It reaches no EngineSide: ServerEntityEngine (Server.Interop)
// stands between, and SideAuditTests' metadata fact holds that.
[assembly: InternalsVisibleTo("SourceSharp.Server")]
