using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Detail;

/// <summary>
/// What the detail props ask the engine, through its forwarders
/// (plan_engine_forwarders.md): IVEngineClient's game lumps, LoadModel and the
/// sprites' lighting (EngineClientFwd), IVModelInfo's two dictionary questions
/// (ModelInfoFwd). Bound on first use, so a reloaded assembly binds again; the
/// engine outlives every reload. A table that cannot be bound answers as an
/// empty map would: no lump, no model, no light.
/// </summary>
internal sealed unsafe class DetailPropsEngine(NativeInterfaceFactory interfaces, ILog log) : IDetailPropEngine
{
    private const string ModelInfoVersion = "VModelInfoClient006";

    private void* _engine;
    private EngineClientFwd* _engineFwd;
    private void* _modelInfo;
    private ModelInfoFwd* _modelInfoFwd;
    private bool _bound;

    private bool Bind()
    {
        if (_bound)
            return _engineFwd != null && _modelInfoFwd != null;
        _bound = true;

        if (!Forwarders.TryAcquire(interfaces, SourceSharpFactoryType.AppSystem, EngineClient.InterfaceVersion,
                                   out _engine, out _engineFwd, out string why))
        {
            log.Warning($"[SourceSharp/Detail] the engine client is not bound ({why}): no detail props\n");
            return false;
        }
        if (!Forwarders.TryAcquire(interfaces, SourceSharpFactoryType.AppSystem, ModelInfoVersion,
                                   out _modelInfo, out _modelInfoFwd, out why))
        {
            log.Warning($"[SourceSharp/Detail] model info is not bound ({why}): no detail props\n");
            return false;
        }
        return true;
    }

    public int GameLumpVersion(int lumpId) => Bind() ? _engineFwd->GameLumpVersion(_engine, lumpId) : 0;

    public int GameLumpSize(int lumpId) => Bind() ? _engineFwd->GameLumpSize(_engine, lumpId) : 0;

    public bool LoadGameLump(int lumpId, Span<byte> buffer)
    {
        if (!Bind())
            return false;
        fixed (byte* p = buffer)
            return _engineFwd->LoadGameLump(_engine, lumpId, p, buffer.Length) != 0;
    }

    public nint LoadModel(string name, bool prop)
    {
        if (!Bind())
            return 0;
        int n = Encoding.UTF8.GetByteCount(name);
        Span<byte> text = n < 512 ? stackalloc byte[n + 1] : new byte[n + 1];
        Encoding.UTF8.GetBytes(name, text);
        text[n] = 0;
        fixed (byte* p = text)
            return (nint)_engineFwd->LoadModel(_engine, p, prop ? (byte)1 : (byte)0);
    }

    public bool IsModelVertexLit(nint model)
        => Bind() && model != 0 && _modelInfoFwd->IsModelVertexLit(_modelInfo, (void*)model) != 0;

    public bool ModelHasMaterialProxy(nint model)
        => Bind() && model != 0 && _modelInfoFwd->ModelHasMaterialProxy(_modelInfo, (void*)model) != 0;

    public Vector LinearToGamma(Vector linear)
    {
        if (!Bind())
            return linear;
        Vector gamma = default;
        _engineFwd->LinearToGamma(_engine, &linear, &gamma);
        return gamma;
    }

    public float LightStyleValue(int style) => Bind() ? _engineFwd->LightStyleValue(_engine, style) : 0.0f;

    public Vector ComputeDynamicLighting(Vector point, Vector normal)
    {
        Vector color = default;
        if (Bind())
            _engineFwd->ComputeDynamicLighting(_engine, &point, &normal, &color);
        return color;
    }

    public void Warning(string text) => log.Warning(text);
}

/// <summary>Where the client module gets <see cref="DetailPropsEngine"/>.</summary>
public static class DetailPropsEngines
{
    /// <summary>The detail props' engine on <paramref name="engine"/>'s own side.</summary>
    public static IDetailPropEngine For(ModuleEngine engine, ILog log) => new DetailPropsEngine(engine.Interfaces, log);
}
