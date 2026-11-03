using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The TF2 scoreboard's stock rules, held over the samples: the class
/// column's gate (<c>!AreEnemyTeams(row, local) &amp;&amp; local !=
/// TEAM_UNASSIGNED</c>, tf_clientscoreboard.cpp:1467-1468), the spectator
/// line of <c>UpdateSpectatorList</c> (:1646-1705), the server-time box
/// between the bands (<c>TimerBG</c>, UpdateServerTimeLeft :2031-2114), and
/// the head-count token pair
/// (TF_ScoreBoard_Player/Players, tf_english.txt:59-60).
/// </summary>
public class TFScoreboardTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_class_falls_back_to_the_name_where_the_gate_allows()
    {
        // With the art library missing, the class name stands in where the
        // gate allows (the local team's rows): the RED rows' Medic, Sniper,
        // Spy, Scout, Soldier and Pyro of the fake's indices 7-12; the BLU
        // rows' Demoman, Heavy and Engineer do not, though the data holds
        // them. The gate's icon arms are TFScoreboardArtTests'. The longer
        // names elide to the 15-px cell's leading fragment.
        Assert.True(HasTextFrom("two-teams-and-the-clock", "Me"));
        Assert.True(HasTextFrom("two-teams-and-the-clock", "Sol"));
        Assert.False(HasText("two-teams-and-the-clock", "Demoman"));
        Assert.False(HasText("two-teams-and-the-clock", "Heavy"));
        Assert.False(HasText("two-teams-and-the-clock", "Engineer"));

        // A spectator is on no team: stock's gate arms to every row.
        Assert.True(HasTextFrom("spectating", "Eng"));
    }

    [Fact]
    public void The_spectator_line_names_them_and_hides_when_there_are_none()
    {
        Assert.True(HasText("two-teams-with-spectators", "SPECTATORS:"));
        Assert.True(HasText("two-teams-with-spectators",
                            "Uriah, Magnusson, xXx_Headcrab_Slayer_From_Ravenholm_xXx"));
        Assert.False(HasText("two-teams-and-the-clock", "SPECTATORS:"));
    }

    [Fact]
    public void The_server_time_box_counts_down_between_the_bands()
    {
        Assert.True(HasText("two-teams-and-the-clock", "6:11"));     // 371 s
        Assert.False(HasText("no-round-timer", "6:11"));
        Assert.True(HasText("server-time-in-hours", "1:15:00"));     // 4500 s
    }

    [Fact]
    public void The_server_time_box_names_a_server_without_a_limit()
        => Assert.True(HasText("no-round-timer", "NO TIME LIMIT"));

    [Theory]
    [InlineData("one-player-each", "player")]
    [InlineData("two-teams-and-the-clock", "players")]
    public void The_head_count_takes_the_stock_token_pair(string slug, string expected)
        => Assert.True(HasText(slug, expected), $"no {expected} on {slug}");

    [Fact]
    public void The_bottom_bar_names_the_local_player_the_server_and_the_map()
    {
        Assert.True(HasText("two-teams-and-the-clock", "Vortigaunt"));   // the local row's name
        Assert.True(HasText("two-teams-and-the-clock", "SourceSharp TF2 | ctf_2fort 24/7"));
        Assert.True(HasText("two-teams-and-the-clock", "ctf_2fort"));
    }

    [Fact]
    public void An_empty_server_still_draws_the_bands_but_no_rows()
    {
        Assert.True(HasText("empty-teams", "players"));   // 0 heads, plural, mixed as spoken
        Assert.False(HasText("empty-teams", "Gordon"));   // nobody to list
    }

    [Fact]
    public void The_band_is_the_teams_colour_with_the_score_huge_at_the_inner_edge()
    {
        // Stock's band: the BlueScoreBG/RedScoreBG art is the team colour
        // itself, the score the largest ink on it at the inner edge
        // (BlueTeamScore east-aligned, RedTeamScore west, scoreboard.res
        // 212, 299, y12 h55 of the 71 band) and the name the smaller role.
        var ctx = Render("two-teams-and-the-clock");
        var blue = UiTestKit.Named(ctx, "tf band blue");
        var red = UiTestKit.Named(ctx, "tf band red");
        var blueScore = UiTestKit.Named(ctx, "tf score blue");
        var redScore = UiTestKit.Named(ctx, "tf score red");
        var blueCount = UiTestKit.Named(ctx, "tf count blue");

        Assert.True(blueScore.Rect.X > blue.Rect.X + blue.Rect.W / 2,
                    "the blue score is not at the band's inner (right) edge");
        Assert.True(redScore.Rect.X + redScore.Rect.W < red.Rect.X + red.Rect.W / 2,
                    "the red score is not at the band's inner (left) edge");
        Assert.True(blueScore.Rect.H > blueCount.Rect.H,
                    "the score does not stand taller than the count");
        foreach (var (score, band) in new[] { (blueScore, blue), (redScore, red) })
        {
            float centre = score.Rect.Y + score.Rect.H / 2f;
            Assert.True(centre > band.Rect.Y && centre < band.Rect.Y + band.Rect.H,
                        "the score is not inside its band's height");
        }
    }

    [Fact]
    public void The_bands_sit_at_the_centred_plate_and_the_timer_hangs_above()
    {
        // Stock centres the dialog on the screen (MoveToCenterOfScreen,
        // ClientScoreBoardDialog.cpp:297 and 580-587), so the 448-tall
        // root's top is (480 - 448) / 2 = 16 units of the screen, not the
        // res's ypos 31 — the bands' own ypos 9 below it makes 25 units,
        // 56.25 du. The bands' row is the bands' height: the server-time
        // box hangs its 12 units above the band tops (TimerBG y-3 against
        // y9) out of the row, and the bands sit at the row's top, not
        // centred in the timer cell's reach.
        const float unit = 2.25f;
        var ctx = Render("two-teams-and-the-clock");
        var blue = UiTestKit.Named(ctx, "tf band blue").Rect;
        var red = UiTestKit.Named(ctx, "tf band red").Rect;
        var timer = UiTestKit.Named(ctx, "tf server time").Rect;
        Assert.Equal(25f * unit, blue.Y, 2f);
        Assert.Equal(25f * unit, red.Y, 2f);
        Assert.Equal((25f - 12f) * unit, timer.Y, 2f);   // the hang above the band tops
        Assert.Equal(25f * unit + 71f * unit, blue.Y + blue.H, 2f);
    }

    [Fact]
    public void The_chrome_leaves_the_plate_unwashed_and_the_artless_case_keeps_its_plate()
    {
        // Stock's dialog paints no background of its own — SetBgColor
        // (0,0,0,0), "light up scoreboard a bit" (ClientScoreBoardDialog.cpp
        // :193-194): the MainBG chrome's own translucent centre is the fill,
        // and a plate under it doubles the darkening. The Surface wash is
        // the artless case's stand-in only.
        var artless = Render("two-teams-and-the-clock");
        Assert.Equal(1, Washes(artless));

        var withArt = RenderWithArt("two-teams-and-the-clock");
        Assert.Equal(0, Washes(withArt));
    }

    private static int Washes(UiContext ctx)
    {
        int count = 0;
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == "tf scoreboard wash")
                count++;
        return count;
    }

    [Fact]
    public void The_scoreboard_covers_the_hud_as_a_hud_screen()
    {
        // Stock's scoreboard is a hud dialog drawn over the hud — the
        // team-goal hint included — while the game under it keeps rendering
        // (tf_clientscoreboard.cpp:361-362 paints the dialog transparently
        // in the hud pass): the composition drops the core's menu-layer
        // scoreboard and lands this screen on the hud's tail, after TFHud.
        var scoreboard = Sample("two-teams-and-the-clock").Create();
        var (hud, menu) = UiModScreens.Place([],
            [new LayerStub("scoreboard", UiLayerKind.Menu)],
            [new LayerStub("tfhud", UiLayerKind.Hud), scoreboard]);

        Assert.DoesNotContain(menu, s => s.Name == "scoreboard");
        Assert.Equal(2, hud.Length);
        Assert.Equal("tfhud", hud[0].Name);
        Assert.Same(scoreboard, hud[1]);
    }

    private static UiSample Sample(string slug)
        => Assert.Single(PreviewRun.AllSamples(),
                         s => !s.Gallery && s.Group == "scoreboard" && s.Slug == slug);

    /// <summary>A named, layered screen placeholder: Place reads only the
    /// identity, the layer and the order.</summary>
    private sealed class LayerStub(string name, UiLayerKind layer, int order = 0) : UiScreen
    {
        public override string Name => name;
        public override UiLayerKind Layer => layer;
        public override int Order => order;
        public override void Describe(ref UiFrame ui) { }
    }

    /// <summary>The rendered sample's context (the idiom of
    /// <see cref="TFScoreboardArtTests.Render"/> without the recording library).</summary>
    private static UiContext Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "scoreboard" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        return ctx;
    }

    /// <summary>The rendered sample over a library that answers every find:
    /// the chrome's own arm draws (the idiom of
    /// <see cref="TFScoreboardArtTests.Render"/>).</summary>
    private static UiContext RenderWithArt(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "scoreboard" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(new TFScoreboardArtTests.RecordingLibrary()),
                         UiSamples.Screen(1920, 1080), sample.Now);
        return ctx;
    }

    /// <summary>Whether any element on the sample drew <paramref name="text"/>
    /// (the idiom of <see cref="TFHudTests.HasElement"/>).</summary>
    private static bool HasText(string slug, string text)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "scoreboard" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.RunText(ctx, i) == text)
                return true;
        return false;
    }

    /// <summary>Whether any element drew a text starting with the prefix —
    /// the elided cells draw their leading fragment (the class column's
    /// 15-px cell against the longer names).</summary>
    private static bool HasTextFrom(string slug, string prefix)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "scoreboard" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.RunText(ctx, i).StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }
}
