//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Combat;

namespace TF2.Server.Combat;

/// <summary>
/// <c>CTFGameRules::FlPlayerFallDamage</c> (<c>shared/tf/tf_gamerules.cpp:13368-13414</c>):
/// a landing faster than <c>TF_PLAYER_MAX_SAFE_FALL_SPEED</c> (650) does the
/// old TFC damage, <c>5 × (speed / 300)</c>, scaled by the player's max
/// health over 100 and, unless <c>tf_fall_damage_disablespread</c>, by a
/// random 0.8-1.2. The karts, the grappling hook, the agility rune and the
/// <c>cancel_falling_damage</c> attribute arms wait for what they read
/// (<c>docs/plans/tf2/deferred.tsv</c>).
///
/// <para>
/// <b>THE SPEED.</b> The movement loop arms the landing on HL2's own
/// threshold and hands the core's damage op <see cref="MoveMath.FallDamage"/>
/// of the landing speed (<c>CGameMovement::CheckFalling</c>, then
/// <c>PlayerFallingDamage</c>), which is where stock asks the rules
/// (<c>g_pGameRules-&gt;FlPlayerFallDamage</c>). That formula is linear above
/// its threshold, so the speed is recovered from the damage exactly
/// (<see cref="FallSpeedOf"/>) and TF's rule replaces HL2's.
/// </para>
/// </summary>
public static class TFFallDamage
{
    /// <summary>The landing's damage at <paramref name="fallSpeed"/>, for a player of <paramref name="maxHealth"/>.</summary>
    /// <param name="random"><c>RandomFloat(0.8, 1.2)</c>, asked only when the spread is enabled.</param>
    public static float Damage(float fallSpeed, int maxHealth, bool disableSpread, Func<float, float, float> random)
    {
        if (!(fallSpeed > TFCombatDefs.PlayerMaxSafeFallSpeed))
            return 0f;

        // Old TFC damage formula, scaled to the player's max health.
        float damage = 5 * (fallSpeed / 300);
        float ratio = (float)maxHealth / 100.0f;
        damage *= ratio;

        if (!disableSpread)
            damage *= random(0.8f, 1.2f);

        return damage;
    }

    /// <summary>
    /// The landing speed behind <paramref name="coreDamage"/>, the inverse of
    /// <see cref="MoveMath.FallDamage"/> above its threshold; 0 for no damage.
    /// </summary>
    public static float FallSpeedOf(float coreDamage)
    {
        if (!(coreDamage > 0f))
            return 0f;

        return coreDamage * (MoveMath.PlayerFatalFallSpeed - MoveMath.PlayerMaxSafeFallSpeed) / 100.0f
             + MoveMath.PlayerMaxSafeFallSpeed;
    }
}
