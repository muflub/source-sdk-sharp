//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The scope's reads (tf_hud_scope.cpp): the zoom is the local player's
/// <c>TF_COND_ZOOMED</c> condition word (<c>CHudScope::ShouldDraw</c>'s
/// arm, :373-390), and the charge meter's source is the carried rifle
/// (<c>CHudScopeCharge::Paint</c>'s <c>assert_cast</c> arm, :162-226) —
/// the charge derived on the client from the carried state's charge-begin
/// clock (<see cref="TFSniperRifleBehaviour.Charge"/>) against the client's
/// clock, where stock's material proxy reads the networked
/// <c>m_flChargedDamage</c>. ShouldDraw's in-eye spectator arm (:376-379)
/// is stock's OBS_MODE_IN_EYE: no spectator target read reaches this
/// adapter, so the scope draws for the local player only
/// (docs/divergences.md). Both elements carry HIDEHUD_PLAYERDEAD
/// (:110, :265), the alive arm here.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFScope)])]
public sealed class TFHudScope(IEngineHud screen, ITFPlayerData data, ILocalArsenal arsenal,
                               TFWeaponScripts scripts, IClientStandalones standalones,
                               IWeaponDrawStates states, IGameClock clock) : ITFScope
{
    /// <summary>Stock's ShouldDraw over the local carrier: zoomed and alive
    /// (the HIDEHUD_PLAYERDEAD bit, tf_hud_scope.cpp:110).</summary>
    public bool Zoomed => screen.LocalPlayerHealth > 0
                          && Carrier is { } state && state.InCond(TFCond.Zoomed);

    /// <summary>Stock's weapon arm: the hands hold a sniper rifle
    /// (tf_hud_scope.cpp:178).</summary>
    public bool MeterPresent => CarriedRifle() is not null;

    /// <summary>The carried rifle's charge over its max
    /// (<c>m_flChargedDamage</c> / TF_WEAPON_SNIPERRIFLE_DAMAGE_MAX 150),
    /// 0 when the charge has not begun (<see cref="TFSniperRifleBehaviour.Charge"/>'s
    /// zero-clock arm).</summary>
    public float Charge => CarriedRifle() is { } weapon
                           && (states.PredictedStateOf(weapon) ?? weapon.State) is { } state
        ? TFSniperRifleBehaviour.Charge(state, clock.Now) / TFSniperRifleBehaviour.DamageMax
        : 0f;

    /// <summary>The local player's decoded carrier, 0 the world and out of
    /// range nobody (the same slot guard TFHudLocalPlayer keeps).</summary>
    private TFPlayerNetState? Carrier
        => screen.LocalPlayerIndex is > 0 ? data.Networked(screen.LocalPlayerIndex) : null;

    /// <summary>The hands' weapon when it is a sniper rifle: the carried
    /// entity is the active one, whose kind moves on a switch, and the
    /// script's kind is what stock's assert_cast tests.</summary>
    private ClientSsWeapon? CarriedRifle()
        => Carried() is { } weapon
           && arsenal.TryGetActive(out string name, out _, out _)
           && scripts.Get(name) is { } script
           && TFWeaponKinds.Of(script) == TFWeaponKind.SniperRifle
            ? weapon : null;

    /// <summary>The local player's carried weapon — the one entity the core
    /// holds per armed player.</summary>
    private ClientSsWeapon? Carried()
        => standalones.Live().OfType<ClientSsWeapon>()
            .FirstOrDefault(w => w.State.OwnerIndex == screen.LocalPlayerIndex);
}
