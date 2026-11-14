using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The character sheet's points box (found live): at the window's default size it sat at the
/// bottom of the scrolled body, cut off under the Reset / Apply row. It stands in the footer
/// now, whole, above the buttons, at every size the previews draw.
/// </summary>
public class DescentCharSheetLayoutTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_points_box_stands_whole_above_the_apply_button_at_every_size()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(), s => !s.Gallery && s.Group == "charsheet" && s.Slug == "mid-spend-l18");
        foreach (var size in UiSamples.Sizes())
        {
            var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
            UiSamples.Render(ctx, sample.Create(), size.Screen, 10.0);
            var els = UiTestKit.Elements(ctx);
            var points = Assert.Single(els, e => UiTestKit.NameOf(ctx, e) == "points");
            var apply = Assert.Single(els, e => UiTestKit.NameOf(ctx, e) == "apply");

            Assert.True(points.Rect.Y + points.Rect.H <= apply.Rect.Y,
                        $"{size.Name}: the points box {points.Rect} runs into the apply button {apply.Rect}");
        }
    }
}
