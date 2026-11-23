using System.Security.Cryptography;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// The navigation file a level was loaded from, whichever kind it is (plan_bots_teams_tf2_part_g
/// G.7): what answers the derivation's questions (<see cref="Source"/>) and whether a point is in
/// free space (<see cref="Space"/>). <see cref="Name"/> is the file as a line names it
/// (<c>maps/&lt;map&gt;.nav3d</c>, <c>maps/&lt;map&gt;.navhull</c>, or the server's own build).
/// </summary>
public sealed record NavFile(string Kind, string Name, INavSource Source, IVoxelSpace Space)
{
    /// <summary>A <c>.nav3d</c>: every agent's exact clearance, a leaf per run of a column.</summary>
    public const string Nav3d = "nav3d";

    /// <summary>A <c>.navhull</c>: boxes with a ceiling per built width, which holds a map too tall for a <c>.nav3d</c>.</summary>
    public const string Hull = "navhull";

    /// <summary>The file's own jump height: the highest jump link it writes.</summary>
    public float JumpHeight => Source.JumpHeight;

    public static NavFile Of(Nav3dReader reader, string name)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new NavFile(Nav3d, name, new Nav3dNavSource(reader), new Nav3dVoxelSpace(reader));
    }

    public static NavFile Of(NavHullReader reader, string name)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new NavFile(Hull, name, NavHullNavSource.Over(reader), new NavHullVoxelSpace(reader));
    }
}

