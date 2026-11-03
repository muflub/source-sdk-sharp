using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The team menu's art arms: the screen asks the library for stock's
/// 2D material — the loadout gradient's shaded bar (teammenu.res's
/// ShadedBar, :310-331) — and a library that misses everything draws the
/// layout without it, the team-tone doors standing in for stock's model
/// panels (docs/plans/tf2/deferred.tsv).
/// </summary>
public class TFTeamMenuArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_team_menu_asks_the_library_for_the_shaded_bar()
    {
        var (ctx, lib) = Render("on-red");
        Assert.Contains("vgui/loadout_bottom_gradient", lib.Asked);   // ShadedBar
        Assert.True(Has(ctx, "shaded bar"));
    }

    [Fact]
    public void A_library_that_misses_everything_draws_the_fallback_doors()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2teammenu" && s.Slug == "on-red");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);

        // No texture layer drew; the team-tone doors with their names and
        // the counts over them stand in for the models.
        Assert.False(Has(ctx, "shaded bar"));
        Assert.True(Has(ctx, "team-card-2"));
        Assert.True(Has(ctx, "team-card-3"));
        Assert.True(Has(ctx, "count-2"));
    }

    private static bool Has(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    private static (UiContext Ctx, TFClassMenuArtTests.RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2teammenu" && s.Slug == slug);
        var lib = new TFClassMenuArtTests.RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, lib);
    }
}
