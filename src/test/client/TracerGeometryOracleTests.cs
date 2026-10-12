using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TracerGeometry"/> against stock c_tracer.cpp's
/// <c>Tracer_ComputeVerts</c> as the explosions' and impacts' spark trails
/// called it: <c>Fixtures/tracer-stock.tsv.gz</c> (README beside it), bit for bit.
/// </summary>
public class TracerGeometryOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    [Fact]
    public void Every_recorded_tracer_is_reproduced_bit_for_bit()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "tracer-stock.tsv.gz");
        Assert.True(File.Exists(path), path + " is missing; a replay of nothing would pass");
        int drawn = 0, refused = 0;
        Span<Vector> verts = stackalloc Vector[4];
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("trv\t", StringComparison.Ordinal))
                continue;
            string[] r = line.Split('\t');
            var start = new Vector(F(r[1]), F(r[2]), F(r[3]));
            var delta = new Vector(F(r[4]), F(r[5]), F(r[6]));
            bool ok = TracerGeometry.ComputeVerts(start, delta, F(r[7]), verts);
            Assert.True(ok == (r[9] == "1"), $"{line}: the port answered {ok}");
            if (!ok)
            {
                refused++;
                continue;
            }
            drawn++;
            for (int i = 0; i < 4; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float port = c == 0 ? verts[i].X : c == 1 ? verts[i].Y : verts[i].Z;
                    Assert.True(BitConverter.SingleToInt32Bits(port) == BitConverter.SingleToInt32Bits(F(r[10 + i * 3 + c])),
                                $"{line}: corner {i} axis {c} is {port:R}");
                }
            }
        }
        Assert.True(drawn >= 500 && refused >= 20, $"both arms: {drawn} drawn, {refused} refused");
    }
}
