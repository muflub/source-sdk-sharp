using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The smoke stack's simulation against stock <c>C_SmokeStack</c>, recorded
/// live (<c>Fixtures/smokestack-stock.tsv</c>, README beside it): 150 frames of
/// one env_smokestack with twist, roll and wind all non-zero, from its Start
/// on. Bit-exact: the draws are replayed with their bounds compared as bits.
/// </summary>
public class SmokeStackSimOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private static Vector V(string[] c, int at) => new(F(c[at]), F(c[at + 1]), F(c[at + 2]));

    private sealed class Frame
    {
        public required string[] Head;
        public string[] Params = [];
        public string[]? Twist;
        public readonly List<string[]> Draws = new();
        public string[]? Step;
        public readonly List<string[]> State = new();
        public readonly List<string[]> Quads = new();
    }

    private static List<Frame> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "smokestack-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock smoke stack is missing: " + path);

        var frames = new List<Frame>();
        Frame? cur = null;
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] c = line.Split('\t');
            if (c[0] == "F")
            {
                cur = new Frame { Head = c };
                frames.Add(cur);
                continue;
            }

            Assert.True(cur is not null, $"a '{c[0]}' row before the first frame");
            switch (c[0])
            {
                case "P": cur!.Params = c; break;
                case "T": cur!.Twist = c; break;
                case "RI": case "RF": cur!.Draws.Add(c); break;
                case "S": cur!.Step = c; break;
                case "Q": cur!.State.Add(c); break;
                case "D": cur!.Quads.Add(c); break;
                default: Assert.Fail($"an unknown row kind '{c[0]}' in the smoke stack oracle"); break;
            }
        }

        Assert.True(frames.Count >= 30 && frames.Sum(f => f.State.Count) > 1000 && frames.Sum(f => f.Draws.Count) > 100,
            $"the oracle parsed ({frames.Count} frames)");
        return frames;
    }

    private sealed class Replay : IParticleRandom
    {
        public readonly Queue<string[]> Pending = new();

        public float RandomFloat(float min, float max)
        {
            var c = Next("RF");
            Assert.True(Bits(F(c[1])) == Bits(min) && Bits(F(c[2])) == Bits(max),
                $"RandomFloat bounds: stock ({c[1]}, {c[2]}), the port ({min:R}, {max:R})");
            return F(c[3]);
        }

        public int RandomInt(int min, int max)
        {
            var c = Next("RI");
            Assert.True(int.Parse(c[1], CultureInfo.InvariantCulture) == min && int.Parse(c[2], CultureInfo.InvariantCulture) == max,
                $"RandomInt bounds: stock ({c[1]}, {c[2]}), the port ({min}, {max})");
            return int.Parse(c[3], CultureInfo.InvariantCulture);
        }

        private string[] Next(string kind)
        {
            Assert.True(Pending.Count > 0, $"a {kind} draw stock did not take");
            var c = Pending.Dequeue();
            Assert.True(c[0] == kind, $"stock drew {c[0]} where the port drew {kind}");
            return c;
        }
    }

    private static void Apply(SmokeStackSim sim, string[] p)
    {
        sim.SpreadSpeed = F(p[1]); sim.Speed = F(p[2]); sim.StartSize = F(p[3]); sim.EndSize = F(p[4]);
        sim.Rate = F(p[5]); sim.JetLength = F(p[6]); sim.RollSpeed = F(p[7]); sim.BaseSpread = F(p[8]);
        sim.Twist = F(p[9]); sim.Emitting = p[10] != "0"; sim.Wind = V(p, 11);
        sim.Alpha = byte.Parse(p[17], CultureInfo.InvariantCulture);
        sim.MaxFrames = int.Parse(p[19], CultureInfo.InvariantCulture);
    }

    private static string Row(IEnumerable<float> values)
        => string.Join(",", values.Select(v => Bits(v).ToString("x8", CultureInfo.InvariantCulture)));

    [Fact]
    public void OneHundredFiftyFramesReplayBitForBit()
    {
        var frames = Stock();
        var sim = new SmokeStackSim();
        Apply(sim, frames[0].Params);
        sim.Start(0);
        sim.AlwaysSimulateUntil = F(frames[0].Head[6]);
        Assert.True(Bits(sim.InvLifetime) == Bits(F(frames[0].Params[18])), $"1/lifetime {sim.InvLifetime:R} vs stock {frames[0].Params[18]}");

        var random = new Replay();
        int states = 0, quads = 0, emitted = 0, twisted = 0;

        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            Apply(sim, frame.Params);
            sim.Rederive();

            Assert.True(sim.AlwaysSimulate == (frame.Head[3] != "0"),
                $"frame {f}: always-simulate {sim.AlwaysSimulate}, stock {frame.Head[3]}");

            bool drawn = frame.Head[2] != "0";
            foreach (var d in frame.Draws)
                random.Pending.Enqueue(d);

            emitted += sim.Emit(F(frame.Head[1]), drawn, F(frame.Head[4]), F(frame.Head[5]), V(frame.Head, 7), V(frame.Head, 10), random);
            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} recorded draw(s) untaken");

            var twist = sim.TwistMatrix;
            var st = frame.Twist!;
            Assert.True(twist.On == (st[1] != "0"), $"frame {f}: twist on {twist.On}, stock {st[1]}");
            if (twist.On)
            {
                Assert.True(Row([twist.M00, twist.M01, twist.M10, twist.M11]) == Row([F(st[2]), F(st[3]), F(st[4]), F(st[5])]),
                    $"frame {f}: the twist matrix differs");
                twisted++;
            }

            if (frame.Step is { } step)
                Assert.True(sim.Simulate(F(step[1]), drawn), $"frame {f}: stock simulated, the port returned early");

            var want = frame.State.Select(q => Row(q.Skip(2).Take(12).Select(F))).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var have = sim.Puffs.Select(p => Row(
                    [p.Position.X, p.Position.Y, p.Position.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z,
                     p.Accel.X, p.Accel.Y, p.Accel.Z, p.Lifetime, p.Angle, p.RollDelta]))
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (frame.Step is not null)
            {
                Assert.True(want.SequenceEqual(have),
                    $"frame {f}: stock holds {want.Count} puff(s), the port {have.Count}; first stock '{want.FirstOrDefault()}', port '{have.FirstOrDefault()}'");
                states += want.Count;
            }

            if (frame.Quads.Count > 0)
            {
                var wantQ = frame.Quads.Select(q => Row(q.Skip(1).Take(6).Select(F))).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var haveQ = sim.Puffs.Select(p => sim.Draw(p))
                    .Select(q => Row([q.Position.X, q.Position.Y, q.Position.Z, q.Alpha, q.Size, q.Roll]))
                    .OrderBy(x => x, StringComparer.Ordinal).ToList();
                Assert.True(wantQ.SequenceEqual(haveQ),
                    $"frame {f}: quads differ; first stock '{wantQ.FirstOrDefault()}', port '{haveQ.FirstOrDefault()}'");
                quads += wantQ.Count;
            }
        }

        Assert.True(states > 1000 && quads > 1000 && emitted > 50 && twisted > 100,
            $"the replay reached its subject ({states} states, {quads} quads, {emitted} emitted, {twisted} twisted frames)");
    }

    [Fact]
    public void AReloadCarriesThePuffsInFlight()
    {
        var frames = Stock();
        var sim = new SmokeStackSim();
        Apply(sim, frames[0].Params);
        sim.Start(0);
        var random = new Replay();
        foreach (var frame in frames.Take(60))
        {
            foreach (var d in frame.Draws)
                random.Pending.Enqueue(d);
            sim.Emit(F(frame.Head[1]), frame.Head[2] != "0", F(frame.Head[4]), F(frame.Head[5]), V(frame.Head, 7), V(frame.Head, 10), random);
            if (frame.Step is { } step)
                sim.Simulate(F(step[1]), frame.Head[2] != "0");
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        byte[] carried = buffer.ToArray();

        var fresh = new SmokeStackSim();
        Assert.True(fresh.Puffs.Count == 0 && sim.Puffs.Count > 10, $"a fresh sim has no puffs; the saved one {sim.Puffs.Count}");
        fresh.Restore(new BinaryReader(new MemoryStream(carried)));

        sim.Simulate(0.0166f, true);
        fresh.Simulate(0.0166f, true);
        Assert.True(sim.Puffs.Select(p => Bits(p.Position.X)).SequenceEqual(fresh.Puffs.Select(p => Bits(p.Position.X))),
            "the restored sim steps (twist included) as the original does");

        using var again = new MemoryStream();
        var copy = new SmokeStackSim();
        copy.Restore(new BinaryReader(new MemoryStream(carried)));
        copy.Save(new BinaryWriter(again));
        Assert.True(again.ToArray().AsSpan().SequenceEqual(carried), "a restored copy saves the same bytes");
    }
}
