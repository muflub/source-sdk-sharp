using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The particle library's emitter classes a muzzle flash builds on.</summary>
public enum FxEmitterKind
{
    /// <summary><c>CSimpleEmitter</c>, in world space.</summary>
    Simple = 0,

    /// <summary><c>CLocalSpaceEmitter</c>, in an entity attachment's space.</summary>
    LocalSpace = 1,

    /// <summary><c>CEmberEffect</c>.</summary>
    Ember = 2,

    /// <summary><c>CTrailParticles</c>.</summary>
    Trail = 3,
}

/// <summary>The fields a decision writes on a <c>SimpleParticle</c> after adding it.</summary>
public struct FxSimpleParticle
{
    public float Lifetime;
    public float DieTime;
    public Vector Velocity;
    public byte R, G, B;
    public byte StartAlpha, EndAlpha;
    public byte StartSize, EndSize;
    public float Roll;
    public float RollDelta;
}

/// <summary>The fields a decision writes on a <c>TrailParticle</c> after adding it.</summary>
public struct FxTrailParticle
{
    public float Lifetime;
    public float DieTime;
    public Vector Velocity;
    public byte R, G, B, A;
    public float Length;
    public float Width;
}

/// <summary>
/// The emitters the legacy tempents' muzzle flashes fill: stock's particle
/// library emitter classes, made and filled as the decision says (C# since
/// lane c5-particles: PoolEmitters, on the managed executors). An emitter is
/// addressed by the id <see cref="Create"/> answered until
/// <see cref="Release"/>; a particle by the index <see cref="Add"/> answered.
/// </summary>
public interface IFxEmitters
{
    /// <summary>Make an emitter; 0 when none could be made. Handle and attachment are a local-space emitter's.</summary>
    int Create(FxEmitterKind kind, string name, uint handle, int attachment, int flags);

    void DrawBeforeViewModel(int emitter);

    void SetBBox(int emitter, in Vector mins, in Vector maxs);

    void SetSortOrigin(int emitter, in Vector origin);

    /// <summary>A trail emitter's <c>SetFlag</c>.</summary>
    void SetTrailFlag(int emitter, int flag);

    /// <summary>A trail emitter's <c>m_ParticleCollision.SetGravity</c>.</summary>
    void SetCollisionGravity(int emitter, float gravity);

    /// <summary><c>AddParticle</c> with a material by name; -1 when the emitter is full (stock's NULL).</summary>
    int Add(int emitter, string material, in Vector position);

    void SetSimple(int emitter, int particle, in FxSimpleParticle fields);

    void SetTrail(int emitter, int particle, in FxTrailParticle fields);

    /// <summary>The emitter's reference goes (CSmartPtr leaving scope); the library keeps it while it has particles.</summary>
    void Release(int emitter);
}

/// <summary>The muzzle flashes', the bounce sounds' and the named tempents' carry-out, over IShimFxClient's slots.</summary>
internal sealed unsafe class ClientFxEmitterSlots(FxClientArea fxClient) : IFxFlashReads, IFxGameSounds, IFxTempEntModels
{
    private static byte[] Utf8(string s) => System.Text.Encoding.UTF8.GetBytes(s + "\0");

    private static void Six(float* v, out Vector origin, out QAngle angles)
    {
        origin = new Vector(v[0], v[1], v[2]);
        angles = new QAngle(v[3], v[4], v[5]);
    }

    public bool AttachmentTransform(uint handle, int attachment, out Vector origin, out QAngle angles)
        => FxFlashAttachments.Transform(this, handle, attachment, out origin, out angles);

    public bool AttachmentMatrixOrigin(uint handle, int attachment, out Vector origin)
        => FxFlashAttachments.MatrixOrigin(this, handle, attachment, out origin);

    public bool RenderableAttachment(uint handle, int attachment, out Vector origin, out QAngle angles)
    {
        origin = default;
        angles = default;
        float* v = stackalloc float[6];
        if (fxClient.FxRenderableAttachment(handle, attachment, v) == 0)
            return false;
        Six(v, out origin, out angles);
        return true;
    }

    public int EntityIndex(uint handle) => fxClient.FxHandleEntityIndex(handle);

