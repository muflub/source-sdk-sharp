using Xunit;

namespace SourceSharp.Ai.Tests.Inspect;

/// <summary>What a second trigger does to the aim meter (ruling 2026-11-03): its damage counts against the victim, its hits are in no hit rate of the aim, and what its blast does to the bot counts too.</summary>
public sealed class AimStatsAltTests
{
    [Fact]
    public void ABlastsDamageIsNoHitOfAnAimedShot()
    {
        var s = new AimStats(8);
        s.AltHit(3, 60);
        AimStats.Row r = s.Of(3);
        Assert.Equal((60L, 0L, 0L), (r.AltDamage, r.Hits, r.Damage));
    }

    [Fact]
    public void TheSecondTriggerCountsByWeaponAndNotAsAShot()
    {
        var s = new AimStats(8);
        s.AltShot(3);
        s.AltShot(3);
        s.AltShot(7);
        Assert.Equal(2, s.AltFired[3]);
        Assert.Equal(1, s.AltFired[7]);
        Assert.Equal(0, s.Of(0).Shots);
    }

    [Fact]
    public void AnOwnBlastCounts()
    {
        var s = new AimStats(8);
        s.SelfBlast(40);
        s.SelfBlast(5);
        Assert.Equal(45, s.SelfBlastDamage);
    }
}