/// <summary>A level's navigation, loaded and derived: the file, the graph, the map's entities, and the hashes that name them.</summary>
/// <remarks><see cref="NumbersKey"/> names the derivation's numbers in the cache when they are not the core's default movement's, else "".</remarks>
/// <param name="Built">Whether the file is the server's own build from the cache directory (<see cref="BuiltNavStore"/>), not one beside the map.</param>
public sealed record LoadedNav(string Map, NavFile File, DerivedNav Nav, MapEntities Entities, string BspSha256, string NavSha256,
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
/// A level whose navigation is a file per room (plan_bots_teams_tf2_part_g R.2 stage A): the room
/// index, the room table, the map's bytes and entities, and what names a room's graph in the
/// cache. Nothing is derived here: a room is derived when the game asks for it
/// (<see cref="NavRoomJob"/>).
/// </summary>
public sealed record NavRoomsLevel(string Map, NavRoomIndex Index, NavRoomTable Table, byte[] Bsp, string BspSha256, MapEntities Entities)
{
    /// <summary>The room a place is in: the room in play when it is one of the index's answers, else the first answer with a room in the table; -1 for none.</summary>
    public int RoomAt(AiVector at, int inPlay, List<int> scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        Index.RegionsAt(new SourceSharp.MapFormats.Geometry.Vec3(at.X, at.Y, at.Z + 1f), scratch);
        if (scratch.Contains(inPlay))
            return inPlay;
        foreach (int region in scratch)
        {
            if (Table.Room(region) is not null)
                return region;
        }

        return -1;
    }
}

/// <summary>
/// Loads a level's navigation (plan_hl2mp_ai §6.3, plan_bots_teams_tf2 E.3,
/// plan_bots_teams_tf2_part_g G.7), in order: <c>maps/&lt;map&gt;.nav3d</c>
/// beside its bsp, read through the engine's file seam (a shipped or
/// hand-built file); then <c>maps/&lt;map&gt;.navhull</c> beside it, the hull
/// navigation a map too tall for a <c>.nav3d</c> has; then the server's own
/// build of the <c>.nav3d</c> in the cache directory
/// (<see cref="BuiltNavStore"/>). A readable file bound to this bsp wins. A
/// missing file, one that is not a navigation, or one built from another map
/// is a refusal naming the map and the file, and the answer carries the bsp
/// so the server can build one. A map carrying <c>ss_level_id</c> binds its
/// <c>.nav3d</c> by it (reader <c>MatchesMap</c>); any other, and every
/// <c>.navhull</c>, by the bsp's checksum and SHA-256 (<c>MatchesBsp</c>),
/// which also binds the server's build of a linked level. A <c>.navhull</c>
/// built without a width the game's numbers ask is refused, naming the file,
/// the width and the widths to build it with
/// (<see cref="NavHullNavSource.MissingWidth"/>). The graph is derived by the
/// game's <see cref="INavProfile"/> (the core's default movement without one)
/// and cached by both files' hashes, the reader version and, when they are
/// not the default's, the profile's numbers.
/// <para>
/// <b>Why the <c>.nav3d</c> first.</b> Where a map has both, the hull file
/// answers a leaf with a sloped brush in its record differently from the
/// <c>.nav3d</c> (it keeps one ceiling per width, the <c>.nav3d</c> tests the
/// body's box at the height asked), and on ctf_2fort the graph derived from
/// it lacks rows of the stairs and cuts each team's spawn from the far flag
/// (NavHullDifferentialTests). <see cref="Order"/> is that order, in one place.
/// </para>
/// </summary>
public static class NavLoader
{
    /// <summary>The kinds of file beside a map, in the order they are tried.</summary>
    public static IReadOnlyList<string> Order => [NavFile.Nav3d, NavFile.Hull];

    public static string NavPath(string map) => $"maps/{map}.nav3d";

    /// <summary>The map's hull navigation beside it.</summary>
    public static string HullPath(string map) => $"maps/{map}.navhull";

    /// <summary>The file of <paramref name="kind"/> beside the map.</summary>
    public static string PathOf(string kind, string map) => kind == NavFile.Hull ? HullPath(map) : NavPath(map);

    public static string BspPath(string map) => $"maps/{map}.bsp";

    public static NavLoadResult Load(string map, Func<string, byte[]?> read, NavGraphCache? cache, INavProfile? profile = null, BuiltNavStore? built = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        byte[]? first = read(PathOf(Order[0], map));
        byte[]? bsp = read(BspPath(map));
        if (bsp is null)
        {
            string? named = Beside(map, read, first).Select(f => f.Kind).FirstOrDefault();
            return named is null ? new NavLoadResult(null, $"no navigation for {map}: expected {NavPath(map)}", NavPath(map))
                                 : NavLoadResult.Refuse($"no {BspPath(map)} to bind {PathOf(named, map)} to");
        }

        string? refusal = null;
        foreach ((string kind, byte[] bytes) in Beside(map, read, first))
        {
            NavLoadResult shipped = Bind(map, kind, PathOf(kind, map), bytes, bsp, cache, profile, fromStore: false);
            if (shipped.Nav is not null)
                return shipped;
            refusal = refusal is null ? shipped.Refusal : $"{refusal}; {shipped.Refusal}";
        }

        bool none = refusal is null;
        refusal ??= $"no navigation for {map}: expected {NavPath(map)}";
        if (built is not null && built.TryRead(map, BuiltNavStore.Sha256(bsp)) is { } own)
        {
            NavLoadResult cached = Bind(map, NavFile.Nav3d, built.Name(map, BuiltNavStore.Sha256(bsp)), own, bsp, cache, profile, fromStore: true);
            if (cached.Nav is not null)
                return cached;
            refusal = $"{refusal}; {cached.Refusal}";
        }

        return new NavLoadResult(null, refusal, none ? NavPath(map) : null) { Bsp = bsp };
    }

    /// <summary>The map's room index beside it (<c>ai-replay navsplit rooms</c>).</summary>
    public static string RoomIndexPath(string map) => $"maps/{map}.navrooms";

    /// <summary>The map's room table beside it.</summary>
    public static string RoomTablePath(string map) => $"maps/{map}.navrooms.json";

    /// <summary>A room's own file beside the map.</summary>
    public static string RoomPath(string file) => $"maps/{file}";

    /// <summary>
    /// What a room's graph adds to its numbers' name in the cache: the hull numbering's, the rules
    /// the room was made by (<see cref="NavRoomTable.RulesRevision"/>), and the 4 by 4 merge.
    /// </summary>
    public static string RoomKey(string numbersKey) => $"{HullKey(numbersKey)}rooms{NavRoomTable.RulesRevision}k4";

    /// <summary>
    /// The level in rooms when it is one (plan_bots_teams_tf2_part_g R.2 item 1): a room index and table beside the
    /// map, the table naming a whole navigation over <paramref name="maxLeaves"/>. Null when it is not (the level
    /// loads as before), with <paramref name="refusal"/> null; null with why when the files are there but cannot
    /// be used (the caller says so and loads the level as one with none).
    /// </summary>
    public static NavRoomsLevel? Rooms(string map, Func<string, byte[]?> read, int maxLeaves, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(read);
        refusal = null;
        if (read(RoomTablePath(map)) is not { } tableBytes)
            return null;
        NavRoomTable table;
        try
        {
            table = NavRoomTable.FromJson(System.Text.Encoding.UTF8.GetString(tableBytes));
        }
        catch (InvalidDataException e)
        {
            refusal = $"{RoomTablePath(map)} cannot be read: {e.Message}";
            return null;
        }

        if (table.WholeLeaves <= maxLeaves)
            return null;
        if (read(RoomIndexPath(map)) is not { } indexBytes)
        {
            refusal = $"no navigation for {map}: {RoomTablePath(map)} has no {RoomIndexPath(map)} beside it";
            return null;
        }

        if (read(BspPath(map)) is not { } bsp)
        {
            refusal = $"no {BspPath(map)} to bind {RoomTablePath(map)} to";
            return null;
        }

        NavRoomIndex index;
        MapEntities entities;
        try
        {
            index = NavRoomIndex.Read(indexBytes);
            entities = MapEntities.Read(bsp);
        }
        catch (InvalidDataException e)
        {
            refusal = $"the rooms of {map} cannot be read: {e.Message}";
            return null;
        }

        if (table.Revision != NavRoomTable.RulesRevision)
        {
            refusal = $"{RoomTablePath(map)} was made by room rules {table.Revision}, this game's are {NavRoomTable.RulesRevision}: build it again (tools/ai-nav-build {map} --hull --rooms --force)";
            return null;
        }

        if (table.StartRoom is null)
        {
            refusal = $"{RoomTablePath(map)} has no room with a spawn";
            return null;
        }

        return new NavRoomsLevel(map, index, table, bsp, Convert.ToHexStringLower(SHA256.HashData(bsp)), entities);
    }

    /// <summary>
    /// The navigation files beside the map, in <see cref="Order"/>, each read only when the one
    /// before it did not bind (<paramref name="first"/> is the first kind's bytes, already read):
    /// a map whose <c>.nav3d</c> binds reads nothing more than it did before the hull file.
    /// </summary>
    private static IEnumerable<(string Kind, byte[] Bytes)> Beside(string map, Func<string, byte[]?> read, byte[]? first)
    {
        for (int i = 0; i < Order.Count; i++)
        {
            if ((i == 0 ? first : read(PathOf(Order[i], map))) is { } bytes)
                yield return (Order[i], bytes);
        }
    }

    /// <summary>
    /// Where a usable navigation for <paramref name="map"/> is, without deriving
    /// it: <c>shipped</c> (beside the map) or <c>cached</c> (the server's build),
    /// or null with <paramref name="refusal"/> saying why not and
    /// <paramref name="bsp"/> the map's bytes when there is a map to build from.
    /// </summary>
    public static string? Usable(string map, Func<string, byte[]?> read, BuiltNavStore? built, out string? refusal, out byte[]? bsp)
    {
        ArgumentNullException.ThrowIfNull(read);
        byte[]? first = read(PathOf(Order[0], map));
        bsp = read(BspPath(map));
        if (bsp is null)
        {
            string? named = Beside(map, read, first).Select(f => f.Kind).FirstOrDefault();
            refusal = named is null ? $"no navigation for {map}: expected {NavPath(map)}" : $"no {BspPath(map)} to bind {PathOf(named, map)} to";
            return null;
        }

        refusal = null;
        foreach ((string kind, byte[] bytes) in Beside(map, read, first))
        {
            string? why = Check(map, kind, PathOf(kind, map), bytes, bsp, out _, out _);
            if (why is null)
            {
                refusal = null;
                return "shipped";
            }

            refusal = refusal is null ? why : $"{refusal}; {why}";
        }

        refusal ??= $"no navigation for {map}: expected {NavPath(map)}";
        if (built is not null && built.TryRead(map, BuiltNavStore.Sha256(bsp)) is { } own)
        {
            string? why = Check(map, NavFile.Nav3d, built.Name(map, BuiltNavStore.Sha256(bsp)), own, bsp, out _, out _);
            if (why is null)
            {
                refusal = null;
                return "cached";
            }

            refusal = $"{refusal}; {why}";
        }

        return null;
    }

    /// <summary>
    /// Opens a navigation of <paramref name="kind"/> and binds it to the bsp: null, or why it cannot be this map's.
    /// Whether a hull navigation holds the game's widths is asked when it is bound for a game (<see cref="Bind"/>).
    /// </summary>
    private static string? Check(string map, string kind, string navName, byte[] nav, byte[] bsp, out NavFile? file, out MapEntities? entities)
    {
        file = null;
        entities = null;
        Nav3dReader? reader = null;
        NavHullReader? hull = null;
        try
        {
            if (kind == NavFile.Hull)
                hull = NavHullReader.Open(nav);
            else
                reader = Nav3dReader.Open(nav);
            entities = MapEntities.Read(bsp);
        }
        catch (InvalidDataException e)
        {
            return $"{navName} for {map} cannot be read: {e.Message}";
        }

        if (hull is not null)
        {
            // A hull navigation names no level: it binds by the map file alone.
            if (!hull.MatchesBsp(bsp))
                return $"{navName} was built from another map: it {Named(hull.Bsp)}, not {BspPath(map)} (checksum {Nav3dBspBinding.Of(bsp).MapChecksum})";
            file = NavFile.Of(hull, navName);
            return null;
        }

        string? levelId = entities.LevelId;
        if (!string.IsNullOrEmpty(levelId) && !reader!.MatchesMap(levelId) && !reader.MatchesBsp(bsp))
            return $"{navName} was built for another level: it names {reader.LevelId}, {map}'s ss_level_id is {levelId}";
        if (string.IsNullOrEmpty(levelId) && !reader!.MatchesBsp(bsp))
            return $"{navName} was built from another map: it {Named(reader.Bsp)}, not {BspPath(map)} (checksum {Nav3dBspBinding.Of(bsp).MapChecksum})";

        file = NavFile.Of(reader!, navName);
        return null;
    }

    private static string Named(Nav3dBspBinding? binding) => binding is { } b ? $"names map checksum {b.MapChecksum}" : "names no map";

    /// <summary>
    /// What a graph derived from a hull navigation adds to its numbers' name in the cache: the
    /// order <see cref="NavHullNavSource"/> numbers a box's columns in is what a cached graph's
    /// leaves mean, so another numbering is another name.
    /// </summary>
    public static string HullKey(string numbersKey) => $"{numbersKey}hull{NavHullNavSource.Revision}";

    private static NavLoadResult Bind(string map, string kind, string navName, byte[] nav, byte[] bsp, NavGraphCache? cache, INavProfile? profile, bool fromStore)
    {
        if (Check(map, kind, navName, nav, bsp, out NavFile? file, out MapEntities? entities) is { } why)
            return NavLoadResult.Refuse(why);

        NavNumbers numbers = profile?.Numbers(file!.JumpHeight) ?? NavNumbers.Stock(file!.JumpHeight);
        string key = NavGraphCache.NumbersKey(numbers, file.JumpHeight);
        if (file.Source is NavHullNavSource hull)
        {
            if (NavHullNavSource.MissingWidth(hull.Reader, numbers, navName) is { } missing)
                return NavLoadResult.Refuse(missing);
            key = HullKey(key);
        }

        string bspSha = Convert.ToHexStringLower(SHA256.HashData(bsp));
        string navSha = Convert.ToHexStringLower(SHA256.HashData(nav));
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        DerivedNav? derived = cache?.TryRead(map, bspSha, navSha, key);
        bool fromCache = derived is not null;
        if (derived is null)
        {
            derived = NavGraphFromNav3d.Derive(file.Source, numbers, NavEdgeCosts.Default, entities!.Hurt, entities.LadderClimbs);
            cache?.Write(map, bspSha, navSha, derived, key);
        }

        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new NavLoadResult(new LoadedNav(map, file, derived, entities!, bspSha, navSha, fromCache, ms, key, fromStore), null);
    }
}
