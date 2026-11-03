using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

// THE ICON SET, as vector shapes: the mod has no art of its own, and convex
// polygons and quads are what every backend draws (vgui's textured polygon with
// a white texture). Each shape fills the node's rect in its colour.
public sealed partial class UiContext
{
    private readonly UiVertex[] _poly = new UiVertex[DiscSides];

    private const int DiscSides = 72;

    /// <summary>
    /// A convex polygon from points in hundredths of the rect (0..100). Bytes, not
    /// floats: a ReadOnlySpan&lt;byte&gt; literal is data in the assembly, while a
    /// float one allocated an array a call in Debug (UiAllocationTests found it).
    /// </summary>
    private void Poly(in UiRect r, UiColor c, int element, ReadOnlySpan<byte> unit)
    {
        int count = unit.Length / 2;
        for (int i = 0; i < count; i++)
            _poly[i] = new UiVertex(MathF.Round(r.X + unit[2 * i] / 100f * r.W, 1), MathF.Round(r.Y + unit[2 * i + 1] / 100f * r.H, 1));
        List.Polygon(_poly.AsSpan(0, count), c, element);
    }

    // A slice of a ring as quads, each convex as every backend needs, a step
    // short enough that the curve reads as one at any size a menu draws it.
    private void Sector(in UiRect r, UiColor c, int element, float start, float span, float inner)
    {
        const float StepRadians = MathF.PI / 36f;
        float rx = r.W / 2f, ry = r.H / 2f, cx = r.X + rx, cy = r.Y + ry;

        // A whole disc is one polygon: a fan of quads shows its seams on anything behind it.
        if (inner <= 0f && span >= MathF.Tau)
        {
            for (int i = 0; i < DiscSides; i++)
            {
                float a = MathF.Tau * i / DiscSides;
                _poly[i] = new UiVertex(cx + MathF.Sin(a) * rx, cy - MathF.Cos(a) * ry);
            }
            List.Polygon(_poly.AsSpan(0, DiscSides), c, element);
            return;
        }

        int steps = Math.Max(1, (int)MathF.Ceiling(span / StepRadians));
        for (int i = 0; i < steps; i++)
        {
            float a0 = start + span * i / steps, a1 = start + span * (i + 1) / steps;
            float s0 = MathF.Sin(a0), c0 = MathF.Cos(a0), s1 = MathF.Sin(a1), c1 = MathF.Cos(a1);
            _poly[0] = new UiVertex(cx + s0 * rx * inner, cy - c0 * ry * inner);
            _poly[1] = new UiVertex(cx + s0 * rx, cy - c0 * ry);
            _poly[2] = new UiVertex(cx + s1 * rx, cy - c1 * ry);
            _poly[3] = new UiVertex(cx + s1 * rx * inner, cy - c1 * ry * inner);
            List.Polygon(_poly.AsSpan(0, 4), c, element);
        }
    }

    private void Lines(in UiRect r, UiColor c, int element, float[]? segments, float thickness)
    {
        if (segments is null)
            return;

        float half = thickness / 2f;
        for (int i = 0; i + 3 < segments.Length; i += 4)
        {
            float x0 = r.X + segments[i] * r.W, y0 = r.Y + segments[i + 1] * r.H;
            float x1 = r.X + segments[i + 2] * r.W, y1 = r.Y + segments[i + 3] * r.H;
            float dx = x1 - x0, dy = y1 - y0, length = MathF.Sqrt(dx * dx + dy * dy);
            if (length <= 0f)
                continue;

            // Along by half too, so two segments that meet leave no notch at the corner.
            float ux = dx / length * half, uy = dy / length * half;
            _poly[0] = new UiVertex(x0 - ux + uy, y0 - uy - ux);
            _poly[1] = new UiVertex(x1 + ux + uy, y1 + uy - ux);
            _poly[2] = new UiVertex(x1 + ux - uy, y1 + uy + ux);
            _poly[3] = new UiVertex(x0 - ux - uy, y0 - uy + ux);
            List.Polygon(_poly.AsSpan(0, 4), c, element);
        }
    }

    private void Box(float x, float y, float w, float h, UiColor c, int element)
        => List.Quad(new UiQuad(Snap(x, y, w, h), c, 0, default, 0, UiFade.None, 0), element);

