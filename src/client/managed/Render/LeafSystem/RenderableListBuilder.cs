//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.LeafSystem;

/// <summary>The ConVars the leaf system's decisions read (stock declared them in clientleafsystem.cpp).</summary>
public interface ILeafListSettings
{
    /// <summary>r_PortalTestEnts: clip entities against their area's portal frustum.</summary>
    bool PortalTestEnts { get; }

    /// <summary>r_portalsopenall.</summary>
    bool PortalsOpenAll { get; }

    /// <summary>cl_drawleaf: draw only that world-list leaf's renderables (a debug build's filter); -1 all.</summary>
    int DrawLeaf { get; }
}

/// <summary>
/// What runs before each blend batch asks the renderables for their blends:
/// the standalone entities' per-view fade (<c>StandaloneFadePass</c>), which
/// writes the blend each one's <c>GetFxBlend</c> then answers.
/// </summary>
public interface IRenderableBlendPass
{
    void BeforeBlends();
}

/// <summary>
/// The leaf system's per-view decisions (C7 cluster 12, L1 and L2 of
/// docs/plans/c7_leaf_system.md), ported from clientleafsystem.cpp and made
/// over the managed registry (<see cref="LeafRegistry"/>):
/// <list type="bullet">
/// <item>DECISION 1, which renderables a view draws and in which group
/// (CClientLeafSystem::CollateRenderablesInLeaf and DetectBucketedRenderGroup,
/// over the leaves in BuildRenderablesList's order);</item>
/// <item>DECISION 2, the leaf each translucent renderable renders in
/// (ComputeTranslucentRenderLeaf) and the back-to-front order of each leaf's
/// new translucent entries (SortEntities);</item>
/// <item>DECISION 5, the view models' opaque/translucent split
/// (CollateViewModelRenderables).</item>
/// </list>
/// Each answers into the registry's operation queue
/// (<see cref="LeafOpKind.ListEntry"/>, <see cref="LeafOpKind.ViewModel"/>)
/// for the forwarder to write out. Every question of a renderable or the
/// engine is a batch: a view asks the renderables its registry checks keep for
/// their blends, the ones its blend keeps for their boxes, and the engine only
/// about those boxes, as stock asked only what its earlier arms kept. The
/// scratch is per instance and reused; one view at a time.
/// </summary>
public sealed class RenderableListBuilder
{
    // clientleafsystem.cpp's render flags.
    private const int RenderFlagsTwoPass = 0x01;
    private const int RenderFlagsStaticProp = 0x02;
    private const int RenderFlagsStudioModel = 0x08;
    private const int RenderFlagsAlternateSorting = 0x20;

    // engine/IClientLeafSystem.h's render groups.
    private const int NumOpaqueEntBuckets = 4;
    private const int GroupOpaqueStatic = (NumOpaqueEntBuckets - 1) * 2;
    private const int GroupOpaqueEntity = GroupOpaqueStatic + 1;
    private const int GroupTranslucentEntity = GroupOpaqueEntity + 1;
    private const int GroupViewModelOpaque = GroupTranslucentEntity + 2;
    private const int GroupCount = GroupViewModelOpaque + 4;

    // CClientRenderablesList::MAX_GROUP_ENTITIES.
    private const int MaxGroupEntities = 4096;

    // The list being built: per group, its entries (source, leaf index, two-pass).
    private readonly int[][] _entries = new int[GroupCount][];
    private readonly int[] _counts = new int[GroupCount];
    private readonly float[] _dists = new float[MaxGroupEntities];

    // DETAIL_PROP_RENDER_HANDLE.
    private const int DetailPropRenderHandle = 0xFFFE;

    private readonly LeafRegistry _registry;
    private readonly ILeafListSettings _settings;

    // Scratch, per view.
    private readonly List<int> _order = [];
    private readonly List<long> _toBlend = [];
    private readonly int[] _pending = new int[LeafPending.Words * LeafPending.Max];
    private readonly List<int> _candidates = [];        // handle per candidate
    private readonly List<int> _candidateLeaf = [];     // its world-list leaf index
    private readonly List<int> _leafCandidateStart = [];
    private readonly List<int> _detailRanges = [];
    private readonly List<int> _detailLeaf = [];        // world-list leaf index per detail model
    private readonly List<int> _leafDetailStart = [];
    private nint[] _detailModels = [];
    private int[] _alpha = [];
    private int[] _candidateFacts = [];
    private int[] _detailFacts = [];
    private int[] _answers = [];
    private bool[] _survives = [];

