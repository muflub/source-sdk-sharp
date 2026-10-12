//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>One corner of a glyph quad: where, the texture coordinate, the colour bytes.</summary>
public struct WorldTextVertex
{
    public Vector Position;
    public float S, T;
    public byte R, G, B, A;
}

/// <summary>
/// <c>C_PointWorldText</c>'s layout (c_point_worldtext.cpp, carried in
/// shell_worldtext_sim.cpp until lane c2-sims): the five baked glyph tables
/// and the thirteen fonts that name them, the text's world size and render
/// bounds, and each frame's glyph quads (four corners, stock's corner order,
/// with stock's rainbow colouring). The native renderer builds what this lays
/// out. Replayed bit for bit against <c>Fixtures/worldtext-stock.tsv</c>.
/// </summary>
public sealed class WorldTextLayout
{
    /// <summary><c>m_szText</c>'s size, <c>MAX_PATH</c>: stock copies at most this less one byte.</summary>
    public const int MaxTextBytes = 260;

    /// <summary><c>g_mapfonts</c>: (glyph table, material) per font index.</summary>
    public const int FontCount = 13;

    public static string MaterialOf(int font) => "editor/worldtext_" + (Math.Clamp(font, 0, FontCount - 1) + 1);

    /// <summary>The glyph table a font index draws with: 0 TF2 Build, 1 TF2, 2 Liberation Sans, 3 TF2 Professor, 4 Roboto Mono.</summary>
    private static int TableOf(int font) => Math.Clamp(font, 0, FontCount - 1) switch
    {
        0 or 1 or 12 => 0,
        2 or 3 => 1,
        4 or 5 => 2,
        6 or 7 => 3,
        _ => 4,
    };

    // The tables' heads: size, then the texture's width and height.
    private static int TableSize(int table) => table >= 3 ? 128 : 64;

    private static int TableWidth(int table) => table switch { 2 => 859, 4 => 2048, _ => 1024 };

    private static int TableHeight(int table) => table switch { 1 => 256, 2 => 290, _ => 512 };

    private static ReadOnlySpan<short> Table(int table) => table switch
    {
        0 => Tf2Build,
        1 => Tf2,
        2 => Liberationsans,
        3 => Tf2Professor,
        _ => RobotoMono,
    };

    /// <summary>One glyph: x, y, width, height, originX, originY, advance.</summary>
    private readonly record struct Glyph(int X, int Y, int Width, int Height, int OriginX, int OriginY, int Advance);

    /// <summary>
    /// The glyph for a text byte: stock's <c>Clamp( (unsigned)nChar - 32, 0, 94 )</c>
    /// on a SIGNED char, so a control byte or one past 127 wraps high and draws
    /// as <c>'~'</c>.
    /// </summary>
    private static Glyph GlyphOf(int table, byte b)
    {
        uint idx = unchecked((uint)(sbyte)b - 32u);
        if (idx > 94u)
            idx = 94u;
        var t = Table(table).Slice((int)idx * 7, 7);
        return new Glyph(t[0], t[1], t[2], t[3], t[4], t[5], t[6]);
    }

    private byte[] _text = [];

    public float TextSize = 10.0f;
    public float SpacingX, SpacingY;
    public byte R = 255, G = 255, B = 255, A = 255;
    public int Orientation;
    public bool Rainbow;

    /// <summary>The clamped font index (<c>SetFont</c>'s clamp).</summary>
    public int Font { get; private set; }

    public float WorldWidth { get; private set; }

    public float WorldHeight { get; private set; }

    public Vector BoundsMin { get; private set; }

    public Vector BoundsMax { get; private set; }

    public int TextLength => _text.Length;

