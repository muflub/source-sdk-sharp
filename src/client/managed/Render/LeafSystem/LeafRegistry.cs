//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.LeafSystem;

/// <summary>
/// The client leaf system's registry (C7 cluster 12, L2 of
/// docs/plans/c7_leaf_system.md), ported from clientleafsystem.cpp's
/// CClientLeafSystem: which renderables exist and their state, which BSP
/// leaves each sits in, the shadows projected into leaves and the receivers
/// each lands on, the detail objects' and sub-systems' data per leaf, the
/// dirty list PreRender re-inserts from, and the view models. Decisions 3
/// (<see cref="ShouldDrawDetailObjectsInLeaf"/>) and 4
/// (<see cref="ShouldRenderableReceiveShadow"/>) are here; decisions 1, 2 and
/// 5 are <see cref="RenderableListBuilder"/>'s, over this.
///
/// <para>
/// THE NATIVE OBJECTS ARE HANDLES HERE. A renderable is its IClientRenderable
/// pointer, held as a number and never followed; what this asks of one goes
/// through <see cref="ILeafNative"/> in batches, and what native must do to
/// one (a shadow receiver added or cleared, a render handle written, a
/// sub-system's data deleted) is an operation queued for the forwarder to
/// carry out when the call returns (<see cref="TakeOps"/>), in the order stock
/// did it.
/// </para>
///
/// <para>
/// THE ORDER IS STOCK'S. Handles come from a free list as CUtlLinkedList's
/// do (LIFO, fresh ones counting up, all reset by a level's purge); a leaf's
/// renderables and shadows, and a renderable's leaves, are newest first as
/// CBidirectionalSet links them; the dirty and view-model lists remove by
/// moving the last entry into the hole (CUtlVector::FastRemove). Each of
/// those orders reaches the render list or the shadow manager.
/// </para>
///
/// <para>
/// One divergence: stock's m_ShadowsOnRenderable is written and never read,
/// so it is not kept.
/// </para>
/// </summary>
public sealed class LeafRegistry : IReloadParticipant
{
    /// <summary>INVALID_CLIENT_RENDER_HANDLE, and CLIENT_LEAF_SHADOW_INVALID_HANDLE.</summary>
    public const int InvalidHandle = 0xFFFF;

    // clientleafsystem.cpp's render flags.
    internal const int FlagTwoPass = 0x01;
    internal const int FlagStaticProp = 0x02;
    internal const int FlagBrushModel = 0x04;
    internal const int FlagStudioModel = 0x08;
    internal const int FlagHasChanged = 0x10;
    internal const int FlagAlternateSorting = 0x20;

    // The engine's modtype_t.
    internal const int ModBrush = 1;
    internal const int ModStudio = 3;

    // engine/IClientLeafSystem.h's render groups.
    internal const int GroupOpaqueStatic = 6;
    internal const int GroupOpaqueEntity = 7;
    internal const int GroupTranslucentEntity = 8;
    internal const int GroupTwoPass = 9;
    internal const int GroupViewModelOpaque = 10;
    internal const int GroupViewModelTranslucent = 11;
    internal const int GroupCount = 14;

    // engine/ishadowmgr.h's projected-texture kinds.
    internal const int ShadowFlagsProjectedTextureMask = 0x3;

    // iclientshadowmgr.h's ShadowReceiver_t.
    internal const int ReceiverBrushModel = 0;
    internal const int ReceiverStaticProp = 1;
    internal const int ReceiverStudioModel = 2;

    // view_shared.h's VIEW_ILLEGAL.
    private const int ViewIllegal = -2;

    private const int MaxLeavesPerRenderable = 128;

    /// <summary>One renderable's slot (stock's RenderableInfo_t).</summary>
    internal struct RenderableInfo
    {
        public long Renderable;
        public int RenderFrame;
        public int RenderFrame2;
        public int EnumCount;
        public int TranslucencyCalculated;
        public uint RenderLeaf;
        public byte Flags;
        public byte Group;
        public short Area;
        public sbyte TranslucencyCalculatedView;
    }

    /// <summary>One shadow's slot (stock's ShadowInfo_t, less the write-only receiver list).</summary>
    internal struct ShadowInfo
    {
        public int UserId;
        public int Flags;
        public int EnumCount;
    }

    /// <summary>One leaf's data (stock's ClientLeaf_t, less the two list heads the buckets keep).</summary>
    internal struct LeafData
    {
        public ushort FirstDetailProp;
        public ushort DetailPropCount;
        public int DetailPropRenderFrame;
        public long SubSystemData;
    }

    private readonly ILeafNative _native;

    private RenderableInfo[] _renderables = new RenderableInfo[256];
    private readonly HandleList _renderableHandles = new();
    private ShadowInfo[] _shadows = new ShadowInfo[64];
    private readonly HandleList _shadowHandles = new();
    private LeafData[] _leaves = [];
    private int _leafCount;

    // m_RenderablesInLeaf and m_ShadowsInLeaf.
    private readonly LeafBuckets _renderablesInLeaf = new();
    private readonly LeafBuckets _shadowsInLeaf = new();

    private readonly List<int> _dirty = [];
    private readonly List<int> _viewModels = [];

    private readonly List<LeafOp> _ops = [];
    private int _opsTaken;

    // Batch scratch, reused.
    private readonly List<long> _batchRenderables = [];
    private readonly List<int> _batchInts = [];
    private readonly List<float> _batchBoxes = [];
    private int[] _scratch = new int[256];
    private readonly List<(int Handle, int Shadow)> _pendingReceivers = [];

    public LeafRegistry(ILeafNative native)
    {
        _native = native;
    }

    /// <summary>m_DrawStaticProps.</summary>
    public bool DrawStaticPropsEnabled { get; private set; } = true;

    /// <summary>m_DrawSmallObjects (stock asserts it stays on).</summary>
    public bool DrawSmallObjects { get; private set; } = true;

    /// <summary>m_ShadowEnum: advanced to add each shadow to a renderable, and each renderable to a shadow, once.</summary>
    public int ShadowEnum { get; private set; }

