using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The class menu's art arms: the screen asks the library for stock's
/// material names — the <c>class_sel_sm_*</c> portraits (the .res's
/// image_default, and the team art <c>SetImageSelected</c> arms,
/// tf_classmenu.cpp:567,576) and the loadout gradient's shaded bar —
/// and a library that misses everything draws the team-tone tiles with
/// their names instead of any texture layer.
/// </summary>
public class TFClassMenuArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_class_menu_asks_the_library_for_the_stock_portraits()
    {
        var (ctx, lib) = Render("on-red-as-soldier");
        Assert.Contains("vgui/class_sel_sm_scout_inactive", lib.Asked);     // image_default
        Assert.Contains("vgui/class_sel_sm_soldier_red", lib.Asked);        // the current class arms in the team's art
        Assert.Contains("vgui/class_sel_sm_random_inactive", lib.Asked);    // random keeps the .res's inactive pair
        Assert.Contains("vgui/loadout_bottom_gradient", lib.Asked);         // ShadedBar
        Assert.True(Has(ctx, "art rest"));
        Assert.True(Has(ctx, "art armed"));
    }

    [Fact]
    public void The_blue_menu_asks_for_the_blue_portraits()
    {
        var lib = new RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var screen = new TFClassMenu(new FakeTFClassMenuSource { Team = 3 }, new TFClassTile(lib), lib);
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 100.0);

        Assert.Contains("vgui/class_sel_sm_medic_blu", lib.Asked);
        Assert.DoesNotContain("vgui/class_sel_sm_soldier_red", lib.Asked);
    }

    [Fact]
    public void A_library_that_misses_everything_draws_the_fallback_tiles()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2classmenu" && s.Slug == "on-red-as-soldier");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);

        // No texture layer drew; the team-tone tiles with their names and
        // counts stand in for the portraits.
        Assert.False(Has(ctx, "art rest"));
        Assert.False(Has(ctx, "art armed"));
        Assert.False(Has(ctx, "shaded bar"));
        Assert.True(Has(ctx, "count-1"));
    }

    private static bool Has(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    private static (UiContext Ctx, RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2classmenu" && s.Slug == slug);
        var lib = new RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, lib);
    }

    /// <summary>Answers every name with a non-zero handle, recording each
    /// ask (the brief's fake: it records the Find calls).</summary>
    internal sealed class RecordingLibrary : IUiTextureLibrary
    {
        public readonly List<string> Asked = new();
        public int Find(string name) { Asked.Add(name); return 0x51; }
        public int Material(string name) => 0;
        public UiTextureInfo? About(string name) { Asked.Add(name); return null; }
        public UiTextureIcon? Icon(string name) { Asked.Add(name); return null; }
    }
}
