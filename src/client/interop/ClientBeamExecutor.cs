using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The native beam executor (shell_beam_render.cpp) and the engine reads the
/// managed beam system makes. Game thread; the draw calls only from inside a
/// beam renderable's draw (<see cref="IClientBeamCallbacks.Draw"/>).
/// </summary>
public interface IClientBeamExecutor
{
    /// <summary>The view basis (only when <paramref name="insideView"/>), the
    /// main view's up and right, the world-to-screen matrix, the clock and
    /// (only when <paramref name="insideView"/>) the render context's camera: 42
    /// floats in BeamView's order (origin, forward, right, up, angles, main up,
    /// main right, matrix, curtime, frametime, camera).</summary>
    void View(bool insideView, Span<float> out42);

    /// <summary>Whether a model index resolves, and its frame count.</summary>
    bool ModelInfo(int modelIndex, out int frameCount);

    /// <summary><c>modelinfo-&gt;GetModelIndex( name )</c>.</summary>
    int ModelIndex(string name);

    /// <summary>The handle of the entity at an entry, or <see cref="EntityHandle.None"/>.</summary>
    EntityHandle EntityAt(int entry);

    /// <summary>Whether an entity resolves and has a model.</summary>
    bool EntityHasModel(EntityHandle entity);

    /// <summary>
    /// What an entity endpoint's position is decided from: the BeamEntityFact
    /// bits (resolved, SDK, renderable, player, attachment answered, has a
    /// collideable), and in <paramref name="out9"/> the attachment (asked only
    /// when <paramref name="tryAttachment"/>), the collision centre and the
    /// render origin.
    /// </summary>
    int EntityFacts(EntityHandle entity, int attachment, bool tryAttachment, Span<float> out9);

    /// <summary><c>IsBoxVisible</c> and <c>CullBox</c> together.</summary>
    void BoxVisibility(in Vector mins, in Vector maxs, out bool visible, out bool culled);

    /// <summary>The halo's sight test (stock's handle-less pixel visibility).</summary>
    float HaloVisibility(in Vector origin, float proxySize);

    /// <summary><c>debugoverlay-&gt;ScreenPosition</c>.</summary>
    Vector ScreenPosition(in Vector point);

    /// <summary>A translucent leaf renderable at an origin with a box; its index.</summary>
    int RenderableAdd(in Vector origin, in Vector mins, in Vector maxs);

    /// <summary>The renderable moved or grew.</summary>
    void RenderableChanged(int renderable, in Vector origin, in Vector mins, in Vector maxs);

    /// <summary>The renderable goes.</summary>
    void RenderableRemove(int renderable);

    /// <summary>Binds a sprite model's material (or the wireframe one).</summary>
    bool BindSprite(int modelIndex, int frame, int renderMode, bool wireframe);

    /// <summary>Sets <c>$hdrcolorscale</c> on a sprite's material for a render mode.</summary>
    void SetHdrColorScale(int modelIndex, int renderMode, float scale);

    /// <summary>Whether a sprite has a glow material, or (<paramref name="bind"/>) binds it.</summary>
    bool Glow(int modelIndex, bool bind);

    /// <summary>One camera-facing strip: the vertices BeamSegDraw made,
    /// seventeen floats a vertex (beam_strip_vertex.h).</summary>
    void Strip(ReadOnlySpan<float> vertices);

    /// <summary>One dynamic mesh: nine floats a vertex.</summary>
    void Mesh(int primitive, int primitives, int colorKind, ReadOnlySpan<float> vertices);
}

/// <summary>What native's beam calls reach (IManagedBeamsClient's bodies).</summary>
public interface IClientBeamCallbacks
{
    void Lifecycle(int op, int arg);
    void Update(float curTime, float frameTime);
    void KillDead(EntityHandle entity, float curTime);
    bool Create(int kind, ReadOnlySpan<int> ints, ReadOnlySpan<float> floats, string? model, string? halo, float curTime);
    void Draw(int renderable);
}

/// <summary>
/// The managed-built IManagedBeamsClient: native's `beams` forwards here. The
/// payload widths are shell_beam_render.cpp's ForwardBeamInfo.
/// </summary>
public sealed unsafe class BeamsClientBinding(Func<IClientBeamCallbacks?> target) : IManagedBeamsClient
{
    /// <summary>BeamInfo_t's ints and floats, as ForwardBeamInfo packs them.</summary>
    public const int Ints = 11, Floats = 23;

    public void BeamsLifecycle(int a0, int a1) => target()?.Lifecycle(a0, a1);

    public void BeamsUpdate(float a0, float a1) => target()?.Update(a0, a1);

    public void BeamsKillDead(uint a0, float a1) => target()?.KillDead(new EntityHandle(a0), a1);

    public int BeamsCreate(int a0, void* a1, void* a2, byte* a3, byte* a4, float a5)
    {
        IClientBeamCallbacks? t = target();
        if (t is null || a1 == null || a2 == null)
            return 0;

        return t.Create(a0, new ReadOnlySpan<int>(a1, Ints), new ReadOnlySpan<float>(a2, Floats),
                        Utf8(a3), Utf8(a4), a5) ? 1 : 0;
    }