    /// <summary>The native side, for the builder's batches.</summary>
    internal ILeafNative Native => _native;

    /// <summary>The level's leaf count.</summary>
    public int LeafCount => _leafCount;

    internal ref RenderableInfo Info(int handle) => ref _renderables[handle];

    internal LeafBuckets RenderablesInLeaf => _renderablesInLeaf;

    internal bool IsValidLeaf(int leaf) => (uint)leaf < (uint)_leafCount;

    /// <summary>Whether a render handle names a live renderable (CUtlLinkedList::IsValidIndex).</summary>
    public bool IsValidHandle(int handle) => _renderableHandles.IsValid(handle);

    /// <summary>The live renderables, for diagnostics.</summary>
    public int RenderableCount => _renderableHandles.Live;

    /// <summary>The view models' list, in stock's order.</summary>
    internal List<int> ViewModels => _viewModels;

    //-------------------------------------------------------------------------
    // The operations queue.
    //-------------------------------------------------------------------------

    /// <summary>Queues one operation for the forwarder.</summary>
    internal void Op(LeafOpKind kind, long a, long b = 0, long c = 0) => _ops.Add(new LeafOp(kind, a, b, c));

    /// <summary>The operations queued and not yet taken.</summary>
    public int PendingOps => _ops.Count - _opsTaken;

    /// <summary>
    /// Moves up to <paramref name="destination"/>'s length of the queued
    /// operations into it, oldest first, and answers how many. The queue
    /// empties as it is taken.
    /// </summary>
    public int TakeOps(Span<LeafOp> destination)
    {
        int n = Math.Min(destination.Length, _ops.Count - _opsTaken);
        for (int i = 0; i < n; i++)
            destination[i] = _ops[_opsTaken + i];
        _opsTaken += n;
        if (_opsTaken == _ops.Count)
        {
            _ops.Clear();
            _opsTaken = 0;
        }
        return n;
    }

    //-------------------------------------------------------------------------
    // Level init and shutdown.
    //-------------------------------------------------------------------------

    /// <summary>LevelInitPreEntity: the level's leaves, each empty.</summary>
    public void LevelInitPreEntity(int leafCount)
    {
        if (leafCount < 0)
            leafCount = 0;
        int total = _leafCount + leafCount;
        if (_leaves.Length < total)
            Array.Resize(ref _leaves, total);
        for (int i = _leafCount; i < total; i++)
            _leaves[i] = new LeafData { DetailPropRenderFrame = -1 };
        _leafCount = total;
        _renderablesInLeaf.EnsureBuckets(total);
        _shadowsInLeaf.EnsureBuckets(total);
    }

    /// <summary>
    /// LevelShutdownPostEntity: everything purged, and each leaf's sub-system
    /// data deleted (queued, in leaf order). Handles start again from 0; the
    /// render handles renderables still hold are not reset (as stock's are not).
    /// </summary>
    public void LevelShutdownPostEntity()
    {
        _viewModels.Clear();
        _renderableHandles.Purge();
        _renderablesInLeaf.Purge();
        _shadowHandles.Purge();
        for (int i = 0; i < _leafCount; i++)
        {
            if (_leaves[i].SubSystemData != 0)
            {
                Op(LeafOpKind.DeleteSubSystemData, _leaves[i].SubSystemData);
                _leaves[i].SubSystemData = 0;
            }
        }
        _leafCount = 0;
        _shadowsInLeaf.Purge();
        _dirty.Clear();
    }

    //-------------------------------------------------------------------------
    // Renderables.
    //-------------------------------------------------------------------------

    private static bool IsViewModelGroup(int group) => group == GroupViewModelTranslucent || group == GroupViewModelOpaque;

    // NewRenderable: a slot, its model's kind in the flags, and the view models' list.
    private int NewRenderable(long renderable, int group, int flags, int modelType)
    {
        int handle = _renderableHandles.Alloc();
        if (handle >= _renderables.Length)
            Array.Resize(ref _renderables, Math.Max(handle + 1, _renderables.Length * 2));
        _renderablesInLeaf.EnsureElements(handle + 1);

        if (modelType == ModBrush)
            flags |= FlagBrushModel;
        else if (modelType == ModStudio)
            flags |= FlagStudioModel;

        ref RenderableInfo info = ref _renderables[handle];
        info.Renderable = renderable;
        info.RenderFrame = -1;
        info.RenderFrame2 = -1;
        info.TranslucencyCalculated = -1;
        info.TranslucencyCalculatedView = ViewIllegal;
        info.Flags = (byte)flags;
        info.Group = (byte)group;
        info.EnumCount = 0;
        info.RenderLeaf = uint.MaxValue;
        if (IsViewModelGroup(group))
            _viewModels.Add(handle);
        return handle;
    }

    /// <summary>
    /// CreateRenderableHandle: the engine's static props and the client's
    /// handle-only renderables. Translucent if it says so (two-pass if it
    /// says that too); a static prop's opaque group is the static one. The
    /// handle, which native writes into the renderable.
    /// </summary>
    public int CreateRenderableHandle(long renderable, bool staticProp, int modelType, bool transparent, bool twoPass)
    {
        int group = transparent ? GroupTranslucentEntity : GroupOpaqueEntity;
        bool bTwoPass = group == GroupTranslucentEntity && twoPass;
        int flags = 0;
        if (staticProp)
        {
            flags = FlagStaticProp;
            if (group == GroupOpaqueEntity)
                group = GroupOpaqueStatic;
        }
        if (bTwoPass)
            flags |= FlagTwoPass;
        return NewRenderable(renderable, group, flags, modelType);
    }

    /// <summary>
    /// AddRenderable: a renderable in a group (two-pass is translucent with
    /// the flag), dirty so the next PreRender puts it in its leaves. The handle.
    /// </summary>
    public int AddRenderable(long renderable, int group, int modelType)
    {
        int flags = FlagHasChanged;
        if (group == GroupTwoPass)
        {
            group = GroupTranslucentEntity;
            flags |= FlagTwoPass;
        }
        int handle = NewRenderable(renderable, group, flags, modelType);
        _dirty.Add(handle);
        return handle;
    }

