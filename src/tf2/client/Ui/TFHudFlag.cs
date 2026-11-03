//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Client.Entities;
using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The CTF flags the HUD draws, straight off the client's objective
/// mirror: each game team's flag status and reset deadline, the captures
/// stock's OnTick sets as dialog variables
/// (tf_hud_flagstatus.cpp:643-679), the compass bearings
/// (<c>CTFArrowPanel::GetAngleRotation</c>, :167-207) and the
/// carried-by-local read (<c>UpdateStatus</c>, :779-794). Stock's HUD
/// collected all of these by walking the capture-flag and capture-zone
/// entities on a tick; here they are the same numbers the server writes
/// into tf_objective_resource.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFFlagStatus)])]
public sealed class TFHudFlag(ITFObjectiveSource objectives, ITFTeamSource teams, TFFlagCapsPerRoundConVar capsPerRound,
                              IGameClock clock, IEngineHud screen, IEngineEffects effects, ViewReadout view,
                              TFClientPlayerResources resources, IClientStandalones standalones) : ITFFlagStatus
{
    /// <summary>FIRST_GAME_TEAM: the first team that is not a shared one
    /// (tf_team.h); the walk runs to the last game team, TF_TEAM_BLUE.</summary>
    private const int FirstGameTeam = (int)TFTeam.Red;

    /// <summary>ETFFlagStatus: stolen (tf_shareddefs.h).</summary>
    private const int Stolen = 1;

    public bool Present
    {
        get
        {
            for (int team = FirstGameTeam; team <= (int)TFTeam.Blue; team++)
                if (Has(team))
                    return true;
            return false;
        }
    }

    public bool Has(int team) => objectives.HasFlag(team);

    public int Status(int team) => objectives.FlagStatus(team);

    public float ResetSeconds(int team)
        => MathF.Max(0f, objectives.FlagResetTime(team) - clock.Now);

    public int Captures(int team)
        => CapsPerRound > 0 ? teams.FlagCaptures(team) : teams.Score(team);

    public int CapsPerRound => Math.Max(0, capsPerRound.Value);

    public bool CarriedByLocal(int team)
        => objectives.FlagStatus(team) == Stolen && objectives.FlagCarrier(team) == screen.LocalPlayerIndex;

    public float Bearing(int team) => BearingTo(FlagOrigin(team));

    public float ZoneBearing
    {
        get
        {
            int local = screen.LocalPlayerIndex;
            if (local <= 0 || local >= TFPlayerResourceNetState.Slots)
                return 0f;

            return BearingTo(objectives.ZoneOrigin(resources.Live?.State.Team[local] ?? 0));
        }
    }

    /// <summary>
    /// GetAngleRotation: the flag's flattened direction against the view
    /// yaw — <c>acos(dot(flag, forward))</c>, negated when the flag sits
    /// to the left of it (:193-203). Where the flag is now: the
    /// networked origin while home or dropped, the carrier's live origin
    /// while stolen.
    /// </summary>
    private float BearingTo(Vector origin)
    {
        if (!view.Known)
            return 0f;

        Vector flat = origin - view.Eye;
        flat.Z = 0f;
        float length = MathF.Sqrt(flat.X * flat.X + flat.Y * flat.Y);
        if (length < 1f)
            return 0f;
        flat.X /= length;
        flat.Y /= length;

        float yaw = view.Yaw * MathF.PI / 180f;
        float forwardX = MathF.Cos(yaw), forwardY = MathF.Sin(yaw);
        float rightX = MathF.Sin(yaw), rightY = -MathF.Cos(yaw);

        float angle = MathF.Acos(Math.Clamp(flat.X * forwardX + flat.Y * forwardY, -1f, 1f));
        if (flat.X * rightX + flat.Y * rightY < 0f)
            angle = -angle;

        return angle * 180f / MathF.PI;
    }

    /// <summary>The flag's world origin: the carrier's live origin while stolen, else the networked one.</summary>
    private Vector FlagOrigin(int team)
        => objectives.FlagStatus(team) == Stolen && objectives.FlagCarrier(team) > 0
           && effects.ClientEntityOrigin(objectives.FlagCarrier(team), out Vector carried)
               ? carried
               : objectives.FlagOrigin(team);

    /// <summary>
    /// The no-arrow arm's read (tf_hud_flagstatus.cpp:127-159): the local
    /// player's <c>ObserverMode</c> is <c>OBS_MODE_IN_EYE</c> — the same
    /// walk the in-eye material proxies' view player makes — and their
    /// <c>ObserverTarget</c> (the handle's entry, the low 16 bits) is the
    /// team's stolen flag's carrier, stock's
    /// <c>HasTheFlag() &amp;&amp; GetItem() == pEnt</c> against the flag this
    /// panel tracks.
    /// </summary>
    public bool SpectatingCarrier(int team)
    {
        if (screen.LocalPlayerIndex <= 0
            || standalones.GetByIndex(screen.LocalPlayerIndex)
               is not ClientSsPlayer { State.ObserverMode: GameMovement.ObsModeInEye } viewer)
            return false;

        uint target = viewer.State.ObserverTarget;
        if (target == EntityHandle.None.Value || target == 0xFFFFFFFFu)
            return false;

        int index = (int)(target & ClientPlayerPrediction.HandleEntryMask);
        return index > 0 && objectives.FlagStatus(team) == Stolen && objectives.FlagCarrier(team) == index;
    }
}
