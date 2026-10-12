using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables step A4: <see cref="ShimConsole"/> is acquired from
/// the root by name, refuses a root that does not serve it or serves fewer
/// slots than the layout needs, and routes a call through slot 0 with the text
/// intact. The root and the console are fakes built in unmanaged memory, in
/// the native shape: an object whose first word is its vtable.
/// </summary>
public unsafe class ShimConsoleTests : IDisposable
{
    private readonly List<nint> _blocks = new();

    // What the fake console's Msg and Warning slots were last handed.
    private static string? s_lastMsg;
    private static string? s_lastWarning;

    // What the fake root answers for the console: its object and its count.
    private static nint s_console;
    private static int s_consoleEntries;

    [Fact]
    public void MsgReachesSlotZeroWithTheTextIntact()
    {
        var console = Acquire(ShimConsole.ExpectedEntries);

        console.Msg("héllo, 100%\n");

        Assert.Equal("héllo, 100%\n", s_lastMsg);
    }

    [Fact]
    public void WarningReachesSlotOneAndNotSlotZero()
    {
        var console = Acquire(ShimConsole.ExpectedEntries);
        s_lastMsg = null;

        console.Warning("careful");

        Assert.Equal("careful", s_lastWarning);
        Assert.Null(s_lastMsg);
    }

    [Fact]
    public void ARootThatDoesNotServeTheConsoleIsRefused()
    {
        ShimRoot root = Root(serveConsole: false, entries: 0);

        Assert.False(ShimConsole.TryAcquire(root, out var bound, out string why));
        Assert.Null(bound);
        Assert.Contains("does not serve SourceSharpConsole001", why);
    }

    [Fact]
    public void FewerSlotsThanTheLayoutNeedsIsRefusedNamingBoth()
    {
        ShimRoot root = Root(serveConsole: true, entries: ShimConsole.ExpectedEntries - 1);

        Assert.False(ShimConsole.TryAcquire(root, out var bound, out string why));
        Assert.Null(bound);
        Assert.Contains($"{ShimConsole.ExpectedEntries - 1} slot(s)", why);
        Assert.Contains($"needs {ShimConsole.ExpectedEntries}", why);
    }

    [Fact]
    public void MoreSlotsThanTheLayoutKnowsIsAccepted()
    {
        // Append-only: a native side that has grown keeps every known slot.
        ShimRoot root = Root(serveConsole: true, entries: ShimConsole.ExpectedEntries + 3);

        Assert.True(ShimConsole.TryAcquire(root, out var bound, out string why), why);
        Assert.NotNull(bound);
    }

    [Fact]
    public void TheLayoutIsElevenSlots()
        // Ten, then RevertNamedConVar (sound cluster 17, lane c2-sims).
        => Assert.Equal(11, ShimConsole.ExpectedEntries);

    private ShimConsole Acquire(int entries)
    {
        ShimRoot root = Root(serveConsole: true, entries);
        Assert.True(ShimConsole.TryAcquire(root, out var bound, out string why), why);
        return bound!;
    }

    private ShimRoot Root(bool serveConsole, int entries)
    {
        var consoleVtbl = Alloc(ShimConsole.ExpectedEntries);
        consoleVtbl[0] = (nint)(delegate* unmanaged<void*, byte*, void>)&Msg;
        consoleVtbl[1] = (nint)(delegate* unmanaged<void*, byte*, void>)&Warning;

        var console = Alloc(1);
        console[0] = (nint)consoleVtbl;
        s_console = serveConsole ? (nint)console : 0;
        s_consoleEntries = entries;

        var rootVtbl = Alloc(3);   // Acquire, AcquireInterface, Release
        rootVtbl[0] = (nint)(delegate* unmanaged<void*, byte*, void*, void*>)&RootAcquire;
        rootVtbl[2] = (nint)(delegate* unmanaged<void*, void>)&RootRelease;

        var root = Alloc(1);
        root[0] = (nint)rootVtbl;

        Assert.True(ShimRoot.TryBind(root, ShimRoot.ExpectedEntries, out var bound, out string why), why);
        return bound!;
    }

    private nint* Alloc(int words)
    {
        var p = (nint*)NativeMemory.AllocZeroed((nuint)(words * sizeof(nint)));
        _blocks.Add((nint)p);
        return p;
    }

    [UnmanagedCallersOnly]
    private static void* RootAcquire(void* self, byte* name, void* pEntries)
    {
        bool console = Marshal.PtrToStringUTF8((nint)name) == ShimConsole.InterfaceVersion && s_console != 0;
        *(int*)pEntries = console ? s_consoleEntries : 0;
        return console ? (void*)s_console : null;
    }

    [UnmanagedCallersOnly]
    private static void RootRelease(void* self)
    {
    }

    [UnmanagedCallersOnly]
    private static void Msg(void* self, byte* text) => s_lastMsg = Marshal.PtrToStringUTF8((nint)text);

    [UnmanagedCallersOnly]
    private static void Warning(void* self, byte* text) => s_lastWarning = Marshal.PtrToStringUTF8((nint)text);

    public void Dispose()
    {
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
    }
}
