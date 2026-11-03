using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The build menu's art arms: the screen asks the library for stock's
/// CIconPanel icon keys (hudmenuengybuild.res's hud_menu_bg and ico_build,
/// base_active.res's hud_menu_item_bg, ico_metal and ico_key_blank, and
/// the per-slot building art), and a library that misses everything draws
/// the tokens without any texture layer.
/// </summary>
public class TFBuildMenuArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_menu_asks_the_library_for_the_stock_art()
    {
        var (ctx, lib) = Render("all-available");
        Assert.Contains("hud_menu_bg", lib.Asked);              // MainBackground
        Assert.Contains("ico_build", lib.Asked);                // the badge and its shadow
        Assert.Contains("hud_menu_sentry_build", lib.Asked);    // sentry_active.res:13
        Assert.Contains("hud_menu_item_bg", lib.Asked);         // base_active.res:36
        Assert.Contains("ico_metal", lib.Asked);                // base_active.res:68
        Assert.Contains("ico_key_blank", lib.Asked);            // base_active.res:122
        Assert.True(Has(ctx, "menu bg"));
    }

    [Fact]
    public void A_blocked_slot_asks_for_the_outline_background()
    {
        var (ctx, lib) = Render("one-built-one-unaffordable");
        Assert.Contains("hud_menu_item_bg_outline", lib.Asked); // base_already_built.res:36
        Assert.True(Has(ctx, "reason-1"));
        Assert.True(Has(ctx, "reason-2"));
    }

    [Fact]
    public void A_library_that_misses_everything_draws_the_fallback()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2buildmenu" && s.Slug == "all-available");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var lib = new NoLibrary();
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);

        // No texture layer drew; the tokens stand in for the art.
        Assert.False(Has(ctx, "menu bg"));
        Assert.False(Has(ctx, "item bg"));
        Assert.False(Has(ctx, "building"));
        Assert.False(Has(ctx, "chip"));
        Assert.True(Has(ctx, "name"));
        Assert.True(Has(ctx, "cost-1"));
    }

    private static bool Has(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    private static (UiContext Ctx, RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2buildmenu" && s.Slug == slug);
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
        public UiTextureIcon? Icon(string name) { Asked.Add(name); return new UiTextureIcon(0x51, new UiRect(0, 0, 1f, 1f), 1, 1); }
    }

    /// <summary>Misses everything: the fallback arm.</summary>
    internal sealed class NoLibrary : IUiTextureLibrary
    {
        public int Find(string name) => 0;
        public int Material(string name) => 0;
        public UiTextureInfo? About(string name) => null;
        public UiTextureIcon? Icon(string name) => null;
    }
}
