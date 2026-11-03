using SkiaSharp;
using SourceSharp.Ai.Hl2mp.Nav;

namespace SourceSharp.Ai.Replay;

/// <summary>
/// A level's derived graph drawn from above (plan_hl2mp_ai §7.1, the nav
/// view offline): each cell a square in its class's colour (green stands,
/// amber crouch only, blue water, cyan ladder), lower floors first so an
/// upper floor covers what it stands over, shaded by height; each link that
/// is not a walk a line in its type's colour. No antialiasing, so the same
/// graph is the same pixels.
/// </summary>
public static class NavPng
{
    public const int Margin = 8;

    public static SKColor CellColour(NavCell c, float lo, float hi)
    {
        (byte r, byte g, byte b) = (c.Flags & NavCellFlags.Ladder) != 0 ? ((byte)77, (byte)230, (byte)230)
            : (c.Flags & NavCellFlags.Water) != 0 ? ((byte)77, (byte)140, (byte)242)
            : c.CrouchOnly ? ((byte)242, (byte)199, (byte)64) : ((byte)77, (byte)199, (byte)107);
        float t = hi > lo ? (c.FloorZ - lo) / (hi - lo) : 1f;
        float shade = 0.55f + 0.45f * t;
        return new SKColor((byte)(r * shade), (byte)(g * shade), (byte)(b * shade));
    }

    public static SKColor LinkColour(NavEdgeType type) => type switch
    {
        NavEdgeType.Jump => new SKColor(242, 140, 51),
        NavEdgeType.CrouchJump => new SKColor(242, 77, 51),
        NavEdgeType.Drop => new SKColor(166, 102, 230),
        NavEdgeType.Ladder => new SKColor(77, 230, 230),
        NavEdgeType.Door => new SKColor(242, 242, 242),
        _ => new SKColor(128, 128, 128),
    };

    /// <summary>The PNG of <paramref name="nav"/> at <paramref name="pixelsPerUnit"/>.</summary>
    public static byte[] Render(DerivedNav nav, float pixelsPerUnit)
    {
        using SKBitmap bitmap = Draw(nav, pixelsPerUnit);
        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    public static SKBitmap Draw(DerivedNav nav, float pixelsPerUnit)
    {
        ArgumentNullException.ThrowIfNull(nav);
        NavGraph g = nav.Graph;
        if (g.CellCount == 0)
            throw new ArgumentException("an empty graph draws nothing", nameof(nav));
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, lo = float.MaxValue, hi = float.MinValue;
        for (int c = 0; c < g.CellCount; c++)
        {
            NavCell cell = g.Cell(c);
            minX = MathF.Min(minX, cell.MinX);
            minY = MathF.Min(minY, cell.MinY);
            maxX = MathF.Max(maxX, cell.MaxX);
            maxY = MathF.Max(maxY, cell.MaxY);
            lo = MathF.Min(lo, cell.FloorZ);
            hi = MathF.Max(hi, cell.FloorZ);
        }

        int width = (int)MathF.Ceiling((maxX - minX) * pixelsPerUnit) + 2 * Margin;
        int height = (int)MathF.Ceiling((maxY - minY) * pixelsPerUnit) + 2 * Margin;
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(16, 18, 23));
        // y up in the world, down in the image.
        float X(float x) => Margin + (x - minX) * pixelsPerUnit;
        float Y(float y) => height - Margin - (y - minY) * pixelsPerUnit;

        using var fill = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        foreach (int c in Enumerable.Range(0, g.CellCount).OrderBy(c => g.Cell(c).FloorZ).ThenBy(c => c))
        {
            NavCell cell = g.Cell(c);
            fill.Color = CellColour(cell, lo, hi);
            canvas.DrawRect(SKRect.Create(X(cell.MinX), Y(cell.MaxY), (cell.MaxX - cell.MinX) * pixelsPerUnit,
                                          (cell.MaxY - cell.MinY) * pixelsPerUnit), fill);
        }

        using var line = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        for (int c = 0; c < g.CellCount; c++)
        {
            foreach (NavEdge e in g.EdgesFrom(c))
            {
                if (e.Type == NavEdgeType.Walk)
                    continue;
                AiVector a = g.Cell(e.From).Centre, b = g.Cell(e.To).Centre;
                line.Color = LinkColour(e.Type);
                canvas.DrawLine(X(a.X), Y(a.Y), X(b.X), Y(b.Y), line);
            }
        }

        canvas.Flush();
        return bitmap;
    }
}
