using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.World;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.World;

/// <summary>
/// lamp.json (HE10; mine plan M9, D7): the light's numbers in content, the depth the lamp is lit
/// from on arrival, the step a hero's look is sent in. Each validation rule by a bad value planted
/// in a copy of the shipped tables, against the shipped tables loading clean.
/// </summary>
public sealed class LampContentTests
{
    private static readonly TF2DescentRules Rules = new();

    private static ContentError One(IContentSource source) => Assert.Single(Errors(source));

    private static MemoryContentSource Lamp(Action<JsonObject> mutate) => Planted(LampTable.TableName, n => mutate(n.AsObject()));

    [Fact]
    public void The_shipped_lamp_loads_with_no_error()
    {
        Assert.Empty(Errors(ShippedCopy()));
        Assert.True(Rules.Content.Lamp.Light.Radius > 0);   // the stimulus: the table was read
    }

    [Fact]
    public void The_lamp_is_a_warm_cone_lit_on_every_level_and_not_in_the_town()
    {
        var s = Rules.Content.Lamp;

        Assert.True(s.Light.InnerAngle < s.Light.OuterAngle);
        Assert.True(s.Light.Red >= s.Light.Green && s.Light.Green >= s.Light.Blue);   // warm: red over green over blue
        Assert.False(LampRules.OnByDefault(s, 0));                                    // the town is daylight
        Assert.True(LampRules.OnByDefault(s, 1));                                     // D7: on for every hero in a level
        Assert.True(LampRules.OnByDefault(s, 15));
    }

    [Fact]
    public void An_inner_cone_wider_than_the_outer_is_out_of_range()
    {
        var error = One(Lamp(n => n["light"]!["innerAngle"] = 60));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("innerAngle", error.Message);
    }

    [Fact]
    public void A_colour_channel_past_255_is_out_of_range()
    {
        var error = One(Lamp(n => n["light"]!["colour"]!["g"] = 300));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("colour.g", error.Message);
    }

    [Fact]
    public void A_radius_of_nothing_is_out_of_range()
    {
        var error = One(Lamp(n => n["light"]!["radius"] = 0));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("radius", error.Message);
    }

    [Fact]
    public void A_pitch_step_finer_than_the_wire_holds_is_out_of_range()
    {
        var error = One(Lamp(n => n["pitchStep"] = 1));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Contains("pitchStep", error.Message);
    }

    [Fact]
    public void A_field_nobody_reads_is_malformed()
    {
        var error = One(Lamp(n => n["flicker"] = 0.2));

        Assert.Equal(ContentRule.Malformed, error.Rule);
    }

    [Fact]
    public void A_pitch_goes_to_the_wire_in_steps_and_comes_back_to_within_half_a_step()
    {
        var s = Rules.Content.Lamp;

        foreach (double pitch in new[] { -90.0, -47.3, -1.4, 0, 12.6, 33.3, 89.9, 90 })
            Assert.InRange(LampRules.PitchOf(s, LampRules.PitchSteps(s, pitch)), pitch - s.PitchStep / 2, pitch + s.PitchStep / 2);
    }

    [Fact]
    public void A_pitch_past_straight_up_or_down_is_held_there()
    {
        var s = Rules.Content.Lamp;

        Assert.Equal(0, LampRules.PitchSteps(s, -170));
        Assert.Equal(LampRules.MaxSteps(s), LampRules.PitchSteps(s, 170));
        Assert.Equal(LampRules.PitchSteps(s, 0), LampRules.PitchSteps(s, double.NaN));
    }

    [Fact]
    public void Every_step_the_content_allows_fits_the_wires_seven_bits()
    {
        var finest = Rules.Content.Lamp with { PitchStep = 1.5 };   // the finest step validation lets through

        Assert.True(LampRules.MaxSteps(finest) <= 127);
        Assert.True(LampRules.MaxSteps(Rules.Content.Lamp) <= 127);
    }
}
