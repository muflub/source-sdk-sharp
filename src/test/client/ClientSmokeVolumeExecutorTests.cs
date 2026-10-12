using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The func_smokevolume client entity as the native draw executor sees it
/// (lane c2-sims): no volume without a model or a usable spacing; the grid
/// fills from the brush's model bounds, asking the world for each cell; the
/// fill's puffs are announced to the executor; the quads carry the distance
/// fade and the fixed sort key with ids descending; a reload carries the grid
/// and the puffs not yet announced.
/// </summary>
public class ClientSmokeVolumeExecutorTests
{
    private static readonly EntityHandle Owner = new(79u);

    private sealed class OpenAir : IClientPlaneTrace, IClientWorldLight
    {
        public int Traces;

        public (float Fraction, Vector Normal, float PlaneDist) TraceLine(Vector start, Vector end, int mask)
        {
            Traces++;
            Assert.Equal(0x400B, mask);
            return (1f, default, 0f);
        }

        public Vector LightForPoint(Vector position, bool clamp) => new(0.25f, 0.25f, 0.25f);
    }

    private static (ClientSmokeVolume Volume, RecordingEngine Engine, OpenAir Air) Volume(RecordingParticleExecutor executor, bool hasModel = true)
    {
        var engine = new RecordingEngine();
        engine.Returns("WorldQuery.ClientEntityModelBounds", hasModel);
        engine.Returns("WorldQuery.ClientEntityModelBounds.Mins", new Vector(0, 0, 0));
        engine.Returns("WorldQuery.ClientEntityModelBounds.Maxs", new Vector(96, 48, 48));
        engine.Returns("WorldQuery.PointContents", Contents.Empty);
        engine.Returns("Console.FindConVarValue", "0");
        engine.Returns("Entities.GetIndex", 79);
        engine.Returns("WorldQuery.ClientEntityModelBounds.Angles", new Vector(0, 0, 0));
        var air = new OpenAir();
        var volume = new ClientSmokeVolume(engine, executor, engine, engine, air, air, engine, new RandomSeeds(5));
        volume.BindEngine(engine, engine);
        volume.State.BindLocal();
        volume.Handle = Owner;
        volume.State.Material = "particle/particle_smokegrenade";
        volume.State.ParticleDrawWidth = 32;
        volume.State.ParticleSpacingDistance = 48;
        volume.State.DensityRampSpeed = 1;
        volume.State.RotationSpeed = 10;
        volume.State.MovementSpeed = 10;
        volume.State.Density = 1;
        return (volume, engine, air);
    }

    [Fact]
    public void TheGridFillsFromTheModelBoundsAndIsAnnounced()
    {
        var executor = new RecordingParticleExecutor();
        var (volume, _, air) = Volume(executor);

        volume.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Equal(["create 79 0 particle/particle_smokegrenade 16 True", "configure 79 0 16"], executor.Calls);
        Assert.True(volume.Sim.XCount == 2 && volume.Sim.YCount == 1 && volume.Sim.ZCount == 1 && volume.Sim.Puffs.Count == 2,
            $"a 96 x 48 x 48 box at spacing 48 is two cells ({volume.Sim.XCount}x{volume.Sim.YCount}x{volume.Sim.ZCount})");
        Assert.True(air.Traces == 12, $"six fade traces per cell ({air.Traces})");

        IManagedParticleSource source = volume;
        Span<int> ids = stackalloc int[8];
        var emit = new ParticleEmit { Dt = 0.1f, AlwaysSimulate = 1 };
        int announced = source.Emit(0, ref emit, ids);
        Assert.True(announced == 2 && emit.HasBounds == 1, $"the fill's puffs are handed over once, with the first bounds ({announced})");
        Assert.True(source.Emit(0, ref emit, ids) == 0, "and not again");

        var quads = new ParticleQuadRaw[8];
        int n = source.Draw(0, quads);
        Assert.True(n == 2 && quads[0].Id > quads[1].Id && quads[0].FadeFar == 30f && quads[0].FixedSort == 1 && quads[0].SortKey == 1f,
            "quads with ids descending, the 10..30 fade and the fixed sort key");
    }

    [Fact]
    public void NoVolumeWithoutAModelOrAUsableSpacing()
    {
        var noModel = new RecordingParticleExecutor();
        Volume(noModel, hasModel: false).Volume.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        Assert.Empty(noModel.Calls);

        var noSpacing = new RecordingParticleExecutor();
        var (volume, _, _) = Volume(noSpacing);
        volume.State.ParticleSpacingDistance = 0;
        volume.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        Assert.Empty(noSpacing.Calls);
    }

    [Fact]
    public void AReloadCarriesTheGridAndThePuffsNotYetAnnounced()
    {
        var (before, _, _) = Volume(new RecordingParticleExecutor());
        before.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var record = new SavedEntity(Owner.Value, before.ClassName, string.Empty);
        before.SaveState(record);

        var executor = new RecordingParticleExecutor();
        var (after, _, _) = Volume(executor);
        Assert.True(((IManagedParticleSource)after).Draw(0, new ParticleQuadRaw[8]) == 0, "a rebuilt entity holds no grid");

        after.LoadState(record);
        Span<int> ids = stackalloc int[8];
        var emit = new ParticleEmit { Dt = 0.1f };
        Assert.True(((IManagedParticleSource)after).Emit(0, ref emit, ids) == 2, "the unannounced puffs came across");

        after.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        Assert.True(executor.Calls.SequenceEqual(["configure 79 0 16"]),
            "and the executor's existence: no second create (" + string.Join("; ", executor.Calls) + ")");
    }
}
