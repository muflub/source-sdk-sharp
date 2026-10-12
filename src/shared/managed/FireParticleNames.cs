//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// Which of the eight fire particle systems a flame of a given size is drawn
/// with.
///
/// <para>
/// Stock's, and it lives here rather than on either half because it is the one
/// decision <c>C_FireSmoke::Start</c> made and it is arithmetic over a float:
/// the size in units divided by 36, floored, gives a bucket, and the bucket
/// plus the smoke flag names one of <c>env_fire_{tiny,small,medium,large}</c>
/// with or without <c>_smoke</c>.
/// </para>
///
/// <para>
/// TWO THINGS ARE FIXED HERE rather than copied across, and both were live.
/// </para>
///
/// <para>
/// The first is the DIVIDE-BY-DEFAULT shape this build has been bitten by
/// before — see <c>src/sourcesharp/game/shared/jet_lifetime.h</c>. The size
/// arrives from a map key through a C# property, so a map that omits it sends
/// zero, and NaN and infinity are reachable from a mod that computes one. Stock
/// hands the quotient straight to a <c>switch</c> whose <c>default</c> label
/// sits with <c>case 3</c>, so every value it did not expect — negative, NaN
/// cast to int, anything past 3 — comes out as the LARGEST fire there is. A
/// fire nobody configured becoming the biggest one in the map is the wrong
/// direction to fail in. Here an unusable size is replaced by the size stock's
/// own <c>CFire</c> constructor starts with, so an unconfigured flame looks
/// like an unconfigured flame rather than like an inferno.
/// </para>
///
/// <para>
/// The second is that the number reaching this decision was not a size at all.
/// <c>env_fire</c> passed <c>heat / MaxHeatLevel</c> — a ratio that cannot
/// exceed 1 — where stock passes <c>m_flFireSize</c> in units, so
/// <c>floor(ratio / 36)</c> was zero for every fire in every map and all of
/// them drew as <c>env_fire_tiny</c> whatever their firesize said. The bucket
/// is chosen from the SIZE now, which is what the divisor 36 was ever scaled
/// for.
/// </para>
/// </summary>
public static class FireParticleNames
{
    /// <summary>
    /// What stock's <c>CFire</c> constructor leaves <c>m_flFireSize</c> at, and
    /// so what a flame whose size did not survive the trip is drawn as.
    /// </summary>
    public const float DefaultSize = 64f;

    /// <summary>Units of flame per bucket. Stock's divisor.</summary>
    public const float UnitsPerBucket = 36f;

    /// <summary>The largest bucket there is — <c>env_fire_large</c>.</summary>
    public const int MaxBucket = 3;

    /// <summary>
    /// Finite and strictly positive — the same test
    /// <c>SourceSharp_JetIsUsable</c> makes, for the same reason.
    /// </summary>
    private static bool IsUsable(float f) => float.IsFinite(f) && f > 0f;

    /// <summary>
    /// The bucket a flame of this many units falls in, always 0..3.
    ///
    /// <para>
    /// A size that is not finite and strictly positive is not a size, and comes
    /// back as the bucket <see cref="DefaultSize"/> falls in rather than as an
    /// answer derived from it.
    /// </para>
    /// </summary>
    public static int BucketForSize(float sizeUnits)
    {
        if (!IsUsable(sizeUnits))
            sizeUnits = DefaultSize;

        // Floored, as stock does, so 71 units is still the same fire as 36.
        int bucket = (int)MathF.Floor(sizeUnits / UnitsPerBucket);

        if (bucket < 0)
            bucket = 0;

        if (bucket > MaxBucket)
            bucket = MaxBucket;

        return bucket;
    }

    /// <summary>
    /// The particle system a flame of this size and smokiness is drawn with.
    /// </summary>
    public static string ForSize(float sizeUnits, bool smoke)
        => ForBucket(BucketForSize(sizeUnits), smoke);

    /// <summary>The name for an already-chosen bucket.</summary>
    public static string ForBucket(int bucket, bool smoke) => bucket switch
    {
        0 => smoke ? "env_fire_tiny_smoke"   : "env_fire_tiny",
        1 => smoke ? "env_fire_small_smoke"  : "env_fire_small",
        2 => smoke ? "env_fire_medium_smoke" : "env_fire_medium",
        _ => smoke ? "env_fire_large_smoke"  : "env_fire_large",
    };

    /// <summary>
    /// Every name this class can return, which is what the server precaches.
    ///
    /// Both smoke variants of every bucket: which one a fire needs is decided
    /// by a spawnflag, and a particle system that was not precached does not
    /// draw.
    /// </summary>
    public static IEnumerable<string> All()
    {
        for (int bucket = 0; bucket <= MaxBucket; bucket++)
        {
            yield return ForBucket(bucket, smoke: false);
            yield return ForBucket(bucket, smoke: true);
        }
    }
}
