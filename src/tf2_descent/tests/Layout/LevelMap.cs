using System.Text;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// A plan as an ASCII map, north at the top: one character a room, <c>-</c> and <c>|</c> for
/// joined doors. A arrival, Z exit, $ treasure, ! shrine stub; a hallway o, a chamber c, a hall h,
/// a shaft s, upper case on the critical path; a space is an empty cell.
/// </summary>
internal static class LevelMap
{
    public static string Render(LevelPlan plan, IEnumerable<GridCell> path, IReadOnlyList<RoomInfo> catalogue)
    {
        var byName = catalogue.ToDictionary(r => r.Name, StringComparer.Ordinal);
        var onPath = path.ToHashSet();
        var at = plan.Placements.ToDictionary(p => (p.Row, p.Column));
        var sides = plan.Placements.ToDictionary(p => (p.Row, p.Column), p => DoorSets.Rotate(RoomName.Parse(p.Room).Doors, p.Rotation));
        bool Joined((int, int) a, Doors side, (int, int) b) =>
            sides.TryGetValue(a, out var da) && sides.TryGetValue(b, out var db) && (da & side) != 0 && (db & DoorSets.Opposite(side)) != 0;

        var text = new StringBuilder();
        for (var row = plan.Rows - 1; row >= 0; row--)
        {
            var line = new StringBuilder();
            for (var column = 0; column < plan.Columns; column++)
            {
                line.Append(at.TryGetValue((row, column), out var p) ? Glyph(p, byName[p.Room], onPath.Contains(new GridCell(row, column))) : ' ');
                if (column < plan.Columns - 1) line.Append(Joined((row, column), Doors.East, (row, column + 1)) ? '-' : ' ');
            }
            text.Append(line.ToString().TrimEnd()).Append('\n');
            if (row == 0) break;
            var joins = new StringBuilder();
            for (var column = 0; column < plan.Columns; column++)
                joins.Append(Joined((row, column), Doors.South, (row - 1, column)) ? '|' : ' ').Append(' ');
            text.Append(joins.ToString().TrimEnd()).Append('\n');
        }
        return text.ToString();
    }

    private static char Glyph(Placement p, RoomInfo info, bool onPath)
    {
        if (p.Role == "up") return 'A';
        if (p.Role == "down") return 'Z';
        if (info.Tags.Contains("treasure")) return '$';
        if (info.Tags.Contains("shrine")) return '!';
        var shape = RoomName.Parse(p.Room);
        var hallway = info.Tags.Contains("corridor") || info.Tags.Contains("junction") || shape.Height == HeightClass.Low;
        var c = hallway ? 'o' : shape.Height switch { HeightClass.Chamber => 'c', HeightClass.Hall => 'h', _ => 's' };
        return onPath ? char.ToUpperInvariant(c) : c;
    }
}
