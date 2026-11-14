using System.Globalization;
using System.Text.Json;
using SourceSharp.Host.Contracts;
using SourceSharp.Tests;
using TF2.Descent.Server.Level;
using Xunit;

namespace TF2.Descent.Tests.Level;

/// <summary>
/// A real linked level's files: the committed depth 1 seed 1 fixture (Fixtures/README.md), or a
/// level this tree built with <c>make descent-level</c> (bin/&lt;config&gt;/descent-rooms), which a
/// fact asks for through <see cref="BuiltLevelFactAttribute"/>.
/// </summary>
public sealed record LevelFixture(string Map, int Depth, ulong Seed, byte[] Nav3d, byte[] Map2d, string LevelYaml, string LibraryJson)
{
    public static LevelFixture Committed()
    {
        string dir = RepoLayout.Repo("src", "tf2_descent", "tests", "Level", "Fixtures");
        return new LevelFixture("tf2_descent-1-s1", 1, 1,
            File.ReadAllBytes(Path.Combine(dir, "tf2_descent-1-s1.nav3d")),
            File.ReadAllBytes(Path.Combine(dir, "tf2_descent-1-s1.map2d")),
            File.ReadAllText(Path.Combine(dir, "tf2_descent-1-s1.yaml")),
            File.ReadAllText(Path.Combine(dir, "descent_d01.library.json")));
    }

    /// <summary>The directory <c>make descent-rooms DEPTH=depth</c> writes.</summary>
    public static string BuiltDirectory(int depth) =>
        RepoLayout.Repo("bin", RepoLayout.Config, "descent-rooms", "d" + depth.ToString("00", CultureInfo.InvariantCulture));

    public static string BuiltMap(int depth, ulong seed) => string.Create(CultureInfo.InvariantCulture, $"tf2_descent-{depth}-s{seed}");

    public static LevelFixture Built(int depth, ulong seed)
    {
        string dir = BuiltDirectory(depth), map = BuiltMap(depth, seed);
        return new LevelFixture(map, depth, seed,
            File.ReadAllBytes(Path.Combine(dir, "maps", map + ".nav3d")),
            File.ReadAllBytes(Path.Combine(dir, "maps", map + ".map2d")),
            File.ReadAllText(Path.Combine(dir, map + ".yaml")),
            File.ReadAllText(Path.Combine(dir, "library.json")));
    }

    public LevelFiles Files(bool nav3d = true, bool map2d = true) =>
        new(Map, Depth, Seed, "map name", nav3d ? Nav3d : null, map2d ? Map2d : null);

    public LevelData Read() => LevelReader.Read(Files());

    /// <summary>The generator's own count of the level's rooms and joints: the level file's second comment line.</summary>
    public (int Rooms, int Joints) GeneratorCounts()
    {
        string line = LevelYaml.Split('\n').Single(l => l.StartsWith("# rooms ", StringComparison.Ordinal));
        string[] parts = line[2..].Split(", ");
        return (int.Parse(parts[0]["rooms ".Length..], CultureInfo.InvariantCulture), int.Parse(parts[1]["joints ".Length..], CultureInfo.InvariantCulture));
    }

    /// <summary>Each library room's marker kinds, as its <c>library.json</c> entry lists them.</summary>
    public IReadOnlyDictionary<string, string[]> LibraryMarkers()
    {
        using var doc = JsonDocument.Parse(LibraryJson);
        return doc.RootElement.GetProperty("rooms").EnumerateObject()
            .ToDictionary(r => r.Name, r => r.Value.GetProperty("markers").EnumerateArray().Select(m => m.GetString()!).ToArray());
    }

    /// <summary>The library's rooms as the layout takes them (src/mapgen/Descent/DescentLevel.ReadManifest's rules).</summary>
    public IReadOnlyList<RoomInfo> LibraryRooms()
    {
        using var doc = JsonDocument.Parse(LibraryJson);
        var rooms = new List<RoomInfo>();
        foreach (JsonProperty room in doc.RootElement.GetProperty("rooms").EnumerateObject())
        {
            JsonElement e = room.Value;
            string[] tags = [.. e.GetProperty("tags").EnumerateArray().Select(x => x.GetString() ?? "")];
            int[] depths = [.. e.GetProperty("depths").EnumerateArray().Select(x => x.GetInt32())];
            rooms.Add(new RoomInfo(room.Name, tags, e.GetProperty("weight").GetInt32(), depths[0], depths.Length > 1 ? depths[1] : depths[0]));
        }
        return [.. rooms.OrderBy(r => r.Name, StringComparer.Ordinal)];
    }
}

/// <summary>A fact over a level this tree built; skipped, with the command to build it, when the tree has none (per-tree build output, CLAUDE.md).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BuiltLevelFactAttribute : FactAttribute
{
    public BuiltLevelFactAttribute(int depth, int seed)
    {
        string map = LevelFixture.BuiltMap(depth, (ulong)seed);
        string nav = Path.Combine(LevelFixture.BuiltDirectory(depth), "maps", map + ".nav3d");
        if (!File.Exists(nav))
            Skip = string.Create(CultureInfo.InvariantCulture,
                $"no {nav}: build it with make descent-rooms DEPTH={depth} && make descent-level DEPTH={depth} SEED={seed}");
    }
}
