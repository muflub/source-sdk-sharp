using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared.Interop;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's own entry (plan_side_entries SE2). The Bootstrap's ClientEntry
/// makes the client's load context and loads the mod's listed assemblies, then
/// resolves THIS type and its four methods by their fixed names on every load
/// and calls them through function pointers, with raw arguments only. No side
/// travels as an argument: this type IS the client's entry, and hands
/// <see cref="SideLoad"/> its side as a constant. Each hands
/// <see cref="SideLoad"/> its own crossing op first, which counts it on the
/// side's <see cref="SideState"/> (statics to zero: there is no static table).
/// </summary>
public static unsafe class ClientSideEntry
{
    [UnmanagedCallersOnly]
    public static int LoadClient(void* pShimRoot, void* pShimRootTable, void** ppManagedRoot,
                           nint* pVtables, nint* pSession, byte* pFacts)
    {
        return SideLoad.Load(Side.Client, CrossingOp.LoadClient, pShimRoot, pShimRootTable, ppManagedRoot, pVtables, pSession, pFacts);
    }

    [UnmanagedCallersOnly]
    public static int StampedClient(nint session, byte* pFacts)
    {
        return SideLoad.Stamped(CrossingOp.StampedClient, session, pFacts);
    }

    [UnmanagedCallersOnly]
    public static int ReloadCheckClient(nint session, byte* pWhy, int nWhy)
    {
        return SideLoad.ReloadCheck(CrossingOp.ReloadCheckClient, session, pWhy, nWhy);
    }

    [UnmanagedCallersOnly]
    public static int UnloadClient(nint session)
    {
        return SideLoad.Unload(CrossingOp.UnloadClient, session);
    }
}
