using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The lamp helper (Phase 0 review, point 3): a light with its colour,
/// brightness and falloff, and defaults measured to light a grey room
/// without clipping.
/// </summary>
public sealed class DescentLampTests
{
    private static RoomEntity LampIn(Lamp? lamp = null) =>
        Assert.Single(new RoomBuilder("r", RoomKit.Normal, RoomTheme.Grey).WithHeight(512).WithLamp(new Point(512, 512, 448), lamp).Build().Entities);

    [Fact]
    public void A_lamp_writes_its_colour_and_brightness_as_light()
    {
        Assert.Equal("10 20 30 77", LampIn(new Lamp("10 20 30", 77)).Key("_light"));
    }

    [Fact]
    public void A_lamp_writes_its_falloff_distances()
    {
        var e = LampIn(new Lamp(FiftyPercent: 111, ZeroPercent: 222));

        Assert.Equal(("111", "222"), (e.Key("_fifty_percent_distance"), e.Key("_zero_percent_distance")));
    }

    [Fact]
    public void The_default_lamp_fades_to_nothing_inside_one_cell()
    {
        Lamp d = Lamp.Default;

        Assert.InRange(d.FiftyPercent, 1, d.ZeroPercent - 1);
        Assert.InRange(d.ZeroPercent, 1, RoomKit.Normal.Cell);
    }

    [Fact]
    public void A_light_without_a_lamp_takes_the_default_falloff()
    {
        var e = Assert.Single(new RoomBuilder("r", RoomKit.Normal, RoomTheme.Grey).WithHeight(512).WithLight(new Point(512, 512, 448), 99).Build().Entities);

        Assert.Equal(Lamp.DefaultZero.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Key("_zero_percent_distance"));
    }

    [Fact]
    public void A_lamp_whose_half_distance_is_not_before_its_zero_distance_is_refused()
    {
        Assert.Throws<ArgumentException>(() => LampIn(new Lamp(FiftyPercent: 500, ZeroPercent: 500)));
    }
}
