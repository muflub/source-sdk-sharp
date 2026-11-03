using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The demoman's sticky corner's two stock arms
/// (<c>CHudDemomanPipes::OnTick</c>, tf_hud_demomanpipes.cpp:138-146) and
/// its gate (<c>ShouldDraw</c>'s live-demoman arm): bombs out counts them by
/// the live icon, the empty tray keeps the corner with the same label dull
/// by the faded one, and no demoman stands no corner at all.
/// </summary>
public class TFHudPipesTests
{
    [Fact]
    public void Bombs_out_count_by_the_live_icon()
    {
        Assert.True(HasElement("demoman-pipes-out", "pipes bg"));
        Assert.True(HasElement("demoman-pipes-out", "pipes icon"));
        Assert.Equal("3", TextOf("demoman-pipes-out", "pipes count"));
    }

    [Fact]
    public void The_empty_tray_keeps_the_corner_and_its_label()
    {
        Assert.True(HasElement("demoman-tray-empty", "pipes bg"));
        Assert.True(HasElement("demoman-tray-empty", "pipes icon"));
        Assert.Equal("0", TextOf("demoman-tray-empty", "pipes count"));
    }

    [Fact]
    public void No_demoman_no_corner()
    {
        // The soldier samples build with the at-rest pipes (Present false):
        // stock's ShouldDraw is a demoman arm first (tf_hud_demomanpipes.cpp:104).
        Assert.False(HasElement("healthy-soldier", "pipes bg"));
        Assert.False(HasElement("healthy-soldier", "pipes count"));
        // ... while the corner's neighbours still draw (the stimulus reaches
        // the screen this fact reads).
        Assert.True(HasElement("healthy-soldier", "player corner"));
    }

    /// <summary>The sample's frame, art and all (the same run
    /// <see cref="TFHudTests.HasElement"/> reads).</summary>
    private static UiContext Rendered(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(),
                                           UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(TFHudTests.AllArt.Library), UiSamples.Screen(1920, 1080), sample.Now);
        return ctx;
    }

    private static bool HasElement(string slug, string key)
    {
        var ctx = Rendered(slug);
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == key);
    }

    /// <summary>The text the named element drew.</summary>
    private static string TextOf(string slug, string key)
    {
        var ctx = Rendered(slug);
        var els = UiTestKit.Elements(ctx);
        int i = Array.FindIndex(els, e => UiTestKit.NameOf(ctx, e) == key);
        Assert.True(i >= 0, $"{key} never drew on {slug}");
        return UiTestKit.RunText(ctx, i);
    }
}
