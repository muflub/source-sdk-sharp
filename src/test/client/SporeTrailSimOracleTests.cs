using SourceSharp.Client.Effects;

using Xunit;

using static SourceSharp.Tests.TrailOracle;

namespace SourceSharp.Tests;

/// <summary>
/// The spore trail against stock <c>C_SporeTrail</c>, recorded live
/// (<c>Fixtures/sporetrail-stock.tsv</c>, README beside it): 120 frames of one
/// env_sporetrail with its emit flag forced on (no map can raise it). Spawns
/// and steps bit-exact; the two quads stock drew (its first frame: after that
/// its effect's bounds, kept in the trail's own space, are culled) are
/// compared through the logged attachment matrix.
/// </summary>
public class SporeTrailSimOracleTests
{
    private static List<TrailOracle.Frame> Stock()
    {
        var frames = Load("sporetrail-stock.tsv");
        Assert.True(frames.Sum(f => f.Of("N").Count) > 100 && frames.Sum(f => f.Of("Q").Count) > 1000 && frames.Sum(f => f.Of("D").Count) > 0,
            "the spore trail oracle holds spawns, steps and quads");
        return frames;
    }

    private static float[] Matrix(string[] p) => p.Skip(2).Take(12).Select(F).ToArray();

    [Fact]
    public void OneHundredTwentyFramesReplayBitForBit()
    {
        var frames = Stock();
        var sim = new SporeTrailSim { Emitting = true };
        sim.Start();

        var random = new Replay();
        var added = new List<SimpleParticle>();
        int spawned = 0, states = 0, quads = 0;
        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            sim.Emitting = frame.Params[1] != "0";
            random.Queue(frame);

            added.Clear();
            sim.Emit(frame.Dt, random, added);
            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} draw(s) untaken");
            Assert.True(added.Count == frame.Of("N").Count, $"frame {f}: stock spawned {frame.Of("N").Count}, the port {added.Count}");
            for (int k = 0; k < added.Count; k++)
            {
                Assert.True(Row(added[k]) == Row(frame.Of("N")[k]), $"frame {f} spore {k}: stock {string.Join(" ", frame.Of("N")[k].Skip(2))}");
                spawned++;
            }

            if (f > 0)
            {
                sim.Simulate(frame.Dt);
                states += CheckState(f, "Q", frame.Of("Q"), sim.Particles);
            }

            if (frame.Of("D").Count > 0)
            {
                float[] m = Matrix(frame.Params);
                quads += CheckQuads(f, "D", frame.Of("D"), sim.Particles.Select(p => SporeTrailSim.Draw(p, m)));
            }
        }

        Assert.True(spawned > 100 && states > 1000 && quads > 0, $"the replay reached its subject ({spawned} spawned, {states} states, {quads} quads)");
    }

    [Fact]
    public void NothingSpawnsStepsOrDrawsWhileNotEmitting()
    {
        var frames = Stock();
        var sim = new SporeTrailSim { Emitting = true };
        sim.Start();
        var random = new Replay();
        var added = new List<SimpleParticle>();
        random.Queue(frames[0]);
        Assert.True(sim.Emit(frames[0].Dt, random, added) > 0, "an emitting trail spawns");

        sim.Emitting = false;
        var before = sim.Particles.Select(Row).ToList();
        Assert.True(sim.Emit(0.5f, random, added) == 0, "a stopped one does not");
        sim.Simulate(0.5f);
        Assert.True(sim.Particles.Select(Row).SequenceEqual(before), "and does not step");
    }

    [Fact]
    public void AReloadCarriesTheTrailAndItsSpores()
    {
        var frames = Stock();
        var sim = new SporeTrailSim { Emitting = true };
        sim.Start();
        var random = new Replay();
        var added = new List<SimpleParticle>();
        for (int f = 0; f < 20; f++)
        {
            random.Queue(frames[f]);
            sim.Emit(frames[f].Dt, random, added);
            if (f > 0)
                sim.Simulate(frames[f].Dt);
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        var fresh = new SporeTrailSim();
        Assert.True(fresh.Particles.Count == 0 && sim.Particles.Count > 10, "a fresh trail has no spores");
        fresh.Restore(new BinaryReader(new MemoryStream(buffer.ToArray())));
        sim.Simulate(0.0166f);
        fresh.Simulate(0.0166f);
        Assert.True(fresh.Emitting && sim.Particles.Select(Row).SequenceEqual(fresh.Particles.Select(Row)), "the restored trail steps as the original does");
    }
}
