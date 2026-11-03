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
/// The sniper's rifle: <c>CTFSniperRifle</c>'s zoom, charge and shot
/// (<c>shared/tf/tf_weapon_sniperrifle.cpp</c>).
///
/// <para>
/// THE SCOPE (<c>HandleZooms</c> :267-327): the secondary toggles it when its
/// timer allows (<c>Zoom</c> :479-490: the primary at least 0.1 s away, the
/// secondary <c>TF_WEAPON_SNIPERRIFLE_ZOOM_TIME</c> 0.3 s). Zooming in needs
/// ammunition and puts the owner in <c>TF_COND_ZOOMED</c> and
/// <c>TF_COND_AIMING</c> (<c>ZoomIn</c> :523-545); zooming out takes both away
/// and drops the charge (<c>ZoomOut</c> :574-603). A shot fired zoomed unzooms
/// 0.5 s later and rezooms 0.9 s after that while ammunition lasts
/// (<c>Fire</c> :784-857, <c>ZoomOutIn</c> :499-518, with <c>cl_autorezoom</c>'s
/// default on). The field of view is the server's effect
/// (<see cref="ITFWeaponEffects.Zoom"/>).
/// </para>
/// <para>
/// THE CHARGE (<c>ItemPostFrame</c> :329-400): zoomed and not waiting to
/// rezoom, once the secondary's timer has passed, the shot charges at
/// <c>TF_WEAPON_SNIPERRIFLE_CHARGE_PER_SEC</c> 50 a second up to
/// <c>TF_WEAPON_SNIPERRIFLE_DAMAGE_MAX</c> 150; the shot's damage is
/// <c>Max(charge, TF_WEAPON_SNIPERRIFLE_DAMAGE_MIN 50)</c> through
/// <c>mult_dmg</c> (<c>GetProjectileDamage</c> :870-905), so 0%, 50% and 100%
/// of a charge are 50, 75 and 150. The charge is kept as the time it began
/// (<see cref="WeaponNetState.BehaviourMeter"/>), not summed per frame, and
/// with the aiming stopped but the zoom still up it decays at
/// <c>TF_WEAPON_SNIPERRIFLE_UNCHARGE_PER_SEC</c> 75 a second (:392-400).
/// Leaving the ground unzooms and drops the charge and the rezoom
/// (<c>m_Shared.IsJumping</c> :360-371, ruled as not <c>FL_ONGROUND</c>).
/// </para>
/// <para>
/// THE CRIT (<c>CanFireCriticalShot</c> :1004-1062): no random crits; only a
/// crit boost (or the headshot the damage rules decide, Stage 2.7) crits.
/// </para>
///
/// <para>
/// Its predicted state: <see cref="WeaponNetState.BehaviourTimeA"/> the
/// unzoom time (under <see cref="ModeDecayClock"/>, the charge decay's last
/// update instead), <see cref="WeaponNetState.BehaviourTimeB"/> the rezoom
/// time (0 = none, stock's -1), <see cref="WeaponNetState.BehaviourMeter"/>
/// the charge's start (0 = none), <see cref="ModeRezoomAfterShot"/> and
/// <see cref="ModeDecayClock"/>.
/// </para>
/// </summary>
public class TFSniperRifleBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    public const float ChargePerSec = 50f;
    public const float UnchargePerSec = 75f;
    public const float DamageMin = 50f;
    public const float DamageMax = 150f;
    public const float ZoomTime = 0.3f;
    public const float UnzoomDelay = 0.5f;
    public const float RezoomDelay = 0.9f;

    /// <summary>TF_WEAPON_SNIPERRIFLE_NO_CRIT_AFTER_ZOOM_TIME (tf_weapon_sniperrifle.cpp:41): the zoom a headshot needs to be behind it.</summary>
    public const float NoCritAfterZoomTime = 0.2f;

    /// <summary><c>m_bRezoomAfterShot</c>.</summary>
    public const int ModeRezoomAfterShot = 1 << ModeKindShift;

    /// <summary>
    /// <see cref="WeaponNetState.BehaviourTimeA"/> is holding the charge
    /// decay's last update, not an unzoom time (see <see cref="UpdateCharge"/>).
    /// </summary>
    public const int ModeDecayClock = 1 << 9;

    /// <summary>
    /// The charged damage at <paramref name="now"/> (<c>m_flChargedDamage</c>)
    /// grown at <paramref name="ratePerSec"/> a second from the begin
    /// (<c>ItemPostFrame</c>'s accrual, capped as stock caps each frame,
    /// tf_weapon_sniperrifle.cpp:384).
    /// </summary>
    public static float Charge(WeaponNetState weapon, float now, float ratePerSec = ChargePerSec) =>
        weapon.BehaviourMeter > 0f ? Math.Clamp((now - weapon.BehaviourMeter) * ratePerSec, 0f, DamageMax) : 0f;

    /// <summary><c>GetProjectileDamage</c> before <c>mult_dmg</c>: the charge, never under the minimum.</summary>
    public static float DamageAt(float charge) => MathF.Max(charge, DamageMin);

    /// <summary>
    /// The accrual's rate: the stock 50 through the hooks
    /// (<c>ApplyChargeSpeedModifications</c>, tf_weapon_sniperrifle.cpp:377-378,
    /// 643-645), clamped as stock clamps it (:381-382). Without a script the
    /// rifle is stock's own.
    /// </summary>
    private float ChargeRate(in WeaponAttackContext attack) =>
        ScriptOf(attack.Type) is not { } script
            ? ChargePerSec
            : Math.Clamp(Parts.Hooks.ModifyChargeRate(Context(in attack, script), ChargePerSec), 0f, 2f * ChargePerSec);

    protected override float ShotBaseDamage(in WeaponAttackContext attack, TFWeaponScript script) =>
        DamageAt(Charge(attack.Weapon, attack.Now, ChargeRate(in attack)));

    /// <summary>
    /// <c>GetProjectileDamage</c>'s tail (tf_weapon_sniperrifle.cpp:896-899):
    /// a fully charged shot's damage through the chain's
    /// <c>sniper_full_charge_damage_bonus</c> arm, after the damage the chain
    /// has already scaled, as stock asks it last.
    /// </summary>
    public override ShotDamage ModifyShot(in WeaponAttackContext attack, ShotDamage shot)
    {
        var result = base.ModifyShot(in attack, shot);

        if (ScriptOf(attack.Type) is not { } script
            || Charge(attack.Weapon, attack.Now, ChargeRate(in attack)) < DamageMax)
            return result;

        return result with { Damage = Parts.Hooks.ModifyFullChargeDamage(Context(in attack, script), result.Damage) };
    }

    protected override TFCritOwner KindCritPolicy(in WeaponAttackContext attack, TFCritOwner policy) =>
        policy with { CanFireCriticalShot = policy.CanFireCriticalShot && policy.CritBoosted };

    protected override bool HoldsAiming(in WeaponAttackContext attack) => IsZoomed(in attack);

    private bool IsZoomed(in WeaponAttackContext attack) =>
        attack.Command.OwnerIndex > 0 && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } c && c.Has(TFCond.Zoomed);

    private bool IsAiming(in WeaponAttackContext attack) =>
        attack.Command.OwnerIndex > 0 && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } c && c.Has(TFCond.Aiming);

    public override void PrimaryAttack(in WeaponAttackContext attack)
    {
        base.PrimaryAttack(in attack);

        var weapon = attack.Weapon;
        ClearDecayClock(weapon);
        if (IsZoomed(in attack))
        {
            // SetRezoom: out in half a second, and back in when there is more to fire.
            weapon.BehaviourTimeA = attack.Now + UnzoomDelay;
            weapon.BehaviourMode = weapon.Reserve > 0
                ? weapon.BehaviourMode | ModeRezoomAfterShot
                : weapon.BehaviourMode & ~ModeRezoomAfterShot;
        }
        else
        {
            // The rack's SequenceDuration: none without a viewmodel animation.
            weapon.NextSecondaryAttack = attack.Now;
        }

        weapon.BehaviourMeter = 0f;
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script)
        {
            HandleZooms(in attack, script);
            UpdateCharge(in attack);
        }

        base.ItemPostFrame(in attack);
    }

    /// <summary>The zoom is the rifle's own secondary (<c>CTFSniperRifle</c>'s attack2, tf_weapon_sniperrifle.cpp:479-490), not the class skill; <see cref="ItemPostFrame"/>'s zoom arm runs it.</summary>
    public override void SecondaryAttack(in WeaponAttackContext attack) { }

    private void HandleZooms(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;
        float now = attack.Now;
        bool offGround = attack.Command.OwnerIndex > 0 && Parts.Owners.OffGround(attack.Command.OwnerIndex);

        // Left the ground: out, and the charge and the rezoom with it
        // (ItemPostFrame's m_Shared.IsJumping arm, tf_weapon_sniperrifle.cpp:360-371).
        if (offGround)
        {
            if ((weapon.BehaviourMode & ModeDecayClock) != 0)
                weapon.BehaviourTimeA = 0f;
            if (IsZoomed(in attack))
                ZoomOut(in attack, script);
            weapon.BehaviourMeter = 0f;
            weapon.BehaviourMode &= ~(ModeRezoomAfterShot | ModeDecayClock);
            weapon.BehaviourTimeB = 0f;
        }

        bool decaying = (weapon.BehaviourMode & ModeDecayClock) != 0;

        if (!decaying && weapon.BehaviourTimeA > 0f && now > weapon.BehaviourTimeA)
        {
            bool rezoom = (weapon.BehaviourMode & ModeRezoomAfterShot) != 0;
            ZoomOut(in attack, script);
            if (rezoom)
                weapon.BehaviourTimeB = now + RezoomDelay;
        }

        if (weapon.BehaviourTimeB > 0f && now > weapon.BehaviourTimeB)
        {
            weapon.BehaviourTimeB = 0f;
            ZoomIn(in attack, script);
        }

        if ((weapon.LastButtons & (int)InputButtons.Attack2) != 0 && weapon.NextSecondaryAttack <= now)
        {
            if (!decaying && (weapon.BehaviourTimeB > 0f || weapon.BehaviourTimeA > 0f))
            {
                // Rezooming already: cancel it, and no sooner than it would have.
                weapon.NextSecondaryAttack = weapon.BehaviourTimeB + ZoomTime;
                weapon.BehaviourTimeB = 0f;
            }
            else if (IsZoomed(in attack))
            {
                ZoomOut(in attack, script);
                weapon.NextPrimaryAttack = MathF.Max(weapon.NextPrimaryAttack, now + 0.1f);
                weapon.NextSecondaryAttack = now + ZoomTime;
            }
            else if (!offGround)
            {
                // Zoom refuses a zoom-in while jumping (tf_weapon_sniperrifle.cpp:479-490).
                ZoomIn(in attack, script);
                weapon.NextPrimaryAttack = MathF.Max(weapon.NextPrimaryAttack, now + 0.1f);
                weapon.NextSecondaryAttack = now + ZoomTime;
            }
        }
    }

    private void UpdateCharge(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;
        if (weapon.NextSecondaryAttack > attack.Now)
            return;

        if (IsAiming(in attack) && (weapon.BehaviourMode & ModeRezoomAfterShot) == 0)
        {
            ClearDecayClock(weapon);
            if (weapon.BehaviourMeter <= 0f)
                weapon.BehaviourMeter = attack.Now;
        }
        else if (weapon.BehaviourMeter > 0f)
        {
            // Aiming stopped without a zoom-out: the charge runs back down at
            // TF_WEAPON_SNIPERRIFLE_UNCHARGE_PER_SEC 75 (ItemPostFrame :392-400).
            // The charge is kept as the time it began, so the decay moves the
            // begin later — 75/s off the charge is 75/r a second off it (1.5 at
            // the stock 50) — over the frames since the last one, read from the
            // charge as the last frame froze it (a read at now would grow it at
            // the charge rate first). That clock rides BehaviourTimeA under
            // ModeDecayClock, which is only free while no unzoom or rezoom is
            // pending: a pending one only follows a shot, and a shot zeroes
            // the charge.
            if ((weapon.BehaviourMode & ModeDecayClock) != 0)
            {
                float rate = ChargeRate(in attack);
                float charge = Charge(weapon, weapon.BehaviourTimeA, rate)
                             - UnchargePerSec * (attack.Now - weapon.BehaviourTimeA);
                if (charge > 0f)
                {
                    weapon.BehaviourMeter = attack.Now - charge / rate;
                    weapon.BehaviourTimeA = attack.Now;
                }
                else
                {
                    weapon.BehaviourMeter = 0f;
                    weapon.BehaviourMode &= ~ModeDecayClock;
                    weapon.BehaviourTimeA = 0f;
                }
            }
            else if (weapon.BehaviourTimeA == 0f && weapon.BehaviourTimeB == 0f)
            {
                weapon.BehaviourMode |= ModeDecayClock;
                weapon.BehaviourTimeA = attack.Now;
            }
        }
    }

    /// <summary>The decay's clock is not an unzoom time: put <see cref="WeaponNetState.BehaviourTimeA"/> back to none.</summary>
    private static void ClearDecayClock(WeaponNetState weapon)
    {
        if ((weapon.BehaviourMode & ModeDecayClock) != 0)
        {
            weapon.BehaviourMode &= ~ModeDecayClock;
            weapon.BehaviourTimeA = 0f;
        }
    }

    private void ZoomIn(in WeaponAttackContext attack, TFWeaponScript script)
    {
        if (attack.Weapon.Reserve <= 0 || attack.Command.OwnerIndex <= 0
            || Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is not { } conditions)
            return;

        ClearDecayClock(attack.Weapon);
        conditions.Add(TFCond.Zoomed);
        conditions.Add(TFCond.Aiming);
        if (attack.EffectsLive)
            Parts.Effects.Zoom(in attack, script, zoomed: true);
    }

    private void ZoomOut(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;
        if (attack.Command.OwnerIndex > 0 && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } conditions)
        {
            conditions.Remove(TFCond.Zoomed);
            conditions.Remove(TFCond.Aiming);
        }

        weapon.BehaviourTimeA = 0f;
        weapon.BehaviourTimeB = 0f;
        weapon.BehaviourMode &= ~(ModeRezoomAfterShot | ModeDecayClock);
        weapon.BehaviourMeter = 0f;
        if (attack.EffectsLive)
            Parts.Effects.Zoom(in attack, script, zoomed: false);
    }
}
