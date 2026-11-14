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

        var ordinary = Content.Robots.Rows.Where(r => r.Size == "ordinary").ToList();

        Assert.Equal(classes.Order(), ordinary.Select(r => r.Class).Order());
        Assert.All(ordinary, r => Assert.Equal((0.85, 1.15), (r.ScaleMin, r.ScaleMax)));
    }

    [Fact]
    public void Giants_are_the_classes_mvm_has_giants_of()
    {
        var giants = Content.Robots.Rows.Where(r => r.Size == "giant").Select(r => r.Class).Order();

        Assert.Equal(["demoman", "heavy", "medic", "pyro", "scout", "soldier"], giants);
        Assert.All(Content.Robots.Rows.Where(r => r.Size == "giant"), r => Assert.Equal((1.5, 1.9), (r.ScaleMin, r.ScaleMax)));
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
