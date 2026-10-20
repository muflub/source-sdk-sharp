//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.PixelVisibility;

/// <summary>The frame the questions are asked in: gpGlobals' frame count and frame time.</summary>
public interface IPixelVisFrame
{
    int FrameCount { get; }

    float FrameTime { get; }
}

/// <summary>The ConVars the system reads (its own, and glow_overlay.cpp's building_cubemaps).</summary>
public interface IPixelVisSettings
{
    /// <summary>r_pixelvisibility_partial.</summary>
    bool Partial { get; }

    /// <summary>r_dopixelvisibility.</summary>
    bool Enabled { get; }

    /// <summary>r_pixelvisibility_spew.</summary>
    bool Spew { get; }

    /// <summary>building_cubemaps.</summary>
    bool BuildingCubemaps { get; }
}

/// <summary>
/// A view's end, as EndView draws it: the material bound, the depth range,
/// and each proxy (its query object, the material, its four triangles as
/// twelve vertices). Carried out natively in the order given.
/// </summary>
public interface IPixelVisDraw
{
    void Bind(int material);

    void DepthRange(float near, float far);

    void Proxy(int query, int material, ReadOnlySpan<Vector> triangles);
}

/// <summary>The view a view's end is drawn in: CurrentView*'s and the engine's world-to-screen matrix.</summary>
public readonly record struct PixelVisView(int Id, Vector Origin, Vector Forward, Vector Right, Vector Up, float[] WorldToScreen);

/// <summary>
/// c_pixel_visibility.cpp, the client's pixel visibility: CPixelVisibilitySystem
/// (a set per caller, found by the caller's handle; a query per set per view;
/// unused ones freed at the scene's end), CPixelVisibilityQuery (the fade over
/// the occlusion queries' pixel counts), PixelVisibility_DrawProxy (the
/// pyramid each query draws at its view's end), GlowSightDistance and
/// PixelVisibility_ShiftVisibilityViews. Held to the pristine file by
/// PixelVisibilityOracleTests over Fixtures/pixelvis-stock.tsv.
///
/// <para>
/// The indices are CUtlMultiList's (<see cref="UtlMultiList{T}"/>), because a
/// caller's handle is <c>(serial &lt;&lt; 16) | (node + 1)</c> and survives in
/// the caller. The query objects are the render context's, made and destroyed
/// in the order stock made and destroyed them.
/// </para>
///
/// <para>
/// ONE DIFFERENCE IN THE ORDER OF ENGINE CALLS, NOT IN WHAT IS ASKED: a view's
/// end asks every sphere diameter its proxies need in one batch before any
/// proxy is drawn (stock asked each just before its draw). The answers are a
/// function of the view's matrices, which drawing a proxy does not change.
/// GlowSightDistance's zoom factor is 1: this client has no local-player FOV
/// to scale by (docs/absence-client.md row 6). The spew prints a query's
/// native id where stock printed its render-context handle.
/// </para>
///
/// <para>
/// A MANAGED RELOAD mid-level hands the new build the level's materials and
/// whether the card answers, and the old build's query objects, which the new
/// build destroys: its lists start empty, so each caller's next question makes
/// it a new set and its glow fades in again.
/// </para>
/// </summary>
public sealed class PixelVisibilitySystem : IPixelVisibility, IReloadParticipant
{
    /// <summary>MIN_PROXY_PIXELS (not the X360's 25).</summary>
    private const float MinProxyPixels = 5.0f;

    private const string ProxyMaterialName = "engine/occlusionproxy";
    private const string DrawMaterialName = "engine/occlusionproxy_countdraw";

    /// <summary>The client effect precache registration stock made (CLIENTEFFECT_REGISTER_BEGIN( PrecacheOcclusionProxy )).</summary>
    public const string PrecacheName = "PrecacheOcclusionProxy";

    /// <summary>The registration's materials, in its order.</summary>
    public static IReadOnlyList<string> PrecacheMaterials => [ProxyMaterialName, DrawMaterialName];

    /// <summary>MASK_OPAQUE | CONTENTS_MONSTER | CONTENTS_DEBRIS.</summary>
    public const uint GlowSightMask = 0x06004081;

