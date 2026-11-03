using Xunit;

namespace SourceSharp.Ai.Tests.Skills;

/// <summary>§12.1's "set med bot to match my hit rate": the hit rate file and the arithmetic from it to the curve.</summary>
public sealed class AimCalibrationTests
{
    private const string HandWritten = """
        # weapon     band      hit_rate  samples
        weapon_smg1  300-800   0.30      420
        weapon_smg1  under300  0.50      100
        weapon_357   300-800   0.40      12
        """;

    private static string SampleFile => SourceSharp.Tests.RepoLayout.Repo("src", "ai", "tests", "recordings", "hitrate-sample.tsv");

    [Fact]
    public void TheCommittedSampleFileReads()
    {
        var rows = AimCalibration.ParseFile(File.ReadAllText(SampleFile));
        Assert.Equal(4, rows.Length);
        Assert.Equal(new HitRateRow("weapon_smg1", "300-800", 0.27, 412), rows[0]);
        Assert.Equal(["weapon_smg1", "weapon_smg1", "weapon_shotgun"], AimCalibration.Usable(rows, 50).Select(r => r.Weapon));
    }

    private const string Metrics = """
        actor                 kind  commands
        1#1073741824          human   3600
        hit rate: hits/shots (a shot is one trigger pull, a hit one victim in one tick)
        1#1073741824          human overall 4/270 1.5% | weapon_smg1 4/270 1.5% | <300 0/0 -, 300-800 3/7 42.9%, >800 1/10 10.0%, no target 253 shots
        2#1                   bot   overall 9/90 10.0% | weapon_smg1 9/90 10.0% | <300 0/0 -, 300-800 9/90 10.0%, >800 0/0 -, no target 0 shots
        """;

    [Fact]
    public void TheMetricsLinesBandsAreTheRowsAndNoTargetShotsAreLeftOut()
    {
        var rows = AimCalibration.FromMetrics(Metrics);
        Assert.Equal(2, rows.Length);
        Assert.Equal(new HitRateRow("any", "300-800", 3 / 7.0, 7), rows[0]);
        Assert.Equal(new HitRateRow("any", "over800", 0.1, 10), rows[1]);
    }

    [Fact]
    public void WhatTheMetricsGiveReadsBackAsAFile()
        => Assert.Equal(2, AimCalibration.ParseFile(AimCalibration.WriteFile(AimCalibration.FromMetrics(Metrics))).Length);

    [Fact]
    public void MetricsWithNoPersonAreRefused()
        => Assert.Contains("no line of a person", Assert.Throws<InvalidDataException>(() => AimCalibration.FromMetrics("hit rate: x\n2#1 bot overall 0/0 - | w 0/0 - | <300 0/0 -, 300-800 0/0 -, >800 0/0 -")).Message);

    [Fact]
    public void AHandWrittenFileReads()
    {
        var rows = AimCalibration.ParseFile(HandWritten);
        Assert.Equal(3, rows.Length);
        Assert.Equal(new HitRateRow("weapon_smg1", "300-800", 0.30, 420), rows[0]);
    }

    [Theory]
    [InlineData("weapon_smg1 300-800 0.3", "weapon, band, hit rate and samples")]
    [InlineData("weapon_smg1 mid 0.3 10", "no band 'mid'")]
    [InlineData("weapon_smg1 300-800 30 10", "a share from 0 to 1")]
    [InlineData("weapon_smg1 300-800 0.3 0", "a count above 0")]
    [InlineData("# only a comment", "no rows")]
    public void AFileItCannotUseIsRefusedByName(string text, string says)
        => Assert.Contains(says, Assert.Throws<InvalidDataException>(() => AimCalibration.ParseFile(text)).Message);

    [Fact]
    public void RowsWithTooFewShotsAreLeftOutAndNoneLeftIsARefusal()
    {
        var rows = AimCalibration.ParseFile(HandWritten);
        Assert.Equal(["300-800", "under300"], AimCalibration.Usable(rows, 50).Select(r => r.Band));
        Assert.Contains("1000 samples", Assert.Throws<InvalidDataException>(() => AimCalibration.Usable(rows, 1000)).Message);
    }

