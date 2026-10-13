using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Ropes;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed <see cref="BeamSegDraw"/> against tier2.a's own CBeamSegDraw,
/// run offline by src/test/oracle/native/beamsegdraw_oracle_test.cpp
/// (<c>Fixtures/beamsegdraw-stock.tsv</c>, README beside it). Every case's
/// strips are expanded from the recorded segments and camera, and each vertex
/// is compared with what stock wrote: positions, texture coordinates and
/// tangents by their bits, the colour by the bytes CMeshBuilder's Color4f makes
/// of managed's floats. The native writer's half (those floats to stock's
/// buffers) is the oracle binary's third test.
/// </summary>
public class BeamSegDrawOracleTests
{
    private sealed class Case
    {
        public int Index;
        public Vector Camera;
        public int Base;
        public readonly List<BeamSeg[]> Strips = [];
        public readonly List<string[]> Vertices = [];
    }

    private static float Bits(string hex) => BitConverter.Int32BitsToSingle(int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static string Hex(float f) => BitConverter.SingleToInt32Bits(f).ToString("x8", CultureInfo.InvariantCulture);

    // CMeshBuilder::Color4f's byte (FastFToC, mathlib.h): c*255 + 2^23, the low byte.
    private static int Byte(float c) => BitConverter.SingleToInt32Bits(c * 255.0f + 8388608.0f) & 255;

    private static List<Case> Golden()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "beamsegdraw-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock strips are missing: " + path);

        var cases = new List<Case>();
        List<BeamSeg>? strip = null;
        int expected = 0;
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] f = line.Split('\t');
            switch (f[0])
            {
                case "C":
                    cases.Add(new Case
                    {
                        Index = int.Parse(f[1], CultureInfo.InvariantCulture),
                        Base = int.Parse(f[3], CultureInfo.InvariantCulture),
                        Camera = new Vector(Bits(f[4]), Bits(f[5]), Bits(f[6])),
                    });
                    break;
                case "S":
                    strip = [];
                    expected = int.Parse(f[1], CultureInfo.InvariantCulture);
                    break;
                case "G":
                    strip!.Add(new BeamSeg
                    {
                        Pos = new Vector(Bits(f[1]), Bits(f[2]), Bits(f[3])),
                        Color = new Vector(Bits(f[4]), Bits(f[5]), Bits(f[6])),
                        TexCoord = Bits(f[7]),
                        Width = Bits(f[8]),
                        Alpha = Bits(f[9]),
                    });
                    if (strip.Count == expected)
                        cases[^1].Strips.Add([.. strip]);
                    break;
                case "V":
                    cases[^1].Vertices.Add(f);
                    break;
            }
        }
        return cases;
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void EveryVertexIsTheOneStockWrote()
    {
        List<Case> cases = Golden();

        // The stimulus reached the subject: the whole scenario, every strip.
        Assert.True(cases.Count > 50, "the golden holds " + cases.Count + " cases");
        int compared = 0;

        foreach (Case c in cases)
        {
            var ours = new List<BeamSegVertex>();
            foreach (BeamSeg[] segs in c.Strips)
            {
                var v = new BeamSegVertex[segs.Length * 2];
                Assert.Equal(v.Length, BeamSegDraw.Expand(segs, c.Camera, v));
                ours.AddRange(v);
            }

            Assert.Equal(c.Vertices.Count, ours.Count);
            for (int i = 0; i < ours.Count; i++)
            {
                string[] s = c.Vertices[i];
                BeamSegVertex o = ours[i];
                string where = $"case {c.Index} vertex {c.Base + i}";

                Assert.True(Hex(o.Pos.X) == s[2] && Hex(o.Pos.Y) == s[3] && Hex(o.Pos.Z) == s[4], where + ": position");
                string color = $"{Byte(o.B):x2}{Byte(o.G):x2}{Byte(o.R):x2}{Byte(o.A):x2}";
                Assert.True(color == s[5], where + ": colour " + color + ", stock " + s[5]);
                Assert.True(Hex(o.U0) == s[6] && Hex(o.V0) == s[7] && Hex(o.U1) == s[8] && Hex(o.V1) == s[9], where + ": texture coordinates");
                Assert.True(Hex(o.TangentS.X) == s[10] && Hex(o.TangentS.Y) == s[11] && Hex(o.TangentS.Z) == s[12], where + ": tangent S");
                Assert.True(Hex(o.TangentT.X) == s[13] && Hex(o.TangentT.Y) == s[14] && Hex(o.TangentT.Z) == s[15], where + ": tangent T");
                compared++;
            }
        }
        Assert.True(compared > 1000, "compared " + compared + " vertices");
    }

    [Fact]
    public void AVertexIsSeventeenFloatsInTheWritersOrder()
    {
        // beam_strip_vertex.h's BEAM_STRIP_VERTEX_FLOATS and WriteVertex's reads.
        Assert.Equal(17 * sizeof(float), System.Runtime.CompilerServices.Unsafe.SizeOf<BeamSegVertex>());
        Assert.Equal(17, BeamSegDraw.VertexFloats);
        var v = new BeamSegVertex
        {
            Pos = new Vector(1, 2, 3), R = 4, G = 5, B = 6, A = 7, U0 = 8, V0 = 9, U1 = 10, V1 = 11,
            TangentS = new Vector(12, 13, 14), TangentT = new Vector(15, 16, 17),
        };
        float[] f = System.Runtime.InteropServices.MemoryMarshal.Cast<BeamSegVertex, float>(new[] { v }).ToArray();
        Assert.Equal(Enumerable.Range(1, 17).Select(i => (float)i), f);
    }

    [Fact]
    public void FewerThanTwoSegmentsWriteNothing()
    {
        var v = new BeamSegVertex[2];
        Assert.Equal(0, BeamSegDraw.Expand([new BeamSeg { Width = 1 }], default, v));
        Assert.Equal(default, v[0]);
    }
}
