using System.Globalization;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// One defect: which rule, where, and what to do about it, as one line an
/// agent can act on (plan_client_ui §5.8.5):
/// <c>scoreboard/12-players@2560x1080 [clipped] TeamTable[team0] › "xXx_Long…" overflows its box by 38 du; use .Elide() or a wider column</c>.
/// </summary>
public readonly record struct UiLintFinding(string Rule, string Where, string Element, string Message)
{
    public override string ToString() => $"{Where} [{Rule}] {Element} {Message}";
}

/// <summary>A world still under the UI: the extreme colours in a screen region (for contrast).</summary>
public interface IUiBackdrop
{
    string Name { get; }

    /// <summary>The darkest and the lightest colour the still shows inside <paramref name="r"/> on a w×h screen.</summary>
    (UiColor Dark, UiColor Light) Range(UiRect r, int w, int h);
}

/// <summary>
/// The design lint (plan_client_ui §5.8.5): reads a laid-out frame (the render
/// list, the element facts, the frame report) and lists what a reviewer would
/// send back. Each rule is a method; each has a fact that plants its defect.
/// </summary>
public sealed class UiLint
{
    private readonly UiTheme _theme;
    private readonly IUiFontMetrics _fonts;
    private readonly IReadOnlyList<IUiBackdrop> _backdrops;

    public UiLint(UiTheme theme, IUiFontMetrics fonts, IReadOnlyList<IUiBackdrop> backdrops)
    {
        _theme = theme;
        _fonts = fonts;
        _backdrops = backdrops;
    }

    /// <summary>Every rule, in the order the guide lists them.</summary>
    public static IReadOnlyList<string> Rules { get; } = ["clipped", "offscreen", "contrast", "alignment", "overlap", "budget", "target", "localise", "raw", "ids"];

    /// <summary>Lints the frame <paramref name="ctx"/> last emitted; <paramref name="where"/> prefixes each line (screen/sample@WxH).</summary>
    public List<UiLintFinding> Check(UiContext ctx, string where)
    {
        var f = new Frame(ctx, where);
        Clipped(f);
        Offscreen(f, ctx.Screen.Safe);
        Contrast(f);
        Alignment(f);
        Overlap(f);
        Budget(f);
        Target(f);
        Localise(f);
        RawUse(f);
        Ids(f);
        return f.Findings;
    }

    /// <summary>What every rule reads, gathered once.</summary>
    private sealed class Frame
    {
        public readonly UiContext Ctx;
        public readonly string Where;
        public readonly List<UiLintFinding> Findings = new();
        public readonly UiElementInfo[] Elements;
        public readonly float Scale;
        public readonly int W, H;

        public Frame(UiContext ctx, string where)
        {
            Ctx = ctx;
            Where = where;
            var list = ctx.List.AsDrawList();
            Elements = list.Elements.ToArray();
            Scale = list.Screen.Scale;
            W = list.Screen.W;
            H = list.Screen.H;
        }

        public string Name(int i)
        {
            var list = Ctx.List.AsDrawList();
            var e = Elements[i];
            return new string(list.Text.Slice(e.NameStart, e.NameLength));
        }

        /// <summary>The element and up to two named ancestors: <c>TeamTable[team0] › row 3 › "Alyx"</c>.</summary>
        public string Path(int i)
        {
            var parts = new List<string>();
            for (int k = i; k >= 0 && parts.Count < 3; k = Elements[k].Parent)
            {
                string n = Name(k);
                if (n.Length > 0 && !n.StartsWith("Box", StringComparison.Ordinal) && !n.StartsWith("Stack", StringComparison.Ordinal)
                    && !n.StartsWith("Grid", StringComparison.Ordinal) && !n.StartsWith("Spacer", StringComparison.Ordinal))
                    parts.Add(Elements[k].Kind == UiElementKind.Text && !n.StartsWith('"') ? $"\"{n}\"" : n);
            }
            if (parts.Count == 0) parts.Add(Elements[i].Kind.ToString());
            parts.Reverse();
            return string.Join(" › ", parts);
        }

        public float Du(float px) => px / Scale;

        public void Add(string rule, int element, string message)
            => Findings.Add(new UiLintFinding(rule, Where, element >= 0 ? Path(element) : "screen", message));
    }

    private static string D(float du) => MathF.Round(du).ToString(CultureInfo.InvariantCulture);

