using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

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
internal sealed unsafe class ClientBeamExecutor(EffectsRenderClientArea effectsRenderClient) : IClientBeamExecutor
{
    public void View(bool insideView, Span<float> out42)
    {
        out42.Clear();
        if (out42.Length < 42)
            return;
        fixed (float* p = out42)
            effectsRenderClient.BeamView(insideView ? 1 : 0, p);
    }

    public bool ModelInfo(int modelIndex, out int frameCount)
    {
        frameCount = 0;
        int n = 0;
        bool exists = effectsRenderClient.BeamModelInfo(modelIndex, &n) != 0;
        frameCount = n;
        return exists;
    }

    public int ModelIndex(string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return effectsRenderClient.BeamModelIndex(p);
    }

    public EntityHandle EntityAt(int entry)
        => new EntityHandle(effectsRenderClient.BeamEntityAt(entry));

    public bool EntityHasModel(EntityHandle entity)
        => effectsRenderClient.BeamEntityHasModel(entity.Value) != 0;

    public int EntityFacts(EntityHandle entity, int attachment, bool tryAttachment, Span<float> out9)
    {
        out9.Clear();
        if (out9.Length < 9)
            return 0;
        fixed (float* p = out9)
            return effectsRenderClient.BeamEntityFacts(entity.Value, attachment, tryAttachment ? 1 : 0, p);
    }

    public void BoxVisibility(in Vector mins, in Vector maxs, out bool visible, out bool culled)
    {
        visible = false;
        culled = true;
        float* v = stackalloc float[6] { mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        int bits = effectsRenderClient.BeamBoxVisibility(v, v + 3);
        visible = (bits & 1) != 0;
        culled = (bits & 2) != 0;
    }

    public Vector ScreenPosition(in Vector point)
    {
        float* v = stackalloc float[6] { point.X, point.Y, point.Z, 0, 0, 0 };
        effectsRenderClient.BeamScreenPosition(v, v + 3);
        return new Vector(v[3], v[4], v[5]);
    }

    public int RenderableAdd(in Vector origin, in Vector mins, in Vector maxs)
    {
        float* v = stackalloc float[9] { origin.X, origin.Y, origin.Z, mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        return effectsRenderClient.BeamRenderableAdd(v);
    }

    public void RenderableChanged(int renderable, in Vector origin, in Vector mins, in Vector maxs)
    {
        float* v = stackalloc float[9] { origin.X, origin.Y, origin.Z, mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        effectsRenderClient.BeamRenderableChanged(renderable, v);
    }

    public void RenderableRemove(int renderable)
    {
        effectsRenderClient.BeamRenderableRemove(renderable);
    }

    public bool BindSprite(int modelIndex, int frame, int renderMode, bool wireframe)
        => effectsRenderClient.BeamBindSprite(modelIndex, frame, renderMode, wireframe ? 1 : 0) != 0;

    public void SetHdrColorScale(int modelIndex, int renderMode, float scale)
    {
        effectsRenderClient.BeamSetHdrColorScale(modelIndex, renderMode, scale);
    }

    public bool Glow(int modelIndex, bool bind)
        => effectsRenderClient.BeamGlow(modelIndex, bind ? 1 : 0) != 0;

    public void Strip(ReadOnlySpan<float> vertices)
    {
        // Seventeen floats a vertex, two a segment: at least two segments.
        if (vertices.Length < 4 * 17)
            return;
        fixed (float* p = vertices)
            effectsRenderClient.BeamDrawStrip(p, vertices.Length / 17);
    }

    public void Mesh(int primitive, int primitives, int colorKind, ReadOnlySpan<float> vertices)
    {
        if (vertices.Length < 9)
            return;
        fixed (float* p = vertices)
            effectsRenderClient.BeamDrawMesh(primitive, primitives, colorKind, p, vertices.Length / 9);
    }
}
