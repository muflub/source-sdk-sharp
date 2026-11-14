using TF2.Descent.Rules;
using TF2.Descent.Rules.Robots;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// The temperament roll's gate in full (plan_tf2_rpg B.11, R20; plan_descent_logic RU7): the
/// sum is round(40 × scale) at both ends and the middle of every size range; the budget rises
/// strictly with scale and never passes 100; equal scale is an equal budget whatever the
/// depth, template or affixes; a cap's spill loses nothing; levels 0..10 and caps hold; the
/// roll is seed-stable; it is spiky (at least 90 % of 100k rolls a size class have a skill at
/// 7 or more); biased skills average above unbiased ones; and a one-style pack is re-rolled.
/// </summary>
public sealed class TemperamentTests
{
    private static readonly TF2DescentRules Rules = new();
    private static TemperamentRules Temperaments => Rules.Temperaments;

    private static RobotTemplate T(string id) => Rules.Robots.Template(id);

    private static IEnumerable<RobotTemplate> Rolled => Rules.Content.Robots.Rows.Where(t => !t.Boss);

    [Fact]
    public void Every_roll_spends_exactly_its_budget_at_the_ends_and_middle_of_every_size_range()
    {
        int checkedRolls = 0;
        foreach (var t in Rolled)
            foreach (double scale in new[] { t.ScaleMin, (t.ScaleMin + t.ScaleMax) / 2, t.ScaleMax })
                for (ulong seed = 0; seed < 200; seed++)
                {
                    var m = Temperaments.Roll(seed, (int)(seed % 5), t, scale);
                    Assert.Equal((int)Math.Round(40 * scale, MidpointRounding.AwayFromZero), m.Sum);
                    Assert.Equal(m.Budget, m.Sum);
                    checkedRolls++;
                }
        Assert.True(checkedRolls > 10_000);
    }

    [Fact]
    public void The_budget_rises_strictly_with_scale_and_never_passes_100()
    {
        int last = -1;
        for (double s = 0.6; s <= 2.0 + 1e-9; s += 0.025)
        {
            int b = Rules.Robots.Budget(s);
            Assert.True(b > last, $"budget at {s} is {b}, not above {last}");
            Assert.True(b <= 100);
            last = b;
        }
        Assert.All(Rules.Content.RobotRules.Sizes, z => Assert.True(Rules.Robots.Budget(z.ScaleMax) <= 100));
    }

    [Fact]
    public void Equal_scale_is_an_equal_budget_whatever_the_depth_template_or_affixes()
    {
        foreach (double scale in new[] { 0.7, 1.0, 1.7 })
        {
            var budgets = new HashSet<int>();
            foreach (var t in Rolled)
                foreach (int level in new[] { 1, 15, 29 })
                {
                    budgets.Add(Rules.Robots.Stats(t, scale, level, []).Budget);
                    budgets.Add(Rules.Robots.Stats(t, scale, level, ["giant", "crit_aura"]).Budget);
                    budgets.Add(Temperaments.Roll((ulong)level, 0, t, scale).Sum);
                }
            Assert.Single(budgets);
        }
    }

    [Fact]
    public void A_cap_refuses_points_without_losing_any()
    {
        int soloness = Temperaments.Index("soloness");
        int spilled = 0;
        foreach (var id in new[] { "medic", "giant_medic" })
        {
            var t = T(id);
            for (ulong seed = 0; seed < 10_000; seed++)
            {
                double scale = RobotRules.RollScale(t, seed, 0);
                var m = Temperaments.Roll(seed, 0, t, scale);
                Assert.True(m.Levels[soloness] <= 2);
                Assert.Equal(Rules.Robots.Budget(scale), m.Sum);
                if (m.Signatures.Contains("soloness")) spilled++;
            }
        }
        Assert.True(spilled > 0);   // the stimulus: a capped skill was picked as a signature and spilled
    }

    [Fact]
    public void Every_level_lies_in_0_to_10_and_under_its_templates_cap()
    {
        foreach (var t in Rolled)
            for (ulong seed = 0; seed < 2000; seed++)
            {
                var m = Temperaments.Roll(seed, 1, t, RobotRules.RollScale(t, seed, 1));
                for (int i = 0; i < m.Levels.Length; i++)
                    Assert.InRange(m.Levels[i], 0, t.Cap(Temperaments.Skills[i], 10));
            }
    }

    [Fact]
    public void The_roll_is_seed_stable()
    {
        var a = Temperaments.Roll(99, 2, T("sniper"), 1.03);
        var b = Temperaments.Roll(99, 2, T("sniper"), 1.03);
        var c = Temperaments.Roll(99, 3, T("sniper"), 1.03);

        Assert.Equal(a.Levels.ToArray(), b.Levels.ToArray());
        Assert.Equal(a.Style, b.Style);
        Assert.NotEqual(a.Levels.ToArray(), c.Levels.ToArray());
    }

