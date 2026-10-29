namespace SourceSharp.StockAb;

/// <summary>How two screenshots of the same camera are scored.</summary>
/// <param name="Threshold">A pixel differs when any channel differs by more than this (0-255).</param>
/// <param name="DynamicDilation">How far, in pixels, the moving mask grows around each moving pixel.</param>
/// <param name="TolerancePercent">The most differing still pixels, in percent, that still pass.</param>
/// <param name="MinimumStillPercent">Below this share of still pixels the shot proves nothing.</param>
public sealed record ComparisonSettings(
    int Threshold = 24,
    int DynamicDilation = 3,
    double TolerancePercent = 5.0,
    double MinimumStillPercent = 50.0);

public enum Verdict
{
    Pass,
    Fail,

    /// <summary>Too much of the frame moved between a game's own two shots to judge it.</summary>
    Inconclusive,
}

/// <summary>One camera's score.</summary>
/// <param name="StillPercent">Pixels that held still in both games' own pairs.</param>
/// <param name="DifferingPercent">Of the still pixels, those stock and SourceSharp disagree on.</param>
/// <param name="MeanAbsDiffPercent">Mean channel difference over the still pixels, as a percent of 255.</param>
/// <param name="LumaRatio">SourceSharp's mean luma over stock's, over the still pixels (1 is equal).</param>
public sealed record ShotScore(
    double StillPercent,
    double DifferingPercent,
    double MeanAbsDiffPercent,
    double LumaRatio,
    Verdict Verdict);

/// <summary>
/// Stock against SourceSharp from one camera, each game shot twice a moment
/// apart. A pixel that changed between a game's OWN two shots is moving (a
/// mover, a sprite, a flickering light) and the two games are not in step, so
/// it is left out of the score rather than counted as a difference.
/// </summary>
public static class ShotComparison
{
    public static ShotScore Score(
        RgbImage stockA, RgbImage stockB, RgbImage sharpA, RgbImage sharpB,
        ComparisonSettings settings, out RgbImage heatmap)
    {
        RequireSameSize(stockA, stockB, sharpA, sharpB);

        int width = stockA.Width;
        int height = stockA.Height;
        bool[] moving = Dilate(Moving(stockA, stockB, sharpA, sharpB, settings.Threshold), width, height, settings.DynamicDilation);

        long still = 0;
        long differing = 0;
        double absSum = 0;
        double stockLuma = 0;
        double sharpLuma = 0;
        heatmap = RgbImage.Blank(width, height);
        byte[] a = stockA.Pixels;
        byte[] b = sharpA.Pixels;
        byte[] h = heatmap.Pixels;

        for (int p = 0, i = 0; p < moving.Length; p++, i += 3)
        {
            int grey = ((a[i] * 3) + (a[i + 1] * 6) + a[i + 2]) / 30;

            if (moving[p])
            {
                h[i] = (byte)grey;
                h[i + 1] = (byte)grey;
                h[i + 2] = (byte)Math.Min(255, grey + 120);
                continue;
            }

            still++;
            int dr = Math.Abs(a[i] - b[i]);
            int dg = Math.Abs(a[i + 1] - b[i + 1]);
            int db = Math.Abs(a[i + 2] - b[i + 2]);
            absSum += (dr + dg + db) / 3.0;
            stockLuma += Luma(a, i);
            sharpLuma += Luma(b, i);

            if (Math.Max(dr, Math.Max(dg, db)) > settings.Threshold)
            {
                differing++;
                h[i] = 255;
                h[i + 1] = (byte)(grey / 2);
                h[i + 2] = (byte)(grey / 2);
            }
            else
            {
                h[i] = h[i + 1] = h[i + 2] = (byte)grey;
            }
        }

        double stillPercent = 100.0 * still / moving.Length;
        double differingPercent = still == 0 ? 0 : 100.0 * differing / still;
        double meanAbs = still == 0 ? 0 : 100.0 * absSum / still / 255.0;
        double ratio = stockLuma == 0 ? (sharpLuma == 0 ? 1 : double.PositiveInfinity) : sharpLuma / stockLuma;

        Verdict verdict = stillPercent < settings.MinimumStillPercent
            ? Verdict.Inconclusive
            : differingPercent <= settings.TolerancePercent ? Verdict.Pass : Verdict.Fail;

        return new ShotScore(stillPercent, differingPercent, meanAbs, ratio, verdict);
    }

    private static double Luma(byte[] pixels, int i) =>
        (0.2126 * pixels[i]) + (0.7152 * pixels[i + 1]) + (0.0722 * pixels[i + 2]);

    private static bool[] Moving(RgbImage stockA, RgbImage stockB, RgbImage sharpA, RgbImage sharpB, int threshold)
    {
        var moving = new bool[stockA.Width * stockA.Height];
        for (int p = 0, i = 0; p < moving.Length; p++, i += 3)
            moving[p] = Differs(stockA.Pixels, stockB.Pixels, i, threshold) || Differs(sharpA.Pixels, sharpB.Pixels, i, threshold);
        return moving;
    }

    private static bool Differs(byte[] a, byte[] b, int i, int threshold) =>
        Math.Abs(a[i] - b[i]) > threshold
        || Math.Abs(a[i + 1] - b[i + 1]) > threshold
        || Math.Abs(a[i + 2] - b[i + 2]) > threshold;

    /// <summary>The mask grown by <paramref name="radius"/> pixels in a square, as two separable passes.</summary>
    internal static bool[] Dilate(bool[] mask, int width, int height, int radius)
    {
        if (radius <= 0)
            return mask;

        var across = new bool[mask.Length];
        for (int y = 0; y < height; y++)
        {
            int last = int.MinValue / 2;
            for (int x = 0; x < width; x++)
            {
                if (mask[(y * width) + x])
                    last = x;
                across[(y * width) + x] = x - last <= radius;
            }

            last = int.MaxValue / 2;
            for (int x = width - 1; x >= 0; x--)
            {
                if (mask[(y * width) + x])
                    last = x;
                if (last - x <= radius)
                    across[(y * width) + x] = true;
            }
        }

        var grown = new bool[mask.Length];
        for (int x = 0; x < width; x++)
        {
            int last = int.MinValue / 2;
            for (int y = 0; y < height; y++)
            {
                if (across[(y * width) + x])
                    last = y;
                grown[(y * width) + x] = y - last <= radius;
            }

            last = int.MaxValue / 2;
            for (int y = height - 1; y >= 0; y--)
            {
                if (across[(y * width) + x])
                    last = y;
                if (last - y <= radius)
                    grown[(y * width) + x] = true;
            }
        }

        return grown;
    }

    private static void RequireSameSize(params RgbImage[] images)
    {
        foreach (var image in images)
        {
            if (image.Width != images[0].Width || image.Height != images[0].Height)
                throw new ArgumentException(
                    $"screenshots differ in size: {image.Width}x{image.Height} against {images[0].Width}x{images[0].Height}");
        }
    }
}