    /// <summary>
    /// RemoveRenderable: the renderable's handle reset, off the dirty and view
    /// model lists, out of its leaves (its receivers cleared), and its slot
    /// freed. A handle that names nothing (as after a level's purge) does nothing.
    /// </summary>
    public void RemoveRenderable(int handle)
    {
        if (!_renderableHandles.IsValid(handle))
            return;
        ref RenderableInfo info = ref _renderables[handle];
        Op(LeafOpKind.SetRenderHandle, info.Renderable, InvalidHandle);

        if ((info.Flags & FlagHasChanged) != 0)
        {
            int i = _dirty.IndexOf(handle);
            if (i >= 0)
                FastRemove(_dirty, i);
        }
        if (IsViewModelGroup(info.Group))
            FastRemove(_viewModels, _viewModels.IndexOf(handle));

        RemoveFromTree(handle);
        _renderableHandles.Free(handle);
    }

    // CUtlVector::FastRemove: the last entry moves into the hole.
    private static void FastRemove(List<int> list, int i)
    {
        if (i < 0)
            return;
        int last = list.Count - 1;
        if (i < last)
            list[i] = list[last];
        list.RemoveAt(last);
    }

    /// <summary>ChangeRenderableRenderGroup: the static props' fade between opaque and translucent.</summary>
    public void ChangeRenderableRenderGroup(int handle, int group)
    {
        if (_renderableHandles.IsValid(handle))
            _renderables[handle].Group = (byte)group;
    }

    /// <summary>EnableAlternateSorting: a translucent renderable drawn in the furthest leaf it is in.</summary>
    public void EnableAlternateSorting(int handle, bool enable)
    {
        if (!_renderableHandles.IsValid(handle))
            return;
        ref RenderableInfo info = ref _renderables[handle];
        info.Flags = (byte)(enable ? info.Flags | FlagAlternateSorting : info.Flags & ~FlagAlternateSorting);
    }

    /// <summary>
    /// SetRenderGroup: two-pass is translucent with the flag (anything else
    /// clears it); a move into or out of the view model groups joins or
    /// leaves their list.
    /// </summary>
    public void SetRenderGroup(int handle, int group)
    {
        if (!_renderableHandles.IsValid(handle))
            return;
        ref RenderableInfo info = ref _renderables[handle];
        bool twoPass = false;
        if (group == GroupTwoPass)
        {
            twoPass = true;
            group = GroupTranslucentEntity;
        }
        info.Flags = (byte)(twoPass ? info.Flags | FlagTwoPass : info.Flags & ~FlagTwoPass);

        bool wasViewModel = IsViewModelGroup(info.Group);
        bool isViewModel = IsViewModelGroup(group);
        if (wasViewModel != isViewModel)
        {
            if (wasViewModel)
                FastRemove(_viewModels, _viewModels.IndexOf(handle));
            else
                _viewModels.Add(handle);
        }
        info.Group = (byte)group;
    }

    /// <summary>
    /// The forwarder's queue (<see cref="LeafPending"/>), in order: each a
    /// RenderableChanged, a ChangeRenderableRenderGroup or a SetRenderGroup, as if each had
    /// crossed on its own. A kind it does not know is skipped.
    /// </summary>
    public void ApplyPending(ReadOnlySpan<int> entries)
    {
        for (int i = 0; i + LeafPending.Words <= entries.Length; i += LeafPending.Words)
        {
            int handle = entries[i + 1] & 0xFFFF;
            if (entries[i] == LeafPending.RenderableChanged)
                RenderableChanged(handle);
            else if (entries[i] == LeafPending.ChangeRenderGroup)
                ChangeRenderableRenderGroup(handle, entries[i + 2]);
            else if (entries[i] == LeafPending.SetRenderGroup)
                SetRenderGroup(handle, entries[i + 2]);
        }
    }

    /// <summary>RenderableChanged: marked, and on the dirty list once until PreRender re-inserts it.</summary>
    public void RenderableChanged(int handle)
    {
        if (!_renderableHandles.IsValid(handle))
            return;
        ref RenderableInfo info = ref _renderables[handle];
        if ((info.Flags & FlagHasChanged) == 0)
        {
            info.Flags |= FlagHasChanged;
            _dirty.Add(handle);
        }
    }

    /// <summary>DrawStaticProps.</summary>
    public void DrawStaticProps(bool enable) => DrawStaticPropsEnabled = enable;

    /// <summary>DrawSmallEntities.</summary>
    public void DrawSmallEntities(bool enable) => DrawSmallObjects = enable;

    /// <summary>
    /// GetRenderableLeaves: the leaves the renderable is in, newest first, at
    /// most 128; -1 when the handle names nothing or the renderable is in none.
    /// </summary>
    public int GetRenderableLeaves(int handle, Span<int> leaves)
    {
        if (!_renderableHandles.IsValid(handle))
            return -1;
        int node = _renderablesInLeaf.FirstOfElement(handle);
        if (node < 0)
            return -1;
        int n = 0;
        int max = Math.Min(MaxLeavesPerRenderable, leaves.Length);
        for (; node >= 0 && n < max; node = _renderablesInLeaf.NextOfElement(node))
            leaves[n++] = _renderablesInLeaf.Bucket(node);
        return n;
    }

    /// <summary>
    /// GetRenderableLeaf: the first leaf the renderable is in, or (given the
    /// iterator the last call answered) the next; false at the end. The
    /// iterator is opaque, -1 meaning none.
    /// </summary>
    public bool GetRenderableLeaf(int handle, bool hasIterator, int iterator, out int leaf, out int nextIterator)
    {
        leaf = -1;
        nextIterator = -1;
        if (!_renderableHandles.IsValid(handle))
            return false;
        int node;
        if (hasIterator)
        {
            if (iterator < 0)
                return false;
            node = _renderablesInLeaf.NextOfElement(iterator);
        }
        else
        {
            node = _renderablesInLeaf.FirstOfElement(handle);
        }
        if (node < 0)
            return false;
        leaf = _renderablesInLeaf.Bucket(node);
        nextIterator = node;
        return true;
    }

