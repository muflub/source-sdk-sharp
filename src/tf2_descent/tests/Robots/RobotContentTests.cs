using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// The robot tables (plan_descent_logic RU7; plan_tf2_rpg 2f, D13): the shipped templates,
/// the depth table behind the host's LevelRow, and a load error for each rule a template
/// must keep, planted in a copy of the shipped tables.
/// </summary>
public sealed class RobotContentTests
{
    private static readonly TF2DescentRules Rules = new();
    private static DescentContent Content => Rules.Content;

    [Fact]
    public void The_nine_classes_are_ordinary_robots()
    {
        string[] classes = ["scout", "soldier", "pyro", "demoman", "heavy", "engineer", "medic", "sniper", "spy"];

        var ordinary = Content.Robots.Rows.Where(r => r.Size == "ordinary" && r.Class != "tank").ToList();   // HO13: the Tank is a machine of its own

        Assert.Equal(classes.Order(), ordinary.Select(r => r.Class).Order());
        Assert.All(ordinary, r => Assert.Equal((0.85, 1.15), (r.ScaleMin, r.ScaleMax)));
    }

    /// <summary>
    /// The user's ruling (2026-11-22, plan L26): a class with no giant model (the engineer, medic,
    /// sniper and spy) has a giant all the same, wearing its ordinary model scaled up.
    /// </summary>
    [Fact]
    public void Every_class_has_one_giant()
    {
        Assert.Equal(["demoman", "engineer", "heavy", "medic", "pyro", "scout", "sniper", "soldier", "spy"], Giants().Select(r => r.Class).Order());
    }

    [Fact]
    public void Giants_roll_their_scale_from_the_giant_range()
    {
        Assert.All(Giants(), r => Assert.Equal((1.5, 1.9), (r.ScaleMin, r.ScaleMax)));
    }

    [Fact]
    public void A_giant_fights_with_its_classs_ordinary_weapon()
    {
        Assert.All(Giants(), g => Assert.Equal((Ordinary(g).Family, Ordinary(g).Attack), (g.Family, g.Attack)));
    }

    [Fact]
    public void A_giant_has_twice_its_ordinary_robots_life_half_again_its_damage_and_its_speed()
    {
        Assert.All(Giants(), g => Assert.Equal((Ordinary(g).Life * 2, Ordinary(g).Damage * 1.5, Ordinary(g).Speed, Ordinary(g).Reach),
                                               (g.Life, g.Damage, g.Speed, g.Reach)));
    }

    /// <summary>The six giants before the ruling each paid 1.775 times their ordinary robot's XP, rounded half away (44, 71, 62, 62, 107, 62).</summary>
    [Fact]
    public void A_giant_pays_its_ordinary_robots_xp_times_1_775()
    {
        Assert.All(Giants(), g => Assert.Equal(Math.Round(Ordinary(g).Xp * 1.775, MidpointRounding.AwayFromZero), g.Xp));
    }

    [Fact]
    public void A_giant_keeps_its_ordinary_robots_resists_but_shock_hurts_it_half_again()
    {
        Assert.All(Giants(), g =>
        {
            var expected = new Dictionary<string, double>(Ordinary(g).Resist) { ["shock"] = -50 };
            Assert.Equal(expected.OrderBy(kv => kv.Key), g.Resist.OrderBy(kv => kv.Key));
        });
    }

    [Fact]
    public void A_giant_has_its_ordinary_robots_temperament_and_is_a_miniboss()
    {
        Assert.All(Giants(), g =>
        {
            Assert.Equal(Ordinary(g).Weights.OrderBy(kv => kv.Key), g.Weights.OrderBy(kv => kv.Key));
            Assert.Equal(Ordinary(g).Caps.OrderBy(kv => kv.Key), g.Caps.OrderBy(kv => kv.Key));
            Assert.Equal(["miniboss"], g.Flags);
        });
    }

    /// <summary>The giants with no giant model of their own join the packs where the last of the others (the giant medic) does: depth 10 and deeper.</summary>
    [Theory]
    [InlineData("giant_engineer")]
    [InlineData("giant_sniper")]
    [InlineData("giant_spy")]
    public void A_scaled_up_giant_is_drawn_from_depth_10(string id)
    {
        var drawnAt = Content.Levels.Rows.Where(l => l.Templates.Any(t => t.Template == id && t.Weight > 0)).Select(l => l.Depth);

        Assert.Equal(Enumerable.Range(10, 6), drawnAt);
    }

    [Fact]
    public void The_sentry_buster_is_a_giant_of_stocks_scale_that_never_takes_cover()
    {
        var buster = Content.Robots["sentry_buster"];

        Assert.Equal(("giant", 1.75, 1.75), (buster.Size, buster.ScaleMin, buster.ScaleMax));
        Assert.Equal(0, buster.Cap("cover", 10));
    }

    [Fact]
    public void Runts_roll_their_scale_from_the_runt_range()
    {
        var runts = Content.Robots.Rows.Where(r => r.Size == "runt").ToList();

        Assert.NotEmpty(runts);
        Assert.All(runts, r => Assert.Equal((0.6, 0.75), (r.ScaleMin, r.ScaleMax)));
    }

    [Fact]
    public void The_three_bosses_have_fixed_scales_and_authored_temperaments()
    {
        var bosses = Content.Robots.Rows.Where(r => r.Boss).ToList();

        Assert.Equal(["captain_punch", "major_crits", "sir_nukesalot"], bosses.Select(b => b.Id).Order());
        Assert.All(bosses, b => Assert.Equal(b.ScaleMin, b.ScaleMax));
    }

