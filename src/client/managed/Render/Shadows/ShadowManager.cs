//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

/// <summary>
/// The client shadow manager (C7 cluster 12, S1 of
/// docs/plans/c7_shadow_manager.md), ported whole from clientshadowmgr.cpp's
/// CClientShadowMgr: which shadows exist and of what kind (D1), the modes
/// (D2), when each is re-projected (D3), how (D4), the flashlights (D5), the
/// receivers each lands on (D6), the render-to-texture pass, its budget, its
/// LOD fallback and its atlas (D7), the flashlight depth textures (D8) and the
/// debug overlays and console commands (D9).
///
/// <para>
/// THE NATIVE OBJECTS ARE NUMBERS HERE. A renderable is its pointer, an
/// engine shadow its ShadowHandle_t, a material or render target an
/// <see cref="IShadowMaterials"/> id; what this asks of one, and every call
/// stock made on the engine, goes through the seams it is built with, in
/// stock's order. Nothing is wired yet (S1): the stock manager still runs;
/// ShadowManagerOracleTests holds this to the golden stock wrote.
/// </para>
///
/// <para>
/// THE ORDER IS STOCK'S. Client shadow handles come as CUtlLinkedList's do
/// (<see cref="UtlLinkedListModel"/>), and every walk over the shadows is the
/// list's order; the dirty set is walked in handle order; the atlas hands out
/// fragments as stock's LRU does (<see cref="ShadowTextureAllocator"/>).
/// </para>
///
/// <para>
/// THE QUIRKS ARE STOCK'S TOO, each held by the golden: a shadow retired
/// during PreRender's walk (its entity gone) ends the walk, and the shadows
/// after it in handle order leave the dirty set unprojected with their
/// renderables still marked dirty; SetViewFlashlightState hands the engine a
/// client handle.
/// </para>
/// </summary>
public sealed partial class ShadowManager : IReloadParticipant, IShadowProxySource
{
    // clientshadowmgr.cpp's constants.
    private const float TexelSizePerCasterSize = 2.0f;
    private const int MaxFalloffAmount = 240;

    // The material system's names (imaterialsystem.h, imageformat.h, texture_group_names.h).
    private const string TextureGroupDecal = "Decal textures";
    private const int RtSizeOffscreen = 5;
    private const int MaterialRtDepthNone = 2;
    private const int MaterialFogNone = 0;
    private const int MaterialHeightClipModeDisable = 0;
    private const int MaterialView = 0;
    private const int MaterialProjection = 1;
    private const int ModBrush = 1;
    private const int ModStudio = 3;

    /// <summary>INVALID_CLIENTENTITY_HANDLE.</summary>
    public const uint InvalidEntity = 0xFFFFFFFF;

    private const int Invalid = ShadowFlags.InvalidHandle;

    /// <summary>One shadow's state (stock's ClientShadow_t).</summary>
    internal struct ClientShadow
    {
        public uint Entity;
        public int ShadowHandle;
        public int LeafShadow;
        public int Flags;
        public ShadowMatrix WorldToShadow;
        public float WorldSizeX;
        public float WorldSizeY;
        public Vector LastOrigin;
        public QAngle LastAngles;
        public int ShadowTexture;
        public int RenderFrame;
        public uint TargetEntity;
        public bool HasState;
        public FlashlightState State;
    }

    private readonly IShadowEngine _engine;
    private readonly IShadowMaterials _materials;
    private readonly IShadowRenderContext _context;
    private readonly IShadowWorld _world;
    private readonly IShadowRenderables _renderables;
    private readonly IShadowDebugOverlay _debugOverlay;
    private readonly IShadowLeafSystem _leafSystem;
    private readonly IShadowSettings _settings;
    private readonly IShadowConsole _console;

    private Vector _simpleShadowDir;
    private byte _ambientR, _ambientG, _ambientB;
    private int _simpleShadow;
    private int _renderShadow;
    private int _renderModelShadow;
    private int _dummyColorTexture;
    private readonly UtlLinkedListModel _shadowHandles = new();
    private ClientShadow[] _shadows = new ClientShadow[64];
    private readonly ShadowTextureAllocator _allocator;
    private readonly VisibleShadowList _visible;

