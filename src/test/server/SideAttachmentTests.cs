using System.Text;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What one side's load attaches to (<see cref="SideAttachment"/>,
/// plan_engineside_removal Phase 1): built detached, replaced whole at the
/// attach, and nothing shared between two of them.
/// </summary>
public class SideAttachmentTests
{
    [Fact]
    public void ADetachedAttachmentHasNoRootAndAnswersTheBoundaryIsNotUp()
    {
        var attachment = SideAttachment.Detached(Side.Server, new SideState(Side.Server));

        Assert.Null(attachment.Root);
        Assert.False(attachment.IsInitialized);
        Assert.Equal("the native half of this side is not initialised", Ask(attachment, "VEngineServer023"));
    }

    [Fact]
    public void TheAttachReplacesTheAreasAndTheRootAndRunsWhatWaitsOnIt()
    {
        using var root = new FakeShimRoot();
        var attachment = SideAttachment.Detached(Side.Server, new SideState(Side.Server));
        object before = attachment.Areas.Require<IEntitiesServerArea>();
        int ran = 0;
        attachment.WhenAttached(() => ran++);

        Assert.True(attachment.AttachShims(root.Bind(), out string why), why);

        Assert.NotNull(attachment.Root);
        Assert.NotSame(before, attachment.Areas.Require<IEntitiesServerArea>());
        Assert.Equal(1, ran);
    }

    /// <summary>
    /// The areas attach all or none: a root that does not serve one leaves the
    /// attachment as it was (no root, its detached areas, nothing waiting run)
    /// and says which area it missed, which the load prints in its refusal.
    /// The positive arm is the attach fact above, over the same root unwithheld.
    /// </summary>
    [Fact]
    public void AWithheldAreaLeavesTheAttachmentDetachedAndNamesTheArea()
    {
        using var root = new FakeShimRoot().Withhold("SourceSharpEntitiesServer001");
        var attachment = SideAttachment.Detached(Side.Server, new SideState(Side.Server));
        object before = attachment.Areas.Require<IEntitiesServerArea>();
        int ran = 0;
        attachment.WhenAttached(() => ran++);

        Assert.False(attachment.AttachShims(root.Bind(), out string why));

        Assert.Contains("SourceSharpEntitiesServer001", why, StringComparison.Ordinal);
        Assert.Null(attachment.Root);
        Assert.Same(before, attachment.Areas.Require<IEntitiesServerArea>());
        Assert.Equal(0, ran);
    }

    [Fact]
    public void TheAttachMakesAFreshBoundSet()
    {
        using var root = new FakeShimRoot();
        var attachment = SideAttachment.Detached(Side.Server, new SideState(Side.Server));
        EngineBoundSet before = attachment.Bound;

        Assert.True(attachment.AttachShims(root.Bind(), out string why), why);

        Assert.NotSame(before, attachment.Bound);
    }

    [Fact]
    public void TheSideOverAnAttachmentTakesUpTheAttachedAreas()
    {
        using var root = new FakeShimRoot();
        var attachment = SideAttachment.Detached(Side.Server, new SideState(Side.Server));
        EngineSide side = EngineSide.Over(attachment);

        // Before the attach the side's console is the detached area, which
        // throws, naming itself; after it the side answers from the root's.
        Assert.Throws<InvalidOperationException>(() => side.Msg("before\n"));
        Assert.True(attachment.AttachShims(root.Bind(), out string why), why);

        Assert.Same(attachment, side.Attachment);
        side.Msg("after\n");
    }

    [Fact]
    public void TwoAttachmentsShareNothing()
    {
        var a = SideAttachment.Detached(Side.Server, new SideState(Side.Server));
        var b = SideAttachment.Detached(Side.Server, new SideState(Side.Server));
        a.ManagedDirectory = "/a";
        a.Initialize();

        Assert.NotSame(a.Areas, b.Areas);
        Assert.NotSame(a.Bound, b.Bound);
        Assert.NotSame(a.Interfaces, b.Interfaces);
        Assert.Equal(string.Empty, b.ManagedDirectory);
        Assert.False(b.IsInitialized);
    }

    [Fact]
    public void AnAttachmentRefusesTheOtherSidesState()
        => Assert.Throws<ArgumentException>(() => SideAttachment.Detached(Side.Server, new SideState(Side.Client)));

    private static unsafe string Ask(SideAttachment attachment, string version)
    {
        byte* error = stackalloc byte[256];
        byte[] name = Encoding.ASCII.GetBytes(version + "\0");
        fixed (byte* p = name)
            Assert.True(attachment.AcquireInterface(SourceSharpFactoryType.AppSystem, p, error, 256) == null);
        return Encoding.ASCII.GetString(error, new ReadOnlySpan<byte>(error, 256).IndexOf((byte)0));
    }
}
