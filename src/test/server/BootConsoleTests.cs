using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Bootstrap;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Bootstrap's raw console (plan_side_entries SE2) reaches the same slots
/// the generated forwarder structs name (plan_forwarders_everywhere.md),
/// without the Bootstrap naming them.
/// </summary>
public sealed unsafe class BootConsoleTests
{
    private static int Slot(Type fwd, string field) => (int)Marshal.OffsetOf(fwd, field) / sizeof(nint);

    [Fact]
    public void ItsSlotsAreTheGeneratedForwarderStructs()
    {
        Assert.Equal(BootConsole.RootAcquireSlot, Slot(typeof(ShimRootFwd), nameof(ShimRootFwd.Acquire)));
        Assert.Equal(BootConsole.MsgSlot, Slot(typeof(ShimConsoleFwd), nameof(ShimConsoleFwd.Msg)));
        Assert.Equal(BootConsole.WarningSlot, Slot(typeof(ShimConsoleFwd), nameof(ShimConsoleFwd.Warning)));
    }

    [Fact]
    public void ItAsksForTheShimConsolesInterfaceAndTable()
    {
        Assert.Equal(ShimConsole.InterfaceVersion, BootConsole.InterfaceVersion);
        Assert.Equal(BootConsole.ConsoleTable, ShimConsoleFwd.TableName);
    }

    /// <summary>What a console object, whose second word is a handle to this, was given.</summary>
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
        nint* slots = (nint*)NativeMemory.AllocZeroed((nuint)(sizeof(ShimConsoleFwd)));
        nint* console = (nint*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(nint)));
        try
        {
            slots[Slot(typeof(ShimConsoleFwd), nameof(ShimConsoleFwd.Msg))] = (nint)(delegate* unmanaged<void*, byte*, void>)&Msg;
            slots[Slot(typeof(ShimConsoleFwd), nameof(ShimConsoleFwd.Warning))] = (nint)(delegate* unmanaged<void*, byte*, void>)&Warning;
            console[1] = GCHandle.ToIntPtr(handle);

            using var root = new FakeShimRoot().Area(ShimConsole.InterfaceVersion, console, slots);
            BootConsole boot = BootConsole.Acquire(root.Raw(), root.RawTable());

            Assert.True(boot.Bound);
            boot.Msg("[SourceSharp] one\n");
            boot.Warning("[SourceSharp] two\n");
            Assert.Equal(["msg:[SourceSharp] one\n", "warning:[SourceSharp] two\n"], lines.Written);
        }
        finally
        {
            NativeMemory.Free(console);
            NativeMemory.Free(slots);
            handle.Free();
        }
    }

    [Fact]
    public void WithoutARootItWritesToStderr()
    {
        using var root = new FakeShimRoot();
        Assert.False(BootConsole.Acquire(null, root.RawTable()).Bound);
        Assert.False(BootConsole.Acquire(root.Raw(), null).Bound);
        Assert.True(BootConsole.Acquire(root.Raw(), root.RawTable()).Bound);   // the control
    }
}