    //-------------------------------------------------------------------------
    // Leaves: the detail objects and the sub-systems' data.
    //-------------------------------------------------------------------------

    /// <summary>
    /// SetSubSystemDataInLeaf: the leaf's data replaced, and the data it had
    /// answered for native to delete (0 for none). A leaf or index out of range
    /// stores nothing and answers 0 (stock asserts, and the new data leaks).
    /// </summary>
    public long SetSubSystemDataInLeaf(int leaf, int index, long data)
    {
        if (!IsValidLeaf(leaf) || index != 0)
            return 0;
        long old = _leaves[leaf].SubSystemData;
        _leaves[leaf].SubSystemData = data;
        return old;
    }

    /// <summary>GetSubSystemDataInLeaf, or 0.</summary>
    public long GetSubSystemDataInLeaf(int leaf, int index) =>
        IsValidLeaf(leaf) && index == 0 ? _leaves[leaf].SubSystemData : 0;

    /// <summary>SetDetailObjectsInLeaf: the leaf's range of detail objects (stock keeps 16 bits of each).</summary>
    public void SetDetailObjectsInLeaf(int leaf, int first, int count)
    {
        if (!IsValidLeaf(leaf))
            return;
        _leaves[leaf].FirstDetailProp = (ushort)first;
        _leaves[leaf].DetailPropCount = (ushort)count;
    }

    /// <summary>GetDetailObjectsInLeaf: false, with nothing written, for a leaf out of range.</summary>
    public bool GetDetailObjectsInLeaf(int leaf, out int first, out int count)
    {
        first = count = 0;
        if (!IsValidLeaf(leaf))
            return false;
        first = _leaves[leaf].FirstDetailProp;
        count = _leaves[leaf].DetailPropCount;
        return true;
    }

    /// <summary>
    /// DrawDetailObjectsInLeaf: the leaf marked as drawing its detail objects
    /// in the frame, and their range. A leaf out of range (stock does not
    /// check) answers an empty range.
    /// </summary>
    public void DrawDetailObjectsInLeaf(int leaf, int frame, out int first, out int count)
    {
        first = count = 0;
        if (!IsValidLeaf(leaf))
            return;
        _leaves[leaf].DetailPropRenderFrame = frame;
        first = _leaves[leaf].FirstDetailProp;
        count = _leaves[leaf].DetailPropCount;
    }

    /// <summary>
    /// DECISION 3, ShouldDrawDetailObjectsInLeaf: the leaf was marked this
    /// frame (it is close enough to the view) and has detail objects or a
    /// sub-system's data. False for a leaf out of range.
    /// </summary>
    public bool ShouldDrawDetailObjectsInLeaf(int leaf, int frame)
    {
        if (!IsValidLeaf(leaf))
            return false;
        ref LeafData data = ref _leaves[leaf];
        return data.DetailPropRenderFrame == frame && (data.DetailPropCount != 0 || data.SubSystemData != 0);
    }

    //-------------------------------------------------------------------------
    // Shadows.
    //-------------------------------------------------------------------------

    /// <summary>AddShadow: a shadow the shadow manager knows by its user id; its handle.</summary>
    public int AddShadow(int userId, int flags)
    {
        int handle = _shadowHandles.Alloc();
        if (handle >= _shadows.Length)
            Array.Resize(ref _shadows, Math.Max(handle + 1, _shadows.Length * 2));
        _shadowsInLeaf.EnsureElements(handle + 1);
        _shadows[handle] = new ShadowInfo { UserId = userId & 0xFFFF, Flags = flags & 0xFFFF, EnumCount = 0 };
        return handle;
    }

    /// <summary>RemoveShadow: out of every leaf, and its slot freed.</summary>
    public void RemoveShadow(int handle)
    {
        if (!_shadowHandles.IsValid(handle))
            return;
        _shadowsInLeaf.RemoveElement(handle);
        _shadowHandles.Free(handle);
    }

    /// <summary>
    /// ProjectShadow and ProjectFlashlight (stock's two bodies are the same
    /// but for an assert): out of the leaves it was in, then into these, and
    /// onto each renderable in them that takes it, once each.
    /// </summary>
    public void ProjectShadow(int handle, ReadOnlySpan<int> leaves)
    {
        if (!_shadowHandles.IsValid(handle))
            return;
        _shadowsInLeaf.RemoveElement(handle);
        ++ShadowEnum;
        _pendingReceivers.Clear();
        foreach (int leaf in leaves)
        {
            if (IsValidLeaf(leaf))
                AddShadowToLeaf(leaf, handle);
        }
        FinishReceivers();
    }

    // AddShadowToLeaf: the shadow into the leaf, and offered to each renderable
    // in it not yet offered it this time.
    private void AddShadowToLeaf(int leaf, int shadow)
    {
        _shadowsInLeaf.Add(leaf, shadow);
        for (int node = _renderablesInLeaf.FirstInBucket(leaf); node >= 0; node = _renderablesInLeaf.NextInBucket(node))
        {
            int renderable = _renderablesInLeaf.Element(node);
            ref RenderableInfo info = ref _renderables[renderable];
            if (info.EnumCount != ShadowEnum)
            {
                AddShadowToRenderable(renderable, shadow);
                info.EnumCount = ShadowEnum;
            }
        }
    }

    /// <summary>
    /// EnumerateShadowsInLeaves: each shadow in the leaves once, as an
    /// EnumShadow operation with its user id. Nothing for no leaves.
    /// </summary>
    public void EnumerateShadowsInLeaves(ReadOnlySpan<int> leaves)
    {
        if (leaves.Length == 0)
            return;
        ++ShadowEnum;
        foreach (int leaf in leaves)
        {
            if (!IsValidLeaf(leaf))
                continue;
            for (int node = _shadowsInLeaf.FirstInBucket(leaf); node >= 0; node = _shadowsInLeaf.NextInBucket(node))
            {
                ref ShadowInfo shadow = ref _shadows[_shadowsInLeaf.Element(node)];
                if (shadow.EnumCount != ShadowEnum)
                {
                    Op(LeafOpKind.EnumShadow, 0, shadow.UserId);
                    shadow.EnumCount = ShadowEnum;
                }
            }
        }
    }

