using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The smoke trail against stock <c>C_SmokeTrail</c> and its
/// <c>CSmokeParticle</c> emitter, recorded live
/// (<c>Fixtures/smoketrail-stock.tsv</c>, README beside it): 120 frames of one
/// env_smoketrail from its Start, every spawned puff, every puff after each
/// simulate step and every quad's numbers. Bit-exact; the one column not
/// compared is the spawned particle's flags byte, which stock never writes
/// (AddParticle does not run SimpleParticle's constructor, so it is whatever
/// the pool held; only bit 0, wind-blown, is ever read).
/// </summary>
public class SmokeTrailSimOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private static Vector V(string[] c, int at) => new(F(c[at]), F(c[at + 1]), F(c[at + 2]));

    private sealed class Frame
    {
        public required string[] Head;
        public string[] Params = [];
        public readonly List<string[]> Draws = new();
        public readonly List<string[]> Spawned = new();
        public readonly List<string[]> State = new();
        public readonly List<string[]> Quads = new();
    }

    private static List<Frame> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "smoketrail-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock smoke trail is missing: " + path);

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
                case "RI": case "RF": cur!.Draws.Add(c); break;
                case "N": cur!.Spawned.Add(c); break;
                case "Q": cur!.State.Add(c); break;
                case "D": cur!.Quads.Add(c); break;
                default: Assert.Fail($"an unknown row kind '{c[0]}' in the smoke trail oracle"); break;
            }
        }

        Assert.True(frames.Count >= 30 && frames.Sum(f => f.Spawned.Count) > 100 && frames.Sum(f => f.State.Count) > 1000,
            $"the oracle parsed ({frames.Count} frames)");
        return frames;
    }

    private sealed class Replay : IParticleRandom
    {
        public readonly Queue<string[]> Pending = new();

        public float RandomFloat(float min, float max)
        {
            var c = Next("RF");
            Assert.True(Bits(F(c[1])) == Bits(min) && Bits(F(c[2])) == Bits(max), $"RandomFloat bounds ({c[1]}, {c[2]}) vs ({min:R}, {max:R})");
            return F(c[3]);
        }

        public int RandomInt(int min, int max)
        {
            var c = Next("RI");
            Assert.True(I(c[1]) == min && I(c[2]) == max, $"RandomInt bounds ({c[1]}, {c[2]}) vs ({min}, {max})");
            return I(c[3]);
        }

        private string[] Next(string kind)
        {
            Assert.True(Pending.Count > 0, $"a {kind} draw stock did not take");
            var c = Pending.Dequeue();
            Assert.True(c[0] == kind, $"stock drew {c[0]} where the port drew {kind}");
            return c;
        }
    }

    private static void Apply(SmokeTrailSim sim, string[] p)
    {
        sim.SpawnRate = F(p[1]); sim.ParticleLifetime = F(p[2]); sim.MinSpeed = F(p[3]); sim.MaxSpeed = F(p[4]);
        sim.MinDirectedSpeed = F(p[5]); sim.MaxDirectedSpeed = F(p[6]); sim.StartSize = F(p[7]); sim.EndSize = F(p[8]);
        sim.SpawnRadius = F(p[9]); sim.Opacity = F(p[10]); sim.StartColor = V(p, 11); sim.StopEmitTime = F(p[14]);
        sim.Emitting = p[15] != "0";
    }

    /// <summary>A particle row's compared columns: positions, velocity, roll, rolldelta, life, die as bits, then the bytes (not the flags).</summary>
    private static string Row(string[] c) => Row(c.Skip(2).Take(10).Select(F), c.Skip(12).Take(7).Select(I));

    private static string Row(SimpleParticle p)
        => Row([p.Position.X, p.Position.Y, p.Position.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z, p.Roll, p.RollDelta, p.Lifetime, p.DieTime],
               [p.R, p.G, p.B, p.StartAlpha, p.EndAlpha, p.StartSize, p.EndSize]);

    private static string Row(IEnumerable<float> floats, IEnumerable<int> ints)
        => string.Join(",", floats.Select(v => Bits(v).ToString("x8", CultureInfo.InvariantCulture))
                                  .Concat(ints.Select(i => i.ToString(CultureInfo.InvariantCulture))));

    private static string Quad(IEnumerable<float> values)
        => string.Join(",", values.Select(v => Bits(v).ToString("x8", CultureInfo.InvariantCulture)));

    /// <summary>
    /// The flags byte stock never wrote, as the recording found it, put on the
    /// port's freshly spawned puffs (newest first in the emitter, spawn order in
    /// the rows): the replay's one seam into stock's uninitialised memory.
    /// </summary>
    private static void InjectFlags(SmokeTrailSim sim, List<string[]> spawned)
    {
        for (int k = 0; k < spawned.Count; k++)
            sim.Emitter.SetFlags(sim.Emitter.Particles[spawned.Count - 1 - k].Id, (byte)I(spawned[k][19]));
    }

    [Fact]
    public void OneHundredTwentyFramesReplayBitForBit()
    {
        var frames = Stock();
        var sim = new SmokeTrailSim();
        Apply(sim, frames[0].Params);
        sim.Start();

        var random = new Replay();
        var added = new List<SimpleParticle>();
        int spawned = 0, states = 0, quads = 0;

        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            Apply(sim, frame.Params);
            foreach (var d in frame.Draws)
                random.Pending.Enqueue(d);

            added.Clear();
            float dt = F(frame.Head[1]);
            sim.Emit(dt, F(frame.Head[2]), V(frame.Head, 3), V(frame.Head, 6), V(frame.Head, 9), random, added);
            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} draw(s) untaken");

            Assert.True(added.Count == frame.Spawned.Count, $"frame {f}: stock spawned {frame.Spawned.Count}, the port {added.Count}");
            InjectFlags(sim, frame.Spawned);
            for (int k = 0; k < added.Count; k++)
            {
                Assert.True(added[k].Material == I(frame.Spawned[k][1]), $"frame {f} puff {k}: material");
                Assert.True(Row(added[k]) == Row(frame.Spawned[k]), $"frame {f} puff {k}: stock {string.Join(" ", frame.Spawned[k].Skip(2))}");
                spawned++;
            }

            if (frame.State.Count > 0)
            {
                sim.Emitter.Simulate(dt, random);
                var want = frame.State.Where(q => F(q[10]) < F(q[11])).Select(Row).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var have = sim.Emitter.Particles.Select(Row).OrderBy(x => x, StringComparer.Ordinal).ToList();
                Assert.True(want.SequenceEqual(have),
                    $"frame {f}: stock holds {want.Count}, the port {have.Count}; first stock '{want.FirstOrDefault()}', port '{have.FirstOrDefault()}'");
                states += want.Count;
            }

            if (frame.Quads.Count > 0)
            {
                var want = frame.Quads.Select(q => Quad(q.Skip(1).Take(9).Select(F))).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var have = sim.Emitter.Particles.Select(p => sim.Emitter.Draw(p))
                    .Select(q => Quad([q.Position.X, q.Position.Y, q.Position.Z, q.Color.X, q.Color.Y, q.Color.Z, q.Alpha, q.Size, q.Roll]))
                    .OrderBy(x => x, StringComparer.Ordinal).ToList();
                Assert.True(want.SequenceEqual(have), $"frame {f}: quads differ; first stock '{want.FirstOrDefault()}', port '{have.FirstOrDefault()}'");
                quads += want.Count;
            }
        }

        Assert.True(spawned > 100 && states > 1000 && quads > 1000, $"the replay reached its subject ({spawned} spawned, {states} states, {quads} quads)");
    }

    [Fact]
    public void AReloadCarriesTheTrailAndItsPuffs()
    {
        var frames = Stock();
        var sim = new SmokeTrailSim();
        Apply(sim, frames[0].Params);
        sim.Start();
        var random = new Replay();
        var added = new List<SimpleParticle>();
        foreach (var frame in frames.Take(50))
        {
            foreach (var d in frame.Draws)
                random.Pending.Enqueue(d);
            float dt = F(frame.Head[1]);
            sim.Emit(dt, F(frame.Head[2]), V(frame.Head, 3), V(frame.Head, 6), V(frame.Head, 9), random, added);
            if (frame.State.Count > 0)
                sim.Emitter.Simulate(dt, random);
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        byte[] carried = buffer.ToArray();

        var fresh = new SmokeTrailSim();
        Assert.True(fresh.Emitter.Particles.Count == 0 && sim.Emitter.Particles.Count > 10, "a fresh trail has no puffs");
        fresh.Restore(new BinaryReader(new MemoryStream(carried)));
        sim.Emitter.Simulate(0.0166f, random);
        fresh.Emitter.Simulate(0.0166f, random);
        Assert.True(sim.Emitter.Particles.Select(Row).SequenceEqual(fresh.Emitter.Particles.Select(Row)), "the restored trail steps as the original does");

        using var again = new MemoryStream();
        var copy = new SmokeTrailSim();
        copy.Restore(new BinaryReader(new MemoryStream(carried)));
        copy.Save(new BinaryWriter(again));
        Assert.True(again.ToArray().AsSpan().SequenceEqual(carried), "a restored copy saves the same bytes");
    }
}
