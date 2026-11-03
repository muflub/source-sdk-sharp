using System.Security.Cryptography;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>A level's navigation, loaded and derived: the reader, the graph, the map's entities, and the hashes that name them.</summary>
public sealed record LoadedNav(string Map, Nav3dReader Reader, DerivedNav Nav, MapEntities Entities, string BspSha256, string NavSha256,
                               bool FromCache, double DeriveMs);

/// <summary>What loading answered: the navigation, or a refusal naming the map and the file (never both).</summary>
public sealed record NavLoadResult(LoadedNav? Nav, string? Refusal)
{
    public static NavLoadResult Refuse(string why) => new(null, why);
}

/// <summary>
/// Loads a level's <c>maps/&lt;map&gt;.nav3d</c> beside its bsp
/// (plan_hl2mp_ai §6.3), read through the engine's file seam: a missing
/// file, one that is not a navigation, or one built from another map is a
/// refusal naming the map and the expected file, never a fallback. A map
/// carrying <c>ss_level_id</c> binds by it (reader <c>MatchesMap</c>); any
/// other by the bsp's checksum and SHA-256 (<c>MatchesBsp</c>). The derived
/// graph is cached by both files' hashes and the reader version.
/// </summary>
public static class NavLoader
{
    public static string NavPath(string map) => $"maps/{map}.nav3d";

    public static string BspPath(string map) => $"maps/{map}.bsp";

    public static NavLoadResult Load(string map, Func<string, byte[]?> read, NavGraphCache? cache)
    {
        ArgumentNullException.ThrowIfNull(read);
        byte[]? nav = read(NavPath(map));
        if (nav is null)
            return NavLoadResult.Refuse($"no navigation for {map}: expected {NavPath(map)}");
        byte[]? bsp = read(BspPath(map));
        if (bsp is null)
            return NavLoadResult.Refuse($"no {BspPath(map)} to bind {NavPath(map)} to");

        Nav3dReader reader;
        MapEntities entities;
        try
        {
            reader = Nav3dReader.Open(nav);
            entities = MapEntities.Read(bsp);
        }
        catch (InvalidDataException e)
        {
            return NavLoadResult.Refuse($"{NavPath(map)} for {map} cannot be read: {e.Message}");
        }

        string? levelId = entities.LevelId;
        if (!string.IsNullOrEmpty(levelId))
        {
            if (!reader.MatchesMap(levelId))
                return NavLoadResult.Refuse($"{NavPath(map)} was built for another level: it names {reader.LevelId}, {map}'s ss_level_id is {levelId}");
        }
        else if (!reader.MatchesBsp(bsp))
        {
            string named = reader.Bsp is { } b ? $"names map checksum {b.MapChecksum}" : "names no map";
            return NavLoadResult.Refuse($"{NavPath(map)} was built from another map: it {named}, not {BspPath(map)} "
                                        + $"(checksum {Nav3dBspBinding.Of(bsp).MapChecksum})");
        }

        string bspSha = Convert.ToHexStringLower(SHA256.HashData(bsp));
        string navSha = Convert.ToHexStringLower(SHA256.HashData(nav));
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        DerivedNav? derived = cache?.TryRead(map, bspSha, navSha);
        bool fromCache = derived is not null;
        if (derived is null)
        {
            derived = NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), Hl2mpNavNumbers.Stock(reader.JumpHeight), NavEdgeCosts.Default,
                                               entities.Hurt, entities.LadderClimbs);
            cache?.Write(map, bspSha, navSha, derived);
        }

        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new NavLoadResult(new LoadedNav(map, reader, derived, entities, bspSha, navSha, fromCache, ms), null);
    }
}
