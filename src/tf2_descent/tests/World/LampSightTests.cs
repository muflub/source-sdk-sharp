using SourceSharp.Abi;

using TF2.Descent.Rules.World;
using TF2.Descent.Server.Lamp;
using TF2.Descent.Server.Mode;
using TF2.Descent.Tests.Hero;
using Xunit;

namespace TF2.Descent.Tests.World;

/// <summary>
/// The lamp and the dark in robot sight (this lane; HE10's seam <see cref="IHeroVisibility"/>):
/// a hero's multiplier on every robot's range is lamp.json's scale of the depth's darkness and
/// his lamp, lit or out; <c>tf2_descent_dark</c> stands in for the depth's darkness when set.
/// </summary>
public sealed class LampSightTests
{
    private sealed class World : ILampWorld
    {
        public int Depth { get; set; } = 1;
        public bool IsAlive(EntityHandle player) => true;
        public double Pitch(EntityHandle player) => 0;
    }

    private sealed class ModeOn : IDescentModeState
    {
        public bool Active => true;
    }

    private readonly HeroRig _rig = new();
    private readonly World _world = new();
    private readonly HeroLamps _lamps;

    public LampSightTests() => _lamps = new HeroLamps(new ModeOn(), _rig.Heroes, _rig.Rules, _world, _rig.Log);

    private LampSight Sight => _rig.Rules.Content.Lamp.Sight;

    private HeroVisibility Visibility(string dark = "-1") =>
        new(_lamps, _rig.Heroes, _world, _rig.Rules, DeclaredConVars.Holding<DescentDarkConVar>(dark));

    private (EntityHandle Player, int Index) Hero(bool lamp)
    {
        var hero = _rig.Join()!;
        _lamps.Set(hero.PlayerIndex, lamp, "fact");
        return (hero.Player, hero.PlayerIndex);
    }

    [Fact]
    public void On_a_lit_depth_the_lamp_changes_nothing()
    {
        _world.Depth = 1;
        var on = Hero(lamp: true);
        var off = Hero(lamp: false);

        Assert.Equal(1.0, Visibility().SightScale(on.Player));
        Assert.Equal(1.0, Visibility().SightScale(off.Player));
    }

    [Fact]
    public void On_the_black_depth_his_lamp_out_shortens_a_robots_range_by_the_contents_ratio()
    {
        _world.Depth = 15;
        var off = Hero(lamp: false);

        Assert.Equal(Sight.LampOff, Visibility().SightScale(off.Player));
        Assert.True(Sight.LampOff < 1.0);
    }

    [Fact]
    public void On_the_black_depth_his_lamp_lit_is_a_robots_own_range_or_more()
    {
        _world.Depth = 15;
        var on = Hero(lamp: true);

        Assert.Equal(Sight.LampOn, Visibility().SightScale(on.Player));
    }

    [Fact]
    public void Between_lit_and_black_the_scale_is_the_depths_darkness_of_the_way()
    {
        _world.Depth = 13;
        var off = Hero(lamp: false);

        Assert.Equal(LampRules.SightScale(_rig.Rules.Content.Lamp, LampRules.Darkness(_rig.Rules.Content.Lamp, 13), lit: false),
                     Visibility().SightScale(off.Player));
        Assert.InRange(Visibility().SightScale(off.Player), Sight.LampOff + 0.01, 0.99);
    }

    [Fact]
    public void The_dev_darkness_stands_in_for_the_depths()
    {
        _world.Depth = 1;
        var off = Hero(lamp: false);

        var visibility = Visibility("1");

        Assert.True(visibility.Overridden);
        Assert.Equal(Sight.LampOff, visibility.SightScale(off.Player));
        Assert.Contains("tf2_descent_dark", visibility.Describe(off.Player));
    }

    [Fact]
    public void The_dev_darkness_unset_reads_the_depths()
    {
        _world.Depth = 15;
        var visibility = Visibility("-1");

        Assert.False(visibility.Overridden);
        Assert.Equal(1.0, visibility.Darkness);
        Assert.Contains("depth 15", visibility.Describe(Hero(lamp: true).Player));
    }

    [Fact]
    public void A_player_with_no_hero_carries_no_lamp()
    {
        _world.Depth = 15;

        Assert.Equal(Sight.LampOff, Visibility().SightScale(HeroRig.PlayerOf(30)));
    }

    [Fact]
    public void The_description_names_the_scale_the_darkness_and_the_lamp()
    {
        _world.Depth = 15;
        var off = Hero(lamp: false);

        string why = Visibility().Describe(off.Player);

        Assert.StartsWith("scale 0.25: darkness 1.00 (depth 15, lamp.json), lamp out", why);
    }
}
