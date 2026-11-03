using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// A window (plan_client_ui §5.7a, "Windows"): a movable, resizable frame over
/// the screen, as stock's vgui Frames are: a title bar (drag to move) with a
/// close button, resize from every edge and corner (a grip at the bottom
/// right), min and max sizes, raise on click, Esc closes the window on top.
/// Each edge and corner handle is also a snap: clicked rather than dragged,
/// the window takes that screen segment (a corner its quarter, an edge its
/// half), above the taskbar's reserved band. A title bar dragged to a screen
/// edge offers that edge's segment as <see cref="UiWindows.SnapGhost"/> for
/// the taskbar to draw, and snaps to it on release; dragged out again, a
/// snapped window takes back the size it had before the snap.
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

    /// <summary>How far the pointer may travel, px, and a handle press still counts as the click that snaps.</summary>
    public const float ClickSnapMaxPx = 5;

    /// <summary>How near a screen edge, du, the pointer dragging a title bar
    /// offers that edge's segment, and how near the top or the bar a side
    /// edge's corner offers its quarter.</summary>
    public const float EdgeSnapDu = 8, CornerSnapDu = 96;

    /// <summary>
    /// The segment a title bar dragged to (<paramref name="x"/>,
    /// <paramref name="y"/>) du offers: a side edge its half, near the top or
    /// the bar's band its quarter, the top edge the top half; none elsewhere,
    /// and none at the bottom, where the bar stands.
    /// </summary>
    public static UiWindowHandle EdgeHandle(float x, float y, float screenW, float screenH, float reservedBelow)
    {
        bool left = x <= EdgeSnapDu, right = x >= screenW - EdgeSnapDu;
        if (!left && !right)
            return y <= EdgeSnapDu ? UiWindowHandle.Top : UiWindowHandle.None;
        var side = left ? UiWindowHandle.Left : UiWindowHandle.Right;
        if (y <= CornerSnapDu)
            return side | UiWindowHandle.Top;
        if (y >= screenH - reservedBelow - CornerSnapDu)
            return side | UiWindowHandle.Bottom;
        return side;
    }

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
        windows.Titled(id, titleToken);
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

    /// <summary>
    /// One edge or corner handle. Hovering one offers no ghost: the ghost is a
    /// drag's (a title bar at a screen edge), and offered on hover it flashed
    /// whenever the pointer crossed a window's edge.
    /// </summary>
    private static UiBox Handle(ref UiFrame ui, string key, UiAnchor at, UiSize w, UiSize h, UiCursor cursor)
    {
        var box = ui.Rect(Tone.None, w, h).At(at).Key(key).Layered();
        ui.Context.Node(box.Index).Handle = true;
        var hit = ui.Interact(box);
        if (hit.Hovered || hit.Pressed)
            ui.Context.Cursor = cursor;
        return box;
    }

    /// <summary>
    /// Where a snap puts the window: the segment, within the window's limits,
    /// held against the segment's outer edges (a right half's window keeps to
    /// the screen's right edge when its maximum is narrower than the half).
    /// </summary>
    public static UiRect SnapPlace(in UiRect zone, in UiWindowSpec spec, float maxW, float maxH)
    {
        float w = Math.Clamp(zone.W, MathF.Min(spec.MinW, maxW), maxW), h = Math.Clamp(zone.H, MathF.Min(spec.MinH, maxH), maxH);
        float x = zone.X > 0 ? zone.X + zone.W - w : zone.X, y = zone.Y > 0 ? zone.Y + zone.H - h : zone.Y;
        return new UiRect(x, y, w, h);
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
        if (a == ctx.IdOf("wnd-tl")) return UiWindowHandle.TopLeft;
        if (a == ctx.IdOf("wnd-tr")) return UiWindowHandle.TopRight;
        if (a == ctx.IdOf("wnd-bl")) return UiWindowHandle.BottomLeft;
        if (a == ctx.IdOf("wnd-br")) return UiWindowHandle.BottomRight;
        return UiWindowHandle.None;
    }

    /// <summary>
    /// Applies this frame's drag, the limits, and the screen clamp; stores the
    /// placement when a drag ends. A resize handle released as a click (travel
    /// under <see cref="ClickSnapMaxPx"/>) snaps the window to its segment,
    /// which only the screen and the taskbar bound, not the spec's maximum;
    /// so does a title bar released at a screen edge (<see cref="EdgeHandle"/>),
    /// whose segment shows as the ghost while it is held there.
    /// </summary>
    private static void Drag(ref UiFrame ui, UiWindows windows, string id, ref UiWindowPlacement p, in UiWindowSpec spec,
                             float screenW, float screenH)
    {
        var ctx = ui.Context;
        float s = ctx.Screen.Scale;
        ref var dragging = ref windows.Dragging(id);
        // Still held: the press began on this handle and has not been released.
        var held = ctx.IsPressed(ctx.ActiveBefore) ? Held(ctx) : UiWindowHandle.None;
        float dx = (ctx.MouseX - ctx.MouseXBefore) / s, dy = (ctx.MouseY - ctx.MouseYBefore) / s;
        float reserved = windows.DockReserved;
        if (held != UiWindowHandle.None)
        {
            if (dragging == UiWindowHandle.None)
                windows.BeginDrag(id);
            windows.AddTravel(id, ctx.MouseX - ctx.MouseXBefore, ctx.MouseY - ctx.MouseYBefore);
        }
        float maxW = MathF.Min(spec.MaxW, screenW), maxH = MathF.Min(spec.MaxH, screenH - reserved);
        // A title bar dragged (not clicked: one at the top edge would snap) to a screen edge.
        bool moved = (held == UiWindowHandle.Move || (held == UiWindowHandle.None && dragging == UiWindowHandle.Move))
                     && windows.Travel(id) > ClickSnapMaxPx;
        var edge = moved
            ? EdgeHandle(ctx.MouseX / s, ctx.MouseY / s, screenW, screenH, reserved)
            : UiWindowHandle.None;
        if (held == UiWindowHandle.Move && UiWindows.TrySnapZone(edge, screenW, screenH, reserved, out var ghost))
            windows.SnapGhost = ghost;
        // Dragged out of a snap, a window takes back the size it had before it,
        // the point grabbed staying under the pointer; resized, it keeps the new
        // size and forgets the old.
        if (moved && held == UiWindowHandle.Move && windows.TryUnsnap(id, out var before))
        {
            float grabX = ctx.MouseXBefore / s;
            float along = p.W > 0f ? Math.Clamp((grabX - p.X) / p.W, 0f, 1f) : 0.5f;
            p = new UiWindowPlacement(grabX - along * before.W, p.Y, before.W, before.H);
        }
        else if (held is not UiWindowHandle.None and not UiWindowHandle.Move && windows.Travel(id) > ClickSnapMaxPx)
        {
            windows.TryUnsnap(id, out _);
        }
        var snap = held != UiWindowHandle.None ? UiWindowHandle.None
                   : dragging == UiWindowHandle.Move ? edge
                   : windows.Travel(id) <= ClickSnapMaxPx ? dragging
                   : UiWindowHandle.None;
        if (UiWindows.TrySnapZone(snap, screenW, screenH, reserved, out var zone))
        {
            windows.Snap(id, SnapPlace(zone, spec, maxW, maxH));
            windows.TryGet(id, out p);
            dragging = UiWindowHandle.None;
            return;
        }
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
        // the title bar stays reachable (a resolution change, a dragged-away
        // window) and above the taskbar.
        w = Math.Clamp(w, MathF.Min(spec.MinW, screenW), maxW);
        h = Math.Clamp(h, MathF.Min(spec.MinH, screenH), maxH);
        x = Math.Clamp(x, KeepVisible - w, screenW - KeepVisible);
        y = Math.Clamp(y, 0f, MathF.Max(0f, screenH - reserved - TitleBar - 2 * Ring));
        p = new UiWindowPlacement(x, y, w, h);

        if (held == UiWindowHandle.None && dragging != UiWindowHandle.None)
            windows.Store(id);
        dragging = held;
    }
}
