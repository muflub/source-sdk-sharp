using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The freeze panel's art arms: the screen asks the library for stock's
/// 2D materials — the killer-team colour_panel bar (tf_hud_freezepanel.cpp:
/// 580-584), the killer's class image in the avatar slot (freezepanel_basic.res
/// :311-325), the killer-health cross (freezepanelkillerhealth.res), the
/// nemesis sub-panel's brown bar and icon (:327-406) and the screenshot
/// hint (:409-467) — and the label's and nemesis panel's arms read the
/// death's flags (:234-246, :380-402).
/// </summary>
public class TFFreezePanelArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_freeze_panel_asks_the_library_for_the_stock_materials()
    {
        var (ctx, lib) = Render("killed-by-a-living-soldier");
        Assert.Contains("hud/color_panel_red", lib.Asked);              // the RED killer's bar (:583)
        Assert.Contains("hud/class_soldierred", lib.Asked);             // the class image in the avatar slot
        Assert.Contains("hud/health_bg", lib.Asked);                    // freezepanelkillerhealth.res:27
        Assert.Contains("hud/health_color", lib.Asked);                 // the cross's fg
        Assert.True(Has(ctx, "panel bg"));
        Assert.True(Has(ctx, "killer portrait"));
        Assert.True(Has(ctx, "cross"));
        Assert.True(Has(ctx, "killer health"));

        // The nemesis sub-panel and the hint are not this death's.
        Assert.DoesNotContain("hud/color_panel_brown", lib.Asked);
        Assert.False(Has(ctx, "nemesis"));

        // The hint: the screenshot key is bound in the samples, so the
        // camera and its token draw (:992-1015).
        Assert.Contains("hud/ico_camera", lib.Asked);
        Assert.True(Has(ctx, "hint"));
    }

    [Fact]
    public void A_blue_killer_dyes_the_bar_blue()
    {
        var (ctx, lib) = Render("killed-by-a-blu-soldier");
        Assert.Contains("hud/color_panel_blu", lib.Asked);              // (:583)
        Assert.Contains("hud/class_soldierblue", lib.Asked);
        Assert.DoesNotContain("hud/color_panel_red", lib.Asked);
        Assert.True(Has(ctx, "panel bg"));
    }

    [Fact]
    public void A_domination_names_the_killer_our_nemesis()
    {
        var (ctx, lib) = Render("your-new-nemesis");
        Assert.Contains("hud/color_panel_brown", lib.Asked);            // the nemesis bar (:349)
        Assert.Contains("hud/leaderboard_nemesis_freezecam", lib.Asked); // the icon (:369)
        Assert.True(Has(ctx, "nemesis"));
        Assert.Equal("New Nemesis!", RunText(ctx, Index(ctx, "nemesis")));
        Assert.Equal("You were killed again by", RunText(ctx, Index(ctx, "killed by")));
    }

    [Fact]
    public void A_revenge_is_named_without_making_the_killer_our_nemesis()
    {
        var (ctx, _) = Render("revenge-is-theirs");
        Assert.True(Has(ctx, "nemesis"));
        Assert.Equal("Revenge!", RunText(ctx, Index(ctx, "nemesis")));
        Assert.Equal("You were killed by", RunText(ctx, Index(ctx, "killed by")));
    }

    [Fact]
    public void A_dead_killer_takes_the_late_and_the_dead_cross()
    {
        var (ctx, lib) = Render("killed-by-a-dead-soldier");
        Assert.Contains("hud/health_dead", lib.Asked);                  // CTFHealthPanel's m_flHealth <= 0
        Assert.DoesNotContain("hud/health_color", lib.Asked);
        Assert.False(Has(ctx, "cross"));
        Assert.False(Has(ctx, "killer health"));                        // the value only while there is health
        Assert.Equal("You were killed by the late", RunText(ctx, Index(ctx, "killed by")));
    }

    /// <summary>
    /// A suicide or a world death draws no freeze panel: the death-cam target
    /// of a killerless death is NULL (Event_Killed's final arm,
    /// tf_player.cpp:12487-12490), the freeze cam is entered only with a
    /// target that is not us (StateThinkDYING, tf_player.cpp:13956-13968),
    /// and show_freezepanel fires only on entering OBS_MODE_FREEZECAM
    /// (c_baseplayer.cpp:885-910) — so the panel's show arm
    /// (tf_hud_freezepanel.cpp:293) never runs. A kill by another player
    /// shows it.
    /// </summary>
    [Fact]
    public void A_suicide_draws_no_freeze_panel()
    {
        var screen = TFFreezePanelSamples.Build(new UiSampleServices(), death: new FakeDeathScreen());
        Assert.False(screen.Visible);
    }

    [Fact]
    public void A_kill_by_another_player_shows_the_freeze_panel()
    {
        var screen = TFFreezePanelSamples.Build(new UiSampleServices(),
            death: new FakeDeathScreen { HasKiller = true, KillerName = "Bob", KillerTeam = 2 });
        Assert.True(screen.Visible);
    }

    [Fact]
    public void Nothing_bound_to_screenshot_draws_no_hint()
    {
        var (ctx, _) = Render("no-screenshot-key");
        Assert.False(Has(ctx, "hint"));                                 // ShowSnapshotPanel's no-key arm
        Assert.False(Has(ctx, "camera"));
        Assert.True(Has(ctx, "panel bg"));                              // the rest of the panel still draws
        Assert.True(Has(ctx, "killed by"));
    }

    /// <summary>The killer's name rides beside the image when it drew and
    /// at the avatar's x when it did not (tf_hud_freezepanel.cpp:586-598):
    /// the beside position is the avatar's 49 + its 14 + XRES(2), inside
    /// the panel host.</summary>
    [Fact]
    public void The_killer_name_sits_beside_the_image_when_it_drew()
    {
        var (withImage, _) = Render("killed-by-a-living-soldier");
        float panelX = Named(withImage, "panel").Rect.X;
        Assert.Equal(panelX + 65f * StockRes.Scale, Named(withImage, "killer name").Rect.X, 1f);

        // No art: the fallback arm's build, the name at the avatar's x.
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, TFFreezePanelSamples.Build(new UiSampleServices(),
                        death: new FakeDeathScreen { HasKiller = true, KillerName = "Bob" },
                        keys: new SampleKeys().With("F5", "screenshot")),
                         UiSamples.Screen(1920, 1080), 100.0);
        Assert.Equal(Named(ctx, "panel").Rect.X + 49f * StockRes.Scale, Named(ctx, "killer name").Rect.X, 1f);
    }

    /// <summary>The health child's own .res geometry inside the panel
    /// host: the bar at 8 units, the cross bg at 8+5, the cross at 8+7,
    /// and the value box centred in the cross (freezepanelkillerhealth.res
    /// through freezepanel_basic.res's 8 160 31x31 health panel).</summary>
    [Fact]
    public void The_killer_health_cross_sits_at_its_res_box_in_the_panel()
    {
        var (ctx, _) = Render("killed-by-a-living-soldier");
        float panelX = Named(ctx, "panel").Rect.X;
        Assert.Equal(panelX + 8f * StockRes.Scale, Named(ctx, "panel bg").Rect.X, 1f);
        Assert.Equal(panelX + 13f * StockRes.Scale, Named(ctx, "cross bg").Rect.X, 1f);
        Assert.Equal(panelX + 15f * StockRes.Scale, Named(ctx, "cross").Rect.X, 1f);

        var cross = Named(ctx, "cross").Rect;
        var value = Named(ctx, "killer health").Rect;
        Assert.Equal(cross.X + cross.W / 2, value.X + value.W / 2, 1f);
        Assert.Equal(cross.Y + cross.H / 2, value.Y + value.H / 2, 1f);
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

    private static (UiContext Ctx, TFClassMenuArtTests.RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2freezepanel" && s.Slug == slug);
        var lib = new TFClassMenuArtTests.RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, lib);
    }
}
