using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.World;
using TF2.Descent.Server.Lamp;
using TF2.Descent.Server.Mode;
using TF2.Descent.Tests.Hero;
using Xunit;

namespace TF2.Descent.Tests.World;

/// <summary>
/// The miner's lamp on the server (HE10): the switch a hero, its state on his networked entity
/// for every client, the toggle's validation, the depth's default and the reset at a level's end.
/// </summary>
public sealed class HeroLampsTests
{
    private sealed class World : ILampWorld
    {
        public HashSet<EntityHandle> Dead { get; } = [];
        public double Look { get; set; }
        public int Depth { get; set; } = 1;
        public bool IsAlive(EntityHandle player) => !Dead.Contains(player);
        public double Pitch(EntityHandle player) => Look;
    }

    private sealed class ModeOn : IDescentModeState
    {
        public bool Active => true;
    }

    private readonly HeroRig _rig = new();
    private readonly World _world = new();
    private readonly HeroLamps _lamps;

    public HeroLampsTests() => _lamps = new HeroLamps(new ModeOn(), _rig.Heroes, _rig.Rules, _world, _rig.Log);

    private LampSettings Lamp => _rig.Rules.Content.Lamp;

    private TF2.Descent.Server.Hero.HeroComponent Hero()
    {
        var hero = _rig.Join();
        Assert.NotNull(hero?.State);
        return hero!;
    }

    [Fact]
    public void A_hero_on_a_level_has_his_lamp_lit_on_the_wire()
    {
        var hero = Hero();
        Assert.False(hero.State!.LampOn);   // the stimulus: nothing lit it before the think

        _lamps.OnThink(0.015f);

        Assert.True(hero.State.LampOn);
    }

    [Fact]
    public void A_hero_in_the_town_has_his_lamp_out()
    {
        _world.Depth = 0;
        var hero = Hero();

        _lamps.OnThink(0.015f);

        Assert.False(hero.State!.LampOn);
        Assert.False(_lamps.SwitchOf(HeroRig.Index));
    }

    [Fact]
    public void The_key_puts_the_lamp_out_for_every_client_and_lights_it_again()
    {
        var hero = Hero();
        _lamps.OnThink(0.015f);
        Assert.True(hero.State!.LampOn);

        Assert.Equal(LampAnswer.Off, _lamps.Set(HeroRig.Index, null, "key"));
        Assert.False(hero.State.LampOn);                                  // at once, not at the next think
        _lamps.OnThink(0.015f);
        Assert.False(hero.State.LampOn);                                  // and it stays out

        Assert.Equal(LampAnswer.On, _lamps.Set(HeroRig.Index, null, "key"));
        Assert.True(hero.State.LampOn);
    }

    [Fact]
    public void A_dead_heroes_switch_is_refused_and_his_lamp_is_dark_on_the_wire()
    {
        var hero = Hero();
        _lamps.OnThink(0.015f);
        _world.Dead.Add(hero.Player);

        Assert.Equal(LampAnswer.Dead, _lamps.Set(HeroRig.Index, false, "key"));
        Assert.True(_lamps.SwitchOf(HeroRig.Index));                      // the switch is untouched
        _lamps.OnThink(0.015f);
        Assert.False(hero.State!.LampOn);                                  // but a dead hero's lamp is dark
        Assert.Equal(1, _lamps.Refusals);
    }

    [Fact]
    public void A_downed_heroes_switch_is_refused()
    {
        var hero = Hero();
        _lamps.OnThink(0.015f);
        hero.ShowDowned(true, 10, 20);

        Assert.Equal(LampAnswer.Downed, _lamps.Set(HeroRig.Index, null, "key"));
        Assert.True(hero.State!.LampOn);
    }

    [Fact]
    public void A_player_who_is_no_hero_is_refused()
    {
        Assert.Equal(LampAnswer.NoHero, _lamps.Set(9, true, "key"));
    }

    [Fact]
    public void A_levels_end_forgets_the_switch_and_the_next_depth_decides()
    {
        Hero();
        _lamps.Set(HeroRig.Index, false, "key");
        Assert.False(_lamps.SwitchOf(HeroRig.Index));

        _lamps.OnLevelShutdown();
        _lamps.OnLevelInit("tf2_descent-2-s1");

        Assert.True(_lamps.SwitchOf(HeroRig.Index));
    }

    [Fact]
    public void A_lit_lamp_carries_the_heroes_look_in_steps()
    {
        var hero = Hero();
        _world.Look = 30;

        _lamps.OnThink(0.015f);

        Assert.Equal(LampRules.PitchSteps(Lamp, 30), hero.State!.LampPitch);
        Assert.NotEqual(LampRules.PitchSteps(Lamp, 0), hero.State.LampPitch);   // the look moved the wire
    }

    [Fact]
    public void The_lamp_verb_flips_a_heroes_switch()
    {
        var hero = Hero();
        _lamps.OnThink(0.015f);
        var verbs = new LampClientCommands(_lamps, _rig.Heroes, _rig.Engine);

        Assert.True(verbs.Handle(hero.Player, LampClientCommands.Toggle, []));

        Assert.False(hero.State!.LampOn);
    }

    [Fact]
    public void The_lamp_verb_sets_the_switch_it_names()
    {
        var hero = Hero();
        var verbs = new LampClientCommands(_lamps, _rig.Heroes, _rig.Engine);

        Assert.True(verbs.Handle(hero.Player, LampClientCommands.Toggle, ["off"]));
        Assert.True(verbs.Handle(hero.Player, LampClientCommands.Toggle, ["off"]));

        Assert.False(hero.State!.LampOn);
        Assert.Equal(2, _lamps.Flips);
    }

    [Fact]
    public void Another_verb_or_a_player_who_is_no_hero_passes_on()
    {
        var hero = Hero();
        var verbs = new LampClientCommands(_lamps, _rig.Heroes, _rig.Engine);

        Assert.False(verbs.Handle(hero.Player, "tf2_descent_belt", ["1"]));
        Assert.False(verbs.Handle(HeroRig.PlayerOf(9), LampClientCommands.Toggle, []));
    }
}

/// <summary>The sight-in-the-dark seam (HE10): its stand-in answers a robot's own range for every hero, lamp lit or not.</summary>
public sealed class HeroVisibilityTests
{
    [Fact]
    public void Every_hero_is_seen_at_a_robots_own_range_until_a_lane_scales_it()
    {
        IHeroVisibility visibility = new NeutralHeroVisibility();

        Assert.Equal(1.0, visibility.SightScale(HeroRig.PlayerOf(1)));
        Assert.Equal(1.0, visibility.SightScale(HeroRig.PlayerOf(4)));
        Assert.Equal(1.0, visibility.SightScale(EntityHandle.None));
    }
}
