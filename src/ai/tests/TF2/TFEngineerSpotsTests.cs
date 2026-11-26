using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Where the Engineer builds his sentry (<see cref="TFEngineerSpots"/>): stock's candidate rules (tf_bot_engineer_move_to_build.cpp:
/// 135-186) and the research's score, on <see cref="TFField.Floor"/>'s strip (64 by 8 cells of 64 u).
/// </summary>
public sealed class TFEngineerSpotsTests
{
    private static readonly AiVector Point = new(2048f, 256f, 0f), OwnSpawn = new(64f, 256f, 0f), EnemySpawn = new(4032f, 256f, 0f);

    private sealed class Sees(Func<int, int, bool> visible, Func<int, float>? exposure = null) : ICellSight
    {
        public float Range => 4096f;

        public bool Visible(int a, int b) => visible(a, b);

        public float Exposure(int cell) => exposure?.Invoke(cell) ?? 0f;
    }

    private static List<TFSentrySpot> Candidates(NavGraph g, ICellSight? sight = null, TeamZone[]? zones = null, AiVector? point = null)
        => TFEngineerSpots.Candidates(g, sight, point ?? Point, TFField.Red, [.. zones ?? []], OwnSpawn, EnemySpawn);

    [Fact]
    public void NoCandidateStandsOnThePoint()
    {
        List<TFSentrySpot> spots = Candidates(TFField.Floor());
        Assert.NotEmpty(spots);
        Assert.All(spots, s => Assert.True(Flat(s.At, Point) >= TFEngineerSpots.OffPoint, $"{s.At}"));
    }

    [Fact]
    public void NoCandidateIsOutOfTheSentrysReach()
    {
        List<TFSentrySpot> spots = Candidates(TFField.Floor());
        Assert.All(spots, s => Assert.True(Flat(s.At, Point) <= TFEngineerSpots.SentryRange * TFEngineerSpots.RangeTolerance, $"{s.At}"));
        Assert.Contains(spots, s => Flat(s.At, Point) > 1000f);
    }

    [Fact]
    public void NoCandidateIsInARespawnRoom()
    {
        var room = new TeamZone(TFField.Red, ZoneKind.RespawnRoom, new AiVector(1024f, 0f, -16f), new AiVector(1536f, 512f, 200f));
        List<TFSentrySpot> spots = Candidates(TFField.Floor(), zones: [room]);
        Assert.Contains(Candidates(TFField.Floor()), s => room.Contains(s.At + new AiVector(0f, 0f, 16f)));
        Assert.DoesNotContain(spots, s => room.Contains(s.At + new AiVector(0f, 0f, 16f)));
    }

    [Fact]
    public void ACellThatDoesNotSeeThePointIsNoCandidate()
    {
        NavGraph g = TFField.Floor();
        // Only the cells on the strip's north row (y 448-512) see the point.
        List<TFSentrySpot> spots = Candidates(g, new Sees((a, _) => g.Cell(a).MinY >= 448f));
        Assert.NotEmpty(spots);
        Assert.All(spots, s => Assert.True(s.At.Y > 448f, $"{s.At}"));
    }

    [Fact]
    public void TheBestSpotIsOnHisOwnSideOfThePoint()
        => Assert.True(Candidates(TFField.Floor())[0].At.X < Point.X);

    [Fact]
    public void ALessExposedSpotScoresAboveAnOpenOne()
    {
        NavGraph g = TFField.Floor();
        // The south row is in the open; the rest is covered.
        List<TFSentrySpot> spots = Candidates(g, new Sees((_, _) => true, c => g.Cell(c).MinY < 64f ? 1f : 0f));
        Assert.True(spots[0].At.Y > 64f);
        TFSentrySpot open = spots.First(s => s.At.Y < 64f), covered = spots.First(s => s.At.Y > 64f && MathF.Abs(s.At.X - open.At.X) < 1f);
        // By the exposure's weight (0.75 for the whole share): the other terms differ between the two by far less.
        Assert.True(covered.Score - open.Score > 0.5f, $"{covered.Score} against {open.Score}");
    }

