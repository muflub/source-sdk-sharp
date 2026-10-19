using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Shared.Interop;

/// <summary>One assembly the side's load context read, as the Bootstrap hashed it going past.</summary>
public readonly record struct LoadedAssembly(string Name, string Hash, DateTime WrittenUtc);

/// <summary>
/// What the Bootstrap tells a side's entry about its load (plan_side_entries
/// SE2): the mod, its directories and both assembly lists. The ONE reader of
/// the blocks <c>SourceSharp.Bootstrap.LoadFacts</c> writes: UTF-8,
/// NUL-terminated, one <c>key&lt;TAB&gt;value...</c> per line.
///
/// <para>
/// Two blocks, read by two methods that each refuse the other's rows: Load's
/// (<see cref="ReadLoad"/>) carries no <c>loaded</c> row, because the context
/// has not finished reading when Load starts; Stamped's
/// (<see cref="ReadLoaded"/>) carries nothing else. A missing or unknown key is
/// refused, never defaulted.
/// </para>
/// </summary>
public sealed record SideFacts(
    string ManagedDir,
    string ModId,
    string GameDir,
    IReadOnlyList<string> ServerAssemblies,
    IReadOnlyList<string> ClientAssemblies,
    string? BrowserJson = null,
    IReadOnlyList<string>? ClientUiAssemblies = null,
    IReadOnlyList<string>? Localisation = null)
{
    /// <summary>The mod's UI assemblies (mods.yaml `assemblies.client_ui`); empty when it named none.</summary>
    public IReadOnlyList<string> ModClientUiAssemblies => ClientUiAssemblies ?? [];

    /// <summary>The mod's extra language files (mods.yaml `localisation`); empty when it named none.</summary>
    public IReadOnlyList<string> ModLocalisation => Localisation ?? [];

    /// <summary>The side's own list of assemblies.</summary>
    public IReadOnlyList<string> AssembliesFor(bool server) => server ? ServerAssemblies : ClientAssemblies;

    /// <summary>Reads Load's block at <paramref name="pFacts"/>, up to its NUL.</summary>
    public static unsafe SideFacts ReadLoad(byte* pFacts) => ParseLoad(Span(pFacts));

    /// <summary>Reads Stamped's block at <paramref name="pFacts"/>, up to its NUL.</summary>
    public static unsafe IReadOnlyList<LoadedAssembly> ReadLoaded(byte* pFacts) => ParseLoaded(Span(pFacts));

    /// <summary>Load's block, without its NUL.</summary>
    public static SideFacts ParseLoad(ReadOnlySpan<byte> utf8)
    {
        string? managedDir = null, modId = null, gameDir = null, browser = null;
        IReadOnlyList<string>? server = null, client = null, clientUi = null, localisation = null;

        foreach (string[] f in Lines(utf8))
        {
            switch (f[0])
            {
                case "managed_dir": managedDir = One(f); break;
                case "mod_id": modId = One(f); break;
                case "game_dir": gameDir = One(f); break;
                case "server_assemblies": server = Names(One(f)); break;
                case "client_assemblies": client = Names(One(f)); break;
                case "browser": browser = One(f); SourceSharp.Abi.ModBrowser.Parse(browser); break;    // refused here, not when read
                case "client_ui_assemblies": clientUi = Names(One(f)); break;
                case "localisation": localisation = Names(One(f)); break;
                default:
                    throw new FormatException($"Load's facts do not carry {f[0]}: {string.Join('\t', f)}");
            }
        }

        return new SideFacts(
            managedDir ?? throw Missing("managed_dir"),
            modId ?? throw Missing("mod_id"),
            gameDir ?? throw Missing("game_dir"),
            server ?? throw Missing("server_assemblies"),
            client ?? throw Missing("client_assemblies"),
            browser, clientUi, localisation);
    }

    /// <summary>Stamped's block, without its NUL.</summary>
    public static IReadOnlyList<LoadedAssembly> ParseLoaded(ReadOnlySpan<byte> utf8)
    {
        var loaded = new List<LoadedAssembly>();
        foreach (string[] f in Lines(utf8))
        {
            if (f[0] != "loaded")
                throw new FormatException($"Stamped's facts carry only loaded rows: {string.Join('\t', f)}");
            if (f.Length != 4 || !long.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
                throw new FormatException($"a loaded fact needs a name, a hash and a tick count: {string.Join('\t', f)}");
            loaded.Add(new LoadedAssembly(f[1], f[2], new DateTime(ticks, DateTimeKind.Utc)));
        }
        return loaded;
    }

    private static unsafe ReadOnlySpan<byte> Span(byte* pFacts)
    {
        if (pFacts == null)
            throw new FormatException("the Bootstrap handed no load facts");
        return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(pFacts);
    }

    private static IEnumerable<string[]> Lines(ReadOnlySpan<byte> utf8)
        => Encoding.UTF8.GetString(utf8).Split('\n').Where(l => l.Length > 0).Select(l => l.Split('\t'));

    private static string One(string[] fields)
        => fields.Length == 2 ? fields[1] : throw new FormatException($"the {fields[0]} fact needs one value");

    private static string[] Names(string list)
        => list.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static FormatException Missing(string key) => new($"the load facts have no {key}");
}
