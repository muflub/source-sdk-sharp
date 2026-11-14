using System.Globalization;
using System.Text;
using System.Text.Json;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// The transition keys of a level: the map above (the town for depth 1) and
/// the map below, or null for none.
/// </summary>
public sealed record LevelKeys(string? UpMap, string? DownMap)
{
    /// <summary>The town map the cage at the shaft's head is in.</summary>
    public const string TownMap = "tf2_descent_town";

    /// <summary>The mod's name, which names its levels <c>&lt;mod&gt;-&lt;depth&gt;</c>.</summary>
    public const string Mod = "tf2_descent";

    /// <summary>The keys the host gives a level of <paramref name="depth"/>: up to the town or the level above, down to the level below but at the bottom.</summary>
    public static LevelKeys ForDepth(int depth) => new(
        depth == 1 ? TownMap : string.Create(CultureInfo.InvariantCulture, $"{Mod}-{depth - 1}"),
        depth < DepthTable.Depths ? string.Create(CultureInfo.InvariantCulture, $"{Mod}-{depth + 1}") : null);
}

/// <summary>
/// A Descent level from the real layout (plan task C1 through the tools): a
/// library's <c>library.json</c> read as the host reads it, laid out by the
/// rules module's <see cref="LevelLayout"/>, and written as the level file the
/// host writes for <c>ssmap link</c>.
/// </summary>
public static class DescentLevel
{
    /// <summary>The largest grid a level file may hold, in cells (the tools' guard).</summary>
    public const int MaxCells = 4096;

    /// <summary>
    /// The rooms of a <c>library.json</c> as the layout takes them: name, tags,
    /// weight and depth range, in name order (the host's
    /// <c>LibraryManifest.RoomInfos</c>; a missing weight is 1, missing depths
    /// [1, 99], one depth both ends).
    /// </summary>
    /// <exception cref="InvalidDataException">The text is not a manifest.</exception>
    public static (string Library, IReadOnlyList<RoomInfo> Rooms) ReadManifest(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"library.json: {e.Message}", e);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            string library = root.TryGetProperty("library", out JsonElement l) ? l.GetString() ?? "" : "";
            var rooms = new List<RoomInfo>();
            if (root.TryGetProperty("rooms", out JsonElement all))
                foreach (JsonProperty room in all.EnumerateObject())
                {
                    JsonElement e = room.Value;
                    string[] tags = e.TryGetProperty("tags", out JsonElement t) ? [.. t.EnumerateArray().Select(x => x.GetString() ?? "")] : [];
                    int weight = e.TryGetProperty("weight", out JsonElement w) ? w.GetInt32() : 1;
                    int[] depths = e.TryGetProperty("depths", out JsonElement d) ? [.. d.EnumerateArray().Select(x => x.GetInt32())] : [1, 99];
                    int min = depths.Length > 0 ? depths[0] : 1;
                    int max = depths.Length > 1 ? depths[1] : min;
                    rooms.Add(new RoomInfo(room.Name, tags, weight, min, max));
                }

