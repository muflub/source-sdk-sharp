using System.Globalization;
using System.IO.Compression;

using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="LuminanceHistogram"/>, <see cref="ToneMapScale"/>,
/// <see cref="BloomAmount"/> and <see cref="ExposureRange"/> against
/// viewpostprocess.cpp's tonemap, frame by frame over a live recording of
/// stock's queries, readbacks and decisions on ss_sandbox
/// (Fixtures/tonemap-stock.tsv.gz, README-tonemap-stock.md).
/// </summary>
public class TonemapTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static float[] Fs(string s) => s.Split(',').Select(F).ToArray();

    private static int[] Is(string s) => s.Split(',').Select(I).ToArray();

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static List<string[]> Rows()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "tonemap-stock.tsv.gz");
        Assert.True(File.Exists(path), "the fixture is missing: " + path);
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] != '#')
                rows.Add(line.Split('\t'));
        }
        return rows;
    }

    // The recording's frames: each starts at its GetBloomAmount row.
    private static List<List<string[]>> Frames(List<string[]> rows, out List<string[]> before)
    {
        before = [];
        var frames = new List<List<string[]>>();
        foreach (string[] r in rows)
        {
            if (r[0] == "tb")
                frames.Add([]);
            (frames.Count == 0 ? before : frames[^1]).Add(r);
        }
        return frames;
    }

    private sealed class Replay(Queue<string[]> readbacks, Func<int> frame) : IOcclusionReadback
    {
        public int PixelsRendered(int bucket)
        {
            Assert.True(readbacks.Count > 0, $"frame {frame()}: bucket {bucket} read back where stock read nothing");
            string[] r = readbacks.Dequeue();
            Assert.True(I(r[2]) == bucket, $"frame {frame()}: bucket {bucket} read back where stock read {r[2]}");
            return I(r[3]);
        }
    }

    private sealed record Tally(int Frames, int Queries, int Readbacks, int Averaged, int Resets, int Algorithm0);

    private static Tally ReplayAll()
    {
        List<List<string[]>> frames = Frames(Rows(), out List<string[]> before);
        var histogram = new LuminanceHistogram();
        var scale = new ToneMapScale();
        var bloom = new BloomAmount();
        int queries = 0, readbacks = 0, averaged = 0, resets = 0, algorithm0 = 0;

        foreach (string[] r in before)
        {
            Assert.Equal("tr", r[0]);
            scale.Reset();
        }

        int n = 0;
        foreach (List<string[]> frame in frames)
        {
            n++;
            string where = $"frame {n}";
            float min = 0, max = 0, target = 0, clamped = 0;
            int algorithm = 1;
            ToneMapAction? action = null;
            for (int k = 0; k < frame.Count; k++)
            {
                string[] r = frame[k];
                switch (r[0])
                {
                    case "tb":
                    {
                        int[] hw = Is(r[1]);
                        float[] cv = Fs(r[4]);
                        var b = new BloomInputs(hw[0], hw[1], hw[2], hw[3] != 0, hw[4], hw[5], false, I(r[2]), r[3] != "0",
                            cv[0], cv[1], cv[2] >= 0f, cv[2], cv[3], cv[4]);
                        float got = bloom.Next(b);
                        Assert.True(Same(got, F(r[5])), $"{where}: bloom amount {got:R}, stock's {r[5]}");
                        break;
                    }
                    case "tx":
                    {
                        string[] cmin = r[1].Split(','), cmax = r[2].Split(',');
                        float[] cv = Fs(r[3]);
                        (min, max) = ExposureRange.Of(cmin[0] != "0", F(cmin[1]), cmax[0] != "0", F(cmax[1]), cv[0], cv[1], I(r[4]));
                        float[] want = Fs(r[5]);
                        Assert.True(Same(min, want[0]) && Same(max, want[1]), $"{where}: exposure range {min:R},{max:R}, stock's {r[5]}");
                        break;
                    }
                    case "tu":
                    {
                        algorithm = I(r[2]);
                        var rb = new Queue<string[]>();
                        var tiq = new List<string[]>();
                        int j = k + 1;
                        for (; j < frame.Count && (frame[j][0] == "trb" || frame[j][0] == "tiq"); j++)
                        {
                            if (frame[j][0] == "trb")
                                rb.Enqueue(frame[j]);
                            else
                                tiq.Add(frame[j]);
                        }
                        readbacks += rb.Count;
                        HistogramView view = default;
                        if (tiq.Count > 0)
                        {
                            int[] vp = Is(tiq[0][5]);
                            float[] cr = Fs(tiq[0][7]);
                            view = new HistogramView(vp[0], vp[1], vp[2], vp[3], tiq[0][6] != "0", cr[0], cr[1], cr[2], cr[3], F(tiq[0][9]));
                        }
                        var issued = new List<LuminanceQuery>();
                        int inForce = histogram.Update(algorithm, "mods/test", view, new Replay(rb, () => n), issued);
                        Assert.Equal(algorithm, inForce);
                        Assert.True(rb.Count == 0, $"{where}: stock read back {rb.Count} more");
                        Assert.True(issued.Count == tiq.Count, $"{where}: {issued.Count} queries, stock's {tiq.Count}");
                        for (int q = 0; q < issued.Count; q++)
                        {
                            LuminanceQuery got = issued[q];
                            string[] w = tiq[q];
                            HistogramBucket bucket = histogram.Buckets[got.Bucket];
                            float[] lum = Fs(w[3]);
                            int[] rect = Is(w[8]);
                            Assert.True(Same(bucket.MinLum, lum[0]) && Same(bucket.MaxLum, lum[1]),
                                $"{where}: bucket {got.Bucket} is {bucket.MinLum:R},{bucket.MaxLum:R}, stock queried {w[3]}");
                            Assert.True(rect.SequenceEqual([got.X0, got.Y0, got.X1, got.Y1]),
                                $"{where}: rectangle {got.X0},{got.Y0},{got.X1},{got.Y1}, stock's {w[8]}");
                            Assert.True(bucket.Pixels == I(w[10]), $"{where}: {bucket.Pixels} pixels, stock's {w[10]}");
                            Assert.True(Same(got.TestMax, lum[1] == 1.0f ? 10000.0f : lum[1]), $"{where}: test max {got.TestMax:R}");
                        }
                        queries += issued.Count;
                        k = j - 1;
                        break;
                    }
                    case "tts":
                    {
                        float[] cv = Fs(r[2]);
                        target = histogram.TargetTonemapScalar(algorithm, cv[0], cv[1], cv[2], F(r[3]), r[1] != "0");
                        Assert.True(Same(target, F(r[4])), $"{where}: target {target:R}, stock's {r[4]}");
                        break;
                    }
                    case "tts0":
                    {
                        algorithm0++;
                        target = histogram.TargetTonemapScalar(algorithm, 0f, 0f, 0f, 0f);
                        Assert.True(Same(target, F(r[1])), $"{where}: original target {target:R}, stock's {r[1]}");
                        break;
                    }
                    case "tt":
                    {
                        Assert.True(Same(target, F(r[1])), $"{where}: target {target:R}, stock's {r[1]}");
                        clamped = ExposureRange.Clamp(target, min, max);
                        Assert.True(Same(clamped, F(r[2])), $"{where}: clamped {clamped:R}, stock's {r[2]}");
                        break;
                    }
                    case "tsm":
                    {
                        Assert.True(Same(clamped, F(r[1])), $"{where}: set {clamped:R}, stock's {r[1]}");
                        action = scale.Set(clamped, min, max, F(r[3]), I(r[4]));
                        string? next = k + 1 < frame.Count ? frame[k + 1][0] : null;
                        if (next == "tsr")
                        {
                            resets++;
                            Assert.True(action.Value.Kind == ToneMapActionKind.Reset && Same(action.Value.Value, F(frame[k + 1][1])),
                                $"{where}: {action}, stock reset to {frame[k + 1][1]}");
                        }
                        else if (next == "tsa")
                        {
                            averaged++;
                            Assert.True(action.Value is { Kind: ToneMapActionKind.Goal, Averaged: true } && Same(action.Value.Average, F(frame[k + 1][1])),
                                $"{where}: {action}, stock's averaged goal {frame[k + 1][1]}");
                        }
                        else
                        {
                            Assert.True(action.Value is { Kind: ToneMapActionKind.Goal, Averaged: false } && Same(action.Value.Value, clamped),
                                $"{where}: {action}, stock set the goal to {clamped:R} alone");
                        }
                        break;
                    }
                    case "tsr":
                    case "tsa":
                        break;
                    case "tr":
                        scale.Reset();
                        break;
                    default:
                        Assert.Fail($"{where}: an unknown row {r[0]}");
                        break;
                }
            }
            Assert.True(action is not null, $"{where}: stock set no tonemap scale");
        }
        return new Tally(frames.Count, queries, readbacks, averaged, resets, algorithm0);
    }

    [Fact]
    public void EachFramesQueriesReadbacksAndScaleAreStocksToTheBit()
    {
        Tally t = ReplayAll();
        Assert.True(t.Frames > 1000, $"only {t.Frames} frames");
    }

    // The replay proves nothing about an arm the recording never took.
    [Fact]
    public void TheRecordingReachesEveryArm()
    {
        Tally t = ReplayAll();
        Assert.True(t.Queries >= t.Frames, $"{t.Queries} queries over {t.Frames} frames");
        Assert.True(t.Readbacks > 100, $"{t.Readbacks} readbacks");
        Assert.True(t.Averaged > 100, $"{t.Averaged} averaged goals");
        Assert.True(t.Resets > 5, $"{t.Resets} forced resets");
        Assert.True(t.Algorithm0 > 30, $"{t.Algorithm0} frames of the original algorithm");
        List<string[]> rows = Rows();
        Assert.Contains(rows, r => r[0] == "trb" && r[3] == "-1");
        Assert.True(rows.Where(r => r[0] == "tb").Select(r => r[5]).Distinct().Count() > 20, "the bloom amount never eased");
        Assert.Contains(rows, r => r[0] == "tx" && r[4] != "0");
    }

    // Arms ss_sandbox never takes: the mod fallback, a map's exposure range,
    // fullbright, a bad target, old hardware and LDR bloom.
    [Fact]
    public void CounterStrikeFallsBackToTheOriginalAlgorithmAndHl1DoesNot()
    {
        Assert.True(LuminanceHistogram.ForcesOriginalAlgorithm("/games/CSTRIKE"));
        Assert.True(LuminanceHistogram.ForcesOriginalAlgorithm("lostcoast"));
        Assert.False(LuminanceHistogram.ForcesOriginalAlgorithm("hl1"));
        Assert.False(LuminanceHistogram.ForcesOriginalAlgorithm("mods/test"));

        var h = new LuminanceHistogram();
        var issued = new List<LuminanceQuery>();
        Assert.Equal(0, h.Update(1, "/games/dod", new HistogramView(0, 0, 640, 480, false, 0.9f, 0.85f, 0.9f, 0.85f, 1f), new NoReadback(), issued));
        Assert.Equal(100000.0f, h.Buckets[LuminanceHistogram.Ranges - 1].MaxLum);
    }

    private sealed class NoReadback : IOcclusionReadback
    {
        public int PixelsRendered(int bucket) => -1;
    }

    [Fact]
    public void AMapsExposureRangeOverridesTheCvarsAndUncapWinsOverBoth()
    {
        Assert.Equal((0.8f, 3f), ExposureRange.Of(true, 0.8f, true, 3f, 0.5f, 2f, 0));
        Assert.Equal((0.5f, 2f), ExposureRange.Of(true, 0f, false, 3f, 0.5f, 2f, 0));
        Assert.Equal((0f, 20f), ExposureRange.Of(true, 0.8f, true, 3f, 0.5f, 2f, 1));
        Assert.Equal((2.5f, 2.5f), ExposureRange.Of(true, 2.5f, false, 0f, 0.5f, 2f, 0));
    }

    [Fact]
    public void FullbrightForcesTheScaleToOneAndANonFiniteTargetDoesNothing()
    {
        var s = new ToneMapScale();
        Assert.Equal(new ToneMapAction(ToneMapActionKind.Reset, 1f, false, 0f), s.Set(1.3f, 0.5f, 2f, 0f, 1));
        Assert.Equal(ToneMapActionKind.None, s.Set(float.NaN, 0.5f, 2f, 0f, 0).Kind);
        Assert.Equal(new ToneMapAction(ToneMapActionKind.Goal, 1.3f, false, 0f), s.Set(1.3f, 0.5f, 2f, 0f, 0));
    }

    [Fact]
    public void BloomIsOffOnOldHardwareAndScaledWithoutHdr()
    {
        var on = new BloomInputs(90, BloomAmount.HdrTypeNone, 2, true, 0, 0, false, 0, false, 1f, 1f, false, 0f, 0.3f, 1f);
        Assert.Equal(0f, new BloomAmount().Next(on with { DxSupportLevel = 70 }));
        Assert.Equal(0.3f, new BloomAmount().Next(on));
        Assert.Equal(1f, new BloomAmount().Next(on with { HdrType = 1 }));
        Assert.Equal(0.6f, new BloomAmount().Next(on with { HdrType = 1, UseCustomBloomScale = true, CustomBloomScale = 0.6f }));
        Assert.Equal(0f, new BloomAmount().Next(on with { HdrType = 1, BuildingCubemaps = true }));
        Assert.Equal(1f, new BloomAmount().Next(on with { HdrType = 1, MapHasHdrLighting = false, ForceBloom = 1 }));
    }
}
