//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Econ;

namespace TF2.Shared.Movement;

/// <summary>
/// <c>CTFPlayerShared::CalculateChargeCap</c> (tf_player_shared.cpp:6100-6115):
/// the shield charge's yaw cap in degrees per second — the base 0.45 (:6102)
/// through the wearer's <c>charge_turn_control</c> hook (:6104,
/// <c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER</c> on the player), times the frametime
/// scale while <c>tf_demoman_charge_frametime_scaling</c> (:106) is on
/// (:6107-6112): <c>RemapValClamped(frametime, TICK_INTERVAL *
/// YAW_CAP_SCALE_MIN, TICK_INTERVAL * YAW_CAP_SCALE_MAX, 0.25, 2)</c> with the
/// two constants of :107-108 (0.2 and 2), so a slow client's cap rises and a
/// fast one's falls and every side turns the same distance per tick.
///
/// <para>
/// Every dependency is handed in, as <see cref="TFMaxSpeed"/> does: the
/// client's input caps and the server's command clamp both ask it (the
/// charge-turn rows of this stage), each with its side's frame time and tick
/// interval and the player's own manager for the hook.
/// </para>
/// </summary>
public static class TFChargeCap
{
    /// <summary>The cap's base, degrees (:6102).</summary>
    public const float BaseCap = 0.45f;

    /// <summary><c>charge_turn_control</c>, the cap's hook (:6104).</summary>
    public const string ChargeTurnControlAttrib = "charge_turn_control";

    /// <summary><c>YAW_CAP_SCALE_MIN</c>, the remap's low input (tf_player_shared.cpp:107).</summary>
    public const float YawCapScaleMin = 0.2f;

    /// <summary><c>YAW_CAP_SCALE_MAX</c>, the remap's high input (:108).</summary>
    public const float YawCapScaleMax = 2f;

    /// <summary>The scale's floor, the remap's low output (:6110).</summary>
    public const float ScaleFloor = 0.25f;

    /// <summary>The scale's ceiling, the remap's high output (:6110).</summary>
    public const float ScaleCeiling = 2f;

    /// <summary>
    /// The cap. Stock reads the side's frametime and its own hook receiver;
    /// the caller hands both in — the client's input sample frame time and the
    /// side's tick interval, the player's attribute manager from the weapon
    /// owners' lookup, null when the player has none (stock's absent manager,
    /// the hook then answering the base unchanged).
    /// </summary>
    /// <param name="frametime">The side's frame time (<c>gpGlobals-&gt;frametime</c>).</param>
    /// <param name="tickInterval">Stock's <c>TICK_INTERVAL</c>, the remap's anchor.</param>
    /// <param name="frametimeScaling"><c>tf_demoman_charge_frametime_scaling</c>.</param>
    /// <param name="attribs">The side's attribute walk, the one
    /// <c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER</c> takes.</param>
    /// <param name="playerAttributes"><c>m_AttributeManager</c>, the hook's root.</param>
    public static float Calculate(float frametime, float tickInterval, bool frametimeScaling,
                                  AttributeHooks attribs, IAttributeProvider? playerAttributes)
    {
        float cap = attribs.Float(BaseCap, ChargeTurnControlAttrib, playerAttributes);   // :6102, :6104
        if (frametimeScaling)                                                            // :6107
        {
            float flMod = CritBucket.RemapValClamped(frametime, tickInterval * YawCapScaleMin,
                                                     tickInterval * YawCapScaleMax, ScaleFloor, ScaleCeiling);   // :6110
            cap *= flMod;                                          // :6111
        }
        return cap;                                                // :6114
    }
}
