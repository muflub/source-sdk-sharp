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

    /// <summary>
    /// A cell with no way back to his spawn (a drop he could not climb) is still a candidate, measured as the crow flies: a field
    /// to the spawn is each cell's walk to it, not from it (the review's C3: on cp_granary a forward spawn emptied the list).
    /// </summary>
    [Fact]
    public void ACellWithNoWayBackToHisSpawnIsStillACandidate()
    {
        // A strip west to east; the cells east of x 1536 are reached one way only (a drop): no walk leads back west.
        var b = new NavGraphBuilder();
        var ids = new CellId[TFField.Length, TFField.Width];
        for (int x = 0; x < TFField.Length; x++)
            for (int y = 0; y < TFField.Width; y++)
                ids[x, y] = b.AddCell(new NavCell(x * 64f, y * 64f, (x + 1) * 64f, (y + 1) * 64f, 0f, ClearanceClasses.Stand, NavCellFlags.None, 1f));
        for (int x = 0; x < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
            {
                if (x + 1 < TFField.Length)
                {
                    if (x + 1 == 24)
                        b.AddEdge(ids[x, y], ids[x + 1, y], NavEdgeType.Walk);
                    else
                        b.AddBoth(ids[x, y], ids[x + 1, y], NavEdgeType.Walk);
                }

                if (y + 1 < TFField.Width)
                    b.AddBoth(ids[x, y], ids[x, y + 1], NavEdgeType.Walk);
            }
        }

        List<TFSentrySpot> spots = Candidates(b.Build());
        Assert.Contains(spots, s => s.At.X > 1536f);
    }

    /// <summary>The axis-aligned gap between a body's box (24 u) and a building's (20 u) at those middles: under 0 they overlap.</summary>
    private static float BoxGap(AiVector body, AiVector building)
        => MathF.Max(MathF.Abs(body.X - building.X), MathF.Abs(body.Y - building.Y)) - (24f + 20f);

    /// <summary>
    /// Round 1's stand was 45 u from the gun towards him: along a diagonal that put his box 13 u into the gun's, and on both maps
    /// a grown gun held him where he stood (2026-11-24). Every way he may come from, the stand clears the gun's box.
    /// </summary>
    [Fact]
    public void TheWorkStandClearsTheGunsBoxFromEveryWay()
    {
        AiVector gun = new(1024f, 256f, 0f);
        for (int k = 0; k < 16; k++)
        {
            // He stands right by the gun: the stand nearest him must still clear it.
            AiVector from = gun + TFEngineerSpots.Forward(k * 22.5f) * 30f;
            AiVector at = TFEngineerSpots.WorkStand(TFField.Floor(), gun, null, [gun], from - gun, from);
            Assert.True(BoxGap(at, gun) >= 8f, $"from {k * 22.5f} deg: {at}, gap {BoxGap(at, gun)}");
        }
    }

    [Fact]
    public void TheWorkStandIsInTheWrenchsReachOfTheGun()
    {
        AiVector gun = new(1024f, 256f, 0f);
        for (int k = 0; k < 16; k++)
        {
            AiVector from = gun + TFEngineerSpots.Forward(k * 22.5f) * 300f;
            AiVector at = TFEngineerSpots.WorkStand(TFField.Floor(), gun, null, [gun], from - gun, from);
            // The wrench's swing (48 u from the eye and an 18 u hull) reaches the gun's face, 20 u off its middle, from 80 u.
            Assert.InRange(at.DistanceTo(gun), 1f, 80f);
        }
    }

    [Fact]
    public void BesideHisDispenserTheWorkStandIsInItsReachAndClearOfBoth()
    {
        AiVector gun = new(1024f, 256f, 0f), dispenser = new(1024f + 72f, 256f + 64f, 0f);
        AiVector at = TFEngineerSpots.WorkStand(TFField.Floor(), gun, dispenser, [gun, dispenser], new AiVector(-1f, 0f, 0f), gun - new AiVector(300f, 0f, 0f));
        Assert.True(BoxGap(at, gun) >= 8f && BoxGap(at, dispenser) >= 8f, $"{at}");
        Assert.True(Flat(at, dispenser) <= TFEngineerSpots.NestFeedUnits, $"{at}: {Flat(at, dispenser)} from the dispenser");
    }

    [Fact]
    public void TheWorkStandIsOnFloorAtTheBuildingsHeight()
    {
        // The strip's floor is at 0; a gun on a ledge 256 u up has no floor round it at its height: no stand on the floor below.
        AiVector gun = new(1024f, 256f, 256f);
        AiVector at = TFEngineerSpots.WorkStand(TFField.Floor(), gun, null, [gun], new AiVector(-1f, 0f, 0f), gun);
        Assert.Equal(256f, at.Z);
    }

    /// <summary>Live on cp_granary, flat arrival held him 256 u under his nest for 74 s, his wrench reaching nothing.</summary>
    [Fact]
    public void AStandUnderALedgeIsNotTheStandOnIt()
    {
        AiVector stand = new(100f, 100f, -160f);
        Assert.True(TFEngineerSpots.Arrived(stand + new AiVector(5f, 0f, 0f), stand, 20f));
        Assert.False(TFEngineerSpots.Arrived(stand - new AiVector(0f, 0f, 256f), stand, 20f));
    }

    private static float Flat(AiVector a, AiVector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
