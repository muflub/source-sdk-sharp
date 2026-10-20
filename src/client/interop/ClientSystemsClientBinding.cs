using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="IManagedClientSystemsClient"/>, managed-built: a kClientSystems[]
/// row's adapter (shell_managed_clientsystems_client.cpp) asks the managed
/// system of the row's name for one stage. The body counts its crossing first
/// and keeps its own catch; a failure answers -1, which refuses nothing.
/// </summary>
public sealed unsafe class ClientSystemsClientBinding(Crossings crossings, ILog log, IClientSystemRows rows)
    : IManagedClientSystemsClient
{
    public int Stage(byte* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientSystemStage);
        try
        {
            string? name = a0 == null ? null : Marshal.PtrToStringUTF8((IntPtr)a0);
            return name is null ? -1 : rows.Stage(name, a1);
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedClientSystemsClient.Stage", e);
            return -1;
        }
    }
}

/// <summary>The real layout, over the UI area's <c>ClientModeLayout</c>.</summary>
internal sealed class ClientModeLayoutSlot(IUiClientArea uiClient) : IClientModeLayout
{
    public void Layout() => uiClient.ClientModeLayout();
}
