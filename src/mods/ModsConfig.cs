using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SourceSharp.Mods;

/// <summary>A mods.yaml that failed to load, with the reason in words.</summary>
public sealed class ModsConfigException(string message) : Exception(message);

/// <summary>
/// How the engine is started in one role (hl2.sh's job, now the launcher's):
/// the install that is the engine directory, the executable and the install
/// that holds it, the restart code, the fd limit, and the role's own arguments
/// and environment.
/// </summary>
public sealed record EngineSettings(
    uint AppId,
    string Exe,
    uint ExeAppId,
    int RestartExitCode,
    int FdLimit,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env);

/// <summary>
/// A hosted mod's server-browser entry, validated: every template names only
/// keys the host publishes (<see cref="ModsConfig.BrowserKeys"/>).
/// </summary>
public sealed record BrowserSettings(
    string Name,
    string Description,
    string Tags,
    string Region,
    int MaxPlayers,
    string Map,
    IReadOnlyDictionary<string, string> Rules);

/// <summary>One mod, merged over the defaults, validated, with its paths made absolute.</summary>
public sealed record Mod(
    string Id,
    string Title,
    string GameDir,
    string? Content,
    uint SteamAppId,
    EngineSettings Client,
    EngineSettings? Dedicated,
    IReadOnlyList<string> ServerAssemblies,
    IReadOnlyList<string> ClientAssemblies,
    string ManagedDir,
    BrowserSettings? Browser = null,
    IReadOnlyList<string> ClientUiAssemblies = default!,
    IReadOnlyList<string> Localisation = default!)
{
    /// <summary>
    /// The variable source# sets when the game runs on a game directory other
    /// than <see cref="GameDir"/>: an -instance game's own directory, which
    /// links the mod's content and holds what this game writes.
    /// </summary>
    public const string GameDirVariable = "SOURCESHARP_GAME_DIR";

    /// <summary>The assemblies for one side, in load order.</summary>
    public IReadOnlyList<string> AssembliesFor(bool server) => server ? ServerAssemblies : ClientAssemblies;

    /// <summary>
    /// This mod as a game running on <paramref name="gameDir"/> sees it (the
    /// value of <see cref="GameDirVariable"/>); itself when that is unset, so
    /// managed code writes where the engine reads.
    /// </summary>
    public Mod RunningIn(string? gameDir) =>
        string.IsNullOrEmpty(gameDir) ? this : this with { GameDir = Path.GetFullPath(gameDir) };

    /// <summary>The engine settings for one role; the dedicated role only if the mod has one.</summary>
    public EngineSettings Engine(bool dedicated) =>
        !dedicated ? Client
        : Dedicated ?? throw new ModsConfigException($"mod '{Id}' has no engine.dedicated: it cannot run as a dedicated server");
}

/// <summary>
/// mods.yaml, loaded, merged and validated: every mod the launcher can start.
///
/// <para>A top-level <c>defaults</c> block holds any of a mod's settings; each
/// mod overrides what it gives (<see cref="Merge"/>). <c>{id}</c> in a title or
/// a path is the mod's id, substituted after the merge.</para>
///
/// <para>Rejected, each with a message that names the mod and the key path: a
/// key the schema does not have (in <c>defaults</c> or a mod), a
/// <c>default_mod</c> that names no mod, a mod with no <c>game_dir</c> after the
/// merge, and a relative path that climbs out of the directory it is relative
/// to. Built-in defaults exist only where hl2.sh had a fixed behaviour.</para>
/// </summary>
public sealed class ModsConfig
{
    /// <summary>Source SDK Base 2013 Multiplayer: the engine every mod here runs on.</summary>
    public const uint Sdk2013MpAppId = 243750;

