using SourceSharp.Client.Effects;

using Xunit;

using static SourceSharp.Tests.TrailOracle;

namespace SourceSharp.Tests;

/// <summary>
/// The fire trail against stock <c>C_FireTrail</c> and its
/// <c>CSmokeParticle</c> emitter, recorded live
/// (<c>Fixtures/firetrail-stock.tsv</c>, README beside it): 120 frames of one
/// env_fire_trail swept back and forth so the flame gap fill runs. Bit-exact;
/// the flags byte stock never writes is replayed from the recording.
/// </summary>
public class FireTrailSimOracleTests
{
    private static List<TrailOracle.Frame> Stock()
    {
        var frames = Load("firetrail-stock.tsv");
        Assert.True(frames.Sum(f => f.Of("N").Count) > 500 && frames.Sum(f => f.Of("Q").Count) > 10000,
            "the fire oracle holds flames and smoke and their steps");
        return frames;
    }

    [Fact]
    public void OneHundredTwentyFramesReplayBitForBit()
    {
        var frames = Stock();
        var sim = new FireTrailSim();
        sim.Start(V(frames[0].Params, 2));

        var random = new Replay();
        var added = new List<SimpleParticle>();
        int spawned = 0, states = 0, quads = 0;
        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            sim.Lifetime = F(frame.Params[1]);
            Assert.True(sim.LastPosition.Equals(V(frame.Params, 2)), $"frame {f}: the last position carried over");
            random.Queue(frame);

            added.Clear();
            sim.Emit(frame.CurTime, frame.Origin, random, added);
            spawned += CheckSpawned(f, "N", frame.Of("N"), added);
            InjectFlags(sim.Emitter, frame.Of("N"));

            if (f > 0)
            {
                sim.Emitter.Simulate(frame.Dt, random);
                states += CheckState(f, "Q", frame.Of("Q"), sim.Emitter.Particles);
            }

            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} draw(s) untaken");
            quads += CheckQuads(f, "D", frame.Of("D"), sim.Emitter.Particles.Select(p => sim.Emitter.Draw(p)));
        }

        Assert.True(spawned > 500 && states > 10000 && quads > 10000, $"the replay reached its subject ({spawned} spawned, {states} states, {quads} quads)");
    }

    [Fact]
    public void AReloadCarriesTheTrailAndItsFlames()
    {
        var frames = Stock();
        var sim = new FireTrailSim();
        sim.Start(V(frames[0].Params, 2));
        var random = new Replay();
        var added = new List<SimpleParticle>();
        for (int f = 0; f < 40; f++)
        {
            random.Queue(frames[f]);
            sim.Emit(frames[f].CurTime, frames[f].Origin, random, added);
            if (f > 0)
                sim.Emitter.Simulate(frames[f].Dt, random);
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));

        var fresh = new FireTrailSim();
        Assert.True(fresh.Emitter.Particles.Count == 0 && sim.Emitter.Particles.Count > 10, "a fresh trail has no flames");
        fresh.Restore(new BinaryReader(new MemoryStream(buffer.ToArray())));
        Assert.True(fresh.LastPosition.Equals(sim.LastPosition), "the gap's start came across");
        sim.Emitter.Simulate(0.0166f, random);
        fresh.Emitter.Simulate(0.0166f, random);
        Assert.True(sim.Emitter.Particles.Select(Row).SequenceEqual(fresh.Emitter.Particles.Select(Row)), "the restored trail steps as the original does");
    }
}
