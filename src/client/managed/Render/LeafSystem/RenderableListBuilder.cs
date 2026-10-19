//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.LeafSystem;

/// <summary>
/// Whether managed builds the views' render lists (1, the default) or
/// answers -1 and leaves them to stock's BuildRenderablesList, which is still
/// compiled until L2 moves the registry: the A/B switch the render trace
/// compares the two by.
/// </summary>
[ConVar("cs_cl_leaf_lists", "1", Help = "Build each view's render list and the view models' split in managed (1) or with the leaf system's stock code (0).")]
public sealed class CsClLeafListsConVar : FloatConVar { }

/// <summary>
/// The leaf system's per-view decisions (C7 cluster 12, L1 of
/// docs/plans/c7_leaf_system.md), ported from clientleafsystem.cpp:
/// <list type="bullet">
/// <item>which renderables a view draws, and in which group
/// (CClientLeafSystem::CollateRenderablesInLeaf and DetectBucketedRenderGroup,
/// over the leaves in BuildRenderablesList's order);</item>
/// <item>the leaf each translucent renderable renders in
/// (ComputeTranslucentRenderLeaf's second loop) and the back-to-front order
/// of each leaf's new translucent entries (SortEntities);</item>
/// <item>the view models' opaque/translucent split
/// (CollateViewModelRenderables).</item>
/// </list>
/// Each is one buffer in and one answer out: the registry is still stock's
/// (L2 moves it), read by src/client/native/leaf_view_gather.h, whose header
/// gives both layouts, with the engine's CullBox, DoesBoxTouchAreaFrustum and
/// IsOccluded already asked for every renderable. The scratch is per instance
/// and reused; one view at a time.
/// </summary>
public sealed class RenderableListBuilder : IClientLeafLists
{
    // The buffer's layout (leaf_view_gather.h).
    private const int HeaderWords = 14;
    private const int LeafWords = 6;
    private const int RenderableWords = 24;
    private const int DetailWords = 12;
    private const int StateWords = 3;
    private const int EntryWords = 4;

    private const int FlagDrawTranslucent = 1;
    private const int FlagDrawDetail = 2;
    private const int FlagDrawStaticProps = 4;
    private const int FlagPortalTestEnts = 8;
    private const int FlagPortalsOpenAll = 16;
    private const int FlagTranslucentLeafPass = 32;

    private const int AnswerCulled = 1;
    private const int AnswerTouchesArea = 2;
    private const int AnswerOccluded = 4;

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
    private int[] _frame = [];
    private int[] _leaf = [];
    private int[] _frame2 = [];

    private readonly Func<bool> _enabled;

    /// <summary>Always on: the oracle's and the tests' builder.</summary>
    public RenderableListBuilder() : this(() => true) { }

    /// <summary>The game's: on while <paramref name="enabled"/> says so (cs_cl_leaf_lists).</summary>
    public RenderableListBuilder(Func<bool> enabled)
    {
        _enabled = enabled;
        for (int g = 0; g < GroupCount; g++)
            _entries[g] = new int[MaxGroupEntities * 3];
    }

    /// <inheritdoc/>
    public int BuildRenderableList(ReadOnlySpan<int> view, Span<int> answer)
    {
        if (!_enabled() || view.Length < HeaderWords || view[0] != view.Length)
            return -1;
        int leafCount = view[10], renderableCount = view[11], occurrenceCount = view[12], detailCount = view[13];
        if (leafCount < 0 || renderableCount < 0 || occurrenceCount < 0 || detailCount < 0)
            return -1;
        int leafAt = HeaderWords;
        int occurrenceAt = leafAt + LeafWords * leafCount;
        int renderableAt = occurrenceAt + occurrenceCount;
        int detailAt = renderableAt + RenderableWords * renderableCount;
        if ((long)detailAt + (long)DetailWords * detailCount != view.Length)
            return -1;

        var input = new ViewInput(view, leafAt, occurrenceAt, renderableAt, detailAt);
        int frame = view[1];
        int flags = view[2];
        int drawLeaf = view[3];
        bool drawTranslucent = (flags & FlagDrawTranslucent) != 0;

        if (_frame.Length < renderableCount)
        {
            _frame = new int[renderableCount];
            _leaf = new int[renderableCount];
            _frame2 = new int[renderableCount];
        }
        for (int r = 0; r < renderableCount; r++)
        {
            _frame[r] = input.Renderable(r, 3);
            _leaf[r] = input.Renderable(r, 4);
            _frame2[r] = input.Renderable(r, 5);
        }
        Array.Clear(_counts);
        int overflow = 0;

        if ((flags & FlagTranslucentLeafPass) != 0)
            ComputeTranslucentRenderLeaf(input, leafCount, frame);

        // BuildRenderablesList: each leaf collated, then its new translucent
        // entries sorted.
        for (int i = 0; i < leafCount; i++)
        {
            int translucentBefore = _counts[GroupTranslucentEntity];
            CollateRenderablesInLeaf(input, i, frame, flags, drawLeaf, ref overflow);
            int added = _counts[GroupTranslucentEntity] - translucentBefore;
            if (added != 0 && drawTranslucent)
                SortEntities(input, translucentBefore, added, view);
        }

        // The answer: the rows' state, then each group's entries in order.
        int entries = 0;
        for (int g = 0; g < GroupCount; g++)
            entries += _counts[g];
        int total = 2 + StateWords * renderableCount + 1 + EntryWords * entries;
        if (answer.Length < total)
            return -1;
        answer[0] = total;
        answer[1] = overflow;
        int at = 2;
        for (int r = 0; r < renderableCount; r++)
        {
            answer[at++] = _frame[r];
            answer[at++] = _leaf[r];
            answer[at++] = _frame2[r];
        }
        answer[at++] = entries;
        for (int g = 0; g < GroupCount; g++)
        {
            int[] list = _entries[g];
            for (int e = 0; e < _counts[g]; e++)
            {
                answer[at++] = g;
                answer[at++] = list[3 * e];
                answer[at++] = list[3 * e + 1];
                answer[at++] = list[3 * e + 2];
            }
        }
        return total;
    }

