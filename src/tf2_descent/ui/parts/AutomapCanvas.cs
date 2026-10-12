using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Parts;

/// <summary>Room names the canvas may draw (supplied by the caller — the
/// canvas itself reads only the map shape).</summary>
public interface IAutomapRoomLabels
{
    string RoomLabel(int room);
}

/// <summary>What an <see cref="AutomapCanvas"/> draws and how big. The storey,
/// heading, label and marker-size props are additive; their defaults keep the
/// corner minimap's behaviour for the full automap overlay's scaled-up canvas.
/// <c>HeroHeading</c> replaces the hero's dot with the plan §2.2's accent
/// triangle pointing along the heading.</summary>
public readonly record struct AutomapProps(float WidthDu, float HeightDu, int? FocusStorey = null, bool HeroHeading = false, bool HeroFollowsStorey = false, float MarkerDu = 0f, IAutomapRoomLabels? Labels = null, bool ShowLabels = false);

/// <summary>
/// The automap's drawing (R23), shared by the corner minimap and the full map
/// screen: revealed rooms as hairline rects from the server-sent outline and
/// nothing else, door stubs as short ticks, markers mapped by kind
/// (stairs → ChevronDown, corpse → Cross45, party → Person, shrine → Star,
/// locked container → Lock — an open one a plain dot, so the lock means
/// locked (plan §2.2) — portal → Check; boss → Shield, trap → Chevron,
/// fountain → Cross: every kind its own shape), the hero as an accent triangle
/// oriented by heading (a dot where no heading is asked for). The
/// focus storey draws bright; other storeys are a muted layer under it.
/// </summary>
public sealed class AutomapCanvas : UiPanel<AutomapProps>
{
    private readonly IDescentAutomap _map;

    public AutomapCanvas(IDescentAutomap map) => _map = map;

    // The abstract grid the generator's du become canvas du.
    private const float GridW = 1024, GridH = 1024;

    // The marker and hero glyphs, in canvas du.
    private const float DefaultMarkerDu = 12;

