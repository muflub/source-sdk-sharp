using SourceSharp.Host.Contracts;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Shapes;
using SourceSharp.MapGen.Descent.Themes;
using TF2.Descent.Rules.Layout;
using Xunit;
using LayoutShape = TF2.Descent.Rules.Layout.RoomShape;

namespace SourceSharp.Tests;

/// <summary>
/// Level 5 as a square maze round a narrowed boss arena (brief p18, rulings M35 and M36): a 10 by 10 block of
/// 512 cells, the arena one room on its centre 2 by 2, the way down beside it joined to it alone, every other
/// cell a maze room of its own built from the door-mask family, the maze a seeded spanning tree with loops,
/// entered from the way in and opening into the arena by exactly two doors.
/// </summary>
public sealed class DescentMazeLevelTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    private (LevelPlan Plan, MazeLevel.Maze Maze) Laid(ulong seed)
    {
        var (plan, maze, error) = MazeLevel.Plan(seed, libs.Pool(5), Breakables(5));
        Assert.True(error is null, error);
        return (plan!, maze!);
    }

    private Func<string, int> Breakables(int depth)
    {
        var counts = libs.Rooms(depth, 0).ToDictionary(r => r.Name, r => MapGen.Descent.Breakables.Of(r).Count);
        return name => counts.GetValueOrDefault(name);
    }

    private static LayoutShape Shape(Placement p) => RoomName.Parse(p.Room);

    /// <summary>The level grid's cells a placement covers.</summary>
    private static IEnumerable<(int Row, int Column)> Covered(Placement p)
    {
        var s = Shape(p);
        for (int r = 0; r < s.CellsY; r++)
            for (int c = 0; c < s.CellsX; c++)
                yield return (p.Row + r, p.Column + c);
    }

    public static TheoryData<ulong> Seeds => [1, 2, 3];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_block_is_10_by_10_cells_every_one_covered_once(ulong seed)
    {
        var (plan, _) = Laid(seed);
        var cover = plan.Placements.SelectMany(p => Covered(p).Select(c => (c, p.Room))).GroupBy(x => x.c).ToDictionary(g => g.Key, g => g.Count());

        var block = Enumerable.Range(0, MazeLevel.Block).SelectMany(r => Enumerable.Range(MazeLevel.BlockColumn, MazeLevel.Block).Select(c => (r, c))).ToArray();
        Assert.Equal(10, MazeLevel.Block);
        Assert.Equal(100, block.Length);
        Assert.All(block, c => Assert.True(cover.GetValueOrDefault(c) == 1, $"block cell {c} is covered {cover.GetValueOrDefault(c)} times"));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_layout_holds_one_arena_room_covering_the_blocks_centre_2_by_2(ulong seed)
    {
        var (plan, _) = Laid(seed);
        var arenas = plan.Placements.Where(p => Shape(p).Family == "arena").ToArray();

        var only = Assert.Single(arenas);
        int c0 = MazeLevel.BlockColumn + MazeLevel.Block / 2 - 1, r0 = MazeLevel.Block / 2 - 1;
        Assert.Equal([(r0, c0), (r0, c0 + 1), (r0 + 1, c0), (r0 + 1, c0 + 1)], Covered(only).Order().ToArray());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_level_holds_under_255_rooms_and_96_breakables(ulong seed)
    {
        var (plan, _) = Laid(seed);
        var count = Breakables(5);

        Assert.InRange(plan.Placements.Count, MazeLevel.MazeCells.Count + 5, MazeLevel.MaxRooms - 1);
        Assert.InRange(plan.Placements.Sum(p => count(p.Room)), 1, MapGen.Descent.Breakables.LevelCap);
    }

    [Fact]
    public void The_same_seed_lays_the_same_maze_and_another_seed_another()
    {
        string Text(MazeLevel.Maze m) => string.Join(",", m.Masks.OrderBy(kv => kv.Key).Select(kv => (int)kv.Value)) + "/" + m.EntryRow;

        Assert.Equal(Text(MazeLevel.Lay(1)), Text(MazeLevel.Lay(1)));
        Assert.NotEqual(Text(MazeLevel.Lay(1)), Text(MazeLevel.Lay(2)));
        Assert.NotEqual(Text(MazeLevel.Lay(2)), Text(MazeLevel.Lay(3)));
    }

    /// <summary>The maze cells a walk through the doors reaches from the way in's cell.</summary>
    private static HashSet<(int X, int Y)> Reached(MazeLevel.Maze maze)
    {
        var seen = new HashSet<(int X, int Y)> { (0, maze.EntryRow) };
        var queue = new Queue<(int X, int Y)>(seen);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var (side, n) in new (Doors, (int X, int Y))[] { (Doors.East, (c.X + 1, c.Y)), (Doors.West, (c.X - 1, c.Y)), (Doors.North, (c.X, c.Y + 1)), (Doors.South, (c.X, c.Y - 1)) })
                if ((maze.Masks[c] & side) != 0 && maze.Masks.TryGetValue(n, out Doors back) && (back & DoorSets.Opposite(side)) != 0 && seen.Add(n))
                    queue.Enqueue(n);
        }

        return seen;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_maze_cell_is_reached_from_the_way_in(ulong seed)
    {
        var maze = MazeLevel.Lay(seed);

        Assert.True((maze.Masks[(0, maze.EntryRow)] & Doors.West) != 0, "the way in's cell has no west door");
        Assert.Equal(MazeLevel.MazeCells.Count, Reached(maze).Count);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_maze_is_a_spanning_tree_with_its_loops_and_dead_ends(ulong seed)
    {
        var maze = MazeLevel.Lay(seed);
        // Each maze-to-maze door from both its cells; the arena's two and the way in's are counted once.
        int sides = maze.Masks.Values.Sum(DoorSets.Count) - 3;

        Assert.Equal(MazeLevel.MazeCells.Count - 1 + MazeLevel.Loops, sides / 2);
        Assert.Equal(MazeLevel.Loops, maze.LoopDoors.Count);
        Assert.InRange(maze.DeadEnds, 1, MazeLevel.MazeCells.Count / 4);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_arena_is_entered_by_exactly_two_maze_doors_on_opposite_sides(ulong seed)
    {
        var maze = MazeLevel.Lay(seed);
        var (ax, ay) = MazeLevel.Arena;
        var into = new List<Doors>();
        foreach (var (c, mask) in maze.Masks)
        {
            if (c.Y == ay + 2 && c.X >= ax && c.X < ax + 2 && (mask & Doors.South) != 0) into.Add(Doors.North);
            if (c.Y == ay - 1 && c.X >= ax && c.X < ax + 2 && (mask & Doors.North) != 0) into.Add(Doors.South);
            if (c.X == ax - 1 && c.Y >= ay && c.Y < ay + 2 && (mask & Doors.East) != 0) into.Add(Doors.West);
            if (c.X == ax + 2 && c.Y >= ay && c.Y < ay + 2 && (mask & Doors.West) != 0) into.Add(Doors.East);
        }

        Assert.Equal([Doors.North, Doors.South], into.Order().ToArray());
        Room arena = libs.Rooms(5, 0).Single(r => r.Entry.Tags.Contains("boss_arena"));
        Assert.Equal([ArenaShape.MagazineExit, ArenaShape.MagazineNorth, ArenaShape.MagazineSouth], arena.Sockets.ToArray());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_way_down_touches_the_arena_and_its_only_door_leads_into_it(ulong seed)
    {
        var (plan, maze) = Laid(seed);
        Placement exit = plan.Placements.Single(p => p.Role == "down");
        Placement arena = plan.Placements.Single(p => p.Role == "arena");
        Room room = libs.Rooms(5, 0).Single(r => r.Name == exit.Room);

        Assert.Equal([(CellFace.West, 0)], room.Sockets.ToArray());
        // Its west door's cell edge is the arena's east door's.
        Assert.Equal(arena.Column + 2, exit.Column);
        Assert.Equal(arena.Row + ArenaShape.MagazineExit.Offset, exit.Row);
        // No maze cell opens toward it.
        var (ex, ey) = MazeLevel.Exit;
        foreach (var (c, mask) in maze.Masks)
        {
            bool beside = (c.X == ex - 1 || c.X == ex + 2) && c.Y >= ey && c.Y < ey + 2 || (c.Y == ey - 1 || c.Y == ey + 2) && c.X >= ex && c.X < ex + 2;
            Doors toward = c.X == ex - 1 ? Doors.East : c.X == ex + 2 ? Doors.West : c.Y == ey - 1 ? Doors.North : Doors.South;
            Assert.False(beside && (mask & toward) != 0, $"maze cell {c} opens toward the way down");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_maze_cell_is_the_door_mask_familys_room_for_its_doors_unturned(ulong seed)
    {
        var (plan, maze) = Laid(seed);
        var cells = plan.Placements.Where(p => p.Column >= MazeLevel.BlockColumn && MazeShape.IsMaze(p.Room)).ToArray();

        Assert.Equal(MazeLevel.MazeCells.Count, cells.Length);
        Assert.All(cells, p =>
        {
            Doors mask = maze.Masks[(p.Column - MazeLevel.BlockColumn, p.Row)];
            Assert.Equal(0, p.Rotation);
            Assert.Equal(mask, Shape(p).Doors);
            Assert.Equal(MazeShape.FamilyOf(mask), Shape(p).Family);
        });
    }

    [Fact]
    public void Level_5s_library_holds_its_row_its_arena_its_way_down_the_lobby_and_the_maze()
    {
        var built = libs.Built(5);
        var families = built.Where(r => !r.Entry.Quiet && !MazeShape.IsMaze(r.Name)).Select(r => RoomName.Parse(r.Name).Family).Distinct().Order();

        Assert.Equal(["antechamber", "arena", "arrival", "exit"], families);
        Assert.Equal("drift", RoomName.Parse(Assert.Single(built, r => r.Entry.Quiet).Name).Family);
        Assert.All(built.Where(r => !MazeShape.IsMaze(r.Name)), r => Assert.Equal(2, r.Cells));
    }

    [Fact]
    public void The_door_mask_family_builds_every_mask_once_a_variant_on_the_512_kit()
    {
        var maze = libs.Rooms(5, 0).Where(r => MazeShape.IsMaze(r.Name)).ToArray();

        Assert.Equal(15 * MazeShape.VariantLetters.Count, maze.Length);
        Assert.All(maze, r => Assert.Equal((RoomKit.MazeCell, 1, MazeShape.Height), (r.Kit.Cell, r.Cells, r.Height)));
        foreach (char v in MazeShape.VariantLetters)
            Assert.Equal(Enumerable.Range(1, 15).Select(m => (Doors)m), maze.Where(r => RoomName.Parse(r.Name).Variant == MazeShape.VariantPrefix + v).Select(r => RoomName.Parse(r.Name).Doors).Order());
    }

    [Fact]
    public void A_maze_cells_corridor_is_its_width_from_its_middle_to_each_door_under_a_solid_roof()
    {
        Room cross = libs.Rooms(5, 0).Single(r => r.Name.StartsWith("cross_mazea-", StringComparison.Ordinal));
        var columns = new RoomColumns(cross);
        float m = cross.Extent / 2f, f = cross.Kit.Sill;

        // Open floor across the corridor's width at each arm, and rock either side of it.
        foreach (float along in (float[])[64, 448])
        {
            Assert.Equal<float?>(f, columns.SurfaceUnder(along, m - MazeShape.Width / 2f + 24, f + 40)?.Z);
            Assert.Equal<float?>(f, columns.SurfaceUnder(m - MazeShape.Width / 2f + 24, along, f + 40)?.Z);
            Assert.Null(columns.SurfaceUnder(along, m - MazeShape.Width / 2f - 24, f + 40));
        }

        Assert.Equal(MazeShape.Height - cross.Kit.WallDepth - f, columns.SurfaceUnder(m, m, f + 40)!.Value.Clear);
    }

    //-------------------------------------------------------------------------
    // The arena, narrowed.
    //-------------------------------------------------------------------------

    [Fact]
    public void The_magazine_is_2_by_2_of_the_512_kit_992_inside_with_its_boss_at_the_centre_facing_the_way_in()
    {
        Room arena = libs.Rooms(5, 0).Single(r => r.Entry.Tags.Contains("boss_arena"));

        Assert.Equal((512, 2, 992), (arena.Kit.Cell, arena.Cells, (int)arena.Body.Width));
        Assert.Equal(new Point(arena.Extent / 2f, arena.Extent / 2f, arena.Kit.Sill), arena.Arena!.Boss);
        Assert.Equal(180, arena.Arena.BossYaw);
        Assert.Empty(arena.Arena.Track);
    }

    [Fact]
    public void The_magazines_three_mouths_stand_inside_it_at_its_edge_facing_in()
    {
        Room arena = libs.Rooms(5, 0).Single(r => r.Entry.Tags.Contains("boss_arena"));
        var mouths = arena.Arena!.Mouths;
        Point boss = arena.Arena.Boss;

        Assert.Equal([1, 2, 3], mouths.Select(m => m.Leg).Order().ToArray());
        Assert.All(mouths, m =>
        {
            Assert.True(arena.Body.Contains(m.At.X, m.At.Y), $"mouth {m.Leg} at {m.At} is outside the arena's body");
            Assert.True(Math.Max(Math.Abs(m.At.X - boss.X), Math.Abs(m.At.Y - boss.Y)) > 300, $"mouth {m.Leg} at {m.At} is not at the edge");
            double toward = Math.Atan2(boss.Y - m.At.Y, boss.X - m.At.X) * 180 / Math.PI;
            double off = Math.Abs(((m.Yaw - toward) % 360 + 540) % 360 - 180);
            Assert.True(off <= 45, $"mouth {m.Leg} faces {m.Yaw}, {off:0} off the arena's middle");
        });
    }

    //-------------------------------------------------------------------------
    // The jump rules and the maze.
    //-------------------------------------------------------------------------

    private static Room Tunnel(string tag) =>
        new RoomBuilder("drift_t", RoomKit.For("normal", RoomKit.MazeCell, RoomKit.Normal.Frame), RoomTheme.Grey).WithHeight(MazeShape.Height)
            .WithDoors(CellFace.West, CellFace.East).WithOutline(new Rect(16, 128, 496, 384))
            .WithEntry(new RoomEntry(tag.Length == 0 ? [] : [tag], Jump: true)).Build();

    [Fact]
    public void The_jump_air_rule_passes_over_a_tunnel_by_its_role_and_not_over_a_chamber()
    {
        Room corridor = Tunnel("corridor"), junction = Tunnel("junction"), chamber = Tunnel("");

        Assert.True(RoomLint.JumpSpaceExempt(corridor) && RoomLint.JumpSpaceExempt(junction));
        Assert.Empty(RoomLint.CheckJumpClearAir(corridor, new RoomColumns(corridor)));
        Assert.Empty(RoomLint.CheckJumpClearAir(junction, new RoomColumns(junction)));
        Assert.NotEmpty(RoomLint.CheckJumpClearAir(chamber, new RoomColumns(chamber)));
    }

    [Fact]
    public void Every_maze_cell_is_a_tunnel_to_the_jump_rules()
    {
        var maze = libs.Rooms(5, 0).Where(r => MazeShape.IsMaze(r.Name)).ToArray();

        Assert.NotEmpty(maze);
        Assert.All(maze, r => Assert.True(RoomLint.JumpSpaceExempt(r), r.Name));
    }
}
