using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The scoreboard's art arms over the samples: the class column asks the
/// library for stock's class images (g_pszClassIcons, tf_shareddefs.cpp:
/// 1856-1877, resized to 13x13, tf_clientscoreboard.cpp:350-355), dead rows
/// the darker "_d" set (:1498), the gate the local-team arm (:1467-1468);
/// the dominating column the count images (pszDominationIcons, :120-138,
/// dead :140-158, the span clamp :1350-1353); and a library that misses
/// everything falls back to the class name.
/// </summary>
public class TFScoreboardArtTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_class_column_asks_the_library_for_the_stock_class_images()
    {
        // Local RED: the RED rows' classes are asked (Medic and Spy alive,
        // Sniper and Pyro dead of the fake's indices 7-12); the BLU rows'
        // are not, though the data holds them.
        var (ctx, lib) = Render("two-teams-and-the-clock");
        Assert.Contains("hud/leaderboard_class_medic", lib.Asked);
        Assert.Contains("hud/leaderboard_class_spy", lib.Asked);
        Assert.Contains("hud/leaderboard_class_sniper_d", lib.Asked);        // dead: :1498's +9 arm
        Assert.Contains("hud/leaderboard_class_pyro_d", lib.Asked);
        Assert.Contains(UiTestKit.Elements(ctx), e => e.Kind == UiElementKind.Image);   // the images drew

        Assert.DoesNotContain("hud/leaderboard_class_sniper", lib.Asked);
        Assert.DoesNotContain("hud/leaderboard_class_engineer", lib.Asked);   // a BLU row's class
    }

    [Fact]
    public void A_spectator_sees_every_rows_class_image()
    {
        // Stock's gate arms to every row when the local is on no team
        // (localteam == TEAM_UNASSIGNED, :1468): the BLU rows' Engineer and
        // the RED rows' Medic are both asked.
        var (_, lib) = Render("spectating");
        Assert.Contains("hud/leaderboard_class_engineer", lib.Asked);
        Assert.Contains("hud/leaderboard_class_medic", lib.Asked);
    }

    [Fact]
    public void A_dominating_row_asks_the_library_for_the_count_image()
    {
        // The fake's index % 4 counts, with no team gate — stock sets the
        // count for every row (tf_clientscoreboard.cpp:1376). Index 1 holds
        // one domination, index 2 two.
        var (_, lib) = Render("two-teams-and-the-clock");
        Assert.Contains("hud/leaderboard_dom1", lib.Asked);
        Assert.Contains("hud/leaderboard_dom2", lib.Asked);

        // A count of none asks nothing: the fake's index % 4 == 0 rows.
        Assert.DoesNotContain("hud/leaderboard_dom4", lib.Asked);
    }

    [Fact]
    public void A_dead_row_asks_the_darker_count_image_and_a_full_count_clamps()
    {
        // The count-3 rows of the fake (indices 3, 7 and 11) are alive: the
        // bright image. The dead rows are indices 4, 8 and 12 — index 8
        // holds a count, and asks the "_d" arm (tf_clientscoreboard.cpp:1370).
        var (_, lib) = Render("two-teams-and-the-clock");
        Assert.Contains("hud/leaderboard_dom3", lib.Asked);
        Assert.DoesNotContain("hud/leaderboard_dom3_d", lib.Asked);

        // Indices 6 and 8 hold more than the table's span: clamped to the
        // last image (SCOREBOARD_DOMINATION_ICONS - 1, tf_shareddefs.h:638),
        // alive (6) and dead (8).
        Assert.Contains("hud/leaderboard_dom16", lib.Asked);
        Assert.Contains("hud/leaderboard_dom16_d", lib.Asked);
    }

    [Fact]
    public void A_row_with_a_ping_asks_the_threshold_image_and_a_dead_row_the_darker_one()
    {
        // The fake's pings run 18..154: the low image, and the med one for
        // the rows of 129, 148 and 154; the thresholds above are not asked
        // (tf_clientscoreboard.cpp:1433-1446).
        var (_, lib) = Render("two-teams-and-the-clock");
        Assert.Contains("hud/scoreboard_ping_low", lib.Asked);
        Assert.Contains("hud/scoreboard_ping_med", lib.Asked);
        Assert.DoesNotContain("hud/scoreboard_ping_high", lib.Asked);
        Assert.DoesNotContain("hud/scoreboard_ping_very_high", lib.Asked);

        // The dead rows (the fake's indices 4, 8 and 12, pinging 129, 117
        // and 105) ask the "_d" set (:109-116).
        Assert.Contains("hud/scoreboard_ping_med_d", lib.Asked);
        Assert.Contains("hud/scoreboard_ping_low_d", lib.Asked);
    }

    [Fact]
    public void A_row_that_dominates_the_local_player_asks_the_nemesis_art()
    {
        // The fake's index 2 dominates the local player, alive; the dead
        // index 3 the darker arm (tf_clientscoreboard.cpp:1512).
        var (_, lib) = Render("two-teams-and-the-clock");
        Assert.Contains("hud/leaderboard_nemesis", lib.Asked);
        Assert.Contains("hud/leaderboard_nemesis_d", lib.Asked);
    }

    [Fact]
    public void A_row_the_local_player_dominates_asks_the_dominated_art()
    {
        // The fake's index 9 is dominated by the local player, alive; the
        // dead index 11 the darker arm (tf_clientscoreboard.cpp:1517-1518).
        var (_, lib) = Render("two-teams-and-the-clock");
        Assert.Contains("hud/leaderboard_dominated", lib.Asked);
        Assert.Contains("hud/leaderboard_dominated_d", lib.Asked);
    }

    [Fact]
    public void A_bot_row_asks_the_bot_image_for_its_team()
    {
        // Bots before any ping is read (:1397-1408): the fake's indices 5
        // and 10 are a BLU and a RED bot, both alive.
        var (_, lib) = Render("two-teams-and-the-clock");
        Assert.Contains("hud/scoreboard_ping_bot_blue", lib.Asked);
        Assert.Contains("hud/scoreboard_ping_bot_red", lib.Asked);
        Assert.DoesNotContain("hud/scoreboard_ping_bot_red_d", lib.Asked);
    }

    [Fact]
    public void The_bottom_bar_asks_for_the_local_players_team_class_image()
    {
        // The fake's local row is the RED Medic (tf_clientscoreboard.cpp:
        // 2011 reads the same table the HUD's portrait does,
        // tf_hud_playerstatus.cpp:47-75): the red image, not the blue.
        var (_, lib) = Render("two-teams-and-the-clock");
        Assert.Contains("hud/class_medicred", lib.Asked);
        Assert.DoesNotContain("hud/class_medicblue", lib.Asked);
    }

    [Fact]
    public void A_spectators_bottom_bar_asks_no_class_image()
    {
        // Stock's gate (tf_clientscoreboard.cpp:1981-1982): the selected
        // player must be on a game team with a normal class. The spectator
        // sample has no local row on one.
        var (_, lib) = Render("spectating");
        Assert.DoesNotContain(lib.Asked, a => a.StartsWith("hud/class_", StringComparison.Ordinal));
    }

    [Fact]
    public void A_library_that_misses_draws_the_class_name()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "scoreboard" && s.Slug == "two-teams-and-the-clock");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);

        Assert.True(HasText(ctx, "Medic"));               // the alive RED row spells its class
        Assert.True(HasText(ctx, "Sniper"));              // the dead one too: the "_d" name stands in
        Assert.DoesNotContain(UiTestKit.Elements(ctx), e => e.Kind == UiElementKind.Image);
    }

    private static (UiContext Ctx, RecordingLibrary Lib) Render(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "scoreboard" && s.Slug == slug);
        var lib = new RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, lib);
    }

    private static bool HasText(UiContext ctx, string text)
    {
        var elements = UiTestKit.Elements(ctx);
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.RunText(ctx, i) == text)
                return true;
        return false;
    }

    /// <summary>Answers every name with a non-zero handle, recording each
    /// ask (the brief's fake: it records the Find calls).</summary>
    internal sealed class RecordingLibrary : IUiTextureLibrary
    {
        public readonly List<string> Asked = new();
        public int Find(string name) { Asked.Add(name); return 0x51; }
        public int Material(string name) => 0;
        public UiTextureInfo? About(string name) { Asked.Add(name); return null; }
        public UiTextureIcon? Icon(string name) { Asked.Add(name); return null; }
    }
}