    /// <summary>
    /// Stock's <c>SourceSharpUpdate</c>: the styling, then <c>SetFont</c> (the
    /// clamp), <c>SetText</c> (the UTF-8 bytes, at most <see cref="MaxTextBytes"/>
    /// less one) and the remeasure, then the bounds.
    /// </summary>
    public void Update(string text, float textSize, float spacingX, float spacingY, uint color, int orientation, int font, bool rainbow)
    {
        TextSize = textSize;
        SpacingX = spacingX;
        SpacingY = spacingY;
        R = (byte)color; G = (byte)(color >> 8); B = (byte)(color >> 16); A = (byte)(color >> 24);
        Orientation = orientation;
        Rainbow = rainbow;
        Font = Math.Clamp(font, 0, FontCount - 1);

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        int nul = Array.IndexOf(bytes, (byte)0);
        int n = Math.Min(nul >= 0 ? nul : bytes.Length, MaxTextBytes - 1);
        _text = bytes[..n];

        Measure();
        UpdateRenderBounds();
    }

    /// <summary><c>CalcTextTotalSize</c>: the height starts at the table's UNSCALED size, as stock's does.</summary>
    private void Measure()
    {
        float width = 0.0f, height = 0.0f;
        if (_text.Length > 0)
        {
            int table = TableOf(Font);
            int size = TableSize(table);
            height += size;
            float lineWidth = 0.0f;
            foreach (byte b in _text)
            {
                Glyph g = GlyphOf(table, b);
                float scale = TextSize / (float)size;
                if (b == (byte)'\n')
                {
                    width = MathF.Max(width, lineWidth);
                    lineWidth = 0.0f;
                    height += (size + SpacingY) * scale;
                    continue;
                }

                lineWidth += (g.Advance + SpacingX) * scale;
            }

            width = MathF.Max(width, lineWidth);
        }

        WorldWidth = width;
        WorldHeight = height;
    }

    /// <summary>
    /// <c>UpdateRenderBounds</c>, local: orientation 0 takes the quad at zero
    /// angles (stock's own choice, whatever the entity's angles are); a
    /// view-facing one takes a cube of half the width.
    /// </summary>
    private void UpdateRenderBounds()
    {
        Span<Vector> c = stackalloc Vector[4];
        if (Orientation == 0)
        {
            MoveVec.AngleVectors(default, out _, out Vector right, out Vector up);
            c[0] = default(Vector) - (right + up);
            c[1] = c[0] + right * WorldWidth;
            c[2] = c[1] + up * WorldHeight;
            c[3] = c[0] + up * WorldHeight;
        }
        else
        {
            float w = WorldWidth * 0.5f;
            c[0] = new Vector(-w, -w, -w);
            c[1] = c[0];
            c[2] = new Vector(w, w, w);
            c[3] = c[2];
        }

        BoundsMin = ParticleMath.Min(ParticleMath.Min(c[0], c[1]), ParticleMath.Min(c[2], c[3]));
        BoundsMax = ParticleMath.Max(ParticleMath.Max(c[0], c[1]), ParticleMath.Max(c[2], c[3]));
    }

