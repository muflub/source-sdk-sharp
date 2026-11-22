using SourceSharp.Ai;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde.Boss;
using TF2.Descent.Server.Horde.Nav;
using TF2.Descent.Server.Level;
using TF2.Descent.Tests.Level;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A corridor of six 64-unit cells whose middle cell a pillar takes whole: standing, it cuts the
/// corridor in two, which the level's components (made with every obstacle open) do not know.
/// </summary>
internal static class CutCorridor
{
    public const int Length = 6, PillarCell = 2;
    public const float Cell = 64f, FloorZ = 16f;

    public static AiVector Centre(int x) => new(x * Cell + Cell * 0.5f, Cell * 0.5f, FloorZ);

    public static HordeNavData Data()
    {
        var b = new NavGraphBuilder();
        for (int x = 0; x < Length; x++)
            b.AddCell(new NavCell(x * Cell, 0f, (x + 1) * Cell, Cell, FloorZ, ClearanceClasses.Stand | ClearanceClasses.Crouch, 0, 1f));
        NavObstacleInfo[] obstacles =
        [
            new(ArenaStage.PillarName(1), NavObstacleKind.Toggle, "func_brush", new AiVector(PillarCell * Cell, 0f, FloorZ),
                new AiVector((PillarCell + 1) * Cell, Cell, FloorZ + 1000f)),
        ];
        b.AddObstacle();
        for (int x = 0; x < Length; x++)
        {
            foreach (int nx in new[] { x - 1, x + 1 })
            {
                if (nx < 0 || nx >= Length)
                    continue;
                b.AddEdge(new CellId(x), new CellId(nx), NavEdgeType.Walk, 0f, nx == PillarCell ? 0 : -1);
            }
        }

        var nav = new DerivedNav(b.Build(), [], [], obstacles, 0);
        var columns = VoxelColumns.Of(0f, 0f, Cell, Length, 1, (_, _) => [new FreeSpan(FloorZ, 4096f)]);
        return HordeNavData.Of(nav, columns);
    }

    public static HordeLevel Level => HordeLevel.Create(1, 10,
        [new HordeRoom(0, new AiVector(0f, 0f, 0f), new AiVector(Length * Cell, Cell, 512f), [])], []);
}

/// <summary>
/// Reachability as the arena stands: whether two cells are joined is asked of each class's
/// components under its own obstacles, so a goal the arena's brushes alone cut off is off the
/// robot's floor (<see cref="PathAnswer.Nearest"/>: it is led to the nearest place it can reach
/// and holds there) rather than unreachable, which sent it down the room route into the brush (on
/// a level the size of level 10 the search ran out of its budget first).
/// </summary>
public sealed class HordeArenaJoinTests
{
    private static (PathAnswer Answer, AiVector[] Corners) Find(HordePaths paths, AiVector from, AiVector to)
    {
        var corners = new AiVector[HordeRoute.MaxCorners];
        var arrive = new NavEdgeType[HordeRoute.MaxCorners];
        var answer = paths.Find(from, to, ClearanceClass.Stand, HordeRoute.SearchBudget, corners, arrive, out int n, out _);
        return (answer, corners[..n]);
    }

    private static HordePaths Paths(HordeNavData nav, Dictionary<string, bool> changes)
    {
        var paths = new HordePaths();
        paths.Ready(nav, CutCorridor.Level, HordeArenaObstacles.State(nav.Nav, nav.Open, changes, out _));
        return paths;
    }

    /// <summary>
    /// A giant pressed against landed rubble stands in a cell its class keeps off the box (blocked
    /// for its hull, in no component): it is joined to the floor it steps out onto, as the search
    /// steps out of its cell, and its way round is found.
    /// </summary>
    [Fact]
    public void A_giant_standing_in_a_cell_its_hull_keeps_off_landed_rubble_finds_its_way_round()
    {
        var nav = ClearanceArena.Data();
        using var paths = new HordePaths();
        paths.Ready(nav, ClearanceArena.Level, HordeArenaObstacles.State(nav.Nav, nav.Open,
            new Dictionary<string, bool> { [ArenaStage.PillarName(1)] = false, [ArenaStage.RubbleName(1)] = true }, out _));
        var cls = ClearanceClass.Hull5;
        var from = new AiVector(248f, 300f, ClearanceArena.FloorZ);   // one cell inside the x1.90 hull's ring west of the rubble
        var to = new AiVector(500f, 300f, ClearanceArena.FloorZ);
        Assert.True(paths.ObstaclesFor(cls)!.CellBlocked(nav.CellAt(from, cls).Index), "the control: the giant's cell is blocked for its hull");
        var corners = new AiVector[HordeRoute.MaxCorners];
        var arrive = new NavEdgeType[HordeRoute.MaxCorners];

        var answer = paths.Find(from, to, cls, HordeRoute.SearchBudget, corners, arrive, out int n, out _);

        Assert.Equal(PathAnswer.Found, answer);
        Assert.Equal(to, corners[n - 1]);
    }

    [Fact]
    public void A_goal_a_standing_pillar_alone_cuts_off_is_led_to_the_nearest_place_on_the_robots_side()
    {
        var nav = CutCorridor.Data();
        using var paths = Paths(nav, []);
        var from = CutCorridor.Centre(0);
        var goal = CutCorridor.Centre(4);
        Assert.True(nav.Joined(nav.CellAt(from, ClearanceClass.Stand), nav.CellAt(goal, ClearanceClass.Stand), ClearanceClass.Stand),
                    "the control: with every obstacle open the corridor is one floor");

        var (answer, corners) = Find(paths, from, goal);

        Assert.Equal(PathAnswer.Nearest, answer);
        Assert.NotEmpty(corners);
        Assert.True(corners[^1].X < CutCorridor.PillarCell * CutCorridor.Cell, $"the path {string.Join(" ", corners)} ends past the pillar");
    }

