using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The disguise menu's art arms: the screen asks the library for stock's
/// CIconPanel materials (hudmenuspydisguise.res's hud_menu_bg and
/// hud_spy_disguise_menu_icon, the per-class .res's hud_menu_&lt;class&gt;_&lt;team&gt;
/// icon and ico_key_blank chip), and a library that misses everything
/// draws the tokens without any texture layer.
/// </summary>
public class TFDisguiseMenuArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_menu_asks_the_library_for_the_stock_art()
    {
        var (ctx, lib) = Render("showing-blue");
        Assert.Contains("hud_menu_bg", lib.Asked);                  // MainBackground
        Assert.Contains("hud_spy_disguise_menu_icon", lib.Asked);   // SpyIcon
        Assert.Contains("hud_menu_scout_blu", lib.Asked);           // scout_blue.res:35
        Assert.Contains("ico_key_blank", lib.Asked);                // scout_blue.res:50
        Assert.True(Has(ctx, "menu bg"));
    }

    [Fact]
    public void The_shown_team_names_the_icons()
    {
        var (_, lib) = Render("showing-red");
        Assert.Contains("hud_menu_spy_red", lib.Asked);             // spy_red.res:35
        Assert.DoesNotContain(lib.Asked, a => a.EndsWith("_blue"));     // the other team's are hidden
    }

    [Fact]
    public void A_library_that_misses_everything_draws_the_fallback()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2disguisemenu" && s.Slug == "showing-blue");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var lib = new NoLibrary();
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);

        // No texture layer drew; the tokens stand in for the art.
        Assert.False(Has(ctx, "menu bg"));
        Assert.False(Has(ctx, "disguise badge"));
        Assert.False(Has(ctx, "class icon"));
        Assert.False(Has(ctx, "chip"));
        Assert.True(Has(ctx, "number-1"));
    }

    private static bool Has(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    private static (UiContext Ctx, RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2disguisemenu" && s.Slug == slug);
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
