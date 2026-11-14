using System.Globalization;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// What <c>MapGen descent-rooms</c> writes (task A13): a room library VMF of
/// the catalogue's families, its <c>library.json</c>, and a sample level per
/// depth asked for, each a small grid of the library's rooms that
/// <c>ssmap link</c> accepts. A library that does not lint clean is refused.
/// </summary>
public static class DescentRooms
{
    /// <summary>The library's name, and its VMF's stem.</summary>
    public const string Library = "descent_demo";

    /// <summary>The sample level's file for a depth.</summary>
    public static string LevelFile(int depth) => string.Create(CultureInfo.InvariantCulture, $"{Library}_d{depth:00}.yaml");

    /// <summary>The library's rooms for a seed: Phase 1's demonstration families, grey.</summary>
    public static IReadOnlyList<Room> Rooms(ulong seed) =>
        RoomCatalogue.Rooms(RoomCatalogue.Demonstration, RoomKit.Normal, RoomTheme.Grey, seed);

    /// <summary>
    /// A sample level: a hall, a drift and the three-door hall in a row, and
    /// a drift turned north off the last, so every joint is a framed doorway
    /// and the rest are caps. Odd depths run west to east, even ones east to
    /// west (the whole grid turned half round).
    /// </summary>
    public static LevelGrid Level(int depth)
    {
        IReadOnlyList<IReadOnlyList<string?>> rows = depth % 2 == 1
            ? [[null, null, "drift_ew@90"], ["hall_we", "drift_ew", "hall_wen"]]
            : [["hall_wen@180", "drift_ew@180", "hall_we@180"], ["drift_ew@270", null, null]];
        return new LevelGrid(Library + ".vmf", rows);
    }

    /// <summary>Writes the library, its manifest and the levels into <paramref name="dir"/>; returns the files written.</summary>
    /// <exception cref="InvalidOperationException">A room has lint findings, listed in the message.</exception>
    public static IReadOnlyList<string> Write(string dir, ulong seed, IEnumerable<int> depths)
    {
        var rooms = Rooms(seed);
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

        Put(Library + ".vmf", RoomCatalogue.Library(RoomKit.Normal, rooms).Build().Write());
        Put("library.json", LibraryManifestWriter.Write(Library, rooms));
        foreach (int depth in depths)
        {
            if (depth is < 1 or > LibraryManifestWriter.Depths)
                throw new ArgumentOutOfRangeException(nameof(depths), depth, "a depth is 1 to 15");
            Put(LevelFile(depth), Level(depth).Write());
        }

        return written;
    }
}
