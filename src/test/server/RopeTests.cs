using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// move_rope / keyframe_rope: the chain resolution and the two derivations the
/// server half owns — Type to segments, and the material's shape on the wire.
///
/// <para>
/// The hanging itself is not here, on purpose: the sag, the wind and the
/// drawing live in the stock C_RopeKeyframe that shell_rope_render.cpp builds
/// client-side, and a managed test asserting on a verlet ring it does not run
/// would be a check that cannot fail. What the server half owns is exactly
/// what these tests pin.
/// </para>
/// </summary>
public class RopeTests
{
    private static readonly EntityHandle Head = new(0x0052_0001u);
    private static readonly EntityHandle Tail = new(0x0052_0002u);

    private static (ScriptedEntityRegistry, RopeKeyframe) SpawnWith(string className,
        EntityHandle handle, RecordingEngine engine,
        params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(RopeKeyframe).Assembly);

        // The recording double answers the entity lookups the resolution does.
        registry.Services = ServicesWith(engine);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);

        registry.Spawn(handle, className);

        return (registry, (RopeKeyframe)registry.Get(handle)!);
    }

    private static ServiceContainer ServicesWith(RecordingEngine engine)
    {
        var services = new ServiceContainer();
        foreach (var (contract, _) in EngineServices.Areas(new RecordingLog()))
            services.AddInstance(contract, engine);
        services.AddInstance<IGameClock>(engine);
        return services;
    }

    //-------------------------------------------------------------------------
    // The two pure derivations.
    //-------------------------------------------------------------------------

    [Fact]
    public void TypeDerivesSegmentsTheWayStockDoes()
    {
        // CRopeKeyframe::KeyValue: 0 full (10), 1 semi-rigid (4), 2 rigid (2).
        Assert.Equal(10, RopeKeyframe.SegmentsForType(0));
        Assert.Equal(4, RopeKeyframe.SegmentsForType(1));
        Assert.Equal(2, RopeKeyframe.SegmentsForType(2));

        // No Type key never enters the branch, so the constructor's 5 stands.
        Assert.Equal(5, RopeKeyframe.SegmentsForType(-1));

        // A nonsense value falls through the same way.
        Assert.Equal(5, RopeKeyframe.SegmentsForType(7));
    }

    [Fact]
    public void MaterialTravelsWithoutItsExtension()
    {
        // FindMaterial refuses the suffixed form, so it is stripped at ingest.
        Assert.Equal("cable/cable", RopeKeyframe.StripVmt("cable/cable.vmt"));
        Assert.Equal("cable/cable", RopeKeyframe.StripVmt("cable/cable.VMT"));
        Assert.Equal("cable/chain", RopeKeyframe.StripVmt("cable/chain"));
    }

    //-------------------------------------------------------------------------
    // The chain resolution: one think, one answer.
    //-------------------------------------------------------------------------

    [Fact]
    public void AResolvedNextKeyIsOneSpan()
    {
        var engine = Mock();
        engine.Returns("Entities.FindByName", Tail);
        engine.Returns("Entities.GetOrigin", new Vector(96f, 128f, 230f));

        var (registry, rope) = SpawnWith(EntityNames.MoveRope, Head, engine,
            ("NextKey", "sandbox_rope1"), ("Slack", "120"), ("Width", "2"),
            ("TextureScale", "1"), ("Subdiv", "4"), ("Type", "0"),
            ("RopeMaterial", "cable/cable.vmt"));

        registry.Think(Head);

        Assert.True(rope.State.HasRope, "a resolved NextKey is a drawn span");
        Assert.Equal(96f, rope.State.EndPos.X);
        Assert.Equal(230f, rope.State.EndPos.Z);
        Assert.Equal(120f, rope.State.Slack);
        Assert.Equal(10, rope.State.Segments);
        Assert.Equal(4, rope.State.Subdiv);
        Assert.Equal("cable/cable", rope.State.Material);
    }

    [Fact]
    public void ATailLinkDrawsNothing()
    {
        var engine = Mock();

        var (registry, rope) = SpawnWith(EntityNames.KeyframeRope, Tail, engine,
            ("Slack", "30"));

        registry.Think(Tail);

        Assert.False(rope.State.HasRope, "a link with no NextKey is an anchor, not a span");
    }

    [Fact]
    public void AnUnresolvedNextKeyDrawsNothingAfterThePatienceRunsOut()
    {
        var engine = Mock();
        engine.Returns("Entities.FindByName", EntityHandle.None);

        var (registry, rope) = SpawnWith(EntityNames.KeyframeRope, Head, engine,
            ("NextKey", "no_such_link"));

        for (int i = 0; i < RopeKeyframe.ResolveAttemptLimit + 1; i++)
            registry.Think(Head);

        Assert.False(rope.State.HasRope, "a NextKey naming nothing is a warning, not a span to the origin");
    }

    [Fact]
    public void ALateSpawnedNextKeyIsWaitedFor()
    {
        // dm_lockdown's plug_wire_03 arrives from a template after the map's
        // own spawn pass; the first live run drew two spans of a three-span
        // chain because the resolution gave up on the first miss.
        var engine = Mock();
        engine.Returns("Entities.FindByName", EntityHandle.None);
        engine.Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f));

        var (registry, rope) = SpawnWith(EntityNames.KeyframeRope, Head, engine,
            ("NextKey", "late_link"));

        registry.Think(Head);
        Assert.False(rope.State.HasRope, "not resolved yet, still waiting");

        engine.Returns("Entities.FindByName", Tail);
        registry.Think(Head);

        Assert.True(rope.State.HasRope, "a link that appears late still gets its span");
        Assert.Equal(3f, rope.State.EndPos.Z);
    }
}
