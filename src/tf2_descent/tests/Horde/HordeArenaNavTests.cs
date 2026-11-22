using SourceSharp.Ai;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde.Boss;
using TF2.Descent.Server.Horde.Nav;
using TF2.Descent.Tests.Level;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A boss arena's graph built by hand: six 64-unit cells by three on one floor, each arena brush
/// a Toggle obstacle taking its cells as the level's <c>.nav3d</c> holds them (the box open in the
/// grid, its leaves naming it): a pillar in the middle of row 1, rubble beside it in row 0 whose
/// box reaches over the pillar's cell (rubble lands where its pillar stood), the exit's pit at the end of row 1, a platform section, a door and an unnamed toggled
/// brush (a generated room's non-solid strip).
/// </summary>
internal static class ArenaGraph
{
    public const int Width = 6, Height = 3;
    public const float Cell = 64f, FloorZ = 16f;
    public const int Pillar = 0, Rubble = 1, Exit = 2, Platform = 3, Door = 4, Strip = 5;

    public static readonly string PillarName = ArenaStage.PillarName(1), RubbleName = ArenaStage.RubbleName(1), PlatformName = ArenaStage.PlatformName(2);

    /// <summary>The obstacle taking cell (<paramref name="x"/>, <paramref name="y"/>), or -1.</summary>
    public static int ObstacleAt(int x, int y) => (x, y) switch
    {
        (2, 1) => Pillar,
        (2, 0) => Rubble,
        (5, 1) => Exit,
        (0, 0) => Platform,
        (4, 2) => Door,
        (0, 2) => Strip,
        _ => -1,
    };

    public static AiVector Centre(int x, int y) => new(x * Cell + Cell * 0.5f, y * Cell + Cell * 0.5f, FloorZ);

    public static HordeNavData Data()
    {
        var b = new NavGraphBuilder();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
                b.AddCell(new NavCell(x * Cell, y * Cell, (x + 1) * Cell, (y + 1) * Cell, FloorZ, ClearanceClasses.Stand | ClearanceClasses.Crouch, 0, 1f));
        }

        NavObstacleInfo[] obstacles =
        [
            Info(PillarName, NavObstacleKind.Toggle, "func_brush", 2, 1, 2, 1),
            Info(RubbleName, NavObstacleKind.Toggle, "func_brush", 2, 0, 2, 1),
            Info(ArenaStage.ExitName, NavObstacleKind.Toggle, "func_brush", 5, 1, 5, 1),
            Info(PlatformName, NavObstacleKind.Toggle, "func_brush", 0, 0, 0, 0),
            Info("door_a", NavObstacleKind.Door, "func_door", 4, 2, 4, 2),
            Info("func_brush@32,160,16", NavObstacleKind.Toggle, "func_brush", 0, 2, 0, 2),
        ];
        foreach (var _ in obstacles)
            b.AddObstacle();

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                        continue;
                    int a = ObstacleAt(x, y), c = ObstacleAt(nx, ny);
                    int obstacle = a >= 0 && (c < 0 || a == Door) ? a : c;   // as the derivation does: the entered leaf's
                    var type = obstacle == Door ? NavEdgeType.Door : NavEdgeType.Walk;
                    b.AddEdge(new CellId(y * Width + x), new CellId(ny * Width + nx), type, 0f, obstacle);
                }
            }
        }

        var nav = new DerivedNav(b.Build(), [], [], obstacles, 0);
        var columns = VoxelColumns.Of(0f, 0f, Cell, Width, Height, (_, _) => [new FreeSpan(FloorZ, 4096f)]);
        return HordeNavData.Of(nav, columns);
    }

    public static HordeLevel Level => HordeLevel.Create(1, 10,
        [new HordeRoom(0, new AiVector(0f, 0f, 0f), new AiVector(Width * Cell, Height * Cell, 512f), [])], []);

    private static NavObstacleInfo Info(string name, NavObstacleKind kind, string cls, int x0, int y0, int x1, int y1) =>
        new(name, kind, cls, new AiVector(x0 * Cell, y0 * Cell, FloorZ), new AiVector((x1 + 1) * Cell, (y1 + 1) * Cell, FloorZ + 160f));
}