    private readonly IPixelVisEngine _engine;
    private readonly ICurrentView _view;
    private readonly IPixelVisFrame _frame;
    private readonly IPixelVisSettings _settings;

    private readonly UtlMultiList<PixelVisSet> _setList = new(() => new PixelVisSet());
    private readonly UtlMultiList<PixelVisQuery> _queryList;
    private ushort _freeQueriesList = UtlMultiList<PixelVisQuery>.InvalidIndex;
    private ushort _activeSetsList = UtlMultiList<PixelVisSet>.InvalidIndex;
    private ushort _freeSetsList = UtlMultiList<PixelVisSet>.InvalidIndex;

    private int _proxyMaterial;
    private int _drawMaterial;
    private bool _hwCanTestGlows = true;
    private bool _drawQueries;

    public PixelVisibilitySystem(IPixelVisEngine engine, ICurrentView view, IPixelVisFrame frame, IPixelVisSettings settings)
    {
        _engine = engine;
        _view = view;
        _frame = frame;
        _settings = settings;
        _queryList = new UtlMultiList<PixelVisQuery>(() => new PixelVisQuery(engine), q => q.Destroy(engine));
    }

    /// <summary>The query sets in all, those in the free list, the queries in all, those free (pixelvis_debug's numbers).</summary>
    public (int Sets, int FreeSets, int Queries, int FreeQueries) Counts
        => _activeSetsList == UtlMultiList<PixelVisSet>.InvalidIndex
            ? (_setList.TotalCount, 0, _queryList.TotalCount, 0)
            : (_setList.TotalCount, _setList.Count(_freeSetsList), _queryList.TotalCount, _queryList.Count(_freeQueriesList));

    // HasFastQueries: only OSX asks gl_can_query_fast.
    private static bool HasFastQueries() => true;

    //-------------------------------------------------------------------------
    // The level
    //-------------------------------------------------------------------------

    /// <summary>LevelInitPreEntity: whether the card can answer, the two materials, the three lists.</summary>
    public void LevelInitPreEntity()
    {
        bool fastqueries = HasFastQueries();
        _hwCanTestGlows = _settings.Enabled && fastqueries && _engine.DxSupportLevel() >= 80;
        if (_hwCanTestGlows)
        {
            int query = _engine.CreateQuery();
            if (query != 0)
                _engine.DestroyQuery(query);
            else
                _hwCanTestGlows = false;
        }

        _proxyMaterial = _engine.FindMaterial(ProxyMaterialName);
        _drawMaterial = _engine.FindMaterial(DrawMaterialName);
        _freeQueriesList = _queryList.CreateList();
        _activeSetsList = _setList.CreateList();
        _freeSetsList = _setList.CreateList();
    }

    /// <summary>LevelShutdownPostEntity: the materials let go, every set and query freed, then purged.</summary>
    public void LevelShutdownPostEntity()
    {
        _engine.ReleaseMaterial(_proxyMaterial);
        _proxyMaterial = 0;
        _engine.ReleaseMaterial(_drawMaterial);
        _drawMaterial = 0;
        DeleteUnusedSets(true);
        _setList.Purge();
        _queryList.Purge();
        _freeQueriesList = UtlMultiList<PixelVisQuery>.InvalidIndex;
        _activeSetsList = UtlMultiList<PixelVisSet>.InvalidIndex;
        _freeSetsList = UtlMultiList<PixelVisSet>.InvalidIndex;
    }

    //-------------------------------------------------------------------------
    // The questions
    //-------------------------------------------------------------------------

    /// <summary>
    /// PixelVisibility_FractionVisible with the caller's handle: how much of a
    /// proxy at the position was visible, faded in and out.
    /// </summary>
    public float FractionVisible(in PixelVisParams p, ref int queryHandle)
    {
        if (!_hwCanTestGlows || _settings.BuildingCubemaps)
            return GlowSightDistance(p.Position, true) > 0 ? 1.0f : 0.0f;
        int viewId = _view.Read().Id;
        if (viewId < 0)
            return 0.0f;

        ushort setNode = FindOrCreatePixelVisSet(p, ref queryHandle);
        PixelVisSet set = _setList[setNode];
        ushort node = FindOrCreateQueryForView(set, viewId);
        PixelVisQuery query = _queryList[node];
        query.Origin = p.Position;
        float fraction = query.GetFractionVisible(set.FadeTimeInv, viewId, this);
        set.MarkActive(_frame.FrameCount);
        return fraction;
    }

