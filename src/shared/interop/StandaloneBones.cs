namespace SourceSharp.Abi;

/// <summary>
/// What a <see cref="StandaloneBones"/> says beyond its fields.
///
/// <para>
/// Extension methods rather than members since plan_shim_areas_split phase 4:
/// the bones are in a seam's signature (<c>IEngineEntityRender.BoneMatrices</c>),
/// so they live with the seam in an interface assembly, which holds only data.
/// </para>
/// </summary>
public static class StandaloneBonesReading
{
    /// <summary>
    /// Whether the build wrote bone <paramref name="bone"/> under the mask the
    /// entity last asked for. False for an index outside the cache.
    /// </summary>
    public static bool IsWritten(this StandaloneBones bones, int bone)
    {
        if (bone < 0 || bone >= bones.Count) return false;

        int word = bone / 32;

        return word < bones.WrittenBits.Length && (bones.WrittenBits[word] & (1u << (bone % 32))) != 0;
    }

    /// <summary>
    /// Whether bone <paramref name="bone"/> differs from
    /// <see cref="StandaloneBones.EntityToWorld"/> — which is what "the pose branch ran for this
    /// bone" means, and the only thing that separates a posed skeleton from an
    /// unposed one.
    ///
    /// <para>
    /// False for a bone the build did not write: an untouched slot holds
    /// whatever was in the buffer, and "it differs from the entity transform"
    /// is true of uninitialised memory for reasons that have nothing to do with
    /// posing.
    /// </para>
    /// </summary>
    public static bool IsPosed(this StandaloneBones bones, int bone)
    {
        if (!bones.IsWritten(bone)) return false;

        int at = bone * StandaloneBones.FloatsPerMatrix;

        if (at + StandaloneBones.FloatsPerMatrix > bones.BoneToWorld.Length ||
            bones.EntityToWorld.Length < StandaloneBones.FloatsPerMatrix)
            return false;

        for (int i = 0; i < StandaloneBones.FloatsPerMatrix; i++)
        {
            // BitwiseEquals, not ==: the native test is a memcmp, and two
            // matrices that differ only in the sign of a zero are the same
            // matrix to == and different bytes to memcmp. Reading the bits is
            // what makes the two sides answer the same number.
            if (BitConverter.SingleToInt32Bits(bones.BoneToWorld[at + i]) !=
                BitConverter.SingleToInt32Bits(bones.EntityToWorld[i]))
                return true;
        }

        return false;
    }

    /// <summary>How many of the cached bones the build wrote.</summary>
    public static int WrittenCount(this StandaloneBones bones)
    {
        int n = 0;

        for (int i = 0; i < bones.Count; i++)
            if (bones.IsWritten(i)) n++;

        return n;
    }

    /// <summary>
    /// How many of the WRITTEN bones differ from the entity's own transform.
    /// Equal to <see cref="WrittenCount"/> on a skeleton every bone of which
    /// the pose branch ran for.
    /// </summary>
    public static int PosedCount(this StandaloneBones bones)
    {
        int n = 0;

        for (int i = 0; i < bones.Count; i++)
            if (bones.IsPosed(i)) n++;

        return n;
    }
}
