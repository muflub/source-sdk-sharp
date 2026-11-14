using System.Buffers.Binary;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Rows of settings every mode gets beside its own (plan_bots_teams_tf2 D.2):
/// a chain, so a binding that runs in every mode (the AI's bot count and
/// difficulty) publishes its rows once rather than into each mode.
/// </summary>
public interface IModeSettingsProvider
{
    /// <summary>The rows to add to <paramref name="mode"/>; a key the mode has already is skipped.</summary>
    IEnumerable<SettingSpec> SettingsFor(ModeSpec mode);
}

/// <summary>The entity classes a map's bsp names, read from its entity lump.</summary>
public interface IMapEntityClasses
{
    /// <summary>The class names <paramref name="map"/>'s entity lump holds; null when its bsp cannot be read.</summary>
    IReadOnlySet<string>? Of(string map);
}

/// <summary>
/// Reads the entity lump (lump 0) of a bsp on the GAME paths for its class
/// names, once per map: a map's entities do not change while the server runs,
/// and the read is the whole file.
/// </summary>
public sealed class BspEntityClasses(IEngineFiles files) : IMapEntityClasses
{
    private const int Ident = 'V' | ('B' << 8) | ('S' << 16) | ('P' << 24);
    private const int HeaderBytes = 8 + 64 * 16;

    private readonly Dictionary<string, IReadOnlySet<string>?> _byMap = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string>? Of(string map)
    {
        if (map.Length == 0 || map.Contains("..", StringComparison.Ordinal) || map.Contains('/', StringComparison.Ordinal))
            return null;
        if (_byMap.TryGetValue(map, out var known))
            return known;
        var read = files.ReadGameFileBytes($"maps/{map}.bsp") is { } bytes ? Read(bytes) : null;
        _byMap[map] = read;
        return read;
    }

    /// <summary>
    /// The class names of <paramref name="bsp"/>'s entity lump; null when it is
    /// not a bsp, its lump lies outside the file, or the lump is compressed (a
    /// non-zero uncompressed size in its fourCC, which this reader cannot open).
    /// </summary>
    public static IReadOnlySet<string>? Read(ReadOnlySpan<byte> bsp)
    {
        if (bsp.Length < HeaderBytes || BinaryPrimitives.ReadInt32LittleEndian(bsp) != Ident)
            return null;
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bsp[8..]);
        int length = BinaryPrimitives.ReadInt32LittleEndian(bsp[12..]);
        int fourCc = BinaryPrimitives.ReadInt32LittleEndian(bsp[20..]);
        if (offset < 0 || length < 0 || (long)offset + length > bsp.Length || fourCc != 0)
            return null;
        ReadOnlySpan<byte> lump = bsp.Slice(offset, length);
        int end = lump.IndexOf((byte)0);
        return ClassNames(Encoding.UTF8.GetString(end >= 0 ? lump[..end] : lump));
    }

    /// <summary>Every <c>"classname" "value"</c> pair's value in entity-lump text.</summary>
    public static IReadOnlySet<string> ClassNames(string text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '{' || c == '}')
            {
                key = null;
                i++;
                continue;
            }
            if (c != '"')
            {
                i++;
                continue;
            }
            int close = text.IndexOf('"', i + 1);
            if (close < 0)
                break;
            string token = text[(i + 1)..close];
            i = close + 1;
            if (key is null)
            {
                key = token;
                continue;
            }
            if (string.Equals(key, "classname", StringComparison.OrdinalIgnoreCase))
                found.Add(token);
            key = null;
        }
        return found;
    }
}

/// <summary>
/// The game's modes as the menus see them (plan_bots_teams_tf2 D.2): each
/// registered mode's <see cref="IGameMode.Settings"/> (or its id alone), with the
/// rows every mode gets from the <see cref="IModeSettingsProvider"/> chain,
/// and which modes a map can play from its entity lump. Votes, the admin menu
/// and the client's copy all read this one list.
/// </summary>
public sealed class GameModeCatalog(GameModes modes, IMapEntityClasses maps, ILevelInfo level, IHooks<IModeSettingsProvider>? providers = null)
{
    /// <summary>Every registered mode, deathmatch first, the rest in registration order.</summary>
    public IReadOnlyList<ModeSpec> Modes()
        => [.. modes.Registered.OrderBy(m => string.Equals(m.Id, GameModes.NullModeId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                .Select(SpecOf)];

    public ModeSpec? Find(string? id)
        => id is null ? null : modes.Registered.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) is { } mode ? SpecOf(mode) : null;

    /// <summary>The mode playing now.</summary>
    public ModeSpec? Active => modes.Active is { } mode ? SpecOf(mode) : null;

    /// <summary>The level playing now; empty before the first.</summary>
    public string CurrentMap => level.Map ?? "";

    /// <summary>One setting of the mode playing now, by its convar; null when that mode has no such setting.</summary>
    public SettingSpec? ActiveSetting(string key)
        => Active?.Settings.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="map"/> can play the mode <paramref name="modeId"/>, and why not.</summary>
    public MapSupport Supports(string map, string modeId)
        => Find(modeId) is { } mode ? Judge(map, mode, mode.RequiresAnyOf.Count == 0 ? null : maps.Of(map))
                                    : new MapSupport(false, $"no mode is called '{modeId}'");

    /// <summary>
    /// The rule: a mode that requires nothing plays anywhere; one that does
    /// needs one of its classes in the map's lump, and a lump that cannot be
    /// read supports nothing that needs one.
    /// </summary>
    public static MapSupport Judge(string map, ModeSpec mode, IReadOnlySet<string>? classes)
    {
        if (mode.RequiresAnyOf.Count == 0)
            return new MapSupport(true, "");
        if (classes is null)
            return new MapSupport(false, $"{map}'s entities could not be read");
        if (mode.RequiresAnyOf.Any(classes.Contains))
            return new MapSupport(true, "");
        return new MapSupport(false, $"{map} has no {string.Join(" or ", mode.RequiresAnyOf)}");
    }

    private ModeSpec SpecOf(IGameMode mode)
    {
        var spec = mode.Settings is { } own ? new ModeSpec(mode.Id, own.Title, mode.Teamplay, own.Settings, own.RequiresAnyOf)
                                            : new ModeSpec(mode.Id, mode.Id, mode.Teamplay, [], []);
        if (providers is null)
            return spec;
        var settings = new List<SettingSpec>(spec.Settings);
        foreach (var provider in providers.All)
        {
            foreach (var row in provider.SettingsFor(spec))
            {
                if (!settings.Any(s => string.Equals(s.Key, row.Key, StringComparison.OrdinalIgnoreCase)))
                    settings.Add(row);
            }
        }
        return spec with { Settings = settings };
    }
}
