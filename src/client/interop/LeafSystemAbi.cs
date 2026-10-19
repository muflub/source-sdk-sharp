namespace SourceSharp.Client.Interop;

/// <summary>
/// What the managed leaf system asks of native code (C7 cluster 12, L2 of
/// docs/plans/c7_leaf_system.md): every question is a batch, one crossing for
/// any number of renderables, boxes or leaves. The native implementation is
/// the client forwarder library's LeafSystem table (src/fwd/native/fwd_leaf_system.cpp),
/// which loops each batch over the objects it is handed and decides nothing;
/// the oracle's replay answers from the scenario.
/// </summary>
public interface ILeafNative
{
    /// <summary>
    /// <c>IClientRenderable::ComputeFxBlend</c> of each renderable, in order;
    /// the calls the blends queued on the forwarder (<paramref name="system"/>,
    /// the leaf system the slot was called on) are written to
    /// <paramref name="pending"/> (<see cref="LeafPending"/> entries) and their
    /// count answered, for the caller to apply before it reads the groups.
    /// </summary>
    int ComputeFxBlends(nint system, ReadOnlySpan<nint> renderables, Span<int> pending);

    /// <summary>
    /// For each renderable, the answers <paramref name="what"/> asks for (a
    /// mask of <see cref="LeafFacts"/>' bits), written into its
    /// <see cref="LeafFacts.Words"/> words of <paramref name="facts"/>. A word
    /// not asked for is left as it was.
    /// </summary>
    void Facts(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> what, Span<int> facts);

    /// <summary><c>ShouldReceiveProjectedTextures( flags )</c> of each renderable: 1 or 0.</summary>
    void Receives(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> flags, Span<int> answers);

    /// <summary>
    /// The BSP leaves each box (six floats: mins, maxs) touches, as the
    /// engine's <c>EnumerateLeavesInBox</c> calls them back: per box its count
    /// and then its leaves, into <paramref name="output"/>. The words written,
    /// or the negative of the words needed when they do not fit.
    /// </summary>
    int EnumerateLeaves(ReadOnlySpan<float> boxes, Span<int> output);

    /// <summary>
    /// <c>IVEngineClient::GetLeavesArea</c> of each run in <paramref name="runs"/>
    /// (a count, then that many leaves; <paramref name="count"/> runs).
    /// </summary>
    void LeavesAreas(ReadOnlySpan<int> runs, int count, Span<int> areas);

    /// <summary>
    /// The engine's culling questions over boxes (six floats each), each box's
    /// program a <see cref="LeafQuery"/> word: its tests in order, area frustum,
    /// main frustum, occlusion, stopping at the first that rejects. Each
    /// answer is the <see cref="LeafQuery"/> bit of the test that rejected,
    /// or 0.
    /// </summary>
    void BoxQueries(ReadOnlySpan<float> boxes, ReadOnlySpan<int> programs, Span<int> answers);

    /// <summary>
    /// <c>IDetailObjectSystem::GetDetailModel</c> over ranges (a first index
    /// and a count each): the models, in order, with 0 for a sprite's slot.
    /// </summary>
    void DetailModels(nint detailSystem, ReadOnlySpan<int> ranges, Span<nint> models);
}

/// <summary>The bits of <see cref="ILeafNative.Facts"/>' mask, and where each answer lands.</summary>
public static class LeafFacts
{
    /// <summary>GetFxBlend, into word 0.</summary>
    public const int Blend = 1;

    /// <summary>IsTransparent (1 or 0), into word 1.</summary>
    public const int Transparent = 2;

    /// <summary>GetRenderBoundsWorldspace: mins into words 2-4, maxs into 5-7.</summary>
    public const int WorldBounds = 4;

    /// <summary>GetRenderBounds: mins into words 8-10, maxs into 11-13.</summary>
    public const int RenderBounds = 8;

    /// <summary>GetRenderOrigin, into words 14-16.</summary>
    public const int Origin = 16;

    /// <summary>Words per renderable; floats are their bits.</summary>
    public const int Words = 17;
}

/// <summary>The program and answer bits of <see cref="ILeafNative.BoxQueries"/>.</summary>
public static class LeafQuery
{
    /// <summary>DoesBoxTouchAreaFrustum( area ), the area in the program's bits 8 and up; rejects when it does not.</summary>
    public const int Area = 1;

    /// <summary>CullBox; rejects when it culls.</summary>
    public const int Cull = 2;

    /// <summary>IsOccluded; rejects when the box is occluded.</summary>
    public const int Occluded = 4;

    /// <summary>The program word for a box: its tests, and the area for <see cref="Area"/>.</summary>
    public static int Program(int tests, int area) => tests | (area << 8);
}

/// <summary>
/// What the forwarder carries out after a leaf-system call, as four 64-bit
/// words each: a kind and up to three arguments (src/fwd/native/fwd_leaf_system.cpp
/// executes them in order).
/// </summary>
public enum LeafOpKind
{
    /// <summary>IClientShadowMgr::AddShadowToReceiver: renderable, shadow, receiver kind.</summary>
    AddShadowToReceiver = 1,

    /// <summary>IClientShadowMgr::RemoveAllShadowsFromReceiver: renderable, -, receiver kind.</summary>
    RemoveAllShadowsFromReceiver = 2,

    /// <summary>IClientRenderable::RenderHandle() = the handle: renderable, handle.</summary>
    SetRenderHandle = 3,

    /// <summary>delete the sub-system data: its pointer.</summary>
    DeleteSubSystemData = 4,

    /// <summary>One render list entry: renderable, handle | leaf index &lt;&lt; 16 | two-pass &lt;&lt; 32, group.</summary>
    ListEntry = 5,

