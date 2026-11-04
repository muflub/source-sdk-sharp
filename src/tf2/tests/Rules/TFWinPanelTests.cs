using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using TF2.Server;
using TF2.Server.Entities;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Tests.Rules;

/// <summary>
/// The win panel: the machine asks <see cref="ITFWinPanel"/> as it enters
/// TEAM_WIN (stock's State_Enter_TEAM_WIN → SendWinPanelInfo), and
/// <see cref="TFWinPanelAnnouncer"/> fires <c>teamplay_win_panel</c> with the
/// scores, the top three round scorers and the capture limit
/// (tf_gamerules.cpp:13599-13768).
/// </summary>
public sealed class TFWinPanelTests
{
    /// <summary>Every fired event's keys, in fire order.</summary>
    private sealed class RecordingEvents : IEngineGameEvents
    {
        public readonly List<SortedDictionary<string, string>> Fired = new();
        private readonly Dictionary<IntPtr, SortedDictionary<string, string>> _open = new();
        private IntPtr _next = new(1);

        public IntPtr EventCreate(string name)
        {
            IntPtr h = _next;
            _next = new IntPtr(_next.ToInt64() + 1);
            _open[h] = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["event"] = name };
            return h;
        }

        public bool EventFire(IntPtr handle, bool dontBroadcast = false)
        {
            Fired.Add(_open[handle]);
            return true;
        }

        public void EventSetInt(IntPtr handle, string key, int value) => _open[handle][key] = value.ToString();
        public void EventSetBool(IntPtr handle, string key, bool value) => _open[handle][key] = value.ToString();
        public void EventSetString(IntPtr handle, string key, string value) => _open[handle][key] = value;
        public bool EventGetBool(IntPtr handle, string key) => false;
        public float EventGetFloat(IntPtr handle, string key) => 0f;
        public int EventGetInt(IntPtr handle, string key) => 0;
        public string EventGetString(IntPtr handle, string key) => string.Empty;
        public void ListenForGameEvent(string name) { }

