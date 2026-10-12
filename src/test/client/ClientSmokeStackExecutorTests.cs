using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The env_smokestack client entity as the native draw executor sees it (lane
/// c2-sims): the material is the networked model's path with its ".vmt" cut,
/// the executor starts always-simulating and is told when to stop, the
/// executor gets stock's lights, the quads are the lit kind and lit, and a
/// reload carries the column.
/// </summary>
public class ClientSmokeStackExecutorTests
{
    private static readonly EntityHandle Owner = new(78u);

    private sealed class Names(string? name) : IClientModelNames
    {
        public string? NameOf(int modelIndex) => modelIndex == 12 ? name : null;
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 100f;
    }

    private static ClientSmokeStack Stack(RecordingParticleExecutor executor, string? model, Clock clock)
    {
        var engine = new RecordingEngine();
        var stack = new ClientSmokeStack(engine, executor, new Names(model), clock, new RandomSeeds(3));
        stack.BindEngine(engine, engine);
        stack.State.BindLocal();
        stack.Handle = Owner;
        stack.State.Emit = true;
        stack.State.Speed = 30;
        stack.State.SpreadSpeed = 15;
        stack.State.StartSize = 10;
        stack.State.EndSize = 15;
        stack.State.Rate = 40;
        stack.State.JetLength = 180;
        stack.State.BaseSpread = 20;
        stack.State.MaterialModel = 12;
        stack.State.Red = 255;
        stack.State.Green = 128;
        stack.State.Blue = 0;
        stack.State.Alpha = 200;
        stack.State.AmbientColor = new Vector(40, 40, 40);
        stack.State.AmbientIntensity = 8000;
        return stack;
    }

    [Fact]
    public void TheMaterialIsTheModelPathWithoutItsExtensionAndTheLightsAreStocks()
    {
        var executor = new RecordingParticleExecutor();
        var stack = Stack(executor, "particle/SmokeStack.VMT", new Clock());

        stack.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Equal("create 78 0 particle/SmokeStack 0 True", executor.Calls[0]);
        Assert.Equal("lights 78 0 17", executor.Calls[1]);
        Assert.True(executor.LastLights[0] == 1f && executor.LastLights[1] == 128 / 255.0f && executor.LastLights[6] == 40f && executor.LastLights[9] == 8000f,
            "base colour 0..1, then the ambient light as networked");

        var unset = new RecordingParticleExecutor();
        Stack(unset, null, new Clock()).OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        Assert.Equal("create 78 0 unset_material 0 True", unset.Calls[0]);
    }

    [Fact]
    public void AlwaysSimulateTurnsOffFiveSecondsAfterStart()
    {
        var clock = new Clock();
        var stack = Stack(new RecordingParticleExecutor(), "particle/SmokeStack.vmt", clock);
        stack.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = stack;
        Span<int> ids = stackalloc int[64];

        var early = new ParticleEmit { Dt = 0.1f, CurTime = 104.9f, FrameTime = 0.1f, AlwaysSimulate = 1 };
        int added = source.Emit(0, ref early, ids);
        Assert.True(early.AlwaysSimulate == 1 && added > 0, $"still always-simulating at 4.9 s, and emitting unseen ({added})");

        var late = new ParticleEmit { Dt = 0.1f, CurTime = 105.0f, FrameTime = 0.1f, AlwaysSimulate = 1 };
        source.Emit(0, ref late, ids);
        Assert.True(late.AlwaysSimulate == 0, "off at five seconds: the executor is told to stop");

        var quads = new ParticleQuadRaw[64];
        int n = source.Draw(0, quads);
        Assert.True(n == added && quads[0].Kind == (int)ParticleQuadKind.SphereLit, "the quads are the sphere renderer's");
    }

    [Fact]
    public void AReloadCarriesTheColumn()
    {
        var before = Stack(new RecordingParticleExecutor(), "particle/SmokeStack.vmt", new Clock());
        before.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        Span<int> ids = stackalloc int[64];
        var emit = new ParticleEmit { Dt = 0.25f, CurTime = 100.25f, FrameTime = 0.25f, AlwaysSimulate = 1 };
        ((IManagedParticleSource)before).Emit(0, ref emit, ids);

        var record = new SavedEntity(Owner.Value, before.ClassName, string.Empty);
        before.SaveState(record);

        var executor = new RecordingParticleExecutor();
        var after = Stack(executor, "particle/SmokeStack.vmt", new Clock());
        Assert.True(((IManagedParticleSource)after).Draw(0, new ParticleQuadRaw[64]) == 0, "a rebuilt entity holds no puffs");

        after.LoadState(record);
        Assert.True(((IManagedParticleSource)after).Draw(0, new ParticleQuadRaw[64]) == before.Sim.Puffs.Count && before.Sim.Puffs.Count > 3,
            "the restore carried the puffs");
        Assert.True(after.Sim.AlwaysSimulate && after.Sim.AlwaysSimulateUntil == before.Sim.AlwaysSimulateUntil,
            "and the always-simulate deadline");

        after.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        // The draw after the restore asks the material's var again (a reload
        // carries no answer it can re-ask for); no second create.
        Assert.True(executor.Calls.SequenceEqual(["materialvar 78 0 $USINGPIXELSHADER", "lights 78 0 17"]),
            "and knows its executor exists: no second create (" + string.Join("; ", executor.Calls) + ")");
    }

    /// <summary>
    /// The quads arrive lit (lane c5-particles): each puff's colour is
    /// SphereLighting's for the networked lights, and a material that lights
    /// on the GPU ($USINGPIXELSHADER) skips the directional light, as
    /// CParticleSphereRenderer did. The executor is asked for the var once.
    /// </summary>
    [Fact]
    public void TheQuadsAreLitWithTheNetworkedLightsAndThePixelShaderSkip()
    {
        Vector[] Colours(int? pixelShaders, out RecordingParticleExecutor executor)
        {
            executor = new RecordingParticleExecutor { MaterialVar = pixelShaders };
            var stack = Stack(executor, "particle/SmokeStack.vmt", new Clock());
            stack.State.AmbientColor = new Vector(0.2f, 0.1f, 0.05f);
            stack.State.AmbientIntensity = 4000;
            stack.State.DirColor = new Vector(0, 0, 1);
            stack.State.DirIntensity = 90000;
            stack.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
            IManagedParticleSource source = stack;
            Span<int> ids = stackalloc int[64];
            var emit = new ParticleEmit { Dt = 0.5f, CurTime = 100.5f, FrameTime = 0.5f, AlwaysSimulate = 1, Origin = new Vector(0, 0, 0) };
            source.Emit(0, ref emit, ids);
            var quads = new ParticleQuadRaw[64];
            int n = source.Draw(0, quads);
            source.Draw(0, quads);
            Assert.True(n > 0, "the stack emitted nothing to light");
            return quads.Take(n).Select(q => q.Color).ToArray();
        }

        Vector[] gpu = Colours(1, out var gpuExecutor);
        Vector[] cpu = Colours(null, out _);

        Assert.Single(gpuExecutor.Calls, c => c == "materialvar 78 0 $USINGPIXELSHADER");
        Assert.All(gpu, c => Assert.True(c.X > 0 && c.X <= 255f, $"lit colour {c}"));
        Assert.True(gpu.Zip(cpu).All(p => p.First.Z < p.Second.Z),
            "without the pixel-shader var the directional (blue) light is added");
    }
}