    /// <summary>
    /// DECISION 4, ShouldRenderableReceiveShadow's first half: only a brush
    /// model, a static prop or a studio model takes projected textures at all.
    /// The second half is the renderable's own ShouldReceiveProjectedTextures,
    /// which native answers in a batch.
    /// </summary>
    internal static bool CanReceiveShadows(int flags) => (flags & (FlagBrushModel | FlagStaticProp | FlagStudioModel)) != 0;

    /// <summary>
    /// DECISION 4, whole: whether the renderable takes a projected texture of
    /// these kinds, asking it when its kind can.
    /// </summary>
    public bool ShouldRenderableReceiveShadow(int handle, int shadowFlags)
    {
        if (!_renderableHandles.IsValid(handle) || !CanReceiveShadows(_renderables[handle].Flags))
            return false;
        Span<int> answer = stackalloc int[1];
        _native.Receives([(nint)_renderables[handle].Renderable], [shadowFlags], answer);
        return answer[0] != 0;
    }

    // AddShadowToRenderable, deferred: the renderable's own answer for the
    // shadow's flags is asked in FinishReceivers' batch, which then adds it.
    private void AddShadowToRenderable(int renderable, int shadow)
    {
        if (CanReceiveShadows(_renderables[renderable].Flags))
            _pendingReceivers.Add((renderable, shadow));
    }

    // The receivers the last operation offered shadows to: one batch of their
    // answers, then each that takes its shadow is added (in offer order).
    private void FinishReceivers()
    {
        int n = _pendingReceivers.Count;
        if (n == 0)
            return;
        var renderables = new nint[n];
        var flags = new int[n];
        var answers = new int[n];
        for (int i = 0; i < n; i++)
        {
            renderables[i] = (nint)_renderables[_pendingReceivers[i].Handle].Renderable;
            flags[i] = _shadows[_pendingReceivers[i].Shadow].Flags;
        }
        _native.Receives(renderables, flags, answers);
        for (int i = 0; i < n; i++)
        {
            if (answers[i] == 0)
                continue;
            var (handle, shadow) = _pendingReceivers[i];
            ref RenderableInfo info = ref _renderables[handle];
            int kind = (info.Flags & FlagBrushModel) != 0 ? ReceiverBrushModel
                : (info.Flags & FlagStaticProp) != 0 ? ReceiverStaticProp
                : ReceiverStudioModel;
            Op(LeafOpKind.AddShadowToReceiver, info.Renderable, _shadows[shadow].UserId, kind);
        }
        _pendingReceivers.Clear();
    }

    //-------------------------------------------------------------------------
    // The leaves a renderable is in.
    //-------------------------------------------------------------------------

    // RemoveFromTree: out of every leaf, and the shadow manager's receivers
    // cleared for a brush or studio model.
    private void RemoveFromTree(int handle)
    {
        _renderablesInLeaf.RemoveElement(handle);
        ref RenderableInfo info = ref _renderables[handle];
        if ((info.Flags & FlagBrushModel) != 0)
            Op(LeafOpKind.RemoveAllShadowsFromReceiver, info.Renderable, 0, ReceiverBrushModel);
        else if ((info.Flags & FlagStudioModel) != 0)
            Op(LeafOpKind.RemoveAllShadowsFromReceiver, info.Renderable, 0, ReceiverStudioModel);
    }

    // The renderables whose ShouldReceiveProjectedTextures( mask ) the next
    // inserts depend on, and whose answer matters: a kind that can take one,
    // going into a leaf that holds a shadow. Answered in one batch into
    // `receives` (1, 0, or -1 not asked).
    private void AskMasks(ReadOnlySpan<int> handles, Func<int, ReadOnlySpan<int>> leavesOf, int[] receives)
    {
        _batchRenderables.Clear();
        _batchInts.Clear();
        for (int i = 0; i < handles.Length; i++)
        {
            receives[i] = -1;
            if (!CanReceiveShadows(_renderables[handles[i]].Flags))
                continue;
            bool anyShadow = false;
            foreach (int leaf in leavesOf(i))
            {
                if (IsValidLeaf(leaf) && _shadowsInLeaf.FirstInBucket(leaf) >= 0)
                {
                    anyShadow = true;
                    break;
                }
            }
            if (!anyShadow)
                continue;
            _batchRenderables.Add(_renderables[handles[i]].Renderable);
            _batchInts.Add(i);
        }
        int n = _batchRenderables.Count;
        if (n == 0)
            return;
        var renderables = new nint[n];
        var flags = new int[n];
        var answers = new int[n];
        for (int k = 0; k < n; k++)
        {
            renderables[k] = (nint)_batchRenderables[k];
            flags[k] = ShadowFlagsProjectedTextureMask;
        }
        _native.Receives(renderables, flags, answers);
        for (int k = 0; k < n; k++)
            receives[_batchInts[k]] = answers[k];
    }

    // AddRenderableToLeaf: into the leaf, and (when it takes projected
    // textures at all) offered each shadow in the leaf not offered to it yet.
    private void AddRenderableToLeaf(int leaf, int handle, int receivesMask)
    {
        _renderablesInLeaf.Add(leaf, handle);
        if (receivesMask <= 0)
            return;
        for (int node = _shadowsInLeaf.FirstInBucket(leaf); node >= 0; node = _shadowsInLeaf.NextInBucket(node))
        {
            int shadow = _shadowsInLeaf.Element(node);
            ref ShadowInfo info = ref _shadows[shadow];
            if (info.EnumCount != ShadowEnum)
            {
                AddShadowToRenderable(handle, shadow);
                info.EnumCount = ShadowEnum;
            }
        }
    }

