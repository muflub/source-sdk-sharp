using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using SourceSharp.Mods;

namespace SourceSharp.Bootstrap;

/// <summary>
/// The Bootstrap's half of each side's load, reload check and unload, reached
/// from <see cref="ServerEntry"/> and <see cref="ClientEntry"/>. Lives in the
/// Bootstrap's own (non-collectible) context and names no SourceSharp.Abi type
/// (plan_side_entries SE2).
///
/// <para>
/// It makes the side's collectible load context, loads the mod's listed
/// assemblies into it, resolves the side's own entry there by its fixed name
/// (fresh on every load: a function pointer into a collectible context dangles
/// once it unloads) and calls it with raw arguments only. Everything past "make
/// the context and load the assemblies" is the side's: see
/// <c>SourceSharp.Shared.Interop.SideLoad</c>.
/// </para>
/// </summary>
public static unsafe class Entry
{
    private static string s_ManagedDir = string.Empty;

    //-------------------------------------------------------------------------
    // Native-facing entry points
    //-------------------------------------------------------------------------

    /// <summary>
    /// A side's load (plan_side_entries SE1/SE2). <paramref name="pShimRoot"/>
    /// is the one native object every shim interface is acquired from
    /// (plan_engineapi_vtables A3), checked by the side's entry;
    /// <paramref name="ppManagedRoot"/> receives the module's managed root (D4).
    /// </summary>
    internal static int LoadSide(BootSide side, void* pShimRoot, int shimRootEntries, void** ppManagedRoot)
    {
        if (ppManagedRoot != null)
            *ppManagedRoot = null;

        // The Bootstrap's own console for this side, raw over the root: its own
        // lines go through it, and so do its unload's ("did NOT unload", PIN).
        BootConsole console = side.Console = BootConsole.Acquire(pShimRoot, shimRootEntries);

        try
        {
            WaitForDebugger(console);

            if (s_ManagedDir.Length == 0)
            {
                s_ManagedDir = Path.GetDirectoryName(typeof(Entry).Assembly.Location) is { Length: > 0 } d
                    ? d
                    : AppContext.BaseDirectory;
            }

            // The side's root, made by its first load and kept for the process
            // (plan_side_isolation §2.3): its own copy of the Abi, hashed as it
            // loaded, before the module, so a load that then fails has still
            // stamped the Abi it ran against.
            SideRoot root = side.Root ??= new SideRoot(side.RootName, s_ManagedDir);
            WarnIfTheAbiMoved(console, side, root);

            // Which assemblies make up this side comes from the mod's mods.yaml
            // entry, not from here. A missing or invalid file fails the load with
            // the parser's own message; there is no fallback to fixed names, which
            // would load the wrong game without a word.
            IReadOnlyList<string> names;
            try
            {
                names = ModAssemblies.For(side.Server, s_ManagedDir);
            }
            catch (ModsConfigException e)
            {
                console.Warning($"[SourceSharp] {e.Message}\n");
                return -3;
            }

            Mod mod = ModAssemblies.Current!;
            if (names.Count == 0)
            {
                console.Warning($"[SourceSharp] mods.yaml lists no {side.Name} assemblies for mod '{ModAssemblies.ModId}'\n");
                return -3;
            }

            console.Msg($"[SourceSharp/{side.Name}] mod '{ModAssemblies.ModId}': loading {string.Join(", ", names)}\n");

            var context = new GameLoadContext(side.ContextName, s_ManagedDir, root);
            foreach (string name in names)
            {
                if (context.LoadFromManagedDir(name) is null)
                {
                    console.Warning($"[SourceSharp] missing {name}.dll in {s_ManagedDir}\n");
                    return -3;
                }
            }

            if (SideEntryPoints.Resolve(context, side.EntryAssembly, side.EntryType, side.Name, out string why) is not { } entry)
            {
                console.Warning($"[SourceSharp] {side.Name}: {why}\n");
                return -3;
            }

            byte[] facts = LoadFacts.WriteLoad(s_ManagedDir, mod.Id, mod.GameDir,
                                               mod.AssembliesFor(server: true), mod.AssembliesFor(server: false),
                                               mod.Browser);

            nint vtables = side.Vtables;
            int rc = LoadAndStamp(entry, context, root.AbiFile, facts,
                                  pShimRoot, shimRootEntries, ppManagedRoot, ref vtables, out nint session);
            side.Vtables = vtables;

            // A session means the side has something to unload, whatever the
            // load went on to answer.
            if (session != 0)
            {
                side.Session = session;
                side.Context = context;
                side.Entry = entry;
            }

            return rc;
        }
        catch (Exception e)
        {
            Fail(console, "LoadSide", e);
            return -99;
        }
    }