    public void BeamsDraw(int a0) => target()?.Draw(a0);

    private static string? Utf8(byte* p)
    {
        if (p == null)
            return null;
        int n = 0;
        while (p[n] != 0)
            n++;
        return Encoding.UTF8.GetString(p, n);
    }
}

/// <summary>The real executor, over the IShimEffectsRenderClient api.</summary>
internal sealed unsafe class ClientBeamExecutor(EngineSide api) : IClientBeamExecutor
{
    public void View(bool insideView, Span<float> out42)
    {
        out42.Clear();
        if (!api.ClientAttached || out42.Length < 42)
            return;
        fixed (float* p = out42)
            api.ClientShims.BeamView(insideView ? 1 : 0, p);
    }

    public bool ModelInfo(int modelIndex, out int frameCount)
    {
        frameCount = 0;
        if (!api.ClientAttached)
            return false;
        int n = 0;
        bool exists = api.ClientShims.BeamModelInfo(modelIndex, &n) != 0;
        frameCount = n;
        return exists;
    }

    public int ModelIndex(string name)
    {
        if (!api.ClientAttached)
            return -1;
        byte[] bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return api.ClientShims.BeamModelIndex(p);
    }

    public EntityHandle EntityAt(int entry)
        => api.ClientAttached ? new EntityHandle(api.ClientShims.BeamEntityAt(entry)) : EntityHandle.None;

    public bool EntityHasModel(EntityHandle entity)
        => api.ClientAttached && api.ClientShims.BeamEntityHasModel(entity.Value) != 0;

    public int EntityFacts(EntityHandle entity, int attachment, bool tryAttachment, Span<float> out9)
    {
        out9.Clear();
        if (!api.ClientAttached || out9.Length < 9)
            return 0;
        fixed (float* p = out9)
            return api.ClientShims.BeamEntityFacts(entity.Value, attachment, tryAttachment ? 1 : 0, p);
    }

    public void BoxVisibility(in Vector mins, in Vector maxs, out bool visible, out bool culled)
    {
        visible = false;
        culled = true;
        if (!api.ClientAttached)
            return;
        float* v = stackalloc float[6] { mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        int bits = api.ClientShims.BeamBoxVisibility(v, v + 3);
        visible = (bits & 1) != 0;
        culled = (bits & 2) != 0;
    }

    public float HaloVisibility(in Vector origin, float proxySize)
    {
        if (!api.ClientAttached)
            return 0;
        float* v = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        return api.ClientShims.BeamHaloVisibility(v, proxySize);
    }

    public Vector ScreenPosition(in Vector point)
    {
        if (!api.ClientAttached)
            return default;
        float* v = stackalloc float[6] { point.X, point.Y, point.Z, 0, 0, 0 };
        api.ClientShims.BeamScreenPosition(v, v + 3);
        return new Vector(v[3], v[4], v[5]);
    }

    public int RenderableAdd(in Vector origin, in Vector mins, in Vector maxs)
    {
        if (!api.ClientAttached)
            return -1;
        float* v = stackalloc float[9] { origin.X, origin.Y, origin.Z, mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        return api.ClientShims.BeamRenderableAdd(v);
    }

    public void RenderableChanged(int renderable, in Vector origin, in Vector mins, in Vector maxs)
    {
        if (!api.ClientAttached)
            return;
        float* v = stackalloc float[9] { origin.X, origin.Y, origin.Z, mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        api.ClientShims.BeamRenderableChanged(renderable, v);
    }

    public void RenderableRemove(int renderable)
    {
        if (api.ClientAttached)
            api.ClientShims.BeamRenderableRemove(renderable);
    }

    public bool BindSprite(int modelIndex, int frame, int renderMode, bool wireframe)
        => api.ClientAttached && api.ClientShims.BeamBindSprite(modelIndex, frame, renderMode, wireframe ? 1 : 0) != 0;

    public void SetHdrColorScale(int modelIndex, int renderMode, float scale)
    {
        if (api.ClientAttached)
            api.ClientShims.BeamSetHdrColorScale(modelIndex, renderMode, scale);
    }

    public bool Glow(int modelIndex, bool bind)
        => api.ClientAttached && api.ClientShims.BeamGlow(modelIndex, bind ? 1 : 0) != 0;

    public void Strip(ReadOnlySpan<float> vertices)
    {
        // Seventeen floats a vertex, two a segment: at least two segments.
        if (!api.ClientAttached || vertices.Length < 4 * 17)
            return;
        fixed (float* p = vertices)
            api.ClientShims.BeamDrawStrip(p, vertices.Length / 17);
    }

    public void Mesh(int primitive, int primitives, int colorKind, ReadOnlySpan<float> vertices)
    {
        if (!api.ClientAttached || vertices.Length < 9)
            return;
        fixed (float* p = vertices)
            api.ClientShims.BeamDrawMesh(primitive, primitives, colorKind, p, vertices.Length / 9);
    }
}
