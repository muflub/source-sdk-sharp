//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Server.Player;

/// <summary>
/// The player half of stock's <c>OnConditionAdded</c>/<c>OnConditionRemoved</c>
/// (tf_player_shared.cpp:1571-1886, 1892-2230) that the condition arms cannot
/// reach — the arms that touch the player through the engine, run for every
/// add, refresh, removal and expiry by <see cref="TFPlayerConditions"/>.
///
/// <para>
/// One so far: <c>OnAddBurning</c>'s shared fire-starting sound
/// (<c>EmitSound( "Fire.Engulf" )</c>, :7320), which on the server is the
/// engine's entity emission the clients hear. The arm's client half (the
/// burning particles and the screen overlay, :7296-7317) waits for the
/// client's condition-effect driver (<c>docs/plans/tf2/deferred.tsv</c>).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerConditionEffectArms(IEngineSound sound, TFPlayerCarriers carriers)
    : ITFConditionHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.ConditionEffectArms;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        // OnAddBurning (:7320): outside both #ifdefs, so it runs on the server
        // too — and it is the server's sound, networked out to everyone.
        if (cond == TFCond.Burning)
            sound.EntityEmitSound(player, "Fire.Engulf");
    }

    /// <summary>
    /// The removal arms land with the conditions they clear:
    /// <c>OnRemoveBurning</c>'s server half (<c>m_hBurnAttacker</c> and the
    /// afterburn forgotten) is <see cref="TF2.Server.Combat.TFBurn.Extinguished"/>,
    /// and its <c>TF_COND_HEALING_DEBUFF</c> clear is a condition-only arm
    /// (<see cref="TFConditionArms"/>); what is left is the disguise pair and
    /// the phase's slow-down below.
    /// The charge's removal is
    /// <c>OnRemoveShieldCharge</c> (tf_player_shared.cpp:6195-6204), riding
    /// every removal and expiry as stock's dispatch does
    /// (:2015-2017): the meter zeroes. Its phase effects and
    /// <c>TeamFortress_SetSpeed</c> wait in the deferred rows — the speed is
    /// this build's per-command <c>TFMaxSpeed</c> ask already, the phases
    /// have no carrier to clear — as do <c>m_bPostShieldCharge</c> and the
    /// end time, whose reader is the unported critboost think.
    /// </summary>
    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.ShieldCharge && carriers.Get(player.EntryIndex) is { } state)
            state.ChargeMeter = 0f;                                           // :6203

        // OnRemovePhase (:3551-3577): once the phase is gone, the damage it
        // dodded pays in a five-second slow, scaled by what was dodded
        // (:3566-3572). The tally is still readable here — the removal
        // clears it after the hooks (TFPlayerConditions.Remove). Stock's
        // TF_STUN_SOUND bit (:3571, tf_shareddefs.h:1342) has no member in
        // this build's enum; its consumer, the stun sound, is out (the
        // stack's own note).
        if (cond != TFCond.Phase || carriers.ConditionsOf(player.EntryIndex) is not { } conditions)
            return;

        int prevented = conditions.PreventedDamage(TFCond.Phase);
        if (prevented == 0)
            return;

        carriers.Part<TFStunStack>(player.EntryIndex)?.Stun(player, SlowDuration,
                                                            SlowAmount(prevented), TFStunFlags.Movement, player);
    }

    /// <summary>The slow's fixed five seconds (:3569 — the remap that follows it in stock is commented out there).</summary>
    public const float SlowDuration = 5f;

    /// <summary>Stock's <c>RemapValClamped( nPreventedDamage, 10.f, 200.f, 0.25f, 0.5f )</c> (:3570).</summary>
    public static float SlowAmount(int prevented) =>
        0.25f + Math.Clamp((prevented - 10f) / 190f, 0f, 1f) * 0.25f;
}
