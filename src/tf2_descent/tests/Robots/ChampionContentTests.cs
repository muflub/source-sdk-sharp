using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// champions.json's behaviours (plan_tf2_rpg 2f, HO12): every affix names a behaviour the horde
/// carries out, with exactly the numbers that behaviour reads, each in its bounds.
/// </summary>
public sealed class ChampionContentTests
{
    private static readonly TF2DescentRules Rules = new();

    private static IReadOnlyList<ContentError> Errors(Action<JsonNode> mutate) => RobotContentTests.Errors(ContentTables.Champions, mutate);

    private static JsonObject Row(JsonNode n, string id) => n.AsArray().Select(r => r!.AsObject()).Single(r => (string)r["id"]! == id);

    [Fact]
    public void Every_affix_names_a_behaviour_the_horde_carries_out_with_its_numbers()
    {
        Assert.All(Rules.Content.Champions.Rows, a =>
        {
            Assert.True(ChampionBehaviours.Numbers.TryGetValue(a.Behaviour, out var numbers), a.Id);
            Assert.Equal(numbers!.Select(n => n.Name).Order(), a.Params.Keys.Order());
        });
    }

    [Fact]
    public void An_affix_whose_behaviour_the_horde_does_not_know_is_refused()
    {
        var errors = Errors(n => Row(n, "teleporting")["behaviour"] = "blink");

        Assert.Contains(errors, e => e.Row == "teleporting" && e.Rule == ContentRule.DanglingReference && e.Message.Contains("'blink'"));
    }

    [Fact]
    public void An_affix_missing_a_number_its_behaviour_reads_is_refused()
    {
        var errors = Errors(n => Row(n, "uber_low_health")["params"]!.AsObject().Remove("seconds"));

        Assert.Contains(errors, e => e.Row == "uber_low_health" && e.Message.Contains("params.seconds"));
    }

    [Fact]
    public void A_number_its_behaviour_does_not_read_is_refused()
    {
        var errors = Errors(n => Row(n, "extra_fast")["params"]!.AsObject()["radius"] = 5);

        Assert.Contains(errors, e => e.Row == "extra_fast" && e.Message.Contains("'params.radius'"));
    }

    [Fact]
    public void A_number_out_of_its_bounds_is_refused()
    {
        var errors = Errors(n => Row(n, "multishot")["params"]!.AsObject()["shots"] = 1);

        Assert.Contains(errors, e => e.Row == "multishot" && e.Rule == ContentRule.OutOfRange && e.Message.Contains("params.shots"));
    }

    [Fact]
    public void An_immunity_on_a_behaviour_that_is_not_immune_is_refused()
    {
        var errors = Errors(n => Row(n, "giant")["immunity"] = "fire");

        Assert.Contains(errors, e => e.Row == "giant" && e.Message.Contains("immune behaviour"));
    }

    [Fact]
    public void A_behaviours_number_is_read_by_name()
    {
        var uber = Rules.Content.Champions["uber_low_health"];

        Assert.Equal(30.0, uber.Param("belowLifePct"), 9);
        Assert.Throws<KeyNotFoundException>(() => uber.Param("radius"));
    }
}
