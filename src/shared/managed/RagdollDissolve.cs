//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
namespace SourceSharp.Shared;

/// <summary>
/// The times a body dissolves by: EntityDissolve.cpp's DISSOLVE_FADE_* for
/// ENTITY_DISSOLVE_NORMAL, and C_EntityDissolve's fades, each from 0 to 1 by the
/// seconds since it began.
/// </summary>
public static class RagdollDissolve
{
    private const float FadeInEnd = 1f, ModelFadeStart = 1.9f;

    /// <summary>DISSOLVE_FADE_OUT_END_TIME: when the body is gone.</summary>
    public const float Seconds = 2f;

    /// <summary>GetFadeInPercentage: how far the body has gone to black.</summary>
    public static float FadeIn(float elapsed) => elapsed >= FadeInEnd || elapsed >= Seconds ? 1f : MathF.Max(0f, elapsed / FadeInEnd);

    /// <summary>GetModelFadeOutPercentage: what is left of the body's alpha.</summary>
    public static float ModelFadeOut(float elapsed)
        => elapsed <= ModelFadeStart ? 1f : MathF.Max(0f, 1f - (elapsed - ModelFadeStart) / (Seconds - ModelFadeStart));
}
