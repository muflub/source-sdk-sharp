using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared.Interop;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's own entry (plan_side_entries SE2). The Bootstrap's ServerEntry
/// makes the server's load context and loads the mod's listed assemblies, then
/// resolves THIS type and its four methods by their fixed names on every load
/// and calls them through function pointers, with raw arguments only. No side
/// travels as an argument: this type IS the server's entry, and hands
/// <see cref="SideLoad"/> its side as a constant. Each hands
/// <see cref="SideLoad"/> its own crossing op first, which counts it on the
/// side's <see cref="SideState"/> (statics to zero: there is no static table).
/// </summary>
public static unsafe class ServerSideEntry
{
    [UnmanagedCallersOnly]
    public static int LoadServer(void* pShimRoot, int shimRootEntries, void** ppManagedRoot,
                           nint* pVtables, nint* pSession, byte* pFacts)
    {
        return SideLoad.Load(Side.Server, CrossingOp.LoadServer, pShimRoot, shimRootEntries, ppManagedRoot, pVtables, pSession, pFacts);
    }

    [UnmanagedCallersOnly]
    public static int StampedServer(nint session, byte* pFacts)
    {
        return SideLoad.Stamped(CrossingOp.StampedServer, session, pFacts);
    }

    [UnmanagedCallersOnly]
    public static int ReloadCheckServer(nint session, byte* pWhy, int nWhy)
    {
        return SideLoad.ReloadCheck(CrossingOp.ReloadCheckServer, session, pWhy, nWhy);
    }

    [UnmanagedCallersOnly]
    public static int UnloadServer(nint session)
    {
        return SideLoad.Unload(CrossingOp.UnloadServer, session);
    }
}