/// <summary>
/// The horde's paths follow the boss arena's toggled brushes (the ARNA contract): a standing pillar,
/// rubble once it lands and the exit's cover block; a pillar's fall and the exit's Disable open the
/// way; a platform section never blocks; doors and other toggled brushes stay as they were; a
/// second load starts as the map ships; the stage tells the paths what it fires; a walker plans
/// again when the arena changes.
/// </summary>
public sealed class HordeArenaNavTests
{
    private static readonly LevelFixture Fixture = LevelFixture.Committed();

    /// <summary>The server's navigation, ready on the hand-built arena (no thread: derived where asked).</summary>
    private static (ServerHordeRig Rig, HordeNav Nav) Ready(Action<HordeNav>? before = null)
    {
        var rig = new ServerHordeRig(ArenaGraph.Level);
        var nav = rig.Nav(new LevelServiceTests.LevelFilesOnDisk(Fixture));
        nav.Work = (_, _) => (ArenaGraph.Data(), []);
        Load(rig, nav, before);
        return (rig, nav);
    }

    private static void Load(ServerHordeRig rig, HordeNav nav, Action<HordeNav>? before = null)
    {
        nav.BeginNow(Fixture.Map, ArenaGraph.Level);
        before?.Invoke(nav);
        for (int i = 0; i < 10 && nav.State != HordeNavState.Ready; i++)
        {
            rig.Now++;
            nav.Tick();
        }

        Assert.Equal(HordeNavState.Ready, nav.State);
    }

    private static (PathAnswer Answer, AiVector[] Corners) Find(HordePaths paths, AiVector from, AiVector to)
    {
        var corners = new AiVector[HordeRoute.MaxCorners];
        var arrive = new NavEdgeType[HordeRoute.MaxCorners];
        var answer = paths.Find(from, to, ClearanceClass.Stand, HordeRoute.SearchBudget, corners, arrive, out int n, out _);
        return (answer, corners[..n]);
    }

    /// <summary>Whether a leg of the path from <paramref name="from"/> passes over cell (<paramref name="x"/>, <paramref name="y"/>), sampled every 2 units.</summary>
    private static bool Crosses(AiVector from, AiVector[] corners, int x, int y)
    {
        var at = from;
        foreach (var c in corners)
        {
            int n = Math.Max(1, (int)((c - at).Length2D / 2f));
            for (int i = 0; i <= n; i++)
            {
                var p = at + (c - at) * ((float)i / n);
                if (p.X > x * ArenaGraph.Cell + 1f && p.X < (x + 1) * ArenaGraph.Cell - 1f && p.Y > y * ArenaGraph.Cell + 1f && p.Y < (y + 1) * ArenaGraph.Cell - 1f)
                    return true;
            }

            at = c;
        }

        return false;
    }

    [Fact]
    public void A_platform_section_never_blocks_in_either_state()
    {
        var data = ArenaGraph.Data();
        Assert.Equal(ArenaBrushRole.Platform, HordeArenaObstacles.RoleOf(ArenaGraph.PlatformName));
        Assert.False(HordeArenaObstacles.Blocks(ArenaBrushRole.Platform, enabled: true));
        Assert.False(HordeArenaObstacles.Blocks(ArenaBrushRole.Platform, enabled: false));

        var enabled = HordeArenaObstacles.State(data.Nav, data.Open, new Dictionary<string, bool> { [ArenaGraph.PlatformName] = true }, out _);
        var disabled = HordeArenaObstacles.State(data.Nav, data.Open, new Dictionary<string, bool> { [ArenaGraph.PlatformName] = false }, out _);
        Assert.True(enabled.Blocking(ArenaGraph.Pillar), "the control: the state blocks a standing pillar beside the platform");
        Assert.False(enabled.Blocking(ArenaGraph.Platform));
        Assert.False(disabled.Blocking(ArenaGraph.Platform));
    }

