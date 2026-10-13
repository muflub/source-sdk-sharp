using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ViewEffects"/> against view_effects.cpp's CViewEffects, call by
/// call and frame by frame over a live recording of stock on ss_sandbox
/// (Fixtures/view-effects-stock.tsv, README-view-effects-stock.md): every
/// Shake and Fade, every CalcShake with the random draws it made, and every
/// GetFadeParams. Arms the recording never takes are synthetic facts below.
/// </summary>
public class ViewEffectsTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static List<string[]> Rows()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "view-effects-stock.tsv");
        Assert.True(File.Exists(path), "the fixture is missing: " + path);
        return File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
    }

    /// <summary>The draws stock made, in order: each RandomFloat answers the next and is checked against its range.</summary>
    private sealed class Replayed(Queue<float> draws) : IUniformRandom
    {
        public float RandomFloat(float min, float max)
        {
            Assert.True(draws.Count > 0, "a draw stock did not make");
            float v = draws.Dequeue();
            Assert.InRange(v, min, max);
            return v;
        }

        public int RandomInt(int min, int max) => throw new InvalidOperationException("stock draws no int here");

        public float RandomFloatExp(float min, float max, float exponent) => throw new InvalidOperationException();
    }

    private sealed record Tally(int Frames, int Draws, int ShakingFrames, int FadingFrames, int Shakes, int Fades);

    private static Tally Replay()
    {
        List<string[]> rows = Rows();
        var fx = new ViewEffects();
        int frames = 0, draws = 0, shaking = 0, fading = 0, shakes = 0, fades = 0;
        for (int k = 0; k < rows.Count; k++)
        {
            string[] r = rows[k];
            string where = $"row {k + 2} ({r[0]})";
            switch (r[0])
            {
                case "vl":
                    fx.LevelInit();
                    break;
                case "vs":
                    Assert.True(fx.ShakeCount == I(r[6]), $"{where}: {fx.ShakeCount} shakes before, stock's {r[6]}");
                    fx.Shake((ShakeCommand)I(r[2]), F(r[3]), F(r[4]), F(r[5]), F(r[1]));
                    shakes++;
                    break;
                case "vf":
                {
                    Assert.True(fx.FadeCount == I(r[6]), $"{where}: {fx.FadeCount} fades before, stock's {r[6]}");
                    int[] c = r[5].Split(',').Select(I).ToArray();
                    fx.Fade(I(r[2]), I(r[3]), (FadeFlags)I(r[4]), (byte)c[0], (byte)c[1], (byte)c[2], (byte)c[3], F(r[1]));
                    fades++;
                    break;
                }
                case "vc":
                {
                    Assert.True(fx.ShakeCount == I(r[3]), $"{where}: {fx.ShakeCount} shakes, stock's {r[3]}");
                    var queue = new Queue<float>();
                    int j = k + 1;
                    for (; j < rows.Count && rows[j][0] == "vcr"; j++)
                    {
                        float[] o = rows[j][2].Split(',').Select(F).ToArray();
                        queue.Enqueue(o[0]);
                        queue.Enqueue(o[1]);
                        queue.Enqueue(o[2]);
                        queue.Enqueue(F(rows[j][3]));
                    }
                    draws += j - k - 1;
                    ShakeFrame got = fx.CalcShake(F(r[1]), F(r[2]), new Replayed(queue));
                    Assert.True(queue.Count == 0, $"{where}: stock drew {queue.Count} more");
                    string[] o2 = rows[j];
                    Assert.Equal("vco", o2[0]);
                    float[] off = o2[1].Split(',').Select(F).ToArray();
                    Assert.True(Same(got.Offset.X, off[0]) && Same(got.Offset.Y, off[1]) && Same(got.Offset.Z, off[2]),
                                $"{where}: offset {got.Offset.X:R},{got.Offset.Y:R},{got.Offset.Z:R}, stock's {o2[1]}");
                    Assert.True(Same(got.Angle, F(o2[2])), $"{where}: roll {got.Angle:R}, stock's {o2[2]}");
                    Assert.True(Same(got.RumbleAngle, F(o2[3])), $"{where}: rumble {got.RumbleAngle:R}, stock's {o2[3]}");
                    Assert.True(fx.ShakeCount == I(o2[4]), $"{where}: {fx.ShakeCount} shakes after, stock's {o2[4]}");
                    frames++;
                    if (got.Angle != 0f)
                        shaking++;
                    k = j;
                    break;
                }
                case "vg":
                {
                    FadeParams p = fx.GetFadeParams(F(r[1]));
                    int[] c = r[2].Split(',').Select(I).ToArray();
                    Assert.True(p.R == c[0] && p.G == c[1] && p.B == c[2] && p.A == c[3],
                                $"{where}: fade {p.R},{p.G},{p.B},{p.A}, stock's {r[2]}");
                    Assert.True(p.Modulate == (r[3] != "0"), $"{where}: modulate {p.Modulate}");
                    Assert.True(fx.FadeCount == I(r[4]), $"{where}: {fx.FadeCount} fades, stock's {r[4]}");
                    if (p.A != 0)
                        fading++;
                    break;
                }
                default:
                    Assert.Fail($"{where}: an unknown row");
                    break;
            }
        }
        return new Tally(frames, draws, shaking, fading, shakes, fades);
    }

    [LinuxOnlyFact("a golden recorded bit for bit against glibc's libm; the Windows C runtime rounds the last bit of sin, cos and pow differently")]
    public void EachShakeAndFadeFrameIsStocksToTheBit()
    {
        Tally t = Replay();
        Assert.True(t.Frames > 400, $"{t.Frames} frames");
    }

    [LinuxOnlyFact("a golden recorded bit for bit against glibc's libm; the Windows C runtime rounds the last bit of sin, cos and pow differently")]
    public void TheRecordingShakesFadesAndRetunesAShake()
    {
        Tally t = Replay();
        Assert.True(t.Draws > 50, $"{t.Draws} redraws");
        Assert.True(t.ShakingFrames > 100, $"{t.ShakingFrames} shaking frames");
        Assert.True(t.FadingFrames > 20, $"{t.FadingFrames} fading frames");
        List<string[]> rows = Rows();
        foreach (int command in new[] { 0, 1, 2, 3 })
            Assert.Contains(rows, r => r[0] == "vs" && r[2] == command.ToString(CultureInfo.InvariantCulture));
    }

    // The arms the sandbox never takes.
    [Fact]
    public void AFadeInRampsUpThenHoldsThenGoes()
    {
        var fx = new ViewEffects();
        // One second in, half a second held, alpha 200: In's End is after the hold.
        fx.Fade(512, 256, FadeFlags.In, 10, 20, 30, 200, 1.0f);
        Assert.Equal(new FadeParams(10, 20, 30, 200, false), fx.GetFadeParams(1.0f));  // Speed * (End - now) = 200 * 1.5, capped
        Assert.Equal(100, fx.GetFadeParams(2.0f).A);   // half the ramp left
        Assert.Equal(0, fx.GetFadeParams(2.5f).A);
        fx.GetFadeParams(2.6f);
        Assert.Equal(0, fx.FadeCount);
    }

    [Fact]
    public void AStayOutFadeStaysUntilClearedAndModulates()
    {
        var fx = new ViewEffects();
        fx.Fade(0, 0, FadeFlags.StayOut | FadeFlags.Modulate, 0, 0, 0, 255, 1.0f);
        FadeParams p = fx.GetFadeParams(100f);
        Assert.True(p.Modulate);
        Assert.Equal(255, p.A);
        fx.ClearPermanentFades();
        Assert.Equal(0, fx.FadeCount);
    }

    [Fact]
    public void ColoursAverageAndTheHighestAlphaWins()
    {
        var fx = new ViewEffects();
        fx.Fade(0, 512, (FadeFlags)0, 100, 0, 0, 50, 0f);
        fx.Fade(0, 512, (FadeFlags)0, 0, 100, 0, 150, 0f);
        Assert.Equal(new FadeParams(50, 50, 0, 150, false), fx.GetFadeParams(0.5f));
    }

    [Fact]
    public void AnIntroFadeOverridesTheList()
    {
        var fx = new ViewEffects();
        fx.Fade(0, 512, FadeFlags.Modulate, 1, 2, 3, 4, 0f);
        Assert.Equal(new FadeParams(9, 8, 7, 6, false), fx.GetFadeParams(0.1f, (9, 8, 7, 6)));
        Assert.Equal(4, fx.GetFadeParams(0.1f, (9, 8, 7, 0)).A);
    }

    private sealed class Fixed(float v) : IUniformRandom
    {
        public float RandomFloat(float min, float max) => v * max;

        public int RandomInt(int min, int max) => min;

        public float RandomFloatExp(float min, float max, float exponent) => min;
    }

    [Fact]
    public void RumbleOnlyShakesFeedTheRumbleAndNotTheViewAndANoRumbleStartIsIgnored()
    {
        var fx = new ViewEffects();
        fx.Shake(ShakeCommand.StartRumbleOnly, 4, 10, 2, 0f);
        ShakeFrame f = fx.CalcShake(0.3f, 0.01f, new Fixed(0.5f));
        Assert.Equal(0f, f.Angle);
        Assert.NotEqual(0f, f.RumbleAngle);

        // Stock's Shake starts only SHAKE_START and SHAKE_START_RUMBLEONLY: a
        // SHAKE_START_NORUMBLE message adds nothing (CalcShake's no-rumble arm
        // is reachable only by a shake that never arrives).
        var other = new ViewEffects();
        other.Shake(ShakeCommand.StartNoRumble, 4, 10, 2, 0f);
        Assert.Equal(0, other.ShakeCount);
    }

    [Fact]
    public void NoMoreThanThirtyTwoShakes()
    {
        var fx = new ViewEffects();
        for (int i = 0; i < 40; i++)
            fx.Shake(ShakeCommand.Start, 1, 1, 1, 0f);
        Assert.Equal(ViewEffects.MaxShakes, fx.ShakeCount);
    }

    [Fact]
    public void ApplyShakeAddsTheFactorOfTheOffsetAndRoll()
    {
        var fx = new ViewEffects();
        fx.Shake(ShakeCommand.Start, 4, 10, 2, 0f);
        fx.CalcShake(0.3f, 0.01f, new Fixed(0.5f));
        Vector origin = new(1, 2, 3), angles = new(10, 20, 30);
        fx.ApplyShake(ref origin, ref angles, 0.5f);
        Assert.Equal(1 + 0.5f * fx.AppliedOffset.X, origin.X);
        Assert.Equal(30 + fx.AppliedAngle * 0.5f, angles.Z);
        Assert.Equal(10f, angles.X);
    }

    /// <summary>Records each draw's range: the order the shakes are visited, by their amplitudes.</summary>
    private sealed class Ranges : IUniformRandom
    {
        public List<float> Maxes { get; } = [];

        public float RandomFloat(float min, float max)
        {
            Maxes.Add(max);
            return 0.5f * max;
        }

        public int RandomInt(int min, int max) => min;

        public float RandomFloatExp(float min, float max, float exponent) => min;
    }

    // CalcShake retires a shake with FastRemove: the last shake takes its slot,
    // which changes the order the rest are visited (and so which draws each gets).
    [Fact]
    public void ARetiredShakesSlotGoesToTheLastShake()
    {
        var fx = new ViewEffects();
        fx.Shake(ShakeCommand.Start, 1, 100, 10, 0f);    // 0
        fx.Shake(ShakeCommand.Start, 2, 100, 0.05f, 0f); // 1, retires first
        fx.Shake(ShakeCommand.Start, 3, 100, 10, 0f);    // 2
        fx.Shake(ShakeCommand.Start, 4, 100, 10, 0f);    // 3
        fx.CalcShake(0.1f, 0.0001f, new Ranges());        // 1 retires: [0, 3, 2]
        var r = new Ranges();
        fx.CalcShake(0.2f, 0.0001f, r);
        float[] firsts = [r.Maxes[0], r.Maxes[4], r.Maxes[8]];
        Assert.Equal(3f, firsts[0], 1);
        Assert.Equal(4f, firsts[1], 1);
        Assert.Equal(1f, firsts[2], 1);
    }

    // FindLongestShake keeps the first of equal durations.
    [Fact]
    public void AnAmplitudeRetuneTakesTheFirstOfTheLongest()
    {
        var fx = new ViewEffects();
        fx.Shake(ShakeCommand.Start, 1, 100, 10, 0f);
        fx.Shake(ShakeCommand.Start, 2, 100, 10, 0f);
        fx.Shake(ShakeCommand.Amplitude, 9, 0, 0, 0f);
        var r = new Ranges();
        fx.CalcShake(0.1f, 0f, r);
        Assert.Equal(2f, r.Maxes[0]);  // shake 1, visited first, untouched
        Assert.Equal(9f, r.Maxes[4]);  // shake 0, retuned
    }
}