    // ---- rules ----

    /// <summary>
    /// clipped: text that ran past its box without being allowed to elide, or
    /// that an ancestor's clip cuts.
    /// </summary>
    private static void Clipped(Frame f)
    {
        var report = f.Ctx.Report;
        for (int i = 0; i < f.Elements.Length; i++)
        {
            var e = f.Elements[i];
            if (e.Kind != UiElementKind.Text || report.IsHidden(i))
                continue;
            if ((e.Flags & UiElementFlags.Overflowed) != 0)
            {
                f.Add("clipped", i, $"overflows its box by {D(f.Du(report.OverflowOf(i)))} du; use .Elide() or a wider box");
                continue;
            }
            // Cut by a scroll region's edge is scrolling, not a defect; cut by the
            // screen's edge, in a window the player dragged there, neither.
            if (InScroll(f, i))
                continue;
            if (InWindow(f, i) && (e.Rect.X < 0 || e.Rect.Y < 0 || e.Rect.X + e.Rect.W > f.W || e.Rect.Y + e.Rect.H > f.H
                                   || OffScreenAncestor(f, i)))
                continue;
            var c = report.ClipOf(i);
            float over = MathF.Max(MathF.Max(c.X - e.Rect.X, e.Rect.X + e.Rect.W - (c.X + c.W)),
                                   MathF.Max(c.Y - e.Rect.Y, e.Rect.Y + e.Rect.H - (c.Y + c.H)));
            if (over > 0.5f)
                f.Add("clipped", i, $"is cut by an enclosing clip by {D(f.Du(over))} du; make the clipping box bigger or the text smaller");
        }
    }

    private static bool OffScreenAncestor(Frame f, int i)
    {
        for (int k = f.Elements[i].Parent; k >= 0; k = f.Elements[k].Parent)
        {
            var r = f.Elements[k].Rect;
            if (r.X < 0 || r.Y < 0 || r.X + r.W > f.W || r.Y + r.H > f.H)
                return true;
            if (f.Ctx.Report.IsMovable(k))
                break;
        }
        return false;
    }

    private static bool InWindow(Frame f, int i)
    {
        for (int k = i; k >= 0; k = f.Elements[k].Parent)
            if (f.Ctx.Report.IsMovable(k))
                return true;
        return false;
    }

    private static bool InScroll(Frame f, int i)
    {
        for (int k = f.Elements[i].Parent; k >= 0; k = f.Elements[k].Parent)
            if (f.Ctx.Report.IsScroll(k))
                return true;
        return false;
    }

    /// <summary>offscreen: anything drawn past the screen's edge or into its safe insets.</summary>
    private static void Offscreen(Frame f, UiInsets safe)
    {
        float l = safe.Left, t = safe.Top, r = f.W - safe.Right, b = f.H - safe.Bottom;
        for (int i = 0; i < f.Elements.Length; i++)
        {
            var e = f.Elements[i].Rect;
            // What is drawn: the rect inside its clip (a scrolled-away row is not on screen, it is clipped).
            if (e.W <= 0 || e.H <= 0 || f.Ctx.Report.IsBlocker(i))
                continue;
            // A window is where the player put it; its own clamp keeps its title bar reachable.
            if (InWindow(f, i))
                continue;
            if (InScroll(f, i))
            {
                // In a scroll region what counts is what is drawn: the rect inside
                // its clip (a scrolled-away row is clipped, not off screen).
                if (f.Ctx.Report.IsHidden(i))
                    continue;
                var c = f.Ctx.Report.ClipOf(i);
                float x0 = MathF.Max(e.X, c.X), y0 = MathF.Max(e.Y, c.Y);
                e = new UiRect(x0, y0, MathF.Min(e.X + e.W, c.X + c.W) - x0, MathF.Min(e.Y + e.H, c.Y + c.H) - y0);
                if (e.W <= 0 || e.H <= 0)
                    continue;
            }
            float over = MathF.Max(MathF.Max(l - e.X, e.X + e.W - r), MathF.Max(t - e.Y, e.Y + e.H - b));
            if (over > 0.5f)
            {
                bool inScreen = e.X >= 0 && e.Y >= 0 && e.X + e.W <= f.W && e.Y + e.H <= f.H;
                f.Add("offscreen", i, inScreen
                    ? $"enters the safe area by {D(f.Du(over))} du; anchor it inside the safe insets"
                    : $"is {D(f.Du(over))} du off screen; anchor it to the edge it belongs to, or size it in Percent/Fill");
            }
        }
    }