    /// <summary>PixelVisibility_FractionVisible with no handle: the sight line, 1 or 0.</summary>
    public float FractionVisibleNoHandle(in Vector position) => GlowSightDistance(position, true) > 0.0f ? 1.0f : 0.0f;

    /// <summary>PixelVisibility_IsAvailable.</summary>
    public bool IsAvailable => _settings.Enabled && HasFastQueries() && _hwCanTestGlows;

    /// <summary>
    /// GlowSightDistance: the distance from the eye, or -1 when
    /// <paramref name="shouldTrace"/> and the line to a point four units short of
    /// it (when it is further than four) is stopped.
    /// </summary>
    public float GlowSightDistance(in Vector glowOrigin, bool shouldTrace)
    {
        CurrentViewState view = _view.Read();
        Vector eye = view.Origin;
        float dist = ( glowOrigin - eye ).Length;
        if (shouldTrace)
        {
            Vector end = glowOrigin;
            if (dist > 4)
                end = end - view.Forward * 4;
            if (_engine.TraceGlowSight(eye, end) != 1.0f)
                return -1;
        }
        return dist;
    }

    /// <summary>r_drawpixelvisibility's change callback: the proxies drawn visibly, with no counting pass.</summary>
    public void ShowQueries(bool show) => _drawQueries = show;

    /// <summary>pixelvis_debug.</summary>
    public void DebugInfo()
    {
        (int sets, int freeSets, int queries, int freeQueries) = Counts;
        _engine.Msg(string.Create(CultureInfo.InvariantCulture,
            $"Pixel vis system using {sets} sets total ({freeSets} in free list), {queries} queries total ({freeQueries} in free list)\n"));
    }

    //-------------------------------------------------------------------------
    // The view's end and the scene's
    //-------------------------------------------------------------------------

    /// <summary>
    /// PixelVisibility_EndCurrentView: every active set's query for this view
    /// draws its proxy -- the counting proxies first, between DepthRange( 0,
    /// 0.01 ) and ( 0, 1 ), unless the proxies are drawn visibly -- through
    /// <paramref name="draw"/>.
    /// </summary>
    public void EndView(in PixelVisView view, IPixelVisDraw draw)
    {
        if (!IsAvailable && view.Id >= 0)
            return;
        if (_activeSetsList == UtlMultiList<PixelVisSet>.InvalidIndex || _setList.Head(_activeSetsList) == UtlMultiList<PixelVisSet>.InvalidIndex)
            return;

        int proxy = _drawQueries ? _drawMaterial : _proxyMaterial;
        bool counting = _settings.Partial && !_drawQueries;

        // Which proxies draw, in stock's order, and the spheres each asks for:
        // decided before anything is drawn, from state the drawing does not
        // change before each one's own turn.
        var draws = new List<(PixelVisSet Set, PixelVisQuery Query, bool Counting)>();
        var spheres = new List<float>();
        if (counting)
            CollectDraws(view, true, draws, spheres);
        CollectDraws(view, false, draws, spheres);
        var diameters = new float[spheres.Count / 4];
        if (diameters.Length > 0)
            _engine.PixelDiameters(spheres.ToArray(), diameters);

        draw.Bind(proxy);
        int next = 0;
        int d = 0;
        if (counting)
        {
            draw.DepthRange(0.0f, 0.01f);
            for (; next < draws.Count && draws[next].Counting; next++)
            {
                (PixelVisSet set, PixelVisQuery query, _) = draws[next];
                query.IssueCountingQuery(view, set.ProxySize, set.ProxyAspect, proxy, set.SizeIsScreenSpace, draw, diameters, ref d);
            }
            draw.DepthRange(0.0f, 1.0f);
        }
        for (; next < draws.Count; next++)
        {
            (PixelVisSet set, PixelVisQuery query, _) = draws[next];
            query.IssueQuery(view, set.ProxySize, set.ProxyAspect, proxy, set.SizeIsScreenSpace, draw, diameters, ref d, this);
        }
    }

