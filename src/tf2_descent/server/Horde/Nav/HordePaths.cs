using System.Diagnostics;

using SourceSharp.Ai;
using SourceSharp.Ai.Server.Nav;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Server.Horde.Nav;

/// <summary>
/// The horde's paths over the level's graph (plan_descent_logic 3.6, 3.7; <see cref="IHordePaths"/>),
/// asked by the deciders on the AI workers. A search: the start's and the goal's cells for the
/// class (a body's own cell, or the nearest within reach: <see cref="HordeNavData.CellAt"/>), the
/// core's A* (<see cref="NavPathfinder"/>) with the obstacles as the arena stands
/// (<see cref="Obstruct"/>; every other one open), the cells a standing brush takes from the
/// class's hull blocked (<see cref="ObstaclesFor"/>), the funnel through the
/// cells' shared sides and the shortcut across open ground (<see cref="PathSmoother"/>), then
/// each door the path crosses taken square on (<see cref="DoorApproach"/> before and after its
/// centre along its normal, as the room route crosses it), where the graph has both points.
///
/// <para>
/// Every search is a pure function of its arguments and the graph, on any thread: each thread
/// has its own pathfinder and lists (<see cref="ThreadLocal{T}"/>). The graph is handed over once,
/// when it is ready (<see cref="Ready"/>), and never changes after; the obstacle state it is
/// searched with is replaced whole when the arena changes (<see cref="Obstruct"/>). The graph, its
/// level, the obstacles and <see cref="Epoch"/>, which names the graph and the obstacles together,
/// are one published snapshot: a search reads it once, so it plans with the obstacles its epoch names.
/// </para>
/// </summary>
public sealed class HordePaths : IHordePaths, IDisposable
{
    /// <summary>How far before and after a door's centre a path crosses it.</summary>
    public const float DoorApproach = 48f;

    private readonly ThreadLocal<Scratch> _scratch = new(() => new Scratch(), trackAllValues: false);
    private Published? _now;
    private long _searches, _found, _unreachable, _outOfBudget, _nearest, _ticks, _mostTicks, _pathCells;
    private long _joinsWindowed, _joinsWhole, _joinTicks, _mostJoinTicks;

    public int Epoch => Volatile.Read(ref _now)?.Epoch ?? 0;

    public HordeNavData? Data => Volatile.Read(ref _now)?.Data;

    /// <summary>The obstacles searches plan with now, or none before the graph is ready.</summary>
    public NavObstacleState? Obstacles => Volatile.Read(ref _now)?.Obstacles;

    /// <summary>Searches asked, and how each ended.</summary>
    public long Searches => Interlocked.Read(ref _searches);

    public long Found => Interlocked.Read(ref _found);

    public long Unreachable => Interlocked.Read(ref _unreachable);

    public long OutOfBudget => Interlocked.Read(ref _outOfBudget);

    /// <summary>Searches answered <see cref="PathAnswer.Nearest"/>: a goal off the class's floor, led to the nearest place of the start's.</summary>
    public long Nearest => Interlocked.Read(ref _nearest);

    /// <summary>The cells the found paths hold, all searches (what their length cost).</summary>
    public long CellsOnPaths => Interlocked.Read(ref _pathCells);

    /// <summary>The searches' wall time, in microseconds: all of them, and the longest.</summary>
    public double Micros => Interlocked.Read(ref _ticks) * 1e6 / Stopwatch.Frequency;

    public double MostMicros => Interlocked.Read(ref _mostTicks) * 1e6 / Stopwatch.Frequency;

    /// <summary>Hands the paths the level's graph, every obstacle open: from now on <see cref="Epoch"/> names it.</summary>
    public void Ready(HordeNavData data, HordeLevel level)
    {
        ArgumentNullException.ThrowIfNull(data);
        Ready(data, level, data.Open);
    }