    // The pass each blend batch is preceded by (none in a fact that builds the
    // lists alone).
    private readonly IRenderableBlendPass? _blendPass;

    public RenderableListBuilder(LeafRegistry registry, ILeafListSettings settings)
        : this(registry, settings, null)
    {
    }

    public RenderableListBuilder(LeafRegistry registry, ILeafListSettings settings, IRenderableBlendPass? blendPass)
    {
        _registry = registry;
        _settings = settings;
        _blendPass = blendPass;
        for (int g = 0; g < GroupCount; g++)
            _entries[g] = new int[MaxGroupEntities * 3];
    }

    private LeafRegistry Registry => _registry;

    /// <summary>
    /// DECISION 2's first half, ComputeTranslucentRenderLeaf: each renderable
    /// in the view's leaves computes its blend once per frame and view (one
    /// batch, before anything reads a group the blend may change), then,
    /// walking the leaves front to back, a translucent renderable met first
    /// this frame renders in that leaf, and under alternate sorting in the
    /// last one it is met in.
    /// </summary>
    public void ComputeTranslucentRenderLeaf(ReadOnlySpan<int> leaves, int frameNumber, int viewId, int globalFrame, nint system = 0)
    {
        LeafRegistry reg = Registry;
        LeafBuckets inLeaf = reg.RenderablesInLeaf;
        _order.Clear();
        _toBlend.Clear();
        foreach (int leaf in leaves)
        {
            if (!reg.IsValidLeaf(leaf))
                continue;
            _order.Add(-1 - leaf);
            for (int node = inLeaf.FirstInBucket(leaf); node >= 0; node = inLeaf.NextInBucket(node))
            {
                int handle = inLeaf.Element(node);
                ref LeafRegistry.RenderableInfo info = ref reg.Info(handle);
                if (info.TranslucencyCalculated != globalFrame || info.TranslucencyCalculatedView != viewId)
                {
                    _toBlend.Add(info.Renderable);
                    info.TranslucencyCalculated = globalFrame;
                    info.TranslucencyCalculatedView = (sbyte)viewId;
                }
                _order.Add(handle);
            }
        }
        if (_toBlend.Count > 0)
            Blend(reg, system);

        int current = 0;
        foreach (int entry in _order)
        {
            if (entry < 0)
            {
                current = -1 - entry;
                continue;
            }
            ref LeafRegistry.RenderableInfo info = ref reg.Info(entry);
            bool translucent = info.Group == GroupTranslucentEntity;
            if (info.RenderFrame != frameNumber)
            {
                if (translucent)
                    info.RenderLeaf = (uint)current;
                info.RenderFrame = frameNumber;
            }
            else if ((info.Flags & RenderFlagsAlternateSorting) != 0 && translucent)
            {
                info.RenderLeaf = (uint)current;
            }
        }
    }

    // The blend batch, then the group changes its blends queued (a fading
    // static prop's), applied before the caller reads the groups.
    private void Blend(LeafRegistry reg, nint system)
    {
        _blendPass?.BeforeBlends();
        int n = reg.Native.ComputeFxBlends(system, Pointers(_toBlend), _pending);
        if (n > 0)
            reg.ApplyPending(_pending.AsSpan(0, LeafPending.Words * Math.Min(n, LeafPending.Max)));
    }

    private static nint[] Pointers(List<long> list)
    {
        var pointers = new nint[list.Count];
        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = (nint)list[i];
        return pointers;
    }

