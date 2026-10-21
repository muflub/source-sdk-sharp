using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// THE UI-FIDELITY FACT (plan_client_ui §8 U2): one render list, drawn by the
/// SVG backend the previews and goldens use and by the game's VguiBackend, must
/// agree element for element: the same kind in the same order, the same
/// bounds to a pixel, the same colour, and for text the same string in the
/// same family, size and weight. Glyph shapes are the one thing this cannot
/// compare; the game's jpeg is for that.
/// </summary>
public class UiFidelityTests
{
    private static readonly UiFontLibrary Fonts = new();

    /// <summary>Every sample at every preview size.</summary>
    [Fact]
    public void Every_sample_draws_the_same_elements_through_vgui_as_in_its_svg()
    {
        var lang = UiSamplesTests.Language();
        var theme = SourceSharpTheme.Create();
        var problems = new List<string>();
        int lists = 0, compared = 0;

        foreach (var s in PreviewRun.AllSamples())
        {
            var sizes = s.Gallery ? [new UiPreviewSize(PreviewRun.GalleryW, PreviewRun.GalleryH, 1f)] : UiSamples.Sizes().ToArray();
            foreach (var size in sizes)
            {
                var ctx = UiSamples.PreviewContext(theme, Fonts, lang);
                UiSamples.Render(ctx, s.Create(), size.Screen, s.Now);
                string? why = Compare(ctx, out int n);
                lists++;
                compared += n;
                if (why is not null)
                    problems.Add($"{s.Group}/{s.Slug}@{size.Name}: {why}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
        Assert.True(lists > 50 && compared > 5000, $"{lists} lists, {compared} elements compared");
    }

    /// <summary>The comparison sees a difference: a ledger entry dropped is caught.</summary>
    [Fact]
    public void A_ledger_missing_one_element_does_not_match()
    {
        var ctx = UiTestKit.Context(UiTestKit.PlainTheme());
        UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Panel().Size(Size.Du(200), Size.Du(100)))
                ui.Text("hello");
        });
        var svg = Elements(UiTestKit.Svg(ctx), new SvgBackendTests.FixedMetrics(), ctx.List.AsDrawList());
        var (backend, surface) = Vgui(new SvgBackendTests.FixedMetrics());
        backend.Draw(ctx.List.AsDrawList());
        var ledger = Ledger(backend).ToList();

        Assert.Null(Diff(svg, ledger, surface, new SvgBackendTests.FixedMetrics()));
        Assert.True(ledger.Count >= 2, $"{ledger.Count} entries");
        ledger.RemoveAt(0);
        Assert.NotNull(Diff(svg, ledger, surface, new SvgBackendTests.FixedMetrics()));
    }

    /// <summary>
    /// A face vgui draws smaller than its em at the tall asked (IBM Plex Mono,
    /// live) is made again at the tall that brings its advances to the TTF's.
    /// </summary>
    [Fact]
    public void A_face_vgui_draws_small_is_made_again_at_a_corrected_tall()
    {
        var surface = new RecordingSurface();
        surface.Shrink["IBM Plex Mono"] = 0.8f;
        var backend = new VguiBackend(surface, Fonts, new UiFontFiles([]));
        var mono = new UiFontSpec("IBM Plex Mono", 14, 400, UiFontFlags.None);
        var sans = new UiFontSpec("IBM Plex Sans", 14, 400, UiFontFlags.None);

        float want = Width(Fonts, mono), got = Width(backend, mono);
        Assert.InRange(got / want, 0.95f, 1.05f);
        Assert.Equal(2, surface.Created.Count(c => c.Value.Family == "IBM Plex Mono"));

        Assert.InRange(Width(backend, sans) / Width(Fonts, sans), 0.95f, 1.05f);
        Assert.Equal(1, surface.Created.Count(c => c.Value.Family == "IBM Plex Sans"));
    }

    private static float Width(IUiFontMetrics m, in UiFontSpec f)
    {
        float w = 0;
        foreach (char c in "The quick brown fox 0123456789")
            w += m.Advance(f, c);
        return w;
    }

