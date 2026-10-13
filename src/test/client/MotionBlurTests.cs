using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="MotionBlur"/> against DoImageSpaceMotionBlur (viewpostprocess.cpp),
/// frame by frame over a live recording of stock's own inputs and values
/// (Fixtures/motionblur-stock.tsv, README-motionblur-stock.md).
/// </summary>
public class MotionBlurTests
{
    private readonly record struct Row(int Line, MotionBlurFrame Frame, float[] Values);

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static float[] Floats(string s) => s.Split(',').Select(F).ToArray();

    private static Vector V(string s)
    {
        float[] f = Floats(s);
        Assert.Equal(3, f.Length);
        return new Vector(f[0], f[1], f[2]);
    }

    private static List<Row> Rows()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "motionblur-stock.tsv");
        Assert.True(File.Exists(path), "the fixture is missing: " + path);
        var rows = new List<Row>();
        string[] lines = File.ReadAllLines(path);
        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n];
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] c = line.Split('\t');
            Assert.True(c.Length == 8, $"line {n + 1} has {c.Length} columns");
            float[] cv = Floats(c[6]);
            Assert.Equal(5, cv.Length);
            float[] values = Floats(c[7]);
            Assert.Equal(4, values.Length);
            var frame = new MotionBlurFrame(F(c[0]), V(c[1]), V(c[2]), F(c[3]), F(c[4]), c[5] != "0",
                cv[0], cv[1], cv[2], cv[3], cv[4]);
            rows.Add(new Row(n + 1, frame, values));
        }
        return rows;
    }

    [LinuxOnlyFact("a golden recorded bit for bit against glibc's libm; the Windows C runtime rounds the last bit of sin, cos and pow differently")]
    public void EachFramesValuesAreStocksToTheBit()
    {
        List<Row> rows = Rows();
        Assert.True(rows.Count > 400, $"the recording has only {rows.Count} frames");
        var blur = new MotionBlur();
        foreach (Row row in rows)
        {
            MotionBlurValues got = blur.Update(row.Frame);
            float[] g = [got.X, got.Y, got.Falling, got.Roll];
            for (int i = 0; i < 4; i++)
            {
                Assert.True(BitConverter.SingleToInt32Bits(g[i]) == BitConverter.SingleToInt32Bits(row.Values[i]),
                    $"line {row.Line}: value [{i}] is {g[i]:R}, stock's {row.Values[i]:R}");
            }
        }
    }

    // The replay above proves nothing about an arm the recording never took.
    [Fact]
    public void TheRecordingReachesEveryValueAndBothForwardSettings()
    {
        List<Row> rows = Rows();
        for (int i = 0; i < 4; i++)
            Assert.True(rows.Count(r => r.Values[i] != 0f) >= 5, $"value [{i}] is never non-zero");
        Assert.Contains(rows, r => r.Frame.ForwardEnabled && r.Values[2] != 0f);
        Assert.Contains(rows, r => !r.Frame.ForwardEnabled && r.Values[1] != 0f);
        Assert.Contains(rows, r => r.Frame.Strength != 1f && r.Values[0] != 0f);
    }

    [Fact]
    public void ATeleportHoldsOffRotationalBlurForASecond()
    {
        var blur = new MotionBlur();
        var at = new Vector(0, 0, 0);
        MotionBlurFrame Frame(float t, Vector o, float yaw) =>
            new(t, o, new Vector(0, yaw, 0), 90f, 1.5f, false, 10f, 20f, 1f, 1f, 1f);

        blur.Update(Frame(1.0f, at, 0f));
        blur.Update(Frame(1.0f + 1f / 60f, at, 0f));
        Assert.NotEqual(0f, blur.Update(Frame(1.0f + 2f / 60f, at, -2f)).X);

        var far = new Vector(100, 0, 0);
        blur.Update(Frame(1.0f + 3f / 60f, far, -4f));
        Assert.Equal(0f, blur.Update(Frame(1.0f + 4f / 60f, far, -6f)).X);
        float t = 1.0f + 4f / 60f;
        while (t < 1.0f + 3f / 60f + 1.0f)
        {
            t += 1f / 60f;
            blur.Update(Frame(t, far, -6f));
        }
        Assert.NotEqual(0f, blur.Update(Frame(t + 1f / 60f, far, -8f)).X);
    }

    private static MotionBlurFrame Turn(float t, float yaw) =>
        new(t, new Vector(0, 0, 0), new Vector(0, yaw, 0), 90f, 1.5f, false, 10f, 20f, 1f, 1f, 1f);

    // The recording ran at a steady 60 fps; these arms need other frame times.
    // Below 30 fps the fade already zeroes the values, so what tells the
    // 15 fps cut-off apart is that it skips the teleport check: a far move in
    // a frame that slow does not hold off the next frame's rotational blur.
    [Fact]
    public void AFarMoveInAFrameSlowerThan15FpsHoldsNothingOff()
    {
        var blur = new MotionBlur();
        blur.Update(Turn(1.0f, 0f));
        var far = new MotionBlurFrame(1.09f, new Vector(60, 0, 0), new Vector(0, -2, 0), 90f, 1.5f, false, 10f, 20f, 1f, 1f, 1f);
        Assert.Equal(new MotionBlurValues(0f, 0f, 0f, 0f), blur.Update(far));
        var next = far with { RealTime = 1.09f + 1f / 60f, Angles = new Vector(0, -4, 0) };
        Assert.NotEqual(0f, blur.Update(next).X);
    }

    [Fact]
    public void BlurFadesOutFrom50FpsDownTo30()
    {
        float At(float elapsed)
        {
            var blur = new MotionBlur();
            blur.Update(Turn(2.0f, 0f));
            return blur.Update(Turn(2.0f + elapsed, -1f)).X;
        }

        float fast = At(1f / 60f), at40 = At(1f / 40f), at50 = At(1f / 50.5f);
        Assert.NotEqual(0f, fast);
        Assert.Equal(fast, at50);
        Assert.Equal(0.5f, at40 / fast, 3);
        Assert.Equal(0f, At(1f / 29f));
    }
}
