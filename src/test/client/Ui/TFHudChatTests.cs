using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The chat feed (tf_hud_chat.cpp through CBaseHudChat): the corner takes
/// the core hud's chat and draws it at stock's HudChat box
/// (hudlayout.res:980-992) — nothing over an empty feed, the box's bottom
/// 85 units up on any canvas height.
/// </summary>
public class TFHudChatTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_feed_draws_only_over_a_spoken_line()
    {
        Assert.True(HasElement("chat-over-the-corner", "chat"));
        Assert.False(HasElement("healthy-soldier", "chat"));     // no chat sample line
    }

    [Fact]
    public void The_feed_sits_at_its_res_spot()
    {
        // HudChat: xpos 10, bottom 85 units up (480 - 275 - 120) — the
        // column's bottom-left, x2.25 (hudlayout.res:986-990).
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == "chat-over-the-corner");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        var corner = UiTestKit.Named(ctx, "chat").Rect;
        Assert.Equal(22.5f, corner.X, 0.5f);        // 10 x 2.25
        Assert.Equal(191.25f, 1080f - (corner.Y + corner.H), 0.5f);   // 85 x 2.25
    }

    [Fact]
    public void The_feed_hugs_the_bottom_on_a_raised_ui_scale()
    {
        // The canvas's unit space shrinks with the ui scale (320 tall at
        // 1920x1080@1.5, not the .res's 480): a top-referenced ypos would
        // hang the feed 75 units below the screen; the bottom-referenced
        // box keeps its bottom 85 units up on the du canvas, 191.25 x the
        // 1.5 scale.
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == "chat-over-the-corner");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080, 1.5f), sample.Now);
        var corner = UiTestKit.Named(ctx, "chat").Rect;
        Assert.True(corner.Y >= 0f && corner.Y + corner.H <= 1080f,
                    $"chat {(corner.Y + corner.H - 1080f) / 1.5f:0.#} du off the raised canvas");
        Assert.Equal(191.25f * 1.5f, 1080f - (corner.Y + corner.H), 0.5f);
    }

    /// <summary>Whether the named part drew on the sample's screen.</summary>
    private static bool HasElement(string slug, string name)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);
    }
}