    public void Elight(int key, in Vector origin, byte r, byte g, byte b, sbyte exponent, float radius, float decay, float die)
    {
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        fxClient.FxElight(key, o, r, g, b, exponent, radius, decay, die);
    }

    public Vector VectorRandom(float lo, float hi)
    {
        float* v = stackalloc float[3];
        fxClient.FxVectorRandom(lo, hi, v);
        return new Vector(v[0], v[1], v[2]);
    }

    public int MaterialsCached() => fxClient.FxMaterialsCached();

    public bool Parameters(string script, out GameSoundParams parameters)
    {
        parameters = default;
        float* v = stackalloc float[6];
        byte* wave = stackalloc byte[260];
        int ok;
        fixed (byte* s = Utf8(script))
            ok = fxClient.FxSoundParameters(s, v, wave, 260);
        if (ok == 0)
            return false;
        parameters = new GameSoundParams(new string((sbyte*)wave), v[0], (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5]);
        return true;
    }

    public void EmitAtPosition(in GameSoundParams p, float volume, int pitch, in Vector origin)
    {
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        fixed (byte* w = Utf8(p.Wave))
            fxClient.FxEmitWorldSound(w, p.Channel, volume, p.SoundLevel, pitch, o);
    }

    public bool CreateNamed(int slot, in Vector origin, string model, int priority)
    {
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        fixed (byte* m = Utf8(model))
            return fxClient.FxTempEntCreateNamed(slot, o, m, priority) != 0;
    }

    public void LoadModel(string model)
    {
        fixed (byte* m = Utf8(model))
            fxClient.FxLoadModel(m);
    }

    public int FrameCount(string model)
    {
        fixed (byte* m = Utf8(model))
            return fxClient.FxModelFrameCount(m);
    }
}

/// <summary>What native's legacy tempents reach (IManagedFxClient's TempEnt* bodies).</summary>
public interface IClientTempEntHost
{
    bool IsTransparent(bool translucentModel, int renderMode);

    int RenderGroup(int renderMode, int blend, bool translucentModel, bool twoPass);

    /// <summary>ComputeFxBlend: fourteen floats in (fx, alpha, mode, index, curtime, origin, view origin, view forward); blend and alpha out.</summary>
    void Blend(ReadOnlySpan<float> input, Span<int> output);

    int OccludedAlpha(int flags, int alpha, int renderAmt, in Vector normal, in Vector origin, in Vector mainViewOrigin);

    /// <summary>The slot whose origin lights a breakmodel's slot; -1 for none.</summary>
    int LightingOrigin(int slot);

    void MuzzleFlash(int type, uint handle, int attachment, bool firstPerson, bool worldPath);

    void EjectBrass(in Vector origin, in QAngle angles, in QAngle gunAngles, int type);

    int Attach(int client, int modelIndex, float zoffset, float life, int maxClients);

    void LevelInit();
}

/// <summary>
/// <c>FX_GetAttachmentTransform</c>'s two spellings over an
/// <see cref="IFxFlashReads"/>'s renderable read, for an implementation that
/// answers them the engine's way.
///
/// <para>
/// They were default bodies on the interface until plan_shim_areas_split
/// phase 4 moved it to an interface assembly, which holds no bodies. They stay
/// members of the interface rather than becoming extension methods because
/// the oracle facts' doubles answer them from recorded stock runs rather than
/// through the renderable read.
/// </para>
/// </summary>
public static class FxFlashAttachments
{
    /// <summary>
    /// Nothing for an invalid handle or an attachment below 1, else the
    /// renderable's attachment (false, and zeros, when the handle has no
    /// renderable).
    /// </summary>
    public static bool Transform(IFxFlashReads reads, uint handle, int attachment, out Vector origin, out QAngle angles)
    {
        origin = default;
        angles = default;
        if (handle == EntityHandle.None.Value || attachment < 1)
            return false;
        return reads.RenderableAttachment(handle, attachment, out origin, out angles);
    }

    /// <summary>The matrix's translation, which <c>AngleMatrix</c> copies from the origin unchanged.</summary>
    public static bool MatrixOrigin(IFxFlashReads reads, uint handle, int attachment, out Vector origin)
        => Transform(reads, handle, attachment, out origin, out _);
}
