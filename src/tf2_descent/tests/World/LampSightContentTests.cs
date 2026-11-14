using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.World;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.World;

/// <summary>
/// lamp.json's <c>sight</c> (this lane; mine plan M9 and section 6; plan_tf2_rpg B.3): a darkness
/// a depth, lit to depth 10 and black at 15, and what the lamp lit or out makes of a robot's range
/// for its hero in the dark. Lit, the lamp changes nothing: the scale is exactly 1.
/// </summary>
public sealed class LampSightContentTests
{
    private static readonly TF2DescentRules Rules = new();

    private static LampSettings S => Rules.Content.Lamp;

    private static ContentError One(IContentSource source) => Assert.Single(Errors(source));

    private static MemoryContentSource Lamp(Action<JsonObject> mutate) => Planted(LampTable.TableName, n => mutate(n.AsObject()));

    [Fact]
    public void The_first_ten_depths_and_the_town_are_lit()
    {
        Assert.Equal(0.0, LampRules.Darkness(S, 0));
        Assert.All(Enumerable.Range(1, 10), d => Assert.Equal(0.0, LampRules.Darkness(S, d)));
    }

    [Fact]
    public void From_depth_eleven_down_it_darkens_to_black_at_fifteen()
    {
        double[] deep = [.. Enumerable.Range(11, 5).Select(d => LampRules.Darkness(S, d))];

        Assert.All(deep, d => Assert.True(d > 0));
        Assert.Equal(deep.Order(), deep);
        Assert.Equal(1.0, deep[^1]);
        Assert.Equal(1.0, LampRules.Darkness(S, 40));   // past the table: its last
    }

    [Fact]
    public void Lit_the_scale_is_exactly_one_lamp_on_or_off()
    {
        Assert.Equal(1.0, LampRules.SightScale(S, 0, lit: true));
        Assert.Equal(1.0, LampRules.SightScale(S, 0, lit: false));
    }

    [Fact]
    public void In_the_black_the_lamp_lit_is_a_robots_own_range_and_out_is_the_contents_ratio()
    {
        Assert.Equal(S.Sight.LampOn, LampRules.SightScale(S, 1, lit: true));
        Assert.Equal(S.Sight.LampOff, LampRules.SightScale(S, 1, lit: false));
        Assert.True(S.Sight.LampOn >= 1.0, "lamp on: seen from the normal range or farther");
        Assert.True(S.Sight.LampOff < 1.0, "lamp off: seen only from nearer");
    }

    [Fact]
    public void Half_dark_eases_the_lamps_ratio_halfway()
    {
        Assert.Equal(1 + 0.5 * (S.Sight.LampOff - 1), LampRules.SightScale(S, 0.5, lit: false), 12);
    }

    [Fact]
    public void A_darkness_past_black_or_none_is_held_to_its_ends()
    {
        Assert.Equal(LampRules.SightScale(S, 1, lit: false), LampRules.SightScale(S, 7, lit: false));
        Assert.Equal(1.0, LampRules.SightScale(S, double.NaN, lit: false));
    }

    [Fact]
    public void A_darkness_past_one_is_out_of_range()
    {
        var error = One(Lamp(n => n["sight"]!["darkness"]![3] = 1.5));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("sight.darkness[3]", error.Message);
    }

    [Fact]
    public void A_darkness_that_is_not_a_number_is_malformed()
    {
        var error = One(Lamp(n => n["sight"]!["darkness"]![0] = "dim"));

        Assert.Equal(ContentRule.Malformed, error.Rule);
    }

    [Fact]
    public void A_lamp_ratio_of_nothing_is_out_of_range()
    {
        var error = One(Lamp(n => n["sight"]!["lampOff"] = 0));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("lampOff", error.Message);
    }

    [Fact]
    public void A_table_of_no_depth_is_out_of_range()
    {
        var error = One(Lamp(n => n["sight"]!["darkness"] = new JsonArray()));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("sight.darkness", error.Message);
    }
}
