//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Render;

using TF2.Client.Entities;
using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Client.Player;

/// <summary>
/// The teammate glows under <c>TF_COND_TEAM_GLOWS</c>
/// (<c>tf_shareddefs.h:804</c>), the teammate halves of stock's client glow:
/// while a player wears the condition, the mirror registers its glow in the
/// team colour as stock's <c>C_BaseCombatCharacter::UpdateGlowEffect</c> does
/// for a glow-flagged character (<c>c_basecombatcharacter.cpp:136-152</c>:
/// destroy the old slot, register colour and alpha 1.0 with occluded on,
/// <c>c_tf_player.cpp:11349</c>), the colour the rules'
/// <c>GetTeamGlowColor</c> (<c>tf_gamerules.cpp:17565-17584</c>) through
/// <see cref="ClientTFFlag.GetTeamGlowColor"/>. The add registers, the
/// removal unregisters — the condition itself is what carries the glow,
/// stock's server granting it to the teammates
/// (<c>ShouldForceTransmitsForTeam</c>, <c>tf_player.cpp:22887</c>).
///
/// <para>
/// Stock's coach glow, powerup glow, the health-band colours of a CTF
/// carrier, and the spy's disguise-team override in
/// <c>C_TFPlayer::GetGlowEffectColor</c> (:11417-11476) wait on their seams
/// (<c>docs/plans/tf2/deferred.tsv</c>).
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFPlayerGlows(
    GlowObjectManager glow,
    TFClientPlayerResources resources) : ITFClientConditionHook, IOrderedHook
{
    /// <summary>The glow slot each glowing teammate holds, by entry index.</summary>
    private readonly Dictionary<int, int> _slots = [];

    public int HookOrder => TFClientConditionOrders.TeamGlows;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (cond != TFCond.TeamGlows)
            return;

        // Stock's UpdateGlowEffect destroys its old slot before registering again
        // (c_basecombatcharacter.cpp:136-142), so a re-add re-reads the colour too.
        if (_slots.Remove(player.EntryIndex, out int old))
            glow.UnregisterGlowObject(old);

        int slot = glow.RegisterGlowObject(player, ClientTFFlag.GetTeamGlowColor(TeamOf(player)), 1f, true, false);
        _slots[player.EntryIndex] = slot;
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond != TFCond.TeamGlows)
            return;

        if (_slots.Remove(player.EntryIndex, out int slot))
            glow.UnregisterGlowObject(slot);
    }

    /// <summary>The scoreboard's team row; 0 is stock's switch default, grey.</summary>
    private int TeamOf(EntityHandle player)
        => resources.Live is { } resource ? resource.State.Team[player.EntryIndex] : 0;
}