    private void CollectDraws(in PixelVisView view, bool counting, List<(PixelVisSet, PixelVisQuery, bool)> draws, List<float> spheres)
    {
        for (ushort node = _setList.Head(_activeSetsList); node != UtlMultiList<PixelVisSet>.InvalidIndex; node = _setList.Next(node))
        {
            PixelVisSet set = _setList[node];
            ushort queryNode = FindQueryForView(set, view.Id);
            if (queryNode == UtlMultiList<PixelVisQuery>.InvalidIndex)
                continue;
            PixelVisQuery query = _queryList[queryNode];
            draws.Add((set, query, counting));
            if (query.Failed)
                continue;
            spheres.Add(query.Origin.X);
            spheres.Add(query.Origin.Y);
            spheres.Add(query.Origin.Z);
            spheres.Add(1.0f);
            if (set.SizeIsScreenSpace)
            {
                Vector at = view.Origin + view.Forward * 1.0f;
                spheres.Add(at.X);
                spheres.Add(at.Y);
                spheres.Add(at.Z);
                spheres.Add(set.ProxySize * 0.5f);
            }
        }
    }

    /// <summary>PixelVisibility_EndScene: the sets and queries nobody asked about lately, freed.</summary>
    public void EndScene() => DeleteUnusedSets(false);

    /// <summary>
    /// PixelVisibility_ShiftVisibilityViews: each set's query for
    /// <paramref name="sourceViewId"/> becomes its query for
    /// <paramref name="destViewId"/>, replacing the one it had.
    /// </summary>
    public void ShiftVisibilityViews(int sourceViewId, int destViewId)
    {
        if (_activeSetsList == UtlMultiList<PixelVisSet>.InvalidIndex)
            return;
        ushort node = _setList.Head(_activeSetsList);
        while (node != UtlMultiList<PixelVisSet>.InvalidIndex)
        {
            ushort next = _setList.Next(node);
            PixelVisSet set = _setList[node];
            ushort source = FindQueryForView(set, sourceViewId);
            ushort dest = FindQueryForView(set, destViewId);
            if (dest != UtlMultiList<PixelVisQuery>.InvalidIndex)
            {
                _queryList.Unlink(set.QueryList, dest);
                _queryList.LinkToHead(_freeQueriesList, dest);
                if (_queryList.Head(set.QueryList) == UtlMultiList<PixelVisQuery>.InvalidIndex)
                    FreeSet(node);
            }
            if (source != UtlMultiList<PixelVisQuery>.InvalidIndex)
                _queryList[source].ViewId = (ushort)destViewId;
            node = next;
        }
    }

    //-------------------------------------------------------------------------
    // The lists
    //-------------------------------------------------------------------------

    private ushort FindQueryForView(PixelVisSet set, int viewId)
    {
        for (ushort node = _queryList.Head(set.QueryList); node != UtlMultiList<PixelVisQuery>.InvalidIndex; node = _queryList.Next(node))
        {
            if (_queryList[node].ViewId == viewId)
                return node;
        }
        return UtlMultiList<PixelVisQuery>.InvalidIndex;
    }

    private ushort FindOrCreateQueryForView(PixelVisSet set, int viewId)
    {
        ushort node = FindQueryForView(set, viewId);
        if (node != UtlMultiList<PixelVisQuery>.InvalidIndex)
            return node;
        node = AllocQuery();
        _queryList.LinkToHead(set.QueryList, node);
        _queryList[node].SetView(viewId);
        return node;
    }

    private void DeleteUnusedQueries(PixelVisSet set, bool deleteAll)
    {
        ushort node = _queryList.Head(set.QueryList);
        while (node != UtlMultiList<PixelVisQuery>.InvalidIndex)
        {
            ushort next = _queryList.Next(node);
            if (deleteAll || !_queryList[node].IsActive(_frame.FrameCount))
            {
                _queryList.Unlink(set.QueryList, node);
                _queryList.LinkToHead(_freeQueriesList, node);
            }
            node = next;
        }
    }

