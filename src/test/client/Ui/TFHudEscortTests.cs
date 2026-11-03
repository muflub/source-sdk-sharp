using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The escort track (tf_hud_escort.cpp): the row draws only when the map
/// has a track, the cart carries its pushers, the recede countdown, the
/// backwards chevron or the blocked mark, and the alarm paints the danger
/// border. The escort row also replaces the point icons
/// (tf_hud_objectivestatus.cpp:259-279).
/// </summary>
public class TFHudEscortTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_track_draws_only_when_the_map_has_one()
    {
        Assert.False(HasElement("round-clock-in-setup", "cart"));      // before: no track, no row
        Assert.True(HasElement("cart-rolls-down-the-track", "cart"));  // after: the cart rides
        Assert.False(HasElement("round-clock-in-setup", "escort corner"));
    }

    [Fact]
    public void The_escort_row_replaces_the_point_icons()
    {
        // A map with both reads draws the escort only (the game-type gating).
        Assert.True(HasElement("cart-rolls-down-the-track", "escort corner"));
        Assert.False(HasElement("cart-rolls-down-the-track", "cp corner"));
        Assert.False(HasElement("cart-rolls-down-the-track", "cp"));
        Assert.False(HasElement("red-caps-the-middle-point", "escort corner"));
        Assert.True(HasElement("red-caps-the-middle-point", "cp corner"));
    }

    [Fact]
    public void A_pushing_cart_shows_its_cappers()
    {
        Assert.True(HasElement("cart-rolls-down-the-track", "cappers"));
        Assert.False(HasElement("cart-waits-with-time-to-recede", "cappers"));
    }

    [Fact]
    public void A_cart_soon_to_recede_shows_the_countdown()
    {
        // Ten seconds left is inside TF_ESCORT_RECEDE_COUNTDOWN 20.
        Assert.True(HasElement("cart-waits-with-time-to-recede", "recede"));
        Assert.False(HasElement("cart-rolls-down-the-track", "recede"));
    }

    [Fact]
    public void A_receding_cart_shows_the_backwards_chevron()
    {
        Assert.True(HasElement("cart-rolls-backwards", "backwards"));
        Assert.False(HasElement("cart-rolls-down-the-track", "backwards"));
    }

    [Fact]
    public void A_blocked_track_marks_the_blocked_checkpoint_and_the_idle_cart()
    {
        Assert.True(HasElement("cart-blocked-with-nobody-pushing", "checkpoint blocked"));
        Assert.True(HasElement("cart-blocked-with-nobody-pushing", "blocked"));
        Assert.False(HasElement("cart-rolls-down-the-track", "blocked"));
    }

    [Fact]
    public void The_alarm_paints_the_cart_danger()
    {
        // The alert and pulse ride only on a multiple-train map
        // (UpdateAlarmAnimations, :656-691): a single-train map paints none.
        Assert.True(HasElement("cart-alarms", "cart alarm"));
        Assert.False(HasElement("cart-alarms-on-one-train", "cart alarm"));
        Assert.False(HasElement("cart-rolls-down-the-track", "cart alarm"));
    }

    [Fact]
    public void The_track_asks_the_library_for_the_res_art()
    {
        var art = new FakeTextureLibrary(Track, Cart, Home, PointBlue);
        Render(new FakeEscort { Distances = [0f, 0.5f, 1f], Owners = [0, 3, 0] }, art);
        Assert.Contains(Track, art.Asked);          // the LevelBar image (objectivestatusescort.res)
        Assert.Contains(Home, art.Asked);           // HomeCPIcon
        Assert.Contains(Cart, art.Asked);           // EscortItemImage (if_team_blue)
        Assert.Contains(PointBlue, art.Asked);      // the owned point (CSimpleControlPoint::UpdateImage)
        Assert.DoesNotContain(PointRed, art.Asked); // no red-owned point on the track
    }

    [Fact]
    public void A_track_with_hills_asks_the_opaque_variants()
    {
        var art = new FakeTextureLibrary(TrackOpaque, HomeOpaque, PointNeutralOpaque);
        Render(new FakeEscort { Distances = [0.5f], HillSpans = [(0.1f, 0.4f)] }, art);
        Assert.Contains(TrackOpaque, art.Asked);    // if_single_with_hills
        Assert.Contains(HomeOpaque, art.Asked);
        Assert.Contains(PointNeutralOpaque, art.Asked);
        Assert.DoesNotContain(Track, art.Asked);
    }

    [Fact]
    public void A_pushing_cart_asks_the_running_man_and_the_marks_ask_theirs()
    {
        var pushing = new FakeTextureLibrary(Man);
        Render(new FakeEscort { Distances = [0.5f], SpeedLevel = 1, Cappers = 2 }, pushing);
        Assert.Contains(Man, pushing.Asked);        // CapPlayerImage's capture_icon_white

        var backwards = new FakeTextureLibrary(Backwards);
        Render(new FakeEscort { Distances = [0.5f], SpeedLevel = -1 }, backwards);
        Assert.Contains(Backwards, backwards.Asked);  // Speed_Backwards

        var blocked = new FakeTextureLibrary(Blocked);
        Render(new FakeEscort { Distances = [0.5f], Blocks = [true] }, blocked);
        Assert.Contains(Blocked, blocked.Asked);      // the Blocked image
    }

    [Fact]
    public void A_found_handle_draws_the_art_and_a_missing_one_the_fallback()
    {
        var found = Render(new FakeEscort { Distances = [0.5f] },
                           new FakeTextureLibrary(Track, Cart));
        Assert.Equal(UiElementKind.Image, UiTestKit.Named(found, "track art").Kind);
        Assert.Equal(UiElementKind.Image, UiTestKit.Named(found, "cart art").Kind);

        var missing = Render(new FakeEscort { Distances = [0.5f] }, new FakeTextureLibrary());
        Assert.Equal(UiElementKind.Box, UiTestKit.Named(missing, "rail").Kind);
        Assert.Equal(UiElementKind.Box, UiTestKit.Named(missing, "checkpoint").Kind);
    }

    [Fact]
    public void The_track_sits_at_its_res_spot_bottom_centre()
    {
        // ObjectiveStatusEscort: xpos c-200 ypos r150, 400x150
        // (objectivestatusescort.res:3-22) — the panel's left is the screen
        // centre - 200 units (ComputePos, vgui Panel.cpp:8928-8931) and its
        // top 150 above the screen's bottom, so at 1920x1080 it spans
        // 510..1410 du with its bottom on the screen's; the LevelBar
        // 254x4 at 73,123 (:24-55) rides inside it at x2.25.
        var ctx = Render(new FakeEscort { Distances = [0f, 0.5f, 1f] }, new FakeTextureLibrary());
        var root = UiTestKit.Named(ctx, "escort corner").Rect;
        Assert.Equal(510f, root.X, 0.5f);      // 960 - 200 x 2.25
        Assert.Equal(742.5f, root.Y, 0.5f);    // 1080 - 150 x 2.25
        Assert.Equal(900f, root.W, 0.5f);      // 400 x 2.25
        Assert.Equal(337.5f, root.H, 0.5f);    // 150 x 2.25
        var rail = UiTestKit.Named(ctx, "rail").Rect;
        Assert.Equal(674.25f, rail.X, 0.5f);   // 510 + 73 x 2.25
        Assert.Equal(1019.25f, rail.Y, 0.5f);  // 1080 - (123+4) x 2.25
        Assert.Equal(571.5f, rail.W, 0.5f);    // 254 x 2.25
        Assert.Equal(9f, rail.H, 0.5f);        // 4 x 2.25
    }

    [Fact]
    public void The_cart_rides_the_bar_centred_at_its_progress()
    {
        // The item panel slides with the cart: SetPos newX = iBarX +
        // iBarW*progress - w/2 (tf_hud_escort.cpp:760-766), so at progress
        // 0.4 its origin is (73 + 254*0.4 - 40) x 2.25 = 302.85 du into the
        // root; the cart image (EscortItemImage 40x40 at 20,77 of
        // EscortItemPanel, objectivestatusescort.res:244-283) is then
        // centred on the bar at the progress (bar centre 902.85 du), its
        // bottom 2 units under the bar line; CapPlayerImage rides 6x12 at
        // +33 of the panel (its res x 33 against the cart's 20).
        var ctx = Render(new FakeEscort { Progress = 0.4f, Cappers = 2, SpeedLevel = 1, Distances = [0f, 0.5f, 1f] },
                         new FakeTextureLibrary("capture_icon_white"));
        var cart = UiTestKit.Named(ctx, "cart").Rect;
        Assert.Equal(812.85f, cart.X, 0.5f);   // 510 + (73 + 254*0.4 - 40) x 2.25
        Assert.Equal(760.5f, cart.Y, 0.5f);    // panel top: 1080 - 60.75 - 115 x 2.25
        Assert.Equal(180f, cart.W, 0.5f);      // 80 x 2.25
        var art = UiTestKit.Named(ctx, "cart fallback").Rect;
        Assert.Equal(857.85f, art.X, 0.5f);    // bar centre 902.85 - 45
        Assert.Equal(933.75f, art.Y, 0.5f);    // 1080 - 25 x 2.25 - 40 x 2.25
        Assert.Equal(90f, art.W, 0.5f);        // 40 x 2.25
        var man = UiTestKit.Named(ctx, "cap man art").Rect;
        Assert.Equal(cart.X + 74.25f, man.X, 0.5f);   // +33 x 2.25
        Assert.Equal(940.5f, man.Y, 0.5f);            // panel y 50, x2.25
    }

    private const string Track = "hud/cart_track";
    private const string TrackOpaque = "hud/cart_track_neutral_opaque";
    private const string Home = "hud/cart_home_blue";
    private const string HomeOpaque = "hud/cart_home_blue_opaque";
    private const string Cart = "hud/cart_blue";
    private const string PointBlue = "hud/cart_point_blue";
    private const string PointRed = "hud/cart_point_red";
    private const string PointNeutralOpaque = "hud/cart_point_neutral_opaque";
    private const string Man = "capture_icon_white";
    private const string Backwards = "hud/cart_arrow_left";
    private const string Blocked = "hud/cart_blocked";

    /// <summary>One escort-shaped map rendered over the fake library,
    /// like the preview's samples are.</summary>
    private static UiContext Render(FakeEscort escort, FakeTextureLibrary art)
    {
        var screen = TFHudSamples.Build(new UiSampleServices(), escort: escort, textures: art);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 100.0);
        return ctx;
    }

    /// <summary>Whether the named part drew on the sample's screen.</summary>
    private static bool HasElement(string slug, string name)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);
    }
}
