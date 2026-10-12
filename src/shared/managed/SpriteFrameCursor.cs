//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// Where an animating sprite's playhead is, given when it started and how fast
/// it runs.
///
/// <para>
/// This is <c>C_Plasma::UpdateAnimation</c>, restated so that it does not need a
/// frame delta. Stock advances a cursor by <c>framerate * frametime</c> every
/// frame and wraps it; the same answer falls out of the elapsed time directly,
/// and a cursor that is computed rather than accumulated cannot drift, cannot
/// double-step when two draws land in one tick, and cannot be left holding a
/// poisoned value from a frame where the clock misbehaved.
/// </para>
///
/// <para>
/// THE THIRD DIVIDE-BY-DEFAULT IN THIS FAMILY, and the one most easily missed
/// because the operator is a modulo rather than a slash. The frame count comes
/// from the loaded model, so a sprite whose model failed to load, or whose index
/// never arrived, has a count of ZERO — and <c>x % 0</c> is the same fault as
/// <c>x / 0</c>. Stock's own wrap is
/// <c>if ( frame &gt;= numFrames ) frame -= (int)frame;</c>, which with a count
/// of zero is an unconditional truncation every frame rather than an animation,
/// and with a NaN cursor never fires at all.
/// </para>
///
/// <para>
/// GUARANTEE: the answer is always finite, always at least zero, and always
/// strictly less than <paramref name="frameCount"/> when that is at least one.
/// </para>
/// </summary>
public static class SpriteFrameCursor
{
    /// <summary>
    /// The frame to draw, for a sprite that was at <paramref name="startFrame"/>
    /// when it began and has been running <paramref name="elapsed"/> seconds at
    /// <paramref name="frameRate"/> frames a second.
    /// </summary>
    public static float Advance(float startFrame, float frameRate, float elapsed, int frameCount)
    {
        // A sprite with no frames has no frame to be on. Not an error worth
        // reporting from a draw path — a missing model has already been
        // reported by whatever failed to load it — but it must not reach the
        // modulo.
        if (frameCount <= 1)
            return 0f;

        if (!float.IsFinite(startFrame) || startFrame < 0f)
            startFrame = 0f;

        if (!float.IsFinite(frameRate))
            frameRate = 0f;

        if (!float.IsFinite(elapsed) || elapsed < 0f)
            elapsed = 0f;

        float cursor = startFrame + frameRate * elapsed;

        // A rate and an elapsed time that are each finite can still multiply to
        // an infinity, and the modulo of an infinity is NaN.
        if (!float.IsFinite(cursor))
            return 0f;

        cursor %= frameCount;

        // A negative rate walks the cursor backwards, which is a legitimate
        // thing to ask for; the remainder then carries the sign and has to be
        // brought back into range.
        if (cursor < 0f)
            cursor += frameCount;

        // Belt and braces against a remainder that lands exactly on the count
        // through rounding, which would index one past the last frame.
        if (!(cursor < frameCount))
            return 0f;

        return cursor;
    }
}
