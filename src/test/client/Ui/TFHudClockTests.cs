using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The round clock's stock rules (<c>CTFHudTimeStatus::OnThink</c>,
/// tf_time_panel.cpp:674-814): the <c>m:ss</c> of the seconds on show
/// (:720-724), the count-up choice for a timer that does not show the
/// remaining (:696-699), the clamp at zero (:708-717), and the state
/// bands only over their own states (:479-486, :510-571). The match HUD
/// arms carry no art of their own: the if_match blocks hide the plain
/// TimePanelBG box and progress ring (hudobjectivetimepanel.res:24-27,
/// :54-57) and the backdrop hangs in the match status layers.
/// </summary>
public class TFHudClockTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_clock_shows_minutes_and_zero_padded_seconds()
    {
        Assert.Equal("1:45", ClockText("round-clock-counting-down"));   // 105 s as "%d:%02d" (:720-724)
    }

    [Fact]
    public void A_timer_that_hides_the_remaining_shows_the_elapsed_seconds()
    {
        // 600 − 540 elapsed (:696-699): one minute up, not nine to go.
        Assert.Equal("1:00", ClockText("round-clock-counting-up"));
    }

    [Fact]
    public void The_overrun_clamps_at_zero_and_shows_the_overtime_label()
    {
        Assert.Equal("1:45", ClockText("round-clock-counting-down"));   // before: the clock runs
        Assert.Equal("0:00", ClockText("round-clock-in-overtime"));     // after: never below zero (:708-717)
        Assert.False(TFHudTests.HasElement("round-clock-counting-down", "overtime"));
        Assert.True(TFHudTests.HasElement("round-clock-in-overtime", "overtime"));     // :510-571
    }

    [Fact]
    public void The_setup_label_shows_only_over_the_pre_round()
    {
        Assert.False(TFHudTests.HasElement("round-clock-counting-down", "setup"));
        Assert.True(TFHudTests.HasElement("round-clock-in-setup", "setup"));           // :479-486
        Assert.True(TFHudTests.HasElement("round-clock-in-setup", "clock"));           // and it keeps counting
    }

    [Fact]
    public void The_overtime_label_seesaws_tan_to_red()
    {
        // OvertimeLabelPulseRed (hudanimations_tf.txt:551-557): RedSolid up
        // to TanLight and back, 0.3 s each way on the 0.6 s loop, the ramps
        // linear (RedSolid clientscheme.res:49, TanLight :100).
        Assert.Equal(RedSolid, LabelColour(0.0));
        Assert.Equal(TanLight, LabelColour(0.3));
        Assert.Equal(RedSolid, LabelColour(0.6));
        var mid = LabelColour(0.45);                   // halfway on the back ramp
        Assert.True(mid.R > RedSolid.R && mid.R < TanLight.R, $"{mid.R}: not the ramp's midpoint");
    }

    [Fact]
    public void The_digits_draw_HudFontSmall_and_the_bands_HudFontSmallestBold()
    {
        // hudmatchstatus.res:352 sizes the value HudFontSmall: stock's 14
        // tall at its base screen, ~13 tall (29.25 du) once the hud scales
        // the face down with a 720-line screen — the step between Smallest
        // and Small. hudobjectivetimepanel.res's if_match bands take
        // HudFontSmallestBold (the ClockSubText step, 20.25 du, for the
        // condensed bold's width at stock's sizes) on the hud's bold display
        // family, not the body face the label role carries.
        var theme = SourceSharpTheme.Create();
        var ctx = Render(new FakeRoundTimer { Present = true, WaitingForPlayers = true,
                                              ShowTimeRemaining = true, SecondsLeft = 30, TotalSeconds = 600 }, theme: theme);
        var list = ctx.List.AsDrawList();
        var digits = GlyphFont(ctx, list, "clock");
        var band = GlyphFont(ctx, list, "waiting");
        Assert.Equal(29, digits.PxHeight);             // 29.25 du: HudFontSmall ~13 x 2.25 at 720 lines
        Assert.Equal(22, band.PxHeight);               // 21.5 du: the band's condensed bold at ~9.5 tall at 720 lines
        Assert.Equal(theme.Type(Text.Heading).Family, band.Family);
        Assert.Equal(theme.Type(Text.Label).Family, digits.Family);
    }

    /// <summary>The font of the first glyph run on the named element.</summary>
    private static UiFontSpec GlyphFont(UiContext ctx, UiDrawList list, string key)
    {
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == key)
                foreach (var op in list.Ops)
                    if (op.Kind == UiPrimitiveKind.GlyphRun && op.Element == i)
                        return list.Fonts[list.GlyphRuns[op.Index].Font];
        throw new InvalidOperationException($"no {key} glyph run drew");
    }

    [Fact]
    public void The_match_panel_draws_no_art_of_its_own()
    {
        // The if_match blocks hide the TimePanelBG box and the progress ring
        // (hudobjectivetimepanel.res:24-27, :54-57): the backdrop and centre
        // art hang in the match status layers, not here.
        var art = new FakeTextureLibrary();
        Render(new FakeRoundTimer { Present = true, Overtime = true, ShowTimeRemaining = true, SecondsLeft = -5, TotalSeconds = 600 }, art);
        Assert.DoesNotContain("hud/objectives_timepanel_blue_bg", art.Asked);
        Assert.DoesNotContain("hud/objectives_timepanel_suddendeath", art.Asked);
        var ctx = Render(new FakeRoundTimer { Present = true, ShowTimeRemaining = true, SecondsLeft = 105, TotalSeconds = 600 });
        Assert.Throws<InvalidOperationException>(() => UiTestKit.Named(ctx, "timer bg"));
        Assert.Throws<InvalidOperationException>(() => UiTestKit.Named(ctx, "timer progress"));
    }

    [Fact]
    public void The_clock_sits_at_its_res_spot()
    {
        // ObjectiveStatusTimePanel is the clock's own 110x150 panel, xpos
        // c-55 ypos 0 (hudmatchstatus.res:276-292): ComputePos sets the
        // element's x to the parent centre + N (vgui Panel.cpp:8928-8931),
        // so on the 1920 canvas the panel's left is 960 - 55x2.25 = 836.25
        // du and its top the screen's. Under if_match the value is centred,
        // 45x10 at y12 (hudmatchstatus.res:342-353), and the band label is
        // the panel's full width at y21 (hudobjectivetimepanel.res if_match).
        var ctx = Render(new FakeRoundTimer { Present = true, InSetup = true, ShowTimeRemaining = true, SecondsLeft = 30, TotalSeconds = 600 });
        var digits = UiTestKit.Named(ctx, "clock").Rect;
        Assert.Equal(909.375f, digits.X, 0.5f);   // 836.25 + (110 - 45)/2 x 2.25
        Assert.Equal(27f, digits.Y, 0.5f);        // 12 x 2.25
        var band = UiTestKit.Named(ctx, "setup").Rect;
        Assert.Equal(830f, band.X, 0.5f);         // centred on the panel, the 6-du slack half each side
        Assert.Equal(47.25f, band.Y, 0.5f);       // 21 x 2.25
        Assert.Equal(260f, band.W, 0.5f);         // the panel's width plus the slack, as laid out
        Assert.Equal(42.75f, band.H, 0.5f);       // the res's tall 19 through if_match: the label box,
                                                  // its text centred in it as stock paints (hudobjectivetimepanel.res:74-81)
    }

    [Fact]
    public void The_sudden_death_band_shows_only_over_a_stalemate()
    {
        Assert.False(TFHudTests.HasElement("round-clock-counting-down", "sudden death"));
        Assert.True(TFHudTests.HasElement("round-clock-in-sudden-death", "sudden death"));   // :489-508
        Assert.False(TFHudTests.HasElement("round-clock-in-overtime", "sudden death"));      // overtime hides it :547-554
    }

    [Fact]
    public void Waiting_for_players_hides_the_setup_band()
    {
        // "can't be waiting *AND* in setup" (tf_time_panel.cpp:578-588):
        // the sample has a setup timer up, and the wait wins.
        Assert.True(TFHudTests.HasElement("round-clock-waiting-for-players", "waiting"));
        Assert.False(TFHudTests.HasElement("round-clock-waiting-for-players", "setup"));
        Assert.False(TFHudTests.HasElement("round-clock-counting-down", "waiting"));
    }

    [Fact]
    public void The_server_time_band_shows_only_when_asked_and_the_limit_is_on()
    {
        // tf_hud_show_servertimelimit off by default: no band.
        Assert.False(TFHudTests.HasElement("round-clock-counting-down", "server limit"));
        // On, the timer stating a max length and mp_timelimit up: the band
        // counts GetTimeLeft down as m:ss (tf_time_panel.cpp:791-804).
        Assert.True(TFHudTests.HasElement("round-clock-server-time-limit", "server limit"));
        Assert.Equal("12:35", BandText(new FakeRoundTimer { Present = true, ShowTimeRemaining = true, SecondsLeft = 105,
                                                            TotalSeconds = 600, ServerTimeLimit = 1200,
                                                            ServerTimeLeft = 755, ShowServerLimit = true }));
        // A spent limit names its change-on-round-end arm (:784-789); an
        // hour or more names the h:mm:ss arm (:806).
        Assert.Equal("CHANGES ON ROUND END", BandText(new FakeRoundTimer { Present = true, ShowTimeRemaining = true,
                                                                           SecondsLeft = 100, TotalSeconds = 600,
                                                                           ServerTimeLimit = 600, ServerTimeLeft = 0,
                                                                           ShowServerLimit = true }));
        Assert.Equal("1:02:35", BandText(new FakeRoundTimer { Present = true, ShowTimeRemaining = true, SecondsLeft = 105,
                                                              TotalSeconds = 600, ServerTimeLimit = 7200,
                                                              ServerTimeLeft = 3755, ShowServerLimit = true }));
    }

    [Fact]
    public void A_stop_watch_still_shows_the_digits_without_a_ring()
    {
        // m_bStopWatchTimer hides the progress bar (tf_time_panel.cpp
        // :447-452); under the match HUD the ring is gone for every state,
        // the digits alone.
        var ctx = Render(new FakeRoundTimer { Present = true, ShowTimeRemaining = true, SecondsLeft = 105, TotalSeconds = 600, StopWatch = true });
        Assert.Throws<InvalidOperationException>(() => UiTestKit.Named(ctx, "timer progress"));
        Assert.NotEmpty(UiTestKit.RunText(ctx, IndexOf(ctx, "clock")));
    }

    /// <summary>The element index keyed <paramref name="key"/>.</summary>
    private static int IndexOf(UiContext ctx, string key)
    {
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == key)
                return i;
        throw new InvalidOperationException($"no {key} drew");
    }

    /// <summary>One frame of the tfhud screen over the given timer and art
    /// fake, built the way the samples are.</summary>
    private static UiContext Render(FakeRoundTimer timer, FakeTextureLibrary? art = null, UiTheme? theme = null)
    {
        var s = new UiSampleServices();
        var screen = TFHudSamples.Build(s, timer: timer, textures: art);
        var ctx = UiSamples.PreviewContext(theme ?? SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 100.0);
        return ctx;
    }

    /// <summary>The overtime label's colour at the pulse's now: one render
    /// of the overtime state at <paramref name="now"/> with the band art
    /// found (the pulse rides the art; the fallback keeps the digits'
    /// tone), the label's glyph run read back the way
    /// <see cref="UiTestKit.RunText"/> reads text.</summary>
    private static UiColor LabelColour(double now)
    {
        var s = new UiSampleServices();
        var screen = TFHudSamples.Build(s,
            timer: new FakeRoundTimer { Present = true, Overtime = true, ShowTimeRemaining = true, SecondsLeft = -5, TotalSeconds = 600 },
            textures: new FakeTextureLibrary("hud/objectives_timepanel_suddendeath"));
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), now);
        var list = ctx.List.AsDrawList();
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == "overtime")
                foreach (var op in list.Ops)
                    if (op.Kind == UiPrimitiveKind.GlyphRun && op.Element == i)
                        return list.GlyphRuns[op.Index].Color;
        throw new InvalidOperationException("no overtime label drew");
    }

    // The pulse's two scheme colours (clientscheme.res:49 RedSolid, :100 TanLight).
    private static readonly UiColor RedSolid = new(192, 28, 0, 255);
    private static readonly UiColor TanLight = new(235, 226, 202, 255);

    /// <summary>The text the sample's clock drew.</summary>
    private static string ClockText(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == "clock")
                return UiTestKit.RunText(ctx, i);
        throw new InvalidOperationException($"{slug}: no clock drew");
    }

    /// <summary>The server-time band's label, over the given timer.</summary>
    private static string BandText(FakeRoundTimer timer)
    {
        var ctx = Render(timer);
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == "server limit")
                return UiTestKit.RunText(ctx, i);
        throw new InvalidOperationException("no server limit band drew");
    }
}
