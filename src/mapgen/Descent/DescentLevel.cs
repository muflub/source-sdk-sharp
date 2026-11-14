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

    /// <summary>
    /// Lays out a level of <paramref name="depth"/> from the library in
    /// <paramref name="libraryDir"/> and writes its level file to
    /// <paramref name="output"/>; the layout's error when the pool cannot make one.
    /// </summary>
    public static (string? Error, LayoutReport? Report) Generate(string libraryDir, int depth, ulong seed, string output)
    {
        var (library, rooms) = ReadManifest(File.ReadAllText(Path.Combine(libraryDir, "library.json")));
        LayoutResult result = new LevelLayout().Generate(depth, seed, rooms);
        if (!result.Ok)
            return (result.Error, null);
        File.WriteAllText(output, Write(result.Plan!, library, LevelKeys.ForDepth(depth), Comments(depth, seed, result.Report!)));
        return (null, result.Report);
    }
}
