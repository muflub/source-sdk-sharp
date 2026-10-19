using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;

using SourceSharp.Abi;
using SourceSharp.Client.Interface;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Vgui;

/// <summary>Reload the UI whenever a deploy changes only the UI's assemblies (plan_client_ui U5).</summary>
[ConVar("cs_ui_autoreload", "1", Help = "Reload the UI's own assemblies when a deploy changes only them (0 = never)")]
public sealed class UiAutoReloadConVar : BoolConVar
{
}

/// <summary>Swaps the UI's assemblies now, carrying its state: cs_ui_reload.</summary>
[ConCommand("cs_ui_reload", Help = "Reload the UI's assemblies (toolkit, screens, host) from disk, keeping open screens")]
public sealed class UiReloadCommand(UiLoader ui, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        var r = ui.Reload("command");
        log.Msg(r is null
            ? "[SourceSharp/Ui] reload: no UI to reload\n"
            : $"[SourceSharp/Ui] reload {r.Sequence}: {r.Milliseconds:0.0} ms, {r.CarriedBytes} bytes carried, " +
              $"{r.Reopened} screen(s) reopened; the old context is judged next frame\n");
    }
}

/// <summary>
/// THE UI'S OWN LOAD CONTEXT (plan_client_ui U5). The toolkit, the screens and
/// the UI host load here, collectibly, from bytes (the files stay unlocked);
/// everything else they name (the core, the contracts, the ABI) is the core's
/// own copy, asked of the context the core was loaded in. The core names only
/// <see cref="IUiRuntime"/>, so swapping this context swaps the whole UI and
/// nothing else: carry, dispose, unload, load, restore.
///
/// <para>
/// A watcher, polled from the HUD's paint (no thread), classifies a deploy:
/// when only these assemblies changed (by hash, once their files have stopped
/// changing) it is UI-only and, with cs_ui_autoreload, reloads here; when
/// anything else changed it needs the full client reload, which it reports.
/// </para>
/// </summary>
public sealed class UiLoader : IUiClientEvents, IDisposable
{
    /// <summary>
    /// The core's UI assemblies, in load order: what this context holds besides
    /// the mod's own, and what a UI-only deploy may change. A mod names its own
    /// in mods.yaml (<c>assemblies.client_ui</c>, stage 0.9) and they join here
    /// per loader, so one collectible swap and one watcher cover them all.
    /// </summary>
    public static readonly IReadOnlyList<string> Assemblies =
        ["SourceSharp.Client.Ui.Toolkit", "SourceSharp.Client.Ui", "SourceSharp.Client.Ui.Host"];

    /// <summary>How often the watcher looks at the files, in the clock's seconds.</summary>
    public const double PollSeconds = 0.5;

    /// <summary>The most collections a reload runs while waiting for the old context to go.</summary>
    public const int MaxCollections = 10;

    private readonly ILog _log;
    private readonly IGameClock _clock;
    private readonly string _managedDir;
    private readonly Func<UiRuntimeServices> _services;
    private readonly UiAutoReloadConVar? _auto;
    private readonly AssemblyLoadContext _parent;

    private UiLoadContext? _context;
    private Dictionary<string, (DateTime Written, long Length)> _seen = [];
    private Dictionary<string, (DateTime Written, long Length)>? _changing;
    private double _polledAt = double.NegativeInfinity;
    private DebugClientUiReload? _last;
    private int _loads;
    private string _deploy = "none";
    private IReadOnlyList<string> _deployChanged = [];

    private readonly IReadOnlyList<string> _uiAssemblies;

    public UiLoader(ILog log, IGameClock clock, string managedDir, Func<UiRuntimeServices> services,
                    UiAutoReloadConVar? auto = null, AssemblyLoadContext? parent = null,
                    IEnumerable<string>? modAssemblies = null)
    {
        (_log, _clock, _managedDir, _services, _auto) = (log, clock, managedDir, services, auto);
        _parent = parent ?? AssemblyLoadContext.GetLoadContext(typeof(UiLoader).Assembly) ?? AssemblyLoadContext.Default;
        _uiAssemblies = [.. Assemblies, .. (modAssemblies ?? []).Where(n => !Assemblies.Contains(n))];
    }

    /// <summary>The assemblies this loader holds: the core's three, then the mod's, in load order.</summary>
    public IReadOnlyList<string> HeldAssemblies => _uiAssemblies;

    /// <summary>The UI now; null before <see cref="Load"/> or when it failed.</summary>
    public IUiRuntime? Current { get; private set; }

    /// <summary>UI-only reloads made.</summary>
    public int Sequence { get; private set; }

    /// <summary>The current context's name, for the facts: one per load.</summary>
    public string? ContextName => _context?.Name;

    /// <summary>The first load. False, with the reason logged, when the UI's assemblies are not there.</summary>
    public bool Load()
    {
        _seen = Stamp();
        var runtime = Start(out string? why);
        if (runtime is null)
        {
            _log.Warning($"[SourceSharp/Ui] the UI did not load: {why}\n");
            return false;
        }
        Current = runtime;
        return true;
    }

