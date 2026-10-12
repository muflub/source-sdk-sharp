using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

// LAYOUT: two passes over the frame's tree (plan_client_ui §5.2). Measure runs
// bottom-up and answers each node's natural size; arrange runs top-down and
// gives each child its rect inside its parent's content box. Pixels from here
// on: design units are multiplied by the screen's scale as they are read.
public sealed partial class UiContext
{
    private float S => Screen.Scale;

    private static (float X, float Y) Frac(UiAnchor a) => a switch
    {
        UiAnchor.TopLeft => (0f, 0f),
        UiAnchor.Top => (0.5f, 0f),
        UiAnchor.TopRight => (1f, 0f),
        UiAnchor.Left => (0f, 0.5f),
        UiAnchor.Centre => (0.5f, 0.5f),
        UiAnchor.Right => (1f, 0.5f),
        UiAnchor.BottomLeft => (0f, 1f),
        UiAnchor.Bottom => (0.5f, 1f),
        _ => (1f, 1f),
    };

    internal UiFontSpec FontFor(Text role)
    {
        var t = Theme.Type(role);
        int px = Math.Max(1, (int)MathF.Round(t.SizeDu * S));
        return new UiFontSpec(t.Family, px, t.Weight, t.Flags);
    }

    private float PadX(in UiNode n) => (n.PadL + n.PadR) * S;

    private float PadY(in UiNode n) => (n.PadT + n.PadB) * S;

    /// <summary>A text node's natural size: its widest line and its line count.</summary>
    private (float W, float H) MeasureText(in UiNode n)
    {
        var font = FontFor(n.Role);
        var m = FontMetrics.Metrics(font);
        var text = TextOf(n);
        float wrap = n.WrapDu > 0f ? n.WrapDu * S : float.PositiveInfinity;
        float widest = 0f;
        int lines = 0, at = 0;
        while (NextLine(text, ref at, wrap, font, out _, out float w))
        {
            widest = MathF.Max(widest, w);
            lines++;
        }
        return (MathF.Ceiling(widest), MathF.Ceiling(m.LineHeight) * Math.Max(1, lines));
    }

    /// <summary>
    /// The next line of <paramref name="text"/> from <paramref name="at"/>: up to a
    /// newline, or, past <paramref name="wrap"/> pixels, back to the last space
    /// (a word longer than the line is cut where it overflows). False at the end.
    /// </summary>
    internal bool NextLine(ReadOnlySpan<char> text, ref int at, float wrap, in UiFontSpec font,
                           out ReadOnlySpan<char> line, out float width)
    {
        line = default;
        width = 0f;
        if (at > text.Length || (at == text.Length && at > 0 && text[at - 1] != '\n'))
            return false;

        int start = at, lastSpace = -1;
        float w = 0f, atSpace = 0f;
        int i = start;
        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
                break;
            float a = FontMetrics.Advance(font, c);
            if (w + a > wrap && i > start)
            {
                if (lastSpace > start)
                {
                    line = text[start..lastSpace];
                    width = atSpace;
                    at = lastSpace + 1;
                    return true;
                }
                line = text[start..i];
                width = w;
                at = i;
                return true;
            }
            if (c == ' ')
            {
                lastSpace = i;
                atSpace = w;
            }
            w += a;
        }

