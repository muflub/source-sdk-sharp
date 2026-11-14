using System.Collections.Immutable;
using SourceSharp.Ai;

namespace TF2.Descent.Ai.Level;

/// <summary>
/// The room a marker has round it (plan_tf2_rpg 5d, plan_descent_logic HO9): how far a robot
/// standing on it can reach west (−x), east (+x), south (−y) and north (+y) before a wall, a
/// prop or anything else solid, above a step, and how far up to the ceiling. The server
/// measures it at level ready (a level ships none); a robot whose scaled hull it holds can
/// stand there.
/// </summary>
public readonly record struct MarkerClearance(float West, float East, float South, float North, float Height)
{
    /// <summary>The nearest wall, whichever side it is on.</summary>
    public float Narrowest => MathF.Min(MathF.Min(West, East), MathF.Min(South, North));

    /// <summary>Whether a box <paramref name="half"/> wide each way and <paramref name="height"/> tall stands here.</summary>
    public bool Holds(float half, float height) => Narrowest >= half && Height >= height;
}

/// <summary>
/// A <c>descent_spawn_pack</c> marker: where a robot of the room's pack stands, the way it faces
/// (degrees), and the room it has round it, when measured (none: unmeasured, which holds
/// anything, and the director counts it).
/// </summary>
public readonly record struct PackMarker(AiVector Position, float Yaw, MarkerClearance? Clearance = null);

/// <summary>
/// A room as the horde sees it: its id (its index in the level), its bounds, and its pack
/// markers in the order robots take them.
/// </summary>
public sealed record HordeRoom(int Id, AiVector Min, AiVector Max, ImmutableArray<PackMarker> Markers)
{
    public bool Contains(AiVector p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;
}

/// <summary>
/// A door between two rooms: its centre (the middle of its sill), its normal (unit length,
/// pointing from <see cref="RoomA"/> into <see cref="RoomB"/>), its width and its height
/// (unbounded when the level does not say).
/// </summary>
public sealed record HordeDoor(int RoomA, int RoomB, AiVector Position, AiVector Normal, float Width, float Height = float.PositiveInfinity)
{
    /// <summary>The room on the far side from <paramref name="room"/>.</summary>
    public int Other(int room) => room == RoomA ? RoomB : RoomA;

    /// <summary>The unit direction through the door from <paramref name="from"/> into the other room.</summary>
    public AiVector Into(int from) => from == RoomA ? Normal : -Normal;
}

/// <summary>A level's boss: its template, the room that is its arena, and where it stands.</summary>
public sealed record HordeBoss(string Template, int ArenaRoom, PackMarker Spot);

/// <summary>
/// The level as the horde sees it (plan_descent_logic 3.3 to 3.5): rooms, doors, the room
/// graph and its distances, the seed and depth the roster is rolled from, and the boss. A
/// later package (WO1) fills it from what the level ships. Immutable; every query after
/// <see cref="Create"/> reads precomputed tables and allocates nothing.
/// </summary>
public sealed class HordeLevel
{
    /// <summary>The room-graph distance between rooms no door path joins.</summary>
    public const int Unreachable = int.MaxValue;

    private readonly int[] _hops;          // rooms × rooms, in doors crossed
    private readonly int[] _next;          // rooms × rooms: the neighbour of a shortest route, -1 for none
    private readonly int[] _doorOf;        // rooms × rooms: the door joining two neighbours, -1 for none
    private readonly int[][] _roomDoors;   // per room, its doors
    private readonly float[] _doorPath;    // doors × doors: the shortest walk between two doors, through doors

    private HordeLevel(ulong seed, int depth, ImmutableArray<HordeRoom> rooms, ImmutableArray<HordeDoor> doors, HordeBoss? boss,
        int[] hops, int[] next, int[] doorOf, int[][] roomDoors, float[] doorPath)
    {
        Seed = seed;
        Depth = depth;
        Rooms = rooms;
        Doors = doors;
        Boss = boss;
        _hops = hops;
        _next = next;
        _doorOf = doorOf;
        _roomDoors = roomDoors;
        _doorPath = doorPath;
    }

    public ulong Seed { get; }
    public int Depth { get; }
    public ImmutableArray<HordeRoom> Rooms { get; }
    public ImmutableArray<HordeDoor> Doors { get; }
    public HordeBoss? Boss { get; }
    public int RoomCount => Rooms.Length;

    /// <summary>Doors crossed on the shortest route from <paramref name="a"/> to <paramref name="b"/>; <see cref="Unreachable"/> when none.</summary>
    public int Distance(int a, int b) => _hops[a * RoomCount + b];

    /// <summary>The neighbour of <paramref name="from"/> a shortest route to <paramref name="to"/> enters next (the lowest id among ties); -1 when already there or unreachable.</summary>
    public int NextRoom(int from, int to) => _next[from * RoomCount + to];

    /// <summary>The door joining neighbours <paramref name="a"/> and <paramref name="b"/>; -1 when they do not touch.</summary>
    public int DoorBetween(int a, int b) => _doorOf[a * RoomCount + b];

    /// <summary>The doors of <paramref name="room"/>.</summary>
    public ReadOnlySpan<int> DoorsOf(int room) => _roomDoors[room];

    /// <summary>The first room whose bounds hold <paramref name="p"/>; -1 when none does.</summary>
    public int RoomAt(AiVector p)
    {
        for (int i = 0; i < Rooms.Length; i++)
            if (Rooms[i].Contains(p)) return i;
        return -1;
    }