    private bool _renderToTextureActive;
    private bool _renderTargetNeedsClear;
    private bool _updatingDirtyShadows;
    private float _shadowCastDist;
    private float _minShadowArea;
    private readonly DirtyShadowSet _dirtyShadows = new();
    private readonly List<int> _transparentShadows = [];

    private bool _depthTextureActive;
    private int _depthTextureResolution;
    private readonly List<int> _depthTextureCache = [];
    private readonly List<bool> _depthTextureCacheLocks = [];
    private int _maxDepthTextureShadows;

    // Stock's function-local statics, the manager's here.
    private bool _depthOverflowWarned;
    private int _shadowMapWarnings;
    private bool _break;

    // fork's cs_cl_shadow_report counter.
    private int _rttShadowTexturesDrawn;

    // Scratch, reused.
    private readonly List<int> _leafScratch = [];
    private readonly List<int> _walkScratch = [];

    public ShadowManager(IShadowEngine engine, IShadowMaterials materials, IShadowRenderContext context, IShadowWorld world,
                         IShadowRenderables renderables, IShadowDebugOverlay debugOverlay, IShadowLeafSystem leafSystem,
                         IShadowSettings settings, IShadowConsole console)
    {
        _engine = engine;
        _materials = materials;
        _context = context;
        _world = world;
        _renderables = renderables;
        _debugOverlay = debugOverlay;
        _leafSystem = leafSystem;
        _settings = settings;
        _console = console;
        _allocator = new ShadowTextureAllocator(materials);
        _visible = new VisibleShadowList(this);
        _depthTextureResolution = settings.FlashlightDepthResolution;
    }

    internal ref ClientShadow Shadow(int handle) => ref _shadows[handle];

    /// <summary>Whether a handle names a live shadow.</summary>
    public bool IsShadow(int handle) => _shadowHandles.IsValid(handle);

    /// <summary>The live shadows, in the list's order.</summary>
    public IEnumerable<int> Shadows()
    {
        for (int h = _shadowHandles.Head; h != Invalid; h = _shadowHandles.Next(h))
            yield return h;
    }

    public bool RenderToTextureActive => _renderToTextureActive;

    /// <summary>The flags a shadow has now (the facts' and the report's view).</summary>
    public int FlagsOf(int handle) => _shadows[handle].Flags;

    /// <summary>The engine shadow behind a client shadow.</summary>
    public int EngineShadowOf(int handle) => _shadows[handle].ShadowHandle;

    //-------------------------------------------------------------------------
    // D2: initialisation, shutdown, the modes.
    //-------------------------------------------------------------------------

    /// <summary><c>Init</c>.</summary>
    public bool Init()
    {
        _renderTargetNeedsClear = false;
        _simpleShadow = _materials.InitMaterial("decals/simpleshadow", TextureGroupDecal);

        SetShadowDirection(new Vector((float)0.1, (float)0.1, -1));
        SetShadowDistance(50);
        SetShadowBlobbyCutoffArea((float)0.005);

        bool tools = _materials.ToolsMode();
        _maxDepthTextureShadows = tools ? 4 : 1;

        bool lowEnd = _materials.DXSupportLevel() < 80;

        if (!lowEnd && _settings.RenderToTexture)
            InitRenderToTextureShadows();

        if (_settings.FlashlightDepthTexture && !_materials.SupportsShadowDepthTextures())
        {
            _settings.SetFlashlightDepthTexture(0);
            ShutdownDepthTextureShadows();
        }

        if (!lowEnd && _settings.FlashlightDepthTexture)
            InitDepthTextureShadows();

        _materials.AddRestoreFunc();
        return true;
    }

    /// <summary><c>Shutdown</c>: the shadow list is emptied before the modes are shut down, as stock does.</summary>
    public void Shutdown()
    {
        _materials.ShutdownMaterial(_simpleShadow);
        _simpleShadow = 0;
        _shadowHandles.RemoveAll();
        ShutdownRenderToTextureShadows();
        ShutdownDepthTextureShadows();
        _materials.RemoveRestoreFunc();
    }

