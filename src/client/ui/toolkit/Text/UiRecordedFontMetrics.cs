using System.Globalization;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// Font metrics as the game measured them (plan_client_ui §5.4): the rows of
/// <c>src/test/client/Fixtures/ui-fonts.tsv</c>, recorded from
/// <c>/client/{slot}/ui/fonts?sweep=1</c>, so a preview lays text out with the
/// widths vgui draws it at. A font or character the recording does not hold is
/// measured by <c>fallback</c> (the TTF metrics).
/// </summary>
public sealed class UiRecordedFontMetrics : IUiFontMetrics
{
    private readonly record struct Row(int Tall, int Ascent, int First, int[] Advances);

    private readonly IUiFontMetrics _fallback;
    private readonly Dictionary<(string Family, int Px, int Weight), Row> _rows = [];

    /// <param name="tsv">family, px, weight, flags, tall, ascent, first char, advances (comma-separated); # comments.</param>
    public UiRecordedFontMetrics(IUiFontMetrics fallback, string tsv)
    {
        _fallback = fallback;
        foreach (string raw in tsv.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] f = line.Split('\t');
            if (f.Length != 8)
                throw new InvalidDataException($"ui-fonts row of {f.Length} fields, not 8: {line}");
            int I(int i) => int.Parse(f[i], CultureInfo.InvariantCulture);
            int[] advances = f[7].Split(',').Select(a => int.Parse(a, CultureInfo.InvariantCulture)).ToArray();
            _rows[(f[0], I(1), I(2))] = new Row(I(4), I(5), I(6), advances);
        }
    }

    /// <summary>The recording at <paramref name="path"/>, or <paramref name="fallback"/> alone when there is none yet.</summary>
    public static IUiFontMetrics LoadOrFallback(IUiFontMetrics fallback, string path)
        => File.Exists(path) ? new UiRecordedFontMetrics(fallback, File.ReadAllText(path)) : fallback;

    public int Count => _rows.Count;

    public bool Holds(in UiFontSpec font) => _rows.ContainsKey((font.Family, font.PxHeight, font.Weight));

    public UiFontMetrics Metrics(in UiFontSpec font)
        => _rows.TryGetValue((font.Family, font.PxHeight, font.Weight), out var r) && r.Tall > 0
            ? new UiFontMetrics(r.Ascent, r.Tall - r.Ascent, r.Tall)
            : _fallback.Metrics(font);

    public float Advance(in UiFontSpec font, char c)
    {
        if (_rows.TryGetValue((font.Family, font.PxHeight, font.Weight), out var r))
        {
            int i = c - r.First;
            if ((uint)i < (uint)r.Advances.Length && (r.Advances[i] > 0 || c == ' '))
                return r.Advances[i];
        }
        return _fallback.Advance(font, c);
    }
}
