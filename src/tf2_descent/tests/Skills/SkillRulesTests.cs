using System.Reflection;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;
using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// Buying and using skills as pure rules (plan_tf2_rpg 2e, Appendix A; RU6 step 4): buying a
/// rank and each refusal, bonus ranks from gear, the action damage formula and the attribute
/// each tab reads from Now, the same damage whatever weapon is held, which upgrades apply to
/// which weapon family, and passive upgrades feeding the hero sheet under its caps.
/// </summary>
public sealed class SkillRulesTests
{
    private static readonly TF2DescentRules Rules = new();
    private static HeroRules Heroes => Rules.Heroes;
    private static SkillRules Skills => Rules.Skills;
    private static DescentContent Content => Rules.Content;
    private static SkillBonuses None => SkillBonuses.None;

    /// <summary>A hero at <paramref name="level"/> with every point it earned unspent.</summary>
    internal static HeroSheet AtLevel(string cls, int level) =>
        Heroes.GainXp(Heroes.New(cls, "t"), Heroes.XpAtLevel(level)).Sheet;

    /// <summary>Buys each rank in order, every buy asserted to succeed.</summary>
    internal static HeroSheet Learn(HeroSheet sheet, params string[] ranks)
    {
        foreach (var id in ranks)
        {
            var r = Skills.Buy(sheet, id);
            Assert.True(r.Ok, $"buying {id}: {r.Refusal} {r.Detail}");
            sheet = r.Sheet;
        }
        return sheet;
    }

    internal static string[] Times(string id, int n) => [.. Enumerable.Repeat(id, n)];

    private static ContributionSource Gear(string id) => new(SourceKind.Item, id);

    //-------------------------------------------------------------------------
    // Buying
    //-------------------------------------------------------------------------

    [Fact]
    public void Buying_a_tier_one_skill_spends_a_point_and_sets_rank_one()
    {
        var hero = AtLevel("soldier", 2);
        var r = Skills.Buy(hero, "patriot_missile");

        Assert.True(r.Ok);
        Assert.Equal(1, r.Sheet.SkillRanks["patriot_missile"]);
        Assert.Equal(hero.SkillPoints - 1, r.Sheet.SkillPoints);
        Assert.True(Heroes.Audit(r.Sheet).Ok);   // ranks + unspent = level - 1 still holds
    }

    [Fact]
    public void A_rank_below_its_tiers_level_is_refused()
    {
        // Tier 2 unlocks at level 6.
        var at5 = Learn(AtLevel("soldier", 5), "patriot_missile");
        var at6 = Learn(AtLevel("soldier", 6), "patriot_missile");

        Assert.Equal(BuyRefusal.BelowLevel, Skills.Buy(at5, "salvo").Refusal);
        Assert.True(Skills.Buy(at6, "salvo").Ok);
    }

    [Fact]
    public void A_skill_without_its_prerequisite_at_rank_one_is_refused()
    {
        var hero = AtLevel("soldier", 6);

        Assert.Equal(BuyRefusal.Prerequisite, Skills.Buy(hero, "salvo").Refusal);
        Assert.True(Skills.Buy(Learn(hero, "patriot_missile"), "salvo").Ok);
    }

    [Fact]
    public void A_prerequisite_with_a_minimum_rank_needs_that_rank()
    {
        // Parachute needs Jump Training 3 (A.3).
        var two = Learn(AtLevel("soldier", 18), Times("jump_training", 2));

        Assert.Equal(BuyRefusal.Prerequisite, Skills.Buy(two, "parachute").Refusal);
        Assert.True(Skills.Buy(Learn(two, "jump_training"), "parachute").Ok);
    }

    [Fact]
    public void Chain_of_command_needs_any_two_orders()
    {
        var one = Learn(AtLevel("soldier", 18), "battle_orders", "basic_training");

        Assert.Equal(BuyRefusal.Prerequisite, Skills.Buy(one, "chain_of_command").Refusal);
        Assert.True(Skills.Buy(Learn(one, "dig_in"), "chain_of_command").Ok);
    }