    private void InitDepthTextureShadows()
    {
        if (_depthTextureActive)
            return;
        _depthTextureActive = true;

        int dstFormat = _materials.ShadowDepthTextureFormat();
        int nullFormat = _materials.NullTextureFormat();
        _materials.BeginRenderTargetAllocation();

        int resolution = _settings.FlashlightDepthResolution;
        _dummyColorTexture = _materials.InitRenderTarget("_rt_ShadowDummy", resolution, resolution, RtSizeOffscreen, nullFormat,
                                                         MaterialRtDepthNone, false);

        _depthTextureCache.Clear();
        _depthTextureCacheLocks.Clear();
        for (int i = 0; i < _maxDepthTextureShadows; i++)
        {
            int depthTex = _materials.InitRenderTarget("_rt_ShadowDepthTexture_" + i, _depthTextureResolution, _depthTextureResolution,
                                                       RtSizeOffscreen, dstFormat, MaterialRtDepthNone, false);
            if (i == 0)
            {
                // Shadow may be resized during allocation (due to resolution constraints etc)
                _depthTextureResolution = _materials.ActualWidth(depthTex);
                _settings.SetFlashlightDepthResolution(_depthTextureResolution);
            }
            _depthTextureCache.Add(depthTex);
            _depthTextureCacheLocks.Add(false);
        }

        _materials.EndRenderTargetAllocation();
    }

    private void ShutdownDepthTextureShadows()
    {
        if (!_depthTextureActive)
            return;
        _materials.ShutdownTexture(_dummyColorTexture);
        _dummyColorTexture = 0;
        while (_depthTextureCache.Count > 0)
        {
            int last = _depthTextureCache.Count - 1;
            _materials.ShutdownTexture(_depthTextureCache[last]);
            _depthTextureCacheLocks.RemoveAt(last);
            _depthTextureCache.RemoveAt(last);
        }
        _depthTextureActive = false;
    }

    private void InitRenderToTextureShadows()
    {
        if (_renderToTextureActive)
            return;
        _renderToTextureActive = true;
        _renderShadow = _materials.InitMaterial("decals/rendershadow", TextureGroupDecal);
        _renderModelShadow = _materials.InitMaterial("decals/rendermodelshadow", TextureGroupDecal);
        _allocator.Init();
        _allocator.Reset();
        _renderTargetNeedsClear = true;

        float fr = _ambientR / 255.0f;
        float fg = _ambientG / 255.0f;
        float fb = _ambientB / 255.0f;
        _materials.ColorModulate(_renderShadow, fr, fg, fb);
        _materials.ColorModulate(_renderModelShadow, fr, fg, fb);

        for (int i = _shadowHandles.Head; i != Invalid; i = _shadowHandles.Next(i))
        {
            if ((_shadows[i].Flags & ShadowFlags.UseRenderToTexture) != 0)
            {
                SetupRenderToTextureShadow(i);
                MarkRenderToTextureShadowDirty(i);
                _engine.SetShadowMaterial(_shadows[i].ShadowHandle, _renderShadow, _renderModelShadow, i);
            }
        }
    }

    private void ShutdownRenderToTextureShadows()
    {
        if (!_renderToTextureActive)
            return;
        for (int i = _shadowHandles.Head; i != Invalid; i = _shadowHandles.Next(i))
        {
            CleanUpRenderToTextureShadow(i);
            _engine.SetShadowMaterial(_shadows[i].ShadowHandle, _simpleShadow, _simpleShadow, Invalid);
            _engine.SetShadowTexCoord(_shadows[i].ShadowHandle, 0, 0, 1, 1);
            ClearExtraClipPlanes(i);
        }
        _materials.ShutdownMaterial(_renderShadow);
        _materials.ShutdownMaterial(_renderModelShadow);
        _renderShadow = 0;
        _renderModelShadow = 0;
        _allocator.DeallocateAllTextures();
        _allocator.Shutdown();
        _materials.UncacheUnusedMaterials();
        _renderToTextureActive = false;
    }