    private void DeleteUnusedSets(bool deleteAll)
    {
        if (_activeSetsList == UtlMultiList<PixelVisSet>.InvalidIndex)
            return;
        ushort node = _setList.Head(_activeSetsList);
        while (node != UtlMultiList<PixelVisSet>.InvalidIndex)
        {
            ushort next = _setList.Next(node);
            PixelVisSet set = _setList[node];
            if (deleteAll || !set.IsActive(_frame.FrameCount))
                DeleteUnusedQueries(set, true);
            else
                DeleteUnusedQueries(set, false);
            if (_queryList.Head(set.QueryList) == UtlMultiList<PixelVisQuery>.InvalidIndex)
                FreeSet(node);
            node = next;
        }
    }

    private ushort AllocQuery()
    {
        ushort node = _queryList.Head(_freeQueriesList);
        if (node != UtlMultiList<PixelVisQuery>.InvalidIndex)
        {
            _queryList.Unlink(_freeQueriesList, node);
            _queryList[node].ResetOcclusionQueries(_engine);
        }
        else
        {
            node = _queryList.Alloc();
        }
        return node;
    }

    private ushort AllocSet()
    {
        ushort node = _setList.Head(_freeSetsList);
        if (node != UtlMultiList<PixelVisSet>.InvalidIndex)
        {
            _setList.Unlink(_freeSetsList, node);
        }
        else
        {
            node = _setList.Alloc();
            _setList[node].QueryList = _queryList.CreateList();
        }
        _setList.LinkToHead(_activeSetsList, node);
        return node;
    }

    private void FreeSet(ushort node)
    {
        _setList.Unlink(_activeSetsList, node);
        _setList.LinkToHead(_freeSetsList, node);
        _setList[node].Serial++;
    }

    private ushort FindOrCreatePixelVisSet(in PixelVisParams p, ref int queryHandle)
    {
        if (queryHandle != 0)
        {
            ushort handle = (ushort)( queryHandle & 0xFFFF );
            handle--;
            ushort serial = (ushort)( (uint)queryHandle >> 16 );
            if (_setList.IsValidIndex(handle) && _setList[handle].Serial == serial)
                return handle;
        }

        ushort node = AllocSet();
        _setList[node].Init(p);
        uint outHandle = _setList[node].Serial;
        ushort nodeHandle = (ushort)( node + 1 );
        outHandle <<= 16;
        outHandle |= nodeHandle;
        queryHandle = unchecked((int)outHandle);
        return node;
    }

    //-------------------------------------------------------------------------
    // The pyramid (PixelVisibility_DrawProxy)
    //-------------------------------------------------------------------------

    /// <summary>
    /// The proxy for a query at <paramref name="origin"/>: its four triangles
    /// drawn through <paramref name="draw"/> under <paramref name="query"/>, and
    /// the fraction of its screen area the frustum keeps; -1, and nothing
    /// drawn, when a corner is behind the eye.
    /// </summary>
    internal static float DrawProxy(in PixelVisView view, int query, Vector origin, float scale, float proxyAspect, int material,
                                    bool screenspace, IPixelVisDraw draw, float[] diameters, ref int d)
    {
        // don't expand this with distance to fit pixels or the sprite will poke through
        // only expand the parts perpendicular to the view
        float forwardScale = scale;
        float pixelsPerUnit = diameters[d++];
        pixelsPerUnit = Max(pixelsPerUnit, 1e-4f);
        if (screenspace)
        {
            // Force this to be the size of a sphere of diameter "scale" at some reference distance (1.0 unit)
            float pixelsPerUnit2 = diameters[d++];
            scale = pixelsPerUnit2 / pixelsPerUnit;
        }
        else
        {
            float pixels = scale * pixelsPerUnit;
            if (pixels < MinProxyPixels)
                scale = MinProxyPixels / pixelsPerUnit;
        }

        // collapses the pyramid to a plane - so this could be a quad instead
        Vector dir = origin - view.Origin;
        MoveVec.Normalize(ref dir);
        origin = origin - dir * forwardScale;
        forwardScale = 0.0f;

        const float sqrt2 = 0.707106781f;
        scale *= sqrt2;
        float scale45x = scale;
        float scale45y = scale / proxyAspect;
        Span<Vector> verts = stackalloc Vector[5];
        verts[0] = origin - view.Forward * forwardScale;
        verts[1] = origin + view.Up * scale45y - view.Right * scale45x;
        verts[2] = origin + view.Up * scale45y + view.Right * scale45x;
        verts[3] = origin - view.Up * scale45y + view.Right * scale45x;
        verts[4] = origin - view.Up * scale45y - view.Right * scale45x;

        Span<Vector> screen = stackalloc Vector[4];
        for (int i = 0; i < 4; i++)
        {
            if (ScreenTransform(view.WorldToScreen, verts[i + 1], out screen[i]))
                return -1;
        }

        float w = screen[1].X - screen[0].X;
        float h = screen[0].Y - screen[3].Y;
        float ws = Min(1.0f, screen[1].X) - Max(-1.0f, screen[0].X);
        float hs = Min(1.0f, screen[0].Y) - Max(-1.0f, screen[3].Y);
        float area = w * h;
        float areaClipped = ws * hs;
        float ratio = 0.0f;
        if (area != 0)
        {
            ratio = areaClipped / area;
            ratio = Clamp(ratio, 0.0f, 1.0f);
        }

        Span<Vector> triangles = stackalloc Vector[12];
        for (int i = 0; i < 4; i++)
        {
            int a = i + 1;
            int b = ( a % 4 ) + 1;
            triangles[i * 3] = verts[0];
            triangles[i * 3 + 1] = verts[a];
            triangles[i * 3 + 2] = verts[b];
        }
        draw.Proxy(query, material, triangles);
        return ratio;
    }

