using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

using SourceSharp.Abi;

namespace SourceSharp.Shared.Interop;

/// <summary>
/// One side's live load: what its Stamped, ReloadCheck and Unload need, held by
/// the session handle Load writes and Unload frees (plan_side_entries SE2).
/// Lives in the side's own load context, so freeing the handle is what lets
/// that context go.
/// </summary>
internal sealed class SideSession(Side side, EngineSide engine, SideFacts facts)
{
    public Side Side { get; } = side;
    public EngineSide Engine { get; } = engine;

    /// <summary>What the side keeps across its loads: the engine's own <see cref="SideState"/>.</summary>
    public SideState State => Engine.State;

    public ManagedVtables Vtables => State.Vtables;
    public SideFacts Facts { get; } = facts;

    /// <summary>The side's module, once it has initialised.</summary>
    public ISideModule? Module { get; set; }

    public bool Server => Side == Side.Server;
}

/// <summary>
/// Everything a side's load does past "make the context and load the
/// assemblies" (plan_side_entries SE2), once for both sides. Each side's
/// Interop assembly declares a thin fixed-name entry
/// (<c>ServerSideEntry</c>, <c>ClientSideEntry</c>) whose
/// [UnmanagedCallersOnly] methods count their crossing and call these with
/// their side as a constant. The Bootstrap resolves those entries fresh on
/// every load and calls them with raw arguments only.
///
/// <para>
/// Return codes are the ones native has always been given: -4 the shim root
/// was refused, -5 its console, -6 its areas, -1 a throw while binding them,
/// -3 no module, -99 a throw while loading, -7 the managed root.
/// </para>
/// </summary>
public static unsafe class SideLoad
{
    /// <summary>
    /// The side's load. <paramref name="pVtables"/> is the side's
    /// <see cref="SideState"/> (its managed-built registry, crossing counters
    /// and the rest) as an opaque GCHandle the Bootstrap keeps across reloads:
    /// 0 on the first load, when this creates it. <paramref name="pSession"/>
    /// receives the session handle as soon as there is a session, so a load
    /// that fails later still unloads. <paramref name="op"/> is the entry's
    /// own crossing, counted on that state before anything else.
    /// </summary>
    public static int Load(Side side, CrossingOp op, void* pShimRoot, void* pShimRootTable, void** ppManagedRoot,
                           nint* pVtables, nint* pSession, byte* pFacts)
        => Load(side, op, pShimRoot, pShimRootTable, ppManagedRoot, pVtables, pSession, pFacts, bindSide: null);

