using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The view a draw is happening in (with the render context's camera,
/// which a strip faces), and the facts about the screen a rope's widths read
/// (<c>ClientViewState</c>).</summary>
public readonly record struct ClientViewState(
    Vector Forward, Vector Origin, Vector MainOrigin, int ScreenWidth, int DxLevel, bool AAEnabled,
    Vector Camera);

/// <summary>One hull trace against the world (<c>ClientTraceHullWorld</c>).</summary>
public readonly record struct ClientHullTrace(float Fraction, bool AllSolid, bool StartSolid, Vector PlaneNormal, float PlaneDist);

/// <summary>
/// <c>engine-&gt;ComputeLighting( pos, NULL, true, colour )</c>: the light at
/// a point, averaged, and the six box colours when asked (IShimEffectsRenderClient).
/// </summary>
public interface IClientComputeLighting
{
    Vector ComputeLighting(in Vector pos, Span<Vector> boxColors);
}

/// <summary>
/// The native rope executor (shell_rope_render.cpp) and the reads a managed
/// rope makes of the engine. Game thread; <see cref="Draw"/> only from inside a
/// draw callback.
/// </summary>
public interface IClientRopeExecutor
{
    /// <summary>The handle for a material (and whether it has a "_back"
    /// partner); -1 when there is no name. The same name answers the same
    /// handle until the level ends.</summary>
    int Material(string name, out int textureHeight, out bool hasBack);

    /// <summary>Queues one strip for this view's rope pass. Both spans are the
    /// camera-facing vertices BeamSegDraw made, seventeen floats a vertex
    /// (beam_strip_vertex.h); the solid one is empty or the same length.</summary>
    void Draw(int material, ReadOnlySpan<float> vertices, ReadOnlySpan<float> solid, int flags);

    /// <summary>The view being drawn (only when <paramref name="insideView"/>:
    /// a draw callback), the main view, and the screen.</summary>
    ClientViewState View(bool insideView);

    /// <summary><c>engine-&gt;ComputeLighting</c>: the averaged colour, and the six box colours.</summary>
    Vector ComputeLighting(in Vector pos, Span<Vector> boxColors);

    /// <summary>A hull trace against the world only.</summary>
    ClientHullTrace TraceHullWorld(in Vector start, in Vector end, in Vector mins, in Vector maxs, int mask);
}

/// <summary>The real one, over the IShimEffectsRenderClient api.</summary>
internal sealed unsafe class ClientRopeExecutor(EngineSide api) : IClientRopeExecutor, IClientComputeLighting
{
    public int Material(string name, out int textureHeight, out bool hasBack)
    {
        textureHeight = 1;
        hasBack = false;

        if (!api.ClientAttached || string.IsNullOrEmpty(name))
            return -1;

        byte[] bytes = Encoding.UTF8.GetBytes(name + "\0");
        int height = 1, back = 0;
        int handle;

        fixed (byte* p = bytes)
            handle = api.ClientShims.RopeMaterial(p, &height, &back);

        textureHeight = height;
        hasBack = back != 0;
        return handle;
    }

    public void Draw(int material, ReadOnlySpan<float> vertices, ReadOnlySpan<float> solid, int flags)
    {
        // Seventeen floats a vertex, two a segment: at least two segments.
        if (!api.ClientAttached || vertices.Length < 4 * 17)
            return;

        fixed (float* pVerts = vertices)
        fixed (float* pSolid = solid)
            api.ClientShims.RopeDraw(material, pVerts, vertices.Length / 17, solid.Length == 0 ? null : pSolid, solid.Length / 17, flags);
    }

    public ClientViewState View(bool insideView)
    {
        if (!api.ClientAttached)
            return default;

        float* f = stackalloc float[12];
        new Span<float>(f, 12).Clear();
        int width = 0, dx = 0, aa = 0;

        api.ClientShims.ClientViewState(insideView ? 1 : 0, f, f + 3, f + 6, f + 9, &width, &dx, &aa);

        return new ClientViewState(new Vector(f[0], f[1], f[2]), new Vector(f[3], f[4], f[5]),
                                   new Vector(f[6], f[7], f[8]), width, dx, aa != 0,
                                   new Vector(f[9], f[10], f[11]));
    }

    public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors)
    {
        if (!api.ClientAttached)
            return default;

        float* p = stackalloc float[3] { pos.X, pos.Y, pos.Z };
        float* color = stackalloc float[3];
        float* box = stackalloc float[18];

        api.ClientShims.ClientComputeLighting(p, color, box);

        for (int i = 0; i < 6 && i < boxColors.Length; i++)
            boxColors[i] = new Vector(box[i * 3], box[i * 3 + 1], box[i * 3 + 2]);

        return new Vector(color[0], color[1], color[2]);
    }

    public ClientHullTrace TraceHullWorld(in Vector start, in Vector end, in Vector mins, in Vector maxs, int mask)
    {
        if (!api.ClientAttached)
            return new ClientHullTrace(1f, false, false, default, 0f);

        float* v = stackalloc float[12]
        {
            start.X, start.Y, start.Z, end.X, end.Y, end.Z,
            mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z,
        };
        float* plane = stackalloc float[4];
        int solid = 0;

        float fraction = api.ClientShims.ClientTraceHullWorld(v, v + 3, v + 6, v + 9, mask, &solid, plane);

        return new ClientHullTrace(fraction, (solid & 1) != 0, (solid & 2) != 0,
                                   new Vector(plane[0], plane[1], plane[2]), plane[3]);
    }
}