    private void EmitShape(in UiNode n, UiRect r, float opacity, int element)
    {
        var c = Paint(n.TextPaint, opacity);
        float w = r.W, h = r.H;

        switch (n.Shape)
        {
            case UiShape.Art:
            {
                // The atlas copy at least this tall, scaled to the rect; the
                // texture id names the copy (UiIconSet.TextureOf), so a backend
                // finds the pixels, or the art, from it.
                var icons = Icons;
                int rect = icons.RectFor((int)n.ShapeValue, h);
                if (rect < 0)
                    break;
                var a = icons.Rects[rect];
                float aw = icons.AtlasWidth, ah = icons.AtlasHeight;
                List.TexturedQuad(new UiTexturedQuad(r, UiIconSet.TextureOf(rect), new UiRect(a.X / aw, a.Y / ah, a.W / aw, a.H / ah), c),
                                  element);
                break;
            }
            case UiShape.Cross:
            {
                float t = MathF.Round(MathF.Min(w, h) * 0.34f);
                Box(r.X, r.Y + (h - t) / 2f, w, t, c, element);
                Box(r.X + (w - t) / 2f, r.Y, t, (h - t) / 2f, c, element);
                Box(r.X + (w - t) / 2f, r.Y + (h + t) / 2f, t, (h - t) / 2f, c, element);
                break;
            }
            case UiShape.Shield:
                Poly(r, c, element, [50, 0, 100, 14, 94, 62, 50, 100, 6, 62, 0, 14]);
                break;
            case UiShape.Bullet:
                Poly(r, c, element, [32, 36, 40, 10, 50, 0, 60, 10, 68, 36]);
                Box(r.X + w * 0.32f, r.Y + h * 0.4f, w * 0.36f, h * 0.5f, c, element);
                Box(r.X + w * 0.26f, r.Y + h * 0.92f, w * 0.48f, h * 0.08f, c, element);
                break;
            case UiShape.Signal:
            {
                int lit = (int)n.ShapeValue;
                float bw = w / 5.5f, step = (w - bw) / 3f;
                for (int i = 0; i < 4; i++)
                {
                    float bh = h * (0.3f + 0.7f * i / 3f);
                    var bc = i < lit ? c : c with { A = (byte)(c.A * 0.28f) };
                    Box(r.X + i * step, r.Y + h - bh, bw, bh, bc, element);
                }
                break;
            }
            case UiShape.Sector:
                Sector(r, c, element, n.ShapeValue, n.ShapeSpan, n.ShapeInner);
                break;
            case UiShape.Lines:
                Lines(r, c, element, n.Lines, n.ShapeValue * Screen.Scale);
                break;
            case UiShape.Dot:
                Poly(r, c, element, [30, 0, 70, 0, 100, 30, 100, 70, 70, 100, 30, 100, 0, 70, 0, 30]);
                break;
            case UiShape.Chevron:
                Poly(r, c, element, [15, 0, 95, 50, 15, 100]);
                break;
            case UiShape.Cross45:
                Poly(r, c, element, [12, 0, 100, 88, 88, 100, 0, 12]);
                Poly(r, c, element, [88, 0, 100, 12, 12, 100, 0, 88]);
                break;
            case UiShape.Person:
                Poly(r, c, element, [40, 0, 60, 0, 72, 12, 72, 36, 60, 48, 40, 48, 28, 36, 28, 12]);
                Poly(r, c, element, [30, 56, 70, 56, 94, 72, 100, 100, 0, 100, 6, 72]);
                break;
            case UiShape.Check:
                Poly(r, c, element, [8, 52, 22, 38, 42, 58, 42, 86]);
                Poly(r, c, element, [42, 86, 28, 72, 78, 16, 92, 30]);
                break;
            case UiShape.ChevronDown:
                Poly(r, c, element, [6, 26, 22, 26, 50, 58, 50, 78]);
                Poly(r, c, element, [50, 58, 78, 26, 94, 26, 50, 78]);
                break;
            case UiShape.ChevronUp:
                Poly(r, c, element, [6, 74, 50, 22, 50, 42, 22, 74]);
                Poly(r, c, element, [50, 22, 94, 74, 78, 74, 50, 42]);
                break;
            case UiShape.Pencil:
                // Convex pieces only: the eraser, the shaft a gap below it, the point.
                Poly(r, c, element, [74, 2, 98, 26, 88, 36, 64, 12]);
                Poly(r, c, element, [58, 18, 82, 42, 34, 90, 10, 66]);
                Poly(r, c, element, [6, 70, 30, 94, 0, 100]);
                break;
            case UiShape.ChevronLeft:
                Poly(r, c, element, [85, 0, 5, 50, 85, 100]);
                break;
            case UiShape.Lock:
                // Convex pieces only (vgui fills convex polygons): body, two posts, the arch's top.
                Box(r.X + w * 0.16f, r.Y + h * 0.44f, w * 0.68f, h * 0.52f, c, element);
                Box(r.X + w * 0.26f, r.Y + h * 0.18f, w * 0.12f, h * 0.28f, c, element);
                Box(r.X + w * 0.62f, r.Y + h * 0.18f, w * 0.12f, h * 0.28f, c, element);
                Poly(r, c, element, [26, 20, 36, 6, 64, 6, 74, 20, 62, 20, 38, 20]);
                break;
            case UiShape.Star:
                // A pentagon and five points: convex pieces only.
                Poly(r, c, element, [37, 36, 63, 36, 71, 61, 50, 76, 29, 61]);
                Poly(r, c, element, [50, 0, 63, 36, 37, 36]);
                Poly(r, c, element, [100, 38, 71, 61, 63, 36]);
                Poly(r, c, element, [81, 98, 50, 76, 71, 61]);
                Poly(r, c, element, [19, 98, 29, 61, 50, 76]);
                Poly(r, c, element, [0, 38, 37, 36, 29, 61]);
                break;
            case UiShape.Grip:
            {
                float d = MathF.Max(1f, MathF.Round(w * 0.2f));
                for (int row = 0; row < 3; row++)
                    for (int col = 2 - row; col < 3; col++)
                        Box(r.X + w - (3 - col) * d * 1.6f, r.Y + h - (3 - row) * d * 1.6f, d, d, c, element);
                break;
            }
            case UiShape.Compass:
            {
                // A dim ring, and the arrow rotated to ShapeValue degrees about
                // the centre (0 = up): stock's CTFArrowPanel spins an arrow
                // material to the flag's bearing (tf_hud_flagstatus.cpp).
                Poly(r, c with { A = (byte)(c.A * 0.22f) }, element,
                     [30, 0, 70, 0, 100, 30, 100, 70, 70, 100, 30, 100, 0, 70, 0, 30]);
                Span<byte> arrow = stackalloc byte[6];
                float deg = n.ShapeValue * MathF.PI / 180f;
                float dirX = MathF.Sin(deg), dirY = -MathF.Cos(deg);
                float perpX = MathF.Cos(deg), perpY = MathF.Sin(deg);
                arrow[0] = (byte)Math.Clamp(MathF.Round(50f + dirX * 46f), 0, 100);
                arrow[1] = (byte)Math.Clamp(MathF.Round(50f + dirY * 46f), 0, 100);
                arrow[2] = (byte)Math.Clamp(MathF.Round(50f - dirX * 16f + perpX * 18f), 0, 100);
                arrow[3] = (byte)Math.Clamp(MathF.Round(50f - dirY * 16f + perpY * 18f), 0, 100);
                arrow[4] = (byte)Math.Clamp(MathF.Round(50f - dirX * 16f - perpX * 18f), 0, 100);
                arrow[5] = (byte)Math.Clamp(MathF.Round(50f - dirY * 16f - perpY * 18f), 0, 100);
                Poly(r, c, element, arrow);
                break;
            }
            case UiShape.Crosshair:
            {
                float gap = n.ShapeValue * S;
                float t = MathF.Max(2f, MathF.Round(2f * S));
                float cx = r.X + w / 2f, cy = r.Y + h / 2f;
                float len = MathF.Max(1f, w / 2f - gap);
                var shade = new UiColor(0, 0, 0, (byte)(c.A * 0.55f));
                // A one-pixel dark outline first, so the ticks read over a bright wall.
                Box(cx - gap - len - 1, cy - t / 2f - 1, len + 2, t + 2, shade, element);
                Box(cx + gap - 1, cy - t / 2f - 1, len + 2, t + 2, shade, element);
                Box(cx - t / 2f - 1, cy - gap - len - 1, t + 2, len + 2, shade, element);
                Box(cx - t / 2f - 1, cy + gap - 1, t + 2, len + 2, shade, element);
                Box(cx - gap - len, cy - t / 2f, len, t, c, element);
                Box(cx + gap, cy - t / 2f, len, t, c, element);
                Box(cx - t / 2f, cy - gap - len, t, len, c, element);
                Box(cx - t / 2f, cy + gap, t, len, c, element);
                break;
            }
            case UiShape.Sweep:
            {
                // The pie as convex triangles from the centre — stock's
                // CircularProgressBar fans the arc the same way (a twelfth of
                // the circle a triangle is enough for icon sizes). 0 the empty
                // sweep draws nothing; 1 is the disc.
                float frac = Math.Clamp(n.ShapeValue, 0f, 1f);
                if (frac >= 1f)
                {
                    Poly(r, c, element, [30, 0, 70, 0, 100, 30, 100, 70, 70, 100, 30, 100, 0, 70, 0, 30]);
                    break;
                }

                int segments = (int)MathF.Ceiling(frac * 12f);
                Span<byte> tri = stackalloc byte[6];
                for (int i = 0; i < segments; i++)
                {
                    float a0 = 2f * MathF.PI * frac * i / segments;
                    float a1 = 2f * MathF.PI * frac * (i + 1) / segments;
                    tri[0] = 50;
                    tri[1] = 50;
                    Point(tri, 2, a0);
                    Point(tri, 4, a1);
                    Poly(r, c, element, tri);
                }

                static void Point(Span<byte> into, int at, float angle)
                {
                    into[at] = (byte)Math.Clamp(MathF.Round(50f + 50f * MathF.Sin(angle)), 0, 100);
                    into[at + 1] = (byte)Math.Clamp(MathF.Round(50f - 50f * MathF.Cos(angle)), 0, 100);
                }

                break;
            }
        }
    }
}
