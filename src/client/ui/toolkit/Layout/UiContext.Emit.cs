using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

// EMIT: the arranged tree into the render list, in tree order, pixel-snapped.
// Edges are rounded, not sizes, so neighbours neither gap nor overlap. Clips
// intersect down the tree; a node wholly outside its clip still gets an element
// (the instruments see it) but draws nothing.
public sealed partial class UiContext
{
    private char[] _scratch = new char[256];

    private static UiRect Snap(float x, float y, float w, float h)
    {
        float x0 = MathF.Round(x), y0 = MathF.Round(y);
        return new UiRect(x0, y0, MathF.Round(x + w) - x0, MathF.Round(y + h) - y0);
    }

    private static UiRect Intersect(in UiRect a, in UiRect b)
    {
        float x0 = MathF.Max(a.X, b.X), y0 = MathF.Max(a.Y, b.Y);
        float x1 = MathF.Min(a.X + a.W, b.X + b.W), y1 = MathF.Min(a.Y + a.H, b.Y + b.H);
        return new UiRect(x0, y0, MathF.Max(0f, x1 - x0), MathF.Max(0f, y1 - y0));
    }

    private static bool Touches(in UiRect a, in UiRect clip)
        => a.X < clip.X + clip.W && a.X + a.W > clip.X && a.Y < clip.Y + clip.H && a.Y + a.H > clip.Y;

    private UiColor Paint(in UiPaint paint, float opacity)
    {
        var c = Theme.Resolve(paint);
        if (opacity < 1f)
            c = c with { A = (byte)MathF.Round(c.A * opacity) };
        return c;
    }

    private void EmitScrim(int root)
    {
        var scrim = Paint(Tone.Scrim, 1f);
        int element = List.Element(new UiElementInfo(_nodes[root].Id ^ 0x5c5c5c5cu, -1, FullScreen(),
            UiElementKind.Box, 0, 0, UiElementFlags.Overlaps));
        Report.Set(element, Toolkit.Text.Body, Tone.Scrim, Tone.None, false, blocker: true);
        List.Quad(new UiQuad(FullScreen(), scrim, 0, default, 0, UiFade.None, 0), element);
    }

