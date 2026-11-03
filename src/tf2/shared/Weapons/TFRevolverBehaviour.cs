//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The spy's revolver: <c>CTFRevolver</c>'s accuracy clock
/// (<c>shared/tf/tf_weapon_revolver.cpp</c>). The gun is the gun behaviour
/// with one member of its own — <c>m_flLastAccuracyCheck</c>, which this
/// behaviour rides on <see cref="WeaponNetState.BehaviourKeptTime"/>, the one
/// behaviour slot a deploy does not clear (stock zeroes the member in the
/// constructor alone, :51, and stock declares it the client's only predicted
/// field, exact: <c>DEFINE_PRED_FIELD(m_flLastAccuracyCheck, FIELD_FLOAT, 0)</c>,
/// <c>tf_weapon_revolver.cpp:29-31</c>) — the shot stamps it
/// (<c>PrimaryAttack</c>'s <c>m_flLastAccuracyCheck = curtime</c>, :157, a
/// plain statement after the base's shot, so it is the machine's fired-shot
/// tail) and the headshot reads it: <c>CanHeadshot</c> is the item's
/// <c>set_weapon_mode</c> 1 (<c>tf_weapon_revolver.h:49</c>, the Ambassador;
/// the stock spy revolver carries no mode and never blooms).
///
/// <para>
/// <see cref="CrosshairScale"/> is <c>GetWeaponCrosshairScale</c>
/// (tf_weapon_revolver.cpp:207-223) under that mode: the clock's age
/// <c>RemapValClamped</c> from the pair 1.0, 0.5 to the pair 0.75, 2.5 —
/// wide (2.5) just after a shot, back to 0.75 once a second has passed, so the HUD
/// crosshair blooms and settles with the aim (<c>CHudTFCrosshair::Paint</c>
/// takes it as <c>flWeaponScale</c>, <c>tf_hud_crosshair.cpp:189-216</c>).
/// Without the mode the base answers 1.0 (:221). The clock's two other
/// consumers, the headshot spread (:187-201) and the freshness gate of the
/// hit-location damage type (:83-92), wait on the attribute-weapon work
/// (deferred.tsv).
/// </para>
/// </summary>
public class TFRevolverBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary>The clock ages this far before the crosshair sits at its floor.</summary>
    private const float CheckAge = 1f;

    /// <summary>And this close that it sits at its full bloom (the remap's second pair).</summary>
    private const float BloomAge = 0.5f;

    public override void PrimaryAttack(in WeaponAttackContext attack)
    {
        base.PrimaryAttack(in attack);

        // :157, after the base's shot — and the machine calls this tail only
        // for a shot that fired, which is stock's early-return shape (:126-139).
        // Stock's m_flLastAccuracyCheck, on the behaviour's kept clock.
        attack.Weapon.BehaviourKeptTime = attack.Now;
    }

    /// <summary>
    /// <c>GetWeaponCrosshairScale</c> (:207-223): the headshot's remap of the
    /// age of stock's <c>m_flLastAccuracyCheck</c>, the clock the shot stamped
    /// into <see cref="WeaponNetState.BehaviourKeptTime"/>, else the base's
    /// 1.0. The HUD asks it at the player's final predicted time (our
    /// <c>IGameClock.Now</c>, the lane's stand-in for
    /// <c>GetFinalPredictedTime</c> plus the interpolation), so
    /// <paramref name="now"/> comes from the caller's clock.
    /// </summary>
    public static float CrosshairScale(WeaponNetState weapon, float now, bool canHeadshot) =>
        canHeadshot
            ? CritBucket.RemapValClamped(now - weapon.BehaviourKeptTime, CheckAge, BloomAge, 0.75f, 2.5f)
            : 1f;
}
