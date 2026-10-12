using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_dustmotes / func_dustcloud as the native draw executor sees them (lane
/// c5-particles): one channel, made on the first update that has a model and
/// destroyed with the entity; one tick a frame, whose motes go where the
/// owner's own brush answers solid; the box from the volume; the step on the
/// published wind; the draw in the view the executor names.
/// </summary>
public class ClientDustExecutorTests
{
    private static readonly EntityHandle Owner = new(81u);

    private sealed class Brush : IClientBrushContents
    {
        public readonly List<(EntityHandle Owner, Vector Point)> Asked = [];
        public int Answer = DustVolumeSim.ContentsSolid;

        public int PointContents(EntityHandle owner, Vector point)
        {
            Asked.Add((owner, point));
            return Answer;
        }
    }

    private sealed class Winds : IClientWinds
    {
        public Vector Windspeed { get; set; }
        public int Count => 1;
        public void Add(EntityHandle owner, WindThinker wind) { }
        public void Remove(EntityHandle owner) { }
    }

    private sealed class View : IParticleViewSource
    {
        public ParticleView Read() => new(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -300 }, 0);
    }

    private static (ClientDustMotes Dust, RecordingParticleExecutor Executor, Brush Brush, Winds Winds) Make(bool hasModel)
    {
        var engine = new RecordingEngine();
        engine.Returns("Entities.GetIndex", 81);
        engine.Returns("WorldQuery.ClientEntityModelBounds", hasModel);
        engine.Returns("WorldQuery.ClientEntityModelBounds.Mins", new Vector(0, 0, 0));
        engine.Returns("WorldQuery.ClientEntityModelBounds.Maxs", new Vector(100, 100, 100));
        engine.Returns("WorldQuery.ClientEntityModelBounds.Angles", default(Vector));
        var executor = new RecordingParticleExecutor();
        var brush = new Brush();
        var winds = new Winds();
        var dust = new ClientDustMotes(engine, executor, brush, winds, new ParticleViews(new View()), new RandomSeeds(3), engine, engine);
        dust.BindEngine(engine, engine);
        dust.Handle = Owner;
        dust.State.BindLocal();
        dust.State.SpawnRate = 20;
        dust.State.SizeMin = 2;
        dust.State.SizeMax = 4;
        dust.State.SpeedMax = 6;
        dust.State.LifetimeMin = 2;
        dust.State.LifetimeMax = 4;
        dust.State.DistMax = 800;
        dust.State.Alpha = 200;
        dust.State.DustFlags = DustVolumeSim.FlagOn;
        return (dust, executor, brush, winds);
    }

    private static ParticleEmit Frame(float curTime, float dt = 0.1f)
        => new() { Dt = dt, FrameTime = dt, CurTime = curTime, AlwaysSimulate = 1 };

    [Fact]
    public void NoModelNoChannel_AModelMakesOneAndTheRemovalDestroysIt()
    {
        var bare = Make(hasModel: false);
        bare.Dust.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        Assert.True(bare.Executor.Calls.Count == 0 && !bare.Dust.Created, "no model: no motes, as stock refused the volume");

        var full = Make(hasModel: true);
        full.Dust.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        full.Dust.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        full.Dust.OnRemoved();
        Assert.Equal(["create 81 0 particle/sparkles 0 True", "destroy 81 0"], full.Executor.Calls);
    }

    [Fact]
    public void ATickSpawnsWhereTheOwnersBrushIsSolidAndOnlyOnceAFrame()
    {
        var (dust, _, brush, _) = Make(hasModel: true);
        dust.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = dust;
        Span<int> ids = stackalloc int[64];
        var emit = Frame(1f);
        int n = source.Emit(0, ref emit, ids);
        Assert.True(n == 3 && dust.Sim.Motes.Count == 3, $"a tenth of a second at 20 a second, the first event at once (TimedEvent): {n}");
        Assert.True(brush.Asked.Count == 3 && brush.Asked.All(a => a.Owner == Owner), "each mote's point asked of the owner's own brush");
        Assert.True(dust.Sim.Motes.All(m => m.Position.X is >= 0 and <= 100 && m.Position.Z is >= 0 and <= 100), "inside the box");
        Assert.True(emit.HasBounds == 1 && emit.Mins.Equals(new Vector(-4, -4, -4)) && emit.Maxs.Equals(new Vector(104, 104, 104)),
            "the box handed over is the volume grown by the largest mote");

        var again = Frame(1f);
        Assert.Equal(0, source.Emit(0, ref again, ids));

        brush.Answer = 0;
        var next = Frame(1.1f);
        Assert.Equal(0, source.Emit(0, ref next, ids));
    }

    [Fact]
    public void TheStepDriftsOnThePublishedWindAndTheDrawIsInTheExecutorsView()
    {
        var (dust, _, _, winds) = Make(hasModel: true);
        dust.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = dust;
        Span<int> ids = stackalloc int[64];
        var emit = Frame(1f);
        int n = source.Emit(0, ref emit, ids);
        Vector before = dust.Sim.Motes[0].Velocity;

        winds.Windspeed = new Vector(1000, 0, 0);
        Span<byte> alive = stackalloc byte[n];
        Assert.Equal(1, source.Simulate(0, 0.1f, true, ids[..n], alive));
        Assert.True(alive.ToArray().All(a => a == 1), "every mote lives");
        Assert.Equal(before.X + 0.1f * DustVolumeSim.DustAccel, dust.Sim.Motes[0].Velocity.X);

        Span<ParticleQuadRaw> quads = new ParticleQuadRaw[8];
        Assert.Equal(n, source.Draw(0, quads));
        Assert.True(quads[0].Kind == (int)ParticleQuadKind.ViewColor255Size && quads[0].Position.Z == dust.Sim.Motes[n - 1].Position.Z - 300,
            "the newest mote first, in the view the executor named");
    }
}
