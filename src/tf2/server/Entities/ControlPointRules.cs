//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Resources;
using TF2.Shared.Rules;

namespace TF2.Server.Entities;

/// <summary>
/// The objective entities, found by class name: stock's g_hControlPointMasters
/// (the first one is "the" master) and the auto-lists the TF rules walk.
/// </summary>
public interface IObjectiveEntities
{
    /// <summary>g_hControlPointMasters[0], or null on a map with none.</summary>
    TeamControlPointMaster? Master { get; }

    IEnumerable<TeamControlPointMaster> Masters { get; }
    IEnumerable<TeamControlPoint> Points { get; }
    IEnumerable<TeamControlPointRound> Rounds { get; }
    IEnumerable<TriggerAreaCapture> Areas { get; }
    IEnumerable<InfoPlayerTeamSpawn> Spawns { get; }

    /// <summary>gEntList.FindEntityByName + dynamic_cast: the named entity if it is a T.</summary>
    T? FindByName<T>(string name) where T : ScriptedEntity;
}

/// <summary><see cref="IObjectiveEntities"/> over the side's entity directory.</summary>
[SideService(Side.Server, As = [typeof(IObjectiveEntities)])]
public sealed class ObjectiveEntities(IEntityDirectory directory) : IObjectiveEntities
{
    public TeamControlPointMaster? Master => Masters.FirstOrDefault();

    public IEnumerable<TeamControlPointMaster> Masters => directory.OfClass(TeamControlPointMaster.MapClass).OfType<TeamControlPointMaster>();

    public IEnumerable<TeamControlPoint> Points => directory.OfClass(TeamControlPoint.MapClass).OfType<TeamControlPoint>();

    public IEnumerable<TeamControlPointRound> Rounds => directory.OfClass(TeamControlPointRound.MapClass).OfType<TeamControlPointRound>();

    public IEnumerable<TriggerAreaCapture> Areas => directory.OfClass(TriggerAreaCapture.MapClass).OfType<TriggerAreaCapture>();

    public IEnumerable<InfoPlayerTeamSpawn> Spawns => directory.OfClass(InfoPlayerTeamSpawn.MapClass).OfType<InfoPlayerTeamSpawn>();

    public T? FindByName<T>(string name) where T : ScriptedEntity
        => name.Length == 0 ? null : directory.FindByName(name) as T;
}

/// <summary>
/// The rules the control points answer to: the CTFGameRules members the
/// stack calls that are about the objectives rather than the round
/// (tf_gamerules.cpp): whether points may be captured now, whether a team may
/// capture a given point (tf_caplinear), the farthest point a team holds,
/// and which team spawns are live for the points a team holds.
/// </summary>
public interface IControlPointRules
{
    /// <summary>
    /// PointsMayBeCaptured: the round is running (or in stalemate), not
    /// waiting for players, and not in setup (CTFGameRules L22047 over
    /// CTeamplayRoundBasedRules, teamplayroundbased_gamerules.h:192).
    /// </summary>
    bool PointsMayBeCaptured { get; }

    /// <summary>TeamMayCapturePoint (tf_gamerules.cpp L16769).</summary>
    bool TeamMayCapturePoint(int team, int pointIndex);

    /// <summary>GetFarthestOwnedControlPoint (L16734).</summary>
    int GetFarthestOwnedControlPoint(int team, bool withSpawnpoints);

    /// <summary>RecalculateControlPointState (L4739): enable the spawns of each team's farthest point.</summary>
    void RecalculateControlPointState();

    /// <summary>
    /// SetupOnRoundStart's spawn half (L4876-4893): record which points have
    /// team spawns, disable those spawns, and recalculate. The round flow
    /// calls this after the RoundSpawn/RoundActivate walk.
    /// </summary>
    void SetupControlPointSpawns();

    /// <summary>SetupSpawnPointsForRound (L13849): the mini-round's spawns on, the rest off.</summary>
    void SetupSpawnPointsForRound();

    /// <summary>m_bControlSpawnsPerTeam[team][point].</summary>
    bool ControlSpawnsFor(int team, int pointIndex);
}

