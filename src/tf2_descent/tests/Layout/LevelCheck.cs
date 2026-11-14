using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// A generated level's properties, read from the plan alone (and the report's
/// critical path), with the geometry worked out here rather than taken from
/// the generator: a door's world side is its unrotated side turned as a vector,
/// and the joints are the pairs of doors that face each other across a wall.
/// </summary>
internal sealed class LevelCheck
{
    public const string Grid = "grid";
    public const string Roles = "roles";
    public const string DoorRule = "doors";
    public const string Connected = "connected";
    public const string Path = "path";
    public const string LowRule = "low";
    public const string Jumps = "jumps";
    public const string Treasure = "treasure";
    public const string Boss = "boss";
    public const string DeadEndRule = "deadend";
    public const string Mix = "mix";
    public const string Budget = "budget";

    private readonly int _rows;
    private readonly int _columns;
    private readonly RoomShape?[] _shape;
    private readonly RoomInfo?[] _info;
    private readonly int[] _sides; // world door bits per cell: 1 N, 2 E, 4 S, 8 W

    public List<(string Property, string Message)> Faults { get; } = [];

    public int Joints { get; private set; }
    public int Rooms { get; private set; }
    public bool HasRotation { get; private set; }
    public int JumpRooms { get; private set; }
    public int CombatRooms { get; private set; }

    /// <summary>Rooms over the plan's cells (M19).</summary>
    public double FillRatio { get; private set; }

    /// <summary>Placed rooms beside each room, joined or not, on average.</summary>
    public double MeanNeighbours { get; private set; }

    /// <summary>The most rooms in a straight line along the report's critical path.</summary>
    public int LongestStraightRun { get; private set; }

    /// <summary>The mean size of the groups of joined halls and shafts; 0 with none.</summary>
    public double TallClusterMean { get; private set; }

    /// <summary>Doors from each dead end to the nearest room of the critical path, summed (side tunnels' length).</summary>
    public int TunnelDoors { get; private set; }

    /// <summary>Rooms with one joined door, but the arrival and the exit (M18).</summary>
    public int DeadEnds { get; private set; }

    /// <summary>Dead ends other than the treasure room: branches ending in a combat room or a shrine.</summary>
    public int BranchEnds { get; private set; }

    /// <summary>Dead ends whose room is a connector rather than a destination.</summary>
    public int DeadEndConnectors { get; private set; }

    /// <summary>Doors facing an empty cell inside the grid, and doors facing the grid's edge.</summary>
    public int DoorsOnEmptyCells { get; private set; }
    public int DoorsOnTheEdge { get; private set; }

    /// <summary>Rooms of the low height class.</summary>
    public int LowRooms { get; private set; }

    /// <summary>Low rooms of a connector family (plan section 5: drift, bend, tee, cross, dogleg, stub, wide drift).</summary>
    public int LowConnectors { get; private set; }

    private static bool IsConnector(string family) => family is "drift" or "bend" or "tee" or "cross" or "dogleg" or "stub" or "widedrift";

