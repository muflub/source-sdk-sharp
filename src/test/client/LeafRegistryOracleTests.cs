using System.Globalization;
using System.Text;

using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.LeafSystem;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed leaf system (<see cref="LeafRegistry"/> and the registry path
/// of <see cref="RenderableListBuilder"/>) against stock's CClientLeafSystem,
/// run offline from the pristine clientleafsystem.cpp by
/// src/test/oracle/native/leafsystem_oracle_test.cpp
/// (<c>Fixtures/leafsystem-stock.tsv</c>, README beside it). L2 of
/// docs/plans/c7_leaf_system.md.
///
/// <para>
/// THE GOLDEN CARRIES ITS OWN STIMULUS: every call the oracle's scenario made
/// is a row before it was made, every thing is described before it is
/// registered, and the engine's answers are the README's rules over the
/// level's boxes. So the replay here builds the same world, makes the same
/// calls on the managed registry through a stand-in for the native side, and
/// writes what stock wrote, row for row: the handles, each renderable's
/// leaves and area after PreRender, the shadow manager's receivers added and
/// cleared, the shadows enumerated, the blends computed and asked, the
/// engine's culling questions, each view's list and each renderable's state
/// after it, the view models' split, the PVS answers, the leaf walks, the
/// detail state and the sub-systems' data deleted.
/// </para>
///
/// <para>
/// The two logs are compared between the scenario's calls (its stimulus rows,
/// which the replay echoes): within one stretch, each kind of row in order.
/// Two orders are not stock's on purpose and are compared per kind instead:
/// the batches ask every candidate's blend before any detail model's (stock
/// interleaved them leaf by leaf), and a full group is reported once per group
/// rather than once per dropped entry.
/// </para>
/// </summary>
public class LeafRegistryOracleTests
{
    private const int Invalid = 0xFFFF;

    // The rows the replay acts on and echoes: the scenario's calls.
    private static readonly HashSet<string> Anchors =
    [
        "LV", "LS", "LA", "WB", "T", "DM", "MV", "RM", "F", "PS", "PF", "SD", "ES", "V", "CT", "VE", "VM",
        "RG", "CG", "AS", "SP", "CV", "DS", "DSS", "DD", "PV", "DST", "RMS", "END", "SL",
    ];

    // Rows only the oracle writes (its own buffers for L1's facts).
    private static readonly HashSet<string> OracleOnly = ["VB", "VMB"];

    private static string[] Golden()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "leafsystem-stock.tsv");
        Assert.True(File.Exists(path), path + " is missing");
        return File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').ToArray();
    }

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static float H(string s) => BitConverter.Int32BitsToSingle(unchecked((int)uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    private static string X(float f) => unchecked((uint)BitConverter.SingleToInt32Bits(f)).ToString("x8", CultureInfo.InvariantCulture);

    private sealed class Box
    {
        public float[] Mins = new float[3];
        public float[] Maxs = new float[3];

        public bool Overlaps(ReadOnlySpan<float> mins, ReadOnlySpan<float> maxs) =>
            mins[0] < Maxs[0] && maxs[0] > Mins[0] && mins[1] < Maxs[1] && maxs[1] > Mins[1] && mins[2] < Maxs[2] && maxs[2] > Mins[2];

        public bool Inside(ReadOnlySpan<float> mins, ReadOnlySpan<float> maxs) =>
            mins[0] >= Mins[0] && maxs[0] <= Maxs[0] && mins[1] >= Mins[1] && maxs[1] <= Maxs[1] && mins[2] >= Mins[2] && maxs[2] <= Maxs[2];
    }

    private sealed class Thing
    {
        public int Id;
        public int ModelType;
        public bool Transparent;
        public bool TwoPass;
        public int Blend;
        public int BlendTo;
        public int GroupOnBlend;
        public int Receive;
        public float[] AbsMins = new float[3];
        public float[] AbsMaxs = new float[3];
        public float[] Mins = new float[3];
        public float[] Maxs = new float[3];
        public float[] Origin = new float[3];
        public int Handle = Invalid;
        public nint Pointer;
    }

    /// <summary>The scenario's world and things, the native side the registry asks, and the forwarder that carries its operations out.</summary>
    private sealed class Replay : ILeafNative, ILeafListSettings, SourceSharp.Client.Render.Shadows.IShadowReceivers,
                                  SourceSharp.Client.Render.Shadows.IShadowLeafEnumerator
    {
        public readonly List<string> Log = [];
        public LeafRegistry Registry;
        public RenderableListBuilder Builder;

        private readonly List<Thing> _things = [];
        private readonly Dictionary<int, Thing> _byId = [];
        private readonly Dictionary<nint, Thing> _byPointer = [];
        private readonly List<Box> _leafBoxes = [];
        private readonly List<int> _leafAreas = [];
        private readonly Dictionary<string, Box> _engineBoxes = [];
        private readonly List<nint> _detailModels = [];
        private readonly Dictionary<long, int> _subDataTags = [];
        private readonly Dictionary<int, int> _shadowHandles = [];
        private long _nextSubData = 0x7000_0000;
        private float _leafWidth;
        private int _frameCount;
        private bool _portalTestEnts = true;
        private bool _portalsOpenAll;
        private int _drawLeaf = -1;

        // The list the forwarder wrote for the last view, and the view models' split.
        private readonly List<(int Group, long Renderable, int Handle, int Leaf, int TwoPass)> _list = [];
        private readonly List<(long Renderable, int Translucent)> _viewModels = [];

        public Replay()
        {
            Registry = new LeafRegistry(this) { Receivers = this };
            Builder = new RenderableListBuilder(Registry, this);
        }

        /// <summary>A hot reload: the registry saved, a new one built, and the save restored into it.</summary>
        public int Reload()
        {
            var writer = new SourceSharp.Client.ReloadWriter();
            Registry.Save(writer);
            byte[] bytes = writer.ToArray();
            Registry = new LeafRegistry(this) { Receivers = this };
            Builder = new RenderableListBuilder(Registry, this);
            var reader = new SourceSharp.Client.ReloadReader(bytes);
            Registry.Restore(reader);
            Assert.True(reader.AtEnd, "the restore left bytes unread");
            return bytes.Length;
        }

        public bool PortalTestEnts => _portalTestEnts;

        public bool PortalsOpenAll => _portalsOpenAll;

        public int DrawLeaf => _drawLeaf;

        private void Emit(params object[] fields) => Log.Add(string.Join('\t', fields.Select(f => Convert.ToString(f, CultureInfo.InvariantCulture))));

        private Thing ThingOf(nint p) => _byPointer.TryGetValue(p, out Thing? t) ? t : throw new InvalidOperationException($"no thing at {p}");

        // ---- the managed shadow manager (C7 cluster 12 S2): the receivers and the enumerator, in-process ----

        public void AddShadowToReceiver(int shadow, nint renderable, int kind) => Emit("SR", shadow, ThingOf(renderable).Id, kind);

        public void RemoveAllShadowsFromReceiver(nint renderable, int kind) => Emit("SX", ThingOf(renderable).Id, kind);

        public void EnumShadow(int userId) => Emit("EN", userId);

        // ---- the forwarder: carries the queued operations out ----

        private void Carry()
        {
            Span<LeafOp> ops = stackalloc LeafOp[16];
            int n;
            while ((n = Registry.TakeOps(ops)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    LeafOp op = ops[i];
                    switch (op.Kind)
                    {
                        case LeafOpKind.SetRenderHandle: ThingOf((nint)op.A).Handle = (int)op.B; break;
                        case LeafOpKind.DeleteSubSystemData: Emit("SDEL", _subDataTags[op.A]); break;
                        case LeafOpKind.ListEntry:
                            _list.Add(((int)op.C, op.A, (int)(op.B & 0xFFFF), (int)((op.B >> 16) & 0xFFFF), (int)((op.B >> 32) & 0xFFFF)));
                            break;
                        case LeafOpKind.ListOverflow:
                            for (int g = 0; g < 32; g++)
                                if ((op.B & (1L << g)) != 0)
                                    Emit("NP", 10, $"Warning: overflowed CClientRenderablesList group {g}");
                            break;
                        case LeafOpKind.ViewModel: _viewModels.Add((op.A, (int)op.B)); break;
                        default: throw new InvalidOperationException($"an operation of kind {op.Kind}");
                    }
                }
            }
        }

        // ---- ILeafNative: the stand-ins' answers (the README's rules) ----

        public int ComputeFxBlends(nint system, ReadOnlySpan<nint> renderables, Span<int> pending)
        {
            foreach (nint p in renderables)
            {
                Thing t = ThingOf(p);
                if (t.BlendTo >= 0)
                    t.Blend = t.BlendTo;
                if (t.GroupOnBlend >= 0 && t.Handle != Invalid)
                    Registry.SetRenderGroup(t.Handle, t.GroupOnBlend);
                Emit("BL", t.Id, t.Blend);
            }
            return 0;
        }

        public void Facts(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> what, Span<int> facts)
        {
            _boundsAsked.Clear();
            for (int i = 0; i < renderables.Length; i++)
            {
                Thing t = ThingOf(renderables[i]);
                if ((what[i] & LeafFacts.WorldBounds) != 0)
                    _boundsAsked.Add(t);
                Span<int> f = facts.Slice(i * LeafFacts.Words, LeafFacts.Words);
                if ((what[i] & LeafFacts.Blend) != 0)
                {
                    Emit("FB", t.Id, t.Blend);
                    f[0] = t.Blend;
                }
                if ((what[i] & LeafFacts.Transparent) != 0)
                    f[1] = t.Transparent ? 1 : 0;
                for (int k = 0; k < 3; k++)
                {
                    if ((what[i] & LeafFacts.WorldBounds) != 0)
                    {
                        f[2 + k] = BitConverter.SingleToInt32Bits(t.AbsMins[k]);
                        f[5 + k] = BitConverter.SingleToInt32Bits(t.AbsMaxs[k]);
                    }
                    if ((what[i] & LeafFacts.RenderBounds) != 0)
                    {
                        f[8 + k] = BitConverter.SingleToInt32Bits(t.Mins[k]);
                        f[11 + k] = BitConverter.SingleToInt32Bits(t.Maxs[k]);
                    }
                    if ((what[i] & LeafFacts.Origin) != 0)
                        f[14 + k] = BitConverter.SingleToInt32Bits(t.Origin[k]);
                }
            }
        }

        public void Receives(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> flags, Span<int> answers)
        {
            for (int i = 0; i < renderables.Length; i++)
                answers[i] = (ThingOf(renderables[i]).Receive & flags[i]) != 0 ? 1 : 0;
        }

        public int EnumerateLeaves(ReadOnlySpan<float> boxes, Span<int> output)
        {
            var words = new List<int>();
            for (int b = 0; b < boxes.Length / 6; b++)
            {
                ReadOnlySpan<float> mins = boxes.Slice(6 * b, 3), maxs = boxes.Slice(6 * b + 3, 3);
                int at = words.Count;
                words.Add(0);
                for (int leaf = 0; leaf < _leafBoxes.Count; leaf++)
                {
                    if (_leafBoxes[leaf].Overlaps(mins, maxs))
                    {
                        words.Add(leaf);
                        words[at]++;
                    }
                }
            }
            if (words.Count > output.Length)
                return -words.Count;
            words.CopyTo(output);
            return words.Count;
        }

        public void LeavesAreas(ReadOnlySpan<int> runs, int count, Span<int> areas)
        {
            int at = 0;
            for (int r = 0; r < count; r++)
            {
                int n = runs[at++];
                int area = n == 0 ? -1 : _leafAreas[runs[at]];
                for (int k = 1; k < n; k++)
                    if (_leafAreas[runs[at + k]] != area)
                        area = -1;
                areas[r] = area;
                at += n;
            }
        }

        // The thing a box the engine is asked about belongs to (stock's stand-in
        // logged the renderable whose bounds were asked last): the box's place
        // among the world bounds the last facts batch asked for, which are the
        // ones the view then asks the engine about, in order. Held to the box.
        private readonly List<Thing> _boundsAsked = [];

        private int IdOfBox(int b, ReadOnlySpan<float> box)
        {
            Assert.True(b < _boundsAsked.Count, $"the engine was asked about box {b} of {_boundsAsked.Count} whose bounds were asked");
            Thing t = _boundsAsked[b];
            Assert.True(t.AbsMins[0] == box[0] && t.AbsMins[1] == box[1] && t.AbsMins[2] == box[2]
                        && t.AbsMaxs[0] == box[3] && t.AbsMaxs[1] == box[4] && t.AbsMaxs[2] == box[5],
                        $"box {b} is not thing {t.Id}'s");
            return t.Id;
        }

        public void BoxQueries(ReadOnlySpan<float> boxes, ReadOnlySpan<int> programs, Span<int> answers)
        {
            for (int b = 0; b < programs.Length; b++)
            {
                ReadOnlySpan<float> box = boxes.Slice(6 * b, 6);
                ReadOnlySpan<float> mins = box[..3], maxs = box[3..];
                int id = IdOfBox(b, box);
                int program = programs[b];
                answers[b] = 0;
                if ((program & LeafQuery.Area) != 0)
                {
                    int area = program >> 8;
                    bool touch = _engineBoxes["area" + area].Overlaps(mins, maxs);
                    Emit("Q", "area", id, area, touch ? 1 : 0);
                    if (!touch)
                    {
                        answers[b] = LeafQuery.Area;
                        continue;
                    }
                }
                if ((program & LeafQuery.Cull) != 0)
                {
                    bool cull = !_engineBoxes["frustum"].Overlaps(mins, maxs);
                    Emit("Q", "cull", id, cull ? 1 : 0);
                    if (cull)
                    {
                        answers[b] = LeafQuery.Cull;
                        continue;
                    }
                }
                if ((program & LeafQuery.Occluded) != 0)
                {
                    bool occluded = _engineBoxes["occluder"].Inside(mins, maxs);
                    Emit("Q", "occ", id, occluded ? 1 : 0);
                    if (occluded)
                        answers[b] = LeafQuery.Occluded;
                }
            }
        }

        public void DetailModels(nint detailSystem, ReadOnlySpan<int> ranges, Span<nint> models)
        {
            Assert.Equal((nint)0xDE7A11, detailSystem);
            int at = 0;
            for (int r = 0; r < ranges.Length; r += 2)
                for (int k = 0; k < ranges[r + 1]; k++)
                {
                    int idx = ranges[r] + k;
                    models[at++] = idx >= 0 && idx < _detailModels.Count ? _detailModels[idx] : 0;
                }
        }

        // ---- the scenario's calls ----

        private static string Leaves(IEnumerable<int> leaves) => string.Join(',', leaves);

        private void EmitLeaves(Thing t)
        {
            Span<int> leaves = stackalloc int[128];
            int n = Registry.GetRenderableLeaves(t.Handle, leaves);
            Emit("IN", t.Id, t.Handle, Registry.Info(t.Handle).Area, Leaves(leaves[..Math.Max(n, 0)].ToArray()));
        }

        private string RRow(Thing t, string how, int group) =>
            string.Join('\t', "R", t.Id, how, group, t.ModelType, t.Transparent ? 1 : 0, t.TwoPass ? 1 : 0, t.Blend,
                        X(t.AbsMins[0]), X(t.AbsMins[1]), X(t.AbsMins[2]), X(t.AbsMaxs[0]), X(t.AbsMaxs[1]), X(t.AbsMaxs[2]), t.Handle);

        // The gather's numbering of a view's renderables (first met, over the
        // leaves in order) and of its detail models (each leaf that draws its
        // detail objects, its range in order), which the golden's O and ST rows use.
        private (List<int> Rows, List<nint> Details) Numbering(int[] leaves, int detailFrame)
        {
            var rows = new List<int>();
            var details = new List<nint>();
            LeafBuckets inLeaf = Registry.RenderablesInLeaf;
            foreach (int leaf in leaves)
            {
                for (int node = inLeaf.FirstInBucket(leaf); node >= 0; node = inLeaf.NextInBucket(node))
                {
                    int h = inLeaf.Element(node);
                    if (!rows.Contains(h))
                        rows.Add(h);
                }
                if (Registry.ShouldDrawDetailObjectsInLeaf(leaf, detailFrame) && Registry.GetDetailObjectsInLeaf(leaf, out int first, out int count))
                    for (int k = 0; k < count; k++)
                        details.Add(first + k < _detailModels.Count ? _detailModels[first + k] : 0);
            }
            return (rows, details);
        }

        public void Run(string[] golden)
        {
            foreach (string line in golden)
            {
                string[] r = line.Split('\t');
                string kind = r[0];
                if (Anchors.Contains(kind))
                    Log.Add(line);
                switch (kind)
                {
                    case "LS":
                        // The oracle drops its things and detail models with the level.
                        Registry.LevelShutdownPostEntity();
                        Carry();
                        _things.Clear();
                        _detailModels.Clear();
                        break;
                    case "LV":
                        _leafBoxes.Clear();
                        _leafAreas.Clear();
                        _leafWidth = H(r[2]);
                        for (int i = 0; i < I(r[1]); i++)
                        {
                            _leafBoxes.Add(new Box { Mins = [i * _leafWidth, -500, -500], Maxs = [(i + 1) * _leafWidth, 500, 500] });
                            _leafAreas.Add(0);
                        }
                        Registry.LevelInitPreEntity(I(r[1]));
                        break;
                    case "LA":
                        for (int i = I(r[1]); i <= I(r[2]); i++)
                            _leafAreas[i] = I(r[3]);
                        break;
                    case "WB":
                        _engineBoxes[r[1]] = new Box { Mins = [H(r[2]), H(r[3]), H(r[4])], Maxs = [H(r[5]), H(r[6]), H(r[7])] };
                        break;
                    case "T":
                        var t = new Thing
                        {
                            Id = I(r[1]), ModelType = I(r[2]), Transparent = r[3] == "1", TwoPass = r[4] == "1", Blend = I(r[5]),
                            BlendTo = I(r[6]), GroupOnBlend = I(r[7]), Receive = I(r[8]),
                            AbsMins = [H(r[9]), H(r[10]), H(r[11])], AbsMaxs = [H(r[12]), H(r[13]), H(r[14])],
                            Mins = [H(r[15]), H(r[16]), H(r[17])], Maxs = [H(r[18]), H(r[19]), H(r[20])],
                            Origin = [H(r[21]), H(r[22]), H(r[23])],
                        };
                        t.Pointer = 0x10000 + t.Id * 16;
                        _things.Add(t);
                        _byId[t.Id] = t;
                        _byPointer[t.Pointer] = t;
                        break;
                    case "R" when r[2] == "add":
                    {
                        Thing a = _byId[I(r[1])];
                        a.Handle = Registry.AddRenderable(a.Pointer, I(r[3]), a.ModelType);
                        Carry();
                        Log.Add(RRow(a, "add", I(r[3])));
                        break;
                    }
                    case "R" when r[2] == "static":
                    {
                        Thing s = _byId[I(r[1])];
                        Log.Add(RRow(s, "static", Registry.Info(s.Handle).Group));
                        break;
                    }
                    case "CH":
                    {
                        Thing s = _byId[I(r[1])];
                        s.Handle = Registry.CreateRenderableHandle(s.Pointer, true, s.ModelType, s.Transparent, s.TwoPass);
                        Emit("CH", s.Id, s.Handle);
                        break;
                    }
                    case "SL":
                    {
                        Thing s = _byId[I(r[1])];
                        int[] leaves = r[2].Length == 0 ? [] : r[2].Split(',').Select(I).ToArray();
                        Registry.AddRenderableToLeaves(s.Handle, leaves);
                        Carry();
                        break;
                    }
                    case "DM":
                    {
                        int id = I(r[2]);
                        _detailModels.Add(id < 0 ? 0 : _byId[id].Pointer);
                        break;
                    }
                    case "MV":
                    {
                        Thing m = _byId[I(r[1])];
                        float x0 = H(r[2]);
                        float wx = m.AbsMaxs[0] - m.AbsMins[0];
                        m.AbsMins[0] = x0;
                        m.AbsMaxs[0] = x0 + wx;
                        m.Origin[0] = x0;
                        Registry.RenderableChanged(m.Handle);
                        break;
                    }
                    case "RM":
                        Registry.RemoveRenderable(_byId[I(r[1])].Handle);
                        Carry();
                        break;
                    case "RH":
                        Emit("RH", I(r[1]), _byId[I(r[1])].Handle);
                        break;
                    case "RMS":
                        Registry.RemoveRenderable(I(r[1]));
                        Carry();
                        break;
                    case "F":
                        _frameCount = I(r[1]);
                        Registry.PreRender();
                        Carry();
                        foreach (Thing th in _things)
                            if (th.Handle != Invalid && Registry.IsValidHandle(th.Handle))
                                EmitLeaves(th);
                        break;
                    case "SH":
                    {
                        int h = Registry.AddShadow(I(r[1]), I(r[3]));
                        _shadowHandles[I(r[2])] = h;
                        Emit("SH", I(r[1]), h, I(r[3]));
                        break;
                    }
                    case "PS":
                    case "PF":
                        Registry.ProjectShadow(_shadowHandles[I(r[1])], r[2].Split(',').Select(I).ToArray());
                        Carry();
                        break;
                    case "SD":
                        Registry.RemoveShadow(_shadowHandles[I(r[2])]);
                        break;
                    case "ES":
                        Registry.EnumerateShadowsInLeaves(r.Length < 2 || r[1].Length == 0 ? [] : r[1].Split(',').Select(I).ToArray(), this);
                        Carry();
                        break;
                    case "V":
                        View(r);
                        break;
                    case "VM":
                        _viewModels.Clear();
                        Builder.CollateViewModelRenderables();
                        Carry();
                        Emit("VMI" + string.Concat(_viewModels.Select(v => "\t" + ThingOf((nint)v.Renderable).Id)));
                        Emit("VML\to" + string.Concat(_viewModels.Where(v => v.Translucent == 0).Select(v => "\t" + ThingOf((nint)v.Renderable).Id)));
                        Emit("VML\tt" + string.Concat(_viewModels.Where(v => v.Translucent != 0).Select(v => "\t" + ThingOf((nint)v.Renderable).Id)));
                        break;
                    case "RG":
                        Registry.SetRenderGroup(_byId[I(r[1])].Handle, I(r[2]));
                        break;
                    case "CG":
                        Registry.ChangeRenderableRenderGroup(_byId[I(r[1])].Handle, I(r[2]));
                        break;
                    case "AS":
                        Registry.EnableAlternateSorting(_byId[I(r[1])].Handle, r[2] == "1");
                        break;
                    case "SP":
                        Registry.DrawStaticProps(r[1] == "1");
                        break;
                    case "CV":
                        switch (r[1])
                        {
                            case "r_PortalTestEnts": _portalTestEnts = I(r[2]) != 0; break;
                            case "r_portalsopenall": _portalsOpenAll = I(r[2]) != 0; break;
                            case "cl_drawleaf": _drawLeaf = I(r[2]); break;
                            default: throw new InvalidOperationException("a ConVar the leaf system has not: " + r[1]);
                        }
                        break;
                    case "DS":
                        Registry.SetDetailObjectsInLeaf(I(r[1]), I(r[2]), I(r[3]));
                        break;
                    case "DSS":
                    {
                        long data = _nextSubData += 16;
                        _subDataTags[data] = I(r[2]);
                        long old = Registry.SetSubSystemDataInLeaf(I(r[1]), 0, data);
                        if (old != 0)
                            Emit("SDEL", _subDataTags[old]);
                        break;
                    }
                    case "DD":
                        Registry.DrawDetailObjectsInLeaf(I(r[1]), I(r[2]), out int dfirst, out int dcount);
                        Emit("DDA", I(r[1]), dfirst, dcount);
                        break;
                    case "PV":
                    {
                        Thing p = _byId[I(r[1])];
                        Span<int> leaves = stackalloc int[128];
                        int n = Registry.GetRenderableLeaves(p.Handle, leaves);
                        bool visible = false;
                        if (n != -1)
                        {
                            visible = leaves[..n].ToArray().Any(l => l < 6);
                            Emit("PVQ", Leaves(leaves[..n].ToArray()), visible ? 1 : 0);
                        }
                        Emit("PVA", p.Id, visible ? 1 : 0);
                        break;
                    }
                    case "GL":
                    {
                        int id = I(r[1]);
                        int h = _byId[id].Handle;
                        var walked = new List<int>();
                        bool more = Registry.GetRenderableLeaf(h, false, -1, out int leaf, out int it);
                        while (more)
                        {
                            walked.Add(leaf);
                            more = Registry.GetRenderableLeaf(h, true, it, out leaf, out it);
                        }
                        Emit("GL", id, Leaves(walked));
                        break;
                    }
                    case "DST":
                    {
                        int frame = I(r[1]);
                        for (int leaf = 0; leaf < Registry.LeafCount; leaf++)
                        {
                            int first = -7, count = -7;
                            if (Registry.GetDetailObjectsInLeaf(leaf, out int f, out int c))
                                (first, count) = (f, c);
                            Emit("GD", leaf, first, count, Registry.GetSubSystemDataInLeaf(leaf, 0) != 0 ? 1 : 0,
                                 Registry.ShouldDrawDetailObjectsInLeaf(leaf, frame) ? 1 : 0);
                        }
                        break;
                    }
                }
            }
        }

        private int[] _viewLeaves = [];
        private (List<int> Rows, List<nint> Details) _numbering;

        private void View(string[] r)
        {
            int frame = I(r[2]);
            int viewId = I(r[3]);
            bool translucentPass = r[5] == "1", drawTranslucent = r[6] == "1", drawDetail = r[7] == "1";
            int detailFrame = I(r[9]);
            _viewLeaves = r[16].Split(',').Select(I).ToArray();
            _numbering = Numbering(_viewLeaves, detailFrame);
            _pendingView = (frame, viewId, translucentPass, drawTranslucent, drawDetail, detailFrame,
                            [H(r[10]), H(r[11]), H(r[12])], [H(r[13]), H(r[14]), H(r[15])]);
            if (!translucentPass)
                BuildView();
        }

        private (int Frame, int ViewId, bool TranslucentPass, bool DrawTranslucent, bool DrawDetail, int DetailFrame, float[] Origin, float[] Forward) _pendingView;

        public void OnCt()
        {
            var v = _pendingView;
            Builder.ComputeTranslucentRenderLeaf(_viewLeaves, v.Frame, v.ViewId, _frameCount);
            BuildView();
        }

        private void BuildView()
        {
            var v = _pendingView;
            _list.Clear();
            var view = new LeafView(_viewLeaves, v.Frame, v.DetailFrame, v.DrawDetail, v.DrawTranslucent,
                                    v.Origin[0], v.Origin[1], v.Origin[2], v.Forward[0], v.Forward[1], v.Forward[2],
                                    0xDE7A11, drawLeafFilter: true);
            Builder.BuildRenderablesList(view);
            Carry();
            var (rows, details) = _numbering;
            var position = new Dictionary<int, int>();
            foreach (var e in _list)
            {
                int pos = position.TryGetValue(e.Group, out int p) ? p : 0;
                position[e.Group] = pos + 1;
                string source;
                int id;
                if (e.Renderable == 0)
                {
                    source = "-\t-1";
                    id = -1;
                }
                else if (e.Handle == 0xFFFE)
                {
                    source = "d\t" + details.IndexOf((nint)e.Renderable);
                    id = ThingOf((nint)e.Renderable).Id;
                }
                else
                {
                    source = "r\t" + rows.IndexOf(e.Handle);
                    id = ThingOf((nint)e.Renderable).Id;
                }
                Emit("O", e.Group, pos, source, id, e.Leaf, e.TwoPass, e.Handle);
            }
            for (int row = 0; row < rows.Count; row++)
            {
                ref LeafRegistry.RenderableInfo info = ref Registry.Info(rows[row]);
                Emit("ST", row, ThingOf((nint)info.Renderable).Id, info.RenderFrame, (int)info.RenderLeaf, info.RenderFrame2);
            }
        }
    }

    // The two logs, cut at the scenario's calls, each stretch's rows put in a
    // canonical order: by kind (the blends' candidates before detail models),
    // a full group's warning once.
    private static List<string> Canonical(IEnumerable<string> lines)
    {
        var output = new List<string>();
        var stretch = new List<string>();
        void Flush()
        {
            var seenNp = new HashSet<string>();
            var kept = stretch.Where(l => !l.StartsWith("NP\t", StringComparison.Ordinal) || seenNp.Add(l)).ToList();
            // The warnings by group, in whichever order the groups filled.
            output.AddRange(kept.Select((l, i) => (l, i))
                                .OrderBy(x => Key(x.l))
                                .ThenBy(x => x.l.StartsWith("NP\t", StringComparison.Ordinal) ? x.l : "")
                                .ThenBy(x => x.i)
                                .Select(x => x.l));
            stretch.Clear();
        }
        foreach (string line in lines)
        {
            string kind = line.Split('\t')[0];
            if (OracleOnly.Contains(kind))
                continue;
            if (Anchors.Contains(kind))
            {
                Flush();
                output.Add(line);
            }
            else
            {
                stretch.Add(line);
            }
        }
        Flush();
        return output;
    }

    private static string Key(string line)
    {
        string[] f = line.Split('\t');
        // A detail model's blend (ids from 1001) after the candidates'.
        return f[0] == "FB" && I(f[1]) >= 1000 ? "FB~" : f[0];
    }

    private static List<string> Replayed(out List<string> golden)
    {
        string[] lines = Golden();
        var replay = new Replay();
        RunWithCt(replay, lines);
        golden = Canonical(lines);
        return Canonical(replay.Log);
    }

    private static void RunWithCt(Replay replay, string[] lines, Func<string, bool>? reloadAfter = null)
    {
        // A view with a translucent-leaf pass waits for its CT row.
        foreach (string line in lines)
        {
            replay.Run([line]);
            if (line.StartsWith("CT\t", StringComparison.Ordinal))
                replay.OnCt();
            if (reloadAfter != null && reloadAfter(line))
                replay.Reload();
        }
    }

    private static void AssertSame(List<string> golden, List<string> replay, Func<string, bool> which, int atLeast, string what)
    {
        var g = golden.Where(which).ToList();
        var r = replay.Where(which).ToList();
        Assert.True(g.Count >= atLeast, $"the golden holds only {g.Count} {what} rows");
        for (int i = 0; i < Math.Max(g.Count, r.Count); i++)
        {
            string a = i < g.Count ? g[i] : "<end>", b = i < r.Count ? r[i] : "<end>";
            if (a != b)
            {
                var context = new StringBuilder();
                for (int k = Math.Max(0, i - 4); k < Math.Min(g.Count, i + 3); k++)
                    context.Append("\n  golden ").Append(g[k][..Math.Min(g[k].Length, 160)]);
                Assert.Fail($"{what} row {i}: stock wrote\n  {a[..Math.Min(a.Length, 200)]}\nand the managed registry\n  {b[..Math.Min(b.Length, 200)]}{context}");
            }
        }
    }

    private static bool Is(string line, params string[] kinds) => kinds.Contains(line.Split('\t')[0]);

    [Fact]
    public void TheWholeRunIsStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, _ => true, 10000, "any");
    }

    // A hot reload between any two of the scenario's frames, and before the
    // registry's own (frame 4), changes nothing stock would see.
    [Fact]
    public void TheRegistrySurvivesAHotReload()
    {
        string[] lines = Golden();
        var replay = new Replay();
        int reloads = 0;
        RunWithCt(replay, lines, line =>
        {
            bool at = line.StartsWith("VE\t", StringComparison.Ordinal) || line.StartsWith("RG\t", StringComparison.Ordinal)
                      || line.StartsWith("SL\t", StringComparison.Ordinal);
            reloads += at ? 1 : 0;
            return at;
        });
        Assert.True(reloads >= 20, $"only {reloads} reloads");
        List<string> golden = Canonical(lines);
        AssertSame(golden, Canonical(replay.Log), _ => true, 10000, "any");
    }

    [Fact]
    public void HandlesAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => Is(l, "R", "CH", "RH", "SH"), 50, "handle");
    }

    [Fact]
    public void EachRenderablesLeavesAndAreaAfterPreRenderAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => Is(l, "IN"), 150, "IN");
    }

    [Fact]
    public void TheShadowManagersReceiversAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => Is(l, "SR", "SX"), 40, "receiver");
        // The quirk the scenario was built for: of the two static props added
        // under shadow 102, only the first takes it.
        Assert.Contains("SR\t102\t48\t1", replay);
        Assert.DoesNotContain("SR\t102\t49\t1", replay);
    }

    [Fact]
    public void TheShadowsEnumeratedAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => Is(l, "EN"), 6, "EN");
    }

    [Fact]
    public void TheBlendsAndTheEnginesQuestionsAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => Is(l, "BL"), 150, "BL");
        AssertSame(golden, replay, l => Is(l, "FB"), 300, "FB");
        AssertSame(golden, replay, l => Is(l, "Q"), 700, "Q");
    }

    [Fact]
    public void EveryViewsListAndStateAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => Is(l, "O"), 8000, "O");
        AssertSame(golden, replay, l => Is(l, "ST"), 400, "ST");
        AssertSame(golden, replay, l => Is(l, "NP"), 2, "NP");
    }

    [Fact]
    public void TheViewModelsSplitIsStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => Is(l, "VMI", "VML"), 12, "view model");
    }

    [Fact]
    public void ThePvsTheLeafWalkAndTheDetailStateAreStocks()
    {
        List<string> replay = Replayed(out List<string> golden);
        AssertSame(golden, replay, l => Is(l, "PVQ", "PVA"), 9, "PVS");
        AssertSame(golden, replay, l => Is(l, "GL"), 3, "GL");
        AssertSame(golden, replay, l => Is(l, "GD", "DDA"), 39, "detail");
        AssertSame(golden, replay, l => Is(l, "SDEL"), 3, "SDEL");
    }
}
