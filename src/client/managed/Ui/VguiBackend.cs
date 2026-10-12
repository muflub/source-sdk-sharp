using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>The TTF files the UI's font families come from, registered with the surface before the first font.</summary>
public sealed record UiFontFiles(IReadOnlyList<string> Paths);

/// <summary>
/// THE GAME'S UI BACKEND (plan_client_ui §8 U2): a render list, drawn through
/// vgui's surface (<see cref="IUiSurface"/>, IShimUiClient), one ledger entry
/// per element the SVG backend writes for the same list.
///
/// <para>
/// What the surface cannot do directly is done here, so native only carries
/// it out: a rounded rect is a polygon of its corners; a border wider than a
/// pixel is four rects (square) or one quad per edge segment (rounded); a line
/// wider than a pixel is a quad; a drop shadow is a second run in black, as
/// the SVG draws it, not a vgui font flag; clips nest by intersection.
/// Untextured polygons use a 1x1 white texture this backend uploads once.
/// </para>
///
/// <para>
/// Fonts: one vgui font per (family, px, weight, flags), created on first use
/// and kept; their metrics and the advances of printable ASCII are read once
/// per font, so layout measures without crossing. A character outside that
/// table, or a font the surface refused, is measured by
/// <paramref name="fallback"/> (the TTF metrics the previews use).
/// </para>
/// </summary>
public sealed class VguiBackend(IUiSurface surface, IUiFontMetrics fallback, UiFontFiles files) : IUiBackend
{
    private const int FirstAdvance = 32, AdvanceCount = 95;   // ' ' .. '~'

    // vgui's FONTFLAG_* (vgui/ISurface.h).
    private const int FontFlagAntialias = 0x010, FontFlagAdditive = 0x100, FontFlagOutline = 0x200;

    private sealed class Font
    {
        public int Handle;
        public bool Measured;
        public int Tall, Ascent;
        public readonly int[] Advances = new int[AdvanceCount];
    }

    private readonly Dictionary<UiFontSpec, Font> _fonts = [];
    private readonly float[] _vertices = new float[256 * 4];
    private readonly UiRect[] _clips = new UiRect[32];
    private int _clipDepth;
    private int _white;
    private bool _filesAdded;

    /// <summary>What the last <see cref="Draw"/> drew.</summary>
    public UiDrawLedger Ledger { get; set; } = new();

    /// <summary>The vgui handle behind a font (0 when the surface refused it); creates it on first use.</summary>
    public int FontHandle(in UiFontSpec spec) => Resolve(spec).Handle;

    /// <summary>The fonts made so far, for /ui/fonts.</summary>
    public IEnumerable<UiFontSpec> Fonts => _fonts.Keys;

    /// <summary>The menu root's cursor (plan_client_ui U3): a window's move and resize handles.</summary>
    public void SetCursor(UiCursor cursor) => surface.SetCursor((int)cursor);

    /// <summary>A video-mode change: re-read every font's metrics before the next layout.</summary>
    public void ScreenSizeChanged()
    {
        foreach (var f in _fonts.Values)
            f.Measured = false;
        // The C0 probe: a texture uploaded by DrawSetTextureRGBA does not survive
        // mat_setvideomode, so the icon atlas goes up again on its next use.
        _atlasUploaded = false;
    }

    /// <summary>The icon atlas, set by the UI that draws through this backend (its toolkit's icon set).</summary>
    public IUiIconAtlas? Icons { get; set; }

    private int _atlas;
    private bool _atlasUploaded;

    /// <summary>
    /// The icon set's atlas as one surface texture (white, the art's alpha):
    /// what a render list's icon quads (negative texture ids) draw from, tinted
    /// by the quad. Uploaded on first use and again after a video-mode change.
    /// </summary>
    private int IconAtlas()
    {
        if (_atlasUploaded || Icons is not { } icons)
            return _atlas;
        if (_atlas == 0)
            _atlas = surface.TextureCreate();
        var a = icons.Alpha;
        var rgba = new byte[a.Length * 4];
        for (int i = 0; i < a.Length; i++)
        {
            rgba[4 * i] = rgba[4 * i + 1] = rgba[4 * i + 2] = 255;
            rgba[4 * i + 3] = a[i];
        }
        surface.TextureUpload(_atlas, rgba, icons.AtlasWidth, icons.AtlasHeight);
        _atlasUploaded = true;
        return _atlas;
    }