    /// <inheritdoc/>
    public int SplitViewModels(ReadOnlySpan<int> groups, Span<int> answer)
    {
        if (!_enabled() || groups.Length < 1 || groups[0] < 0 || groups.Length != groups[0] + 1 || answer.Length < groups[0])
            return -1;
        int count = groups[0];
        // The group after the view model's ComputeFxBlend: opaque when it is
        // the opaque view model group or became an opaque entity, else translucent.
        for (int i = 0; i < count; i++)
        {
            int group = groups[1 + i];
            answer[i] = group == GroupViewModelOpaque || group == GroupOpaqueEntity ? 0 : 1;
        }
        return count;
    }

    // ComputeTranslucentRenderLeaf's second loop: walking the leaves front to
    // back, a translucent renderable met for the first time this frame renders
    // in that leaf; met again under alternate sorting, in the later one.
    private void ComputeTranslucentRenderLeaf(in ViewInput input, int leafCount, int frame)
    {
        for (int i = 0; i < leafCount; i++)
        {
            int leaf = input.Leaf(i, 0);
            int first = input.Leaf(i, 1), count = input.Leaf(i, 2);
            for (int k = 0; k < count; k++)
            {
                int r = input.Occurrence(first + k);
                bool translucent = input.Renderable(r, 1) == GroupTranslucentEntity;
                if (_frame[r] != frame)
                {
                    if (translucent)
                        _leaf[r] = leaf;
                    _frame[r] = frame;
                }
                else if ((input.Renderable(r, 2) & RenderFlagsAlternateSorting) != 0)
                {
                    if (translucent)
                        _leaf[r] = leaf;
                }
            }
        }
    }