    // MIN / MAX and tier0's clamp, as the macros and the template compare.
    private static float Min(float a, float b) => a < b ? a : b;

    private static float Max(float a, float b) => a > b ? a : b;

    private static float Clamp(float v, float lo, float hi) => hi < lo ? hi : v < lo ? lo : v > hi ? hi : v;

    /// <summary>
    /// view_scene.cpp's FrustumTransform through the world-to-screen matrix
    /// (row-major): the point on the unit screen, and true when it is behind
    /// the eye (w under 0.001, its x and y then pushed far off screen).
    /// </summary>
    internal static bool ScreenTransform(float[] m, in Vector point, out Vector screen)
    {
        float x = m[0] * point.X + m[1] * point.Y + m[2] * point.Z + m[3];
        float y = m[4] * point.X + m[5] * point.Y + m[6] * point.Z + m[7];
        float w = m[12] * point.X + m[13] * point.Y + m[14] * point.Z + m[15];
        if (w < 0.001f)
        {
            screen = new Vector(x * 100000, y * 100000, 0.0f);
            return true;
        }
        float invw = 1.0f / w;
        screen = new Vector(x * invw, y * invw, 0.0f);
        return false;
    }

    internal void Spew(int level, string text) => _engine.DevMsg(level, text);

    internal bool SpewOn(int viewId) => _settings.Spew && viewId == 0;

    internal bool PartialOn => _settings.Partial;

    internal int FrameCount => _frame.FrameCount;

    internal float FrameTime => _frame.FrameTime;

    internal IPixelVisEngine Engine => _engine;

    /// <summary>"%p" as glibc prints it: (nil) for a null handle, else 0x and lowercase hex.</summary>
    internal static string Pointer(int p) => p == 0 ? "(nil)" : "0x" + ( (uint)p ).ToString("x", CultureInfo.InvariantCulture);

    //-------------------------------------------------------------------------
    // A managed reload mid-level
    //-------------------------------------------------------------------------

    /// <summary>Whether a level's lists are up (between LevelInitPreEntity and LevelShutdownPostEntity).</summary>
    public bool LevelActive => _activeSetsList != UtlMultiList<PixelVisSet>.InvalidIndex;

    /// <summary>Every query object the lists hold, the visible one of each first.</summary>
    internal IEnumerable<int> QueryObjects()
    {
        for (ushort i = 0; i < _queryList.TotalSlots; i++)
        {
            if (!_queryList.IsValidIndex(i))
                continue;
            yield return _queryList[i].VisibleQuery;
            yield return _queryList[i].CountQuery;
        }
    }

