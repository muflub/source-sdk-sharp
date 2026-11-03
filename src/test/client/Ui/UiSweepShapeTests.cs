using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The sweep shape: the pie of a fraction of the circle, from 12 o'clock
/// clockwise, fanned as convex triangles from the centre — the radial
/// progress's fill (the cap teardrop's bar).
/// </summary>
public class UiSweepShapeTests
{
    private const string Svg = "sweep";

    [Fact]
    public void A_quarter_sweep_fans_three_triangles_from_the_centre()
    {
        var svg = Draw(0.25f);
        // 0°, 30°, 60°, 90° clockwise from 12 o'clock over the 16-du rect:
        // three triangles, each centre, arc point, arc point.
        Assert.Contains("<polygon points=\"8,8 8,0 12,1.1\"", svg);
        Assert.Contains("<polygon points=\"8,8 12,1.1 14.9,4\"", svg);
        Assert.Contains("<polygon points=\"8,8 14.9,4 16,8\"", svg);
    }

    [Fact]
    public void The_full_sweep_is_the_disc()
    {
        var svg = Draw(1f);
        Assert.Contains("<polygon points=\"4.8,0 11.2,0 16,4.8 16,11.2 11.2,16 4.8,16 0,11.2 0,4.8\"", svg);
    }

    [Fact]
    public void The_empty_sweep_draws_nothing()
    {
        Assert.DoesNotContain("<polygon", Draw(0f));
    }

    [Fact]
    public void A_past_full_sweep_clamps_to_the_disc()
    {
        var svg = Draw(1.5f);
        Assert.Contains("4.8,0 11.2,0 16,4.8", svg);
        Assert.DoesNotContain("8,8", svg);
    }

    private static string Draw(float fraction)
        => UiTestKit.Svg(UiTestKit.Frame(UiTestKit.Context(), UiTestKit.Screen(200, 200),
            (ref UiFrame ui) =>
            {
                using (ui.Box().Size(Size.Du(100), Size.Du(100)))
                    ui.Shape(UiShape.Sweep, Tone.Text, 100, fraction).Name(Svg);
            }));
}