    public UiFontMetrics Metrics(in UiFontSpec font)
    {
        var f = Measure(font);
        if (f.Handle == 0 || f.Tall <= 0)
            return fallback.Metrics(font);
        return new UiFontMetrics(f.Ascent, f.Tall - f.Ascent, f.Tall);
    }

    public float Advance(in UiFontSpec font, char c)
    {
        var f = Measure(font);
        int i = c - FirstAdvance;
        if (f.Handle == 0 || (uint)i >= AdvanceCount || f.Advances[i] <= 0 && c != ' ')
            return fallback.Advance(font, c);
        return f.Advances[i];
    }

    public void Draw(scoped in UiDrawList list)
    {
        Ledger.Clear();
        _clipDepth = 0;

        foreach (var op in list.Ops)
        {
            switch (op.Kind)
            {
                case UiPrimitiveKind.Quad:
                    Quad(list.Quads[op.Index], op.Element);
                    break;
                case UiPrimitiveKind.TexturedQuad:
                {
                    var t = list.TexturedQuads[op.Index];
                    Edges(t.Rect, out int x0, out int y0, out int x1, out int y1);
                    surface.TexturedRect(t.Texture < 0 ? IconAtlas() : t.Texture, x0, y0, x1, y1, t.Uv.X, t.Uv.Y, t.Uv.X + t.Uv.W, t.Uv.Y + t.Uv.H, Pack(t.Tint));
                    Ledger.Add(UiLedgerKind.Texture, x0, y0, x1, y1, t.Tint, op.Element, texture: t.Texture);
                    break;
                }
                case UiPrimitiveKind.GlyphRun:
                    Run(list, list.GlyphRuns[op.Index], op.Element);
                    break;
                case UiPrimitiveKind.Line:
                    Line(list.Lines[op.Index], op.Element);
                    break;
                case UiPrimitiveKind.Polygon:
                    Polygon(list, list.Polygons[op.Index], op.Element);
                    break;
                case UiPrimitiveKind.ScissorPush:
                    Push(list.Scissors[op.Index].Rect, op.Element);
                    break;
                case UiPrimitiveKind.ScissorPop:
                    Pop(op.Element);
                    break;
            }
        }

        // A list that left a clip open must not leave it on the surface.
        if (_clipDepth > 0)
        {
            _clipDepth = 0;
            surface.SetClip(false, 0, 0, 0, 0);
        }
    }

    private void Quad(in UiQuad q, int element)
    {
        Edges(q.Rect, out int x0, out int y0, out int x1, out int y1);

        // The same conditions as the SVG's: a fade is drawn whatever its alpha,
        // a solid fill only when it shows.
        if (q.Fade != UiFade.None)
        {
            surface.FilledRectFade(x0, y0, x1, y1, Pack(q.Color), q.Color.A, q.AlphaEnd, q.Fade == UiFade.Horizontal);
            Ledger.Add(UiLedgerKind.Fade, x0, y0, x1, y1, q.Color, element);
        }
        else if (q.Color.A > 0)
        {
            if (q.Radius >= 1f)
                Rounded(q.Rect, q.Radius, q.Color);
            else
                surface.FilledRect(x0, y0, x1, y1, Pack(q.Color));
            Ledger.Add(UiLedgerKind.Fill, x0, y0, x1, y1, q.Color, element);
        }

        if (q.BorderWidth > 0f && q.Border.A > 0)
        {
            Border(q, x0, y0, x1, y1);
            Ledger.Add(UiLedgerKind.Border, x0, y0, x1, y1, q.Border, element);
        }
    }

