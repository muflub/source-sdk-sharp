using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// A window (plan_client_ui §5.7a, "Windows"): a movable, resizable frame over
/// the screen, as stock's vgui Frames are: a title bar (drag to move) with a
/// close button, resize from every edge and corner (a grip at the bottom
/// right), min and max sizes, raise on click, Esc closes the window on top.
/// The pointer shape for each handle goes to the render list. Placement is kept
/// by <see cref="UiWindows"/>; layout inside the window responds to the
/// window's size (ui.DesignWidth and ui.Narrow inside it are the window's).
/// </summary>
public static class UiWindowWidget
{
    /// <summary>The frame's handle ring, the corners' handles, the title bar, du.</summary>
    public const float Ring = 6, Corner = 16, TitleBar = 40;

    /// <summary>How much of the title bar stays on screen, du, however far a window is dragged.</summary>
    public const float KeepVisible = 96;

    /// <summary>
    /// Opens a window and answers its body (an open column): describe the content
    /// into it and close it with <c>using</c>, which closes the window.
    /// </summary>
    public static UiBox Window(this ref UiFrame ui, UiWindows windows, string id, string titleToken, in UiWindowSpec spec)
    {
        var ctx = ui.Context;
        windows.BeginFrame(ctx);
        float s = ctx.Screen.Scale;
        float screenW = UiScale.DesignWidth(ctx.Screen), screenH = UiScale.DesignHeight(ctx.Screen);
        ref var p = ref windows.Place(id, spec, screenW, screenH, out _);

        ctx.PushScope(id);
        Drag(ref ui, windows, id, ref p, spec, screenW, screenH);

        // The window on top: Esc closes it.
        bool top = windows.Top == id;
        if (top && ui.ConsumeKey((int)UiKey.Escape))
            windows.RequestClose(id);

        ui.Overlay(id);
        ctx.SetLastRootZ(2 * (1 + windows.Rank(id)));   // even: a window's own popups go at z + 1
        var frame = ui.Box().At(UiAnchor.TopLeft).Inset(new UiLength(p.X, false), new UiLength(p.Y, false))
                      .Size(Size.Du(p.W), Size.Du(p.H)).Fill(Tone.Window).Round(Radius.L).Raise(Elevation.Overlay)
                      .Border(top ? ((UiPaint)Tone.Accent).WithOpacity(0.6f) : Tone.Line, 1).Key("wnd-frame");
        ctx.Node(frame.Index).Movable = true;
        windows.Seen(id, ctx.FrameNumber, new UiRect(ctx.Screen.Safe.Left + p.X * s, ctx.Screen.Safe.Top + p.Y * s, p.W * s, p.H * s));

        // The handles, in the ring the content does not reach: edges first, corners over them.
        Handle(ref ui, "wnd-left", UiAnchor.Left, Size.Du(Ring), Size.Fill, UiCursor.ResizeEW);
        Handle(ref ui, "wnd-right", UiAnchor.Right, Size.Du(Ring), Size.Fill, UiCursor.ResizeEW);
        Handle(ref ui, "wnd-top", UiAnchor.Top, Size.Fill, Size.Du(Ring), UiCursor.ResizeNS);
        Handle(ref ui, "wnd-bottom", UiAnchor.Bottom, Size.Fill, Size.Du(Ring), UiCursor.ResizeNS);
        Handle(ref ui, "wnd-tl", UiAnchor.TopLeft, Size.Du(Corner), Size.Du(Corner), UiCursor.ResizeNWSE);
        Handle(ref ui, "wnd-tr", UiAnchor.TopRight, Size.Du(Corner), Size.Du(Corner), UiCursor.ResizeNESW);
        Handle(ref ui, "wnd-bl", UiAnchor.BottomLeft, Size.Du(Corner), Size.Du(Corner), UiCursor.ResizeNESW);
        var grip = Handle(ref ui, "wnd-br", UiAnchor.BottomRight, Size.Du(Corner), Size.Du(Corner), UiCursor.ResizeNWSE);
        _ = grip;
        ui.Shape(UiShape.Grip, Tone.TextMuted, 12).At(UiAnchor.BottomRight).Inset(Space.XS).Layered();

        var column = ui.Column(Space.None, Align.Stretch).Size(Size.Fill, Size.Fill).Pad(new UiLength(Ring, false)).Layered();

        // The title bar: drag to move; the close button. Kept clear of the top
        // corners' handles, so they resize rather than move.
        using var barLine = ui.Row(Space.None).Width(Size.Fill).Pad(new UiLength(Corner - Ring, false), Space.None);
        var bar = ui.Row(Space.M).Key("wnd-title").Size(Size.Fill, Size.Du(TitleBar)).Fill(Tone.SurfaceRaised)
                    .Round(Radius.M).Pad(Space.M, Space.None, Space.XS, Space.None);
        var barHit = ui.Interact(bar);
        ctx.Node(bar.Index).Handle = true;
        if (barHit.Hovered || barHit.Pressed)
            ctx.Cursor = UiCursor.Move;
        using (bar)
        {
            ui.Rect(top ? Tone.Accent : Tone.TextMuted, Size.Du(4), Size.Du(20)).Round(Radius.S);
            ui.Text(titleToken, Text.Heading, top ? Tone.Text : Tone.TextMuted);
            ui.Flex();
            var close = ui.Box("wnd-close").Size(Size.Du(32), Size.Du(32)).Round(Radius.M);
            var closeHit = ui.Interact(close);
            if (closeHit.Clicked)
                windows.RequestClose(id);
            using (close.Fill(closeHit.Hovered ? ((UiPaint)Tone.Danger).WithOpacity(0.35f) : Tone.None))
                ui.Shape(UiShape.Cross45, closeHit.Hovered ? Tone.Text : Tone.TextMuted, 12).At(UiAnchor.Centre);
        }
        barLine.Dispose();

        // The body: the caller's; its layout answers to the window's size.
        var body = ui.Column(Space.L, Align.Stretch).Size(Size.Fill, Size.Fill).Pad(Space.L).Clip();
        ctx.PushDesign(body.Index, p.W, p.H - TitleBar, spec.NarrowBelowDu);   // the window's size: what its content chooses by
        ctx.Node(body.Index).ClosesOuter = 3;   // the column, the frame, the overlay
        _ = column;
        _ = frame;
        ctx.PopScope();
        return body;
    }