        public int Int(int fired, string key) => int.Parse(Fired[fired][key]);
        public string Text(int fired, string key) => Fired[fired][key];
        public bool Has(int fired, string key) => Fired[fired].ContainsKey(key);
    }

    /// <summary>A convar backend that stores what it is told, so a SetValue round-trips.</summary>
    private sealed class Backend : IConVarBackend
    {
        private readonly List<string> _names = [];
        private readonly List<string> _values = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _names.Add(name);
            _values.Add(defaultValue);
            return _names.Count - 1;
        }

        public void Set(int id, string value) => _values[id] = value;

        public string Get(int id) => _values[id];

        public string? ValueOf(string name) => _names.Contains(name) ? _values[_names.IndexOf(name)] : null;
    }

    private sealed class PanelRig
    {
        public readonly RecordingEvents Events = new();
        public readonly TFWinPanelCappers Cappers = new();
        public readonly TFPlayerStats Stats = new();
        public readonly TFTeamScores Scores = new(new TeamResource());
        public readonly FakeRoster Roster;
        public readonly TFWinPanelAnnouncer Panel;

        public PanelRig(bool ctfMode = false, int capsPerRound = 3)
        {
            Roster = new FakeRoster(new FakeClock());
            var convars = new ConVarRegistry(new Backend());
            convars.RegisterTypes([typeof(TFFlagCapsPerRoundConVar), typeof(TFGameModeCtfConVar)]);
            convars.Declared<TFFlagCapsPerRoundConVar>().SetValue(capsPerRound);
            if (ctfMode)
                convars.Declared<TFGameModeCtfConVar>().SetValue(true);
            Panel = new TFWinPanelAnnouncer(Events, Scores, Stats, Roster, Cappers,
                                            convars.Declared<TFFlagCapsPerRoundConVar>(), convars.Declared<TFGameModeCtfConVar>());
        }

        public void Send(bool gameOver = false, TFTeam winning = TFTeam.Red, WinReason reason = WinReason.FlagCaptureLimit,
                         bool roundComplete = true, bool perCapture = false, bool anim = true)
            => Panel.Send(new TFWinPanelData(gameOver, winning, reason, roundComplete, perCapture, anim));

        public EntityHandle Player(TFTeam team) => Roster.Add(team);
    }

    // ---- the machine's ask -------------------------------------------------

    [Fact]
    public void A_win_asks_the_panel_as_it_enters_team_win()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.FlagCaptureLimit);

        var sent = Assert.Single(rig.WinPanel.Sent);
        Assert.Equal(TFTeam.Red, sent.WinningTeam);
        Assert.Equal(WinReason.FlagCaptureLimit, sent.Reason);
        Assert.True(sent.RoundComplete);          // the default forceMapReset
        Assert.True(sent.UseAddScoreAnim);        // the win added the round's score
        Assert.False(sent.GameOver);              // no win limit, no time limit
    }

    [Fact]
    public void A_stalemate_asks_with_no_winning_team()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetStalemate(StalemateReason.Timer);

        var sent = Assert.Single(rig.WinPanel.Sent);
        Assert.Equal(TFTeam.Unassigned, sent.WinningTeam);
        Assert.Equal(WinReason.Stalemate, sent.Reason);
    }

    [Fact]
    public void A_win_plays_the_song_between_the_transition_and_the_round_win()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.FlagCaptureLimit);

        // teamplayroundbased_gamerules.cpp:2369-2386: transition, song, event.
        Assert.Contains("win_song Blue", rig.Announcer.Said);
        Assert.True(rig.Announcer.Said.IndexOf("win_song Blue")
                    < rig.Announcer.Said.FindIndex(s => s.StartsWith("round_win", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_stalemate_plays_the_sudden_death_song_before_the_transition()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        rig.Running();

        rig.Flow.SetStalemate(StalemateReason.Timer);

        // teamplayroundbased_gamerules.cpp:2535-2539.
        Assert.Contains("sudden_death_song", rig.Announcer.Said);
        Assert.True(rig.Announcer.Said.IndexOf("sudden_death_song")
                    < rig.Announcer.Said.IndexOf("round_stalemate Timer"));
    }

    [Fact]
    public void A_running_round_starts_with_the_team_voices()
    {
        var rig = new RoundRig();

        rig.Running();

        Assert.Equal(1, rig.Announcer.Count("start_round_voice"));
    }

    [Fact]
    public void A_win_that_added_no_score_has_no_add_score_anim()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.TimeLimit, dontAddScore: true);

        Assert.False(rig.WinPanel.Sent.Single().UseAddScoreAnim);
    }

    // ---- the event ---------------------------------------------------------

    [Fact]
    public void A_win_fires_the_event_with_the_teams_and_the_scores()
    {
        var rig = new PanelRig();
        rig.Player(TFTeam.Red);
        rig.Scores.AddScore(TFTeam.Blue, 2);
        rig.Scores.AddScore(TFTeam.Red, 5);

        rig.Send(winning: TFTeam.Red, reason: WinReason.FlagCaptureLimit);

        Assert.Equal("teamplay_win_panel", rig.Events.Text(0, "event"));
        Assert.Equal(0, rig.Events.Int(0, "panel_style"));        // WINPANEL_BASIC
        Assert.Equal((int)TFTeam.Red, rig.Events.Int(0, "winning_team"));
        Assert.Equal((int)WinReason.FlagCaptureLimit, rig.Events.Int(0, "winreason"));
        Assert.Equal(5, rig.Events.Int(0, "red_score"));
        Assert.Equal(2, rig.Events.Int(0, "blue_score"));
        Assert.Equal(3, rig.Events.Int(0, "flagcaplimit"));
        Assert.Equal(1, rig.Events.Int(0, "round_complete"));
        Assert.Equal(0, rig.Events.Int(0, "killstreak_player_1_count"));
        Assert.Equal("False", rig.Events.Text(0, "game_over"));
    }

    [Fact]
    public void A_complete_round_shows_the_winner_score_before_the_win()
    {
        var rig = new PanelRig();
        rig.Player(TFTeam.Red);
        rig.Scores.AddScore(TFTeam.Red, 5);

        rig.Send(winning: TFTeam.Red, anim: true);

        Assert.Equal(4, rig.Events.Int(0, "red_score_prev"));
        Assert.Equal(5, rig.Events.Int(0, "red_score"));
        Assert.Equal(0, rig.Events.Int(0, "blue_score_prev"));    // the loser's stands
    }

    [Fact]
    public void No_add_score_anim_leaves_the_scores_as_they_stand()
    {
        var rig = new PanelRig();
        rig.Player(TFTeam.Red);
        rig.Scores.AddScore(TFTeam.Red, 5);

        rig.Send(winning: TFTeam.Red, anim: false);

        Assert.Equal(5, rig.Events.Int(0, "red_score_prev"));
    }

    [Fact]
    public void The_top_three_are_the_rounds_highest_scorers()
    {
        var rig = new PanelRig();
        var quiet = rig.Player(TFTeam.Red);
        var middle = rig.Player(TFTeam.Red);
        var best = rig.Player(TFTeam.Red);
        rig.Stats.Increment(middle, TFStat.Kills, 3);
        rig.Stats.Increment(best, TFStat.Kills, 8);

        rig.Send();

        Assert.Equal(rig.Roster.EntIndex(best), rig.Events.Int(0, "player_1"));
        Assert.Equal(8, rig.Events.Int(0, "player_1_points"));
        Assert.Equal(rig.Roster.EntIndex(middle), rig.Events.Int(0, "player_2"));
        Assert.Equal(3, rig.Events.Int(0, "player_2_points"));
        Assert.False(rig.Events.Has(0, "player_3"));              // a zero scorer stops the row
        _ = quiet;
    }

    [Fact]
    public void A_tied_round_score_breaks_by_total_then_index()
    {
        var rig = new PanelRig();
        var first = rig.Player(TFTeam.Red);
        var second = rig.Player(TFTeam.Red);
        var late = rig.Player(TFTeam.Red);
        foreach (var p in new[] { first, second, late })
            rig.Stats.Increment(p, TFStat.Kills, 2);              // equal round scores
        rig.Stats.Increment(late, TFStat.Kills, 4);               // a higher total

        rig.Send();

        Assert.Equal(rig.Roster.EntIndex(late), rig.Events.Int(0, "player_1"));
        Assert.Equal(rig.Roster.EntIndex(second), rig.Events.Int(0, "player_2"));   // the higher index first
        Assert.Equal(rig.Roster.EntIndex(first), rig.Events.Int(0, "player_3"));
    }

    [Fact]
    public void A_stalemate_scores_everyone_and_fires_no_winning_team()
    {
        var rig = new PanelRig();
        var red = rig.Player(TFTeam.Red);
        var blue = rig.Player(TFTeam.Blue);
        rig.Stats.Increment(blue, TFStat.Kills, 2);
        rig.Stats.Increment(red, TFStat.Kills, 1);

        rig.Send(winning: TFTeam.Unassigned, reason: WinReason.Stalemate);

        Assert.Equal((int)TFTeam.Unassigned, rig.Events.Int(0, "winning_team"));
        Assert.Equal(rig.Roster.EntIndex(blue), rig.Events.Int(0, "player_1"));   // both teams in the running
        Assert.Equal(rig.Roster.EntIndex(red), rig.Events.Int(0, "player_2"));
    }

    [Fact]
    public void The_rounds_best_killstreak_leads_the_killstreak_row()
    {
        var rig = new PanelRig();
        var quiet = rig.Player(TFTeam.Red);
        var streaker = rig.Player(TFTeam.Red);
        rig.Stats.Increment(streaker, TFStat.KillstreakMax, 5);

        rig.Send();

        Assert.Equal(rig.Roster.EntIndex(streaker), rig.Events.Int(0, "killstreak_player_1"));
        Assert.Equal(5, rig.Events.Int(0, "killstreak_player_1_count"));
        _ = quiet;
    }

    [Fact]
    public void The_cappers_ride_only_for_a_capture_reason()
    {
        var rig = new PanelRig();
        rig.Cappers.NoteCapper(7);

        rig.Send(reason: WinReason.FlagCaptureLimit);
        Assert.Equal("\a", rig.Events.Text(0, "cappers"));        // char 7

        rig.Send(reason: WinReason.TimeLimit);
        Assert.Equal(string.Empty, rig.Events.Text(1, "cappers"));
    }

    [Fact]
    public void An_off_limit_ctf_scores_per_capture_so_no_score_anim_shows()
    {
        var rig = new PanelRig(ctfMode: true, capsPerRound: 0);
        rig.Player(TFTeam.Red);
        rig.Scores.AddScore(TFTeam.Red, 5);

        rig.Send(roundComplete: true, perCapture: false, anim: true);

        Assert.Equal(5, rig.Events.Int(0, "red_score_prev"));     // tf_gamerules.cpp:13627-13633
    }

    // ---- the cappers list --------------------------------------------------

    [Fact]
    public void The_cappers_list_holds_the_most_recent_capture_and_resets_each_round()
    {
        var cappers = new TFWinPanelCappers();
        cappers.NoteCapper(3);
        cappers.NoteCappers([1, 2]);

        Assert.Equal("", cappers.MostRecent);         // one player index per character (1 and 2)

        ((ITeamRoundListener)cappers).OnStateChanged(RoundState.RoundRunning, RoundState.Preround);

        Assert.Equal(string.Empty, cappers.MostRecent);
    }

    // ---- the stats half ----------------------------------------------------

    [Fact]
    public void The_round_score_is_calc_player_score_over_this_rounds_stats()
    {
        var stats = new TFPlayerStats();
        var player = new EntityHandle(0x0001_0001u);

        stats.Increment(player, TFStat.Kills, 3);
        stats.Increment(player, TFStat.Captures, 1);              // 2 points
        Assert.Equal(5, stats.RoundScore(player.EntryIndex));
        Assert.Equal(5, stats.Score(player.EntryIndex));

        ((ITeamRoundListener)stats).OnStateChanged(RoundState.RoundRunning, RoundState.Preround);
        Assert.Equal(0, stats.RoundScore(player.EntryIndex));
        Assert.Equal(5, stats.Score(player.EntryIndex));          // the accumulated stands
    }
}
