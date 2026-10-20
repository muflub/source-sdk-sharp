using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real one, over the IShimEffectsRenderClient api.</summary>
internal sealed unsafe class ClientRopeExecutor(IEffectsRenderClientArea effectsRenderClient) : IClientRopeExecutor, IClientComputeLighting
{
    public int Material(string name, out int textureHeight, out bool hasBack)
    {
        textureHeight = 1;
        hasBack = false;

        if (string.IsNullOrEmpty(name))
            return -1;

        byte[] bytes = Encoding.UTF8.GetBytes(name + "\0");
        int height = 1, back = 0;
        int handle;

        fixed (byte* p = bytes)
            handle = effectsRenderClient.RopeMaterial(p, &height, &back);

        textureHeight = height;
        hasBack = back != 0;
        return handle;
    }

    public void Draw(int material, ReadOnlySpan<float> vertices, ReadOnlySpan<float> solid, int flags)
    {
        // Seventeen floats a vertex, two a segment: at least two segments.
        if (vertices.Length < 4 * 17)
            return;

        fixed (float* pVerts = vertices)
        fixed (float* pSolid = solid)
            effectsRenderClient.RopeDraw(material, pVerts, vertices.Length / 17, solid.Length == 0 ? null : pSolid, solid.Length / 17, flags);
    }

    public ClientViewState View(bool insideView)
    {
        float* f = stackalloc float[12];
        new Span<float>(f, 12).Clear();
        int width = 0, dx = 0, aa = 0;

        effectsRenderClient.ClientViewState(insideView ? 1 : 0, f, f + 3, f + 6, f + 9, &width, &dx, &aa);

        return new ClientViewState(new Vector(f[0], f[1], f[2]), new Vector(f[3], f[4], f[5]),
                                   new Vector(f[6], f[7], f[8]), width, dx, aa != 0,
                                   new Vector(f[9], f[10], f[11]));
    }

    public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors)
    {
        float* p = stackalloc float[3] { pos.X, pos.Y, pos.Z };
        float* color = stackalloc float[3];
        float* box = stackalloc float[18];

        effectsRenderClient.ClientComputeLighting(p, color, box);

        for (int i = 0; i < 6 && i < boxColors.Length; i++)
            boxColors[i] = new Vector(box[i * 3], box[i * 3 + 1], box[i * 3 + 2]);

        return new Vector(color[0], color[1], color[2]);
    }

    public ClientHullTrace TraceHullWorld(in Vector start, in Vector end, in Vector mins, in Vector maxs, int mask)
    {
        float* v = stackalloc float[12]
        {
            start.X, start.Y, start.Z, end.X, end.Y, end.Z,
            mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z,
        };
        float* plane = stackalloc float[4];
        int solid = 0;

        float fraction = effectsRenderClient.ClientTraceHullWorld(v, v + 3, v + 6, v + 9, mask, &solid, plane);

        return new ClientHullTrace(fraction, (solid & 1) != 0, (solid & 2) != 0,
                                   new Vector(plane[0], plane[1], plane[2]), plane[3]);
    }
}
