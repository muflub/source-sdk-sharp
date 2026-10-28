using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// A handle on one element of the frame being described: place it, size it,
/// paint it. Every setter answers the handle, so they chain. A container's
/// handle closes it when disposed (<c>using (ui.Column(...)) { ... }</c>); a
/// leaf's Dispose does nothing.
/// </summary>
public readonly struct UiBox : IDisposable
{
    private readonly UiContext _ctx;

    internal readonly int Index;

    internal UiBox(UiContext ctx, int index)
    {
        _ctx = ctx;
        Index = index;
    }

    private ref UiNode N => ref _ctx.Node(Index);

    public void Dispose()
    {
        if (_ctx.IsOpen(Index))
            _ctx.Close(Index);
    }

    // ---- placement ----

    /// <summary>Places the element at the same point of itself and its parent: TopLeft to TopLeft, Centre to Centre.</summary>
    public UiBox At(UiAnchor anchor)
    {
        N.ParentAnchor = anchor;
        N.SelfAnchor = anchor;
        return this;
    }

    /// <summary>Places the element's <paramref name="self"/> point on its parent's <paramref name="parent"/> point.</summary>
    public UiBox At(UiAnchor parent, UiAnchor self)
    {
        N.ParentAnchor = parent;
        N.SelfAnchor = self;
        return this;
    }

    /// <summary>Moves the element in from its anchor: positive is always towards the parent's middle.</summary>
    public UiBox Inset(UiLength x, UiLength y)
    {
        N.OffX = x.Du;
        N.OffY = y.Du;
        if (x.IsRaw || y.IsRaw) N.Flags |= UiElementFlags.Raw;
        return this;
    }

    /// <summary>
    /// Places the element where the GAME says, as a fraction (0..1) of the space
    /// its parent has left around it; -1 centres on that axis. For text a map
    /// positions (game_text, env_message); a screen's own layout uses anchors.
    /// </summary>
    public UiBox PlaceFraction(float x, float y)
    {
        N.PlaceX = x;
        N.PlaceY = y;
        return this;
    }

    /// <summary>
    /// Places the element at a fixed coordinate of the game's HUD art, not a
    /// design spacing — the same escape
    /// <see cref="UiPaint.FromGame"/> gives game colours, so the raw rule
    /// does not list it. <paramref name="corner"/> is a corner and is also
    /// the element's own anchor: <paramref name="left"/> is from the edge
    /// named by the corner's x (positive moves inward, negative outward past
    /// the edge) and <paramref name="bottom"/> from the bottom (positive up).
    /// Stock .res values go through <see cref="StockRes"/>: vgui's "r" ypos
    /// reaches the element's TOP edge, so a resolved spot feeds these
    /// offsets directly.
    /// </summary>
    public UiBox AtRes(UiAnchor corner, float left, float bottom)
    {
        N.ParentAnchor = corner;
        N.SelfAnchor = corner;
        N.OffX = left;
        N.OffY = bottom;
        return this;
    }

    /// <summary>The same distance in on both axes.</summary>
    public UiBox Inset(UiLength both) => Inset(both, both);

    /// <summary>
    /// For an overlay: places it with its <paramref name="self"/> point on
    /// <paramref name="target"/>'s <paramref name="point"/> (a dropdown's top left
    /// on its field's bottom left), kept on screen.
    /// </summary>
    public UiBox AnchorTo(UiBox target, UiAnchor point, UiAnchor self)
    {
        N.AnchorTarget = target.Index;
        N.TargetAnchor = point;
        N.SelfAnchor = self;
        N.W = default;
        N.H = default;
        return this;
    }

    public UiBox Width(UiSize width)
    {
        N.W = width;
        return this;
    }

    public UiBox Height(UiSize height)
    {
        N.H = height;
        return this;
    }

    public UiBox Size(UiSize width, UiSize height)
    {
        N.W = width;
        N.H = height;
        return this;
    }

    public UiBox MinWidth(float du)
    {
        N.MinW = du;
        return this;
    }

    public UiBox MaxWidth(float du)
    {
        N.MaxW = du;
        return this;
    }

    // ---- as a container ----

    public UiBox Pad(UiLength all) => Pad(all, all);

    public UiBox Pad(UiLength x, UiLength y) => Pad(x, y, x, y);

    public UiBox Pad(UiLength left, UiLength top, UiLength right, UiLength bottom)
    {
        ref var n = ref N;
        n.PadL = left.Du;
        n.PadT = top.Du;
        n.PadR = right.Du;
        n.PadB = bottom.Du;
        if (left.IsRaw || top.IsRaw || right.IsRaw || bottom.IsRaw) n.Flags |= UiElementFlags.Raw;
        return this;
    }

    /// <summary>How a stack places its children along (<paramref name="main"/>) and across (<paramref name="cross"/>) its axis.</summary>
    public UiBox Align(Align main, Align cross)
    {
        N.MainAlign = main;
        N.CrossAlign = cross;
        return this;
    }

    /// <summary>Clips the children to this element's rect (intersected with every ancestor's clip).</summary>
    public UiBox Clip()
    {
        N.Clip = true;
        return this;
    }

    // ---- paint ----

    public UiBox Fill(UiPaint paint)
    {
        N.Fill = paint;
        return this;
    }

    public UiBox Border(UiPaint paint, float du = 1f)
    {
        N.Border = paint;
        N.BorderDu = du;
        return this;
    }

    public UiBox Round(Radius radius)
    {
        N.RadiusDu = (float)radius;
        return this;
    }

    /// <summary>
    /// Fades the fill across the element: <paramref name="start"/> of its alpha at
    /// the top (left) edge to <paramref name="end"/> at the bottom (right).
    /// </summary>
    public UiBox Fade(UiFade direction, float end = 0f, float start = 1f)
    {
        N.Fade = direction;
        N.FadeStart = start;
        N.FadeEnd = end;
        return this;
    }

    public UiBox Raise(Elevation elevation)
    {
        N.Elevation = elevation;
        return this;
    }

    /// <summary>Multiplies this element's and its children's alpha.</summary>
    public UiBox Opacity(float opacity)
    {
        N.Opacity = opacity;
        return this;
    }

    // ---- text and shapes ----

    /// <summary>The text's or shape's colour.</summary>
    public UiBox Color(UiPaint paint)
    {
        N.TextPaint = paint;
        return this;
    }

    /// <summary>The text's type role.</summary>
    public UiBox Style(Text role)
    {
        N.Role = role;
        if (_ctx.Theme.Type(role).Upper)
            ApplyUpper();
        return this;
    }

    internal void ApplyUpper()
    {
        ref var n = ref N;
        if (n.KeepCase)
            return;
        var span = _ctx.TextSpan(n.TextStart, n.TextLength);
        for (int i = 0; i < span.Length; i++)
            span[i] = char.ToUpperInvariant(span[i]);
    }

    /// <summary>Where the text sits in its box, horizontally.</summary>
    public UiBox TextAlign(Align align)
    {
        N.TextAlign = align;
        return this;
    }

    /// <summary>Cuts text that does not fit with an ellipsis, instead of letting it overflow (which the lint reports).</summary>
    public UiBox Elide()
    {
        N.Elide = true;
        return this;
    }

    /// <summary>Breaks the text into lines no wider than <paramref name="widthDu"/> (or its box, if narrower).</summary>
    public UiBox Wrap(float widthDu)
    {
        N.WrapDu = widthDu;
        return this;
    }

    /// <summary>
    /// Draws the text as an edited line: a caret at <paramref name="caret"/>
    /// (a char index) and a selection of <paramref name="selLength"/> chars from
    /// <paramref name="selStart"/>. Put it in a clipping box (ui.TextField does).
    /// </summary>
    public UiBox Caret(int caret, int selStart = 0, int selLength = 0)
    {
        N.Caret = caret;
        N.SelStart = selStart;
        N.SelLength = selLength;
        return this;
    }

    // ---- identity ----

    /// <summary>A stable id from <paramref name="key"/> in the current scope: what hover and state follow.</summary>
    public UiBox Key(string key)
    {
        _ctx.SetKey(Index, key);
        return this;
    }

    public UiBox Key(int key)
    {
        _ctx.SetKey(Index, key);
        return this;
    }

    /// <summary>Names the element for the lint and the inspector (only recorded when names are).</summary>
    public UiBox Name(string name)
    {
        if (_ctx.RecordNames)
            _ctx.Name(Index, name);
        return this;
    }

    /// <summary>Allowed to overlap its siblings: a badge over a corner, a glow behind a number.</summary>
    public UiBox Layered()
    {
        N.Flags |= UiElementFlags.Overlaps;
        return this;
    }
}
