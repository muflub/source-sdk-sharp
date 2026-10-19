using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real one, over the IShimEffectsRenderClient api.</summary>
internal sealed unsafe class ClientRopeExecutor(EffectsRenderClientArea? effectsRenderClient) : IClientRopeExecutor, IClientComputeLighting
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private EffectsRenderClientArea EffectsRenderClient => effectsRenderClient ?? throw ShimAreaGuard.NotAttached(nameof(EffectsRenderClient));

    public int Material(string name, out int textureHeight, out bool hasBack)
    {
        textureHeight = 1;
        hasBack = false;

        if (effectsRenderClient is null || string.IsNullOrEmpty(name))
            return -1;

        byte[] bytes = Encoding.UTF8.GetBytes(name + "\0");
        int height = 1, back = 0;
        int handle;

        fixed (byte* p = bytes)
            handle = EffectsRenderClient.RopeMaterial(p, &height, &back);

        textureHeight = height;
        hasBack = back != 0;
        return handle;
    }

    public void Draw(int material, ReadOnlySpan<float> vertices, ReadOnlySpan<float> solid, int flags)
    {
        // Seventeen floats a vertex, two a segment: at least two segments.
        if (effectsRenderClient is null || vertices.Length < 4 * 17)
            return;

        fixed (float* pVerts = vertices)
        fixed (float* pSolid = solid)
            EffectsRenderClient.RopeDraw(material, pVerts, vertices.Length / 17, solid.Length == 0 ? null : pSolid, solid.Length / 17, flags);
    }

    public ClientViewState View(bool insideView)
    {
        if (effectsRenderClient is null)
            return default;

        float* f = stackalloc float[12];
        new Span<float>(f, 12).Clear();
        int width = 0, dx = 0, aa = 0;

        EffectsRenderClient.ClientViewState(insideView ? 1 : 0, f, f + 3, f + 6, f + 9, &width, &dx, &aa);

        return new ClientViewState(new Vector(f[0], f[1], f[2]), new Vector(f[3], f[4], f[5]),
                                   new Vector(f[6], f[7], f[8]), width, dx, aa != 0,
                                   new Vector(f[9], f[10], f[11]));
    }

    public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors)
    {
        if (effectsRenderClient is null)
            return default;

        float* p = stackalloc float[3] { pos.X, pos.Y, pos.Z };
        float* color = stackalloc float[3];
        float* box = stackalloc float[18];

        EffectsRenderClient.ClientComputeLighting(p, color, box);

        for (int i = 0; i < 6 && i < boxColors.Length; i++)
            boxColors[i] = new Vector(box[i * 3], box[i * 3 + 1], box[i * 3 + 2]);

        return new Vector(color[0], color[1], color[2]);
    }

    public ClientHullTrace TraceHullWorld(in Vector start, in Vector end, in Vector mins, in Vector maxs, int mask)
    {
        if (effectsRenderClient is null)
            return new ClientHullTrace(1f, false, false, default, 0f);

        float* v = stackalloc float[12]
        {
            start.X, start.Y, start.Z, end.X, end.Y, end.Z,
            mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z,
        };
        float* plane = stackalloc float[4];
        int solid = 0;

        float fraction = EffectsRenderClient.ClientTraceHullWorld(v, v + 3, v + 6, v + 9, mask, &solid, plane);

        return new ClientHullTrace(fraction, (solid & 1) != 0, (solid & 2) != 0,
                                   new Vector(plane[0], plane[1], plane[2]), plane[3]);
    }
}