    /// <summary>
    /// AddRenderableToLeaves: the engine's static prop into the leaves it
    /// computed (without advancing the shadow enumerator, as stock does not),
    /// and its area.
    /// </summary>
    public void AddRenderableToLeaves(int handle, ReadOnlySpan<int> leaves)
    {
        if (!_renderableHandles.IsValid(handle))
            return;
        int[] leafArray = leaves.ToArray();
        var receives = new int[1];
        AskMasks([handle], _ => leafArray, receives);
        _pendingReceivers.Clear();
        foreach (int leaf in leafArray)
        {
            if (IsValidLeaf(leaf))
                AddRenderableToLeaf(leaf, handle, receives[0]);
        }
        FinishReceivers();
        Span<int> area = stackalloc int[1];
        AreasOf([handle], area);
        _renderables[handle].Area = (short)area[0];
    }

    // GetRenderableArea for each handle, in one batch: the area all its leaves
    // are in (-1 when they span several), 0 when it is in none.
    private void AreasOf(ReadOnlySpan<int> handles, Span<int> areas)
    {
        _batchInts.Clear();
        int runs = 0;
        Span<int> leaves = stackalloc int[MaxLeavesPerRenderable];
        var asked = new int[handles.Length];
        for (int i = 0; i < handles.Length; i++)
        {
            int n = GetRenderableLeaves(handles[i], leaves);
            if (n == -1)
            {
                areas[i] = 0;
                asked[i] = -1;
                continue;
            }
            asked[i] = runs++;
            _batchInts.Add(n);
            for (int k = 0; k < n; k++)
                _batchInts.Add(leaves[k]);
        }
        if (runs == 0)
            return;
        var runWords = _batchInts.ToArray();
        var answers = new int[runs];
        _native.LeavesAreas(runWords, runs, answers);
        for (int i = 0; i < handles.Length; i++)
        {
            if (asked[i] >= 0)
                areas[i] = answers[asked[i]];
        }
    }

    /// <summary>
    /// PreRender: every dirty renderable out of its leaves and into the ones
    /// its box now touches (the last dirtied first, as stock walks the list
    /// backwards), the shadows in those leaves offered to it, and its area
    /// computed; the dirty list then empty. Its bounds, leaves, receivers and
    /// areas are each one batch.
    /// </summary>
    public void PreRender()
    {
        int n = _dirty.Count;
        if (n == 0)
            return;
        // Stock's order: i from the last down to the first.
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = _dirty[n - 1 - i];

        foreach (int handle in order)
            RemoveFromTree(handle);

        // The boxes (CalcRenderableWorldSpaceAABB_Fast, the renderable's own
        // world bounds), then the leaves each touches.
        var renderables = new nint[n];
        var what = new int[n];
        var facts = new int[n * LeafFacts.Words];
        for (int i = 0; i < n; i++)
        {
            renderables[i] = (nint)_renderables[order[i]].Renderable;
            what[i] = LeafFacts.WorldBounds;
        }
        _native.Facts(renderables, what, facts);
        var boxes = new float[n * 6];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 6; k++)
                boxes[6 * i + k] = BitConverter.Int32BitsToSingle(facts[i * LeafFacts.Words + 2 + k]);
        }
        int[] enumerated = EnumerateLeaves(boxes);
        var leafAt = new int[n];
        var leafCount = new int[n];
        for (int i = 0, at = 0; i < n; i++)
        {
            leafCount[i] = enumerated[at];
            leafAt[i] = at + 1;
            at += 1 + leafCount[i];
        }

        var receives = new int[n];
        AskMasks(order, i => enumerated.AsSpan(leafAt[i], leafCount[i]), receives);

        _pendingReceivers.Clear();
        for (int i = 0; i < n; i++)
        {
            // InsertIntoTree: each renderable takes each shadow once.
            ++ShadowEnum;
            for (int k = 0; k < leafCount[i]; k++)
            {
                int leaf = enumerated[leafAt[i] + k];
                if (IsValidLeaf(leaf))
                    AddRenderableToLeaf(leaf, order[i], receives[i]);
            }
        }
        FinishReceivers();

        var areas = new int[n];
        AreasOf(order, areas);
        for (int i = 0; i < n; i++)
        {
            ref RenderableInfo info = ref _renderables[order[i]];
            info.Flags = (byte)(info.Flags & ~FlagHasChanged);
            info.Area = (short)areas[i];
        }
        _dirty.RemoveRange(0, n);
    }

    // The engine's EnumerateLeavesInBox over the boxes, retried once with the
    // room it asked for.
    private int[] EnumerateLeaves(float[] boxes)
    {
        int need = boxes.Length / 6 * 9;
        if (_scratch.Length < need)
            _scratch = new int[need];
        int words = _native.EnumerateLeaves(boxes, _scratch);
        if (words < 0)
        {
            _scratch = new int[-words];
            words = _native.EnumerateLeaves(boxes, _scratch);
        }
        if (words < 0)
            throw new InvalidOperationException($"the leaf enumeration asked for {-words} words twice");
        return _scratch.AsSpan(0, words).ToArray();
    }

    //-------------------------------------------------------------------------
    // Hot reload: the registry outlives the module.
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Save(ReloadWriter writer)
    {
        writer.Write(1);  // format
        writer.Write(DrawStaticPropsEnabled);
        writer.Write(DrawSmallObjects);
        writer.Write(ShadowEnum);
        writer.Write(_leafCount);
        for (int i = 0; i < _leafCount; i++)
        {
            ref LeafData d = ref _leaves[i];
            writer.Write(d.FirstDetailProp);
            writer.Write(d.DetailPropCount);
            writer.Write(d.DetailPropRenderFrame);
            WriteLong(writer, d.SubSystemData);
        }
        _renderableHandles.Save(writer);
        for (int h = 0; h <= _renderableHandles.LastAlloc; h++)
        {
            ref RenderableInfo r = ref _renderables[h];
            WriteLong(writer, r.Renderable);
            writer.Write(r.RenderFrame);
            writer.Write(r.RenderFrame2);
            writer.Write(r.EnumCount);
            writer.Write(r.TranslucencyCalculated);
            writer.Write(unchecked((int)r.RenderLeaf));
            writer.Write(r.Flags | (r.Group << 8) | (r.TranslucencyCalculatedView << 16));
            writer.Write(r.Area);
        }
        _shadowHandles.Save(writer);
        for (int h = 0; h <= _shadowHandles.LastAlloc; h++)
        {
            writer.Write(_shadows[h].UserId);
            writer.Write(_shadows[h].Flags);
            writer.Write(_shadows[h].EnumCount);
        }
        _renderablesInLeaf.Save(writer);
        _shadowsInLeaf.Save(writer);
        WriteList(writer, _dirty);
        WriteList(writer, _viewModels);
    }

    /// <inheritdoc/>
    public void Restore(ReloadReader reader)
    {
        if (reader.ReadInt() != 1)
            throw new ReloadFormatException("the leaf registry's format is not 1");
        DrawStaticPropsEnabled = reader.ReadBool();
        DrawSmallObjects = reader.ReadBool();
        ShadowEnum = reader.ReadInt();
        _leafCount = reader.ReadInt();
        _leaves = new LeafData[_leafCount];
        for (int i = 0; i < _leafCount; i++)
        {
            _leaves[i].FirstDetailProp = (ushort)reader.ReadInt();
            _leaves[i].DetailPropCount = (ushort)reader.ReadInt();
            _leaves[i].DetailPropRenderFrame = reader.ReadInt();
            _leaves[i].SubSystemData = ReadLong(reader);
        }
        _renderableHandles.Restore(reader);
        _renderables = new RenderableInfo[Math.Max(256, _renderableHandles.LastAlloc + 1)];
        for (int h = 0; h <= _renderableHandles.LastAlloc; h++)
        {
            ref RenderableInfo r = ref _renderables[h];
            r.Renderable = ReadLong(reader);
            r.RenderFrame = reader.ReadInt();
            r.RenderFrame2 = reader.ReadInt();
            r.EnumCount = reader.ReadInt();
            r.TranslucencyCalculated = reader.ReadInt();
            r.RenderLeaf = unchecked((uint)reader.ReadInt());
            int packed = reader.ReadInt();
            r.Flags = (byte)packed;
            r.Group = (byte)(packed >> 8);
            r.TranslucencyCalculatedView = (sbyte)(packed >> 16);
            r.Area = (short)reader.ReadInt();
        }
        _shadowHandles.Restore(reader);
        _shadows = new ShadowInfo[Math.Max(64, _shadowHandles.LastAlloc + 1)];
        for (int h = 0; h <= _shadowHandles.LastAlloc; h++)
        {
            _shadows[h].UserId = reader.ReadInt();
            _shadows[h].Flags = reader.ReadInt();
            _shadows[h].EnumCount = reader.ReadInt();
        }
        _renderablesInLeaf.Restore(reader);
        _shadowsInLeaf.Restore(reader);
        ReadList(reader, _dirty);
        ReadList(reader, _viewModels);
        _ops.Clear();
        _opsTaken = 0;
    }

    private static void WriteLong(ReloadWriter writer, long value)
    {
        writer.Write(unchecked((int)value));
        writer.Write(unchecked((int)(value >> 32)));
    }

    private static long ReadLong(ReloadReader reader)
    {
        long lo = (uint)reader.ReadInt();
        long hi = reader.ReadInt();
        return lo | (hi << 32);
    }

    private static void WriteList(ReloadWriter writer, List<int> list)
    {
        writer.Write(list.Count);
        foreach (int v in list)
            writer.Write(v);
    }

    private static void ReadList(ReloadReader reader, List<int> list)
    {
        list.Clear();
        int n = reader.ReadInt();
        for (int i = 0; i < n; i++)
            list.Add(reader.ReadInt());
    }
}

