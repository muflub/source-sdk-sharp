using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Hero;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>HE3's instruments: the target dummy's arithmetic and the dev contribution source.</summary>
public sealed class HeroDevTests
{
    [Fact]
    public void The_dummy_takes_a_hit_rounded()
    {
        Assert.Equal(117, HeroDummy.Taken(116.5f, HeroDummy.DefaultHealth, mortal: false));
        Assert.Equal(116, HeroDummy.Taken(116.49f, HeroDummy.DefaultHealth, mortal: false));
    }

    [Fact]
    public void The_dummy_never_falls_below_one_unless_mortal()
    {
        Assert.Equal(49, HeroDummy.Taken(90f, 50, mortal: false));
        Assert.Equal(0, HeroDummy.Taken(90f, 1, mortal: false));
        Assert.Equal(90, HeroDummy.Taken(90f, 50, mortal: true));
    }

    [Fact]
    public void A_dev_contribution_feeds_the_heros_stats_and_a_clear_takes_it_away()
    {
        var rig = new HeroStatsRig();
        double before = rig.Hero.Stats!.DamageMultiplier("blast");

        rig.Dev.Add(HeroRig.Index, "damage_blast_pct", 50);
        rig.Hero.Recompute();
        Assert.Equal(before + 0.5, rig.Hero.Stats!.DamageMultiplier("blast"), 9);

        Assert.Equal(1, rig.Dev.Clear(HeroRig.Index));
        rig.Hero.Recompute();
        Assert.Equal(before, rig.Hero.Stats!.DamageMultiplier("blast"), 9);
    }

    [Fact]
    public void A_dev_contribution_keeps_the_state_it_was_given()
    {
        var dev = new DevContributions();

        var added = dev.Add(1, "armour", 10, SourceState.Broken);

        Assert.False(added.Source.Counts);
        Assert.Equal([added], dev.ContributionsOf(1, new HeroSheet { ClassId = "soldier" }));
        Assert.Empty(dev.ContributionsOf(2, new HeroSheet { ClassId = "soldier" }));
    }
}