/// <summary>The TF rules for the control points (<see cref="IControlPointRules"/>).</summary>
[SideService(Side.Server, As = [typeof(IControlPointRules)])]
public sealed class ControlPointRules(ITeamRoundState round, IObjectiveResource objectives,
                                      IObjectiveSettings settings, IObjectiveEntities entities,
                                      IKothRoundState koth) : IControlPointRules
{
    private readonly bool[,] _controlSpawnsPerTeam = new bool[ObjectiveLimits.Teams, ObjectiveLimits.MaxControlPoints];

    public bool PointsMayBeCaptured
    {
        get
        {
            // CTFGameRules: no capture in setup (stock asks GetActiveRoundTimer() && InSetup()).
            if (round.InSetup)
                return false;

            return (round.State == RoundState.RoundRunning || round.State == RoundState.Stalemate)
                   && !round.InWaitingForPlayers;
        }
    }

    public bool TeamMayCapturePoint(int team, int pointIndex)
    {
        if (!settings.CapLinear)
            return true;

        // Any previous points necessary?
        int pointNeeded = objectives.GetPreviousPointForPoint(pointIndex, team, 0);

        // Points set to require themselves are always cappable.
        if (pointNeeded == pointIndex)
            return true;

        // Stock's koth arm (shared/tf/tf_gamerules.cpp:16781-16782): a koth
        // map captures nothing while it is waiting for players — the round
        // cannot be decided before the teams are all in.
        if (koth.InKothMode && round.InWaitingForPlayers)
            return false;

        // Is the point locked?
        if (objectives.GetCPLocked(pointIndex))
            return false;

        // No required points specified? Require all previous points.
        if (pointNeeded == -1)
        {
            if (!objectives.PlayingMiniRounds)
            {
                // No custom previous point, team must own all previous points.
                int farthest = GetFarthestOwnedControlPoint(team, withSpawnpoints: false);
                return Math.Abs(farthest - pointIndex) <= 1;
            }

            // No custom previous point, team must own all previous points in the current mini-round.
            return true;
        }

        // Loop through each previous point and see if the team owns it.
        for (int previous = 0; previous < ObjectiveLimits.MaxPreviousPoints; previous++)
        {
            pointNeeded = objectives.GetPreviousPointForPoint(pointIndex, team, previous);
            if (pointNeeded != -1 && objectives.GetOwningTeam(pointNeeded) != team)
                return false;
        }

        return true;
    }

    public int GetFarthestOwnedControlPoint(int team, bool withSpawnpoints)
    {
        int ownedEnd = objectives.GetBaseControlPointForTeam(team);
        if (ownedEnd == -1)
            return -1;

        int numControlPoints = objectives.NumControlPoints;
        int walk = 1;
        int enemyEnd = numControlPoints - 1;
        if (ownedEnd != 0)
        {
            walk = -1;
            enemyEnd = 0;
        }

        // Walk towards the other side, and find the farthest owned point that has spawn points.
        int farthest = ownedEnd;
        for (int point = ownedEnd; point != enemyEnd; point += walk)
        {
            // If we've hit a point we don't own, we're done.
            if (objectives.GetOwningTeam(point) != team)
                break;

            if (withSpawnpoints && !ControlSpawnsFor(team, point))
                continue;

            farthest = point;
        }

        return farthest;
    }

    public void RecalculateControlPointState()
    {
        if (entities.Master is null)
            return;

        if (objectives.PlayingMiniRounds)
            return;

        for (int team = (int)TFTeam.Red; team < ObjectiveLimits.Teams; team++)
        {
            int farthest = GetFarthestOwnedControlPoint(team, withSpawnpoints: true);
            if (farthest == -1)
                continue;

            // Now enable all spawn points for that spawn point.
            foreach (var spawn in entities.Spawns)
            {
                if (spawn.ControlPoint is { } point && spawn.Team == team)
                    spawn.Disabled = point.PointIndex != farthest;
            }
        }
    }

    public void SetupControlPointSpawns()
    {
        if (objectives.PlayingMiniRounds)
            return;

        // Find all the control points with associated spawnpoints.
        Array.Clear(_controlSpawnsPerTeam);
        foreach (var spawn in entities.Spawns)
        {
            if (spawn.ControlPoint is { } point && (uint)spawn.Team < ObjectiveLimits.Teams
                && (uint)point.PointIndex < ObjectiveLimits.MaxControlPoints)
            {
                _controlSpawnsPerTeam[spawn.Team, point.PointIndex] = true;
                spawn.Disabled = true;
            }
        }

        RecalculateControlPointState();
    }

    public void SetupSpawnPointsForRound()
    {
        var master = entities.Master;
        if (master is null || !master.PlayingMiniRounds)
            return;

        var current = master.CurrentRound;
        if (current is null)
            return;

        // Loop through the spawn points in the map and find which ones are
        // associated with this round or the control points in this round.
        foreach (var spawn in entities.Spawns)
        {
            if (spawn.ControlPoint is { } point && current.IsControlPointInRound(point))
            {
                // this spawn is associated with one of our control points
                spawn.Disabled = false;
                spawn.Team = point.Owner;
            }
            else if (spawn.RoundBlueSpawn is { } blue && ReferenceEquals(blue, current))
            {
                spawn.Disabled = false;
                spawn.Team = (int)TFTeam.Blue;
            }
            else if (spawn.RoundRedSpawn is { } red && ReferenceEquals(red, current))
            {
                spawn.Disabled = false;
                spawn.Team = (int)TFTeam.Red;
            }
            else
            {
                // this spawn isn't associated with this round or the control points in this round
                spawn.Disabled = true;
            }
        }
    }

    public bool ControlSpawnsFor(int team, int pointIndex)
        => (uint)team < ObjectiveLimits.Teams && (uint)pointIndex < ObjectiveLimits.MaxControlPoints
           && _controlSpawnsPerTeam[team, pointIndex];
}
