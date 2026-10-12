//========= SourceSharp — when a cached skeleton is still the right one =======//
//
// The managed twin of src/shared/native/bone_cache_key.h (plan
// physics_bones_native_zero step 1.0). The header holds the argument; this
// is its rule, kept in the same shape:
//
//   * every field is compared for EQUALITY except the bone mask, which is a
//     CONTAINMENT test (a superset cache answers a subset request, never the
//     reverse: bone_setup evaluates only what the mask names);
//   * exact float compares, on purpose: the fields are the same variables
//     copied, not measurements, so a tolerance would be a window in which a
//     moved entity keeps its old skeleton;
//   * the autoplay clock is a DECISION (AutoplayClock): the clock for a model
//     that animates itself, a constant zero for one that does not.
//
// Plain data and pure functions: no state, no clock, no convar.
//=============================================================================//

using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// <c>BoneCacheKey_t</c>: everything a skeleton build is fed, as plain
/// numbers. <see cref="Origin"/> and <see cref="Angles"/> are the native
/// key's <c>flOrigin[3]</c>/<c>flAngles[3]</c> (angles in pitch, yaw, roll
/// order).
/// </summary>
public struct BoneCacheKey
{
    /// <summary>Which model; a model change is a different skeleton.</summary>
    public int ModelIndex;

    /// <summary>Which bones were evaluated. Compared by containment.</summary>
    public int BoneMask;

    /// <summary>Where the skeleton was placed.</summary>
    public Vector Origin;

    /// <summary>How the skeleton was turned (pitch, yaw, roll).</summary>
    public Vector Angles;

    /// <summary>Which sequence the pose was evaluated from.</summary>
    public int Sequence;

    /// <summary>Where in that sequence.</summary>
    public float Cycle;

    /// <summary>The pose-parameter generation the owner advances only when
    /// a stored value changes.</summary>
    public int PoseGeneration;

    /// <summary>The clock, for a model with autoplay layers; zero
    /// otherwise (see <see cref="AutoplayClock"/>).</summary>
    public float AutoplayTime;

    /// <summary>How big the skeleton was built.</summary>
    public float ModelScale;

    /// <summary>
    /// <c>Reset()</c>: never valid, so the first request always misses.
    /// <see cref="ModelScale"/> resets to ZERO, not 1: 1 is what a real
    /// unscaled entity carries, and the never-computed key must not agree
    /// with a live one on that field.
    /// </summary>
    public void Reset()
    {
        ModelIndex = -1;
        BoneMask = 0;
        Sequence = 0;
        Cycle = 0.0f;
        PoseGeneration = 0;
        AutoplayTime = 0.0f;
        ModelScale = 0.0f;
        Origin = Vector.Zero;
        Angles = Vector.Zero;
    }

    /// <summary>
    /// <c>BoneCache_IsValidFor</c>: whether a cache built with
    /// <paramref name="cached"/> can answer a request for
    /// <paramref name="wanted"/>.
    /// </summary>
    public static bool IsValidFor(in BoneCacheKey cached, in BoneCacheKey wanted)
    {
        // Never computed, or computed for a model this entity no longer wears.
        if (cached.ModelIndex < 0 || cached.ModelIndex != wanted.ModelIndex)
        {
            return false;
        }

        // Containment: the cache must hold at least every bone asked for.
        if ((cached.BoneMask & wanted.BoneMask) != wanted.BoneMask)
        {
            return false;
        }

        if (cached.Sequence != wanted.Sequence || cached.Cycle != wanted.Cycle)
        {
            return false;
        }

        if (cached.PoseGeneration != wanted.PoseGeneration)
        {
            return false;
        }

        if (cached.AutoplayTime != wanted.AutoplayTime)
        {
            return false;
        }

        if (cached.ModelScale != wanted.ModelScale)
        {
            return false;
        }

        return cached.Origin.X == wanted.Origin.X && cached.Angles.X == wanted.Angles.X
            && cached.Origin.Y == wanted.Origin.Y && cached.Angles.Y == wanted.Angles.Y
            && cached.Origin.Z == wanted.Origin.Z && cached.Angles.Z == wanted.Angles.Z;
    }

    /// <summary>
    /// <c>BoneCache_AutoplayClock</c>: the clock for a model that animates
    /// itself, a constant zero for one that does not.
    /// </summary>
    public static float AutoplayClock(bool modelHasAutoplay, float currentTime)
    {
        return modelHasAutoplay ? currentTime : 0.0f;
    }
}
