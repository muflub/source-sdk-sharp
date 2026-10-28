using SourceSharp.Abi;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="BoneEntityState"/> and <see cref="BoneEntityStates"/>,
/// hand-derived from the shells' constructors and the small rules that read
/// only their own members (shell_standalone_client.cpp SequenceCycle,
/// AnimLayerCycle, ActiveAnimLayers, SetBoneMergeSource, SetModelScale,
/// PoseParametersForDuration; shell_standalone_hierarchy.cpp PoseCycle,
/// SetModelScale; shell_standalone.h InvalidateBoneCache).
/// </summary>
public class BonesEntityStateTests
{
    private static BoneCacheKey LiveKey()
    {
        BoneCacheKey k = default;
        k.Reset();
        k.ModelIndex = 3;
        k.BoneMask = 0x100;
        k.ModelScale = 1f;
        return k;
    }

    // ---- constructor ---------------------------------------------------------

    [Fact]
    public void AHeldCycleIsTheCycleWhateverTheClockSays()
    {
        var s = new BoneEntityState { AnimTime = 0f, PlaybackRate = 1f, SeqDuration = 2f, SeqLooping = true };
        Assert.Equal(0.25f, s.SequenceCycle(0.5f), 4);

        s.HeldCycle = 0.7f;

        Assert.Equal(0.7f, s.SequenceCycle(0.5f));
        Assert.Equal(0.7f, s.SequenceCycle(100f));
    }

    [Fact]
    public void ANewStatesCacheKeyIsNeverValid()
    {
        BoneEntityState s = new();
        BoneCacheKey wanted = default;
        wanted.Reset();
        wanted.ModelIndex = -1;
        Assert.False(BoneCacheKey.IsValidFor(s.CacheKey, wanted));
    }

    [Fact]
    public void ANewStateMergesFromNothingWithANeverValidMapKey()
    {
        BoneEntityState s = new();
        Assert.Equal((-1, -1, (nint)0), (s.BoneMergeSource, s.MergeKey.OwnModelIndex, s.MergeKey.SourceModel));
    }

    [Fact]
    public void ANewStateOwesPoseDefaultsToNoModel()
    {
        Assert.Equal(-1, new BoneEntityState().PoseDefaultsFor);
    }

    [Fact]
    public void ANewStateWearsScaleOneAndPlaysAtRateOne()
    {
        BoneEntityState s = new();
        Assert.Equal((1f, 1f), (s.ModelScale, s.PlaybackRate));
    }

    [Fact]
    public void ANewStatesLayersAreUnfilledAtRateOne()
    {
        BoneEntityState s = new();
        for (int i = 0; i < AnimLayerRule.MaxLayers; i++)
        {
            Assert.Equal((-1, 1f, 0f), (s.Layers[i].Sequence, s.LayerRate[i], s.LayerDuration[i]));
        }
    }

    [Fact]
    public void ANewStateHasNoRagdollIkOrSprings()
    {
        BoneEntityState s = new();
        Assert.Equal(BoneEntityState.RagdollNone, s.Ragdoll);
        Assert.Null(s.Ik);
        Assert.Null(s.Jiggle);
    }

    // ---- InvalidateBoneCache -------------------------------------------------

    [Fact]
    public void InvalidateResetsTheKeyAndTheAttachments()
    {
        BoneEntityState s = new() { CacheKey = LiveKey(), AttachmentsValid = true };
        s.InvalidateBoneCache();
        Assert.Equal(-1, s.CacheKey.ModelIndex);
        Assert.False(s.AttachmentsValid);
    }

    // ---- SetModelScale -------------------------------------------------------

    [Fact]
    public void AZeroScaleIsRefusedAndChangesNothing()
    {
        BoneEntityState s = new() { CacheKey = LiveKey() };
        Assert.Equal(ModelScaleChange.Refused, s.SetModelScale(0f));
        Assert.Equal((1f, 3), (s.ModelScale, s.CacheKey.ModelIndex));
    }