    /// <summary>
    /// <c>DrawModel</c>'s layout: four corners per drawn glyph (a space and a
    /// newline draw none), in stock's order, from the entity's place or the
    /// view's basis by orientation. <paramref name="viewForward"/> is unused by
    /// the layout, as stock's is; it is taken so the caller hands the whole basis.
    /// </summary>
    /// <returns>The vertex count; the vertices past <paramref name="out"/>'s length are counted, not written.</returns>
    public int Layout(Vector origin, Vector angles, Vector viewRight, Vector viewUp, float curTime,
                      float rainbowSpeedTime, float rainbowSpeedChar, Span<WorldTextVertex> @out)
    {
        if (_text.Length == 0)
            return 0;

        Vector right, up;
        Vector start = origin;
        switch (Orientation)
        {
            case 1:
                up = viewUp;
                right = viewRight;
                start = start - right * (WorldWidth * 0.5f);
                break;
            case 2:
                up = new Vector(0, 0, 1);
                right = viewRight;
                start = start - right * (WorldWidth * 0.5f);
                break;
            default:
                MoveVec.AngleVectors(angles, out _, out right, out up);
                break;
        }

        int table = TableOf(Font);
        int size = TableSize(table);
        float texW = TableWidth(table), texH = TableHeight(table);
        Vector origStart = start;
        int n = 0;
        for (int i = 0; i < _text.Length; i++)
        {
            byte b = _text[i];
            Glyph g = GlyphOf(table, b);
            float scale = TextSize / (float)size;
            if (b == (byte)'\n')
            {
                origStart = origStart - up * ((size + SpacingY) * scale);
                start = origStart;
                continue;
            }

            byte r = R, gr = G, bl = B;
            if (Rainbow)
            {
                float h = curTime * rainbowSpeedTime + i * rainbowSpeedChar;
                h = h % 360.0f;
                var (dr, dg, db) = HsvToRgb(h, 0.7, 1.0);
                r = (byte)(dr * 255.0f);
                gr = (byte)(dg * 255.0f);
                bl = (byte)(db * 255.0f);
            }

            if (b != (byte)' ')
            {
                Corner(@out, n++, start, right, up, scale, -g.OriginX, -g.OriginY, g.X / texW, g.Y / texH, r, gr, bl);
                Corner(@out, n++, start, right, up, scale, -g.OriginX, -g.OriginY + g.Height, g.X / texW, (g.Y + g.Height) / texH, r, gr, bl);
                Corner(@out, n++, start, right, up, scale, -g.OriginX + g.Width, -g.OriginY + g.Height, (g.X + g.Width) / texW, (g.Y + g.Height) / texH, r, gr, bl);
                Corner(@out, n++, start, right, up, scale, -g.OriginX + g.Width, -g.OriginY, (g.X + g.Width) / texW, g.Y / texH, r, gr, bl);
            }

            start = start + right * ((g.Advance + SpacingX) * scale);
        }

        return n;
    }

    private void Corner(Span<WorldTextVertex> @out, int at, Vector start, Vector right, Vector up, float scale,
                        float x, float y, float s, float t, byte r, byte g, byte b)
    {
        if (at >= @out.Length)
            return;
        @out[at] = new WorldTextVertex
        {
            Position = start + right * x * scale + up * (-y) * scale,
            S = s, T = t, R = r, G = g, B = b, A = A,
        };
    }

    /// <summary>Stock's <c>hsv2rgb</c>, in double as stock's is.</summary>
    public static (double R, double G, double B) HsvToRgb(double h, double s, double v)
    {
        if (h == 360.0)
            h = 0.0;
        else
            h /= 60.0;
        double fract = h - Math.Floor(h);
        double p = v * (1.0 - s);
        double q = v * (1.0 - s * fract);
        double t = v * (1.0 - s * (1.0 - fract));
        if (0.0 <= h && h < 1.0)
            return (v, t, p);
        if (1.0 <= h && h < 2.0)
            return (q, v, p);
        if (2.0 <= h && h < 3.0)
            return (p, v, t);
        if (3.0 <= h && h < 4.0)
            return (p, q, v);
        if (4.0 <= h && h < 5.0)
            return (t, p, v);
        if (5.0 <= h && h < 6.0)
            return (v, p, q);
        return (0.0, 0.0, 0.0);
    }

    public void Save(BinaryWriter w)
    {
        w.Write(_text.Length); w.Write(_text);
        w.Write(TextSize); w.Write(SpacingX); w.Write(SpacingY);
        w.Write(R); w.Write(G); w.Write(B); w.Write(A);
        w.Write(Orientation); w.Write(Rainbow); w.Write(Font);
    }

    public void Restore(BinaryReader r)
    {
        _text = r.ReadBytes(r.ReadInt32());
        TextSize = r.ReadSingle(); SpacingX = r.ReadSingle(); SpacingY = r.ReadSingle();
        R = r.ReadByte(); G = r.ReadByte(); B = r.ReadByte(); A = r.ReadByte();
        Orientation = r.ReadInt32(); Rainbow = r.ReadBoolean(); Font = r.ReadInt32();
        Measure();
        UpdateRenderBounds();
    }

