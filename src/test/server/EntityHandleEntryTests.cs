using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The entry arithmetic on <see cref="EntityHandle"/> (plan_abi_hotreload D7):
/// moved off <c>EntityDirectory</c> so the shell-less route, which stays beside
/// the handle type, asks it without naming the server's directory.
/// </summary>
public class EntityHandleEntryTests
{
    [Fact]
    public void TheEntryIsTheLowSixteenBits()
        => Assert.Equal(0x1234, new EntityHandle(0x0007_1234u).EntryIndex);

    [Fact]
    public void TheInvalidHandleAnswersTheLastEntry()
        => Assert.Equal(EntityHandle.EntryCount - 1, EntityHandle.None.EntryIndex);

    [Fact]
    public void TheLastEdictIsNotInTheInvisibleRange()
        => Assert.False(new EntityHandle((1u << 16) | 2047u).IsInvisibleRange);

    [Fact]
    public void TheFirstEntryPastTheEdictsIsInTheInvisibleRange()
        => Assert.True(new EntityHandle((1u << 16) | 2048u).IsInvisibleRange);

    [Fact]
    public void TheDirectoryAnswersWhatTheHandleAnswers()
    {
        var handle = new EntityHandle(0x0003_0901u);
        Assert.Equal(handle.EntryIndex, EntityDirectory.EntryIndex(handle));
        Assert.Equal(EntityHandle.IsInvisibleEntry(4000), EntityDirectory.IsInvisibleEntry(4000));
    }
}
