using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

using static SourceSharp.Tests.TrailOracle;

namespace SourceSharp.Tests;

/// <summary>
/// The spore explosion against stock <c>C_SporeExplosion</c> and its
/// <c>SporeEffect</c> emitter, recorded live
/// (<c>Fixtures/sporeexplosion-stock.tsv</c>, README beside it): 120 frames of
/// one env_sporeexplosion with its emit flag forced on (as a map's Enable
/// would). Bit-exact, with the port's spores lined up in stock's step order
/// before each step (the step's wander draws follow it; see
/// <see cref="TrailOracle.StockOrder"/>) and the flags byte replayed.
/// </summary>
public class SporeExplosionSimOracleTests
{
    private static void Apply(SporeExplosionSim sim, string[] p)
    {
        sim.SpawnRate = F(p[1]); sim.ParticleLifetime = F(p[3]); sim.StartSize = F(p[4]); sim.EndSize = F(p[5]);
        sim.SpawnRadius = F(p[6]); sim.Emitting = p[7] != "0";
    }

    private static List<TrailOracle.Frame> Stock()
    {
        var frames = Load("sporeexplosion-stock.tsv");
        Assert.True(frames.Sum(f => f.Of("N").Count) > 200 && frames.Sum(f => f.Of("Q").Count) > 10000,
            "the spore explosion oracle holds spores and their steps");
        return frames;
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void OneHundredTwentyFramesReplayBitForBit()
    {
        var frames = Stock();
        var sim = new SporeExplosionSim();
        Apply(sim, frames[0].Params);
        sim.Arm(F(frames[0].Params[2]));

        var random = new Replay();
        var added = new List<SimpleParticle>();
        int spawned = 0, states = 0, quads = 0;
        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            Apply(sim, frame.Params);
            Assert.True(Bits(sim.PreviousSpawnRate) == Bits(F(frame.Params[2])), $"frame {f}: the running rate carried over");
            random.Queue(frame);

            added.Clear();
            sim.Emit(frame.Dt, frame.Origin, frame.Angles, V(frame.Params, 8), F(frame.Params[11]), random, added);
            spawned += CheckSpawned(f, "N", frame.Of("N"), added);
            InjectFlags(sim.Emitter, frame.Of("N"));

            if (f > 0)
            {
                StockOrder(sim.Emitter, frame.Of("Q"), frame.Dt);
                sim.Emitter.Simulate(frame.Dt, random);
                states += CheckState(f, "Q", frame.Of("Q"), sim.Emitter.Particles);
            }

            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} draw(s) untaken");
            quads += CheckQuads(f, "D", frame.Of("D"), sim.Emitter.Particles.Select(p => sim.Emitter.Draw(p)));
        }

        Assert.True(spawned > 200 && states > 10000 && quads > 10000, $"the replay reached its subject ({spawned} spawned, {states} states, {quads} quads)");
    }

    private sealed class Fixed : IParticleRandom
    {
        public float RandomFloat(float min, float max) => min;

        public int RandomInt(int min, int max) => min;
    }

    [Fact]
    public void AViewPastTheClipDistanceHalvesTheRateAndACloserOneRestoresIt()
    {
        var sim = new SporeExplosionSim { SpawnRate = 40 };
        sim.Arm(40);
        var added = new List<SimpleParticle>();
        var origin = new Vector(0, 0, 0);

        sim.Emit(0.01f, origin, default, new Vector(600, 0, 0), 512, new Fixed(), added);
        Assert.True(sim.PreviousSpawnRate == 20, $"far: half the rate ({sim.PreviousSpawnRate})");
        sim.Emit(0.01f, origin, default, new Vector(600, 0, 0), 512, new Fixed(), added);
        Assert.True(sim.PreviousSpawnRate == 20, "and not halved again");
        sim.Emit(0.01f, origin, default, new Vector(100, 0, 0), 512, new Fixed(), added);
        Assert.True(sim.PreviousSpawnRate == 40, "near: the full rate back");
    }

    [Fact]
    public void AReloadCarriesTheCloudAndItsRate()
    {
        var frames = Stock();
        var sim = new SporeExplosionSim();
        Apply(sim, frames[0].Params);
        sim.Arm(F(frames[0].Params[2]));
        var random = new Replay();
        var added = new List<SimpleParticle>();
        for (int f = 0; f < 30; f++)
        {
            random.Queue(frames[f]);
            sim.Emit(frames[f].Dt, frames[f].Origin, frames[f].Angles, V(frames[f].Params, 8), F(frames[f].Params[11]), random, added);
            if (f > 0)
            {
                StockOrder(sim.Emitter, frames[f].Of("Q"), frames[f].Dt);
                sim.Emitter.Simulate(frames[f].Dt, random);
            }
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        var fresh = new SporeExplosionSim();
        Assert.True(fresh.Emitter.Particles.Count == 0 && sim.Emitter.Particles.Count > 10, "a fresh explosion has no spores");
        fresh.Restore(new BinaryReader(new MemoryStream(buffer.ToArray())));
        Assert.True(fresh.PreviousSpawnRate == sim.PreviousSpawnRate && fresh.Emitter.Particles.Select(Row).SequenceEqual(sim.Emitter.Particles.Select(Row)),
            "the running rate and every spore came across");
    }
}