    /// <summary><c>SetShadowColor( r, g, b )</c>.</summary>
    public void SetShadowColor(byte r, byte g, byte b)
    {
        float fr = r / 255.0f;
        float fg = g / 255.0f;
        float fb = b / 255.0f;
        _materials.ColorModulate(_simpleShadow, fr, fg, fb);
        if (_renderToTextureActive)
        {
            _materials.ColorModulate(_renderShadow, fr, fg, fb);
            _materials.ColorModulate(_renderModelShadow, fr, fg, fb);
        }
        _ambientR = r;
        _ambientG = g;
        _ambientB = b;
    }

    public (byte R, byte G, byte B) ShadowColor => (_ambientR, _ambientG, _ambientB);

    /// <summary><c>LevelInitPreEntity</c>: the shadow colour from the ambient light, and the atlas reset.</summary>
    public void LevelInitPreEntity()
    {
        _updatingDirtyShadows = false;

        Vector ambient = _world.AmbientLightColor();
        ambient = new Vector(ambient.X * 3, ambient.Y * 3, ambient.Z * 3);
        ambient = new Vector(ambient.X + 0.3f, ambient.Y + 0.3f, ambient.Z + 0.3f);

        byte r = ToColorByte(ambient.X);
        byte g = ToColorByte(ambient.Y);
        byte b = ToColorByte(ambient.Z);
        SetShadowColor(r, g, b);

        if (_renderToTextureActive)
        {
            _allocator.Reset();
            _renderTargetNeedsClear = true;
        }
    }

    // `ambientColor[0] > 1.0 ? 255 : 255 * ambientColor[0]`, narrowed to unsigned char.
    private static byte ToColorByte(float c) => unchecked((byte)(int)(c > 1.0 ? 255f : 255 * c));

    /// <summary><c>LevelShutdownPostEntity</c>: every shadow left destroyed, the atlas emptied, the game's control released.</summary>
    public void LevelShutdownPostEntity()
    {
        int h = _shadowHandles.Head;
        while (h != Invalid)
        {
            int next = _shadowHandles.Next(h);
            DestroyShadow(h);
            h = next;
        }
        if (_renderToTextureActive)
            _allocator.DeallocateAllTextures();
        _settings.SetShadowsGameControl(-1);
    }

    /// <summary><c>RestoreRenderState</c> (alt-tab): every texture dirty, the colour set again, the atlas cleared on next use.</summary>
    public void RestoreRenderState()
    {
        for (int h = _shadowHandles.Head; h != Invalid; h = _shadowHandles.Next(h))
            _shadows[h].Flags |= ShadowFlags.TextureDirty;
        SetShadowColor(_ambientR, _ambientG, _ambientB);
        _renderTargetNeedsClear = true;
    }

    private void SetupRenderToTextureShadow(int h)
    {
        ref ClientShadow shadow = ref _shadows[h];
        nint renderable = _renderables.RenderableOf(shadow.Entity);
        if (renderable == 0)
            return;

        _renderables.ShadowRenderBounds(renderable, GetActualShadowCastType(h), out Vector mins, out Vector maxs);
        Vector size = new(maxs.X - mins.X, maxs.Y - mins.Y, maxs.Z - mins.Z);
        float maxSize = size.X > size.Y ? size.X : size.Y;
        maxSize = maxSize > size.Z ? maxSize : size.Z;

        int texelCount = (int)(TexelSizePerCasterSize * maxSize);
        int textureSize = 1;
        while (textureSize < texelCount)
            textureSize <<= 1;

        shadow.ShadowTexture = _allocator.AllocateTexture(textureSize, textureSize);
    }

    private void CleanUpRenderToTextureShadow(int h)
    {
        ref ClientShadow shadow = ref _shadows[h];
        if (_renderToTextureActive && (shadow.Flags & ShadowFlags.UseRenderToTexture) != 0)
        {
            _allocator.DeallocateTexture(shadow.ShadowTexture);
            shadow.ShadowTexture = Invalid;
        }
    }

    /// <summary><c>UpdateAllShadows</c>: every shadow but the flashlights dirtied and forced.</summary>
    private void UpdateAllShadows()
    {
        for (int i = _shadowHandles.Head; i != Invalid; i = _shadowHandles.Next(i))
        {
            if ((_shadows[i].Flags & ShadowFlags.Flashlight) != 0)
                continue;
            nint renderable = _renderables.RenderableOf(_shadows[i].Entity);
            if (renderable == 0)
                continue;
            AddToDirtyShadowList(renderable, true);
        }
    }

