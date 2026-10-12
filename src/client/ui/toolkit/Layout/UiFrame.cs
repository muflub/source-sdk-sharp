using System.Runtime.CompilerServices;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>What a pointer did to an element this frame.</summary>
public readonly record struct UiInteraction(bool Hovered, bool Pressed, bool Clicked);

/// <summary>
/// The builder a screen describes itself into, once a frame (plan_client_ui §2).
/// Immediate mode: there are no widget objects to keep in sync; a screen's state
/// is its own fields, or <see cref="State{T}"/> by key.
///
/// <para>
/// Containers (<see cref="Box"/>, <see cref="Column"/>, <see cref="Row"/>,
/// <see cref="Grid"/>, <see cref="Scroll"/>, <see cref="Overlay"/>) open when
/// made and close when disposed: write them in a <c>using</c>. Leaves
/// (<see cref="Text(string, Toolkit.Text, Tone)"/>, <see cref="Shape"/>, ...)
/// do not open. Every call answers a <see cref="UiBox"/> to place and paint the
/// element with. Sizes and offsets are design units and tokens; there is no
/// pixel anywhere above the render list.
/// </para>
/// </summary>
public ref struct UiFrame
{
    internal readonly UiContext Context;

    internal UiFrame(UiContext context) => Context = context;

    public UiTheme Theme => Context.Theme;

    public ScreenInfo Screen => Context.Screen;

    /// <summary>The screen's width in design units (W / Scale): what layouts choose by.</summary>
    /// <remarks>Inside a window, the window's: layout inside a window responds to the window.</remarks>
    public float DesignWidth => Context.DesignWidth;

    /// <summary>The screen's (or the window's) height in design units.</summary>
    public float DesignHeight => Context.DesignHeight;

    /// <summary>
    /// Whether the design width is under the narrow breakpoint: use the narrow
    /// arrangement. Inside a window, the window's width against the window's
    /// breakpoint; elsewhere the screen's against the theme's.
    /// </summary>
    /// <remarks>
    /// Exactly at the breakpoint is not narrow (half a du of slack for float
    /// error): 1024x768 is 1440 du wide, and the regular arrangements, which
    /// need about 1300 du, fit it.
    /// </remarks>
    public bool Narrow => DesignWidth < Context.NarrowBelowDu - 0.5f;

    /// <summary>The time this frame is drawn for, in the layer's clock (seconds).</summary>
    public double Now => Context.Now;

    public float Dt => Context.Dt;

    /// <summary>Whether element names are being recorded: build a costly name only when this is true.</summary>
    public bool Naming => Context.RecordNames;

    // ---- containers ----

    /// <summary>A container whose children are each placed by anchor and offset (HUD corners, overlays of layers).</summary>
    public UiBox Box(string? key = null)
    {
        var box = new UiBox(Context, Context.Open(NodeKind.Box));
        return key is null ? box : box.Key(key);
    }

    /// <summary>A vertical stack: children top to bottom, <paramref name="gap"/> apart.</summary>
    public UiBox Column(UiLength gap = default, Align cross = Align.Stretch) => Stack(Axis.Vertical, gap, cross);

    /// <summary>A horizontal stack: children left to right, <paramref name="gap"/> apart.</summary>
    public UiBox Row(UiLength gap = default, Align cross = Align.Centre) => Stack(Axis.Horizontal, gap, cross);

    public UiBox Stack(Axis axis, UiLength gap, Align cross)
    {
        int i = Context.Open(NodeKind.Stack);
        ref var n = ref Context.Node(i);
        n.Axis = axis;
        n.Gap = gap.Du;
        n.CrossAlign = cross;
        if (gap.IsRaw) n.Flags |= UiElementFlags.Raw;
        return new UiBox(Context, i);
    }

    /// <summary>
    /// A grid: children fill <paramref name="columns"/> left to right, then the
    /// next row. Each cell takes its column's width and its row's height.
    /// </summary>
    public UiBox Grid(scoped ReadOnlySpan<UiSize> columns, UiLength gap = default, UiLength rowGap = default)
    {
        int i = Context.Open(NodeKind.Grid);
        var (start, count) = Context.AddColumns(columns);
        ref var n = ref Context.Node(i);
        n.ColumnStart = start;
        n.ColumnCount = count;
        n.Gap = gap.Du;
        n.RowGap = rowGap.Du;
        if (gap.IsRaw || rowGap.IsRaw) n.Flags |= UiElementFlags.Raw;
        return new UiBox(Context, i);
    }

    /// <summary>
    /// A clipped list that scrolls: with the wheel (the theme's step, eased),
    /// its scrollbar, and keys when focused. Its offset is kept by
    /// <paramref name="key"/>. Give it a size. <paramref name="axes"/> picks
    /// vertical (a column), horizontal (a row) or both (a column whose content
    /// may be wider than the view: a wide table).
    /// </summary>
    public UiBox Scroll(string key, UiLength gap = default, ScrollAxes axes = ScrollAxes.Vertical, bool followEnd = false)
    {
        var box = Stack(axes == ScrollAxes.Horizontal ? Axis.Horizontal : Axis.Vertical, gap,
                        axes == ScrollAxes.Vertical ? Align.Stretch : Align.Start).Key(key);
        ref var n = ref Context.Node(box.Index);
        n.Clip = true;
        n.Scroll = true;
        n.ScrollAxes = axes;
        n.Follow = followEnd;
        Context.UpdateScroll(ref n);
        return box;
    }

    /// <summary>
    /// A long list (a server browser): <paramref name="count"/> rows of
    /// <paramref name="rowHeightDu"/>, each described by <paramref name="row"/>
    /// with its index as props, and only the rows in view are described and
    /// laid out (a 10,000-row list costs what its visible dozen does). A scroll
    /// region; give it a size. Already closed. With <paramref name="followEnd"/>
    /// (a log) it starts at the end and stays there while it is there.
    /// </summary>
    public UiBox VirtualList(string key, int count, float rowHeightDu, UiPanel<int> row, bool followEnd = false)
    {
        var box = Scroll(key, followEnd: followEnd);
        ref var n = ref Context.Node(box.Index);
        var (first, last) = Context.VisibleRows(n.Id, n.ScrollOffset, count, rowHeightDu, followEnd);

        // Only the rows in view are described; spacers stand for the rest, so
        // the region's content (and its thumb, its End) is the whole list.
        Spacer(Toolkit.Size.Fill, Toolkit.Size.Du(first * rowHeightDu));
        for (int i = first; i < last; i++)
            Sub(i, row, i).Height(Toolkit.Size.Du(rowHeightDu));
        Spacer(Toolkit.Size.Fill, Toolkit.Size.Du((count - last) * rowHeightDu));
        box.Dispose();
        return box;
    }

    /// <summary>
    /// A layer above the whole screen, outside every parent's clip: dropdowns,
    /// tooltips, menus, dialogs. Drawn after the screen and hit first. A modal one
    /// dims the screen and takes every click, so nothing under it is hit.
    /// Fills the safe area; place children in it by anchor, or anchor the overlay
    /// itself to an element with <see cref="UiBox.AnchorTo"/>.
    /// </summary>
    public UiBox Overlay(string key, bool modal = false) => new(Context, Context.OpenOverlay(key, modal));

    /// <summary>
    /// A named extension point: every <see cref="IUiSlotContributor"/> that
    /// answers for <paramref name="name"/> describes into it here, in listing
    /// order. A vertical stack; place it like any element. Already closed.
    /// </summary>
    public UiBox Slot(string name, UiLength gap = default)
    {
        var slot = Stack(Axis.Vertical, gap, Align.Start).Key(name);
        Context.PushScope(name);
        Context.FillSlot(ref this, name);
        Context.PopScope();
        slot.Dispose();
        return slot;
    }

    /// <summary>
    /// Embeds a sub-panel under its own id scope, so two instances never share
    /// hover, focus, scroll or animation state. In a <see cref="Box"/> the
    /// sub-panel's root fills the parent (so it can anchor to corners); in a
    /// stack or grid it takes its natural size.
    /// </summary>
    public UiBox Sub<TProps>(string key, UiPanel<TProps> panel, in TProps props)
    {
        int i = OpenSub(panel);
        Context.SetKey(i, key);
        Context.PushScope(key);
        return DescribeSub(i, panel, props);
    }

    /// <summary>The same, keyed by a number (a row, a slot index).</summary>
    public UiBox Sub<TProps>(int key, UiPanel<TProps> panel, in TProps props)
    {
        int i = OpenSub(panel);
        Context.SetKey(i, key);
        Context.PushScope(key);
        return DescribeSub(i, panel, props);
    }

    /// <summary>Embeds a sub-panel that takes no per-call props, under its own id scope.</summary>
    public UiBox Sub(string key, UiPanel panel)
    {
        int i = OpenSub(panel);
        Context.SetKey(i, key);
        Context.PushScope(key);
        if (Context.RecordNames)
            Context.Name(i, $"{panel.GetType().Name}[{key}]");
        panel.Describe(ref this);
        Context.PopScope();
        if (Context.Current != i)
            throw new InvalidOperationException(
                $"{panel.GetType().Name} left {Context.Describe(Context.Current)} open; close each container with `using`");
        Context.Close(i);
        return new UiBox(Context, i);
    }

    private int OpenSub(object panel)
    {
        bool inBox = Context.Node(Context.Current).Kind == NodeKind.Box;
        int i = Context.Open(NodeKind.Box);
        ref var n = ref Context.Node(i);
        if (inBox)
        {
            n.W = Size.Fill;
            n.H = Size.Fill;
            n.Flags |= UiElementFlags.Overlaps;
        }
        return i;
    }

    private UiBox DescribeSub<TProps>(int i, UiPanel<TProps> panel, in TProps props)
    {
        if (Context.RecordNames)
            Context.Name(i, $"{panel.GetType().Name}[{Context.Describe(i)}]");
        panel.Describe(ref this, props);
        Context.PopScope();
        if (Context.Current != i)
            throw new InvalidOperationException(
                $"{panel.GetType().Name} left {Context.Describe(Context.Current)} open; close each container with `using`");
        Context.Close(i);
        return new UiBox(Context, i);
    }

    internal void Describe(IUiSlotContributor contributor, string slot) => contributor.Describe(ref this, slot);

    // ---- leaves ----

    /// <summary>
    /// User-facing text: a <c>#Token</c>, resolved through the localiser. Literal
    /// text is reported by the lint (rule <c>localise</c>); game data such as a
    /// player name goes through <see cref="Data"/>, numbers through the
    /// interpolated overload.
    /// </summary>
    public UiBox Text(string token, Text role = Toolkit.Text.Body, Tone tone = Tone.Text)
    {
        string text = Context.Localise(token, out bool found);
        var box = Leaf(role, tone);
        ref var n = ref Context.Node(box.Index);
        n.TextStart = Context.CharCount;
        Context.AppendChars(text, Theme.Type(role).Upper);
        n.TextLength = Context.CharCount - n.TextStart;
        if (!found)
            n.Flags |= UiElementFlags.Unlocalised;
        return box;
    }

    /// <summary>
    /// Text built from values without allocating: <c>ui.Text($"{clip} / {reserve}")</c>.
    /// Counts as data (numbers and names), not as user-facing copy. A
    /// compile-time <c>#</c> in the literal is a token stranded inside the
    /// interpolation — it never reaches the localiser, so the lint's
    /// <c>localise</c> rule reports it.
    /// </summary>
    public UiBox Text([InterpolatedStringHandlerArgument("")] scoped ref UiTextHandler text, Text role = Toolkit.Text.Body,
                      Tone tone = Tone.Text)
    {
        var box = Leaf(role, tone);
        ref var n = ref Context.Node(box.Index);
        n.TextStart = text.Start;
        n.TextLength = Context.CharCount - text.Start;
        n.IsData = true;
        if (text.HashInLiteral)
            n.Flags |= UiElementFlags.StrandedToken;
        if (Theme.Type(role).Upper)
            box.ApplyUpper();
        return box;
    }

    /// <summary>
    /// Game data shown as is: a player's name, a server's, a map's. Never
    /// localised, and never upper-cased, even in an upper-case role (Title,
    /// Heading, Label): "xXx_Name" is the player's spelling.
    /// </summary>
    public UiBox Data(string value, Text role = Toolkit.Text.Body, Tone tone = Tone.Text)
    {
        var box = Leaf(role, tone);
        ref var n = ref Context.Node(box.Index);
        n.TextStart = Context.CharCount;
        Context.AppendChars(value, false);
        n.TextLength = Context.CharCount - n.TextStart;
        n.IsData = true;
        n.KeepCase = true;
        return box;
    }

    /// <summary>An integer, formatted without allocating.</summary>
    public UiBox Number(int value, Text role = Toolkit.Text.Number, Tone tone = Tone.Text)
    {
        var box = Leaf(role, tone);
        ref var n = ref Context.Node(box.Index);
        n.TextStart = Context.CharCount;
        Context.EnsureChars(11);
        value.TryFormat(Context.CharsFree, out int written, default, System.Globalization.CultureInfo.InvariantCulture);
        Context.Advance(written);
        n.TextLength = written;
        n.IsData = true;
        return box;
    }

    private UiBox Leaf(Text role, Tone tone)
    {
        int i = Context.Add(NodeKind.Text);
        ref var n = ref Context.Node(i);
        n.Role = role;
        n.TextPaint = tone;
        return new UiBox(Context, i);
    }

    /// <summary>
    /// An icon of the icon set (src/client/ui/icons) by name, <paramref name="heightDu"/>
    /// tall and as wide as its art; an unknown name draws the set's "generic".
    /// </summary>
    public UiBox Icon(string name, UiPaint paint, float heightDu)
    {
        var icons = Context.Icons;
        int index = icons.IndexOf(name);
        if (index < 0)
            index = icons.IndexOf("generic");
        int i = Context.Add(NodeKind.Shape);
        ref var n = ref Context.Node(i);
        n.Shape = UiShape.Art;
        n.TextPaint = paint;
        n.ShapeValue = index;
        n.W = Size.Du(MathF.Round(heightDu * icons.Icon(index).Aspect));
        n.H = Size.Du(heightDu);
        return new UiBox(Context, i);
    }

    /// <summary>A vector icon, <paramref name="sizeDu"/> square.</summary>
    public UiBox Shape(UiShape shape, UiPaint paint, float sizeDu, float value = 0f)
    {
        int i = Context.Add(NodeKind.Shape);
        ref var n = ref Context.Node(i);
        n.Shape = shape;
        n.TextPaint = paint;
        n.ShapeValue = value;
        n.W = Size.Du(sizeDu);
        n.H = Size.Du(sizeDu);
        return new UiBox(Context, i);
    }

    /// <summary>A filled rectangle with no children: a bar, a stripe, a divider.</summary>
    public UiBox Rect(UiPaint fill, UiSize width, UiSize height)
    {
        int i = Context.Add(NodeKind.Spacer);
        ref var n = ref Context.Node(i);
        n.Fill = fill;
        n.W = width;
        n.H = height;
        return new UiBox(Context, i);
    }

    /// <summary>
    /// A texture drawn into a rectangle: the engine's own 3D renders (a weapon
    /// inspect, a vendor's portrait), named by the backend's texture id. A leaf
    /// like <see cref="Rect"/>: give it a size. The tint is white and the uv
    /// the whole surface; the pixels are the engine's data, so this declares a
    /// surface rather than paints one - it fills nothing and writes no text,
    /// so the lint's raw/contrast rules have nothing to judge.
    /// </summary>
    public UiBox Texture(int textureHandle, UiSize width, UiSize height)
        => Texture(textureHandle, width, height, new UiRect(0, 0, 1, 1));

    /// <summary>
    /// <see cref="Texture(int, UiSize, UiSize)"/> sampling only <paramref name="uv"/>
    /// of the surface (0..1 of its width and height): a second camera's view,
    /// drawn into the top left of a larger render target.
    /// </summary>
    public UiBox Texture(int textureHandle, UiSize width, UiSize height, UiRect uv)
    {
        int i = Context.Add(NodeKind.Texture);
        ref var n = ref Context.Node(i);
        n.Texture = textureHandle;
        n.TextureUv = uv;
        n.W = width;
        n.H = height;
        return new UiBox(Context, i);
    }

    /// <summary>Empty space in a stack.</summary>
    public UiBox Spacer(UiSize width, UiSize height)
    {
        int i = Context.Add(NodeKind.Spacer);
        ref var n = ref Context.Node(i);
        n.W = width;
        n.H = height;
        return new UiBox(Context, i);
    }

    /// <summary>Takes the rest of a stack's main axis: pushes what follows to the far end.</summary>
    public UiBox Flex() => Spacer(Size.Fill, Size.Fill);

    // ---- interaction and state ----

    /// <summary>
    /// Makes <paramref name="box"/> a click target and answers what the pointer
    /// did to it (as of last frame's layout). Key the box first if two could share
    /// a place in the tree.
    /// </summary>
    public UiInteraction Interact(UiBox box)
    {
        ref var n = ref Context.Node(box.Index);
        n.Flags |= UiElementFlags.Interactive;
        uint id = n.Id;
        return new UiInteraction(Context.Hovered == id, Context.IsPressed(id), Context.WasClicked(id));
    }

    /// <summary>Eases a value towards <paramref name="target"/> with the theme's curve for <paramref name="motion"/>.</summary>
    public float Animate(string key, float target, Motion motion = Motion.Standard)
        => Context.Animate(Context.IdOf(key), target, motion);

    /// <summary>Eases a value kept against <paramref name="box"/>'s id (a hover fade on a button): no key string to build.</summary>
    public float Animate(UiBox box, float target, Motion motion = Motion.Standard)
        => Context.Animate(Context.Node(box.Index).Id ^ 0xA5A5A5A5u, target, motion);

    /// <summary>
    /// State of type <typeparamref name="T"/> kept by the layer under
    /// <paramref name="key"/> in the current scope: for lists of anonymous
    /// sub-panel instances. A panel the parent owns keeps state in its fields.
    /// </summary>
    public ref T State<T>(string key) where T : struct => ref Context.State<T>(Context.IdOf(key));

    /// <summary>Whether <paramref name="box"/> (an interactive element) has the keyboard focus.</summary>
    public bool IsFocused(UiBox box) => Context.Focused != 0 && Context.Focused == Context.Node(box.Index).Id;

    /// <summary>The characters typed since the last frame: a focused text field takes them.</summary>
    public ReadOnlySpan<char> Typed => Context.Typed;

    /// <summary>The mouse, in pixels.</summary>
    public float MouseX => Context.MouseX;

    /// <summary>Where <paramref name="box"/> was laid out last frame, in pixels: what a press now was aimed at.</summary>
    public bool TryLastRect(UiBox box, out UiRect rect) => Context.TryLastRect(Context.Node(box.Index).Id, out rect);

    /// <summary>Pixels per design unit on this screen: what turns an engine's pixel position into a place.</summary>
    public float PixelScale => MathF.Max(Context.Screen.Scale, 0.0001f);

    /// <summary>The icon set: its names, for a sheet or a picker.</summary>
    public UiIconSet Icons => Context.Icons;

    /// <summary>The frame's number: a screen not described last frame has just been opened.</summary>
    public long FrameNumber => Context.FrameNumber;

    /// <summary>Whether key <paramref name="code"/> went down since the last frame.</summary>
    public bool KeyPressed(int code) => Context.KeyPressed(code);

    /// <summary>The same, and no one else this frame sees it.</summary>
    public bool ConsumeKey(int code) => Context.ConsumeKey(code);

    /// <summary>Every key and mouse button that went down this frame (ButtonCode_t), the wheel and Tab included.</summary>
    public ReadOnlySpan<int> ButtonsPressed => Context.Buttons;

    /// <summary>A mouse button is held on some target.</summary>
    public bool Holding => Context.Holding;
}