    [Fact]
    public void Once_the_pillar_falls_the_same_goal_is_found()
    {
        var nav = CutCorridor.Data();
        using var paths = Paths(nav, []);
        var from = CutCorridor.Centre(0);
        var goal = CutCorridor.Centre(4);
        Assert.NotEqual(PathAnswer.Found, Find(paths, from, goal).Answer);   // the control: standing, it cuts the corridor

        var fell = HordeArenaObstacles.State(nav.Nav, nav.Open, new Dictionary<string, bool> { [ArenaStage.PillarName(1)] = false }, out _);
        paths.Obstruct(fell);

        var (answer, corners) = Find(paths, from, goal);
        Assert.Equal(PathAnswer.Found, answer);
        Assert.Equal(goal, corners[^1]);
    }

    [Fact]
    public void Cells_the_standing_pillar_parts_are_not_joined_and_those_on_one_side_are()
    {
        var nav = CutCorridor.Data();
        using var paths = Paths(nav, []);
        CellId Cell(int x) => nav.CellAt(CutCorridor.Centre(x), ClearanceClass.Stand);

        Assert.True(paths.Joined(Cell(0), Cell(1), ClearanceClass.Stand));
        Assert.True(paths.Joined(Cell(3), Cell(5), ClearanceClass.Stand));
        Assert.False(paths.Joined(Cell(1), Cell(3), ClearanceClass.Stand));
    }

    /// <summary>
    /// The components each arena change makes by flooding windows round what changed
    /// (<see cref="NavComponents.Near"/>) are the whole graph's flood under the same obstacles, for
    /// every class, over level 10's states: as shipped, each pillar drilled, and all of them with the
    /// exit open.
    /// </summary>
    [BuiltLevelFact(10, 1)]
    public void Level_10s_components_made_round_each_change_are_the_whole_graphs()
    {
        var files = LevelFixture.Built(10, 1);
        var nav = HordeNavData.Derive(files.Nav3d);
        var level = LevelAdapters.ToHorde(files.Read());
        var states = new List<Dictionary<string, bool>> { new() };
        var all = new Dictionary<string, bool> { [ArenaStage.ExitName] = false };
        for (int n = 1; n <= 5; n++)
        {
            states.Add(new() { [ArenaStage.PillarName(n)] = false, [ArenaStage.RubbleName(n)] = true });
            all[ArenaStage.PillarName(n)] = false;
            all[ArenaStage.RubbleName(n)] = true;
        }

        states.Add(all);
        using var paths = new HordePaths();
        foreach (var changes in states)
        {
            paths.Ready(nav, level, HordeArenaObstacles.State(nav.Nav, nav.Open, changes, out _));
            foreach (var cls in new[] { ClearanceClass.Stand, ClearanceClass.Hull2, ClearanceClass.Hull3, ClearanceClass.Hull4, ClearanceClass.Hull5 })
            {
                var near = paths.ComponentsFor(cls)!;
                var whole = NavComponents.Of(nav.Graph, cls, paths.ObstaclesFor(cls)!);
                Assert.True(SamePartition(near, whole, nav.Graph.CellCount, out string why),
                            $"{HordeHulls.NameOf(cls)} with {string.Join(", ", changes.Select(c => $"{c.Key}={c.Value}"))}: {why}");
            }
        }

        Assert.True(paths.JoinsWindowed > 0, $"the control: no class's components were made by their windows ({paths.JoinsWhole} by the whole graph)");
    }

    /// <summary>Whether two labellings part the cells alike (the same cells in no component, the same groups), and the first cell where not.</summary>
    internal static bool SamePartition(NavComponents a, NavComponents b, int cells, out string why)
    {
        var ab = new Dictionary<int, int>();
        var ba = new Dictionary<int, int>();
        for (int i = 0; i < cells; i++)
        {
            int x = a.Of(new CellId(i)), y = b.Of(new CellId(i));
            if ((x < 0) != (y < 0) || (x >= 0 && (ab.TryGetValue(x, out int y0) && y0 != y || ba.TryGetValue(y, out int x0) && x0 != x)))
            {
                why = $"cell {i}: {x} against {y}";
                return false;
            }

            if (x >= 0)
            {
                ab[x] = y;
                ba[y] = x;
            }
        }

        why = "";
        return true;
    }

    /// <summary>With nothing of the arena blocking, the paths ask the level's own components: nothing is made at the hand-over.</summary>
    [Fact]
    public void With_nothing_blocking_the_paths_join_by_the_levels_own_components()
    {
        var nav = CutCorridor.Data();
        using var standing = Paths(nav, []);
        using var fallen = Paths(nav, new Dictionary<string, bool> { [ArenaStage.PillarName(1)] = false });

        Assert.NotSame(nav.Components(ClearanceClass.Stand), standing.ComponentsFor(ClearanceClass.Stand));   // the control: a blocking pillar makes its own
        Assert.Same(nav.Components(ClearanceClass.Stand), fallen.ComponentsFor(ClearanceClass.Stand));
    }
}
