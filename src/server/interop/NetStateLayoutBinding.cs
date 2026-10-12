using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// A net state's field layout as the server's syncs resolve it, managed-built
/// (plan_engineapi_vtables E16). These are the Entry.cs thunks' bodies, moved.
/// The declarations are process-wide (StandaloneNetStates), so the thunks
/// marked no side and neither does this. A failure answers -1 ("not
/// published"), and the generated catch would answer 0, a real offset, so each
/// keeps its own catch.
/// </summary>
public sealed unsafe class NetStateLayoutBinding(Crossings crossings, ILog log, NetStateCatalog catalog) : IManagedNetState
{
    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    public int NetStateFieldOffset(byte* a0, byte* a1)
    {
        crossings.Count(CrossingOp.ServerNetStateFieldOffset);
        try { return catalog.FieldOffset(Utf8(a0), Utf8(a1)); }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedNetState.NetStateFieldOffset", e);
            return -1;
        }
    }

    public int NetStateFieldBits(byte* a0, byte* a1)
    {
        crossings.Count(CrossingOp.ServerNetStateFieldBits);
        try { return catalog.FieldBits(Utf8(a0), Utf8(a1)); }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedNetState.NetStateFieldBits", e);
            return -1;
        }
    }
}
