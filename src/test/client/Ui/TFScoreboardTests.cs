using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The TF2 scoreboard's stock rules, held over the samples: the class
/// column's gate (<c>!AreEnemyTeams(row, local) &amp;&amp; local !=
/// TEAM_UNASSIGNED</c>, tf_clientscoreboard.cpp:1467-1468), the spectator
/// line of <c>UpdateSpectatorList</c> (:1646-1705), the clock between the
/// bands (<c>TimerBG</c>), and the head-count token pair
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
        // them. The gate's icon arms are TFScoreboardArtTests'.
        Assert.True(HasText("two-teams-and-the-clock", "Medic"));
        Assert.True(HasText("two-teams-and-the-clock", "Soldier"));
        Assert.False(HasText("two-teams-and-the-clock", "Demoman"));
        Assert.False(HasText("two-teams-and-the-clock", "Heavy"));
        Assert.False(HasText("two-teams-and-the-clock", "Engineer"));

        // A spectator is on no team: stock's gate arms to every row.
        Assert.True(HasText("spectating", "Engineer"));
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
    public void The_clock_sits_between_the_bands()
    {
        Assert.True(HasText("two-teams-and-the-clock", "6:11"));     // 371 s
        Assert.False(HasText("no-round-timer", "6:11"));
    }

    [Theory]
    [InlineData("one-player-each", "PLAYER")]
    [InlineData("two-teams-and-the-clock", "PLAYERS")]
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
        Assert.True(HasText("empty-teams", "PLAYERS"));   // 0 heads, plural
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
        var blueName = UiTestKit.Named(ctx, "tf name blue");

        Assert.True(blueScore.Rect.X > blue.Rect.X + blue.Rect.W / 2,
                    "the blue score is not at the band's inner (right) edge");
        Assert.True(redScore.Rect.X + redScore.Rect.W < red.Rect.X + red.Rect.W / 2,
                    "the red score is not at the band's inner (left) edge");
        Assert.True(blueScore.Rect.H > blueName.Rect.H,
                    "the score does not stand taller than the name");
        foreach (var (score, band) in new[] { (blueScore, blue), (redScore, red) })
        {
            float centre = score.Rect.Y + score.Rect.H / 2f;
            Assert.True(centre > band.Rect.Y && centre < band.Rect.Y + band.Rect.H,
                        "the score is not inside its band's height");
        }
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
}
