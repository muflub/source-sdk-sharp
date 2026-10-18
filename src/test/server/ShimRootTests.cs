using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables step A3: <see cref="ShimRoot"/> binds the one native
/// object LoadSideV2 hands a side, with the forwarder table native hands beside
/// it (plan_forwarders_everywhere.md phase 3), and refuses the two roots that
/// would misbind every later call. The native half here is a fake built in
/// unmanaged memory: an object, and a table of function pointers each called
/// with the object first.
/// </summary>
public unsafe class ShimRootTests : IDisposable
{
    private static readonly nint FrameObject = 0x5EED;
    private readonly List<nint> _blocks = new();

    [Fact]
    public void ANullRootIsRefused()
    {
        Assert.False(ShimRoot.TryBind(null, FakeTable(), out var bound, out string why));
        Assert.Null(bound);
        Assert.Contains("no shim root", why);
    }

    [Fact]
    public void ATableShorterThanTheStructIsRefusedNamingBoth()
    {
        void* root = FakeRoot();

        Assert.False(ShimRoot.TryBind(root, FakeTable(count: 2), out var bound, out string why));
        Assert.Null(bound);
        Assert.Contains("with 2 forwarder(s)", why);
        Assert.Contains($"reads {ShimRoot.ExpectedEntries}", why);
    }

    [Fact]
    public void ALongerTableIsANewerLibraryAndBinds()
        // The root's table only appends (tools/engine-forwarders), so slots
        // past the struct's are a newer library's, never read here.
        => Assert.True(ShimRoot.TryBind(FakeRoot(), FakeTable(count: 4), out _, out string why), why);

    [Fact]
    public void NoTableIsRefused()
    {
        Assert.False(ShimRoot.TryBind(FakeRoot(), null, out var bound, out string why));
        Assert.Null(bound);
        Assert.Contains("no table", why);
    }

    [Fact]
    public void AGoodRootBindsAndAcquireReturnsTheObjectAndItsCount()
    {
        void* root = FakeRoot();

        Assert.True(ShimRoot.TryBind(root, FakeTable(), out var bound, out string why), why);
        void* frame = bound!.Acquire("SourceSharpFrame001", out int entries);

        Assert.Equal(FrameObject, (nint)frame);
        Assert.Equal(19, entries);
    }

    [Fact]
    public void ANameTheSideDoesNotServeAnswersNullAndZero()
    {
        void* root = FakeRoot();

        Assert.True(ShimRoot.TryBind(root, FakeTable(), out var bound, out _));
        void* none = bound!.Acquire("SourceSharpNoSuchThing001", out int entries);

        Assert.True(none == null);
        Assert.Equal(0, entries);
    }

    [Fact]
    public void TheTableIsThreeSlots()
        => Assert.Equal(3, ShimRoot.ExpectedEntries);   // AcquireInterface joined at C5; AbiVersion left 2026-10-01

    private void* FakeRoot()
    {
        var obj = (nint*)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        _blocks.Add((nint)obj);
        return obj;
    }

    /// <summary>The root's forwarder table: Acquire, AcquireInterface, Release, at <paramref name="count"/> slots.</summary>
    private void* FakeTable(int count = 3)
    {
        var slots = (nint*)NativeMemory.AllocZeroed((nuint)(Math.Max(count, 3) * sizeof(nint)));
        slots[0] = (nint)(delegate* unmanaged<void*, byte*, void*, void*>)&Acquire;
        slots[2] = (nint)(delegate* unmanaged<void*, void>)&Release;

        var header = (ForwarderTableHeader*)NativeMemory.AllocZeroed((nuint)sizeof(ForwarderTableHeader));
        header->Slots = (void**)slots;
        header->SlotCount = count;

        _blocks.Add((nint)slots);
        _blocks.Add((nint)header);
        return header;
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
