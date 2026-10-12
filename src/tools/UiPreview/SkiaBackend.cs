using SkiaSharp;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.UiPreview;

/// <summary>
/// The third backend (plan_client_ui §5.8.4): a render list straight to pixels
/// with SkiaSharp, no SVG step. Text is drawn from the same TrueType files the
/// layout measured (<see cref="UiFontLibrary"/>), through FreeType, so glyphs
/// sit where the layout put them and look close to vgui's on Linux.
/// Optionally over a world backdrop, scaled to cover.
/// </summary>
public sealed class SkiaBackend : IUiBackend, IDisposable
{
    private readonly UiFontLibrary _fonts;
    private readonly Dictionary<TtfFace, SKTypeface> _typefaces = new();
    private SKBitmap? _bitmap;
    private UiIconSet? _icons;
    private SKBitmap? _atlas;

    // The icon atlas as an alpha-only bitmap: drawn, Skia paints it in the paint's colour.
    private SKBitmap Atlas()
    {
        if (_atlas is not null)
            return _atlas;
        _icons ??= UiIconSet.Embedded();
        _atlas = new SKBitmap(new SKImageInfo(_icons.AtlasWidth, _icons.AtlasHeight, SKColorType.Alpha8, SKAlphaType.Premul));
        var pixels = _atlas.GetPixelSpan();
        for (int y = 0; y < _icons.AtlasHeight; y++)
            _icons.Alpha.AsSpan(y * _icons.AtlasWidth, _icons.AtlasWidth).CopyTo(pixels.Slice(y * _atlas.RowBytes));
        return _atlas;
    }

    public SkiaBackend(UiFontLibrary fonts) => _fonts = fonts;

    /// <summary>Drawn under the list, scaled to cover the screen; null for a flat colour.</summary>
    public SKBitmap? Backdrop { get; set; }

    /// <summary>The colour under everything when there is no backdrop.</summary>
    public SKColor Clear { get; set; } = new(58, 62, 66);

    /// <summary>The last list drawn.</summary>
    public SKBitmap Bitmap => _bitmap ?? throw new InvalidOperationException("nothing drawn yet");

    public UiFontMetrics Metrics(in UiFontSpec font) => _fonts.Metrics(font);

    public float Advance(in UiFontSpec font, char c) => _fonts.Advance(font, c);

    private SKTypeface Typeface(in UiFontSpec font)
    {
        var face = _fonts.Face(font);
        if (!_typefaces.TryGetValue(face, out var tf))
        {
            tf = SKTypeface.FromData(SKData.CreateCopy(face.Data));
            _typefaces[face] = tf;
        }
        return tf;
    }

    private static SKColor C(UiColor c) => new(c.R, c.G, c.B, c.A);

    private static SKRect R(UiRect r) => new(r.X, r.Y, r.X + r.W, r.Y + r.H);

    public void Draw(scoped in UiDrawList list)
    {
        var s = list.Screen;
        if (_bitmap is null || _bitmap.Width != s.W || _bitmap.Height != s.H)
        {
            _bitmap?.Dispose();
            _bitmap = new SKBitmap(new SKImageInfo(s.W, s.H, SKColorType.Rgba8888, SKAlphaType.Premul));
        }

        using var canvas = new SKCanvas(_bitmap);
        canvas.Clear(Clear);
        if (Backdrop is not null)
            DrawCover(canvas, Backdrop, s.W, s.H);

        using var paint = new SKPaint { IsAntialias = true };
        foreach (var op in list.Ops)
        {
            switch (op.Kind)
            {
                case UiPrimitiveKind.Quad:
                    Quad(canvas, paint, list.Quads[op.Index]);
                    break;
                case UiPrimitiveKind.TexturedQuad:
                {
                    var t = list.TexturedQuads[op.Index];
                    if (UiIconSet.RectOf(t.Texture) is int a and >= 0)
                    {
                        // An icon: the atlas copy, as the game draws it, tinted.
                        var atlas = Atlas();
                        var src = _icons!.Rects[a];
                        paint.Shader = null;
                        paint.Style = SKPaintStyle.Fill;
                        paint.Color = C(t.Tint);
                        using var image = SKImage.FromBitmap(atlas);
                        canvas.DrawImage(image, new SKRect(src.X, src.Y, src.X + src.W, src.Y + src.H), R(t.Rect),
                                         new SKSamplingOptions(SKFilterMode.Linear), paint);
                        break;
                    }
                    // Other textures are not in previews yet: a tinted checker marks the
                    // place, and the handle is stamped muted in the corner so a PNG
                    // review sees which surface the element asks for.
                    paint.Shader = null;
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = C(t.Tint).WithAlpha((byte)(t.Tint.A / 3));
                    canvas.DrawRect(R(t.Rect), paint);
                    paint.Color = new SKColor(170, 170, 170, 235);
                    var stamp = new SKFont(Typeface(new UiFontSpec("stamped", 12, 400, UiFontFlags.None)), 12);
                    canvas.DrawText(t.Texture.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    t.Rect.X + 3, t.Rect.Y + 13, SKTextAlign.Left, stamp, paint);
                    break;
                }
                case UiPrimitiveKind.GlyphRun:
                    Run(canvas, list, list.GlyphRuns[op.Index]);
                    break;
                case UiPrimitiveKind.Line:
                {
                    var l = list.Lines[op.Index];
                    paint.Shader = null;
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = l.Width;
                    paint.Color = C(l.Color);
                    canvas.DrawLine(l.X0, l.Y0, l.X1, l.Y1, paint);
                    break;
                }
                case UiPrimitiveKind.Polygon:
                {
                    var p = list.Polygons[op.Index];
                    var points = new SKPoint[p.VertexCount];
                    for (int i = 0; i < p.VertexCount; i++)
                        points[i] = new SKPoint(list.Vertices[p.VertexStart + i].X, list.Vertices[p.VertexStart + i].Y);
                    using var builder = new SKPathBuilder();
                    builder.AddPoly(points, close: true);
                    using var path = builder.Detach();
                    paint.Shader = null;
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = C(p.Color);
                    canvas.DrawPath(path, paint);
                    break;
                }
                case UiPrimitiveKind.ScissorPush:
                    canvas.Save();
                    canvas.ClipRect(R(list.Scissors[op.Index].Rect));
                    break;
                case UiPrimitiveKind.ScissorPop:
                    canvas.Restore();
                    break;
            }
        }
    }

