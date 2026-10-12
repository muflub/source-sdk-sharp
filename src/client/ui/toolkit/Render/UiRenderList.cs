using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The arrays a frame's draw is emitted into, reused frame after frame: they
/// grow to the busiest frame and then stop allocating (plan_client_ui §5.2's
/// zero-GC rule). <see cref="AsDrawList"/> hands a backend spans over them.
/// </summary>
public sealed class UiRenderList
{
    private UiDrawOp[] _ops = new UiDrawOp[256];
    private UiQuad[] _quads = new UiQuad[128];
    private UiTexturedQuad[] _textured = new UiTexturedQuad[8];
    private UiGlyphRun[] _runs = new UiGlyphRun[128];
    private UiLine[] _lines = new UiLine[16];
    private UiPolygon[] _polygons = new UiPolygon[16];
    private UiVertex[] _vertices = new UiVertex[64];
    private UiScissor[] _scissors = new UiScissor[16];
    private UiFontSpec[] _fonts = new UiFontSpec[16];
    private char[] _text = new char[4096];
    private UiElementInfo[] _elements = new UiElementInfo[256];

    public int OpCount { get; private set; }
    public int QuadCount { get; private set; }
    public int TexturedQuadCount { get; private set; }
    public int GlyphRunCount { get; private set; }
    public int LineCount { get; private set; }
    public int PolygonCount { get; private set; }
    public int VertexCount { get; private set; }
    public int ScissorCount { get; private set; }
    public int FontCount { get; private set; }
    public int TextLength { get; private set; }
    public int ElementCount { get; private set; }

    /// <summary>The pointer shape this frame asks for.</summary>
    public UiCursor Cursor { get; set; }

    /// <summary>The screen the list was laid out for.</summary>
    public ScreenInfo Screen { get; set; }

    /// <summary>Empties the list, keeping its storage.</summary>
    public void Clear()
    {
        OpCount = QuadCount = TexturedQuadCount = GlyphRunCount = LineCount = 0;
        PolygonCount = VertexCount = ScissorCount = FontCount = TextLength = ElementCount = 0;
        Cursor = UiCursor.Default;
    }

    private static void Grow<T>(ref T[] array, int needed)
    {
        if (needed <= array.Length)
            return;
        Array.Resize(ref array, Math.Max(needed, array.Length * 2));
    }

    private void Op(UiPrimitiveKind kind, int index, int element)
    {
        Grow(ref _ops, OpCount + 1);
        _ops[OpCount++] = new UiDrawOp(kind, index, element);
    }

    public void Quad(in UiQuad quad, int element = -1)
    {
        Grow(ref _quads, QuadCount + 1);
        _quads[QuadCount] = quad;
        Op(UiPrimitiveKind.Quad, QuadCount++, element);
    }

    public void TexturedQuad(in UiTexturedQuad quad, int element = -1)
    {
        Grow(ref _textured, TexturedQuadCount + 1);
        _textured[TexturedQuadCount] = quad;
        Op(UiPrimitiveKind.TexturedQuad, TexturedQuadCount++, element);
    }

    /// <summary>A line of text; the characters are copied into the list.</summary>
    public void GlyphRun(float x, float y, int font, UiColor color, ReadOnlySpan<char> text, int element = -1)
    {
        int start = AppendText(text);
        Grow(ref _runs, GlyphRunCount + 1);
        _runs[GlyphRunCount] = new UiGlyphRun(x, y, font, color, start, text.Length);
        Op(UiPrimitiveKind.GlyphRun, GlyphRunCount++, element);
    }

    public void Line(in UiLine line, int element = -1)
    {
        Grow(ref _lines, LineCount + 1);
        _lines[LineCount] = line;
        Op(UiPrimitiveKind.Line, LineCount++, element);
    }

    public void Polygon(ReadOnlySpan<UiVertex> vertices, UiColor color, int element = -1)
    {
        Grow(ref _vertices, VertexCount + vertices.Length);
        vertices.CopyTo(_vertices.AsSpan(VertexCount));
        Grow(ref _polygons, PolygonCount + 1);
        _polygons[PolygonCount] = new UiPolygon(VertexCount, vertices.Length, color);
        VertexCount += vertices.Length;
        Op(UiPrimitiveKind.Polygon, PolygonCount++, element);
    }

    public void PushScissor(in UiRect rect, int element = -1)
    {
        Grow(ref _scissors, ScissorCount + 1);
        _scissors[ScissorCount] = new UiScissor(rect);
        Op(UiPrimitiveKind.ScissorPush, ScissorCount++, element);
    }

    public void PopScissor(int element = -1) => Op(UiPrimitiveKind.ScissorPop, -1, element);

    /// <summary>The index of <paramref name="font"/> in the list's font table, adding it once.</summary>
    public int Font(in UiFontSpec font)
    {
        for (int i = 0; i < FontCount; i++)
        {
            ref readonly var f = ref _fonts[i];
            if (f.PxHeight == font.PxHeight && f.Weight == font.Weight && f.Flags == font.Flags
                && string.Equals(f.Family, font.Family, StringComparison.Ordinal))
                return i;
        }

        Grow(ref _fonts, FontCount + 1);
        _fonts[FontCount] = font;
        return FontCount++;
    }

    /// <summary>Appends characters to the list's text, answering where they start.</summary>
    public int AppendText(ReadOnlySpan<char> text)
    {
        Grow(ref _text, TextLength + text.Length);
        text.CopyTo(_text.AsSpan(TextLength));
        int start = TextLength;
        TextLength += text.Length;
        return start;
    }

    /// <summary>Adds an element and answers its index.</summary>
    public int Element(in UiElementInfo element)
    {
        Grow(ref _elements, ElementCount + 1);
        _elements[ElementCount] = element;
        return ElementCount++;
    }

    /// <summary>Overwrites element <paramref name="index"/> (its rect and flags are known only after emit).</summary>
    public void SetElement(int index, in UiElementInfo element) => _elements[index] = element;

    public ref readonly UiElementInfo ElementAt(int index) => ref _elements[index];

    public UiDrawList AsDrawList() => new()
    {
        Screen = Screen,
        Cursor = Cursor,
        Ops = _ops.AsSpan(0, OpCount),
        Quads = _quads.AsSpan(0, QuadCount),
        TexturedQuads = _textured.AsSpan(0, TexturedQuadCount),
        GlyphRuns = _runs.AsSpan(0, GlyphRunCount),
        Lines = _lines.AsSpan(0, LineCount),
        Polygons = _polygons.AsSpan(0, PolygonCount),
        Vertices = _vertices.AsSpan(0, VertexCount),
        Scissors = _scissors.AsSpan(0, ScissorCount),
        Fonts = _fonts.AsSpan(0, FontCount),
        Text = _text.AsSpan(0, TextLength),
        Elements = _elements.AsSpan(0, ElementCount),
    };
}
