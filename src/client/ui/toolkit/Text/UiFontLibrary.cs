using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The UI's fonts, measured from their TrueType files (plan_client_ui §5.4,
/// decision 6): the faces under src/client/ui/fonts, embedded in this assembly
/// so previews and tests measure exactly what a rasteriser of the same files
/// draws. In game the vgui backend answers metrics from the engine's own fonts;
/// this is the preview's source until those are recorded.
///
/// <para>
/// A family the library does not have falls back to IBM Plex Sans; a weight it
/// does not have resolves to the nearest it does.
/// </para>
/// </summary>
public sealed class UiFontLibrary : IUiFontMetrics
{
    public const string Condensed = "IBM Plex Sans Condensed";
    public const string Sans = "IBM Plex Sans";
    public const string Mono = "IBM Plex Mono";

    private readonly TtfFace[] _faces;

    public UiFontLibrary()
    {
        var asm = typeof(UiFontLibrary).Assembly;
        _faces =
        [
            Load(asm, "IBMPlexSansCondensed-Regular.ttf", Condensed, 400),
            Load(asm, "IBMPlexSansCondensed-Medium.ttf", Condensed, 500),
            Load(asm, "IBMPlexSansCondensed-SemiBold.ttf", Condensed, 600),
            Load(asm, "IBMPlexSansCondensed-Bold.ttf", Condensed, 700),
            Load(asm, "IBMPlexSans-Regular.ttf", Sans, 400),
            Load(asm, "IBMPlexSans-Medium.ttf", Sans, 500),
            Load(asm, "IBMPlexMono-Regular.ttf", Mono, 400),
            Load(asm, "IBMPlexMono-Medium.ttf", Mono, 500),
        ];
    }

    private static TtfFace Load(System.Reflection.Assembly asm, string file, string family, int weight)
    {
        using var stream = asm.GetManifestResourceStream("fonts/" + file)
            ?? throw new InvalidOperationException($"font {file} is not embedded in {asm.GetName().Name}");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return new TtfFace(family, weight, bytes);
    }

    public IReadOnlyList<TtfFace> Faces => _faces;

    /// <summary>The face <paramref name="font"/> is drawn with.</summary>
    public TtfFace Face(in UiFontSpec font)
    {
        TtfFace? best = null;
        int bestDistance = int.MaxValue;
        bool familyKnown = false;
        foreach (var f in _faces)
            familyKnown |= f.Family == font.Family;

        string family = familyKnown ? font.Family : Sans;
        foreach (var f in _faces)
        {
            if (f.Family != family)
                continue;
            int d = Math.Abs(f.Weight - font.Weight);
            if (d < bestDistance)
            {
                best = f;
                bestDistance = d;
            }
        }
        return best!;
    }

    public UiFontMetrics Metrics(in UiFontSpec font)
    {
        var f = Face(font);
        float k = font.PxHeight / (float)f.UnitsPerEm;
        float ascent = f.Ascender * k, descent = -f.Descender * k;
        return new UiFontMetrics(ascent, descent, ascent + descent);
    }

    public float Advance(in UiFontSpec font, char c)
    {
        var f = Face(font);
        return f.AdvanceUnits(c) * font.PxHeight / (float)f.UnitsPerEm;
    }
}