    public LevelCheck(int depth, LevelPlan plan, LayoutReport report, IReadOnlyList<RoomInfo> catalogue)
    {
        _rows = plan.Rows;
        _columns = plan.Columns;
        var cells = _rows * _columns;
        _shape = new RoomShape?[cells];
        _info = new RoomInfo?[cells];
        _sides = new int[cells];
        var profile = DepthTable.For(depth);
        var byName = catalogue.ToDictionary(r => r.Name, StringComparer.Ordinal);

        // M19: a boss level is its row; a normal level's grid is its rooms' bounding box, within the span.
        if (profile.IsBoss ? plan.Rows != 1 : plan.Rows > profile.MaxSpan || plan.Columns > profile.MaxSpan)
            Fault(Grid, $"grid {plan.Rows}x{plan.Columns}, the span is {profile.MaxSpan}");
        foreach (var p in plan.Placements)
        {
            if (p.Row < 0 || p.Row >= _rows || p.Column < 0 || p.Column >= _columns) { Fault(Grid, $"{p.Room} off the grid"); continue; }
            if (p.Rotation is not (0 or 90 or 180 or 270)) Fault(Grid, $"{p.Room} turned {p.Rotation}");
            if (!byName.TryGetValue(p.Room, out var info)) { Fault(Grid, $"{p.Room} is not in the catalogue"); continue; }
            if (depth < info.MinDepth || depth > info.MaxDepth) Fault(Grid, $"{p.Room} does not serve depth {depth}");
            var cell = p.Row * _columns + p.Column;
            if (_info[cell] is not null) { Fault(Grid, $"row {p.Row}, column {p.Column} holds two rooms"); continue; }
            _info[cell] = info;
            _shape[cell] = RoomName.Parse(p.Room);
            _sides[cell] = Turn(_shape[cell]!.Doors, p.Rotation);
            Rooms++;
            HasRotation |= p.Rotation != 0;
            if (_shape[cell]!.Height == HeightClass.Low) LowRooms++;
            if (_shape[cell]!.Height == HeightClass.Low && IsConnector(_shape[cell]!.Family)) LowConnectors++;
            var combat = !info.Tags.Contains("corridor") && !info.Tags.Contains("junction");
            if (combat && Kind(cell) == "fill") CombatRooms++;
            if (combat && Kind(cell) == "fill" && _shape[cell]!.IsJumpRoom) JumpRooms++;
        }

        var ups = Cells().Where(c => Kind(c) == "up").ToList();
        var downs = Cells().Where(c => Kind(c) == "down").ToList();
        if (ups.Count != 1) Fault(Roles, $"{ups.Count} up rooms");
        if (downs.Count != (profile.HasDownExit ? 1 : 0)) Fault(Roles, $"{downs.Count} down rooms at depth {depth}");
        if (ups.Count == 1 && plan.UpRoom != _info[ups[0]]!.Name) Fault(Roles, $"UpRoom {plan.UpRoom} is not the placed {_info[ups[0]]!.Name}");
        if (downs.Count == 1 && plan.DownRoom != _info[downs[0]]!.Name) Fault(Roles, $"DownRoom {plan.DownRoom} is not the placed {_info[downs[0]]!.Name}");

        // Doors: each joins a door across the wall, or faces the edge or an empty cell.
        var degree = new int[cells];
        foreach (var cell in Cells())
            for (var side = 0; side < 4; side++)
            {
                if ((_sides[cell] & (1 << side)) == 0) continue;
                var next = Across(cell, side);
                if (next < 0) { DoorsOnTheEdge++; continue; }
                if (_info[next] is null) { DoorsOnEmptyCells++; continue; }
                if ((_sides[next] & (1 << ((side + 2) % 4))) != 0)
                {
                    Joints++;
                    degree[cell]++;
                }
                else Fault(DoorRule, $"{_info[cell]!.Name} at {Where(cell)} opens on the blank wall of {_info[next]!.Name}");
            }
        Joints /= 2;

        // M18: a dead end is a destination, never a hallway, and a level holds no more than its cap.
        foreach (var cell in Cells().Where(c => degree[c] == 1 && Kind(c) is not ("up" or "down")))
        {
            DeadEnds++;
            if (Kind(cell) != "treasure") BranchEnds++;
            if (IsDestination(_info[cell]!, _shape[cell]!)) continue;
            DeadEndConnectors++;
            if (!profile.IsBoss) Fault(DeadEndRule, $"{_info[cell]!.Name} at {Where(cell)} is a hallway that ends");
        }
        if (!profile.IsBoss) CheckMix(profile);
        if (!profile.IsBoss && (report.DeadEnds, report.DeadEndConnectors) != (DeadEnds, DeadEndConnectors))
            Fault(DeadEndRule, $"the report counts {report.DeadEnds} dead ends ({report.DeadEndConnectors} hallways), the plan holds {DeadEnds} ({DeadEndConnectors})");

        var start = ups.Count == 1 ? ups[0] : Cells().FirstOrDefault(-1);
        var distance = Distances(start);
        if (start >= 0 && Cells().Any(c => distance[c] < 0))
            Fault(Connected, $"{Cells().Count(c => distance[c] < 0)} room(s) cannot be walked to from the arrival");

        var path = report.CriticalPath.Select(c => c.Row * _columns + c.Column).ToList();
        Shape(profile, path, degree);
        CheckPath(profile, path, start, downs, distance);
        CheckLowRule(path);

        if (!profile.IsBoss)
        {
            var s = Cells().Sum(c => _shape[c]!.STierSpots);
            var b = Cells().Sum(c => _shape[c]!.BTierSpots);
            if (s < profile.MinSTierSpots || b < profile.MinBTierSpots)
                Fault(Jumps, $"{s} S-tier and {b} B-tier spots, the depth asks {profile.MinSTierSpots} and {profile.MinBTierSpots}");
            if (s != report.STierSpots || b != report.BTierSpots)
                Fault(Jumps, $"the report counts {report.STierSpots} S and {report.BTierSpots} B, the plan holds {s} and {b}");

            var treasures = Cells().Where(c => _info[c]!.Tags.Contains("treasure")).ToList();
            if (treasures.Count != 1) Fault(Treasure, $"{treasures.Count} treasure rooms");
            if (treasures.Any(path.Contains)) Fault(Treasure, "the treasure room is on the critical path");
        }
        else
        {
            var row = Cells().OrderBy(c => c).Select(Kind).ToList();
            string[] expect = profile.HasDownExit ? ["up", "antechamber", "arena", "down"] : ["up", "antechamber", "arena"];
            if (!row.SequenceEqual(expect)) Fault(Boss, $"the row is {string.Join(", ", row)}");
        }
    }

