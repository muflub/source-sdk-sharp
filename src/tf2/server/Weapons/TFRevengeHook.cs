//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Server.Weapons;

/// <summary>
/// The Frontier Justice's shot (<c>CTFShotgun_Revenge</c>,
/// <c>tf_weapon_shotgun.cpp:132-209</c>): after the attack — so the shot the
/// window paid for is the one that named its custom damage — a spent crit
/// leaves the pile (<c>PrimaryAttack</c>'s tail, :143-145), and before one
/// the window arms for a count the grant lit while another weapon was in the
/// hands, stock's deploy re-add (:184-192) asked where the shot is.
///
/// <para>
/// Stock puts the cond out at the holster and back at the deploy
/// (:168-195); the shared behaviour's <c>Holster</c>/<c>Deploy</c> are
/// those arms (<see cref="TF2.Shared.TFWeaponBehaviour"/>), so a
/// switch mid-window leaves the boost with the Justice as stock's does.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFRevengeHook(TFRevengeCrits revenge) : ITFWeaponHook
{
    /// <summary>After the boost's (+210) and the king reload's (+215) arms: the shot's own bookends.</summary>
    public int HookOrder => 1_000_000 - 1_000 + 217;

    public bool BeforeFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (weapon.Script.Id == TFWeaponId.SentryRevenge)
            revenge.Arm(weapon.OwnerIndex);

        return true;
    }

    public void AfterFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (weapon.Script.Id == TFWeaponId.SentryRevenge)
            revenge.Consume(weapon.OwnerIndex);
    }
}
