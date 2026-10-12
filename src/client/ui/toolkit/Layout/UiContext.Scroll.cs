namespace SourceSharp.Client.Ui.Toolkit;

// SCROLLING: the kept state of each scroll region (by id), the wheel, easing,
// and (in Arrange) clamping to the content, written back so the kept offset
// never holds overscroll.
public sealed partial class UiContext
{
    internal struct ScrollState
    {
        /// <summary>Where the region is going and where it is drawn, du, each axis.</summary>
        public float Target, Shown, TargetX, ShownX;

        /// <summary>Last frame's view and content heights and the thumb's travel, px: what keys and drags scroll by.</summary>
        public float ViewPx, ContentPx, ThumbTravelPx;

        /// <summary>Last frame, the focused element was inside this region.</summary>
        public bool HoldsFocus;

        /// <summary>Set once the region has been laid out; and whether it ended at its end.</summary>
        public bool Seen, AtEnd;
    }

    internal static uint ThumbId(uint region) => Hash(region, "thumb");

    /// <summary>Applies this frame's wheel to the region's target and eases what is drawn towards it.</summary>
    internal void UpdateScroll(ref UiNode n)
    {
        ref var st = ref State<ScrollState>(n.Id);
        float wheel = WheelFor(n.Id) * Theme.ScrollStepDu;
        if (n.ScrollAxes == ScrollAxes.Horizontal)
            st.TargetX -= wheel;
        else
            st.Target -= wheel;

        // Keys, when the region or something in it has focus.
        if (Focused != 0 && (Focused == n.Id || st.HoldsFocus))
        {
            float page = st.ViewPx / MathF.Max(Screen.Scale, 0.0001f);
            if (ConsumeKey((int)UiKey.PageDown)) st.Target += page;
            if (ConsumeKey((int)UiKey.PageUp)) st.Target -= page;
            if (ConsumeKey((int)UiKey.Down)) st.Target += Theme.ScrollStepDu;
            if (ConsumeKey((int)UiKey.Up)) st.Target -= Theme.ScrollStepDu;
            if (ConsumeKey((int)UiKey.Home)) st.Target = 0f;
            if (ConsumeKey((int)UiKey.End)) st.Target = float.MaxValue / 4;
        }

        // The thumb, dragged: content moves in proportion, with no easing behind the pointer.
        bool dragging = IsPressed(ThumbId(n.Id));
        if (dragging && ActiveBefore == ThumbId(n.Id) && st.ThumbTravelPx > 0f)
        {
            float dy = MouseY - MouseYBefore;
            st.Target += dy * (st.ContentPx - st.ViewPx) / st.ThumbTravelPx / MathF.Max(Screen.Scale, 0.0001f);
        }

        st.Target = MathF.Max(0f, st.Target);
        st.TargetX = MathF.Max(0f, st.TargetX);

        st.Shown = dragging ? st.Target : Ease(st.Shown, st.Target);
        st.ShownX = Ease(st.ShownX, st.TargetX);
        n.ScrollOffset = st.Shown;
        n.ScrollOffsetX = st.ShownX;
    }

    private float Ease(float shown, float target)
    {
        float duration = Theme.Duration(Motion.Quick);
        if (duration <= 0f || Dt <= 0f)
            return Dt <= 0f ? shown : target;
        float k = 1f - MathF.Exp(-3f * Dt / duration);
        float next = shown + (target - shown) * k;
        return MathF.Abs(target - next) < 0.25f ? target : next;
    }

    /// <summary>Clamps a region's offsets to its content and keeps the clamped values (Arrange).</summary>
    internal void ClampScroll(ref UiNode n, float maxY, float maxX)
    {
        ref var st = ref State<ScrollState>(n.Id);
        // A followed region (a log) that was at its end stays there as it grows.
        if (n.Follow && (st.AtEnd || !st.Seen))
        {
            st.Target = maxY;
            if (!st.Seen) st.Shown = maxY;
        }
        st.Target = Math.Clamp(st.Target, 0f, maxY);
        st.Shown = Math.Clamp(st.Shown, 0f, maxY);
        st.TargetX = Math.Clamp(st.TargetX, 0f, maxX);
        st.ShownX = Math.Clamp(st.ShownX, 0f, maxX);
        n.ScrollOffset = st.Shown;
        n.ScrollOffsetX = st.ShownX;
    }

    // ---- emit: the bar, focus, reveal ----

    private int[] _scrollStack = new int[8];
    private int _scrollDepth;

    private void EnterScroll(int node)
    {
        if (_scrollDepth == _scrollStack.Length)
            Array.Resize(ref _scrollStack, _scrollStack.Length * 2);
        _scrollStack[_scrollDepth++] = node;
        State<ScrollState>(_nodes[node].Id).HoldsFocus = false;
    }

    private void LeaveScroll() => _scrollDepth--;

