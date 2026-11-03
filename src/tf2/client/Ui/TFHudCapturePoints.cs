//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Client.Entities;
using TF2.Shared;

namespace TF2.Client.Ui;

/// <summary>
/// The capture points the HUD draws, straight off the client's objective
/// table: every member is a stock getter over the decoded mirror;
/// <see cref="CapProgress"/> runs the client's lazy cap model to now and
/// <see cref="Locked"/> is the icon's own derivation — no game team may
/// cap, the owner ignored (hud_controlpointicons.cpp:414-433).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFCapturePoints)])]
public sealed class TFHudCapturePoints(ITFObjectiveSource objectives, IGameClock clock) : ITFCapturePoints
{
    public int NumPoints => objectives.NumControlPoints;

    public string CapLayout => objectives.CapLayoutInHud;

    public bool Visible(int point) => objectives.IsVisible(point);

    public bool InMiniRound(int point) => objectives.InMiniRound(point);

    public int OwningTeam(int point) => objectives.OwningTeam(point);

    public int CappingTeam(int point) => objectives.CappingTeam(point);

    public float CapProgress(int point) => objectives.CapProgress(point, clock.Now);

    public bool Blocked(int point) => objectives.CapIsBlocked(point);

    public bool Locked(int point)
    {
        for (int team = FirstGameTeam; team <= (int)TFTeam.Blue; team++)
            if (team != objectives.OwningTeam(point) && objectives.TeamCanCap(point, team))
                return false;

        return true;
    }

    public int Cappers(int point, int team) => objectives.NumPlayersInArea(point, team);

    public float TimerSeconds(int point) => objectives.TimerTime(point) - clock.Now;

    public float UnlockSeconds(int point) => objectives.UnlockTime(point) - clock.Now;

    /// <summary>FIRST_GAME_TEAM: the first team that is not a shared one
    /// (tf_team.h); the walk runs to the last game team, TF_TEAM_BLUE.</summary>
    private const int FirstGameTeam = (int)TFTeam.Red;
}
