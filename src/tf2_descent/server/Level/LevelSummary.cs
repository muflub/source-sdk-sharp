using System.Globalization;
using System.Text;
using SourceSharp.Abi;

namespace TF2.Descent.Server.Level;

/// <summary>The level's lines in the console: the load line and the dev command's listing.</summary>
public static class LevelSummary
{
    private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>A position as the console prints one: <c>(x y z)</c>.</summary>
    public static string At(Vector v) => $"({F(v.X)} {F(v.Y)} {F(v.Z)})";

    private static string Count(int n, string one, string many) => n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);

    /// <summary>
    /// <c>[TF2Descent/Level] &lt;map&gt;: depth D seed S: R rooms (N derived), K doors, M markers (by kind),
    /// A arrival points, exit at ..., missing: ...</c>. A piece that was derived says so beside its count;
    /// <c>missing</c> lists every absent piece, or <c>none</c>.
    /// </summary>
    public static string LoadLine(LevelData level, TimeSpan took)
    {
        var line = new StringBuilder("[TF2Descent/Level] ");
        line.Append(level.Map).Append(": depth ").Append(level.Depth.ToString(CultureInfo.InvariantCulture))
            .Append(" seed ").Append(level.Seed is { } seed ? seed.ToString(CultureInfo.InvariantCulture) + " (" + level.SeedFrom + ")" : "none")
            .Append(": ").Append(Count(level.Rooms.Length, "room", "rooms"))
            .Append(" (").Append((level.Source(LevelPiece.Rooms) == PieceSource.Derived ? level.Rooms.Length : 0).ToString(CultureInfo.InvariantCulture))
            .Append(" derived), ").Append(Count(level.Doors.Length, "door", "doors"));
        if (level.Source(LevelPiece.Doors) == PieceSource.Derived)
            line.Append(" (derived)");
        line.Append(", ").Append(Count(level.Markers.Length, "marker", "markers"));
        var kinds = level.Markers.GroupBy(m => m.Kind).OrderBy(g => g.Key)
            .Select(g => $"{g.Key.ToString().ToLowerInvariant()} {g.Count().ToString(CultureInfo.InvariantCulture)}").ToList();
        if (kinds.Count > 0)
            line.Append(" (").Append(string.Join(", ", kinds)).Append(')');
        line.Append(", ").Append(Count(level.Arrivals.Length, "arrival point", "arrival points"));
        if (level.Exit is { } exit)
            line.Append(", exit at ").Append(At(exit.Position)).Append(" in room ").Append(exit.Room.ToString(CultureInfo.InvariantCulture))
                .Append(" to ").Append(exit.Map).Append(level.Source(LevelPiece.Exit) == PieceSource.Derived ? " (derived)" : "");
        else
            line.Append(", no exit");
        if (level.BossArena is int arena)
            line.Append(", boss arena room ").Append(arena.ToString(CultureInfo.InvariantCulture));
        if (level.Arena is { } layout)
            line.Append(", arena layout: boss at ").Append(At(layout.Spot)).Append(" yaw ").Append(F(layout.SpotYaw)).Append(", ")
                .Append(Count(layout.Track.Length, "track point", "track points")).Append(" (")
                .Append(layout.Stations.ToString(CultureInfo.InvariantCulture)).Append(" stations), ")
                .Append(Count(layout.Mouths.Length, "mouth", "mouths"));
        var missing = level.Sources.Where(s => s.Value == PieceSource.Absent).Select(s => s.Key.ToString().ToLowerInvariant()).Order(StringComparer.Ordinal).ToList();
        line.Append(", missing: ").Append(missing.Count == 0 ? "none" : string.Join(", ", missing));
        line.Append(" (read in ").Append(((int)took.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append(" ms)\n");
        return line.ToString();
    }

    /// <summary>The dev command's listing: every room, door, marker and arrival point, a line each.</summary>
    public static IEnumerable<string> Listing(LevelData level)
    {
        yield return LoadLine(level, TimeSpan.Zero);
        foreach (LevelRoom r in level.Rooms)
            yield return string.Create(CultureInfo.InvariantCulture,
                $"room {r.Id} {r.Name} '{r.Label}' {r.Role}{(r.Low ? " low" : "")}{(r.Jump ? " jump" : "")}: cells ({r.Column},{r.Row}) {r.CellsX}x{r.CellsY} turn {r.Rotation}, bounds {At(r.Min)}-{At(r.Max)}, neighbours [{string.Join(' ', level.Neighbours(r.Id))}]\n");
        foreach (LevelDoor d in level.Doors)
            yield return string.Create(CultureInfo.InvariantCulture,
                $"door {d.Id} room {d.RoomA} -> {d.RoomB} at {At(d.Position)} normal {At(d.Normal)} {F(d.Width)} by {F(d.Height)} ({d.Socket})\n");
        foreach (LevelMarker m in level.Markers)
            yield return string.Create(CultureInfo.InvariantCulture,
                $"marker {m.Id} {m.PoiType} at {At(m.Position)} yaw {F(m.Yaw)} room {m.Room}{(m.Label.Length > 0 ? " '" + m.Label + "'" : "")}\n");
        for (int i = 0; i < level.Arrivals.Length; i++)
        {
            ArrivalPoint a = level.Arrivals[i];
            yield return string.Create(CultureInfo.InvariantCulture, $"arrival {i} at {At(a.Position)} yaw {F(a.Yaw)} room {a.Room} ({a.Source})\n");
        }
    }
}