    [Theory]
    [InlineData("runt")]
    [InlineData("ordinary")]
    [InlineData("giant")]
    public void At_least_90_percent_of_100k_rolls_a_size_class_have_a_skill_at_7_or_more(string size)
    {
        var templates = Rolled.Where(t => t.Size == size).ToList();
        int spiky = 0;
        const int Rolls = 100_000;
        for (int i = 0; i < Rolls; i++)
        {
            var t = templates[i % templates.Count];
            var m = Temperaments.Roll((ulong)i, i % 6, t, RobotRules.RollScale(t, (ulong)i, i % 6));
            if (m.Levels.Max() >= 7) spiky++;
        }

        Assert.True(spiky >= Rolls * 0.9, $"{spiky} of {Rolls} {size} rolls have a skill at 7 or more");
    }

    [Fact]
    public void A_templates_biased_skills_average_above_its_unbiased_ones_over_10k_rolls()
    {
        foreach (var t in Rolled.Where(t => t.Weights.Values.Any(w => w > 1)))
        {
            var biased = Temperaments.Skills.Select((s, i) => (s, i)).Where(x => t.Weight(x.s) > 1).Select(x => x.i).ToList();
            var plain = Temperaments.Skills.Select((s, i) => (s, i)).Where(x => t.Weight(x.s) == 1 && !t.Caps.ContainsKey(x.s)).Select(x => x.i).ToList();
            double sumBiased = 0, sumPlain = 0;
            for (ulong seed = 0; seed < 10_000; seed++)
            {
                var m = Temperaments.Roll(seed, 0, t, RobotRules.RollScale(t, seed, 0));
                sumBiased += biased.Average(i => m.Levels[i]);
                sumPlain += plain.Average(i => m.Levels[i]);
            }
            Assert.True(sumBiased > sumPlain, $"{t.Id}: biased mean {sumBiased / 10_000:F2} vs unbiased {sumPlain / 10_000:F2}");
        }
    }

    [Fact]
    public void The_style_is_the_style_words_of_the_top_two_skills()
    {
        var m = Temperaments.Roll(5, 0, T("sniper"), 1.0);
        var top = m.Levels.Select((v, i) => (v, i)).OrderByDescending(x => x.v).ThenByDescending(x => T("sniper").Weight(Temperaments.Skills[x.i])).ThenBy(x => x.i).Take(2)
            .Select(x => Rules.Content.RobotRules.Temperament.Skills[x.i].Style);

        Assert.Equal(string.Join(" ", top), m.Style);
        Assert.Equal(2, m.Style.Split(' ').Length);
    }

    [Fact]
    public void A_boss_has_its_authored_temperament()
    {
        var m = Temperaments.Roll(1, 0, T("captain_punch"), 1.9);

        Assert.Equal(76, m.Sum);
        Assert.Equal(0, m.Levels[Temperaments.Index("cover")]);
        Assert.Equal(10, m.Levels[Temperaments.Index("nerve")]);
        Assert.Equal(m.Levels.ToArray(), Temperaments.Roll(999, 3, T("captain_punch"), 1.9).Levels.ToArray());
    }

    [Fact]
    public void A_pack_whose_members_all_share_a_style_is_re_rolled()
    {
        // Find packs whose first roll was one style throughout: each was re-rolled and is mixed now.
        int rerolled = 0;
        for (ulong seed = 0; seed < 5000 && rerolled < 5; seed++)
        {
            var pack = Rules.Packs.RollPack(seed, 0, 1, 1, 3);
            if (pack.StyleRerolls == 0) continue;
            var first = pack.Robots.Select(r => Temperaments.Roll(pack.Seed, r.Index, T(r.Template), r.Scale, 0).Style).Distinct().Count();
            Assert.Equal(1, first);
            Assert.True(pack.Robots.Select(r => r.Temperament.Style).Distinct().Count() > 1);
            rerolled++;
        }
        Assert.True(rerolled > 0, "no pack in 5000 needed a re-roll: the rule was never exercised");
    }

    [Fact]
    public void No_rolled_pack_of_two_or_more_is_all_one_style_over_10k_seeds()
    {
        for (ulong seed = 0; seed < 10_000; seed++)
        {
            var pack = Rules.Packs.RollPack(seed, (int)(seed % 9), 1 + (int)(seed % 15), 1, 3);
            if (pack.Robots.Length >= 2)
                Assert.True(pack.Robots.Select(r => r.Temperament.Style).Distinct().Count() > 1, $"seed {seed}");
        }
    }
}
