using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Bootstrap;

/// <summary>
/// The Bootstrap's own console (plan_side_entries SE2): a raw writer over the
/// shim root native hands a side's load, so the Bootstrap's lines reach
/// console.log without the Bootstrap naming an Abi type.
///
/// <para>
/// The root's vtable slot <see cref="RootAcquireSlot"/> is
/// <c>Acquire(name, &amp;entries)</c>; it is asked for
/// <see cref="InterfaceVersion"/>, whose vtable has <c>Msg</c> at
/// <see cref="MsgSlot"/> and <c>Warning</c> at <see cref="WarningSlot"/>.
/// BootConsoleTests pins those numbers to VtableLayouts.g.cs and to
/// ShimConsole. With no root, or a root that does not serve the console, the
/// lines go to stderr instead.
/// </para>
/// </summary>
internal sealed unsafe class BootConsole
{
    internal const string InterfaceVersion = "SourceSharpConsole001";
    internal const int RootAcquireSlot = 0;
    internal const int MsgSlot = 0;
    internal const int WarningSlot = 1;

    private readonly void* _self;

    private BootConsole(void* self) => _self = self;

    /// <summary>Whether lines reach the game's console rather than stderr.</summary>
    public bool Bound => _self != null;

    /// <summary>The console stderr stands in for.</summary>
    public static BootConsole Stderr() => new(null);

    /// <summary>The console served by <paramref name="pShimRoot"/>, or stderr's.</summary>
    public static BootConsole Acquire(void* pShimRoot, int shimRootEntries)
    {
        if (pShimRoot == null || shimRootEntries <= RootAcquireSlot)
            return Stderr();

        var acquire = (delegate* unmanaged<void*, byte*, int*, void*>)(*(void***)pShimRoot)[RootAcquireSlot];
        int entries = 0;
        void* self;
        fixed (byte* name = Utf8(InterfaceVersion))
            self = acquire(pShimRoot, name, &entries);

        return self == null || entries <= WarningSlot ? Stderr() : new BootConsole(self);
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

        var write = (delegate* unmanaged<void*, byte*, void>)(*(void***)_self)[slot];
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
