//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Client.Player;

/// <summary>
/// The jarate's and the milk's drips, the bodies of stock's
/// <c>CTFPlayerShared::OnAddUrine</c> and <c>OnAddMadMilk</c> client halves
/// (<c>tf_player_shared.cpp:3582-3612, 3779-3841</c>):
/// <c>peejar_drips</c> on the urine and <c>peejar_drips_milk</c> on the milk,
/// each attached to follow the player's origin — the native shell's per-frame
/// sweep feeds the owner's origin to the running system, that attachment's
/// effect, as the burn's and the crit glow's arms wear theirs. The
/// already-running guard is stock's <c>!m_pUrineEffect</c> /
/// <c>!m_pMilkEffect</c>, keyed by the player's handle; the removal's
/// <c>StopEmission</c> is the slot's stop (:3623-3627, 3849-3853).
///
/// <para>
/// The two run beside each other and beside the burn and the glow, so each
/// wears its own slot: the urine the third, the milk the fourth
/// (<c>ParticleSystemStartSlot</c>), one system per player per slot as stock
/// hangs one member per effect on <c>C_TFPlayer</c>.
/// </para>
///
/// <para>
/// <b>DIVERGENCE.</b> Stock's colourblind assistant overhead icon
/// (<c>AddOverheadEffect("peejar_icon")</c>, :3585-3588, and its removal
/// :3621) has no overhead-effect seam; the screen overlay halves
/// (:3600-3607, 3629-3638) wait on the overlay seam in
/// <c>docs/plans/tf2/deferred.tsv</c> — the milk's is commented out in stock
/// anyway (:3832-3839, 3857-3863).
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFConditionDrips(IEngineEffects effects) : ITFClientConditionHook, IOrderedHook
{
    /// <summary>The drips' slot: the third, after the burn's and the glow's.</summary>
    public const int UrineSlot = 2, MilkSlot = 3;

    /// <summary>The players whose urine drips are running.</summary>
    private readonly HashSet<EntityHandle> _urine = [];

    /// <summary>The players whose milk drips are running.</summary>
    private readonly HashSet<EntityHandle> _milk = [];

    public int HookOrder => TFClientConditionOrders.Drips;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        switch (cond)
        {
            case TFCond.Urine when !_urine.Contains(player):
                if (effects.ParticleSystemStartSlot(player, "peejar_drips", UrineSlot))
                    _urine.Add(player);

                break;

            case TFCond.MadMilk when !_milk.Contains(player):
                if (effects.ParticleSystemStartSlot(player, "peejar_drips_milk", MilkSlot))
                    _milk.Add(player);

                break;
        }
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        switch (cond)
        {
            case TFCond.Urine when _urine.Remove(player):
                effects.ParticleSystemStopSlot(player, UrineSlot);

                break;

            case TFCond.MadMilk when _milk.Remove(player):
                effects.ParticleSystemStopSlot(player, MilkSlot);

                break;
        }
    }
}
