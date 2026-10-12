using System.Globalization;
using System.IO.Compression;
using System.Reflection;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>One icon of the set: its name and its art's width on the 24-unit height grid.</summary>
public readonly record struct UiIconInfo(string Name, float Width, string PathData)
{
    /// <summary>Width over height.</summary>
    public float Aspect => Width / UiIconSet.GridHeight;
}

/// <summary>One rasterised copy of an icon in the atlas, in atlas pixels.</summary>
public readonly record struct UiAtlasRect(int Icon, int PixelHeight, int X, int Y, int W, int H);

/// <summary>
/// THE ICON SET (src/client/ui/icons): original vector art, one SVG per icon
/// on a 24-unit height grid, and the atlas the build step rasterises from it
/// (tools/ui-icons: every icon at every <see cref="PixelHeights"/>, alpha only,
/// with a manifest). Both are embedded in the toolkit, so previews and the game
/// read the same bytes. The vector sources are what the SVG backend draws; the
/// atlas is what the Skia and vgui backends draw.
///
/// <para>
/// AN ICON IN THE RENDER LIST is a textured quad whose texture id is negative:
/// <see cref="TextureOf"/> packs the atlas rect's index into it, and a backend
/// that sees a negative id draws that rect (or its vector source). The id is a
/// pure function of the rect, so it costs nothing per frame.
/// </para>
/// </summary>
public sealed class UiIconSet : IUiIconAtlas
{
    /// <summary>The art's height grid, in SVG units.</summary>
    public const float GridHeight = 24f;

    /// <summary>The heights the atlas holds each icon at, in pixels.</summary>
    public static ReadOnlySpan<int> PixelHeights => [16, 20, 24, 32, 40, 48, 64, 96];

    private readonly UiIconInfo[] _icons;
    private readonly Dictionary<string, int> _byName;
    private readonly UiAtlasRect[] _rects;