/// <summary>
/// Formats an interpolated string straight into the frame's text arena: the
/// values' own <see cref="ISpanFormattable"/> writes, no string made.
/// </summary>
[InterpolatedStringHandler]
public ref struct UiTextHandler
{
    private readonly UiContext _ctx;

    /// <summary>Set when a compile-time literal carried '#': a token written
    /// inside an interpolation never reaches the localiser (the compare row
    /// once drew "#Descent_Tip_Compare" verbatim), so the lint flags it.</summary>
    internal bool HashInLiteral;

    internal readonly int Start;

    public UiTextHandler(int literalLength, int formattedCount, scoped UiFrame frame)
    {
        _ctx = frame.Context;
        Start = _ctx.CharCount;
        _ctx.EnsureChars(literalLength + formattedCount * 11);
    }

    public void AppendLiteral(string value)
    {
        HashInLiteral |= value.Contains('#');
        _ctx.AppendChars(value, false);
    }

    public void AppendFormatted(string? value) => _ctx.AppendChars(value, false);

    public void AppendFormatted(ReadOnlySpan<char> value) => _ctx.AppendChars(value, false);

    // The common numbers have their own overloads: the generic path's
    // `value is ISpanFormattable` boxes in unoptimised (Debug) code, and the
    // allocation fact runs in Debug.
    public void AppendFormatted(int value) => Format(value);

    public void AppendFormatted(int value, string? format)
    {
        _ctx.EnsureChars(16);
        value.TryFormat(_ctx.CharsFree, out int n, format, System.Globalization.CultureInfo.InvariantCulture);
        _ctx.Advance(n);
    }

    public void AppendFormatted(long value) => Format(value);

    public void AppendFormatted(float value) => Format(value);

    public void AppendFormatted(double value) => Format(value);

    private void Format(int value)
    {
        _ctx.EnsureChars(11);
        value.TryFormat(_ctx.CharsFree, out int n, default, System.Globalization.CultureInfo.InvariantCulture);
        _ctx.Advance(n);
    }

    private void Format(long value)
    {
        _ctx.EnsureChars(20);
        value.TryFormat(_ctx.CharsFree, out int n, default, System.Globalization.CultureInfo.InvariantCulture);
        _ctx.Advance(n);
    }

    private void Format(double value)
    {
        _ctx.EnsureChars(32);
        value.TryFormat(_ctx.CharsFree, out int n, "0.##", System.Globalization.CultureInfo.InvariantCulture);
        _ctx.Advance(n);
    }

    public void AppendFormatted<T>(T value) => AppendFormatted(value, null);

    public void AppendFormatted<T>(T value, string? format)
    {
        if (value is ISpanFormattable)
        {
            while (true)
            {
                if (((ISpanFormattable)value).TryFormat(_ctx.CharsFree, out int written, format,
                                                        System.Globalization.CultureInfo.InvariantCulture))
                {
                    _ctx.Advance(written);
                    return;
                }
                _ctx.EnsureChars(Math.Max(32, _ctx.CharsFree.Length * 2));
            }
        }

        _ctx.AppendChars(value?.ToString(), false);
    }
}
