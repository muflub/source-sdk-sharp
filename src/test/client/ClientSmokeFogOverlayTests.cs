using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The smoke fog overlay against stock's, recorded:
/// <c>Fixtures/smokefog-stock.tsv</c> (README beside it) holds 401 draws over a
/// sweep of the overlay alpha and colour, each with what stock drew. The
/// managed plan, asked the same, must draw the same quads with the same colour
/// and alpha, bit for bit, and skip the same frames.
/// </summary>
public class ClientSmokeFogOverlayTests
{
    private static List<string[]> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "smokefog-stock.tsv");
        Assert.True(File.Exists(path), "the recorded smoke fog overlay is missing: " + path);
        return File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
    }

    private static float F(string s, string key) => float.Parse(s[(key.Length + 1)..], CultureInfo.InvariantCulture);

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        List<string[]> rows = Recorded();

        Assert.True(rows.Count(r => r[0] == "sf-in") == 401, "401 draws");
        Assert.True(rows.Count(r => r[0] == "sf-draw") == 292, "292 quads");
        Assert.True(rows.Count(r => r[0] == "sf-in" && F(r[1], "alpha") < 0) > 0, "a negative alpha (the low clamp)");
        Assert.True(rows.Count(r => r[0] == "sf-in" && F(r[1], "alpha") > 1) > 0, "an alpha past one (the high clamp)");
    }

    [Fact]
    public void EveryRecordedDrawIsPlannedAsStockDrewIt()
    {
        List<string[]> rows = Recorded();
        var overlay = new SmokeFogOverlay();
        var wrong = new List<string>();
        int n = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "sf-in")
                continue;
            n++;
            string[] r = rows[i];
            float alpha = F(r[1], "alpha");
            var color = new Vector(F(r[2], "col"), float.Parse(r[3], CultureInfo.InvariantCulture), float.Parse(r[4], CultureInfo.InvariantCulture));
            bool material = r[5] == "mat=1";
            string[] next = rows[i + 1];

            bool drawn = overlay.Plan(alpha, material, ref color, out Vector drawColor, out float drawAlpha);

            if (next[0] == "sf-skip")
            {
                if (drawn)
                    wrong.Add($"draw {n} (alpha {alpha}): stock skipped");
                continue;
            }

            var want = new Vector(F(next[1], "col"), float.Parse(next[2], CultureInfo.InvariantCulture), float.Parse(next[3], CultureInfo.InvariantCulture));
            float wantAlpha = F(next[4], "alpha");
            if (!drawn || !drawColor.X.Equals(want.X) || !drawColor.Y.Equals(want.Y) || !drawColor.Z.Equals(want.Z) || !drawAlpha.Equals(wantAlpha))
                wrong.Add($"draw {n} (alpha {alpha}): managed {drawn} {drawColor} {drawAlpha:G9}, stock {want} {wantAlpha:G9}");
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} of {n} draws disagree: " + string.Join(" | ", wrong.Take(6)));
        Assert.True(overlay.Drawn == 292, $"the plan drew every recorded quad, drew {overlay.Drawn}");
    }

    [Fact]
    public void ADrawOverwritesTheOverlayColour()
    {
        var color = new Vector(1, 0, 0);
        new SmokeFogOverlay().Plan(0.5f, true, ref color, out _, out _);

        Assert.True(color.X == 0.3f && color.Y == 0.3f && color.Z == 0.3f, $"stock's fixed grey replaces the global, got {color}");
    }

    [Fact]
    public void NoMaterialDrawsNothing()
    {
        var color = new Vector(1, 0, 0);

        Assert.False(new SmokeFogOverlay().Plan(0.5f, false, ref color, out _, out _));
        Assert.True(color.X == 1, "and leaves the overlay colour alone");
    }
}
