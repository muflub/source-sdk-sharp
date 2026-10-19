using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables step A4: <see cref="ShimConsole"/> is acquired from
/// the root by name, refuses a root that does not serve it or serves its
/// forwarder table (plan_forwarders_everywhere.md) with fewer slots than the
/// struct reads, and routes a call through slot 0 with the text intact. The
/// root and the console are fakes built in unmanaged memory, in the native
/// shape: an object, and a table of functions each handed the object.
/// </summary>
public unsafe class ShimConsoleTests : IDisposable
{
    private readonly List<nint> _blocks = new();

    // What the fake console's Msg and Warning slots were last handed.
    private static string? s_lastMsg;
    private static string? s_lastWarning;

    // What the fake root answers for the console: its object, and its
    // forwarder table under the table's name.
    private static nint s_console;
    private static nint s_consoleTable;

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
        Assert.Contains($"with {ShimConsole.ExpectedEntries - 1} forwarder(s)", why);
        Assert.Contains($"reads {ShimConsole.ExpectedEntries}", why);
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
    public void TheTableIsElevenSlots()
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
        var consoleSlots = Alloc(Math.Max(entries, ShimConsole.ExpectedEntries));
        consoleSlots[0] = (nint)(delegate* unmanaged<void*, byte*, void>)&Msg;
        consoleSlots[1] = (nint)(delegate* unmanaged<void*, byte*, void>)&Warning;
        var consoleTable = (ForwarderTableHeader*)Alloc(sizeof(ForwarderTableHeader) / sizeof(nint));
        consoleTable->Slots = (void**)consoleSlots;
        consoleTable->SlotCount = entries;

        s_console = serveConsole ? (nint)Alloc(1) : 0;
        s_consoleTable = (nint)consoleTable;

        var rootSlots = Alloc(3);   // the root's forwarder table: Acquire, AcquireInterface, Release
        rootSlots[0] = (nint)(delegate* unmanaged<void*, byte*, void*>)&RootAcquire;
        rootSlots[2] = (nint)(delegate* unmanaged<void*, void>)&RootRelease;
        var rootTable = (ForwarderTableHeader*)Alloc(sizeof(ForwarderTableHeader) / sizeof(nint));
        rootTable->Slots = (void**)rootSlots;
        rootTable->SlotCount = 3;

        Assert.True(ShimRoot.TryBind(Alloc(1), rootTable, out var bound, out string why), why);
        return bound!;
    }

    private nint* Alloc(int words)
    {
        var p = (nint*)NativeMemory.AllocZeroed((nuint)(words * sizeof(nint)));
        _blocks.Add((nint)p);
        return p;
    }

    [UnmanagedCallersOnly]
    private static void* RootAcquire(void* self, byte* name)
    {
        string n = Marshal.PtrToStringUTF8((nint)name) ?? string.Empty;
        if (n == ShimConsoleFwd.TableName)
            return (void*)s_consoleTable;
        return n == ShimConsole.InterfaceVersion && s_console != 0 ? (void*)s_console : null;
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