    public override void Describe(ref UiFrame ui, in AutomapProps p)
    {
        float sx = p.WidthDu / GridW, sy = p.HeightDu / GridH;
        float markerDu = p.MarkerDu > 0f ? p.MarkerDu : DefaultMarkerDu;
        int focus = p.FocusStorey ?? _map.FocusStorey;
        // On a storey-switching map the hero rides the focus storey only when
        // it is their storey (or the map has one storey).
        bool heroShown = !p.HeroFollowsStorey || _map.StoreyCount <= 1 || focus == _map.HeroStorey;

        // The canvas box is a full-size layer over the part box, so the lint
        // does not compare it against the room rects drawn inside it, and each
        // marker gets a zero-size wrapper so its glyph is not a free sibling of
        // the rooms it happens to sit near (lint `alignment`/`overlap`).
        using (ui.Box().Size(Size.Du(p.WidthDu), Size.Du(p.HeightDu)).Clip().Layered())
        {
            for (int r = 0; r < _map.RoomCount; r++)
            {
                // Bright on the storey the view shows, and only as the server
                // says: the hero's own storey's rooms read brighter (R23), so
                // a tab on another storey dims them all.
                bool focusRoom = _map.RoomStorey(r) == focus && _map.RoomCurrentStorey(r);
                var paint = focusRoom ? DescentPaints.MapRoomCurrent : DescentPaints.MapRoom;
                // Rooms stack by storey: a lower storey's room legitimately
                // sits under another storey's in plan view, so the rects are
                // layers, not siblings to line up (lint `overlap`).
                ui.Rect(paint, Size.Du(_map.RoomW(r) * sx), Size.Du(_map.RoomH(r) * sy))
                    .At(UiAnchor.TopLeft)
                    .Inset(new UiLength(_map.RoomX(r) * sx, false), new UiLength(_map.RoomY(r) * sy, false))
                    .Border(DescentPaints.MapLine, 1)
                    .Layered();
            }
            for (int i = 0; i < _map.DoorStubCount; i++)
                // Wrapped like the markers: a stub straddles a room border, and
                // the lint would read its clamped hairline width as an accidental
                // near-miss with the wall it marks (lint `alignment`).
                using (ui.Box().Size(Size.Du(0), Size.Du(0)).At(UiAnchor.TopLeft)
                         .Inset(new UiLength(_map.DoorStubX(i) * sx, false), new UiLength(_map.DoorStubY(i) * sy, false)))
                    ui.Rect(DescentPaints.MapLine,
                            Size.Du(System.Math.Max(2, _map.DoorStubW(i) * sx)),
                            Size.Du(System.Math.Max(2, _map.DoorStubH(i) * sy)))
                        .At(UiAnchor.Centre);

            // Room names (plan §2.7) on the focus storey, each in its own
            // zero-size wrapper so the text is
            // not a free sibling to line up against the rooms (lint
            // `alignment`), on a small Surface plate (lint `contrast`). Each
            // hangs from the room edge with more clearance to its nearest mark:
            // centred, a plate met the hero's arrow ("Entry Hall") and, drawn
            // last, hid the shrine's star (RevD). Drawn before the marks: the
            // server puts markers anywhere, so where both ends are crowded the
            // mark paints over the plate, never under it.
            if (p.ShowLabels && p.Labels is { } labels)
                for (int r = 0; r < _map.RoomCount; r++)
                {
                    if (_map.StoreyCount > 1 && _map.RoomStorey(r) != focus) continue;
                    bool bottom = BottomClearer(r, focus, heroShown);
                    float cx = (_map.RoomX(r) + _map.RoomW(r) / 2f) * sx;
                    float y = bottom
                        ? (_map.RoomY(r) + _map.RoomH(r)) * sy - (float)Space.XS
                        : _map.RoomY(r) * sy + (float)Space.XS;
                    using (ui.Box().Size(Size.Du(0), Size.Du(0)).At(UiAnchor.TopLeft)
                             .Inset(new UiLength(cx, false), new UiLength(y, false)))
                        using (ui.Box().Size(Size.Auto, Size.Auto).At(bottom ? UiAnchor.Bottom : UiAnchor.Top)
                                 .Fill(Tone.Surface).Round(Radius.S).Pad(Space.S, Space.XS))
                            ui.Data(labels.RoomLabel(r), Text.Caption);
                }

            // Each glyph sits in its own zero-size wrapper box centred on its grid
            // point: the glyph fills that box, so the lint reads it as the box's
            // layer (not a free sibling to line up against the rooms), and the
            // rooms only ever compare against each other (lint `alignment`).
            for (int i = 0; i < _map.MarkerCount; i++)
            {
                var m = _map.Marker(i);
                if (_map.StoreyCount > 1 && m.Storey != focus) continue;
                using (ui.Box().Size(Size.Du(0), Size.Du(0)).At(UiAnchor.TopLeft)
                         .Inset(new UiLength(m.X * sx, false), new UiLength(m.Y * sy, false)))
                    ui.Shape(ShapeOf(m.Kind), ToneOf(m.Kind), markerDu).At(UiAnchor.Centre);
            }

            // The hero (plan §2.2): an accent triangle pointing along the
            // heading; without one asked for, the plain dot.
            if (heroShown)
            {
                float hx = _map.HeroX * sx, hy = _map.HeroY * sy;
                if (p.HeroHeading)
                    HeroArrow(ref ui, hx, hy, markerDu, _map.HeroHeadingDeg);
                else
                    using (ui.Box().Size(Size.Du(0), Size.Du(0)).At(UiAnchor.TopLeft)
                             .Inset(new UiLength(hx, false), new UiLength(hy, false)))
                        ui.Shape(UiShape.Dot, Tone.Accent, markerDu).At(UiAnchor.Centre);
            }
        }
    }

    /// <summary>Whether room <paramref name="r"/>'s bottom edge is farther from
    /// its nearest mark (a marker on the shown storey, or the hero) than its
    /// top edge is; ties and empty rooms keep the label at the top.</summary>
    private bool BottomClearer(int r, int focus, bool heroShown)
    {
        float x0 = _map.RoomX(r), y0 = _map.RoomY(r), x1 = x0 + _map.RoomW(r), y1 = y0 + _map.RoomH(r);
        float top = float.MaxValue, bottom = float.MaxValue;
        for (int i = 0; i < _map.MarkerCount; i++)
        {
            var m = _map.Marker(i);
            if ((_map.StoreyCount > 1 && m.Storey != focus) || m.X < x0 || m.X > x1 || m.Y < y0 || m.Y > y1) continue;
            top = System.Math.Min(top, m.Y - y0);
            bottom = System.Math.Min(bottom, y1 - m.Y);
        }
        if (heroShown && _map.HeroX >= x0 && _map.HeroX <= x1 && _map.HeroY >= y0 && _map.HeroY <= y1)
        {
            top = System.Math.Min(top, _map.HeroY - y0);
            bottom = System.Math.Min(bottom, y1 - _map.HeroY);
        }
        return bottom > top;
    }

