using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client wind against stock <c>CEnvWindShared::WindThink</c>, recorded
/// live (<c>Fixtures/wind-stock.tsv</c>, README beside it): an env_wind with
/// short gusts, from its Start through 400 thinks (ten gust switches).
/// Bit-exact: the wind vector, the speed and its average, the direction, the
/// gust state, the three clocks, the variation, and every tree-sway vector
/// handed to the shaders. The streams are the port's own, seeded as stock's.
/// </summary>
public class WindSimOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static string Bits(params float[] v) => string.Join(",", v.Select(f => BitConverter.SingleToInt32Bits(f).ToString("x8", CultureInfo.InvariantCulture)));

    private static (string[] Start, List<(string[] Sway, string[] Think)> Thinks) Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "wind-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock wind is missing: " + path);
        string[]? start = null;
        string[]? sway = null;
        var thinks = new List<(string[], string[])>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] c = line.Split('\t');
            switch (c[0])
            {
                case "S": start = c; break;
                case "V": sway = c; break;
                case "W":
                    Assert.True(sway is not null, "a think row without its sway row");
                    thinks.Add((sway!, c));
                    sway = null;
                    break;
                default: Assert.Fail($"an unknown row kind '{c[0]}'"); break;
            }
        }

        Assert.True(start is not null && thinks.Count >= 300, $"the oracle parsed ({thinks.Count} thinks)");
        return (start!, thinks);
    }

    private static WindSim Started(string[] s)
    {
        var sim = new WindSim
        {
            MinWind = I(s[1]), MaxWind = I(s[2]), MinGust = I(s[3]), MaxGust = I(s[4]),
            MinGustDelay = F(s[5]), MaxGustDelay = F(s[6]), GustDirChange = I(s[7]), GustDuration = F(s[8]),
        };
        sim.Init(I(s[9]), F(s[12]), I(s[10]), F(s[11]));
        return sim;
    }

    [Fact]
    public void FourHundredThinksReplayBitForBit()
    {
        var (start, thinks) = Stock();
        var sim = Started(start);
        int switches = 0;
        bool gusting = sim.Gusting;
        for (int k = 0; k < thinks.Count; k++)
        {
            var (v, w) = thinks[k];
            float t = F(w[1]);
            float next = sim.Think(t);
            Assert.True(next == t + 0.01f, "the next think is a hundredth on");

            string want = Bits(F(w[2]), F(w[3]), F(w[4]), F(w[5]), F(w[6])) + $",{w[7]},{w[8]}," + Bits(F(w[9]), F(w[10]), F(w[11]), F(w[12]), F(w[13]));
            string have = Bits(sim.CurrentWind.X, sim.CurrentWind.Y, sim.CurrentWind.Z, sim.WindSpeed, sim.AveWindSpeed)
                + $",{sim.WindDir},{(sim.Gusting ? 1 : 0)}," + Bits(sim.SwitchTime, sim.SimTime, sim.AngleVariation, sim.SpeedVariation, sim.VariationTime);
            Assert.True(want == have, $"think {k} at {w[1]}: stock {want}, port {have}");

            string wantSway = Bits(F(v[2]), F(v[3]), F(v[4]), F(v[5]));
            string haveSway = Bits(sim.SwayTime, sim.SwayParameter.X, sim.SwayParameter.Y, sim.SwayParameter.Z);
            Assert.True(wantSway == haveSway, $"think {k}: sway stock {wantSway}, port {haveSway}");

            if (sim.Gusting != gusting)
                switches++;
            gusting = sim.Gusting;
        }

        Assert.True(switches >= 10, $"the recording switched gusts ({switches})");
    }

    [Fact]
    public void AReloadCarriesTheStreamsAndTheClocks()
    {
        var (start, thinks) = Stock();
        var sim = Started(start);
        foreach (var (_, w) in thinks.Take(150))
            sim.Think(F(w[1]));

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        var fresh = new WindSim();
        Assert.True(fresh.SwitchTime == 0 && sim.SwitchTime > 0, "a fresh wind has no clock");
        fresh.Restore(new BinaryReader(new MemoryStream(buffer.ToArray())));
        foreach (var (_, w) in thinks.Skip(150))
        {
            sim.Think(F(w[1]));
            fresh.Think(F(w[1]));
        }

        Assert.True(fresh.CurrentWind.Equals(sim.CurrentWind) && fresh.SwitchTime == sim.SwitchTime && fresh.WindDir == sim.WindDir,
            "the restored wind blows as the original does, through the remaining gusts");
    }
}
