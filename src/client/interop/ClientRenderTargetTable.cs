using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real store, over the view area's <c>RenderTargetDeclare</c>.</summary>
internal sealed unsafe class ClientRenderTargetTable(ViewClientArea viewClient) : IRenderTargetTable
{
    public bool Declare(int slot, string name, string group, bool complain, int creationFlags, int releaseOrder)
    {
        byte[] n = Vtable.Utf8(name);
        byte[] g = Vtable.Utf8(group);
        fixed (byte* pn = n)
        fixed (byte* pg = g)
            return viewClient.RenderTargetDeclare(slot, pn, pg, complain ? 1 : 0, creationFlags, releaseOrder) != 0;
    }
}
