//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using TF2.Shared.Conditions;

namespace TF2.Shared.Player;

/// <summary>
/// The invisibility level, <c>CTFPlayerShared::GetPercentInvisible</c>
/// (<c>shared/tf/tf_player_shared.cpp:8047-8051</c>) as a pure function of the
/// carrier and the clock. Stock approaches the level in
/// <c>InvisibilityThink</c> (:7977-8041), which the server runs in
/// <c>SharedThink</c> (:3093-3097) and the client in its think (:1494-1498):
/// the inputs are only <c>m_flInvisChangeCompleteTime</c> (networked, :969)
/// and the conditions, so the level is recomputed from them wherever it is
/// asked instead of being carried. The motion cloak's movement-scaled arm
/// (:8017-8029) is the Dead Ringer lane's (docs/plans/tf2/deferred.tsv).
/// </summary>
public static class TFInvisibility
{
    /// <summary><c>TF_SPY_STEALTH_BLINKSCALE</c> (tf_player_shared.cpp:215).</summary>
    public const float BlinkScale = 0.85f;

    /// <summary>How invisible the player is, 0..1, at <paramref name="now"/>.</summary>
    public static float Percent(TFPlayerNetState state, float now)
    {
        // The safety net's arm (:7979-7988): a stealthed non-spy is not
        // invisible (the think also strips the condition; the slice's tick
        // carries that half).
        if ((TFClass)state.PlayerClass != TFClass.Spy && state.Conditions().Has(TFCond.Stealthed))
            return 0f;

        float scale = 1f;
        if (state.Conditions().Has(TFCond.StealthedBlink) || state.Conditions().Has(TFCond.Urine))
        {
            // We were bumped into or hit for some damage (:7992-7996).
            scale = BlinkScale;
        }

        float target;
        if (state.InvisChangeCompleteTime > now)
        {
            // Fading: in over the invis time, out over the unstealth time's
            // half-rate (:7999-8008).
            target = state.Conditions().Has(TFCond.Stealthed)
                ? 1f - (state.InvisChangeCompleteTime - now)
                : (state.InvisChangeCompleteTime - now) * 0.5f;
        }
        else
        {
            target = state.Conditions().Has(TFCond.Stealthed) ? 1f : 0f;
        }

        return Math.Clamp(target * scale, 0f, 1f);
    }
}
