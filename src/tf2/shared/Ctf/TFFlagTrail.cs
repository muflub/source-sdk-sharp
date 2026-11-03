//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Ctf;

/// <summary>
/// Stock's <c>FLAG_EFFECTS_*</c> (shared/tf/entity_capture_flag.cpp:72-75),
/// the map's <c>trail_effect</c> key: which of the two carried-flag effects
/// run. The colour trail is the sprite ribbon the SERVER lays; the paper
/// trail is the particle system the CLIENT starts.
/// </summary>
public enum TFFlagEffects
{
    None = 0,
    All = 1,
    PaperOnly = 2,
    ColorOnly = 3,
}

/// <summary>
/// The carried flag's effects' numbers and names (shared/tf/entity_capture_flag.h):
/// <c>TF_FLAG_TRAIL_ALPHA</c> (:24), <c>TF_FLAG_EFFECT</c> (:29) and
/// <c>TF_FLAG_TRAIL</c> (:30), plus the factory values stock sets on the
/// created trail (<c>StartFlagTrail</c>, entity_capture_flag.cpp:2704-2707).
/// </summary>
public static class TFFlagTrail
{
    /// <summary>TF_FLAG_TRAIL_ALPHA: the ribbon's brightness while carried.</summary>
    public const int Alpha = 96;

    /// <summary>TF_FLAG_EFFECT: the paper-trail particle system.</summary>
    public const string PaperEffect = "player_intel_papertrail";

    /// <summary>TF_FLAG_TRAIL: the colour ribbon's material, per team.</summary>
    public const string MaterialBase = "flagtrail";

    /// <summary>StartFlagTrail's <c>SetLifeTime( 0.70 )</c>.</summary>
    public const float LifeTime = 0.70f;

    /// <summary>StartFlagTrail's <c>SetStartWidth( 32 )</c>.</summary>
    public const float StartWidth = 32f;

    /// <summary>StartFlagTrail's <c>SetTextureResolution( 1.0f / (96.0f * 1.0f) )</c>.</summary>
    public const float TextureRes = 1f / 96f;

    /// <summary>
    /// GetTrailEffect (entity_capture_flag.cpp:328-333):
    /// <c>effects/&lt;flag_trail or flagtrail&gt;_&lt;red|blu&gt;.vmt</c>.
    /// </summary>
    public static string Material(TFTeam team, string mapName = "")
        => $"effects/{(mapName.Length > 0 ? mapName : MaterialBase)}_{(team == TFTeam.Red ? "red" : "blu")}.vmt";
}
