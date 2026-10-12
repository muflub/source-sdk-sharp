using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="SphereLighting"/> against stock particlesphererenderer.h's
/// lighting as the smoke stack's quads were drawn through it:
/// <c>Fixtures/sphere-stock.tsv.gz</c> (README beside it). Bit-exact on the
/// colour the quad builder is handed.
/// </summary>
public class SphereLightingOracleTests
{
    private static List<float[]> Rows()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "sphere-stock.tsv.gz");
        Assert.True(File.Exists(path), path + " is missing; a replay of nothing would pass");
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<float[]>();
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("sphere\t", StringComparison.Ordinal))
                continue;
            rows.Add(line.Split('\t').Skip(1).Where(c => c != "|")
                         .Select(c => float.Parse(c, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray());
        }
        return rows;
    }

    private static Vector V(float[] r, int at) => new(r[at], r[at + 1], r[at + 2]);

    [Fact]
    public void Every_recorded_lit_colour_is_reproduced_bit_for_bit()
    {
        var rows = Rows();
        // Both arms, so the directional light's skip is exercised both ways.
        Assert.True(rows.Count(r => r[17] == 1) >= 1000, "rows with the pixel-shader skip");
        Assert.True(rows.Count(r => r[17] == 0) >= 1000, "rows without it");
        Assert.Contains(rows, r => r[22] > 254 || r[23] > 254 || r[24] > 254);   // the clamp's scaled arm

        foreach (var r in rows)
        {
            var ambient = new ParticleLight(V(r, 3), V(r, 6), r[9]);
            var directional = new ParticleLight(V(r, 10), V(r, 13), r[16]);
            Vector lit = SphereLighting.Lit(V(r, 0), ambient, directional, r[17] != 0, V(r, 18));
            Assert.True(BitConverter.SingleToInt32Bits(lit.X) == BitConverter.SingleToInt32Bits(r[22])
                        && BitConverter.SingleToInt32Bits(lit.Y) == BitConverter.SingleToInt32Bits(r[23])
                        && BitConverter.SingleToInt32Bits(lit.Z) == BitConverter.SingleToInt32Bits(r[24]),
                        $"lit {lit} against stock ({r[22]:R}, {r[23]:R}, {r[24]:R})");
        }
    }

    // The recording never puts a particle within 0.01 units of a light; this
    // arm is stock's AddLightColor read: inside that distance the amount is
    // 1000, not intensity over the squared distance.
    [Fact]
    public void A_particle_at_the_light_takes_the_fixed_amount()
    {
        // The ambient light sits on the particle: its amount is 1000, so it adds
        // 0.001 * 1000 = 1 red. The directional one is 1 unit away with
        // intensity 1: 1 green. (1, 1, 0) is not over 1, so it is scaled by 255.
        // Any other amount for the coincident light changes the red to green
        // ratio, which the clamp keeps.
        var position = new Vector(10, 20, 30);
        var ambient = new ParticleLight(position, new Vector(0.001f, 0, 0), 1);
        var directional = new ParticleLight(new Vector(11, 20, 30), new Vector(0, 1, 0), 1);
        Vector lit = SphereLighting.Lit(default, ambient, directional, usingPixelShaders: false, position);
        Assert.Equal(new Vector(0.001f * 1000f * 255f, 255f, 0), lit);
    }
}