        line = text[start..i];
        width = w;
        at = i + 1;
        return true;
    }

    private void Measure(int node)
    {
        for (int c = _nodes[node].FirstChild; c >= 0; c = _nodes[c].Next)
            Measure(c);

        ref var n = ref _nodes[node];
        float w = 0f, h = 0f;

        switch (n.Kind)
        {
            case NodeKind.Text:
                (w, h) = MeasureText(n);
                break;

            case NodeKind.Shape:
            case NodeKind.Spacer:
            // A texture leaf carries its own size, like a shape or a spacer.
            case NodeKind.Texture:
                break;

            case NodeKind.Box:
                for (int c = n.FirstChild; c >= 0; c = _nodes[c].Next)
                {
                    ref var k = ref _nodes[c];
                    w = MathF.Max(w, Natural(k, horizontal: true) + MathF.Abs(k.OffX) * S);
                    h = MathF.Max(h, Natural(k, horizontal: false) + MathF.Abs(k.OffY) * S);
                }
                break;

            case NodeKind.Stack:
            {
                int count = 0;
                bool vertical = n.Axis == Axis.Vertical;
                for (int c = n.FirstChild; c >= 0; c = _nodes[c].Next)
                {
                    if (Collapsed(c))
                        continue;
                    ref var k = ref _nodes[c];
                    float kw = Natural(k, true), kh = Natural(k, false);
                    if (vertical) { h += kh; w = MathF.Max(w, kw); }
                    else { w += kw; h = MathF.Max(h, kh); }
                    count++;
                }
                float gaps = count > 1 ? n.Gap * S * (count - 1) : 0f;
                if (vertical) h += gaps; else w += gaps;
                break;
            }

            case NodeKind.Grid:
                (w, h) = MeasureGrid(node, null, out _);
                break;
        }

        n.MeasuredW = w + PadX(n);
        n.MeasuredH = h + PadY(n);
    }

    /// <summary>A child's size as its parent's measure counts it: fixed sizes as given, the rest as measured.</summary>
    private float Natural(in UiNode k, bool horizontal)
    {
        var size = horizontal ? k.W : k.H;
        float v = size.Unit == UiUnit.Du ? size.Value * S : (horizontal ? k.MeasuredW : k.MeasuredH);
        if (horizontal)
            v = Math.Clamp(v, k.MinW * S, k.MaxW * S);
        return v;
    }

    private float Resolve(in UiNode k, bool horizontal, float available)
    {
        var size = horizontal ? k.W : k.H;
        float v = size.Unit switch
        {
            UiUnit.Du => size.Value * S,
            UiUnit.Percent => available * size.Value / 100f,
            UiUnit.Fill or UiUnit.Fraction => available,
            _ => horizontal ? k.MeasuredW : k.MeasuredH,
        };
        if (horizontal)
            v = Math.Clamp(v, k.MinW * S, k.MaxW * S);
        return v;
    }

    private void SetRect(int node, float x, float y, float w, float h)
    {
        ref var n = ref _nodes[node];
        n.X = x;
        n.Y = y;
        n.Wpx = MathF.Max(0f, w);
        n.Hpx = MathF.Max(0f, h);
    }

    /// <summary>Places an anchored child in a content box.</summary>
    private void PlaceInBox(int node, float cx, float cy, float cw, float ch)
    {
        ref var k = ref _nodes[node];
        float w = Resolve(k, true, cw), h = Resolve(k, false, ch);
        var (pfx, pfy) = Frac(k.ParentAnchor);
        var (sfx, sfy) = Frac(k.SelfAnchor);

        // Offsets point INWARD from the anchor: 16 du from the right edge moves left.
        float sx = pfx > 0.75f ? -1f : 1f, sy = pfy > 0.75f ? -1f : 1f;
        float x = cx + pfx * cw - sfx * w + sx * k.OffX * S;
        float y = cy + pfy * ch - sfy * h + sy * k.OffY * S;

        // Placed by the game as a fraction of the free space (game_text): -1 centres.
        if (!float.IsNaN(k.PlaceX))
            x = cx + (k.PlaceX < 0f ? (cw - w) / 2f : Math.Clamp(k.PlaceX, 0f, 1f) * (cw - w));
        if (!float.IsNaN(k.PlaceY))
            y = cy + (k.PlaceY < 0f ? (ch - h) / 2f : Math.Clamp(k.PlaceY, 0f, 1f) * (ch - h));
        SetRect(node, x, y, w, h);
    }

    /// <summary>Places an overlay against the rect of the node it is anchored to (a dropdown under its field).</summary>
    private void PlaceAgainst(int node, UiRect safe)
    {
        ref var k = ref _nodes[node];
        ref var t = ref _nodes[k.AnchorTarget];
        float w = Resolve(k, true, safe.W), h = Resolve(k, false, safe.H);
        var (tfx, tfy) = Frac(k.TargetAnchor);
        var (sfx, sfy) = Frac(k.SelfAnchor);
        float x = t.X + tfx * t.Wpx - sfx * w + k.OffX * S;
        float y = t.Y + tfy * t.Hpx - sfy * h + k.OffY * S;

        // Kept on the safe area: a dropdown at the bottom edge opens upward by sliding.
        x = Math.Clamp(x, safe.X, MathF.Max(safe.X, safe.X + safe.W - w));
        y = Math.Clamp(y, safe.Y, MathF.Max(safe.Y, safe.Y + safe.H - h));
        SetRect(node, x, y, w, h);
    }

    private void Arrange(int node)
    {
        ref var n = ref _nodes[node];
        float cx = n.X + n.PadL * S, cy = n.Y + n.PadT * S;
        float cw = MathF.Max(0f, n.Wpx - PadX(n)), ch = MathF.Max(0f, n.Hpx - PadY(n));

        // A scroll region moves its content; its offsets are clamped to the
        // content and the clamped values kept (no stored overscroll).
        if (n.Scroll)
        {
            float contentH = n.MeasuredH - PadY(n), contentW = n.MeasuredW - PadX(n);
            ClampScroll(ref n, MathF.Max(0f, contentH - ch) / S, n.ScrollAxes == ScrollAxes.Vertical ? 0f : MathF.Max(0f, contentW - cw) / S);
            cy -= n.ScrollOffset * S;
            cx -= n.ScrollOffsetX * S;
            // Content wider than the view keeps its width (a wide table), rather than being squeezed.
            if (n.ScrollAxes != ScrollAxes.Vertical)
                cw = MathF.Max(cw, contentW);
        }

        switch (n.Kind)
        {
            case NodeKind.Box:
                for (int c = n.FirstChild; c >= 0; c = _nodes[c].Next)
                    PlaceInBox(c, cx, cy, cw, ch);
                break;
            case NodeKind.Stack:
                ArrangeStack(node, cx, cy, cw, ch);
                break;
            case NodeKind.Grid:
                ArrangeGrid(node, cx, cy, cw, ch);
                break;
        }

        for (int c = _nodes[node].FirstChild; c >= 0; c = _nodes[c].Next)
            Arrange(c);
    }

    /// <summary>
    /// An empty, auto-sized container (a slot no one contributed to) takes no
    /// room and no gap in a stack, so declaring a slot costs the layout nothing.
    /// </summary>
    private bool Collapsed(int c)
    {
        ref var k = ref _nodes[c];
        return k.ChildCount == 0 && k.Kind is NodeKind.Stack or NodeKind.Box or NodeKind.Grid
               && k.W.Unit == UiUnit.Auto && k.H.Unit == UiUnit.Auto;
    }

    private void ArrangeStack(int node, float cx, float cy, float cw, float ch)
    {
        ref var n = ref _nodes[node];
        bool vertical = n.Axis == Axis.Vertical;
        float main = vertical ? ch : cw, cross = vertical ? cw : ch;
        float gap = n.Gap * S;

        float fixedSum = 0f, weights = 0f;
        int count = 0;
        for (int c = n.FirstChild; c >= 0; c = _nodes[c].Next)
        {
            if (Collapsed(c))
                continue;
            ref var k = ref _nodes[c];
            var size = vertical ? k.H : k.W;
            if (size.Unit is UiUnit.Fill or UiUnit.Fraction)
                weights += size.Unit == UiUnit.Fill ? 1f : size.Value;
            else
                fixedSum += Resolve(k, !vertical, main);
            count++;
        }

        float gaps = count > 1 ? gap * (count - 1) : 0f;
        float free = MathF.Max(0f, main - fixedSum - gaps);
        float used = fixedSum + gaps + (weights > 0f ? free : 0f);

        float pos = 0f, between = gap;
        if (weights <= 0f)
        {
            switch (n.MainAlign)
            {
                case Align.Centre: pos = (main - used) / 2f; break;
                case Align.End: pos = main - used; break;
                case Align.SpaceBetween when count > 1:
                    between = gap + (main - used) / (count - 1);
                    break;
            }
        }

        for (int c = n.FirstChild; c >= 0; c = _nodes[c].Next)
        {
            if (Collapsed(c))
            {
                SetRect(c, vertical ? cx : cx + pos, vertical ? cy + pos : cy, 0f, 0f);
                continue;
            }
            ref var k = ref _nodes[c];
            var mainSize = vertical ? k.H : k.W;
            float m = mainSize.Unit switch
            {
                UiUnit.Fill => free / weights,
                UiUnit.Fraction => free * mainSize.Value / weights,
                _ => Resolve(k, !vertical, main),
            };

            var crossSize = vertical ? k.W : k.H;
            float x;
            if (n.CrossAlign == Align.Stretch || crossSize.Unit == UiUnit.Fill)
                x = cross;
            else
                x = MathF.Min(Resolve(k, vertical, cross), cross);

            float off = n.CrossAlign switch
            {
                Align.Centre => (cross - x) / 2f,
                Align.End => cross - x,
                _ => 0f,
            };

            if (vertical)
                SetRect(c, cx + off, cy + pos, x, m);
            else
                SetRect(c, cx + pos, cy + off, m, x);

            pos += m + between;
        }
    }

    /// <summary>
    /// A grid's columns and rows. With <paramref name="widths"/>, also fills the
    /// resolved column widths for <paramref name="available"/> width.
    /// </summary>
    private (float W, float H) MeasureGrid(int node, Span<float> widths, out int rows, float available = float.NaN)
    {
        ref var n = ref _nodes[node];
        int cols = Math.Max(1, n.ColumnCount);
        Span<float> natural = stackalloc float[cols];
        natural.Clear();

        int i = 0;
        for (int c = n.FirstChild; c >= 0; c = _nodes[c].Next, i++)
            natural[i % cols] = MathF.Max(natural[i % cols], Natural(_nodes[c], true));
        rows = (i + cols - 1) / cols;

        float fixedSum = 0f, weights = 0f;
        for (int k = 0; k < cols; k++)
        {
            var spec = k < n.ColumnCount ? _columns[n.ColumnStart + k] : Size.Auto;
            switch (spec.Unit)
            {
                case UiUnit.Du: fixedSum += spec.Value * S; break;
                case UiUnit.Percent when !float.IsNaN(available): fixedSum += available * spec.Value / 100f; break;
                case UiUnit.Fraction or UiUnit.Fill: weights += spec.Unit == UiUnit.Fill ? 1f : spec.Value; break;
                default: fixedSum += natural[k]; break;
            }
        }

        float gaps = cols > 1 ? n.Gap * S * (cols - 1) : 0f;
        float width = fixedSum + gaps;
        if (float.IsNaN(available))
        {
            // Measuring: a share column is as wide as its widest cell.
            for (int k = 0; k < cols; k++)
            {
                var spec = k < n.ColumnCount ? _columns[n.ColumnStart + k] : Size.Auto;
                if (spec.Unit is UiUnit.Fraction or UiUnit.Fill or UiUnit.Percent)
                    width += natural[k];
            }
        }
        else if (!widths.IsEmpty)
        {
            float free = MathF.Max(0f, available - fixedSum - gaps);
            for (int k = 0; k < cols; k++)
            {
                var spec = k < n.ColumnCount ? _columns[n.ColumnStart + k] : Size.Auto;
                widths[k] = spec.Unit switch
                {
                    UiUnit.Du => spec.Value * S,
                    UiUnit.Percent => available * spec.Value / 100f,
                    UiUnit.Fraction => weights > 0 ? free * spec.Value / weights : 0f,
                    UiUnit.Fill => weights > 0 ? free / weights : 0f,
                    _ => natural[k],
                };
            }
        }

        float height = 0f;
        int r = 0;
        float rowH = 0f;
        i = 0;
        for (int c = n.FirstChild; c >= 0; c = _nodes[c].Next, i++)
        {
            rowH = MathF.Max(rowH, Natural(_nodes[c], false));
            if (i % cols == cols - 1 || _nodes[c].Next < 0)
            {
                height += rowH;
                rowH = 0f;
                r++;
            }
        }
        if (rows > 1)
            height += n.RowGap * S * (rows - 1);

        return (width, height);
    }

    private void ArrangeGrid(int node, float cx, float cy, float cw, float ch)
    {
        ref var n = ref _nodes[node];
        int cols = Math.Max(1, n.ColumnCount);
        Span<float> widths = stackalloc float[cols];
        MeasureGrid(node, widths, out _, cw);

        float gap = n.Gap * S, rowGap = n.RowGap * S;
        float y = cy;

        // A grid given more height than its rows need shares it out: a 36 du
        // table row centres its cells in 36 du, not in the text's own height.
        var (_, natural) = MeasureGrid(node, default, out int rowCount);
        float extra = rowCount > 0 && n.H.Unit != UiUnit.Auto ? MathF.Max(0f, ch - natural) / rowCount : 0f;
        int i = 0;
        int rowStart = n.FirstChild;
        while (rowStart >= 0)
        {
            // The row's height: its tallest cell.
            float rowH = 0f;
            int c = rowStart;
            for (int k = 0; k < cols && c >= 0; k++, c = _nodes[c].Next)
                rowH = MathF.Max(rowH, Natural(_nodes[c], false));
            rowH += extra;

            float x = cx;
            c = rowStart;
            for (int k = 0; k < cols && c >= 0; k++, c = _nodes[c].Next, i++)
            {
                // A cell with a fixed size (an icon) keeps it, centred
                // vertically at the cell's start; anything else fills its cell.
                ref var cell = ref _nodes[c];
                if (cell.Kind == NodeKind.Shape && cell.W.Unit == UiUnit.Du && cell.H.Unit == UiUnit.Du)
                {
                    float cw2 = MathF.Min(cell.W.Value * S, widths[k]), ch2 = MathF.Min(cell.H.Value * S, rowH);
                    SetRect(c, x, y + (rowH - ch2) / 2f, cw2, ch2);
                }
                else
                    SetRect(c, x, y, widths[k], rowH);
                x += widths[k] + gap;
            }

            rowStart = c;
            y += rowH + rowGap;
        }
    }
}
