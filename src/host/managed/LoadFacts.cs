using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SourceSharp.Bootstrap;

/// <summary>One assembly a side's load context read: its file name, content hash and write time.</summary>
internal readonly record struct LoadedFile(string Name, string Hash, DateTime WrittenUtc);

/// <summary>
/// The facts the Bootstrap hands a side's entry (plan_side_entries SE2): two
/// UTF-8, NUL-terminated blocks, one <c>key&lt;TAB&gt;value...</c> per line.
/// <see cref="WriteLoad"/>'s goes to the side's Load: the mod, its directories
/// and both assembly lists. <see cref="WriteStamped"/>'s goes to its Stamped,
/// after the module has initialised: one <c>loaded</c> row per assembly the
/// context read by then (the integrator's ruling A, 2026-10-02). The ONE
/// writer; the one reader is <c>SourceSharp.Shared.Interop.SideFacts</c>, and
/// LoadFactsTests round-trips the two.
/// </summary>
internal static class LoadFacts
{
    public const string ManagedDir = "managed_dir";
    public const string ModId = "mod_id";
    public const string GameDir = "game_dir";
    public const string ServerAssemblies = "server_assemblies";
    public const string ClientAssemblies = "client_assemblies";
    public const string Browser = "browser";
    public const string ClientUiAssemblies = "client_ui_assemblies";
    public const string Localisation = "localisation";
    public const string Loaded = "loaded";

    /// <summary>
    /// Load's block, NUL included. <paramref name="browser"/> is the mod's
    /// server-browser entry (host plan_steam_auth S-18), written as one line
    /// of JSON (<see cref="BrowserJson"/>); a mod without one writes no row.
    /// <paramref name="clientUiAssemblies"/> and <paramref name="localisation"/>
    /// are the mod's <c>assemblies.client_ui</c> and <c>localisation</c> lists
    /// (stage 0.9): the client's UiLoader loads the first into the UI's
    /// collectible context, and its localiser asks the second of the engine
    /// after the mod's own disk file. A mod without either writes an empty
    /// row, exactly like the side lists.
    /// </summary>
    public static byte[] WriteLoad(string managedDir, string modId, string gameDir,
                                   IEnumerable<string> serverAssemblies, IEnumerable<string> clientAssemblies,
                                   SourceSharp.Mods.BrowserSettings? browser = null,
                                   IEnumerable<string>? clientUiAssemblies = null,
                                   IEnumerable<string>? localisation = null)
    {
        var text = new StringBuilder();
        Line(text, ManagedDir, managedDir);
        Line(text, ModId, modId);
        Line(text, GameDir, gameDir);
        Line(text, ServerAssemblies, List(serverAssemblies));
        Line(text, ClientAssemblies, List(clientAssemblies));
        if (browser is not null)
            Line(text, Browser, BrowserJson(browser));
        Line(text, ClientUiAssemblies, List(clientUiAssemblies ?? []));
        Line(text, Localisation, List(localisation ?? []));
        return Block(text);
    }

    /// <summary>
    /// The browser entry as one line of JSON: <c>name</c>, <c>description</c>,
    /// <c>tags</c>, <c>region</c>, <c>max_players</c>, <c>map</c>, <c>rules</c>
    /// (an object). JSON escapes every tab and newline, so it fits a fact.
    /// </summary>
    public static string BrowserJson(SourceSharp.Mods.BrowserSettings b)
    {
        using var buffer = new MemoryStream();
        using (var w = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("name", b.Name);
            w.WriteString("description", b.Description);
            w.WriteString("tags", b.Tags);
            w.WriteString("region", b.Region);
            w.WriteNumber("max_players", b.MaxPlayers);
            w.WriteString("map", b.Map);
            w.WriteStartObject("rules");
            foreach (var (k, v) in b.Rules.OrderBy(p => p.Key, StringComparer.Ordinal))
                w.WriteString(k, v);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Stamped's block, NUL included: what the context has read.</summary>
    public static byte[] WriteStamped(IEnumerable<LoadedFile> loaded)
    {
        var text = new StringBuilder();
        foreach (LoadedFile a in loaded)
            Line(text, Loaded, a.Name, a.Hash, a.WrittenUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        return Block(text);
    }

    private static byte[] Block(StringBuilder text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text.ToString());
        var block = new byte[utf8.Length + 1];
        utf8.CopyTo(block, 0);
        return block;
    }

    /// <summary>
    /// A file's content hash: SHA-256, lowercase hex. The same answer as
    /// BuildStampBuilder.HashOf, which the side stamps with
    /// (LoadFactsTests pins that the two agree).
    /// </summary>
    public static string HashOf(ReadOnlySpan<byte> content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string List(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Any(n => n.Length == 0 || n.Contains(',')))
            throw new ArgumentException("an assembly name is empty or holds a comma");
        return string.Join(',', list);
    }

    private static void Line(StringBuilder text, string key, params string[] values)
    {
        text.Append(key);
        foreach (string value in values)
        {
            if (value.AsSpan().IndexOfAny('\t', '\n', '\0') >= 0)
                throw new ArgumentException($"the {key} fact holds a tab, newline or NUL: {value}");
            text.Append('\t').Append(value);
        }
        text.Append('\n');
    }
}
