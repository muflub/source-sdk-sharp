using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>Where a thing is on the map, in words a person can use (§3.6): each part empty when it says nothing here.</summary>
public readonly record struct ChatPlace(string End, string Side, string Floor, string Near, bool ByLadder, bool UpALadder);

/// <summary>
/// Describes places on a map that has no place names (plan_hl2mp_ai §3.6): which end of the
/// map's long axis, which side across it (north is up on the minimap, as in the map's own
/// +Y), which floor (bands of the nav's floor heights), what stands beside it, and whether
/// a ladder is involved. Everything comes from what a bot knows before it plays (§5.0): the
/// item and spawn lists, and the nav graph where there is one. Pure, built once per level;
/// the ladder question walks the graph once, on the main thread, when it is first asked.
/// </summary>
public sealed class ChatPlaces
{
    /// <summary>A gap this tall in the floor heights divides two floors.</summary>
    public const float FloorGap = 48f;

    /// <summary>A band of floor needs this share of the map's floor area to be a floor at all, not a ramp or a ledge.</summary>
    public const float FloorShare = 0.03f;

    /// <summary>The height of a band of floor.</summary>
    public const float FloorBin = 16f;

    /// <summary>How far something may stand from a thing to be what it stands by.</summary>
    public const float NearRange = 220f;

    /// <summary>How far a ladder may be to say "by the ladder".</summary>
    public const float LadderRange = 160f;

    /// <summary>How far a spawn may be to say "by a spawn".</summary>
    public const float SpawnRange = 128f;

    /// <summary>The short axis must be this long before a map has sides.</summary>
    public const float MinSideSpan = 800f;

    private readonly MapKnowledge _map;
    private readonly NavGraph? _graph;
    private readonly float _minX, _maxX, _minY, _maxY;
    private readonly float[] _floorTops;
    private bool[]? _walkable;

