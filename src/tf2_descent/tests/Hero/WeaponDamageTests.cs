using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;
using TF2.Descent.Tests.Skills;
using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// damage_weapon_pct, the weapon upgrades' stat (R17, Appendix A: Marksmanship "all
/// weapons: +3 %/rank damage", +5 since RU14's retune; Trench Fighter's melee +8 %/rank): it joins the multiplier of
/// every weapon hit (any weapon kind: melee, blast, bullet, projectile, or the plain weapon
/// kind of a family with none of those) and never an action's, whose damage is its own.
/// </summary>
public sealed class WeaponDamageTests
{
    private static readonly TF2DescentRules Rules = new();
    private static SkillRules Skills => Rules.Skills;

    /// <summary>A level-5 Soldier with Marksmanship at rank 3.</summary>
    private static HeroStats Marksman() =>
        Skills.Stats(SkillRulesTests.Learn(SkillRulesTests.AtLevel("soldier", 5), SkillRulesTests.Times("marksmanship", 3)), SkillBonuses.None);

    private static HeroStats Plain() => Skills.Stats(SkillRulesTests.AtLevel("soldier", 5), SkillBonuses.None);

    [Fact]
    public void Marksmanship_rank_three_raises_a_rockets_multiplier_by_fifteen_points()
    {
        Assert.Equal(0.15, Marksman().DamageMultiplier("blast", "projectile") - Plain().DamageMultiplier("blast", "projectile"), 9);
    }

    [Fact]
    public void Marksmanship_rank_three_raises_a_shotguns_multiplier_by_fifteen_points()
    {
        Assert.Equal(0.15, Marksman().DamageMultiplier("bullet") - Plain().DamageMultiplier("bullet"), 9);
    }

    [Fact]
    public void Marksmanship_rank_three_raises_a_plain_weapon_hit_by_fifteen_points()
    {
        Assert.Equal(0.15, Marksman().DamageMultiplier("weapon") - Plain().DamageMultiplier("weapon"), 9);
    }

    [Fact]
    public void Marksmanship_leaves_an_actions_multiplier_alone()
    {
        var marksman = Marksman();

        Assert.Equal(15, marksman.Total("damage_weapon_pct"), 9);   // the stimulus reached the sheet
        Assert.Equal(Plain().DamageMultiplier("action"), marksman.DamageMultiplier("action"), 9);
    }

    [Fact]
    public void A_hit_of_two_weapon_kinds_takes_the_weapon_stat_once()
    {
        var stats = Marksman();
        double expected = 1 + (stats.Total("damage_all_pct") + stats.Total("damage_blast_pct")
                               + stats.Total("damage_projectile_pct") + stats.Total("damage_weapon_pct")) / 100;

        Assert.Equal(expected, stats.DamageMultiplier("blast", "projectile"), 9);
    }

    [Fact]
    public void With_no_weapon_stat_a_weapon_hit_is_the_all_damage_and_kind_stats_alone()
    {
        var stats = Plain();
        double expected = 1 + (stats.Total("damage_all_pct") + stats.Total("damage_bullet_pct")) / 100;

        Assert.Equal(0, stats.Total("damage_weapon_pct"), 9);
        Assert.Equal(expected, stats.DamageMultiplier("bullet"), 9);
    }

    [Fact]
    public void A_weapon_hit_naming_an_unknown_kind_is_a_load_error()
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(ContentTables.Hero, out var bytes));
        var hero = JsonNode.Parse(bytes)!;
        hero["weaponHit"]!["kinds"]!.AsArray().Add("laser");

        var errors = new ContentLoader().Load(source.Set(ContentTables.Hero, Encoding.UTF8.GetBytes(hero.ToJsonString()))).Errors;

        Assert.Contains(errors, e => e.Table == ContentTables.Hero && e.Message.Contains("'laser'"));
    }

    [Fact]
    public void Trench_fighter_raises_a_melee_hit_while_melee_is_in_hand()
    {
        var sheet = SkillRulesTests.Learn(SkillRulesTests.AtLevel("soldier", 18), "boot_camp", "pain_tolerance", "trench_fighter");
        var inHand = Rules.Heroes.Stats(sheet, [.. Skills.Contributions(sheet, SkillBonuses.None), .. Skills.WeaponContributions(sheet, SkillBonuses.None, "melee")]);
        var none = Rules.Heroes.Stats(sheet, [.. Skills.Contributions(sheet, SkillBonuses.None)]);

        Assert.Equal(0.08, inHand.DamageMultiplier("melee") - none.DamageMultiplier("melee"), 9);
    }
}