    //-------------------------------------------------------------------------
    // The build stamp: which build this game is running.
    //
    // Two halves, taken in two different places and for a reason. The native
    // half is stamped ONCE, by the side's own entry, because native cannot be
    // replaced in a running process; the managed half is stamped on every load,
    // by the side's Stamped from what this context read, because cs_sv_reload
    // replaces it without restarting anything. The Abi's own row is its side
    // root's, hashed once as the root loaded it (SideRoot.AbiFile).
    //-------------------------------------------------------------------------

    /// <summary>
    /// The side's Load, then its Stamped with every assembly
    /// <paramref name="context"/> has read BY THEN (the integrator's ruling A,
    /// 2026-10-02): most of a side's assemblies load lazily while its module is
    /// created and initialised, so a list written before Load would stamp three
    /// of the server's seven. <paramref name="abi"/> joins them: the context
    /// never reads the Abi, which is its side root's.
    /// </summary>
    internal static int LoadAndStamp(ISideEntry entry, GameLoadContext context, LoadedFile? abi, byte[] loadFacts,
                                     void* pShimRoot, int shimRootEntries, void** ppManagedRoot,
                                     ref nint vtables, out nint session)
    {
        nint vt = vtables, s = 0;
        int rc;
        fixed (byte* pFacts = loadFacts)
            rc = entry.Load(pShimRoot, shimRootEntries, ppManagedRoot, &vt, &s, pFacts);
        vtables = vt;
        session = s;

        if (rc != 0)
            return rc;

        IEnumerable<LoadedFile> loaded = context.Loaded;
        if (abi is { } a)
            loaded = loaded.Append(a);

        byte[] stamped = LoadFacts.WriteStamped(loaded.ToList());
        fixed (byte* pStamped = stamped)
            return entry.Stamped(session, pStamped);
    }

    /// <summary>
    /// The side's reload check, on the OLD build (plan_managed_vtable_reload.md
    /// L5). 0 when no side is loaded: native holds nothing of it.
    /// </summary>
    internal static int ReloadCheck(BootSide side, byte* pWhy, int nWhy)
    {
        if (side.Session == 0 || side.Entry is not { } entry)
            return 0;

        return entry.ReloadCheck(side.Session, pWhy, nWhy);
    }

    internal static int UnloadSide(BootSide side)
    {
        try
        {
            UnloadModule(side);
            return 0;
        }
        catch (Exception e)
        {
            Fail(side.Console, "UnloadSide", e);
            return -99;
        }
    }

    //-------------------------------------------------------------------------
    // Module lifetime
    //-------------------------------------------------------------------------

    /// <summary>How long a debug run will sit waiting for a debugger to arrive.</summary>
    private static readonly TimeSpan DebuggerWait = TimeSpan.FromSeconds(30);

    private static bool s_Waited;

    /// <summary>
    /// Holds the game still until a debugger is attached, when one is expected.
    ///
    /// Everything worth breaking on at startup — the convar and command
    /// registrations, the entity classes, the module's own Init — is over within
    /// a second of the game launching, and a debugger cannot attach until the
    /// runtime exists. That leaves a window measured in milliseconds to win a
    /// race in, which is no way to debug anything.
    ///
    /// So the game waits instead: by this point the runtime is up, so an editor
    /// can attach, and nothing managed has run yet, so nothing has been missed.
    /// Only when SOURCESHARP_DEBUG says a debugger is coming, and only once —
    /// LoadSide runs twice on a listen server, and waiting twice would ask for
    /// the debugger again after it had already arrived.
    ///
    /// It gives up rather than hanging: an unattended run that was left with the
    /// variable set should start late, not never.
    /// </summary>
    private static void WaitForDebugger(BootConsole console)
    {
        if (s_Waited) return;
        s_Waited = true;

        string? wanted = Environment.GetEnvironmentVariable("SOURCESHARP_DEBUG");

        if (string.IsNullOrEmpty(wanted) || wanted == "0" || Debugger.IsAttached)
            return;

        console.Msg($"[SourceSharp] waiting for a debugger (pid {Environment.ProcessId})\n");

        var until = DateTime.UtcNow + DebuggerWait;
        int reported = 0;

        while (!Debugger.IsAttached && DateTime.UtcNow < until)
        {
            Thread.Sleep(100);

            // A countdown rather than silence: a frozen window with nothing in
            // the console is indistinguishable from a hang, and this one lasts
            // half a minute.
            int left = (int)(until - DateTime.UtcNow).TotalSeconds;

            if (left != reported)
            {
                reported = left;
                console.Msg($"[SourceSharp] waiting for a debugger, {left}s left\n");
            }
        }

        console.Msg(Debugger.IsAttached
            ? "[SourceSharp] debugger attached; carrying on\n"
            : "[SourceSharp] no debugger arrived; carrying on without one\n");
    }

