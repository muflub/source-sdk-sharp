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
public sealed record StandaloneBones(int Count, float[] BoneToWorld, uint[] WrittenBits,
                                      float[] EntityToWorld)
{
    /// <summary>A matrix3x4_t is three rows of four floats.</summary>
    public const int FloatsPerMatrix = 12;

    /// <summary>How many bones the cache holds.</summary>
    public int Count { get; } = Count;

    /// <summary>
    /// <see cref="Count"/> matrices, <see cref="FloatsPerMatrix"/> floats each,
    /// in bone order.
    /// </summary>
    public float[] BoneToWorld { get; } = BoneToWorld;

    /// <summary>
    /// One bit per bone, LSB of word 0 being bone 0. Read it through
    /// <see cref="StandaloneBonesReading.IsWritten"/>.
    /// </summary>
    public uint[] WrittenBits { get; } = WrittenBits;

    /// <summary>
    /// The entity's own transform — where it is, how it is turned, and how big
    /// it is worn. <see cref="FloatsPerMatrix"/> floats.
    /// </summary>
    public float[] EntityToWorld { get; } = EntityToWorld;
}