    [Fact]
    public void A_skill_at_max_rank_is_refused()
    {
        var hero = Learn(AtLevel("soldier", 10), Times("patriot_missile", 5));

        Assert.Equal(BuyRefusal.AtMaxRank, Skills.Buy(hero, "patriot_missile").Refusal);
    }

    [Fact]
    public void A_buy_with_no_skill_point_is_refused()
    {
        var hero = Learn(AtLevel("soldier", 2), "patriot_missile");   // its one point spent

        var r = Skills.Buy(hero, "marksmanship");
        Assert.Equal(BuyRefusal.NoPoints, r.Refusal);
        Assert.Same(hero, r.Sheet);
    }

    [Theory]
    [InlineData("scout")]
    [InlineData("soldier")]
    [InlineData("pyro")]
    [InlineData("demoman")]
    [InlineData("heavy")]
    [InlineData("engineer")]
    [InlineData("medic")]
    [InlineData("sniper")]
    [InlineData("spy")]
    public void Every_class_may_buy_mercenary_skills(string cls)
    {
        var hero = AtLevel(cls, 3);

        Assert.True(Skills.Buy(hero, "deep_pockets").Ok);
        Assert.True(Skills.Buy(hero, "leech").Ok);
    }

    [Fact]
    public void Another_classs_tree_is_refused()
    {
        var scout = AtLevel("scout", 10);

        Assert.Equal(BuyRefusal.OtherClass, Skills.Buy(scout, "patriot_missile").Refusal);
        Assert.Equal(BuyRefusal.OtherClass, Skills.Buy(scout, "basic_training").Refusal);
    }

    [Fact]
    public void A_mercenary_rank_needs_its_own_hero_level_floor()
    {
        // Deep Pockets rank r needs level 4r - 3: rank 2 at level 5.
        var at4 = Learn(AtLevel("soldier", 4), "deep_pockets");
        var at5 = Learn(AtLevel("soldier", 5), "deep_pockets");

        Assert.Equal(BuyRefusal.BelowLevel, Skills.Buy(at4, "deep_pockets").Refusal);
        Assert.True(Skills.Buy(at5, "deep_pockets").Ok);
    }

    [Fact]
    public void A_respec_clears_skill_ranks_and_returns_every_point()
    {
        var hero = Learn(AtLevel("soldier", 8), "patriot_missile", "patriot_missile", "deep_pockets");

        var after = Heroes.Respec(hero).Sheet;
        Assert.Empty(after.SkillRanks);
        Assert.Equal(7, after.SkillPoints);
    }

    //-------------------------------------------------------------------------
    // Bonus ranks
    //-------------------------------------------------------------------------

    [Fact]
    public void Bonus_ranks_may_pass_max_rank_by_up_to_three()
    {
        var hero = Learn(AtLevel("soldier", 10), Times("patriot_missile", 5));

        Assert.Equal(6, Skills.EffectiveRank(hero, None.WithSkill("patriot_missile", 1), "patriot_missile"));
        Assert.Equal(8, Skills.EffectiveRank(hero, None.WithSkill("patriot_missile", 2).WithTab("ordnance", 2).WithAll(1), "patriot_missile"));
    }

    [Fact]
    public void Bonus_ranks_never_unlock_a_skill_with_no_real_point()
    {
        var hero = AtLevel("soldier", 10);

        Assert.Equal(0, Skills.EffectiveRank(hero, None.WithSkill("patriot_missile", 3).WithAll(2), "patriot_missile"));
    }

    [Fact]
    public void Tab_and_all_skill_bonuses_reach_the_class_tabs_only()
    {
        var hero = Learn(AtLevel("soldier", 10), "patriot_missile", "jump_training", "leech");
        var bonuses = None.WithTab("ordnance", 1).WithAll(1);

        Assert.Equal(3, Skills.EffectiveRank(hero, bonuses, "patriot_missile"));
        Assert.Equal(2, Skills.EffectiveRank(hero, bonuses, "jump_training"));
        Assert.Equal(1, Skills.EffectiveRank(hero, bonuses.WithSkill("leech", 2), "leech"));   // the shared tab takes no bonus
    }

