using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dust motes' arms the recording does not reach (Fixtures/README-dust-stock.md):
/// a test point outside the brush, a frozen volume, a volume switched off.
/// Stock-derived from c_func_dust.cpp, not recorded.
/// </summary>
public class DustVolumeSimTests
{
    private sealed class Draws(params int[] values) : IParticleRandom
    {
        private readonly Queue<int> _q = new(values);
        public int Drawn;

        public float RandomFloat(float min, float max) => throw new InvalidOperationException("the motes draw only rand()");

        public int RandomInt(int min, int max)
        {
            Drawn++;
            return _q.TryDequeue(out int v) ? v : 16384;
        }
    }

    private sealed class Contents(params int[] answers) : IDustBrushPointTest
    {
        private readonly Queue<int> _q = new(answers);
        public readonly List<Vector> Tested = [];

        public int PointContents(Vector point)
        {
            Tested.Add(point);
            return _q.TryDequeue(out int v) ? v : DustVolumeSim.ContentsSolid;
        }
    }

    private static DustVolumeSim Volume(int flags) => new()
    {
        SpawnRate = 10, SizeMin = 2, SizeMax = 4, SpeedMax = 8, LifetimeMin = 2, LifetimeMax = 4, DistMax = 500,
        FallSpeed = 1, Flags = flags, VolumeMins = new Vector(0, 0, 0), VolumeMaxs = new Vector(100, 100, 100),
    };

    [Fact]
    public void APointTheBrushIsNotSolidAtIsPassedOverForTheNext()
    {
        var sim = Volume(DustVolumeSim.FlagOn);
        var brush = new Contents(0, 0, DustVolumeSim.ContentsSolid);
        var random = new Draws();
        Assert.True(sim.AttemptSpawn(brush, random));
        Assert.Equal(3, brush.Tested.Count);
        // three draws a test point, then velocity (3), die time and size
        Assert.Equal(3 * 3 + 3 + 2, random.Drawn);
        Assert.Equal(1, sim.SpawnCalls);
        Assert.Equal(1, sim.SpawnSolid);
        Assert.Single(sim.Motes);
    }

    [Fact]
    public void TenPointsOutsideTheBrushMakeNoMote()
    {
        var sim = Volume(DustVolumeSim.FlagOn);
        var brush = new Contents(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DustVolumeSim.ContentsSolid);
        Assert.False(sim.AttemptSpawn(brush, new Draws()));
        Assert.Equal(DustVolumeSim.SpawnTests, brush.Tested.Count);
        Assert.Empty(sim.Motes);
    }

    [Fact]
    public void AFrozenVolumeMakesOneBatchAtItsFirstTickAndNothingAfter()
    {
        var sim = Volume(DustVolumeSim.FlagOn | DustVolumeSim.FlagFrozen);
        sim.ApplyUpdate(created: true);
        var brush = new Contents();
        Assert.Equal(0, sim.Tick(0.1f, default, brush, new Draws()));
        Assert.Equal(sim.SpawnRate, sim.Motes.Count);
        Assert.Equal(0, sim.Tick(0.1f, default, brush, new Draws()));
        Assert.Equal(sim.SpawnRate, sim.Motes.Count);
    }

