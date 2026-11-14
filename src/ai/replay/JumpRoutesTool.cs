using System.Globalization;
using System.Text;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;

namespace SourceSharp.Ai.Replay;

/// <summary>A place a CTF bot goes, its point and the cell a body stands in there.</summary>
public sealed record RoutePlace(string Name, AiVector At, int Cell);

/// <summary>
/// One route both ways it can be searched: on foot (an agent with no blast reach) and with the
/// level's links (a Soldier's reach and the health to spend on each), in seconds at the Soldier's
/// run (null where there is no way), and the links the second takes, as indices into the set.
/// </summary>
public sealed record RouteChange(RoutePlace From, RoutePlace To, float? WalkSeconds, float? LinkSeconds, IReadOnlyList<int> Links)
{
    /// <summary>The seconds the links save, or null when either way is none.</summary>
    public float? Saved => WalkSeconds is { } w && LinkSeconds is { } l ? w - l : null;
}

/// <summary>
/// <c>ai-replay jumps ... routes</c> (lane G5c-2a step 5): which routes between the places a CTF bot
/// goes (each team's spawn rooms, both flags, both capture zones, from the map's entities as the
/// objective feed knows them in the game) would take a blast link, which, and how much sooner
/// they arrive than on foot; and which of the links' landings only a link reaches. Nobody in the
/// game is given the reach yet: this is what a Soldier's searches would answer when one is.
/// </summary>
public static class JumpRoutesTool
{
    /// <summary>The health a Soldier may spend on one link: his 200, less the floor the jump's rule keeps after a blast (90).</summary>
    public const float LinkHealth = 200f - 90f;

    private static string Team(int team) => team switch
    {
        2 => "red",
        3 => "blue",
        _ => $"team{team}",
    };

    private static bool Inside(AiVector p, AiVector mins, AiVector maxs)
        => p.X >= mins.X && p.X <= maxs.X && p.Y >= mins.Y && p.Y <= maxs.Y && p.Z >= mins.Z - 1f && p.Z <= maxs.Z;

    /// <summary>How many times each field is built for the cost (lane G5c-2b).</summary>
    public const int FieldRepeats = 5;

