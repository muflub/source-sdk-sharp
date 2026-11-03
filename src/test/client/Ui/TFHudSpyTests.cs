using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The spy's meters (tf_hud_itemeffectmeter.cpp, tf_hud_disguise_status.cpp):
/// the cloak meter hangs over the cross only while a live spy presents, and
/// the disguise status draws at stock's DisguiseStatus spot only while the
/// TF_COND_DISGUISED word is on.
/// </summary>
public class TFHudSpyTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_cloak_meter_draws_only_over_a_live_spy()
    {
        Assert.True(HasElement("spy-builds-the-cloak", "cloak label"));
        Assert.True(HasElement("spy-cloak-full", "cloak label"));
        Assert.False(HasElement("healthy-soldier", "cloak label"));      // a soldier, no watch
        Assert.False(HasElement("medic-builds-the-ubercharge", "cloak label"));
    }

    [Fact]
    public void The_cloak_meter_reads_the_watch()
    {
        Assert.True(HasElement("spy-builds-the-cloak", "cloak label"));
        Assert.True(HasElement("spy-builds-the-cloak", "cloak meter"));
        Assert.True(HasElement("spy-builds-the-cloak", "cloak meter fill"));
        Assert.True(HasElement("spy-cloak-full", "cloak meter"));
    }

    [Fact]
    public void The_disguise_status_draws_only_while_disguised()
    {
        Assert.True(HasElement("spy-disguised-as-a-red-soldier", "disguise"));
        Assert.True(HasElement("spy-disguised-as-a-red-soldier", "disguise corner"));
        Assert.False(HasElement("spy-builds-the-cloak", "disguise"));
        Assert.False(HasElement("healthy-soldier", "disguise corner"));
    }

    [Fact]
    public void The_disguise_status_sits_at_its_res_spot()
    {
        // DisguiseStatus: xpos 10 ypos r70, 500x200 (hudlayout.res:241-253)
        // — the status row rides the panel's top edge, 10 units in and 70
        // units above the screen's bottom (x2.25).
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == "spy-disguised-as-a-red-soldier");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        var corner = UiTestKit.Named(ctx, "disguise corner").Rect;
        Assert.Equal(22.5f, corner.X, 0.5f);    // 10 x 2.25
        Assert.Equal(922.5f, corner.Y, 0.5f);   // (480-70) x 2.25
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
