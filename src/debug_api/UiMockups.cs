using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Debug.Api;

/// <summary>
/// POST /preview/live: the UI's live mock-ups (the web UI's Mockups tab), served
/// by the game as <c>tools/ui-preview --watch</c> serves them. The dev API links
/// nothing of the client, so the client's UI assemblies are loaded straight
/// from the managed directory into a collectible context of their own, on
/// the first request: the toolkit, and every <c>*.Ui</c> assembly beside it
/// (the core's screens, each game's UI). The toolkit's
/// <c>UiLive.ForGame</c> builds the answerer over them, with the mod's
/// theme and every mod's copy, and each request is handed to its
/// <c>Answer</c>. Answered on the request's own thread: it reads nothing of
/// the game, so it never costs a frame.
/// </summary>
internal sealed class UiMockups : IDisposable
{
    public const string Toolkit = "SourceSharp.Client.Ui.Toolkit";

    private readonly Func<string> _managedDirectory;
    private readonly Func<string> _modDirectory;
    private readonly Lock _lock = new();
    private Context? _context;
    private Func<string, string>? _answer;
    private string? _failure;

    /// <param name="managedDirectory">Where the managed assemblies are.</param>
    /// <param name="modDirectory">The running mod's directory: its theme, and its siblings' copy.</param>
    public UiMockups(Func<string> managedDirectory, Func<string> modDirectory)
    {
        _managedDirectory = managedDirectory;
        _modDirectory = modDirectory;
    }

    /// <summary>Whether the UI assemblies are loaded (a test's view of the lazy load).</summary>
    internal bool Loaded => _answer is not null;

    public HttpReply Answer(string body)
    {
        lock (_lock)
        {
            if (_answer is null && _failure is null)
                Load();
            if (_answer is null)
                return new HttpReply(503, JsonSerializer.Serialize(new { error = _failure }));
            return new HttpReply(200, _answer(body.Length > 0 ? body : "{\"op\":\"list\"}"));
        }
    }

    /// <summary>The UI assemblies in <paramref name="dir"/>: the toolkit, the core's screens, then each other <c>*.Ui</c> by name.</summary>
    internal static IReadOnlyList<string> UiAssemblies(string dir)
    {
        if (!Directory.Exists(dir))
            return [];
        return Directory.GetFiles(dir, "*.dll")
            .Where(p => Path.GetFileNameWithoutExtension(p) is var n && (n == Toolkit || n.EndsWith(".Ui", StringComparison.Ordinal)))
            .OrderBy(p => Path.GetFileNameWithoutExtension(p) switch { Toolkit => 0, "SourceSharp.Client.Ui" => 1, _ => 2 })
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The copy the screens read: the mod's own language files, then every
    /// sibling mod's (a game's screens shown from another mod keep their
    /// words), each directory's files by name.
    /// </summary>
    internal static IReadOnlyList<string> LanguageFiles(string modDir)
    {
        var files = new List<string>();
        void Add(string mod)
        {
            string dir = Path.Combine(mod, "resource");
            if (!Directory.Exists(dir)) return;
            var found = Directory.GetFiles(dir, "*_english.txt");
            Array.Sort(found, StringComparer.Ordinal);
            files.AddRange(found);
        }
        if (modDir.Length == 0)
            return files;
        Add(modDir);
        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(modDir));
        if (parent is not null && Directory.Exists(parent))
        {
            var siblings = Directory.GetDirectories(parent);
            Array.Sort(siblings, StringComparer.Ordinal);
            foreach (var sibling in siblings.Where(d => !string.Equals(Path.GetFullPath(d), Path.GetFullPath(modDir), StringComparison.Ordinal)))
                Add(sibling);
        }
        return files;
    }

    private void Load()
    {
        string dir = _managedDirectory();
        var paths = UiAssemblies(dir);
        if (!paths.Any(p => Path.GetFileNameWithoutExtension(p) == Toolkit))
        {
            _failure = $"no {Toolkit}.dll in {dir}: the client's UI is not deployed here (a dedicated server?)";
            return;
        }
        var context = new Context(dir);
        try
        {
            var assemblies = paths.Select(context.LoadFromAssemblyPath).ToList();
            var live = assemblies[0].GetType(Toolkit + ".UiLive", throwOnError: true)!;
            var forGame = live.GetMethod("ForGame", BindingFlags.Public | BindingFlags.Static)
                          ?? throw new MissingMethodException(live.FullName, "ForGame");
            string modDir = _modDirectory();
            string? theme = modDir.Length > 0 ? Path.Combine(modDir, "resource", "ui-theme.yaml") : null;
            var answerer = forGame.Invoke(null, [assemblies, theme, LanguageFiles(modDir)])!;
            _answer = (Func<string, string>)Delegate.CreateDelegate(typeof(Func<string, string>), answerer, "Answer");
            _context = context;
        }
        catch (Exception e) when (e is TargetInvocationException or IOException or BadImageFormatException or TypeLoadException
                                       or MissingMethodException or ArgumentException or InvalidCastException)
        {
            var inner = e is TargetInvocationException { InnerException: { } i } ? i : e;
            _failure = $"the UI assemblies in {dir} did not load: {inner.GetType().Name}: {inner.Message}";
            context.Unload();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _answer = null;
            _failure = "shutting down";
            _context?.Unload();
            _context = null;
        }
    }

    /// <summary>The UI assemblies' own context: each dependency from the managed directory, the framework from the default.</summary>
    private sealed class Context(string dir) : AssemblyLoadContext("UiMockups", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            string path = Path.Combine(dir, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
