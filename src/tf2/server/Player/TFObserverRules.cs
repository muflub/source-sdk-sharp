//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// CTFPlayer's own observer rules over the core's <see cref="Observers"/>:
/// <list type="bullet">
/// <item><see cref="Filter"/> is <c>CTFPlayer::SetObserverMode</c>'s mode arm
/// (tf_player.cpp:13718-13750): point of interest is roaming outside
/// Passtime; a player on a team (a dead one) is never roaming but in-eye,
/// and with <c>mp_fadetoblack</c> only chase; and none of CBasePlayer's
/// <c>mp_forcecamera</c> remap, which the override does not call.</item>
/// <item><see cref="Enter"/> is <c>StateEnterOBSERVER</c> (:13835-13870):
/// "always start a spectator session in chase mode" (the remembered mode
/// becomes chase), the first target the nearest watchable teammate of the
/// same class first (<c>FindInitialObserverTarget</c>'s last arm,
/// <c>FindNearestObservableTarget</c>, :16502), the current mode kept when it
/// is already a watching one (past the freeze cam) and the remembered one
/// otherwise, and fixed with a target is in-eye.</item>
/// </list>
/// Not ported, each named: Passtime's point of interest; the tournament and
/// match-group spectator lock (bAllowSpecModeChange); the active boss and
/// the contested control point arms of FindInitialObserverTarget; the
/// Engineer's own sentry as the last resort; the AFK clock.
/// </summary>
[SideService(Side.Server)]
public sealed class TFObserverRules(IEntityDirectory directory, IEnginePlayers players, IEngineEntities entities,
                                    TFPlayerCarriers carriers, Observers observers, FadeToBlackConVar fadeToBlack,
                                    SpecFreezeTravelTimeConVar travelTime)
    : IObserverRules
{
    /// <summary>
    /// <c>CTFPlayer::AttemptToExitFreezeCam</c> (tf_player.cpp:14017-14024):
    /// once the camera has arrived (the death, TF_DEATH_ANIMATION_TIME, the
    /// travel and half a second), the dying aborts the freeze cam
    /// (<see cref="TFDeathCam"/> watches in chase then); before it, nothing.
    /// Never CBasePlayer's return to the death cam.
    /// </summary>
    public bool ExitFreezeCam(SsPlayer player, float now)
    {
        float arrived = player.Observer.DeathTime + TFDeathCam.DeathAnimationTime + travelTime.Value + 0.5f;
        if (now >= arrived)
            player.Observer.AbortFreezeCam = true;
        return true;
    }

    public ObserverMode Filter(SsPlayer player, ObserverMode mode)
    {
        // Skip over OBS_MODE_POI if we're not in Passtime mode.
        if (mode == ObserverMode.PointOfInterest)
            mode = ObserverMode.Roaming;

        // Skip over OBS_MODE_ROAMING for dead players.
        if (players.GetTeam(player.Handle) > (int)TFTeam.Spectator)
        {
            if (!player.State.Alive && mode > ObserverMode.Fixed && fadeToBlack.Value)
                mode = ObserverMode.Chase;
            else if (mode == ObserverMode.Roaming)
                mode = ObserverMode.InEye;
        }

        return mode;
    }

    public ObserverMode Enter(SsPlayer player, ObserverMode asked, float now)
    {
        // Always start a spectator session in chase mode.
        player.Observer.LastMode = ObserverMode.Chase;

        if (Nearest(player, now) is { } target)
            player.State.ObserverTarget = target.Handle.Value;

        var current = (ObserverMode)player.State.ObserverMode;
        var mode = current <= ObserverMode.FreezeCam ? player.Observer.LastMode : current;

        // If we're in fixed mode, but we found an observer target, move to non fixed.
        if (observers.TargetOf(player) is not null && mode == ObserverMode.Fixed)
            mode = ObserverMode.InEye;

        return mode;
    }

    /// <summary>
    /// CTFPlayer::FindNearestObservableTarget( GetAbsOrigin(), FLT_MAX ): the
    /// nearest watchable player of the watcher's team (anyone's, for a
    /// spectator), and once one of the watcher's own class is found only a
    /// nearer one of that class replaces it.
    /// </summary>
    public SsPlayer? Nearest(SsPlayer player, float now)
    {
        int team = players.GetTeam(player.Handle);
        TFClass own = carriers.ClassOf(player.Handle);
        Vector origin = entities.GetOrigin(player.Handle);

        SsPlayer? found = null;
        bool foundClass = false;
        float best = float.MaxValue;
        foreach (var entity in directory.OfClass(EntityNames.Player))
        {
            if (entity is not SsPlayer candidate)
                continue;
            if (team != (int)TFTeam.Spectator && players.GetTeam(candidate.Handle) != team)
                continue;
            if (!observers.IsValidTarget(player, candidate, now))
                continue;

            float distance = (entities.GetOrigin(candidate.Handle) - origin).LengthSquared;
            bool sameClass = carriers.ClassOf(candidate.Handle) == own;
            if (distance < best)
            {
                if (!foundClass || sameClass)
                {
                    found = candidate;
                    best = distance;
                    foundClass |= sameClass;
                }
            }
            else if (!foundClass && sameClass)
            {
                found = candidate;
                best = distance;
                foundClass = true;
            }
        }

        return found;
    }
}
