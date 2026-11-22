using System.Collections.Immutable;
using SourceSharp.Ai.TF2.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The measure of how spread the bots' routes are (lane Groutes, <see cref="TFRouteSpread"/>): walks are cut into bands
/// along the line from their mean start to the goal, their sideways offsets compared per band, and the walks that arrived
/// grouped into corridors.
/// </summary>
public sealed class TFRouteSpreadTests
{
    private const int Red = 2, Blue = 3;
    private static readonly AiVector Goal = new(4000f, 0f, 0f);

    /// <summary>A walk along x from 0 to the goal, <paramref name="side"/> to the left of the line, one sample every 100 u, arriving.</summary>
    private static RouteWalk Along(int slot, float side, int team = Blue)
    {
        var w = new RouteWalk(slot, 1, team, 7, Goal, 0, new AiVector(0f, side, 0f));
        int tick = 0;
        for (float x = 100f; x < Goal.X - 200f; x += 100f)
            w.Add(new AiVector(x, side, 0f), tick += 10);
        w.Add(Goal, tick + 10);
        return w;
    }

    [Fact]
    public void TwoWalksAHallApartAreTwoCorridorsAndSpreadByHalfTheirDistance()
    {
        RouteGroup g = TFRouteSpread.MeasureGroup([Along(1, 500f), Along(2, -500f), Along(3, 500f)], 0.015f);
        Assert.Equal([2, 1], g.Corridors);
        Assert.Equal(3, g.Reached);
        RouteBand[] middle = [.. g.Middle];
        Assert.NotEmpty(middle);
        // Offsets 500, -500 and 500 about the mean start's line (y = 166.7): a standard deviation of 471.
        Assert.All(middle, b => Assert.InRange(b.Sd, 470f, 472f));
    }

    [Fact]
    public void WalksOfOneHallAreOneCorridorWithLittleSpread()
    {
        RouteGroup g = TFRouteSpread.MeasureGroup([Along(1, 40f), Along(2, -60f), Along(3, 0f), Along(4, 150f)], 0.015f);
        Assert.Equal([4], g.Corridors);
        Assert.All(g.Middle, b => Assert.True(b.Sd < 100f, $"band {b.Index}: {b.Sd}"));
    }

    [Fact]
    public void AWalkThatDidNotArriveSpreadsTheBandsButIsNoCorridor()
    {
        RouteWalk lost = new(9, 1, Blue, 7, Goal, 0, new AiVector(0f, 900f, 0f));
        for (int i = 1; i < 20; i++)
            lost.Add(new AiVector(i * 100f, 900f, 0f), i * 10);
        lost.Open = false;
        RouteGroup g = TFRouteSpread.MeasureGroup([Along(1, 0f), Along(2, 0f), Along(3, 0f), lost], 0.015f);
        Assert.Equal(3, g.Reached);
        Assert.Equal([3], g.Corridors);
        RouteBand band1 = g.Bands.Single(b => b.Index == 1);
        Assert.Equal(4, band1.Walks);
        Assert.True(band1.Sd > 300f, $"{band1.Sd}");
    }

    [Fact]
    public void ALeftOffsetIsPlusAndEachBandHoldsItsOwnPoints()
    {
        Dictionary<int, float> sides = TFRouteSpread.BandSides([new AiVector(100f, 50f, 0f), new AiVector(700f, 150f, 0f), new AiVector(800f, -200f, 0f)],
                                                               AiVector.Zero, Goal);
        Assert.Equal(100f, sides[0]);
        Assert.Equal(-200f, sides[1]);
    }

    [Fact]
    public void AWalkNotesTheFrontAndArrivesOnThePoint()
    {
        var w = new RouteWalk(1, 1, Blue, 7, Goal, 100, AiVector.Zero);
        w.Add(new AiVector(2000f, 0f, 0f), 200);
        Assert.Equal(-1, w.FrontTick);
        w.Add(new AiVector(3100f, 0f, 0f), 300);
        Assert.Equal(300, w.FrontTick);
        Assert.True(w.Open);
        w.Add(new AiVector(3800f, 0f, 0f), 400);
        Assert.Equal(400, w.ArriveTick);
        Assert.False(w.Open);
        RouteGroup g = TFRouteSpread.MeasureGroup([w], 0.015f);
        Assert.Equal([3f], g.FrontSeconds.Select(s => MathF.Round(s, 3)));
        Assert.Equal([4.5f], g.PointSeconds.Select(s => MathF.Round(s, 3)));
    }

    [Fact]
    public void TheGoalIsTheNearestPointTheTeamMayCaptureElseTheOtherTeamsFlag()
    {
        int blue = 1 << Blue, red = 1 << Red;
        var points = new ObjectiveKnowledge(ImmutableArray.Create(
            new Objective(1, ObjectiveKind.Capture, Red, blue, ObjectiveState.Open, new AiVector(9000f, 0f, 0f)),
            new Objective(2, ObjectiveKind.Capture, 0, blue | red, ObjectiveState.Open, new AiVector(3000f, 0f, 0f)),
            new Objective(3, ObjectiveKind.Capture, 0, blue, ObjectiveState.Locked, new AiVector(100f, 0f, 0f)),
            new Objective(4, ObjectiveKind.Capture, Blue, red, ObjectiveState.Open, new AiVector(50f, 0f, 0f))));
        Assert.Equal(2, TFRouteSpread.GoalOf(points, Blue, AiVector.Zero).Id);
        Assert.Equal(1, TFRouteSpread.GoalOf(points, Blue, new AiVector(8000f, 0f, 0f)).Id);
        Assert.Equal(4, TFRouteSpread.GoalOf(points, Red, AiVector.Zero).Id);

        var flags = new ObjectiveKnowledge(ImmutableArray.Create(
            new Objective(5, ObjectiveKind.Carry, Red, blue | red, ObjectiveState.Home, new AiVector(-3000f, 0f, 0f)),
            new Objective(6, ObjectiveKind.Carry, Blue, blue | red, ObjectiveState.Home, new AiVector(3000f, 0f, 0f))));
        Assert.Equal(5, TFRouteSpread.GoalOf(flags, Blue, AiVector.Zero).Id);
        Assert.Equal(6, TFRouteSpread.GoalOf(flags, Red, AiVector.Zero).Id);
        Assert.Equal(-1, TFRouteSpread.GoalOf(new ObjectiveKnowledge([]), Blue, AiVector.Zero).Id);
    }
}