    /// <summary><c>characters_TF2_Build</c>: x, y, width, height, originX, originY, advance per glyph, from <c>' '</c> to <c>'~'</c>.</summary>
    private static ReadOnlySpan<short> Tf2Build =>
    [
        146,238,12,12,6,6,25, 67,182,24,56,6,50,16, 0,238,24,24,6,54,15, 609,182,51,50,6,47,42,
        477,182,34,54,6,49,25, 564,182,45,51,6,47,36, 636,0,49,58,6,50,39, 1000,182,19,25,6,54,10,
        118,0,34,62,6,54,25, 152,0,34,62,6,54,25, 861,182,34,34,6,54,25, 782,182,36,37,6,40,27,
        978,182,22,26,6,16,12, 70,238,32,19,6,30,23, 24,238,23,22,6,16,14, 373,182,36,55,6,50,27,
        91,182,52,55,6,50,43, 409,182,32,55,5,50,25, 239,182,46,55,6,50,37, 192,182,47,55,6,50,37,
        143,182,49,55,6,50,41, 330,182,43,55,6,50,34, 0,182,40,56,6,51,30, 285,182,45,55,6,50,36,
        961,125,45,56,6,50,36, 873,0,42,58,6,50,32, 760,182,22,43,6,41,14, 738,182,22,48,6,42,13,
        660,182,39,49,6,46,30, 895,182,39,28,6,36,29, 699,182,39,49,6,46,30, 294,125,47,57,6,51,38,
        511,182,53,53,6,47,44, 596,68,54,57,6,51,45, 341,125,47,57,6,51,38, 534,0,51,58,6,51,42,
        486,68,55,57,6,51,46, 102,125,48,57,6,51,39, 150,125,48,57,6,51,39, 916,68,51,57,6,51,43,
        258,68,57,57,6,51,48, 915,0,26,58,4,51,18, 709,125,39,57,6,51,30, 529,125,46,57,6,51,37,
        748,125,39,57,6,51,32, 142,68,58,57,6,51,49, 967,68,51,57,6,51,42, 186,0,59,58,6,52,50,
        685,0,47,58,6,51,38, 0,0,59,68,6,51,50, 388,125,47,57,6,51,38, 732,0,47,58,6,51,38,
        812,68,52,57,6,51,43, 315,68,57,57,6,51,48, 245,0,59,58,6,52,49, 0,68,71,57,6,51,61,
        650,68,54,57,6,51,46, 422,0,56,58,6,51,47, 621,125,44,57,6,51,38, 892,125,25,57,6,51,16,
        441,182,36,54,6,50,27, 917,125,25,57,6,51,16, 818,182,43,34,6,54,34, 102,238,44,18,6,3,35,
        47,238,23,21,6,54,10, 704,68,54,57,6,51,45, 435,125,47,57,6,51,38, 585,0,51,58,6,51,42,
        541,68,55,57,6,51,46, 198,125,48,57,6,51,39, 246,125,48,57,6,51,39, 0,125,51,57,6,51,43,
        372,68,57,57,6,51,48, 941,0,26,58,4,51,21, 787,125,39,57,6,51,30, 575,125,46,57,6,51,37,
        826,125,39,57,6,51,30, 200,68,58,57,6,51,49, 51,125,51,57,6,51,42, 304,0,59,58,6,52,50,
        779,0,47,58,6,51,38, 59,0,59,68,6,51,50, 482,125,47,57,6,51,38, 826,0,47,58,6,51,38,
        864,68,52,57,6,51,43, 429,68,57,57,6,51,48, 363,0,59,58,6,52,49, 71,68,71,57,6,51,61,
        758,68,54,57,6,51,46, 478,0,56,58,6,51,47, 665,125,44,57,6,51,38, 40,182,27,56,6,51,18,
        942,125,19,57,0,51,16, 865,125,27,57,6,51,18, 934,182,44,26,6,35,34,
    ];