    private static UiBox Handle(ref UiFrame ui, string key, UiAnchor at, UiSize w, UiSize h, UiCursor cursor)
    {
        var box = ui.Rect(Tone.None, w, h).At(at).Key(key).Layered();
        ui.Context.Node(box.Index).Handle = true;
        var hit = ui.Interact(box);
        if (hit.Hovered || hit.Pressed)
            ui.Context.Cursor = cursor;
        return box;
    }

    /// <summary>Which handle is held (as of the press that started it).</summary>
    private static UiWindowHandle Held(UiContext ctx)
    {
        uint a = ctx.ActiveBefore;
        if (a == 0) return UiWindowHandle.None;
        if (a == ctx.IdOf("wnd-title")) return UiWindowHandle.Move;
        if (a == ctx.IdOf("wnd-left")) return UiWindowHandle.Left;
        if (a == ctx.IdOf("wnd-right")) return UiWindowHandle.Right;
        if (a == ctx.IdOf("wnd-top")) return UiWindowHandle.Top;
        if (a == ctx.IdOf("wnd-bottom")) return UiWindowHandle.Bottom;
        if (a == ctx.IdOf("wnd-tl")) return UiWindowHandle.Top | UiWindowHandle.Left;
        if (a == ctx.IdOf("wnd-tr")) return UiWindowHandle.Top | UiWindowHandle.Right;
        if (a == ctx.IdOf("wnd-bl")) return UiWindowHandle.Bottom | UiWindowHandle.Left;
        if (a == ctx.IdOf("wnd-br")) return UiWindowHandle.Bottom | UiWindowHandle.Right;
        return UiWindowHandle.None;
    }

    /// <summary>Applies this frame's drag, the limits, and the screen clamp; stores the placement when a drag ends.</summary>
    private static void Drag(ref UiFrame ui, UiWindows windows, string id, ref UiWindowPlacement p, in UiWindowSpec spec,
                             float screenW, float screenH)
    {
        var ctx = ui.Context;
        float s = ctx.Screen.Scale;
        ref var dragging = ref windows.Dragging(id);
        // Still held: the press began on this handle and has not been released.
        var held = ctx.IsPressed(ctx.ActiveBefore) ? Held(ctx) : UiWindowHandle.None;
        float dx = (ctx.MouseX - ctx.MouseXBefore) / s, dy = (ctx.MouseY - ctx.MouseYBefore) / s;

        float maxW = MathF.Min(spec.MaxW, screenW), maxH = MathF.Min(spec.MaxH, screenH);
        float x = p.X, y = p.Y, w = p.W, h = p.H;
        if (held == UiWindowHandle.Move)
        {
            x += dx;
            y += dy;
        }
        else if (held != UiWindowHandle.None)
        {
            if ((held & UiWindowHandle.Left) != 0)
            {
                float right = x + w;
                w = Math.Clamp(w - dx, spec.MinW, maxW);
                x = right - w;
            }
            if ((held & UiWindowHandle.Right) != 0)
                w = Math.Clamp(w + dx, spec.MinW, maxW);
            if ((held & UiWindowHandle.Top) != 0)
            {
                float bottom = y + h;
                h = Math.Clamp(h - dy, spec.MinH, maxH);
                y = bottom - h;
            }
            if ((held & UiWindowHandle.Bottom) != 0)
                h = Math.Clamp(h + dy, spec.MinH, maxH);
        }

        // Limits always (a stored placement may predate them), then on screen so
        // the title bar stays reachable (a resolution change, a dragged-away window).
        w = Math.Clamp(w, MathF.Min(spec.MinW, screenW), maxW);
        h = Math.Clamp(h, MathF.Min(spec.MinH, screenH), maxH);
        x = Math.Clamp(x, KeepVisible - w, screenW - KeepVisible);
        y = Math.Clamp(y, 0f, MathF.Max(0f, screenH - TitleBar - 2 * Ring));
        p = new UiWindowPlacement(x, y, w, h);

        if (held == UiWindowHandle.None && dragging != UiWindowHandle.None)
            windows.Store(id);
        dragging = held;
    }
}