    /// <summary><c>SetShadowDirection( dir )</c>: normalised; with render-to-texture on, every shadow re-projects.</summary>
    public void SetShadowDirection(Vector dir)
    {
        _simpleShadowDir = ShadowMath.Normalize(dir);
        if (_renderToTextureActive)
            UpdateAllShadows();
    }

    /// <summary><c>GetShadowDirection()</c>: straight down while blobs are all there is.</summary>
    public Vector GetShadowDirection() => _renderToTextureActive ? _simpleShadowDir : new Vector(0, 0, -1);

    private float GetShadowDistance(nint renderable)
    {
        float dist = _shadowCastDist;
        _renderables.ShadowCastDistance(renderable, GetActualShadowCastType(renderable), ref dist);
        return dist;
    }

    private Vector GetShadowDirection(nint renderable)
    {
        Vector result = GetShadowDirection();
        _renderables.ShadowCastDirection(renderable, GetActualShadowCastType(renderable), ref result);
        return result;
    }

    /// <summary><c>SetShadowDistance( d )</c>: every shadow re-projects.</summary>
    public void SetShadowDistance(float maxDistance)
    {
        _shadowCastDist = maxDistance;
        UpdateAllShadows();
    }

    public float ShadowDistance => _shadowCastDist;

    public void SetShadowBlobbyCutoffArea(float minArea) => _minShadowArea = minArea;

    public float BlobbyCutoffArea => _minShadowArea;

    /// <summary><c>SetFalloffBias( h, bias )</c>.</summary>
    public void SetFalloffBias(int handle, byte bias) => _engine.SetFalloffBias(_shadows[handle].ShadowHandle, bias);

    /// <summary><c>SetShadowsDisabled( b )</c>: r_shadows_gamecontrol, 0 to disable.</summary>
    public void SetShadowsDisabled(bool disabled) => _settings.SetShadowsGameControl(disabled ? 0 : 1);

    /// <summary><c>RenderShadowTexture( w, h )</c>: the atlas drawn on a quad, with render-to-texture on.</summary>
    public void RenderShadowTexture(int w, int h)
    {
        if (_renderToTextureActive)
            _context.DrawTextureQuad(_renderShadow, w, h);
    }

    //-------------------------------------------------------------------------
    // D1: which shadows exist.
    //-------------------------------------------------------------------------

    private int CreateProjectedTexture(uint entity, int flags)
    {
        if ((flags & ShadowFlags.Flashlight) == 0)
        {
            nint renderable = _renderables.RenderableOf(entity);
            if (renderable == 0)
                return Invalid;
            int modelType = _renderables.ModelType(_renderables.Model(renderable));
            if (modelType == ModBrush)
                flags |= ShadowFlags.BrushModel;
        }

        int h = _shadowHandles.AddToTail();
        if (h >= _shadows.Length)
            Array.Resize(ref _shadows, Math.Max(_shadows.Length * 2, h + 1));
        _shadows[h] = default;
        ref ClientShadow shadow = ref _shadows[h];
        shadow.Entity = entity;
        shadow.LeafShadow = _leafSystem.AddShadow(h, flags & 0xFFFF);
        shadow.Flags = flags & 0xFFFF;
        shadow.RenderFrame = -1;
        shadow.LastOrigin = new Vector(float.MaxValue, float.MaxValue, float.MaxValue);
        shadow.LastAngles = new QAngle(float.MaxValue, float.MaxValue, float.MaxValue);
        shadow.ShadowTexture = Invalid;
        shadow.TargetEntity = InvalidEntity;

        int material = _simpleShadow;
        int modelMaterial = _simpleShadow;
        int proxyData = Invalid;

        if (_renderToTextureActive && (flags & ShadowFlags.UseRenderToTexture) != 0)
        {
            SetupRenderToTextureShadow(h);
            material = _renderShadow;
            modelMaterial = _renderModelShadow;
            proxyData = h;
        }

        if ((flags & ShadowFlags.UseDepthTexture) != 0)
        {
            material = _renderShadow;
            modelMaterial = _renderModelShadow;
            proxyData = h;
        }

        int createShadowFlags = (flags & ShadowFlags.Flashlight) != 0 ? ShadowFlags.CreateFlashlight : ShadowFlags.CreateCacheVerts;
        _shadows[h].ShadowHandle = _engine.CreateShadowEx(material, modelMaterial, proxyData, createShadowFlags);
        return h;
    }