    /// <summary>
    /// The same, with <paramref name="bindSide"/> run on the side's engine as
    /// soon as it exists: what only one side's assembly can bind (the server's
    /// IVEngineServer, whose wrapper is Server.Interop's).
    /// </summary>
    internal static int Load(Side side, CrossingOp op, void* pShimRoot, void* pShimRootTable, void** ppManagedRoot,
                             nint* pVtables, nint* pSession, byte* pFacts, Action<EngineSide>? bindSide)
    {
        // statics to zero: the side's state, which outlives every load, held by
        // the Bootstrap as the one opaque handle it has always kept for the
        // side. Created on the side's first load and never replaced.
        SideState state;
        try
        {
            if (*pVtables != 0)
            {
                state = (SideState)GCHandle.FromIntPtr(*pVtables).Target!;
            }
            else
            {
                state = new SideState(side);
                *pVtables = GCHandle.ToIntPtr(GCHandle.Alloc(state));
            }

            state.Crossings.Count(op);
        }
        catch (Exception e)
        {
            Fail(null, "LoadSideV2 (side state)", e);
            return -1;
        }

        // The side's engine API, built for this load and held by its session
        // (plan_abi_hotreload Phase 1: the static Engine that held one per
        // side for the process is gone). Everything below binds its shims,
        // initialises it, tells it the managed directory and gives it to the
        // module, so the module is composed over this instance. Nothing of it
        // outlives the load: its routing is re-attached, its globals re-asked
        // and its caches re-filled by the next one.
        EngineSide engine = new(side, state);
        SideSession session;

        try
        {
            // What only one side's assembly can bind (the server's IVEngineServer,
            // whose wrapper is Server.Interop's): handed in by that side's entry,
            // before anything below can ask for it.
            bindSide?.Invoke(engine);

            if (ppManagedRoot != null)
                *ppManagedRoot = null;

            SideFacts facts = SideFacts.ReadLoad(pFacts);

            // plan_managed_vtable_reload.md L3: the side's registry of
            // managed-built blocks is the state's, so it outlives every load
            // context the side's module comes and goes in.
            session = new SideSession(side, engine, facts);
            *pSession = GCHandle.ToIntPtr(GCHandle.Alloc(session));

            if (!ShimRoot.TryBind(pShimRoot, pShimRootTable, out ShimRoot? root, out string why))
            {
                Trace(state.Log, $"LoadSideV2({side}) refused: {why}");
                return -4;
            }

            // The first area through the root (step A4): acquired and count-
            // checked on every load, and one line written through its vtable,
            // so console.log shows the route working end to end.
            if (!ShimConsole.TryAcquire(root!, out ShimConsole? console, out why))
            {
                Trace(state.Log, $"LoadSideV2({side}) refused: {why}");
                return -5;
            }

            // Log's sink for this side until the module composes, and again once
            // it has shut down (plan_abi_hotreload D5). Never detached: the next
            // load's console replaces it. This side's Log: the Abi is the side
            // root's own copy (plan_side_isolation P5).
            state.Log = console!;

            // Phase C: every generated area, acquired and count-checked on every
            // load, attached to this side's engine API (docs/engineapi-switched.txt).
            // The client attaches its shared bases instead (plan_client_vtables
            // CB; docs/engineapi-client-switched.txt).
            bool attached = session.Server
                ? engine.AttachShims(root!, out why)
                : engine.AttachClientShims(root!, out why);
            if (!attached)
            {
                Trace(state.Log, $"LoadSideV2({side}) refused: {why}");
                return -6;
            }

            console!.Msg($"[SourceSharp] LoadSideV2({side}): shim root bound "
                + $"({ShimRoot.ExpectedEntries} slots), {ShimConsole.InterfaceVersion} acquired "
                + $"through it ({ShimConsole.ExpectedEntries} slots), "
                + $"{(session.Server ? ShimAreas.Count : ClientShimAreas.Count)} {(session.Server ? "" : "client ")}areas attached\n");
        }
        catch (Exception e)
        {
            Fail(state.Log, "LoadSideV2", e);
            return -1;
        }

        int rc = LoadModule(session);
        if (rc != 0)
            return rc;

        // plan_engineapi_vtables D4: the managed root, built by the module now
        // that it is loaded, in the side's registry. Native accepts it through
        // its Validate.
        try
        {
            ManagedVtables vtables = session.Vtables;
            nint managedRoot = session.Module?.BuildManagedRoot(vtables) ?? 0;

            // L4: every block native held across a reload must be patched by
            // this build, or native would call a trap.
            if (vtables.Detached > 0)
            {
                state.Log?.Warning($"[SourceSharp] the reloaded build did not rebuild {vtables.Detached} managed-built " +
                            $"object(s) native holds: {string.Join(", ", vtables.DetachedContracts)}\n");
                return -7;
            }

            if (ppManagedRoot != null)
                *ppManagedRoot = (void*)managedRoot;
        }
        catch (Exception e)
        {
            Fail(state.Log, "LoadSideV2 (managed root)", e);
            return -7;
        }

        return 0;
    }

