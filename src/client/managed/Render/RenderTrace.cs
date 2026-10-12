using System.Globalization;
using System.Runtime.InteropServices;

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// A recorded frame (shell_render_trace_client.cpp's text): its calls in order,
/// and the inputs RenderView had (the <c>inbytes</c> line), read back.
///
/// <para>
/// WHAT A CALL IS. Every line but four kinds: <c>q</c> lines are queries whose
/// answer feeds a decision (the plan asks its inputs up front, stock mid-frame);
/// <c>~</c> lines are what the shadow manager does inside its own frame work
/// (its render-to-texture refresh redraws only the shadows that went dirty, so
/// it differs frame to frame); <c>frame</c>, <c>plan</c>, <c>in</c> and
/// <c>inbytes</c> are the recorder's and the executor's notes, as are
/// <c>scene</c> and <c>scenebytes</c>, each world view's lists (C7 step 2).
/// </para>
/// </summary>
public sealed class RenderTrace
{
    private RenderTrace(List<string> calls, byte[]? inputs, int frameCount, string? plan, List<byte[]> scenes, List<(int Scene, string Text)> queries,
        Dictionary<string, string> environment, Dictionary<string, string> conVars)
    {
        Environment = environment;
        ConVars = conVars;
        Calls = calls;
        InputBytes = inputs;
        FrameCount = frameCount;
        PlanNote = plan;
        SceneBytes = scenes;
        Queries = queries;
    }

    /// <summary>
    /// The engine's answers noted at the frame's start (<c>env</c> lines: realtime,
    /// HDR type, DX level, the tonemap scale, the viewport, the map's tonemap
    /// overrides, ...), by name. Empty for a recording older than C7 step 3.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>
    /// Every convar's value at the frame's start (<c>cvar</c> lines), by name: a
    /// frozen recording answers for an input a later port reads. Empty for a
    /// recording older than C7 step 3.
    /// </summary>
    public IReadOnlyDictionary<string, string> ConVars { get; }

    /// <summary>Each world view's scene as the recorder noted it (<c>scenebytes</c>), in the order the views built their lists.</summary>
    public IReadOnlyList<byte[]> SceneBytes { get; }

    /// <summary>
    /// The <c>q</c> lines, in order, without their <c>q</c> head, each with how
    /// many scenes were noted before it (a view's queries follow its scene).
    /// </summary>
    public IReadOnlyList<(int Scene, string Text)> Queries { get; }

    /// <summary>
    /// Each noted scene with the translucent-surface answers stock got for its
    /// leaves: LeafContainsTranslucentSurfaces asked with
    /// <paramref name="flagsOf"/>(scene index) after the scene and before the next.
    /// Null when a scene is unreadable or one of its leaves went unasked.
    /// </summary>
    public List<ViewSceneData>? ScenesWithAnswers(Func<int, int> flagsOf, out string? why)
    {
        why = null;
        var scenes = new List<ViewSceneData>();
        for (int k = 0; k < SceneBytes.Count; k++)
        {
            ViewSceneData? scene = ViewSceneData.FromBytes(SceneBytes[k]);
            if (scene == null)
            {
                why = $"scene {k} is not this build's layout (re-record it)";
                return null;
            }
            int flags = flagsOf(k);
            var answers = new Dictionary<int, bool>();
            foreach ((int segment, string text) in Queries)
            {
                if (segment != k + 1 || !text.StartsWith("render.LeafContainsTranslucentSurfaces\t", StringComparison.Ordinal))
                    continue;
                int leaf = Field(text, "leaf=");
                int asked = Convert.ToInt32(FieldText(text, "flags=0x"), 16);
                if (asked == flags)
                    answers.TryAdd(leaf, text.EndsWith("=> 1", StringComparison.Ordinal));
            }
            ViewScene header = scene.Header;
            header.SetTranslucentFlags(0, flags);
            var leaves = scene.Leaves.ToArray();
            for (int i = 0; i < leaves.Length; i++)
            {
                if (!answers.TryGetValue(leaves[i].ActualLeaf, out bool answer))
                {
                    why = $"scene {k}: stock never asked leaf {leaves[i].ActualLeaf} with flags 0x{flags:x}";
                    return null;
                }
                leaves[i].SetTranslucent(0, answer);
            }
            scenes.Add(new ViewSceneData(header, leaves, scene.Entries));
        }
        return scenes;
    }

