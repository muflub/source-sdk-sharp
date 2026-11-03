using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The control-point icons (hud_controlpointicons.cpp): the row draws only
/// when the map has points, each icon asks the library for the owner's
/// obj_icons material (the locked variant while no team may cap it), a cap
/// in progress shows the capping team's swipe and its cappers, a blocked
/// cap is marked, and a point off the round draws nothing.
/// </summary>
public class TFHudCpIconsTests
{
    private static readonly UiFontLibrary Fonts = new();

    private const int Blue = 3, Red = 2;
    private const string BluBase = "sprites/obj_icons/icon_obj_blu";
    private const string RedSwipe = "sprites/obj_icons/icon_obj_cap_red";
    private const string RedSwipeUp = "sprites/obj_icons/icon_obj_cap_red_up";
    private const string BluSwipe = "sprites/obj_icons/icon_obj_cap_blu";
    private const string NeutralLocked = "sprites/obj_icons/icon_obj_neutral_locked";
    private const string Capper = "vgui/capture_icon";
    private const string TimerArt = "sprites/obj_icons/icon_obj_timer";

    [Fact]
    public void The_point_icons_draw_only_when_the_map_has_points()
    {
        Assert.Equal(0, CountIcons("round-clock-in-setup"));           // before: no points, no row
        Assert.Equal(1, CountIcons("blu-owns-the-middle-point"));      // after: the owned point
    }

