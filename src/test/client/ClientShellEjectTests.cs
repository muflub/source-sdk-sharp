using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The shell-eject effects against stock's, recorded:
/// <c>Fixtures/shelleject-stock.tsv</c> (README beside it) holds 216 calls into
/// fx_shelleject.cpp's callbacks with whether the owner had a renderable and
/// its render angles, and the brass each ejected. Replayed through the managed
/// handlers with the same answers, each must eject exactly when stock did, the
/// same kind, from the same place, oriented the same way.
/// </summary>
public class ClientShellEjectTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static List<(string[] Call, string? Eject)> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "shelleject-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock shell ejects are missing: " + path);

        var calls = new List<(string[], string?)>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] c = line.Split('\t');
            if (c[0] == "fxs-call")
                calls.Add((c, null));
            else if (c[0] == "fxs-eject")
                calls[^1] = (calls[^1].Item1, c[1]["type=".Length..]);
            else
                Assert.Fail("not a fixture row: " + line);
        }
        return calls;
    }

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        var calls = Recorded();
        Assert.True(calls.Count == 216, $"216 calls, read {calls.Count}");
        Assert.True(calls.Count(c => c.Eject is not null) == 108, "108 ejected (the positive arm)");
        Assert.True(calls.Select(c => c.Eject).Distinct().Count() == 4, "every kind, and none");
    }

    [Fact]
    public void EveryRecordedCallEjectsAsStockDid()
    {
        var rig = new FxTestRig();
        var handlers = new Dictionary<int, ShellEjectEffect>
        {
            [0] = new PistolShellEjectEffect(rig),
            [1] = new RifleShellEjectEffect(rig),
            [2] = new ShotgunShellEjectEffect(rig),
        };

        // Stock's session dispatched the three names in turn, four owners each.
        var wrong = new List<string>();
        var calls = Recorded();
        for (int i = 0; i < calls.Count; i++)
        {
            string[] c = calls[i].Call;
            int kind = i / 4 % 3;
            rig.RenderAnglesAnswer = c[8] == "has=1";
            rig.RenderAngles = new QAngle(F(c[9]["ra=".Length..]), F(c[10]), F(c[11]));
            var data = new EffectData
            {
                EntIndex = int.Parse(c[1]["ent=".Length..], CultureInfo.InvariantCulture),
                Origin = new Vector(F(c[2]["o=".Length..]), F(c[3]), F(c[4])),
                Angles = new Vector(F(c[5]["a=".Length..]), F(c[6]), F(c[7])),
            };

            int before = rig.Brass.Count;
            handlers[kind].Dispatch(data);

            string? want = calls[i].Eject;
            if (want is null)
            {
                if (rig.Brass.Count != before)
                    wrong.Add($"call {i}: stock ejected nothing, managed did");
                continue;
            }
            if (rig.Brass.Count != before + 1)
            {
                wrong.Add($"call {i}: stock ejected type {want}, managed nothing");
                continue;
            }
            var b = rig.Brass[^1];
            if (b.Type.ToString(CultureInfo.InvariantCulture) != want
                || b.Origin.X != data.Origin.X || b.Origin.Y != data.Origin.Y || b.Origin.Z != data.Origin.Z
                || b.Angles.Pitch != data.Angles.X || b.Angles.Yaw != data.Angles.Y || b.Angles.Roll != data.Angles.Z
                || b.GunAngles != rig.RenderAngles)
                wrong.Add($"call {i}: managed ejected type {b.Type} at {b.Origin.X} {b.Origin.Y} {b.Origin.Z}");
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} disagreement(s): " + string.Join("; ", wrong.Take(8)));
    }
}
