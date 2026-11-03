using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
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

    /// <summary>The bars are backdrop the HUD reads over: stock paints the
    /// panel's own background transparent and leaves the panel without a
    /// z-pos of its own (SpectatorGUI.cpp:485, :494), so the hud elements
    /// draw over it. Order -1 joins the screen ahead of the Order-0 hud
    /// screens, the scope's place — the hud screen and the kill feed keep
    /// their places over it.</summary>
    [Fact]
    public void The_spectator_screen_draws_below_the_hud_screens()
    {
        var hud = TFHudSamples.Build(new UiSampleServices());
        var feed = TFKillFeedSamples.Build(new UiSampleServices());
        var spectator = TFSpectatorGuiSamples.Build(new UiSampleServices());
        var (layer, _) = UiModScreens.Place([], [], [hud, feed, spectator]);
        int iSpectator = Array.IndexOf(layer, spectator);
        int iHud = Array.IndexOf(layer, hud);
        int iFeed = Array.IndexOf(layer, feed);
        Assert.True(iSpectator >= 0 && iSpectator < iHud && iSpectator < iFeed,
                    $"the spectator ({iSpectator}) must draw before the hud ({iHud}) and feed ({iFeed})");
    }

    [Fact]
    public void The_spectator_takes_the_core_specgui_slot()
    {
        // Stock's spectator.res answers the core specgui with an empty block
        // (spectator.res:14-16): the base panel is suppressed, the TF bars
        // the only "specgui". The composition drops the core screen and
        // seats this one in its slot — one spectator screen, named specgui.
        var spectator = TFSpectatorGuiSamples.Build(new UiSampleServices());
        var (layer, menu) = UiModScreens.Place([], [],
            [TFHudSamples.Build(new UiSampleServices()), new SpecGui(new FakeObserver()), spectator]);

        Assert.Single(layer, s => s.Name == "specgui");
        Assert.Same(spectator, layer.Single(s => s.Name == "specgui"));
        Assert.DoesNotContain(layer.Concat(menu), s => s is SourceSharp.Client.Ui.Screens.SpecGui);
    }

    [Fact]
    public void The_bars_fill_stocks_black_bar_colour()
    {
        // GetBlackBarColor's TF override, the opaque 52 48 45
        // (tf_spectatorgui.h:68) ApplySchemeSettings paints both bars with
        // (SpectatorGUI.cpp:486-487).
        var opaque = new UiColor(52, 48, 45, 255);
        Assert.Equal(opaque, PlateColour("top plate"));
        Assert.Equal(opaque, PlateColour("bottom plate"));
    }

    /// <summary>The fill of the named plate: its own (or nearest ancestor's)
    /// quad in the draw list.</summary>
    private static UiColor PlateColour(string name)
    {
        var (ctx, _, _) = Render("spectating-a-map");
        var list = ctx.List.AsDrawList();
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
        {
            if (UiTestKit.NameOf(ctx, elements[i]) != name)
                continue;
            for (int e = i; e >= 0; e = elements[e].Parent)
                foreach (var op in list.Ops)
                    if (op.Kind == UiPrimitiveKind.Quad && op.Element == e)
                        return list.Quads[op.Index].Color;
        }
        throw new InvalidOperationException($"no quad above {name}");
    }


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
                                   s => !s.Gallery && s.Group == "specgui" && s.Slug == slug);
        var lib = new TFClassMenuArtTests.RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var screen = sample.Create(lib);
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, screen, lib);
    }
}