    /// <summary><c>characters_TF2</c>: x, y, width, height, originX, originY, advance per glyph, from <c>' '</c> to <c>'~'</c>.</summary>
    private static ReadOnlySpan<short> Tf2 =>
    [
        423,175,12,12,6,6,22, 478,118,20,55,4,51,11, 313,175,18,19,6,53,8, 104,175,29,29,6,38,20,
        0,0,61,61,6,54,49, 913,118,33,47,6,46,24, 946,118,36,44,4,44,30, 268,175,17,20,6,53,8,
        884,61,28,57,5,51,19, 912,61,28,57,6,51,18, 155,175,26,27,6,54,17, 76,175,28,31,6,39,19,
        225,175,17,21,5,11,8, 331,175,26,17,6,30,18, 357,175,17,17,5,13,9, 607,118,31,51,6,49,22,
        638,118,43,50,6,47,34, 888,118,25,49,6,47,16, 681,118,41,50,6,48,32, 762,118,39,50,6,47,30,
        722,118,40,50,6,48,31, 837,118,35,50,6,48,26, 538,118,36,51,6,49,27, 801,118,36,50,6,47,27,
        498,118,40,51,5,48,31, 574,118,33,51,6,48,24, 57,175,19,36,6,39,10, 39,175,18,39,6,38,9,
        181,175,21,27,6,36,12, 285,175,28,19,6,32,19, 133,175,22,28,6,36,13, 435,118,43,55,6,49,34,
        0,175,39,39,6,38,30, 817,0,43,57,6,51,32, 510,61,41,57,6,51,31, 242,118,42,56,5,50,33,
        150,118,46,56,6,50,36, 258,61,42,57,6,51,32, 300,61,42,57,6,51,32, 860,0,43,57,5,51,35,
        342,61,42,57,6,51,33, 965,61,23,57,6,51,14, 822,61,31,57,6,51,23, 903,0,43,57,6,51,32,
        551,61,41,57,6,51,31, 359,0,48,57,6,51,39, 946,0,43,57,6,51,34, 161,0,50,57,5,51,41,
        674,61,40,57,6,51,31, 61,0,50,61,6,51,40, 261,0,49,57,6,51,38, 754,61,34,57,6,51,24,
        639,0,45,57,6,51,35, 0,61,43,57,6,51,34, 729,0,44,57,6,51,34, 44,118,53,56,6,50,43,
        455,0,46,57,6,51,36, 501,0,46,57,6,51,35, 284,118,42,56,6,50,32, 0,118,22,57,6,51,11,
        982,118,28,44,5,42,18, 22,118,22,57,6,51,12, 202,175,23,22,6,48,14, 391,175,32,16,6,6,23,
        374,175,17,17,3,56,9, 43,61,43,57,6,51,32, 592,61,41,57,6,51,31, 326,118,42,56,5,50,33,
        196,118,46,56,6,50,36, 384,61,42,57,6,51,32, 426,61,42,57,6,51,32, 86,61,43,57,5,51,35,
        468,61,42,57,6,51,33, 988,61,23,57,6,51,14, 853,61,31,57,6,51,23, 129,61,43,57,6,51,32,
        633,61,41,57,6,51,31, 407,0,48,57,6,51,39, 172,61,43,57,6,51,34, 211,0,50,57,5,51,41,
        714,61,40,57,6,51,31, 111,0,50,61,6,51,40, 310,0,49,57,6,51,38, 788,61,34,57,6,51,24,
        684,0,45,57,6,51,35, 215,61,43,57,6,51,34, 773,0,44,57,6,51,34, 97,118,53,56,6,50,43,
        547,0,46,57,6,51,36, 593,0,46,57,6,51,35, 368,118,42,56,6,50,32, 940,61,25,57,6,51,16,
        872,118,16,50,4,48,9, 410,118,25,56,6,50,16, 242,175,26,20,6,30,17,
    ];