    private void CheckPath(DepthProfile profile, List<int> path, int start, List<int> downs, int[] distance)
    {
        if (path.Count == 0) { Fault(Path, "no critical path"); return; }
        var end = profile.HasDownExit ? (downs.Count == 1 ? downs[0] : -1) : Cells().First(c => Kind(c) == "arena");
        if (path[0] != start || path[^1] != end) Fault(Path, $"the path runs {Where(path[0])} to {Where(path[^1])}, not arrival to exit");
        for (var i = 1; i < path.Count; i++)
        {
            var side = SideTowards(path[i - 1], path[i]);
            if (side < 0 || (_sides[path[i - 1]] & (1 << side)) == 0 || (_sides[path[i]] & (1 << ((side + 2) % 4))) == 0)
                Fault(Path, $"the path steps from {Where(path[i - 1])} to {Where(path[i])} through no joint");
        }
        if (end >= 0 && distance[end] != path.Count - 1)
            Fault(Path, $"the path walks {path.Count - 1} doors and the exit is {distance[end]} doors from the arrival");
        if (!profile.IsBoss)
        {
            if (path.Count < profile.PathMin || path.Count > profile.PathMax)
                Fault(Path, $"the path holds {path.Count} rooms, the depth {profile.PathMin} to {profile.PathMax}");
            var apart = Math.Abs(path[0] / _columns - path[^1] / _columns) + Math.Abs(path[0] % _columns - path[^1] % _columns);
            if (2 * apart < path.Count - 1) Fault(Path, $"the arrival and exit are {apart} cells apart on a path of {path.Count - 1} steps");
        }
    }

    /// <summary>M19's figures, and its faults: the budget met and the bounding box tight.</summary>
    private void Shape(DepthProfile profile, List<int> path, int[] degree)
    {
        if (Rooms == 0) return;
        if (!profile.IsBoss && Rooms != profile.Rooms) Fault(Budget, $"{Rooms} rooms, the depth's budget is {profile.Rooms}");
        var rows = Cells().Select(c => c / _columns).ToList();
        var columns = Cells().Select(c => c % _columns).ToList();
        if (rows.Min() != 0 || columns.Min() != 0 || rows.Max() != _rows - 1 || columns.Max() != _columns - 1)
            Fault(Grid, $"the {_rows}x{_columns} grid is not the bounding box of its rooms");

        FillRatio = (double)Rooms / (_rows * _columns);
        MeanNeighbours = Cells().Average(c => Enumerable.Range(0, 4).Count(side => Across(c, side) is >= 0 and var n && _info[n] is not null));

        LongestStraightRun = Math.Min(path.Count, 2);
        for (int i = 2, run = 2; i < path.Count; i++)
        {
            run = SideTowards(path[i - 2], path[i - 1]) == SideTowards(path[i - 1], path[i]) ? run + 1 : 2;
            LongestStraightRun = Math.Max(LongestStraightRun, run);
        }

        // Halls and shafts joined to each other, group by group.
        var seen = new bool[_rows * _columns];
        var groups = new List<int>();
        bool Tall(int c) => _shape[c]!.Height is HeightClass.Hall or HeightClass.Shaft;
        foreach (var cell in Cells().Where(c => Tall(c)))
        {
            if (seen[cell]) continue;
            var size = 0;
            var stack = new Stack<int>([cell]);
            seen[cell] = true;
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                size++;
                foreach (var next in Joined(at).Where(n => !seen[n] && Tall(n)))
                {
                    seen[next] = true;
                    stack.Push(next);
                }
            }
            groups.Add(size);
        }
        TallClusterMean = groups.Count == 0 ? 0 : groups.Average();

