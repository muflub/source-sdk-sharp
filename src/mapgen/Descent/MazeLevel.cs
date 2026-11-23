using System.Globalization;
using SourceSharp.Host.Contracts;
using SourceSharp.MapGen.Descent.Shapes;
using TF2.Descent.Rules.Layout;
using LayoutShape = TF2.Descent.Rules.Layout.RoomShape;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// Level 5's layout (brief p18, rulings M35 and M36): the lift's last station, the quiet lobby and the
/// antechamber in a row to the west, each a room of 2 by 2 cells of the 512 kit, then a square maze of
/// <see cref="Block"/> by <see cref="Block"/> cells round Sir Nukesalot's arena (the centre 2 by 2, one
/// room), with the way down beside the arena on the east (2 by 2, its one door into the arena). Every
/// other cell of the block is a maze room of its own (<see cref="MazeShape"/>), so the automap shows the
/// maze cell by cell. The doors between maze cells are a seeded spanning tree over them, plus
/// <see cref="Loops"/> more that make loops so packs can flank; exactly two doors open into the arena,
/// north and south; the antechamber opens on one cell of the block's west side. Cells the tree leaves as
/// leaves are the dead ends (M35 lifts M18 for this maze). The same seed lays the same maze.
/// </summary>
/// <remarks>
/// The rules module's boss row (<c>LevelLayout.BossRow</c>) lays a boss level as one row; this block is
/// laid here, in the map generator, until the rules module lays it (the report of brief p18). The M16
/// room budget does not count the maze's cells (M36); the level holds under <see cref="MaxRooms"/>,
/// since the automap sends a room's number as a byte.
/// </remarks>
public static class MazeLevel
{
    /// <summary>The depth laid as a maze.</summary>
    public const int Depth = 5;

    /// <summary>The block's cells a side.</summary>
    public const int Block = 10;

    /// <summary>Doors beyond the spanning tree, each making a loop.</summary>
    public const int Loops = 6;

    /// <summary>The fewest doors along the tree between a loop door's two cells: a loop worth flanking by.</summary>
    public const int LoopSpan = 8;

    /// <summary>The level's rooms stay under this: the automap's room number is a byte.</summary>
    public const int MaxRooms = 255;

    /// <summary>The cells a side of the way in, the lobby, the antechamber, the arena and the way down.</summary>
    public const int Big = 2;

    /// <summary>The block's first column in the level grid: the row's three rooms west of it.</summary>
    public const int BlockColumn = 3 * Big;

    /// <summary>The arena's south-west cell in the block.</summary>
    public static (int X, int Y) Arena => (Block / 2 - 1, Block / 2 - 1);

    /// <summary>The way down's south-west cell in the block: east of the arena, its socket on the arena's east door's row.</summary>
    public static (int X, int Y) Exit => (Arena.X + Big, Arena.Y + ArenaShape.MagazineExit.Offset);

    /// <summary>The maze cell north of the arena's north door, and south of its south door.</summary>
    public static (int X, int Y) NorthDoorCell => (Arena.X + ArenaShape.MagazineNorth.Offset, Arena.Y + Big);

    /// <inheritdoc cref="NorthDoorCell"/>
    public static (int X, int Y) SouthDoorCell => (Arena.X + ArenaShape.MagazineSouth.Offset, Arena.Y - 1);

    /// <summary>Whether a block cell is the arena's or the way down's (no maze room).</summary>
    public static bool Reserved((int X, int Y) c) =>
        (c.X >= Arena.X && c.X < Arena.X + Big && c.Y >= Arena.Y && c.Y < Arena.Y + Big)
        || (c.X >= Exit.X && c.X < Exit.X + Big && c.Y >= Exit.Y && c.Y < Exit.Y + Big);

    /// <summary>The block's maze cells, row by row from the south-west.</summary>
    public static IReadOnlyList<(int X, int Y)> MazeCells { get; } =
        [.. Enumerable.Range(0, Block * Block).Select(i => (i % Block, i / Block)).Where(c => !Reserved(c))];

