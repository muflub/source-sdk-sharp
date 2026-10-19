using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

internal sealed unsafe class ClientDebugOverlay(EngineSide api) : IClientDebugOverlay
{
    public int Text(int index, out float x, out float y, out int line, out uint rgba, Span<byte> text)
    {
        float fx = 0, fy = 0;
        int l = 0;
        uint c = 0;
        int r;
        fixed (byte* p = text)
            r = api.ClientShims.UiDebugOverlayText(index, &fx, &fy, &l, &c, p, text.Length);
        (x, y, line, rgba) = (fx, fy, l, c);
        return r;
    }

    public void ClearDead() => api.ClientShims.UiDebugOverlayClearDead();
}