    [Fact]
    public void A_standing_pillar_blocks_a_path_across_it_which_goes_round()
    {
        var (_, nav) = Ready();
        var (a, b) = (ArenaGraph.Centre(0, 1), ArenaGraph.Centre(4, 1));
        var open = new HordePaths();
        open.Ready(nav.Data!, ArenaGraph.Level);
        Assert.True(Crosses(a, Find(open, a, b).Corners, 2, 1), "the control: with every obstacle open the path crosses the pillar's cell");

        var (answer, corners) = Find(nav.Paths, a, b);

        Assert.Equal(PathAnswer.Found, answer);
        Assert.False(Crosses(a, corners, 2, 1), $"the path {string.Join(" ", corners)} goes through the standing pillar");
    }

    // Live on level 15 seed 1 (2026-11-16): each platform's drop logged "paths in hand are planned
    // again" under the same epoch, when nothing the paths plan with had changed.
    [Fact]
    public void A_platforms_drop_leaves_the_paths_as_they_were_and_says_so()
    {
        var (rig, nav) = Ready();
        int before = nav.Paths.Epoch;

        nav.Changed(ArenaGraph.PlatformName, enabled: false);

        Assert.Equal(before, nav.Paths.Epoch);
        var line = Assert.Single(rig.Log.Lines, l => l.Text.Contains("nav: " + ArenaGraph.PlatformName, StringComparison.Ordinal));
        Assert.DoesNotContain("planned again", line.Text, StringComparison.Ordinal);
        Assert.Contains("paths unchanged", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pillars_fall_opens_the_way_across_it_under_a_new_epoch()
    {
        var (_, nav) = Ready();
        var (a, b) = (ArenaGraph.Centre(0, 1), ArenaGraph.Centre(4, 1));
        int before = nav.Paths.Epoch;
        Assert.False(Crosses(a, Find(nav.Paths, a, b).Corners, 2, 1), "the control: the standing pillar is gone round");

        nav.Changed(ArenaGraph.PillarName, enabled: false);

        Assert.True(Crosses(a, Find(nav.Paths, a, b).Corners, 2, 1), "the path still goes round the fallen pillar");
        Assert.NotEqual(before, nav.Paths.Epoch);
    }

    [Fact]
    public void Rubble_once_enabled_blocks_a_path_across_it()
    {
        var (_, nav) = Ready();
        nav.Changed(ArenaGraph.PillarName, enabled: false);
        var (a, b) = (ArenaGraph.Centre(0, 0), ArenaGraph.Centre(4, 0));
        Assert.True(Crosses(a, Find(nav.Paths, a, b).Corners, 2, 0), "the control: rubble that has not landed is walked across");

        nav.Changed(ArenaGraph.RubbleName, enabled: true);

        var (answer, corners) = Find(nav.Paths, a, b);
        Assert.Equal(PathAnswer.Found, answer);
        Assert.False(Crosses(a, corners, 2, 0), $"the path {string.Join(" ", corners)} goes through the rubble");
    }

    [Fact]
    public void Landed_rubble_blocks_the_fallen_pillars_cell_its_box_covers()
    {
        var (_, nav) = Ready();
        nav.Changed(ArenaGraph.PillarName, enabled: false);
        var (a, b) = (ArenaGraph.Centre(0, 1), ArenaGraph.Centre(4, 1));
        Assert.True(Crosses(a, Find(nav.Paths, a, b).Corners, 2, 1), "the control: the fallen pillar's cell is walked across");

        nav.Changed(ArenaGraph.RubbleName, enabled: true);

        var (answer, corners) = Find(nav.Paths, a, b);
        Assert.Equal(PathAnswer.Found, answer);
        Assert.False(Crosses(a, corners, 2, 1), $"the path {string.Join(" ", corners)} goes through the landed rubble over the pillar's cell");
    }

    // A hero standing on the exit's cover is over its pit in the grid (the cover's box is open there):
    // with the cover blocking, every way into the pit is shut and the search answered unreachable.
    [Fact]
    public void A_goal_in_a_standing_brushs_place_is_led_to_the_nearest_free_place()
    {
        var (_, nav) = Ready();
        var hero = new AiVector(5 * ArenaGraph.Cell + 4f, 1.5f * ArenaGraph.Cell, ArenaGraph.FloorZ);   // on the exit's cover, by its west edge

        var (answer, corners) = Find(nav.Paths, ArenaGraph.Centre(4, 0), hero);

        Assert.Equal(PathAnswer.Nearest, answer);
        Assert.True(corners[^1].X < 5 * ArenaGraph.Cell, $"the path ends at {corners[^1]}, in the covered pit");
    }

    // Live on level 10 seed 1 (2026-11-16, at 680ce6e82): a pyro hurt from behind landed rubble 1 looked for
    // the shooter at (4784,2311), inside the rubble over pillar 1's place; every search for it ran out of its
    // budget (146 of 193 searches, 11 ms each on the workers) and the robot walked the room route into the rubble.
    [Fact]
    public void A_goal_under_landed_rubble_on_its_fallen_pillars_place_is_led_to_the_nearest_free_place()
    {
        var (_, nav) = Ready();
        nav.Changed(ArenaGraph.PillarName, enabled: false);
        var under = ArenaGraph.Centre(2, 1);
        Assert.Equal(PathAnswer.Found, Find(nav.Paths, ArenaGraph.Centre(0, 1), under).Answer);   // the control: the fallen pillar's place is reached

        nav.Changed(ArenaGraph.RubbleName, enabled: true);
        var (answer, corners) = Find(nav.Paths, ArenaGraph.Centre(0, 1), under);

        Assert.Equal(PathAnswer.Nearest, answer);
        Assert.NotEmpty(corners);
        Assert.False(Crosses(ArenaGraph.Centre(0, 1), corners, 2, 1), $"the path {string.Join(" ", corners)} goes into the landed rubble");
    }

    [Fact]
    public void The_exits_cover_blocks_its_pit_until_its_disable_opens_it()
    {
        var (_, nav) = Ready();
        var (a, pit) = (ArenaGraph.Centre(4, 0), ArenaGraph.Centre(5, 1));
        var covered = Find(nav.Paths, a, pit);
        Assert.Equal(PathAnswer.Nearest, covered.Answer);
        Assert.False(Crosses(a, covered.Corners, 5, 1), $"the path {string.Join(" ", covered.Corners)} goes into the covered pit");

        nav.Changed(ArenaStage.ExitName, enabled: false);

        Assert.Equal(PathAnswer.Found, Find(nav.Paths, a, pit).Answer);
    }

    [Fact]
    public void A_second_load_starts_as_the_map_ships()
    {
        var (rig, nav) = Ready();
        nav.Changed(ArenaGraph.PillarName, enabled: false);
        nav.Changed(ArenaGraph.RubbleName, enabled: true);
        nav.Changed(ArenaStage.ExitName, enabled: false);
        Assert.False(nav.Paths.Obstacles!.Blocking(ArenaGraph.Pillar), "the control: the first load's pillar fell");

        Load(rig, nav);

        var now = nav.Paths.Obstacles!;
        Assert.True(now.Blocking(ArenaGraph.Pillar));
        Assert.False(now.Blocking(ArenaGraph.Rubble));
        Assert.True(now.Blocking(ArenaGraph.Exit));
        Assert.Empty(nav.Arena);
    }

    [Fact]
    public void A_change_told_before_the_graph_is_ready_is_planned_with_when_it_is()
    {
        var (_, nav) = Ready(before: n =>
        {
            Assert.NotEqual(HordeNavState.Ready, n.State);
            n.Changed(ArenaGraph.PillarName, enabled: false);
        });

        Assert.False(nav.Paths.Obstacles!.Blocking(ArenaGraph.Pillar));
        Assert.True(nav.Paths.Obstacles!.Blocking(ArenaGraph.Exit), "the control: the brushes not told of stand as shipped");
    }

    // Live on level 10 seed 1 (2026-11-20, at 225fe2e96): a drill told the paths its pillar's fall and its rubble's landing
    // one at a time, so the arena stood for a moment with the pillar gone and no rubble, the paths planned twice, and the
    // pillar's line said 4 arena obstacles block on a load whose boss had died before the drill and 5 on one whose had not.
    [Fact]
    public void A_drilled_stations_pillar_and_rubble_are_one_change_of_the_paths()
    {
        var (rig, nav) = Ready();
        int before = nav.Paths.Epoch;
        int lines = rig.Log.Lines.Count(l => l.Text.Contains("nav: ", StringComparison.Ordinal));

        nav.Changed([(ArenaGraph.PillarName, false), (ArenaGraph.RubbleName, true)]);

        var told = rig.Log.Lines.Where(l => l.Text.Contains("nav: ", StringComparison.Ordinal)).Skip(lines).ToList();
        var line = Assert.Single(told);
        Assert.Contains(ArenaGraph.PillarName + " disabled", line.Text, StringComparison.Ordinal);
        Assert.Contains(ArenaGraph.RubbleName + " enabled", line.Text, StringComparison.Ordinal);
        Assert.Contains("2 arena obstacle(s) block", line.Text, StringComparison.Ordinal);   // the rubble and the exit
        Assert.NotEqual(before, nav.Paths.Epoch);
        Assert.False(nav.Paths.Obstacles!.Blocking(ArenaGraph.Pillar));
        Assert.True(nav.Paths.Obstacles!.Blocking(ArenaGraph.Rubble));
    }

    /// <summary>Two loads, the boss dying before the drill on one and after it on the other, end with the same arena under one epoch.</summary>
    [Fact]
    public void Two_loads_whose_exit_opens_before_and_after_the_drill_end_alike()
    {
        var (rig, nav) = Ready();
        nav.Changed([(ArenaGraph.PillarName, false), (ArenaGraph.RubbleName, true)]);
        nav.Changed(ArenaStage.ExitName, enabled: false);
        int first = nav.Paths.Epoch, firstBlocking = nav.ArenaBlocking;
        Assert.NotEqual(0, first);

        Load(rig, nav);
        Assert.NotEqual(first, nav.Paths.Epoch);   // the control: the second load starts as the map ships
        nav.Changed(ArenaStage.ExitName, enabled: false);
        nav.Changed([(ArenaGraph.PillarName, false), (ArenaGraph.RubbleName, true)]);

        Assert.Equal(first, nav.Paths.Epoch);
        Assert.Equal(firstBlocking, nav.ArenaBlocking);
    }

    [Fact]
    public void Doors_and_other_toggled_brushes_stay_open_as_before()
    {
        var (_, nav) = Ready();
        var now = nav.Paths.Obstacles!;
        Assert.True(now.Blocking(ArenaGraph.Pillar), "the control: the state blocks the arena's standing brushes");

        Assert.False(now.Blocking(ArenaGraph.Door));
        Assert.False(now.Blocking(ArenaGraph.Strip));
        Assert.True(now.OpenDoors);
        Assert.Equal(PathAnswer.Found, Find(nav.Paths, ArenaGraph.Centre(3, 2), ArenaGraph.Centre(4, 2)).Answer);
        Assert.Equal(PathAnswer.Found, Find(nav.Paths, ArenaGraph.Centre(1, 2), ArenaGraph.Centre(0, 2)).Answer);
    }
}
