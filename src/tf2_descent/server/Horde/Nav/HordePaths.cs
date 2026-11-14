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
/// core's A* (<see cref="NavPathfinder"/>) with every obstacle open, the funnel through the
/// cells' shared sides and the shortcut across open ground (<see cref="PathSmoother"/>), then
/// each door the path crosses taken square on (<see cref="DoorApproach"/> before and after its
/// centre along its normal, as the room route crosses it), where the graph has both points.
///
/// <para>
/// Every search is a pure function of its arguments and the graph, on any thread: each thread
/// has its own pathfinder and lists (<see cref="ThreadLocal{T}"/>). The graph is handed over once,
/// when it is ready (<see cref="Ready"/>), and never changes after; <see cref="Epoch"/> names it.
/// </para>
/// </summary>
public sealed class HordePaths : IHordePaths, IDisposable
{
    /// <summary>How far before and after a door's centre a path crosses it.</summary>
    public const float DoorApproach = 48f;

    private readonly ThreadLocal<Scratch> _scratch = new(() => new Scratch(), trackAllValues: false);
    private HordeNavData? _data;
    private HordeLevel? _level;
    private int _epoch;
    private long _searches, _found, _unreachable, _outOfBudget, _ticks, _mostTicks, _pathCells;

    public int Epoch => Volatile.Read(ref _epoch);

    public HordeNavData? Data => _data;

    /// <summary>Searches asked, and how each ended.</summary>
    public long Searches => Interlocked.Read(ref _searches);

    public long Found => Interlocked.Read(ref _found);

    public long Unreachable => Interlocked.Read(ref _unreachable);

    public long OutOfBudget => Interlocked.Read(ref _outOfBudget);

    /// <summary>The cells the found paths hold, all searches (what their length cost).</summary>
    public long CellsOnPaths => Interlocked.Read(ref _pathCells);

    /// <summary>The searches' wall time, in microseconds: all of them, and the longest.</summary>
    public double Micros => Interlocked.Read(ref _ticks) * 1e6 / Stopwatch.Frequency;

    public double MostMicros => Interlocked.Read(ref _mostTicks) * 1e6 / Stopwatch.Frequency;

    /// <summary>Hands the paths the level's graph: from now on <see cref="Epoch"/> names it.</summary>
    public void Ready(HordeNavData data, HordeLevel level)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _level = level;
        Volatile.Write(ref _epoch, EpochOf(data.Identity));
    }

    /// <summary>The level is going: no graph, epoch 0. A search already running finishes on the graph it read.</summary>
    public void Clear()
    {
        Volatile.Write(ref _epoch, 0);
        _data = null;
        _level = null;
    }

    /// <summary>A graph's epoch: its identity folded to an int, never 0.</summary>
    public static int EpochOf(ulong identity) => (int)(identity ^ (identity >> 32)) | 1;

    public PathAnswer Find(AiVector from, AiVector to, ClearanceClass cls, int budget, Span<AiVector> corners, Span<NavEdgeType> arrive,
                           out int count, out bool truncated)
    {
        count = 0;
        truncated = false;
        if (_data is not { } data || _level is not { } level || Volatile.Read(ref _epoch) == 0)
            return PathAnswer.NotReady;

        long t0 = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _searches);
        try
        {
            NavGraph graph = data.Graph;
            CellId start = data.CellAt(from, cls), goal = data.CellAt(to, cls);
            if (!start.IsValid || !goal.IsValid || !data.Joined(start, goal, cls))
            {
                Interlocked.Increment(ref _unreachable);
                return PathAnswer.Unreachable;
            }

            // A goal more than the horizon's doors away: as far as just past the horizon's last door this time.
            AiVector end = to;
            if (Horizon(data, level, from, to, cls, out AiVector past, out CellId pastCell) && data.Joined(start, pastCell, cls))
            {
                end = past;
                goal = pastCell;
                truncated = true;
            }

            Scratch s = _scratch.Value!;
            PathStatus status = s.Finder.Find(graph, start, goal, cls, data.Open, s.Cells, budget, out _);
            if (status != PathStatus.Found)
            {
                Interlocked.Increment(ref status == PathStatus.OutOfBudget ? ref _outOfBudget : ref _unreachable);
                return status == PathStatus.OutOfBudget ? PathAnswer.OutOfBudget : PathAnswer.Unreachable;
            }

            Interlocked.Add(ref _pathCells, s.Cells.Count);
            PathSmoother.Funnel(graph, s.Cells, from, end, s.Corners);
            PathSmoother.Shortcut(graph, cls, data.Open, s.Corners);
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

    /// <summary>One thread's search state.</summary>
    private sealed class Scratch
    {
        public NavPathfinder Finder { get; } = new();

        public List<CellId> Cells { get; } = new(256);

        public List<PathCorner> Corners { get; } = new(64);

        public List<PathCorner> Squared { get; } = new(64);
    }
}