    /// <summary>
    /// The world-status keys the host publishes for browser templates (the host's
    /// docs/mod-image.md, "The browser block"). Any other <c>{key}</c> refuses the
    /// host's Booting; it is refused here first, so a typo fails at load, not in a pod.
    /// </summary>
    public static readonly IReadOnlySet<string> BrowserKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "hub_map", "players", "max_players", "levels_open", "deepest", "players_by_level", "pool_ready",
    };

    public string Path { get; }
    public string? DefaultId { get; }
    public IReadOnlyDictionary<string, Mod> Mods { get; }

    private ModsConfig(string path, string? defaultId, IReadOnlyDictionary<string, Mod> mods)
    {
        Path = path;
        DefaultId = defaultId;
        Mods = mods;
    }

    public static ModsConfig Load(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ModsConfigException($"cannot read {full}: {e.Message}");
        }

        return Parse(text, full);
    }

    public static ModsConfig Parse(string yaml, string path)
    {
        ModsFile file;
        try
        {
            IDeserializer d = new StaticDeserializerBuilder(new ModsYamlContext())
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            file = d.Deserialize<ModsFile>(yaml) ?? new ModsFile();
        }
        catch (YamlException e)
        {
            string what = e.InnerException?.Message ?? e.Message;
            throw new ModsConfigException($"{path}:{e.Start.Line}:{e.Start.Column}: {what}");
        }

        string baseDir = System.IO.Path.GetDirectoryName(path) ?? ".";
        var mods = new Dictionary<string, Mod>(StringComparer.Ordinal);
        foreach (var (id, entry) in file.Mods ?? [])
            mods[id] = Validate(path, baseDir, id, Merge(file.Defaults, entry));

        if (mods.Count == 0)
            throw new ModsConfigException($"{path}: no mods");

        if (file.DefaultMod is not null && !mods.ContainsKey(file.DefaultMod))
        {
            throw new ModsConfigException(
                $"{path}: default_mod '{file.DefaultMod}' names no mod (known: {string.Join(", ", mods.Keys.Order())})");
        }

        return new ModsConfig(path, file.DefaultMod, mods);
    }

    public Mod Select(string? id)
    {
        string? want = id ?? DefaultId;
        if (want is null)
            throw new ModsConfigException($"{Path}: no default_mod, so -mod <id> is required (known: {Known})");

        if (!Mods.TryGetValue(want, out var mod))
            throw new ModsConfigException($"unknown mod '{want}' (known: {Known})");

        return mod;
    }

    private string Known => string.Join(", ", Mods.Keys.Order());

    //-------------------------------------------------------------------------
    // The merge: one mod's entry over the defaults. One rule, stated once and
    // ported once (tools/setup's merge_mod, held to this by a parity fact):
    // objects merge field by field, maps key by key, and a scalar or a list
    // the mod gives replaces the default's. Lists replace rather than append,
    // or "remove a default" could not be said.
    //-------------------------------------------------------------------------

    public static ModEntry Merge(ModEntry? defaults, ModEntry? mod)
    {
        defaults ??= new ModEntry();
        mod ??= new ModEntry();
        return new ModEntry
        {
            Title = mod.Title ?? defaults.Title,
            GameDir = mod.GameDir ?? defaults.GameDir,
            Content = mod.Content ?? defaults.Content,
            SteamAppid = mod.SteamAppid ?? defaults.SteamAppid,
            ManagedDir = mod.ManagedDir ?? defaults.ManagedDir,
            Engine = Merge(defaults.Engine, mod.Engine),
            Assemblies = defaults.Assemblies is null && mod.Assemblies is null ? null : new AssembliesEntry
            {
                Server = mod.Assemblies?.Server ?? defaults.Assemblies?.Server,
                Client = mod.Assemblies?.Client ?? defaults.Assemblies?.Client,
                ClientUi = mod.Assemblies?.ClientUi ?? defaults.Assemblies?.ClientUi,
            },
            Localisation = mod.Localisation ?? defaults.Localisation,
            Browser = Merge(defaults.Browser, mod.Browser),
        };
    }

    private static BrowserEntry? Merge(BrowserEntry? defaults, BrowserEntry? mod) =>
        defaults is null && mod is null ? null : new BrowserEntry
        {
            Name = mod?.Name ?? defaults?.Name,
            Description = mod?.Description ?? defaults?.Description,
            Tags = mod?.Tags ?? defaults?.Tags,
            Region = mod?.Region ?? defaults?.Region,
            MaxPlayers = mod?.MaxPlayers ?? defaults?.MaxPlayers,
            Map = mod?.Map ?? defaults?.Map,
            Rules = MergeMap(defaults?.Rules, mod?.Rules),
        };

    private static EngineEntry? Merge(EngineEntry? defaults, EngineEntry? mod) =>
        defaults is null && mod is null ? null : new EngineEntry
        {
            Client = Merge(defaults?.Client, mod?.Client),
            Dedicated = Merge(defaults?.Dedicated, mod?.Dedicated),
        };

    private static EngineRoleEntry? Merge(EngineRoleEntry? defaults, EngineRoleEntry? mod) =>
        defaults is null && mod is null ? null : new EngineRoleEntry
        {
            Appid = mod?.Appid ?? defaults?.Appid,
            Exe = mod?.Exe ?? defaults?.Exe,
            ExeAppid = mod?.ExeAppid ?? defaults?.ExeAppid,
            RestartExitCode = mod?.RestartExitCode ?? defaults?.RestartExitCode,
            FdLimit = mod?.FdLimit ?? defaults?.FdLimit,
            Args = mod?.Args ?? defaults?.Args,
            Env = MergeMap(defaults?.Env, mod?.Env),
        };

    private static Dictionary<string, string>? MergeMap(Dictionary<string, string>? defaults, Dictionary<string, string>? mod)
    {
        if (defaults is null && mod is null)
            return null;

        var merged = new Dictionary<string, string>(defaults ?? [], StringComparer.Ordinal);
        foreach (var (k, v) in mod ?? [])
            merged[k] = v;
        return merged;
    }

    /// <summary><c>{id}</c> in a title or a path is the mod's id.</summary>
    private static string? WithId(string? text, string id) => text?.Replace("{id}", id, StringComparison.Ordinal);

    //-------------------------------------------------------------------------
    // Validation, on the merged entry.
    //-------------------------------------------------------------------------

    private static Mod Validate(string path, string baseDir, string id, ModEntry e)
    {
        string where = $"{path}: mod '{id}'";

        string? gameDirText = WithId(e.GameDir, id);
        if (string.IsNullOrWhiteSpace(gameDirText))
            throw new ModsConfigException($"{where} has no game_dir (neither the mod nor defaults gives one)");

        string gameDir = Inside(where, "game_dir", baseDir, gameDirText);
        string managedDir = Inside(where, "managed_dir", gameDir, WithId(e.ManagedDir, id) ?? "bin/managed");

        var client = Role(where, "engine.client", e.Engine?.Client, new EngineRoleEntry())!;
        var dedicated = e.Engine?.Dedicated is null ? null : Role(where, "engine.dedicated", e.Engine.Dedicated, null);

        return new Mod(
            id,
            WithId(e.Title, id) ?? id,
            gameDir,
            WithId(e.Content, id),
            e.SteamAppid ?? client.AppId,
            client,
            dedicated,
            e.Assemblies?.Server ?? [],
            e.Assemblies?.Client ?? [],
            managedDir,
            e.Browser is null ? null : Browser(where, e.Browser),
            e.Assemblies?.ClientUi ?? [],
            e.Localisation ?? []);
    }

    private static BrowserSettings Browser(string where, BrowserEntry b)
    {
        if (string.IsNullOrWhiteSpace(b.Name))
            throw new ModsConfigException($"{where}: browser has no name");
        if (b.MaxPlayers is < 0)
            throw new ModsConfigException($"{where}: browser.max_players is {b.MaxPlayers}; 0 means the host's capacity");

        string Template(string key, string? text)
        {
            text ??= "";
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\{([^{}]*)\}"))
            {
                if (!BrowserKeys.Contains(m.Groups[1].Value))
                    throw new ModsConfigException(
                        $"{where}: browser.{key} uses {{{m.Groups[1].Value}}}, which the host does not publish " +
                        $"(it publishes {string.Join(", ", BrowserKeys.Order().Select(k => "{" + k + "}"))})");
            }
            return text;
        }

        var rules = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in b.Rules ?? [])
        {
            if (k.Length == 0 || k.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
                throw new ModsConfigException($"{where}: browser.rules key '{k}' is not letters, digits and _");
            rules[k] = Template($"rules.{k}", v);
        }

        string region = b.Region ?? "";
        if (region.Contains('{') || region.Contains('}'))
            throw new ModsConfigException($"{where}: browser.region '{region}' is not a template");

        return new BrowserSettings(
            Template("name", b.Name),
            Template("description", b.Description),
            Template("tags", b.Tags),
            region,
            b.MaxPlayers ?? 0,
            Template("map", b.Map),
            rules);
    }

    private static EngineSettings? Role(string where, string key, EngineRoleEntry? entry, EngineRoleEntry? fallback)
    {
        var r = entry ?? fallback;
        if (r is null)
            return null;

        uint appId = r.Appid ?? Sdk2013MpAppId;
        var settings = new EngineSettings(
            appId,
            r.Exe ?? "hl2_linux64",
            r.ExeAppid ?? appId,
            r.RestartExitCode ?? 42,
            r.FdLimit ?? 2048,
            r.Args ?? [],
            r.Env ?? new Dictionary<string, string>());

        if (settings.Exe.Contains('/'))
            throw new ModsConfigException($"{where}: {key}.exe is a file name in the engine install, not a path");

        return settings;
    }

    private static string Inside(string where, string key, string baseDir, string rel)
    {
        if (System.IO.Path.IsPathRooted(rel))
            throw new ModsConfigException($"{where}: {key} must be relative, not {rel}");

        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, rel));
        if (!IsWithin(System.IO.Path.GetFullPath(baseDir), full, System.IO.Path.DirectorySeparatorChar,
                      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ModsConfigException($"{where}: {key} '{rel}' leaves {baseDir}");

        return full;
    }

    /// <summary>
    /// Whether <paramref name="full"/> is <paramref name="root"/> or beneath it,
    /// both already full paths spelled with <paramref name="separator"/>.
    ///
    /// <para>
    /// The separator and the comparison are the platform's, passed in so a fact
    /// can hold the Windows spelling on any OS. The check was written with a
    /// literal '/', and on Windows, where GetFullPath answers with backslashes,
    /// it refused every mod's game_dir as leaving the game directory, so the
    /// launcher could start nothing there.
    /// </para>
    /// </summary>
    internal static bool IsWithin(string root, string full, char separator, StringComparison comparison)
    {
        string prefix = root.TrimEnd(separator) + separator;
        return (full.TrimEnd(separator) + separator).StartsWith(prefix, comparison);
    }
}
