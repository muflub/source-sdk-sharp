//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// How large a plasma flame is at a given moment, while it is growing or
/// shrinking towards a size it was told to reach.
///
/// <para>
/// This is <c>C_Plasma::UpdateScale</c>, and it is here rather than on the
/// client half because it is arithmetic over five floats and because it carries
/// a fix that has to be testable without a running Source.
/// </para>
///
/// <para>
/// THE DIVIDE-BY-DEFAULT, found in the native code this replaces. Stock reads:
/// </para>
/// <code>
/// if ( time &gt; m_flScaleTimeEnd )  register = end;
/// else  frac = 1 - ( m_flScaleTimeEnd - time ) / ( m_flScaleTimeEnd - m_flScaleTimeStart );
/// </code>
/// <para>
/// and the two branches do not cover the case that actually happens. A flame is
/// created with a ramp of ZERO seconds — <c>env_fire</c> asks for its starting
/// size immediately — so the recv proxy sets start and end to the same instant.
/// On that first frame <c>time &gt; end</c> is false, because the two are equal,
/// and the else branch divides <c>0 / 0</c>. What comes back is NaN, and NaN is
/// then the flame's scale: seven sprites sized NaN, handed to
/// <c>CVertexBuilder::Position3f</c> and its
/// <c>Assert( IsFinite(x) &amp;&amp; IsFinite(y) &amp;&amp; IsFinite(z) )</c>.
/// It is the <c>EnvSmokeStackVars.JetLength</c> shape exactly — a duration
/// nobody set, feeding a divide — and it is not caught downstream for the same
/// reason: every comparison against NaN is false, so
/// <c>MAX( 0.000001f, scale )</c> returns the NaN rather than the floor.
/// </para>
///
/// <para>
/// The invariant here is not "do not divide by zero". It is:
/// </para>
/// <para>
/// THE ANSWER IS ALWAYS FINITE, AND ALWAYS BETWEEN THE TWO SIZES GIVEN.
/// </para>
/// <para>
/// A ramp whose duration is not finite and strictly positive has no fraction to
/// compute — it is an instruction to be the destination size NOW, which is what
/// a zero-second ramp means and what stock meant to do — so it answers the
/// destination rather than a quotient. And the fraction is clamped, so a clock
/// that has run past the end, or has not reached the start, cannot push the
/// size outside the pair either.
/// </para>
/// </summary>
public static class PlasmaScaleRamp
{
    /// <summary>
    /// Finite and strictly positive, spelled out so this file needs nothing
    /// but itself.
    /// </summary>
    private static bool IsUsableDuration(float f) => float.IsFinite(f) && f > 0f;

    /// <summary>
    /// The size at <paramref name="now"/> of a ramp that runs
    /// <paramref name="from"/> to <paramref name="to"/> between
    /// <paramref name="timeStart"/> and <paramref name="timeEnd"/>.
    /// </summary>
    /// <returns>
    /// Always finite, and always within the closed interval between
    /// <paramref name="from"/> and <paramref name="to"/> when both of those are
    /// themselves finite.
    /// </returns>
    public static float Evaluate(float now, float timeStart, float timeEnd,
                                 float from, float to)
    {
        // A destination that is not a number is not a destination. Nothing
        // downstream can rescue a NaN size, so it is refused here where there is
        // still a sane answer to give.
        if (!float.IsFinite(to))
            return float.IsFinite(from) ? from : 0f;

        if (!float.IsFinite(from))
            return to;

        float duration = timeEnd - timeStart;

        // THE GUARD. A zero-length ramp, a backwards one, or one whose
        // endpoints are not finite, all mean the same thing: be the destination
        // size now. This is the branch stock fell through into a 0/0.
        if (!IsUsableDuration(duration) || !float.IsFinite(now))
            return to;

        float fraction = (now - timeStart) / duration;

        // Re-checked rather than trusted: a finite numerator over a finite
        // denominator can still overflow, which is the same failure wearing a
        // different hat.
        if (!float.IsFinite(fraction))
            return to;

        if (fraction <= 0f)
            return from;

        if (fraction >= 1f)
            return to;

        return from + (to - from) * fraction;
    }

    /// <summary>
    /// Whether a ramp starting at <paramref name="timeStart"/> and ending at
    /// <paramref name="timeEnd"/> has finished by <paramref name="now"/>.
    ///
    /// A ramp with no usable duration is over the moment it begins, which is
    /// the same reading <see cref="Evaluate"/> takes of it.
    /// </summary>
    public static bool IsFinished(float now, float timeStart, float timeEnd)
    {
        float duration = timeEnd - timeStart;

        if (!IsUsableDuration(duration) || !float.IsFinite(now))
            return true;

        return now >= timeEnd;
    }
}
