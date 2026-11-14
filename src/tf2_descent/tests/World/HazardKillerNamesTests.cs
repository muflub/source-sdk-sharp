using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.World;
using TF2.Descent.Server.Hazards;
using TF2.Descent.Server.Mode;
using TF2.Descent.Tests.Hero;
using Xunit;

namespace TF2.Descent.Tests.World;

/// <summary>The kill feed names the hazard that killed a hero (HE10): a world death just after a bite, or a bleed-out a bite began.</summary>
public sealed class HazardKillerNamesTests
{
    private sealed class Clock : IHazardWorld
    {
        public double Now { get; set; } = 50;
        public int Depth => 1;
        public IReadOnlyList<(string Kind, Vector Min, Vector Max)> MapVolumes(IReadOnlyList<string> kinds) => [];
        public bool IsAlive(EntityHandle player) => true;
        public Vector Feet(EntityHandle player) => default;
        public void Hurt(EntityHandle victim, float amount, DamageType bits, Vector at) { }
        public IReadOnlyList<EntityHandle> RobotsIn(Vector min, Vector max) => [];
    }

    private sealed class ModeOn : IDescentModeState
    {
        public bool Active => true;
    }

    private readonly HeroRig _rig = new();
    private readonly Clock _clock = new();
    private readonly HazardKillerNames _names;

    public HazardKillerNamesTests() => _names = new HazardKillerNames(new ModeOn(), _clock, _rig.Engine, _rig.Log);

    private static EntityHandle Victim => HeroRig.PlayerOf(HeroRig.Index);

    private bool Ask(EntityHandle attacker, out string token) =>
        _names.TryToken(new DeathNoticeFacts(Victim, attacker, EntityHandle.None, attacker, ""), out token, out _);

    [Fact]
    public void A_world_death_just_after_a_lava_bite_is_the_lavas()
    {
        _names.Bitten(HeroRig.Index, "lava", 50);
        _clock.Now = 50.4;

        Assert.True(Ask(EntityHandle.None, out string token));
        Assert.Equal(HazardRules.Token("lava"), token);
    }

    [Fact]
    public void A_world_death_long_after_the_last_bite_is_not_the_hazards()
    {
        _names.Bitten(HeroRig.Index, "lava", 50);
        _clock.Now = 50 + HazardKillerNames.Window + 0.5;

        Assert.False(Ask(EntityHandle.None, out _));
    }

    [Fact]
    public void The_bleed_out_of_a_hero_a_bite_put_down_is_the_hazards()
    {
        _names.Bitten(HeroRig.Index, "lava", 50);
        _names.HeroDowned(HeroRig.Index, true, default);
        _clock.Now = 70;   // twenty seconds on, he bleeds out

        Assert.True(Ask(EntityHandle.None, out string token));
        Assert.Equal(HazardRules.Token("lava"), token);
    }

    [Fact]
    public void A_hero_put_down_by_something_else_after_a_bite_is_not_the_hazards()
    {
        _names.Bitten(HeroRig.Index, "lava", 50);
        _clock.Now = 52;
        _names.HeroDowned(HeroRig.Index, true, default);
        _clock.Now = 72;

        Assert.False(Ask(EntityHandle.None, out _));
    }

    [Fact]
    public void A_death_with_an_attacker_is_not_the_hazards()
    {
        _names.Bitten(HeroRig.Index, "lava", 50);

        Assert.False(Ask(HeroStatsRig.Robot, out _));
    }

    [Fact]
    public void A_levels_start_forgets_the_bites()
    {
        _names.Bitten(HeroRig.Index, "lava", 50);

        _names.OnLevelInit("tf2_descent-2-s1");

        Assert.False(Ask(EntityHandle.None, out _));
    }
}