    private void Emit(int node, int parentElement, float opacity, UiRect clip)
    {
        ref var n = ref _nodes[node];
        opacity *= n.Opacity;
        var rect = Snap(n.X, n.Y, n.Wpx, n.Hpx);
        bool visible = Touches(rect, clip) && opacity > 0f;

        var kind = n.Kind switch
        {
            NodeKind.Text => UiElementKind.Text,
            NodeKind.Stack => UiElementKind.Stack,
            NodeKind.Grid => UiElementKind.Grid,
            NodeKind.Shape => n.Shape == UiShape.Art ? UiElementKind.Image : UiElementKind.Shape,
            NodeKind.Spacer => UiElementKind.Spacer,
            NodeKind.Texture => UiElementKind.Image,
            _ => UiElementKind.Box,
        };

        if (n.Clip)
            n.Flags |= UiElementFlags.Clips;
        if (_screenLayer == UiLayerKind.Hud)
            n.Flags |= UiElementFlags.OverWorld;
        if ((n.Fill.IsRaw && !n.Fill.IsGame) || (n.TextPaint.IsRaw && !n.TextPaint.IsGame) || (n.Border.IsRaw && !n.Border.IsGame))
            n.Flags |= UiElementFlags.Raw;

        int element = List.Element(new UiElementInfo(n.Id, parentElement, rect, kind, 0, 0, UiElementFlags.None));
        Report.ResetOverflow(element);

        if (visible)
        {
            if (n.Elevation != Elevation.Flat && !n.Fill.IsNone)
                EmitShadow(n, rect, opacity, element);

            if (!n.Fill.IsNone || (n.BorderDu > 0f && !n.Border.IsNone))
            {
                var fill = n.Fill.IsNone ? default : Paint(n.Fill, opacity * (n.Fade != UiFade.None ? n.FadeStart : 1f));
                var border = n.BorderDu > 0f && !n.Border.IsNone ? Paint(n.Border, opacity) : default;
                byte end = n.Fill.IsNone ? (byte)0 : (byte)MathF.Round(Paint(n.Fill, opacity).A * Math.Clamp(n.FadeEnd, 0f, 1f));
                List.Quad(new UiQuad(rect, fill, MathF.Round(n.RadiusDu * S), border,
                                     n.BorderDu > 0f ? MathF.Max(1f, MathF.Round(n.BorderDu * S)) : 0f, n.Fade, end), element);
            }

            if (n.Kind == NodeKind.Text)
                EmitText(ref n, rect, opacity, element);
            else if (n.Kind == NodeKind.Shape)
                EmitShape(n, rect, opacity, element);
            else if (n.Kind == NodeKind.Texture)
                List.TexturedQuad(new UiTexturedQuad(rect, n.Texture, n.TextureUv,
                                     new UiColor(255, 255, 255, (byte)MathF.Round(255 * opacity))), element);
        }
        else if (n.Kind == NodeKind.Text)
        {
            // Still measured for the report: hidden text can be clipped text.
            n.Font = List.Font(FontFor(n.Role));
        }

        if (n.Id == Focused && Focused != 0 && !n.Scroll)
            FocusedEmitted(rect);

        var childClip = clip;
        bool pushed = false;
        if (n.Clip)
        {
            childClip = Intersect(clip, rect);
            List.PushScissor(childClip, element);
            pushed = true;
        }

        if (n.Scroll)
            EnterScroll(node);
        for (int c = n.FirstChild; c >= 0; c = _nodes[c].Next)
            Emit(c, element, opacity, childClip);
        if (n.Scroll)
            LeaveScroll();

        if (pushed)
            List.PopScissor(element);

        if (n.Scroll && visible)
            EmitScrollbar(node, element, opacity);

        // The element's final facts, now that text has been fitted.
        int nameStart = 0, nameLength = 0;
        if (RecordNames)
        {
            string name = Describe(node);
            nameStart = List.AppendText(name);
            nameLength = name.Length;
        }

        var flags = n.Flags & ~Keyed;
        List.SetElement(element, new UiElementInfo(n.Id, parentElement, rect, kind, nameStart, nameLength, flags));

        bool large = n.Kind == NodeKind.Text && Theme.Type(n.Role).SizeDu >= 24f;
        Report.Set(element,
                   n.Kind == NodeKind.Text ? n.Role : (Text)255,
                   n.Kind is NodeKind.Text or NodeKind.Shape ? n.TextPaint.Tone : Tone.None,
                   n.Fill.IsRaw ? Tone.None : n.Fill.Tone, large, blocker: false,
                   clip: visible ? clip : default, hidden: !visible, scroll: n.Scroll, fade: opacity);
        if (n.Handle)
            Report.MarkThumb(element);
        if (n.Movable)
            Report.MarkMovable(element);
    }

    private void EmitShadow(in UiNode n, UiRect rect, float opacity, int element)
    {
        var shadow = Theme.Shadow(n.Elevation);
        float off = MathF.Round(shadow.OffsetDu * S), spread = MathF.Round(shadow.SpreadDu * S);
        for (int layer = 2; layer >= 1; layer--)
        {
            float g = spread * layer / 2f;
            byte a = (byte)MathF.Round(shadow.Alpha * opacity / (layer == 2 ? 2.5f : 1f));
            var r = new UiRect(rect.X - g, rect.Y - g + off, rect.W + 2 * g, rect.H + 2 * g);
            List.Quad(new UiQuad(r, new UiColor(0, 0, 0, a), MathF.Round(n.RadiusDu * S) + g, default, 0, UiFade.None, 0), element);
        }
    }

