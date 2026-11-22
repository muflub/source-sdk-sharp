using System.Globalization;
using TF2.Descent.Rules.Layout;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// What <c>MapGen descent-rooms</c> writes (task A13): one depth's room
/// library (plan E2), a VMF of the catalogue's families on the normal kit in
/// its theme, and its <c>library.json</c>. A normal depth takes the mine's
/// catalogue; a boss depth (5, 10, 15) only the rooms of a boss level's row,
/// its arena a room of 2x2, 3x3 or 4x4 cells (§3.2). A library that
/// does not lint clean is refused. <c>descent-level</c> lays levels out of it.
/// </summary>
public static class DescentRooms
{
    /// <summary>The library of a depth: its name, and its VMF's stem.</summary>
    public static string LibraryName(int depth) => string.Create(CultureInfo.InvariantCulture, $"descent_d{Check(depth):00}");

    /// <summary>
    /// The kit a depth's rooms are built on (§3.1), cut to its theme's door frame: the normal kit at
    /// every depth, the boss levels' arenas being rooms larger than one cell on it (§3.2, M24).
    /// </summary>
    public static RoomKit KitFor(int depth) => RoomKit.For(RoomKit.Normal.Name, 1024, ThemeFor(Check(depth)).DoorFrame, RoomKit.Normal.NavVoxel);

    /// <summary>The theme a depth is drawn in (§7, task A9): Act I's at depths 1 to 4, Phase 1's grey below them for now.</summary>
    public static RoomTheme ThemeFor(int depth) => RoomTheme.ForDepth(Check(depth));

    /// <summary>The families a depth's library is made of.</summary>
    public static IReadOnlyList<RoomShape> ShapesFor(int depth) =>
        DepthTable.For(Check(depth)).IsBoss ? RoomCatalogue.Boss : RoomCatalogue.Mine;

    /// <summary>The rooms of a depth's library for a seed: its families' rooms whose depth range holds the depth, dressed in its theme.</summary>
    public static IReadOnlyList<Room> Rooms(int depth, ulong seed) => [.. Dressed(depth, seed).Select(r => r.Dressed)];

    /// <summary>
    /// A depth's rooms as the shapes built them, undressed: its families' rooms whose depth range holds
    /// the depth, and the quiet twins the layout puts beside the way in
    /// (<see cref="RoomCatalogue.QuietTwins"/>): at a boss depth the drift's alone, the lobby before the antechamber.
    /// </summary>
    public static IReadOnlyList<Room> Built(int depth, ulong seed)
    {
        var rooms = RoomCatalogue.Rooms(ShapesFor(depth), KitFor(depth), ThemeFor(depth), seed)
            .Where(r => depth >= r.Entry.MinDepth && depth <= r.Entry.MaxDepth).ToList();
        if (!DepthTable.For(Check(depth)).IsBoss)
            rooms.AddRange(RoomCatalogue.QuietTwins(rooms));
        else
            // A boss level's lobby between its way in and its antechamber: the quiet drift, at this depth.
            rooms.AddRange(RoomCatalogue.QuietTwins(RoomCatalogue.Rooms(RoomCatalogue.Mine.Where(s => s.Family == "drift"), KitFor(depth), ThemeFor(depth), seed))
                .Select(r => r with { Entry = r.Entry with { MinDepth = depth, MaxDepth = depth } }));
        return rooms;
    }

    /// <summary>
    /// Each room of a depth's library as the shapes built it, as dressed
    /// (<see cref="Dressing.RoomDresser"/>), and the theme it is dressed in:
    /// the depth's, or for a way in (<c>room_role up</c>) the theme's
    /// <see cref="RoomTheme.Arrival"/>, redrawn in it first.
    /// </summary>
    public static IReadOnlyList<(Room Built, Room Dressed, RoomTheme Theme)> Dressed(int depth, ulong seed)
    {
        RoomTheme theme = ThemeFor(depth);
        var rooms = new List<(Room, Room, RoomTheme)>();
        foreach (Room room in Built(depth, seed))
        {
            var (built, use) = room.Role == RoomRole.Up && theme.Arrival is { } arrival ? (theme.Redraw(room, arrival), arrival) : (room, theme);
            rooms.Add((built, Dressing.RoomDresser.Dress(built, use, seed), use));
        }

        return rooms;
    }

    /// <summary>The library of a depth's rooms: its kit, and its theme's fog, tone map, sky and sun (the library's singletons).</summary>
    public static RoomLibrary Library(int depth, IEnumerable<Room> rooms)
    {
        RoomTheme theme = ThemeFor(depth);
        var library = new RoomLibrary(KitFor(depth), sky: theme.Sky) { Fog = theme.Fog, ToneMap = theme.ToneMap };
        if (theme.Sun is { } sun)
            library.WithSun(sun);
        foreach (Room room in rooms)
            library.Add(room);
        return library;
    }

    /// <summary>The mod folder the themes' own materials are committed in, beneath which they are <c>materials/...</c>.</summary>
    public const string ModContent = "game/mod_tf2_descent";

    /// <summary>
    /// Writes every theme's own materials (<see cref="RoomTheme.OwnMaterials"/>)
    /// under <paramref name="modDir"/>/materials; returns the files written. The
    /// mod's copies in <see cref="ModContent"/> are this output, committed, so
    /// they reach the game and the compile through the mod's game directory.
    /// </summary>
    public static IReadOnlyList<string> WriteMaterials(string modDir)
    {
        var written = new List<string>();
        foreach (var (path, text) in RoomTheme.All.SelectMany(t => t.OwnMaterials).Distinct())
        {
            string file = Path.Combine(modDir, "materials", path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
            written.Add(file);
        }

        return written;
    }

    /// <summary>Writes a depth's library and its manifest into <paramref name="dir"/>; returns the files written.</summary>
    /// <exception cref="InvalidOperationException">A room has lint findings, listed in the message.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The depth is not 1 to 15.</exception>
    public static IReadOnlyList<string> Write(string dir, int depth, ulong seed)
    {
        var dressed = Dressed(depth, seed);
        var rooms = dressed.Select(r => r.Dressed).ToArray();
        var findings = rooms.SelectMany(RoomLint.Check)
            .Concat(dressed.SelectMany(r => Dressing.DressLint.Check(r.Built, r.Dressed, r.Theme))).ToArray();
        if (findings.Length > 0)
            throw new InvalidOperationException("the library does not lint clean:\n  " + string.Join("\n  ", findings.Select(f => f.ToString())));

        Directory.CreateDirectory(dir);
        var written = new List<string>();
        void Put(string name, string text)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, text);
            written.Add(path);
        }

        string library = LibraryName(depth);
        Put(library + ".vmf", Library(depth, rooms).Build().Write());
        Put("library.json", LibraryManifestWriter.Write(library, rooms));
        return written;
    }

    private static int Check(int depth) =>
        depth is >= 1 and <= LibraryManifestWriter.Depths ? depth
            : throw new ArgumentOutOfRangeException(nameof(depth), depth, "a depth is 1 to 15");
}