    [Fact]
    public void ANegativeScaleIsRefused()
    {
        BoneEntityState s = new();
        Assert.Equal(ModelScaleChange.Refused, s.SetModelScale(-2f));
        Assert.Equal(1f, s.ModelScale);
    }

    [Fact]
    public void TheSameScaleKeepsTheCache()
    {
        BoneEntityState s = new() { CacheKey = LiveKey(), AttachmentsValid = true };
        Assert.Equal(ModelScaleChange.Unchanged, s.SetModelScale(1f));
        Assert.Equal((3, true), (s.CacheKey.ModelIndex, s.AttachmentsValid));
    }

    [Fact]
    public void ANewScaleIsStoredAndInvalidatesTheCache()
    {
        BoneEntityState s = new() { CacheKey = LiveKey(), AttachmentsValid = true };
        Assert.Equal(ModelScaleChange.Changed, s.SetModelScale(2f));
        Assert.Equal((2f, -1, false), (s.ModelScale, s.CacheKey.ModelIndex, s.AttachmentsValid));
    }

    // ---- SequenceCycle / PoseCycle -------------------------------------------

    [Fact]
    public void NoDurationIsCycleZero()
    {
        BoneEntityState s = new() { SeqDuration = 0f, AnimTime = 0f };
        Assert.Equal(0f, s.SequenceCycle(5f));
    }

    [Fact]
    public void AClockBeforeTheStartIsCycleZero()
    {
        BoneEntityState s = new() { SeqDuration = 2f, AnimTime = 10f };
        Assert.Equal(0f, s.SequenceCycle(9f));
    }

    [Fact]
    public void ALoopingSequenceWraps()
    {
        BoneEntityState s = new() { SeqDuration = 2f, AnimTime = 1f, PlaybackRate = 1f, SeqLooping = true };
        Assert.Equal(0.5f, s.SequenceCycle(4f)); // (4-1)*1/2 = 1.5 -> 0.5
    }

    [Fact]
    public void AOneShotHoldsAtStocksClamp()
    {
        BoneEntityState s = new() { SeqDuration = 2f, AnimTime = 1f, PlaybackRate = 1f, SeqLooping = false };
        Assert.Equal(0.999f, s.SequenceCycle(4f));
    }

    [Fact]
    public void ThePlaybackRateScalesTheCycle()
    {
        BoneEntityState s = new() { SeqDuration = 4f, AnimTime = 0f, PlaybackRate = 2f };
        Assert.Equal(0.5f, s.SequenceCycle(1f));
    }

    // ---- layers --------------------------------------------------------------

    [Fact]
    public void ALayerSlotOutOfRangeHasCycleZero()
    {
        BoneEntityState s = new();
        Assert.Equal(0f, s.AnimLayerCycle(AnimLayerRule.MaxLayers, 3f));
        Assert.Equal(0f, s.AnimLayerCycle(-1, 3f));
    }

    [Fact]
    public void ALayerRunsOnItsOwnClock()
    {
        BoneEntityState s = new();
        s.LayerStart[2] = 1f;
        s.LayerRate[2] = 0.5f;
        s.LayerDuration[2] = 1f;
        s.LayerLooping[2] = true;
        Assert.Equal(0.5f, s.AnimLayerCycle(2, 4f)); // (4-1)*0.5/1 = 1.5 -> 0.5
    }

    [Fact]
    public void AOneShotLayerHoldsAtStocksClamp()
    {
        BoneEntityState s = new();
        s.LayerDuration[0] = 1f;
        Assert.Equal(0.999f, s.AnimLayerCycle(0, 5f));
    }

    [Fact]
    public void ActiveLayersCountsFilledWeightedSlotsWithNoRangeTest()
    {
        BoneEntityState s = new();
        s.Layers[0] = new AnimLayer { Sequence = 900, Weight = 1f };
        s.Layers[3] = new AnimLayer { Sequence = 1, Weight = 0f };
        s.Layers[5] = new AnimLayer { Sequence = 0, Weight = 0.1f };
        Assert.Equal(2, s.ActiveAnimLayers());
    }