        // Side tunnels: doors from each dead end back to the critical path.
        if (path.Count == 0) return;
        var fromPath = new int[_rows * _columns];
        Array.Fill(fromPath, -1);
        var queue = new Queue<int>();
        foreach (var c in path.Where(c => c >= 0 && c < fromPath.Length))
        {
            fromPath[c] = 0;
            queue.Enqueue(c);
        }
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var next in Joined(at).Where(n => fromPath[n] < 0))
            {
                fromPath[next] = fromPath[at] + 1;
                queue.Enqueue(next);
            }
        }
        TunnelDoors = Cells().Where(c => degree[c] == 1 && fromPath[c] > 0).Sum(c => fromPath[c]);
    }

    private IEnumerable<int> Joined(int cell)
    {
        for (var side = 0; side < 4; side++)
            if ((_sides[cell] & (1 << side)) != 0 && Across(cell, side) is >= 0 and var n && _info[n] is not null && (_sides[n] & (1 << ((side + 2) % 4))) != 0)
                yield return n;
    }

    private void CheckLowRule(List<int> path)
    {
        var run = 0;
        foreach (var cell in path)
        {
            var info = _info[cell];
            var low = info is not null && !info.Tags.Contains("corridor") && !info.Tags.Contains("junction") && _shape[cell]!.Height == HeightClass.Low;
            run = low ? run + 1 : 0;
            if (run == 3) Fault(LowRule, $"three low combat rooms in a row end at {Where(cell)}");
        }
    }

    /// <summary>The fill rooms by kind (connector, chamber, hall, shaft), M21's reading, written here.</summary>
    public int[] ClassCounts { get; } = new int[4];

    /// <summary>The fill rooms by family, in the order first placed.</summary>
    public Dictionary<string, int> FamilyCounts { get; } = new(StringComparer.Ordinal);

    /// <summary>The most times one variant (room name) stands among the fill rooms, and among the combat rooms.</summary>
    public int MostRepeated { get; private set; }
    public int MostRepeatedCombat { get; private set; }

    /// <summary>Joined pairs of fill rooms of one family, hallways and the rest.</summary>
    public int SameFamilyPairs { get; private set; }
    public int SameFamilyCombatPairs { get; private set; }

    public int FillRooms { get; private set; }

    /// <summary>M21: a level's mosts are never passed and its fewests met (shrines counted).</summary>
    private void CheckMix(DepthProfile profile)
    {
        var fill = Cells().Where(c => Kind(c) == "fill").ToList();
        FillRooms = fill.Count;
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        var combatNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var shrines = 0;
        foreach (var cell in fill)
        {
            var info = _info[cell]!;
            var shape = _shape[cell]!;
            var connector = !IsDestinationIgnoringPurpose(info, shape);
            var kind = connector ? 0 : shape.Height switch { HeightClass.Hall => 2, HeightClass.Shaft => 3, _ => 1 };
            ClassCounts[kind]++;
            FamilyCounts[shape.Family] = FamilyCounts.GetValueOrDefault(shape.Family) + 1;
            names[info.Name] = names.GetValueOrDefault(info.Name) + 1;
            if (!connector) combatNames[info.Name] = combatNames.GetValueOrDefault(info.Name) + 1;
            if (shape.Family == "stub" && info.Tags.Contains("shrine")) shrines++;
            foreach (var next in Joined(cell).Where(n => n > cell && Kind(n) == "fill" && _shape[n]!.Family == shape.Family))
            {
                SameFamilyPairs++;
                if (!connector) SameFamilyCombatPairs++;
            }
        }
        MostRepeated = names.Count == 0 ? 0 : names.Values.Max();
        MostRepeatedCombat = combatNames.Count == 0 ? 0 : combatNames.Values.Max();

        foreach (var kind in profile.Kinds.Where(k => ClassCounts[(int)k.Kind] > k.Max))
            Fault(Mix, $"{ClassCounts[(int)kind.Kind]} {kind.Kind} rooms, the most is {kind.Max}");
        foreach (var family in profile.FamilyTargets)
        {
            var n = FamilyCounts.GetValueOrDefault(family.Family);
            if (n > family.Max) Fault(Mix, $"{n} {family.Family} rooms, the most is {family.Max}");
            if (n < family.Min) Fault(Mix, $"{n} {family.Family} rooms, the fewest is {family.Min}");
        }
        if (shrines > profile.MaxShrines) Fault(Mix, $"{shrines} shrine stubs, the most is {profile.MaxShrines}");
    }

    /// <summary>The kind reading: a hallway is a corridor, a junction or a connector family, whatever its tags of purpose.</summary>
    private static bool IsDestinationIgnoringPurpose(RoomInfo info, RoomShape shape) =>
        !(info.Tags.Contains("corridor") || info.Tags.Contains("junction") || IsConnector(shape.Family) || shape.Family == "stub");

    /// <summary>M18's reading, written here from the rule rather than taken from the generator.</summary>
    private static bool IsDestination(RoomInfo info, RoomShape shape)
    {
        var connector = info.Tags.Contains("corridor") || info.Tags.Contains("junction") || IsConnector(shape.Family) || shape.Family == "stub";
        return info.Tags.Contains("treasure") || (shape.Family == "stub" && info.Tags.Contains("shrine")) || !connector;
    }

    private string Kind(int cell)
    {
        var info = _info[cell]!;
        var shape = _shape[cell]!;
        return info.Tags.Contains("start") || shape.Family == "camp" ? "up"
            : info.Tags.Contains("stairs") ? "down"
            : info.Tags.Contains("boss_arena") ? "arena"
            : shape.Family == "antechamber" ? "antechamber"
            : info.Tags.Contains("treasure") ? "treasure"
            : "fill";
    }

    private IEnumerable<int> Cells() => Enumerable.Range(0, _rows * _columns).Where(c => _info[c] is not null);

    private void Fault(string property, string message) => Faults.Add((property, message));

    private string Where(int cell) => $"row {cell / _columns}, column {cell % _columns}";

    // Sides by index: 0 N (0, +1), 1 E (+1, 0), 2 S (0, -1), 3 W (-1, 0).
    private static readonly (int Dx, int Dy)[] Vector = [(0, 1), (1, 0), (0, -1), (-1, 0)];

    /// <summary>The doors' world sides: each side's vector turned counter-clockwise, (x, y) to (-y, x) per quarter.</summary>
    private static int Turn(Doors doors, int degrees)
    {
        var result = 0;
        for (var side = 0; side < 4; side++)
        {
            if (((int)doors & (1 << side)) == 0) continue;
            var (x, y) = Vector[side];
            for (var q = 0; q < degrees / 90; q++) (x, y) = (-y, x);
            result |= 1 << Array.IndexOf(Vector, (x, y));
        }
        return result;
    }

    private int Across(int cell, int side)
    {
        var (dx, dy) = Vector[side];
        int row = cell / _columns + dy, column = cell % _columns + dx;
        return row < 0 || row >= _rows || column < 0 || column >= _columns ? -1 : row * _columns + column;
    }

    private int SideTowards(int from, int to)
    {
        for (var side = 0; side < 4; side++)
            if (Across(from, side) == to) return side;
        return -1;
    }

    private int[] Distances(int from)
    {
        var d = new int[_rows * _columns];
        Array.Fill(d, -1);
        if (from < 0) return d;
        var queue = new Queue<int>();
        d[from] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            for (var side = 0; side < 4; side++)
            {
                if ((_sides[cell] & (1 << side)) == 0) continue;
                var next = Across(cell, side);
                if (next < 0 || _info[next] is null || d[next] >= 0 || (_sides[next] & (1 << ((side + 2) % 4))) == 0) continue;
                d[next] = d[cell] + 1;
                queue.Enqueue(next);
            }
        }
        return d;
    }
}
