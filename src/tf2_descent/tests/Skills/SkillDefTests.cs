using System.Text.Json;
using System.Text.Json.Nodes;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Skills;
using Xunit;
using static TF2.Descent.Tests.Skills.SkillContent;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// <see cref="SkillDef"/> and its tables (plan_descent_logic RU6, plan_tf2_rpg 2e): the
/// shipped skill tables load, a row reads into the descriptor it was written as, a rank curve
/// answers every rank in each of its forms, and a malformed effect is a load error.
/// </summary>
public sealed class SkillDefTests
{
    private static RankCurve Curve(string json, int ranks)
    {
        var errors = new ContentErrors();
        using var doc = JsonDocument.Parse(json);
        var c = RankCurve.Read(doc.RootElement, ranks, "x", "t", "r", errors);
        Assert.Empty(errors.All);
        return c;
    }

    //-------------------------------------------------------------------------
    // The shipped tables
    //-------------------------------------------------------------------------

    [Fact]
    public void The_shipped_skill_tables_load_with_no_errors()
    {
        var result = new ContentLoader().Load(ShippedCopy());

        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Content.Skills.Rows);           // the stimulus: the table was read
        Assert.Equal(4, result.Content.SkillTabs.Count);
        Assert.Equal([1, 6, 12, 18, 24, 30], result.Content.SkillSettings.TierLevels);
    }

    [Fact]
    public void An_action_row_reads_into_its_descriptor()
    {
        var pm = Shipped().Skills["patriot_missile"];

        Assert.Equal(("soldier", "ordnance", 1, SkillKind.Action, 5, "blast"), (pm.Class, pm.Tab, pm.Tier, pm.Kind, pm.MaxRank, pm.DamageType));
        Assert.Null(pm.Scope);
        Assert.Equal(4, pm.ManaCost.At(1));
        var e = Assert.Single(pm.Effect);
        Assert.Equal(Primitive.Projectile, e.Primitive);
        Assert.Equal((20.0, 60.0), (e.At("damage", 1), e.At("damage", 5)));
        Assert.Equal(110, e.At("radius", 3));
        Assert.Equal(1, e.At("count", 1));                     // a parameter the row leaves out is the primitive's default
        Assert.Equal(new Synergy("marksmanship", 4, "damage_pct"), Assert.Single(pm.Synergies));
    }

    [Fact]
    public void An_upgrade_row_reads_its_scope_and_passive_stats()
    {
        var m = Shipped().Skills["marksmanship"];

        Assert.Equal((SkillKind.Upgrade, "all"), (m.Kind, m.Scope));
        Assert.Null(m.Family);
        var stats = Assert.Single(m.Effect).Stats.ToDictionary(s => s.Stat, s => s.Amount);
        Assert.Equal((5.0, 25.0), (stats["damage_weapon_pct"].At(1), stats["damage_weapon_pct"].At(5)));
        Assert.Equal(-20, stats["spread_pct"].At(5));
    }

    //-------------------------------------------------------------------------
    // Rank curves
    //-------------------------------------------------------------------------

    [Fact]
    public void A_from_to_curve_interpolates_linearly_from_rank_one_to_max_rank()
    {
        var c = Curve("""{ "from": 20, "to": 60 }""", 5);

        Assert.Equal([20, 30, 40, 50, 60], c.Values);
    }

    [Fact]
    public void A_per_rank_curve_is_base_plus_per_rank_times_rank()
    {
        Assert.Equal([1, 5, 9, 13, 17, 21, 25], Curve("""{ "base": -3, "perRank": 4 }""", 7).Values);
        Assert.Equal([3, 6, 9], Curve("""{ "perRank": 3 }""", 3).Values);
    }

    [Fact]
    public void A_table_curve_is_read_as_written_and_a_number_is_constant()
    {
        Assert.Equal([0, 1, 1, 2, 2], Curve("[0, 1, 1, 2, 2]", 5).Values);
        Assert.Equal([4, 4, 4], Curve("4", 3).Values);
    }

    [Fact]
    public void Past_max_rank_a_curve_continues_its_last_step()
    {
        Assert.Equal(80, Curve("""{ "from": 20, "to": 60 }""", 5).At(7));   // bonus ranks past max (A.1)
        Assert.Equal(3, Curve("[0, 1, 2]", 3).At(4));
        Assert.Equal(4, Curve("4", 5).At(8));
    }

    [Fact]
    public void A_table_curve_with_the_wrong_number_of_ranks_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Skills, t => Row(t, "patriot_missile")["manaCost"] = new JsonArray(4, 4, 4)));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.Malformed, "patriot_missile"), (e.Rule, e.Row));
        Assert.Contains("3 ranks", e.Message);
    }

    //-------------------------------------------------------------------------
    // Effects
    //-------------------------------------------------------------------------

    [Fact]
    public void An_unknown_primitive_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "patriot_missile")["effect"]!.AsArray()[0]!["primitive"] = "laser"));

        Assert.Contains(errors, e => e.Rule == ContentRule.Malformed && e.Row == "patriot_missile" && e.Message.Contains("'laser'"));
    }

    [Fact]
    public void A_parameter_its_primitive_does_not_take_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "patriot_missile")["effect"]!.AsArray()[0]!["drain"] = 2));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.Malformed, "patriot_missile"), (e.Rule, e.Row));
        Assert.Contains("'drain'", e.Message);
    }

    [Fact]
    public void A_missing_required_parameter_is_malformed()
    {
        var errors = Errors(Planted(ContentTables.Skills, t =>
        {
            Row(t, "patriot_missile")["effect"]!.AsArray()[0]!.AsObject().Remove("damage");
            Row(t, "patriot_missile").Remove("synergies");   // its synergy scales the damage removed
        }));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.Malformed, e.Rule);
        Assert.Contains("'damage' is required", e.Message);
    }

    [Fact]
    public void An_effect_stat_that_is_not_a_stat_is_an_unknown_stat()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "marksmanship")["effect"]!.AsArray()[0]!["stats"]!.AsArray()[0]!["stat"] = "damage_weapons_pct"));

        var e = Assert.Single(errors);
        Assert.Equal((ContentRule.UnknownStat, "marksmanship"), (e.Rule, e.Row));
    }

    [Fact]
    public void A_trigger_outside_the_vocabulary_is_out_of_range()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "marksmanship")["effect"]!.AsArray()[0]!["trigger"] = "on_sneeze"));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Contains("on_sneeze", e.Message);
    }

    [Fact]
    public void A_negative_effect_parameter_is_out_of_range()
    {
        var errors = Errors(Planted(ContentTables.Skills,
            t => Row(t, "patriot_missile")["effect"]!.AsArray()[0]!["radius"] = -1));

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Contains("radius", e.Message);
    }

    [Fact]
    public void Every_primitive_of_the_spec_is_in_the_vocabulary()
    {
        // plan_descent_logic RU6's list: projectile, hitscan, area burst, aura, buff, summon,
        // dash, heal, passive, charge, toggle with drain, triggered passive, threat.
        string[] names = ["projectile", "hitscan", "area_burst", "aura", "buff", "debuff", "summon", "dash", "heal",
            "passive", "charge", "toggle", "triggered", "threat"];

        Assert.Equal(names, Primitives.All().Select(p => p.Name));
        Assert.All(Primitives.All(), p => Assert.Equal(p.Primitive, Primitives.Find(p.Name)!.Primitive));
        Assert.Equal(Enum.GetValues<Primitive>().Length, Primitives.All().Count);
    }
}
