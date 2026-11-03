extern alias client;

using Xunit;

using ClientPlayerInfo = client::SourceSharp.Abi.ClientPlayerInfo;
using ClientShared = client::SourceSharp.Shared;
using Clock = client::SourceSharp.Abi.IGameClock;
using Localiser = client::SourceSharp.Client.Ui.IUiLocalisation;
using PlayerResource = client::TF2.Client.Player.ClientTFPlayerResource;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using RoundArgs = client::SourceSharp.Shared.TeamplayRoundStartArgs;
using RoundEvent = client::SourceSharp.Shared.TeamplayRoundStartEvent;
using Source = client::TF2.Client.Ui.TFWinPanelSource;
using WinArgs = client::SourceSharp.Shared.TeamplayWinPanelArgs;
using WinEvent = client::SourceSharp.Shared.TeamplayWinPanelEvent;
using TFClassId = client::TF2.Shared.TFClass;

namespace TF2.Tests.Client;

/// <summary>
/// The win panel's data source (src/tf2/client/Ui/TFWinPanelSource.cs): the
/// teamplay_win_panel event snapshotted into what the screen draws — the
/// stock sentences ConstructString-ed at the event (tf_hud_winpanel.cpp:209-457),
/// the rows read off the player resource and g_PR (:491-658) — and the next
/// round start hiding it (:119-126).
/// </summary>
public sealed class TFWinPanelSourceTests
{
    /// <summary>The win event shows the panel with its decoded fields: the
    /// team banner, the flag-limit sentence, the previous scores and the
    /// rows with their classes (FireGameEvent's reads).</summary>
    [Fact]
    public void The_win_event_shows_the_panel_with_its_decoded_fields()
    {
        var (source, wins, _, _, _) = Built();
        Assert.False(source.Showing);

        wins.Raise(new WinArgs(WinningTeam: Red, WinReason: 3, Cappers: "", FlagCapLimit: 3,
            BlueScore: 1, RedScore: 3, BlueScorePrev: 1, RedScorePrev: 2, RoundComplete: true,
            Player1: 3, Player1Points: 6, Player2: 4, Player2Points: 4));

        Assert.True(source.Showing);
        Assert.Equal(Red, source.WinningTeam);
        Assert.True(source.RoundComplete);
        Assert.Equal("RED wins the round!", source.Banner);
        Assert.Equal("RED captured the flag 3 times", source.WinReason);
        Assert.Equal("RED MVPs", source.TopPlayersLabel);
        Assert.False(source.ScoresTick);                 // the tick is 2 s out
        Assert.Equal(1, source.BlueScore);               // the previous ones
        Assert.Equal(2, source.RedScore);

        var first = source.Player(0);
        Assert.Equal("Jane Doe", first.Name);
        Assert.Equal("Soldier", first.Class);
        Assert.Equal(6, first.Points);
        Assert.Equal(Red, first.Team);
        Assert.Equal("Tavish DeGroot", source.Player(1).Name);
        Assert.Equal(0, source.Player(2).Points);        // the unset row stays hidden

        // The killstreak row comes from its own pair of fields.
        wins.Raise(new WinArgs(WinningTeam: Red, KillstreakPlayer: 3, KillstreakCount: 5));
        Assert.Equal("Jane Doe", source.KillStreak.Name);
        Assert.Equal(5, source.KillStreak.Points);
    }

    /// <summary>A stalemate names no team: the stalemate banner and the
    /// plain top players (stock's winning_team 0 arms, :283-296).</summary>
    [Fact]
    public void A_stalemate_names_no_team()
    {
        var (source, wins, _, _, _) = Built();

        wins.Raise(new WinArgs(WinningTeam: 0, WinReason: 5, RoundComplete: true));

        Assert.True(source.Showing);
        Assert.Equal(0, source.WinningTeam);
        Assert.Equal("Stalemate!", source.Banner);
        Assert.Equal("Top players", source.TopPlayersLabel);
    }

    /// <summary>The next round start hides the panel again (stock's
    /// teamplay_round_start listener, :119-126).</summary>
    [Fact]
    public void The_round_start_hides_the_panel()
    {
        var (source, wins, rounds, _, _) = Built();

        wins.Raise(new WinArgs(WinningTeam: Red));
        Assert.True(source.Showing);

        rounds.Raise(new RoundArgs(FullReset: false));
        Assert.False(source.Showing);
    }

    /// <summary>The two-second tick: a complete round's differing scores
    /// show the previous ones until it lands, then the final ones — and a
    /// panel whose scores agree ticks at once (stock's
    /// m_flTimeUpdateTeamScore arm, :704-737).</summary>
    [Fact]
    public void The_scores_tick_up_two_seconds_in()
    {
        var (source, wins, _, clock, _) = Built();

        wins.Raise(new WinArgs(WinningTeam: Red, RoundComplete: true,
            BlueScore: 1, RedScore: 3, BlueScorePrev: 1, RedScorePrev: 2));
        Assert.Equal(2, source.RedScore);
        Assert.False(source.ScoresTick);

        clock.Now = 102f;
        Assert.True(source.ScoresTick);
        Assert.Equal(3, source.RedScore);
        Assert.Equal(1, source.BlueScore);

        wins.Raise(new WinArgs(WinningTeam: Red, RoundComplete: true,
            BlueScore: 1, RedScore: 2, BlueScorePrev: 1, RedScorePrev: 2));
        Assert.Equal(2, source.RedScore);                // the same scores: nothing to tick to
        Assert.True(source.ScoresTick);
    }