    [Fact]
    public void TheGunFacesThePoint()
    {
        TFSentrySpot s = Candidates(TFField.Floor())[0];
        AiVector to = new AiVector(Point.X - s.At.X, Point.Y - s.At.Y, 0f).Normalised();
        AiVector facing = TFEngineerSpots.Forward(s.Yaw);
        Assert.True(to.X * facing.X + to.Y * facing.Y > 0.999f);
    }

    [Fact]
    public void AFailedSpotIsNeverPickedAgain()
    {
        List<TFSentrySpot> spots = Candidates(TFField.Floor());
        var failed = new HashSet<int>(spots.Take(TFEngineerSpots.DrawFrom).Select(s => s.Cell));
        for (ulong seed = 1; seed <= 50; seed++)
        {
            TFSentrySpot? pick = TFEngineerSpots.Pick(spots, failed, new AiRandom(seed));
            Assert.NotNull(pick);
            Assert.DoesNotContain(pick!.Value.Cell, failed);
        }
    }

    [Fact]
    public void TwoSeedsCanPickTwoSpots()
    {
        List<TFSentrySpot> spots = Candidates(TFField.Floor());
        var picked = new HashSet<int>();
        for (ulong seed = 1; seed <= 50; seed++)
            picked.Add(TFEngineerSpots.Pick(spots, new HashSet<int>(), new AiRandom(seed))!.Value.Cell);
        Assert.True(picked.Count > 1);
        Assert.All(picked, c => Assert.Contains(c, spots.Take(TFEngineerSpots.DrawFrom).Select(s => s.Cell)));
    }

    /// <summary>
    /// With candidates on his side of the point, none on the enemy's (by the walk from each spawn): live on cp_granary a spot
    /// over the far side of the middle had him walk through the enemy and never build.
    /// </summary>
    [Fact]
    public void NoCandidateIsOnTheEnemysSideWhenHisSideHasOne()
    {
        List<TFSentrySpot> spots = Candidates(TFField.Floor());
        Assert.NotEmpty(spots);
        Assert.All(spots, s => Assert.True(s.At.X < Point.X, $"{s.At}"));
    }

    /// <summary>A spot on a cell no walk reaches (an island of the graph) is not reachable; one on the floor is.</summary>
    [Fact]
    public void AnIslandSpotIsNotReachable()
    {
        var b = new NavGraphBuilder();
        CellId a = b.AddCell(new NavCell(0f, 0f, 64f, 64f, 0f, ClearanceClasses.Stand, NavCellFlags.None, 1f));
        CellId c = b.AddCell(new NavCell(64f, 0f, 128f, 64f, 0f, ClearanceClasses.Stand, NavCellFlags.None, 1f));
        CellId island = b.AddCell(new NavCell(512f, 0f, 576f, 64f, 0f, ClearanceClasses.Stand, NavCellFlags.None, 1f));
        b.AddBoth(a, c, NavEdgeType.Walk);
        NavGraph g = b.Build();
        var from = new AiVector(32f, 32f, 0f);
        Assert.True(TFEngineerSpots.Reachable(g, from, new TFSentrySpot(c.Index, new AiVector(96f, 32f, 0f), 0f, 0f)));
        Assert.False(TFEngineerSpots.Reachable(g, from, new TFSentrySpot(island.Index, new AiVector(544f, 32f, 0f), 0f, 0f)));
    }

    /// <summary>A point whose place stands over no floor (an origin on its model) still finds its cell: the nearest one round it.</summary>
    [Fact]
    public void APointOffTheFloorStillFindsItsCell()
    {
        NavGraph g = TFField.Floor();
        Assert.False(g.FindCell(new AiVector(2048f, 600f, 0f)).IsValid);
        CellId cell = TFEngineerSpots.PointCell(g, new AiVector(2048f, 560f, 0f));
        Assert.True(cell.IsValid);
        Assert.True(g.Cell(cell.Index).Centre.DistanceTo(new AiVector(2048f, 560f, 0f)) < TFEngineerSpots.PointCellReach);
    }

    private static float Flat(AiVector a, AiVector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
