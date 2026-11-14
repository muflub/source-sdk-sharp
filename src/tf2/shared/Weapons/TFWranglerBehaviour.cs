//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The Wrangler's word to the builder's sentries (<c>CTFLaserPointer</c>'s
/// server arms, <c>tf_weapon_laser_pointer.cpp</c>): the behaviour runs on
/// both sides, but only a server knows the buildings, so the triggers ask
/// <see cref="ITFSentryWrangle"/> and the server's use layer
/// (<c>TF2.Server.Buildings.TFSentryWrangle</c>) answers. On a client replay
/// the seam is empty and every call is a no-op: the sentry is the server's.
/// </summary>
public interface ITFSentryWrangle
{
    /// <summary>
    /// <c>UpdateLaserDot</c> (<c>tf_weapon_laser_pointer.cpp:289-329</c>): the
    /// aim point one command's view traces to, recorded on the builder's
    /// sentries — the dot the turret tracks.
    /// </summary>
    void Aim(int ownerIndex);

    /// <summary>
    /// <c>PrimaryAttack</c>'s <c>FireNextFrame</c> (:192-214): the trigger held,
    /// the shells the sentry's own beat.
    /// </summary>
    void Fire(int ownerIndex);

    /// <summary>
    /// <c>SecondaryAttack</c>'s <c>FireRocketNextFrame</c> (:219-237): the
    /// rockets on demand from a level 3's pod.
    /// </summary>
    void FireRockets(int ownerIndex);

    /// <summary>
    /// <c>DestroyLaserDot</c>'s <c>ClearTarget</c> (:265-284): the pointer
    /// left the hands, the control and its shield fade with it.
    /// </summary>
    void Release(int ownerIndex);
}

/// <summary>
/// The Wrangler (<c>CTFLaserPointer</c>, tf_weapon_laser_pointer.cpp): no
/// shot of the pointer's own — the base-gun fire is overridden away (:192's
/// body is sentry commands alone), so the primary is vetoed and the triggers
/// drive the builder's sentry through <see cref="ITFSentryWrangle"/>. The
/// held trigger is asked every command (the stock weapon's
/// <c>m_flNextPrimaryAttack</c> is never set) and answers
/// <c>FireNextFrame</c> (:205) with the spin-up anim it rides (:212); the
/// alt-fire answers <c>FireRocketNextFrame</c> (:234); every command aims the
/// dot (<c>ItemPostFrame</c> → <c>UpdateLaserDot</c>, :154). The laser dot
/// entity and its sprite are the client lane's.
/// </summary>
public sealed class TFWranglerBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts), IWeaponBehaviour
{
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        // The veto is the whole attack (the stock override fires no bullet):
        // the shells are the sentry's, on the sentry's beat.
        if (attack.EffectsLive && Parts.SentryWrangle is { } sentry)
        {
            sentry.Fire(attack.Command.OwnerIndex);
            SendAnim(in attack, WeaponAnim.Reload);   // ACT_ITEM1_VM_RELOAD, the held trigger's spin (:212)
        }

        return false;
    }

    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        if (attack.EffectsLive && Parts.SentryWrangle is { } sentry)
            sentry.FireRockets(attack.Command.OwnerIndex);
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        // UpdateLaserDot, one ask per command whether or not anything is held
        // (tf_weapon_laser_pointer.cpp:154): the dot follows the view.
        if (attack.EffectsLive && Parts.SentryWrangle is { } sentry)
            sentry.Aim(attack.Command.OwnerIndex);

        base.ItemPostFrame(attack);
    }

    public void Holster(in WeaponAttackContext attack)
    {
        // As the builder's: outside a command, the owner is the weapon
        // state's — the deploy dressed it — stock's GetOwner().
        Parts.SentryWrangle?.Release(attack.Weapon.OwnerIndex);
    }
}
