using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The spectator's bars (tf_spectatorgui.cpp UpdateKeyLabels :450-705):
/// the map names itself on the top bar, the key hints carry the bound
/// keys, the class-or-team ask arms on the local player's team, and
/// playing draws nothing.
/// </summary>
public class TFSpectatorGuiArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_map_names_itself_on_the_top_bar()
    {
        var (ctx, _, _) = Render("spectating-a-map");
        Assert.True(Has(ctx, "map"));
        // The label's %s1 is the display name, stock's GetMapDisplayName
        // (tf_gamerules.cpp:18974) — the map-info source's answer.
        Assert.Equal("map: 2Fort", RunText(ctx, Index(ctx, "map")));
        Assert.True(Has(ctx, "top plate"));
        Assert.True(Has(ctx, "bottom plate"));
    }

    [Fact]
    public void The_key_hints_carry_the_bound_keys()
    {
        var (ctx, _, _) = Render("spectating-a-map");
        Assert.Equal("[SPACE]", RunText(ctx, Index(ctx, "switch key")));
        Assert.Equal("[MOUSE1]", RunText(ctx, Index(ctx, "cycle fwd key")));
        Assert.Equal("[MOUSE2]", RunText(ctx, Index(ctx, "cycle rev key")));
        Assert.Equal("Switch Camera Mode", RunText(ctx, Index(ctx, "switch cam")));
        Assert.Equal("Cycle Targets (fwd)", RunText(ctx, Index(ctx, "cycle fwd")));
        Assert.Equal("Cycle Targets (rev)", RunText(ctx, Index(ctx, "cycle rev")));
    }

    [Fact]
    public void The_spectator_team_is_asked_to_change_team()
    {
        var (ctx, _, _) = Render("spectating-a-map");
        Assert.Equal("Press [ F2 ] to Change Team", RunText(ctx, Index(ctx, "class or team")));
    }

    [Fact]
    public void A_team_is_asked_to_change_class()
    {
        var (ctx, _, _) = Render("dead-on-a-team");
        Assert.Equal("Press [ F1 ] to Change Class", RunText(ctx, Index(ctx, "class or team")));
    }

    /// <summary>The bars are the playing player's none (the gate): the
    /// sample draws (previews ignore Visible) for the fact to refuse.</summary>
    [Fact]
    public void Playing_the_bars_refuse()
    {
        var (_, screen, _) = Render("playing-draws-nothing");
        Assert.False(screen.Visible);
    }

    /// <summary>The top bar's .res geometry (spectator.res:17-32): the
    /// full-width 84-tall band at the screen's top, its labels at their
    /// boxes — the map's right edge 260 units from the screen's right.</summary>
    [Fact]
    public void The_top_bar_bands_the_screen_at_its_res_box()
    {
        var (ctx, _, _) = Render("spectating-a-map");
        var top = Named(ctx, "top plate").Rect;
        Assert.Equal(0f, top.X, 1f);
        Assert.Equal(0f, top.Y, 1f);
        Assert.Equal(1920f, top.W, 1f);
        Assert.Equal(84f * StockRes.Scale, top.H, 1f);

        // The map label's .res box: right-anchored, its right edge 640-380
        // = 20 units from the screen's right (the r260 box's left at 380
        // on the 640-wide canvas), 16 units from the top.
        var map = Named(ctx, "map").Rect;
        Assert.Equal(1875f, map.X + map.W, 1f);
        Assert.Equal(16f * StockRes.Scale, map.Y, 1f);
    }

    private static bool Has(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    /// <summary>The named element's index (RunText's handle).</summary>
    private static int Index(UiContext ctx, string name)
    {
        var all = UiTestKit.Elements(ctx);
        for (int i = 0; i < all.Length; i++)
            if (UiTestKit.NameOf(ctx, all[i]) == name)
                return i;
        throw new InvalidOperationException($"no element named {name}");
    }

    private static (UiContext Ctx, UiScreen Screen, TFClassMenuArtTests.RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2spectatorgui" && s.Slug == slug);
        var lib = new TFClassMenuArtTests.RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var screen = sample.Create(lib);
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, screen, lib);
    }
}
