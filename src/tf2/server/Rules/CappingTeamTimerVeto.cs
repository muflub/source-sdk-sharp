//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities;
using TF2.Shared;

namespace TF2.Server.Rules;

/// <summary>
/// The capping-team walk of <c>CTFGameRules::TimerMayExpire</c>
/// (shared/tf/tf_gamerules.cpp:14004-14020): while any control point has a
/// capping team the round timer may not expire, and not for a tenth of a
/// second after the last one either — the cap-in-progress buffer that keeps a
/// cap's AddTime output from losing the race with a round timer's OnFinished.
/// The mode and the train watchers answer the rest of stock's function (the
/// flag arm in <see cref="TF2.Server.Modes.CtfMode"/>, the watcher walk in
/// <c>TrainWatcherTimerVeto</c>).
/// </summary>
[SideService(Side.Server, As = [typeof(ITimerExpiryVeto)])]
public sealed class CappingTeamTimerVeto(IObjectiveResource objectives, IGameClock clock) : ITimerExpiryVeto, IOrderedHook
{
    /// <summary>Stock's <c>m_flCapInProgressBuffer</c>: expiry is refused until this time.</summary>
    private float _capInProgressBuffer;

    public int HookOrder => TeamRoundOrders.CappingTeamTimerVeto;

    public bool TimerMayExpire()
    {
        int numControlPoints = objectives.NumControlPoints;
        for (int iPoint = 0; iPoint < numControlPoints; iPoint++)
        {
            if (objectives.GetCappingTeam(iPoint) != (int)TFTeam.Unassigned)
            {
                _capInProgressBuffer = clock.Now + 0.1f;
                return false;
            }
        }

        // Stock: this delay prevents an order-of-operations issue with caps
        // that fire an AddTime output, and round timers' OnFinished output.
        if (_capInProgressBuffer >= clock.Now)
            return false;

        return true;
    }
}
