using SourceSharp.Abi;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.World;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Lamp;
using TF2.Descent.Server.Mode;
using TF2.Descent.Tests.Hero;
using Xunit;

namespace TF2.Descent.Tests.World;

/// <summary>
/// A hero's lamp range and brightness on his own networked state (ruling L11: what a hero has
/// equipped is seen by all): the server writes his stats' radius in whole units and his brightness
/// in percent of lamp.json's light at every think, so every client draws his lamp with them.
/// </summary>
public sealed class LampWireTests
{
    private sealed class World : ILampWorld
    {
        public bool IsAlive(EntityHandle player) => true;
        public double Pitch(EntityHandle player) => 0;
        public int Depth => 12;
    }

    private sealed class ModeOn : IDescentModeState
    {
        public bool Active => true;
    }

    /// <summary>What a hat and a skill would give: the lamp's two stats, from a source of their own.</summary>
    private sealed class LampGear : IHeroContributionSource
    {
        public double Range { get; set; }
        public double Brightness { get; set; }
        public string RangeStat { get; init; } = "";
        public string BrightnessStat { get; init; } = "";

        public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet) =>
        [
            new(RangeStat, Range, new ContributionSource(SourceKind.Item, "hat")),
            new(BrightnessStat, Brightness, new ContributionSource(SourceKind.Skill, "lamplighter")),
        ];
    }

    private readonly HeroRig _rig = new();
    private readonly HeroLamps _lamps;
    private readonly LampGear _gear;

    public LampWireTests()
    {
        _lamps = new HeroLamps(new ModeOn(), _rig.Heroes, _rig.Rules, new World(), _rig.Log);
        _gear = new LampGear { RangeStat = Lamp.Range.Stat, BrightnessStat = Lamp.Brightness.Stat };
        _rig.Contributions.Add(_gear);
    }

    private LampSettings Lamp => _rig.Rules.Content.Lamp;

    private HeroComponent Hero()
    {
        var hero = _rig.Join();
        Assert.NotNull(hero?.State);
        return hero!;
    }

    [Fact]
    public void A_bare_heroes_wire_carries_lamp_jsons_lamp()
    {
        var hero = Hero();
        Assert.Equal(0, hero.State!.LampRadius);   // the stimulus: nothing wrote it before the think

        _lamps.OnThink(0.015f);

        Assert.Equal((int)Math.Round(Lamp.Light.Radius), hero.State.LampRadius);
        Assert.Equal(100, hero.State.LampBrightness);
    }

    [Fact]
    public void A_heroes_wire_carries_his_own_lamp_range_and_brightness()
    {
        _gear.Range = 20;
        _gear.Brightness = 30;
        var hero = Hero();

        _lamps.OnThink(0.015f);

        Assert.Equal((int)Math.Round(Lamp.Light.Radius * 1.2), hero.State!.LampRadius);
        Assert.Equal(130, hero.State.LampBrightness);
    }

    [Fact]
    public void A_change_in_his_stats_reaches_the_wire_at_the_next_think()
    {
        var hero = Hero();
        _lamps.OnThink(0.015f);
        int before = hero.State!.LampRadius;

        _gear.Range = 400;   // past the ceiling
        hero.Recompute();
        _lamps.OnThink(0.015f);

        Assert.NotEqual(before, hero.State.LampRadius);
        Assert.Equal((int)Math.Round(Lamp.Light.Radius * (1 + Lamp.Range.Max / 100)), hero.State.LampRadius);
    }

    [Fact]
    public void A_dark_lamp_still_carries_its_range_and_brightness()
    {
        _gear.Brightness = 50;
        var hero = Hero();
        _lamps.Set(HeroRig.Index, false, "key");

        _lamps.OnThink(0.015f);

        Assert.False(hero.State!.LampOn);
        Assert.Equal(150, hero.State.LampBrightness);   // lit again, every client already knows it
    }

    [Fact]
    public void The_wire_rounds_to_whole_units_and_percent_and_never_writes_its_not_written_zero()
    {
        Assert.Equal(865, LampRules.WireRadius(864.5));
        Assert.Equal(1, LampRules.WireRadius(0));
        Assert.Equal(LampRules.WireRadiusMost, LampRules.WireRadius(1e9));
        Assert.Equal(133, LampRules.WireBrightness(1.333));
        Assert.Equal(1, LampRules.WireBrightness(0));
        Assert.Equal(LampRules.WireBrightnessMost, LampRules.WireBrightness(99));
    }

    [Fact]
    public void Every_lamp_the_content_allows_fits_the_wire()
    {
        var lamp = Lamp;

        Assert.True(lamp.Light.Radius * (1 + lamp.Range.Max / 100) <= LampRules.WireRadiusMost);
        Assert.True(100 + LampTable.BrightnessCeilingMost <= LampRules.WireBrightnessMost);   // the most validation allows
    }
}
