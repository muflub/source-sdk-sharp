using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The six trail entities as the native draw executor sees them (lane
/// c2-sims): one channel per material, made on the first update and destroyed
/// with the entity; one sim step a frame, whichever channel asks first, each
/// channel handed only its own new ids; per-channel simulate and draw (ids
/// descending, CSimpleEmitter's truncated depth sort); bounds from the
/// particles; and the sim carried through a reload.
/// </summary>
public class ClientTrailsExecutorTests
{
    private static readonly EntityHandle Owner = new(81u);

    private static T Bind<T>(T trail, RecordingEngine engine) where T : ClientTrailBase
    {
        trail.BindEngine(engine, engine);
        trail.Handle = Owner;
        return trail;
    }

    private static ClientSmokeTrail Smoke(RecordingParticleExecutor executor)
    {
        var engine = new RecordingEngine();
        var trail = Bind(new ClientSmokeTrail(engine, executor, new RandomSeeds(4)), engine);
        trail.State.BindLocal();
        trail.State.SpawnRate = 40;
        trail.State.StartColor = new Vector(0.5f, 0.5f, 0.5f);
        trail.State.Opacity = 0.5f;
        trail.State.ParticleLifetime = 2;
        trail.State.MinSpeed = 2;
        trail.State.MaxSpeed = 4;
        trail.State.StartSize = 20;
        trail.State.EndSize = 40;
        trail.State.SpawnRadius = 4;
        trail.State.Emit = true;
        return trail;
    }

    private static ParticleEmit Frame(float curTime, float dt = 0.1f, Vector origin = default)
        => new() { Dt = dt, FrameTime = dt, CurTime = curTime, Origin = origin, AlwaysSimulate = 1 };

    [Fact]
    public void TheFirstUpdateMakesOneChannelPerMaterialAndTheRemovalDestroysThem()
    {
        var executor = new RecordingParticleExecutor();
        var trail = Smoke(executor);
        trail.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        trail.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        trail.OnRemoved();

        Assert.Equal(["create 81 0 particle/particle_smokegrenade 0 True", "create 81 1 particle/particle_noisesphere 0 True",
                      "destroy 81 0", "destroy 81 1"], executor.Calls);
    }

    [Fact]
    public void EveryTrailNamesAMaterialForEachChannel()
    {
        var engine = new RecordingEngine();
        var executor = new RecordingParticleExecutor();
        ClientTrailBase[] trails =
        [
            new ClientSmokeTrail(engine, executor, new RandomSeeds(1)),
            new ClientDustTrail(engine, executor, new RandomSeeds(1), engine),
            new ClientFireTrail(engine, executor, new RandomSeeds(1)),
            new ClientRocketTrail(engine, executor, new RandomSeeds(1)),
            new ClientSporeTrail(engine, executor, new RandomSeeds(1)),
            new ClientSporeExplosion(engine, executor, new RandomSeeds(1), engine),
        ];

        var names = trails.SelectMany(t => Enumerable.Range(0, t.ChannelCount).Select(c => t.ClassName + ":" + t.MaterialOf(c))).ToList();
        Assert.True(names.Count == 2 + 1 + 7 + 10 + 1 + 2, $"{names.Count} channels");
        Assert.Contains("env_rockettrail:effects/muzzleflash4", names);
        Assert.Contains("env_rockettrail:sprites/flamelet1", names);
        Assert.Contains("env_fire_trail:sprites/flamelet5", names);
        Assert.Contains("env_sporeexplosion:particle/fire", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void OneStepAFrameAndEachChannelGetsOnlyItsOwnIds()
    {
        var trail = Smoke(new RecordingParticleExecutor());
        trail.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = trail;

        Span<int> ids0 = stackalloc int[64];
        Span<int> ids1 = stackalloc int[64];
        var emit = Frame(1.0f);
        int n1 = source.Emit(1, ref emit, ids1);
        int n0 = source.Emit(0, ref emit, ids0);
        int held = trail.Sim.Emitter.Particles.Count;

        Assert.True(n0 + n1 == held && held >= 3, $"channel 1 asked first ran the frame: {n0} + {n1} of {held}");
        Assert.True(ids0[..n0].ToArray().All(id => trail.Sim.Emitter.Particles.Single(p => p.Id == id).Material == 0)
                    && ids1[..n1].ToArray().All(id => trail.Sim.Emitter.Particles.Single(p => p.Id == id).Material == 1),
            "each channel's ids are its material's");
        Assert.True(emit.HasBounds == 1 && emit.Mins.X < emit.Maxs.X, "the bounds are the particles'");

        var again = Frame(1.0f);
        Assert.True(source.Emit(0, ref again, ids0) == 0 && trail.Sim.Emitter.Particles.Count == held, "a second ask in the same frame steps nothing");
    }

    [Fact]
    public void DrawIsPerChannelIdsDescendingWithTheTruncatedDepthSort()
    {
        var trail = Smoke(new RecordingParticleExecutor());
        trail.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = trail;
        Span<int> ids = stackalloc int[64];
        for (int f = 1; f <= 5; f++)
        {
            var emit = Frame(f * 0.1f);
            source.Emit(0, ref emit, ids);
        }

        var quads = new ParticleQuadRaw[64];
        int n = source.Draw(0, quads);
        int want = trail.Sim.Emitter.Particles.Count(p => p.Material == 0);
        Assert.True(n == want && n > 1, $"channel 0 draws its {want} ({n})");
        for (int i = 1; i < n; i++)
            Assert.True(quads[i - 1].Id > quads[i].Id, "ids descending");
        Assert.True(quads[0].FixedSort == 2 && quads[0].Kind == 0 && quads[0].FadeNear == 64 && quads[0].FadeFar == 128,
            "CSimpleEmitter's truncated depth sort, ColorSizeAngle and the 64..128 near clip");
        Assert.True(source.Draw(0, new ParticleQuadRaw[1]) == n, "a short buffer is told the count it needs");
    }

    [Fact]
    public void SimulateStepsOnlyTheChannelAndReportsWhoLives()
    {
        var trail = Smoke(new RecordingParticleExecutor());
        trail.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = trail;
        Span<int> ids = stackalloc int[64];
        var emit = Frame(0.5f);
        source.Emit(0, ref emit, ids);

        var before1 = trail.Sim.Emitter.Particles.Where(p => p.Material == 1).Select(p => p.Lifetime).ToList();
        int[] held0 = trail.Sim.Emitter.Particles.Where(p => p.Material == 0).Select(p => p.Id).ToArray();
        var alive = new byte[held0.Length + 1];
        Assert.True(source.Simulate(0, 3.0f, true, [.. held0, 99999], alive) == 1, "simulate answers");
        Assert.True(alive.All(a => a == 0), "three seconds kill every two-second puff, and an unknown id is not alive");
        Assert.True(trail.Sim.Emitter.Particles.Where(p => p.Material == 1).Select(p => p.Lifetime).SequenceEqual(before1),
            "the other channel was not stepped");
    }

    [Fact]
    public void TheRocketsFlashIsDrawnTheFrameItIsBornAndDiesOnTheNext()
    {
        var engine = new RecordingEngine();
        var rocket = Bind(new ClientRocketTrail(engine, new RecordingParticleExecutor(), new RandomSeeds(9)), engine);
        rocket.State.BindLocal();
        rocket.State.FlareScale = 1.5f;
        rocket.State.StartSize = 8;
        rocket.State.EndSize = 20;
        rocket.State.ParticleLifetime = 1;
        rocket.State.Emit = true;
        rocket.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = rocket;

        Span<int> ids = stackalloc int[64];
        var emit = Frame(1.0f, 0.016f);
        source.Emit(0, ref emit, ids);
        int flash = Enumerable.Range(2, 4).Sum(c => source.Draw(c, new ParticleQuadRaw[16]));
        Assert.True(flash == 8, $"eight flash sprites over channels 2..5 ({flash})");

        for (int c = 2; c < 6; c++)
            source.Simulate(c, 0.016f, true, [], []);
        Assert.True(rocket.Sim.Flash.Particles.Count == 8, "not stepped in the frame they were born");

        var next = Frame(1.016f, 0.016f, new Vector(64, 0, 0));
        source.Emit(0, ref next, ids);
        for (int c = 2; c < 6; c++)
            source.Simulate(c, 0.016f, true, [], []);
        Assert.True(rocket.Sim.Flash.Particles.Count == 8 && rocket.Sim.Puffs.Particles.Count > 0,
            "the next frame's step kills the old flash and keeps the new; the move filled the gap with puffs");
    }

    [Fact]
    public void TheSporeExplosionReadsTheViewAndTheClipDistance()
    {
        var engine = new RecordingEngine();
        engine.Returns("Console.FindConVarValue", "100");
        var spores = Bind(new ClientSporeExplosion(engine, new RecordingParticleExecutor(), new RandomSeeds(2), engine), engine);
        spores.State.BindLocal();
        spores.State.SpawnRate = 40;
        spores.State.SpawnRadius = 16;
        spores.State.ParticleLifetime = 1;
        spores.State.Emit = true;
        spores.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = spores;

        Span<int> ids = stackalloc int[64];
        var emit = Frame(1.0f);
        emit.ViewOrigin = new Vector(500, 0, 0);
        source.Emit(0, ref emit, ids);
        Assert.True(spores.Sim.PreviousSpawnRate == 20, $"a view past cl_sporeclipdistance halves the rate ({spores.Sim.PreviousSpawnRate})");
        Assert.True(engine.Calls.Any(c => c.Member == "Console.FindConVarValue" && (string?)c.Args[0] == "cl_sporeclipdistance"),
            "the distance is the convar's");
    }

    [Fact]
    public void AReloadCarriesTheSimAndNoSecondCreate()
    {
        var before = Smoke(new RecordingParticleExecutor());
        before.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = before;
        Span<int> ids = stackalloc int[64];
        var emit = Frame(1.0f);
        source.Emit(0, ref emit, ids);

        var record = new SavedEntity(Owner.Value, before.ClassName, string.Empty);
        before.SaveState(record);

        var executor = new RecordingParticleExecutor();
        var after = Smoke(executor);
        Assert.True(after.Sim.Emitter.Particles.Count == 0 && before.Sim.Emitter.Particles.Count > 0, "a rebuilt trail holds nothing");
        after.LoadState(record);
        Assert.True(after.Sim.Emitter.Particles.Select(p => p.Id).SequenceEqual(before.Sim.Emitter.Particles.Select(p => p.Id)),
            "the puffs came across");

        after.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        Assert.True(executor.Calls.Count == 0, "and the channels' existence: no second create (" + string.Join("; ", executor.Calls) + ")");
    }

    [Fact]
    public void EmbersNeedTheirBrushAndSpawnInsideItsBounds()
    {
        var engine = new RecordingEngine();
        engine.Returns("Entities.GetIndex", 81);
        engine.Returns("WorldQuery.ClientEntityModelBounds", false);
        engine.Returns("WorldQuery.ClientEntityModelBounds.Mins", default(Vector));
        engine.Returns("WorldQuery.ClientEntityModelBounds.Maxs", default(Vector));
        engine.Returns("WorldQuery.ClientEntityModelBounds.Angles", default(Vector));
        var none = new RecordingParticleExecutor();
        var bare = Bind(new ClientEmbers(engine, none, new RandomSeeds(5), engine, engine), engine);
        bare.State.BindLocal();
        bare.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        Assert.True(none.Calls.Count == 0 && !bare.Created, "no model: no embers, as stock refused the volume");

        var engine2 = new RecordingEngine();
        engine2.Returns("Entities.GetIndex", 81);
        engine2.Returns("WorldQuery.ClientEntityModelBounds", true);
        engine2.Returns("WorldQuery.ClientEntityModelBounds.Mins", new Vector(-10, -10, -10));
        engine2.Returns("WorldQuery.ClientEntityModelBounds.Maxs", new Vector(10, 10, 10));
        engine2.Returns("WorldQuery.ClientEntityModelBounds.Angles", new Vector(0, 0, 0));
        var executor = new RecordingParticleExecutor();
        var embers = Bind(new ClientEmbers(engine2, executor, new RandomSeeds(5), engine2, engine2), engine2);
        embers.State.BindLocal();
        embers.State.Density = 50;
        embers.State.Lifetime = 2;
        embers.State.Speed = 16;
        embers.State.Emit = true;
        embers.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        Assert.Equal(["create 81 0 particle/fire 0 True"], executor.Calls);

        IManagedParticleSource source = embers;
        Span<int> ids = stackalloc int[64];
        var emit = Frame(1f, 0.1f);
        int n = source.Emit(0, ref emit, ids);
        Assert.True(n >= 4 && embers.Sim.Emitter.Particles.All(p => MathF.Abs(p.Position.X) <= 10 && MathF.Abs(p.Position.Z) <= 10),
            $"a tenth of a second at 50 a second, every one inside the brush ({n})");
    }
}