    /// <summary>
    /// contrast: text against what is behind it: every quad drawn before it that
    /// covers it, composited over the darkest and the lightest the world shows
    /// there in each backdrop. Needs 4.5:1, or 3:1 for text of 24 du or more.
    /// </summary>
    private void Contrast(Frame f)
    {
        var list = f.Ctx.List.AsDrawList();
        var worst = new Dictionary<int, (float Ratio, string Backdrop, float Need)>();
        for (int o = 0; o < list.Ops.Length; o++)
        {
            var op = list.Ops[o];
            if (op.Kind != UiPrimitiveKind.GlyphRun || op.Element < 0)
                continue;
            var run = list.GlyphRuns[op.Index];
            var font = list.Fonts[run.Font];
            float width = 0f;
            foreach (char c in list.Text.Slice(run.TextStart, run.TextLength))
                width += _fonts.Advance(font, c);
            var rect = new UiRect(run.X, run.Y, MathF.Max(1f, width), _fonts.Metrics(font).LineHeight);
            float cx = rect.X + rect.W / 2, cy = rect.Y + rect.H / 2;
            float need = f.Ctx.Report.IsLargeText(op.Element) ? 3f : 4.5f;

            foreach (var backdrop in _backdrops)
            {
                var (dark, light) = backdrop.Range(rect, f.W, f.H);
                foreach (var world in new[] { dark, light })
                {
                    var bg = world;
                    for (int q = 0; q < o; q++)
                    {
                        var qop = list.Ops[q];
                        if (qop.Kind != UiPrimitiveKind.Quad)
                            continue;
                        var quad = list.Quads[qop.Index];
                        var r = quad.Rect;
                        if (cx < r.X || cy < r.Y || cx >= r.X + r.W || cy >= r.Y + r.H)
                            continue;
                        var fill = quad.Fade == UiFade.None ? quad.Color : quad.Color with { A = (byte)((quad.Color.A + quad.AlphaEnd) / 2) };
                        float qf = qop.Element >= 0 ? f.Ctx.Report.FadeOf(qop.Element) : 1f;
                        if (qf > 0f && qf < 1f)
                            fill = fill with { A = (byte)MathF.Min(255f, MathF.Round(fill.A / qf)) };
                        if (fill.A > 0)
                            bg = UiContrast.Over(fill, bg);
                    }
                    // Judged at full strength: a fade in progress (.Opacity) is motion, not the design.
                    float fade = f.Ctx.Report.FadeOf(op.Element);
                    var color = fade > 0f && fade < 1f ? run.Color with { A = (byte)MathF.Min(255f, MathF.Round(run.Color.A / fade)) } : run.Color;
                    var ink = color.A == 255 ? color : UiContrast.Over(color, bg);
                    float ratio = UiContrast.Ratio(ink, bg);
                    if (ratio < need && (!worst.TryGetValue(op.Element, out var w) || ratio < w.Ratio))
                        worst[op.Element] = (ratio, backdrop.Name, need);
                }
            }
        }

        foreach (var (element, w) in worst.OrderBy(k => k.Key))
            f.Add("contrast", element, string.Create(CultureInfo.InvariantCulture,
                $"is {w.Ratio:0.0}:1 over the {w.Backdrop} still (needs {w.Need:0.#}:1); put it on a Surface, or use Tone.Text"));
    }

    /// <summary>
    /// The elements placed freely in each anchored box (a Box parent), looking
    /// through full-size layers (sub-panels in a box fill it): what a reviewer
    /// compares by eye, because nothing lines them up but the author.
    /// </summary>
    private static List<List<int>> FreeGroups(Frame f) => Groups(f, freeOnly: true);

