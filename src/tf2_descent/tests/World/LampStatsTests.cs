using System.Text.Json.Nodes;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.World;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.World;

/// <summary>
/// The miner's lamp's two hero stats (the user, 2026-11-13: "brightness increase and range
/// increase from skills and itmes"): lamp range and lamp brightness are bonuses in percent over
/// lamp.json's light, summed from contributions like every other stat and held between lamp.json's
/// floor and ceiling; a source that does not count gives nothing. Each validation rule of the new
/// lamp.json fields by a bad value planted in a copy of the shipped tables.
/// </summary>
public sealed class LampStatsTests
{
    private static readonly TF2DescentRules Rules = new();

    private static LampSettings Lamp => Rules.Content.Lamp;

    private static HeroStats Stats(params Contribution[] contributions) => Rules.Heroes.Stats(Soldier(Rules), contributions);

    private static Contribution Range(double amount, SourceState state = SourceState.Active) =>
        new(Lamp.Range.Stat, amount, new ContributionSource(SourceKind.Item, "hat", state));

    private static Contribution Brightness(double amount, SourceState state = SourceState.Active) =>
        new(Lamp.Brightness.Stat, amount, new ContributionSource(SourceKind.Skill, "lamplighter", state));

    private static ContentError One(IContentSource source) => Assert.Single(Errors(source));

    private static MemoryContentSource PlantedLamp(Action<JsonObject> mutate) => Planted(LampTable.TableName, n => mutate(n.AsObject()));

    [Fact]
    public void A_bare_hero_has_lamp_jsons_lamp()
    {
        var stats = Stats();

        Assert.Equal(Lamp.Light.Radius, stats.LampRadius);
        Assert.Equal(1, stats.LampBrightness);
        Assert.Equal((0.0, 0.0), (stats.LampRangeBonus, stats.LampBrightnessBonus));
    }

    [Fact]
    public void Range_is_a_percent_over_lamp_jsons_radius_summed_from_every_source()
    {
        var stats = Stats(Range(12), Range(8));

        Assert.Equal(20, stats.LampRangeBonus, 9);
        Assert.Equal(Lamp.Light.Radius * 1.2, stats.LampRadius, 9);
    }

    [Fact]
    public void Brightness_is_a_percent_over_lamp_jsons_light()
    {
        var stats = Stats(Brightness(30));

        Assert.Equal(1.3, stats.LampBrightness, 9);
        Assert.Equal(Lamp.Light.Radius, stats.LampRadius);   // brightness reaches no further
    }

    [Fact]
    public void Range_stops_at_lamp_jsons_ceiling()
    {
        var stats = Stats(Range(400));

        Assert.True(Lamp.Range.Max < 400);   // the stimulus is past the ceiling
        Assert.Equal(Lamp.Range.Max, stats.LampRangeBonus);
        Assert.Equal(Lamp.Light.Radius * (1 + Lamp.Range.Max / 100), stats.LampRadius, 9);
    }

    [Fact]
    public void Range_stops_at_lamp_jsons_floor()
    {
        var stats = Stats(Range(-400));

        Assert.True(Lamp.Range.Min > -400);
        Assert.Equal(Lamp.Light.Radius * (1 + Lamp.Range.Min / 100), stats.LampRadius, 9);
        Assert.True(stats.LampRadius > 0);
    }

    [Fact]
    public void Brightness_stops_at_lamp_jsons_ceiling_and_floor()
    {
        Assert.Equal(1 + Lamp.Brightness.Max / 100, Stats(Brightness(900)).LampBrightness, 9);
        Assert.Equal(1 + Lamp.Brightness.Min / 100, Stats(Brightness(-900)).LampBrightness, 9);
        Assert.True(Stats(Brightness(-900)).LampBrightness > 0);
    }

    [Fact]
    public void A_broken_or_inactive_source_gives_the_lamp_nothing()
    {
        var stats = Stats(Range(20, SourceState.Broken), Brightness(30, SourceState.Inactive));

        Assert.Equal(Lamp.Light.Radius, stats.LampRadius);
        Assert.Equal(1, stats.LampBrightness);
        Assert.True(Stats(Range(20)).LampRadius > Lamp.Light.Radius);   // the same source, counting, does
    }

    [Fact]
    public void A_stat_says_what_its_total_comes_from()
    {
        var hat = Range(12);
        var skill = Range(8) with { Source = new ContributionSource(SourceKind.Skill, "lamplighter") };
        var broken = Range(5, SourceState.Broken);

        Assert.Equal([hat, skill], Stats(hat, broken, skill, Brightness(30)).From(Lamp.Range.Stat));
    }

    [Fact]
    public void The_shipped_lamp_stats_name_stats_and_let_a_bare_hero_keep_the_base()
    {
        Assert.True(Rules.Content.Stats.TryGet(Lamp.Range.Stat, out _));
        Assert.True(Rules.Content.Stats.TryGet(Lamp.Brightness.Stat, out _));
        Assert.InRange(0, Lamp.Range.Min, Lamp.Range.Max);
        Assert.InRange(0, Lamp.Brightness.Min, Lamp.Brightness.Max);
        Assert.True(Lamp.Range.Max > 0 && Lamp.Brightness.Max > 0);   // something can raise each
    }

    [Fact]
    public void A_range_ceiling_that_lights_past_the_lights_own_range_is_out_of_range()
    {
        var error = One(PlantedLamp(n => n["range"]!["max"] = 800));   // 480 x 9 = 4,320 > 4,096

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("range.max", error.Message);
    }

    [Fact]
    public void A_floor_that_puts_the_lamp_out_is_out_of_range()
    {
        var error = One(PlantedLamp(n => n["brightness"]!["min"] = -100));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("brightness.min", error.Message);
    }

    [Fact]
    public void A_floor_above_nothing_is_out_of_range_as_a_bare_hero_must_keep_the_base()
    {
        var error = One(PlantedLamp(n => n["range"]!["min"] = 10));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("range.min", error.Message);
    }

    [Fact]
    public void A_brightness_ceiling_past_what_the_wire_carries_is_out_of_range()
    {
        var error = One(PlantedLamp(n => n["brightness"]!["max"] = 350));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("brightness.max", error.Message);
    }

    [Fact]
    public void A_lamp_stat_that_is_no_stat_is_an_unknown_stat()
    {
        var error = One(PlantedLamp(n => n["brightness"]!["stat"] = "lamp_glow"));

        Assert.Equal(ContentRule.UnknownStat, error.Rule);
    }
}
