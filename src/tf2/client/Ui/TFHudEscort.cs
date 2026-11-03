//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Client.Entities;

namespace TF2.Client.Ui;

/// <summary>
/// The escort HUD's reads off the objective mirror: the cart's progress,
/// cappers, speed and recede deadline, the track alarm, and the points and
/// hills along the bar. Stock's CTFHudEscort takes these from game events
/// (escort_progress, escort_speed, escort_recede); here they are the same
/// numbers the server's train watcher writes into tf_objective_resource.
/// The single-train escort follows stock's blue bar (m_nTeam,
/// tf_hud_escort.cpp:445).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFEscort)])]
public sealed class TFHudEscort(ITFObjectiveSource objectives, IGameClock clock) : ITFEscort
{
    /// <summary>Stock's TF_TEAM_BLUE, as a literal: this assembly holds no
    /// TF2.Shared reference.</summary>
    private const int Team = 3;

    public bool Present
    {
        get
        {
            for (int point = 0; point < objectives.NumControlPoints; point++)
                if (PointOnTrack(point) && objectives.PathDistance(point) > 0f)
                    return true;
            return false;
        }
    }

    public float Progress => objectives.TrainProgress(Team);

    public int Cappers => objectives.TrainCappers(Team);

    public int SpeedLevel => objectives.TrainSpeedLevel(Team);

    public float RecedeSeconds
        => MathF.Max(0f, MathF.Max(0f, objectives.TrainRecedeTime(Team)) - clock.Now);

    public bool Alarm => objectives.TrackAlarm(Team);

    public int Points => objectives.NumControlPoints;

    public bool MultipleTrains => objectives.MultipleTrains;

    /// <summary>Stock's icon validity arm (tf_hud_escort.cpp:1075-1080): on
    /// a multiple-train map only the local team's group's points show
    /// (GetCPGroup != m_nTeam - 2 is skipped).</summary>
    public bool PointOnTrack(int point)
        => objectives.InMiniRound(point) && objectives.IsVisible(point)
           && (!objectives.MultipleTrains || objectives.Group(point) == Team - 2);

    public float PathDistance(int point) => objectives.PathDistance(point);

    public bool Blocked(int point) => objectives.CapIsBlocked(point);

    public int OwningTeam(int point) => objectives.OwningTeam(point);

    public int NumHills => objectives.NumNodeHillData(Team);

    public bool Hill(int hill, out float start, out float end)
        => objectives.HillData(Team, hill, out start, out end);

    public bool HillDownhill(int hill) => objectives.IsHillDownhill(Team, hill);
}
