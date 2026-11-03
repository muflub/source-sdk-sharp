using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The building status screen's art layout (hud_obj_*.res): the not-built
/// label rides x60 but stays on the item's 120-wide plate — stock's
/// NotBuiltLabel never draws past the plate the way one unwrapped line at
/// a wide font does. The kind icon is the item's own child in the .res, so
/// the not-built item shows it too, over the token's two lines.
/// </summary>
public class TFHudBuildingStatusScreenTests
{
    private static readonly UiFontLibrary Fonts = new();

    /// <summary>The sentry-under-construction sample, drawn: its dispenser
    /// is the not-built arm.</summary>
    private static UiContext Placing()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfbuildingstatus" && s.Slug == "sentry-under-construction");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1280, 720), sample.Now);
        return ctx;
    }

    [Fact]
    public void The_not_built_label_ends_at_the_plates_right_edge()
    {
        var ctx = Placing();
        var elements = UiTestKit.Elements(ctx);
        var label = Named(ctx, "not built");
        var plate = elements.First(e => (UiTestKit.NameOf(ctx, e) == "building bg"
                                         || UiTestKit.NameOf(ctx, e) == "building bg fallback")
                                        && e.Parent == label.Parent);
        Assert.True(label.Rect.X + label.Rect.W <= plate.Rect.X + plate.Rect.W + 0.5f,
                    $"not-built label ends at {label.Rect.X + label.Rect.W}, the plate at {plate.Rect.X + plate.Rect.W}");
    }

    [Fact]
    public void The_not_built_item_keeps_its_kind_icon()
    {
        var ctx = Placing();

        // Two items, one mid-build and one not started: the icon is the
        // item's child (hud_obj_*.res Icon_*), so both draw one.
        Assert.Equal(2, UiTestKit.Elements(ctx).Count(e => UiTestKit.NameOf(ctx, e) == "kind icon"));
    }

    [Fact]
    public void The_not_built_label_is_the_tokens_two_lines()
    {
        var ctx = Placing();

        // #TF2_Building_NotBuilt_Dispenser's "Dispenser\nNot Built", as the
        // label's runs: one per line, the name line then the state line.
        Assert.Equal("DispenserNot Built", UiTestKit.RunText(ctx, At(ctx, "not built")));
    }

    private static int At(UiContext ctx, string name)
        => Array.FindIndex(UiTestKit.Elements(ctx), e => UiTestKit.NameOf(ctx, e) == name);

    private static UiElementInfo Named(UiContext ctx, string name)
    {
        var elements = UiTestKit.Elements(ctx);
        int at = Array.FindIndex(elements, e => UiTestKit.NameOf(ctx, e) == name);
        Assert.True(at >= 0, $"no {name} drew");
        return elements[at];
    }
}
