//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Shared.Movement;

/// <summary>
/// The scout's air dash, as a pure decision over predicted state: whether a
/// jump pressed in the air is a dash (<c>CTFPlayer::CanAirDash</c>,
/// src/game/shared/tf/tf_player_shared.cpp:12840) and how fast it goes up
/// (<c>CTFGameMovement::AirDash</c>, src/game/shared/tf/tf_gamemovement.cpp:992).
/// The core's loop has already made <c>CheckJumpButton</c>'s refusals (dead,
/// water, the unduck transition, the held button, :1160) and applies the
/// dash's velocity; the count the decision reads is the carrier's
/// <c>m_iAirDash</c>, raised by each dash (:1062) and cleared on ground
/// (<c>SetGroundEntity</c>, :2958).
/// </summary>
public static class TFJump
{
    /// <summary>
    /// <c>flDashZ</c> before the jump-height attributes: stock's literal
    /// (tf_gamemovement.cpp:1023), the speed that reaches the ground jump's
    /// height under <c>sv_gravity</c> 800 ("Apply approx. the jump velocity
    /// added to an air dash", asserted at 800 there).
    /// </summary>
    public const float DashSpeed = 268.3281572999747f;

    /// <summary>The soda popper's hype allows this many dashes (tf_player_shared.cpp:12852-12856).</summary>
    public const int HypeDashes = 5;

    /// <summary><c>set_scout_doublejump_disabled</c>, the no-dash attribute's hook (:12876).</summary>
    public const string DoubleJumpDisabledAttrib = "set_scout_doublejump_disabled";

    /// <summary><c>air_dash_count</c>, the extra-dash attribute on the active weapon (:12862).</summary>
    public const string AirDashCountAttrib = "air_dash_count";

    /// <summary><c>mod_jump_height</c>, the passive jump-height attribute (:999).</summary>
    public const string JumpHeightAttrib = "mod_jump_height";

    /// <summary><c>mod_jump_height_from_weapon</c>, the weapon-restricted one (:1004).</summary>
    public const string JumpHeightFromWeaponAttrib = "mod_jump_height_from_weapon";

    /// <summary>
    /// <c>CTFPlayer::CanAirDash</c>: the halloween kart never, the halloween
    /// speed boost always, any class but the scout never; with the soda
    /// popper's hype up to <see cref="HypeDashes"/>; else fewer than
    /// <paramref name="dashCount"/> — <c>tf_scout_air_dash_count</c> with the
    /// active weapon's <c>air_dash_count</c> walked over it
    /// (<c>CALL_ATTRIB_HOOK_INT_ON_OTHER</c>, :12861-12862) — so far, and not
    /// with <c>set_scout_doublejump_disabled</c> 1 on the player (:12875-12878).
    /// Not ported (deferred.tsv): the Atomizer's third-jump deploy delay.
    /// </summary>
    /// <param name="activeWeapon">The active weapon's item, the ON_OTHER root
    /// of the dash count; null for none, as stock's null <c>pTFActiveWeapon</c>.</param>
    /// <param name="attribs">The side's attribute walk, the player's own manager
    /// as its root; null for none, every hook then unset.</param>
    /// <param name="playerAttributes"><c>m_AttributeManager</c>.</param>
    public static bool CanAirDash(TFClass playerClass, in TFCondMask conditions, int airDash, int dashCount,
                                  IAttributeProvider? activeWeapon = null, AttributeHooks? attribs = null,
                                  IAttributeProvider? playerAttributes = null)
    {
        if (conditions.Has(TFCond.HalloweenKart))
            return false;

        if (conditions.Has(TFCond.HalloweenSpeedBoost))
            return true;

        if (playerClass != TFClass.Scout)
            return false;

        if (conditions.Has(TFCond.SodapopperHype))
            return airDash < HypeDashes;

        if (attribs is not null)
            dashCount = attribs.Int(dashCount, AirDashCountAttrib, activeWeapon);

        if (airDash >= dashCount)
            return false;

        return attribs?.Int(0, DoubleJumpDisabledAttrib, playerAttributes) != 1;
    }

    /// <summary>
    /// The dash a jump pressed in the air takes: <see cref="DashSpeed"/> under
    /// the jump-height attributes (<c>flJumpMod</c>, :997-1005 — the player's
    /// passive <c>mod_jump_height</c>, then the active weapon's
    /// <c>mod_jump_height_from_weapon</c>) when <see cref="CanAirDash"/>
    /// allows one, else 0 (refused). The agility rune is not ported
    /// (deferred.tsv).
    /// </summary>
    public static float AirDash(TFClass playerClass, in TFCondMask conditions, int airDash, int dashCount,
                                IAttributeProvider? activeWeapon = null, AttributeHooks? attribs = null,
                                IAttributeProvider? playerAttributes = null)
    {
        if (!CanAirDash(playerClass, in conditions, airDash, dashCount, activeWeapon, attribs, playerAttributes))
            return 0f;

        float jumpMod = attribs?.Float(1f, JumpHeightAttrib, playerAttributes) ?? 1f;
        jumpMod = attribs?.Float(jumpMod, JumpHeightFromWeaponAttrib, activeWeapon) ?? jumpMod;
        return DashSpeed * jumpMod;
    }
}
