using SourceSharp.Abi;
using SourceSharp.Client.ClientSystems;
using SourceSharp.Client.Flashlight;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.PixelVisibility;

/// <summary>
/// <see cref="IPixelVisEngine"/> and <see cref="ICurrentView"/> as the game
/// answers them: the query objects, diameters and view through IShimViewClient's
/// slots, the two materials through the effect list's material table (found in
/// TEXTURE_GROUP_CLIENT_EFFECTS and referenced, as stock found and referenced
/// them), the sight line through the client trace with the glow mask and
/// filter, and the messages through the console.
/// </summary>
public sealed class ClientPixelVisEngine(IPixelVisSlots slots, IFxRender fx, IEngineWorldQuery world, Func<int?> dxLevel,
                                         ILog log, Func<string, string?> findConVar) : IPixelVisEngine, ICurrentView
{
    /// <summary>engine->GetDXSupportLevel(); 0 with no engine client, which asks no query.</summary>
    public int DxSupportLevel() => dxLevel() ?? 0;

    public int CreateQuery() => slots.QueryCreate();

    public void DestroyQuery(int query) => slots.QueryDestroy(query);

    public void ResetQuery(int query) => slots.QueryReset(query);

    public void PixelsRendered(int visible, int count, out int visiblePixels, out int countPixels)
        => slots.QueryPixels(visible, count, out visiblePixels, out countPixels);

    public void PixelDiameters(ReadOnlySpan<float> spheres, Span<float> diameters) => slots.Diameters(spheres, diameters);

    public int FindMaterial(string name) => fx.MaterialFind(name);

    public void ReleaseMaterial(int material) => fx.MaterialRelease(material);

    public float TraceGlowSight(in Vector start, in Vector end)
        => world.ClientTraceLine(start, end, TraceMask.GlowSight, TraceFilter.Glow).Fraction;

    /// <summary>tier0's DevMsg: printed when <c>developer</c> is at least the level.</summary>
    public void DevMsg(int level, string text)
    {
        if (VariantParse.Atof(findConVar("developer")) >= level)
            log.Msg(text);
    }

    public void Msg(string text) => log.Msg(text);

    public CurrentViewState Read() => slots.View();
}

/// <summary>gpGlobals' frame count and frame time, from the client's own CGlobalVars.</summary>
public sealed class PixelVisFrame(IEngineEnvironment environment) : IPixelVisFrame
{
    public int FrameCount => environment.Globals.IsAvailable ? environment.Globals.FrameCount : 0;

    public float FrameTime => environment.Globals.IsAvailable ? environment.Globals.FrameTime : 0f;
}

/// <summary>
/// The ConVars the pixel visibility reads: c_pixel_visibility.cpp's four and
/// glow_overlay.cpp's building_cubemaps, read by name through the convar watch
/// while those files declare them. r_drawpixelvisibility's change is stock's
/// change callback (<see cref="DrawChanged"/>): the proxies drawn visibly.
/// </summary>
public sealed class PixelVisSettings(WatchedConVarValues watch) : IPixelVisSettings
{
    public const string PartialName = "r_pixelvisibility_partial";
    public const string EnabledName = "r_dopixelvisibility";
    public const string DrawName = "r_drawpixelvisibility";
    public const string SpewName = "r_pixelvisibility_spew";
    public const string BuildingCubemapsName = "building_cubemaps";

    /// <summary>The names the watch reads.</summary>
    public static string[] Names() => [PartialName, EnabledName, DrawName, SpewName, BuildingCubemapsName];

    /// <summary>r_drawpixelvisibility changed: its new value.</summary>
    public Action<bool>? DrawChanged { get; set; }

    public void Start() => watch.Watch(Names());

    /// <summary>
    /// A watched convar changed; false for an id this set does not hold. A
    /// change to r_drawpixelvisibility's string is stock's callback firing.
    /// </summary>
    public bool OnChanged(int id, string newValue)
    {
        string? before = watch.Raw(DrawName);
        if (!watch.OnChanged(id, newValue))
            return false;
        if (!string.Equals(before, watch.Raw(DrawName), StringComparison.Ordinal))
            DrawChanged?.Invoke(watch.Bool(DrawName));
        return true;
    }

    public bool Partial => watch.Bool(PartialName);

