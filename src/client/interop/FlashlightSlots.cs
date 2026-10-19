using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>One flashlight trace's answer: stock's DidHit(), the fraction and the end.</summary>
public readonly record struct FlashlightTrace(bool Hit, float Fraction, Vector End);

/// <summary>
/// What the managed flashlight (SourceSharp.Client.Flashlight, C7 cluster 12)
/// asks native and tells it: the client traces with stock's
/// CTraceFilterSkipPlayerAndViewModel, the shadow manager's four flashlight
/// calls and the texture they name (IShimFlashlightClient), the engine's DX
/// level and the debug overlay (engine forwarders). Each is one call carried
/// out; every decision is the caller's.
/// </summary>
public interface IFlashlightSlots
{
    /// <summary>engine->GetDXSupportLevel(); null when the engine client is not bound.</summary>
    int? DXSupportLevel();

    /// <summary>The hardware config's SupportsBorderColor.</summary>
    bool SupportsBorderColor();

    /// <summary>A texture held by name for the shadow manager; its id, or -1.</summary>
    int Texture(string name);

    /// <summary>CreateFlashlight; the shadow handle, or -1.</summary>
    int Create(in FlashlightState state);

    /// <summary>UpdateFlashlightState.</summary>
    void Update(int handle, in FlashlightState state);

    /// <summary>UpdateProjectedTexture( handle, true ).</summary>
    void Project(int handle);

    /// <summary>DestroyFlashlight.</summary>
    void Destroy(int handle);

    /// <summary>UTIL_TraceHull with stock's flashlight filter.</summary>
    FlashlightTrace TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask);

    /// <summary>UTIL_TraceLine with stock's flashlight filter.</summary>
    FlashlightTrace TraceLine(Vector start, Vector end, TraceMask mask);

    /// <summary>debugoverlay->AddBoxOverlay.</summary>
    void BoxOverlay(Vector origin, Vector mins, Vector maxs, QAngle orientation, int r, int g, int b, int a, float duration);

    /// <summary>debugoverlay->AddLineOverlay.</summary>
    void LineOverlay(Vector origin, Vector dest, int r, int g, int b, bool noDepthTest, float duration);
}

/// <summary>The real slots: IShimFlashlightClient, IShimViewClient's traces, and two engine forwarder tables.</summary>
internal sealed unsafe class FlashlightSlots(EngineSide api) : IFlashlightSlots
{
    private const string DebugOverlayVersion = "VDebugOverlay003";

    private void* _engine;
    private EngineClientFwd* _engineFwd;
    private void* _overlay;
    private DebugOverlayFwd* _overlayFwd;
    private bool _bound;

    private void Bind()
    {
        if (_bound || !api.ClientAttached)
            return;
        _bound = true;
        Forwarders.TryAcquire(api.Interfaces, SourceSharpFactoryType.AppSystem, EngineClient.InterfaceVersion,
                              out _engine, out _engineFwd, out _);
        Forwarders.TryAcquire(api.Interfaces, SourceSharpFactoryType.AppSystem, DebugOverlayVersion,
                              out _overlay, out _overlayFwd, out _);
    }

    public int? DXSupportLevel()
    {
        Bind();
        return _engineFwd == null ? null : _engineFwd->GetDXSupportLevel(_engine);
    }

    public bool SupportsBorderColor() => api.ClientAttached && api.ClientShims.FlashlightClient.FlashlightSupportsBorderColor() != 0;

    public int Texture(string name)
    {
        if (!api.ClientAttached)
            return -1;
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return api.ClientShims.FlashlightClient.FlashlightTexture(p);
    }

    public int Create(in FlashlightState state)
    {
        if (!api.ClientAttached)
            return -1;
        fixed (FlashlightState* p = &state)
            return api.ClientShims.FlashlightClient.FlashlightCreate(p);
    }

    public void Update(int handle, in FlashlightState state)
    {
        if (!api.ClientAttached)
            return;
        fixed (FlashlightState* p = &state)
            api.ClientShims.FlashlightClient.FlashlightUpdate(handle, p);
    }

    public void Project(int handle)
    {
        if (api.ClientAttached)
            api.ClientShims.FlashlightClient.FlashlightProject(handle);
    }

    public void Destroy(int handle)
    {
        if (api.ClientAttached)
            api.ClientShims.FlashlightClient.FlashlightDestroy(handle);
    }

    public FlashlightTrace TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask)
    {
        if (!api.ClientAttached)
            return new FlashlightTrace(false, 1f, end);
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };
        float* endPos = stackalloc float[3];
        float fraction = 1f;
        int hit = api.ClientShims.ViewClient.ClientTraceHull(s, e, lo, hi, (int)mask, (int)TraceFilter.SkipPlayerAndViewModel, -1,
                                                  &fraction, endPos);
        return new FlashlightTrace(hit != 0, fraction, new Vector(endPos[0], endPos[1], endPos[2]));
    }

    public FlashlightTrace TraceLine(Vector start, Vector end, TraceMask mask)
    {
        if (!api.ClientAttached)
            return new FlashlightTrace(false, 1f, end);
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* endPos = stackalloc float[3];
        float* normal = stackalloc float[3];
        float fraction = 1f;
        int hitbox = 0, entity = 0, surfaceProps = 0, surfaceFlags = 0;
        int hit = api.ClientShims.ViewClient.ClientTraceLine(s, e, (int)mask, (int)TraceFilter.SkipPlayerAndViewModel, -1,
                                                  &fraction, endPos, normal, &hitbox, &entity, &surfaceProps,
                                                  &surfaceFlags);
        return new FlashlightTrace(hit != 0, fraction, new Vector(endPos[0], endPos[1], endPos[2]));
    }

    public void BoxOverlay(Vector origin, Vector mins, Vector maxs, QAngle orientation, int r, int g, int b, int a,
                           float duration)
    {
        Bind();
        if (_overlayFwd == null)
            return;
        Vector o = origin, lo = mins, hi = maxs;
        QAngle q = orientation;
        _overlayFwd->AddBoxOverlay(_overlay, &o, &lo, &hi, &q, r, g, b, a, duration);
    }

    public void LineOverlay(Vector origin, Vector dest, int r, int g, int b, bool noDepthTest, float duration)
    {
        Bind();
        if (_overlayFwd == null)
            return;
        Vector o = origin, d = dest;
        _overlayFwd->AddLineOverlay(_overlay, &o, &d, r, g, b, noDepthTest ? (byte)1 : (byte)0, duration);
    }
}
