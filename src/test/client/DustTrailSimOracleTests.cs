using SourceSharp.Client.Effects;

using Xunit;

using static SourceSharp.Tests.TrailOracle;

namespace SourceSharp.Tests;

/// <summary>
/// The dust trail against stock <c>C_DustTrail</c> and its <c>CDustFollower</c>
/// emitter, recorded live (<c>Fixtures/dusttrail-stock.tsv</c>, README
/// beside it): 120 frames of one env_dusttrail from its Start, with an emit
/// time short enough that the stop-time fade and the stop both run. Bit-exact;
/// the flags byte stock never writes is replayed from the recording.
/// </summary>
public class DustTrailSimOracleTests
{
    private static void Apply(DustTrailSim sim, string[] p)
    {
        sim.SpawnRate = F(p[1]); sim.ParticleLifetime = F(p[2]); sim.MinSpeed = F(p[3]); sim.MaxSpeed = F(p[4]);
        sim.MinDirectedSpeed = F(p[5]); sim.MaxDirectedSpeed = F(p[6]); sim.StartSize = F(p[7]); sim.EndSize = F(p[8]);
        sim.SpawnRadius = F(p[9]); sim.Opacity = F(p[10]); sim.Color = V(p, 11); sim.StopEmitTime = F(p[14]);
        sim.StartEmitTime = F(p[15]); sim.Emitting = p[16] != "0";
    }

    private static List<TrailOracle.Frame> Stock()
    {
        var frames = Load("dusttrail-stock.tsv");
        Assert.True(frames.Sum(f => f.Of("N").Count) > 20 && frames.Sum(f => f.Of("Q").Count) > 500,
            "the dust oracle holds puffs and their steps");
        Assert.True(frames.Any(f => F(f.Params[14]) != 0 && f.CurTime >= F(f.Params[14])) && frames.Any(f => f.Of("N").Count > 0),
            "the recording reaches the stop time after emitting");
        return frames;
    }

    [Fact]
    public void OneHundredTwentyFramesReplayBitForBit()
    {
        var frames = Stock();
        var sim = new DustTrailSim();
        Apply(sim, frames[0].Params);
        sim.Start();

        var random = new Replay();
        var added = new List<SimpleParticle>();
        int spawned = 0, states = 0, quads = 0;
        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            Apply(sim, frame.Params);
            random.Queue(frame);

            added.Clear();
            sim.Emit(frame.Dt, frame.CurTime, frame.Origin, frame.Angles, frame.Velocity, random, added);
            spawned += CheckSpawned(f, "N", frame.Of("N"), added);
            InjectFlags(sim.Emitter, frame.Of("N"));

            if (frame.Of("Q").Count > 0)
            {
                sim.Emitter.Simulate(frame.Dt, random);
                states += CheckState(f, "Q", frame.Of("Q"), sim.Emitter.Particles);
            }

            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} draw(s) untaken");
            if (frame.Of("D").Count > 0)
                quads += CheckQuads(f, "D", frame.Of("D"), sim.Emitter.Particles.Select(p => sim.Emitter.Draw(p)));
        }

        Assert.True(spawned > 20 && states > 500 && quads > 500, $"the replay reached its subject ({spawned} spawned, {states} states, {quads} quads)");
    }

    [Fact]
    public void AReloadCarriesTheTrailAndItsPuffs()
    {
        var frames = Stock();
        var sim = new DustTrailSim();
        Apply(sim, frames[0].Params);
        sim.Start();
        var random = new Replay();
        var added = new List<SimpleParticle>();
        foreach (var frame in frames.Take(40))
        {
            random.Queue(frame);
            sim.Emit(frame.Dt, frame.CurTime, frame.Origin, frame.Angles, frame.Velocity, random, added);
            if (frame.Of("Q").Count > 0)
                sim.Emitter.Simulate(frame.Dt, random);
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        byte[] carried = buffer.ToArray();

        var fresh = new DustTrailSim();
        Assert.True(fresh.Emitter.Particles.Count == 0 && sim.Emitter.Particles.Count > 10, "a fresh trail has no puffs");
        fresh.Restore(new BinaryReader(new MemoryStream(carried)));
        sim.Emitter.Simulate(0.0166f, random);
        fresh.Emitter.Simulate(0.0166f, random);
        Assert.True(sim.Emitter.Particles.Select(Row).SequenceEqual(fresh.Emitter.Particles.Select(Row)), "the restored trail steps as the original does");
        Assert.True(fresh.StartEmitTime == sim.StartEmitTime && fresh.StopEmitTime == sim.StopEmitTime, "and keeps its emit window");
    }
}