    /// <summary><c>characters_LiberationSans</c>: x, y, width, height, originX, originY, advance per glyph, from <c>' '</c> to <c>'~'</c>.</summary>
    private static ReadOnlySpan<short> Liberationsans =>
    [
        388,247,12,12,6,6,18, 68,189,19,58,0,52,18, 156,247,29,28,3,52,22, 705,0,46,60,5,53,35,
        280,0,43,69,4,56,35, 380,0,61,61,2,53,57, 605,0,51,60,3,53,42, 185,247,19,28,3,52,12,
        161,0,27,72,2,53,21, 133,0,28,72,2,53,21, 81,247,33,32,4,53,25, 769,189,42,43,2,44,37,
        204,247,19,27,1,12,18, 291,247,29,18,4,25,21, 320,247,19,18,0,12,18, 751,0,30,60,6,53,18,
        43,72,42,59,3,52,35, 39,189,29,58,-1,52,35, 582,131,43,58,4,52,35, 85,72,42,59,3,52,35,
        538,131,44,58,5,52,35, 625,131,43,58,3,51,35, 0,72,43,59,4,52,35, 711,131,42,58,3,51,35,
        127,72,42,59,3,52,35, 169,72,42,59,3,52,35, 750,189,19,45,0,39,18, 170,189,19,54,1,39,18,
        811,189,42,43,2,44,37, 114,247,42,31,2,38,37, 0,247,42,43,2,44,37, 211,72,42,59,3,53,35,
        0,0,72,72,3,53,65, 696,72,55,58,6,52,42, 398,131,47,58,1,52,42, 552,0,53,60,3,53,46,
        106,131,50,58,1,52,46, 492,131,46,58,1,52,42, 668,131,43,58,1,52,39, 497,0,55,60,3,53,50,
        206,131,48,58,1,52,46, 87,189,19,58,0,52,18, 500,72,38,59,4,52,32, 156,131,50,58,1,52,42,
        753,131,41,58,1,52,35, 640,72,56,58,1,52,53, 254,131,48,58,1,52,46, 441,0,56,60,3,53,50,
        445,131,47,58,1,52,42, 323,0,57,62,3,53,50, 54,131,52,58,1,52,46, 656,0,49,60,3,53,42,
        302,131,48,58,4,52,39, 811,0,48,59,1,52,46, 751,72,54,58,6,52,42, 569,72,71,58,5,52,60,
        805,72,54,58,6,52,42, 0,131,54,58,6,52,42, 350,131,48,58,5,52,39, 205,0,25,71,2,52,18,
        781,0,30,60,6,53,18, 230,0,25,71,5,52,18, 42,247,39,37,4,53,30, 339,247,49,16,7,-3,35,
        267,247,24,21,3,52,21, 189,189,43,47,4,40,35, 295,72,41,59,2,52,35, 318,189,41,47,3,40,32,
        336,72,41,59,4,52,35, 232,189,43,47,4,40,35, 538,72,31,59,5,53,18, 377,72,41,59,4,40,35,
        0,189,39,58,2,52,35, 106,189,18,58,2,52,14, 255,0,25,71,9,52,14, 794,131,40,58,2,52,32,
        124,189,18,58,2,52,14, 399,189,57,46,2,40,53, 456,189,39,46,2,40,35, 275,189,43,47,4,40,35,
        418,72,41,59,2,40,35, 459,72,41,59,4,40,35, 534,189,30,46,2,40,21, 359,189,40,47,4,40,32,
        142,189,28,57,5,51,18, 495,189,39,46,2,39,35, 622,189,43,45,5,39,32, 564,189,58,45,6,39,46,
        665,189,43,45,5,39,32, 253,72,42,59,5,39,32, 708,189,42,45,5,39,32, 103,0,30,72,4,53,21,
        188,0,17,72,0,53,16, 72,0,31,72,5,53,21, 223,247,44,23,3,34,37,
    ];