    /// <summary>Hands the paths the level's graph, searched with <paramref name="obstacles"/>: from now on <see cref="Epoch"/> names both.</summary>
    public void Ready(HordeNavData data, HordeLevel level, NavObstacleState obstacles)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(obstacles);
        NavObstacleState[] classes = ByClass(data, obstacles);
        Volatile.Write(ref _now, new Published(data, level, obstacles, classes, Joins(data, obstacles, classes), EpochOf(data.Identity, obstacles)));
    }

    /// <summary>
    /// The arena changed: searches plan with <paramref name="obstacles"/> from now on, under a new
    /// <see cref="Epoch"/>, so a walker holding a path planned before plans again at its next turn.
    /// False (nothing done) before the graph is ready. The state is the paths' from now on: the
    /// caller hands a new one each change, never changes one it handed.
    /// </summary>
    public bool Obstruct(NavObstacleState obstacles)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        if (Volatile.Read(ref _now) is not { } now)
            return false;
        NavObstacleState[] classes = ByClass(now.Data, obstacles);
        Volatile.Write(ref _now, now with { Obstacles = obstacles, Classes = classes, Components = Joins(now.Data, obstacles, classes), Epoch = EpochOf(now.Data.Identity, obstacles) });
        return true;
    }

    /// <summary>
    /// The obstacles a search for <paramref name="cls"/> plans with now: <see cref="Obstacles"/> with
    /// the cells a blocking arena brush takes from that class's hull blocked
    /// (<see cref="HordeArenaObstacles.ByClass"/>), or none before the graph is ready.
    /// </summary>
    public NavObstacleState? ObstaclesFor(ClearanceClass cls) => Volatile.Read(ref _now) is { } now ? now.For(cls) : null;

    /// <summary>How long the last hand-over (<see cref="Ready"/>, <see cref="Obstruct"/>) took making each class's obstacles, in microseconds.</summary>
    public double ClassMicros { get; private set; }

    private NavObstacleState[] ByClass(HordeNavData data, NavObstacleState obstacles)
    {
        long t0 = Stopwatch.GetTimestamp();
        // Each class from its own cells (the props its hull meets), when the state is the level's own with the arena on it.
        NavObstacleState[] classes = HordeArenaObstacles.ByClass(data.Nav, obstacles, ReferenceEquals(obstacles.Cells, data.Open.Cells) ? data.ClassCells : null);
        ClassMicros = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        return classes;
    }

    /// <summary>
    /// Each class's components under its obstacles (indexed as <paramref name="classes"/>), made
    /// the first time a search asks (on an AI worker, never at the hand-over on the game thread):
    /// the level's own, made with every obstacle open (<see cref="HordeNavData.Components"/>), for a
    /// class whose state is the level's open one (nothing blocking, the same cells); else made from
    /// them by flooding windows round the blocking boxes (<see cref="NavComponents.Near"/>, the whole
    /// graph when a window cannot tell). A goal the arena's brushes alone cut off from the start is
    /// then off its floor (<see cref="PathAnswer.Nearest"/>), not searched to the end of its budget or
    /// answered unreachable as the open components had it. The answer is the same whoever makes it, so a search stays a pure function of the epoch's state.
    /// </summary>
    private Lazy<NavComponents>?[] Joins(HordeNavData data, NavObstacleState obstacles, NavObstacleState[] classes)
    {
        bool sameCells = ReferenceEquals(obstacles.Cells, data.Open.Cells);
        var blocking = new List<int>();
        for (int o = 0; o < obstacles.Count; o++)
        {
            if (obstacles.Blocking(o) && !data.Open.Blocking(o))
                blocking.Add(o);
        }

        var joins = new Lazy<NavComponents>?[classes.Length];
        for (int k = 0; k < classes.Length; k++)
        {
            var cls = (ClearanceClass)k;
            if (data.Components(cls) is not { } level)
                continue;
            if (sameCells && blocking.Count == 0 && ReferenceEquals(classes[k].Cells, data.OpenFor(cls).Cells))
            {
                joins[k] = new Lazy<NavComponents>(level);
                continue;
            }

            NavObstacleState state = classes[k];
            bool windowed = sameCells && blocking.Count > 0 && blocking.TrueForAll(o => o < data.Nav.Obstacles.Length);
            var boxes = new List<(float MinX, float MinY, float MaxX, float MaxY)>(blocking.Count);
            if (windowed)
            {
                // What changed lies round the blocking boxes: the cells a class's hull keeps off them, and both ends
                // of every step into their leaves (a leaf names the obstacle that reaches it, a body's width round its box).
                float grow = MathF.Max(HordeHulls.HalfWidthOf(cls) + HordeArenaObstacles.CellHalf, ObstacleReach) + EdgeReach;
                foreach (int o in blocking)
                {
                    NavObstacleInfo b = data.Nav.Obstacles[o];
                    boxes.Add((b.Mins.X - grow, b.Mins.Y - grow, b.Maxs.X + grow, b.Maxs.Y + grow));
                }
            }

            joins[k] = new Lazy<NavComponents>(() => MakeJoins(data, level, cls, state, windowed ? boxes : null), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        return joins;
    }

    private NavComponents MakeJoins(HordeNavData data, NavComponents level, ClearanceClass cls, NavObstacleState state,
                                    List<(float MinX, float MinY, float MaxX, float MaxY)>? boxes)
    {
        long t0 = Stopwatch.GetTimestamp();
        int windows = -1;
        NavComponents made = boxes is null ? NavComponents.Of(data.Graph, cls, state) : NavComponents.Near(level, data.Graph, cls, state, boxes, out windows);
        long spent = Stopwatch.GetTimestamp() - t0;
        Interlocked.Increment(ref windows < 0 ? ref _joinsWhole : ref _joinsWindowed);
        Interlocked.Add(ref _joinTicks, spent);
        long most;
        while (spent > (most = Interlocked.Read(ref _mostJoinTicks)) && Interlocked.CompareExchange(ref _mostJoinTicks, spent, most) != most)
        {
        }

        return made;
    }

    /// <summary>Components made since the paths began (<see cref="Joins"/>): by windows round what changed, and by flooding the whole graph.</summary>
    public long JoinsWindowed => Interlocked.Read(ref _joinsWindowed);

    public long JoinsWhole => Interlocked.Read(ref _joinsWhole);

    /// <summary>The components' making time on the workers, in microseconds: all of it, and the longest.</summary>
    public double JoinMicros => Interlocked.Read(ref _joinTicks) * 1e6 / Stopwatch.Frequency;

    public double MostJoinMicros => Interlocked.Read(ref _mostJoinTicks) * 1e6 / Stopwatch.Frequency;

    /// <summary>How far round its box an obstacle's leaves reach: the game's own body's half-width (the derivation's ring).</summary>
    public const float ObstacleReach = 24f;

    /// <summary>How far past a leaf a step into it starts: one of the built levels' cells.</summary>
    public const float EdgeReach = 16f;

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are in one component of <paramref name="cls"/>'s cells as the obstacles stand now (true before the graph is ready).</summary>
    public bool Joined(CellId a, CellId b, ClearanceClass cls) => Volatile.Read(ref _now) is not { } now || now.Joined(a, b, cls);

    /// <summary>The components <see cref="Joined"/> asks for <paramref name="cls"/> now, or none (before the graph is ready, or a class no robot plans by).</summary>
    public NavComponents? ComponentsFor(ClearanceClass cls) =>
        Volatile.Read(ref _now) is { } now && (uint)cls < (uint)now.Components.Length ? now.Components[(int)cls]?.Value : null;

    /// <summary>The level is going: no graph, epoch 0. A search already running finishes on the graph it read.</summary>
    public void Clear() => Volatile.Write(ref _now, null);

    /// <summary>A graph's epoch: its identity folded to an int, never 0.</summary>
    public static int EpochOf(ulong identity) => (int)(identity ^ (identity >> 32)) | 1;

    /// <summary>
    /// A graph's epoch under <paramref name="obstacles"/>: its identity with each blocking obstacle
    /// folded in (FNV-1a), so every arena state is named apart; with none blocking, the graph's own
    /// <see cref="EpochOf(ulong)"/>.
    /// </summary>
    public static int EpochOf(ulong identity, NavObstacleState obstacles)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        ulong h = identity;
        for (int o = 0; o < obstacles.Count; o++)
        {
            if (obstacles.Blocking(o))
                h = (h ^ (ulong)(o + 1) ^ 0xB10C_0000_0000UL) * 0x100000001B3UL;
        }

        return EpochOf(h);
    }

    public PathAnswer Find(AiVector from, AiVector to, ClearanceClass cls, int budget, Span<AiVector> corners, Span<NavEdgeType> arrive,
                           out int count, out bool truncated)
    {
        count = 0;
        truncated = false;
        if (Volatile.Read(ref _now) is not { } now)
            return PathAnswer.NotReady;
        HordeNavData data = now.Data;
        HordeLevel level = now.Level;
        NavObstacleState obstacles = now.For(cls);

        long t0 = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _searches);
        try
        {
            NavGraph graph = data.Graph;
            CellId start = data.CellAt(from, cls), goal = data.CellAt(to, cls);
            AiVector end = to;
            var found = PathAnswer.Found;
            if (!start.IsValid || !goal.IsValid || !now.Joined(start, goal, cls) || Held(graph, obstacles, goal.Index) || obstacles.CellBlocked(goal.Index))
            {
                // A goal off the class's floor, or in a place a standing arena brush takes (a hero on the exit's
                // cover stands over its pit; a place under landed rubble, held by it or blocked as a cell, whose search would
                // spend its whole budget): the free place of the start's floor nearest it, to hold at.
                int near = start.IsValid ? NearestOnFloor(now, obstacles, start, to, cls) : -1;
                if (near < 0)
                {
                    Interlocked.Increment(ref _unreachable);
                    return PathAnswer.Unreachable;
                }

                Interlocked.Increment(ref _nearest);
                goal = new CellId(near);
                end = graph.Cell(near).Centre;
                found = PathAnswer.Nearest;
                if (near == start.Index || (end - from).Length2D <= HoldUnits)
                    return PathAnswer.Nearest;
            }

            // A goal more than the horizon's doors away: as far as just past the horizon's last door this time.
            if (found == PathAnswer.Found && Horizon(data, level, from, to, cls, out AiVector past, out CellId pastCell) && now.Joined(start, pastCell, cls))
            {
                end = past;
                goal = pastCell;
                truncated = true;
            }

            Scratch s = _scratch.Value!;
            PathStatus status = s.Finder.Find(graph, start, goal, cls, obstacles, s.Cells, budget, out _);
            if (status != PathStatus.Found)
            {
                Interlocked.Increment(ref status == PathStatus.OutOfBudget ? ref _outOfBudget : ref _unreachable);
                return status == PathStatus.OutOfBudget ? PathAnswer.OutOfBudget : PathAnswer.Unreachable;
            }

            Interlocked.Add(ref _pathCells, s.Cells.Count);
            PathSmoother.Funnel(graph, s.Cells, from, end, s.Corners);
            PathSmoother.Shortcut(graph, cls, obstacles, s.Corners);
            SquareDoors(data, level, cls, s.Corners, s.Squared);

            // The corners after the start.
            int n = s.Squared.Count - 1;
            count = Math.Min(n, Math.Min(corners.Length, arrive.Length));
            truncated |= count < n;
            for (int i = 0; i < count; i++)
            {
                corners[i] = s.Squared[i + 1].Point;
                arrive[i] = s.Squared[i + 1].Arrive;
            }

            if (found == PathAnswer.Nearest)
                return PathAnswer.Nearest;
            Interlocked.Increment(ref _found);
            return count > 0 ? PathAnswer.Found : PathAnswer.Unreachable;
        }
        finally
        {
            long spent = Stopwatch.GetTimestamp() - t0;
            Interlocked.Add(ref _ticks, spent);
            long most;
            while (spent > (most = Interlocked.Read(ref _mostTicks)) && Interlocked.CompareExchange(ref _mostTicks, spent, most) != most)
            {
            }
        }
    }

    /// <summary>
    /// Where a search for a goal more than <see cref="HorizonDoors"/> doors away along the room
    /// route stops: <see cref="DoorApproach"/> past the last door of the horizon, on a cell of the
    /// class. A walker asks again from there. False for a goal within the horizon, a point in no
    /// room, or a horizon point the class has no cell at (the whole way is searched then).
    /// </summary>
    private static bool Horizon(HordeNavData data, HordeLevel level, AiVector from, AiVector to, ClearanceClass cls, out AiVector past, out CellId cell)
    {
        past = default;
        cell = CellId.None;
        int room = level.RoomAt(from), goal = level.RoomAt(to);
        if (room < 0 || goal < 0 || room == goal || level.Distance(room, goal) <= HorizonDoors)
            return false;
        HordeDoor? door = null;
        int at = room;
        for (int k = 0; k < HorizonDoors; k++)
        {
            int next = level.NextRoom(at, goal);
            int d = next < 0 ? -1 : level.DoorBetween(at, next);
            if (d < 0)
                return false;
            door = level.Doors[d];
            past = door.Position + door.Into(at) * DoorApproach;
            at = next;
        }

        cell = data.CellAt(past, cls);
        if (!cell.IsValid)
            return false;
        past = past with { Z = data.Graph.Cell(cell).FloorZ };
        return true;
    }

    /// <summary>A body this near the place it is led to hold at is there: no corner.</summary>
    public const float HoldUnits = 40f;

    /// <summary>
    /// The cell of <paramref name="start"/>'s floor for <paramref name="cls"/> (joined to it, free, in no place a standing obstacle takes)
    /// nearest <paramref name="to"/>: on the floor plane, with a unit of height counted twice so a
    /// floor under or over the goal is not taken for it; the lower index on a tie. -1 for none.
    /// </summary>
    private static int NearestOnFloor(Published now, NavObstacleState obstacles, CellId start, AiVector to, ClearanceClass cls)
    {
        NavGraph graph = now.Data.Graph;
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < graph.CellCount; i++)
        {
            NavCell c = graph.Cell(i);
            if (!c.Admits(cls) || obstacles.CellBlocked(i) || !now.Joined(start, new CellId(i), cls) || Held(graph, obstacles, i))
                continue;
            float d = (c.Centre - to).Length2D + 2f * MathF.Abs(c.FloorZ - to.Z);
            if (d < bestD)
                (best, bestD) = (i, d);
        }

        return best;
    }

    /// <summary>Whether <paramref name="cell"/> is a place an obstacle blocking now takes (<see cref="HordeArenaObstacles.Holder"/>).</summary>
    private static bool Held(NavGraph graph, NavObstacleState obstacles, int cell)
    {
        int holder = HordeArenaObstacles.Holder(graph, cell);
        return holder >= 0 && holder < obstacles.Count && obstacles.Blocking(holder);
    }

    /// <summary>How many doors along the room route a search plans at most: the rest is asked from there.</summary>
    public const int HorizonDoors = 2;

    /// <summary>
    /// <paramref name="corners"/> with each walked leg that crosses a door's opening replaced by a
    /// crossing square on: in front of the door, then behind it, each where the graph has a cell
    /// for the class near its floor; a leg that starts or ends at the door is left as it is.
    /// </summary>
    private static void SquareDoors(HordeNavData data, HordeLevel level, ClearanceClass cls, List<PathCorner> corners, List<PathCorner> into)
    {
        into.Clear();
        if (corners.Count == 0)
            return;
        into.Add(corners[0]);
        for (int i = 1; i < corners.Count; i++)
        {
            PathCorner a = corners[i - 1], b = corners[i];
            if (b.Arrive == NavEdgeType.Walk && CrossedDoor(level, a.Point, b.Point, out HordeDoor? door, out AiVector into1) && door is not null)
            {
                AiVector before = door.Position - into1 * DoorApproach, after = door.Position + into1 * DoorApproach;
                if (On(data, ref before, cls) && On(data, ref after, cls)
                    && Flat(a.Point - door.Position) > DoorApproach && Flat(b.Point - door.Position) > DoorApproach)
                {
                    into.Add(new PathCorner(before, NavEdgeType.Walk));
                    into.Add(new PathCorner(after, NavEdgeType.Walk));
                }
            }

            into.Add(b);
        }
    }

    /// <summary>The door whose opening the leg from <paramref name="a"/> to <paramref name="b"/> passes through, and its normal the way the leg goes.</summary>
    private static bool CrossedDoor(HordeLevel level, AiVector a, AiVector b, out HordeDoor? door, out AiVector into)
    {
        foreach (HordeDoor d in level.Doors)
        {
            AiVector n = d.Normal;
            float sa = Dot2(a - d.Position, n), sb = Dot2(b - d.Position, n);
            if ((sa > 0f) == (sb > 0f) || sa == sb)
                continue;
            float t = sa / (sa - sb);
            AiVector x = a + (b - a) * t;
            AiVector off = x - d.Position;
            float lateral = MathF.Abs(off.X * -n.Y + off.Y * n.X);
            if (lateral <= d.Width * 0.5f + 8f && MathF.Abs(x.Z - d.Position.Z) <= 96f)
            {
                door = d;
                into = sb > sa ? new AiVector(n.X, n.Y, 0f).Normalised() : new AiVector(-n.X, -n.Y, 0f).Normalised();
                return true;
            }
        }

        door = null;
        into = default;
        return false;
    }

    /// <summary>Whether a cell of <paramref name="cls"/> holds <paramref name="p"/>'s column near its height; <paramref name="p"/> is put on its floor.</summary>
    private static bool On(HordeNavData data, ref AiVector p, ClearanceClass cls)
    {
        CellId c = data.Graph.FindCell(p + new AiVector(0f, 0f, 40f));
        if (!c.IsValid || !data.Graph.Cell(c).Admits(cls) || MathF.Abs(data.Graph.Cell(c).FloorZ - p.Z) > 64f)
            return false;
        p = p with { Z = data.Graph.Cell(c).FloorZ };
        return true;
    }

    private static float Dot2(AiVector a, AiVector b) => a.X * b.X + a.Y * b.Y;

    private static float Flat(AiVector v) => v.Length2D;

    public void Dispose() => _scratch.Dispose();

    /// <summary>What a search reads, published whole: the graph, its level, the obstacles, each class's of them and its components under them, and the epoch naming them.</summary>
    private sealed record Published(HordeNavData Data, HordeLevel Level, NavObstacleState Obstacles, NavObstacleState[] Classes, Lazy<NavComponents>?[] Components, int Epoch)
    {
        public NavObstacleState For(ClearanceClass cls) => (uint)cls < (uint)Classes.Length ? Classes[(int)cls] : Obstacles;

        public bool Joined(CellId a, CellId b, ClearanceClass cls) =>
            (uint)cls < (uint)Components.Length && Components[(int)cls] is { } comp ? comp.Value.JoinedFrom(Data.Graph, a, b, cls, For(cls)) : Data.Joined(a, b, cls);
    }

    /// <summary>One thread's search state.</summary>
    private sealed class Scratch
    {
        public NavPathfinder Finder { get; } = new();

        public List<CellId> Cells { get; } = new(256);

        public List<PathCorner> Corners { get; } = new(64);

        public List<PathCorner> Squared { get; } = new(64);
    }
}
