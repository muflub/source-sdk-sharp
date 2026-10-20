using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables Phase C: once the side's shims are attached, an area
/// listed in docs/engineapi-switched.txt is reached through its vtable. Since
/// C6c a server-only slot has no struct field to fall back on, and calling one
/// unattached throws rather than answering zero.
/// </summary>
public unsafe class EngineSideRoutingTests : IDisposable
{
    private readonly FakeShimRoot _root = new();

    [Fact]
    public void ASwitchedSlotIsAnsweredByItsVtableOnceAttached()
    {
        var side = TestSides.Create(Side.Server);
        side.Initialize();   // plan_client_vtables CC-last: there is no struct

        _root.Live("SourceSharpDebugOverlay001", nameof(ShimDebugOverlayFwd.OverlayCounts),
                   (nint)(delegate* unmanaged<void*, uint*, uint*, uint*, uint*, int*, void>)&Counts);
        Assert.True(side.AttachOwnShims(_root.Bind(), out string why), why);

        Assert.Equal((7u, 1u, 2u, 3u, true), SideBindings.Of<IEngineHud>(side).OverlayReadout());
    }

    [Fact]
    public void AServerOnlySlotIsAbsentBeforeTheShimsAttach()
    {
        var side = TestSides.Create(Side.Server);
        side.Initialize();

        Assert.Null(side.Root);
    }

    [Fact]
    public void AServerOnlySlotCalledUnattachedThrowsRatherThanAnsweringZero()
    {
        // A side as its load makes it, before the attach (not a test's side
        // over the default areas): its areas are detached ones, over no table,
        // and a call refuses with the reason. Nothing checks for the attach.
        var side = EngineSide.Create(Side.Server, new SideState(Side.Server));
        side.Initialize();

        var lifecycle = side.Areas.Require<IEntityLifecycleArea>();
        var e = Assert.Throws<InvalidOperationException>(() => lifecycle.EntityFactoryCount());
        Assert.Contains("before the shim areas were attached", e.Message);
    }

    [UnmanagedCallersOnly]
    private static void Counts(void* self, uint* drawn, uint* clipped, uint* noViewer, uint* noOverlay, int* viewerSet)
    {
        *drawn = 7;
        *clipped = 1;
        *noViewer = 2;
        *noOverlay = 3;
        *viewerSet = 1;
    }

    public void Dispose() => _root.Dispose();
}