    public ChatPlaces(MapKnowledge map, NavGraph? graph)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _graph = graph;
        (_minX, _maxX, _minY, _maxY) = Bounds(map, graph);
        _floorTops = Floors(map, graph);
    }

    /// <summary>How many floors the map has.</summary>
    public int FloorCount => _floorTops.Length + 1;

    /// <summary>The map's long side, in world units.</summary>
    public float LongSide => MathF.Max(_maxX - _minX, _maxY - _minY);

    private bool LongIsY => _maxY - _minY >= _maxX - _minX;

    /// <summary>The end of the long axis <paramref name="p"/> is in.</summary>
    public string EndOf(AiVector p)
    {
        (float t, string low, string high) = LongIsY ? (Share(p.Y, _minY, _maxY), "south", "north") : (Share(p.X, _minX, _maxX), "west", "east");
        return t < 1f / 3f ? low + " end" : t > 2f / 3f ? high + " end" : "middle";
    }

    /// <summary>The side of the short axis <paramref name="p"/> is on; empty in its middle or on a narrow map.</summary>
    public string SideOf(AiVector p)
    {
        float span = LongIsY ? _maxX - _minX : _maxY - _minY;
        if (span < MinSideSpan)
            return "";
        (float t, string low, string high) = LongIsY ? (Share(p.X, _minX, _maxX), "west", "east") : (Share(p.Y, _minY, _maxY), "south", "north");
        return t < 0.4f ? low : t > 0.6f ? high : "";
    }

    /// <summary>The floor <paramref name="p"/> is on, in words; empty on a single floor.</summary>
    public string FloorOf(AiVector p)
    {
        if (_floorTops.Length == 0)
            return "";
        int floor = 0;
        while (floor < _floorTops.Length && p.Z > _floorTops[floor])
            floor++;
        return floor == 0 ? "downstairs" : floor == _floorTops.Length ? "upstairs" : "on the middle floor";
    }

    /// <summary>What <paramref name="item"/> stands by, of what may be named: <paramref name="nameOf"/> answers null for anything a player may not be pointed to.</summary>
    public ChatPlace Describe(AiVector p, Func<ItemSpawn, string?> nameOf, string? notThis = null)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        return new ChatPlace(EndOf(p), SideOf(p), FloorOf(p), Near(p, nameOf, notThis), ByLadder(p), NeedsLadder(p));
    }

    private string Near(AiVector p, Func<ItemSpawn, string?> nameOf, string? notThis)
    {
        string best = "";
        float bestDistance = NearRange;
        foreach (ItemSpawn item in _map.Items)
        {
            float d = Distance(item.Position, p);
            if (d >= bestDistance || d < 1f || nameOf(item) is not { } name || name == notThis)
                continue;
            bestDistance = d;
            best = name;
        }

        if (best.Length > 0)
            return best;
        foreach (AiVector spawn in _map.Spawns)
        {
            if (Distance(spawn, p) < SpawnRange)
                return "a spawn";
        }

        return "";
    }

    /// <summary>Whether a ladder's foot or top is close by.</summary>
    public bool ByLadder(AiVector p)
    {
        if (_graph is null)
            return false;
        var near = new List<int>();
        _graph.CellsNear(p.X - LadderRange, p.Y - LadderRange, p.X + LadderRange, p.Y + LadderRange, near);
        foreach (int cell in near)
        {
            NavCell c = _graph.Cell(cell);
            if ((c.Flags & NavCellFlags.Ladder) != 0 && MathF.Abs(c.FloorZ - p.Z) < LadderRange)
                return true;
            foreach (NavEdge e in _graph.EdgesFrom(cell))
            {
                if (e.Type == NavEdgeType.Ladder && MathF.Abs(c.FloorZ - p.Z) < LadderRange)
                    return true;
            }
        }

        return false;
    }

    /// <summary>The ladder nearest <paramref name="from"/>, or the map's first without one; null on a map with none or no graph.</summary>
    public AiVector? LadderNear(AiVector? from)
    {
        if (_graph is null)
            return null;
        AiVector? best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < _graph.CellCount; i++)
        {
            bool ladder = (_graph.Cell(i).Flags & NavCellFlags.Ladder) != 0;
            foreach (NavEdge e in _graph.EdgesFrom(i))
                ladder |= e.Type == NavEdgeType.Ladder;
            if (!ladder)
                continue;
            AiVector at = _graph.Cell(i).Centre;
            if (from is not { } origin)
                return at;
            float d = Distance(at, origin);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = at;
            }
        }

        return best;
    }

    /// <summary>Whether <paramref name="p"/> can only be reached from the spawns by climbing a ladder.</summary>
    public bool NeedsLadder(AiVector p)
    {
        if (_graph is null)
            return false;
        _walkable ??= WalkableFromSpawns(_graph);
        CellId cell = _graph.FindCell(p);
        if (!cell.IsValid)
            return false;
        return !_walkable[cell.Index];
    }

    private bool[] WalkableFromSpawns(NavGraph graph)
    {
        var seen = new bool[graph.CellCount];
        var open = new Stack<int>();
        foreach (AiVector spawn in _map.Spawns)
        {
            CellId start = graph.FindCell(spawn);
            if (start.IsValid && !seen[start.Index])
            {
                seen[start.Index] = true;
                open.Push(start.Index);
            }
        }

        while (open.TryPop(out int cell))
        {
            foreach (NavEdge e in graph.EdgesFrom(cell))
            {
                if (e.Type != NavEdgeType.Ladder && !seen[e.To])
                {
                    seen[e.To] = true;
                    open.Push(e.To);
                }
            }
        }

        return seen;
    }

    /// <summary>A compass word for the way from <paramref name="from"/> to <paramref name="to"/>, by eighths.</summary>
    public static string Direction(AiVector from, AiVector to)
    {
        float angle = MathF.Atan2(to.Y - from.Y, to.X - from.X) * 180f / MathF.PI;
        int eighth = (int)MathF.Round(angle / 45f);
        return (((eighth % 8) + 8) % 8) switch
        {
            0 => "east",
            1 => "north east",
            2 => "north",
            3 => "north west",
            4 => "west",
            5 => "south west",
            6 => "south",
            _ => "south east",
        };
    }

    public static float Distance(AiVector a, AiVector b)
    {
        float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    private static float Share(float v, float min, float max) => max <= min ? 0.5f : (v - min) / (max - min);

    /// <summary>The parts of the graph a player can stand on: those the spawns are in. A roof the nav mapped and nobody can reach is no floor of the map.</summary>
    private static HashSet<int> Playable(MapKnowledge map, NavGraph graph)
    {
        var parts = new HashSet<int>();
        foreach (AiVector spawn in map.Spawns)
        {
            CellId cell = graph.FindCell(spawn);
            if (cell.IsValid)
                parts.Add(graph.ComponentOf(cell.Index));
        }

        return parts;
    }

    private static (float, float, float, float) Bounds(MapKnowledge map, NavGraph? graph)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        void Take(float x, float y)
        {
            minX = MathF.Min(minX, x);
            maxX = MathF.Max(maxX, x);
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
        }

        foreach (ItemSpawn i in map.Items)
            Take(i.Position.X, i.Position.Y);
        foreach (AiVector s in map.Spawns)
            Take(s.X, s.Y);
        if (graph is not null)
        {
            HashSet<int> playable = Playable(map, graph);
            for (int i = 0; i < graph.CellCount; i++)
            {
                if (playable.Count > 0 && !playable.Contains(graph.ComponentOf(i)))
                    continue;
                NavCell c = graph.Cell(i);
                Take(c.MinX, c.MinY);
                Take(c.MaxX, c.MaxY);
            }
        }

        return minX > maxX ? (0f, 0f, 0f, 0f) : (minX, maxX, minY, maxY);
    }

    /// <summary>
    /// The tops of the floors but the highest. Floors are where the floor area stands: the nav's area
    /// by height in bands of <see cref="FloorBin"/>; a band holding <see cref="FloorShare"/> of the
    /// map is floor, bands within <see cref="FloorGap"/> of each other are one floor, and stairs and
    /// ramps between hold too little to count. With no graph, the item and spawn heights stand in,
    /// each counted the same.
    /// </summary>
    private static float[] Floors(MapKnowledge map, NavGraph? graph)
    {
        var heights = new List<(float Z, float Area)>();
        if (graph is not null)
        {
            HashSet<int> playable = Playable(map, graph);
            for (int i = 0; i < graph.CellCount; i++)
            {
                if (playable.Count > 0 && !playable.Contains(graph.ComponentOf(i)))
                    continue;
                NavCell c = graph.Cell(i);
                heights.Add((c.FloorZ, (c.MaxX - c.MinX) * (c.MaxY - c.MinY)));
            }
        }
        else
        {
            heights.AddRange(map.Items.Select(i => (i.Position.Z, 1f)));
            heights.AddRange(map.Spawns.Select(s => (s.Z, 1f)));
        }

        if (heights.Count == 0)
            return [];
        float total = heights.Sum(h => h.Area);
        var bins = new SortedDictionary<int, float>();
        foreach ((float z, float area) in heights)
        {
            int bin = (int)MathF.Floor(z / FloorBin);
            bins[bin] = bins.GetValueOrDefault(bin) + area;
        }

        var floors = new List<(float Low, float High)>();
        foreach ((int bin, float area) in bins)
        {
            if (area < total * FloorShare)
                continue;
            float low = bin * FloorBin, high = low + FloorBin;
            if (floors.Count > 0 && low - floors[^1].High < FloorGap)
                floors[^1] = (floors[^1].Low, high);
            else
                floors.Add((low, high));
        }

        // Halfway through each gap between floors: something stood on a low step counts as the floor below.
        return [.. floors.Take(floors.Count - 1).Select((f, i) => (f.High + floors[i + 1].Low) * 0.5f)];
    }
}
