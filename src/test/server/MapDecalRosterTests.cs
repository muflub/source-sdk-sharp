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
}