            return (library, [.. rooms.OrderBy(r => r.Name, StringComparer.Ordinal)]);
        }
    }

    /// <summary>The library's VMF as a level file names it.</summary>
    public static string LibraryFile(string library) => library + ".vmf";

    /// <summary>
    /// A plan as a level file, byte for byte what the host writes
    /// (<c>TF2.Descent.MapForge.LevelFileWriter.Write</c> through the tools'
    /// <c>LevelYaml.Write</c>): row 0 is the south row and is written last,
    /// column 0 the west; a placement's rotation is counter-clockwise from
    /// above, written <c>room@degrees</c> when not 0; an empty cell is <c>~</c>;
    /// each column is padded to its widest cell.
    /// </summary>
    /// <exception cref="ArgumentException">A plan the format cannot hold.</exception>
    /// <summary>The token of a cell a larger room covers besides its anchor.</summary>
    public const string CoveredCell = "+";

    public static string Write(LevelPlan plan, string library, LevelKeys? keys, IEnumerable<string>? comments = null)
    {
        if (plan.Rows < 1 || plan.Columns < 1 || (long)plan.Rows * plan.Columns > MaxCells)
            throw new ArgumentException($"a {plan.Rows}x{plan.Columns} plan is not a level grid (1 to {MaxCells} cells)");
        var cells = new (string Room, int Turns)?[plan.Rows * plan.Columns];
        foreach (Placement p in plan.Placements)
        {
            if (p.Row < 0 || p.Row >= plan.Rows || p.Column < 0 || p.Column >= plan.Columns)
                throw new ArgumentException($"placement of '{p.Room}' at row {p.Row}, column {p.Column} is outside the {plan.Rows}x{plan.Columns} grid");
            if (p.Rotation % 90 != 0)
                throw new ArgumentException($"placement of '{p.Room}' turns {p.Rotation} degrees; a level turns rooms by quarter turns");
            ref var cell = ref cells[p.Row * plan.Columns + p.Column];
            if (cell is { } taken)
                throw new ArgumentException($"row {p.Row}, column {p.Column} holds '{taken.Room}' and '{p.Room}'");
            cell = (p.Room, ((p.Rotation / 90) % 4 + 4) % 4);
        }

        // A room larger than one cell (M24) is written in its anchor, the south-west cell as placed;
        // every other cell it covers holds '+' (map tools' level file).
        foreach (Placement p in plan.Placements)
        {
            if (!RoomName.TryParse(p.Room, out var shape, out _) || !shape!.IsMultiCell)
                continue;
            bool turned = (p.Rotation / 90) % 2 != 0;
            int w = turned ? shape.CellsY : shape.CellsX, d = turned ? shape.CellsX : shape.CellsY;
            for (int r = 0; r < d; r++)
                for (int x = 0; x < w; x++)
                {
                    if (r == 0 && x == 0)
                        continue;
                    int row = p.Row + r, column = p.Column + x;
                    if (row >= plan.Rows || column >= plan.Columns)
                        throw new ArgumentException($"'{p.Room}' at row {p.Row}, column {p.Column} covers ({row}, {column}), outside the {plan.Rows}x{plan.Columns} grid");
                    ref var covered = ref cells[row * plan.Columns + column];
                    if (covered is { } taken)
                        throw new ArgumentException($"'{p.Room}' covers row {row}, column {column}, which holds '{taken.Room}'");
                    covered = (CoveredCell, 0);
                }
        }

        var text = new StringBuilder();
        foreach (string comment in comments ?? [])
            text.Append("# ").Append(comment).Append('\n');
        text.Append("library: ").Append(Quote(LibraryFile(library))).Append('\n');
        text.Append("rows: ").Append(plan.Rows.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("columns: ").Append(plan.Columns.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (keys is not null)
        {
            if (keys.UpMap is null)
                text.Append("up: none\n");
            if (keys.DownMap is null)
                text.Append("down: none\n");
            if (keys.UpMap is { } up)
                text.Append("up_map: ").Append(Quote(up)).Append('\n');
            if (keys.DownMap is { } down)
                text.Append("down_map: ").Append(Quote(down)).Append('\n');
        }

        text.Append("grid:\n");
        var tokens = new string[plan.Columns, plan.Rows];
        var width = new int[plan.Columns];
        for (int y = 0; y < plan.Rows; y++)
            for (int x = 0; x < plan.Columns; x++)
            {
                var cell = cells[y * plan.Columns + x];
                string token = cell is not { } c ? "~"
                    : c.Turns == 0 ? c.Room
                    : string.Create(CultureInfo.InvariantCulture, $"{c.Room}@{c.Turns * 90}");
                tokens[x, y] = token;
                width[x] = Math.Max(width[x], token.Length);
            }

        for (int y = plan.Rows - 1; y >= 0; y--)
        {
            text.Append("  - [");
            for (int x = 0; x < plan.Columns; x++)
            {
                if (x == plan.Columns - 1)
                    text.Append(tokens[x, y]);
                else
                    text.Append(tokens[x, y]).Append(',').Append(' ', width[x] - tokens[x, y].Length + 1);
            }

            text.Append("]\n");
        }

        return text.ToString();
    }

    /// <summary>A scalar as the level file writes it: plain when it may be, else double-quoted with escapes.</summary>
    private static string Quote(string value)
    {
        bool plain = value.Length > 0 && value[0] is not ('-' or '?' or ':' or '@' or '`' or '\'' or '"' or '&' or '*' or '!' or '|' or '>' or '%' or '#' or '[' or ']' or '{' or '}' or ',' or ' ');
        foreach (char c in value)
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or '\\' or '@' or '+'))
                plain = false;
        if (plain && value[^1] != ' ')
            return value;

        var quoted = new StringBuilder("\"");
        foreach (char c in value)
            quoted.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\t' => "\\t",
                _ when char.IsControl(c) => string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}"),
                _ => c.ToString(),
            });
        return quoted.Append('"').ToString();
    }

    /// <summary>The report's lines, as comments at the head of the level file.</summary>
    public static IReadOnlyList<string> Comments(int depth, ulong seed, LayoutReport report) =>
    [
        string.Create(CultureInfo.InvariantCulture, $"generated by src/mapgen (descent-level): depth {depth}, seed {seed}"),
        string.Create(CultureInfo.InvariantCulture,
            $"rooms {report.Rooms}, joints {report.Joints}, loops {report.Loops}, path {report.CriticalPath.Count}, capped doors {report.CappedDoors}, wasted doors {report.WastedDoors}"),
        string.Create(CultureInfo.InvariantCulture,
            $"S-tier spots {report.STierSpots}, B-tier spots {report.BTierSpots}, treasure {(report.TreasurePlaced ? "yes" : "no")}, attempts {report.Attempts}")
        + (report.JumpFallbackReason is { } why ? "; fallback: " + why : ""),
    ];

    /// <summary>The most per-room cameras a preview gets (one per family placed).</summary>
    public const int MaxCameras = 12;

    /// <summary>
    /// The preview's per-room cameras (<c>ssmap preview -camera</c>), one per
    /// family the level places, at most <see cref="MaxCameras"/>, a larger room's
    /// first (the level's set piece is never the one the cap cuts): the room's
    /// own corner camera from <c>library.json</c> (<see cref="PreviewCamera"/>,
    /// at a player's eye in a corner of its body looking across it at its
    /// focus), turned with the room; for a manifest without one, at a
    /// player's eye on the line from the family's first placed room's first
    /// door to its focus, 448 short of the focus (96 in from the door at
    /// least), looking at the focus. Each is labelled with the room's name.
    /// Each is "x y z pitch yaw fov label" in the linked map's frame: cell
    /// (row, column) at (column, row) × the cell, turned counter-clockwise
    /// about its centre by the placement's rotation.
    /// </summary>
    public static IReadOnlyList<string> Cameras(LevelPlan plan, string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        var rooms = doc.RootElement.GetProperty("rooms");
        var cameras = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // A larger room (a great hall, a grotto) first, so the cap never cuts the level's set piece
        // (brief p4: level 3 seed 3 lost its hall's camera to the cap).
        static bool Larger(Placement p) => RoomName.TryParse(p.Room, out var s, out _) && s!.IsMultiCell;
        foreach (Placement p in plan.Placements.OrderByDescending(Larger).ThenBy(p => p.Row).ThenBy(p => p.Column))
        {
            if (!rooms.TryGetProperty(p.Room, out JsonElement room) || !RoomName.TryParse(p.Room, out var shape, out _) || !seen.Add(shape!.Family))
                continue;
            string[] doors = [.. room.GetProperty("doors").EnumerateArray().Select(d => d.GetString() ?? "")];
            if (doors.Length == 0 || !room.TryGetProperty("focus", out JsonElement f) || !room.TryGetProperty("cell", out JsonElement cellSize))
                continue;
            // A larger room turns about its footprint's middle.
            float cell = cellSize.GetSingle(), m = shape.CellsX * cell / 2;
            float[] focus = [.. f.EnumerateArray().Select(v => v.GetSingle())];
            float floor = focus[2];
            (float X, float Y) Turn((float X, float Y) q)
            {
                double a = p.Rotation * Math.PI / 180;
                float dx = q.X - m, dy = q.Y - m;
                return (p.Column * cell + m + (float)(dx * Math.Cos(a) - dy * Math.Sin(a)), p.Row * cell + m + (float)(dx * Math.Sin(a) + dy * Math.Cos(a)));
            }

            // The room's own corner camera (PreviewCamera), turned with the room.
            if (room.TryGetProperty("camera", out JsonElement cam))
            {
                float[] c = [.. cam.EnumerateArray().Select(v => v.GetSingle())];
                var corner = Turn((c[0], c[1]));
                cameras.Add(string.Create(CultureInfo.InvariantCulture, $"{Math.Round(corner.X)} {Math.Round(corner.Y)} {c[2]} 6 {Math.Round(((c[3] + p.Rotation) % 360 + 360) % 360)} 90 {p.Room}"));
                if (cameras.Count == MaxCameras)
                    break;
                continue;
            }

            // On the line from the first door to the focus, 448 short of the focus and 96 in at least:
            // just inside the room's door.
            // The door's middle on its wall: a larger room's whole side (its first socket's wall).
            float e = shape.CellsX * cell;
            (float X, float Y) door = doors[0] switch
            {
                "east" => (e, m),
                "west" => (0, m),
                "north" => (m, e),
                _ => (m, 0),
            };
            float fx = focus[0] - door.X, fy = focus[1] - door.Y, length = MathF.Sqrt(fx * fx + fy * fy);
            float inward = length > 0 ? Math.Max(96, length - 448) / length : 0;
            (float X, float Y) eye = (door.X + fx * inward, door.Y + fy * inward);

            var at = Turn(eye);
            var look = Turn((focus[0], focus[1]));
            double yaw = Math.Atan2(look.Y - at.Y, look.X - at.X) * 180 / Math.PI;
            cameras.Add(string.Create(CultureInfo.InvariantCulture, $"{Math.Round(at.X)} {Math.Round(at.Y)} {floor + 64} 12 {Math.Round((yaw + 360) % 360)} 90 {p.Room}"));
            if (cameras.Count == MaxCameras)
                break;
        }

        return cameras;
    }

    /// <summary>
    /// A preview camera on one joined doorway of the level (the review of 0c79c6fc2: the door frames
    /// and the track at a door were not in any view): from inside one room of a joint, 320 in and 96
    /// to the side, looking at the doorway's middle. A joint between a room whose family carries
    /// track (<paramref name="tracked"/>) and one that does not is chosen first, so the view shows
    /// how a track that stops at a door reads; null when the level has no joint of rooms of one cell.
    /// </summary>
    public static string? DoorwayCamera(LevelPlan plan, IReadOnlySet<string> tracked, int cell = 1024)
    {
        var at = new Dictionary<(int, int), (Placement P, Doors Doors, string Family)>();
        foreach (Placement p in plan.Placements)
            if (RoomName.TryParse(p.Room, out var shape, out _) && !shape!.IsMultiCell)
                at[(p.Row, p.Column)] = (p, DoorSets.Rotate(shape.Doors, p.Rotation), shape.Family);
        (Placement P, Doors Side, int Score)? best = null;
        foreach (var ((row, column), (p, doors, family)) in at.OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2))
            foreach (Doors side in (Doors[])[Doors.North, Doors.East, Doors.South, Doors.West])
            {
                if ((doors & side) == 0)
                    continue;
                var next = side switch { Doors.North => (row + 1, column), Doors.South => (row - 1, column), Doors.East => (row, column + 1), _ => (row, column - 1) };
                if (!at.TryGetValue(next, out var other) || (other.Doors & DoorSets.Opposite(side)) == 0)
                    continue;
                int score = tracked.Contains(family) && !tracked.Contains(other.Family) ? 2 : tracked.Contains(family) ? 1 : 0;
                if (best is not { } b || score > b.Score)
                    best = (p, side, score);
            }
        if (best is not { } chosen)
            return null;
        float h = cell / 2f, cx = chosen.P.Column * cell + h, cy = chosen.P.Row * cell + h;
        var (dx, dy) = chosen.Side switch { Doors.North => (0, 1), Doors.South => (0, -1), Doors.East => (1, 0), _ => (-1, 0) };
        float doorX = cx + dx * h, doorY = cy + dy * h;
        float eyeX = doorX - dx * 320 + dy * 96, eyeY = doorY - dy * 320 + dx * 96;
        double yaw = Math.Atan2(doorY - eyeY, doorX - eyeX) * 180 / Math.PI;
        return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(eyeX)} {Math.Round(eyeY)} 80 6 {Math.Round((yaw + 360) % 360)} 90 doorway {chosen.P.Room}");
    }

    /// <summary>
    /// Lays out a level of <paramref name="depth"/> from the library in
    /// <paramref name="libraryDir"/> and writes its level file to
    /// <paramref name="output"/>, and beside it the preview's per-room
    /// cameras, a line each (<c>.cameras</c>); the layout's error when the
    /// pool cannot make one.
    /// </summary>
    public static (string? Error, LayoutReport? Report) Generate(string libraryDir, int depth, ulong seed, string output)
    {
        var (library, rooms) = ReadManifest(File.ReadAllText(Path.Combine(libraryDir, "library.json")));
        LayoutResult result = new LevelLayout().Generate(depth, seed, rooms);
        if (!result.Ok)
            return (result.Error, null);
        File.WriteAllText(output, Write(result.Plan!, library, LevelKeys.ForDepth(depth), Comments(depth, seed, result.Report!)));
        var cameras = Cameras(result.Plan!, File.ReadAllText(Path.Combine(libraryDir, "library.json"))).ToList();
        if (DoorwayCamera(result.Plan!, DescentRooms.ThemeFor(depth).Props.RailFamilies) is { } doorway)
            cameras.Add(doorway);
        File.WriteAllLines(Path.ChangeExtension(output, ".cameras"), cameras);
        return (null, result.Report);
    }
}
