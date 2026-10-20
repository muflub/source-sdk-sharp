using System.Runtime.InteropServices;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared.Interop;

namespace SourceSharp.Client.Interop;

/// <summary>
/// fwd_shadows.h's record kinds (SSShadowOp_t), name for name and number for
/// number (ShadowNativeAbiTests holds the two lists equal). 1-63 answer
/// nothing; 64 and up are questions.
/// </summary>
public enum ShadowNativeOp
{
    DestroyShadow = 1,
    SetShadowMaterial = 2,
    ProjectShadow = 3,
    ProjectFlashlight = 4,
    AddShadowToBrushModel = 5,
    RemoveAllShadowsFromBrushModel = 6,
    SetShadowTexCoord = 7,
    AddShadowToModel = 8,
    RemoveAllShadowsFromModel = 9,
    ClearExtraClipPlanes = 10,
    AddExtraClipPlane = 11,
    EnableShadow = 12,
    SetFalloffBias = 13,
    UpdateFlashlightState = 14,
    AddFlashlightRenderable = 15,
    SetFlashlightDepthTexture = 16,
    SetFlashlightRenderState = 17,
    AddShadowToStaticProp = 18,
    RemoveAllShadowsFromStaticProp = 19,
    ShutdownMaterial = 20,
    ColorModulate = 21,
    ShutdownTexture = 22,
    BeginRenderTargetAllocation = 23,
    EndRenderTargetAllocation = 24,
    UncacheUnusedMaterials = 25,
    AddRestoreFunc = 26,
    RemoveRestoreFunc = 27,
    FogMode = 28,
    SetHeightClipMode = 29,
    ClearColor4ub = 30,
    ClearColor3ub = 31,
    MatrixMode = 32,
    PushMatrix = 33,
    PopMatrix = 34,
    LoadIdentity = 35,
    LoadMatrix = 36,
    Scale = 37,
    Ortho = 38,
    PushRenderTargetAndViewport = 39,
    PopRenderTargetAndViewport = 40,
    ClearBuffers = 41,
    Viewport = 42,
    SetShadowDepthBiasFactors = 43,
    DrawTextureQuad = 44,
    DrawBrushModelShadow = 45,
    UpdateShadowDepthTexture = 46,
    CreateModelInstance = 47,
    AddLineOverlay = 48,
    AddTextOverlay = 49,

    CreateShadowEx = 64,
    GetInfo = 65,
    GetFlashlightFrustum = 66,
    InitMaterial = 67,
    InitRenderTarget = 68,
    TextureSize = 69,
    Hardware = 70,
    ComputePixelDiameterOfSphere = 71,
    GetFogMode = 72,
    GetHeightClipMode = 73,
    DrawModelShadow = 74,
    CullBox = 75,
    AmbientLightColor = 76,
    LeavesAlongRay = 77,
    LeavesInBox = 78,
    FrameCount = 79,
    RenderableOf = 80,
    RenderOrigin = 81,
    RenderAngles = 82,
    RenderableToWorld = 83,
    Model = 84,
    ModelType = 85,
    BodySkin = 86,
    RenderBoundsWorldspace = 87,
    ShadowRenderBounds = 88,
    ShouldReceiveProjectedTextures = 89,
    ShadowCastDistance = 90,
    ShadowCastDirection = 91,
    ShadowParent = 92,
    FirstShadowChild = 93,
    NextShadowPeer = 94,
    ModelInstance = 95,
    NetworkableEntIndex = 96,
}

/// <summary>Runs made, records carried and questions asked: the shadow manager's outbound crossings (cs_cl_shadow_stats).</summary>
public sealed class ShadowCallCounts
{
    /// <summary>Calls of SSFwd_Shadows_Run: one crossing each.</summary>
    public long Runs { get; private set; }

    /// <summary>Records the runs carried (the calls the engine was made, and the questions).</summary>
    public long Records { get; private set; }

    /// <summary>Of those, questions (each one ends its run).</summary>
    public long Questions { get; private set; }

    /// <summary>The largest run's records.</summary>
    public long LargestRun { get; private set; }

    public void Count(int records, bool question)
    {
        Runs++;
        Records += records;
        if (question)
            Questions++;
        if (records > LargestRun)
            LargestRun = records;
    }

