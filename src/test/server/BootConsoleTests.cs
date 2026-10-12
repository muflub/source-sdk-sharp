using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Bootstrap;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Bootstrap's raw console (plan_side_entries SE2) reaches the same slots
/// the generated layouts name, without the Bootstrap naming them.
/// </summary>
public sealed unsafe class BootConsoleTests
{
    private static int Slot(Type vtbl, string field) => (int)Marshal.OffsetOf(vtbl, field) / sizeof(nint);

    [Fact]
    public void ItsSlotsAreTheGeneratedLayouts()
    {
        Assert.Equal(BootConsole.RootAcquireSlot, Slot(typeof(ShimRootVtbl), nameof(ShimRootVtbl.Acquire)));
        Assert.Equal(BootConsole.MsgSlot, Slot(typeof(ShimConsoleVtbl), nameof(ShimConsoleVtbl.Msg)));
        Assert.Equal(BootConsole.WarningSlot, Slot(typeof(ShimConsoleVtbl), nameof(ShimConsoleVtbl.Warning)));
    }

    [Fact]
    public void ItAsksForTheShimConsolesInterface()
    {
        Assert.Equal(ShimConsole.InterfaceVersion, BootConsole.InterfaceVersion);
    }

    /// <summary>A console object: its vtable, then a handle to the lines it was given.</summary>
    private sealed class Lines
    {
        public readonly List<string> Written = new();
    }

    [UnmanagedCallersOnly]
    private static void Msg(void* self, byte* text) => Record(self, "msg", text);

    [UnmanagedCallersOnly]
    private static void Warning(void* self, byte* text) => Record(self, "warning", text);

    private static void Record(void* self, string kind, byte* text)
    {
        var lines = (Lines)GCHandle.FromIntPtr(((nint*)self)[1]).Target!;
        lines.Written.Add(kind + ":" + Marshal.PtrToStringUTF8((nint)text));
    }

    [Fact]
    public void ItsLinesReachTheConsoleTheRootServes()
    {
        var lines = new Lines();
        GCHandle handle = GCHandle.Alloc(lines);
        nint* vtbl = (nint*)NativeMemory.AllocZeroed((nuint)(sizeof(ShimConsoleVtbl)));
        nint* console = (nint*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(nint)));
        try
        {
            vtbl[Slot(typeof(ShimConsoleVtbl), nameof(ShimConsoleVtbl.Msg))] = (nint)(delegate* unmanaged<void*, byte*, void>)&Msg;
            vtbl[Slot(typeof(ShimConsoleVtbl), nameof(ShimConsoleVtbl.Warning))] = (nint)(delegate* unmanaged<void*, byte*, void>)&Warning;
            console[0] = (nint)vtbl;
            console[1] = GCHandle.ToIntPtr(handle);

            using var root = new FakeShimRoot().Area(ShimConsole.InterfaceVersion, console);
            BootConsole boot = BootConsole.Acquire(root.Raw(), ShimRoot.ExpectedEntries);

            Assert.True(boot.Bound);
            boot.Msg("[SourceSharp] one\n");
            boot.Warning("[SourceSharp] two\n");
            Assert.Equal(["msg:[SourceSharp] one\n", "warning:[SourceSharp] two\n"], lines.Written);
        }
        finally
        {
            NativeMemory.Free(console);
            NativeMemory.Free(vtbl);
            handle.Free();
        }
    }

    [Fact]
    public void WithoutARootItWritesToStderr()
    {
        Assert.False(BootConsole.Acquire(null, ShimRoot.ExpectedEntries).Bound);
        using var root = new FakeShimRoot();
        Assert.False(BootConsole.Acquire(root.Raw(), 0).Bound);
        Assert.True(BootConsole.Acquire(root.Raw(), ShimRoot.ExpectedEntries).Bound);   // the control
    }
}
