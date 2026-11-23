using SourceSharp.Ai;
using SourceSharp.Ai.Server.Nav;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde.Boss;
using TF2.Descent.Server.Horde.Nav;
using TF2.Descent.Server.Level;
using TF2.Descent.Tests.Level;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The arena brushes of the built boss levels (5, 10 and 15 at seed 1, as the map session ships
/// them, 2026-11-16) as the horde's paths see them: each named brush is a Toggle obstacle of the
/// level's <c>.nav3d</c>; the standing pillars and the exit cover block from the start, rubble once
/// it lands, a pillar's fall and the exit's Disable open their places, and level 15's platforms
/// never block. Skipped where the level is not built (<see cref="BuiltLevelFactAttribute"/>).
/// </summary>
public sealed class HordeArenaBuiltNavTests
{
    private static (HordeNavData Nav, HordeLevel Level) Built(int depth)
    {
        var files = LevelFixture.Built(depth, 1);
        LevelData data = files.Read();
        return (HordeNavData.Derive(files.Nav3d), LevelAdapters.ToHorde(data));
    }

    private static HordePaths Paths(HordeNavData nav, HordeLevel level, Dictionary<string, bool> changes)
    {
        var paths = new HordePaths();
        paths.Ready(nav, level, HordeArenaObstacles.State(nav.Nav, nav.Open, changes, out _));
        return paths;
    }

    private static HordePaths AllOpen(HordeNavData nav, HordeLevel level)
    {
        var paths = new HordePaths();
        paths.Ready(nav, level);
        return paths;
    }

    private static NavObstacleInfo Box(HordeNavData nav, string name) => nav.Nav.Obstacles.Single(o => o.Name == name);

    private static int[] Indices(HordeNavData nav, Func<string, bool> name) =>
        [.. Enumerable.Range(0, nav.Nav.Obstacles.Length).Where(o => nav.Nav.Obstacles[o].Kind == NavObstacleKind.Toggle && name(nav.Nav.Obstacles[o].Name))];

    private static (PathAnswer Answer, AiVector[] Corners) Find(HordePaths paths, AiVector from, AiVector to)
    {
        var corners = new AiVector[HordeRoute.MaxCorners];
        var arrive = new NavEdgeType[HordeRoute.MaxCorners];
        var answer = paths.Find(from, to, ClearanceClass.Stand, HordeRoute.SearchBudget, corners, arrive, out int n, out _);
        return (answer, corners[..n]);
    }

    /// <summary>Whether a leg of the path from <paramref name="from"/> passes inside <paramref name="box"/>'s footprint drawn in by <paramref name="inset"/>, sampled every 2 units.</summary>
    private static bool Through(AiVector from, AiVector[] corners, NavObstacleInfo box, float inset)
    {
        var at = from;
        foreach (var c in corners)
        {
            int n = Math.Max(1, (int)((c - at).Length2D / 2f));
            for (int i = 0; i <= n; i++)
            {
                var p = at + (c - at) * ((float)i / n);
                if (p.X > box.Mins.X + inset && p.X < box.Maxs.X - inset && p.Y > box.Mins.Y + inset && p.Y < box.Maxs.Y - inset)
                    return true;
            }

            at = c;
        }

        return false;
    }

    private static AiVector OnFloor(HordeNavData nav, float x, float y, float z)
    {
        var c = nav.CellAt(new AiVector(x, y, z), ClearanceClass.Stand);
        Assert.True(c.IsValid, $"no standing cell at ({x} {y} {z})");
        return new AiVector(x, y, nav.Graph.Cell(c).FloorZ);
    }

    [BuiltLevelFact(10, 1)]
    public void Level_10s_pillars_and_exit_block_from_the_start_and_its_rubble_does_not()
    {
        var (nav, _) = Built(10);
        var state = HordeArenaObstacles.State(nav.Nav, nav.Open, new Dictionary<string, bool>(), out int blocking);
        int[] pillars = Indices(nav, n => n.StartsWith("descent_boss_pillar_", StringComparison.Ordinal));
        int[] rubble = Indices(nav, n => n.StartsWith("descent_boss_rubble_", StringComparison.Ordinal));
        int[] exit = Indices(nav, n => n == ArenaStage.ExitName);
        Assert.Equal(5, pillars.Length);
        Assert.Equal(5, rubble.Length);
        Assert.Single(exit);

        Assert.All(pillars, o => Assert.True(state.Blocking(o), nav.Nav.Obstacles[o].Name));
        Assert.All(exit, o => Assert.True(state.Blocking(o)));
        Assert.All(rubble, o => Assert.False(state.Blocking(o), nav.Nav.Obstacles[o].Name));
        Assert.Equal(6, blocking);
    }

    [BuiltLevelFact(10, 1)]
    public void Level_10s_standing_pillar_is_gone_round_and_walked_across_once_it_falls()
    {
        var (nav, level) = Built(10);
        var pillar = Box(nav, ArenaStage.PillarName(2));
        float y = (pillar.Mins.Y + pillar.Maxs.Y) * 0.5f;
        var a = OnFloor(nav, pillar.Mins.X - 80f, y, 16f);
        var b = OnFloor(nav, pillar.Maxs.X + 80f, y, 16f);
        Assert.True(Through(a, Find(AllOpen(nav, level), a, b).Corners, pillar, 32f), "the control: with every obstacle open the path goes through the pillar");

        var standing = Find(Paths(nav, level, []), a, b);
        Assert.Equal(PathAnswer.Found, standing.Answer);
        Assert.False(Through(a, standing.Corners, pillar, 16f), $"the path {string.Join(" ", standing.Corners)} goes through the standing pillar");

        var fallen = Find(Paths(nav, level, new() { [ArenaStage.PillarName(2)] = false }), a, b);
        Assert.True(Through(a, fallen.Corners, pillar, 32f), $"the path {string.Join(" ", fallen.Corners)} still goes round the fallen pillar");
    }

    [BuiltLevelFact(10, 1)]
    public void Level_10s_rubble_once_landed_is_gone_round()
    {
        var (nav, level) = Built(10);
        var rubble = Box(nav, ArenaStage.RubbleName(3));
        var pillar = Box(nav, ArenaStage.PillarName(3));
        float y = (pillar.Maxs.Y + rubble.Maxs.Y) * 0.5f;   // across the rubble, clear of the pillar's place
        var a = OnFloor(nav, rubble.Mins.X - 80f, y, 16f);
        var b = OnFloor(nav, rubble.Maxs.X + 80f, y, 16f);
        var fell = new Dictionary<string, bool> { [ArenaStage.PillarName(3)] = false };
        Assert.True(Through(a, Find(Paths(nav, level, fell), a, b).Corners, rubble, 32f), "the control: rubble not yet landed is walked across");

        var landed = Find(Paths(nav, level, new() { [ArenaStage.PillarName(3)] = false, [ArenaStage.RubbleName(3)] = true }), a, b);

        Assert.Equal(PathAnswer.Found, landed.Answer);
        Assert.False(Through(a, landed.Corners, rubble, 16f), $"the path {string.Join(" ", landed.Corners)} goes through the landed rubble");
    }

    /// <summary>
    /// The map keeps each rubble box 32 units or more off its pillar (bf557ae17), so no cell inside landed rubble is
    /// held by another arena brush: the rubble's own obstacle shuts every one when it lands. At 139604a10 the leaves
    /// along pillar 3's side inside rubble 3's box were the pillar's, and a path ran through the landed rubble until
    /// the paths blocked those cells by hand; that is gone, and this fact says so if the map gives a rubble's cells
    /// to another brush again.
    /// </summary>
    [BuiltLevelFact(10, 1)]
    public void Level_10s_cells_inside_each_rubble_box_are_held_by_no_other_arena_brush()
    {
        var (nav, _) = Built(10);
        var graph = nav.Graph;
        var near = new List<int>();
        for (int n = 1; n <= 5; n++)
        {
            int own = Indices(nav, name => name == ArenaStage.RubbleName(n)).Single();
            var box = nav.Nav.Obstacles[own];
            graph.CellsNear(box.Mins.X, box.Mins.Y, box.Maxs.X, box.Maxs.Y, near);
            int inside = 0;
            foreach (int c in near)
            {
                var m = graph.Cell(c).Centre;
                if (m.X <= box.Mins.X || m.X >= box.Maxs.X || m.Y <= box.Mins.Y || m.Y >= box.Maxs.Y || m.Z < box.Mins.Z - 1f || m.Z >= box.Maxs.Z)
                    continue;
                inside++;
                int holder = HordeArenaObstacles.Holder(graph, c);
                Assert.True(holder < 0 || holder == own || HordeArenaObstacles.RoleOf(nav.Nav.Obstacles[holder].Name) == ArenaBrushRole.None,
                    $"cell {c} at {m}, inside {box.Name}'s box, is held by {(holder < 0 ? "" : nav.Nav.Obstacles[holder].Name)}");
            }

            Assert.True(inside > 0, $"the control: {box.Name}'s box holds no cell");
        }
    }

    /// <summary>
    /// Live (2026-11-16, at 680ce6e82): a pyro east of landed rubble 1, hurt from behind it, looked for the
    /// shooter at (4784,2311), under the rubble over pillar 1's place; each search ran out of its budget.
    /// </summary>
    [BuiltLevelFact(10, 1)]
    public void Level_10s_goal_under_landed_rubble_is_led_to_the_nearest_free_place()
    {
        var (nav, level) = Built(10);
        var rubble = Box(nav, ArenaStage.RubbleName(1));
        var from = OnFloor(nav, 4945f, 2320f, 16f);
        var goal = new AiVector(4784f, 2311f, 23f);
        Assert.True(goal.X > rubble.Mins.X && goal.X < rubble.Maxs.X && goal.Y > rubble.Mins.Y && goal.Y < rubble.Maxs.Y, "the control: the goal is inside rubble 1's box");

        var (answer, corners) = Find(Paths(nav, level, new() { [ArenaStage.PillarName(1)] = false, [ArenaStage.RubbleName(1)] = true }), from, goal);

        Assert.Equal(PathAnswer.Nearest, answer);
        Assert.False(Through(from, corners, rubble, 16f), $"the path {string.Join(" ", corners)} goes into the landed rubble");
    }

    /// <summary>
    /// Live (2026-11-16, at 3c4d1c49d): the boss (x1.75) stood 52 s at (4828,2130) beside landed rubble 1 and a giant pyro
    /// 101 s at (4632,2140) against its west face: the paths ran along its south side 4 units off its box, for every class,
    /// the ring round it being fallen pillar 1's. A robot of each class walking round it keeps its hull off the box.
    /// </summary>
    [BuiltLevelFact(10, 1)]
    public void Level_10s_landed_rubble_is_gone_round_a_bosss_half_width_off()
    {
        var (nav, level) = Built(10);
        var rubble = Box(nav, ArenaStage.RubbleName(1));
        var paths = Paths(nav, level, new() { [ArenaStage.PillarName(1)] = false, [ArenaStage.RubbleName(1)] = true });
        var cls = HordeHulls.ClassOf(1.75f);
        var from = new AiVector(4500f, 2100f, 16f);
        var to = new AiVector(4900f, 2321f, 16f);
        var corners = new AiVector[HordeRoute.MaxCorners];
        var arrive = new NavEdgeType[HordeRoute.MaxCorners];

        var answer = paths.Find(from, to, cls, HordeRoute.SearchBudget, corners, arrive, out int n, out _);

        Assert.Equal(PathAnswer.Found, answer);
        float over = HordeArenaClearanceTests.WorstOverlap(from, corners.AsSpan(0, n), rubble.Mins, rubble.Maxs, HordeArenaClearanceTests.HalfWidth(cls));
        Assert.True(over <= 0f, $"the boss's hull on the path {string.Join(" ", corners[..n])} is {over} units inside landed rubble 1");
    }

    [BuiltLevelFact(10, 1)]
    public void Level_10s_landed_rubble_is_gone_round_a_stand_bodys_half_width_off()
    {
        var (nav, level) = Built(10);
        var rubble = Box(nav, ArenaStage.RubbleName(1));
        var paths = Paths(nav, level, new() { [ArenaStage.PillarName(1)] = false, [ArenaStage.RubbleName(1)] = true });
        var from = new AiVector(4500f, 2100f, 16f);
        var to = new AiVector(4900f, 2321f, 16f);

        var (answer, corners) = Find(paths, from, to);

        Assert.Equal(PathAnswer.Found, answer);
        float over = HordeArenaClearanceTests.WorstOverlap(from, corners, rubble.Mins, rubble.Maxs, HordeArenaClearanceTests.HalfWidth(ClearanceClass.Stand));
        Assert.True(over <= 0f, $"a stand body on the path {string.Join(" ", corners)} is {over} units inside landed rubble 1");
    }

    /// <summary>A hero standing on level 5's exit cover (its top, 176): robots are led to the rim nearest him, never into the covered pit.</summary>
    [BuiltLevelFact(5, 1)]
    public void A_hero_on_level_5s_exit_cover_is_come_at_from_the_rim()
    {
        var (nav, level) = Built(5);
        var exit = Box(nav, ArenaStage.ExitName);
        float cy = (exit.Mins.Y + exit.Maxs.Y) * 0.5f;
        var hero = new AiVector((exit.Mins.X + exit.Maxs.X) * 0.5f, cy, exit.Maxs.Z);
        var from = OnFloor(nav, exit.Mins.X - 40f, cy - 120f, exit.Maxs.Z);

        var (answer, corners) = Find(Paths(nav, level, []), from, hero);

        Assert.Equal(PathAnswer.Nearest, answer);
        Assert.False(Through(from, corners, exit, 8f), $"the path {string.Join(" ", corners)} goes into the covered pit");
        Assert.True(corners.Length == 0 || corners[^1].Z > exit.Maxs.Z - 20f, $"the path ends at {(corners.Length > 0 ? corners[^1] : from)}, off the rim");
    }

    [BuiltLevelFact(5, 1)]
    public void Level_5s_exit_cover_keeps_paths_out_of_its_pit_until_its_disable()
    {
        var (nav, level) = Built(5);
        var exit = Box(nav, ArenaStage.ExitName);
        float cy = (exit.Mins.Y + exit.Maxs.Y) * 0.5f;
        var pit = OnFloor(nav, (exit.Mins.X + exit.Maxs.X) * 0.5f, cy, exit.Mins.Z);
        var beside = OnFloor(nav, exit.Mins.X - 40f, cy, exit.Maxs.Z);   // on the rim round the hole (176), the pit's floor 160 below
        Assert.Equal(PathAnswer.Found, Find(AllOpen(nav, level), beside, pit).Answer);

        Assert.NotEqual(PathAnswer.Found, Find(Paths(nav, level, []), beside, pit).Answer);
        Assert.Equal(PathAnswer.Found, Find(Paths(nav, level, new() { [ArenaStage.ExitName] = false }), beside, pit).Answer);
    }

    [BuiltLevelFact(15, 1)]
    public void Level_15s_platforms_never_block_and_a_path_onto_one_holds_before_and_after_its_drop()
    {
        var (nav, level) = Built(15);
        int[] platforms = Indices(nav, n => n.StartsWith("descent_boss_platform_", StringComparison.Ordinal));
        Assert.Equal(3, platforms.Length);
        var shipped = HordeArenaObstacles.State(nav.Nav, nav.Open, new Dictionary<string, bool>(), out _);
        var dropped = HordeArenaObstacles.State(nav.Nav, nav.Open,
            new Dictionary<string, bool> { [ArenaStage.PlatformName(2)] = false, [ArenaStage.PlatformName(3)] = false, [ArenaStage.PlatformName(4)] = false }, out _);
        Assert.All(platforms, o => Assert.False(shipped.Blocking(o) || dropped.Blocking(o), nav.Nav.Obstacles[o].Name));

        var top = Box(nav, ArenaStage.PlatformName(3));
        var on = OnFloor(nav, (top.Mins.X + top.Maxs.X) * 0.5f, (top.Mins.Y + top.Maxs.Y) * 0.5f, top.Maxs.Z);
        var from = OnFloor(nav, 5120f, 2048f, 32f);
        Assert.Equal(PathAnswer.Found, Find(Paths(nav, level, []), from, on).Answer);
        Assert.Equal(PathAnswer.Found, Find(Paths(nav, level, new() { [ArenaStage.PlatformName(3)] = false }), from, on).Answer);
    }
}
