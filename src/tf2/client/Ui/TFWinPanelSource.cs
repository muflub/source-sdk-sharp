//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The win panel's data (stock's <c>CTFWinPanel::FireGameEvent</c> and its
/// reads, tf_hud_winpanel.cpp): the <c>teamplay_win_panel</c> event is
/// snapshotted once — sentences ConstructString-ed at the event the way
/// stock's are (:209-296, :298-422, :424-457), the rows read off the
/// player resource and g_PR (:491-585, :587-658) — and
/// <c>teamplay_round_start</c> hides the panel again (:119-126, the other
/// hide events are show-consumers this build has none of).
///
/// <para>
/// The two-second score tick (stock's OnThink
/// m_flTimeUpdateTeamScore arm, :714-737) flips the shown scores to the
/// final ones; stock also fires <c>winpanel_show_scores</c> and plays
/// <c>Hud.EndRoundScored</c> there, which this build has no consumer for.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFWinPanelSource)])]
public sealed class TFWinPanelSource : ITFWinPanelSource
{
    private const int Slots = TFPlayerResourceNetState.Slots;

    private readonly IPlayerNames _names;
    private readonly TFClientPlayerResources _resources;
    private readonly IGameClock _clock;
    private readonly IUiLocalisation _localise;

    // The snapshot, held until the next round start hides it.
    private bool _showing;
    private string _banner = "", _winReason = "", _details = "", _topPlayers = "";
    private int _winningTeam;
    private bool _roundComplete;
    private int _blue, _red, _bluePrev, _redPrev;
    private float _tickAt;                        // 0: no tick pending
    private readonly TFWinPanelPlayerView[] _players = new TFWinPanelPlayerView[3];
    private TFWinPanelPlayerView _killStreak;

    public TFWinPanelSource(EventLink events, IPlayerNames names, TFClientPlayerResources resources,
                            IGameClock clock, IUiLocalisation localise)
    {
        (_names, _resources, _clock, _localise) = (names, resources, clock, localise);
        events.OnWinPanel(OnWinPanel);            // stock's ListenForGameEvent in the panel's Init
        events.OnRoundStart(OnRoundStart);
    }

    public bool Showing => _showing;

    public string Banner => _banner;

    public string WinReason => _winReason;

    public string Details => _details;

    public string TopPlayersLabel => _topPlayers;

    public int WinningTeam => _winningTeam;

    public bool RoundComplete => _roundComplete;

    public bool ScoresTick => _tickAt <= 0f || _clock.Now >= _tickAt;

    public int BlueScore => ScoresTick ? _blue : _bluePrev;

    public int RedScore => ScoresTick ? _red : _redPrev;

    public TFWinPanelPlayerView Player(int position) => _players[position];

    public TFWinPanelPlayerView KillStreak => _killStreak;

