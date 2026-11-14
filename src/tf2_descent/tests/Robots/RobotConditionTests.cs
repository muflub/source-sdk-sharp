using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Rules.Skills;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// A skill's conditions on robots (robot_conditions.json, <see cref="RobotConditionRules"/>, HO8):
/// every word a skill or spell can put on a robot is in the table, the target words, the
/// knock-up, the debuff on what a robot deals; each load rule proven by a planted row.
/// </summary>
public sealed class RobotConditionTests
{
    private static readonly TF2DescentRules Rules = new();

    /// <summary>Words on an enemy-aimed effect that change the hero's hit rather than the robot: no robot condition.</summary>
    private static readonly string[] HitWords = ["melee_crit"];

    private static MemoryContentSource Planted(Action<JsonNode> mutate)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(ContentTables.RobotConditions, out var bytes));
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return source.Set(ContentTables.RobotConditions, Encoding.UTF8.GetBytes(node.ToJsonString()));
    }

    private static IReadOnlyList<ContentError> Errors(IContentSource source) => new ContentLoader().Load(source).Errors;

    private static JsonObject Row(JsonNode table, string id) =>
        table.AsArray().Select(n => n!.AsObject()).Single(o => (string?)o["id"] == id);

    [Fact]
    public void Every_word_a_skill_or_spell_aims_at_enemies_is_a_robot_condition()
    {
        var effects = Rules.Content.Skills.Rows.SelectMany(s => s.Effect).Concat(Rules.Content.Spells.Rows.SelectMany(s => s.Effect))
            .Where(e => e.Primitive is Primitive.Debuff or Primitive.AreaBurst || e is { Primitive: Primitive.Triggered, Target: "enemies" });
        var words = effects.SelectMany(e => e.Conditions).Except(HitWords).Distinct().ToList();

        Assert.Contains("emp_stun", words);   // the stimulus: the scan reads the conditions
        Assert.Contains("knock_up", words);
        Assert.DoesNotContain(words, w => Rules.Conditions.Of(w) is null);
    }

    [Theory]
    [InlineData("emp_stun", "stun")]
    [InlineData("stun", "stun")]
    [InlineData("slowed", "slow")]
    [InlineData("mini_crit_marked", "marked")]
    [InlineData("burn", "burn")]
    [InlineData("bleed", "bleed")]
    public void A_word_names_its_status(string word, string status) =>
        Assert.Equal(status, Rules.Conditions.Of(word)!.Status);

    [Fact]
    public void A_word_the_table_does_not_name_does_nothing() => Assert.Null(Rules.Conditions.Of("crit_boost"));

    [Theory]
    [InlineData("enemies", false, true)]
    [InlineData("enemies", true, true)]
    [InlineData("enemy_minions", false, true)]
    [InlineData("enemy_minions", true, false)]
    [InlineData("enemy_bosses", false, false)]
    [InlineData("enemy_bosses", true, true)]
    [InlineData("party", false, false)]
    public void A_target_word_is_for_the_robots_it_names(string target, bool boss, bool admitted) =>
        Assert.Equal(admitted, RobotConditionRules.Admits(target, boss));

    [Fact]
    public void A_knock_up_throws_straight_up_less_the_robots_knockback_resistance()
    {
        var row = Rules.Conditions.Of("knock_up")!;

        Assert.Equal(new Vec3(0, 0, 450), RobotConditionRules.KnockUp(row, 0));
        Assert.Equal(new Vec3(0, 0, 315), RobotConditionRules.KnockUp(row, 30));
    }

    [Fact]
    public void A_debuffs_outgoing_share_is_its_damage_all_pct()
    {
        var maggots = Rules.Skills.Skill("maggots").Effect.Single(e => e.Primitive == Primitive.Debuff);
        StatAmount[] atRank5 = [.. maggots.Stats.Select(c => new StatAmount(c.Stat, c.Amount.At(5)))];

        Assert.Equal(-30, RobotConditionRules.OutgoingPct(atRank5), 9);
        Assert.Equal(0, RobotConditionRules.OutgoingPct([new StatAmount("damage_blast_pct", 10)]));
    }

    [Fact]
    public void A_second_debuff_keeps_the_stronger_reduction_and_the_later_end()
    {
        Assert.Equal((-30.0, 500L), RobotConditionRules.Weaken(-10, 500, -30, 400, 100));
        Assert.Equal((-30.0, 600L), RobotConditionRules.Weaken(-30, 500, -10, 600, 100));
    }

    [Fact]
    public void A_debuff_that_has_ended_gives_way() =>
        Assert.Equal((-10.0, 900L), RobotConditionRules.Weaken(-30, 500, -10, 900, 500));

    [Fact]
    public void A_debuff_lowers_what_a_robot_deals_until_it_ends()
    {
        Assert.Equal(0.8, RobotConditionRules.OutgoingMultiplier(-20, 500, 499), 12);
        Assert.Equal(1.0, RobotConditionRules.OutgoingMultiplier(-20, 500, 500));
    }

    [Fact]
    public void The_shipped_table_loads_clean() => Assert.Empty(Errors(Planted(_ => { })));

    [Fact]
    public void A_condition_naming_no_status_is_a_dangling_reference()
    {
        var e = Assert.Single(Errors(Planted(t => Row(t, "slowed")["status"] = "frozen")));

        Assert.Equal((ContentRule.DanglingReference, ContentTables.RobotConditions, "slowed"), (e.Rule, e.Table, e.Row));
    }

    [Fact]
    public void A_condition_with_neither_a_status_nor_a_knock_up_is_malformed()
    {
        var e = Assert.Single(Errors(Planted(t => Row(t, "slowed").Remove("status"))));

        Assert.Equal((ContentRule.Malformed, "slowed"), (e.Rule, e.Row));
    }

    [Fact]
    public void A_condition_with_both_a_status_and_a_knock_up_is_malformed()
    {
        var e = Assert.Single(Errors(Planted(t => Row(t, "knock_up")["status"] = "stun")));

        Assert.Equal((ContentRule.Malformed, "knock_up"), (e.Rule, e.Row));
    }
}
