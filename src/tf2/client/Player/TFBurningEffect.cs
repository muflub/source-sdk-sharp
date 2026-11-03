//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;

namespace TF2.Client.Player;

/// <summary>
/// The burning player's particle, the body of stock's
/// <c>CTFPlayerShared::OnAddBurning</c> client half
/// (<c>tf_player_shared.cpp:7296-7307</c>): <c>burningplayer_red</c> on the
/// red team and <c>burningplayer_blue</c> on every other, attached to follow
/// the player's origin — here the native shell's per-frame sweep feeds the
/// owner's origin to the running system, that attachment's effect. The
/// already-running guard is stock's <c>!m_pBurningEffect</c>, keyed by the
/// player's handle, which a re-entering player's new serial changes.
///
/// <para>
/// Stock's pyro-vision skip (<c>IsLocalPlayerUsingVisionFilterFlags</c>,
/// :7300) reads <c>localplayer_visionflags</c> bit 0, the same read
/// <c>ClientTeTFBlood</c> and <c>PyroVision</c> make, and applies to the
/// local player's own burn only. The screen overlay half (:7308-7316) is
/// <c>TFConditionOverlays</c>' and OnRemoveBurning's steam, sound and overlay
/// clear (:6857-6885) wait in <c>docs/plans/tf2/deferred.tsv</c>; the burn
/// sound is the server arm's (<c>TFPlayerConditionEffectArms</c>).
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFBurningEffect(
    IEngineEffects effects,
    IEngineHud hud,
    IEngineConsole console,
    TFClientPlayerResources resources) : ITFClientConditionHook, IOrderedHook
{
    /// <summary>The players whose burn particle is running.</summary>
    private readonly HashSet<EntityHandle> _burning = [];

    public int HookOrder => TFClientConditionOrders.BurningEffect;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (cond != TFCond.Burning || _burning.Contains(player))
            return;

        // The pyro's goggles hide his own flames (:7300): the vision bit on
        // the local player skips the particle, as stock's condition does —
        // and leaves nothing for the removal to stop, as stock's null does.
        if (PyroVision(player) && player.EntryIndex == hud.LocalPlayerIndex)
            return;

        string name = TeamOf(player) == (int)TFTeam.Red ? "burningplayer_red" : "burningplayer_blue";

        if (!effects.ParticleSystemStart(player, name))
            return;

        _burning.Add(player);
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond != TFCond.Burning)
            return;

        if (_burning.Remove(player))
            effects.ParticleSystemStop(player);
    }

    /// <summary>The player's team as the scoreboard carries it; not red means blue, stock's ternary.</summary>
    private int TeamOf(EntityHandle player)
        => resources.Live is { } resource ? resource.State.Team[player.EntryIndex] : 0;

    /// <summary><c>localplayer_visionflags</c> &amp; TF_VISION_FILTER_PYRO.</summary>
    private bool PyroVision(EntityHandle player)
    {
        string? raw = console.FindConVarValue("localplayer_visionflags");

        return int.TryParse(raw, out int flags) && (flags & 0x1) != 0;
    }
}
