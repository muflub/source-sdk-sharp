using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// point_worldtext's layout against stock <c>C_PointWorldText</c>, recorded
/// live (<c>Fixtures/worldtext-stock.tsv</c>, README beside it): 150 draws of
/// five texts over the five glyph tables, the three orientations, letter and
/// line spacing and the rainbow. Bit-exact: the world size, the bounds, the
/// material, and every vertex's position, texture coordinate and colour.
/// </summary>
public class WorldTextLayoutOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private static Vector V(string[] c, int at) => new(F(c[at]), F(c[at + 1]), F(c[at + 2]));

    private sealed record Draw(string[] Head, string[] Params, List<string[]> Vertices);

    private static List<Draw> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "worldtext-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock world text is missing: " + path);
        var draws = new List<Draw>();
        string[]? head = null;
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] c = line.Split('\t');
            switch (c[0])
            {
                case "F": head = c; break;
                case "P": draws.Add(new Draw(head!, c, new())); break;
                case "V": draws[^1].Vertices.Add(c); break;
                default: Assert.Fail($"an unknown row kind '{c[0]}'"); break;
            }
        }

        Assert.True(draws.Count >= 150 && draws.Select(d => d.Params[1]).Distinct().Count() == 5 && draws.Sum(d => d.Vertices.Count) > 5000,
            $"the oracle parsed ({draws.Count} draws)");
        return draws;
    }

    private static WorldTextLayout Layout(string[] p)
    {
        var layout = new WorldTextLayout();
        uint color = (uint)I(p[5]) | ((uint)I(p[6]) << 8) | ((uint)I(p[7]) << 16) | ((uint)I(p[8]) << 24);
        layout.Update(p[1], F(p[2]), F(p[3]), F(p[4]), color, I(p[9]), I(p[10]), p[11] != "0");
        return layout;
    }

    private static string Row(Vector pos, float s, float t, int r, int g, int b, int a)
        => string.Join(",", new[] { pos.X, pos.Y, pos.Z, s, t }.Select(v => Bits(v).ToString("x8", CultureInfo.InvariantCulture))) + $",{r},{g},{b},{a}";

    [Fact]
    public void OneHundredFiftyDrawsLayOutBitForBit()
    {
        int vertices = 0, rainbow = 0;
        foreach (var d in Stock())
        {
            var p = d.Params;
            var layout = Layout(p);
            string what = $"'{p[1]}' at {d.Head[1]}";

            Assert.True(WorldTextLayout.MaterialOf(I(p[10])) == p[12], $"{what}: material {WorldTextLayout.MaterialOf(I(p[10]))} vs stock {p[12]}");
            Assert.True(Bits(layout.WorldWidth) == Bits(F(d.Head[19])) && Bits(layout.WorldHeight) == Bits(F(d.Head[20])),
                $"{what}: size {layout.WorldWidth:R} x {layout.WorldHeight:R} vs stock {d.Head[19]} x {d.Head[20]}");
            Assert.True(layout.BoundsMin.Equals(V(p, 13)) && layout.BoundsMax.Equals(V(p, 16)), $"{what}: bounds");

            var out_ = new WorldTextVertex[256];
            int n = layout.Layout(V(d.Head, 2), V(d.Head, 5), V(d.Head, 11), V(d.Head, 14), F(d.Head[1]), F(d.Head[17]), F(d.Head[18]), out_);
            Assert.True(n == d.Vertices.Count, $"{what}: {n} vertices vs stock {d.Vertices.Count}");
            for (int k = 0; k < n; k++)
            {
                var w = d.Vertices[k];
                string want = Row(V(w, 1), F(w[4]), F(w[5]), I(w[6]), I(w[7]), I(w[8]), I(w[9]));
                var v = out_[k];
                string have = Row(v.Position, v.S, v.T, v.R, v.G, v.B, v.A);
                Assert.True(want == have, $"{what} vertex {k}: stock {want}, port {have}");
            }

            vertices += n;
            if (p[11] != "0")
                rainbow += n;
        }

        Assert.True(vertices > 5000 && rainbow > 500, $"the replay reached its subject ({vertices} vertices, {rainbow} rainbow)");
    }

    [Fact]
    public void ABytePastTheGlyphTablesDrawsAsTheLastGlyphAndANewlineStartsALine()
    {
        var plain = new WorldTextLayout();
        plain.Update("~", 10, 0, 0, 0xffffffff, 0, 0, false);
        var high = new WorldTextLayout();
        high.Update("é", 10, 0, 0, 0xffffffff, 0, 0, false);
        Assert.True(high.TextLength == 2 && high.WorldWidth == 2 * plain.WorldWidth,
            $"two UTF-8 bytes past 127, each a '~' ({high.WorldWidth} vs {plain.WorldWidth})");

        var lines = new WorldTextLayout();
        lines.Update("ab\nab", 10, 0, 0, 0xffffffff, 0, 0, false);
        var one = new WorldTextLayout();
        one.Update("ab", 10, 0, 0, 0xffffffff, 0, 0, false);
        Assert.True(lines.WorldWidth == one.WorldWidth && lines.WorldHeight > one.WorldHeight, "a newline adds a line, not width");
    }

    [Fact]
    public void ARestoredLayoutIsTheSame()
    {
        var p = Stock()[0].Params;
        var layout = Layout(p);
        using var buffer = new MemoryStream();
        layout.Save(new BinaryWriter(buffer));
        var fresh = new WorldTextLayout();
        Assert.True(fresh.TextLength == 0 && layout.TextLength > 0, "a fresh layout holds no text");
        fresh.Restore(new BinaryReader(new MemoryStream(buffer.ToArray())));
        Assert.True(fresh.WorldWidth == layout.WorldWidth && fresh.BoundsMax.Equals(layout.BoundsMax) && fresh.Font == layout.Font,
            "the text, its style and its measure came across");
    }
}
