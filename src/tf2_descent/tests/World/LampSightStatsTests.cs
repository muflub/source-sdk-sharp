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
/// What a longer or brighter lamp does to how well a robot sees its hero (the lampstat lane's
/// choice; plan_tf2_rpg B.3 and D.2 row 11): nothing. A lit lamp already gives him away in full
/// (lamp.json's lampOn 1), so on the black depth a hero with his lamp at both ceilings is seen
/// exactly as a bare one, and with his lamp out as lamp.json's lampOff, whatever its stats.
/// </summary>
public sealed class LampSightStatsTests
{
    private sealed class World : ILampWorld
    {
        public int Depth => 15;   // the black depth: darkness 1
        public bool IsAlive(EntityHandle player) => true;
        public double Pitch(EntityHandle player) => 0;
    }

    private sealed class ModeOn : IDescentModeState
    {
        public bool Active => true;
    }

    /// <summary>Far past both ceilings for the player in <see cref="Invested"/>; nothing for anyone else.</summary>
    private sealed class CeilingGear(LampSettings lamp) : IHeroContributionSource
    {
        public const int Invested = 4;

        public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet) =>
            playerIndex != Invested ? [] :
            [
                new(lamp.Range.Stat, 1000, new ContributionSource(SourceKind.Item, "hat")),
                new(lamp.Brightness.Stat, 1000, new ContributionSource(SourceKind.Skill, "lamplighter")),
            ];
    }

    private readonly HeroRig _rig = new();
    private readonly HeroLamps _lamps;
    private readonly HeroVisibility _visibility;

    public LampSightStatsTests()
    {
        var world = new World();
        _rig.Contributions.Add(new CeilingGear(_rig.Rules.Content.Lamp));
        _lamps = new HeroLamps(new ModeOn(), _rig.Heroes, _rig.Rules, world, _rig.Log);
        _visibility = new HeroVisibility(_lamps, _rig.Heroes, world, _rig.Rules, DeclaredConVars.Holding<DescentDarkConVar>("-1"),
                                         new TF2.Descent.Server.Skills.SkillCasters());
    }

    private LampSettings Lamp => _rig.Rules.Content.Lamp;

    private HeroComponent Hero(int index, bool lamp)
    {
        var hero = _rig.Join(index)!;
        _lamps.Set(index, lamp, "fact");
        return hero;
    }

    [Fact]
    public void A_bare_lit_lamp_on_the_black_depth_is_seen_from_a_robots_whole_range()
    {
        var bare = Hero(HeroRig.Index, lamp: true);

        Assert.Equal(1.0, _visibility.Darkness);
        Assert.Equal(Lamp.Sight.LampOn, _visibility.SightScale(bare.Player));
    }

    [Fact]
    public void A_lit_lamp_at_both_ceilings_is_seen_exactly_as_a_bare_one()
    {
        var bare = Hero(HeroRig.Index, lamp: true);
        var invested = Hero(CeilingGear.Invested, lamp: true);

        Assert.Equal(Lamp.Light.Radius * (1 + Lamp.Range.Max / 100), invested.Stats!.LampRadius, 9);   // the stimulus reached his stats
        Assert.Equal(1 + Lamp.Brightness.Max / 100, invested.Stats.LampBrightness, 9);
        Assert.Equal(_visibility.SightScale(bare.Player), _visibility.SightScale(invested.Player));
    }

    [Fact]
    public void A_lamp_at_both_ceilings_put_out_is_seen_as_any_lamp_out()
    {
        var bare = Hero(HeroRig.Index, lamp: false);
        var invested = Hero(CeilingGear.Invested, lamp: false);

        Assert.Equal(Lamp.Sight.LampOff, _visibility.SightScale(invested.Player));
        Assert.Equal(_visibility.SightScale(bare.Player), _visibility.SightScale(invested.Player));
    }

    [Fact]
    public void The_dev_print_names_the_lamps_reach_and_says_its_brightness_is_not_counted()
    {
        var invested = Hero(CeilingGear.Invested, lamp: true);
        double radius = Lamp.Light.Radius * (1 + Lamp.Range.Max / 100);

        Assert.Contains($"its reach {radius:0} (radius {radius:0}; brightness x{1 + Lamp.Brightness.Max / 100:0.##} not counted), lamplighter off",
                        _visibility.Describe(invested.Player));
    }
}
