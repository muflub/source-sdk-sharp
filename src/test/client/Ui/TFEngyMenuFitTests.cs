using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The engineer's menus follow stock's hudlayout choice: stock keeps the
/// centred BUILD panel on the common landscape window even when the
/// engine's ui_scale shrinks the design width under 1440 du (a scaled
/// ui_scale is stock's own scaling, not a narrow screen); the stacked
/// corner list is for portrait (the live A/B: stock centred, ours stacked
/// at the same window — see scratch-engiflow/m1_menu_open.png).
/// </summary>
public class TFEngyMenuFitTests
{
    private sealed class NoArt : IUiTextureLibrary
    {
        public int Find(string name) => 0;
        public int Material(string name) => 0;

        public UiTextureInfo? About(string name) => null;
        public UiTextureIcon? Icon(string name) => null;
    }

    private static TFBuildMenu Screen(FakeTFBuildMenuSource source)
        => new(source, new TFBuildTile(new NoArt()), new NoArt());

    private static UiContext Drawn(UiScreen screen, ScreenInfo screenInfo)
    {
        var ctx = Context();
        Frame(ctx, screenInfo, (ref UiFrame ui) => screen.Describe(ref ui));
        return ctx;
    }

    private static bool Has(UiContext ctx, string name) =>
        Array.Exists(Elements(ctx), e => NameOf(ctx, e) == name);

    /// <summary>The stock arm's cancel sentence is the centred panel's
    /// (:612-619); the stacked arm draws "cancel bar" instead. With the
    /// engine's ui_scale 2 (design width 960 du, the live size) the panel
    /// still has to be the stock one.</summary>
    [Fact]
    public void A_scaled_common_window_keeps_the_stock_panel()
    {
        var ctx = Drawn(Screen(new FakeTFBuildMenuSource { Metal = 400 }),
                        UiScale.Screen(1280, 720, uiScale: 2f));
        Assert.True(Has(ctx, "cancel"), "the stock panel's cancel sentence drew");
    }

    /// <summary>Portrait still stacks (the column fits the width better):
    /// the stacked arm's bar key is there and the stock sentence is not.</summary>
    [Fact]
    public void A_portrait_window_stacks()
    {
        var ctx = Drawn(Screen(new FakeTFBuildMenuSource { Metal = 400 }),
                        UiScale.Screen(1080, 1920, uiScale: 2f));
        Assert.True(Has(ctx, "cancel bar"));
    }
}