    void IReloadParticipant.Save(ReloadWriter writer)
    {
        writer.Write(LevelActive);
        writer.Write(_hwCanTestGlows);
        writer.Write(_drawQueries);
        writer.Write(_proxyMaterial);
        writer.Write(_drawMaterial);
        var queries = LevelActive ? QueryObjects().Where(q => q != 0).ToList() : [];
        writer.Write(queries.Count);
        foreach (int q in queries)
            writer.Write(q);
    }

    void IReloadParticipant.Restore(ReloadReader reader)
    {
        bool active = reader.ReadBool();
        _hwCanTestGlows = reader.ReadBool();
        _drawQueries = reader.ReadBool();
        int proxy = reader.ReadInt();
        int draw = reader.ReadInt();
        int n = reader.ReadInt();
        for (int i = 0; i < n; i++)
            _engine.DestroyQuery(reader.ReadInt());
        if (!active)
            return;
        _proxyMaterial = proxy;
        _drawMaterial = draw;
        _freeQueriesList = _queryList.CreateList();
        _activeSetsList = _setList.CreateList();
        _freeSetsList = _setList.CreateList();
    }
}

/// <summary>c_pixel_visibility.cpp's CPixelVisSet: one caller's proxy, its fade rate and its queries.</summary>
internal sealed class PixelVisSet
{
    public float ProxySize;
    public float ProxyAspect;
    public float FadeTimeInv;
    public ushort QueryList = 0xFFFF;
    public ushort Serial;
    public bool SizeIsScreenSpace;
    private int _frameIssued;

    public void Init(in PixelVisParams p)
    {
        ProxySize = p.ProxySize;
        ProxyAspect = p.ProxyAspect;
        // fade in over 0.125 seconds when the caller names no fade
        FadeTimeInv = p.FadeTime > 0.0f ? 1.0f / p.FadeTime : 1.0f / 0.125f;
        _frameIssued = 0;
        SizeIsScreenSpace = p.SizeInScreenspace;
    }

    public void MarkActive(int frameCount) => _frameIssued = frameCount;

    public bool IsActive(int frameCount) => !( ( frameCount - _frameIssued ) > 1 );
}

/// <summary>c_pixel_visibility.cpp's CPixelVisibilityQuery: one set's question in one view.</summary>
internal sealed class PixelVisQuery
{
    public Vector Origin;
    public int FrameIssued;
    private float _brightnessTarget;
    private float _clipFraction;
    private readonly int _queryHandle;
    private readonly int _queryHandleCount;
    private bool _wasQueriedThisFrame;
    public bool Failed;
    public ushort ViewId;

    /// <summary>The constructor: its two occlusion query objects, the visible one first.</summary>
    public PixelVisQuery(IPixelVisEngine engine)
    {
        SetView(0xFFFF);
        _queryHandle = engine.CreateQuery();
        _queryHandleCount = engine.CreateQuery();
    }

    /// <summary>The destructor.</summary>
    public void Destroy(IPixelVisEngine engine)
    {
        if (_queryHandle != 0)
            engine.DestroyQuery(_queryHandle);
        if (_queryHandleCount != 0)
            engine.DestroyQuery(_queryHandleCount);
    }

    public void SetView(int viewId)
    {
        ViewId = (ushort)viewId;
        _brightnessTarget = 0.0f;
        _clipFraction = 1.0f;
        FrameIssued = -1;
        Failed = false;
        _wasQueriedThisFrame = false;
    }

    /// <summary>A query taken from the free list: its objects marked as never read.</summary>
    public void ResetOcclusionQueries(IPixelVisEngine engine)
    {
        if (_queryHandle != 0)
            engine.ResetQuery(_queryHandle);
        if (_queryHandleCount != 0)
            engine.ResetQuery(_queryHandleCount);
    }

    private bool IsValid => _queryHandle != 0;

    public int VisibleQuery => _queryHandle;

    public int CountQuery => _queryHandleCount;

    public bool IsActive(int frameCount) => !( ( frameCount - FrameIssued ) > 1 );

