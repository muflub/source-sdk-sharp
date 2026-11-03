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

namespace TF2.Client.Weapons;

/// <summary>
/// The fully charged rifle's bell (<c>CTFSniperRifle</c>'s client charge arms,
/// shared/tf/tf_weapon_sniperrifle.cpp:386-396 and :1931-1941): the frame the
/// local player's charge reaches <c>TF_WEAPON_SNIPERRIFLE_DAMAGE_MAX</c>, one
/// <c>TFPlayer.ReCharged</c> at its ears, <c>m_bPlayedBell</c> until the charge
/// resets — the shot, a zoom-out, the ground, a holster, each of which zeroes
/// the rifle's <see cref="WeaponNetState.BehaviourMeter"/> here.
///
/// <para>
/// Stock's flag rides the weapon entity and resets with it; here it is the
/// per-weapon set <c>_rung</c>, emptied on <see cref="Forget"/> (the
/// kind-change and removal arms) and whenever no charge is held. One divergence:
/// a charge that decays to none and is built again rings again, where stock's
/// flag survives until a reset does.
/// </para>
/// </summary>
// [SideService(Side.Client)] plain: IWeaponDrawEffect is a collection seam
// (ClientModule's ResolveAll picks the side services up by their concrete
// registration), and TFMedigunBeam holds the contract's one claim.
[SideService(Side.Client)]
public sealed class TFSniperRifleBell(TFSniperFullChargeBellConVar bell, IEngineSound engineSound,
                                      IClientStandalones standalones, IEngineHud screen)
    : IWeaponDrawEffect
{
    /// <summary>The recharge bell (both charge arms).</summary>
    public const string BellSound = "TFPlayer.ReCharged";

    private readonly HashSet<EntityHandle> _rung = [];

    public string WeaponName => "tf_weapon_sniperrifle";

    public bool TwoPass => false;

    public void Think(in WeaponDrawContext context)
    {
        WeaponNetState state = context.State;
        if (state.BehaviourMeter <= 0f)
        {
            // No charge held: the shot, the zoom-out or the ground has reset it.
            _rung.Remove(context.Weapon.Handle);
            return;
        }

        if (_rung.Contains(context.Weapon.Handle)
            || context.Owner != screen.LocalPlayerIndex
            || TFSniperRifleBehaviour.Charge(state, context.Now) < TFSniperRifleBehaviour.DamageMax)
            return;

        // Stock flags the bell before it asks the convar, so a muted rifle
        // stays muted for the charge either way.
        _rung.Add(context.Weapon.Handle);
        if (bell.Value && standalones.GetByIndex(context.Owner) is { } player)
            engineSound.EntityEmitSound(player.Handle, BellSound);
    }

    public void ViewModelDrawn(in WeaponDrawContext context) { }

    public int DrawModel(in WeaponDrawContext context, int flags) => -1;

    public void Forget(EntityHandle weapon) => _rung.Remove(weapon);
}