    // ---- PoseParametersForDuration -------------------------------------------

    [Fact]
    public void ThePoseIsOfferedOnlyForTheModelItsDefaultsWereReadFor()
    {
        BoneEntityState s = new() { PoseDefaultsFor = 4 };
        Assert.Same(s.PoseParameter, s.PoseParametersForDuration(4));
        Assert.Null(s.PoseParametersForDuration(5));
    }

    // ---- SetBoneMergeSource ----------------------------------------------------

    [Fact]
    public void TheSameMergeSourceKeepsBothKeys()
    {
        BoneEntityState s = new() { BoneMergeSource = 7, CacheKey = LiveKey(), AttachmentsValid = true };
        s.MergeKey = new BoneMergeKey { OwnModelIndex = 3, SourceModel = 99 };
        s.SetBoneMergeSource(7);
        Assert.Equal((3, 3, true), (s.CacheKey.ModelIndex, s.MergeKey.OwnModelIndex, s.AttachmentsValid));
    }

    [Fact]
    public void ANewMergeSourceResetsBothKeysAndTheAttachments()
    {
        BoneEntityState s = new() { BoneMergeSource = 7, CacheKey = LiveKey(), AttachmentsValid = true };
        s.MergeKey = new BoneMergeKey { OwnModelIndex = 3, SourceModel = 99 };
        s.SetBoneMergeSource(8);
        Assert.Equal((8, -1, -1, (nint)0, false),
            (s.BoneMergeSource, s.CacheKey.ModelIndex, s.MergeKey.OwnModelIndex, s.MergeKey.SourceModel, s.AttachmentsValid));
    }

    // ---- ResizeCachedBones -----------------------------------------------------

    [Fact]
    public void ResizeKeepsTheSurvivingPrefix()
    {
        BoneEntityState s = new() { CachedBones = new BoneMatrix3x4[3] };
        s.CachedBones[1].Position = new Vector(1f, 2f, 3f);
        s.ResizeCachedBones(2);
        Assert.Equal((2, new Vector(1f, 2f, 3f)), (s.CachedBones.Length, s.CachedBones[1].Position));
    }

    // ---- BoneEntityStates ------------------------------------------------------

    [Fact]
    public void OneHandleIsOneState()
    {
        BoneEntityStates states = new();
        Assert.Same(states.GetOrCreate(new EntityHandle(5)), states.GetOrCreate(new EntityHandle(5)));
    }

    [Fact]
    public void TwoHandlesAreTwoStates()
    {
        BoneEntityStates states = new();
        Assert.NotSame(states.GetOrCreate(new EntityHandle(5)), states.GetOrCreate(new EntityHandle(6)));
    }

    [Fact]
    public void ARemovedEntityStartsAgain()
    {
        BoneEntityStates states = new();
        BoneEntityState first = states.GetOrCreate(new EntityHandle(5));
        Assert.True(states.Remove(new EntityHandle(5)));
        Assert.False(states.TryGet(new EntityHandle(5), out _));
        Assert.NotSame(first, states.GetOrCreate(new EntityHandle(5)));
    }

    [Fact]
    public void InvalidateAllResetsEveryKeyAndCountsThem()
    {
        BoneEntityStates states = new();
        BoneEntityState a = states.GetOrCreate(new EntityHandle(1));
        BoneEntityState b = states.GetOrCreate(new EntityHandle(2));
        a.CacheKey = LiveKey();
        b.CacheKey = LiveKey();
        Assert.Equal(2, states.InvalidateAll());
        Assert.Equal((-1, -1), (a.CacheKey.ModelIndex, b.CacheKey.ModelIndex));
    }

    [Fact]
    public void ClearDropsEveryState()
    {
        BoneEntityStates states = new();
        states.GetOrCreate(new EntityHandle(1));
        states.Clear();
        Assert.Equal(0, states.Count);
    }
}