    /// <summary>The render list's groups that overflowed: -, the groups' bits.</summary>
    ListOverflow = 6,

    /// <summary>One view model, split: renderable, 1 translucent or 0 opaque.</summary>
    ViewModel = 7,

    /// <summary>IClientLeafShadowEnum::EnumShadow: -, the shadow's user id.</summary>
    EnumShadow = 8,
}

/// <summary>One carried-out operation (see <see cref="LeafOpKind"/>).</summary>
public readonly record struct LeafOp(LeafOpKind Kind, long A, long B, long C)
{
    /// <summary>Words per operation in the forwarder's buffers.</summary>
    public const int Words = 4;
}

/// <summary>One view's request (stock's SetupRenderInfo_t, as the forwarder hands it over).</summary>
public readonly ref struct LeafView
{
    public LeafView(ReadOnlySpan<int> leaves, int renderFrame, int detailBuildFrame, bool drawDetail, bool drawTranslucent,
                    float originX, float originY, float originZ, float forwardX, float forwardY, float forwardZ,
                    nint detailSystem, bool drawLeafFilter)
    {
        Leaves = leaves;
        RenderFrame = renderFrame;
        DetailBuildFrame = detailBuildFrame;
        DrawDetail = drawDetail;
        DrawTranslucent = drawTranslucent;
        OriginX = originX;
        OriginY = originY;
        OriginZ = originZ;
        ForwardX = forwardX;
        ForwardY = forwardY;
        ForwardZ = forwardZ;
        DetailSystem = detailSystem;
        DrawLeafFilter = drawLeafFilter;
    }

    /// <summary>The world list's leaves, front to back.</summary>
    public ReadOnlySpan<int> Leaves { get; }

    public int RenderFrame { get; }

    /// <summary>The detail objects' "render frame" (the view's world list build).</summary>
    public int DetailBuildFrame { get; }

    public bool DrawDetail { get; }

    public bool DrawTranslucent { get; }

    public float OriginX { get; }

    public float OriginY { get; }

    public float OriginZ { get; }

    public float ForwardX { get; }

    public float ForwardY { get; }

    public float ForwardZ { get; }

    /// <summary>The IDetailObjectSystem the detail models are asked of.</summary>
    public nint DetailSystem { get; }

    /// <summary>Whether cl_drawleaf filters the list: only a debug build's stock code had it.</summary>
    public bool DrawLeafFilter { get; }
}

/// <summary>
/// The managed leaf system as the forwarder's slots reach it (C7 cluster 12
/// L2): stock CClientLeafSystem's surface, C#-typed. SourceSharp.Client's
/// ClientLeafSystem answers it over its registry and its decisions;
/// <see cref="LeafSystemClientBinding"/> marshals each IManagedLeafSystemClient
/// slot to it. What native must carry out after a call is queued and taken
/// with <see cref="TakeOps"/>.
/// </summary>
public interface IClientLeafSystemHost
{
    void LevelInitPreEntity(int leafCount);
    void LevelShutdownPostEntity();
    /// <summary>The calls the forwarder queued, <see cref="LeafPending"/> entries, in order.</summary>
    void ApplyPending(ReadOnlySpan<int> entries);
    void PreRender();
    int TakeOps(Span<LeafOp> destination);
    int CreateRenderableHandle(nint renderable, bool staticProp, int modelType, bool transparent, bool twoPass);
    int AddRenderable(nint renderable, int group, int modelType);
    void RemoveRenderable(int handle);
    void AddRenderableToLeaves(int handle, ReadOnlySpan<int> leaves);
    void ChangeRenderableRenderGroup(int handle, int group);
    void SetRenderGroup(int handle, int group);
    void RenderableChanged(int handle);
    void EnableAlternateSorting(int handle, bool enable);
    void DrawStaticProps(bool enable);
    void DrawSmallEntities(bool enable);
    int GetRenderableLeaves(int handle, Span<int> leaves);
    bool GetRenderableLeaf(int handle, bool hasIterator, int iterator, out int leaf, out int nextIterator);
    long SetSubSystemDataInLeaf(int leaf, int index, long data);
    long GetSubSystemDataInLeaf(int leaf, int index);
    void SetDetailObjectsInLeaf(int leaf, int first, int count);
    bool GetDetailObjectsInLeaf(int leaf, out int first, out int count);
    void DrawDetailObjectsInLeaf(int leaf, int frame, out int first, out int count);
    bool ShouldDrawDetailObjectsInLeaf(int leaf, int frame);
    void ComputeTranslucentRenderLeaf(ReadOnlySpan<int> leaves, int frameNumber, int viewId, int globalFrame, nint system);
    void BuildRenderablesList(in LeafView view);
    void CollateViewModelRenderables(nint system);
    int AddShadow(int userId, int flags);
    void RemoveShadow(int handle);
    void ProjectShadow(int handle, ReadOnlySpan<int> leaves);
    void EnumerateShadowsInLeaves(ReadOnlySpan<int> leaves);
}

/// <summary>
/// The forwarder's queue (L3): RenderableChanged, ChangeRenderableRenderGroup
/// and SetRenderGroup arrive per renderable per frame, so src/fwd/native/fwd_leaf_system.cpp keeps
/// them in order, <see cref="Words"/> int32 an entry (kind, handle, value), at
/// most <see cref="Max"/>, and hands them over whole: in PreRender's
/// arguments, in a blend batch's answer, or through Pending.
/// </summary>
public static class LeafPending
{
    public const int Words = 3;
    public const int Max = 256;
    public const int RenderableChanged = 1;
    public const int ChangeRenderGroup = 2;
    public const int SetRenderGroup = 3;
}
