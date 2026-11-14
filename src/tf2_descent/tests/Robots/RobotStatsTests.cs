using TF2.Descent.Rules;
using TF2.Descent.Rules.Robots;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// Robots' numbers (plan_descent_logic RU7; plan_tf2_rpg 2f, R16, R25, D11): the scale roll,
/// the one curve from scale to stats with its stated exponents, the level curve, party
/// scaling, each champion affix's stat effects, resistances and immunity, and depth
/// dominance on the threat metric (life × damage; a pack's is the sum over its robots).
/// </summary>
public sealed class RobotStatsTests
{
    private static readonly TF2DescentRules Rules = new();
    private static RobotRules Robots => Rules.Robots;

    private static RobotTemplate T(string id) => Robots.Template(id);

    private static RobotStats At(string template, double scale, int level = 1, int party = 1, params string[] affixes) =>
        Robots.Stats(T(template), scale, level, affixes, party);

    //-------------------------------------------------------------------------
    // Scale
    //-------------------------------------------------------------------------

    [Fact]
    public void A_rolled_scale_always_lies_in_its_templates_range_over_10k_rolls_a_template()
    {
        foreach (var t in Rules.Content.Robots.Rows)
            for (int i = 0; i < 10_000; i++)
            {
                double s = RobotRules.RollScale(t, (ulong)i * 7919, i % 8);
                Assert.InRange(s, t.ScaleMin, t.ScaleMax);
            }
    }

    [Fact]
    public void A_rolled_scale_is_seed_stable_and_varies_by_index()
    {
        var t = T("soldier");

        Assert.Equal(RobotRules.RollScale(t, 42, 3), RobotRules.RollScale(t, 42, 3));
        Assert.Equal(5, Enumerable.Range(0, 5).Select(i => RobotRules.RollScale(t, 42, i)).Distinct().Count());
    }

    [Fact]
    public void A_bosss_scale_is_fixed()
    {
        Assert.All(Enumerable.Range(0, 100), i => Assert.Equal(1.9, RobotRules.RollScale(T("sir_nukesalot"), (ulong)i, i)));
    }

    //-------------------------------------------------------------------------
    // The one curve
    //-------------------------------------------------------------------------

    private static readonly double[] Scales = [0.6, 0.7, 0.85, 1.0, 1.15, 1.5, 1.7, 1.9];

    [Fact]
    public void Life_goes_as_scale_squared()
    {
        foreach (double s in Scales)
            Assert.Equal(200 * s * s, At("soldier", s).Life, 9);
    }

    [Fact]
    public void Melee_reach_goes_as_scale()
    {
        foreach (double s in Scales)
            Assert.Equal(48 * s, At("soldier", s).Reach, 9);
    }

    [Fact]
    public void Move_speed_goes_as_one_over_the_root_of_scale()
    {
        foreach (double s in Scales)
            Assert.Equal(240 / Math.Sqrt(s), At("soldier", s).Speed, 9);
    }

    [Fact]
    public void Knockback_resistance_goes_as_scale()
    {
        foreach (double s in Scales)
            Assert.Equal(10 * s, At("soldier", s).KnockbackResistPct, 9);
    }

    [Fact]
    public void Xp_and_loot_quality_follow_life_and_the_budget()
    {
        foreach (double s in Scales)
        {
            var r = At("soldier", s);
            double expected = s * s * r.Budget / 40.0;
            Assert.Equal(40 * expected, r.Xp, 9);
            Assert.Equal(expected, r.LootQuality, 9);
        }
    }

    [Fact]
    public void Each_scale_driven_stat_is_monotonic_in_scale()
    {
        for (int i = 1; i < Scales.Length; i++)
        {
            RobotStats a = At("heavy", Scales[i - 1]), b = At("heavy", Scales[i]);
            Assert.True(b.Life > a.Life);
            Assert.True(b.Reach > a.Reach);
            Assert.True(b.KnockbackResistPct > a.KnockbackResistPct);
            Assert.True(b.Speed < a.Speed);
            Assert.True(b.Xp > a.Xp);
        }
    }