    /// <summary>Siblings by parent, looking through full-size layers; with <paramref name="freeOnly"/>, only in anchored boxes.</summary>
    private static List<List<int>> Groups(Frame f, bool freeOnly)
    {
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < f.Elements.Length; i++)
        {
            int p = f.Elements[i].Parent;
            if (p < 0 || (freeOnly && f.Elements[p].Kind != UiElementKind.Box) || f.Ctx.Report.IsHidden(i))
                continue;
            // Climb through layers that fill their parent: their children are the parent's.
            while (f.Elements[p].Parent >= 0 && IsLayer(f, p))
                p = f.Elements[p].Parent;
            // A layer (a free element marked .Layered(), like an accent rail)
            // rides over its siblings by intent; like overlap, alignment must
            // not judge it. A drag handle is an invisible hit area, not
            // something to line up.
            if (IsLayer(f, i) || (f.Elements[i].Flags & UiElementFlags.Overlaps) != 0
                || f.Ctx.Report.IsThumb(i))
                continue;
            if (!groups.TryGetValue(p, out var g))
                groups[p] = g = new List<int>();
            g.Add(i);
        }
        return groups.Values.Where(g => g.Count > 1).ToList();
    }

    private static bool IsLayer(Frame f, int i)
    {
        var e = f.Elements[i];
        int p = e.Parent;
        return p >= 0 && (e.Flags & UiElementFlags.Overlaps) != 0 && e.Rect == f.Elements[p].Rect;
    }

    /// <summary>alignment: free-placed siblings whose edges nearly meet: within 3 du, but not equal.</summary>
    private static void Alignment(Frame f)
    {
        float near = 3f * f.Scale;
        foreach (var g in FreeGroups(f))
        {
            for (int a = 0; a < g.Count; a++)
            {
                for (int b = a + 1; b < g.Count; b++)
                {
                    var ra = f.Elements[g[a]].Rect;
                    var rb = f.Elements[g[b]].Rect;
                    if (ra.W <= 0 || ra.H <= 0 || rb.W <= 0 || rb.H <= 0)
                        continue;   // an empty slot has no edges to line up
                    Span<(string Edge, float A, float B)> edges =
                    [
                        ("left", ra.X, rb.X), ("right", ra.X + ra.W, rb.X + rb.W),
                        ("top", ra.Y, rb.Y), ("bottom", ra.Y + ra.H, rb.Y + rb.H),
                    ];
                    foreach (var (edge, ea, eb) in edges)
                    {
                        float d = MathF.Abs(ea - eb);
                        if (d >= 0.5f && d < near)
                            f.Add("alignment", g[b], $"{edge} edge is {f.Du(d):0.#} du from {f.Path(g[a])}'s; give both the same anchor and inset");
                    }
                }
            }
        }
    }

    /// <summary>overlap: siblings drawn over each other that did not say they may (.Layered()).</summary>
    private static void Overlap(Frame f)
    {
        foreach (var g in Groups(f, freeOnly: false))
        {
            for (int a = 0; a < g.Count; a++)
            {
                var ea = f.Elements[g[a]];
                if ((ea.Flags & UiElementFlags.Overlaps) != 0 || ea.Rect.W <= 0 || ea.Rect.H <= 0)
                    continue;
                for (int b = a + 1; b < g.Count; b++)
                {
                    var eb = f.Elements[g[b]];
                    if ((eb.Flags & UiElementFlags.Overlaps) != 0 || eb.Rect.W <= 0 || eb.Rect.H <= 0)
                        continue;
                    float w = MathF.Min(ea.Rect.X + ea.Rect.W, eb.Rect.X + eb.Rect.W) - MathF.Max(ea.Rect.X, eb.Rect.X);
                    float h = MathF.Min(ea.Rect.Y + ea.Rect.H, eb.Rect.Y + eb.Rect.H) - MathF.Max(ea.Rect.Y, eb.Rect.Y);
                    if (w > 1f && h > 1f)
                        f.Add("overlap", g[b], $"overlaps {f.Path(g[a])} by {f.Du(w):0} x {f.Du(h):0} du; move one, or mark the intended one .Layered()");
                }
            }
        }
    }

    /// <summary>The structural tones: surfaces, lines and the scrim are the page, not its colours.</summary>
    private static bool IsStructural(Tone t) => t is Tone.None or Tone.Surface or Tone.SurfaceRaised or Tone.Line or Tone.Scrim or Tone.Window;

    /// <summary>
    /// budget: at most four type roles and six colour tones (surfaces, lines and
    /// the scrim not counted) on one screen. Past that a screen stops reading
    /// as one design.
    /// </summary>
    private static void Budget(Frame f)
    {
        // Per window: each window is a design of its own (two open at once is
        // two budgets); everything outside windows is the screen's.
        var groups = new SortedDictionary<int, (SortedSet<Text> Roles, SortedSet<Tone> Tones)>();
        var report = f.Ctx.Report;
        for (int i = 0; i < f.Elements.Length; i++)
        {
            if (report.IsHidden(i))
                continue;
            int owner = -1;
            for (int k = i; k >= 0; k = f.Elements[k].Parent)
                if (report.IsMovable(k)) { owner = k; break; }
            if (!groups.TryGetValue(owner, out var g))
                groups[owner] = g = (new SortedSet<Text>(), new SortedSet<Tone>());
            if (f.Elements[i].Kind == UiElementKind.Text)
                g.Roles.Add(report.RoleOf(i));
            if (!IsStructural(report.TextToneOf(i)))
                g.Tones.Add(report.TextToneOf(i));
            if (!IsStructural(report.FillToneOf(i)))
                g.Tones.Add(report.FillToneOf(i));
        }
        foreach (var (owner, g) in groups)
        {
            if (g.Roles.Count > 4)
                f.Add("budget", owner, $"uses {g.Roles.Count} type roles ({string.Join(", ", g.Roles)}); keep to 4");
            if (g.Tones.Count > 6)
                f.Add("budget", owner, $"uses {g.Tones.Count} tones ({string.Join(", ", g.Tones)}); keep to 6");
        }
    }

    /// <summary>target: anything that takes clicks smaller than the theme's minimum (32 du) either way.</summary>
    private void Target(Frame f)
    {
        float min = _theme.MinTargetDu * f.Scale - 1f;   // a pixel for edge snapping
        for (int i = 0; i < f.Elements.Length; i++)
        {
            var e = f.Elements[i];
            // A scrollbar thumb is thin by convention, and the wheel and the keys scroll too.
            if ((e.Flags & UiElementFlags.Interactive) == 0 || f.Ctx.Report.IsHidden(i) || f.Ctx.Report.IsThumb(i))
                continue;
            if (e.Rect.W < min || e.Rect.H < min)
                f.Add("target", i, $"is a {f.Du(e.Rect.W):0} x {f.Du(e.Rect.H):0} du click target; make it at least {_theme.MinTargetDu:0} du each way");
        }
    }

    /// <summary>
    /// localise: user-facing text that is a literal string, or a #Token the
    /// language file does not have, or a #Token stranded inside an
    /// interpolation. Game data goes through ui.Data, numbers through
    /// interpolation; those are not copy.
    /// </summary>
    private static void Localise(Frame f)
    {
        for (int i = 0; i < f.Elements.Length; i++)
        {
            var e = f.Elements[i];
            if ((e.Flags & UiElementFlags.StrandedToken) != 0)
            {
                // The '#' is a compile-time literal inside $"...": it renders
                // verbatim and no language file is ever consulted for it.
                f.Add("localise", i, "interpolates text containing # — a #Token inside $\"…\" never reaches the localiser; give the token its own ui.Text and the value its own ui.Data");
                continue;
            }
            if ((e.Flags & UiElementFlags.Unlocalised) == 0)
                continue;
            string text = f.Name(i).Trim('"');
            f.Add("localise", i, text.StartsWith('#')
                ? $"names {text}, which no language file has; add it to src/client/ui/resource/sourcesharp_ui_english.txt (an #SS_UI_ token) or the mod's resource/*_english.txt"
                : "is a literal string; use a #Token (the core's sourcesharp_ui_english.txt, or the mod's resource/*_english.txt), or ui.Data for game data");
        }
    }

    /// <summary>raw: a colour or length given through Raw.* instead of a token. Listed so a reviewer sees each escape.</summary>
    private static void RawUse(Frame f)
    {
        for (int i = 0; i < f.Elements.Length; i++)
            if ((f.Elements[i].Flags & UiElementFlags.Raw) != 0)
                f.Add("raw", i, "uses Raw.*; use a Tone or Space token, or add one to the theme");
    }

    /// <summary>ids: two keyed elements with one id share hover, focus, scroll and state; key them apart or put each in its own ui.Sub.</summary>
    private static void Ids(Frame f)
    {
        foreach (var c in f.Ctx.Report.Collisions)
            f.Add("ids", -1, $"{c.First} and {c.Second} share id {c.Id:x8}; key them apart, or embed each with its own ui.Sub key");
    }
}
