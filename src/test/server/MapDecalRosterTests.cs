using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The decals a map placed wait in the server module's roster until level init
/// paints them. It was a static list on <c>MapDecal</c>, one for the process.
/// </summary>
public class MapDecalRosterTests
{
    private static readonly EntityHandle Decal = new(0x0061_0061u);

    private static ScriptedEntityRegistry Registry(IEntityRoster<MapDecal> pending)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(InfoDecal).Assembly);

        var services = Services(out var engine);
        engine.Returns("Precache.PrecacheDecal", 3);
        services.AddInstance(pending);
        registry.Services = services;

        return registry;
    }

    private static void SpawnNamedDecal(ScriptedEntityRegistry registry)
    {
        // Named, so level init leaves it waiting rather than painting it.
        registry.KeyValue(Decal, EntityNames.Infodecal, "texture", "decals/lambda");
        registry.KeyValue(Decal, EntityNames.Infodecal, "targetname", "later");
        registry.Spawn(Decal, EntityNames.Infodecal);
    }

    [Fact]
    public void APlacedDecalWaitsInTheRosterItWasGiven()
    {
        var pending = new EntityRoster<MapDecal>();

        SpawnNamedDecal(Registry(pending));

        Assert.Single(pending);
    }

    [Fact]
    public void TwoRostersDoNotShareADecal()
    {
        var mine = new EntityRoster<MapDecal>();
        var theirs = new EntityRoster<MapDecal>();

        SpawnNamedDecal(Registry(theirs));

        Assert.Single(theirs);
        Assert.Empty(mine);
    }

    [Fact]
    public void ClearingTheRosterForgetsItsDecals()
    {
        var pending = new EntityRoster<MapDecal>();
        SpawnNamedDecal(Registry(pending));
        Assert.Single(pending);

        pending.Clear();

        Assert.Empty(pending);
    }

    private static ScriptedEntityRegistry SpawnUnnamedDecal(IEntityRoster<MapDecal> pending, out RecordingEngine engine)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(InfoDecal).Assembly);

        var services = Services(out engine);
        engine.Returns("Precache.PrecacheDecal", 3).Ignores("Effects.PaintDecal", "Entities.GetOrigin");
        services.AddInstance(pending);
        registry.Services = services;

        registry.KeyValue(Decal, EntityNames.Infodecal, "texture", "decals/lambda");
        registry.Spawn(Decal, EntityNames.Infodecal);
        return registry;
    }

    [Fact]
    public void AnUnnamedDecalMadeAfterTheLoadPaintsItselfWhenActivated()
    {
        // A round cleanup's re-create (tf2 ruling N2): the load's pass has
        // run, and the cleanup's parse activates what it made, as stock's
        // CleanUpMap does. The decal stamps itself and goes.
        var pending = new EntityRoster<MapDecal>();
        var registry = SpawnUnnamedDecal(pending, out var engine);

        Assert.True(registry.Activate(Decal));

        Assert.Single(engine.Calls, c => c.Member == "Effects.PaintDecal");
        Assert.True(registry.Directory.IsRemoved(Decal));
    }

    [Fact]
    public void ADecalTheLoadsPassPaintedIsNotPaintedAgainWhenActivated()
    {
        var pending = new EntityRoster<MapDecal>();
        var registry = SpawnUnnamedDecal(pending, out var engine);

        MapDecal.PaintPlaced(pending, new RecordingLog());
        registry.Activate(Decal);

        Assert.Single(engine.Calls, c => c.Member == "Effects.PaintDecal");
    }
}
