using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared.Movement;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// TF2's movement view sizes the player: the spawn writes the mod's standing
/// box into the entity and hands the shadow the mod's boxes — the same
/// composed view the movement loop traces with (SsPlayer.ShadowHulls reads
/// the composition's func). Without a mod registering its own view the same
/// reading falls back to HL2's box, which the test mod's players carry.
/// </summary>
public sealed class TFPlayerHullFacts
{
    private static readonly EntityHandle Player = new(0x0004_0100u);

    private static RecordingEngine Rig()
    {
        var engine = TestServices.Fake();
        engine.Ignores("Entities.GetOrigin", "Entities.GetHealth");
        engine.Returns("Shadows.Init", true);
        return engine;
    }

    /// <summary>The mod's standing box (-24,-24,0)-(24,24,82) and ducked
    /// height 62, from TFViewVectors.Params through the composition.</summary>
    [Fact]
    public void ASpawnedTFPlayerCarriesTheModsStandingBox()
    {
        var engine = Rig();
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(),
                                  TestServices.InfiniteAux(), TestServices.PrintDamage(),
                                  shadows: new RecordingPlayerShadows(engine), entities: engine,
                                  movementParams: () => TFViewVectors.Params)
                             { Handle = Player };
        player.BindAreas(TestServices.Areas(engine));
        player.State.BindLocal();

        player.OnSpawn();

        var bounds = engine.Last("Entities.SetBounds");
        Assert.NotNull(bounds);
        Assert.Equal(Player, bounds!.Value.ArgAs<EntityHandle>(0));
        Assert.Equal(new Vector(-24f, -24f, 0f), bounds.Value.ArgAs<Vector>(1));
        Assert.Equal(new Vector(24f, 24f, 82f), bounds.Value.ArgAs<Vector>(2));

        var shadow = engine.Last("Shadows.Init")!.Value;
        Assert.Equal(new Vector(-24f, -24f, 0f), shadow.ArgAs<Vector>(1));
        Assert.Equal(new Vector(24f, 24f, 82f), shadow.ArgAs<Vector>(2));
        Assert.Equal(new Vector(-24f, -24f, 0f), shadow.ArgAs<Vector>(3));
        Assert.Equal(new Vector(24f, 24f, 62f), shadow.ArgAs<Vector>(4));
    }

    /// <summary>A hand-built player with no mod view answers HL2's box: the
    /// fallback half of the same reading (Movement.Current), the sizes the
    /// test mod's players carry.</summary>
    [Fact]
    public void TheFallbackPlayerCarriesTheHl2BoxTheTestModKnows()
    {
        var engine = Rig();
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(),
                                  TestServices.InfiniteAux(), TestServices.PrintDamage(),
                                  shadows: new RecordingPlayerShadows(engine), entities: engine)
                             { Handle = Player };
        player.BindAreas(TestServices.Areas(engine));
        player.State.BindLocal();

        player.OnSpawn();

        var bounds = engine.Last("Entities.SetBounds")!.Value;
        Assert.Equal(new Vector(-16f, -16f, 0f), bounds.ArgAs<Vector>(1));
        Assert.Equal(new Vector(16f, 16f, 72f), bounds.ArgAs<Vector>(2));
    }
}