    /// <summary>
    /// How far a sound walks from <paramref name="a"/> in room <paramref name="roomA"/> to
    /// <paramref name="b"/> in room <paramref name="roomB"/>: a straight line inside one room,
    /// else the shortest walk out through one of A's doors, door to door, and in through one
    /// of B's. It never goes through a wall: <see cref="float.PositiveInfinity"/> when no door
    /// path joins the rooms.
    /// </summary>
    public float PathDistance(int roomA, AiVector a, int roomB, AiVector b)
    {
        if (roomA == roomB) return a.DistanceTo(b);
        float best = float.PositiveInfinity;
        int n = Doors.Length;
        foreach (int da in _roomDoors[roomA])
        {
            float toDoor = a.DistanceTo(Doors[da].Position);
            foreach (int db in _roomDoors[roomB])
            {
                float d = toDoor + _doorPath[da * n + db] + Doors[db].Position.DistanceTo(b);
                if (d < best) best = d;
            }
        }
        return best;
    }

    /// <summary>
    /// Builds a level and its tables. Room ids are their indices (0, 1, ...); a door joins two
    /// different rooms of the level, at most one door a pair; the boss's arena is a room.
    /// Throws naming the first input that breaks one.
    /// </summary>
    public static HordeLevel Create(ulong seed, int depth, IEnumerable<HordeRoom> rooms, IEnumerable<HordeDoor> doors, HordeBoss? boss = null)
    {
        var r = rooms.ToImmutableArray();
        var d = doors.ToImmutableArray();
        int n = r.Length;
        for (int i = 0; i < n; i++)
            if (r[i].Id != i)
                throw new ArgumentException($"room {i} has id {r[i].Id}: a room's id is its index", nameof(rooms));

        var doorOf = new int[n * n];
        Array.Fill(doorOf, -1);
        var perRoom = new List<int>[n];
        for (int i = 0; i < n; i++) perRoom[i] = [];
        for (int k = 0; k < d.Length; k++)
        {
            var door = d[k];
            if ((uint)door.RoomA >= (uint)n || (uint)door.RoomB >= (uint)n || door.RoomA == door.RoomB)
                throw new ArgumentException($"door {k} joins rooms {door.RoomA} and {door.RoomB}: a door joins two rooms of the level", nameof(doors));
            if (doorOf[door.RoomA * n + door.RoomB] >= 0)
                throw new ArgumentException($"door {k} is a second door between rooms {door.RoomA} and {door.RoomB}", nameof(doors));
            doorOf[door.RoomA * n + door.RoomB] = k;
            doorOf[door.RoomB * n + door.RoomA] = k;
            perRoom[door.RoomA].Add(k);
            perRoom[door.RoomB].Add(k);
        }
        if (boss is not null && (uint)boss.ArenaRoom >= (uint)n)
            throw new ArgumentException($"the boss's arena is room {boss.ArenaRoom}, not a room of the level", nameof(boss));

        // Hops: a breadth-first walk from every room. Next: for each target, the neighbour with
        // one hop less, the lowest id winning ties, so a route is one answer on every machine.
        var hops = new int[n * n];
        Array.Fill(hops, Unreachable);
        var queue = new int[n];
        for (int s = 0; s < n; s++)
        {
            int head = 0, tail = 0;
            hops[s * n + s] = 0;
            queue[tail++] = s;
            while (head < tail)
            {
                int u = queue[head++];
                foreach (int k in perRoom[u])
                {
                    int v = d[k].Other(u);
                    if (hops[s * n + v] != Unreachable) continue;
                    hops[s * n + v] = hops[s * n + u] + 1;
                    queue[tail++] = v;
                }
            }
        }
        var next = new int[n * n];
        Array.Fill(next, -1);
        for (int from = 0; from < n; from++)
            for (int to = 0; to < n; to++)
            {
                int h = hops[from * n + to];
                if (from == to || h == Unreachable) continue;
                int pick = -1;
                foreach (int k in perRoom[from])
                {
                    int v = d[k].Other(from);
                    if (hops[v * n + to] == h - 1 && (pick < 0 || v < pick)) pick = v;
                }
                next[from * n + to] = pick;
            }

        // Door to door: two doors of one room are a straight walk apart; Floyd-Warshall over that.
        int nd = d.Length;
        var path = new float[nd * nd];
        Array.Fill(path, float.PositiveInfinity);
        for (int k = 0; k < nd; k++) path[k * nd + k] = 0;
        for (int room = 0; room < n; room++)
            foreach (int a in perRoom[room])
                foreach (int b in perRoom[room])
                    if (a != b)
                    {
                        float len = d[a].Position.DistanceTo(d[b].Position);
                        if (len < path[a * nd + b]) path[a * nd + b] = len;
                    }
        for (int k = 0; k < nd; k++)
            for (int i = 0; i < nd; i++)
            {
                float ik = path[i * nd + k];
                if (float.IsPositiveInfinity(ik)) continue;
                for (int j = 0; j < nd; j++)
                {
                    float via = ik + path[k * nd + j];
                    if (via < path[i * nd + j]) path[i * nd + j] = via;
                }
            }

        var roomDoors = new int[n][];
        for (int i = 0; i < n; i++) roomDoors[i] = [.. perRoom[i]];
        return new HordeLevel(seed, depth, r, d, boss, hops, next, doorOf, roomDoors, path);
    }

    /// <summary>
    /// This level with each pack marker's clearance as <paramref name="clearance"/> answers it
    /// (room id, marker index, the marker; null leaves it unmeasured); everything else the same.
    /// </summary>
    public HordeLevel WithClearances(Func<int, int, PackMarker, MarkerClearance?> clearance)
    {
        var rooms = Rooms.Select(room => room with
        {
            Markers = [.. room.Markers.Select((m, k) => m with { Clearance = clearance(room.Id, k, m) })],
        });
        return Create(Seed, Depth, rooms, Doors, Boss);
    }
}