    public void Reset()
    {
        Runs = 0;
        Records = 0;
        Questions = 0;
        LargestRun = 0;
    }
}

/// <summary>
/// The engine's side of the managed shadow manager's seams, over the client
/// forwarder library's ShadowNative table (src/fwd/native/fwd_shadows.cpp,
/// tools/engine-forwarders' HAND row Shadows) and the environment client.so
/// built for it (shell_shadows_client.cpp). A call that answers nothing is a
/// record queued; a question is a record sent with the queue in front of it,
/// so the buffer is carried out in the order the manager made its calls, and
/// costs one crossing however long it is. <see cref="Flush"/> sends a queue
/// with no question behind it (the facade calls it when the manager returns).
/// Native decides nothing; every record is a call the manager made.
/// </summary>
public sealed unsafe class ShadowNative : IShadowEngine, IShadowMaterials, IShadowRenderContext, IShadowWorld,
                                         IShadowRenderables, IShadowDebugOverlay, IShadowNativeQueue
{
    // The queue goes when it grows past this many words, a question or no.
    private const int FlushWords = 16384;

    private readonly ShadowsFwd* _fwd;
    private readonly void* _env;
    private int[] _ops = new int[1024];
    private int _n;
    private int _records;
    private int[] _answers = new int[256];
    private bool _hardwareRead;
    private int _depthSupport, _depthFormat, _nullFormat, _dxLevel, _tools;

    private ShadowNative(ShadowsFwd* fwd, void* env)
    {
        _fwd = fwd;
        _env = env;
    }

    /// <summary>The table and client.so's environment, or null with why.</summary>
    public static ShadowNative? Acquire(ModuleEngine engine, out string why)
    {
        ArgumentNullException.ThrowIfNull(engine);
        EngineSide side = engine.Api;
        if (!Forwarders.TryAcquire(side.Interfaces, out ShadowsFwd* table, out why))
            return null;
        if (!side.Interfaces.TryCreate(SourceSharpFactoryType.Internal, "SourceSharpShadowEnv001", out nint env, out string? error))
        {
            why = error ?? "client.so serves no shadow environment";
            return null;
        }
        return new ShadowNative(table, (void*)env);
    }

    /// <summary>The runs made, for cs_cl_shadow_stats.</summary>
    public ShadowCallCounts Calls { get; } = new();

    //-------------------------------------------------------------------------
    // The buffer.
    //-------------------------------------------------------------------------

    private void Reserve(int words)
    {
        if (_n + words > _ops.Length)
            Array.Resize(ref _ops, Math.Max(_ops.Length * 2, _n + words));
    }

    private int Begin(ShadowNativeOp kind)
    {
        Reserve(2);
        int at = _n;
        _ops[_n++] = (int)kind;
        _ops[_n++] = 0;
        return at;
    }

    private void End(int at)
    {
        _ops[at + 1] = _n - at - 2;
        _records++;
        if (_n >= FlushWords && (int)_ops[at] < 64)
            Flush();
    }

    private void I(int w)
    {
        Reserve(1);
        _ops[_n++] = w;
    }

    private void F(float f) => I(BitConverter.SingleToInt32Bits(f));

    private void D(double d)
    {
        long bits = BitConverter.DoubleToInt64Bits(d);
        I(unchecked((int)bits));
        I(unchecked((int)(bits >> 32)));
    }

    private void P(nint p)
    {
        long v = p;
        I(unchecked((int)v));
        I(unchecked((int)(v >> 32)));
    }

    private void V(in Vector v)
    {
        F(v.X);
        F(v.Y);
        F(v.Z);
    }

    private void A(in QAngle a)
    {
        F(a.Pitch);
        F(a.Yaw);
        F(a.Roll);
    }

    private void M(in ShadowMatrix m)
    {
        for (int i = 0; i < 16; i++)
            F(m[i]);
    }

    private void S(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        I(bytes.Length);
        int words = (bytes.Length + 4) / 4;
        Reserve(words);
        var dest = MemoryMarshal.AsBytes(_ops.AsSpan(_n, words));
        dest.Clear();
        bytes.CopyTo(dest);
        _n += words;
    }

    private void Leaves(ReadOnlySpan<int> leaves)
    {
        I(leaves.Length);
        Reserve(leaves.Length);
        leaves.CopyTo(_ops.AsSpan(_n));
        _n += leaves.Length;
    }

    /// <summary>The queue carried out now; nothing when it is empty.</summary>
    public void Flush()
    {
        if (_n == 0)
            return;
        Send(question: false, 0);
    }

    // The buffer to native; the answers' words (the buffer grown and the last
    // record, the question, asked again alone when they did not fit).
    private ReadOnlySpan<int> Send(bool question, int answerWords)
    {
        int n = _n;
        int records = _records;
        int[] ops = _ops;
        _n = 0;
        _records = 0;
        if (_answers.Length < answerWords)
            _answers = new int[Math.Max(answerWords, _answers.Length * 2)];
        // Locals: a record native carries out may re-enter managed (the depth
        // view's nested plan, UpdateShadowDepthTexture, which is always the
        // last record of its buffer: native has read it before it runs), and a
        // call made there queues and sends a buffer of its own.
        int[] answers = _answers;
        Calls.Count(records, question);
        int written;
        fixed (int* o = ops)
        fixed (int* a = answers)
            written = _fwd->Run(_env, o, n, a, answers.Length);
        if (written >= 0)
            return answers.AsSpan(0, written);

        // The question's answer was longer than the buffer: everything before
        // it has been carried out, so only the question goes again.
        int need = -written;
        int last = LastRecord(ops, n);
        var again = ops.AsSpan(last, n - last).ToArray();
        var larger = new int[need];
        _answers = larger;
        Calls.Count(1, question);
        fixed (int* o = again)
        fixed (int* a = larger)
            written = _fwd->Run(_env, o, again.Length, a, larger.Length);
        return larger.AsSpan(0, Math.Max(0, written));
    }

    private static int LastRecord(int[] ops, int n)
    {
        int at = 0, last = 0;
        while (at + 2 <= n)
        {
            last = at;
            at += 2 + ops[at + 1];
        }
        return last;
    }

    private ReadOnlySpan<int> Ask(int at, int answerWords)
    {
        End(at);
        return Send(question: true, answerWords);
    }

    private static float Fl(ReadOnlySpan<int> a, int i) => BitConverter.Int32BitsToSingle(a[i]);

    private static nint Ptr(ReadOnlySpan<int> a, int i) => (nint)(((long)(uint)a[i + 1] << 32) | (uint)a[i]);

    private static Vector Vec(ReadOnlySpan<int> a, int i) => new(Fl(a, i), Fl(a, i + 1), Fl(a, i + 2));

    //-------------------------------------------------------------------------
    // IShadowEngine.
    //-------------------------------------------------------------------------

    public int CreateShadowEx(int material, int modelMaterial, int bindProxy, int creationFlags)
    {
        int at = Begin(ShadowNativeOp.CreateShadowEx);
        I(material);
        I(modelMaterial);
        P(bindProxy);
        I(creationFlags);
        return Ask(at, 1)[0] & 0xFFFF;
    }

    public void DestroyShadow(int shadow)
    {
        int at = Begin(ShadowNativeOp.DestroyShadow);
        I(shadow);
        End(at);
    }

    public void SetShadowMaterial(int shadow, int material, int modelMaterial, int bindProxy)
    {
        int at = Begin(ShadowNativeOp.SetShadowMaterial);
        I(shadow);
        I(material);
        I(modelMaterial);
        P(bindProxy);
        End(at);
    }

    public void ProjectShadow(int shadow, in Vector origin, in Vector projectionDir, in ShadowMatrix worldToShadow, float sizeX,
                              float sizeY, ReadOnlySpan<int> leaves, float maxHeight, float falloffOffset, float falloffAmount,
                              in Vector casterOrigin)
    {
        int at = Begin(ShadowNativeOp.ProjectShadow);
        I(shadow);
        V(origin);
        V(projectionDir);
        M(worldToShadow);
        F(sizeX);
        F(sizeY);
        F(maxHeight);
        F(falloffOffset);
        F(falloffAmount);
        V(casterOrigin);
        Leaves(leaves);
        End(at);
    }

    public void ProjectFlashlight(int shadow, in ShadowMatrix worldToShadow, ReadOnlySpan<int> leaves)
    {
        int at = Begin(ShadowNativeOp.ProjectFlashlight);
        I(shadow);
        M(worldToShadow);
        Leaves(leaves);
        End(at);
    }

    public void GetInfo(int shadow, out ShadowEngineInfo info)
    {
        int at = Begin(ShadowNativeOp.GetInfo);
        I(shadow);
        ReadOnlySpan<int> a = Ask(at, (sizeof(ShadowInfoRaw) + 3) / 4);
        info = default;
        if (a.Length * 4 < sizeof(ShadowInfoRaw))
            return;
        ShadowInfoRaw raw = MemoryMarshal.Read<ShadowInfoRaw>(MemoryMarshal.AsBytes(a));
        for (int i = 0; i < 16; i++)
            info.WorldToShadow[i] = raw.WorldToShadow[i];
        info.FalloffOffset = raw.FalloffOffset;
        info.MaxDist = raw.MaxDist;
        info.FalloffAmount = raw.FalloffAmount;
        info.TexOriginX = raw.TexOriginX;
        info.TexOriginY = raw.TexOriginY;
        info.TexSizeX = raw.TexSizeX;
        info.TexSizeY = raw.TexSizeY;
        info.FalloffBias = raw.FalloffBias;
    }

    public void GetFlashlightFrustum(int shadow, out ShadowFrustum frustum)
    {
        int at = Begin(ShadowNativeOp.GetFlashlightFrustum);
        I(shadow);
        ReadOnlySpan<int> a = Ask(at, (sizeof(FrustumRaw) + 3) / 4);
        frustum = default;
        if (a.Length * 4 < sizeof(FrustumRaw))
            return;
        FrustumRaw raw = MemoryMarshal.Read<FrustumRaw>(MemoryMarshal.AsBytes(a));
        PlaneRaw[] planes = [raw.Right, raw.Left, raw.Top, raw.Bottom, raw.Near, raw.Far];
        for (int i = 0; i < 6; i++)
        {
            frustum[i] = new ShadowPlane
            {
                Normal = new Vector(planes[i].NormalX, planes[i].NormalY, planes[i].NormalZ),
                Dist = planes[i].Dist,
                Type = planes[i].Type,
                SignBits = planes[i].SignBits,
            };
        }
    }

    public void AddShadowToBrushModel(int shadow, nint model, in Vector origin, in QAngle angles)
    {
        int at = Begin(ShadowNativeOp.AddShadowToBrushModel);
        I(shadow);
        P(model);
        V(origin);
        A(angles);
        End(at);
    }

    public void RemoveAllShadowsFromBrushModel(nint model)
    {
        int at = Begin(ShadowNativeOp.RemoveAllShadowsFromBrushModel);
        P(model);
        End(at);
    }

    public void SetShadowTexCoord(int shadow, float x, float y, float w, float h)
    {
        int at = Begin(ShadowNativeOp.SetShadowTexCoord);
        I(shadow);
        F(x);
        F(y);
        F(w);
        F(h);
        End(at);
    }

    public void AddShadowToModel(int shadow, int modelInstance)
    {
        int at = Begin(ShadowNativeOp.AddShadowToModel);
        I(shadow);
        I(modelInstance);
        End(at);
    }

    public void RemoveAllShadowsFromModel(int modelInstance)
    {
        int at = Begin(ShadowNativeOp.RemoveAllShadowsFromModel);
        I(modelInstance);
        End(at);
    }

    public void ClearExtraClipPlanes(int shadow)
    {
        int at = Begin(ShadowNativeOp.ClearExtraClipPlanes);
        I(shadow);
        End(at);
    }

    public void AddExtraClipPlane(int shadow, in Vector normal, float dist)
    {
        int at = Begin(ShadowNativeOp.AddExtraClipPlane);
        I(shadow);
        V(normal);
        F(dist);
        End(at);
    }

    public void EnableShadow(int shadow, bool enable)
    {
        int at = Begin(ShadowNativeOp.EnableShadow);
        I(shadow);
        I(enable ? 1 : 0);
        End(at);
    }

    public void SetFalloffBias(int shadow, byte bias)
    {
        int at = Begin(ShadowNativeOp.SetFalloffBias);
        I(shadow);
        I(bias);
        End(at);
    }

    public void UpdateFlashlightState(int shadow, in FlashlightState state)
    {
        int at = Begin(ShadowNativeOp.UpdateFlashlightState);
        I(shadow);
        int words = sizeof(FlashlightState) / 4;
        Reserve(words);
        FlashlightState copy = state;
        new ReadOnlySpan<int>(&copy, words).CopyTo(_ops.AsSpan(_n));
        _n += words;
        End(at);
    }

    public void AddFlashlightRenderable(int shadow, nint renderable)
    {
        int at = Begin(ShadowNativeOp.AddFlashlightRenderable);
        I(shadow);
        P(renderable);
        End(at);
    }

    public void SetFlashlightDepthTexture(int shadow, int texture, byte stencilBit)
    {
        int at = Begin(ShadowNativeOp.SetFlashlightDepthTexture);
        I(shadow);
        I(texture);
        I(stencilBit);
        End(at);
    }

    public void SetFlashlightRenderState(int shadow)
    {
        int at = Begin(ShadowNativeOp.SetFlashlightRenderState);
        I(shadow);
        End(at);
    }

    public void AddShadowToStaticProp(int shadow, nint renderable)
    {
        int at = Begin(ShadowNativeOp.AddShadowToStaticProp);
        I(shadow);
        P(renderable);
        End(at);
    }

    public void RemoveAllShadowsFromStaticProp(nint renderable)
    {
        int at = Begin(ShadowNativeOp.RemoveAllShadowsFromStaticProp);
        P(renderable);
        End(at);
    }

    //-------------------------------------------------------------------------
    // IShadowMaterials.
    //-------------------------------------------------------------------------

    public int InitMaterial(string name, string textureGroup)
    {
        int at = Begin(ShadowNativeOp.InitMaterial);
        S(name);
        S(textureGroup);
        return Ask(at, 1)[0];
    }

    public void ShutdownMaterial(int material)
    {
        int at = Begin(ShadowNativeOp.ShutdownMaterial);
        I(material);
        End(at);
    }

    public void ColorModulate(int material, float r, float g, float b)
    {
        int at = Begin(ShadowNativeOp.ColorModulate);
        I(material);
        F(r);
        F(g);
        F(b);
        End(at);
    }

    public int InitRenderTarget(string name, int width, int height, int sizeMode, int format, int depth, bool hdr)
    {
        int at = Begin(ShadowNativeOp.InitRenderTarget);
        S(name);
        I(width);
        I(height);
        I(sizeMode);
        I(format);
        I(depth);
        I(hdr ? 1 : 0);
        return Ask(at, 1)[0];
    }

    public void ShutdownTexture(int texture)
    {
        int at = Begin(ShadowNativeOp.ShutdownTexture);
        I(texture);
        End(at);
    }

    private (int Width, int Height) TextureSize(int texture)
    {
        int at = Begin(ShadowNativeOp.TextureSize);
        I(texture);
        ReadOnlySpan<int> a = Ask(at, 2);
        return (a[0], a[1]);
    }

    public int ActualWidth(int texture) => TextureSize(texture).Width;

    public int ActualHeight(int texture) => TextureSize(texture).Height;

    // The hardware's five answers do not change during the process: asked once.
    private void ReadHardware()
    {
        if (_hardwareRead)
            return;
        int at = Begin(ShadowNativeOp.Hardware);
        ReadOnlySpan<int> a = Ask(at, 5);
        _depthSupport = a[0];
        _depthFormat = a[1];
        _nullFormat = a[2];
        _dxLevel = a[3];
        _tools = a[4];
        _hardwareRead = true;
    }

    public bool SupportsShadowDepthTextures()
    {
        ReadHardware();
        return _depthSupport != 0;
    }

    public int ShadowDepthTextureFormat()
    {
        ReadHardware();
        return _depthFormat;
    }

    public int NullTextureFormat()
    {
        ReadHardware();
        return _nullFormat;
    }

    public void BeginRenderTargetAllocation() => End(Begin(ShadowNativeOp.BeginRenderTargetAllocation));

    public void EndRenderTargetAllocation() => End(Begin(ShadowNativeOp.EndRenderTargetAllocation));

    public void UncacheUnusedMaterials() => End(Begin(ShadowNativeOp.UncacheUnusedMaterials));

    public void AddRestoreFunc() => End(Begin(ShadowNativeOp.AddRestoreFunc));

    public void RemoveRestoreFunc() => End(Begin(ShadowNativeOp.RemoveRestoreFunc));

    public int DXSupportLevel()
    {
        ReadHardware();
        return _dxLevel;
    }

    public bool ToolsMode()
    {
        ReadHardware();
        return _tools != 0;
    }

    //-------------------------------------------------------------------------
    // IShadowRenderContext.
    //-------------------------------------------------------------------------

    public float ComputePixelDiameterOfSphere(in Vector center, float radius)
    {
        int at = Begin(ShadowNativeOp.ComputePixelDiameterOfSphere);
        V(center);
        F(radius);
        return Fl(Ask(at, 1), 0);
    }

    public int GetFogMode() => Ask(Begin(ShadowNativeOp.GetFogMode), 1)[0];

    public void FogMode(int mode)
    {
        int at = Begin(ShadowNativeOp.FogMode);
        I(mode);
        End(at);
    }

    public int GetHeightClipMode() => Ask(Begin(ShadowNativeOp.GetHeightClipMode), 1)[0];

    public void SetHeightClipMode(int mode)
    {
        int at = Begin(ShadowNativeOp.SetHeightClipMode);
        I(mode);
        End(at);
    }

    public void ClearColor4ub(byte r, byte g, byte b, byte a)
    {
        int at = Begin(ShadowNativeOp.ClearColor4ub);
        I(r);
        I(g);
        I(b);
        I(a);
        End(at);
    }

    public void ClearColor3ub(byte r, byte g, byte b)
    {
        int at = Begin(ShadowNativeOp.ClearColor3ub);
        I(r);
        I(g);
        I(b);
        End(at);
    }

    public void MatrixMode(int mode)
    {
        int at = Begin(ShadowNativeOp.MatrixMode);
        I(mode);
        End(at);
    }

    public void PushMatrix() => End(Begin(ShadowNativeOp.PushMatrix));

    public void PopMatrix() => End(Begin(ShadowNativeOp.PopMatrix));

    public void LoadIdentity() => End(Begin(ShadowNativeOp.LoadIdentity));

    public void LoadMatrix(in ShadowMatrix matrix)
    {
        int at = Begin(ShadowNativeOp.LoadMatrix);
        M(matrix);
        End(at);
    }

    public void Scale(float x, float y, float z)
    {
        int at = Begin(ShadowNativeOp.Scale);
        F(x);
        F(y);
        F(z);
        End(at);
    }

    public void Ortho(double left, double top, double right, double bottom, double zNear, double zFar)
    {
        int at = Begin(ShadowNativeOp.Ortho);
        D(left);
        D(top);
        D(right);
        D(bottom);
        D(zNear);
        D(zFar);
        End(at);
    }

    public void PushRenderTargetAndViewport(int texture)
    {
        int at = Begin(ShadowNativeOp.PushRenderTargetAndViewport);
        I(texture);
        End(at);
    }

    public void PopRenderTargetAndViewport() => End(Begin(ShadowNativeOp.PopRenderTargetAndViewport));

    public void ClearBuffers(bool color, bool depth, bool stencil)
    {
        int at = Begin(ShadowNativeOp.ClearBuffers);
        I(color ? 1 : 0);
        I(depth ? 1 : 0);
        I(stencil ? 1 : 0);
        End(at);
    }

    public void Viewport(int x, int y, int width, int height)
    {
        int at = Begin(ShadowNativeOp.Viewport);
        I(x);
        I(y);
        I(width);
        I(height);
        End(at);
    }

    public void SetShadowDepthBiasFactors(float slopeScaleDepthBias, float depthBias)
    {
        int at = Begin(ShadowNativeOp.SetShadowDepthBiasFactors);
        F(slopeScaleDepthBias);
        F(depthBias);
        End(at);
    }

    public void DrawTextureQuad(int material, float width, float height)
    {
        int at = Begin(ShadowNativeOp.DrawTextureQuad);
        I(material);
        F(width);
        F(height);
        End(at);
    }

    public bool DrawModelShadow(nint renderable, int body, int skin)
    {
        int at = Begin(ShadowNativeOp.DrawModelShadow);
        P(renderable);
        I(body);
        I(skin);
        return Ask(at, 1)[0] != 0;
    }

    public void DrawBrushModelShadow(nint renderable)
    {
        int at = Begin(ShadowNativeOp.DrawBrushModelShadow);
        P(renderable);
        End(at);
    }

    // The depth view runs a nested plan of its own (it re-enters managed), so
    // it is sent at once, after the calls before it.
    public void UpdateShadowDepthTexture(int renderTarget, int depthTexture, in ShadowDepthView view)
    {
        int at = Begin(ShadowNativeOp.UpdateShadowDepthTexture);
        I(renderTarget);
        I(depthTexture);
        I(view.X);
        I(view.Y);
        I(view.Width);
        I(view.Height);
        F(view.Fov);
        F(view.FovViewmodel);
        V(view.Origin);
        A(view.Angles);
        F(view.ZNear);
        F(view.ZFar);
        F(view.ZNearViewmodel);
        F(view.ZFarViewmodel);
        F(view.AspectRatio);
        I(view.Ortho ? 1 : 0);
        I(view.DoBloomAndToneMapping ? 1 : 0);
        End(at);
        Flush();
    }

    //-------------------------------------------------------------------------
    // IShadowWorld.
    //-------------------------------------------------------------------------

    public bool CullBox(in Vector mins, in Vector maxs)
    {
        int at = Begin(ShadowNativeOp.CullBox);
        V(mins);
        V(maxs);
        return Ask(at, 1)[0] != 0;
    }

    public Vector AmbientLightColor() => Vec(Ask(Begin(ShadowNativeOp.AmbientLightColor), 3), 0);

    public void EnumerateLeavesAlongRay(in Vector start, in Vector delta, in Vector extents, List<int> leaves)
    {
        int at = Begin(ShadowNativeOp.LeavesAlongRay);
        V(start);
        V(delta);
        V(extents);
        AppendLeaves(Ask(at, 513), leaves);
    }

    public void EnumerateLeavesInBox(in Vector mins, in Vector maxs, List<int> leaves)
    {
        int at = Begin(ShadowNativeOp.LeavesInBox);
        V(mins);
        V(maxs);
        AppendLeaves(Ask(at, 513), leaves);
    }

    private static void AppendLeaves(ReadOnlySpan<int> a, List<int> leaves)
    {
        if (a.Length == 0)
            return;
        int count = Math.Min(a[0], a.Length - 1);
        for (int i = 0; i < count; i++)
            leaves.Add(a[1 + i]);
    }

    public int FrameCount => Ask(Begin(ShadowNativeOp.FrameCount), 1)[0];

    //-------------------------------------------------------------------------
    // IShadowRenderables: the renderables' own answers. Whether a renderable's
    // shadow is dirty, and its handle, are the manager's state (the caller
    // wraps these with its own).
    //-------------------------------------------------------------------------

    private ReadOnlySpan<int> AskOf(ShadowNativeOp kind, nint renderable, int answerWords)
    {
        int at = Begin(kind);
        P(renderable);
        return Ask(at, answerWords);
    }

    public nint RenderableOf(uint entity)
    {
        int at = Begin(ShadowNativeOp.RenderableOf);
        I(unchecked((int)entity));
        return Ptr(Ask(at, 2), 0);
    }

    public Vector RenderOrigin(nint renderable) => Vec(AskOf(ShadowNativeOp.RenderOrigin, renderable, 3), 0);

    public QAngle RenderAngles(nint renderable)
    {
        ReadOnlySpan<int> a = AskOf(ShadowNativeOp.RenderAngles, renderable, 3);
        return new QAngle(Fl(a, 0), Fl(a, 1), Fl(a, 2));
    }

    public void RenderableToWorldTransform(nint renderable, out ShadowMatrix3x4 transform)
    {
        ReadOnlySpan<int> a = AskOf(ShadowNativeOp.RenderableToWorld, renderable, 12);
        transform = default;
        for (int i = 0; i < 12 && i < a.Length; i++)
            transform[i] = Fl(a, i);
    }

    public int ShadowHandle(nint renderable) => ShadowFlags.InvalidHandle;

    public nint Model(nint renderable) => Ptr(AskOf(ShadowNativeOp.Model, renderable, 2), 0);

    public int ModelType(nint model)
    {
        int at = Begin(ShadowNativeOp.ModelType);
        P(model);
        return Ask(at, 1)[0];
    }

    private (int Body, int Skin) BodySkin(nint renderable)
    {
        ReadOnlySpan<int> a = AskOf(ShadowNativeOp.BodySkin, renderable, 2);
        return (a[0], a[1]);
    }

    public int Body(nint renderable) => BodySkin(renderable).Body;

    public int Skin(nint renderable) => BodySkin(renderable).Skin;

    public void RenderBoundsWorldspace(nint renderable, out Vector mins, out Vector maxs)
    {
        ReadOnlySpan<int> a = AskOf(ShadowNativeOp.RenderBoundsWorldspace, renderable, 6);
        mins = Vec(a, 0);
        maxs = Vec(a, 3);
    }

    public void ShadowRenderBounds(nint renderable, int shadowType, out Vector mins, out Vector maxs)
    {
        int at = Begin(ShadowNativeOp.ShadowRenderBounds);
        P(renderable);
        I(shadowType);
        ReadOnlySpan<int> a = Ask(at, 6);
        mins = Vec(a, 0);
        maxs = Vec(a, 3);
    }

    public bool ShouldReceiveProjectedTextures(nint renderable, int flags)
    {
        int at = Begin(ShadowNativeOp.ShouldReceiveProjectedTextures);
        P(renderable);
        I(flags);
        return Ask(at, 1)[0] != 0;
    }

    public bool ShadowCastDistance(nint renderable, int shadowType, ref float distance)
    {
        int at = Begin(ShadowNativeOp.ShadowCastDistance);
        P(renderable);
        I(shadowType);
        F(distance);
        ReadOnlySpan<int> a = Ask(at, 2);
        distance = Fl(a, 1);
        return a[0] != 0;
    }

    public bool ShadowCastDirection(nint renderable, int shadowType, ref Vector direction)
    {
        int at = Begin(ShadowNativeOp.ShadowCastDirection);
        P(renderable);
        I(shadowType);
        V(direction);
        ReadOnlySpan<int> a = Ask(at, 4);
        direction = Vec(a, 1);
        return a[0] != 0;
    }

    public bool IsShadowDirty(nint renderable) => false;

    public void MarkShadowDirty(nint renderable, bool dirty)
    {
    }

    public nint ShadowParent(nint renderable) => Ptr(AskOf(ShadowNativeOp.ShadowParent, renderable, 2), 0);

    public nint FirstShadowChild(nint renderable) => Ptr(AskOf(ShadowNativeOp.FirstShadowChild, renderable, 2), 0);

    public nint NextShadowPeer(nint renderable) => Ptr(AskOf(ShadowNativeOp.NextShadowPeer, renderable, 2), 0);

    public void CreateModelInstance(nint renderable)
    {
        int at = Begin(ShadowNativeOp.CreateModelInstance);
        P(renderable);
        End(at);
    }

    public int ModelInstance(nint renderable) => AskOf(ShadowNativeOp.ModelInstance, renderable, 1)[0] & 0xFFFF;

    public int NetworkableEntIndex(nint renderable) => AskOf(ShadowNativeOp.NetworkableEntIndex, renderable, 1)[0];

    // A renderable's move hierarchy is C_BaseEntity's, and no renderable here
    // is one (GetBaseEntity() answers NULL for every one): no children.
    public nint FirstMoveChild(nint renderable) => 0;

    public nint NextMovePeer(nint renderable) => 0;

    //-------------------------------------------------------------------------
    // IShadowDebugOverlay.
    //-------------------------------------------------------------------------

    public void AddLineOverlay(in Vector origin, in Vector dest, int r, int g, int b, bool noDepthTest, float duration)
    {
        int at = Begin(ShadowNativeOp.AddLineOverlay);
        V(origin);
        V(dest);
        I(r);
        I(g);
        I(b);
        I(noDepthTest ? 1 : 0);
        F(duration);
        End(at);
    }

    public void AddTextOverlay(in Vector origin, int lineOffset, string text)
    {
        int at = Begin(ShadowNativeOp.AddTextOverlay);
        V(origin);
        // Stock's AddTextOverlay( origin, 0, fmt, ... ) resolves to the ( origin, float duration, fmt ) overload.
        F(lineOffset);
        S(text);
        End(at);
    }
}

/// <summary>The queue of engine calls a binding keeps, sent when its caller is done.</summary>
public interface IShadowNativeQueue
{
    void Flush();
}
