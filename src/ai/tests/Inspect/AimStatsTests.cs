using Xunit;

namespace SourceSharp.Ai.Tests.Inspect;

/// <summary>The aim meter's arithmetic (plan_hl2mp_ai §12.3): each number a tuning table quotes comes from here.</summary>
public sealed class AimStatsTests
{
    private const int Target = 3;
    private const int Smg1 = 4;

    private static AimSample Seen(float error = 1f, bool onBody = true, bool attack = false, string leaf = "hold range", float distance = 500f)
        => new(true, true, true, error, onBody, attack, leaf, distance);

    private static void Tick(AimStats s, int tick, in AimSample sample)
    {
        s.Observe(Target, tick, in sample);
    }

    [Theory]
    [InlineData(0f, AimBand.Near)]
    [InlineData(299.9f, AimBand.Near)]
    [InlineData(300f, AimBand.Mid)]
    [InlineData(800f, AimBand.Mid)]
    [InlineData(800.1f, AimBand.Far)]
    public void TheBandsAreTheMetricsThree(float distance, AimBand band) => Assert.Equal(band, AimBands.Of(distance));

    [Fact]
    public void TheFirstShotIsTimedFromComingIntoView()
    {
        var s = new AimStats(8);
        // In view, not yet recognised: ten ticks.
        for (int t = 100; t < 110; t++)
            Tick(s, t, new AimSample(true, false, false, 9f, false, false, "", 500f));
        Tick(s, 110, Seen());
        s.Shot(Smg1, 111);
        Assert.Equal([11], s.Of(Target).FirstShotTicks);
    }

    [Fact]
    public void BackInViewAfterTheGapIsANewEngagement()
    {
        var s = new AimStats(8);
        Tick(s, 100, Seen());
        s.Shot(Smg1, 101);
        for (int t = 101; t < 101 + AimStats.EngagementGapTicks; t++)
            Tick(s, t, new AimSample(false, false, false, 0f, false, false, "", 500f));
        Tick(s, 200, Seen());
        s.Shot(Smg1, 205);
        Assert.Equal(2, s.Of(Target).Engagements);
        Assert.Equal([1, 5], s.Of(Target).FirstShotTicks);
    }

    [Fact]
    public void AShortGapIsTheSameEngagement()
    {
        var s = new AimStats(8);
        Tick(s, 100, Seen());
        for (int t = 101; t < 101 + AimStats.EngagementGapTicks - 1; t++)
            Tick(s, t, new AimSample(false, false, false, 0f, false, false, "", 500f));
        Tick(s, 140, Seen());
        Assert.Equal(1, s.Of(Target).Engagements);
    }

    [Fact]
    public void TheErrorsMeanAndSpreadAreOfTheInSightTicksOnly()
    {
        var s = new AimStats(8);
        Tick(s, 1, new AimSample(true, false, false, 90f, false, false, "", 500f));
        Tick(s, 2, Seen(error: 2f));
        Tick(s, 3, Seen(error: 4f));
        Assert.Equal(3.0, s.Of(Target).ErrorMean, 3);
        Assert.Equal(1.0, s.Of(Target).ErrorSpread, 3);
        Assert.Equal(2, s.Of(Target).InSightTicks);
    }

    [Fact]
    public void AShotAtNobodyInSightIsTheNobodyRows()
    {
        var s = new AimStats(8);
        s.Shot(Smg1, 5);
        Assert.Equal(1, s.Of(0).Shots);
        Assert.Equal(0, s.Of(Target).Shots);
    }

    /// <summary>The metrics' rule: the brain about a player, the crosshair well off it, is a shot at nobody.</summary>
    [Fact]
    public void AShotOutsideTheConeIsNoShotAtThatPlayerAndItsHitIsNoHit()
    {
        var s = new AimStats(8);
        Tick(s, 1, Seen(error: 25f, onBody: false) with { Aimed = false });
        s.Hit(Target, 8, 2);
        s.Shot(Smg1, 2);
        Assert.Equal(0, s.Of(Target).Shots);
        Assert.Equal(0, s.Of(Target).Hits);
        Assert.Equal(8, s.Of(Target).Damage);
        Assert.Equal(1, s.Of(0).Shots);
        Assert.Equal([1], s.Of(Target).FirstShotTicks);
    }

    [Fact]
    public void AShotInTheConeOfAPlayerTheBrainIsNotAboutIsAtThatPlayer()
    {
        var s = new AimStats(8);
        s.Observe(Target, 1, new AimSample(true, true, false, 1f, true, true, "hold range", 500f));
        s.Shot(Smg1, 2);
        Assert.Equal(1, s.Of(Target).Shots);
        Assert.Equal(0, s.Of(Target).InSightTicks);
    }

    /// <summary>A hit comes before its own Fired: an unaimed shot after an aimed one takes its hit with it (seen live: 101.9 %).</summary>
    [Fact]
    public void AHitOfAnUnaimedShotDoesNotCountThroughTheShotBefore()
    {
        var s = new AimStats(8);
        Tick(s, 1, Seen());
        s.Shot(Smg1, 2);
        s.Hit(Target, 8, 20);
        s.Shot(Smg1, 20);
        s.Settle(21);
        Assert.Equal(0, s.Of(Target).Hits);
        Assert.Equal(1, s.Of(Target).Shots);
        Assert.Equal(1, s.Of(0).Shots);
    }

