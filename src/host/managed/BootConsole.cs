using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Bootstrap;

/// <summary>
/// The Bootstrap's own console (plan_side_entries SE2): a raw writer over the
/// shim root native hands a side's load, so the Bootstrap's lines reach
/// console.log without the Bootstrap naming an Abi type.
///
/// <para>
/// Both calls go through forwarder tables (plan_forwarders_everywhere.md), each
/// an <c>SSForwarderTable</c>: the slot array, then its count. Slot
/// <see cref="RootAcquireSlot"/> of the root's table, which native hands over
/// beside the root, is <c>Acquire(root, name, &amp;entries)</c>; it is asked for
/// <see cref="InterfaceVersion"/> and for <see cref="ConsoleTable"/>, whose
/// slots <see cref="MsgSlot"/> and <see cref="WarningSlot"/> are <c>Msg</c> and
/// <c>Warning</c>. BootConsoleTests pins those numbers to the generated
/// forwarder structs and to ShimConsole. With no root, or a root that does not
/// serve the console, the lines go to stderr instead.
/// </para>
/// </summary>
internal sealed unsafe class BootConsole
{
    internal const string InterfaceVersion = "SourceSharpConsole001";
    internal const string ConsoleTable = "SSFwd_ShimConsole";
    internal const int RootAcquireSlot = 0;
    internal const int MsgSlot = 0;
    internal const int WarningSlot = 1;

    /// <summary>An SSForwarderTable: the slot array, then its count.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Table
    {
        public void** Slots;
        public int Count;
    }

    private readonly void* _self;
    private readonly void** _slots;

    private BootConsole(void* self, void** slots)
    {
        _self = self;
        _slots = slots;
    }

    /// <summary>Whether lines reach the game's console rather than stderr.</summary>
    public bool Bound => _self != null;

    /// <summary>The console stderr stands in for.</summary>
    public static BootConsole Stderr() => new(null, null);

    /// <summary>The console served by <paramref name="pShimRoot"/>, called through <paramref name="pShimRootTable"/>; or stderr's.</summary>
    public static BootConsole Acquire(void* pShimRoot, void* pShimRootTable)
    {
        var root = (Table*)pShimRootTable;
        if (pShimRoot == null || root == null || root->Slots == null || root->Count <= RootAcquireSlot)
            return Stderr();

        var acquire = (delegate* unmanaged<void*, byte*, int*, void*>)root->Slots[RootAcquireSlot];
        int entries = 0;
        void* self;
        Table* console;
        fixed (byte* name = Utf8(InterfaceVersion))
            self = acquire(pShimRoot, name, &entries);
        fixed (byte* name = Utf8(ConsoleTable))
            console = (Table*)acquire(pShimRoot, name, &entries);

        return self == null || console == null || console->Slots == null || console->Count <= WarningSlot
            ? Stderr()
            : new BootConsole(self, console->Slots);
    }

    public void Msg(string text) => Write(MsgSlot, text);

    public void Warning(string text) => Write(WarningSlot, text);

    private void Write(int slot, string text)
    {
        if (_self == null)
        {
            Console.Error.Write(text);
            return;
        }

        var write = (delegate* unmanaged<void*, byte*, void>)_slots[slot];
        fixed (byte* p = Utf8(text))
            write(_self, p);
    }

    private static byte[] Utf8(string text)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        Encoding.UTF8.GetBytes(text, bytes);
        return bytes;
    }
}
