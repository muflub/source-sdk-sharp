using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Ropes;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The env_spritetrail ribbon against stock's, recorded:
/// <c>Fixtures/spritetrail-stock.tsv.gz</c> (README-sprite-stock.md) holds every
/// tick of the sandbox ribbon (its origin, the random width it drew, the box
/// after) and every draw (each segment, and the ring after the dead points
/// went), under four parameter sets. The managed ring, fed the same ticks and
/// draws, must lay, measure and draw the same, bit for bit.
/// </summary>
public class ClientSpriteTrailTests
{
    private static List<string[]> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "spritetrail-stock.tsv.gz");
        Assert.True(File.Exists(path), "the recorded ribbon is missing: " + path);
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
                rows.Add(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return rows;
    }

    // The value(s) after "key=" in a row.
    private static string At(string[] row, string key, int i = 0)
    {
        for (int k = 1; k < row.Length; k++)
        {
            if (row[k].StartsWith(key + "=", StringComparison.Ordinal))
                return i == 0 ? row[k][(key.Length + 1)..] : row[k + i];
        }
        throw new KeyNotFoundException($"{key} in '{string.Join(' ', row)}'");
    }

    private static float F(string[] row, string key, int i = 0) => float.Parse(At(row, key, i), CultureInfo.InvariantCulture);
    private static int I(string[] row, string key, int i = 0) => int.Parse(At(row, key, i), CultureInfo.InvariantCulture);
    private static Vector V(string[] row, string key) => new(F(row, key), F(row, key, 1), F(row, key, 2));
    private static bool Same(float a, float b) => a.Equals(b);
    private static bool Same(in Vector a, in Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private sealed record Replay(int Ticks, int Draws, int Segments, List<string> Wrong);

    private static Replay Run(List<string[]> rows)
    {
        var trail = new SpriteTrail();
        var rig = new FxTestRig();
        var wrong = new List<string>();
        var segs = new BeamSeg[SpriteTrail.MaxPoints + 1];
        int ticks = 0, draws = 0, segments = 0;

        for (int r = 0; r < rows.Count; r++)
        {
            string[] row = rows[r];
            switch (row[0])
            {
                case "trl-upd":
                    trail.Params = new SpriteTrailParams
                    {
                        ModelIndex = I(row, "model"), LifeTime = F(row, "life"), StartWidth = F(row, "sw"),
                        EndWidth = F(row, "ew"), StartWidthVariance = F(row, "var"), TextureRes = F(row, "tex"),
                        MinFadeLength = F(row, "fade"), RenderMode = I(row, "mode"),
                        R = I(row, "rgba"), G = I(row, "rgba", 1), B = I(row, "rgba", 2), Brightness = I(row, "rgba", 3),
                        Visible = I(row, "vis") != 0,
                    };
                    break;

                case "trl-tick":
                {
                    ticks++;
                    if (trail.StepCount != I(row, "n") || trail.FirstStep != I(row, "first") || !Same(trail.UpdateTime, F(row, "next")))
                        wrong.Add($"tick {ticks}: entered with ring {trail.StepCount}/{trail.FirstStep}/{trail.UpdateTime}, stock {I(row, "n")}/{I(row, "first")}/{F(row, "next")}");

                    // The random widths this tick drew, in order, up to its box.
                    int b = r + 1;
                    for (; b < rows.Count && rows[b][0] == "fxr"; b++)
                        rig.Randoms.Enqueue((float.Parse(rows[b][2], CultureInfo.InvariantCulture), float.Parse(rows[b][3], CultureInfo.InvariantCulture),
                                             float.Parse(rows[b][4], CultureInfo.InvariantCulture)));
                    Assert.True(b < rows.Count && rows[b][0] == "trl-box", $"tick {ticks} has no box row");
                    string[] box = rows[b];
                    r = b;

                    trail.Tick(F(row, "t"), V(row, "o"), rig);

                    if (rig.Randoms.Count != 0)
                    {
                        wrong.Add($"tick {ticks}: {rig.Randoms.Count} random draws stock made and managed did not");
                        rig.Randoms.Clear();
                    }
                    if (trail.StepCount != I(box, "n") || trail.FirstStep != I(box, "first") || !Same(trail.UpdateTime, F(box, "next")))
                        wrong.Add($"tick {ticks}: ring {trail.StepCount}/{trail.FirstStep}/{trail.UpdateTime}, stock {I(box, "n")}/{I(box, "first")}/{F(box, "next")}");
                    if (!Same(trail.RenderMins, V(box, "mins")) || !Same(trail.RenderMaxs, V(box, "maxs")))
                        wrong.Add($"tick {ticks}: box {trail.RenderMins} {trail.RenderMaxs}, stock {V(box, "mins")} {V(box, "maxs")}");
                    break;
                }

                case "trl-draw":
                {
                    draws++;
                    var want = new List<string[]>();
                    int e = r + 1;
                    for (; e < rows.Count && rows[e][0] == "trl-seg"; e++)
                        want.Add(rows[e]);
                    string[]? end = e < rows.Count && rows[e][0] == "trl-end" ? rows[e] : null;
                    r = end is null ? e - 1 : e;

                    if (trail.StepCount != I(row, "n") || trail.FirstStep != I(row, "first"))
                        wrong.Add($"draw {draws}: entered with ring {trail.StepCount}/{trail.FirstStep}, stock {I(row, "n")}/{I(row, "first")}");

                    int n = trail.Draw(F(row, "t"), V(row, "o"), segs);
                    segments += n;

                    if (n != want.Count)
                    {
                        wrong.Add($"draw {draws}: {n} segments, stock {want.Count}");
                        break;
                    }
                    for (int s = 0; s < n; s++)
                    {
                        string[] w = want[s];
                        BeamSeg g = segs[s];
                        if (!Same(g.Pos, V(w, "pos")) || !Same(g.Color, V(w, "col")) || !Same(g.Alpha, F(w, "alpha"))
                            || !Same(g.Width, F(w, "width")) || !Same(g.TexCoord, F(w, "tc")))
                        {
                            wrong.Add($"draw {draws} seg {s}: pos {g.Pos} alpha {g.Alpha:G9} width {g.Width:G9} tc {g.TexCoord:G9}, stock {string.Join(' ', w[1..])}");
                            break;
                        }
                    }
                    if (end is not null && (trail.StepCount != I(end, "n") || trail.FirstStep != I(end, "first")))
                        wrong.Add($"draw {draws}: left ring {trail.StepCount}/{trail.FirstStep}, stock {I(end, "n")}/{I(end, "first")}");
                    break;
                }

                case "fxr":
                    wrong.Add($"row {r}: a random draw outside a tick");
                    break;

                default:
                    wrong.Add($"row {r}: unexpected '{row[0]}'");
                    break;
            }
        }

        foreach (string w in rig.Wrong)
            wrong.Add(w);
        return new Replay(ticks, draws, segments, wrong);
    }

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        List<string[]> rows = Recorded();

        Assert.True(rows.Count(r => r[0] == "trl-tick") == 1258, "1,258 ticks recorded");
        Assert.True(rows.Count(r => r[0] == "trl-draw") == 458, "458 draws recorded");
        Assert.True(rows.Count(r => r[0] == "trl-seg") == 11135, "11,135 segments recorded");
        Assert.True(rows.Count(r => r[0] == "trl-upd") == 5, "five parameter sets (creation, four overrides)");
        Assert.True(rows.Any(r => r[0] == "trl-upd" && At(r, "life") == "0"), "one with a zero lifetime");
        Assert.True(DrawsUnder(rows, "fade", v => v != "0") > 0, "a draw with a tail fade (the fade arm)");
        Assert.True(DrawsUnder(rows, "ew", v => v.StartsWith('-')) > 0, "a draw with no end width (the start-width arm)");
        Assert.True(DrawsUnder(rows, "life", v => v == "0") > 0, "a draw with a zero lifetime (the finished arm)");
        Assert.True(rows.Any(r => r[0] == "fxr" && r[4] != "0" && r[4] != "-0"), "a non-zero width variance was drawn (the positive arm)");
        Assert.True(rows.Any(r => r[0] == "trl-seg" && At(r, "tc") != "0"), "a non-zero texture coordinate was drawn");
    }

    // How many draws happened under parameters whose key matched.
    private static int DrawsUnder(List<string[]> rows, string key, Func<string, bool> match)
    {
        bool on = false;
        int n = 0;
        foreach (string[] r in rows)
        {
            if (r[0] == "trl-upd")
                on = match(At(r, key));
            else if (r[0] == "trl-draw" && on)
                n++;
        }
        return n;
    }

    [Fact]
    public void EveryRecordedTickAndDrawMatchesStock()
    {
        Replay replay = Replay0();

        Assert.True(replay.Wrong.Count == 0, $"{replay.Wrong.Count} disagreements over {replay.Ticks} ticks and {replay.Draws} draws: "
                                             + string.Join(" | ", replay.Wrong.Take(6)));
        Assert.True(replay.Segments == 11135, $"the replay drew every recorded segment, drew {replay.Segments}");
    }

    private static Replay Replay0() => Run(Recorded());

    [Fact]
    public void AnInvisibleRibbonDrawsNothing()
    {
        var trail = new SpriteTrail();
        trail.Params.LifeTime = 1;
        trail.Params.StartWidth = 4;
        var rig = new FxTestRig();
        rig.Randoms.Enqueue((0, 0, 0));
        trail.Tick(1, new Vector(0, 0, 0), rig);
        trail.Params.Visible = false;

        Assert.True(trail.StepCount == 1, "the tick laid a point");
        Assert.True(trail.Draw(1, new Vector(10, 0, 0), new BeamSeg[8]) == 0, "a ribbon switched off is not drawn");
    }

    [Fact]
    public void AFullRingStealsItsOldestPoint()
    {
        var trail = new SpriteTrail();
        trail.Params.LifeTime = 1000;
        var rig = new FxTestRig();
        for (int i = 0; i <= SpriteTrail.MaxPoints; i++)
        {
            rig.Randoms.Enqueue((0, 0, 0));
            trail.Tick(i * 10.0f, new Vector(i * 10.0f, 0, 0), rig);
        }

        Assert.True(trail.StepCount == SpriteTrail.MaxPoints && trail.FirstStep == 1, $"ring {trail.StepCount}/{trail.FirstStep}");
    }
}
