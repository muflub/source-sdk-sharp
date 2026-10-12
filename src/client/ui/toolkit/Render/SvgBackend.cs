using System.Globalization;
using System.Text;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// Draws a render list as SVG text (plan_client_ui §4, novalve §3.2's test
/// renderer): diffable, rendered by any browser, the format of the goldens and
/// of the web UI's Mockups tab. Every primitive carries <c>data-e</c>, the index
/// of the element that emitted it, so the inspector can name what is under the
/// cursor.
///
/// <para>
/// Not a per-frame path: it builds a string. Measuring is delegated to the font
/// metrics it is constructed with, so text is placed exactly as the toolkit laid
/// it out (the baseline is the run's top plus the font's ascent).
/// </para>
/// </summary>
public sealed class SvgBackend : IUiBackend
{
    private readonly IUiFontMetrics _metrics;
    private readonly StringBuilder _svg = new();
    private UiIconSet? _icons;

    public SvgBackend(IUiFontMetrics metrics) => _metrics = metrics;

    /// <summary>The last list drawn, as an SVG document.</summary>
    public string Svg => _svg.ToString();

    public UiFontMetrics Metrics(in UiFontSpec font) => _metrics.Metrics(font);

    public float Advance(in UiFontSpec font, char c) => _metrics.Advance(font, c);

    public void Draw(scoped in UiDrawList list)
    {
        var s = _svg.Clear();
        var screen = list.Screen;
        s.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(screen.W)
         .Append("\" height=\"").Append(screen.H)
         .Append("\" viewBox=\"0 0 ").Append(screen.W).Append(' ').Append(screen.H)
         .Append("\" data-scale=\"").Append(N(screen.Scale)).Append("\">\n");

        int clipId = 0;
        int gradientId = 0;

        foreach (var op in list.Ops)
        {
            switch (op.Kind)
            {
                case UiPrimitiveKind.Quad:
                    Quad(s, list.Quads[op.Index], op.Element, ref gradientId);
                    break;
                case UiPrimitiveKind.TexturedQuad:
                {
                    var t = list.TexturedQuads[op.Index];
                    if (UiIconSet.RectOf(t.Texture) is int a and >= 0)
                    {
                        // An icon: its art, as the vector it was drawn from, scaled into the quad.
                        _icons ??= UiIconSet.Embedded();
                        var art = _icons.Icon(_icons.Rects[a].Icon);
                        s.Append("<path transform=\"translate(").Append(N(t.Rect.X)).Append(' ').Append(N(t.Rect.Y))
                         .Append(") scale(").Append(N(t.Rect.W / art.Width)).Append(' ').Append(N(t.Rect.H / UiIconSet.GridHeight))
                         .Append(")\" fill-rule=\"evenodd\"").Append(Fill(t.Tint)).Append(" d=\"").Append(art.PathData)
                         .Append("\" data-icon=\"").Append(art.Name).Append('"')
                         .Append(" data-rect=\"").Append(N(t.Rect.X)).Append(' ').Append(N(t.Rect.Y)).Append(' ')
                         .Append(N(t.Rect.W)).Append(' ').Append(N(t.Rect.H)).Append('"').Append(E(op.Element)).Append("/>\n");
                        break;
                    }
                    s.Append("<rect").Append(Rect(t.Rect)).Append(Fill(t.Tint))
                     .Append(" data-texture=\"").Append(t.Texture).Append('"').Append(E(op.Element)).Append("/>\n");
                    break;
                }
                case UiPrimitiveKind.GlyphRun:
                    Run(s, list, list.GlyphRuns[op.Index], op.Element);
                    break;
                case UiPrimitiveKind.Line:
                {
                    var l = list.Lines[op.Index];
                    s.Append("<line x1=\"").Append(N(l.X0)).Append("\" y1=\"").Append(N(l.Y0))
                     .Append("\" x2=\"").Append(N(l.X1)).Append("\" y2=\"").Append(N(l.Y1))
                     .Append("\" stroke=\"").Append(Hex(l.Color)).Append('"').Append(Opacity("stroke-opacity", l.Color))
                     .Append(" stroke-width=\"").Append(N(l.Width)).Append('"').Append(E(op.Element)).Append("/>\n");
                    break;
                }
                case UiPrimitiveKind.Polygon:
                {
                    var p = list.Polygons[op.Index];
                    s.Append("<polygon points=\"");
                    for (int i = 0; i < p.VertexCount; i++)
                    {
                        var v = list.Vertices[p.VertexStart + i];
                        if (i > 0) s.Append(' ');
                        s.Append(N(v.X)).Append(',').Append(N(v.Y));
                    }
                    s.Append('"').Append(Fill(p.Color)).Append(E(op.Element)).Append("/>\n");
                    break;
                }
                case UiPrimitiveKind.ScissorPush:
                {
                    var r = list.Scissors[op.Index].Rect;
                    s.Append("<clipPath id=\"c").Append(clipId).Append("\"><rect").Append(Rect(r)).Append("/></clipPath>")
                     .Append("<g clip-path=\"url(#c").Append(clipId).Append(")\"").Append(E(op.Element)).Append(">\n");
                    clipId++;
                    break;
                }
                case UiPrimitiveKind.ScissorPop:
                    s.Append("</g>\n");
                    break;
            }
        }

        s.Append("</svg>\n");
    }