    private readonly Dictionary<SKBitmap, SKImage> _images = new();

    private void DrawCover(SKCanvas canvas, SKBitmap image, int w, int h)
    {
        if (!_images.TryGetValue(image, out var img))
        {
            img = SKImage.FromBitmap(image);
            _images[image] = img;
        }
        float k = MathF.Max(w / (float)image.Width, h / (float)image.Height);
        float dw = image.Width * k, dh = image.Height * k;
        var dst = new SKRect((w - dw) / 2, (h - dh) / 2, (w + dw) / 2, (h + dh) / 2);
        canvas.DrawImage(img, dst, new SKSamplingOptions(SKFilterMode.Linear));
    }

    private static void Quad(SKCanvas canvas, SKPaint paint, in UiQuad q)
    {
        var rect = R(q.Rect);
        paint.Style = SKPaintStyle.Fill;
        paint.Shader = null;
        if (q.Fade != UiFade.None)
        {
            var end = q.Fade == UiFade.Vertical ? new SKPoint(rect.Left, rect.Bottom) : new SKPoint(rect.Right, rect.Top);
            paint.Color = SKColors.White;
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Top), end,
                [C(q.Color), C(q.Color with { A = q.AlphaEnd })], SKShaderTileMode.Clamp);
            Fill(canvas, rect, q.Radius, paint);
            paint.Shader = null;
        }
        else if (q.Color.A > 0)
        {
            paint.Color = C(q.Color);
            Fill(canvas, rect, q.Radius, paint);
        }

        if (q.BorderWidth > 0f && q.Border.A > 0)
        {
            float h = q.BorderWidth / 2f;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = q.BorderWidth;
            paint.Color = C(q.Border);
            var inner = new SKRect(rect.Left + h, rect.Top + h, rect.Right - h, rect.Bottom - h);
            float r = MathF.Max(0f, q.Radius - h);
            if (r > 0f) canvas.DrawRoundRect(inner, r, r, paint); else canvas.DrawRect(inner, paint);
        }
    }

    private static void Fill(SKCanvas canvas, SKRect rect, float radius, SKPaint paint)
    {
        if (radius > 0f) canvas.DrawRoundRect(rect, radius, radius, paint); else canvas.DrawRect(rect, paint);
    }

    private void Run(SKCanvas canvas, scoped in UiDrawList list, in UiGlyphRun run)
    {
        var spec = list.Fonts[run.Font];
        string text = new(list.Text.Slice(run.TextStart, run.TextLength));
        float baseline = run.Y + _fonts.Metrics(spec).Ascent;

        using var font = new SKFont(Typeface(spec), spec.PxHeight)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.Slight,
            Subpixel = true,
        };
        using var paint = new SKPaint { IsAntialias = true };

        if ((spec.Flags & UiFontFlags.DropShadow) != 0)
        {
            paint.Color = new SKColor(0, 0, 0, (byte)(run.Color.A * 0.6f));
            canvas.DrawText(text, run.X + 1, baseline + 1, SKTextAlign.Left, font, paint);
        }

        paint.Color = C(run.Color);
        canvas.DrawText(text, run.X, baseline, SKTextAlign.Left, font, paint);
    }

    /// <summary>The last drawing as PNG bytes.</summary>
    public byte[] Png()
    {
        // zlib level 1: a preview is read once and thrown away; level 6 cost
        // 150 ms an image at 1920x1080 for a file about a quarter smaller.
        using var pixmap = Bitmap.PeekPixels();
        using var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub, 1))
            ?? throw new InvalidOperationException("PNG encode failed");
        return data.ToArray();
    }

    public void Dispose()
    {
        foreach (var tf in _typefaces.Values) tf.Dispose();
        foreach (var img in _images.Values) img.Dispose();
        _typefaces.Clear();
        _bitmap?.Dispose();
        _atlas?.Dispose();
    }
}
