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
/// The Classic (<c>CTFSniperRifleClassic</c>,
/// <c>shared/tf/tf_weapon_sniperrifle.cpp:1764-2076</c>): the sniper rifle
/// whose shot is the trigger's release, not its press.
///
/// <para>
/// THE PRESS (:1907-1920): the first held frame the shot is due begins the
/// charge — <c>m_bCharging</c>, the meter — and adds <c>TF_COND_AIMING</c>
/// with the speed re-ask; the charge runs while the trigger is down, at the
/// rifle's rate through the hooks, whether the scope is up or not (the
/// override replaces the base's whole frame, whose accrual gates on the
/// zoom's aiming). Every held frame vetoes the machine's shot: the Classic
/// never fires on the press.
/// </para>
///
/// <para>
/// THE RELEASE (:1943-1955): the trigger up with a charge on fires —
/// <c>CTFSniperRifle::Fire</c>'s own gates first, the ammunition (:787-791)
/// and the refire clock (:803-804) — and the shot is the charge through
/// <see cref="TFSniperRifleBehaviour.DamageAt"/>'s floor, the same tail the
/// machine's shot takes (the refire, the hooks, the flash and brass); a zoomed
/// shot stays up. Airborne, the release clicks
/// <c>Player.DenyWeaponSelection</c> (:1949-1952) and fires nothing. Either
/// way the frame ends in <c>WeaponReset</c> (:1998-2011): the charge and the
/// aiming cond gone, the next press a new charge.
/// </para>
///
/// <para>
/// The zoom is the scope's own toggle alone (:1835-1856); the headshot arms, the tracer and the charge's caps are
/// <see cref="TFSniperRifleBehaviour"/>'s unchanged.
/// </para>
/// </summary>
public class TFSniperRifleClassicBehaviour(TFWeaponParts parts) : TFSniperRifleBehaviour(parts)
{
    /// <summary>The airborne release's click (:1951).</summary>
    public const string DenySound = "Player.DenyWeaponSelection";

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        // The press (:1909-1919). The Classic's fire is the release's, and
        // the press only charges, past the frame's CanAttack door (:1881).
        if (ScriptOf(attack.Type) is not { } script)
            return false;
        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context))
            return false;

        var weapon = attack.Weapon;
        if (weapon.BehaviourMeter <= 0f)
        {
            weapon.BehaviourMeter = attack.Now;
            if (attack.Command.OwnerIndex > 0
                && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } conditions)
                conditions.Add(TFCond.Aiming);
        }

        return false;
    }

    /// <summary>
    /// <c>CTFSniperRifleClassic::ItemPostFrame</c> (:1870-1965), in place of
    /// the base rifle's frame: no jump clear, no accrual gate on the scope, no
    /// shot unzoom and no rezoom, so a charge carries through the air and a
    /// zoomed shot stays up.
    /// </summary>
    protected override void RifleFrame(in WeaponAttackContext attack, TFWeaponScript script)
    {
        // :1881-1889: refused the attack, out of the scope and reset.
        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context))
        {
            if (IsZoomed(in attack))
                ZoomOut(in attack, script);
            ResetCharge(in attack);
            return;
        }

        // :1842-1850 and :1901-1905: a taunt drops the scope and the charge.
        var conditions = attack.Command.OwnerIndex > 0
            ? Parts.Owners.ConditionsOf(attack.Command.OwnerIndex)
            : null;
        if (conditions is not null && conditions.Has(TFCond.Taunting))
        {
            if (IsZoomed(in attack))
                ZoomOut(in attack, script);
            ResetCharge(in attack);
            return;
        }

        HandleZoomTrigger(in attack, script);

        var weapon = attack.Weapon;
        bool held = (weapon.LastButtons & (int)InputButtons.Attack) != 0;
        if (weapon.BehaviourMeter > 0f && !held)
        {
            if (attack.Command.OwnerIndex > 0 && Parts.Owners.OffGround(attack.Command.OwnerIndex))
            {
                // The airborne release (:1949-1952): the click, no shot.
                if (attack.EffectsLive)
                    attack.Arms.Sound(DenySound);
            }
            else
            {
                ReleaseFire(in attack, script);
            }

            ResetCharge(in attack);
        }
    }

    /// <summary>WeaponReset's tail (:2000-2011): the charge and the press's aiming gone.</summary>
    private void ResetCharge(in WeaponAttackContext attack)
    {
        attack.Weapon.BehaviourMeter = 0f;
        if (attack.Command.OwnerIndex > 0
            && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } conditions)
            conditions.Remove(TFCond.Aiming);
    }

    /// <summary>
    /// The released shot, the gates of stock's <c>CTFSniperRifle::Fire</c>
    /// (:784-846) and then the bullet: the ammunition (:787-791), the refire
    /// clock (:803-804), and the shot the machine's own would have fired —
    /// the roll, the spend, the animation, the sound before the round leaves
    /// and the arms' fire — with <see cref="TFSniperRifleBehaviour.
    /// PrimaryAttack"/>'s tail for the refire and the hooks.
    /// </summary>
    private void ReleaseFire(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;
        var type = attack.Type;

        if (weapon.Reserve <= 0)
        {
            // The dry release: the empty click, nothing spent (:787-791).
            if (attack.EffectsLive)
                attack.Arms.Sound(type.EmptySound);
            return;
        }

        if (weapon.NextPrimaryAttack > attack.Now)
            return; // The refire clock (:803-804): silent, the reset stands.

        ShotDamage shot = ModifyShot(in attack, default);
        weapon.Reserve -= 1;
        SendAnim(in attack, WeaponAnim.Primary);
        if (attack.EffectsLive)
        {
            attack.Arms.Sound(FireSound(in attack, shot) ?? type.FireSound);
            attack.Arms.FireBullets(in type, Math.Max(1, ModifyPellets(in attack, Math.Max(1, type.Pellets))),
                                    BulletSpread(in attack) ?? type.Spread, shot, FixedPellets(in attack));
        }

        // The fire's tail: the refire, the hooks, the muzzle flash and brass,
        // the charge's clear. The unzoom it arms has no reader on the Classic
        // (only the base's HandleZooms reads m_flUnzoomTime), so it's dropped
        // here before the zoom trigger takes it for a rezoom to cancel.
        PrimaryAttack(in attack);
        weapon.BehaviourTimeA = 0f;
        weapon.BehaviourTimeB = 0f;
        weapon.BehaviourMode &= ~ModeRezoomAfterShot;
    }

    /// <summary>The press's aiming, not the zoom's: the charge holds it (:1911).</summary>
    protected override bool HoldsAiming(in WeaponAttackContext attack) => attack.Weapon.BehaviourMeter > 0f;
}
