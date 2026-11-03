using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The map info menu's art arms: the screen asks the library for stock's
/// 2D materials — the map photo (<c>vgui/maps/menu_photos_&lt;map&gt;</c>,
/// LoadMapPage, tf_mapinfomenu.cpp:349-368) and the loadout gradient's
/// shaded bar (mapinfomenu.res's ShadedBar, :218-239) — and a library
/// that misses the photo draws the page without it, exactly as stock's
/// IsErrorMaterial arm hides it (:354-377).
/// </summary>
public class TFMapInfoArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_map_info_menu_asks_the_library_for_the_stock_photo_and_shaded_bar()
    {
        var (ctx, lib) = Render("the-briefing");
        Assert.Contains("vgui/maps/menu_photos_ctf_2fort", lib.Asked);   // MapImage's material
        Assert.Contains("vgui/loadout_bottom_gradient", lib.Asked);      // ShadedBar
        Assert.True(Has(ctx, "map photo"));
        Assert.True(Has(ctx, "shaded bar"));
    }

    [Fact]
    public void A_library_that_misses_the_photo_draws_the_page_without_it()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2mapinfo" && s.Slug == "the-briefing");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);

        // Stock's IsErrorMaterial arm: no material, no photo — the rest
        // of the page still draws.
        Assert.False(Has(ctx, "map photo"));
        Assert.True(Has(ctx, "map title"));
        Assert.True(Has(ctx, "briefing"));
    }

    private static bool Has(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    private static (UiContext Ctx, TFClassMenuArtTests.RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2mapinfo" && s.Slug == slug);
        var lib = new TFClassMenuArtTests.RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, lib);
    }
}