    public float GetFractionVisible(float fadeTimeInv, int viewId, PixelVisibilitySystem system)
    {
        if (!IsValid)
            return 0.0f;

        if (!_wasQueriedThisFrame)
        {
            _wasQueriedThisFrame = true;
            int pixels = -1;
            int pixelsPossible = -1;
            int frame = system.FrameCount;
            if (system.PartialOn)
            {
                if (FrameIssued != -1)
                    system.Engine.PixelsRendered(_queryHandle, _queryHandleCount, out pixels, out pixelsPossible);

                if (system.SpewOn(viewId))
                    system.Spew(1, string.Create(CultureInfo.InvariantCulture,
                        $"Pixels visible: {pixels} (qh:{PixelVisibilitySystem.Pointer(_queryHandle)}) Pixels possible: {pixelsPossible} (qh:{PixelVisibilitySystem.Pointer(_queryHandleCount)}) (frame:{frame})\n"));

                if (pixels < 0 || pixelsPossible < 0)
                {
                    Failed = FrameIssued >= 0;
                    return _brightnessTarget * _clipFraction;
                }

                if (pixelsPossible > 0)
                {
                    float target = (float)pixels / (float)pixelsPossible;
                    target = target >= 0.95f ? 1.0f : target < 0.0f ? 0.0f : target;
                    float rate = system.FrameTime * fadeTimeInv;
                    _brightnessTarget = Approach(target, _brightnessTarget, rate);
                }
                else
                {
                    _brightnessTarget = 0.0f;
                }
            }
            else
            {
                if (FrameIssued != -1)
                    system.Engine.PixelsRendered(_queryHandle, 0, out pixels, out _);

                if (system.SpewOn(viewId))
                    system.Spew(1, string.Create(CultureInfo.InvariantCulture,
                        $"Pixels visible: {pixels} (qh:{PixelVisibilitySystem.Pointer(_queryHandle)}) (frame:{frame})\n"));

                if (pixels < 0)
                {
                    Failed = FrameIssued >= 0;
                    return _brightnessTarget * _clipFraction;
                }
                if (FrameIssued == frame - 1)
                {
                    float rate = system.FrameTime * fadeTimeInv;
                    float target = 0.0f;
                    if (pixels > 0)
                    {
                        // fade in slower than you fade out
                        rate *= 0.5f;
                        target = 1.0f;
                    }
                    _brightnessTarget = Approach(target, _brightnessTarget, rate);
                }
                else
                {
                    _brightnessTarget = 0.0f;
                }
            }
        }

        return _brightnessTarget * _clipFraction;
    }

    public void IssueQuery(in PixelVisView view, float proxySize, float proxyAspect, int material, bool sizeIsScreenSpace,
                           IPixelVisDraw draw, float[] diameters, ref int d, PixelVisibilitySystem system)
    {
        if (!Failed)
        {
            if (system.SpewOn(view.Id))
                system.Spew(1, string.Create(CultureInfo.InvariantCulture,
                    $"Draw Proxy: qh:{PixelVisibilitySystem.Pointer(_queryHandle)} org:<{(int)Origin.X},{(int)Origin.Y},{(int)Origin.Z}> (frame:{system.FrameCount})\n"));

            _clipFraction = PixelVisibilitySystem.DrawProxy(view, _queryHandle, Origin, proxySize, proxyAspect, material, sizeIsScreenSpace,
                                                            draw, diameters, ref d);
            if (_clipFraction < 0)
            {
                // NOTE: In this case, the proxy wasn't issued cause it was offscreen
                // can't set the m_frameissued field since that would cause it to get marked as failed
                _clipFraction = 0;
                _wasQueriedThisFrame = false;
                Failed = false;
                return;
            }
        }

        FrameIssued = system.FrameCount;
        _wasQueriedThisFrame = false;
        Failed = false;
    }

    public void IssueCountingQuery(in PixelVisView view, float proxySize, float proxyAspect, int material, bool sizeIsScreenSpace,
                                   IPixelVisDraw draw, float[] diameters, ref int d)
    {
        if (!Failed)
            PixelVisibilitySystem.DrawProxy(view, _queryHandleCount, Origin, proxySize, proxyAspect, material, sizeIsScreenSpace,
                                            draw, diameters, ref d);
    }

    // mathlib's Approach.
    private static float Approach(float target, float value, float speed)
    {
        float delta = target - value;
        if (delta > speed)
            value += speed;
        else if (delta < -speed)
            value -= speed;
        else
            value = target;
        return value;
    }
}