    /// <summary>The capture's cappers are resolved to names at the event
    /// (stock's cappers walk, :437-447) into the details line; a
    /// mini-round names the capture points left instead.</summary>
    [Fact]
    public void The_details_line_names_the_cappers_or_the_points_left()
    {
        var (source, wins, _, _, _) = Built();

        wins.Raise(new WinArgs(WinningTeam: Red, WinReason: 1, Cappers: "\x03\x04", RoundComplete: true));
        Assert.Equal("Captured by Jane Doe, Tavish DeGroot", source.Details);

        wins.Raise(new WinArgs(WinningTeam: Blue, WinReason: 1, RoundComplete: false, RoundsRemaining: 1));
        Assert.Equal("1 capture point remaining", source.Details);

        wins.Raise(new WinArgs(WinningTeam: Blue, WinReason: 1, RoundComplete: false, RoundsRemaining: 3));
        Assert.Equal("3 capture points remaining", source.Details);
    }

    /// <summary>A flag limit of one takes the singular sentence (stock's
    /// limit == 1 arm, :347-352).</summary>
    [Fact]
    public void A_flag_limit_of_one_is_singular()
    {
        var (source, wins, _, _, _) = Built();

        wins.Raise(new WinArgs(WinningTeam: Blue, WinReason: 3, FlagCapLimit: 1));
        Assert.Equal("BLU captured the flag", source.WinReason);
    }

    // ---- the rig ----

    /// <summary>The source over three named RED players' rows (slots 3..5),
    /// a clock at 100 s and a token table with the sentences the facts read.</summary>
    private static (Source Source, WinEvent Wins, RoundEvent Rounds, FakeClock Clock, PlayerResources Players) Built()
    {
        var clock = new FakeClock { Now = 100f };
        var resources = new PlayerResources();
        var tf = new PlayerResource(new client::SourceSharp.Abi.RecordingEngine(), resources);
        tf.State.BindLocal();
        tf.State.PlayerClass[3] = (byte)TFClassId.Soldier;
        tf.State.Team[3] = Red;
        tf.State.PlayerClass[4] = (byte)TFClassId.Demoman;
        tf.State.Team[4] = Red;
        tf.State.PlayerClass[5] = (byte)TFClassId.HeavyWeapons;
        tf.State.Team[5] = Red;
        tf.OnSpawn();

        var wins = new WinEvent();
        var rounds = new RoundEvent();
        var source = new Source(new client::TF2.Client.EventLink(new ClientShared.PlayerDeathEvent(),
                new ClientShared.PlayerHurtEvent(), new ClientShared.ControlPointStartTouchEvent(),
                new ClientShared.ControlPointEndTouchEvent(), wins, rounds),
            new Roster((3, "Jane Doe"), (4, "Tavish DeGroot"), (5, "Misha Misha")),
            resources, clock, new Tokens());
        return (source, wins, rounds, clock, resources);
    }

    private sealed class FakeClock : Clock
    {
        public float Now { get; set; }
    }

    /// <summary>The sentences the mod's token file carries (its English
    /// under the test's control).</summary>
    private sealed class Tokens : Localiser
    {
        public string? Find(string token) => token switch
        {
            "#Winpanel_TeamWins" => "%s1 wins the round!",
            "#Winpanel_TeamAdvances" => "%s1 advances!",
            "#Winpanel_TeamDefends" => "%s1 defends!",
            "#Winpanel_Stalemate" => "Stalemate!",
            "#Winpanel_RedMVPs" => "RED MVPs",
            "#Winpanel_BlueMVPs" => "BLU MVPs",
            "#Winpanel_TopPlayers" => "Top players",
            "#Winpanel_WinningCapture" => "Captured by %s1",
            "#Winpanel_CapturePointRemaining" => "%s1 capture point remaining",
            "#Winpanel_CapturePointsRemaining" => "%s1 capture points remaining",
            "#Winreason_AllPointsCaptured" => "%s1 captured all the points",
            "#Winreason_FlagCaptureLimit" => "%s1 captured the flag %s2 times",
            "#Winreason_FlagCaptureLimit_One" => "%s1 captured the flag",
            "#Winreason_Stalemate" => "No winners",
            "#Winreason_TimeLimit" => "%s1 was ahead on score when time ran out",
            "#TF_Name_Red" => "RED",
            "#TF_Name_Blue" => "BLU",
            _ => null,
        };
    }

    /// <summary>The g_PR role: the three named slots, the rest nobody.</summary>
    private sealed class Roster(params (int Slot, string Name)[] rows) : client::SourceSharp.Client.Vgui.IPlayerNames
    {
        public bool TryGet(int slot, out ClientPlayerInfo info)
        {
            foreach (var (found, name) in rows)
                if (found == slot)
                {
                    info = new ClientPlayerInfo(name, 0, false);
                    return true;
                }
            info = default;
            return false;
        }
    }

    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_RED 2, TF_TEAM_BLUE 3.
    private const int Red = 2;
    private const int Blue = 3;
}
