//========= SourceSharp — when a cached bone-merge map is still the right one =//
//
// The managed twin of src/shared/native/bone_merge_key.h (plan
// physics_bones_native_zero step 1.0). A merge map is a function of two
// MODELS -- the follower's own and the source's -- and of nothing else about
// either entity: a new source wearing the same model merges identically. So
// the key is the pair, compared for equality, and a key with no source model
// never answers (a merge without a source model never asks for a map).
//
// The source model is an opaque handle: the engine's model_t is interned, so
// identity IS model equality for the lifetime of a level, and this key does
// not outlive one. nint carries the native pointer as a value.
//=============================================================================//

namespace SourceSharp.Shared.Bones;

/// <summary><c>BoneMergeKey_t</c>: the pair of models a merge map was built from.</summary>
public struct BoneMergeKey
{
    /// <summary>The follower's own model index.</summary>
    public int OwnModelIndex;

    /// <summary>The source's model, as an opaque handle (0 = none).</summary>
    public nint SourceModel;

    /// <summary>
    /// <c>Reset()</c>: never valid, -1 / none, so the first ask always builds.
    /// </summary>
    public void Reset()
    {
        OwnModelIndex = -1;
        SourceModel = 0;
    }

    /// <summary>
    /// <c>BoneMergeKey_IsValidFor</c>: whether a map built from
    /// <paramref name="built"/> still answers for <paramref name="wanted"/>.
    /// </summary>
    public static bool IsValidFor(in BoneMergeKey built, in BoneMergeKey wanted)
    {
        if (wanted.SourceModel == 0)
        {
            return false;
        }

        return built.OwnModelIndex == wanted.OwnModelIndex
            && built.SourceModel == wanted.SourceModel;
    }
}