    public UiIconSet(UiIconInfo[] icons, UiAtlasRect[] rects, int atlasWidth, int atlasHeight, byte[] alpha)
    {
        _icons = icons;
        _rects = rects;
        _byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < icons.Length; i++)
            _byName[icons[i].Name] = i;
        AtlasWidth = atlasWidth;
        AtlasHeight = atlasHeight;
        Alpha = alpha;
    }

    public int Count => _icons.Length;

    public UiIconInfo Icon(int i) => _icons[i];

    /// <summary>The icon's index, or -1.</summary>
    public int IndexOf(string name) => _byName.TryGetValue(name, out int i) ? i : -1;

    public int AtlasWidth { get; }

    public int AtlasHeight { get; }

    /// <summary>The atlas, one alpha byte a pixel, row by row: white where the art is.</summary>
    public byte[] Alpha { get; }

    public ReadOnlySpan<UiAtlasRect> Rects => _rects;

    /// <summary>The atlas rect for <paramref name="icon"/> drawn <paramref name="pixelHeight"/> tall: the smallest at least that tall, else the largest.</summary>
    public int RectFor(int icon, float pixelHeight)
    {
        int best = -1, largest = -1;
        for (int i = 0; i < _rects.Length; i++)
        {
            ref readonly var r = ref _rects[i];
            if (r.Icon != icon)
                continue;
            if (largest < 0 || r.PixelHeight > _rects[largest].PixelHeight)
                largest = i;
            if (r.PixelHeight >= pixelHeight && (best < 0 || r.PixelHeight < _rects[best].PixelHeight))
                best = i;
        }
        return best >= 0 ? best : largest;
    }

    /// <summary>The render list's texture id for atlas rect <paramref name="rect"/>: negative, so no surface texture is mistaken for it.</summary>
    public static int TextureOf(int rect) => -1 - rect;

    /// <summary>The atlas rect a texture id names, or -1 when it is a surface texture.</summary>
    public static int RectOf(int texture) => texture < 0 ? -1 - texture : -1;

    // ---- the embedded set ----

    /// <summary>The manifest's resource name and the atlas's: written by tools/ui-icons.</summary>
    public const string ManifestResource = "icons/icons.tsv", AtlasResource = "icons/icons.a8z", ArtPrefix = "icons/art/";

    /// <summary>The set the toolkit carries: the SVG sources, the manifest and the atlas, as embedded.</summary>
    public static UiIconSet Embedded()
    {
        var asm = typeof(UiIconSet).Assembly;
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string res in asm.GetManifestResourceNames())
        {
            if (!res.StartsWith(ArtPrefix, StringComparison.Ordinal) || !res.EndsWith(".svg", StringComparison.Ordinal))
                continue;
            string name = res[ArtPrefix.Length..^4];
            sources[name] = Read(asm, res);
        }
        return Load(sources, Read(asm, ManifestResource), ReadBytes(asm, AtlasResource));
    }

    /// <summary>A set from its parts: each icon's SVG source by name, the manifest's text and the compressed atlas.</summary>
    public static UiIconSet Load(IReadOnlyDictionary<string, string> sources, string manifest, byte[] compressedAtlas)
    {
        var icons = sources.Select(kv => ParseSvg(kv.Key, kv.Value)).OrderBy(i => i.Name, StringComparer.Ordinal).ToArray();
        var index = icons.Select((ic, i) => (ic.Name, i)).ToDictionary(t => t.Name, t => t.i, StringComparer.Ordinal);
        int w = 0, h = 0;
        var rects = new List<UiAtlasRect>();
        foreach (string raw in manifest.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
                continue;
            var f = line.Split('\t');
            if (f[0] == "atlas")
            {
                w = int.Parse(f[1], CultureInfo.InvariantCulture);
                h = int.Parse(f[2], CultureInfo.InvariantCulture);
                continue;
            }
            if (!index.TryGetValue(f[0], out int icon))
                throw new InvalidDataException($"icons.tsv names '{f[0]}', which has no SVG: rebuild the atlas (make ui-icons)");
            rects.Add(new UiAtlasRect(icon, int.Parse(f[1], CultureInfo.InvariantCulture), int.Parse(f[2], CultureInfo.InvariantCulture),
                                      int.Parse(f[3], CultureInfo.InvariantCulture), int.Parse(f[4], CultureInfo.InvariantCulture),
                                      int.Parse(f[5], CultureInfo.InvariantCulture)));
        }
        byte[] alpha;
        using (var z = new ZLibStream(new MemoryStream(compressedAtlas), CompressionMode.Decompress))
        using (var m = new MemoryStream())
        {
            z.CopyTo(m);
            alpha = m.ToArray();
        }
        if (alpha.Length != w * h)
            throw new InvalidDataException($"the icon atlas is {alpha.Length} bytes, the manifest says {w}x{h}");
        return new UiIconSet(icons, [.. rects], w, h, alpha);
    }

    /// <summary>One icon's SVG: the set's subset only (a viewBox 0 0 W 24 and paths' d, filled even-odd).</summary>
    public static UiIconInfo ParseSvg(string name, string svg)
    {
        string viewBox = Attribute(svg, "viewBox") ?? throw new InvalidDataException($"{name}.svg has no viewBox");
        var v = viewBox.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (v.Length != 4 || v[0] != "0" || v[1] != "0" || v[3] != "24")
            throw new InvalidDataException($"{name}.svg's viewBox is '{viewBox}': the set's is 0 0 W 24");
        var d = new List<string>();
        for (int at = svg.IndexOf("<path", StringComparison.Ordinal); at >= 0; at = svg.IndexOf("<path", at + 5, StringComparison.Ordinal))
            d.Add(Attribute(svg[at..], "d") ?? throw new InvalidDataException($"{name}.svg has a path with no d"));
        if (d.Count == 0)
            throw new InvalidDataException($"{name}.svg has no path");
        return new UiIconInfo(name, float.Parse(v[2], CultureInfo.InvariantCulture), string.Join(' ', d));
    }

    private static string? Attribute(string text, string name)
    {
        int at = text.IndexOf(" " + name + "=\"", StringComparison.Ordinal);
        if (at < 0)
            return null;
        at += name.Length + 3;
        int end = text.IndexOf('"', at);
        return end < 0 ? null : text[at..end];
    }

    private static string Read(Assembly asm, string res)
    {
        using var s = asm.GetManifestResourceStream(res) ?? throw new InvalidDataException($"no embedded {res}");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private static byte[] ReadBytes(Assembly asm, string res)
    {
        using var s = asm.GetManifestResourceStream(res) ?? throw new InvalidDataException($"no embedded {res}");
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }
}