    private void Border(in UiQuad q, int x0, int y0, int x1, int y1)
    {
        uint rgba = Pack(q.Border);
        int w = Math.Max(1, (int)MathF.Round(q.BorderWidth));

        if (q.Radius < 1f)
        {
            if (w == 1)
            {
                surface.OutlinedRect(x0, y0, x1, y1, rgba);
                return;
            }
            surface.FilledRect(x0, y0, x1, y0 + w, rgba);
            surface.FilledRect(x0, y1 - w, x1, y1, rgba);
            surface.FilledRect(x0, y0 + w, x0 + w, y1 - w, rgba);
            surface.FilledRect(x1 - w, y0 + w, x1, y1 - w, rgba);
            return;
        }

        // A rounded ring: one quad per segment between the outline and the
        // outline inset by the width.
        var outer = q.Rect;
        var inner = new UiRect(outer.X + w, outer.Y + w, outer.W - 2 * w, outer.H - 2 * w);
        int n = Outline(outer, q.Radius, 0);
        int m = Outline(inner, Math.Max(0f, q.Radius - w), n * 4);
        if (n != m)
            return;
        Span<float> quad = stackalloc float[16];
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vertex(quad, 0, _vertices, i);
            Vertex(quad, 1, _vertices, j);
            Vertex(quad, 2, _vertices, n + j);
            Vertex(quad, 3, _vertices, n + i);
            surface.Polygon(White(), quad, rgba);
        }
    }

    private static void Vertex(Span<float> to, int at, float[] from, int index)
    {
        to[at * 4 + 0] = from[index * 4 + 0];
        to[at * 4 + 1] = from[index * 4 + 1];
        to[at * 4 + 2] = 0f;
        to[at * 4 + 3] = 0f;
    }

    private void Rounded(in UiRect r, float radius, UiColor color)
    {
        int n = Outline(r, radius, 0);
        surface.Polygon(White(), _vertices.AsSpan(0, n * 4), Pack(color));
    }

    /// <summary>
    /// A rounded rect's outline, clockwise from the top-left corner's start,
    /// into <see cref="_vertices"/> from float <paramref name="at"/>: a fixed
    /// number of points per corner, so two outlines of one rect pair up.
    /// </summary>
    private int Outline(in UiRect r, float radius, int at)
    {
        const int PerCorner = 6;
        radius = Math.Min(radius, Math.Min(r.W, r.H) / 2f);
        Span<float> cx = [r.X + r.W - radius, r.X + r.W - radius, r.X + radius, r.X + radius];
        Span<float> cy = [r.Y + radius, r.Y + r.H - radius, r.Y + r.H - radius, r.Y + radius];
        int k = at;
        for (int c = 0; c < 4; c++)
        {
            float start = -MathF.PI / 2f + c * MathF.PI / 2f;
            for (int i = 0; i < PerCorner; i++)
            {
                float a = start + i * (MathF.PI / 2f) / (PerCorner - 1);
                _vertices[k++] = cx[c] + MathF.Cos(a) * radius;
                _vertices[k++] = cy[c] + MathF.Sin(a) * radius;
                _vertices[k++] = 0f;
                _vertices[k++] = 0f;
            }
        }
        return 4 * PerCorner;
    }

    private void Run(scoped in UiDrawList list, in UiGlyphRun run, int element)
    {
        var spec = list.Fonts[run.Font];
        var text = list.Text.Slice(run.TextStart, run.TextLength);
        int handle = Resolve(spec).Handle;
        int x = (int)MathF.Round(run.X), y = (int)MathF.Round(run.Y);

        if ((spec.Flags & UiFontFlags.DropShadow) != 0)
        {
            var shadow = new UiColor(0, 0, 0, (byte)(run.Color.A * 0.6f));
            surface.Text(handle, x + 1, y + 1, text, Pack(shadow));
            Ledger.AddText(x + 1, y + 1, handle, text, shadow, element);
        }

        surface.Text(handle, x, y, text, Pack(run.Color));
        Ledger.AddText(x, y, handle, text, run.Color, element);
    }

    private void Line(in UiLine l, int element)
    {
        int x0 = (int)MathF.Round(l.X0), y0 = (int)MathF.Round(l.Y0);
        int x1 = (int)MathF.Round(l.X1), y1 = (int)MathF.Round(l.Y1);
        if (l.Width <= 1.5f)
        {
            surface.Line(x0, y0, x1, y1, Pack(l.Color));
        }
        else
        {
            float dx = l.X1 - l.X0, dy = l.Y1 - l.Y0;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 0f)
            {
                float nx = -dy / len * l.Width / 2f, ny = dx / len * l.Width / 2f;
                Span<float> quad =
                [
                    l.X0 + nx, l.Y0 + ny, 0f, 0f,
                    l.X1 + nx, l.Y1 + ny, 0f, 0f,
                    l.X1 - nx, l.Y1 - ny, 0f, 0f,
                    l.X0 - nx, l.Y0 - ny, 0f, 0f,
                ];
                surface.Polygon(White(), quad, Pack(l.Color));
            }
        }
        Ledger.Add(UiLedgerKind.Line, x0, y0, x1, y1, l.Color, element);
    }

    private void Polygon(scoped in UiDrawList list, in UiPolygon p, int element)
    {
        int n = Math.Min(p.VertexCount, _vertices.Length / 4);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            var v = list.Vertices[p.VertexStart + i];
            _vertices[i * 4 + 0] = v.X;
            _vertices[i * 4 + 1] = v.Y;
            _vertices[i * 4 + 2] = 0f;
            _vertices[i * 4 + 3] = 0f;
            minX = Math.Min(minX, v.X);
            minY = Math.Min(minY, v.Y);
            maxX = Math.Max(maxX, v.X);
            maxY = Math.Max(maxY, v.Y);
        }
        surface.Polygon(White(), _vertices.AsSpan(0, n * 4), Pack(p.Color));
        Ledger.Add(UiLedgerKind.Polygon, (int)MathF.Round(minX), (int)MathF.Round(minY),
                   (int)MathF.Round(maxX), (int)MathF.Round(maxY), p.Color, element);
    }

    private void Push(in UiRect rect, int element)
    {
        var r = rect;
        if (_clipDepth > 0)
            r = Intersect(_clips[_clipDepth - 1], r);
        if (_clipDepth < _clips.Length)
            _clips[_clipDepth] = r;
        _clipDepth++;
        Edges(r, out int x0, out int y0, out int x1, out int y1);
        surface.SetClip(true, x0, y0, x1, y1);
        Edges(rect, out int a0, out int b0, out int a1, out int b1);
        Ledger.Add(UiLedgerKind.ClipPush, a0, b0, a1, b1, default, element);
    }

    private void Pop(int element)
    {
        if (_clipDepth > 0)
            _clipDepth--;
        if (_clipDepth > 0)
        {
            Edges(_clips[Math.Min(_clipDepth, _clips.Length) - 1], out int x0, out int y0, out int x1, out int y1);
            surface.SetClip(true, x0, y0, x1, y1);
        }
        else
        {
            surface.SetClip(false, 0, 0, 0, 0);
        }
        Ledger.Add(UiLedgerKind.ClipPop, 0, 0, 0, 0, default, element);
    }

    private static UiRect Intersect(in UiRect a, in UiRect b)
    {
        float x0 = Math.Max(a.X, b.X), y0 = Math.Max(a.Y, b.Y);
        float x1 = Math.Min(a.X + a.W, b.X + b.W), y1 = Math.Min(a.Y + a.H, b.Y + b.H);
        return new UiRect(x0, y0, Math.Max(0f, x1 - x0), Math.Max(0f, y1 - y0));
    }

    /// <summary>A rect's pixel edges, each rounded on its own so neighbours share an edge.</summary>
    private static void Edges(in UiRect r, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = (int)MathF.Round(r.X);
        y0 = (int)MathF.Round(r.Y);
        x1 = (int)MathF.Round(r.X + r.W);
        y1 = (int)MathF.Round(r.Y + r.H);
    }

    internal static uint Pack(UiColor c) => (uint)(c.R << 24 | c.G << 16 | c.B << 8 | c.A);

    private int White()
    {
        if (_white == 0)
        {
            _white = surface.TextureCreate();
            surface.TextureUpload(_white, [255, 255, 255, 255], 1, 1);
        }
        return _white;
    }

    private Font Resolve(in UiFontSpec spec)
    {
        if (_fonts.TryGetValue(spec, out var f))
            return f;

        if (!_filesAdded)
        {
            _filesAdded = true;
            foreach (string path in files.Paths)
                surface.FontAddFile(path);
        }

        int flags = 0;
        if ((spec.Flags & UiFontFlags.Antialias) != 0) flags |= FontFlagAntialias;
        if ((spec.Flags & UiFontFlags.Additive) != 0) flags |= FontFlagAdditive;
        if ((spec.Flags & UiFontFlags.Outline) != 0) flags |= FontFlagOutline;

        // vgui's tall is the face's line (ascender + descender), where the
        // toolkit's px is its em, as in the SVG; asked for px, every glyph drew
        // at 1/1.3 of its size (the first live frame). So the line at that em.
        int tall = (int)MathF.Round(fallback.Metrics(spec).LineHeight);
        if (tall <= 0)
            tall = spec.PxHeight;
        int handle = surface.FontCreate(spec.Family, tall, spec.Weight, flags);

        // And checked: a face can come back drawing smaller than that (IBM
        // Plex Mono at 0.76 to 0.89 of its em, live, at 11 to 17 px). Its
        // advances over a sample against the TTF's say by how much; off by more
        // than CalibrationSlack, it is made once more at the tall that corrects it.
        float ratio = handle != 0 ? SampleRatio(handle, spec) : 1f;
        if (ratio > 0f && MathF.Abs(ratio - 1f) > CalibrationSlack)
        {
            // Kept only if nearer: at a small size whole-pixel advances can land
            // either side of the em (Plex Mono at 11 px: 0.93 at one tall, 1.06
            // at the next), and the nearer is the better of two wrongs.
            int corrected = (int)MathF.Round(tall / ratio);
            if (corrected != tall && surface.FontCreate(spec.Family, corrected, spec.Weight, flags) is int again && again != 0
                && MathF.Abs(SampleRatio(again, spec) - 1f) < MathF.Abs(ratio - 1f))
                handle = again;
        }

        f = new Font { Handle = handle };
        _fonts[spec] = f;
        return f;
    }

    /// <summary>How far a made font's advances may sit from the TTF's before it is made again.</summary>
    public const float CalibrationSlack = 0.05f;

    private const string CalibrationSample = "The quick brown fox 0123456789";

    /// <summary>The made font's sample width over the TTF's at the spec's em (0 when either is unknown).</summary>
    private float SampleRatio(int handle, in UiFontSpec spec)
    {
        Span<int> advances = stackalloc int[AdvanceCount];
        if (surface.FontAdvances(handle, FirstAdvance, advances) <= 0)
            return 0f;
        float made = 0f, ttf = 0f;
        foreach (char c in CalibrationSample)
        {
            made += advances[c - FirstAdvance];
            ttf += fallback.Advance(spec, c);
        }
        return made > 0f && ttf > 0f ? made / ttf : 0f;
    }

    private Font Measure(in UiFontSpec spec)
    {
        var f = Resolve(spec);
        if (!f.Measured && f.Handle != 0)
        {
            f.Measured = true;
            surface.FontMetrics(f.Handle, out f.Tall, out f.Ascent);
            surface.FontAdvances(f.Handle, FirstAdvance, f.Advances);
        }
        return f;
    }
}
