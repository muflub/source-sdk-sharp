using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The view area's pixel visibility slots (shell_pixelvis_client.cpp), one
/// crossing each.
/// </summary>
internal sealed unsafe class ClientPixelVisSlots(ViewClientArea viewClient) : IPixelVisSlots
{
    public int QueryCreate() => viewClient.PixelVisQueryCreate();

    public void QueryDestroy(int query) => viewClient.PixelVisQueryDestroy(query);

    public void QueryReset(int query) => viewClient.PixelVisQueryReset(query);

    public void QueryPixels(int visible, int count, out int visiblePixels, out int countPixels)
    {
        int* answer = stackalloc int[2] { -1, -1 };
        viewClient.PixelVisQueryPixels(visible, count, answer);
        visiblePixels = answer[0];
        countPixels = answer[1];
    }

    public void Diameters(ReadOnlySpan<float> spheres, Span<float> diameters)
    {
        int n = Math.Min(spheres.Length / 4, diameters.Length);
        diameters.Clear();
        if (n == 0)
            return;
        fixed (float* s = spheres)
        fixed (float* d = diameters)
            viewClient.PixelVisDiameters(s, n, d);
    }

    public CurrentViewState View()
    {
        float* v = stackalloc float[12];
        new Span<float>(v, 12).Clear();
        int id = viewClient.PixelVisView(v, 12);
        return new CurrentViewState(id, new Vector(v[0], v[1], v[2]), new Vector(v[3], v[4], v[5]),
                                    new Vector(v[6], v[7], v[8]), new Vector(v[9], v[10], v[11]));
    }
}
