using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities;
using TF2.Shared;

namespace TF2.Server.Rules;

/// <summary>
/// The team numbers stock kept on <c>CTeam</c>/<c>CTFTeam</c> (<c>m_iScore</c>,
/// <c>m_nFlagCaptures</c>, <c>m_iRole</c>), as the round machine and the modes
/// write them: a typed face over the networked team table
/// (<see cref="ITeamResource"/>, <c>tf_team_resource</c>), so every write is
/// on the wire; the scoreboard's team scores also reach the core
/// <c>PlayerResource</c> through <see cref="TFTeamScoring"/>.
/// </summary>
public interface ITFTeamScores
{
    /// <summary>The team's score (round wins, or captures under a per-capture master).</summary>
    int Score(TFTeam team);

    /// <summary>Stock <c>CTeam::AddScore</c>.</summary>
    void AddScore(TFTeam team, int points);

    /// <summary>
    /// Stock <c>CTeam::SetScore</c> (team.h:68): the score as it is, the pair
    /// of <c>GetScore</c> that <c>HandleSwitchTeams</c> swaps the two teams'
    /// with (tf_gamerules.cpp:16516-16524).
    /// </summary>
    void SetScore(TFTeam team, int score);

    /// <summary>CTF captures this round (stock <c>CTFTeam::m_nFlagCaptures</c>).</summary>
    int FlagCaptures(TFTeam team);

    /// <summary>Stock <c>CTFTeam::IncrementFlagCaptures</c>.</summary>
    void AddFlagCapture(TFTeam team);

    /// <summary>Stock <c>CTFTeam::SetFlagCaptures</c>, for <c>tf_flag_caps_per_round</c>'s reset.</summary>
    void SetFlagCaptures(TFTeam team, int captures);

    /// <summary>The team's attack/defend role (stock <c>TEAM_ROLE_*</c>: 0 none, 1 defenders, 2 attackers).</summary>
    int Role(TFTeam team);

    void SetRole(TFTeam team, int role);

    /// <summary>Stock <c>CTeam::ResetScores</c> for every team: score and captures.</summary>
    void ResetScores();
}

/// <summary>The team scores over the networked team table (see <see cref="ITFTeamScores"/>).</summary>
[SideService(Side.Server, As = [typeof(ITFTeamScores)])]
public sealed class TFTeamScores(ITeamResource table) : ITFTeamScores
{
    public int Score(TFTeam team) => Valid(team) ? table.Score((int)team) : 0;

    public void AddScore(TFTeam team, int points)
    {
        if (Valid(team))
            table.SetScore((int)team, table.Score((int)team) + points);
    }

    public void SetScore(TFTeam team, int score)
    {
        if (Valid(team))
            table.SetScore((int)team, score);
    }

    public int FlagCaptures(TFTeam team) => Valid(team) ? table.FlagCaptures((int)team) : 0;

    public void AddFlagCapture(TFTeam team)
    {
        if (Valid(team))
            table.SetFlagCaptures((int)team, table.FlagCaptures((int)team) + 1);
    }

    public void SetFlagCaptures(TFTeam team, int captures)
    {
        if (Valid(team))
            table.SetFlagCaptures((int)team, captures);
    }

    public int Role(TFTeam team) => Valid(team) ? table.Role((int)team) : 0;

    public void SetRole(TFTeam team, int role)
    {
        if (Valid(team))
            table.SetRole((int)team, role);
    }

    public void ResetScores()
    {
        for (var team = TFTeam.Unassigned; team < TFTeam.Count; team++)
        {
            table.SetScore((int)team, 0);
            table.SetFlagCaptures((int)team, 0);
        }
    }

    private static bool Valid(TFTeam team) => team is >= TFTeam.Unassigned and < TFTeam.Count;
}

/// <summary>
/// The TF2 modes' <see cref="IScoring"/>: the team table's scores, which the
/// core <c>PlayerResource</c> puts on the wire as the scoreboard's team
/// scores. A death adds nothing (TF2's team score is round wins and
/// captures); a mode switch or a level zeroes the table.
/// </summary>
public sealed class TFTeamScoring(ITFTeamScores scores) : IScoring
{
    public void OnDeath(PlayerDeathArgs death) { }

    public int TeamScore(int team) => team is >= (int)TFTeam.Red and <= (int)TFTeam.Blue
        ? scores.Score((TFTeam)team)
        : -1;

    public void ResetRound() => scores.ResetScores();
}