    private static string FieldText(string text, string name)
    {
        int at = text.IndexOf(name, StringComparison.Ordinal);
        if (at < 0)
            return "-1";
        at += name.Length;
        int end = text.IndexOf(' ', at);
        return end < 0 ? text[at..] : text[at..end];
    }

    private static int Field(string text, string name) => int.Parse(FieldText(text, name), CultureInfo.InvariantCulture);

    /// <summary>The calls, in order.</summary>
    public IReadOnlyList<string> Calls { get; }

    /// <summary>The RenderInputs bytes the executor noted, or null when the frame noted none.</summary>
    public byte[]? InputBytes { get; }

    public int FrameCount { get; }

    /// <summary>The executor's <c>plan</c> note (<c>ops=N</c> or <c>declined</c>), or null on a stock frame.</summary>
    public string? PlanNote { get; }

    public static RenderTrace Parse(string text)
    {
        var calls = new List<string>();
        byte[]? inputs = null;
        int frame = -1;
        string? plan = null;
        var scenes = new List<byte[]>();
        var queries = new List<(int, string)>();
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        var conVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            int tab = line.IndexOf('\t');
            string head = tab < 0 ? line : line[..tab];
            string rest = tab < 0 ? string.Empty : line[(tab + 1)..];
            switch (head)
            {
                case "q":
                    queries.Add((scenes.Count, rest));
                    continue;
                case "~":
                case "in":
                case "scene":
                    continue;
                case "env":
                case "cvar":
                {
                    int sep = rest.IndexOf('\t');
                    string key = sep < 0 ? rest : rest[..sep], value = sep < 0 ? string.Empty : rest[(sep + 1)..];
                    (head == "env" ? environment : conVars)[key] = value;
                    continue;
                }
                case "scenebytes":
                    scenes.Add(Convert.FromHexString(rest));
                    continue;
                case "inbytes":
                    inputs ??= Convert.FromHexString(rest);
                    continue;
                case "plan":
                    plan = rest;
                    continue;
                case "frame":
                    foreach (string field in rest.Split(' '))
                        if (field.StartsWith("framecount=", StringComparison.Ordinal))
                            frame = int.Parse(field["framecount=".Length..], CultureInfo.InvariantCulture);
                    continue;
                default:
                    calls.Add(line);
                    continue;
            }
        }
        return new RenderTrace(calls, inputs, frame, plan, scenes, queries, environment, conVars);
    }

    /// <summary>
    /// The noted inputs, read back; null when there are none or they are larger
    /// than this build's. Fewer bytes are an older layout of the same struct:
    /// RenderInputs only grows at its end (the stock recordings are frozen, C7
    /// step 3), so the fields an old frame lacks read as zero, and a fact that
    /// needs one reads its value from the frame's <see cref="ConVars"/> or
    /// <see cref="Environment"/> instead.
    /// </summary>
    public RenderInputs? Inputs()
    {
        int size = Marshal.SizeOf<RenderInputs>();
        if (InputBytes == null || InputBytes.Length == 0 || InputBytes.Length > size)
            return null;
        if (InputBytes.Length == size)
            return MemoryMarshal.Read<RenderInputs>(InputBytes);
        var padded = new byte[size];
        InputBytes.CopyTo(padded, 0);
        return MemoryMarshal.Read<RenderInputs>(padded);
    }

    /// <summary>
    /// Where two frames' scenes -- the lists their world views drew from -- first
    /// differ, or null when every view's lists held the same renderables with the
    /// same flags, leaves, blends and colours (C7 step 2). Two adjacent frames
    /// whose scenes differ were asked different questions: a call difference
    /// between them says nothing about the plan.
    /// </summary>
    public static string? SceneDifference(RenderTrace a, RenderTrace b)
    {
        if (a.SceneBytes.Count != b.SceneBytes.Count)
            return $"{a.SceneBytes.Count} scenes against {b.SceneBytes.Count}";
        for (int k = 0; k < a.SceneBytes.Count; k++)
        {
            if (a.SceneBytes[k].AsSpan().SequenceEqual(b.SceneBytes[k]))
                continue;
            ViewSceneData? x = ViewSceneData.FromBytes(a.SceneBytes[k]), y = ViewSceneData.FromBytes(b.SceneBytes[k]);
            if (x == null || y == null)
                return $"scene {k} is unreadable";
            if (x.Entries.Length != y.Entries.Length)
                return $"scene {k}: {x.Entries.Length} entries against {y.Entries.Length}";
            if (x.Leaves.Length != y.Leaves.Length)
                return $"scene {k}: {x.Leaves.Length} leaves against {y.Leaves.Length}";
            for (int e = 0; e < x.Entries.Length; e++)
            {
                ViewSceneEntry p = x.Entries[e], q = y.Entries[e];
                if (p.Group != q.Group || p.Flags != q.Flags || p.Leaf != q.Leaf || p.EntIndex != q.EntIndex || p.FxBlend != q.FxBlend)
                    return $"scene {k} entry {e}: group {p.Group} ent {p.EntIndex} blend {p.FxBlend} against group {q.Group} ent {q.EntIndex} blend {q.FxBlend}";
            }
            return $"scene {k}: the same renderables, a leaf or colour differs";
        }
        return null;
    }

    /// <summary>
    /// Entity indices numbered by first appearance (the scenes' entries in order,
    /// then the calls' <c>ent=</c>), the world (0) and none (negative) kept: two
    /// sessions of one map number its entities apart (a frozen frame against a
    /// live one, C7 step 3), and this is the identity both share.
    /// </summary>
    public Func<int, int> EntityOrdinals()
    {
        var map = new Dictionary<int, int>();
        int Id(int e)
        {
            if (e <= 0)
                return e;
            if (!map.TryGetValue(e, out int v))
                map[e] = v = map.Count + 1;
            return v;
        }
        foreach (byte[] bytes in SceneBytes)
            if (ViewSceneData.FromBytes(bytes) is { } scene)
                foreach (ViewSceneEntry entry in scene.Entries)
                    Id(entry.EntIndex);
        var token = EntToken;
        foreach (string call in Calls)
            foreach (System.Text.RegularExpressions.Match m in token.Matches(call))
                Id(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        return e => e <= 0 ? e : map.TryGetValue(e, out int v) ? v : -1000000 - e;
    }

    private static System.Text.RegularExpressions.Regex EntToken => new(@"\bent=(-?\d+)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The calls with each <c>ent=</c> numbered by <paramref name="ordinal"/>.</summary>
    public IReadOnlyList<string> CallsWithEntities(Func<int, int> ordinal)
    {
        var token = EntToken;
        return [.. Calls.Select(c => token.Replace(c, m => "ent=" + ordinal(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                                                                           .ToString(System.Globalization.CultureInfo.InvariantCulture)))];
    }

    /// <summary>
    /// <see cref="SceneDifference(RenderTrace, RenderTrace)"/> with each frame's
    /// entities numbered by their own ordinals: two sessions' frames.
    /// </summary>
    public static string? SceneDifferenceAcrossSessions(RenderTrace a, RenderTrace b)
    {
        Func<int, int> ma = a.EntityOrdinals(), mb = b.EntityOrdinals();
        if (a.SceneBytes.Count != b.SceneBytes.Count)
            return $"{a.SceneBytes.Count} scenes against {b.SceneBytes.Count}";
        for (int k = 0; k < a.SceneBytes.Count; k++)
        {
            ViewSceneData? x = ViewSceneData.FromBytes(a.SceneBytes[k]), y = ViewSceneData.FromBytes(b.SceneBytes[k]);
            if (x == null || y == null)
                return $"scene {k} is unreadable";
            if (x.Entries.Length != y.Entries.Length)
                return $"scene {k}: {x.Entries.Length} entries against {y.Entries.Length}";
            if (x.Leaves.Length != y.Leaves.Length)
                return $"scene {k}: {x.Leaves.Length} leaves against {y.Leaves.Length}";
            for (int e = 0; e < x.Entries.Length; e++)
            {
                ViewSceneEntry p = x.Entries[e], q = y.Entries[e];
                if (p.Group != q.Group || p.Flags != q.Flags || p.Leaf != q.Leaf || ma(p.EntIndex) != mb(q.EntIndex) || p.FxBlend != q.FxBlend)
                    return $"scene {k} entry {e}: group {p.Group} ent {p.EntIndex} blend {p.FxBlend} against group {q.Group} ent {q.EntIndex} blend {q.FxBlend}";
            }
        }
        return null;
    }

    /// <summary>
    /// <see cref="Compare(RenderTrace, RenderTrace)"/> with each frame's
    /// entities numbered by their own ordinals: a frozen frame against a live one.
    /// </summary>
    public static RenderTraceDifference? CompareAcrossSessions(RenderTrace a, RenderTrace b)
        => Compare(a.WithFrameStartCopyNamed(a.CallsWithEntities(a.EntityOrdinals())),
                   b.WithFrameStartCopyNamed(b.CallsWithEntities(b.EntityOrdinals())));

    /// <summary>The <c>env</c> note naming the frame-buffer copy texture (index 0) in force as the frame began.</summary>
    public const string FrameBufferCopyNote = "fbcopy0";

    /// <summary>What a restore of <see cref="FrameStartCopyName"/> reads as across sessions.</summary>
    public const string FrameStartCopy = "<frame start>";

    private const string CopyCall = "ctx.SetFrameBufferCopyTexture\ttexture=";
    private const string CopyIndexZero = " index=0";

    /// <summary>
    /// The frame-buffer copy texture (index 0) in force as the frame began, or
    /// null when the frame cannot say. It is session history, not the frame's:
    /// nothing resets it between frames, so a refracting surface drawn earlier
    /// in the session (ss_sandbox's) leaves _rt_fullframefb behind for every
    /// later frame. A recording older than the <see cref="FrameBufferCopyNote"/>
    /// note answers "-" when it sets index 0 to none (only a restore of an empty
    /// start does: every other set names a texture), else null.
    /// </summary>
    public string? FrameStartCopyName =>
        Environment.TryGetValue(FrameBufferCopyNote, out string? name) ? name
        : Calls.Contains(CopyCall + "-" + CopyIndexZero) ? "-" : null;

    /// <summary>
    /// The calls with each restore of the frame's starting copy texture named
    /// <see cref="FrameStartCopy"/>: a set of index 0 back to the texture the
    /// frame began with, while another is in force (stock's and the plan's
    /// push/pop around the 3D skybox and water). A set to that texture while it
    /// is already in force is a copy of its own (post-processing's) and keeps
    /// its name, so two sessions starting apart still part on it.
    /// </summary>
    private IReadOnlyList<string> WithFrameStartCopyNamed(IReadOnlyList<string> calls)
    {
        string? start = FrameStartCopyName, current = start;
        if (start == null)
            return calls;
        var named = new List<string>(calls.Count);
        foreach (string call in calls)
        {
            if (call.StartsWith(CopyCall, StringComparison.Ordinal) && call.EndsWith(CopyIndexZero, StringComparison.Ordinal))
            {
                string texture = call[CopyCall.Length..^CopyIndexZero.Length];
                bool restore = texture == start && current != start;
                current = texture;
                if (restore)
                {
                    named.Add(CopyCall + FrameStartCopy + CopyIndexZero);
                    continue;
                }
            }
            named.Add(call);
        }
        return named;
    }

    /// <summary>
    /// The first place two frames' calls part, or null when they are the same
    /// calls in the same order.
    /// </summary>
    public static RenderTraceDifference? Compare(RenderTrace a, RenderTrace b) => Compare(a.Calls, b.Calls);

    private static RenderTraceDifference? Compare(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return new RenderTraceDifference(i, a[i], b[i], a.Count, b.Count);
        if (a.Count != b.Count)
            return new RenderTraceDifference(n, n < a.Count ? a[n] : null, n < b.Count ? b[n] : null, a.Count, b.Count);
        return null;
    }
}

/// <summary>Where two recorded frames part: the call index and each side's call there (null past its end).</summary>
public sealed record RenderTraceDifference(int Index, string? Left, string? Right, int LeftCount, int RightCount);
