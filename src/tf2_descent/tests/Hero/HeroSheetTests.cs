using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The hero sheet (plan_descent_logic RU2; plan_tf2_rpg 2a, Appendix C): class starts and
/// caps, what a point buys, the XP curve, level-ups (ruling L3: they refill), spending,
/// respec and the audit identity. The expected numbers are the plan's, written here, so a
/// change to the shipped content that departs from Appendix C fails a fact.
/// </summary>
public sealed class HeroSheetTests
{
    private static readonly TF2DescentRules Rules = new();
    private static HeroRules Heroes => Rules.Heroes;
    private static DescentContent Content => Rules.Content;

    private static readonly string[] AttributeIds = ["brawn", "smarts", "aim", "grit"];

    /// <summary>A hero at <paramref name="level"/> with every point it earned unspent.</summary>
    private static HeroSheet AtLevel(string cls, int level) =>
        Heroes.GainXp(Heroes.New(cls, "t"), Heroes.XpAtLevel(level)).Sheet;

    private static ContributionSource Gear(string id, SourceState state = SourceState.Active) => new(SourceKind.Item, id, state);

    //-------------------------------------------------------------------------
    // Classes: Appendix C.2's table, over the shipped JSON
    //-------------------------------------------------------------------------

    [Theory]
    //          class       brawn     smarts    aim        grit      lifePerGrit manaPerSmarts
    [InlineData("scout", 15, 60, 10, 60, 25, 110, 10, 80, 2.0, 1.5)]
    [InlineData("soldier", 20, 90, 5, 50, 20, 90, 15, 80, 3.0, 1.5)]
    [InlineData("pyro", 20, 85, 10, 75, 10, 60, 20, 90, 3.0, 2.0)]
    [InlineData("demoman", 20, 90, 10, 60, 15, 80, 15, 80, 3.0, 1.5)]
    [InlineData("heavy", 25, 110, 5, 40, 10, 60, 20, 100, 4.0, 1.0)]
    [InlineData("engineer", 10, 60, 25, 100, 15, 80, 10, 70, 2.0, 2.5)]
    [InlineData("medic", 10, 50, 25, 110, 10, 70, 15, 80, 2.5, 3.0)]
    [InlineData("sniper", 10, 55, 10, 70, 30, 120, 10, 65, 2.0, 2.0)]
    [InlineData("spy", 10, 60, 20, 90, 20, 100, 10, 60, 2.0, 2.5)]
    public void Each_class_has_the_appendix_C2_starts_caps_and_multipliers(string cls,
        int brawnStart, int brawnCap, int smartsStart, int smartsCap, int aimStart, int aimCap, int gritStart, int gritCap,
        double lifePerGrit, double manaPerSmarts)
    {
        var row = Content.Classes[cls];
        Assert.Equal((brawnStart, brawnCap), (row.Bounds("brawn").Start, row.Bounds("brawn").Cap));
        Assert.Equal((smartsStart, smartsCap), (row.Bounds("smarts").Start, row.Bounds("smarts").Cap));
        Assert.Equal((aimStart, aimCap), (row.Bounds("aim").Start, row.Bounds("aim").Cap));
        Assert.Equal((gritStart, gritCap), (row.Bounds("grit").Start, row.Bounds("grit").Cap));

        // The multipliers, read off the derived stats: one more point of Now.
        var hero = Heroes.New(cls, "t");
        var plain = Heroes.Stats(hero);
        var grit = Heroes.Stats(hero, [new Contribution("grit", 1, Gear("ring"))]);
        var smarts = Heroes.Stats(hero, [new Contribution("smarts", 1, Gear("ring"))]);
        Assert.Equal(lifePerGrit, grit.MaxLife - plain.MaxLife, 9);
        Assert.Equal(manaPerSmarts, smarts.MaxMana - plain.MaxMana, 9);
    }

    [Fact]
    public void Every_class_starts_with_60_points_and_has_250_of_headroom()
    {
        Assert.Equal(9, Content.Classes.Count);
        Assert.All(Content.Classes.Rows, c =>
        {
            Assert.Equal(AttributeIds.Order(), c.Attributes.Select(a => a.Attribute).Order());
            Assert.Equal(60, c.Attributes.Sum(a => a.Start));
            Assert.Equal(250, c.Attributes.Sum(a => a.Cap - a.Start));
        });
    }

