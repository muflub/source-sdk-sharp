//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Server.Weapons;

/// <summary>
/// The knife's server half: every answer through the spy slice
/// (<see cref="TF2.Shared.ITFSpyState"/>) only a server holds — the swing's
/// trace and backstab, the melt's gate, the disguise and cloak tails.
/// </summary>
[SideService(Side.Server)]
public sealed class TFKnifeServerHook(ITFSpyState spies) : TFKnifeHook, TF2.Shared.ITFWeaponHook
{
    /// <summary>
    /// The knife's crit is <c>IsBackstab()</c> (tf_weapon_knife.cpp:487-500):
    /// answered whole, so no melee roll runs and the bucket is untouched.
    /// </summary>
    public TFCritResult? CritAnswer(in TFWeaponHookContext weapon) =>
        IsKnife(in weapon) ? new TFCritResult(spies.Smack(weapon.OwnerIndex)) : null;

    public bool BeforeFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (!IsKnife(in weapon))
            return true;

        // The Spy-cicle's melted knife (tf_weapon_knife.cpp:521-529): CanDeploy recomputes the
        // knife's existence from the melt's stamp and window (:523) and refuses while it is gone
        // (:525-528), so a melted knife's press runs no attack at all — the melee chain skips the whole
        // swing on a refused BeforeFire, as stock's Undeployed weapon stops before PrimaryAttack.
        if (attack.Command.OwnerIndex > 0 && !spies.KnifeReady(attack.Command.OwnerIndex, attack.Now))
            return false;

        if (attack.Command.OwnerIndex > 0 && attack.EffectsLive)
            spies.Swing(attack.Command.OwnerIndex, attack.Type.MeleeRange);
        return true;
    }

    /// <summary>
    /// The melted knife refuses the switch itself (<c>CTFKnife::CanDeploy</c>,
    /// <c>tf_weapon_knife.cpp:521-531</c>): the knife's existence recomputed
    /// from the melt's stamp and window (:523) through
    /// <see cref="TF2.Shared.ITFSpyState.KnifeReady"/> — a melted one answering
    /// no until it regenerates (:525-528) — so the slot key, the wheel and the
    /// next-best pick all stand at the holstered knife. The replay's half of
    /// the same line is the client hook's, over the carrier's published pair.
    /// Not a knife, or no owner in the ask: the base yes.
    /// </summary>
    public bool CanDeploy(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (!IsKnife(in weapon) || attack.Command.OwnerIndex <= 0)
            return true;

        return spies.KnifeReady(attack.Command.OwnerIndex, attack.Now);
    }

    /// <summary>
    /// The smack's half of the backstab: <see cref="TF2.Shared.TFMeleeBehaviour"/>'s
    /// <c>ItemPostFrame</c> asks the chain for the hit's damage at the smack
    /// time (and the roll asks at the swing), and a backstab answers zero so
    /// the swing's own arm hits for nothing.
    /// </summary>
    public float ModifyDamage(in TFWeaponHookContext weapon, float damage) =>
        IsKnife(in weapon) && weapon.OwnerIndex > 0 && spies.Smack(weapon.OwnerIndex)
            ? 0f
            : damage;

    public void AfterFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (attack.Command.OwnerIndex <= 0)
            return;

        spies.RemoveInvisibility(attack.Command.OwnerIndex);

        // The knife's tail (tf_weapon_knife.cpp:243-264): a successful backstab by a knife holding
        // set_disguise_on_backstab disguises as the victim instead of the plain RemoveDisguise every
        // other attack takes (stock's ShouldRemoveDisguiseOnPrimaryAttack: tf_weaponbase_gun.cpp:136,
        // 210, tf_weapon_knife.cpp:224, 263). Stock's HasTheFlag half of the gate (:246) rides inside
        // DisguiseOnKill, beside the slice's own ctf read.
        if (IsKnife(in weapon) && spies.Smack(attack.Command.OwnerIndex)
            && spies.ShouldDisguiseOnBackstab(attack.Command.OwnerIndex)
            && spies.DisguiseOnKill(attack.Command.OwnerIndex))
            return;

        spies.RemoveDisguise(attack.Command.OwnerIndex);
    }
}
