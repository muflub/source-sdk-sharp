using System.Globalization;
using TF2.Descent.Rules.Layout;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// What <c>MapGen descent-rooms</c> writes (task A13): one depth's room
/// library (plan E2), a VMF of the catalogue's families on the depth's kit in
/// its theme, and its <c>library.json</c>. A normal depth takes the grey
/// catalogue on the normal kit; a boss depth (5, 10, 15) its own kit (§3.2:
/// 2048, 4096, 3072) and only the rooms of a boss level's row. A library that
/// does not lint clean is refused. <c>descent-level</c> lays levels out of it.
/// </summary>
public static class DescentRooms
{
    /// <summary>The library of a depth: its name, and its VMF's stem.</summary>
    public static string LibraryName(int depth) => string.Create(CultureInfo.InvariantCulture, $"descent_d{Check(depth):00}");

    /// <summary>The kit a depth's rooms are built on (§3.1, §3.2), cut to its theme's door frame.</summary>
    public static RoomKit KitFor(int depth) => Check(depth) switch
    {
        5 => RoomKit.For(RoomKit.Boss2048.Name, 2048, ThemeFor(depth).DoorFrame, RoomKit.Boss2048.NavVoxel),
        10 => RoomKit.For(RoomKit.Boss4096.Name, 4096, ThemeFor(depth).DoorFrame, RoomKit.Boss4096.NavVoxel),
        15 => RoomKit.For(RoomKit.Boss3072.Name, 3072, ThemeFor(depth).DoorFrame, RoomKit.Boss3072.NavVoxel),
        _ => RoomKit.For(RoomKit.Normal.Name, 1024, ThemeFor(depth).DoorFrame, RoomKit.Normal.NavVoxel),
    };

    /// <summary>The theme a depth is drawn in: Phase 1's grey at every depth.</summary>
    public static RoomTheme ThemeFor(int depth)
    {
        Check(depth);
        return RoomTheme.Grey;
    }

    /// <summary>The families a depth's library is made of.</summary>
    public static IReadOnlyList<RoomShape> ShapesFor(int depth) =>
        DepthTable.For(Check(depth)).IsBoss ? RoomCatalogue.Boss : RoomCatalogue.Grey;

    /// <summary>The rooms of a depth's library for a seed: its families' rooms whose depth range holds the depth.</summary>
    public static IReadOnlyList<Room> Rooms(int depth, ulong seed) =>
        [.. RoomCatalogue.Rooms(ShapesFor(depth), KitFor(depth), ThemeFor(depth), seed)
            .Where(r => depth >= r.Entry.MinDepth && depth <= r.Entry.MaxDepth)];

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
        var rooms = Rooms(depth, seed);
        var findings = rooms.SelectMany(RoomLint.Check).ToArray();
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
        Put(library + ".vmf", RoomCatalogue.Library(KitFor(depth), rooms).Build().Write());
        Put("library.json", LibraryManifestWriter.Write(library, rooms));
        return written;
    }

    private static int Check(int depth) =>
        depth is >= 1 and <= LibraryManifestWriter.Depths ? depth
            : throw new ArgumentOutOfRangeException(nameof(depth), depth, "a depth is 1 to 15");
}
