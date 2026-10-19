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
internal sealed unsafe class ClientBeamExecutor(EffectsRenderClientArea? effectsRenderClient) : IClientBeamExecutor
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private EffectsRenderClientArea EffectsRenderClient => effectsRenderClient ?? throw ShimAreaGuard.NotAttached(nameof(EffectsRenderClient));

    public void View(bool insideView, Span<float> out42)
    {
        out42.Clear();
        if (effectsRenderClient is null || out42.Length < 42)
            return;
        fixed (float* p = out42)
            EffectsRenderClient.BeamView(insideView ? 1 : 0, p);
    }

    public bool ModelInfo(int modelIndex, out int frameCount)
    {
        frameCount = 0;
        if (effectsRenderClient is null)
            return false;
        int n = 0;
        bool exists = EffectsRenderClient.BeamModelInfo(modelIndex, &n) != 0;
        frameCount = n;
        return exists;
    }

    public int ModelIndex(string name)
    {
        if (effectsRenderClient is null)
            return -1;
        byte[] bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return EffectsRenderClient.BeamModelIndex(p);
    }

    public EntityHandle EntityAt(int entry)
        => (effectsRenderClient is not null) ? new EntityHandle(EffectsRenderClient.BeamEntityAt(entry)) : EntityHandle.None;

    public bool EntityHasModel(EntityHandle entity)
        => (effectsRenderClient is not null) && EffectsRenderClient.BeamEntityHasModel(entity.Value) != 0;

    public int EntityFacts(EntityHandle entity, int attachment, bool tryAttachment, Span<float> out9)
    {
        out9.Clear();
        if (effectsRenderClient is null || out9.Length < 9)
            return 0;
        fixed (float* p = out9)
            return EffectsRenderClient.BeamEntityFacts(entity.Value, attachment, tryAttachment ? 1 : 0, p);
    }

    public void BoxVisibility(in Vector mins, in Vector maxs, out bool visible, out bool culled)
    {
        visible = false;
        culled = true;
        if (effectsRenderClient is null)
            return;
        float* v = stackalloc float[6] { mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        int bits = EffectsRenderClient.BeamBoxVisibility(v, v + 3);
        visible = (bits & 1) != 0;
        culled = (bits & 2) != 0;
    }

    public float HaloVisibility(in Vector origin, float proxySize)
    {
        if (effectsRenderClient is null)
            return 0;
        float* v = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        return EffectsRenderClient.BeamHaloVisibility(v, proxySize);
    }

    public Vector ScreenPosition(in Vector point)
    {
        if (effectsRenderClient is null)
            return default;
        float* v = stackalloc float[6] { point.X, point.Y, point.Z, 0, 0, 0 };
        EffectsRenderClient.BeamScreenPosition(v, v + 3);
        return new Vector(v[3], v[4], v[5]);
    }

    public int RenderableAdd(in Vector origin, in Vector mins, in Vector maxs)
    {
        if (effectsRenderClient is null)
            return -1;
        float* v = stackalloc float[9] { origin.X, origin.Y, origin.Z, mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        return EffectsRenderClient.BeamRenderableAdd(v);
    }

    public void RenderableChanged(int renderable, in Vector origin, in Vector mins, in Vector maxs)
    {
        if (effectsRenderClient is null)
            return;
        float* v = stackalloc float[9] { origin.X, origin.Y, origin.Z, mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        EffectsRenderClient.BeamRenderableChanged(renderable, v);
    }

    public void RenderableRemove(int renderable)
    {
        if ((effectsRenderClient is not null))
            EffectsRenderClient.BeamRenderableRemove(renderable);
    }

    public bool BindSprite(int modelIndex, int frame, int renderMode, bool wireframe)
        => (effectsRenderClient is not null) && EffectsRenderClient.BeamBindSprite(modelIndex, frame, renderMode, wireframe ? 1 : 0) != 0;

    public void SetHdrColorScale(int modelIndex, int renderMode, float scale)
    {
        if ((effectsRenderClient is not null))
            EffectsRenderClient.BeamSetHdrColorScale(modelIndex, renderMode, scale);
    }

    public bool Glow(int modelIndex, bool bind)
        => (effectsRenderClient is not null) && EffectsRenderClient.BeamGlow(modelIndex, bind ? 1 : 0) != 0;

    public void Strip(ReadOnlySpan<float> vertices)
    {
        // Seventeen floats a vertex, two a segment: at least two segments.
        if (effectsRenderClient is null || vertices.Length < 4 * 17)
            return;
        fixed (float* p = vertices)
            EffectsRenderClient.BeamDrawStrip(p, vertices.Length / 17);
    }

    public void Mesh(int primitive, int primitives, int colorKind, ReadOnlySpan<float> vertices)
    {
        if (effectsRenderClient is null || vertices.Length < 9)
            return;
        fixed (float* p = vertices)
            EffectsRenderClient.BeamDrawMesh(primitive, primitives, colorKind, p, vertices.Length / 9);
    }
}