    [Fact]
    public void Every_template_names_a_shared_weapon_family()
    {
        Assert.All(Content.Robots.Rows, r => Assert.True(Content.Families.Contains(r.Family), r.Id));
    }

    [Fact]
    public void There_are_fifteen_depths_and_monster_level_rises_with_each()
    {
        var levels = Content.Levels.Rows;

        Assert.Equal(15, levels.Count);
        Assert.Equal(Enumerable.Range(1, 15), levels.Select(l => l.Depth));
        for (int d = 2; d <= 15; d++)
            Assert.True(Rules.Robots.MonsterLevel(d) > Rules.Robots.MonsterLevel(d - 1));
    }

    [Theory]
    [InlineData(1, 1, "act1", 1)]
    [InlineData(5, 9, "act1", 9)]
    [InlineData(6, 11, "act2", 11)]
    [InlineData(15, 29, "act3", 29)]
    public void The_hosts_level_row_reads_the_levels_table(int depth, int itemLevel, string lootTable, int monsterLevel)
    {
        var row = Rules.LevelTable(depth);

        Assert.Equal((depth, itemLevel, lootTable, monsterLevel), (row.Depth, row.ItemLevel, row.LootTable, row.MonsterLevel));
    }

    [Fact]
    public void A_depth_past_the_deepest_reads_the_deepest_row()
    {
        var row = Rules.LevelTable(40);

        Assert.Equal(40, row.Depth);
        Assert.Equal(Rules.LevelTable(15).MonsterLevel, row.MonsterLevel);
    }

    //-------------------------------------------------------------------------
    // Load errors
    //-------------------------------------------------------------------------

    [Fact]
    public void A_template_of_an_unknown_family_is_a_load_error()
    {
        var e = Assert.Single(Errors(ContentTables.Robots, n => Row(n, "scout")["family"] = "laser"));
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
    }

    [Theory]
    [InlineData(-51)]
    [InlineData(76)]
    public void A_template_resist_outside_minus_50_to_75_is_a_load_error(double resist)
    {
        var e = Assert.Single(Errors(ContentTables.Robots, n => Row(n, "soldier")["resist"]!["blast"] = resist));
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
    }

    [Fact]
    public void A_scale_outside_its_size_class_is_a_load_error()
    {
        var e = Assert.Single(Errors(ContentTables.Robots, n => Row(n, "runt_scout")["scaleMax"] = 0.8));
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
    }

    [Fact]
    public void A_bosss_authored_temperament_that_does_not_spend_its_budget_is_a_load_error()
    {
        var e = Assert.Single(Errors(ContentTables.Robots, n => Row(n, "sir_nukesalot")["fixedTemperament"]![0]!["level"] = 9));
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
    }

    [Fact]
    public void Caps_that_hold_fewer_points_than_the_largest_scale_buys_are_a_load_error()
    {
        var errors = Errors(ContentTables.Robots, n =>
        {
            var caps = new JsonObject();
            foreach (var s in new[] { "aggression", "accuracy", "cover", "soloness", "alertness", "tenacity", "discipline", "agility", "cunning", "nerve" })
                caps[s] = 4;   // 40 points of room; a scale of 1.15 buys 46
            Row(n, "scout")["caps"] = caps;
        });

        Assert.Equal(ContentRule.OutOfRange, Assert.Single(errors).Rule);
    }

    [Fact]
    public void A_flag_mvm_does_not_have_is_a_load_error()
    {
        var e = Assert.Single(Errors(ContentTables.Robots, n => Row(n, "heavy")["flags"] = new JsonArray("always_happy")));
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
    }

    [Fact]
    public void A_boss_in_a_depths_packs_is_a_load_error()
    {
        var e = Assert.Single(Errors(ContentTables.Levels, n => n[4]!["templates"]!["sir_nukesalot"] = 1));
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
    }

    [Fact]
    public void Depths_out_of_order_are_a_load_error()
    {
        var errors = Errors(ContentTables.Levels, n => n[3]!["depth"] = 9);
        Assert.Contains(errors, e => e.Rule == ContentRule.OutOfRange && e.Row == "depth4");
    }

    [Fact]
    public void A_size_whose_largest_scale_buys_more_than_100_points_is_a_load_error()
    {
        var errors = Errors(ContentTables.RobotRules, n => n["sizes"]![3]!["scaleMax"] = 2.6);
        Assert.Contains(errors, e => e.Rule == ContentRule.OutOfRange && e.Row == "boss");
    }

    //-------------------------------------------------------------------------

    private static IEnumerable<RobotTemplate> Giants() => Content.Robots.Rows.Where(r => r.Size == "giant" && r.Attack != "sentry_buster");

    private static RobotTemplate Ordinary(RobotTemplate giant) => Content.Robots.Rows.Single(r => r.Size == "ordinary" && r.Class == giant.Class);

    private static JsonObject Row(JsonNode table, string id) =>
        table.AsArray().Select(n => n!.AsObject()).Single(o => (string?)o["id"] == id);

    internal static IReadOnlyList<ContentError> Errors(string table, Action<JsonNode> mutate)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(table, out var bytes));
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return new ContentLoader().Load(source.Set(table, Encoding.UTF8.GetBytes(node.ToJsonString()))).Errors;
    }

    internal static DescentContent Planted(string table, Action<JsonNode> mutate)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(table, out var bytes));
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return new ContentLoader().Load(source.Set(table, Encoding.UTF8.GetBytes(node.ToJsonString()))).OrThrow();
    }
}