    private static int LoadModule(SideSession session)
    {
        try
        {
            Side side = session.Side;
            EngineSide engine = session.Engine;
            SideFacts facts = session.Facts;

            // Both sides arrive with their shim root bound (its ABI version
            // checked as it was bound). plan_client_vtables CC-last: there is no
            // engine struct; every slot is a shim-area method, routed once the
            // side's areas attach.
            engine.Initialize();

            // Before anything else: process-wide handlers catch faults the
            // per-call try/catch cannot (other threads, unobserved Tasks).
            ManagedDiagnostics.Install(session.State);
            ManagedDiagnostics.ReportRuntimeInfo(session.State.Log!);

            // Published for the modules, which cannot work it out themselves:
            // they are loaded from memory and so have no Location to ask.
            engine.ManagedDirectory = facts.ManagedDir;

            Trace(session.State.Log, $"LoadSide({side})");

            // The native stamp before the module, so a load that then fails has
            // still said which build failed. The managed one is Stamped's, once
            // the module's start-up has pulled its assemblies in.
            StampNativeOnce(side, session.State.Build);

            // What IModInfo reads in the game's load context (plan_host_sdk §2.8):
            // AppContext data is process-wide, so it crosses the context boundary.
            ModInfo.Publish(facts.ModId, facts.GameDir, facts.ManagedDir,
                            facts.ServerAssemblies, facts.ClientAssemblies, facts.BrowserJson,
                            facts.ModClientUiAssemblies, facts.ModLocalisation);

            if (CreateModule(session) is not { } module)
                return -3;

            module.Initialize(side);
            session.Module = module;

            // NEITHER SIDE IS HANDED A STRUCT: every call into managed code is a
            // managed-built area acquired through the managed root.
            return 0;
        }
        catch (Exception e)
        {
            Fail(session.State.Log, "LoadSide", e);
            return -99;
        }
    }

