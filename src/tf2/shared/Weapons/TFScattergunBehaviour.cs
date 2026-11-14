//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Player;

namespace TF2.Shared;

/// <summary>
/// The scattergun family's own arms (<c>CTFScatterGun</c>,
/// <c>tf_weapon_shotgun.cpp:317-364</c>): the Force-a-Nature's
/// <c>set_scattergun_has_knockback</c> shove. Fired airborne and not yet
/// shoved this airtime, the gun throws its wielder backwards along their
/// view — the local-space velocity's x becomes -300, the other axes the
/// velocity they were, plus fifty up — strips the ground flag, and stuns the
/// movement for 0.3 s forward-only. The victim's half of the attribute is
/// the hit's, the damage layer's shove (the stock <c>ApplyPostHitEffects</c>).
///
/// <para>
/// Stock's dead locals — the <c>vecForce</c> the block computes and never
/// uses, <c>CanScatterGunKnockBack</c>'s <c>scattergun_knockback_mult</c>
/// arm — are not here: no item row carries the multiplier, and the shove's
/// numbers are the two constants and the view. The pre-round freeze's early
/// return (:327-328) waits for the round-state seam, like every weapon's.
/// </para>
/// </summary>
public class TFScattergunBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary><c>set_scattergun_has_knockback</c>, the row's attribute.</summary>
    public const string KnockbackAttribute = "set_scattergun_has_knockback";

    /// <summary>The shove's backward speed, the local x stock writes (:349).</summary>
    public const float BackwardSpeed = 300f;

    /// <summary>The extra lift, stock's impulse (:356).</summary>
    public const float Rise = 50f;

    /// <summary><c>StunPlayer( 0.3f, 1.f, TF_STUN_MOVEMENT | TF_STUN_MOVEMENT_FORWARD_ONLY )</c> (:335).</summary>
    public const float StunTime = 0.3f;

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (!base.BeforePrimaryAttack(in attack))
            return false;

        KnockbackSelf(in attack);
        return true;
    }

    /// <summary>
    /// The Soda Popper's drink (<c>CTFSodaPopper::SecondaryAttack</c>,
    /// tf_weapon_shotgun.cpp:484-494): the whole secondary, stock calling no
    /// base — a full meter arms <c>TF_COND_SODAPOPPER_HYPE</c>, the buff's
    /// own standing the only debounce. Every other scattergun is the base's.
    /// </summary>
    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script || script.Id != TFWeaponId.SodaPopper)
        {
            base.SecondaryAttack(in attack);
            return;
        }

        int owner = attack.Command.OwnerIndex;
        if (owner <= 0
            || Parts.Owners.ConditionsOf(owner) is not { } conditions
            || conditions.Has(TFCond.SodapopperHype)                       // IsHypeBuffed, :487
            || Parts.PlayerData?.Predicted(owner) is not { } state
            || state.HypeMeter < TFHype.FullMeter)                         // :490
            return;

        conditions.Add(TFCond.SodapopperHype);                             // :492
    }

    /// <summary>The popper's busy frame drinks too (<c>ItemBusyFrame</c>, :469-481) — "so we can always activate buff when we want (similar to stickies)".</summary>
    protected override void BusySkill(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script || script.Id != TFWeaponId.SodaPopper)
        {
            base.BusySkill(in attack);
            return;
        }

        SecondaryAttack(in attack);
    }

    /// <summary>
    /// The wielder's shove (<c>CTFScatterGun::FireBullet</c>'s head,
    /// :319-361): once per airtime, the velocity re-expressed in the view's
    /// frame with its x replaced, the lift, and the real half to
    /// <see cref="ITFPlayerData.ScattergunKnock"/> — the server's velocity
    /// and stun, the client's replay nothing.
    /// </summary>
    private void KnockbackSelf(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script
            || _attributes.Int(0, KnockbackAttribute, Context(in attack, script).Item) == 0)
            return;

        int owner = attack.Command.OwnerIndex;
        if (owner <= 0 || !Parts.Owners.OffGround(owner))
            return;

        // The latch is the carrier's predicted bit: without it a held trigger
        // shoves every shot of the airtime, where stock shoves once (:331).
        if (Parts.PlayerData?.Predicted(owner) is not { ScattergunJump: false } state)
            return;

        // The shove needs the view: the command's own aim, the same pair the
        // predicted shots trace from. Without it there is no direction to
        // shove along, and stock's frame would have none either.
        if (attack.Arms.Carrier is not { } carrier || !attack.Arms.Aim(out _, out Vector forward))
            return;

        state.ScattergunJump = true;
        Vector shoved = Shoved(carrier.Velocity, forward);
        carrier.Velocity = shoved;

        // The real half: the server's SetAbsVelocity, the ground flag with it,
        // and the movement stun. The client's replay answers the default
        // nothing; the state above is its prediction's whole half.
        Parts.PlayerData?.ScattergunKnock(owner, shoved);
    }

    /// <summary>
    /// The shove itself (:343-353, :356): the velocity in the view's frame
    /// keeps its sideways and vertical parts and takes -300 along the look,
    /// then fifty more up.
    /// </summary>
    public static Vector Shoved(Vector velocity, Vector forward)
    {
        var (right, up) = Angles.RightUp(forward);
        Vector shoved = forward * -BackwardSpeed
                      + right * velocity.Dot(right)
                      + up * velocity.Dot(up);
        return shoved + new Vector(0f, 0f, Rise);
    }
}
