using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The steam jet's simulation against stock <c>C_SteamJet</c>, recorded live
/// (<c>Fixtures/steamjet-stock.tsv</c>, README beside it): 120 frames of one
/// env_steam at a fixed pose from its <c>Start</c> on, with every random draw,
/// every light sample, the bounds, every puff's state after each step and every
/// quad's numbers. Bit-exact: the draws and samples are replayed, not re-rolled.
/// </summary>
public class SteamJetSimOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static Vector V(string[] c, int at) => new(F(c[at]), F(c[at + 1]), F(c[at + 2]));

    private sealed class Frame
    {
        public required string[] Head;
        public required string[] Params;
        public string[]? Bounds;
        public readonly List<string[]> Draws = new();
        public readonly List<string[]> Lights = new();
        public string[]? Step;
        public readonly List<string[]> State = new();
        public readonly List<string[]> Quads = new();
    }

    private static (List<string[]> StartLights, List<Frame> Frames) Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "steamjet-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock steam jet is missing: " + path);

        var start = new List<string[]>();
        var frames = new List<Frame>();
        Frame? cur = null;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] c = line.Split('\t');

            if (c[0] == "F")
            {
                cur = new Frame { Head = c, Params = [] };
                frames.Add(cur);
                continue;
            }

            if (cur is null)
            {
                Assert.True(c[0] == "L", $"only Start's light samples precede the first frame, not '{c[0]}'");
                start.Add(c);
                continue;
            }

            switch (c[0])
            {
                case "P": cur.Params = c; break;
                case "B": cur.Bounds = c; break;
                case "RI": case "RF": cur.Draws.Add(c); break;
                case "L": cur.Lights.Add(c); break;
                case "S": cur.Step = c; break;
                case "Q": cur.State.Add(c); break;
                case "D": cur.Quads.Add(c); break;
                default: Assert.Fail($"an unknown row kind '{c[0]}' in the steam jet oracle"); break;
            }
        }

        Assert.True(start.Count == SteamJetSim.RampCount && frames.Count >= 30
                    && frames.Sum(f => f.State.Count) > 500 && frames.Sum(f => f.Draws.Count) > 100,
            $"the oracle parsed ({start.Count} start samples, {frames.Count} frames)");
        return (start, frames);
    }

    /// <summary>Replays recorded draws, refusing one stock did not take or one asked with other bounds.</summary>
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
            Assert.True(c[1] == min.ToString(CultureInfo.InvariantCulture) && c[2] == max.ToString(CultureInfo.InvariantCulture),
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

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private static Func<Vector, Vector> Lights(Queue<string[]> samples) => at =>
    {
        Assert.True(samples.Count > 0, $"a light sample stock did not take, at {at.X:R} {at.Y:R} {at.Z:R}");
        var c = samples.Dequeue();
        Vector want = V(c, 1);
        Assert.True(Bits(want.X) == Bits(at.X) && Bits(want.Y) == Bits(at.Y) && Bits(want.Z) == Bits(at.Z),
            $"light sampled at {at.X:R} {at.Y:R} {at.Z:R}; stock at {c[1]} {c[2]} {c[3]}");
        return V(c, 4);
    };

    private static void Apply(SteamJetSim sim, string[] p)
    {
        sim.SpreadSpeed = F(p[1]); sim.Speed = F(p[2]); sim.StartSize = F(p[3]); sim.EndSize = F(p[4]);
        sim.Rate = F(p[5]); sim.JetLength = F(p[6]); sim.Emitting = p[7] != "0"; sim.FaceLeft = p[8] != "0";
        sim.Type = int.Parse(p[9], CultureInfo.InvariantCulture); sim.SpawnFlags = int.Parse(p[10], CultureInfo.InvariantCulture);
        sim.RollSpeed = F(p[11]);
        sim.Red = byte.Parse(p[12], CultureInfo.InvariantCulture); sim.Green = byte.Parse(p[13], CultureInfo.InvariantCulture);
        sim.Blue = byte.Parse(p[14], CultureInfo.InvariantCulture); sim.Alpha = byte.Parse(p[15], CultureInfo.InvariantCulture);
    }

    /// <summary>A row as the floats' bit patterns (and any trailing text), so equal means bit-equal.</summary>
    private static string Row(IEnumerable<float> values, params string[] tail)
        => string.Join(",", values.Select(v => Bits(v).ToString("x8", CultureInfo.InvariantCulture)).Concat(tail));

    [Fact]
    public void OneHundredTwentyFramesReplayBitForBit()
    {
        var (startLights, frames) = Stock();
        var sim = new SteamJetSim();
        Apply(sim, frames[0].Params);

        var first = frames[0].Head;
        sim.Start(V(first, 4), V(first, 7), Lights(new Queue<string[]>(startLights)));
        sim.Rederive();
        Assert.True(Bits(sim.Lifetime) == Bits(F(frames[0].Params[16])), $"lifetime {sim.Lifetime:R} vs stock {frames[0].Params[16]}");

        var random = new Replay();
        int states = 0, quads = 0, emitted = 0;

        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            Apply(sim, frame.Params);
            sim.Rederive();

            float dt = F(frame.Head[1]);
            bool drawn = frame.Head[2] != "0";
            foreach (var d in frame.Draws)
                random.Pending.Enqueue(d);

            emitted += sim.Emit(dt, drawn, V(frame.Head, 4), V(frame.Head, 7), random,
                                Lights(new Queue<string[]>(frame.Lights)), out var bounds);

            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} recorded draw(s) untaken");
            Assert.True((bounds is null) == (frame.Bounds is null), $"frame {f}: bounds set {bounds is not null}, stock {frame.Bounds is not null}");
            if (bounds is { } b && frame.Bounds is { } sb)
            {
                Assert.True(Bits(b.Min.X) == Bits(F(sb[1])) && Bits(b.Max.X) == Bits(F(sb[4])) && Bits(b.Min.Z) == Bits(F(sb[3])),
                    $"frame {f}: bounds differ");
            }

            if (frame.Step is { } step)
                Assert.True(sim.Simulate(F(step[1]), drawn), $"frame {f}: stock simulated, the port returned early");

            // The live puffs, as a multiset: the particle manager's bucket sort
            // reorders its list, so the order is not stock's to compare.
            var want = frame.State.Where(q => q[1] == "0")
                .Select(q => Row(q.Skip(2).Take(10).Select(F), q[12], q[13])).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var have = sim.Puffs.Select(p => Row(
                    [p.Position.X, p.Position.Y, p.Position.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z,
                     p.Roll, p.RollDelta, p.Lifetime, p.DieTime],
                    p.StartSize.ToString(CultureInfo.InvariantCulture), p.EndSize.ToString(CultureInfo.InvariantCulture)))
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.True(want.SequenceEqual(have),
                $"frame {f}: stock holds {want.Count} puff(s), the port {have.Count}; first stock '{want.FirstOrDefault()}', port '{have.FirstOrDefault()}'");
            states += want.Count;

            if (frame.Quads.Count > 0)
            {
                var wantQ = frame.Quads.Select(q => Row(q.Skip(1).Take(9).Select(F))).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var haveQ = sim.Puffs.Select(p => sim.Draw(p, random)).Select(q => Row(
                        [q.Position.X, q.Position.Y, q.Position.Z, q.Color.X, q.Color.Y, q.Color.Z, q.Alpha, q.Size, q.Roll]))
                    .OrderBy(x => x, StringComparer.Ordinal).ToList();
                Assert.True(wantQ.SequenceEqual(haveQ),
                    $"frame {f}: quads differ; first stock '{wantQ.FirstOrDefault()}', port '{haveQ.FirstOrDefault()}'");
                quads += wantQ.Count;
            }
        }

        Assert.True(states > 1000 && quads > 1000 && emitted > 40,
            $"the replay reached its subject ({states} puff states, {quads} quads, {emitted} emitted)");
    }

    [Fact]
    public void AReloadCarriesThePuffsInFlight()
    {
        var (startLights, frames) = Stock();
        var sim = new SteamJetSim();
        Apply(sim, frames[0].Params);
        sim.Start(V(frames[0].Head, 4), V(frames[0].Head, 7), Lights(new Queue<string[]>(startLights)));

        var random = new Replay();
        foreach (var frame in frames.Take(40))
        {
            foreach (var d in frame.Draws)
                random.Pending.Enqueue(d);
            sim.Emit(F(frame.Head[1]), frame.Head[2] != "0", V(frame.Head, 4), V(frame.Head, 7), random,
                     Lights(new Queue<string[]>(frame.Lights)), out _);
            if (frame.Step is { } step)
                sim.Simulate(F(step[1]), frame.Head[2] != "0");
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        byte[] carried = buffer.ToArray();

        var fresh = new SteamJetSim();
        Assert.True(fresh.Puffs.Count == 0 && sim.Puffs.Count > 5, $"a fresh sim has no puffs; the saved one {sim.Puffs.Count}");

        fresh.Restore(new BinaryReader(new MemoryStream(carried)));
        Assert.True(fresh.Puffs.Count == sim.Puffs.Count, $"the restore carried {fresh.Puffs.Count} of {sim.Puffs.Count} puffs");

        // The same next step from both: the restored copy continues the stream.
        sim.Simulate(0.0166f, true);
        fresh.Simulate(0.0166f, true);
        Assert.True(sim.Puffs.Select(p => Bits(p.Position.X)).SequenceEqual(fresh.Puffs.Select(p => Bits(p.Position.X))),
            "the restored sim steps as the original does");

        using var again = new MemoryStream();
        var copy = new SteamJetSim();
        copy.Restore(new BinaryReader(new MemoryStream(carried)));
        copy.Save(new BinaryWriter(again));
        Assert.True(again.ToArray().AsSpan().SequenceEqual(carried), "a restored copy saves the same bytes");
    }
}
