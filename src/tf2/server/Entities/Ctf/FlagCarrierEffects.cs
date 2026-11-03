//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// The game's <see cref="IFlagCarrierEffects"/>: what the flag's pick-up and
/// capture do to players beyond the flag. A spy who takes the intelligence
/// loses the disguise — <c>CCaptureFlag::PickUp</c>
/// (entity_capture_flag.cpp:1235-1243) asks <c>CTFPlayer::RemoveDisguise</c>
/// for the player who took it, which the spy slice answers through
/// <see cref="ITFSpyState"/>. A capture crits the capping team —
/// <c>CTFGameRules::HandleCTFCaptureBonus</c> (tf_gamerules.cpp:20523-20538):
/// while <c>GetCTFCaptureBonusTime</c> runs (tf_gamerules.h:451-460 — the
/// map entity's <c>SetCTFCaptureBonusTime</c> override when it stands, else
/// <c>tf_ctf_bonus_time</c>, default 10 :1114), every living player of the
/// capping team takes <c>TF_COND_CRITBOOSTED_CTF_CAPTURE</c> for that long.
/// </summary>
[SideService(Side.Server, As = [typeof(IFlagCarrierEffects)])]
public sealed class FlagCarrierEffects(ITeamRoundRoster roster, ITFPlayerConditions conditions,
                                       ICtfPlayers players, ITFSpyState spies,
                                       TFCTFBonusTimeConVar bonusTime) : IFlagCarrierEffects, IGameLifecycle
{
    /// <summary>The map's override; -1 is stock's ctor <c>SetCTFCaptureBonusTime(-1)</c> (tf_gamerules.cpp:3338).</summary>
    private float _captureBonusTime = -1f;

    int IGameLifecycle.LifecycleOrder => CtfOrders.CaptureBonusTime;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        // The rules ctor's default, which stock re-runs at every level's
        // CWorld::Spawn (world.cpp:608-616) — the map's own tf_gamerules
        // then re-sets it at its Activate.
        _captureBonusTime = -1f;
    }

    /// <summary>The spy's <c>RemoveDisguise</c>, asked by entity index (PickUp:1235-1243).</summary>
    public void RemoveDisguise(EntityHandle player) => spies.RemoveDisguise(players.Index(player));

    /// <summary>Stock's <c>SetCTFCaptureBonusTime</c> (tf_gamerules.h:450), the proxy input's route in.</summary>
    public void SetCaptureBonusTime(float seconds) => _captureBonusTime = seconds;

    /// <summary>
    /// <c>HandleCTFCaptureBonus</c>: at a bonus time of zero or less nobody
    /// is critted (:20525-20528 — there the call ends before the walk), and
    /// only the capping team's living take the condition (:20530-20536).
    /// </summary>
    public void CaptureBonus(TFTeam team)
    {
        // GetCTFCaptureBonusTime (tf_gamerules.h:451-460): the map's
        // override stands when set, else the convar.
        float time = _captureBonusTime >= 0f ? _captureBonusTime : bonusTime.Value;
        if (time <= 0f)
            return;

        foreach (var player in roster.Players(team))
        {
            if (roster.IsAlive(player))
                conditions.Add(player, TFCond.CritboostedCtfCapture, time);
        }
    }
}
