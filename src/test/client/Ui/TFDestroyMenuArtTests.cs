using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The destroy menu's art arms: the screen asks the library for stock's
/// CIconPanel materials (hudmenuengydestroy.res's hud_menu_bg and
/// ico_demolish, base_active.res's hud_menu_item_bg_outline and
/// ico_key_blank, and the per-slot obj_status building art), the not
/// built arm asks none of the building art (base_inactive.res), and a
/// library that misses everything draws the tokens without any texture
/// layer.
/// </summary>
public class TFDestroyMenuArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_menu_asks_the_library_for_the_stock_art()
    {
        var (ctx, lib) = Render("sentry-built");
        Assert.Contains("hud_menu_bg", lib.Asked);              // MainBackground
        Assert.Contains("ico_demolish", lib.Asked);             // the badge and the tile's cross
        Assert.Contains("obj_status_sentrygun_1", lib.Asked);   // sentry_active.res:68
        Assert.Contains("hud_menu_item_bg_outline", lib.Asked); // base_active.res:35
        Assert.Contains("ico_key_blank", lib.Asked);            // base_active.res:83
        Assert.True(Has(ctx, "menu bg"));
    }

    [Fact]
    public void The_not_built_slots_ask_no_building_art()
    {
        var (ctx, lib) = Render("nothing-built");
        Assert.DoesNotContain(lib.Asked, a => a.Contains("obj_status"));  // base_inactive.res has none
        Assert.True(Has(ctx, "not-built-1"));
        Assert.True(Has(ctx, "not-built-4"));
    }

    [Fact]
    public void A_library_that_misses_everything_draws_the_fallback()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2destroymenu" && s.Slug == "sentry-built");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var lib = new NoLibrary();
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);

        // No texture layer drew; the tokens stand in for the art.
        Assert.False(Has(ctx, "menu bg"));
        Assert.False(Has(ctx, "item bg"));
        Assert.False(Has(ctx, "destroy icon"));
        Assert.False(Has(ctx, "building"));
        Assert.False(Has(ctx, "chip"));
        Assert.True(Has(ctx, "name"));
        Assert.True(Has(ctx, "not-built-2"));
    }

    private static bool Has(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    private static (UiContext Ctx, RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2destroymenu" && s.Slug == slug);
        var lib = new RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, lib);
    }

    /// <summary>Answers every name with a non-zero handle, recording each
    /// ask (the brief's fake: it records the Find calls).</summary>
    private sealed class RecordingLibrary : IUiTextureLibrary
    {
        public readonly List<string> Asked = new();
        public int Find(string name) { Asked.Add(name); return 0x51; }
        public int Material(string name) => 0;
        public UiTextureInfo? About(string name) { Asked.Add(name); return null; }
        public UiTextureIcon? Icon(string name) { Asked.Add(name); return new UiTextureIcon(0x51, new UiRect(0, 0, 1f, 1f), 1, 1); }
    }

    /// <summary>Misses everything: the fallback arm.</summary>
    private sealed class NoLibrary : IUiTextureLibrary
    {
        public int Find(string name) => 0;
        public int Material(string name) => 0;
        public UiTextureInfo? About(string name) => null;
        public UiTextureIcon? Icon(string name) => null;
    }
}
