using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

using static SourceSharp.Tests.TrailOracle;

namespace SourceSharp.Tests;

/// <summary>
/// env_embers against stock <c>C_Embers</c> and its <c>CEmberEmitter</c>,
/// recorded live (<c>Fixtures/embers-stock.tsv</c>, README beside it): 120
/// frames of one env_embers on brush *4. Spawns and steps bit-exact, with the
/// port's embers lined up in stock's step order first (the wander draws
/// follow it) and the flags byte replayed. Stock drew none of them (the brush's
/// bounds put them round the world origin, out of view), so the quads are a
/// plain fact below rather than a replay.
/// </summary>
public class EmbersSimOracleTests
{
    private static void Apply(EmbersSim sim, string[] p)
    {
        sim.Density = I(p[1]); sim.Lifetime = I(p[2]); sim.Speed = I(p[3]); sim.Emitting = p[4] != "0";
        sim.Mins = V(p, 5); sim.Maxs = V(p, 8);
        sim.R = (byte)I(p[11]); sim.G = (byte)I(p[12]); sim.B = (byte)I(p[13]);
    }

    private static List<TrailOracle.Frame> Stock()
    {
        var frames = Load("embers-stock.tsv");
        Assert.True(frames.Sum(f => f.Of("N").Count) > 50 && frames.Sum(f => f.Of("Q").Count) > 2000, "the embers oracle holds spawns and steps");
        return frames;
    }

    [Fact]
    public void OneHundredTwentyFramesReplayBitForBit()
    {
        var frames = Stock();
        var sim = new EmbersSim();
        Apply(sim, frames[0].Params);
        sim.Start();
        var random = new Replay();
        var added = new List<SimpleParticle>();
        int spawned = 0, states = 0;
        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            Apply(sim, frame.Params);
            random.Queue(frame);
            added.Clear();
            // F: frametime, curtime, angles, origin
            sim.Emit(F(frame.Head[1]), V(frame.Head, 3), random, added);
            spawned += CheckSpawned(f, "N", frame.Of("N"), added);
            InjectFlags(sim.Emitter, frame.Of("N"));
            if (frame.Of("T").Count > 0)
            {
                // The manager's own step (T), not the frame time the spawn uses.
                float dt = F(frame.Of("T")[0][1]);
                Assert.True(frame.Of("T").All(t => t[1] == frame.Of("T")[0][1]), $"frame {f}: one step for every ember");
                StockOrder(sim.Emitter, frame.Of("Q"), dt);
                sim.Emitter.Simulate(dt, random);
                states += CheckState(f, "Q", frame.Of("Q"), sim.Emitter.Particles);
            }

            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} draw(s) untaken");
        }

        Assert.True(spawned > 50 && states > 2000, $"the replay reached its subject ({spawned} spawned, {states} states)");
    }

    [Fact]
    public void AnEmberDarkensToBlackAndShrinksToNothing()
    {
        var p = new SimpleParticle { R = 200, G = 100, B = 50, StartAlpha = 255, EndAlpha = 0, StartSize = 1, EndSize = 0, Lifetime = 1.5f, DieTime = 3f };
        var q = new SimpleEmitter(new EmberEmitterRules()).Draw(p);
        Assert.True(q.Color.X == (200 * 0.5f) / 255.0f && q.Size == 0.5f && q.Alpha == 0.5f, $"half way: half the colour, size and alpha ({q.Color.X}, {q.Size}, {q.Alpha})");
    }

    [Fact]
    public void AReloadCarriesTheEmbers()
    {
        var frames = Stock();
        var sim = new EmbersSim();
        Apply(sim, frames[0].Params);
        sim.Start();
        var random = new Replay();
        var added = new List<SimpleParticle>();
        for (int f = 0; f < 40; f++)
        {
            random.Queue(frames[f]);
            sim.Emit(F(frames[f].Head[1]), V(frames[f].Head, 3), random, added);
            if (frames[f].Of("T").Count > 0)
            {
                float dt = F(frames[f].Of("T")[0][1]);
                StockOrder(sim.Emitter, frames[f].Of("Q"), dt);
                sim.Emitter.Simulate(dt, random);
            }
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        var fresh = new EmbersSim();
        Assert.True(fresh.Emitter.Particles.Count == 0 && sim.Emitter.Particles.Count > 10, "a fresh volume has no embers");
        fresh.Restore(new BinaryReader(new MemoryStream(buffer.ToArray())));
        Assert.True(fresh.Emitter.Particles.Select(Row).SequenceEqual(sim.Emitter.Particles.Select(Row)) && fresh.Density == sim.Density, "every ember and the numbers came across");
    }
}