    /// <summary>The maze as laid: each maze cell's door mask, the loop doors, and the row the way in joins the block on.</summary>
    public sealed record Maze(IReadOnlyDictionary<(int X, int Y), Doors> Masks, IReadOnlyList<((int X, int Y) A, (int X, int Y) B)> LoopDoors, int EntryRow)
    {
        /// <summary>The maze cells with one door: the dead ends.</summary>
        public int DeadEnds => Masks.Values.Count(m => DoorSets.Count(m) == 1);
    }

    /// <summary>The seeded maze: a spanning tree over the maze cells (randomised Kruskal), the loop doors, the arena's two, the way in's.</summary>
    public static Maze Lay(ulong seed)
    {
        var rng = new RoomSeed(seed, "maze-level");
        int entryRow = rng.Int(0, Block - Big + 1);
        var index = MazeCells.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i);
        var edges = new List<((int X, int Y) A, (int X, int Y) B)>();
        foreach (var c in MazeCells)
            foreach ((int X, int Y) n in new (int X, int Y)[] { (c.X + 1, c.Y), (c.X, c.Y + 1) })
                if (index.ContainsKey(n))
                    edges.Add((c, n));
        Shuffle(edges, rng);

        var parent = Enumerable.Range(0, MazeCells.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        var masks = MazeCells.ToDictionary(c => c, _ => Doors.None);
        var tree = new Dictionary<(int, int), List<(int X, int Y)>>();
        foreach (var c in MazeCells)
            tree[c] = [];
        var rest = new List<((int X, int Y) A, (int X, int Y) B)>();
        foreach (var e in edges)
        {
            int a = Find(index[e.A]), b = Find(index[e.B]);
            if (a == b)
            {
                rest.Add(e);
                continue;
            }

            parent[a] = b;
            Join(masks, e.A, e.B);
            tree[e.A].Add(e.B);
            tree[e.B].Add(e.A);
        }

        // Loop doors between cells far apart along the tree, so a pack has another way round.
        var loops = new List<((int X, int Y) A, (int X, int Y) B)>();
        foreach (var e in rest)
        {
            if (loops.Count == Loops)
                break;
            if (TreeDistance(tree, e.A, e.B) < LoopSpan)
                continue;
            Join(masks, e.A, e.B);
            loops.Add(e);
        }

        masks[NorthDoorCell] |= Doors.South;
        masks[SouthDoorCell] |= Doors.North;
        masks[(0, entryRow)] |= Doors.West;
        return new Maze(masks, loops, entryRow);
    }

    private static void Join(Dictionary<(int X, int Y), Doors> masks, (int X, int Y) a, (int X, int Y) b)
    {
        Doors side = b.X > a.X ? Doors.East : b.X < a.X ? Doors.West : b.Y > a.Y ? Doors.North : Doors.South;
        masks[a] |= side;
        masks[b] |= DoorSets.Opposite(side);
    }

    private static int TreeDistance(Dictionary<(int, int), List<(int X, int Y)>> tree, (int X, int Y) from, (int X, int Y) to)
    {
        var seen = new Dictionary<(int, int), int> { [from] = 0 };
        var queue = new Queue<(int X, int Y)>([from]);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (c == to)
                return seen[c];
            foreach (var n in tree[c])
                if (seen.TryAdd(n, seen[c] + 1))
                    queue.Enqueue(n);
        }