    /// <summary>
    /// The one <see cref="ISideModule"/> across the side's listed assemblies,
    /// which the Bootstrap has already loaded into this context, composed over
    /// the side's engine.
    /// </summary>
    private static ISideModule? CreateModule(SideSession session)
    {
        IReadOnlyList<string> names = session.Facts.AssembliesFor(session.Server);
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(typeof(SideLoad).Assembly)
                                      ?? AssemblyLoadContext.Default;

        var modules = new List<Type>();
        foreach (string name in names)
        {
            var assembly = context.Assemblies.FirstOrDefault(a => a.GetName().Name == name);
            if (assembly is null)
            {
                session.State.Log?.Warning($"[SourceSharp] {session.Side}: {name} is listed but not loaded in {context.Name}\n");
                return null;
            }

            modules.AddRange(assembly.GetTypes().Where(
                t => typeof(ISideModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface));
        }

        // Exactly one entry point across the listed assemblies: none means the
        // side has nothing to run, two means which one runs is an accident.
        if (modules.Count != 1)
        {
            string found = modules.Count == 0 ? "none" : string.Join(", ", modules.Select(t => t.FullName));
            session.State.Log?.Warning($"[SourceSharp] {session.Side}: need exactly one ISideModule implementation across " +
                        $"{string.Join(", ", names)}; found {found}\n");
            return null;
        }

        return (ISideModule)Activator.CreateInstance(modules[0], new ModuleEngine(session.Engine))!;
    }

    /// <summary>
    /// The managed half of the build stamp (the integrator's ruling A): every
    /// assembly the context read by the time the module had initialised, as
    /// the Bootstrap hashed it going past, SourceSharp.Abi's first-load hash
    /// among them. Printed on every load, so a reload's own line is the proof
    /// the stamp moved with it.
    /// </summary>
    public static int Stamped(CrossingOp op, nint sessionHandle, byte* pFacts)
    {
        try
        {
            var session = Session(sessionHandle);
            session.State.Crossings.Count(op);
            IReadOnlyList<LoadedAssembly> loaded = SideFacts.ReadLoaded(pFacts);
            string managedDir = session.Facts.ManagedDir;

            var stamp = new BuildStampBuilder();
            foreach (LoadedAssembly assembly in loaded)
                stamp.Add(assembly.Name, assembly.Hash, assembly.WrittenUtc);

            var source = GitProvenance.Read(Path.Combine(managedDir, GitProvenance.FileName));

            // The file list as well as the stamp: freshness is checked against
            // the assemblies THIS side loaded, not against everything in the
            // directory.
            BuildInfo build = session.State.Build;
            build.SetManaged(new BuildHalf(stamp.Build(), source, managedDir, false), stamp.Names);

            LogOf(sessionHandle)?.Msg($"[SourceSharp/{session.Side}] build: native  {build.Native.Describe()}\n");
            LogOf(sessionHandle)?.Msg($"[SourceSharp/{session.Side}] build: managed {build.Managed.Describe()} " +
                    $"({stamp.Count} assemblies)\n");
            return 0;
        }
        catch (Exception e)
        {
            Fail(LogOf(sessionHandle), "Stamped", e);
            return -99;
        }
    }

    /// <summary>
    /// Stamps the .so this side is running, from the mapping the kernel reports
    /// rather than from a path worked out by convention. Once: native cannot be
    /// replaced in a running process.
    /// </summary>
    private static void StampNativeOnce(Side side, BuildInfo build)
    {
        if (build.Native.Stamp.IsKnown)
            return;

        string library = side == Side.Server ? "server.so" : "client.so";

        if (ProcMaps.Locate(library) is not { } mapped)
            return;

        var stamp = new BuildStampBuilder();
        if (!stamp.AddFile(mapped.Path))
            return;

        var source = GitProvenance.Read(Path.Combine(
            Path.GetDirectoryName(mapped.Path) ?? string.Empty, GitProvenance.FileName));

        build.SetNativeOnce(new BuildHalf(stamp.Build(), source, mapped.Path, mapped.Replaced));
    }

    /// <summary>
    /// plan_managed_vtable_reload.md L5: called by native's reload BEFORE it
    /// saves or unloads anything, on the OLD build. 0 when the build on disk
    /// keeps every slot hash of every managed-built contract native holds (or
    /// native holds none); otherwise 1, with the first difference in
    /// <paramref name="pWhy"/>, and native keeps running the old build.
    /// </summary>
    public static int ReloadCheck(CrossingOp op, nint sessionHandle, byte* pWhy, int nWhy)
    {
        try
        {
            var session = Session(sessionHandle);
            session.State.Crossings.Count(op);
            ManagedVtables vtables = session.Vtables;
            if (vtables.Live == 0)
                return 0;

            var next = ManagedVtableHashFiles.Read(session.Facts.ManagedDir,
                                                   session.Facts.AssembliesFor(session.Server));
            if (vtables.CheckReload(next) is not { } why)
            {
                LogOf(sessionHandle)?.Msg($"[SourceSharp/{session.Side}] reload check: the new build keeps all {vtables.Live} managed-built " +
                        "contract(s) native holds\n");
                return 0;
            }

            WriteReason(why, pWhy, nWhy);
            return 1;
        }
        catch (Exception e)
        {
            Fail(LogOf(sessionHandle), "ReloadCheck", e);
            WriteReason($"the reload check failed: {e.Message}", pWhy, nWhy);
            return 1;
        }
    }

    private static void WriteReason(string why, byte* pWhy, int nWhy)
    {
        if (pWhy == null || nWhy <= 0)
            return;
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(why);
        int n = Math.Min(utf8.Length, nWhy - 1);
        utf8.AsSpan(0, n).CopyTo(new Span<byte>(pWhy, n));
        pWhy[n] = 0;
    }

    /// <summary>
    /// The side's half of its unload: the module's Shutdown, the registry
    /// settled, and the ABI's caches emptied of this context's types; then the
    /// session handle is freed. The Bootstrap unloads the context afterwards
    /// and checks that it went.
    /// </summary>
    public static int Unload(CrossingOp op, nint sessionHandle)
    {
        try
        {
            UnloadCore(op, sessionHandle);
            return 0;
        }
        catch (Exception e)
        {
            Fail(LogOf(sessionHandle), "UnloadSide", e);
            return -99;
        }
    }

    /// <summary>Not inlined: nothing of the session may stay live in the caller's frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnloadCore(CrossingOp op, nint sessionHandle)
    {
        GCHandle handle = GCHandle.FromIntPtr(sessionHandle);
        var session = (SideSession)handle.Target!;
        session.State.Crossings.Count(op);
        try
        {
            try { session.Module?.Shutdown(); }
            catch (Exception e) { Fail(session.State.Log, "module.Shutdown", e); }

            session.Module = null;

            SettleVtables(session);

            // The ABI lives in the Bootstrap's context and caches against game
            // types, so what it kept about this side would outlive the side
            // itself — and a single Type is enough to pin the whole context.
            // Dropped after Shutdown, which is still entitled to ask the ABI
            // about its own classes, and before the Bootstrap's Unload, which is
            // the point of doing it at all. Only this session's engine has
            // routes to search: each side's engine is its own load's now, and
            // a side's container binds only the engine it was composed over.
            if (AssemblyLoadContext.GetLoadContext(typeof(SideLoad).Assembly) is { } going
                && going != AssemblyLoadContext.Default)
            {
                int forgotten = ManagedCaches.Forget(going, [session.Engine]);
                session.State.Reload.NoteCachesDropped(forgotten);
                Trace(session.State.Log, $"UnloadSide({session.Side}): ABI forgot {forgotten} cached type(s)");
            }
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// plan_managed_vtable_reload.md L4: after the module's Shutdown and before
    /// its context unloads, no block may keep a handle into it. The root's
    /// state says which unload this is: native releases the root at the final
    /// unload (so any block still live is a leak, freed and reported), and
    /// keeps it across a reload (so every block is detached, to be patched by
    /// the next build).
    /// </summary>
    private static void SettleVtables(SideSession session)
    {
        ManagedVtables vtables = session.Vtables;
        try
        {
            if (vtables.Holds("IManagedRoot"))
            {
                int detached = vtables.Detach();
                session.State.Log?.Msg($"[SourceSharp/{session.Side}] {detached} managed-built object(s) detached for the reload; native keeps them\n");
                return;
            }

            int caught = vtables.FreeRemaining();
            if (caught > 0)
                session.State.Log?.Warning($"[SourceSharp/{session.Side}] {caught} managed-built object(s) were still bound at shutdown; native should release what it acquires\n");
        }
        catch (Exception e) { Fail(session.State.Log, "SettleVtables", e); }
    }

    /// <summary>
    /// The side's console (statics to zero: it was the static Log): its
    /// state's current shim console, or the process's standard error before
    /// any load has one.
    /// </summary>
    private static void Write(ILog? log, string message, bool warning)
    {
        if (log is null)
            Console.Error.Write(message);
        else if (warning)
            log.Warning(message);
        else
            log.Msg(message);
    }

    /// <summary>The console of the session behind <paramref name="handle"/>, if it is live.</summary>
    private static ILog? LogOf(nint handle)
        => handle != 0 && GCHandle.FromIntPtr(handle).Target is SideSession session ? session.State.Log : null;

    private static SideSession Session(nint handle)
        => handle != 0 && GCHandle.FromIntPtr(handle).Target is SideSession session
            ? session
            : throw new InvalidOperationException("the Bootstrap handed no live session");

    /// <summary>
    /// Traces a managed entry point. Mirrors SOURCESHARP_TRACE on the native
    /// side so both halves of the boundary appear in one console stream.
    /// </summary>
    private static void Trace(ILog? log, string where)
        => Write(log, $"[SourceSharp] > managed {where}\n", warning: false);

    private static void Fail(ILog? log, string where, Exception e)
    {
        Write(log, $"[SourceSharp] ===== EXCEPTION IN {where} =====\n", warning: true);
        Write(log, ManagedDiagnostics.Describe(e), warning: true);
    }
}
