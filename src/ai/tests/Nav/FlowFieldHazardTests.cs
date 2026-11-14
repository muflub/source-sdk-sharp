using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

/// <summary>
/// A route that minds where players died (plan_bots_teams_tf2_part_g G.4, lane G4a): a flow field over a
/// graph of two routes, with deaths seeded in the experience on the shorter, built through
/// <see cref="ExperienceRoutes"/> as the game builds it. The choice is a property of the field's costs
/// and the bot's weight (Cunning / 10), not of noisy runs: the shorter route's seeded stretch is 8 steps
/// (256 u) of cells whose hazard is 0.1 deaths a second (2 s of death a second at 20 s a death), the
/// longer route 192 u longer, so a weight over 0.375 (Cunning 4 and up) goes round and one under it
/// (Cunning 3 and down) takes the shorter.
/// </summary>
public sealed class FlowFieldHazardTests
{
    private const int Red = 2, Blue = 3;

    /// <summary>
    /// The bottom row from (0, 3) to (19, 3), 19 steps (608 u), and the way over the top, 25 steps
    /// (800 u). Row 3's cells are at y 0 to 32, in bin row 0; cells 6 to 13 are bins 3 to 6.
    /// </summary>
    private static readonly string[] TwoRoutes =
    [
        "....................",
        ".                  .",
        ".                  .",
        "....................",
    ];

    private static readonly ExperienceKey Key = new("tf_two_routes", "0123456789abcdef", "ctf");

    /// <summary>A snapshot with <paramref name="deaths"/> in <paramref name="seconds"/> in each of bins 3 to 6 of row 0, for <paramref name="role"/>'s players.</summary>
    private static MapExperience Seeded(double deaths = 1d, double seconds = 10d, int role = Red)
    {
        var rows = new ExperienceDelta();
        for (int x = 3; x <= 6; x++)
            rows.Add(new CellKey(new ExperienceContext(0, role, 3), new WorldBin(x, 0, 0), ExperienceSource.Human), new CellStat(seconds, deaths, 0d, 0d, 0d));
        // A place everyone walks and nobody dies: traffic is no hazard.
        rows.Add(new CellKey(new ExperienceContext(0, role, 3), new WorldBin(1, 1, 0), ExperienceSource.Human), new CellStat(30d, 0d, 0d, 0d, 0d));
        return new MapExperience(Key, 1, rows);
    }

    private static FlowField Field(NavGraph g, CellId goal, NavCellHazard? hazard, float weight)
    {
        var f = new FlowField();
        f.Build(g, goal, ClearanceClass.Stand, new NavObstacleState(0) { Hazard = hazard, HazardWeight = weight });
        return f;
    }

    /// <summary>Whether the route from the start goes over the top.</summary>
    private static bool GoesRound(FlowField f, int[,] ids) => Grids.Walk(f, ids[0, 3]).Contains(ids[10, 0]);

    [Fact]
    public void TheSeededBinsCostTheCellsOnThemAndNoOthers()
    {
        var (g, ids) = Grids.Parse(TwoRoutes);
        NavCellHazard red = ExperienceRoutes.Build(Seeded(), g).For(Red)!;
        Assert.Equal(8, red.Costed);
        for (int x = 0; x < 20; x++)
            Assert.Equal(x is >= 6 and <= 13 ? 2f : 0f, red.SecondsAt(ids[x, 3]), 4);
        Assert.Equal(0f, red.SecondsAt(ids[10, 0]));
    }

    [Fact]
    public void ACunningEightBotsFieldGoesByTheOtherRoute()
    {
        var (g, ids) = Grids.Parse(TwoRoutes);
        NavCellHazard red = ExperienceRoutes.Build(Seeded(), g).For(Red)!;
        FlowField f = Field(g, Grids.At(ids, 19, 3), red, 0.8f);
        Assert.True(GoesRound(f, ids));
        // Its cost from the start is the long way's, 800 u: the seeded stretch would have cost 608 + 256 x 1.6.
        Assert.Equal(800f, f.CostAt(ids[0, 3]), 2);
    }

    [Fact]
    public void ACunningOneBotsFieldTakesTheShorterRoute()
    {
        var (g, ids) = Grids.Parse(TwoRoutes);
        NavCellHazard red = ExperienceRoutes.Build(Seeded(), g).For(Red)!;
        FlowField f = Field(g, Grids.At(ids, 19, 3), red, 0.1f);
        Assert.False(GoesRound(f, ids));
        Assert.Equal(608f + 256f * 0.2f, f.CostAt(ids[0, 3]), 2);
    }