    /// <summary>
    /// What a follower's flow field costs with the links (lane G5c-2b), in thread CPU: a field to each place built
    /// <see cref="FieldRepeats"/> times for an agent with no links, one with the links and no reach for them (every
    /// class but the Soldier), and a Soldier with them; and whether the second is the first, cell for cell.
    /// </summary>
    public static string Fields(NavGraph g, JumpLinkSet set, IReadOnlyList<RoutePlace> places, Func<double>? cpu)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(places);
        NavObstacleState Agent(NavLinks? links, JumpReach reach) => new(g.ObstacleCount) { OpenDoors = true, Links = links, Reach = reach, LinkHealth = LinkHealth };
        (string Name, NavObstacleState State)[] kinds =
        [
            ("no links", Agent(null, JumpReach.None)),
            ("links, no reach", Agent(set.Links, JumpReach.None)),
            ("a Soldier with links", Agent(set.Links, JumpReach.Blast)),
        ];
        var fields = kinds.Select(_ => new FlowField()).ToArray();
        var micros = new double[kinds.Length];
        int builds = 0, differ = 0, linked = 0;
        foreach (RoutePlace p in places)
        {
            for (int k = 0; k < kinds.Length; k++)
            {
                double start = cpu?.Invoke() ?? 0d;
                for (int r = 0; r < FieldRepeats; r++)
                    fields[k].Build(g, new CellId(p.Cell), ClearanceClass.Crouch, kinds[k].State);
                micros[k] += (cpu?.Invoke() ?? 0d) - start;
            }

            builds += FieldRepeats;
            for (int c = 0; c < g.CellCount; c++)
            {
                if (fields[0].CostAt(c) != fields[1].CostAt(c) || fields[0].NextFrom(c) != fields[1].NextFrom(c) || fields[0].ViaFrom(c) != fields[1].ViaFrom(c) || fields[1].LinkFrom(c) >= 0)
                    differ++;
                linked += fields[2].LinkFrom(c) >= 0 ? 1 : 0;
            }
        }

        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"  fields      {builds} builds of each kind over {places.Count} places ({g.CellCount:N0} cells), thread CPU per build:");
        for (int k = 0; k < kinds.Length; k++)
            line.Append(CultureInfo.InvariantCulture, $" {kinds[k].Name} {(builds == 0 ? 0d : micros[k] / builds):F0} us;");
        line.Append(CultureInfo.InvariantCulture, $" links, no reach against no links: {differ} cells differ; cells the Soldier's fields step over a link from: {linked}\n");
        return line.ToString();
    }

    /// <summary>
    /// The map's entities with their teams and a brush's bounds, from a whole bsp (a shipped map's
    /// LZMA-compressed lumps too, through map-tools' reader, as <see cref="NavRoomsTool.ReadEntities"/>
    /// reads them: the game's own lump reader does not unpack them, and in the game the bots read
    /// TF2's placements from the spawned entities).
    /// </summary>
    public static MapEntities ReadEntities(byte[] bsp)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        MapEntities lump = MapEntities.Read(bsp);
        if (lump.Unread is null)
            return lump;
        var all = new List<MapEntity>();
        foreach (RoomEntity e in NavRoomsTool.ReadEntities(bsp))
            all.Add(new MapEntity(e.ClassName, e.TargetName, e.Origin, e.Mins, e.Maxs, Team: e.Team));
        return new MapEntities(all, null);
    }

    /// <summary>The CTF places of <paramref name="entities"/> that stand on <paramref name="graph"/>: each respawn room (at its first spawn), each flag, each capture zone (at its middle, on its floor).</summary>
    public static IReadOnlyList<RoutePlace> CtfPlaces(MapEntities entities, NavGraph graph)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var places = new List<RoutePlace>();
        void Add(string name, AiVector at)
        {
            CellId cell = NavPoints.CellNear(graph, at, ClearanceClass.Stand);
            if (cell.IsValid)
                places.Add(new RoutePlace(name, at, cell.Index));
        }

        var rooms = new Dictionary<int, int>();
        foreach (MapEntity room in entities.All.Where(e => e.ClassName == "func_respawnroom" && e.Mins is not null && e.Maxs is not null))
        {
            MapEntity? spawn = entities.All.Where(s => s.ClassName == "info_player_teamspawn" && Inside(s.Origin, room.Mins!.Value, room.Maxs!.Value))
                                           .Select(s => (MapEntity?)s).FirstOrDefault();
            if (spawn is not { } s)
                continue;
            int team = room.Team != 0 ? room.Team : s.Team;
            rooms[team] = rooms.GetValueOrDefault(team) + 1;
            Add($"{Team(team)} spawn {rooms[team]}", s.Origin);
        }

        foreach (MapEntity flag in entities.All.Where(e => e.ClassName == "item_teamflag"))
            Add($"{Team(flag.Team)} flag", flag.Origin);
        foreach (MapEntity zone in entities.All.Where(e => e.ClassName == "func_capturezone" && e.Mins is not null && e.Maxs is not null))
        {
            // At its middle, on the lowest floor inside it: looked for up the volume a body's step at a time.
            AiVector mins = zone.Mins!.Value, maxs = zone.Maxs!.Value;
            for (float z = mins.Z; z <= maxs.Z; z += 18f)
            {
                var at = new AiVector((mins.X + maxs.X) * 0.5f, (mins.Y + maxs.Y) * 0.5f, z);
                if (NavPoints.CellNear(graph, at, ClearanceClass.Stand).IsValid)
                {
                    Add($"{Team(zone.Team)} capture", at);
                    break;
                }
            }
        }

        return places;
    }

    /// <summary>The route from <paramref name="from"/> to <paramref name="to"/> on foot and with <paramref name="set"/>'s links.</summary>
    public static RouteChange Route(NavGraph graph, JumpLinkSet set, RoutePlace from, RoutePlace to, float runSpeed, float linkHealth = LinkHealth)
    {
        ArgumentNullException.ThrowIfNull(set);
        var finder = new NavPathfinder();
        var path = new List<CellId>();
        var steps = new List<int>();
        var walker = new NavObstacleState(graph.ObstacleCount) { OpenDoors = true };
        var soldier = new NavObstacleState(graph.ObstacleCount) { OpenDoors = true, Reach = JumpReach.Blast, LinkHealth = linkHealth };
        float? walk = finder.Find(graph, set.Links, new CellId(from.Cell), new CellId(to.Cell), ClearanceClass.Stand, walker, path, steps, graph.CellCount, out float w) == PathStatus.Found
            ? w / runSpeed : null;
        float? jump = finder.Find(graph, set.Links, new CellId(from.Cell), new CellId(to.Cell), ClearanceClass.Stand, soldier, path, steps, graph.CellCount, out float j) == PathStatus.Found
            ? j / runSpeed : null;
        return new RouteChange(from, to, walk, jump, jump is null ? [] : [.. steps.Where(s => s >= 0)]);
    }

    /// <summary>
    /// The graph from above with the links drawn on it (the nav view's way, <see cref="NavPng"/>): cells in the part
    /// of the graph the places stand in green, every other part grey, both shaded by height, upper floors over lower;
    /// each link a red arrow from its takeoff to its landing, numbered as the text numbers it; each place a blue ring.
    /// </summary>
    public static byte[] Png(NavGraph g, JumpLinkSet set, IReadOnlyList<RoutePlace> places, float pixelsPerUnit)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(set);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, lo = float.MaxValue, hi = float.MinValue;
        for (int c = 0; c < g.CellCount; c++)
        {
            NavCell cell = g.Cell(c);
            (minX, minY, maxX, maxY) = (MathF.Min(minX, cell.MinX), MathF.Min(minY, cell.MinY), MathF.Max(maxX, cell.MaxX), MathF.Max(maxY, cell.MaxY));
            (lo, hi) = (MathF.Min(lo, cell.FloorZ), MathF.Max(hi, cell.FloorZ));
        }

        int width = (int)MathF.Ceiling((maxX - minX) * pixelsPerUnit) + (2 * NavPng.Margin), height = (int)MathF.Ceiling((maxY - minY) * pixelsPerUnit) + (2 * NavPng.Margin);
        using var bitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(new SkiaSharp.SKColor(16, 18, 23));
        float X(float x) => NavPng.Margin + ((x - minX) * pixelsPerUnit);
        float Y(float y) => height - NavPng.Margin - ((y - minY) * pixelsPerUnit);
        var played = new HashSet<int>(places.Select(p => g.ComponentOf(p.Cell)));
        using var fill = new SkiaSharp.SKPaint { IsAntialias = false, Style = SkiaSharp.SKPaintStyle.Fill };
        foreach (int c in Enumerable.Range(0, g.CellCount).OrderBy(c => g.Cell(c).FloorZ).ThenBy(c => c))
        {
            NavCell cell = g.Cell(c);
            float shade = 0.35f + (0.65f * (hi > lo ? (cell.FloorZ - lo) / (hi - lo) : 1f));
            fill.Color = played.Contains(g.ComponentOf(c)) ? new SkiaSharp.SKColor((byte)(60 * shade), (byte)(200 * shade), (byte)(90 * shade))
                                                           : new SkiaSharp.SKColor((byte)(150 * shade), (byte)(150 * shade), (byte)(150 * shade));
            canvas.DrawRect(SkiaSharp.SKRect.Create(X(cell.MinX), Y(cell.MaxY), (cell.MaxX - cell.MinX) * pixelsPerUnit, (cell.MaxY - cell.MinY) * pixelsPerUnit), fill);
        }

        using var arrow = new SkiaSharp.SKPaint { IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 3f, Color = new SkiaSharp.SKColor(255, 50, 50) };
        using var ring = new SkiaSharp.SKPaint { IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 3f, Color = new SkiaSharp.SKColor(80, 140, 255) };
        using var font = new SkiaSharp.SKFont { Size = 18f };
        using var text = new SkiaSharp.SKPaint { IsAntialias = true, Color = SkiaSharp.SKColors.White };
        for (int i = 0; i < set.Links.Count; i++)
        {
            JumpLink j = set.JumpOf(i);
            float ax = X(j.Recipe.Takeoff.X), ay = Y(j.Recipe.Takeoff.Y), bx = X(j.Recipe.Landing.X), by = Y(j.Recipe.Landing.Y);
            canvas.DrawLine(ax, ay, bx, by, arrow);
            canvas.DrawCircle(bx, by, 4f, arrow);
            canvas.DrawText(i.ToString(CultureInfo.InvariantCulture), bx + 6f, by - 6f, SkiaSharp.SKTextAlign.Left, font, text);
        }

        foreach (RoutePlace p in places)
        {
            canvas.DrawCircle(X(p.At.X), Y(p.At.Y), 10f, ring);
            canvas.DrawText(p.Name, X(p.At.X) + 12f, Y(p.At.Y) + 6f, SkiaSharp.SKTextAlign.Left, font, text);
        }

        canvas.Flush();
        using SkiaSharp.SKData png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static string S(float? seconds) => seconds is { } s ? s.ToString("F1", CultureInfo.InvariantCulture) : "none";

    private static string V(AiVector v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:F0} {v.Y:F0} {v.Z:F0})");

    /// <summary>The routes' text: the places, every ordered pair of them that changes, and every link's landing reached from the places.</summary>
    public static string Text(JumpPlacesReport report, MapEntities entities, float runSpeed, string? pngPath = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        NavGraph g = report.Graph;
        JumpLinkSet set = JumpLinkSet.Of(g, report.Result.Links, new NavLinkCosts { RunSpeed = runSpeed });
        IReadOnlyList<RoutePlace> places = CtfPlaces(entities, g);
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"  routes      {places.Count} places, {set.Links.Count} links (estimate scale {set.Links.EstimateScale:F2}); a Soldier at {runSpeed:F0} u/s, {LinkHealth:F0} health to spend on a link\n");
        foreach (RoutePlace p in places)
            s.Append(CultureInfo.InvariantCulture, $"    place {p.Name,-14} {V(p.At)} cell {p.Cell} floor {g.Cell(p.Cell).FloorZ:F0}\n");

        // Every link, and the joined-up part of the graph its takeoff is in: how big, and which places stand in it.
        var partSize = new Dictionary<int, int>();
        for (int c = 0; c < g.CellCount; c++)
            partSize[g.ComponentOf(c)] = partSize.GetValueOrDefault(g.ComponentOf(c)) + 1;
        for (int i = 0; i < set.Links.Count; i++)
        {
            NavLink l = set.Links[i];
            JumpLink j = set.JumpOf(i);
            int part = g.ComponentOf(l.From);
            string[] inPart = [.. places.Where(p => g.ComponentOf(p.Cell) == part).Select(p => p.Name)];
            var at = new AiVector(j.Recipe.Landing.X, j.Recipe.Landing.Y, j.Recipe.Landing.Z);
            string near = string.Join("; ", entities.All.Where(e => e.Name is { Length: > 0 } && !e.ClassName.StartsWith("info_player", StringComparison.Ordinal) && e.ClassName is not ("env_soundscape_proxy" or "move_rope" or "keyframe_rope"))
                                                        .Select(e => (e, d: (e.Mins is { } mn && e.Maxs is { } mx ? new AiVector((mn.X + mx.X) * 0.5f, (mn.Y + mx.Y) * 0.5f, (mn.Z + mx.Z) * 0.5f) : e.Origin).DistanceTo(at)))
                                                        .OrderBy(x => x.d).Take(3).Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.e.ClassName} '{x.e.Name}' {x.d:F0} u")));
            s.Append(CultureInfo.InvariantCulture,
                $"    link #{i,-2} {V(new AiVector(j.Recipe.Takeoff.X, j.Recipe.Takeoff.Y, j.Recipe.Takeoff.Z))} -> {V(new AiVector(j.Recipe.Landing.X, j.Recipe.Landing.Y, j.Recipe.Landing.Z))} rise {g.Cell(l.To).FloorZ - g.Cell(l.From).FloorZ,4:F0} {j.Family,-7} {l.Seconds:F2} s (walk {S(j.WalkSeconds)}) health {l.Health:F0} robust {l.Robustness:F2} cost {set.Links.CostOf(i) / runSpeed:F2} s; takeoff's part {partSize[part]:N0} cells{(inPart.Length == 0 ? ", no place in it" : $", with {string.Join(", ", inPart)}")}{(g.ComponentOf(l.To) != part ? "; lands in another part" : "")}; near the landing: {near}\n");
        }

        var routes = new List<RouteChange>();
        foreach (RoutePlace a in places)
        {
            foreach (RoutePlace b in places)
            {
                if (!ReferenceEquals(a, b))
                    routes.Add(Route(g, set, a, b, runSpeed));
            }
        }

        List<RouteChange> changed = [.. routes.Where(r => r.Links.Count > 0)];
        s.Append(CultureInfo.InvariantCulture, $"    {routes.Count} routes between them: {changed.Count} take a link, {routes.Count(r => r.WalkSeconds is null)} have no way on foot, {routes.Count(r => r.LinkSeconds is null)} none with links\n");
        foreach (RouteChange r in changed.OrderByDescending(r => r.Saved ?? float.MaxValue))
        {
            s.Append(CultureInfo.InvariantCulture, $"    {r.From.Name,-14} -> {r.To.Name,-14} walk {S(r.WalkSeconds),5} s, with links {S(r.LinkSeconds),5} s, saves {S(r.Saved),5} s; links ")
             .AppendJoin(", ", r.Links.Select(i => string.Create(CultureInfo.InvariantCulture, $"#{i} {V(g.Cell(set.Links[i].From).Centre)}->{V(g.Cell(set.Links[i].To).Centre)}")))
             .Append('\n');
        }

        // Every landing a link makes, from every place: how many only a link reaches, and from how many places.
        var landings = set.Links.Count == 0 ? [] : Enumerable.Range(0, set.Links.Count).Select(i => set.Links[i].To).Distinct().ToList();
        int onlyByLink = 0, noWay = 0;
        foreach (int landing in landings)
        {
            var at = new RoutePlace($"landing {landing}", g.Cell(landing).Centre, landing);
            int walked = 0, linked = 0;
            RouteChange? nearest = null;
            foreach (RoutePlace p in places)
            {
                RouteChange r = Route(g, set, p, at, runSpeed);
                walked += r.WalkSeconds is not null ? 1 : 0;
                linked += r.LinkSeconds is not null ? 1 : 0;
                if (r.LinkSeconds is { } l && (nearest?.LinkSeconds is not { } best || l < best))
                    nearest = r;
            }

            onlyByLink += walked == 0 && linked > 0 ? 1 : 0;
            noWay += linked == 0 ? 1 : 0;
            s.Append(CultureInfo.InvariantCulture, $"    landing {V(g.Cell(landing).Centre)} cell {landing}: from {walked} of {places.Count} places on foot, {linked} with links")
             .Append(nearest is { } n ? string.Create(CultureInfo.InvariantCulture, $"; from {n.From.Name}: walk {S(n.WalkSeconds)} s, with links {S(n.LinkSeconds)} s ({n.Links.Count} link(s))") : "")
             .Append('\n');
        }

        s.Append(CultureInfo.InvariantCulture, $"    {landings.Count} landings: {onlyByLink} reached from the places only with a link, {noWay} not even with links\n");
        s.Append(Fields(g, set, places, SourceSharp.Ai.Server.Senses.ThreadCpuClock.Micros()));
        if (pngPath is not null)
        {
            File.WriteAllBytes(pngPath, Png(g, set, places, 0.125f));
            s.Append(CultureInfo.InvariantCulture, $"    drawn       {pngPath}\n");
        }

        return s.ToString();
    }
}