    [Fact]
    public void ThePoolWeighsARowByItsSamples()
    {
        Assert.Equal((0.30 * 420 + 0.50 * 100) / 520, AimCalibration.Pooled(AimCalibration.Usable(AimCalibration.ParseFile(HandWritten), 50)), 6);
    }

    private static readonly AimProbe[] Probes = [new(1.0, 0.50, 300), new(2.0, 0.30, 300), new(4.0, 0.10, 300)];

    [Fact]
    public void TheValueIsReadOffTheProbes()
    {
        (double v, bool reached) = AimCalibration.Solve(Probes, 0.20);
        Assert.Equal(3.0, v, 3);
        Assert.True(reached);
    }

    [Fact]
    public void ATargetBeyondTheProbesSaysSo()
    {
        Assert.Equal((1.0, false), AimCalibration.Solve(Probes, 0.90));
        Assert.Equal((4.0, false), AimCalibration.Solve(Probes, 0.01));
    }

    [Fact]
    public void ANoisyProbeDoesNotBendTheLine()
    {
        // 0.35 at error 3 is above 0.30 at error 2: pooled, both read 0.325.
        AimProbe[] noisy = [new(1.0, 0.50, 300), new(2.0, 0.30, 300), new(3.0, 0.35, 300), new(4.0, 0.10, 300)];
        Assert.Equal(3.0 + (0.325 - 0.20) / (0.325 - 0.10), AimCalibration.Solve(noisy, 0.20).Value, 3);
    }

    /// <summary>A low probe at the small end does not cap the line (seen on the user's recording: 61 % then 83 %).</summary>
    [Fact]
    public void ALowProbeAtTheSmallEndDoesNotCapTheLine()
    {
        AimProbe[] noisy = [new(0.7, 0.61, 41), new(1.35, 0.83, 60), new(2.7, 0.48, 83), new(4.0, 0.48, 88)];
        (double v, bool reached) = AimCalibration.Solve(noisy, 0.70);
        Assert.True(reached, "pooled, the line starts at 0.74");
        Assert.InRange(v, 1.35, 2.7);
    }

    [Fact]
    public void AProbeOfFewShotsWeighsLittleInThePool()
    {
        AimProbe[] noisy = [new(1.0, 0.20, 10), new(2.0, 0.60, 1000), new(3.0, 0.10, 300)];
        Assert.InRange(AimCalibration.Solve(noisy, 0.35).Value, 2.0, 3.0);
    }

    [Fact]
    public void TheCurvePassesThroughThePresetsAndFalls()
    {
        float[] shipped = [9f, 8f, 7f, 6f, 5f, 4f, 3f, 2f, 1.5f, 1f, 0.5f];
        float[] c = AimCalibration.Curve([(2, 6.0), (4, 3.0), (6, 2.0), (8, 1.0)], shipped);
        Assert.Equal([6f, 3f, 2f, 1f], [c[2], c[4], c[6], c[8]]);
        for (int i = 1; i < c.Length; i++)
            Assert.True(c[i] <= c[i - 1], $"level {i}: {string.Join(",", c)}");
    }

    [Fact]
    public void ACurveThatWouldRiseWithSkillIsRefused()
    {
        float[] shipped = [9f, 8f, 7f, 6f, 5f, 4f, 3f, 2f, 1.5f, 1f, 0.5f];
        Assert.Contains("is not below", Assert.Throws<InvalidDataException>(() => AimCalibration.Curve([(2, 3.0), (4, 3.0)], shipped)).Message);
    }

    private const string ProbeFile = """
        # preset level weapon band value rate shots
        easy   2 weapon_smg1 300-800 2.0 0.30 300
        easy   2 weapon_smg1 300-800 6.0 0.10 300
        normal 4 weapon_smg1 300-800 1.0 0.50 300
        normal 4 weapon_smg1 300-800 3.0 0.20 300
        hard   6 weapon_smg1 300-800 0.5 0.70 300
        hard   6 weapon_smg1 300-800 2.0 0.40 300
        """;