    [Fact]
    public void A_cap_in_progress_shows_the_swipe_and_its_cappers()
    {
        // At rest (the sample's fake answers nothing) no swipe draws; a
        // cap over an answering library draws the swipe and the cappers.
        var atRest = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue] },
                            new FakeTextureLibrary());
        Assert.Throws<InvalidOperationException>(() => UiTestKit.Named(atRest, "cap swipe"));

        var capping = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue], CappingTeams = [Red], CapperCounts = [[0, 0, 2]] },
                             new FakeTextureLibrary(RedSwipe, Capper));
        Assert.Equal(UiElementKind.Image, UiTestKit.Named(capping, "cap swipe").Kind);
        Assert.Contains("cappers", UiTestKit.Elements(capping).Select(e => UiTestKit.NameOf(capping, e)));
    }

    [Fact]
    public void A_blocked_cap_is_marked()
    {
        Assert.True(HasElement("red-caps-the-middle-point", "cp"));
        Assert.False(HasElement("red-caps-the-middle-point", "cp blocked"));
        Assert.True(HasElement("red-caps-and-nobody-pushes", "cp blocked"));
    }

    [Fact]
    public void A_locked_point_draws_the_lock_and_an_off_round_point_draws_nothing()
    {
        // The pyramid's layout names points 3, 1 and 2; point 0 is not on it.
        Assert.True(HasElement("pyramid-with-a-locked-point", "locked"));
        Assert.Equal(3, CountIcons("pyramid-with-a-locked-point"));
        Assert.False(HasElement("pyramid-with-a-point-off-the-round", "locked"));
        Assert.Equal(2, CountIcons("pyramid-with-a-point-off-the-round"));   // the third sits the round out
    }

    [Fact]
    public void The_icons_ask_the_library_for_the_res_art()
    {
        var art = new FakeTextureLibrary(BluBase, RedSwipe, Capper);
        Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue] }, art);
        Assert.Contains(BluBase, art.Asked);                      // the owner's base image (UpdateImage :294-307)
        Assert.DoesNotContain(RedSwipe, art.Asked);               // no cap running: no swipe

        Render(new FakeCapturePoints
        {
            NumPoints = 1,
            Owner = [Blue],
            CappingTeams = [Red],
        }, art);
        Assert.Contains(RedSwipe, art.Asked);                     // the capping team's swipe (UpdateCapImage :357-365)
        Assert.Contains(Capper, art.Asked);                       // CapPlayerImage, the running man
    }

    [Fact]
    public void A_locked_point_asks_the_locked_variant()
    {
        var art = new FakeTextureLibrary(NeutralLocked);
        Render(new FakeCapturePoints { NumPoints = 1, Locks = [true] }, art);
        Assert.Contains(NeutralLocked, art.Asked);
        Assert.DoesNotContain("sprites/obj_icons/icon_obj_neutral", art.Asked);
    }

    [Fact]
    public void A_multi_row_layout_asks_the_up_swipe()
    {
        var art = new FakeTextureLibrary();
        Render(new FakeCapturePoints
        {
            NumPoints = 2,
            CapLayout = "0,1",                     // two lines: the swipe comes from above
            Owner = [Blue],
            CappingTeams = [Red],
        }, art);
        Assert.Contains(RedSwipeUp, art.Asked);
    }

    [Fact]
    public void The_point_timer_counts_down_and_hides_with_none()
    {
        var art = new FakeTextureLibrary(TimerArt);
        var counting = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue], TimerLeft = [7.2f] }, art);
        Assert.Equal("8", Digits(counting, "cp timer"));   // ceil(7.2), OnTick :587-591
        Assert.Equal(UiElementKind.Image, UiTestKit.Named(counting, "cp timer bg").Kind);
        Assert.Contains(TimerArt, art.Asked);              // CPTimerBG's icon_obj_timer

        Assert.Throws<InvalidOperationException>(() =>
            Digits(Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue] }, art), "cp timer"));
        Assert.Throws<InvalidOperationException>(() =>
            Digits(Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue], TimerLeft = [-3f] }, art), "cp timer"));
    }

    [Fact]
    public void The_point_timer_flashes_red_with_ten_or_fewer_left()
    {
        // The label flips on each 100 ms tick once the countdown is at ten
        // or below (OnTick :593-604): the even tenth is the regular
        // TanLight, the next one the flash — stock's RedSolid read as the
        // theme's Danger (the panel's note).
        Assert.Equal(TanLight, DigitColour(Render(
            new FakeCapturePoints { NumPoints = 1, Owner = [Blue], TimerLeft = [7.2f] },
            new FakeTextureLibrary(TimerArt), 100.0), "cp timer"));
        Assert.Equal(new UiColor(255, 78, 64, 255), DigitColour(Render(
            new FakeCapturePoints { NumPoints = 1, Owner = [Blue], TimerLeft = [7.2f] },
            new FakeTextureLibrary(TimerArt), 100.1), "cp timer"));
    }

    [Fact]
    public void The_unlock_countdown_shows_only_its_last_five_seconds()
    {
        var counting = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue], UnlockLeft = [3.4f] },
                              new FakeTextureLibrary());
        Assert.Equal("3", Digits(counting, "cp unlock"));  // the whole seconds left (:1873-1876)
        Assert.Throws<InvalidOperationException>(() =>
            Digits(Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue], UnlockLeft = [6.5f] },
                          new FakeTextureLibrary()), "cp unlock"));
        Assert.Throws<InvalidOperationException>(() =>
            Digits(Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue], UnlockLeft = [0f] },
                          new FakeTextureLibrary()), "cp unlock"));
    }

    [Fact]
    public void The_cap_swipe_samples_the_window_the_cap_percentage_names()
    {
        // CControlPointIconCapArrow::Paint (hud_controlpointicons.cpp:1672-1731):
        // the arrow is a 15/69 head before a 54/69 body, and the window slides
        // across the 33-unit icon with the cap percentage — left to right for
        // the red team (even parity), the other way for blue.
        float head = 15f / 69f, move = 48f / 69f;
        float xa = 0.9f + move + 0.5f * ((1.1f - head) - (0.9f + move));
        float xb = 0.9f + 0.5f * (0f - 0.9f);

        Assert.Equal(new UiRect(xa, 0f, xb - xa, 1f),
                     Window(Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue], CappingTeams = [Red], Progress = [0.5f] },
                                   new FakeTextureLibrary(RedSwipe)), "cap swipe"));
        Assert.Equal(new UiRect(xb, 0f, xa - xb, 1f),
                     Window(Render(new FakeCapturePoints { NumPoints = 1, Owner = [Red], CappingTeams = [Blue], Progress = [0.5f] },
                                   new FakeTextureLibrary(BluSwipe)), "cap swipe"));
    }

    [Fact]
    public void The_swipe_window_rises_over_a_multi_row_layout()
    {
        // ShouldSwipeUp (hud_controlpointicons.cpp:1708-1711): the window
        // climbs the texture bottom-up, indented 0.07 either side.
        float head = 15f / 69f, move = 48f / 69f;
        float ya = (-move - 0.07f) + 0.5f * ((head - 0.07f) - (-move - 0.07f));
        float yb = 0.07f + 0.5f * ((move - 0.07f) - 0.07f);

        Assert.Equal(new UiRect(0f, ya, 1f, yb - ya),
                     Window(Render(new FakeCapturePoints
                     {
                         NumPoints = 2,
                         CapLayout = "0,1",
                         Owner = [Blue],
                         CappingTeams = [Red],
                         Progress = [0.5f],
                     }, new FakeTextureLibrary(RedSwipeUp)), "cap swipe"));
    }

    [Fact]
    public void A_found_handle_draws_the_art_and_a_missing_one_the_fallback()
    {
        var found = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue] },
                           new FakeTextureLibrary(BluBase));
        Assert.Equal(UiElementKind.Image, UiTestKit.Named(found, "cp art").Kind);

        var missing = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue] },
                             new FakeTextureLibrary());
        Assert.Equal(UiElementKind.Box, UiTestKit.Named(missing, "cp fallback").Kind);
    }

    [Fact]
    public void The_icons_sit_at_the_bottom_centre()
    {
        // HudControlPointIcons draws the icons' bottoms separator_height 7
        // units above the screen's bottom (height_offset 0,
        // hudlayout.res:838-850), each line centred with a trailing
        // separator (hud_controlpointicons.cpp:1114-1155): one icon sits
        // half a 9-unit gap left of the centre — at 1920, 960 - 47.25 du —
        // its bottom 15.75 du up.
        var ctx = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue] }, new FakeTextureLibrary());
        var icon = UiTestKit.Named(ctx, "cp").Rect;
        Assert.Equal(912.75f, icon.X, 0.5f);    // 960 - (33+9)/2 x 2.25
        Assert.Equal(990f, icon.Y, 0.5f);       // 1080 - (33+7) x 2.25
        Assert.Equal(74.25f, icon.W, 0.5f);     // 33 x 2.25
        Assert.Equal(74.25f, icon.H, 0.5f);
    }

    [Fact]
    public void The_teardrop_draws_only_over_the_touched_point()
    {
        // Nobody on a point (the samples' default fake): no panel at all.
        var none = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue] }, new FakeTextureLibrary());
        Assert.Throws<InvalidOperationException>(() => UiTestKit.Named(none, "cap progress"));

        var touched = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue] },
                             new FakeTextureLibrary(),
                             progress: new FakeCapProgress { Touched = 0, Bars = [true], Progresses = [0.4f] });
        Assert.Equal(UiElementKind.Box, UiTestKit.Named(touched, "cap progress").Kind);
    }

    [Fact]
    public void The_teardrop_hangs_centred_over_its_point()
    {
        // controlpointprogressbar.res: the 100x65 panel over the icon, its
        // centre on the icon's centre, its bottom on the icon's top
        // (640-space x 2.25 du).
        var ctx = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue] },
                         new FakeTextureLibrary(),
                         progress: new FakeCapProgress { Touched = 0, Bars = [true], Progresses = [0.4f] });
        var panel = UiTestKit.Named(ctx, "cap progress").Rect;
        var icon = UiTestKit.Named(ctx, "cp").Rect;
        Assert.Equal(icon.X + icon.W / 2 - panel.W / 2, panel.X, 0.5f);
        Assert.Equal(icon.Y - panel.H, panel.Y, 0.5f);
        Assert.Equal(225f, panel.W, 0.5f);
        Assert.Equal(146.25f, panel.H, 0.5f);
    }

    [Fact]
    public void A_running_local_cap_sweeps_and_a_refused_one_names_why()
    {
        // The bar arm (SetupForPoint :1519): the sweep and its track, no
        // text. The text arm: the blocked mark and the ladder's token, no
        // sweep.
        var filling = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue], CappingTeams = [Red] },
                             new FakeTextureLibrary(),
                             progress: new FakeCapProgress { Touched = 0, Bars = [true], Progresses = [0.4f] });
        UiTestKit.Named(filling, "cap bar");
        UiTestKit.Named(filling, "cap bar track");
        Assert.Throws<InvalidOperationException>(() => UiTestKit.Named(filling, "cap text"));

        var refused = Render(new FakeCapturePoints { NumPoints = 1, Owner = [Blue], CappingTeams = [Red], Blocks = [true] },
                             new FakeTextureLibrary(),
                             progress: new FakeCapProgress { Touched = 0, Bars = [false], Texts = ["#Team_Capture_Blocked"] });
        UiTestKit.Named(refused, "cap blocked");
        Assert.Contains("apture", Digits(refused, "cap text"));
        Assert.Throws<InvalidOperationException>(() => UiTestKit.Named(refused, "cap bar"));
    }

    /// <summary>One owned-and-capping-shaped map rendered over the fake
    /// library, like the preview's samples are.</summary>
    private static UiContext Render(FakeCapturePoints points, FakeTextureLibrary art,
                                    double now = 100.0, FakeCapProgress? progress = null)
    {
        var screen = TFHudSamples.Build(new UiSampleServices(), points: points, progress: progress, textures: art);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), now);
        return ctx;
    }

    /// <summary>The digits the named countdown drew.</summary>
    private static string Digits(UiContext ctx, string key)
    {
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == key)
                return UiTestKit.RunText(ctx, i);
        throw new InvalidOperationException($"no {key} drew");
    }

    /// <summary>The named countdown's glyph colour.</summary>
    private static UiColor DigitColour(UiContext ctx, string key)
    {
        var list = ctx.List.AsDrawList();
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == key)
                foreach (var op in list.Ops)
                    if (op.Kind == UiPrimitiveKind.GlyphRun && op.Element == i)
                        return list.GlyphRuns[op.Index].Color;
        throw new InvalidOperationException($"no {key} glyphs drew");
    }

    /// <summary>The named textured quad's UV window.</summary>
    private static UiRect Window(UiContext ctx, string key)
    {
        var list = ctx.List.AsDrawList();
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == key)
                foreach (var op in list.Ops)
                    if (op.Kind == UiPrimitiveKind.TexturedQuad && op.Element == i)
                        return list.TexturedQuads[op.Index].Uv;
        throw new InvalidOperationException($"no {key} quad drew");
    }

    // The countdown digits' regular scheme colour (hud_controlpointicons.cpp:154).
    private static readonly UiColor TanLight = new(235, 226, 202, 255);

    /// <summary>Whether the named part drew on the sample's screen.</summary>
    private static bool HasElement(string slug, string key)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == key);
    }

    /// <summary>How many point icons drew (locked and blocked ones count;
    /// they are named <c>cp</c> too, the blocked one <c>cp blocked</c>).</summary>
    private static int CountIcons(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Count(e => UiTestKit.NameOf(ctx, e) == "cp");
    }
}
