//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Vgui;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The medic charge meter's reads (tf_hud_mediccharge.cpp): the local
/// medic's medigun fields — the level off the carried weapon's own state
/// (<c>m_flChargeLevel</c>, networked on DT_WeaponMedigun; the replayed
/// prediction of it while it is fresh), the release (<c>m_bCharging</c>) and
/// the beam's patient (<c>m_hHealTarget</c>, named through g_PR's
/// GetPlayerName) off the decoded carrier. ShouldDraw (:147-170) is a live
/// medic whose active weapon is the medigun, the bonesaw or a syringe gun
/// with an ÜberCharge spend; the script surface carries no per-shot ammo
/// count, so the syringe arm is a medic holding a projectile weapon (the
/// syringe gun is the medic's only one), recorded in docs/divergences.md.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFMedicCharge)])]
public sealed class TFHudMedicCharge(IEngineHud screen, ITFPlayerData data,
                                     ILocalArsenal arsenal, TFWeaponScripts scripts,
                                     IPlayerNames names, IClientStandalones standalones,
                                     IWeaponDrawStates states) : ITFMedicCharge
{
    public float Charge
        => Carried() is { } weapon
            ? Math.Clamp((states.PredictedStateOf(weapon) ?? weapon.State).BehaviourMeter, 0f, 1f)
            : 0f;

    public bool Releasing => Carrier is { } state && state.ChargeReleasing;

    public string HealTarget
    {
        get
        {
            if (Carrier is not { } state || state.HealTargetIndex <= 0
                || !names.TryGet(state.HealTargetIndex, out ClientPlayerInfo info))
                return "";
            return info.Name;
        }
    }

    /// <summary>The local player's carried weapon — the one entity the core
    /// holds per armed player, whose kind moves on a switch.</summary>
    private ClientSsWeapon? Carried()
        => standalones.Live().OfType<ClientSsWeapon>()
            .FirstOrDefault(w => w.State.OwnerIndex == screen.LocalPlayerIndex);

    /// <summary>Stock's ShouldDraw: a live medic holding a medigun-shaped
    /// weapon (:147-170). The carrier is the meter's source either way —
    /// OnTick re-aims at the medigun when the bonesaw or syringe is out
    /// (:190-197), and the fields ride the player here.</summary>
    public bool Present
    {
        get
        {
            if (Carrier is not { } state || state.Class() != TFClass.Medic)
                return false;                    // not a medic: stock's class arm (:151)
            if (screen.LocalPlayerHealth <= 0)
                return false;                    // dead: stock's IsAlive arm
            if (!arsenal.TryGetActive(out string name, out _, out _)
                || scripts.Get(name) is not { } script)
                return false;                    // empty hands: stock's no-weapon arm (:156-161)
            return TFWeaponKinds.Of(script) is TFWeaponKind.Medigun      // the medigun (:163)
                or TFWeaponKind.Melee                                    // the bonesaw
                or TFWeaponKind.Projectile;                              // the syringe gun (divergence, see the header)
        }
    }

    /// <summary>The local player's decoded carrier, 0 the world and out of
    /// range nobody (the same slot guard TFHudLocalPlayer keeps).</summary>
    private TFPlayerNetState? Carrier
        => screen.LocalPlayerIndex is > 0 ? data.Networked(screen.LocalPlayerIndex) : null;
}