    [Fact]
    public void A_bonus_rank_raises_a_skills_numbers()
    {
        var hero = Learn(AtLevel("soldier", 2), "patriot_missile");
        var stats = Skills.Stats(hero, None);

        double plain = Skills.Damage(hero, stats, None, "patriot_missile");
        double bonus = Skills.Damage(hero, stats, None.WithSkill("patriot_missile", 1), "patriot_missile");
        Assert.Equal(30.0 / 20.0, bonus / plain, 9);   // base 20 at rank 1, 30 at rank 2
    }

    //-------------------------------------------------------------------------
    // Damage
    //-------------------------------------------------------------------------

    [Fact]
    public void Action_damage_is_base_times_hero_level_times_tab_attribute()
    {
        // base(1) 20, level 2, Aim 20: 20 x (1 + 0.04 x 2) x (1 + 20 / 50).
        var hero = Learn(AtLevel("soldier", 2), "patriot_missile");

        Assert.Equal(20 * 1.08 * 1.4, Skills.Damage(hero, Skills.Stats(hero, None), None, "patriot_missile"), 9);
    }

    [Theory]
    [InlineData("patriot_missile", "aim")]
    [InlineData("emp_charge", "smarts")]    // Ordnance's shock actions read Smarts
    [InlineData("rocket_leap", "brawn")]
    [InlineData("battle_orders", "smarts")]
    public void Each_tab_reads_its_attribute(string skill, string attribute)
    {
        Assert.Equal(attribute, Skills.DamageAttribute(Content.Skills[skill]));
    }

    [Fact]
    public void Action_damage_reads_the_attribute_from_Now()
    {
        var hero = Learn(AtLevel("soldier", 2), "patriot_missile");
        var plain = Skills.Stats(hero, None);
        var aimRing = Skills.Stats(hero, None, [new Contribution("aim", 10, Gear("ring"))]);
        var brawnRing = Skills.Stats(hero, None, [new Contribution("brawn", 10, Gear("ring"))]);

        double d0 = Skills.Damage(hero, plain, None, "patriot_missile");
        Assert.Equal(20 * 1.08 * (1 + 30.0 / 50), Skills.Damage(hero, aimRing, None, "patriot_missile"), 9);
        Assert.Equal(d0, Skills.Damage(hero, brawnRing, None, "patriot_missile"), 9);   // another tab's attribute does nothing
    }

    [Fact]
    public void A_synergy_raises_damage_by_the_other_skills_real_ranks()
    {
        // Patriot Missile +4 % per rank of Marksmanship.
        var hero = Learn(AtLevel("soldier", 4), "patriot_missile");
        var withM = Learn(hero, "marksmanship", "marksmanship");

        double d0 = Skills.Damage(hero, Skills.Stats(hero, None), None, "patriot_missile");
        double d2 = Skills.Damage(withM, Skills.Stats(withM, None), None, "patriot_missile");
        Assert.Equal(1.08, d2 / d0, 9);
    }