    /// <summary>
    /// The focused element is being emitted: every region around it holds the
    /// focus; when the keyboard moved it here, the innermost brings it into view.
    /// </summary>
    private void FocusedEmitted(in UiRect rect)
    {
        for (int i = 0; i < _scrollDepth; i++)
            State<ScrollState>(_nodes[_scrollStack[i]].Id).HoldsFocus = true;
        if (!FocusMovedByKey || _scrollDepth == 0)
            return;

        ref var region = ref _nodes[_scrollStack[_scrollDepth - 1]];
        ref var st = ref State<ScrollState>(region.Id);
        float top = region.Y + region.PadT * S, bottom = region.Y + region.Hpx - region.PadB * S;
        // Where the element will be once the region has reached its target.
        float lag = (st.Shown - st.Target) * S;
        float elemTop = rect.Y + lag, elemBottom = rect.Y + rect.H + lag;
        if (elemBottom > bottom)
            st.Target += (elemBottom - bottom) / S;
        else if (elemTop < top)
            st.Target -= (top - elemTop) / S;
    }

    /// <summary>A region whose content overflows gets a track and a draggable thumb inside its right edge.</summary>
    private void EmitScrollbar(int node, int element, float opacity)
    {
        ref var n = ref _nodes[node];
        ref var st = ref State<ScrollState>(n.Id);
        float view = MathF.Max(0f, n.Hpx - PadY(n));
        float content = n.MeasuredH - PadY(n);
        st.ViewPx = view;
        st.ContentPx = content;
        st.ThumbTravelPx = 0f;
        if (n.ScrollAxes == ScrollAxes.Horizontal || content <= view + 0.5f || view <= 0f)
            return;

        float w = MathF.Round(6 * S), inset = MathF.Round(2 * S);
        float x = n.X + n.Wpx - inset - w, y = n.Y + n.PadT * S;
        float thumbH = MathF.Max(24 * S, view * view / content);
        float travel = view - thumbH;
        float max = content - view;
        float ty = y + (max > 0f ? travel * Math.Clamp(n.ScrollOffset * S / max, 0f, 1f) : 0f);
        st.ThumbTravelPx = travel;

        uint thumbId = ThumbId(n.Id);
        bool hot = Hovered == thumbId || IsPressed(thumbId);
        var track = Snap(x, y, w, view);
        var thumb = Snap(x, ty, w, thumbH);

        int t = List.Element(new UiElementInfo(Hash(n.Id, "track"), element, track, UiElementKind.Box, 0, 0, UiElementFlags.Overlaps));
        Report.ResetOverflow(t);
        Report.Set(t, (Text)255, Tone.None, Tone.Line, false, false, FullScreen());
        List.Quad(new UiQuad(track, Paint(Tone.Line, opacity), MathF.Round(w / 2), default, 0, UiFade.None, 0), t);

        int nameStart = 0, nameLength = 0;
        if (RecordNames)
        {
            string name = $"{Describe(node)} thumb";
            nameStart = List.AppendText(name);
            nameLength = name.Length;
        }
        int e = List.Element(new UiElementInfo(thumbId, element, thumb, UiElementKind.Box, nameStart, nameLength,
                                               UiElementFlags.Interactive | UiElementFlags.Overlaps));
        Report.ResetOverflow(e);
        Report.Set(e, (Text)255, Tone.None, Tone.TextMuted, false, false, FullScreen());
        Report.MarkThumb(e);
        var ink = hot ? Paint(Tone.Text, opacity) : Paint(((UiPaint)Tone.TextMuted).WithOpacity(0.7f), opacity);
        List.Quad(new UiQuad(thumb, ink, MathF.Round(w / 2), default, 0, UiFade.None, 0), e);
    }

    /// <summary>
    /// The rows of a virtual list in view at <paramref name="offsetDu"/>: last
    /// frame's view height (the screen's on the first frame), one row of margin
    /// either side so easing never shows a gap.
    /// </summary>
    internal (int First, int Last) VisibleRows(uint region, float offsetDu, int count, float rowHeightDu, bool follow)
    {
        ref var st = ref State<ScrollState>(region);
        float viewDu = st.ViewPx > 0f ? st.ViewPx / S : Screen.H / MathF.Max(S, 0.0001f);
        if (follow && (st.AtEnd || !st.Seen))
            return (Math.Max(0, count - (int)MathF.Ceiling(viewDu / rowHeightDu) - 1), count);
        // The target too: a jump (End) lands with its rows already there.
        float top = MathF.Min(offsetDu, st.Target), bottom = MathF.Max(offsetDu, st.Target) + viewDu;
        int first = Math.Clamp((int)MathF.Floor(top / rowHeightDu) - 1, 0, count);
        int last = Math.Clamp((int)MathF.Ceiling(bottom / rowHeightDu) + 1, first, count);
        return (first, last);
    }
}