    [Fact]
    public void AProjectilesLateHitCountsThroughItsAimedShot()
    {
        var s = new AimStats(8);
        Tick(s, 1, Seen());
        s.Shot(Smg1, 2);
        s.Hit(Target, 100, 40);
        s.Settle(41);
        Assert.Equal(1, s.Of(Target).Hits);
    }

    [Fact]
    public void ATheCloseTicksAreCountedByLeafWithTheirFiring()
    {
        var s = new AimStats(8);
        s.Close("go heal", true);
        s.Close("go heal", false);
        s.Close("hold range", true);
        Assert.Equal((2L, 1L), s.CloseByLeaf["go heal"]);
        Assert.Equal((1L, 1L), s.CloseByLeaf["hold range"]);
    }

    [Fact]
    public void TheMetersConeIsTheMetricsOne()
    {
        // 25 degrees off at 500 u is out, 1 degree in; the same arithmetic as AiMetrics.
        var eye = new AiVector(0f, 0f, 64f);
        var origin = new AiVector(500f, 0f, 0f);
        var top = new AiVector(500f, 0f, 64f);
        Assert.False(AimCone.Holds(AimCone.AngleToBody(new AiAngles(0f, 25f, 0f), eye, origin, top), 500f));
        Assert.True(AimCone.Holds(AimCone.AngleToBody(new AiAngles(0f, 1f, 0f), eye, origin, top), 500f));
    }

    [Fact]
    public void PelletsOnOneTickAreOneHitAndTheirDamageAddsUp()
    {
        var s = new AimStats(8);
        Tick(s, 1, Seen());
        s.Shot(Smg1, 2);
        s.Hit(Target, 8, 2);
        s.Hit(Target, 8, 2);
        AimStats.Row r = s.Of(Target);
        Assert.Equal(1, r.Hits);
        Assert.Equal(16, r.Damage);
    }

    [Fact]
    public void ShotsAndHitsAreToldByWeaponBandAndOrder()
    {
        var s = new AimStats(8);
        Tick(s, 1, Seen(distance: 500f));
        s.Shot(Smg1, 2);
        Tick(s, 2, Seen(distance: 500f));
        s.Shot(Smg1, 3);
        s.Hit(Target, 4, 3);
        AimStats.Tally t = s.Of(Target).ByWeapon[(Smg1, AimBand.Mid)];
        Assert.Equal(2, t.Shots);
        Assert.Equal(1, t.Hits);
        Assert.Equal([1L, 1L], s.Of(Target).ShotsByOrder[..2]);
        Assert.Equal([0L, 1L], s.Of(Target).HitsByOrder[..2]);
    }

    [Fact]
    public void TheSideMovesFlipsAreTimedWithZerosSkipped()
    {
        var s = new AimStats(8);
        s.Command(10, 100f, 0f);
        s.Command(11, 0f, 0f);
        s.Command(12, 100f, 0f);
        s.Command(15, -50f, 0f);
        s.Command(16, 0f, 0f);
        s.Command(19, 50f, 0f);
        Assert.Equal([5, 4], s.SideIntervalTicks);
        Assert.Equal(6, s.Commands);
    }

    [Fact]
    public void AYawReversalIsASignChangeOfASizableStep()
    {
        var s = new AimStats(8);
        s.Command(1, 0f, 1f);
        s.Command(2, 0f, -0.2f);   // too small to count either way
        s.Command(3, 0f, 1f);
        s.Command(4, 0f, -1f);
        s.Command(5, 0f, 1f);
        Assert.Equal(2, s.YawReversals);
    }

    [Fact]
    public void LeavesAreCountedInSight()
    {
        var s = new AimStats(8);
        Tick(s, 1, Seen(leaf: "hold range"));
        Tick(s, 2, Seen(leaf: "hold range"));
        Tick(s, 3, Seen(leaf: "push"));
        Assert.Equal(2, s.Of(Target).Leaves["hold range"]);
        Assert.Equal(1, s.Of(Target).Leaves["push"]);
    }

    [Fact]
    public void AResetForgetsAllOfIt()
    {
        var s = new AimStats(8);
        Tick(s, 1, Seen(attack: true));
        s.Shot(Smg1, 2);
        s.Reset();
        Assert.Equal(0, s.Of(Target).InSightTicks);
        Assert.Equal(0, s.Of(Target).Shots);
        Assert.Empty(s.Of(Target).ByWeapon);
    }

    [Fact]
    public void TheCrosshairIsOnABoxItsRayEnters()
    {
        var mins = new AiVector(484f, -16f, 0f);
        var maxs = new AiVector(516f, 16f, 72f);
        var eye = new AiVector(0f, 0f, 64f);
        Assert.True(AimStats.RayHitsBox(eye, new AiVector(1f, 0f, 0f), mins, maxs));
        Assert.False(AimStats.RayHitsBox(eye, new AiVector(1f, 0.05f, 0f).Normalised(), mins, maxs), "2.9 degrees aside at 500 u");
        Assert.False(AimStats.RayHitsBox(eye, new AiVector(-1f, 0f, 0f), mins, maxs), "behind the eye");
    }
}
