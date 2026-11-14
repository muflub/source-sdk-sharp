using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.World;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.World;

/// <summary>
/// hazards.json and the hazards' pure rules (HE10; mine plan D8): lava burns as fire, slime is
/// unresisted until D8 rules it, each bites at the cadence by the depth's damage per second.
/// </summary>
public sealed class HazardContentTests
{
    private static readonly TF2DescentRules Rules = new();

    private static HazardSettings S => Rules.Content.Hazards;

    private static ContentError One(IContentSource source) => Assert.Single(Errors(source));

    private static MemoryContentSource Hazards(Action<JsonObject> mutate) => Planted(HazardTable.TableName, n => mutate(n.AsObject()));

    private static JsonObject Kind(JsonObject table, string id) => Row(table["kinds"]!, id);

    [Fact]
    public void The_shipped_hazards_load_with_no_error()
    {
        Assert.Empty(Errors(ShippedCopy()));
        Assert.NotEmpty(S.Kinds);   // the stimulus: the table was read
    }

    [Fact]
    public void Lava_burns_as_fire_and_hurts_robots()
    {
        var lava = S.Kind("lava")!;

        Assert.Equal("fire", lava.DamageType);
        Assert.True(lava.Robots);
    }

    [Fact]
    public void Slime_is_unresisted_and_leaves_robots_alone_until_d8_rules_it()
    {
        var slime = S.Kind("slime")!;

        Assert.Equal(HazardRules.Unresisted, slime.DamageType);
        Assert.False(slime.Robots);
    }

    [Fact]
    public void A_bite_is_the_depths_damage_per_second_times_the_cadence()
    {
        var lava = S.Kind("lava")!;

        Assert.Equal(lava.PerSecond * S.Cadence, HazardRules.Bite(S, lava, 1), 9);
        Assert.Equal((lava.PerSecond + 12 * lava.PerDepth) * S.Cadence, HazardRules.Bite(S, lava, 13), 9);
        Assert.Equal(HazardRules.Bite(S, lava, 1), HazardRules.Bite(S, lava, 0), 9);   // the town bites as depth 1
    }

    [Fact]
    public void A_victim_is_bitten_at_once_then_each_cadence()
    {
        Assert.True(HazardRules.Due(S, null, 10.0));
        Assert.False(HazardRules.Due(S, 10.0, 10.0 + S.Cadence / 2));
        Assert.True(HazardRules.Due(S, 10.0, 10.0 + S.Cadence));
    }

    [Fact]
    public void A_bite_loses_the_victims_resistance_share()
    {
        Assert.Equal(15.0, HazardRules.AfterResist(20, 0.25), 9);
        Assert.Equal(20.0, HazardRules.AfterResist(20, 0), 9);
    }

    [Theory]
    [InlineData("tf2_descent_hazard_lava", "lava")]
    [InlineData("tf2_descent_hazard_slime", "slime")]
    [InlineData("tf2_descent_hazard_", null)]
    [InlineData("trigger_lava", null)]
    public void A_volumes_name_names_its_kind(string name, string? kind) => Assert.Equal(kind, HazardRules.KindOfVolume(name));

    [Fact]
    public void A_damage_type_that_is_no_damage_type_is_a_dangling_reference()
    {
        var error = One(Hazards(n => Kind(n, "lava")["damageType"] = "ice"));

        Assert.Equal(ContentRule.DanglingReference, error.Rule);
        Assert.Equal("lava", error.Row);
    }

    [Fact]
    public void An_unresisted_kind_that_hurts_robots_is_out_of_range()
    {
        var error = One(Hazards(n => Kind(n, "slime")["robots"] = true));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Equal("slime", error.Row);
    }

    [Fact]
    public void A_kind_named_twice_is_a_duplicate()
    {
        var error = One(Hazards(n => Kind(n, "slime")["id"] = "lava"));

        Assert.Equal(ContentRule.DuplicateId, error.Rule);
    }

    [Fact]
    public void A_kind_whose_id_is_not_one_word_is_out_of_range()
    {
        var error = One(Hazards(n => Kind(n, "slime")["id"] = "toxic_slime"));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
    }

    [Fact]
    public void A_cadence_of_nothing_is_out_of_range()
    {
        var error = One(Hazards(n => n["cadence"] = 0));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("cadence", error.Message);
    }

    [Fact]
    public void A_negative_damage_is_out_of_range()
    {
        var error = One(Hazards(n => Kind(n, "lava")["perSecond"] = -1));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
    }
}