    /// <summary>One teamplay_win_panel (stock FireGameEvent). The team
    /// scores are shown from the previous ones when a complete round's
    /// differ, on the tick (m_flTimeUpdateTeamScore = now + 2.0,
    /// :704-712).</summary>
    private void OnWinPanel(TeamplayWinPanelArgs e)
    {
        _showing = true;
        _winningTeam = e.WinningTeam;
        _roundComplete = e.RoundComplete;
        _blue = e.BlueScore;
        _red = e.RedScore;
        _bluePrev = e.BlueScorePrev;
        _redPrev = e.RedScorePrev;
        _tickAt = e.RoundComplete && (e.BlueScore != e.BlueScorePrev || e.RedScore != e.RedScorePrev)
            ? _clock.Now + 2f : 0f;

        // The banner: the team's win on a complete round, its advance on a
        // mini-round, its defence on a defended point (tf_hud_winpanel.cpp:209-296).
        string team = e.WinningTeam == 3 ? Token("TF_Name_Blue") : Token("TF_Name_Red");
        if (e.WinningTeam is 2 or 3)
        {
            string sentence = e.RoundComplete
                ? Token("Winpanel_TeamWins")
                : e.WinReason == 4                      // WINREASON_DEFEND_UNTIL_TIME_LIMIT
                    ? Token("Winpanel_TeamDefends")
                    : Token("Winpanel_TeamAdvances");
            _banner = sentence.Replace("%s1", team);
            _topPlayers = e.WinningTeam == 3 ? Token("Winpanel_BlueMVPs") : Token("Winpanel_RedMVPs");
        }
        else
        {
            _banner = Token("Winpanel_Stalemate");
            _topPlayers = Token("Winpanel_TopPlayers");
        }

        // The win reason (stock's switch, :298-422): the arms the events
        // this build fires can name. A reason with no sentence shows none.
        _winReason = e.WinReason switch
        {
            1 => Token("Winreason_AllPointsCaptured").Replace("%s1", team),        // ALL_POINTS_CAPTURED
            2 => Token("Winreason_OpponentsDead").Replace("%s1", team),            // OPPONENTS_DEAD
            3 => e.FlagCapLimit == 1                                               // FLAG_CAPTURE_LIMIT
                    ? Token("Winreason_FlagCaptureLimit_One").Replace("%s1", team)
                    : Token("Winreason_FlagCaptureLimit")
                        .Replace("%s1", team).Replace("%s2", e.FlagCapLimit.ToString()),
            4 => Token("Winreason_DefendedUntilTimeLimit").Replace("%s1", team),   // DEFEND_UNTIL_TIME_LIMIT
            5 => Token("Winreason_Stalemate"),                                     // STALEMATE
            6 => Token("Winreason_TimeLimit").Replace("%s1", team),                // TIMELIMIT
            7 => Token("Winreason_WinLimit").Replace("%s1", team),                 // WINLIMIT
            8 => Token("Winreason_WinDiffLimit").Replace("%s1", team),             // WINDIFFLIMIT
            13 => e.FlagCapLimit == 1                                              // SCORED (the score limit)
                    ? Token("Winreason_ScoreLimit_One").Replace("%s1", team)
                    : Token("Winreason_ScoreLimit")
                        .Replace("%s1", team).Replace("%s2", e.FlagCapLimit.ToString()),
            _ => "",
        };

        // The details row: a mini-round's capture points left, or the
        // capture's cappers (stock's DetailsLabel arms, :424-457).
        if (!e.RoundComplete && e.WinReason is not (5 or 0))
        {
            _details = (e.RoundsRemaining == 1
                    ? Token("Winpanel_CapturePointRemaining")
                    : Token("Winpanel_CapturePointsRemaining"))
                .Replace("%s1", e.RoundsRemaining.ToString());
        }
        else if (e.WinReason is 1 or 3 && e.Cappers.Length > 0)
        {
            var cappers = new List<string>();
            foreach (char c in e.Cappers)
                if (_names.TryGet(c, out ClientPlayerInfo info))
                    cappers.Add(info.Name);
            _details = cappers.Count > 0 ? Token("Winpanel_WinningCapture").Replace("%s1", string.Join(", ", cappers)) : "";
        }
        else
            _details = "";

        // The rows (stock's :491-585 and :587-658): only points on the
        // board, and the streak's count, show.
        _players[0] = Row(e.Player1, e.Player1Points);
        _players[1] = Row(e.Player2, e.Player2Points);
        _players[2] = Row(e.Player3, e.Player3Points);
        _killStreak = Row(e.KillstreakPlayer, e.KillstreakCount);
    }

    /// <summary>One row: the event names the entity index; the resource
    /// row carries the team and class (g_TF_PR) and g_PR the name.</summary>
    private TFWinPanelPlayerView Row(int entIndex, int points)
    {
        if (points <= 0 || entIndex <= 0 || entIndex >= Slots)
            return default;

        string name = _names.TryGet(entIndex, out ClientPlayerInfo info) ? info.Name : "";
        string className = "";
        int team = 0;
        if (_resources.Live is { } live)
        {
            if (live.State.PlayerClass[entIndex] is var tfClass && tfClass != (byte)TFClass.Undefined)
                className = TFClassNames.NonLocalized((TFClass)tfClass);
            team = live.State.Team[entIndex];
        }

        return new TFWinPanelPlayerView(name, className, points, team);
    }

    // The next round's start hides the panel (stock's teamplay_round_start
    // listener, :119-126).
    private void OnRoundStart(TeamplayRoundStartArgs e) => _showing = false;

    private string Token(string name) => _localise.Find("#" + name) ?? "#" + name;
}