    /// <summary><c>characters_TF2_Professor</c>: x, y, width, height, originX, originY, advance per glyph, from <c>' '</c> to <c>'~'</c>.</summary>
    private static ReadOnlySpan<short> Tf2Professor =>
    [
        1011,251,12,12,6,6,27, 886,104,18,71,2,71,14, 734,251,26,32,2,90,24, 550,182,61,53,2,67,58,
        171,0,43,94,2,88,40, 206,182,70,66,2,80,66, 49,104,51,77,3,82,46, 805,251,24,30,2,84,20,
        135,0,36,96,2,88,32, 214,0,38,90,2,84,34, 633,251,35,37,2,91,32, 410,182,59,55,2,71,56,
        668,251,26,36,2,20,22, 898,251,37,20,2,44,34, 857,251,21,24,2,20,16, 377,182,33,57,1,58,30,
        276,182,54,64,2,59,50, 561,104,41,72,2,64,38, 0,182,49,69,2,63,46, 170,182,36,68,2,60,32,
        842,104,44,71,2,65,40, 516,104,45,72,2,66,42, 642,104,37,72,2,64,34, 793,104,49,71,2,63,44,
        602,104,40,72,3,66,34, 956,104,41,70,1,57,38, 53,251,22,50,2,54,18, 839,182,28,52,2,56,24,
        521,251,38,46,2,68,34, 760,251,45,30,2,56,42, 469,182,41,54,2,70,38, 344,0,48,84,2,78,40,
        71,182,58,68,2,66,54, 918,0,48,79,2,76,43, 343,104,48,74,2,78,44, 148,104,45,77,3,72,40,
        248,104,48,75,2,75,44, 100,104,48,77,4,77,42, 129,182,41,68,3,70,36, 502,0,46,82,3,76,42,
        193,104,55,76,3,74,50, 49,182,22,69,2,73,18, 966,0,41,79,2,75,38, 777,0,53,80,3,76,48,
        0,104,49,78,3,77,44, 679,104,63,71,3,69,58, 904,104,52,70,2,72,48, 742,104,51,71,6,74,44,
        391,104,39,74,3,77,36, 285,0,59,84,3,84,54, 614,0,57,81,2,82,51, 477,104,39,73,2,74,34,
        548,0,66,81,5,80,56, 430,104,47,73,3,77,41, 453,0,49,83,2,81,44, 851,0,67,79,2,76,64,
        296,104,47,75,2,74,44, 671,0,50,81,2,82,46, 721,0,56,80,2,80,52, 252,0,33,87,2,85,30,
        392,0,61,83,2,84,56, 0,0,44,104,2,92,40, 829,251,28,28,2,78,24, 935,251,76,19,2,20,72,
        878,251,20,24,2,89,22, 510,182,40,54,2,48,34, 611,182,35,53,2,48,31, 941,182,32,51,4,47,26,
        120,251,35,49,1,47,32, 905,182,36,51,2,47,32, 646,182,31,53,2,50,28, 867,182,38,51,2,47,33,
        722,182,42,52,2,48,37, 155,251,17,49,1,47,14, 294,251,30,48,2,46,26, 677,182,45,52,2,46,39,
        257,251,37,48,0,47,34, 430,251,51,46,2,42,46, 75,251,45,49,2,47,40, 481,251,40,46,3,43,35,
        803,182,36,52,2,48,31, 559,251,42,45,2,43,35, 219,251,38,48,0,44,33, 324,251,29,48,3,45,24,
        172,251,47,48,5,44,38, 399,251,31,47,2,43,27, 601,251,32,43,2,41,26, 0,251,53,50,4,46,45,
        353,251,46,47,5,45,35, 330,182,47,57,5,51,38, 764,182,39,52,3,48,32, 44,0,47,98,2,92,44,
        830,0,21,80,2,80,18, 91,0,44,98,5,93,34, 694,251,40,33,2,59,36,
    ];

