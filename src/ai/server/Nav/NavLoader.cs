using System.Security.Cryptography;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>A level's navigation, loaded and derived: the reader, the graph, the map's entities, and the hashes that name them.</summary>
/// <remarks><see cref="NumbersKey"/> names the derivation's numbers in the cache when they are not the core's default movement's, else "".</remarks>
/// <param name="Built">Whether the file is the server's own build from the cache directory (<see cref="BuiltNavStore"/>), not one beside the map.</param>
public sealed record LoadedNav(string Map, Nav3dReader Reader, DerivedNav Nav, MapEntities Entities, string BspSha256, string NavSha256,
                               bool FromCache, double DeriveMs, string NumbersKey = "", bool Built = false);

/// <summary>
/// What loading answered: the navigation, or a refusal naming the map and the file (never both);
/// <see cref="Missing"/> is the file a refusal found absent, null for any other refusal.
/// </summary>
public sealed record NavLoadResult(LoadedNav? Nav, string? Refusal, string? Missing = null)
{
    /// <summary>The map's bsp when it has one and no navigation bound to it: what a build starts from (plan_bots_teams_tf2 E.3); else null.</summary>
    public byte[]? Bsp { get; init; }

    public static NavLoadResult Refuse(string why) => new(null, why);
}

/// <summary>
/// Loads a level's navigation (plan_hl2mp_ai §6.3, plan_bots_teams_tf2 E.3),
/// in order: <c>maps/&lt;map&gt;.nav3d</c> beside its bsp, read through the
/// engine's file seam (a shipped or hand-built file), then the server's own
/// build of it in the cache directory (<see cref="BuiltNavStore"/>). A
/// readable file bound to this bsp wins. A missing file, one that is not a
/// navigation, or one built from another map is a refusal naming the map and
/// the file, and the answer carries the bsp so the server can build one. A
/// map carrying <c>ss_level_id</c> binds by it (reader <c>MatchesMap</c>); any
/// other by the bsp's checksum and SHA-256 (<c>MatchesBsp</c>), which also
/// binds the server's build of a linked level. The graph is derived by the
/// game's <see cref="INavProfile"/> (the core's default movement without one)
/// and cached by both files' hashes, the reader version and, when they are
/// not the default's, the profile's numbers.
/// </summary>
public static class NavLoader
{
    public static string NavPath(string map) => $"maps/{map}.nav3d";

    public static string BspPath(string map) => $"maps/{map}.bsp";

    public static NavLoadResult Load(string map, Func<string, byte[]?> read, NavGraphCache? cache, INavProfile? profile = null, BuiltNavStore? built = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        byte[]? nav = read(NavPath(map));
        byte[]? bsp = read(BspPath(map));
        if (nav is null && bsp is null)
            return new NavLoadResult(null, $"no navigation for {map}: expected {NavPath(map)}", NavPath(map));
        if (bsp is null)
            return NavLoadResult.Refuse($"no {BspPath(map)} to bind {NavPath(map)} to");

        string refusal = $"no navigation for {map}: expected {NavPath(map)}";
        if (nav is not null)
        {
            NavLoadResult shipped = Bind(map, NavPath(map), nav, bsp, cache, profile, fromStore: false);
            if (shipped.Nav is not null)
                return shipped;
            refusal = shipped.Refusal!;
        }

        if (built is not null && built.TryRead(map, BuiltNavStore.Sha256(bsp)) is { } own)
        {
            NavLoadResult cached = Bind(map, built.Name(map, BuiltNavStore.Sha256(bsp)), own, bsp, cache, profile, fromStore: true);
            if (cached.Nav is not null)
                return cached;
            refusal = $"{refusal}; {cached.Refusal}";
        }

        return new NavLoadResult(null, refusal, nav is null ? NavPath(map) : null) { Bsp = bsp };
    }

    private static NavLoadResult Bind(string map, string navName, byte[] nav, byte[] bsp, NavGraphCache? cache, INavProfile? profile, bool fromStore)
    {
        Nav3dReader reader;
        MapEntities entities;
        try
        {
            reader = Nav3dReader.Open(nav);
            entities = MapEntities.Read(bsp);
        }
        catch (InvalidDataException e)
        {
            return NavLoadResult.Refuse($"{navName} for {map} cannot be read: {e.Message}");
        }

        string? levelId = entities.LevelId;
        if (!string.IsNullOrEmpty(levelId) && !reader.MatchesMap(levelId) && !reader.MatchesBsp(bsp))
            return NavLoadResult.Refuse($"{navName} was built for another level: it names {reader.LevelId}, {map}'s ss_level_id is {levelId}");
        if (string.IsNullOrEmpty(levelId) && !reader.MatchesBsp(bsp))
        {
            string named = reader.Bsp is { } b ? $"names map checksum {b.MapChecksum}" : "names no map";
            return NavLoadResult.Refuse($"{navName} was built from another map: it {named}, not {BspPath(map)} "
                                        + $"(checksum {Nav3dBspBinding.Of(bsp).MapChecksum})");
        }

        string bspSha = Convert.ToHexStringLower(SHA256.HashData(bsp));
        string navSha = Convert.ToHexStringLower(SHA256.HashData(nav));
        NavNumbers numbers = profile?.Numbers(reader.JumpHeight) ?? NavNumbers.Stock(reader.JumpHeight);
        string key = NavGraphCache.NumbersKey(numbers, reader.JumpHeight);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        DerivedNav? derived = cache?.TryRead(map, bspSha, navSha, key);
        bool fromCache = derived is not null;
        if (derived is null)
        {
            derived = NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), numbers, NavEdgeCosts.Default,
                                               entities.Hurt, entities.LadderClimbs);
            cache?.Write(map, bspSha, navSha, derived, key);
        }

        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new NavLoadResult(new LoadedNav(map, reader, derived, entities, bspSha, navSha, fromCache, ms, key, fromStore), null);
    }
}