    private void EmitText(ref UiNode n, UiRect rect, float opacity, int element)
    {
        var font = FontFor(n.Role);
        n.Font = List.Font(font);
        var m = FontMetrics.Metrics(font);
        float lineH = MathF.Ceiling(m.LineHeight);
        var color = Paint(n.TextPaint, opacity);
        var text = TextOf(n);

        // The laid-out width, not the snapped one: snapping can take a pixel off
        // a box that the text was measured to fit exactly.
        float avail = n.Wpx - (n.PadL + n.PadR) * S;
        float left = rect.X + n.PadL * S;
        float wrap = n.WrapDu > 0f ? MathF.Min(n.WrapDu * S, MathF.Max(avail, 1f)) : float.PositiveInfinity;

        int lines = 0, at = 0;
        while (NextLine(text, ref at, wrap, font, out _, out _))
            lines++;
        lines = Math.Max(1, lines);
        float y = rect.Y + MathF.Round((rect.H - lineH * lines) / 2f);

        if (n.Caret >= 0)
        {
            EmitEdited(ref n, text, font, left, y, lineH, avail, color, opacity, element);
            return;
        }

        at = 0;
        while (NextLine(text, ref at, wrap, font, out var line, out float w))
        {
            ReadOnlySpan<char> drawn = line;
            if (w > avail + 0.5f)
            {
                if (n.Elide)
                {
                    drawn = ElideTo(line, font, avail, out w);
                    n.Flags |= UiElementFlags.Elided;
                }
                else
                {
                    n.Flags |= UiElementFlags.Overflowed;
                    Report.Overflow(element, MathF.Max(Report.OverflowOf(element), w - avail));
                }
            }

            float x = n.TextAlign switch
            {
                Align.Centre => left + (avail - w) / 2f,
                Align.End => left + avail - w,
                _ => left,
            };

            if (drawn.Length > 0)
                List.GlyphRun(MathF.Round(x), y, n.Font, color, drawn, element);

            y += lineH;
        }
    }

    /// <summary>
    /// An edited line (a text field): the selection behind the text, the text
    /// shifted left so the caret stays in view, and the caret (blinking on the
    /// layer's clock). One line; never elided or reported as overflowing, since
    /// the field clips it.
    /// </summary>
    private void EmitEdited(ref UiNode n, ReadOnlySpan<char> text, in UiFontSpec font, float left, float y, float lineH,
                            float avail, UiColor color, float opacity, int element)
    {
        int caret = Math.Clamp(n.Caret, 0, text.Length);
        float caretX = 0f, selX0 = 0f, selX1 = 0f;
        int s0 = Math.Clamp(n.SelStart, 0, text.Length), s1 = Math.Clamp(n.SelStart + n.SelLength, 0, text.Length);
        float pen = 0f;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i == caret) caretX = pen;
            if (i == s0) selX0 = pen;
            if (i == s1) selX1 = pen;
            if (i < text.Length) pen += FontMetrics.Advance(font, text[i]);
        }

        float caretW = MathF.Max(1f, MathF.Round(2 * S));
        float shift = MathF.Max(0f, caretX + caretW - avail);
        float x = MathF.Round(left - shift);

        if (s1 > s0)
        {
            var sel = Paint(((UiPaint)Tone.Accent).WithOpacity(0.35f), opacity);
            List.Quad(new UiQuad(Snap(x + selX0, y, selX1 - selX0, lineH), sel, 0, default, 0, UiFade.None, 0), element);
        }
        if (text.Length > 0)
            List.GlyphRun(x, y, n.Font, color, text, element);

        // Blinks at about 1 Hz, on whole half-seconds of the layer's clock (stable in previews).
        bool on = ((long)Math.Floor(Now * 2)) % 2 == 0;
        if (on)
            List.Quad(new UiQuad(Snap(x + caretX, y + 1, caretW, lineH - 2), Paint(Tone.Accent, opacity), 0, default, 0, UiFade.None, 0), element);
    }

    /// <summary>The longest prefix that fits with an ellipsis after it.</summary>
    private ReadOnlySpan<char> ElideTo(ReadOnlySpan<char> line, scoped in UiFontSpec font, float avail, out float width)
    {
        const char ellipsis = '…';
        float e = FontMetrics.Advance(font, ellipsis);
        float w = 0f;
        int keep = 0;
        while (keep < line.Length)
        {
            float a = FontMetrics.Advance(font, line[keep]);
            if (w + a + e > avail)
                break;
            w += a;
            keep++;
        }

        // No dangling space before the ellipsis.
        while (keep > 0 && line[keep - 1] == ' ')
            w -= FontMetrics.Advance(font, line[--keep]);

        if (_scratch.Length < keep + 1)
            Array.Resize(ref _scratch, Math.Max(_scratch.Length * 2, keep + 1));
        line[..keep].CopyTo(_scratch);
        _scratch[keep] = ellipsis;
        width = w + e;
        return _scratch.AsSpan(0, keep + 1);
    }
}
