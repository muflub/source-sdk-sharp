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
        // The status's visible plate is the res's DisguiseStatusBG
        // (disguisestatuspanel.res:31-61): 10,30 inside the DisguiseStatus
        // element (hudlayout.res:241-253, xpos 10 ypos r70 500x200) — 20
        // units in, 200x32, its top 40 units above the screen's bottom
        // (x2.25): under the health number's row, not over it.
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == "spy-disguised-as-a-red-soldier");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        var plate = UiTestKit.Named(ctx, "disguise corner").Rect;
        Assert.Equal(45f, plate.X, 0.5f);       // (10+10) x 2.25
        Assert.Equal(990f, plate.Y, 0.5f);      // (480-40) x 2.25
        Assert.Equal(450f, plate.W, 0.5f);      // 200 x 2.25
        Assert.Equal(72f, plate.H, 0.5f);       // 32 x 2.25
    }

    [Fact]
    public void The_cloak_meter_sits_at_huditemeffectmeter_res_spot()
    {
        // The cloak meter is the plain CHudItemEffectMeter, whose res is the
        // base HudItemEffectMeter.res (GetResFile, tf_hud_itemeffectmeter.cpp:86):
        // xpos r174 ypos r62, 100x50 — the spy res's r162 r92 is the revolver's
        // meter (tf_hud_itemeffectmeter.cpp:322). 1920/1080: an r xpos is the
        // LEFT edge 174x2.25 in from the right (StockRes), so the panel's left
        // 1920 - 174x2.25 and its top (480-62)x2.25; the bg plate 12,6 inside
        // it and the bar 47,28.
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == "spy-builds-the-cloak");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        var corner = UiTestKit.Named(ctx, "cloak corner").Rect;
        Assert.Equal(1528.5f, corner.X, 0.5f);      // 1920 - 174 x 2.25
        Assert.Equal(940.5f, corner.Y, 0.5f);       // (480-62) x 2.25
        var plate = UiTestKit.Named(ctx, "cloak plate").Rect;
        Assert.Equal(1555.5f, plate.X, 0.5f);       // corner + 12 x 2.25
        Assert.Equal(954f, plate.Y, 0.5f);          // corner + 6 x 2.25
        var bar = UiTestKit.Named(ctx, "cloak meter").Rect;
        Assert.Equal(1634.25f, bar.X, 0.5f);        // corner + 47 x 2.25
        Assert.Equal(1003.5f, bar.Y, 0.5f);         // corner + 28 x 2.25
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