    [Fact]
    public void An_action_deals_the_same_damage_with_every_weapon_family_held_and_with_none()
    {
        // A held weapon reaches the rules only as its family's upgrade contributions; an
        // action reads none of them. A hero holding both family upgrades and every damaging
        // Soldier action shows it for all 18 families, every action and every spell.
        var hero = Learn(AtLevel("soldier", 30), "patriot_missile", "marksmanship", "rocket_specialist",
            "boot_camp", "pain_tolerance", "trench_fighter", "salvo", "emp_charge", "rain_of_freedom", "fire_mission",
            "jump_training", "stomp", "rocket_leap", "death_from_above", "human_rocket");
        foreach (var spell in Content.Spells.Rows)
            hero = Rules.Spells.ReadTome(hero, spell.Id).Sheet;
        var actions = Content.Skills.Rows.Where(s => s.IsCast && s.DealsDamage).Select(s => s.Id).ToList();
        Assert.Equal(["patriot_missile", "salvo", "emp_charge", "rain_of_freedom", "fire_mission", "rocket_leap", "human_rocket"], actions);

        double[] Each(HeroStats stats) =>
            [.. actions.Select(a => Skills.Damage(hero, stats, None, a)),
             .. Content.Spells.Rows.Select(s => Rules.Spells.Resolve(hero, stats, s.Id, 1).Damaging!["damage"])];
        var none = Each(Skills.Stats(hero, None));

        Assert.Equal(18, Content.Families.Count);
        int withAny = 0;
        foreach (var family in Content.Families.Rows)
        {
            var held = Skills.WeaponContributions(hero, None, family.Id);
            withAny += held.Count;
            Assert.Equal(none, Each(Skills.Stats(hero, None, held)));
        }
        Assert.True(withAny > 0);   // the stimulus: some family's weapon did add contributions
    }

    [Fact]
    public void The_damage_rules_take_no_weapon_input()
    {
        // R17 by signature: no parameter of the damage entry points names a weapon, an item or a family.
        string[] entries = [nameof(SkillRules.Damage), nameof(SkillRules.ActionDamage), nameof(SkillRules.Resolve)];
        var methods = typeof(SkillRules).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => entries.Contains(m.Name)).ToList();