    /// <summary>
    /// DECISIONS 1 AND 2, BuildRenderablesList over the registry: each leaf's
    /// markers, renderables and detail objects (CollateRenderablesInLeaf's
    /// arms), then the leaf's new translucent entries sorted back to front.
    /// The list is queued as <see cref="LeafOpKind.ListEntry"/> operations,
    /// group by group, and a <see cref="LeafOpKind.ListOverflow"/> when a group
    /// filled.
    /// </summary>
    public void BuildRenderablesList(in LeafView view)
    {
        LeafRegistry reg = Registry;
        ILeafListSettings settings = _settings;
        ILeafNative native = reg.Native;
        LeafBuckets inLeaf = reg.RenderablesInLeaf;
        ReadOnlySpan<int> leaves = view.Leaves;
        int frame = view.RenderFrame;
        bool drawTranslucent = view.DrawTranslucent;
        bool portalTestEnts = settings.PortalTestEnts && !settings.PortalsOpenAll;
        int drawLeaf = view.DrawLeafFilter ? settings.DrawLeaf : -1;

        // The registry's arms, in stock's order: the static-prop switch, then
        // one entry per renderable per frame (a translucent one only in the
        // leaf it renders in). Each leaf's detail range when the leaf draws
        // its detail objects.
        _candidates.Clear();
        _candidateLeaf.Clear();
        _leafCandidateStart.Clear();
        _detailRanges.Clear();
        _detailLeaf.Clear();
        _leafDetailStart.Clear();
        for (int i = 0; i < leaves.Length; i++)
        {
            int leaf = leaves[i];
            _leafCandidateStart.Add(_candidates.Count);
            _leafDetailStart.Add(_detailLeaf.Count);
            if (!reg.IsValidLeaf(leaf))
                continue;
            for (int node = inLeaf.FirstInBucket(leaf); node >= 0; node = inLeaf.NextInBucket(node))
            {
                int handle = inLeaf.Element(node);
                ref LeafRegistry.RenderableInfo info = ref reg.Info(handle);
                if (!reg.DrawStaticPropsEnabled && (info.Flags & RenderFlagsStaticProp) != 0)
                    continue;
                if (info.Group != GroupTranslucentEntity)
                {
                    if (info.RenderFrame2 == frame)
                        continue;
                    info.RenderFrame2 = frame;
                }
                else if (info.RenderLeaf != (uint)leaf)
                {
                    continue;
                }
                _candidates.Add(handle);
                _candidateLeaf.Add(i);
            }
            if (view.DrawDetail && reg.ShouldDrawDetailObjectsInLeaf(leaf, view.DetailBuildFrame)
                && reg.GetDetailObjectsInLeaf(leaf, out int first, out int count) && count > 0)
            {
                _detailRanges.Add(first);
                _detailRanges.Add(count);
                for (int k = 0; k < count; k++)
                    _detailLeaf.Add(i);
            }
        }
        _leafCandidateStart.Add(_candidates.Count);
        _leafDetailStart.Add(_detailLeaf.Count);

        int nCandidates = _candidates.Count;
        int nDetails = _detailLeaf.Count;
        if (_detailModels.Length < nDetails)
            _detailModels = new nint[nDetails];
        if (nDetails > 0)
            native.DetailModels(view.DetailSystem, _detailRanges.ToArray(), _detailModels.AsSpan(0, nDetails));
        Grow(ref _alpha, nCandidates);
        Grow(ref _survives, nCandidates);
        Grow(ref _answers, nCandidates);
        Grow(ref _candidateFacts, nCandidates * LeafFacts.Words);
        Grow(ref _detailFacts, nDetails * LeafFacts.Words);

        // The first batch: the candidates' blends (asked only when the view
        // draws translucent objects) and the detail models' transparency.
        var pointers = new List<nint>();
        var what = new List<int>();
        var into = new List<(bool Detail, int Index)>();
        if (drawTranslucent)
        {
            for (int c = 0; c < nCandidates; c++)
            {
                pointers.Add((nint)reg.Info(_candidates[c]).Renderable);
                what.Add(LeafFacts.Blend);
                into.Add((false, c));
            }
        }
        for (int d = 0; d < nDetails; d++)
        {
            if (_detailModels[d] == 0)
                continue;
            pointers.Add(_detailModels[d]);
            what.Add(LeafFacts.Transparent);
            into.Add((true, d));
        }
        AskFacts(native, pointers, what, into);

        // An invisible renderable is not drawn (stock's alpha is a byte; with
        // no translucent objects it is 255). The second batch: the visible
        // ones' boxes (and, for the translucent ones, what the sort centres
        // on), and the transparent detail models' blends and centres.
        pointers.Clear();
        what.Clear();
        into.Clear();
        for (int c = 0; c < nCandidates; c++)
        {
            _alpha[c] = drawTranslucent ? (byte)_candidateFacts[c * LeafFacts.Words] : 255;
            _survives[c] = _alpha[c] != 0;
            if (!_survives[c])
                continue;
            ref LeafRegistry.RenderableInfo info = ref reg.Info(_candidates[c]);
            pointers.Add((nint)info.Renderable);
            what.Add(LeafFacts.WorldBounds | (info.Group == GroupTranslucentEntity ? LeafFacts.RenderBounds | LeafFacts.Origin : 0));
            into.Add((false, c));
        }
        if (drawTranslucent)
        {
            for (int d = 0; d < nDetails; d++)
            {
                if (_detailModels[d] == 0 || _detailFacts[d * LeafFacts.Words + 1] == 0)
                    continue;
                pointers.Add(_detailModels[d]);
                what.Add(LeafFacts.Blend | LeafFacts.RenderBounds | LeafFacts.Origin);
                into.Add((true, d));
            }
        }
        AskFacts(native, pointers, what, into);

        // The engine's questions, for the visible ones only: inside an area,
        // that area's portal frustum, else the main frustum; then occlusion
        // for a studio model.
        var boxes = new List<float>();
        var programs = new List<int>();
        var asked = new List<int>();
        for (int c = 0; c < nCandidates; c++)
        {
            _answers[c] = 0;
            if (!_survives[c])
                continue;
            ref LeafRegistry.RenderableInfo info = ref reg.Info(_candidates[c]);
            int tests = portalTestEnts && info.Area != -1 ? LeafQuery.Area : LeafQuery.Cull;
            if ((info.Flags & RenderFlagsStudioModel) != 0)
                tests |= LeafQuery.Occluded;
            for (int k = 0; k < 6; k++)
                boxes.Add(BitConverter.Int32BitsToSingle(_candidateFacts[c * LeafFacts.Words + 2 + k]));
            programs.Add(LeafQuery.Program(tests, (tests & LeafQuery.Area) != 0 ? info.Area : 0));
            asked.Add(c);
        }
        if (asked.Count > 0)
        {
            var answers = new int[asked.Count];
            native.BoxQueries(boxes.ToArray(), programs.ToArray(), answers);
            for (int k = 0; k < asked.Count; k++)
                _answers[asked[k]] = answers[k];
        }

        // The list, leaf by leaf.
        Array.Clear(_counts);
        int overflow = 0;
        for (int i = 0; i < leaves.Length; i++)
        {
            int translucentBefore = _counts[GroupTranslucentEntity];
            Add(GroupOpaqueStatic, -1, i, 0, drawLeaf, ref overflow);
            Add(GroupOpaqueEntity, -1, i, 0, drawLeaf, ref overflow);

            for (int c = _leafCandidateStart[i]; c < _leafCandidateStart[i + 1]; c++)
            {
                if (!_survives[c] || _answers[c] != 0)
                    continue;
                ref LeafRegistry.RenderableInfo info = ref reg.Info(_candidates[c]);
                int group = info.Group;
                if (group != GroupTranslucentEntity)
                {
                    if (group >= GroupOpaqueStatic && group <= GroupOpaqueEntity)
                    {
                        int at = c * LeafFacts.Words + 2;
                        float dx = F(_candidateFacts[at + 3]) - F(_candidateFacts[at]);
                        float dy = F(_candidateFacts[at + 4]) - F(_candidateFacts[at + 1]);
                        float dz = F(_candidateFacts[at + 5]) - F(_candidateFacts[at + 2]);
                        float dimension = Max(Max(MathF.Abs(dx), MathF.Abs(dy)), MathF.Abs(dz));
                        group = BucketedRenderGroup(group, dimension);
                    }
                    Add(group, c, i, 0, drawLeaf, ref overflow);
                }
                else
                {
                    bool twoPass = (info.Flags & RenderFlagsTwoPass) != 0 && _alpha[c] == 255;
                    if (drawTranslucent)
                        Add(GroupTranslucentEntity, c, i, twoPass ? 1 : 0, drawLeaf, ref overflow);
                    if (twoPass)
                        Add(GroupOpaqueEntity, c, i, 1, drawLeaf, ref overflow);
                }
            }

            // The detail objects, which have no render handles.
            for (int d = _leafDetailStart[i]; d < _leafDetailStart[i + 1]; d++)
            {
                if (_detailModels[d] == 0)
                    continue;
                if (_detailFacts[d * LeafFacts.Words + 1] != 0)
                {
                    if (drawTranslucent && _detailFacts[d * LeafFacts.Words] > 0)
                        Add(GroupTranslucentEntity, -2 - d, i, 0, drawLeaf, ref overflow);
                }
                else
                {
                    Add(GroupOpaqueEntity, -2 - d, i, 0, drawLeaf, ref overflow);
                }
            }

            int added = _counts[GroupTranslucentEntity] - translucentBefore;
            if (added != 0 && drawTranslucent)
                SortRegistryEntries(translucentBefore, added, view);
        }

        for (int g = 0; g < GroupCount; g++)
        {
            int[] list = _entries[g];
            for (int e = 0; e < _counts[g]; e++)
            {
                int source = list[3 * e];
                long renderable = 0;
                long handle = 0;
                if (source >= 0)
                {
                    handle = _candidates[source];
                    renderable = reg.Info(_candidates[source]).Renderable;
                }
                else if (source <= -2)
                {
                    handle = DetailPropRenderHandle;
                    renderable = _detailModels[-2 - source];
                }
                long packed = handle | ((long)(list[3 * e + 1] & 0xFFFF) << 16) | ((long)(list[3 * e + 2] & 0xFFFF) << 32);
                reg.Op(LeafOpKind.ListEntry, renderable, packed, g);
            }
        }
        if (overflow != 0)
            reg.Op(LeafOpKind.ListOverflow, 0, overflow);
    }

