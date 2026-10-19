using System;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The arithmetic the bone probe's two remaining facts are: how many of an
/// entity's cached bones the build WROTE, and how many of those are POSED —
/// that is, differ from the entity's own transform.
///
/// <para>
/// Neither is a bone count, and that is the whole reason the
/// <c>StandaloneBoneMatrices</c> slot exists.
/// <c>Studio_BuildMatrices</c> writes what its mask names and leaves the rest
/// of the buffer as it found it; the static-prop branch writes the entity's own
/// transform into every bone the model declares, count and all. So a skeleton
/// nothing posed reports the full bone count and not one posed bone, and a
/// check on the count passes on it.
/// </para>
///
/// <para>
/// The numbers below are the shapes <c>tools/6c-gate-probe</c> measured on a
/// real build: 56 written of 56, all posed on a good build; 31 of 56 posed on a
/// build whose ordinary bones were written AS the entity transform, because the
/// procedural and IK-solved ones are written by something else and survive.
/// Both read "more than one", which is why the live gate asserts an equality.
/// </para>
/// </summary>
public class StandaloneBonesTests
{
    private const int M = StandaloneBones.FloatsPerMatrix;

    /// <summary>The entity's own transform, as any AngleMatrix would build one.</summary>
    private static float[] EntityTransform()
        => new float[] { 1, 0, 0, 12.5f, 0, 1, 0, -4f, 0, 0, 1, 64f };

    /// <summary>
    /// A skeleton of <paramref name="bones"/> bones. Every bone is written
    /// unless its index is in <paramref name="unwritten"/>, and every written
    /// bone is a copy of the entity transform unless its index is in
    /// <paramref name="posed"/> — which is the unposed build, the one a count
    /// cannot see.
    /// </summary>
    private static StandaloneBones Build(int bones, int[] unwritten, int[] posed)
    {
        var entity    = EntityTransform();
        var matrices  = new float[bones * M];
        var written   = new uint[(bones + 31) / 32];

        for (int b = 0; b < bones; b++)
        {
            if (Array.IndexOf(unwritten, b) >= 0)
                continue;

            written[b / 32] |= 1u << (b % 32);

            Array.Copy(entity, 0, matrices, b * M, M);

            if (Array.IndexOf(posed, b) >= 0)
                matrices[b * M + 3] += 3.25f;   // moved off the entity's origin
        }

        return new StandaloneBones(bones, matrices, written, entity);
    }

    [Fact]
    public void EveryBoneOfAFullyPosedSkeletonCounts()
    {
        var skeleton = Build(56, Array.Empty<int>(), Enumerable56());

        Assert.Equal(56, skeleton.WrittenCount());
        Assert.Equal(56, skeleton.PosedCount());
    }

    private static int[] Enumerable56()
    {
        var all = new int[56];

        for (int i = 0; i < 56; i++) all[i] = i;

        return all;
    }

    /// <summary>
    /// The defect this whole slot is for: every bone written, and written AS
    /// the entity transform. The bone count and the written count are both
    /// right and nothing is posed.
    /// </summary>
    [Fact]
    public void AnUnposedSkeletonIsFullyWrittenAndNotPosedAtAll()
    {
        var skeleton = Build(56, Array.Empty<int>(), Array.Empty<int>());

        Assert.Equal(56, skeleton.Count);
        Assert.Equal(56, skeleton.WrittenCount());
        Assert.Equal(0, skeleton.PosedCount());
    }

    /// <summary>
    /// The partial version, which is the one that reads as healthy under a
    /// threshold. 31 of 56 posed passes "more than one" and is a build in which
    /// 25 bones went wrong.
    /// </summary>
    [Fact]
    public void APartlyPosedSkeletonIsCountedExactly()
    {
        var posed = new int[31];

        for (int i = 0; i < 31; i++) posed[i] = i;

        var skeleton = Build(56, Array.Empty<int>(), posed);

        Assert.Equal(56, skeleton.WrittenCount());
        Assert.Equal(31, skeleton.PosedCount());
    }

    /// <summary>
    /// An UNWRITTEN bone is never posed, however different its slot looks. The
    /// buffer holds whatever was there, and "it differs from the entity
    /// transform" is true of uninitialised memory for reasons that have nothing
    /// to do with posing — the first replay of the server fixture found sixty
    /// rows of exactly that.
    /// </summary>
    [Fact]
    public void AnUnwrittenBoneIsNotPosedEvenWhenItsSlotDiffers()
    {
        var skeleton = Build(8, new[] { 3, 5 }, Enumerable56());

        Assert.False(skeleton.IsWritten(3));
        Assert.False(skeleton.IsPosed(3));
        Assert.Equal(6, skeleton.WrittenCount());
        Assert.Equal(6, skeleton.PosedCount());
    }

    /// <summary>
    /// The written mask is read bit by bit and in the right word. A skeleton
    /// wider than 32 bones is the ordinary case — Male_01 is 56 — so an
    /// off-by-one-word mask would report the whole second half unwritten.
    /// </summary>
    [Fact]
    public void TheWrittenMaskSpansMoreThanOneWord()
    {
        var skeleton = Build(56, new[] { 40 }, Array.Empty<int>());

        Assert.True(skeleton.IsWritten(39));
        Assert.False(skeleton.IsWritten(40));
        Assert.True(skeleton.IsWritten(41));
        Assert.True(skeleton.IsWritten(55));
    }

    [Fact]
    public void AnIndexOutsideTheCacheIsNeitherWrittenNorPosed()
    {
        var skeleton = Build(8, Array.Empty<int>(), Enumerable56());

        Assert.False(skeleton.IsWritten(-1));
        Assert.False(skeleton.IsWritten(8));
        Assert.False(skeleton.IsPosed(8));
    }

    /// <summary>
    /// The comparison is BITWISE, matching the native probe's memcmp.
    ///
    /// <para>
    /// A negative zero equals a positive zero under <c>==</c> and is four
    /// different bytes to <c>memcmp</c>. If managed compared with <c>==</c> the
    /// two sides would report different posed counts for the same skeleton, and
    /// the disagreement would show up as a flaky gate rather than as a wrong
    /// answer.
    /// </para>
    /// </summary>
    [Fact]
    public void ANegativeZeroCountsAsADifferenceJustAsMemcmpWould()
    {
        var entity   = EntityTransform();
        var matrices = (float[])entity.Clone();

        matrices[1] = -0.0f;   // entity holds +0.0f at the same offset

        var skeleton = new StandaloneBones(1, matrices, new uint[] { 1 }, entity);

        Assert.True(0.0f == -0.0f);          // the trap, spelled out
        Assert.True(skeleton.IsPosed(0));
        Assert.Equal(1, skeleton.PosedCount());
    }
}