    [Fact]
    public void A_new_hero_is_level_1_at_its_class_starts_with_nothing_to_spend()
    {
        var hero = Heroes.New("Soldier", "Jane");

        Assert.Equal("soldier", hero.ClassId);
        Assert.Equal((1, 0L, 0, 0), (hero.Level, hero.Xp, hero.AttributePoints, hero.SkillPoints));
        Assert.Equal([20, 5, 20, 15], AttributeIds.Select(hero.Base));
        Assert.True(Heroes.Audit(hero).Ok);
    }

    [Fact]
    public void An_unknown_class_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Heroes.New("necromancer", "x"));
    }

    //-------------------------------------------------------------------------
    // What a point buys: one fact per row of C.2
    //-------------------------------------------------------------------------

    /// <summary>The stats of a Soldier with one more point of <paramref name="attribute"/> than <paramref name="hero"/>.</summary>
    private static (HeroStats Before, HeroStats After) OnePointMore(string attribute)
    {
        var hero = Heroes.New("soldier", "t");
        return (Heroes.Stats(hero), Heroes.Stats(hero, [new Contribution(attribute, 1, Gear("ring"))]));
    }

    [Fact]
    public void A_point_of_brawn_buys_melee_and_blast_damage_and_knockback_resistance()
    {
        var (a, b) = OnePointMore("brawn");

        Assert.Equal(0.01, b.DamageMultiplier("melee") - a.DamageMultiplier("melee"), 9);
        Assert.Equal(0.005, b.DamageMultiplier("blast") - a.DamageMultiplier("blast"), 9);
        Assert.Equal(0.005, b.KnockbackResist - a.KnockbackResist, 9);
        Assert.Equal(a.DamageMultiplier("bullet"), b.DamageMultiplier("bullet"), 9);
    }

    [Fact]
    public void A_point_of_smarts_buys_mana_action_damage_and_mana_regeneration()
    {
        var (a, b) = OnePointMore("smarts");

        Assert.Equal(1.5, b.MaxMana - a.MaxMana, 9);                                  // the Soldier's manaPerSmarts
        Assert.Equal(0.01, b.DamageMultiplier("action") - a.DamageMultiplier("action"), 9);
        Assert.Equal(1 * 0.005, b.ManaRegenRate - a.ManaRegenRate, 9);                // base 1 %/s, +0.5 % of it a point
    }

    [Fact]
    public void A_point_of_aim_buys_bullet_and_projectile_damage_crit_tighter_spread_and_armour()
    {
        var (a, b) = OnePointMore("aim");

        Assert.Equal(0.01, b.DamageMultiplier("bullet") - a.DamageMultiplier("bullet"), 9);
        Assert.Equal(0.01, b.DamageMultiplier("projectile") - a.DamageMultiplier("projectile"), 9);
        Assert.Equal(0.001, b.CritChance - a.CritChance, 9);
        Assert.Equal(-0.003, b.SpreadMultiplier - a.SpreadMultiplier, 9);
        Assert.Equal(0.2, b.Armour - a.Armour, 9);
    }

    [Fact]
    public void A_point_of_grit_buys_life_and_every_resistance()
    {
        var (a, b) = OnePointMore("grit");

        Assert.Equal(3.0, b.MaxLife - a.MaxLife, 9);                                  // the Soldier's lifePerGrit
        Assert.All(new[] { "fire", "blast", "bullet", "shock" },
            r => Assert.Equal(0.002, b.Resist(r) - a.Resist(r), 9));
    }

    [Fact]
    public void Max_life_is_class_life_plus_life_per_grit_and_per_level()
    {
        // Soldier: 200 life, 3 a Grit (15 at start), 18 a level (RU14's pacing: 2 was).
        Assert.Equal(200 + 3 * 15 + 18 * 1, Heroes.Stats(Heroes.New("soldier", "t")).MaxLife, 9);
        Assert.Equal(200 + 3 * 15 + 18 * 10, Heroes.Stats(AtLevel("soldier", 10)).MaxLife, 9);
    }

    //-------------------------------------------------------------------------
    // Base and Now; contributions
    //-------------------------------------------------------------------------

    [Fact]
    public void An_active_items_contribution_raises_now_and_leaves_base()
    {
        var stats = Heroes.Stats(Heroes.New("soldier", "t"), [new Contribution("brawn", 7, Gear("helm"))]);

        Assert.Equal(20, stats.Base("brawn"));
        Assert.Equal(27, stats.Now("brawn"), 9);
    }

    [Theory]
    [InlineData(SourceState.Inactive)]
    [InlineData(SourceState.Broken)]
    public void An_inactive_or_broken_items_contribution_counts_for_nothing(SourceState state)
    {
        var hero = Heroes.New("soldier", "t");
        var given = new[] { new Contribution("brawn", 7, Gear("helm", state)), new Contribution("max_life", 50, Gear("helm", state)) };

        var stats = Heroes.Stats(hero, given);

        Assert.Equal(20, stats.Now("brawn"), 9);
        Assert.Equal(Heroes.Stats(hero).MaxLife, stats.MaxLife, 9);
        Assert.NotEqual(Heroes.Stats(hero).MaxLife, Heroes.Stats(hero, [given[1] with { Source = Gear("helm") }]).MaxLife);
    }

    [Fact]
    public void Skill_and_buff_contributions_count_beside_items()
    {
        var hero = Heroes.New("soldier", "t");
        var stats = Heroes.Stats(hero,
        [
            new Contribution("max_life_pct", 10, new ContributionSource(SourceKind.Skill, "basic_training")),
            new Contribution("max_life", 20, new ContributionSource(SourceKind.Buff, "rally")),
        ]);

        Assert.Equal((Heroes.Stats(hero).MaxLife + 20) * 1.10, stats.MaxLife, 9);
    }

    //-------------------------------------------------------------------------
    // Experience and levels
    //-------------------------------------------------------------------------

    [Fact]
    public void Xp_to_next_is_100_times_level_to_the_2_2_rounded()
    {
        Assert.Equal(100, Heroes.XpToNext(1));
        Assert.Equal(459, Heroes.XpToNext(2));      // 100 * 2^2.2 = 459.48
        Assert.Equal(15849, Heroes.XpToNext(10));   // 100 * 10^2.2 = 15848.93
        for (int level = 1; level < 30; level++)
            Assert.Equal((long)Math.Round(100 * Math.Pow(level, 2.2), MidpointRounding.AwayFromZero), Heroes.XpToNext(level));
    }

    [Fact]
    public void Experience_is_a_running_total_and_each_level_starts_where_the_last_needed()
    {
        Assert.Equal(0, Heroes.XpAtLevel(1));
        Assert.Equal(100, Heroes.XpAtLevel(2));
        Assert.Equal(559, Heroes.XpAtLevel(3));
        Assert.Equal(1, Heroes.LevelForXp(99));
        Assert.Equal(2, Heroes.LevelForXp(100));
        Assert.Equal(2, Heroes.LevelForXp(558));
        Assert.Equal(3, Heroes.LevelForXp(559));
    }

    [Fact]
    public void A_level_up_grants_5_attribute_points_and_1_skill_point()
    {
        var gain = Heroes.GainXp(Heroes.New("soldier", "t"), 100);

        Assert.Equal(1, gain.LevelsGained);
        Assert.Equal((2, 5, 1), (gain.Sheet.Level, gain.Sheet.AttributePoints, gain.Sheet.SkillPoints));
        Assert.Equal((5, 1), (gain.AttributePointsGranted, gain.SkillPointsGranted));
    }

    [Fact]
    public void Several_levels_at_once_grant_each_levels_points()
    {
        var gain = Heroes.GainXp(Heroes.New("soldier", "t"), Heroes.XpAtLevel(4));

        Assert.Equal((4, 3), (gain.Sheet.Level, gain.LevelsGained));
        Assert.Equal((15, 3), (gain.Sheet.AttributePoints, gain.Sheet.SkillPoints));
    }

    [Fact]
    public void A_level_up_refills_life_and_mana_to_full()
    {
        var hero = Heroes.New("soldier", "t");
        var gain = Heroes.GainXp(hero, 100);
        var after = Heroes.Stats(gain.Sheet);

        var vitals = HeroRules.AfterGain(gain, new Vitals(1, 0), after);

        Assert.True(gain.RefillsLifeAndMana);
        Assert.Equal(after.Full, vitals);
        Assert.True(after.MaxLife > 1 && after.MaxMana > 0);   // the stimulus: full is not where it started
    }

    [Fact]
    public void Experience_that_gains_no_level_refills_nothing()
    {
        var gain = Heroes.GainXp(Heroes.New("soldier", "t"), 99);

        Assert.False(gain.RefillsLifeAndMana);
        Assert.Equal(new Vitals(1, 0), HeroRules.AfterGain(gain, new Vitals(1, 0), Heroes.Stats(gain.Sheet)));
    }

    [Fact]
    public void Levels_stop_at_30_and_experience_past_it_is_discarded()
    {
        var gain = Heroes.GainXp(Heroes.New("soldier", "t"), Heroes.XpCap + 1234);

        Assert.Equal(30, gain.Sheet.Level);
        Assert.Equal(Heroes.XpCap, gain.Sheet.Xp);
        Assert.Equal(1234, gain.XpDiscarded);
        Assert.Equal(5 * 29, gain.Sheet.AttributePoints);
        Assert.Equal(0, Heroes.GainXp(gain.Sheet, 1_000_000).LevelsGained);
    }

    //-------------------------------------------------------------------------
    // Spending
    //-------------------------------------------------------------------------

    [Fact]
    public void A_spend_moves_one_point_from_unspent_into_base()
    {
        var r = Heroes.Spend(AtLevel("soldier", 2), "aim", 1);

        Assert.True(r.Ok);
        Assert.Equal((21, 4, 1, 0), (r.Sheet.Base("aim"), r.Sheet.AttributePoints, r.Spent, r.Remainder));
    }

    [Fact]
    public void A_spend_with_no_points_is_refused()
    {
        var hero = Heroes.New("soldier", "t");

        var r = Heroes.Spend(hero, "aim", 1);

        Assert.Equal(SpendRefusal.NoPoints, r.Refusal);
        Assert.Same(hero, r.Sheet);
    }

    [Fact]
    public void A_spend_into_a_capped_attribute_is_refused()
    {
        var hero = AtLevel("soldier", 12) with { Attributes = Heroes.New("soldier", "t").Attributes.SetItem("smarts", 50) };

        var r = Heroes.Spend(hero, "smarts", 1);

        Assert.Equal(SpendRefusal.AtCap, r.Refusal);
        Assert.Equal(50, r.Sheet.Base("smarts"));
    }

    [Fact]
    public void A_shift_spend_stops_at_the_cap_and_returns_the_remainder()
    {
        // Soldier Smarts: start 5, cap 50. 43 points in, 2 below the cap.
        var hero = AtLevel("soldier", 11) with { Attributes = Heroes.New("soldier", "t").Attributes.SetItem("smarts", 48), AttributePoints = 7 };

        var r = Heroes.Spend(hero, "smarts", 5);

        Assert.True(r.Ok);
        Assert.Equal((50, 2, 3, 5), (r.Sheet.Base("smarts"), r.Spent, r.Remainder, r.Sheet.AttributePoints));
    }

    [Fact]
    public void A_shift_spend_stops_at_the_points_held_and_returns_the_remainder()
    {
        var hero = AtLevel("soldier", 2) with { AttributePoints = 3 };

        var r = Heroes.Spend(hero, "grit", 5);

        Assert.Equal((18, 3, 2, 0), (r.Sheet.Base("grit"), r.Spent, r.Remainder, r.Sheet.AttributePoints));
    }

    [Fact]
    public void A_spend_into_something_that_is_not_an_attribute_is_refused()
    {
        Assert.Equal(SpendRefusal.UnknownAttribute, Heroes.Spend(AtLevel("soldier", 2), "luck", 1).Refusal);
    }

    //-------------------------------------------------------------------------
    // Respec
    //-------------------------------------------------------------------------

    [Fact]
    public void A_respec_returns_every_invested_point_and_restores_the_class_starts()
    {
        var hero = Heroes.Spend(Heroes.Spend(AtLevel("medic", 7), "smarts", 5).Sheet, "grit", 5).Sheet
                   with { SkillRanks = AtLevel("medic", 7).SkillRanks.Add("leech", 2), SkillPoints = 4 };
        Assert.True(Heroes.Audit(hero).Ok);

        var r = Heroes.Respec(hero);

        Assert.Equal([10, 25, 10, 15], AttributeIds.Select(r.Sheet.Base));
        Assert.Equal((30, 6), (r.Sheet.AttributePoints, r.Sheet.SkillPoints));
        Assert.Empty(r.Sheet.SkillRanks);
        Assert.True(Heroes.Audit(r.Sheet).Ok);
    }

    [Fact]
    public void Each_respec_costs_more_than_the_last()
    {
        var hero = AtLevel("soldier", 5);
        var prices = new List<long>();
        for (int i = 0; i < 6; i++)
        {
            var r = Heroes.Respec(hero);
            prices.Add(r.Price);
            hero = r.Sheet;
        }

        Assert.Equal(500, prices[0]);
        Assert.All(prices.Zip(prices.Skip(1)), p => Assert.True(p.Second > p.First, $"{p.Second} <= {p.First}"));
        Assert.Equal(6, hero.RespecCount);
    }

    //-------------------------------------------------------------------------
    // The audit identity (C.3)
    //-------------------------------------------------------------------------

    [Fact]
    public void The_audit_identity_holds_over_random_level_ups_spends_and_respecs()
    {
        int steps = 0, spends = 0, levelUps = 0, respecs = 0;
        foreach (var seed in Enumerable.Range(1, 12))
        {
            var random = new Random(seed);   // seeded: a failure names its seed and reproduces
            var cls = Content.Classes.Rows[random.Next(Content.Classes.Count)];
            var hero = Heroes.New(cls.Id, "p");
            for (int step = 0; step < 400; step++, steps++)
            {
                switch (random.Next(10))
                {
                    case < 3:
                        var gain = Heroes.GainXp(hero, random.Next(0, 20_000));
                        levelUps += gain.LevelsGained;
                        hero = gain.Sheet;
                        break;
                    case < 9:
                        var spend = Heroes.Spend(hero, AttributeIds[random.Next(4)], random.Next(2) == 0 ? 1 : 5);
                        spends += spend.Spent;
                        hero = spend.Sheet;
                        break;
                    default:
                        hero = Heroes.Respec(hero).Sheet;
                        respecs++;
                        break;
                }

                int invested = cls.Attributes.Sum(b => hero.Base(b.Attribute) - b.Start);
                Assert.True(invested + hero.AttributePoints == 5 * (hero.Level - 1), $"seed {seed} step {step}: identity");
                Assert.True(cls.Attributes.All(b => hero.Base(b.Attribute) <= b.Cap), $"seed {seed} step {step}: cap");
                var audit = Heroes.Audit(hero);
                Assert.True(audit.Ok, $"seed {seed} step {step}: {audit.Rule} {audit.Detail}");
            }
        }
        // The stimulus reached every arm.
        Assert.True(spends > 1000 && levelUps > 100 && respecs > 100, $"spends {spends} level-ups {levelUps} respecs {respecs}");
    }

    [Fact]
    public void A_sheet_with_one_point_more_than_its_level_earned_fails_the_audit_naming_the_rule()
    {
        var hero = AtLevel("soldier", 5);
        var forged = hero with { AttributePoints = hero.AttributePoints + 1 };

        Assert.True(Heroes.Audit(hero).Ok);
        Assert.Equal("attribute_points", Heroes.Audit(forged).Rule);
    }

    [Fact]
    public void A_base_above_its_class_cap_fails_the_audit()
    {
        var hero = AtLevel("soldier", 30);
        var forged = hero with { Attributes = hero.Attributes.SetItem("smarts", 51), AttributePoints = hero.AttributePoints - 46 };

        Assert.Equal("attribute_over_cap", Heroes.Audit(forged).Rule);
    }

    [Fact]
    public void A_level_its_experience_does_not_reach_fails_the_audit()
    {
        var hero = AtLevel("soldier", 5);

        Assert.Equal("xp_level_mismatch", Heroes.Audit(hero with { Xp = hero.Xp - 1 }).Rule);
    }

    [Fact]
    public void A_skill_rank_bought_with_no_point_fails_the_audit()
    {
        var hero = AtLevel("soldier", 5);

        Assert.Equal("skill_points", Heroes.Audit(hero with { SkillRanks = hero.SkillRanks.Add("leech", 1) }).Rule);
    }
}
