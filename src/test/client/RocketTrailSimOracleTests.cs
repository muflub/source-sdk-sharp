using SourceSharp.Client.Effects;

using Xunit;

using static SourceSharp.Tests.TrailOracle;

namespace SourceSharp.Tests;

/// <summary>
/// The rocket trail against stock <c>C_RocketTrail</c>, recorded live
/// (<c>Fixtures/rockettrail-stock.tsv</c>, README beside it): 120 frames of
/// one env_rockettrail swept back and forth so the gap fill runs, damaged from
/// frame 61 on. Three emitters, each checked on its own rows: the puffs
/// (<c>CRocketTrailParticle</c>, N/Q/D), the muzzle flash (a plain
/// <c>CSimpleEmitter</c> made each frame, NF/QF/DF) and the embers
/// (<c>CEmberEffect</c> made each frame, NE/QE/DE, whose velocity step draws).
/// Bit-exact; the flags byte stock never writes is replayed from the recording.
/// </summary>
public class RocketTrailSimOracleTests
{
    private static void Apply(RocketTrailSim sim, string[] p)
    {
        sim.SpawnRate = F(p[1]); sim.ParticleLifetime = F(p[2]); sim.MinSpeed = F(p[3]); sim.MaxSpeed = F(p[4]);
        sim.StartSize = F(p[5]); sim.EndSize = F(p[6]); sim.SpawnRadius = F(p[7]); sim.Opacity = F(p[8]);
        sim.StartColor = V(p, 9); sim.StopEmitTime = F(p[12]); sim.Emitting = p[13] != "0";
        sim.Damaged = p[14] != "0"; sim.FlareScale = F(p[15]);
    }

    private static List<TrailOracle.Frame> Stock()
    {
        var frames = Load("rockettrail-stock.tsv");
        Assert.True(frames.Sum(f => f.Of("N").Count) > 100 && frames.Sum(f => f.Of("NF").Count) > 500 && frames.Sum(f => f.Of("NE").Count) > 100,
            "the rocket oracle holds puffs, flashes and embers");
        return frames;
    }

    private static List<SimpleParticle> Stepped(SimpleEmitter e, int bornBefore) => e.Particles.Where(p => p.Id < bornBefore).ToList();

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void OneHundredTwentyFramesReplayBitForBit()
    {
        var frames = Stock();
        var sim = new RocketTrailSim();
        Apply(sim, frames[0].Params);
        sim.Start(V(frames[0].Params, 16));

        var random = new Replay();
        List<SimpleParticle> flash = new(), puffs = new(), embers = new();
        int spawned = 0, states = 0, quads = 0;
        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            Apply(sim, frame.Params);
            Assert.True(Row([sim.LastPosition.X, sim.LastPosition.Y, sim.LastPosition.Z]) == Row([F(frame.Params[16]), F(frame.Params[17]), F(frame.Params[18])]),
                $"frame {f}: the last position carried over");
            random.Queue(frame);

            flash.Clear(); puffs.Clear(); embers.Clear();
            sim.Emit(F(frame.Head[12]), frame.Origin, frame.Angles, random, flash, puffs, embers);
            spawned += CheckSpawned(f, "NF", frame.Of("NF"), flash);
            spawned += CheckSpawned(f, "N", frame.Of("N"), puffs);
            spawned += CheckSpawned(f, "NE", frame.Of("NE"), embers);
            InjectFlags(sim.Flash, frame.Of("NF"));
            InjectFlags(sim.Puffs, frame.Of("N"));
            InjectFlags(sim.Embers, frame.Of("NE"));

            if (f > 0)
            {
                StockOrder(sim.Embers, frame.Of("QE"), frame.Dt);
                sim.Simulate(frame.Dt, random);
                states += CheckState(f, "Q", frame.Of("Q"), sim.Puffs.Particles);
                states += CheckState(f, "QF", frame.Of("QF"), Stepped(sim.Flash, sim.FrameFirstId));
                states += CheckState(f, "QE", frame.Of("QE"), Stepped(sim.Embers, sim.FrameFirstId));
            }

            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} draw(s) untaken");
            quads += CheckQuads(f, "D", frame.Of("D"), sim.Puffs.Particles.Select(p => sim.Puffs.Draw(p)));
            quads += CheckQuads(f, "DF", frame.Of("DF"), sim.Flash.Particles.Select(p => sim.Flash.Draw(p)));
            quads += CheckQuads(f, "DE", frame.Of("DE"), sim.Embers.Particles.Select(p => sim.Embers.Draw(p)));
        }

        Assert.True(spawned > 1000 && states > 10000 && quads > 10000, $"the replay reached its subject ({spawned} spawned, {states} states, {quads} quads)");
    }

    private static string Row(float[] v) => string.Join(",", v.Select(Bits));

    [Fact]
    public void AReloadCarriesTheTrailAndAllThreeEmitters()
    {
        var frames = Stock();
        var sim = new RocketTrailSim();
        Apply(sim, frames[0].Params);
        sim.Start(V(frames[0].Params, 16));
        var random = new Replay();
        List<SimpleParticle> scratch = new();
        for (int f = 0; f < 70; f++)
        {
            Apply(sim, frames[f].Params);
            random.Queue(frames[f]);
            sim.Emit(F(frames[f].Head[12]), frames[f].Origin, frames[f].Angles, random, scratch, scratch, scratch);
            if (f > 0)
            {
                StockOrder(sim.Embers, frames[f].Of("QE"), frames[f].Dt);
                sim.Simulate(frames[f].Dt, random);
            }
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        byte[] carried = buffer.ToArray();

        var fresh = new RocketTrailSim();
        Assert.True(fresh.Puffs.Particles.Count == 0 && sim.Puffs.Particles.Count > 10 && sim.Embers.Particles.Count > 0 && sim.Flash.Particles.Count > 0,
            "a fresh trail has nothing; the recorded one has all three");
        fresh.Restore(new BinaryReader(new MemoryStream(carried)));
        Assert.True(fresh.LastPosition.Equals(sim.LastPosition) && fresh.FrameFirstId == sim.FrameFirstId, "the gap's start and the frame's first id came across");
        Assert.True(TrailOracle.Row(sim.Embers.Particles[0]) == TrailOracle.Row(fresh.Embers.Particles[0])
                    && sim.Puffs.Particles.Select(TrailOracle.Row).SequenceEqual(fresh.Puffs.Particles.Select(TrailOracle.Row))
                    && sim.Flash.Particles.Count == fresh.Flash.Particles.Count,
            "and every emitter's particles");
    }
}