    /// <summary>
    /// Swaps the UI's context, carrying its state; null when there is no UI to
    /// swap. A new build that fails to load leaves the old one running.
    /// </summary>
    public DebugClientUiReload? Reload(string trigger)
    {
        if (Current is null || _context is null)
            return null;

        long start = Stopwatch.GetTimestamp();
        var (weak, carriedBytes, reopened, why) = Swap();
        if (weak is null)
        {
            _log.Warning($"[SourceSharp/Ui] reload refused, the old UI stays: {why}\n");
            return null;
        }
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        Sequence++;
        _seen = Stamp();
        _retiring = weak;
        _last = new DebugClientUiReload("ui-only", Sequence, trigger, ms, carriedBytes, reopened,
                                        "pending", 0, 0, _deploy, _deployChanged);
        return _last;
    }

    private WeakReference? _retiring;

    /// <summary>
    /// THE LEAK ARM: once the reload's caller has returned (the next poll, or a
    /// fact), full collections until the old context is gone, at most
    /// <see cref="MaxCollections"/>. Not inside <see cref="Reload"/>, whose
    /// callers' frames are still on the stack. Answers "collected", "alive",
    /// or "none" when no reload is waiting to be judged.
    /// </summary>
    public string SettleOldContext()
    {
        if (_retiring is not { } weak || _last is null)
            return "none";
        long start = Stopwatch.GetTimestamp();
        int collections = 0;
        while (weak.IsAlive && collections < MaxCollections)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            collections++;
        }
        string verdict = weak.IsAlive ? "alive" : "collected";
        _last = _last with
        {
            OldContext = verdict, Collections = collections,
            CollectMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
        };
        _retiring = null;
        if (verdict == "alive")
            _log.Warning("[SourceSharp/Ui] the old UI context is still alive after the reload: something holds it\n");
        else
            _log.Msg($"[SourceSharp/Ui] reload {_last.Sequence}: old context collected after {collections} collection(s), " +
                     $"{_last.CollectMilliseconds:0.0} ms\n");
        return verdict;
    }

    // The swap in a frame of its own: when it returns, nothing on the caller's
    // stack holds the old runtime or its context (a debug build keeps every
    // local of a method alive to its end), so the collections can see it go.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private (WeakReference? Old, int CarriedBytes, int Reopened, string? Why) Swap()
    {
        byte[] carried = Current!.Carry();
        var oldRuntime = Current;
        var oldContext = _context!;
        var runtime = Start(out string? why);
        if (runtime is null)
            return (null, 0, 0, why);

        oldRuntime.Dispose();
        Current = runtime;
        int reopened = runtime.Restore(carried);
        oldContext.Unload();
        return (new WeakReference(oldContext, trackResurrection: false), carried.Length, reopened, null);
    }

    /// <summary>The reload route's UI half; null before any reload or classified deploy.</summary>
    public DebugClientUiReload? Report()
        => _last is { } l ? l with { Deploy = _deploy, DeployChanged = _deployChanged }
         : _deploy == "none" ? null
         : new DebugClientUiReload("none", 0, "", 0, 0, 0, "unknown", 0, 0, _deploy, _deployChanged);

    // A new context with the UI's assemblies, and the runtime the host's factory builds in it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private IUiRuntime? Start(out string? why)
    {
        var context = new UiLoadContext($"SourceSharp.Ui#{++_loads}", _managedDir, _parent, _uiAssemblies);
        try
        {
            foreach (string name in _uiAssemblies)
                // Ask the disk first: a held name with no file makes the
                // runtime fall back to the parent and throw, and the load
                // would be refused for an exception rather than this line.
                if (!File.Exists(Path.Combine(_managedDir, name + ".dll")) || context.LoadFromAssemblyName(new AssemblyName(name)) is null)
                {
                    why = $"{name}.dll is not in {_managedDir}";
                    context.Unload();
                    return null;
                }

            var host = context.LoadFromAssemblyName(new AssemblyName(Assemblies[^1]));
            var factories = host.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(IUiRuntimeFactory).IsAssignableFrom(t)).ToArray();
            if (factories.Length != 1)
            {
                why = $"{host.GetName().Name} has {factories.Length} IUiRuntimeFactory types, not one";
                context.Unload();
                return null;
            }

            var runtime = ((IUiRuntimeFactory)Activator.CreateInstance(factories[0])!).Create(_services());
            _context = context;
            why = null;
            return runtime;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            why = $"{e.GetType().Name}: {e.Message}";
            context.Unload();
            return null;
        }
    }

    // ---- the watcher ----

    private Dictionary<string, (DateTime, long)> Stamp()
    {
        var stamps = new Dictionary<string, (DateTime, long)>(StringComparer.Ordinal);
        if (!Directory.Exists(_managedDir))
            return stamps;
        foreach (string path in Directory.EnumerateFiles(_managedDir, "*.dll"))
        {
            var info = new FileInfo(path);
            stamps[Path.GetFileNameWithoutExtension(path)] = (info.LastWriteTimeUtc, info.Length);
        }
        return stamps;
    }

    /// <summary>
    /// One look at the managed directory, at most every <see cref="PollSeconds"/>:
    /// a change is classified once the files are the same on two looks in a row.
    /// </summary>
    public void Poll()
    {
        double now = _clock.Now;
        if (_retiring is not null)
            SettleOldContext();
        if (now >= _polledAt && now - _polledAt < PollSeconds)
            return;
        _polledAt = now;
        CheckDeploy();
    }

    /// <summary>The watcher's step, unthrottled: what <see cref="Poll"/> runs. Answers the classification made, or null.</summary>
    public string? CheckDeploy()
    {
        var now = Stamp();
        if (SameStamps(now, _seen))
        {
            _changing = null;
            return null;
        }
        if (_changing is null || !SameStamps(now, _changing))
        {
            _changing = now;       // still being written: look again
            return null;
        }

        _changing = null;
        var changed = now.Keys.Union(_seen.Keys)
            .Where(k => !now.TryGetValue(k, out var a) || !_seen.TryGetValue(k, out var b) || a != b)
            .Order(StringComparer.Ordinal).ToList();
        _seen = now;

        var ui = changed.Where(_uiAssemblies.Contains).Where(n => _context?.Hashes.TryGetValue(n, out var h) != true || h != Hash(n)).ToList();
        var other = changed.Where(n => !_uiAssemblies.Contains(n)).ToList();
        if (other.Count > 0)
        {
            _deploy = "full";
            _deployChanged = other;
            _log.Msg($"[SourceSharp/Ui] deploy changed {string.Join(", ", other)}: that needs the full client reload (cs_cl_reload)\n");
            return _deploy;
        }
        if (ui.Count == 0)
            return null;       // rewritten with the same bytes

        _deploy = "ui-only";
        _deployChanged = ui;
        _log.Msg($"[SourceSharp/Ui] deploy changed {string.Join(", ", ui)}: UI only\n");
        if (_auto?.Value != false)
            Reload("deploy");
        return _deploy;
    }

    private static bool SameStamps(Dictionary<string, (DateTime, long)> a, Dictionary<string, (DateTime, long)> b)
        => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private string Hash(string name)
    {
        string path = Path.Combine(_managedDir, name + ".dll");
        return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "";
    }

    // ---- what the native UI calls, forwarded to the UI now ----

    /// <summary>cl_drawhud: off, neither layer is painted (stock hid the viewport and all in it).</summary>
    public ClDrawHudConVar? DrawHud { get; init; }

    public void Paint(int layer, int screenWidth, int screenHeight)
    {
        if (layer == (int)SourceSharp.Client.Ui.UiLayerKind.Hud)
            Poll();
        if (DrawHud is { Value: false })
            return;
        Current?.Paint(layer, screenWidth, screenHeight);
    }

    public void ScreenSizeChanged(int oldWidth, int oldHeight, int newWidth, int newHeight)
        => Current?.ScreenSizeChanged(oldWidth, oldHeight, newWidth, newHeight);

    public void Input(int kind, int x, int y, int code, int character, float delta)
        => Current?.Input(kind, x, y, code, character, delta);

    public bool ViewportPanel(string name, bool show) => Current?.ViewportPanel(name, show) ?? false;

    /// <summary>The UI and its context, at the core's shutdown: a collectible context left loaded would hold the core's.</summary>
    public void Dispose()
    {
        Current?.Dispose();
        Current = null;
        _context?.Unload();
        _context = null;
    }

    /// <summary>
    /// The UI's collectible context: the core's three assemblies and the mod's,
    /// from bytes; the rest from the core's context. The held list is the
    /// context's own, not the static: a mod assembly resolved through the
    /// parent would pin to the core's context, survive the swap, and a reload
    /// would keep the old one — the failure with no symptom.
    /// </summary>
    private sealed class UiLoadContext(string name, string dir, AssemblyLoadContext parent, IReadOnlyList<string> held)
        : AssemblyLoadContext(name, isCollectible: true)
    {
        /// <summary>Each UI assembly's hash as loaded: what tells a rewrite from a change.</summary>
        public Dictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? simple = assemblyName.Name;
            if (simple is null)
                return null;
            if (!held.Contains(simple))
                return parent.LoadFromAssemblyName(assemblyName);

            string path = Path.Combine(dir, simple + ".dll");
            if (!File.Exists(path))
                return null;
            byte[] bytes = File.ReadAllBytes(path);
            Hashes[simple] = Convert.ToHexString(SHA256.HashData(bytes));
            string pdb = Path.ChangeExtension(path, ".pdb");
            using var image = new MemoryStream(bytes);
            if (!File.Exists(pdb))
                return LoadFromStream(image);
            using var symbols = new MemoryStream(File.ReadAllBytes(pdb));
            return LoadFromStream(image, symbols);
        }
    }
}