    /// <summary>
    /// Says so when the Abi on disk is not the Abi the side's root loaded: a
    /// hot reload does not reload it (see <see cref="SideRoot"/>), so the
    /// answer is a message rather than a fix. Once per rebuild per side.
    /// </summary>
    private static void WarnIfTheAbiMoved(BootConsole console, BootSide side, SideRoot root)
    {
        if (!root.AbiMoved())
            return;

        console.Warning(
            $"[SourceSharp] SourceSharp.Abi.dll has been rebuilt since the {side.Name.ToLowerInvariant()} first loaded, " +
            "and a hot reload does NOT reload it — each side keeps the copy its root loaded for the process, so " +
            "that the side's managed-built objects and their registry stay valid across reloads. Everything else " +
            "reloaded; nothing in the ABI did. RESTART THE GAME to pick those changes up.\n");
    }

    /// <summary>
    /// Unloads a side and verifies the context actually died. A collectible
    /// context only unloads when nothing references it; silent failure here is
    /// the number-one hot-reload bug, so it is checked every time rather than
    /// assumed — plan.md Part 5, rule 3.
    /// </summary>
    private static void UnloadModule(BootSide side)
    {
        var weak = UnloadCore(side);
        if (weak is null)
            return;

        // Collect OUTSIDE the frame that held the context reference. If this ran
        // in UnloadCore, the local would still be live and the check would
        // report a false leak — which is exactly what the harness caught first
        // time.
        for (int i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (weak.IsAlive)
        {
            side.Console.Warning(
                $"[SourceSharp] {side.ContextName} did NOT unload — something still references it. " +
                "Reloading will leak. Check for native-cached managed pointers.\n");

            // NAME the pins rather than advise a search. Requested verbatim by
            // lane `loose-ends` (requests/loose-ends.md §1) and taken by lane
            // `defect-alc`; runs ONLY on the leak path, so a healthy unload
            // pays nothing. An EMPTY report is NOT proof of no pin — this walks
            // static fields, not GC roots, and ManagedPins' own header says so.
            if (weak.Target is AssemblyLoadContext going)
            {
                foreach (var pin in ManagedPins.Find(going))
                    side.Console.Warning($"[SourceSharp] PIN {pin}\n");
            }
        }
    }

    /// <summary>
    /// The side's own unload (module Shutdown, registry settled, the ABI's
    /// caches emptied, session freed), then the context's. Must not be inlined:
    /// the caller relies on this frame being gone before it collects.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference? UnloadCore(BootSide side)
    {
        nint session = side.Session;
        GameLoadContext? context = side.Context;
        ISideEntry? entry = side.Entry;
        side.Session = 0;
        side.Context = null;
        side.Entry = null;

        if (session == 0 || entry is null)
            return null;

        int rc = entry.Unload(session);
        if (rc != 0)
            side.Console.Warning($"[SourceSharp] {side.Name}: the side's unload returned {rc}\n");

        if (context is null)
            return null;

        var weak = new WeakReference(context, trackResurrection: true);
        context.Unload();
        return weak;
    }

    private static void Fail(BootConsole console, string where, Exception e)
    {
        console.Warning($"[SourceSharp] ===== EXCEPTION IN {where} =====\n");
        console.Warning(e + "\n");
    }
}