    /// <summary><c>characters_Roboto_Mono</c>: x, y, width, height, originX, originY, advance per glyph, from <c>' '</c> to <c>'~'</c>.</summary>
    private static ReadOnlySpan<short> RobotoMono =>
    [
        167,352,12,12,6,6,77, 675,144,27,104,-24,97,77, 1859,249,44,42,-16,102,77, 702,144,82,103,2,97,77,
        324,0,70,131,-4,112,77, 1322,0,83,106,3,98,77, 1405,0,80,106,-1,98,77, 1903,249,22,42,-25,102,77,
        0,0,45,144,-16,109,77, 45,0,45,144,-14,109,77, 1497,249,72,73,-4,97,77, 1422,249,75,78,-1,81,77,
        1829,249,30,47,-16,20,77, 37,352,60,22,-8,51,77, 2008,249,30,30,-25,22,77, 568,0,60,111,-10,97,77,
        1788,0,71,106,-3,98,77, 342,249,47,103,-7,97,77, 0,144,74,105,1,98,77, 1859,0,70,106,0,98,77,
        1263,144,78,103,1,97,77, 572,144,69,104,-6,97,77, 74,144,70,105,-3,97,77, 1491,144,73,103,-1,97,77,
        1929,0,70,106,-5,98,77, 144,144,70,105,-3,98,77, 459,249,30,85,-28,77,77, 641,144,34,104,-24,77,77,
        1636,249,65,69,-5,75,77, 1761,249,68,48,-5,65,77, 1569,249,67,69,-5,75,77, 283,144,66,105,-6,98,77,
        349,144,80,104,2,97,77, 865,144,80,103,1,97,77, 1852,144,71,103,-5,97,77, 1713,0,75,106,-1,98,77,
        1417,144,74,103,-4,97,77, 71,249,68,103,-5,97,77, 139,249,68,103,-6,97,77, 1485,0,76,106,0,98,77,
        1923,144,71,103,-3,97,77, 275,249,67,103,-5,97,77, 501,144,71,104,0,97,77, 1341,144,76,103,-5,97,77,
        207,249,68,103,-6,97,77, 1564,144,72,103,-3,97,77, 0,249,71,103,-3,97,77, 1561,0,76,106,-1,98,77,
        1636,144,72,103,-6,97,77, 416,0,79,120,0,98,77, 1708,144,72,103,-5,97,77, 1637,0,76,106,-1,98,77,
        945,144,80,103,1,97,77, 429,144,72,104,-3,97,77, 1105,144,79,103,1,97,77, 784,144,81,103,1,97,77,
        1184,144,79,103,0,97,77, 1025,144,80,103,2,97,77, 1780,144,72,103,-1,97,77, 90,0,37,136,-21,110,77,
        628,0,60,111,-9,97,77, 127,0,37,136,-19,110,77, 1701,249,60,61,-9,97,77, 97,352,70,21,-4,6,77,
        0,352,37,29,-20,99,77, 706,249,70,82,-4,75,77, 688,0,70,109,-5,102,77, 635,249,71,82,-3,75,77,
        758,0,69,109,-3,102,77, 563,249,72,82,-2,75,77, 495,0,73,111,-4,105,77, 898,0,69,108,-3,75,77,
        1036,0,68,108,-5,102,77, 214,144,69,105,-7,98,77, 272,0,52,132,-7,98,77, 827,0,71,108,-5,102,77,
        967,0,69,108,-7,102,77, 845,249,78,81,0,75,77, 923,249,68,81,-5,75,77, 489,249,74,82,-2,75,77,
        1184,0,69,107,-5,75,77, 1253,0,69,107,-3,75,77, 1058,249,59,81,-15,75,77, 776,249,69,82,-5,75,77,
        389,249,70,97,-3,90,77, 991,249,67,81,-5,74,77, 1200,249,76,80,0,74,77, 1117,249,83,80,3,74,77,
        1276,249,76,80,-1,74,77, 1104,0,80,107,2,74,77, 1352,249,70,80,-4,74,77, 164,0,54,135,-14,106,77,
        394,0,22,128,-28,97,77, 218,0,54,135,-14,106,77, 1925,249,83,37,3,56,77,
    ];
}