        Assert.Equal(3, methods.Count);
        Assert.All(methods.SelectMany(m => m.GetParameters()), p =>
        {
            Assert.DoesNotMatch("(?i)weapon|item|family", p.Name);
            Assert.DoesNotMatch("(?i)weapon|item|family", p.ParameterType.Name);
        });
    }

    //-------------------------------------------------------------------------
    // Weapon families
    //-------------------------------------------------------------------------

    [Fact]
    public void A_family_scoped_upgrade_applies_to_its_family_and_to_no_other()
    {
        var hero = Learn(AtLevel("soldier", 12), "marksmanship", "rocket_specialist");
        var rs = Content.Skills["rocket_specialist"];

        foreach (var family in Content.Families.Rows)
        {
            var held = Skills.WeaponContributions(hero, None, family.Id);
            bool own = family.Id == "rocket_launcher";
            Assert.Equal(own, SkillRules.Applies(rs, family.Id));
            Assert.Equal(own, held.Any(c => c.Source.Id == "rocket_specialist"));
        }
        Assert.Contains(Skills.WeaponContributions(hero, None, "rocket_launcher"), c => c.Stat == "reload_speed_pct" && c.Amount == 5);
    }

    [Fact]
    public void Trench_fighter_applies_to_melee_weapons_only()
    {
        var hero = Learn(AtLevel("soldier", 18), "boot_camp", "pain_tolerance", "trench_fighter");

        Assert.Contains(Skills.WeaponContributions(hero, None, "melee"), c => c.Stat == "damage_weapon_pct" && c.Amount == 8);
        Assert.All(Content.Families.Rows.Where(f => f.Id != "melee"), f => Assert.Empty(Skills.WeaponContributions(hero, None, f.Id)));
        Assert.DoesNotContain(Skills.Contributions(hero, None), c => c.Source.Id == "trench_fighter");
    }

    [Fact]
    public void An_all_weapons_upgrade_applies_to_every_family_through_the_hero_sheet()
    {
        var hero = Learn(AtLevel("soldier", 3), "marksmanship", "marksmanship");

        Assert.All(Content.Families.Rows, f => Assert.True(SkillRules.Applies(Content.Skills["marksmanship"], f.Id)));
        Assert.Contains(Skills.Contributions(hero, None), c => c.Stat == "damage_weapon_pct" && c.Amount == 6);
        Assert.Equal(6, Skills.Stats(hero, None).Total("damage_weapon_pct"));
    }

    [Fact]
    public void An_action_applies_to_no_weapon_family()
    {
        Assert.All(Content.Families.Rows, f => Assert.False(SkillRules.Applies(Content.Skills["patriot_missile"], f.Id)));
    }

    //-------------------------------------------------------------------------
    // The hero sheet
    //-------------------------------------------------------------------------

    [Fact]
    public void Basic_trainings_max_health_shows_in_the_hero_stats()
    {
        var hero = AtLevel("soldier", 6);
        var trained = Learn(hero, Times("basic_training", 5));

        Assert.Equal(1.15, Skills.Stats(trained, None).MaxLife / Skills.Stats(hero, None).MaxLife, 9);
        Assert.Equal(Heroes.Stats(hero).Resist("fire") + 0.10, Skills.Stats(trained, None).Resist("fire"), 9);
    }

    [Fact]
    public void A_bonus_rank_raises_a_passive_contribution()
    {
        var hero = Learn(AtLevel("soldier", 2), "basic_training");

        Assert.Equal(6, Skills.Stats(hero, None.WithAll(1)).Total("max_life_pct"));
    }

    [Fact]
    public void Life_leech_from_skills_auras_and_gear_stacks_additively_under_the_cap()
    {
        var hero = Learn(AtLevel("soldier", 20), Times("leech", 3).Concat(Times("battle_orders", 5)).Concat(Times("forward_march", 5)).ToArray());
        var march = Skills.Resolve(hero, Skills.Stats(hero, None), None, "forward_march").Effects.First(e => e.Primitive == Primitive.Aura);

        Assert.Equal(0.03, Skills.Stats(hero, None).LifeLeech, 9);   // Leech 3 x 1 %
        var affix = new Contribution("life_leech_pct", 2, Gear("ring"));
        Assert.Equal(0.05, Skills.Stats(hero, None, [affix]).LifeLeech, 9);
        // Forward March at rank 5 (+30 %, raised by Battle Orders' potency) pushes it past the 15 % cap.
        Assert.Equal(0.15, Skills.Stats(hero, None, [affix, .. SkillRules.AsBuff("forward_march", march)]).LifeLeech, 9);
    }

    [Fact]
    public void Regeneration_from_second_wind_and_forward_march_stacks_under_the_three_percent_cap()
    {
        var hero = Learn(AtLevel("soldier", 20), [.. Times("second_wind", 5), .. Times("battle_orders", 5), .. Times("forward_march", 5)]);
        var stats = Skills.Stats(hero, None);

        // Second Wind 5 x 0.3 % of max life a second, plus Forward March's passive 3 HP a second.
        Assert.Equal(0.015 * stats.MaxLife + 3, stats.LifeRegenPerSecond, 9);
        var lots = Skills.Stats(hero, None, [new Contribution("life_regen_pct", 2, Gear("amulet"))]);
        Assert.Equal(0.03 * lots.MaxLife, lots.LifeRegenPerSecond, 9);
    }

    [Fact]
    public void Clear_head_raises_mana_regeneration_by_its_bonus()
    {
        var hero = Learn(AtLevel("soldier", 15), Times("clear_head", 5));

        // (base 1 + 0) x (1 + bonus / 100): Clear Head's 5 x 8 % adds 0.40 of the 1 % base.
        Assert.Equal(Heroes.Stats(hero).ManaRegenRate + 0.40, Skills.Stats(hero, None).ManaRegenRate, 9);
    }

    [Fact]
    public void Capacity_skills_contribute_no_stat()
    {
        var hero = Learn(AtLevel("soldier", 6), "deep_pockets", "storage_plan", "deep_pockets", "storage_plan");

        Assert.Empty(Skills.Contributions(hero, None));
        Assert.Equal(2, SkillRules.RealRank(hero, "deep_pockets"));   // the inventory rules read the rank
    }
}