    [Fact]
    public void AFrozenVolumesMotesNeitherAgeNorMoveAndDrawOpaque()
    {
        var sim = Volume(DustVolumeSim.FlagOn | DustVolumeSim.FlagFrozen);
        sim.ApplyUpdate(created: true);
        sim.Tick(0.1f, default, new Contents(), new Draws());
        DustMote before = sim.Motes[0];
        sim.Simulate(1.0f, 1.0f, new Vector(100, 100, 0));
        Assert.Equal(before, sim.Motes[0]);
        // an identity view: the eye at the origin looking down -z
        var view = ParticleViewReading.From(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -200 }, 0);
        Assert.True(sim.Render(new DustMote { Position = default, Lifetime = 0, DieTime = 3, Size = 2 }, view, out _, out float alpha, out _));
        // flAlpha 1, faded by 1 + (-200 / 500), times the volume's alpha (255)
        Assert.Equal(1.0f * (1 + (-200f / 500)) * 255, alpha);
    }

    [Fact]
    public void AVolumeSwitchedOffSpawnsNothing()
    {
        var sim = Volume(0);
        sim.ApplyUpdate(created: true);
        var brush = new Contents();
        Assert.Equal(0, sim.Tick(0.1f, default, brush, new Draws()));
        Assert.Empty(brush.Tested);
        Assert.Empty(sim.Motes);
    }

    [Fact]
    public void AMotePastDistMaxIsNotDrawnAndOneAtItIs()
    {
        var sim = Volume(DustVolumeSim.FlagOn);
        var mote = new DustMote { Lifetime = 1, DieTime = 2, Size = 3 };
        var at = ParticleViewReading.From(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -500 }, 0);
        var past = ParticleViewReading.From(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -501 }, 0);
        Assert.True(sim.Render(mote, at, out _, out _, out _));
        Assert.False(sim.Render(mote, past, out _, out _, out _));
        Span<ParticleQuadRaw> quads = new ParticleQuadRaw[4];
        Assert.Equal(0, sim.Draw(past, quads));
    }

    [Fact]
    public void ADrawnMoteIsAViewSpaceQuadSortedByItsTruncatedDepth()
    {
        var sim = Volume(DustVolumeSim.FlagOn);
        sim.ApplyUpdate(created: true);
        sim.AttemptSpawn(new Contents(), new Draws());
        var view = ParticleViewReading.From(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -350 }, 0);
        Span<ParticleQuadRaw> quads = new ParticleQuadRaw[4];
        Assert.Equal(1, sim.Draw(view, quads));
        Assert.Equal((int)ParticleQuadKind.ViewColor255Size, quads[0].Kind);
        Assert.Equal(1, quads[0].FixedSort);
        Assert.Equal((float)(int)quads[0].Position.Z, quads[0].SortKey);
        Assert.Equal(sim.Motes[0].Id, quads[0].Id);
    }

    /// <summary>
    /// Stock closes the velocity on the wind by the FRAME's time (gpGlobals->frametime)
    /// and moves by the STEP's (the iterator's delta); the recording has the two equal.
    /// </summary>
    [Fact]
    public void AStepClosesOnTheWindByTheFramesTimeAndMovesByItsOwn()
    {
        var m = new DustMote { Velocity = new Vector(0, 10, 3), Lifetime = 0, DieTime = 5 };
        Assert.True(DustVolumeSim.Step(ref m, dt: 0.5f, frameTime: 0.1f, wind: new Vector(20, 0, 0)));
        // x: 0 -> +5 (50 * 0.1), not +20 or +25; y: 10 -> 5; z untouched
        Assert.Equal(new Vector(5, 5, 3), m.Velocity);
        Assert.Equal(new Vector(2.5f, 2.5f, 1.5f), m.Position);
        Assert.Equal(0.5f, m.Lifetime);
    }

    [Fact]
    public void SaveAndRestoreCarryTheMotesAndTheSpawner()
    {
        var sim = Volume(DustVolumeSim.FlagOn);
        sim.ApplyUpdate(created: true);
        sim.Tick(0.25f, new Vector(1, 2, 3), new Contents(), new Draws());
        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        var copy = new DustVolumeSim();
        copy.Restore(new BinaryReader(new MemoryStream(buffer.ToArray())));
        Assert.Equal(sim.Motes, copy.Motes);
        Assert.Equal(sim.Origin, copy.Origin);
        Assert.Equal(sim.Tick(0.05f, sim.Origin, new Contents(), new Draws()), copy.Tick(0.05f, copy.Origin, new Contents(), new Draws()));
    }
}
