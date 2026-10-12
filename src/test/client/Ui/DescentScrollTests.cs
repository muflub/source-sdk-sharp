using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The shop windows at their minimum placement: the narrow stack scrolls
/// inside the frame. A scroll region sized to its own content never
/// overflows, so the toolkit draws no thumb, and the content past the frame
/// edge is clipped where the player cannot reach it. That was the crate's
/// OPEN and the craftbench's CRAFT in the restarted review, and the vendor's
/// REPAIR ALL before 0abf3404.
/// </summary>
public class DescentScrollTests
{
    private static readonly UiFontLibrary Fonts = new();

    public static TheoryData<string, string> Regions() => new()
    {
        { "crate", "sections" },
        { "craftbench", "sections" },
        { "vendor", "svc-pane" },
    };

    [Theory]
    [MemberData(nameof(Regions))]
    public void The_minimum_window_scrolls_its_stack_inside_the_frame(string screen, string region)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == screen && s.Slug == "resized-to-its-minimum");
        var lang = UiSamplesTests.Language();
        foreach (var size in UiSamples.Sizes())
        {
            var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, lang);
            UiSamples.Render(ctx, sample.Create(), size.Screen, sample.Now);
            var frame = UiTestKit.Named(ctx, "wnd-frame").Rect;
            var pane = UiTestKit.Named(ctx, region).Rect;
            Assert.True(pane.Y + pane.H <= frame.Y + frame.H + 0.5f,
                        $"{screen}@{size.Name}: {region} ends at {pane.Y + pane.H}, frame at {frame.Y + frame.H}");
            // The content overflows at this placement, so the region must offer
            // its thumb: the only way to reach the primary below the fold.
            var thumb = UiTestKit.Named(ctx, region + " thumb").Rect;
            Assert.True(thumb.H > 0f && thumb.Y >= pane.Y - 0.5f && thumb.Y + thumb.H <= pane.Y + pane.H + 0.5f,
                        $"{screen}@{size.Name}: thumb {thumb} outside {region} {pane}");
        }
    }
}