/// <summary>
/// CUtlLinkedList's handle allocation: a freed handle is the next one given
/// out (LIFO), and otherwise the next never used; a purge starts again at 0.
/// </summary>
internal sealed class HandleList
{
    private int[] _next = new int[64];
    private bool[] _live = new bool[64];
    private int _firstFree = -1;

    public int LastAlloc { get; private set; } = -1;

    public int Live { get; private set; }

    public bool IsValid(int handle) => handle >= 0 && handle <= LastAlloc && _live[handle];

    public int Alloc()
    {
        int handle;
        if (_firstFree < 0)
        {
            handle = ++LastAlloc;
            if (handle >= _live.Length)
            {
                Array.Resize(ref _live, _live.Length * 2);
                Array.Resize(ref _next, _next.Length * 2);
            }
        }
        else
        {
            handle = _firstFree;
            _firstFree = _next[handle];
        }
        _live[handle] = true;
        ++Live;
        return handle;
    }

    public void Free(int handle)
    {
        _live[handle] = false;
        _next[handle] = _firstFree;
        _firstFree = handle;
        --Live;
    }

    public void Purge()
    {
        Array.Clear(_live);
        _firstFree = -1;
        LastAlloc = -1;
        Live = 0;
    }

    public void Save(ReloadWriter writer)
    {
        writer.Write(LastAlloc);
        writer.Write(_firstFree);
        for (int h = 0; h <= LastAlloc; h++)
        {
            writer.Write(_live[h]);
            writer.Write(_next[h]);
        }
    }

    public void Restore(ReloadReader reader)
    {
        LastAlloc = reader.ReadInt();
        _firstFree = reader.ReadInt();
        int size = Math.Max(64, LastAlloc + 1);
        _live = new bool[size];
        _next = new int[size];
        Live = 0;
        for (int h = 0; h <= LastAlloc; h++)
        {
            _live[h] = reader.ReadBool();
            _next[h] = reader.ReadInt();
            if (_live[h])
                ++Live;
        }
    }
}

/// <summary>
/// CBidirectionalSet: elements in buckets (renderables or shadows in leaves),
/// each bucket's elements and each element's buckets linked newest first, an
/// element removed from all its buckets at once.
/// </summary>
internal sealed class LeafBuckets
{
    private int[] _bucket = new int[256];
    private int[] _element = new int[256];
    private int[] _prevInBucket = new int[256];
    private int[] _nextInBucket = new int[256];
    private int[] _nextOfElement = new int[256];
    private int _freeNode = -1;
    private int _nodes;

    private int[] _firstInBucket = [];
    private int[] _firstOfElement = new int[64];

