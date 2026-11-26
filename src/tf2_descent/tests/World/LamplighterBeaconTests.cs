using SourceSharp.Abi;

using TF2.Descent.Server.Lamp;
using TF2.Descent.Server.Mode;
using TF2.Descent.Tests.Skills;
using Xunit;

namespace TF2.Descent.Tests.World;

/// <summary>
/// The server's half of "robots notice it when on" (the user's ruling L26): a hero's lit lamp is a
/// beacon (<see cref="IHeroVisibility.Beacon"/>) while his Lamplighter is on, and his lamp's reach
/// is its radius, Lamplighter's bonus in it while on. With his lamp out it is no beacon whatever
/// the toggle says, and off he is seen as before.
/// </summary>
public sealed class LamplighterBeaconTests
{
    private sealed class World : ILampWorld
    {
        public bool IsAlive(EntityHandle player) => true;
        public double Pitch(EntityHandle player) => 0;
        public int Depth => 5;
    }

    private sealed class ModeOn : IDescentModeState
    {
        public bool Active => true;
    }

    private readonly SkillRig _rig = new();
    private readonly HeroLamps _lamps;
    private readonly HeroVisibility _visibility;

    public LamplighterBeaconTests()
    {
        var world = new World();
        _lamps = new HeroLamps(new ModeOn(), _rig.Heroes.Heroes, _rig.Heroes.Rules, world, _rig.Heroes.Log);
        _visibility = new HeroVisibility(_lamps, _rig.Heroes.Heroes, world, _rig.Heroes.Rules, DeclaredConVars.Holding<DescentDarkConVar>("-1"), _rig.Casters);
        _rig.Join(level: 15);
        _rig.Learn("lamplighter", 5);
        _rig.Slot(1, "lamplighter");
        _rig.Run(1);
    }

    private EntityHandle Player => SkillRig.PlayerOf();

    private double Radius => _rig.Heroes.Rules.Content.Lamp.Light.Radius;

    private void Toggle()
    {
        _rig.Runtime.Press(SkillRig.Index, 1);
        _rig.Run(1);
    }

    [Fact]
    public void Off_his_lit_lamp_is_no_beacon_and_reaches_its_bare_radius()
    {
        _lamps.Set(SkillRig.Index, true, "fact");

        Assert.False(_visibility.Beacon(Player));
        Assert.Equal(Radius, _visibility.LampReach(Player), 6);
    }

    [Fact]
    public void On_his_lit_lamp_is_a_beacon_and_reaches_its_ranks_radius()
    {
        _lamps.Set(SkillRig.Index, true, "fact");
        Toggle();

        Assert.True(_visibility.Beacon(Player));
        Assert.Equal(Radius * 1.75, _visibility.LampReach(Player), 6);
    }

    [Fact]
    public void On_with_his_lamp_out_he_is_no_beacon_and_his_lamp_reaches_nowhere()
    {
        Toggle();
        _lamps.Set(SkillRig.Index, false, "fact");

        Assert.Contains("lamplighter", _rig.Casters.Of(SkillRig.Index)!.State.Toggles);   // the toggle is on
        Assert.False(_visibility.Beacon(Player));
        Assert.Equal(0, _visibility.LampReach(Player));
    }

    [Fact]
    public void Switched_off_again_he_is_no_beacon()
    {
        _lamps.Set(SkillRig.Index, true, "fact");
        Toggle();
        Toggle();

        Assert.False(_visibility.Beacon(Player));
    }

    [Fact]
    public void The_dev_print_says_whether_his_lamplighter_is_on()
    {
        _lamps.Set(SkillRig.Index, true, "fact");
        string off = _visibility.Describe(Player);
        Toggle();

        Assert.Contains("lamplighter off", off);
        Assert.Contains("lamplighter on: a beacon", _visibility.Describe(Player));
    }
}
