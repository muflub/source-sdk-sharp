//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// The gamerules state <c>CTFGameRules::SendWinPanelInfo</c> reads
/// (tf_gamerules.cpp:13599): the machine hands it over as it enters TEAM_WIN.
/// <see cref="ScoringPerCapture"/> is the CP master's answer; the CTF
/// <c>tf_flag_caps_per_round == 0</c> arm is the announcer's, over the
/// game-type flag convars <c>Activate</c> wrote.
/// </summary>
public sealed record TFWinPanelData(bool GameOver, TFTeam WinningTeam, WinReason Reason, bool RoundComplete,
                                    bool ScoringPerCapture, bool UseAddScoreAnim);

/// <summary>Stock's <c>SendWinPanelInfo</c>: the <c>teamplay_win_panel</c> event.</summary>
public interface ITFWinPanel
{
    void Send(TFWinPanelData data);
}

/// <summary>
/// CTFGameRules::m_szMostRecentCappers (tf_gamerules.h:1068): the players who
/// made the most recent capture, packed one player index per character so it
/// rides in an event string, cleared at each round's start
/// (tf_gamerules.cpp:4907). Stock filled it from the game events it listened
/// to — one capper for a flag capture (tf_gamerules.cpp:18252-18261), the
/// event's cappers string for a point capture (tf_gamerules.cpp:18200-18202) —
/// so the capture sites call it where they fire those events.
/// </summary>
[SideService(Side.Server)]
public sealed class TFWinPanelCappers : IOrderedHook, ITeamRoundListener
{
    private const int Capacity = 101; // MAX_PLAYERS_ARRAY_SAFE

    private readonly char[] _cappers = new char[Capacity];

    public int HookOrder => TFPlayerOrders.WinPanel;

    public void NoteCapper(int playerIndex) => NoteCappers([playerIndex]);

    public void NoteCappers(IReadOnlyList<int> players)
    {
        int count = Math.Min(players.Count, Capacity - 1);
        for (int i = 0; i < count; i++)
            _cappers[i] = (char)players[i];
        _cappers[count] = '\0';
    }

    public string MostRecent
    {
        get
        {
            int end = Array.IndexOf(_cappers, '\0');
            return new string(_cappers, 0, end < 0 ? Capacity - 1 : end);
        }
    }

    // SetupOnRoundStart clears the list (tf_gamerules.cpp:4907).
    void ITeamRoundListener.OnStateChanged(RoundState from, RoundState to)
    {
        if (to == RoundState.Preround)
            Array.Clear(_cappers);
    }

    void ITeamRoundListener.OnRoundWon(TFTeam team, WinReason reason, float bonusSeconds) { }
}

/// <summary>
/// <c>CTFGameRules::SendWinPanelInfo</c> (tf_gamerules.cpp:13599-13768): the
/// <c>teamplay_win_panel</c> event the client's win panel shows. Not ported:
/// the arena and PvE arms (they are separate senders in stock), the passtime
/// <c>tf_passtime_scores_per_round</c> arm of the capture limit, and the
/// mini-round <c>rounds_remaining</c> (a CP-master question the objectives
/// port does not answer). Stock fills up to six top players, but
/// modevents.res declares only player_1..3, so that is what is set.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFWinPanel)])]
public sealed class TFWinPanelAnnouncer(IEngineGameEvents events, ITFTeamScores scores, ITFPlayerStats stats,
                                        ITeamRoundRoster roster, TFWinPanelCappers cappers,
                                        TFFlagCapsPerRoundConVar capsPerRound, TFGameModeCtfConVar ctfGameMode) : ITFWinPanel
{
    public void Send(TFWinPanelData d)
    {
        // The CTF arm: scoring per capture when the caps limit is off
        // (tf_gamerules.cpp:13627-13633).
        bool perCapture = d.ScoringPerCapture || (ctfGameMode.Value && capsPerRound.Value == 0);

        int blue = scores.Score(TFTeam.Blue);
        int red = scores.Score(TFTeam.Red);
        int bluePrev = blue;
        int redPrev = red;

        if (d.RoundComplete && !perCapture && d.UseAddScoreAnim)
        {
            // if this is a complete round, calc team scores prior to this win
            switch (d.WinningTeam)
            {
                case TFTeam.Blue:
                    bluePrev = Math.Max(0, blue - TeamRoundFlow.RoundWinScore);
                    break;
                case TFTeam.Red:
                    redPrev = Math.Max(0, red - TeamRoundFlow.RoundWinScore);
                    break;
            }
        }

        // The players in the running: the winning team's, or everyone on a
        // game team for a stalemate (tf_gamerules.cpp:13676-13684). The same
        // walk finds the highest round killstreak (tf_gamerules.cpp:13700-13710).
        var scored = new List<(int Index, int Round, int Total)>();
        int streakPlayer = 0;
        int streak = 0;
        var candidates = d.WinningTeam is TFTeam.Red or TFTeam.Blue
            ? roster.Players(d.WinningTeam)
            : roster.Players(TFTeam.Red).Concat(roster.Players(TFTeam.Blue));

        foreach (var player in candidates)
        {
            int index = roster.EntIndex(player);
            int round = stats.RoundScore(index);
            int maxStreak = stats.Round(index, TFStat.KillstreakMax);
            if (maxStreak > streak)
            {
                streak = maxStreak;
                streakPlayer = index;
            }

            scored.Add((index, round, stats.Score(index)));
        }

        // PlayerRoundScoreSortFunc (tf_gamerules.cpp:13773-13785): the round's
        // points first, then the total, then the higher index for determinism.
        scored.Sort((a, b) => a.Round != b.Round ? b.Round - a.Round
                              : a.Total != b.Total ? b.Total - a.Total
                              : b.Index - a.Index);

        IntPtr e = events.EventCreate(TFMessages.EventWinPanel);
        if (e == IntPtr.Zero)
            return;

        events.EventSetInt(e, "panel_style", 0); // WINPANEL_BASIC (tf_shareddefs.h:1725)
        events.EventSetInt(e, "winning_team", (int)d.WinningTeam);
        events.EventSetInt(e, "winreason", (int)d.Reason);
        events.EventSetString(e, "cappers",
                              d.Reason is WinReason.AllPointsCaptured or WinReason.FlagCaptureLimit
                                  ? cappers.MostRecent
                                  : string.Empty);
        events.EventSetInt(e, "flagcaplimit", capsPerRound.Value);
        events.EventSetInt(e, "blue_score", blue);
        events.EventSetInt(e, "red_score", red);
        events.EventSetInt(e, "blue_score_prev", bluePrev);
        events.EventSetInt(e, "red_score_prev", redPrev);
        events.EventSetInt(e, "round_complete", d.RoundComplete ? 1 : 0);

        // only include players who have non-zero points this round; if we get
        // to a player with 0 round points, stop
        int count = Math.Min(3, scored.Count);
        for (int i = 0; i < count; i++)
        {
            if (scored[i].Round == 0)
                break;

            events.EventSetInt(e, $"player_{i + 1}", scored[i].Index);
            events.EventSetInt(e, $"player_{i + 1}_points", scored[i].Round);
        }

        events.EventSetInt(e, "killstreak_player_1", streakPlayer);
        events.EventSetInt(e, "killstreak_player_1_count", streak);
        events.EventSetBool(e, "game_over", d.GameOver);
        events.EventFire(e);
    }
}
