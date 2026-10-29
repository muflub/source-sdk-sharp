using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The standalone area's surrounding-bounds batch
/// (<c>SourceSharp_StandaloneSurroundingBounds</c>), one crossing per call.
/// </summary>
internal sealed unsafe class ClientStandaloneRenderBounds(IStandaloneClientArea standalone) : IStandaloneRenderBounds
{
    public int SurroundingBounds(ReadOnlySpan<uint> handles, Span<float> minsMaxs)
    {
        int n = Math.Min(handles.Length, minsMaxs.Length / 6);
        if (n == 0)
            return 0;
        fixed (uint* h = handles)
        fixed (float* box = minsMaxs)
            return standalone.StandaloneSurroundingBounds(h, n, box);
    }
}