    // One facts batch into the candidates' or the detail models' rows.
    private void AskFacts(ILeafNative native, List<nint> pointers, List<int> what, List<(bool Detail, int Index)> into)
    {
        int n = pointers.Count;
        if (n == 0)
            return;
        var facts = new int[n * LeafFacts.Words];
        native.Facts(pointers.ToArray(), what.ToArray(), facts);
        for (int k = 0; k < n; k++)
        {
            int[] target = into[k].Detail ? _detailFacts : _candidateFacts;
            int at = into[k].Index * LeafFacts.Words;
            int mask = what[k];
            CopyIf(mask, LeafFacts.Blend, facts, k, 0, 1, target, at);
            CopyIf(mask, LeafFacts.Transparent, facts, k, 1, 1, target, at);
            CopyIf(mask, LeafFacts.WorldBounds, facts, k, 2, 6, target, at);
            CopyIf(mask, LeafFacts.RenderBounds, facts, k, 8, 6, target, at);
            CopyIf(mask, LeafFacts.Origin, facts, k, 14, 3, target, at);
        }
    }

    private static void CopyIf(int mask, int bit, int[] facts, int k, int word, int count, int[] target, int at)
    {
        if ((mask & bit) == 0)
            return;
        Array.Copy(facts, k * LeafFacts.Words + word, target, at + word, count);
    }

