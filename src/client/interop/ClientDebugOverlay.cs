using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

internal sealed unsafe class ClientDebugOverlay(UiClientArea? uiClient) : IClientDebugOverlay
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private UiClientArea UiClient => uiClient ?? throw ShimAreaGuard.NotAttached(nameof(UiClient));

    public int Text(int index, out float x, out float y, out int line, out uint rgba, Span<byte> text)
    {
        float fx = 0, fy = 0;
        int l = 0;
        uint c = 0;
        int r;
        fixed (byte* p = text)
            r = UiClient.UiDebugOverlayText(index, &fx, &fy, &l, &c, p, text.Length);
        (x, y, line, rgba) = (fx, fy, l, c);
        return r;
    }

    public void ClearDead() => UiClient.UiDebugOverlayClearDead();
}
