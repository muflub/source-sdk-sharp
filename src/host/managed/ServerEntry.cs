using System.Runtime.InteropServices;

namespace SourceSharp.Bootstrap;

/// <summary>
/// The server's entries from native (plan_side_entries SE1): SourceSharp_LoadServer,
/// SourceSharp_ReloadCheckServer and SourceSharp_UnloadServer resolve these three
/// by name. No side travels as an argument: this type IS the server's entry.
/// Since SE2 they make the server's load context and load its assemblies; the
/// rest is the server's own entry (SourceSharp.Server.Interop.ServerSideEntry),
/// which counts the crossings.
/// </summary>
public static unsafe class ServerEntry
{
    /// <summary>The server's state between native's calls.</summary>
    private static readonly BootSide s_Side = BootSide.ForServer();

    [UnmanagedCallersOnly]
    public static int LoadServer(void* pShimRoot, int shimRootEntries, void** ppManagedRoot)
        => Entry.LoadSide(s_Side, pShimRoot, shimRootEntries, ppManagedRoot);

    [UnmanagedCallersOnly]
    public static int ReloadCheckServer(byte* pWhy, int nWhy)
        => Entry.ReloadCheck(s_Side, pWhy, nWhy);

    [UnmanagedCallersOnly]
    public static int UnloadServer()
        => Entry.UnloadSide(s_Side);
}
