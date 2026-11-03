//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The spy's meter reads: the cloak watch's charge off the decoded carrier
/// (<c>m_flCloakMeter</c>) and the disguise state (m_nDisguiseTeam,
/// m_nDisguiseClass and the TF_COND_DISGUISED condition word). Stock's cloak
/// meter is the item-effect meter over the invis watch (ShouldDraw :390-432,
/// GetProgress :513-519), enabled by owning a CTFWeaponInvis; every spy owns
/// one, so the gate here is the live spy — the watch's feign-death and
/// motion-cloak label variants need the weapon's attributes the script
/// surface does not carry (docs/divergences.md). Stock's CDisguiseStatus also
/// names the imitated player and their weapon and health
/// (:239-303); the carrier networks only the class and the team, so the
/// panel shows those (docs/divergences.md).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFSpy)])]
public sealed class TFHudSpy(IEngineHud screen, ITFPlayerData data) : ITFSpy
{
    /// <summary>Stock's ShouldDraw: a live spy (the item-effect meter's alive
    /// arm :395 and the watch-ownership arm through m_bEnabled).</summary>
    public bool CloakPresent
        => Carrier is { } state && state.Class() == TFClass.Spy
           && screen.LocalPlayerHealth > 0;

    public float Cloak
        => Carrier is { } state ? Math.Clamp(state.CloakMeter / 100f, 0f, 1f) : 0f;

    /// <summary>Stock's ShouldDraw: InCond(TF_COND_DISGUISED)
    /// (tf_hud_disguise_status.cpp:138-145).</summary>
    public bool Disguised => Carrier is { } state && state.InCond(TFCond.Disguised);

    public string DisguiseClassName
        => Carrier is { } state && state.DisguiseAs() is var disguise
           && disguise != TFClass.Undefined ? TFClassNames.NonLocalized(disguise) : "";

    public int DisguiseTeam
        => Carrier is { } state ? state.DisguiseTeam : 0;

    /// <summary>The local player's decoded carrier, 0 the world and out of
    /// range nobody (the same slot guard TFHudLocalPlayer keeps).</summary>
    private TFPlayerNetState? Carrier
        => screen.LocalPlayerIndex is > 0 ? data.Networked(screen.LocalPlayerIndex) : null;
}