    /// <summary>The surface really receives what the ledger says was drawn.</summary>
    [Fact]
    public void Every_ledger_entry_reached_the_surface()
    {
        var ctx = UiTestKit.Context(UiTestKit.PlainTheme());
        UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Panel().Size(Size.Du(200), Size.Du(100)))
                ui.Text("hello");
        });
        var (backend, surface) = Vgui(new SvgBackendTests.FixedMetrics());
        backend.Draw(ctx.List.AsDrawList());

        Assert.True(backend.Ledger.Count > 0);
        Assert.True(surface.Draws >= backend.Ledger.Count, $"{surface.Draws} surface draws for {backend.Ledger.Count} entries");
        Assert.Contains("hello", surface.Texts);
    }

    private static string? Compare(UiContext ctx, out int count)
    {
        var list = ctx.List.AsDrawList();
        var svgBackend = new SvgBackend(Fonts);
        svgBackend.Draw(list);
        var svg = Elements(svgBackend.Svg, Fonts, list);
        var (backend, surface) = Vgui(Fonts);
        backend.Draw(list);
        count = svg.Count;
        return Diff(svg, Ledger(backend).ToList(), surface, Fonts);
    }

    private static (VguiBackend, RecordingSurface) Vgui(IUiFontMetrics metrics)
    {
        var surface = new RecordingSurface();
        return (new VguiBackend(surface, metrics, new UiFontFiles([])), surface);
    }

    private static IEnumerable<Entry> Ledger(VguiBackend backend)
    {
        var l = backend.Ledger;
        for (int i = 0; i < l.Count; i++)
        {
            var e = l[i];
            yield return new Entry(e.Kind, e.X0, e.Y0, e.X1, e.Y1, e.Color, new string(l.TextOf(e)), e.Font);
        }
    }

    private sealed record Entry(UiLedgerKind Kind, float X0, float Y0, float X1, float Y1, UiColor Color, string Text,
                                int Font, string Family = "", int Px = 0, int Weight = 0);

    private static string? Diff(List<Entry> svg, List<Entry> ledger, RecordingSurface surface, IUiFontMetrics metrics)
    {
        if (svg.Count != ledger.Count)
            return $"{svg.Count} svg elements, {ledger.Count} ledger entries; first kinds svg [{Kinds(svg)}] ledger [{Kinds(ledger)}]";

        for (int i = 0; i < svg.Count; i++)
        {
            var a = svg[i];
            var b = ledger[i];
            string at = $"#{i} {a.Kind}";
            if (a.Kind != b.Kind)
                return $"{at}: ledger has {b.Kind}";
            if (a.Kind == UiLedgerKind.ClipPop)
                continue;
            if (!Near(a.X0, b.X0) || !Near(a.Y0, b.Y0) || (a.Kind != UiLedgerKind.Text && (!Near(a.X1, b.X1) || !Near(a.Y1, b.Y1))))
                return $"{at}: svg ({a.X0},{a.Y0})-({a.X1},{a.Y1}), ledger ({b.X0},{b.Y0})-({b.X1},{b.Y1})";
            if (a.Kind != UiLedgerKind.ClipPush && !SameColour(a.Color, b.Color))
                return $"{at}: svg colour {a.Color}, ledger {b.Color}";
            if (a.Kind == UiLedgerKind.Text)
            {
                if (a.Text != b.Text)
                    return $"{at}: svg '{a.Text}', ledger '{b.Text}'";
                // The font as the SURFACE made it, not as the backend keys it: the
                // same family and weight, and glyphs the size the SVG's em draws,
                // judged by the surface's own advances over a sample against the
                // TTF's (vgui's tall is the face's line, not its em: the first live
                // frame drew every text at 1/1.3 of its size, and Plex Mono came back
                // smaller still).
                if (!surface.Created.TryGetValue(b.Font, out var made)
                    || made.Family != a.Family || made.Weight != a.Weight)
                    return $"{at} '{a.Text}': svg {a.Family} {a.Weight}, surface font {b.Font} = {made}";
                // Whole-pixel advances are each up to half a pixel from the em's,
                // so a small size is held to that, a large one to the backend's slack.
                float ttf = SampleWidth(metrics, new UiFontSpec(a.Family, a.Px, a.Weight, UiFontFlags.None));
                float ratio = SampleWidth(surface, b.Font) / ttf;
                if (MathF.Abs(ratio - 1f) > Math.Max(VguiBackend.CalibrationSlack, 0.5f * Sample.Length / ttf) + 0.01f)
                    return $"{at} '{a.Text}': svg {a.Family} {a.Px}px, surface font {b.Font} = {made} draws at {ratio:0.000} of it";
            }
        }
        return null;
    }

    private const string Sample = "The quick brown fox 0123456789";

    private static float SampleWidth(RecordingSurface surface, int font)
    {
        Span<int> advances = stackalloc int[95];
        surface.FontAdvances(font, 32, advances);
        float w = 0;
        foreach (char c in Sample)
            w += advances[c - 32];
        return w;
    }

    private static float SampleWidth(IUiFontMetrics metrics, in UiFontSpec spec)
    {
        float w = 0;
        foreach (char c in Sample)
            w += metrics.Advance(spec, c);
        return w;
    }

    private static string Kinds(List<Entry> l) => string.Join(",", l.Take(12).Select(e => e.Kind));

    // Bounds: the SVG writes floats to two places, the ledger whole pixels
    // rounded from the same float, so they agree to half a pixel and the two
    // places' rounding.
    private static bool Near(float svg, float ledger) => Math.Abs(svg - ledger) <= 0.51f;

    private static bool SameColour(UiColor a, UiColor b)
        => a.R == b.R && a.G == b.G && a.B == b.B && Math.Abs(a.A - b.A) <= 1;

    private static readonly Regex Token = new(
        @"<clipPath id=""c\d+""><rect(?<clip>[^>]*)/></clipPath><g clip-path=""[^""]*""[^>]*>" +
        @"|<linearGradient[^>]*>.*?</linearGradient>" +
        @"|<(?<tag>rect|line|polygon|path)\b(?<attrs>[^>]*)/>" +
        @"|<text\b(?<tattrs>[^>]*)>(?<body>[^<]*)</text>" +
        @"|(?<pop></g>)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>The SVG's drawn elements, as the ledger would describe them.</summary>
    private static List<Entry> Elements(string svg, IUiFontMetrics metrics, scoped in UiDrawList list)
    {
        var fonts = list.Fonts.ToArray();
        var gradients = new Dictionary<string, UiColor>();
        var outList = new List<Entry>();
        foreach (Match m in Token.Matches(svg))
        {
            if (m.Value.StartsWith("<linearGradient", StringComparison.Ordinal))
            {
                var id = Regex.Match(m.Value, @"id=""(g\d+)""").Groups[1].Value;
                var stop = Regex.Match(m.Value, @"stop-color=""(#[0-9a-f]{6})"" stop-opacity=""([\d.]+)""");
                gradients[id] = Colour(stop.Groups[1].Value, stop.Groups[2].Value);
                continue;
            }
            if (m.Groups["clip"].Success)
            {
                var r = Rect(m.Groups["clip"].Value);
                outList.Add(new Entry(UiLedgerKind.ClipPush, r.X, r.Y, r.X + r.W, r.Y + r.H, default, "", 0));
                continue;
            }
            if (m.Groups["pop"].Success)
            {
                outList.Add(new Entry(UiLedgerKind.ClipPop, 0, 0, 0, 0, default, "", 0));
                continue;
            }
            if (m.Groups["tattrs"].Success)
            {
                string a = m.Groups["tattrs"].Value;
                string family = Attr(a, "font-family");
                int px = int.Parse(Attr(a, "font-size"), CultureInfo.InvariantCulture);
                int weight = int.Parse(Attr(a, "font-weight"), CultureInfo.InvariantCulture);
                var spec = fonts.First(f => f.Family == family && f.PxHeight == px && f.Weight == weight);
                float x = F(Attr(a, "x"));
                float y = F(Attr(a, "y")) - metrics.Metrics(spec).Ascent;
                outList.Add(new Entry(UiLedgerKind.Text, x, y, x, y, Fill(a, "fill", "fill-opacity"),
                                      WebUtility.HtmlDecode(m.Groups["body"].Value), 0, family, px, weight));
                continue;
            }

            string attrs = m.Groups["attrs"].Value;
            switch (m.Groups["tag"].Value)
            {
                case "rect":
                {
                    var r = Rect(attrs);
                    string fill = Attr(attrs, "fill");
                    if (fill == "none")
                    {
                        float h = F(Attr(attrs, "stroke-width")) / 2f;
                        outList.Add(new Entry(UiLedgerKind.Border, r.X - h, r.Y - h, r.X + r.W + h, r.Y + r.H + h,
                                              Fill(attrs, "stroke", "stroke-opacity"), "", 0));
                    }
                    else if (fill.StartsWith("url(#", StringComparison.Ordinal))
                    {
                        outList.Add(new Entry(UiLedgerKind.Fade, r.X, r.Y, r.X + r.W, r.Y + r.H,
                                              gradients[fill[5..^1]], "", 0));
                    }
                    else
                    {
                        var kind = attrs.Contains("data-texture=", StringComparison.Ordinal) ? UiLedgerKind.Texture : UiLedgerKind.Fill;
                        outList.Add(new Entry(kind, r.X, r.Y, r.X + r.W, r.Y + r.H, Fill(attrs, "fill", "fill-opacity"), "", 0));
                    }
                    break;
                }
                case "path":
                {
                    // An icon: the art scaled into its quad, whose rect data-rect gives.
                    var r = Attr(attrs, "data-rect").Split(' ').Select(F).ToArray();
                    outList.Add(new Entry(UiLedgerKind.Texture, r[0], r[1], r[0] + r[2], r[1] + r[3], Fill(attrs, "fill", "fill-opacity"), "", 0));
                    break;
                }
                case "line":
                    outList.Add(new Entry(UiLedgerKind.Line, F(Attr(attrs, "x1")), F(Attr(attrs, "y1")),
                                          F(Attr(attrs, "x2")), F(Attr(attrs, "y2")),
                                          Fill(attrs, "stroke", "stroke-opacity"), "", 0));
                    break;
                case "polygon":
                {
                    var pts = Attr(attrs, "points").Split(' ')
                        .Select(p => p.Split(',')).Select(p => (X: F(p[0]), Y: F(p[1]))).ToArray();
                    outList.Add(new Entry(UiLedgerKind.Polygon, pts.Min(p => p.X), pts.Min(p => p.Y),
                                          pts.Max(p => p.X), pts.Max(p => p.Y), Fill(attrs, "fill", "fill-opacity"), "", 0));
                    break;
                }
            }
        }
        return outList;
    }

    private static UiRect Rect(string attrs)
        => new(F(Attr(attrs, "x")), F(Attr(attrs, "y")), F(Attr(attrs, "width")), F(Attr(attrs, "height")));

    private static string Attr(string attrs, string name)
    {
        var m = Regex.Match(attrs, $@"(?:^|\s){Regex.Escape(name)}=""([^""]*)""");
        return m.Success ? m.Groups[1].Value : throw new InvalidOperationException($"no {name} in {attrs}");
    }

    private static UiColor Fill(string attrs, string colour, string opacity)
    {
        var o = Regex.Match(attrs, $@"(?:^|\s){opacity}=""([^""]*)""");
        return Colour(Attr(attrs, colour), o.Success ? o.Groups[1].Value : "1");
    }

    private static UiColor Colour(string hex, string opacity)
        => new(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16),
               (byte)MathF.Round(F(opacity) * 255f));

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>A surface that draws nothing and counts what it was asked; fonts get handles from 1.</summary>
    internal sealed class RecordingSurface : IUiSurface
    {
        public int Draws;
        public List<string> Texts { get; } = [];
        private int _fonts, _textures;

        public void FilledRect(int x0, int y0, int x1, int y1, uint rgba) => Draws++;

        public void FilledRectFade(int x0, int y0, int x1, int y1, uint rgba, int alpha0, int alpha1, bool horizontal) => Draws++;

        public void OutlinedRect(int x0, int y0, int x1, int y1, uint rgba) => Draws++;

        public void Line(int x0, int y0, int x1, int y1, uint rgba) => Draws++;

        public void TexturedRect(int texture, int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1, uint rgba) => Draws++;

        public void Polygon(int texture, ReadOnlySpan<float> xyuv, uint rgba) => Draws++;

        public void Text(int font, int x, int y, ReadOnlySpan<char> text, uint rgba)
        {
            Draws++;
            Texts.Add(new string(text));
        }

        public void MeasureText(int font, ReadOnlySpan<char> text, out int width, out int height)
            => (width, height) = (text.Length * 8, 16);

        public void SetClip(bool enabled, int x0, int y0, int x1, int y1) => Draws++;

        public int TextureCreate() => ++_textures;

        public void TextureUpload(int texture, ReadOnlySpan<byte> rgba, int wide, int tall)
        {
        }

        public bool FontAddFile(string path) => true;

        public Dictionary<int, (string Family, int Tall, int Weight, int Flags)> Created { get; } = [];

        public int FontCreate(string family, int tall, int weight, int vguiFlags)
        {
            Created[++_fonts] = (family, tall, weight, vguiFlags);
            return _fonts;
        }

        public void FontMetrics(int font, out int tall, out int ascent) => (tall, ascent) = (20, 15);

        /// <summary>
        /// Per family, how much smaller than its em vgui draws a face asked for
        /// a tall (1 = as the TTF says). The game drew IBM Plex Mono at 0.76 to
        /// 0.89 of its em at 11 to 17 px (ui/fonts, plan_client_ui U2).
        /// </summary>
        public Dictionary<string, float> Shrink { get; } = [];

        private static readonly UiFontLibrary Ttf = new();

        /// <summary>vgui's advances for a font made at a tall: the TTF's at the em that tall implies.</summary>
        public int FontAdvances(int font, int first, Span<int> advances)
        {
            if (!Created.TryGetValue(font, out var made))
            {
                advances.Fill(8);
                return advances.Length;
            }
            float em = made.Tall / 1.3f * Shrink.GetValueOrDefault(made.Family, 1f);
            var unit = new UiFontSpec(made.Family, 1000, made.Weight, UiFontFlags.None);
            for (int i = 0; i < advances.Length; i++)
                advances[i] = (int)MathF.Round(Ttf.Advance(unit, (char)(first + i)) * em / 1000f);
            return advances.Length;
        }


        public void PlaySound(string path)
        {
        }

        public List<(bool Mouse, bool Keyboard)> MenuInputs { get; } = [];

        public List<int> Cursors { get; } = [];

        public void SetMenuInput(bool mouse, bool keyboard) => MenuInputs.Add((mouse, keyboard));

        public void SetCursor(int shape) => Cursors.Add(shape);
    }
}
