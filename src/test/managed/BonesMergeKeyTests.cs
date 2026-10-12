using SourceSharp.Shared.Bones;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="BoneMergeKey"/>, the managed twin of
/// src/shared/native/bone_merge_key.h. The cases are the native gtest's
/// (src/test/native/bone_merge_key_test.cpp: SamePairStillStands,
/// OwnModelChangeInvalidates, SourceModelChangeInvalidates,
/// FreshKeyNeverAnswers); its two source-text facts are about the native
/// client shell and stay there.
/// </summary>
public class BonesMergeKeyTests
{
    private static readonly nint ModelA = 0x1000; // distinct handles standing in for model_t
    private static readonly nint ModelB = 0x2000;

    private static BoneMergeKey Key(int own, nint source) =>
        new() { OwnModelIndex = own, SourceModel = source };

    private static BoneMergeKey Fresh()
    {
        BoneMergeKey k = new() { OwnModelIndex = 99, SourceModel = ModelA };
        k.Reset();
        return k;
    }

    [Fact]
    public void SamePairStillStands()
    {
        Assert.True(BoneMergeKey.IsValidFor(Key(7, ModelA), Key(7, ModelA)));
    }

    [Fact]
    public void OwnModelChangeInvalidates()
    {
        Assert.False(BoneMergeKey.IsValidFor(Key(7, ModelA), Key(8, ModelA)));
    }

    [Fact]
    public void SourceModelChangeInvalidates()
    {
        Assert.False(BoneMergeKey.IsValidFor(Key(7, ModelA), Key(7, ModelB)));
    }

    [Fact]
    public void AFreshBuiltKeyNeverAnswers()
    {
        Assert.False(BoneMergeKey.IsValidFor(Fresh(), Key(7, ModelA)));
    }

    [Fact]
    public void AFreshWantedKeyIsNeverAnswered()
    {
        Assert.False(BoneMergeKey.IsValidFor(Key(7, ModelA), Fresh()));
    }

    [Fact]
    public void TwoFreshKeysDoNotAnswerEachOther()
    {
        Assert.False(BoneMergeKey.IsValidFor(Fresh(), Fresh()));
    }

    [Fact]
    public void ResetIsMinusOneAndNoModel()
    {
        BoneMergeKey k = Fresh();
        Assert.Equal((-1, (nint)0), (k.OwnModelIndex, k.SourceModel));
    }
}
