//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The player's field of view, as stock computes it: <c>CBasePlayer::GetFOV</c>
/// (game/server/player.cpp:8653, and the same math again on the client,
/// game/client/c_baseplayer.cpp:2437) resolves <c>m_iFOV</c> 0 to the default
/// and then, when <c>m_Local.m_flFOVRate</c> is running, ramps from
/// <c>m_iFOVStart</c> over the rate's seconds from <c>m_flFOVTime</c> with
/// <c>SimpleSplineRemapValClamped</c> (public/mathlib/mathlib.h:1166). The
/// server's <c>SetFOV</c> asks it for the start of its own new ramp —
/// <c>m_iFOVStart = GetFOV()</c> — and the client's view asks it every frame.
/// </summary>
public static class PlayerFov
{
    /// <summary>
    /// The FOV the player sees right now. <paramref name="fov"/> of 0 is the
    /// player's own (<paramref name="defaultFov"/>); a zero
    /// <paramref name="rate"/> is an immediate change; past the rate's time
    /// the target is the answer. Stock's getter then writes
    /// <c>m_iFOVStart = fFOV</c> once the ramp is done; the state's words are
    /// the caller's to settle, which <c>SetFov</c> does each time it runs.
    /// </summary>
    public static int Current(int fov, int fovStart, float fovTime, float rate, int defaultFov, float now)
    {
        int current = fov == 0 ? defaultFov : fov;

        if (rate == 0f)
            return current;

        float deltaTime = (now - fovTime) / rate;

        if (deltaTime >= 1f)
            return current;

        return (int)SimpleSplineRemapValClamped(deltaTime, 0f, 1f, fovStart, current);
    }

    /// <summary>mathlib.h's SimpleSplineRemapValClamped.</summary>
    public static float SimpleSplineRemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;

        float clamped = (val - a) / (b - a);
        clamped = clamped < 0f ? 0f : clamped > 1f ? 1f : clamped;
        return c + (d - c) * SimpleSpline(clamped);
    }

    /// <summary>mathlib.h's SimpleSpline — the ease-in, ease-out curve.</summary>
    private static float SimpleSpline(float value)
    {
        float valueSquared = value * value;
        return 3 * valueSquared - 2 * valueSquared * value;
    }
}