    private void CollateRenderablesInLeaf(in ViewInput input, int leafIndex, int frame, int flags, int drawLeaf, ref int overflow)
    {
        bool portalTestEnts = (flags & FlagPortalTestEnts) != 0 && (flags & FlagPortalsOpenAll) == 0;
        bool drawTranslucent = (flags & FlagDrawTranslucent) != 0;
        bool drawStaticProps = (flags & FlagDrawStaticProps) != 0;
        int leaf = input.Leaf(leafIndex, 0);

        // The leaf's markers for the static and opaque entities in it.
        Add(GroupOpaqueStatic, -1, leafIndex, 0, drawLeaf, ref overflow);
        Add(GroupOpaqueEntity, -1, leafIndex, 0, drawLeaf, ref overflow);

        int first = input.Leaf(leafIndex, 1), count = input.Leaf(leafIndex, 2);
        for (int k = 0; k < count; k++)
        {
            int r = input.Occurrence(first + k);
            int group = input.Renderable(r, 1);
            int renderFlags = input.Renderable(r, 2);

            if (!drawStaticProps && (renderFlags & RenderFlagsStaticProp) != 0)
                continue;

            // One entry per renderable per frame however many leaves it is in;
            // a translucent one only in the leaf it renders in.
            if (group != GroupTranslucentEntity)
            {
                if (_frame2[r] == frame)
                    continue;
                _frame2[r] = frame;
            }
            else if (_leaf[r] != leaf)
            {
                continue;
            }

            // An invisible renderable is not drawn (stock's alpha is a byte).
            int alpha = 255;
            if (drawTranslucent)
            {
                alpha = (byte)input.Renderable(r, 7);
                if (alpha == 0)
                    continue;
            }

            int area = input.Renderable(r, 6);
            int answers = input.Renderable(r, 8);
            if (portalTestEnts && area != -1)
            {
                if ((answers & AnswerTouchesArea) == 0)
                    continue;
            }
            else if ((answers & AnswerCulled) != 0)
            {
                continue;
            }

            if ((renderFlags & RenderFlagsStudioModel) != 0 && (answers & AnswerOccluded) != 0)
                continue;

            if (group != GroupTranslucentEntity)
            {
                if (group >= GroupOpaqueStatic && group <= GroupOpaqueEntity)
                {
                    float dx = input.Float(r, 12) - input.Float(r, 9);
                    float dy = input.Float(r, 13) - input.Float(r, 10);
                    float dz = input.Float(r, 14) - input.Float(r, 11);
                    float dimension = Max(Max(MathF.Abs(dx), MathF.Abs(dy)), MathF.Abs(dz));
                    group = BucketedRenderGroup(group, dimension);
                }
                Add(group, r, leafIndex, 0, drawLeaf, ref overflow);
            }
            else
            {
                bool twoPass = (renderFlags & RenderFlagsTwoPass) != 0 && alpha == 255;
                if (drawTranslucent)
                    Add(GroupTranslucentEntity, r, leafIndex, twoPass ? 1 : 0, drawLeaf, ref overflow);
                if (twoPass)
                    Add(GroupOpaqueEntity, r, leafIndex, 1, drawLeaf, ref overflow);
            }
        }

        // The detail objects, which have no render handles.
        if ((flags & FlagDrawDetail) != 0 && input.Leaf(leafIndex, 3) != 0)
        {
            int firstDetail = input.Leaf(leafIndex, 4), detailCount = input.Leaf(leafIndex, 5);
            for (int d = firstDetail; d < firstDetail + detailCount; d++)
            {
                if (input.Detail(d, 0) == 0)
                    continue;
                if (input.Detail(d, 1) != 0)
                {
                    if (drawTranslucent && input.Detail(d, 2) > 0)
                        Add(GroupTranslucentEntity, -2 - d, leafIndex, 0, drawLeaf, ref overflow);
                }
                else
                {
                    Add(GroupOpaqueEntity, -2 - d, leafIndex, 0, drawLeaf, ref overflow);
                }
            }
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

    // SortEntities: each entry's distance along the view's forward from its
    // render bounds' centre, then stock's H-sort, swap for swap.
    private void SortEntities(in ViewInput input, int start, int count, ReadOnlySpan<int> view)
    {
        if (count <= 1)
            return;
        float ox = Bits(view[4]), oy = Bits(view[5]), oz = Bits(view[6]);
        float fx = Bits(view[7]), fy = Bits(view[8]), fz = Bits(view[9]);
        int[] list = _entries[GroupTranslucentEntity];
        for (int i = 0; i < count; i++)
        {
            int source = list[3 * (start + i)];
            float minX, minY, minZ, maxX, maxY, maxZ, x, y, z;
            if (source >= 0)
            {
                minX = input.Float(source, 15); minY = input.Float(source, 16); minZ = input.Float(source, 17);
                maxX = input.Float(source, 18); maxY = input.Float(source, 19); maxZ = input.Float(source, 20);
                x = input.Float(source, 21); y = input.Float(source, 22); z = input.Float(source, 23);
            }
            else
            {
                int d = -2 - source;
                minX = input.DetailFloat(d, 3); minY = input.DetailFloat(d, 4); minZ = input.DetailFloat(d, 5);
                maxX = input.DetailFloat(d, 6); maxY = input.DetailFloat(d, 7); maxZ = input.DetailFloat(d, 8);
                x = input.DetailFloat(d, 9); y = input.DetailFloat(d, 10); z = input.DetailFloat(d, 11);
            }
            // VectorAdd, then VectorMA( origin, 0.5, sum ), then the delta's
            // dot product with forward.
            float cx = minX + maxX, cy = minY + maxY, cz = minZ + maxZ;
            cx = x + 0.5f * cx;
            cy = y + 0.5f * cy;
            cz = z + 0.5f * cz;
            float dx = cx - ox, dy = cy - oy, dz = cz - oz;
            _dists[i] = dx * fx + dy * fy + dz * fz;
        }

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

    private static void Swap(int[] list, int a, int b)
    {
        for (int k = 0; k < 3; k++)
            (list[3 * a + k], list[3 * b + k]) = (list[3 * b + k], list[3 * a + k]);
    }

    private static float Bits(int word) => BitConverter.Int32BitsToSingle(word);

    // The buffer's sections, by row and word.
    private readonly ref struct ViewInput(ReadOnlySpan<int> view, int leafAt, int occurrenceAt, int renderableAt, int detailAt)
    {
        private readonly ReadOnlySpan<int> _view = view;

        public int Leaf(int i, int word) => _view[leafAt + LeafWords * i + word];

        public int Occurrence(int i) => _view[occurrenceAt + i];

        public int Renderable(int r, int word) => _view[renderableAt + RenderableWords * r + word];

        public float Float(int r, int word) => Bits(Renderable(r, word));

        public int Detail(int d, int word) => _view[detailAt + DetailWords * d + word];

        public float DetailFloat(int d, int word) => Bits(Detail(d, word));
    }
}
