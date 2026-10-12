using System;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="BoneCacheKey"/>, the managed twin of
/// src/shared/native/bone_cache_key.h. The cases are the native gtest's
/// (src/test/native/bone_cache_key_test.cpp), one behaviour each.
/// </summary>
public class BonesCacheKeyTests
{
    private const int UsedByAttachment = 0x2;        // the gtest's kUsedByAttachment
    private const int UsedByAnything = 0x000FFFFF;   // BONE_USED_BY_ANYTHING

    private static BoneCacheKey MakeKey(int modelIndex, int boneMask, float x, float y, float z,
                                        float pitch = 0f, float yaw = 0f, float roll = 0f)
    {
        BoneCacheKey key = default;
        key.Reset();
        key.ModelIndex = modelIndex;
        key.BoneMask = boneMask;
        key.Origin = new Vector(x, y, z);
        key.Angles = new Vector(pitch, yaw, roll);
        return key;
    }

    private static Vector Bump(Vector v, int axis, float by) => axis switch
    {
        0 => v with { X = v.X + by },
        1 => v with { Y = v.Y + by },
        _ => v with { Z = v.Z + by },
    };

    [Fact]
    public void IdenticalRequestHits()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 100f, -50f, 8f, 0f, 90f, 0f);
        Assert.True(BoneCacheKey.IsValidFor(cached, cached));
    }

    [Fact]
    public void AResetKeyAnswersNothing()
    {
        BoneCacheKey cached = default;
        cached.Reset();
        Assert.False(BoneCacheKey.IsValidFor(cached, MakeKey(0, 0, 0f, 0f, 0f)));
    }

    [Fact]
    public void ResetZeroesThePlacementAndTheMask()
    {
        BoneCacheKey key = MakeKey(5, UsedByAnything, 1f, 2f, 3f, 4f, 5f, 6f);
        key.Reset();
        Assert.Equal((-1, 0, Vector.Zero, Vector.Zero, 0, 0f),
                     (key.ModelIndex, key.BoneMask, key.Origin, key.Angles, key.Sequence, key.Cycle));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MovingOnAnyAxisMisses(int axis)
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 100f, -50f, 8f);
        BoneCacheKey wanted = cached;
        wanted.Origin = Bump(wanted.Origin, axis, 1f);
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TurningOnAnyAxisMisses(int axis)
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 100f, -50f, 8f);
        BoneCacheKey wanted = cached;
        wanted.Angles = Bump(wanted.Angles, axis, 1f);
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void TheSmallestRepresentableMoveMisses()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 100f, 0f, 0f);
        BoneCacheKey wanted = cached;
        wanted.Origin = wanted.Origin with { X = 100.0f + 0.0001f };
        Assert.NotEqual(cached.Origin.X, wanted.Origin.X); // the fact's own premise
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void ADifferentModelMisses()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 0f, 0f, 0f);
        BoneCacheKey wanted = cached;
        wanted.ModelIndex = 13;
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void ADifferentSequenceMisses()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 0f, 0f, 0f);
        cached.Sequence = 3;
        BoneCacheKey wanted = cached;
        wanted.Sequence = 4;
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void AMovedCycleMisses()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 0f, 0f, 0f);
        cached.Sequence = 3;
        cached.Cycle = 0.25f;
        BoneCacheKey wanted = cached;
        wanted.Cycle = 0.5f;
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void TheSameCycleStillHits()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 0f, 0f, 0f);
        cached.Sequence = 3;
        cached.Cycle = 0.25f;
        BoneCacheKey wanted = cached;
        Assert.True(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void ASupersetMaskSatisfiesASubsetRequest()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 0f, 0f, 0f);
        BoneCacheKey wanted = cached;
        wanted.BoneMask = UsedByAttachment;
        Assert.True(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void ASubsetMaskCannotSatisfyASupersetRequest()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAttachment, 0f, 0f, 0f);
        BoneCacheKey wanted = cached;
        wanted.BoneMask = UsedByAnything;
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void OverlappingButIncompleteMaskMisses()
    {
        BoneCacheKey cached = MakeKey(12, 0x3, 0f, 0f, 0f);
        BoneCacheKey wanted = cached;
        wanted.BoneMask = 0x6;
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void AnEmptyRequestIsSatisfiedByAComputedCache()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAttachment, 0f, 0f, 0f);
        BoneCacheKey wanted = cached;
        wanted.BoneMask = 0;
        Assert.True(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void AnEmptyRequestIsNotSatisfiedByANeverComputedCache()
    {
        BoneCacheKey wanted = MakeKey(12, 0, 0f, 0f, 0f);
        BoneCacheKey never = default;
        never.Reset();
        Assert.False(BoneCacheKey.IsValidFor(never, wanted));
    }

    [Fact]
    public void ADifferentPoseGenerationMisses()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 0f, 0f, 0f);
        cached.Sequence = 4;
        cached.Cycle = 0.25f;
        cached.PoseGeneration = 7;
        BoneCacheKey wanted = cached;
        wanted.PoseGeneration = 8;
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void TheSamePoseGenerationStillHits()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 0f, 0f, 0f);
        cached.Sequence = 4;
        cached.Cycle = 0.25f;
        cached.PoseGeneration = 7;
        Assert.True(BoneCacheKey.IsValidFor(cached, cached));
    }

    [Fact]
    public void ResetWritesThePoseGeneration()
    {
        BoneCacheKey key = default;
        key.PoseGeneration = 0x5EED;
        key.Reset();
        Assert.Equal(0, key.PoseGeneration);
    }

    [Fact]
    public void AMovedAutoplayClockMisses()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 0f, 0f, 0f);
        cached.AutoplayTime = 10.0f;
        BoneCacheKey wanted = cached;
        wanted.AutoplayTime = 10.015f;
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void AModelWithNoAutoplayStillHitsAcrossFrames()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 0f, 0f, 0f);
        cached.AutoplayTime = 0.0f;
        BoneCacheKey wanted = cached;
        Assert.True(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void AnAutoplayModelPutsTheClockInTheKey()
    {
        Assert.Equal(12.5f, BoneCacheKey.AutoplayClock(modelHasAutoplay: true, 12.5f));
    }

    [Fact]
    public void AModelWithoutAutoplayPutsZeroInTheKeyRatherThanTheClock()
    {
        Assert.Equal(0.0f, BoneCacheKey.AutoplayClock(modelHasAutoplay: false, 12.5f));
    }

    [Fact]
    public void ResetWritesTheAutoplayClock()
    {
        BoneCacheKey key = default;
        key.AutoplayTime = 1234.5f;
        key.Reset();
        Assert.Equal(0.0f, key.AutoplayTime);
    }

    [Fact]
    public void AGrownModelMisses()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 100f, -50f, 8f, 0f, 90f, 0f);
        cached.ModelScale = 1.0f;
        BoneCacheKey wanted = cached;
        wanted.ModelScale = 2.0f;
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void AShrunkModelMisses()
    {
        BoneCacheKey cached = MakeKey(7, UsedByAnything, 0f, 0f, 0f);
        cached.ModelScale = 1.0f;
        BoneCacheKey wanted = cached;
        wanted.ModelScale = 0.5f;
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void TheSmallestRepresentableScaleChangeMisses()
    {
        BoneCacheKey cached = MakeKey(3, UsedByAnything, 0f, 0f, 0f);
        cached.ModelScale = 1.0f;
        BoneCacheKey wanted = cached;
        wanted.ModelScale = MathF.BitIncrement(1.0f); // nextafterf(1, 2)
        Assert.NotEqual(cached.ModelScale, wanted.ModelScale); // not vacuous
        Assert.False(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void AnUnscaledModelStillHitsAcrossFrames()
    {
        BoneCacheKey cached = MakeKey(12, UsedByAnything, 100f, -50f, 8f, 0f, 90f, 0f);
        cached.ModelScale = 1.0f;
        BoneCacheKey wanted = cached;
        Assert.True(BoneCacheKey.IsValidFor(cached, wanted));
    }

    [Fact]
    public void ResetWritesTheModelScaleAsZeroAndNotOne()
    {
        BoneCacheKey key = default;
        key.ModelScale = 3.75f;
        key.Reset();
        Assert.Equal(0.0f, key.ModelScale);
    }
}
