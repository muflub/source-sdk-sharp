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
public sealed class TFPlayerConditionEffectArms(IEngineSound sound) : ITFConditionHook, IOrderedHook
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
    /// (<see cref="TFConditionArms"/>); what is left is the disguise pair.
    /// </summary>
    public void OnRemoved(EntityHandle player, TFCond cond) { }
}
