using System.Runtime.InteropServices;

namespace SourceSharp.Bootstrap;

/// <summary>
/// The client's entries from native (plan_side_entries SE1): SourceSharp_LoadClient,
/// SourceSharp_ReloadCheckClient and SourceSharp_UnloadClient resolve these three
/// by name. No side travels as an argument: this type IS the client's entry.
/// Since SE2 they make the client's load context and load its assemblies; the
/// rest is the client's own entry (SourceSharp.Client.Interop.ClientSideEntry),
/// which counts the crossings.
/// </summary>
public static unsafe class ClientEntry
{
    /// <summary>The client's state between native's calls.</summary>
    private static readonly BootSide s_Side = BootSide.ForClient();

    [UnmanagedCallersOnly]
    public static int LoadClient(void* pShimRoot, void* pShimRootTable, void** ppManagedRoot)
        => Entry.LoadSide(s_Side, pShimRoot, pShimRootTable, ppManagedRoot);

    [UnmanagedCallersOnly]
    public static int ReloadCheckClient(byte* pWhy, int nWhy)
        => Entry.ReloadCheck(s_Side, pWhy, nWhy);

    [UnmanagedCallersOnly]
    public static int UnloadClient()
        => Entry.UnloadSide(s_Side);
}