    /// <summary><c>CreateFlashlight( state )</c>: a flashlight that lights the world, with a depth texture when it wants shadows and they are on.</summary>
    public int CreateFlashlight(in FlashlightState lightState)
    {
        int shadowFlags = ShadowFlags.Flashlight | ShadowFlags.LightWorld;
        if (lightState.EnableShadows != 0 && _settings.FlashlightDepthTexture)
            shadowFlags |= ShadowFlags.UseDepthTexture;

        int shadowHandle = CreateProjectedTexture(InvalidEntity, shadowFlags);
        UpdateFlashlightState(shadowHandle, lightState);
        UpdateProjectedTexture(shadowHandle, true);
        return shadowHandle;
    }

    /// <summary><c>CreateShadow( entity, flags )</c>: the projected-texture kind replaced with SHADOW, the texture dirty, and dirtied.</summary>
    public int CreateShadow(uint entity, int flags)
    {
        flags &= ~ShadowFlags.ProjectedTextureTypeMask;
        flags |= ShadowFlags.Shadow | ShadowFlags.TextureDirty;
        int shadowHandle = CreateProjectedTexture(entity, flags);

        nint renderable = _renderables.RenderableOf(entity);
        if (renderable != 0)
            _renderables.MarkShadowDirty(renderable, true);

        // NOTE: We *have* to call the version that takes a shadow handle
        // even if we have an entity because this entity hasn't set its shadow handle yet
        AddToDirtyShadowList(shadowHandle, true);
        return shadowHandle;
    }

    /// <summary><c>UpdateFlashlightState( h, state )</c>.</summary>
    public void UpdateFlashlightState(int shadowHandle, in FlashlightState flashlightState)
    {
        ref ClientShadow shadow = ref _shadows[shadowHandle];
        shadow.WorldToShadow = BuildPerspectiveWorldToFlashlightMatrix(flashlightState);
        shadow.State = flashlightState;
        shadow.HasState = true;
        _engine.UpdateFlashlightState(shadow.ShadowHandle, flashlightState);
    }

    public void DestroyFlashlight(int shadowHandle) => DestroyShadow(shadowHandle);

    private void RemoveShadowFromDirtyList(int handle)
    {
        if (!_dirtyShadows.Contains(handle))
            return;
        nint renderable = _renderables.RenderableOf(_shadows[handle].Entity);
        if (renderable != 0)
            _renderables.MarkShadowDirty(renderable, false);
        _dirtyShadows.RemoveOne(handle);
    }

    /// <summary><c>DestroyShadow( h )</c>.</summary>
    public void DestroyShadow(int handle)
    {
        RemoveShadowFromDirtyList(handle);
        _engine.DestroyShadow(_shadows[handle].ShadowHandle);
        _leafSystem.RemoveShadow(_shadows[handle].LeafShadow);
        CleanUpRenderToTextureShadow(handle);
        _shadowHandles.Remove(handle);
    }

    /// <summary><c>GetActualShadowCastType( h )</c>: render-to-texture collapses to SIMPLE while the mode is off.</summary>
    public int GetActualShadowCastType(int handle)
    {
        if (handle == Invalid)
            return ShadowTypes.None;
        int flags = _shadows[handle].Flags;
        if ((flags & ShadowFlags.UseRenderToTexture) != 0)
            return _renderToTextureActive ? ShadowTypes.RenderToTexture : ShadowTypes.Simple;
        if ((flags & ShadowFlags.UseDepthTexture) != 0)
            return ShadowTypes.RenderToDepthTexture;
        return ShadowTypes.Simple;
    }

    private int GetActualShadowCastType(nint renderable) => GetActualShadowCastType(_renderables.ShadowHandle(renderable));
}
