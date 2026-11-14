using Xunit;

namespace SourceSharp.Ai.Tests.Motion;

/// <summary>
/// Getting out of a blast's way (plan_bots_teams_tf2_part_g G.2.3, <see cref="BlastDodge"/>): which
/// flights and which things at rest come within their blast of a body, when, and the way out.
/// </summary>
public sealed class BlastDodgeTests
{
    private const float Reach = 170f, RocketSpeed = 1100f;
    private static readonly AiVector Body = new(0f, 0f, 41f);

    private static bool Rocket(AiVector from, AiVector toward, out float seconds, out AiVector nearest)
        => BlastDodge.Threatens(Body, 0f, from, (toward - from).Normalised() * RocketSpeed, 0f, Reach, out seconds, out nearest);

    [Fact]
    public void ARocketFlyingAtTheBodyThreatensItWhenItArrives()
    {
        Assert.True(Rocket(new AiVector(880f, 0f, 41f), Body, out float seconds, out AiVector nearest));
        Assert.Equal(0.8f, seconds, 3);
        Assert.True(nearest.DistanceTo(Body) < 1f, $"nearest at {nearest}");
    }

    [Fact]
    public void ARocketWhosePathPassesWiderThanItsBlastIsNoThreat()
        => Assert.False(Rocket(new AiVector(880f, Reach + 40f, 41f), new AiVector(0f, Reach + 40f, 41f), out _, out _));

    [Fact]
    public void ARocketWhosePathPassesInsideItsBlastThreatens()
        => Assert.True(Rocket(new AiVector(880f, Reach - 40f, 41f), new AiVector(0f, Reach - 40f, 41f), out _, out _));

    [Fact]
    public void ARocketFlyingAwayIsNoThreat()
        => Assert.False(Rocket(new AiVector(60f, 0f, 41f), new AiVector(900f, 0f, 41f), out _, out _));

    [Fact]
    public void ARocketFurtherOffThanTheHorizonIsNotYetAThreat()
        => Assert.False(Rocket(new AiVector(RocketSpeed * (BlastDodge.HorizonSeconds + 0.3f), 0f, 41f), Body, out _, out _));

    /// <summary>A pipe lobbed up from 560 u off that comes down by the body: its fall is read, not its first heading.</summary>
    [Fact]
    public void APipeThatLandsByTheBodyThreatens()
    {
        // Up and across at 700 u/s: it is down after 0.71 s, some 60 u short of the body.
        Assert.True(BlastDodge.Threatens(Body, 0f, new AiVector(560f, 0f, 60f), new AiVector(-700f, 0f, 200f), 800f, Reach, out float seconds, out AiVector nearest));
        Assert.InRange(seconds, 0.3f, 1.2f);
        Assert.True(nearest.DistanceTo(Body) < Reach, $"nearest at {nearest}");
    }

    /// <summary>The same throw from further off lands short: it lies where it landed, out of reach.</summary>
    [Fact]
    public void APipeThatLandsShortIsNoThreat()
        => Assert.False(BlastDodge.Threatens(Body, 0f, new AiVector(1500f, 0f, 60f), new AiVector(-700f, 0f, 200f), 800f, Reach, out _, out _));

    [Fact]
    public void AThingLyingWithinItsBlastOfTheBodyThreatensNow()
    {
        Assert.True(BlastDodge.Threatens(Body, 0f, new AiVector(100f, 0f, 4f), AiVector.Zero, 800f, Reach, out float seconds, out _));
        Assert.Equal(0f, seconds);
    }

    [Fact]
    public void AThingLyingFurtherOffThanItsBlastIsNoThreat()
        => Assert.False(BlastDodge.Threatens(Body, 0f, new AiVector(Reach + 60f, 0f, 4f), AiVector.Zero, 800f, Reach, out _, out _));

    /// <summary>The way out of a flight passing on the body's left is to its right: across the path, on the body's own side.</summary>
    [Fact]
    public void TheWayOutIsAcrossThePathOnTheBodysOwnSide()
    {
        var from = new AiVector(880f, 60f, 41f);
        Rocket(from, new AiVector(0f, 60f, 41f), out _, out AiVector nearest);
        // Told the other side for a tie: it is no tie, and the body's own side wins.
        AiVector away = BlastDodge.Away(Body, new AiVector(-RocketSpeed, 0f, 0f), nearest, -1);
        Assert.Equal((0f, -1f), (MathF.Round(away.X, 3), MathF.Round(away.Y, 3)));
    }

    [Theory]
    [InlineData(1, -1f)]
    [InlineData(-1, 1f)]
    public void ABodyRightOnThePathGoesToTheSideItIsTold(int side, float y)
    {
        Rocket(new AiVector(880f, 0f, 41f), Body, out _, out AiVector nearest);
        AiVector away = BlastDodge.Away(Body, new AiVector(-RocketSpeed, 0f, 0f), nearest, side);
        Assert.Equal((0f, y), (MathF.Round(away.X, 3), MathF.Round(away.Y, 3)));
    }

    [Fact]
    public void TheWayOutFromAThingLyingByTheBodyIsStraightAwayFromIt()
    {
        AiVector away = BlastDodge.Away(Body, AiVector.Zero, new AiVector(100f, 0f, 4f), 1);
        Assert.Equal((-1f, 0f, 0f), (away.X, away.Y, away.Z));
    }
}
