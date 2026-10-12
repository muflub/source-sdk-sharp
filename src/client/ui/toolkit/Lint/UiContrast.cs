using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>WCAG 2 relative luminance and contrast, and alpha compositing: pure functions.</summary>
public static class UiContrast
{
    private static float Channel(byte v)
    {
        float c = v / 255f;
        return c <= 0.03928f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    public static float Luminance(UiColor c) => 0.2126f * Channel(c.R) + 0.7152f * Channel(c.G) + 0.0722f * Channel(c.B);

    /// <summary>The WCAG contrast ratio, 1..21.</summary>
    public static float Ratio(UiColor a, UiColor b)
    {
        float la = Luminance(a), lb = Luminance(b);
        return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
    }

    /// <summary><paramref name="top"/> (straight alpha) over an opaque <paramref name="under"/>.</summary>
    public static UiColor Over(UiColor top, UiColor under)
    {
        float a = top.A / 255f;
        return new UiColor(
            (byte)MathF.Round(top.R * a + under.R * (1 - a)),
            (byte)MathF.Round(top.G * a + under.G * (1 - a)),
            (byte)MathF.Round(top.B * a + under.B * (1 - a)), 255);
    }
}