    public bool Enabled => watch.Bool(EnabledName);

    public bool Spew => watch.Bool(SpewName);

    public bool BuildingCubemaps => watch.Bool(BuildingCubemapsName);
}

/// <summary>
/// What native's view end and scene end reach (IManagedPixelVisClient): the
/// view's proxies written as shell_pixelvis_client.h's command words.
/// </summary>
public sealed class PixelVisHost(PixelVisibilitySystem system) : IClientPixelVisHost
{
    /// <summary>A proxy's words: kind, query, material and twelve vertices (SS_PIXELVIS_PROXY_WORDS).</summary>
    public const int ProxyWords = 3 + 36;

    /// <summary>The view native hands over (SS_PIXELVIS_VIEW_FLOATS).</summary>
    public const int ViewFloats = 29;

    private readonly Writer _writer = new();

    /// <summary>Views ended since the load (pixelvis's share of the render report).</summary>
    public long Views { get; private set; }

    public int EndView(ReadOnlySpan<float> view, Span<int> commands)
    {
        _writer.Words.Clear();
        if (view.Length < ViewFloats)
            return 0;
        var matrix = view.Slice(13, 16).ToArray();
        var v = new PixelVisView((int)view[0], new Vector(view[1], view[2], view[3]), new Vector(view[4], view[5], view[6]),
                                 new Vector(view[7], view[8], view[9]), new Vector(view[10], view[11], view[12]), matrix);
        system.EndView(v, _writer);
        Views++;
        return Commands(commands, _writer.Words.Count);
    }

    public int Commands(Span<int> commands) => Commands(commands, _writer.Words.Count);

    private int Commands(Span<int> commands, int total)
    {
        if (total <= commands.Length)
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_writer.Words).CopyTo(commands);
        return total;
    }

    public void EndScene() => system.EndScene();

    private sealed class Writer : IPixelVisDraw
    {
        public readonly List<int> Words = [];

        public void Bind(int material)
        {
            Words.Add((int)PixelVisCommand.Bind);
            Words.Add(material);
        }

        public void DepthRange(float near, float far)
        {
            Words.Add((int)PixelVisCommand.DepthRange);
            Words.Add(BitConverter.SingleToInt32Bits(near));
            Words.Add(BitConverter.SingleToInt32Bits(far));
        }

        public void Proxy(int query, int material, ReadOnlySpan<Vector> triangles)
        {
            Words.Add((int)PixelVisCommand.Proxy);
            Words.Add(query);
            Words.Add(material);
            for (int i = 0; i < 12; i++)
            {
                Vector p = i < triangles.Length ? triangles[i] : default;
                Words.Add(BitConverter.SingleToInt32Bits(p.X));
                Words.Add(BitConverter.SingleToInt32Bits(p.Y));
                Words.Add(BitConverter.SingleToInt32Bits(p.Z));
            }
        }
    }
}

/// <summary>kClientSystems[]'s "CPixelVisibilitySystem" row: the level's start and end.</summary>
public sealed class PixelVisibilityClientSystem(PixelVisibilitySystem system) : ManagedClientSystem
{
    public override string Name => "CPixelVisibilitySystem";

    public override void LevelInitPreEntity() => system.LevelInitPreEntity();

    public override void LevelShutdownPostEntity() => system.LevelShutdownPostEntity();
}

/// <summary>A particle glow's question (CSimpleGlowEmitter's): the default proxy at its origin, on its handle.</summary>
public sealed class ParticlePixelVisibility(IPixelVisibility pixelVis) : IParticlePixelVisibility
{
    public float FractionVisible(Vector origin, ref int queryHandle) => pixelVis.FractionVisible(new PixelVisParams(origin), ref queryHandle);
}

/// <summary>A sprite's two glow questions (C_SpriteRenderer::GlowBlend's): the proxy it names, and the untraced distance.</summary>
public sealed class PixelVisSpriteGlow(IPixelVisibility pixelVis) : IFxSpriteGlow
{
    public float FractionVisible(in Vector position, float proxySize, float proxyAspect, ref int queryHandle)
        => pixelVis.FractionVisible(new PixelVisParams(position, proxySize, proxyAspect), ref queryHandle);

    public float SightDistance(in Vector position) => pixelVis.GlowSightDistance(position, false);
}
