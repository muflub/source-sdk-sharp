using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;
using TF2.Descent.Rules.Spells;
using Xunit;
using static TF2.Descent.Tests.Skills.SkillContent;
using static TF2.Descent.Tests.Skills.SkillRulesTests;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// Tomes and scrolls (plan_tf2_rpg 2e, R27; RU6 step 5): the spell table and its load
/// errors, a tome teaching and raising a rank to the cap and the next one refused and kept,
/// the Smarts requirement read from Now (inactive, never forgotten), a respec leaving spell
/// ranks alone, casting for mana, and a scroll cast at its rank for nothing and used up
/// exactly once even when interrupted.
/// </summary>
public sealed class SpellRulesTests
{
    private static readonly TF2DescentRules Rules = new();
    private static SpellRules Spells => Rules.Spells;
    private static SkillRules Skills => Rules.Skills;
    private static HeroRules Heroes => Rules.Heroes;
    private static DescentContent Content => Rules.Content;

    private static ContributionSource Gear(string id) => new(SourceKind.Item, id);

    /// <summary>Reads <paramref name="n"/> tomes of the spell, each asserted to teach.</summary>
    private static HeroSheet Read(HeroSheet sheet, string spell, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var r = Spells.ReadTome(sheet, spell);
            Assert.True(r.Ok && r.Consumed, $"tome {i + 1} of {spell}: {r.Refusal}");
            sheet = r.Sheet;
        }
        return sheet;
    }

    /// <summary>A Soldier with Smarts Now at <paramref name="smarts"/> through a ring, so a fact controls the requirement.</summary>
    private static HeroStats WithSmarts(HeroSheet hero, double smarts) =>
        Heroes.Stats(hero, [new Contribution("smarts", smarts - hero.Base("smarts"), Gear("ring"))]);

    private static CasterState Full(double mana = 500) => new() { Mana = mana };

    //-------------------------------------------------------------------------
    // The table
    //-------------------------------------------------------------------------

    [Fact]
    public void The_four_merasmus_spells_load_as_actions_of_one_damage_type()
    {
        Assert.Equal(["fireball", "lightning_ball", "pumpkin_mirv", "blast_jump"], Content.Spells.Rows.Select(s => s.Id));
        Assert.Equal(["fire", "shock", "blast", "blast"], Content.Spells.Rows.Select(s => s.DamageType));
        Assert.All(Content.Spells.Rows, s =>
        {
            Assert.Equal(5, s.MaxRank);
            Assert.Null(s.Scope);
            Assert.Contains(s.Effect, e => e.DealsDamage);
            Assert.All(s.Effect, e => Assert.Null(e.Family));
        });
    }

    [Fact]
    public void Each_spells_smarts_requirement_rises_with_rank()
    {
        Assert.All(Content.Spells.Rows, s => Assert.Equal([10, 20, 30, 40, 50], Enumerable.Range(1, 5).Select(r => s.Smarts.At(r))));
    }

    [Fact]
    public void A_spell_that_names_a_family_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Spells, t => Row(t, "fireball")["scope"] = "flamethrower"));

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Spells, "fireball", ContentRule.FamilyOnAction), (e.Table, e.Row, e.Rule));
    }

    [Fact]
    public void A_spell_with_no_damage_type_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Spells, t => Row(t, "fireball").Remove("damageType")));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.Malformed, "fireball"), (e.Rule, e.Row));
    }

    [Fact]
    public void A_spell_whose_name_is_an_item_bases_is_a_load_error()
    {
        var (source, tables) = WithItemBases(SomeBases);
        var errors = Errors(Planted(source, ContentTables.Spells, t => Row(t, "fireball")["name"] = "Shovel"), tables);

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.NameCollision, "fireball"), (e.Rule, e.Row));
    }

    [Fact]
    public void A_spell_whose_id_is_a_skills_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Spells, t => Row(t, "fireball")["id"] = "stomp"));

        // The rename also leaves fireball's tome naming a spell that is gone (consumables.json, HE8).
        Assert.Contains(errors, e => e.Table == TF2.Descent.Rules.Items.ConsumableTable.TableName && e.Rule == ContentRule.DanglingReference);
        var e = Assert.Single(errors, e => e.Table == ContentTables.Spells);
        Assert.Equal((ContentRule.NameCollision, "stomp"), (e.Rule, e.Row));
    }

    [Fact]
    public void A_spell_rank_cap_other_than_the_settings_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Spells, t =>
        {
            Row(t, "lightning_ball")["maxRank"] = 6;
            Row(t, "lightning_ball")["smarts"] = new JsonObject { ["perRank"] = 10 };
        }));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "lightning_ball"), (e.Rule, e.Row));
        Assert.Contains("'maxRank'", e.Message);
    }

    [Fact]
    public void A_smarts_requirement_that_falls_is_a_load_error()
    {
        var errors = Errors(Planted(ContentTables.Spells, t => Row(t, "fireball")["smarts"] = new JsonArray(10, 20, 15, 40, 50)));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.OutOfRange, "fireball"), (e.Rule, e.Row));
        Assert.Contains("smarts[3]", e.Message);
    }

    //-------------------------------------------------------------------------
    // Tomes
    //-------------------------------------------------------------------------

    [Fact]
    public void The_first_tome_teaches_the_spell_at_rank_one()
    {
        var hero = Heroes.New("heavy", "t");
        var r = Spells.ReadTome(hero, "fireball");

        Assert.True(r.Consumed);
        Assert.Equal(1, SpellRules.Rank(r.Sheet, "fireball"));
        Assert.Equal(0, SpellRules.Rank(hero, "fireball"));
    }

    [Fact]
    public void Each_further_tome_adds_a_rank_to_five()
    {
        var hero = Read(Heroes.New("heavy", "t"), "fireball", 5);

        Assert.Equal(5, SpellRules.Rank(hero, "fireball"));
        Assert.Equal(0, SpellRules.Rank(hero, "lightning_ball"));   // one spell's tomes raise that spell only
    }

    [Fact]
    public void The_sixth_tome_is_refused_and_not_consumed()
    {
        var hero = Read(Heroes.New("heavy", "t"), "fireball", 5);
        var r = Spells.ReadTome(hero, "fireball");

        Assert.Equal(TomeRefusal.AtMaxRank, r.Refusal);
        Assert.False(r.Consumed);
        Assert.Same(hero, r.Sheet);
    }

    [Fact]
    public void Reading_a_tome_spends_no_skill_point()
    {
        var hero = AtLevel("soldier", 5);

        Assert.Equal(hero.SkillPoints, Read(hero, "fireball", 3).SkillPoints);
        Assert.True(Heroes.Audit(Read(hero, "fireball", 3)).Ok);
    }

    [Fact]
    public void A_respec_leaves_spell_ranks_alone()
    {
        var hero = Read(Learn(AtLevel("soldier", 5), "patriot_missile"), "lightning_ball", 3);

        var after = Heroes.Respec(hero).Sheet;
        Assert.Empty(after.SkillRanks);                       // the stimulus: the respec did clear skills
        Assert.Equal(3, SpellRules.Rank(after, "lightning_ball"));
    }

    //-------------------------------------------------------------------------
    // The Smarts requirement
    //-------------------------------------------------------------------------

    [Fact]
    public void Each_rank_needs_its_smarts_read_from_now()
    {
        var hero = Read(Heroes.New("soldier", "t"), "fireball", 2);   // rank 2 needs 20

        Assert.False(Spells.Active(hero, WithSmarts(hero, 19), "fireball"));
        Assert.True(Spells.Active(hero, WithSmarts(hero, 20), "fireball"));
        Assert.Equal(5, Heroes.Stats(hero).Base("smarts"));                 // Base alone would never reach it
    }

    [Fact]
    public void An_unmet_requirement_makes_the_spell_inactive_without_forgetting_it()
    {
        var hero = Read(Heroes.New("soldier", "t"), "fireball", 3);   // rank 3 needs 30
        var low = WithSmarts(hero, 25);

        Assert.False(Spells.Active(hero, low, "fireball"));
        Assert.Equal(CastRefusal.Inactive, Spells.Cast(hero, low, Full(), "fireball").Refusal);
        Assert.Equal(3, SpellRules.Rank(hero, "fireball"));                 // still read
        Assert.True(Spells.Cast(hero, WithSmarts(hero, 30), Full(), "fireball").Ok);   // active again when met
    }

    //-------------------------------------------------------------------------
    // Casting
    //-------------------------------------------------------------------------

    [Fact]
    public void A_spell_cast_spends_its_mana_and_holds_its_cooldown()
    {
        var hero = Read(Heroes.New("soldier", "t"), "fireball", 1);
        var r = Spells.Cast(hero, WithSmarts(hero, 10), Full(20), "fireball");

        Assert.True(r.Ok);
        Assert.Equal(12, r.State.Mana);                                       // 8 at rank 1
        Assert.Equal(CastRefusal.OnCooldown, Spells.Cast(hero, WithSmarts(hero, 10), r.State, "fireball").Refusal);
    }

    [Fact]
    public void A_spell_cast_short_of_mana_is_refused()
    {
        var hero = Read(Heroes.New("soldier", "t"), "fireball", 1);
        var state = Full(7);

        var r = Spells.Cast(hero, WithSmarts(hero, 10), state, "fireball");
        Assert.Equal(CastRefusal.NoMana, r.Refusal);
        Assert.Same(state, r.State);
    }

    [Fact]
    public void A_spell_never_read_cannot_be_cast()
    {
        var hero = Heroes.New("soldier", "t");

        Assert.Equal(CastRefusal.NotLearned, Spells.Cast(hero, WithSmarts(hero, 50), Full(), "fireball").Refusal);
    }

    [Fact]
    public void A_spells_damage_is_the_action_formula_over_smarts_now()
    {
        // Fireball rank 1: base 40 x (1 + 0.04 x 1) x (1 + 30 / 50).
        var hero = Read(Heroes.New("soldier", "t"), "fireball", 1);

        Assert.Equal(40 * 1.04 * 1.6, Spells.Resolve(hero, WithSmarts(hero, 30), "fireball", 1).Damaging!["damage"], 9);
    }

    //-------------------------------------------------------------------------
    // Scrolls
    //-------------------------------------------------------------------------

    [Fact]
    public void A_scroll_casts_at_its_fixed_rank_with_no_smarts_and_no_mana()
    {
        // A Heavy who never read a tome, Smarts 5, throws one rank-3 fireball.
        var heavy = Heroes.New("heavy", "t");
        var r = Spells.UseScroll(heavy, Heroes.Stats(heavy), held: 1, "fireball", rank: 3, interrupted: false);

        Assert.True(r.Cast);
        Assert.Equal(3, r.Spell!.Rank);
        Assert.Equal(0, SpellRules.Rank(heavy, "fireball"));
        Assert.Equal(40 + 40, r.Spell.Damaging!["damage"] / (1.04 * (1 + 5.0 / 50)), 9);   // base 80 at rank 3
    }

    [Fact]
    public void A_scroll_is_consumed_exactly_once_even_when_the_cast_is_interrupted()
    {
        var heavy = Heroes.New("heavy", "t");
        var done = Spells.UseScroll(heavy, Heroes.Stats(heavy), held: 4, "pumpkin_mirv", rank: 2, interrupted: false);
        var cut = Spells.UseScroll(heavy, Heroes.Stats(heavy), held: 4, "pumpkin_mirv", rank: 2, interrupted: true);

        Assert.Equal((3, true, true), (done.Remaining, done.Consumed, done.Cast));
        Assert.Equal((3, true, false), (cut.Remaining, cut.Consumed, cut.Cast));
        Assert.Null(cut.Spell);
    }

    [Fact]
    public void An_empty_scroll_stack_uses_nothing()
    {
        var heavy = Heroes.New("heavy", "t");

        var r = Spells.UseScroll(heavy, Heroes.Stats(heavy), held: 0, "fireball", rank: 1, interrupted: false);
        Assert.Equal((0, false, false), (r.Remaining, r.Consumed, r.Cast));
    }

    [Fact]
    public void Scrolls_stack_to_ten()
    {
        Assert.Equal(new ScrollStack(10, 3), Spells.Stack(8, 5));
        Assert.Equal(new ScrollStack(7, 0), Spells.Stack(2, 5));
    }
}