    private static void Grow<T>(ref T[] array, int count)
    {
        if (array.Length < count)
            array = new T[Math.Max(count, array.Length * 2)];
    }

    private static float F(int bits) => BitConverter.Int32BitsToSingle(bits);

    // SortEntities over the registry path's sources: the centre of each
    // entry's render bounds about its origin, its distance along the view's
    // forward, and stock's H-sort, swap for swap.
    private void SortRegistryEntries(int start, int count, in LeafView view)
    {
        if (count <= 1)
            return;
        int[] list = _entries[GroupTranslucentEntity];
        for (int i = 0; i < count; i++)
        {
            int source = list[3 * (start + i)];
            int[] facts = source >= 0 ? _candidateFacts : _detailFacts;
            int at = (source >= 0 ? source : -2 - source) * LeafFacts.Words;
            float cx = F(facts[at + 8]) + F(facts[at + 11]);
            float cy = F(facts[at + 9]) + F(facts[at + 12]);
            float cz = F(facts[at + 10]) + F(facts[at + 13]);
            cx = F(facts[at + 14]) + 0.5f * cx;
            cy = F(facts[at + 15]) + 0.5f * cy;
            cz = F(facts[at + 16]) + 0.5f * cz;
            float dx = cx - view.OriginX, dy = cy - view.OriginY, dz = cz - view.OriginZ;
            _dists[i] = dx * view.ForwardX + dy * view.ForwardY + dz * view.ForwardZ;
        }
        HSort(list, start, count);
    }

