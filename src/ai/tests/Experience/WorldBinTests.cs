using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// The experience store's places and buckets (plan_bots_teams_tf2 Part G,
/// G.3): bins 64 units square and 72 tall, 16 yaw and 3 pitch buckets, the
/// context a query names, and the decay arithmetic.
/// </summary>
public sealed class WorldBinTests
{
    [Fact]
    public void ABinIs64UnitsSquare()
    {
        Assert.Equal(WorldBin.Of(new AiVector(0f, 0f, 0f)), WorldBin.Of(new AiVector(63.9f, 63.9f, 0f)));
        Assert.Equal(new WorldBin(1, 0, 0), WorldBin.Of(new AiVector(64f, 0f, 0f)));
        Assert.Equal(new WorldBin(0, 1, 0), WorldBin.Of(new AiVector(0f, 64f, 0f)));
    }

    [Fact]
    public void ABinIs72UnitsTall()
    {
        Assert.Equal(new WorldBin(0, 0, 0), WorldBin.Of(new AiVector(0f, 0f, 71.9f)));
        Assert.Equal(new WorldBin(0, 0, 1), WorldBin.Of(new AiVector(0f, 0f, 72f)));
    }

    [Fact]
    public void APointBelowZeroIsInTheBinBelow()
    {
        Assert.Equal(new WorldBin(-1, -1, -1), WorldBin.Of(new AiVector(-0.5f, -0.5f, -0.5f)));
        Assert.Equal(new WorldBin(-2, -1, -1), WorldBin.Of(new AiVector(-64.5f, -64f, -72f)));
    }

    [Fact]
    public void ABinsCentreIsInTheBin()
    {
        var bin = new WorldBin(-3, 7, 2);
        Assert.Equal(new AiVector(-160f, 480f, 180f), bin.Centre);
        Assert.Equal(bin, WorldBin.Of(bin.Centre));
    }

    [Fact]
    public void APackedBinUnpacksToItself()
    {
        foreach (WorldBin bin in new[] { new WorldBin(0, 0, 0), new WorldBin(-256, 255, -228), new WorldBin(12345, -54321, 999), new WorldBin(-1, -1, -1) })
            Assert.Equal(bin, WorldBin.Unpack(bin.Packed));
    }

    [Fact]
    public void TwoBinsNeverPackToOneNumber()
    {
        Assert.NotEqual(new WorldBin(1, 0, 0).Packed, new WorldBin(0, 1, 0).Packed);
        Assert.NotEqual(new WorldBin(0, 1, 0).Packed, new WorldBin(0, 0, 1).Packed);
        Assert.NotEqual(new WorldBin(-1, 0, 0).Packed, new WorldBin(0, -1, 0).Packed);
    }

    [Fact]
    public void YawHasSixteenBucketsCentredOnMultiplesOf22Point5()
    {
        Assert.Equal(0, LookBuckets.Yaw(0f));
        Assert.Equal(0, LookBuckets.Yaw(11f));
        Assert.Equal(1, LookBuckets.Yaw(11.5f));
        Assert.Equal(4, LookBuckets.Yaw(90f));
        Assert.Equal(8, LookBuckets.Yaw(180f));
        Assert.Equal(15, LookBuckets.Yaw(337.5f));
        Assert.Equal(90f, LookBuckets.YawCentre(4));
    }

    [Fact]
    public void AYawOutsideAFullTurnWrapsIntoIt()
    {
        Assert.Equal(0, LookBuckets.Yaw(-11f));
        Assert.Equal(15, LookBuckets.Yaw(-22.5f));
        Assert.Equal(12, LookBuckets.Yaw(-90f));
        Assert.Equal(0, LookBuckets.Yaw(359f));
        Assert.Equal(4, LookBuckets.Yaw(450f));
    }

    [Fact]
    public void PitchHasThreeBucketsUpLevelAndDown()
    {
        Assert.Equal(0, LookBuckets.Pitch(-45f));
        Assert.Equal(1, LookBuckets.Pitch(-20f));
        Assert.Equal(1, LookBuckets.Pitch(0f));
        Assert.Equal(1, LookBuckets.Pitch(20f));
        Assert.Equal(2, LookBuckets.Pitch(45f));
    }

    [Fact]
    public void AQueryFieldOfAnyTakesEveryValue()
    {
        Assert.True(ExperienceContext.All.Takes(new ExperienceContext(2, 1, 7)));
        Assert.True(new ExperienceContext(ExperienceContext.Any, 1, ExperienceContext.Any).Takes(new ExperienceContext(2, 1, 7)));
    }

    [Fact]
    public void ANamedQueryFieldTakesOnlyItsValue()
    {
        var query = new ExperienceContext(ExperienceContext.Any, 1, 7);
        Assert.False(query.Takes(new ExperienceContext(0, 2, 7)));
        Assert.False(query.Takes(new ExperienceContext(0, 1, 3)));
        Assert.False(new ExperienceContext(1, ExperienceContext.Any, ExperienceContext.Any).Takes(new ExperienceContext(0, 1, 7)));
    }

    [Fact]
    public void ARowIsHalvedByEachHalfLifeOfRounds()
    {
        Assert.Equal(0.5d, ExperienceDecay.Factor(20, 20d), 12);
        Assert.Equal(0.25d, ExperienceDecay.Factor(40, 20d), 12);
        Assert.Equal(Math.Pow(0.5d, 0.5d), ExperienceDecay.Factor(10, 20d), 12);
    }

    [Fact]
    public void ARowWrittenThisRoundIsWhole()
    {
        Assert.Equal(1d, ExperienceDecay.Factor(0, 20d));
        Assert.Equal(1d, ExperienceDecay.Factor(-3, 20d));
    }

    [Fact]
    public void AHalfLifeOfZeroNeverDecaysOrForgets()
    {
        Assert.Equal(1d, ExperienceDecay.Factor(1000, 0d));
        Assert.False(ExperienceDecay.Forgotten(1_000_000, 0d));
    }

    [Fact]
    public void ARowIsForgottenPastTenHalfLives()
    {
        Assert.False(ExperienceDecay.Forgotten(200, 20d));
        Assert.True(ExperienceDecay.Forgotten(201, 20d));
    }
}
