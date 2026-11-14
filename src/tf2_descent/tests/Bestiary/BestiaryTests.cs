using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Bestiary;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using Xunit;

namespace TF2.Descent.Tests.Bestiary;

/// <summary>
/// The bestiary (ruling L6, default E7; plan_descent_logic RU12): kills of a template reveal
/// its count at 1, its life at 15, its resistances and immunities at 30, its style at 60. A
/// view built below a threshold is asserted on its serialised bytes: the hidden field's name
/// and its value are absent. The robot's numbers are chosen to appear nowhere else.
/// </summary>
public sealed class BestiaryTests
{
    private static readonly TF2DescentRules Rules = new();
    private static BestiaryRules Bestiary => Rules.Bestiary;

    private static readonly RobotFacts Robot = new(
        "heavy_bot", 87654, 98765,
        ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
            [new KeyValuePair<string, double>("fire", -37), new KeyValuePair<string, double>("bullet", 61)]),
        "shock", "Fearless");

    private static HeroSheet Killed(int kills) =>
        Rules.Heroes.New("soldier", "t") with { Bestiary = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [new KeyValuePair<string, int>("heavy_bot", kills)]) };

    private static string Bytes(int kills) => Encoding.UTF8.GetString(BestiaryRules.Serialise(Bestiary.View(Killed(kills), Robot)));

    [Fact]
    public void The_shipped_thresholds_are_1_15_30_and_60()
    {
        Assert.Equal(new BestiarySettings(1, 15, 30, 60), Rules.Content.Bestiary);
    }

    [Theory]
    [InlineData(0, false, false, false, false)]
    [InlineData(1, true, false, false, false)]
    [InlineData(14, true, false, false, false)]
    [InlineData(15, true, true, false, false)]
    [InlineData(29, true, true, false, false)]
    [InlineData(30, true, true, true, false)]
    [InlineData(59, true, true, true, false)]
    [InlineData(60, true, true, true, true)]
    public void Reveal_shows_each_part_from_its_threshold(int kills, bool count, bool life, bool resists, bool style) =>
        Assert.Equal(new BestiaryReveal(count, life, resists, style), Bestiary.Reveal(kills));

    [Fact]
    public void Reveal_reads_the_kills_from_the_sheet()
    {
        Assert.Equal(Bestiary.Reveal(30), Bestiary.Reveal(Killed(30), "heavy_bot"));
        Assert.Equal(BestiaryReveal.Nothing, Bestiary.Reveal(Killed(30), "scout_bot"));
    }

    [Fact]
    public void A_kill_adds_one_to_the_templates_count_in_the_sheet()
    {
        var sheet = BestiaryRules.RecordKill(BestiaryRules.RecordKill(Killed(14), "heavy_bot"), "scout_bot");

        Assert.Equal(15, BestiaryRules.Kills(sheet, "heavy_bot"));
        Assert.Equal(1, BestiaryRules.Kills(sheet, "scout_bot"));
    }

    [Fact]
    public void With_no_kills_the_bytes_carry_the_template_alone()
    {
        Assert.Equal("{\"template\":\"heavy_bot\"}", Bytes(0));
    }

    [Fact]
    public void Below_15_kills_the_bytes_carry_the_count_but_no_life()
    {
        var json = Bytes(14);

        Assert.Contains("\"kills\":14", json);
        Assert.DoesNotContain("life", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("87654", json);
        Assert.DoesNotContain("98765", json);
    }

    [Fact]
    public void Below_30_kills_the_bytes_carry_life_but_no_resistances_or_immunity()
    {
        var json = Bytes(29);

        Assert.Contains("\"maxLife\":98765", json);
        Assert.DoesNotContain("resist", json);
        Assert.DoesNotContain("immune", json);
        Assert.DoesNotContain("shock", json);
        Assert.DoesNotContain("-37", json);
    }

    [Fact]
    public void Below_60_kills_the_bytes_carry_resistances_but_no_style()
    {
        var json = Bytes(59);

        Assert.Contains("\"immune\":\"shock\"", json);
        Assert.Contains("\"fire\":-37", json);
        Assert.DoesNotContain("style", json);
        Assert.DoesNotContain("Fearless", json);
    }

    [Fact]
    public void At_60_kills_the_bytes_carry_everything()
    {
        Assert.Equal(
            "{\"template\":\"heavy_bot\",\"kills\":60,\"life\":87654,\"maxLife\":98765,\"resist\":{\"bullet\":61,\"fire\":-37},\"immune\":\"shock\",\"style\":\"Fearless\"}",
            Bytes(60));
    }

    [Fact]
    public void Thresholds_out_of_order_are_a_load_error()
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(ContentTables.Bestiary, out var bytes));
        var node = JsonNode.Parse(bytes)!;
        node["revealAt"]!["style"] = 20;   // below resistances' 30

        var errors = new ContentLoader().Load(source.Set(ContentTables.Bestiary, Encoding.UTF8.GetBytes(node.ToJsonString()))).Errors;

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Bestiary, ContentRule.OutOfRange), (e.Table, e.Rule));
    }
}