    // Stock's H-sort over _dists, moving the list's entries with them.
    private void HSort(int[] list, int start, int count)
    {
        int stepSize = 4;
        while (stepSize != 0)
        {
            int end = count - stepSize;
            for (int i = 0; i < end; i += stepSize)
            {
                if (_dists[i] > _dists[i + stepSize])
                {
                    Swap(list, start + i, start + i + stepSize);
                    (_dists[i], _dists[i + stepSize]) = (_dists[i + stepSize], _dists[i]);
                    if (i == 0)
                        i = -stepSize;
                    else
                        i -= stepSize << 1;
                }
            }
            stepSize >>= 1;
        }
    }

    /// <summary>
    /// DECISION 5, CollateViewModelRenderables over the registry: each view
    /// model, from the end of the list, computes its blend (one batch; a blend
    /// may move it out of the view model groups), and is opaque when its group
    /// after that is the opaque view model group or an opaque entity, else
    /// translucent. Queued as <see cref="LeafOpKind.ViewModel"/> operations.
    /// </summary>
    public void CollateViewModelRenderables(nint system = 0)
    {
        LeafRegistry reg = Registry;
        List<int> viewModels = reg.ViewModels;
        var handles = new int[viewModels.Count];
        _toBlend.Clear();
        for (int i = viewModels.Count - 1, k = 0; i >= 0; --i, ++k)
        {
            handles[k] = viewModels[i];
            _toBlend.Add(reg.Info(handles[k]).Renderable);
        }
        if (_toBlend.Count > 0)
            Blend(reg, system);
        foreach (int handle in handles)
        {
            ref LeafRegistry.RenderableInfo info = ref reg.Info(handle);
            bool opaque = info.Group == GroupViewModelOpaque || info.Group == GroupOpaqueEntity;
            reg.Op(LeafOpKind.ViewModel, info.Renderable, opaque ? 0 : 1);
        }
    }

    // stock's MAX: the left one unless the right is greater.
    private static float Max(float a, float b) => a > b ? a : b;

    // DetectBucketedRenderGroup: tree, player and crate sizes split the opaque
    // static and entity groups into four buckets each.
    private static int BucketedRenderGroup(int group, float dimension)
    {
        int bucket;
        if (dimension >= 80f)
            bucket = dimension >= 200f ? 0 : 1;
        else
            bucket = dimension >= 30f ? 2 : 3;
        return group - ((NumOpaqueEntBuckets - 1) - bucket) * 2;
    }

    // AddRenderableToRenderList: the debug build's cl_drawleaf keeps one world
    // list leaf; a full group drops the entry and the console is told.
    private void Add(int group, int source, int leafIndex, int twoPass, int drawLeaf, ref int overflow)
    {
        if (drawLeaf >= 0 && leafIndex != drawLeaf)
            return;
        int n = _counts[group];
        if (n >= MaxGroupEntities)
        {
            overflow |= 1 << group;
            return;
        }
        int[] list = _entries[group];
        list[3 * n] = source;
        list[3 * n + 1] = leafIndex;
        list[3 * n + 2] = twoPass;
        _counts[group] = n + 1;
    }

    private static void Swap(int[] list, int a, int b)
    {
        for (int k = 0; k < 3; k++)
            (list[3 * a + k], list[3 * b + k]) = (list[3 * b + k], list[3 * a + k]);
    }
}
