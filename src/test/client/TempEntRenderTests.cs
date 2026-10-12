using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The legacy tempents' render choices against stock's, recorded:
/// <c>Fixtures/tempents-render-stock.tsv.gz</c> (README-tempents-stock.md)
/// holds C_LocalTempEntity's ComputeFxBlend, IsTransparent, GetRenderGroup and
/// DrawModel occlusion answers for the tempents a dm_lockdown session drew, and
/// every breakable-helper event with the lighting origin each breakmodel drew
/// with. The managed answers, asked the same, must be the same, bit for bit.
/// </summary>
public class TempEntRenderTests
{
    private static List<string[]> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "tempents-render-stock.tsv.gz");
        Assert.True(File.Exists(path), "the recorded tempent render choices are missing: " + path);
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

    private static string At(string[] row, string key, int i = 0)
    {
        for (int k = 1; k < row.Length; k++)
        {
            if (row[k].StartsWith(key + "=", StringComparison.Ordinal))
                return i == 0 ? row[k][(key.Length + 1)..] : row[k + i];
        }
        throw new KeyNotFoundException($"{key} in '{string.Join(' ', row)}'");
    }

    private static float F(string[] r, string k, int i = 0) => float.Parse(At(r, k, i), CultureInfo.InvariantCulture);
    private static int I(string[] r, string k, int i = 0) => int.Parse(At(r, k, i), CultureInfo.InvariantCulture);
    private static Vector V(string[] r, string k) => new(F(r, k), F(r, k, 1), F(r, k, 2));

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        List<string[]> rows = Recorded();

        Assert.True(rows.Count(r => r[0] == "tfx-in") == 14092, "14,092 blends");
        Assert.True(rows.Count(r => r[0] == "tdm-in") == 12414, "12,414 draws");
        Assert.True(rows.Count(r => r[0] == "trg") == 29240, "29,240 render groups");
        Assert.True(rows.Count(r => r[0] == "tlo") == 15144, "15,144 lighting origins");
        foreach (int fx in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16 })
            Assert.True(rows.Any(r => r[0] == "tfx-in" && I(r, "fx") == fx), $"render fx {fx} was blended (the battery reached it)");
        Assert.True(rows.Any(r => r[0] == "tdm-in" && (I(r, "flags") & TempEntRender.FlagBeOccluded) != 0), "an occluded tempent drew");
    }

    [Fact]
    public void EveryRecordedBlendIsComputedAsStockComputedIt()
    {
        List<string[]> rows = Recorded();
        var random = new FxTestRig();
        var render = new TempEntRender(random);
        var wrong = new List<string>();
        int n = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "tfx-in")
                continue;
            string[] r = rows[i];
            int j = i + 1;
            for (; rows[j][0] == "fxr"; j++)
                random.Enqueue(rows[j]);
            string[] o = rows[j];
            Assert.True(o[0] == "tfx-out", $"blend {n}: no tfx-out after its tfx-in");
            n++;

            var (blend, alpha) = render.ComputeFxBlend(new TempEntBlendInput(I(r, "fx"), I(r, "a"), I(r, "mode"), I(r, "index"),
                F(r, "t"), V(r, "o"), V(r, "vo"), V(r, "vf")));
            if (blend != I(o, "blend") || alpha != I(o, "a") || random.Pending != 0)
                wrong.Add($"blend {n} fx {I(r, "fx")} mode {I(r, "mode")}: {blend}/{alpha}, stock {I(o, "blend")}/{I(o, "a")}{(random.Pending != 0 ? ", draws left" : "")}");
            random.ClearDraws();
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} of {n} blends disagree: " + string.Join(" | ", wrong.Take(6)));
        Assert.True(random.Wrong.Count == 0, string.Join(" | ", random.Wrong.Take(4)));
    }

    [Fact]
    public void EveryRecordedRenderGroupIsStocks()
    {
        List<string[]> rows = Recorded();
        var wrong = new List<string>();
        int withTranslucency = 0, independent = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "trg")
                continue;
            string[] g = rows[i];
            int mode = I(g, "mode"), blend = I(g, "blend"), want = I(g, "group");
            bool twoPass = I(g, "twopass") != 0;

            if (i > 0 && rows[i - 1][0] == "ttr")
            {
                bool transl = I(rows[i - 1], "transl") != 0;
                withTranslucency++;
                int got = TempEntRender.RenderGroup(mode, blend, transl, twoPass);
                if (got != want)
                    wrong.Add($"group {i}: mode {mode} blend {blend} transl {transl}: {got}, stock {want}");
                continue;
            }

            // Asked with no IsTransparent row beside it: only an answer the
            // model's translucency cannot change is held.
            int a = TempEntRender.RenderGroup(mode, blend, false, twoPass);
            int b = TempEntRender.RenderGroup(mode, blend, true, twoPass);
            if (a != b)
                continue;
            independent++;
            if (a != want)
                wrong.Add($"group {i}: mode {mode} blend {blend}: {a}, stock {want}");
        }

        Assert.True(withTranslucency == 4338 && independent > 1000, $"{withTranslucency} groups with the model's answer, {independent} without");
        Assert.True(wrong.Count == 0, $"{wrong.Count} groups disagree: " + string.Join(" | ", wrong.Take(6)));
    }

    [Fact]
    public void EveryRecordedDrawFadesAsStockFaded()
    {
        List<string[]> rows = Recorded();
        var wrong = new List<string>();
        int n = 0, faded = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "tdm-in")
                continue;
            string[] r = rows[i], o = rows[i + 1];
            Assert.True(o[0] == "tdm-a", "a draw with no alpha row after it");
            n++;
            int got = TempEntRender.OccludedAlpha(I(r, "flags"), I(r, "a"), I(r, "amt"), V(r, "n"), V(r, "o"), V(r, "mv"));
            if (got != I(r, "a"))
                faded++;
            if (got != I(o, "a"))
                wrong.Add($"draw {n}: {got}, stock {I(o, "a")} (a {I(r, "a")}, amt {I(r, "amt")}, flags {I(r, "flags"):x})");
        }

        Assert.True(faded > 0, "an occluded tempent faded (the positive arm)");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {n} draws disagree: " + string.Join(" | ", wrong.Take(6)));
    }

    [Fact]
    public void EveryBreakmodelDrawsWithStocksLightingOrigin()
    {
        List<string[]> rows = Recorded();
        var groups = new TempEntBreakGroups();
        var wrong = new List<string>();
        int n = 0, lit = 0;

        static long Id(string[] r) => Convert.ToInt64(At(r, "id")[2..], 16);

        for (int i = 0; i < rows.Count; i++)
        {
            string[] r = rows[i];
            switch (r[0])
            {
                case "tbh-insert": groups.Insert(Id(r), I(r, "slave") != 0); break;
                case "tbh-remove": groups.Remove(Id(r)); break;
                case "tbh-clear": groups.Clear(); break;
                case "tlo":
                {
                    n++;
                    long head = groups.LightingOriginOf(Id(r));
                    bool stockLit = I(r, "lo") != 0;
                    if ((head >= 0) != stockLit)
                    {
                        wrong.Add($"draw {n}: managed {(head >= 0 ? "lit" : "unlit")}, stock {(stockLit ? "lit" : "unlit")}");
                        break;
                    }
                    if (!stockLit)
                        break;
                    lit++;

                    // The head's own origin this frame, from its own row nearest this one.
                    Vector want = new(F(r, "lo", 1), F(r, "lo", 2), F(r, "lo", 3));
                    string[]? headRow = null;
                    for (int d = 0; d < 400 && headRow is null; d++)
                    {
                        foreach (int k in new[] { i - d, i + d })
                        {
                            if (k >= 0 && k < rows.Count && rows[k][0] == "tlo" && Id(rows[k]) == head)
                            {
                                headRow = rows[k];
                                break;
                            }
                        }
                    }
                    if (headRow is null || !V(headRow, "o").Equals(want))
                        wrong.Add($"draw {n}: head {head:x} at {(headRow is null ? "?" : V(headRow, "o").ToString())}, stock lit from {want}");
                    break;
                }
            }
        }

        Assert.True(lit > 1000, $"{lit} of {n} breakmodel draws were lit from a group head");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {n} lighting origins disagree: " + string.Join(" | ", wrong.Take(6)));
    }

    // The arm the battery did not reach.
    [Fact]
    public void PulseFastWiderIsTheFullSineMagnitude()
    {
        var render = new TempEntRender(new FxTestRig());
        var (blend, _) = render.ComputeFxBlend(new TempEntBlendInput(TempEntRender.PulseFastWider, 10, 5, -1, 0.1f, default, default, default));

        Assert.True(blend == (int)(0xff * Math.Abs(Math.Sin(0.1f * 12 + -363.0))), $"got {blend}");
    }
}