    [Fact]
    public void MediumMatchesTheFileAndTheOthersFollowByTheirRatios()
    {
        var rows = AimCalibration.Usable(AimCalibration.ParseFile(HandWritten), 200);
        var c = AimCalibration.Calibrate(rows, AimCalibration.ParseProbes(ProbeFile), [("easy", 0.5), ("normal", 1.0), ("hard", 2.0)]);
        Assert.Equal(["easy", "normal", "hard"], c.Select(x => x.Preset));
        Assert.Equal(0.30, c[1].Target, 6);
        Assert.Equal(1.0 + 2.0 * (0.50 - 0.30) / (0.50 - 0.20), c[1].AimErrorDeg, 3);
        Assert.Equal(0.15, c[0].Target, 6);
        Assert.Equal(0.60, c[2].Target, 6);
        Assert.True(c[0].AimErrorDeg > c[1].AimErrorDeg && c[1].AimErrorDeg > c[2].AimErrorDeg, string.Join(",", c));
    }

    [Fact]
    public void WhenMediumMovesTheOthersMoveWithIt()
    {
        var c = AimCalibration.Calibrate([new HitRateRow("weapon_smg1", "300-800", 0.25, 500)], AimCalibration.ParseProbes(ProbeFile), [("normal", 1.0), ("hard", 2.0)]);
        var d = AimCalibration.Calibrate([new HitRateRow("weapon_smg1", "300-800", 0.20, 500)], AimCalibration.ParseProbes(ProbeFile), [("normal", 1.0), ("hard", 2.0)]);
        Assert.True(d[0].AimErrorDeg > c[0].AimErrorDeg, "a lower hit rate is a wider aim");
        Assert.True(d[1].AimErrorDeg > c[1].AimErrorDeg, "and Hard's follows");
    }

    [Fact]
    public void APresetThatCannotReachItsTargetSaysSo()
    {
        var c = AimCalibration.Calibrate([new HitRateRow("weapon_smg1", "300-800", 0.60, 500)], AimCalibration.ParseProbes(ProbeFile), [("normal", 1.0)]);
        Assert.False(c[0].Reached);
    }

    [Fact]
    public void AProbesLineItCannotReadIsRefused()
        => Assert.Contains("line 1", Assert.Throws<InvalidDataException>(() => AimCalibration.ParseProbes("normal four weapon_smg1 300-800 1 0.5 300")).Message);

    [Fact]
    public void ThePresetsRatiosAreTheFilesAndRiseFromMedium()
    {
        var ratios = SkillCurves.HitRateRatios(File.ReadAllText(Curves.Path));
        Assert.Equal(["easy", "normal", "hard", "expert"], ratios.Select(r => r.Preset));
        Assert.Equal(1.0, ratios[1].Ratio);
        Assert.True(ratios[0].Ratio < 1.0 && ratios[2].Ratio > 1.0 && ratios[3].Ratio > ratios[2].Ratio, string.Join(",", ratios));
    }

    [Theory]
    [InlineData("""{"hitRateRatios":{"easy":0.5,"normal":1.1,"hard":1.5,"expert":2}}""", "must be 1")]
    [InlineData("""{"hitRateRatios":{"easy":1.5,"normal":1,"hard":1.5,"expert":2}}""", "not above easy")]
    [InlineData("""{"hitRateRatios":{"easy":0.5,"normal":1,"hard":1.5}}""", "no 'expert'")]
    [InlineData("""{}""", "no 'hitRateRatios'")]
    public void RatiosThatDoNotRiseFromMediumAreRefused(string json, string says)
        => Assert.Contains(says, Assert.Throws<InvalidDataException>(() => SkillCurves.HitRateRatios(json)).Message);

    [Fact]
    public void AProbeOfACurveKeepsItsDirectionAndParses()
    {
        SkillCurves c = Curves.Game();
        SkillCurves probed = c.With("aimErrorDeg", 4, 20f);
        Assert.Equal(20f, probed["aimErrorDeg"].At(4));
        Assert.True(probed["aimErrorDeg"].At(3) >= 20f, "a level below gives way");
        Assert.Equal(c["aimErrorDeg"].At(5), probed["aimErrorDeg"].At(5));
        Assert.Equal(c["leadQuality"].At(4), probed["leadQuality"].At(4));
    }
}
