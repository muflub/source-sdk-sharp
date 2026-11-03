using Xunit;

namespace SourceSharp.Ai.Tests.Memory;

/// <summary>
/// The ammo estimate (plan_hl2mp_ai §5.1, §5.3.4) with the plan's numbers:
/// 17 heard of an SMG1's 45 leaves at most 28; a 4 s gap widens it back toward
/// unknown; a sighted reload fills it at its end; 45 heard shots and no reload
/// make P(empty) at least 0.9.
/// </summary>
public sealed class AmmoEstimateTests
{
    public static readonly WeaponFacts Smg1 = new(3, "weapon_smg1", 45, 0.075f, 1.5f, true, false, 0f);
    public static readonly WeaponFacts Shotgun = new(5, "weapon_shotgun", 6, 0.7f, 0.5f, true, true, 0f);
    private const float Dt = 0.015f;

    private static AmmoEstimate Seen()
    {
        var a = new AmmoEstimate();
        a.SeeWeapon(Smg1, new Tick(1));
        return a;
    }

    [Fact]
    public void FirstSightIsTheWholeClipFlat()
    {
        AmmoEstimate a = Seen();
        Assert.Equal((0, 45), (a.Min, a.Max));
        Assert.Equal(1f / 46f, a.P(20), 4);
    }

    [Fact]
    public void SeventeenHeardShotsLeaveAtMostTwentyEight()
    {
        AmmoEstimate a = Seen();
        for (int i = 0; i < 17; i++)
            a.Shot(Smg1, 1, new Tick(2 + i));
        Assert.Equal(28, a.Max);
        Assert.Equal(0, a.Min);
        Assert.Equal(17, a.ShotsSinceReload);
    }

    [Fact]
    public void FortyFiveHeardShotsAndNoReloadAreProbablyEmpty()
    {
        AmmoEstimate a = Seen();
        for (int i = 0; i < 45; i++)
            a.Shot(Smg1, 1, new Tick(2 + i));
        Assert.True(a.PEmpty >= 0.9f, $"P(empty) {a.PEmpty}");
    }

    [Fact]
    public void AFourSecondGapWidensTheRangeBackTowardUnknown()
    {
        AmmoEstimate a = Seen();
        a.ReloadStarted(Smg1, new Tick(2), Dt);
        a.Advance(Smg1, new Tick(2 + VisionParams.Ticks(1.5f, Dt)), Dt);
        a.Shot(Smg1, 10, new Tick(201));
        Assert.Equal((33, 35), (a.Min, a.Max));   // room for one missed shot and a tenth of those counted
        for (int t = 202; t <= 201 + 267; t += 7)
            a.Advance(Smg1, new Tick(t), Dt);
        Assert.Equal((0, 45), (a.Min, a.Max));
        Assert.True(a.P(35) < 0.5f, $"the mass spread: P(35) {a.P(35)}");
    }

    [Fact]
    public void ASightedReloadFillsTheClipAtItsEndNotBefore()
    {
        AmmoEstimate a = Seen();
        for (int i = 0; i < 30; i++)
            a.Shot(Smg1, 1, new Tick(2 + i));
        a.ReloadStarted(Smg1, new Tick(40), Dt);
        Assert.True(a.ReloadInProgress);
        Assert.Equal(new Tick(40 + VisionParams.Ticks(1.5f, Dt)), a.ReloadEndTick);
        a.Advance(Smg1, a.ReloadEndTick + -1, Dt);
        Assert.NotEqual((45, 45), (a.Min, a.Max));
        a.Advance(Smg1, a.ReloadEndTick, Dt);
        Assert.Equal((45, 45), (a.Min, a.Max));
        Assert.False(a.ReloadInProgress);
        Assert.Equal(30f / 45f, a.LastReloadSpent, 3);
    }

    [Fact]
    public void AShotgunReloadIsOpenEndedAndCountsShells()
    {
        var a = new AmmoEstimate();
        a.SeeWeapon(Shotgun, new Tick(1));
        a.Shot(Shotgun, 6, new Tick(2));
        a.Shell(Shotgun, 1, new Tick(10), Dt);
        a.Shell(Shotgun, 1, new Tick(40), Dt);
        Assert.Equal((2, 2), (a.Min, a.Max));
        Assert.Equal(int.MaxValue, a.ReloadEndTick.Value);
        a.Advance(Shotgun, new Tick(5000), Dt);
        Assert.True(a.ReloadInProgress, "open until its end is perceived");
    }

    [Fact]
    public void ADryClickIsEmpty()
    {
        AmmoEstimate a = Seen();
        a.DryFire(Smg1, new Tick(3));
        Assert.Equal(1f, a.PEmpty);
    }
}
