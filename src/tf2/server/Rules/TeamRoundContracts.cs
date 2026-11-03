using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// The team-round machine as its readers see it: the objective entities
/// (control points, the flag, the train watcher), the modes and the
/// resources the HUD reads. The round flow (Phase 2.1) is its one
/// implementation; a reader takes this by constructor and never the flow.
/// </summary>
public interface ITeamRoundState
{
    /// <summary>The machine's current state.</summary>
    RoundState State { get; }

    /// <summary>Stock's <c>IsInWaitingForPlayers</c>: the pre-game wait before the first round.</summary>
    bool InWaitingForPlayers { get; }

    /// <summary>Stock's <c>InSetup</c>: the running round's setup period (gates open on its end).</summary>
    bool InSetup { get; }

    /// <summary>Stock's <c>m_flMapResetTime</c>: the map clock's start, what
    /// the HUD's server-time arm counts <c>GetTimeLeft</c> from
    /// (teamplayroundbased_gamerules.cpp:1226-1239). Set at level init and
    /// every map reset.</summary>
    float MapResetTime { get; }

    /// <summary>Stock's <c>m_flStalemateStartTime</c>: when the sudden death
    /// began (tf_gamerules.cpp:5561); the class-change window counts from it.
    /// Negative outside one.</summary>
    float StalemateStartTime { get; }

    /// <summary>The team that won the round just ended; <see cref="TFTeam.Unassigned"/> before one has.</summary>
    TFTeam WinningTeam { get; }
}

/// <summary>
/// What an objective tells the round machine: the ends of a round. Mirrors
/// <c>CTeamplayRoundBasedRules::SetWinningTeam</c> and <c>SetStalemate</c>,
/// with their defaults.
/// </summary>
public interface ITeamRoundControl
{
    /// <summary>The round is won by <paramref name="team"/>.</summary>
    void SetWinningTeam(TFTeam team, WinReason reason, bool forceMapReset = true, bool switchTeams = false,
                        bool dontAddScore = false, bool final = false);

    /// <summary>The round ends with no winner.</summary>
    void SetStalemate(StalemateReason reason, bool forceMapReset = true, bool switchTeams = false);

    /// <summary>
    /// Stock's <c>SetAllowStalemateAtTimelimit</c>
    /// (teamplayroundbased_gamerules.cpp:374), the rules proxy's
    /// <c>SetStalemateOnTimelimit</c> input (teamplayroundbased_gamerules.cpp:175):
    /// whether the round timer's expiry may end in sudden death rather than
    /// a restart (<see cref="ITeamRoundState.StalemateStartTime"/>'s arm).
    /// </summary>
    void SetStalemateOnTimelimit(bool stalemate);

    /// <summary>
    /// Stock's <c>SetSwitchTeams</c>/<c>ShouldSwitchTeams</c>
    /// (teamplay_gamerules.h:94-95): the switch of <c>HandleSwitchTeams</c>,
    /// armed by a win's <c>switchTeams</c> (<c>SetWinningTeam</c>,
    /// teamplayroundbased_gamerules.cpp:2346) or the <c>mp_switchteams</c>
    /// command, and done by the next round's start (<c>RoundRespawn</c>,
    /// <c>:2844-2851</c>).
    /// </summary>
    void SetSwitchTeams(bool switchTeams);

    /// <summary>Whether the next round's start switches the teams.</summary>
    bool ShouldSwitchTeams();

    /// <summary>
    /// Stock's <c>SetScrambleTeams</c>/<c>ShouldScrambleTeams</c>
    /// (teamplay_gamerules.h:99-100): the re-draw of <c>HandleScrambleTeams</c>,
    /// armed by the <c>mp_scrambleteams</c> command
    /// (teamplayroundbased_gamerules.cpp:297-321) or — dedicated servers only —
    /// the auto win-counter block (<c>mp_scrambleteams_auto</c>, :2404-2509),
    /// and done by the next round's start (<c>RoundRespawn</c>,
    /// <c>:2853-2858</c>).
    /// </summary>
    void SetScrambleTeams(bool scrambleTeams);

    /// <summary>Whether the next round's start scrambles the teams.</summary>
    bool ShouldScrambleTeams();

    /// <summary>
    /// Stock's <c>ShouldResetScores</c> (teamplayroundbased_gamerules.h:364):
    /// which of the next score reset's two arms — the team scores, the
    /// players' accumulated stats — are live. The <c>mp_switchteams</c> and
    /// <c>mp_scrambleteams</c> commands disarm them so their restart keeps
    /// what the switch swaps and the scramble sorts by
    /// (teamplayroundbased_gamerules.cpp:286, :307); the reset re-arms both
    /// when it runs (<c>:3276-3280</c>).
    /// </summary>
    void ShouldResetScores(bool teamScores, bool playerScores);

    /// <summary>
    /// Stock's <c>ShouldResetRoundsPlayed</c> (<c>:365</c>): whether the next
    /// score reset zeroes the rounds played — disarmed by <c>mp_switchteams</c>
    /// (<c>:287</c>) and by <c>mp_scrambleteams 2</c> (<c>:314</c>).
    /// </summary>
    void ShouldResetRoundsPlayed(bool roundsPlayed);
}