    /// <summary>The hero's arrow (plan §2.2): apex one marker ahead on the
    /// heading (0 du is north, clockwise — the convention the old heading nub
    /// drew with), base half a marker behind. A rotated triangle is not a
    /// primitive, so it is rasterised as the axis-aligned rows a convex makes
    /// of hairline <c>Rect</c>s; each row rides its own zero-size wrapper like
    /// the markers: rows legitimately touch, so they must not be lined up or
    /// separated as siblings (lint <c>overlap</c>).</summary>
    private static void HeroArrow(ref UiFrame ui, float hx, float hy, float markerDu, float headingDeg)
    {
        double rad = headingDeg * (System.Math.PI / 180.0);
        float fdx = (float)System.Math.Sin(rad), fdy = -(float)System.Math.Cos(rad); // forward (y down)
        float pdx = (float)System.Math.Cos(rad), pdy = (float)System.Math.Sin(rad);  // to its right
        float apexX = hx + fdx * markerDu, apexY = hy + fdy * markerDu;
        float baseX = hx - fdx * markerDu * 0.5f, baseY = hy - fdy * markerDu * 0.5f;
        float c1x = baseX + pdx * markerDu * 0.5f, c1y = baseY + pdy * markerDu * 0.5f;
        float c2x = baseX - pdx * markerDu * 0.5f, c2y = baseY - pdy * markerDu * 0.5f;
        float top = System.Math.Min(apexY, System.Math.Min(c1y, c2y));
        float bottom = System.Math.Max(apexY, System.Math.Max(c1y, c2y));
        float rowDu = System.Math.Max(1.5f, markerDu * 0.125f);
        for (float y = top; y < bottom; y += rowDu)
        {
            float mid = y + rowDu * 0.5f;
            float lo = float.MaxValue, hi = float.MinValue;
            Crosses(apexY, apexX, c1y, c1x, mid, ref lo, ref hi);
            Crosses(apexY, apexX, c2y, c2x, mid, ref lo, ref hi);
            Crosses(c1y, c1x, c2y, c2x, mid, ref lo, ref hi);
            if (hi <= lo) continue;
            using (ui.Box().Size(Size.Du(0), Size.Du(0)).At(UiAnchor.TopLeft)
                     .Inset(new UiLength(lo, false), new UiLength(y, false)))
                ui.Rect(Tone.Accent, Size.Du(hi - lo), Size.Du(rowDu + 0.6f)).At(UiAnchor.TopLeft);
        }
    }

    /// <summary>The x where the horizontal line y crosses the segment, widened
    /// into lo..hi; a horizontal edge contributes nothing.</summary>
    private static void Crosses(float y0, float x0, float y1, float x1, float y, ref float lo, ref float hi)
    {
        if (y0 == y1) return;
        float t = (y - y0) / (y1 - y0);
        if (t < 0f || t > 1f) return;
        float x = x0 + (x1 - x0) * t;
        lo = System.Math.Min(lo, x);
        hi = System.Math.Max(hi, x);
    }

    /// <summary>The kind's glyph: the plan's table (§2.2) plus the kinds it
    /// leaves open, each its own shape so the map reads without colour. Boss
    /// is the HUD boss bar's Shield, so the Fountain takes the healing plus;
    /// a trap is the filled triangle in warning ink, the set's nearest to a
    /// hazard sign (the crosshair drew as a thin plus beside the fountain's).</summary>
    public static UiShape ShapeOf(DescentMapMarker kind) => kind switch
    {
        DescentMapMarker.Stairs => UiShape.ChevronDown,
        DescentMapMarker.Boss => UiShape.Shield,
        DescentMapMarker.Corpse => UiShape.Cross45,
        DescentMapMarker.Trap => UiShape.Chevron,
        DescentMapMarker.Party => UiShape.Person,
        DescentMapMarker.ShrineUnused or DescentMapMarker.ShrineUsed => UiShape.Star,
        // Only the locked chest earns the padlock (plan §2.2); an open one is
        // the plain mark, so the lock on the map always means locked.
        DescentMapMarker.Container => UiShape.Dot,
        DescentMapMarker.ContainerLocked => UiShape.Lock,
        DescentMapMarker.Portal => UiShape.Check,
        DescentMapMarker.Fountain => UiShape.Cross,
        _ => UiShape.Dot,
    };

    public static UiPaint ToneOf(DescentMapMarker kind) => kind switch
    {
        DescentMapMarker.Corpse or DescentMapMarker.Boss => Tone.Danger,
        DescentMapMarker.Party => Tone.TeamA,
        DescentMapMarker.Stairs => Tone.Accent,
        DescentMapMarker.ShrineUnused or DescentMapMarker.Trap => Tone.Warning,
        DescentMapMarker.ShrineUsed => Tone.TextMuted,
        // Portal shares the stairs' accent: both are ways through, and the map
        // window's tone set (the frame's text included) must stay at six.
        DescentMapMarker.Portal => Tone.Accent,
        _ => (UiPaint)Tone.Text,
    };
}