    /// <summary>The choice flips where the seeded stretch's added cost passes the long way's 192 u: at a weight of 0.375.</summary>
    [Fact]
    public void TheChoiceFlipsWhereTheAddedCostPassesTheLongWay()
    {
        var (g, ids) = Grids.Parse(TwoRoutes);
        NavCellHazard red = ExperienceRoutes.Build(Seeded(), g).For(Red)!;
        bool[] round = [.. Enumerable.Range(0, 11).Select(cunning => GoesRound(Field(g, Grids.At(ids, 19, 3), red, cunning / 10f), ids))];
        Assert.Equal([false, false, false, false, true, true, true, true, true, true, true], round);
    }

    [Fact]
    public void ADeadlyStretchOnTheOnlyWayCostsAtMostFourTimesItsWalkAndStaysOpen()
    {
        var (g, ids) = Grids.Parse("....................");
        // Every bin of the row: 5 deaths in a second, 100 s of death a second.
        var rows = new ExperienceDelta();
        for (int x = 0; x < 10; x++)
            rows.Add(new CellKey(new ExperienceContext(0, Red, 3), new WorldBin(x, 0, 0), ExperienceSource.Human), new CellStat(1d, 5d, 0d, 0d, 0d));
        NavCellHazard red = ExperienceRoutes.Build(new MapExperience(Key, 1, rows), g).For(Red)!;
        FlowField f = Field(g, Grids.At(ids, 19, 0), red, 1f);
        Assert.Equal(19 * 32f * (1f + NavCellHazard.MaxScale), f.CostAt(ids[0, 0]), 2);
        Assert.Equal(20, f.Reachable);
    }

    [Fact]
    public void BlueDeathsDoNotCostARedRoute()
    {
        var (g, _) = Grids.Parse(TwoRoutes);
        ExperienceRoutes routes = ExperienceRoutes.Build(Seeded(role: Blue), g);
        Assert.Null(routes.For(Red));
        Assert.NotNull(routes.For(Blue));
        Assert.Equal([Blue], routes.Roles);
    }

    [Fact]
    public void AnEmptyExperienceHasNoField()
    {
        var (g, _) = Grids.Parse(TwoRoutes);
        ExperienceRoutes routes = ExperienceRoutes.Build(MapExperience.Empty, g);
        Assert.Empty(routes.Roles);
        Assert.Equal(0, routes.Costed);
    }

    /// <summary>
    /// With the empty experience, with a weight of 0, and with a field of another graph, a field is the one
    /// built without the feature, cell for cell (its cost, its next cell and how it steps): HL2MP, which keeps
    /// no store, and a TF2 server with an empty one route exactly as before.
    /// </summary>
    [Fact]
    public void WithNoHazardTheFieldIsTheOneBeforeItCellForCell()
    {
        var (g, ids) = Grids.Parse(
            "............",
            ". .. . .. ..",
            "....  ......",
            ".  .  .  . .",
            "..... ......");
        var (other, _) = Grids.Parse(TwoRoutes);
        CellId goal = Grids.At(ids, 11, 4);
        var before = new FlowField();
        before.Build(g, goal, ClearanceClass.Stand, new NavObstacleState(0));
        NavCellHazard? empty = ExperienceRoutes.Build(MapExperience.Empty, g).For(Red);
        NavCellHazard seeded = ExperienceRoutes.Build(Seeded(), g).For(Red)!;
        NavCellHazard foreign = ExperienceRoutes.Build(Seeded(), other).For(Red)!;
        // The control: the seeded field at a weight changes the costs.
        Assert.NotEqual(before.CostAt(ids[10, 4]), Field(g, goal, seeded, 1f).CostAt(ids[10, 4]));
        foreach (FlowField with in new[] { Field(g, goal, empty, 1f), Field(g, goal, seeded, 0f), Field(g, goal, foreign, 1f) })
        {
            Assert.Null(with.Hazard);
            for (int c = 0; c < g.CellCount; c++)
            {
                Assert.Equal(before.CostAt(c), with.CostAt(c));
                Assert.Equal(before.NextFrom(c), with.NextFrom(c));
                Assert.Equal(before.ViaFrom(c), with.ViaFrom(c));
            }
        }
    }

    [Fact]
    public void AShovedCopyAndACloneCarryTheHazard()
    {
        var (g, _) = Grids.Parse(TwoRoutes);
        NavCellHazard red = ExperienceRoutes.Build(Seeded(), g).For(Red)!;
        var state = new NavObstacleState(0) { Hazard = red, HazardWeight = 0.5f };
        var copy = new NavObstacleState(0);
        copy.CopyFrom(state);
        foreach (NavObstacleState s in new[] { state.Clone(), state.Shoved(), copy })
        {
            Assert.Same(red, s.Hazard);
            Assert.Equal(0.5f, s.HazardWeight);
        }

        // A shoved copy follows its original's weight as a lane refreshes it.
        NavObstacleState shoved = state.Shoved();
        state.HazardWeight = 0.9f;
        Assert.Equal(0.9f, shoved.HazardWeight);
    }
}
