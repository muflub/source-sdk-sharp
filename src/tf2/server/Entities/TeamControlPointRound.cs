//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Shared;
using TF2.Shared.Resources;

namespace TF2.Server.Entities;

/// <summary>
/// team_control_point_round: a mini-round — a named set of points the master
/// plays one at a time, by priority (stock's CTeamControlPointRound,
/// team_control_point_round.cpp).
/// </summary>
[ScriptedEntity(MapClass)]
public sealed class TeamControlPointRound(IObjectiveEntities objectives, IControlPointRules rules,
                                          IKothRoundState koth) : ScriptedEntity
{
    public const string MapClass = "team_control_point_round";

    /// <summary>TEAM_INVALID.</summary>
    internal const int TeamInvalid = -1;

    public readonly Output OnStart = new();
    public readonly Output OnEnd = new();
    public readonly Output OnWonByTeam1 = new();
    public readonly Output OnWonByTeam2 = new();

    [ScriptVars] public TeamControlPointRoundVars Vars = new();

    /// <summary>m_ControlPoints: AddToHead, so the reverse of the order they were found in.</summary>
    private readonly List<TeamControlPoint> _points = new();

    private bool _disabled;

    public override void OnSpawn() => _disabled = Vars.StartDisabled;

    public override void OnServerActivate() => FindControlPoints();

    public IReadOnlyList<TeamControlPoint> ControlPoints => _points;

    public int Priority => Vars.Priority;

    public int InvalidCapWinner => Vars.RestrictTeamCapWin;

    public string PrintName => Vars.PrintName;

    public bool Disabled => _disabled;

    [ScriptedEvent] private void OnEnable() => _disabled = false;

    [ScriptedEvent] private void OnDisable() => _disabled = true;

    [ScriptedEvent]
    private void OnRoundSpawn()
    {
        // clear out old control points
        _points.Clear();

        // find the control points
        FindControlPoints();
    }

    /// <summary>
    /// FindControlPoints: every point named in cpr_cp_names. Stock's match,
    /// HACK and all: the first case-insensitive occurrence of the point's
    /// name, followed by a space or the end ("to work around a problem with
    /// cp_a being returned for an entity name with cp_A").
    /// </summary>
    private void FindControlPoints()
    {
        if (objectives.Master is not null)
        {
            string names = Vars.ControlPointNames;
            foreach (var point in objectives.Points)
            {
                string name = point.Name;
                int pos = names.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                if (pos < 0)
                    continue;

                int end = pos + name.Length;
                if ((end == names.Length || names[end] == ' ') && !_points.Contains(point))
                    _points.Insert(0, point);
            }
        }

        if (_points.Count == 0)
            Log.Warning($"Error! No control points found in map for team_game_round {Name}!\n");
    }

    /// <summary>CheckWinConditions: the team that owns every point, or -1.</summary>
    public int CheckWinConditions()
    {
        int winners = TeamOwnsAllPoints();
        // Stock's koth arm (team_control_point_round.cpp:122-141): while the
        // holder's koth timer still runs (or the machine refuses expiry)
        // there is no winner here — the -1 leaves the master's round path
        // idle, and the win output waits too.
        if (Vars.RestrictTeamCapWin != 1 && winners >= (int)TFTeam.Red && winners != Vars.RestrictTeamCapWin
            && koth.WinMayStand((TFTeam)winners))
        {
            FireTeamWinOutput(winners);
            return winners;
        }

        return -1;
    }

    private void FireTeamWinOutput(int team)
    {
        if (team == (int)TFTeam.Red)
            OnWonByTeam1.Fire();
        else if (team == (int)TFTeam.Blue)
            OnWonByTeam2.Fire();
    }

    public int GetPointOwner(int point) => _points[point].Owner;

    /// <summary>TeamOwnsAllPoints over this round's points (the master's, per group).</summary>
    public int TeamOwnsAllPoints(TeamControlPoint? overridePoint = null, int overrideNewTeam = (int)TFTeam.Unassigned)
        => TeamOwnership.OwnsAllPoints(_points, overridePoint, overrideNewTeam, Log);

    public bool WouldNewCPOwnerWinGame(TeamControlPoint point, int newOwner)
        => TeamOwnsAllPoints(point, newOwner) == newOwner;

    /// <summary>SelectedToPlay: SetupSpawnPoints, through the rules' SetupSpawnPointsForRound.</summary>
    public void SelectedToPlay() => rules.SetupSpawnPointsForRound();

    public void FireOnStartOutput() => OnStart.Fire();

    public void FireOnEndOutput() => OnEnd.Fire();

    public bool IsControlPointInRound(TeamControlPoint? point) => point is not null && _points.Contains(point);

    /// <summary>IsPlayable: nobody has already won this round by capping.</summary>
    public bool IsPlayable
    {
        get
        {
            int winners = TeamOwnsAllPoints();

            // neither team can win this round by capping
            if (Vars.RestrictTeamCapWin == 1)
                return true;

            // someone has already won this round
            if (winners >= (int)TFTeam.Red && winners != Vars.RestrictTeamCapWin)
                return false;

            return true;
        }
    }

    /// <summary>
    /// MakePlayable: give a non-base point of this round to a team through
    /// its capture area, as stock does (its test is "would NOT win").
    /// </summary>
    public bool MakePlayable()
    {
        var master = objectives.Master;
        if (master is null || IsPlayable)
            return false;

        // we need to try switching the owners of the teams to make this round playable
        for (int team = (int)TFTeam.Red; team < ObjectiveLimits.Teams; team++)
        {
            foreach (var point in _points)
            {
                if (master.IsBaseControlPoint(point.PointIndex) || WouldNewCPOwnerWinGame(point, team))
                    continue;

                // need to find the trigger area associated with this point
                foreach (var area in objectives.Areas)
                {
                    if (area.TeamCanCap(team) && ReferenceEquals(area.ControlPoint, point))
                    {
                        // found! this updates the trigger_area *and* the control_point
                        area.ForceOwner(team);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>GetPointOwnedBy: the first point the team owns.</summary>
    public TeamControlPoint? GetPointOwnedBy(int team) => _points.FirstOrDefault(p => p.Owner == team);
}

/// <summary>The map keys <see cref="TeamControlPointRound"/> declares (team_control_point_round.cpp:14-19).</summary>
[ScriptVars]
public partial class TeamControlPointRoundVars
{
    public bool StartDisabled { get; set; }
    [VarName("cpr_cp_names")] public string ControlPointNames { get; set; } = string.Empty;
    [VarName("cpr_priority")] public int Priority { get; set; }
    [VarName("cpr_restrict_team_cap_win")] public int RestrictTeamCapWin { get; set; }
    [VarName("cpr_printname")] public string PrintName { get; set; } = string.Empty;
}

/// <summary>
/// TeamOwnsAllPoints, which the master and the round each carry a copy of
/// (team_control_point_master.cpp L986, team_control_point_round.cpp L197):
/// the first group whose points all have one game-team owner wins.
/// </summary>
public static class TeamOwnership
{
    public static int OwnsAllPoints(IEnumerable<TeamControlPoint> points, TeamControlPoint? overridePoint,
                                    int overrideNewTeam, ILog log)
    {
        // if TEAM_INVALID, haven't found a flag for this group yet
        // if TEAM_UNASSIGNED, the group is still being contested
        Span<int> winning = stackalloc int[ObjectiveLimits.MaxControlPointGroups];
        winning.Fill(TeamControlPointRound.TeamInvalid);

        foreach (var point in points)
        {
            int group = point.Group;
            if ((uint)group >= ObjectiveLimits.MaxControlPointGroups)
            {
                // Stock indexes past its array; a group out of range is skipped here.
                log.Warning($"[TF2] {TeamControlPoint.MapClass} '{point.Name}': point_group {group} is outside 0..{ObjectiveLimits.MaxControlPointGroups - 1}\n");
                continue;
            }

            int owner = ReferenceEquals(point, overridePoint) ? overrideNewTeam : point.Owner;

            // the first one we find in this group, set the win to true
            if (winning[group] == TeamControlPointRound.TeamInvalid)
                winning[group] = owner;
            // unassigned means this group is already contested, move on
            else if (winning[group] == (int)TFTeam.Unassigned)
                continue;
            // if we find another one in the group that isn't the same owner, set the win to false
            else if (owner != winning[group])
                winning[group] = (int)TFTeam.Unassigned;
        }

        // report the first win we find as the winner
        for (int i = 0; i < ObjectiveLimits.MaxControlPointGroups; i++)
        {
            if (winning[i] >= (int)TFTeam.Red)
                return winning[i];
        }

        // no wins yet
        return (int)TFTeam.Unassigned;
    }
}