    public LeafBuckets()
    {
        Array.Fill(_firstOfElement, -1);
    }

    public void EnsureBuckets(int count)
    {
        int old = _firstInBucket.Length;
        if (old >= count)
            return;
        Array.Resize(ref _firstInBucket, count);
        Array.Fill(_firstInBucket, -1, old, count - old);
    }

    public void EnsureElements(int count)
    {
        int old = _firstOfElement.Length;
        if (old >= count)
            return;
        int size = Math.Max(count, old * 2);
        Array.Resize(ref _firstOfElement, size);
        Array.Fill(_firstOfElement, -1, old, size - old);
    }

    public int FirstInBucket(int bucket) => bucket < _firstInBucket.Length ? _firstInBucket[bucket] : -1;

    public int NextInBucket(int node) => _nextInBucket[node];

    public int Element(int node) => _element[node];

    public int FirstOfElement(int element) => element < _firstOfElement.Length ? _firstOfElement[element] : -1;

    public int NextOfElement(int node) => _nextOfElement[node];

    public int Bucket(int node) => _bucket[node];

    public void Add(int bucket, int element)
    {
        int node = AllocNode();
        _bucket[node] = bucket;
        _element[node] = element;
        _prevInBucket[node] = -1;
        int head = _firstInBucket[bucket];
        _nextInBucket[node] = head;
        if (head >= 0)
            _prevInBucket[head] = node;
        _firstInBucket[bucket] = node;
        _nextOfElement[node] = _firstOfElement[element];
        _firstOfElement[element] = node;
    }

    public void RemoveElement(int element)
    {
        if (element >= _firstOfElement.Length)
            return;
        int node = _firstOfElement[element];
        while (node >= 0)
        {
            int prev = _prevInBucket[node], next = _nextInBucket[node];
            if (prev >= 0)
                _nextInBucket[prev] = next;
            else
                _firstInBucket[_bucket[node]] = next;
            if (next >= 0)
                _prevInBucket[next] = prev;
            int following = _nextOfElement[node];
            FreeNode(node);
            node = following;
        }
        _firstOfElement[element] = -1;
    }

    public void Purge()
    {
        _firstInBucket = [];
        Array.Fill(_firstOfElement, -1);
        _freeNode = -1;
        _nodes = 0;
    }

    private int AllocNode()
    {
        if (_freeNode >= 0)
        {
            int n = _freeNode;
            _freeNode = _nextInBucket[n];
            return n;
        }
        if (_nodes == _bucket.Length)
        {
            int size = _nodes * 2;
            Array.Resize(ref _bucket, size);
            Array.Resize(ref _element, size);
            Array.Resize(ref _prevInBucket, size);
            Array.Resize(ref _nextInBucket, size);
            Array.Resize(ref _nextOfElement, size);
        }
        return _nodes++;
    }

    private void FreeNode(int node)
    {
        _nextInBucket[node] = _freeNode;
        _freeNode = node;
    }

    public void Save(ReloadWriter writer)
    {
        // Each bucket's elements, newest first, as runs; the elements' own
        // chains are rebuilt from them in the same order.
        writer.Write(_firstInBucket.Length);
        writer.Write(_firstOfElement.Length);
        // The chains per element, so a restore rebuilds both orders exactly:
        // every node as (bucket, element), in an order Add() reproduces both
        // links from (oldest first per element and per bucket).
        var nodes = new List<int>();
        var seen = new HashSet<int>();
        for (int b = 0; b < _firstInBucket.Length; b++)
            for (int n = _firstInBucket[b]; n >= 0; n = _nextInBucket[n])
                seen.Add(n);
        writer.Write(seen.Count);
        // Rank each node by its depth in both chains: replaying nodes in
        // decreasing depth of their bucket chain AND element chain is only
        // consistent when both orders agree, which Add's history guarantees.
        // Write each node's bucket position and element position; restore
        // sorts by an order consistent with both.
        foreach (int n in seen)
        {
            writer.Write(_bucket[n]);
            writer.Write(_element[n]);
            writer.Write(Depth(_firstInBucket[_bucket[n]], n, _nextInBucket));
            writer.Write(Depth(_firstOfElement[_element[n]], n, _nextOfElement));
        }
    }

    private static int Depth(int head, int node, int[] next)
    {
        int d = 0;
        for (int n = head; n >= 0 && n != node; n = next[n])
            ++d;
        return d;
    }

    public void Restore(ReloadReader reader)
    {
        int buckets = reader.ReadInt();
        int elements = reader.ReadInt();
        Purge();
        _firstInBucket = new int[buckets];
        Array.Fill(_firstInBucket, -1);
        _firstOfElement = new int[Math.Max(elements, 1)];
        Array.Fill(_firstOfElement, -1);
        int count = reader.ReadInt();
        var entries = new (int Bucket, int Element, int BucketDepth, int ElementDepth)[count];
        for (int i = 0; i < count; i++)
            entries[i] = (reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt());
        // Re-add oldest first: per bucket the deepest first, per element the
        // deepest first. A topological order over both chains: repeatedly add
        // a node that is the deepest remaining in its bucket and its element.
        var byBucket = entries.GroupBy(e => e.Bucket).ToDictionary(g => g.Key, g => new Stack<int>(g.OrderBy(e => e.BucketDepth).Select(e => e.Element)));
        var byElement = entries.GroupBy(e => e.Element).ToDictionary(g => g.Key, g => new Stack<int>(g.OrderBy(e => e.ElementDepth).Select(e => e.Bucket)));
        int added = 0;
        bool progress = true;
        while (added < count && progress)
        {
            progress = false;
            foreach (var (bucket, stack) in byBucket)
            {
                while (stack.Count > 0)
                {
                    int element = stack.Peek();
                    if (!byElement.TryGetValue(element, out var es) || es.Count == 0 || es.Peek() != bucket)
                        break;
                    stack.Pop();
                    es.Pop();
                    Add(bucket, element);
                    ++added;
                    progress = true;
                }
            }
        }
        if (added != count)
            throw new ReloadFormatException($"the leaf buckets' orders could not be rebuilt ({added} of {count})");
    }
}
