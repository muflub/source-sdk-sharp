using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// Armour, resistances and sustain (plan_tf2_rpg 2a, Appendix C.2 and C.4, A.5): armour
/// diminishes, never removes a whole hit and is worth less against a deeper attacker;
/// resistances stop at 75 %; each sustain total stops exactly at its cap however many
/// sources stack, and life from damage counts only damage dealt.
/// </summary>
public sealed class HeroDefenceTests
{
    private static readonly TF2DescentRules Rules = new();
    private static HeroRules Heroes => Rules.Heroes;

    private static HeroStats With(params (string Stat, double Amount)[] given) =>
        Heroes.Stats(Heroes.New("soldier", "t"),
            [.. given.Select((g, i) => new Contribution(g.Stat, g.Amount, new ContributionSource(SourceKind.Item, "item" + i)))]);

    //-------------------------------------------------------------------------
    // Armour
    //-------------------------------------------------------------------------

    [Fact]
    public void Armour_reduction_is_the_appendix_formula()
    {
        // armour / (armour + 50 + 10 * attackerLevel)
        Assert.Equal(0.5, Heroes.ArmourReduction(100, 5), 12);
        Assert.Equal(292.0 / (292 + 50 + 180), Heroes.ArmourReduction(292, 18), 12);
    }

    [Fact]
    public void More_armour_always_removes_more_of_a_hit()
    {
        double last = Heroes.ArmourReduction(0, 10);
        for (int armour = 1; armour <= 5000; armour++)
        {
            double r = Heroes.ArmourReduction(armour, 10);
            Assert.True(r > last, $"armour {armour}: {r} <= {last}");
            last = r;
        }
    }

    [Fact]
    public void Armour_never_removes_a_whole_hit()
    {
        Assert.True(Heroes.ArmourReduction(1e9, 0) < 1);
        Assert.True(Heroes.ArmourReduction(1e9, 30) < 1);
    }

    [Fact]
    public void Armour_is_worth_less_against_a_deeper_attacker()
    {
        for (int level = 1; level < 30; level++)
            Assert.True(Heroes.ArmourReduction(200, level + 1) < Heroes.ArmourReduction(200, level), $"attacker level {level}");
    }

    [Fact]
    public void No_armour_or_negative_armour_removes_nothing()
    {
        Assert.Equal(0, Heroes.ArmourReduction(0, 10));
        Assert.Equal(0, Heroes.ArmourReduction(-40, 10));
    }

    [Fact]
    public void A_heros_armour_is_what_aim_buys_plus_gear()
    {
        // Soldier Aim 20 at 0.2 a point, and a 30-armour helm.
        Assert.Equal(20 * 0.2 + 30, With(("armour", 30)).Armour, 9);
    }

    //-------------------------------------------------------------------------
    // Resistances
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData("fire")]
    [InlineData("blast")]
    [InlineData("bullet")]
    [InlineData("shock")]
    public void A_resistance_stops_at_75_percent(string id)
    {
        var below = With(("resist_" + id + "_pct", 40));
        var over = With([.. Enumerable.Repeat(("resist_" + id + "_pct", 60.0), 5), ("resist_all_pct", 50)]);

        Assert.Equal((40 + 15 * 0.2) / 100, below.Resist(id), 9);   // Grit 15 gives 0.2 % each
        Assert.Equal(0.75, over.Resist(id), 12);
    }

    [Fact]
    public void A_cursed_resistance_can_go_below_zero_to_the_floor()
    {
        Assert.Equal((-50 + 3) / 100.0, With(("resist_fire_pct", -50)).Resist("fire"), 9);
        Assert.Equal(-1.0, With(("resist_fire_pct", -200), ("resist_fire_pct", -200)).Resist("fire"), 12);
    }

    //-------------------------------------------------------------------------
    // Sustain
    //-------------------------------------------------------------------------

    /// <summary>The total a sustain stat reaches with <paramref name="sources"/> sources of <paramref name="each"/>.</summary>
    private static double Sustain(string which, int sources, double each)
    {
        string stat = which switch
        {
            "lifeRegen" => "life_regen_pct",
            "lifeLeech" => "life_leech_pct",
            "manaRegen" => "mana_regen_rate_pct",
            _ => "mana_leech_pct",
        };
        var s = With([.. Enumerable.Repeat((stat, each), sources)]);
        return which switch
        {
            "lifeRegen" => s.LifeRegenRate,
            "lifeLeech" => s.LifeLeech * 100,
            "manaRegen" => s.ManaRegenRate,
            _ => s.ManaLeech * 100,
        };
    }

    [Theory]
    [InlineData("lifeRegen", 3.0)]   // 3 % of max life a second (A.5)
    [InlineData("lifeLeech", 15.0)]  // 15 % of damage dealt (A.5)
    [InlineData("manaRegen", 5.0)]
    [InlineData("manaLeech", 5.0)]
    public void Each_sustain_total_stops_exactly_at_its_cap_however_many_sources_stack(string which, double cap)
    {
        for (int n = 1; n <= 60; n++)
            Assert.True(Sustain(which, n, 0.5) <= cap, $"{n} sources");
        Assert.Equal(cap, Sustain(which, 60, 0.5), 12);
        Assert.Equal(cap, Sustain(which, 1000, 1.0), 12);
    }

    [Theory]
    [InlineData("lifeRegen")]
    [InlineData("lifeLeech")]
    [InlineData("manaRegen")]
    [InlineData("manaLeech")]
    public void Every_sustain_source_adds_below_the_cap(string which)
    {
        Assert.True(Sustain(which, 2, 0.5) > Sustain(which, 1, 0.5));
        Assert.True(Sustain(which, 3, 0.5) > Sustain(which, 2, 0.5));
    }

    [Fact]
    public void Life_regeneration_is_its_rate_of_max_life_a_second()
    {
        var s = With(("life_regen_pct", 1.5));

        Assert.Equal(0.015 * s.MaxLife, s.LifeRegenPerSecond, 9);
    }

    [Fact]
    public void Life_from_damage_counts_only_damage_dealt_so_overkill_and_an_immune_target_heal_nothing_more()
    {
        var s = With(("life_leech_pct", 10));

        Assert.Equal(5, s.LifeFromHit(50, 1000), 9);     // 10 % of 50 dealt
        Assert.Equal(2, s.LifeFromHit(500, 20), 9);      // overkill: the target had 20
        Assert.Equal(0, s.LifeFromHit(0, 1000), 9);      // immune: nothing dealt
        Assert.Equal(0, s.LifeFromHit(50, 0), 9);        // already dead
    }
}
