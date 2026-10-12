namespace SourceSharp.Abi;

/// <summary>
/// One entity's bone cache, as <c>StandaloneBoneMatrices</c> answered it: every
/// cached bone-to-world matrix, which of them the build actually wrote, and the
/// entity's own transform.
///
/// <para>
/// <b>Why a bone COUNT is not this.</b> <c>Studio_BuildMatrices</c> writes what
/// its mask names and leaves the rest of the buffer as it found it, so the
/// number of bones a model declares says nothing about how many were written.
/// And the static-prop branch writes the ENTITY'S OWN transform into every one
/// of them, count and all — so a skeleton nothing posed has the full bone count
/// and not one posed bone. Both facts are arithmetic on the matrices, which is
/// why they needed a slot that carries matrices.
/// </para>
///
/// <para>
/// <b>The comparison is EXACT and that is deliberate.</b>
/// <see cref="IsPosed"/> is a bit-for-bit comparison against
/// <see cref="EntityToWorld"/>, matching the native probe's <c>memcmp</c>. A
/// tolerance would answer "posed" for a bone written AS the entity transform
/// and then rounded, which is the defect being looked for. The transform is
/// taken from native rather than rebuilt here for the same reason: a managed
/// <c>AngleMatrix</c> agreeing to six decimal places would report every bone
/// posed on a skeleton where none is.
/// </para>
/// </summary>
public sealed class StandaloneBones
{
    /// <summary>A matrix3x4_t is three rows of four floats.</summary>
    public const int FloatsPerMatrix = 12;

    internal StandaloneBones(int count, float[] boneToWorld, uint[] writtenBits,
                             float[] entityToWorld)
    {
        Count         = count;
        BoneToWorld   = boneToWorld;
        WrittenBits   = writtenBits;
        EntityToWorld = entityToWorld;
    }

    /// <summary>How many bones the cache holds.</summary>
    public int Count { get; }

    /// <summary>
    /// <see cref="Count"/> matrices, <see cref="FloatsPerMatrix"/> floats each,
    /// in bone order.
    /// </summary>
    public float[] BoneToWorld { get; }

    /// <summary>
    /// One bit per bone, LSB of word 0 being bone 0. Read it through
    /// <see cref="IsWritten"/>.
    /// </summary>
    public uint[] WrittenBits { get; }

    /// <summary>
    /// The entity's own transform — where it is, how it is turned, and how big
    /// it is worn. <see cref="FloatsPerMatrix"/> floats.
    /// </summary>
    public float[] EntityToWorld { get; }

    /// <summary>
    /// Whether the build wrote bone <paramref name="bone"/> under the mask the
    /// entity last asked for. False for an index outside the cache.
    /// </summary>
    public bool IsWritten(int bone)
    {
        if (bone < 0 || bone >= Count) return false;

        int word = bone / 32;

        return word < WrittenBits.Length && (WrittenBits[word] & (1u << (bone % 32))) != 0;
    }

    /// <summary>
    /// Whether bone <paramref name="bone"/> differs from
    /// <see cref="EntityToWorld"/> — which is what "the pose branch ran for this
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
    public bool IsPosed(int bone)
    {
        if (!IsWritten(bone)) return false;

        int at = bone * FloatsPerMatrix;

        if (at + FloatsPerMatrix > BoneToWorld.Length ||
            EntityToWorld.Length < FloatsPerMatrix)
            return false;

        for (int i = 0; i < FloatsPerMatrix; i++)
        {
            // BitwiseEquals, not ==: the native test is a memcmp, and two
            // matrices that differ only in the sign of a zero are the same
            // matrix to == and different bytes to memcmp. Reading the bits is
            // what makes the two sides answer the same number.
            if (BitConverter.SingleToInt32Bits(BoneToWorld[at + i]) !=
                BitConverter.SingleToInt32Bits(EntityToWorld[i]))
                return true;
        }

        return false;
    }

    /// <summary>How many of the cached bones the build wrote.</summary>
    public int WrittenCount
    {
        get
        {
            int n = 0;

            for (int i = 0; i < Count; i++)
                if (IsWritten(i)) n++;

            return n;
        }
    }

    /// <summary>
    /// How many of the WRITTEN bones differ from the entity's own transform.
    /// Equal to <see cref="WrittenCount"/> on a skeleton every bone of which
    /// the pose branch ran for.
    /// </summary>
    public int PosedCount
    {
        get
        {
            int n = 0;

            for (int i = 0; i < Count; i++)
                if (IsPosed(i)) n++;

            return n;
        }
    }
}
