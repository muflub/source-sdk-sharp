using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables step A3: <see cref="ShimRoot"/> binds the one native
/// object LoadSideV2 hands the server, and refuses the two roots that would
/// misbind every later call. The native root here is a fake built in
/// unmanaged memory with the same shape: an object whose first word is a
/// vtable of function pointers, called with the object as `this`.
/// </summary>
public unsafe class ShimRootTests : IDisposable
{
    private static readonly nint FrameObject = 0x5EED;
    private readonly List<nint> _blocks = new();

    [Fact]
    public void ANullRootIsRefused()
    {
        Assert.False(ShimRoot.TryBind(null, ShimRoot.ExpectedEntries, out var bound, out string why));
        Assert.Null(bound);
        Assert.Contains("no shim root", why);
    }

    [Fact]
    public void ACountOtherThanTheLayoutsIsRefusedNamingBoth()
    {
        void* root = FakeRoot();

        Assert.False(ShimRoot.TryBind(root, 2, out var bound, out string why));
        Assert.Null(bound);
        Assert.Contains("2 slot(s)", why);
        Assert.Contains($"has {ShimRoot.ExpectedEntries}", why);
    }

    [Fact]
    public void AGoodRootBindsAndAcquireReturnsTheObjectAndItsCount()
    {
        void* root = FakeRoot();

        Assert.True(ShimRoot.TryBind(root, ShimRoot.ExpectedEntries, out var bound, out string why), why);
        void* frame = bound!.Acquire("SourceSharpFrame001", out int entries);

        Assert.Equal(FrameObject, (nint)frame);
        Assert.Equal(19, entries);
    }

    [Fact]
    public void ANameTheSideDoesNotServeAnswersNullAndZero()
    {
        void* root = FakeRoot();

        Assert.True(ShimRoot.TryBind(root, ShimRoot.ExpectedEntries, out var bound, out _));
        void* none = bound!.Acquire("SourceSharpNoSuchThing001", out int entries);

        Assert.True(none == null);
        Assert.Equal(0, entries);
    }

    [Fact]
    public void TheLayoutIsThreeSlots()
        => Assert.Equal(3, ShimRoot.ExpectedEntries);   // AcquireInterface joined at C5; AbiVersion left 2026-10-01

    private void* FakeRoot()
    {
        var vtbl = (nint*)NativeMemory.AllocZeroed((nuint)(3 * sizeof(nint)));
        vtbl[0] = (nint)(delegate* unmanaged<void*, byte*, void*, void*>)&Acquire;
        vtbl[2] = (nint)(delegate* unmanaged<void*, void>)&Release;

        var obj = (nint*)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        obj[0] = (nint)vtbl;

        _blocks.Add((nint)vtbl);
        _blocks.Add((nint)obj);
        return obj;
    }

    [UnmanagedCallersOnly]
    private static void* Acquire(void* self, byte* name, void* pEntries)
    {
        string n = Marshal.PtrToStringUTF8((nint)name) ?? string.Empty;
        if (n != "SourceSharpFrame001")
        {
            *(int*)pEntries = 0;
            return null;
        }

        *(int*)pEntries = 19;
        return (void*)FrameObject;
    }

    [UnmanagedCallersOnly]
    private static void Release(void* self)
    {
    }

    public void Dispose()
    {
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
    }
}