        return int.MaxValue;
    }

    private static void Shuffle<T>(List<T> items, RoomSeed rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Int(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// The level's plan from the library's rooms: the row's rooms (the way in, the quiet lobby, an antechamber
    /// the seed draws), the arena, the way down, and each maze cell's room by its door mask, its variant drawn
    /// by the seed while the level's breakables (<paramref name="breakablesOf"/>, from <c>library.json</c>)
    /// stay within <see cref="Breakables.LevelCap"/>. Null and the reason when the library lacks a room.
    /// </summary>
    public static (LevelPlan? Plan, Maze? Maze, string? Error) Plan(ulong seed, IReadOnlyList<RoomInfo> rooms, Func<string, int> breakablesOf)
    {
        Maze maze = Lay(seed);
        var rng = new RoomSeed(seed, "maze-rooms");
        var shapes = rooms.Select(r => (Info: r, Ok: RoomName.TryParse(r.Name, out var s, out _), Shape: s)).Where(x => x.Ok).Select(x => (x.Info.Name, Shape: x.Shape!)).ToList();
        string? One(Func<LayoutShape, bool> match, bool draw = false)
        {
            var found = shapes.Where(s => match(s.Shape)).Select(s => s.Name).Order(StringComparer.Ordinal).ToList();
            return found.Count == 0 ? null : draw ? rng.Pick(found) : found[0];
        }

        string? arrival = One(s => s.Family == "arrival");
        string? lobby = One(s => s.Family == "drift" && s.Quiet);
        string? antechamber = One(s => s.Family == RoomTags.AntechamberFamily, draw: true);
        string? arena = One(s => s.Family == "arena");
        string? exit = One(s => s.Family == "exit");
        foreach (var (what, name) in (ReadOnlySpan<(string, string?)>)[("way in", arrival), ("lobby", lobby), ("antechamber", antechamber), ("arena", arena), ("way down", exit)])
            if (name is null)
                return (null, null, $"depth {Depth}: the library has no {what}");

        int row = maze.EntryRow;
        var placements = new List<Placement>
        {
            new(arrival!, row, 0, 0, "up"),
            new(lobby!, row, Big, 0, "path"),
            new(antechamber!, row, 2 * Big, 0, "antechamber"),
            new(arena!, Arena.Y, BlockColumn + Arena.X, 0, "arena"),
            new(exit!, Exit.Y, BlockColumn + Exit.X, 0, "down"),
        };
        int breakables = placements.Sum(p => breakablesOf(p.Room));
        foreach (var c in MazeCells)
        {
            Doors mask = maze.Masks[c];
            string family = MazeShape.FamilyOf(mask);
            string? Variant(char v) => One(s => s.Family == family && s.Variant == MazeShape.VariantPrefix + v && s.Doors == mask && !s.IsMultiCell);
            char pick = rng.Pick(MazeShape.VariantLetters);
            string? room = Variant(pick);
            // Past the level's cap the cell takes whichever variant holds fewest.
            if (room is not null && breakables + breakablesOf(room) > Breakables.LevelCap)
                room = MazeShape.VariantLetters.Select(Variant).OfType<string>().MinBy(breakablesOf);
            if (room is null)
                return (null, null, string.Create(CultureInfo.InvariantCulture, $"depth {Depth}: the library has no maze cell of family {family} with doors {mask}"));
            breakables += breakablesOf(room);
            placements.Add(new(room, c.Y, BlockColumn + c.X, 0, "path"));
        }

        return (new LevelPlan(Block, BlockColumn + Block, placements, arrival!, exit!), maze, null);
    }

    /// <summary>The level file's comments: what the generator laid, the rooms and joints in the shape a reader parses (<c>rooms N, joints M</c>).</summary>
    public static IReadOnlyList<string> Comments(ulong seed, LevelPlan plan, Maze maze)
    {
        // The maze's own doors (each counted from both its cells), less the arena's two and the way in's, which only
        // a maze cell counts; then those three, the way down's, and the row's two.
        int sides = MazeCells.Sum(c => DoorSets.Count(maze.Masks[c]));
        int joints = (sides - 3) / 2 + 3 + 1 + 2;
        return
        [
            string.Create(CultureInfo.InvariantCulture, $"generated by src/mapgen (descent-level, MazeLevel): depth {Depth}, seed {seed}"),
            string.Create(CultureInfo.InvariantCulture,
                $"rooms {plan.Placements.Count}, joints {joints}, loops {maze.LoopDoors.Count}, dead ends {maze.DeadEnds}, maze cells {MazeCells.Count}, entry row {maze.EntryRow}"),
        ];
    }
}
