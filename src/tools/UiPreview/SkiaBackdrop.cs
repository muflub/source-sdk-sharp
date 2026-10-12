using SkiaSharp;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.UiPreview;

/// <summary>
/// A world still as the lint sees it: the image, scaled to cover the screen as
/// the PNGs draw it, averaged into 10-pixel cells (at 1920 wide), so a region's
/// darkest and lightest colours are of the scene and not of single pixels.
/// </summary>
public sealed class SkiaBackdrop : IUiBackdrop
{
    private const int GridW = 192, GridH = 108;
    private readonly UiColor[] _cells = new UiColor[GridW * GridH];
    private readonly float[] _luma = new float[GridW * GridH];

    public SkiaBackdrop(string name, SKBitmap image)
    {
        Name = name;
        using var small = image.Resize(new SKImageInfo(GridW, GridH), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException($"backdrop {name}: could not scale");
        for (int y = 0; y < GridH; y++)
        {
            for (int x = 0; x < GridW; x++)
            {
                var c = small.GetPixel(x, y);
                _cells[y * GridW + x] = new UiColor(c.Red, c.Green, c.Blue, 255);
                _luma[y * GridW + x] = UiContrast.Luminance(_cells[y * GridW + x]);
            }
        }
    }

    public static IReadOnlyList<IUiBackdrop> Load(string dir)
        => Backdrops.Names.Select(n =>
        {
            using var bmp = SKBitmap.Decode(Path.Combine(dir, n + ".jpg"))
                ?? throw new FileNotFoundException($"backdrop {n}.jpg missing in {dir}");
            return (IUiBackdrop)new SkiaBackdrop(n, bmp);
        }).ToList();

    public string Name { get; }

    public (UiColor Dark, UiColor Light) Range(UiRect r, int w, int h)
    {
        // The still is drawn to cover a w×h screen; map the rect into its grid.
        float k = MathF.Max(w / (float)GridW, h / (float)GridH);
        float ox = (w - GridW * k) / 2, oy = (h - GridH * k) / 2;
        int x0 = Math.Clamp((int)((r.X - ox) / k), 0, GridW - 1), x1 = Math.Clamp((int)((r.X + r.W - ox) / k), 0, GridW - 1);
        int y0 = Math.Clamp((int)((r.Y - oy) / k), 0, GridH - 1), y1 = Math.Clamp((int)((r.Y + r.H - oy) / k), 0, GridH - 1);
        int dark = y0 * GridW + x0, light = dark;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * GridW + x;
                if (_luma[i] < _luma[dark]) dark = i;
                if (_luma[i] > _luma[light]) light = i;
            }
        return (_cells[dark], _cells[light]);
    }
}