    private static void Quad(StringBuilder s, in UiQuad q, int element, ref int gradientId)
    {
        string fill = Hex(q.Color);
        if (q.Fade != UiFade.None)
        {
            bool vertical = q.Fade == UiFade.Vertical;
            s.Append("<linearGradient id=\"g").Append(gradientId).Append("\" x1=\"0\" y1=\"0\" x2=\"")
             .Append(vertical ? '0' : '1').Append("\" y2=\"").Append(vertical ? '1' : '0').Append("\">")
             .Append("<stop offset=\"0\" stop-color=\"").Append(fill).Append("\" stop-opacity=\"").Append(N(q.Color.A / 255f)).Append("\"/>")
             .Append("<stop offset=\"1\" stop-color=\"").Append(fill).Append("\" stop-opacity=\"").Append(N(q.AlphaEnd / 255f)).Append("\"/>")
             .Append("</linearGradient>");
            s.Append("<rect").Append(Rect(q.Rect)).Append(Radius(q.Radius))
             .Append(" fill=\"url(#g").Append(gradientId).Append(")\"").Append(E(element)).Append("/>\n");
            gradientId++;
        }
        else if (q.Color.A > 0)
        {
            s.Append("<rect").Append(Rect(q.Rect)).Append(Radius(q.Radius)).Append(Fill(q.Color)).Append(E(element)).Append("/>\n");
        }

        if (q.BorderWidth > 0f && q.Border.A > 0)
        {
            // Inside the rect, as vgui's outlined rect is: inset by half the stroke.
            float h = q.BorderWidth / 2f;
            var inner = new UiRect(q.Rect.X + h, q.Rect.Y + h, q.Rect.W - q.BorderWidth, q.Rect.H - q.BorderWidth);
            s.Append("<rect").Append(Rect(inner)).Append(Radius(Math.Max(0f, q.Radius - h)))
             .Append(" fill=\"none\" stroke=\"").Append(Hex(q.Border)).Append('"').Append(Opacity("stroke-opacity", q.Border))
             .Append(" stroke-width=\"").Append(N(q.BorderWidth)).Append('"').Append(E(element)).Append("/>\n");
        }
    }

    private void Run(StringBuilder s, scoped in UiDrawList list, in UiGlyphRun run, int element)
    {
        var font = list.Fonts[run.Font];
        var text = list.Text.Slice(run.TextStart, run.TextLength);
        float baseline = run.Y + _metrics.Metrics(font).Ascent;

        if ((font.Flags & UiFontFlags.DropShadow) != 0)
            Text(s, font, run.X + 1, baseline + 1, new UiColor(0, 0, 0, (byte)(run.Color.A * 0.6f)), text, element);

        Text(s, font, run.X, baseline, run.Color, text, element);
    }

    private static void Text(StringBuilder s, in UiFontSpec font, float x, float baseline, UiColor color,
                             ReadOnlySpan<char> text, int element)
    {
        s.Append("<text x=\"").Append(N(x)).Append("\" y=\"").Append(N(baseline))
         .Append("\" font-family=\"").Append(font.Family).Append("\" font-size=\"").Append(font.PxHeight)
         .Append("\" font-weight=\"").Append(font.Weight).Append('"').Append(Fill(color))
         .Append(" xml:space=\"preserve\"").Append(E(element)).Append('>');
        foreach (char c in text)
        {
            switch (c)
            {
                case '<': s.Append("&lt;"); break;
                case '>': s.Append("&gt;"); break;
                case '&': s.Append("&amp;"); break;
                case '"': s.Append("&quot;"); break;
                default: s.Append(c); break;
            }
        }
        s.Append("</text>\n");
    }

    private static string E(int element) => element >= 0 ? $" data-e=\"{element}\"" : string.Empty;

    private static string Rect(in UiRect r)
        => $" x=\"{N(r.X)}\" y=\"{N(r.Y)}\" width=\"{N(r.W)}\" height=\"{N(r.H)}\"";

    private static string Radius(float r) => r > 0f ? $" rx=\"{N(r)}\"" : string.Empty;

    private static string Fill(UiColor c) => $" fill=\"{Hex(c)}\"" + Opacity("fill-opacity", c);

    private static string Opacity(string name, UiColor c)
        => c.A == 255 ? string.Empty : $" {name}=\"{N(c.A / 255f)}\"";

    internal static string Hex(UiColor c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    /// <summary>A number as SVG wants it: invariant, at most two decimals, no trailing zeros.</summary>
    internal static string N(float v) => MathF.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