    //-------------------------------------------------------------------------
    // Level and party
    //-------------------------------------------------------------------------

    [Fact]
    public void Each_monster_level_adds_12_percent_life_and_6_percent_damage_compounding()
    {
        var l1 = At("pyro", 1, level: 1);
        var l5 = At("pyro", 1, level: 5);

        Assert.Equal(l1.Life * Math.Pow(1.12, 4), l5.Life, 9);
        Assert.Equal(l1.Damage * Math.Pow(1.06, 4), l5.Damage, 9);
    }

    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(2, 1.6)]
    [InlineData(4, 2.8)]
    public void Each_extra_party_member_adds_60_percent_robot_life_and_no_xp(int party, double factor)
    {
        var solo = At("heavy", 1);
        var scaled = At("heavy", 1, party: party);

        Assert.Equal(solo.Life * factor, scaled.Life, 9);
        Assert.Equal(solo.Xp, scaled.Xp, 9);
    }

    [Theory]
    [InlineData(4, 1, 4)]
    [InlineData(4, 2, 5)]
    [InlineData(4, 4, 7)]
    [InlineData(2, 3, 3)]
    public void Each_extra_party_member_adds_25_percent_pack_size(int baseSize, int party, int size) =>
        Assert.Equal(size, Rules.Packs.PackSize(baseSize, party));

    //-------------------------------------------------------------------------
    // Champion affixes: their stat effects, one row each
    //-------------------------------------------------------------------------

    [Theory]
    //          affix                  life  damage speed  xp    kb+
    [InlineData("extra_fast",          1.0,  1.0,   1.33,  1.25, 0)]
    [InlineData("uber_low_health",     1.0,  1.0,   1.0,   1.30, 0)]
    [InlineData("sentry_buster_death", 1.0,  1.0,   1.0,   1.25, 0)]
    [InlineData("crit_aura",           1.0,  1.0,   1.0,   1.40, 0)]
    [InlineData("fire_enchanted",      1.0,  1.15,  1.0,   1.30, 0)]
    [InlineData("medic_shielded",      1.0,  1.0,   1.0,   1.30, 0)]
    [InlineData("teleporting",         1.0,  1.0,   1.0,   1.25, 0)]
    [InlineData("multishot",           1.0,  0.8,   1.0,   1.35, 0)]
    [InlineData("giant",               2.0,  1.25,  0.85,  1.60, 30)]
    [InlineData("fireproof",           1.0,  1.0,   1.0,   1.40, 0)]
    [InlineData("blast_plated",        1.0,  1.0,   1.0,   1.40, 0)]
    [InlineData("bulletproof",         1.0,  1.0,   1.0,   1.40, 0)]
    [InlineData("shock_hardened",      1.0,  1.0,   1.0,   1.40, 0)]
    public void A_champion_affix_has_its_stat_effects(string affix, double life, double damage, double speed, double xp, double kb)
    {
        var plain = At("soldier", 1);
        var champ = At("soldier", 1, affixes: [affix]);

        Assert.Equal(plain.Life * life, champ.Life, 9);
        Assert.Equal(plain.Damage * damage, champ.Damage, 9);
        Assert.Equal(plain.Speed * speed, champ.Speed, 9);
        Assert.Equal(plain.Xp * xp, champ.Xp, 9);
        Assert.Equal(plain.KnockbackResistPct + kb, champ.KnockbackResistPct, 9);
        Assert.Equal(plain.Budget, champ.Budget);   // affixes never touch the temperament budget
    }

    [Fact]
    public void Every_shipped_affix_has_a_row_in_the_stat_fact()
    {
        string[] covered = ["extra_fast", "uber_low_health", "sentry_buster_death", "crit_aura", "fire_enchanted", "medic_shielded",
            "teleporting", "multishot", "giant", "fireproof", "blast_plated", "bulletproof", "shock_hardened"];

        Assert.Equal(covered.Order(), Rules.Content.Champions.Rows.Select(a => a.Id).Order());
    }

    [Theory]
    [InlineData("fireproof", "fire")]
    [InlineData("blast_plated", "blast")]
    [InlineData("bulletproof", "bullet")]
    [InlineData("shock_hardened", "shock")]
    public void An_immunity_affix_makes_its_damage_type_do_nothing(string affix, string type)
    {
        var r = At("soldier", 1, affixes: [affix]);

        Assert.Equal(type, r.Immunity);
        Assert.Equal(100, r.Resist[type]);
        Assert.Equal(0, RobotRules.DamageTaken(r, type, 250));
    }

    [Fact]
    public void Fire_enchanted_adds_50_to_fire_resistance()
    {
        Assert.Equal(At("soldier", 1).Resist["fire"] + 50, At("soldier", 1, affixes: ["fire_enchanted"]).Resist["fire"]);
    }

    //-------------------------------------------------------------------------
    // Resistance
    //-------------------------------------------------------------------------

    [Fact]
    public void Resistance_is_applied_once()
    {
        var soldier = At("soldier", 1);   // blast +25

        Assert.Equal(75, RobotRules.DamageTaken(soldier, "blast", 100), 9);
        Assert.Equal(150, RobotRules.DamageTaken(At("pyro", 1), "fire", 100), 9);   // fire −50: a weakness
    }

    [Fact]
    public void Resistance_off_champions_is_clamped_to_75()
    {
        var content = RobotContentTests.Planted(Rules.Content.Robots.Name, n =>
            n.AsArray().Single(o => (string?)o!["id"] == "pyro")!["resist"]!["fire"] = 75);
        var r = new RobotRules(content).Stats(content.Robots["pyro"], 1, 1, ["fire_enchanted"]);

        Assert.Equal(75, r.Resist["fire"]);   // 75 + 50, clamped
    }

    [Fact]
    public void Resistance_off_champions_is_clamped_to_minus_50()
    {
        var content = RobotContentTests.Planted(Rules.Content.Champions.Name, n =>
            n.AsArray().Single(o => (string?)o!["id"] == "fire_enchanted")!["resist"]!["fire"] = -50);
        var r = new RobotRules(content).Stats(content.Robots["pyro"], 1, 1, ["fire_enchanted"]);

        Assert.Equal(-50, r.Resist["fire"]);   // −50 − 50, clamped
    }

    [Fact]
    public void No_template_alone_reaches_100_percent()
    {
        Assert.All(Rules.Content.Robots.Rows, t =>
            Assert.All(Robots.Stats(t, t.ScaleMin, 1, []).Resist.Values, v => Assert.InRange(v, -50, 75)));
    }

    [Fact]
    public void A_robot_immune_to_two_types_cannot_be_made()
    {
        Assert.Throws<ArgumentException>(() => At("soldier", 1, affixes: ["fireproof", "bulletproof"]));
    }

    //-------------------------------------------------------------------------
    // Dominance
    //-------------------------------------------------------------------------

    [Fact]
    public void Every_template_is_more_threatening_one_depth_deeper()
    {
        foreach (var t in Rules.Content.Robots.Rows)
            for (int d = 2; d <= 15; d++)
            {
                var shallow = Robots.Stats(t, t.ScaleMin, Robots.MonsterLevel(d - 1), []);
                var deep = Robots.Stats(t, t.ScaleMin, Robots.MonsterLevel(d), []);
                Assert.True(deep.Threat > shallow.Threat, $"{t.Id} at depth {d}");
            }
    }

    [Fact]
    public void Depth_n_plus_1_packs_dominate_depth_n_packs_over_2000_seeds()
    {
        // The threat metric: a pack's summed life × damage, solo, averaged over 2000 rooms a depth.
        double Mean(int depth) => Enumerable.Range(0, 2000).Average(s => Rules.Packs.RollPack((ulong)s, s % 7, depth, 1, 4).Threat);

        var means = Enumerable.Range(1, 15).Select(Mean).ToList();
        for (int d = 1; d < 15; d++)
            Assert.True(means[d] > means[d - 1], $"depth {d + 1}: {means[d]:F0} is not above depth {d}: {means[d - 1]:F0}");
    }
}
