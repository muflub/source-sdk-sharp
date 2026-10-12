using System.Buffers.Binary;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The metrics of one TrueType face, read from its file: units per em, the
/// horizontal header's ascender and descender, and each glyph's advance through
/// the character map. Enough to lay text out exactly as a rasteriser of the same
/// file will advance it (kerning aside); nothing here draws.
/// </summary>
public sealed class TtfFace
{
    private readonly ushort[] _advances;
    private readonly Dictionary<int, ushort> _glyphs = new();

    public TtfFace(string family, int weight, byte[] data)
    {
        Family = family;
        Weight = weight;
        Data = data;

        var tables = Tables(data);
        var head = tables["head"];
        UnitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(head + 18));

        var hhea = tables["hhea"];
        Ascender = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(hhea + 4));
        Descender = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(hhea + 6));
        LineGap = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(hhea + 8));
        int metrics = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(hhea + 34));

        int numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tables["maxp"] + 4));
        _advances = new ushort[numGlyphs];
        var hmtx = tables["hmtx"];
        ushort last = 0;
        for (int g = 0; g < numGlyphs; g++)
        {
            if (g < metrics)
                last = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(hmtx + 4 * g));
            _advances[g] = last;
        }

        ReadCmap(data, tables["cmap"]);
    }

    public string Family { get; }

    public int Weight { get; }

    /// <summary>The file, for a backend that rasterises from the same bytes.</summary>
    public byte[] Data { get; }

    public int UnitsPerEm { get; }

    public int Ascender { get; }

    /// <summary>Negative: below the baseline.</summary>
    public int Descender { get; }

    public int LineGap { get; }

    public bool Has(char c) => _glyphs.ContainsKey(c);

    /// <summary>The advance of <paramref name="c"/> in font units; the missing glyph's for a character the face lacks.</summary>
    public int AdvanceUnits(char c) => _advances[_glyphs.TryGetValue(c, out var g) ? g : 0];

    private static Dictionary<string, int> Tables(byte[] data)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        var tables = new Dictionary<string, int>();
        for (int i = 0; i < count; i++)
        {
            int rec = 12 + 16 * i;
            string tag = System.Text.Encoding.ASCII.GetString(data, rec, 4);
            tables[tag] = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8));
        }
        return tables;
    }

    /// <summary>Reads the Unicode BMP subtable (format 4) into a char → glyph map.</summary>
    private void ReadCmap(byte[] data, int cmap)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cmap + 2));
        int sub = -1;
        for (int i = 0; i < count; i++)
        {
            int rec = cmap + 4 + 8 * i;
            int platform = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rec));
            int encoding = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rec + 2));
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 4));
            int format = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cmap + offset));
            if (format == 4 && (platform == 3 && encoding == 1 || platform == 0))
                sub = cmap + offset;
        }

        if (sub < 0)
            throw new InvalidDataException($"{Family} {Weight}: no format-4 Unicode cmap");

        int segX2 = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(sub + 6));
        int ends = sub + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
        for (int s = 0; s < segX2 / 2; s++)
        {
            int end = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(ends + 2 * s));
            int start = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(starts + 2 * s));
            short delta = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(deltas + 2 * s));
            int rangeAt = ranges + 2 * s;
            int range = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rangeAt));
            for (int c = start; c <= end && c != 0xFFFF; c++)
            {
                int glyph;
                if (range == 0)
                    glyph = (c + delta) & 0xFFFF;
                else
                {
                    int at = rangeAt + range + 2 * (c - start);
                    glyph = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));
                    if (glyph != 0)
                        glyph = (glyph + delta) & 0xFFFF;
                }
                if (glyph != 0 && glyph < _advances.Length)
                    _glyphs[c] = (ushort)glyph;
            }
        }
    }
}
