extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

//-----------------------------------------------------------------------------
// Fixtures. Entity classes declared here rather than reusing real ones, so a
// failure below points at the wiring and not at a change in game behaviour.
//-----------------------------------------------------------------------------

/// <summary>
/// Entities built from the container, on both sides.
///
/// <para>
/// The failure worth guarding against is not "the entity did not spawn" — that
/// is loud. It is an entity that spawns, takes every keyvalue in the map, and
/// consults something that is not the live system: it thinks, it fires its
/// outputs, and it is wrong in a way nothing logs. So these check WHICH object
/// arrived, not merely that one did.
/// </para>
/// </summary>
public class EntityInjectionTests
{
    private static readonly EntityHandle Ent = new(0x0700_0001u);
    private static readonly EntityHandle Other = new(0x0700_0002u);

    private static (ScriptedEntityRegistry Registry, TestSystem System) Server(int answer = 42)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EntityInjectionTests).Assembly);

        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(EntityInjectionTests).Assembly);

        var system = new TestSystem(answer);
        var services = new ServiceContainer();

        convars.PublishTo(services);
        services.AddInstance<ITestSystem>(system);
        services.AddInstance<IEntityDirectory>(registry);

        registry.Services = services;
        return (registry, system);
    }

    [Fact]
    public void ServerEntitiesResolve()
    {
        var (registry, system) = Server();

        registry.Spawn(Ent, "test_injected");
        var entity = registry.Get(Ent) as TestInjectedEntity;

        Assert.True(entity is not null, "an entity with dependencies is built");
        Assert.True(entity is not null && ReferenceEquals(entity.System, system), "and handed the live system");
        Assert.True(entity is not null && entity.Reach.Value == 3.5f, "and the convar it asked for by class");
        Assert.True(entity is not null && entity.Spawned, "and its own spawn still ran");

        // The map's values still reach it, which is the thing most easily broken
        // by moving where the instance comes from: the instance is created when
        // the FIRST KEYVALUE arrives, not at spawn, and an entity built at spawn
        // would have missed every key in the map.
        var second = new ScriptedEntityRegistry();
        second.RegisterAll(typeof(EntityInjectionTests).Assembly);
        second.Services = registry.Services;
        second.KeyValue(Other, "test_injected", "targetname", "whatever");

        Assert.True(second.Get(Other) is TestInjectedEntity built && ReferenceEquals(built.System, system), "an entity built by its first keyvalue is injected too");

        // By name, which is the key a map has and a class name cannot be.
        registry.Spawn(Other, "test_injected_by_name");
        Assert.True(registry.Get(Other) is TestNamedInjectionEntity named
            && named.Reach.Name == "ss_test_entity_reach", "a convar reaches an entity by the name a player types");
    }

    [Fact]
    public void ServerEntitiesWithoutAContainer()
    {
        // No container at all, which is every test written before this existed
        // and every module that has not been handed one.
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EntityInjectionTests).Assembly);

        registry.Spawn(Ent, "test_injection_plain");
        Assert.True(registry.Get(Ent) is TestPlainEntity plain && plain.Spawned, "an entity that asks for nothing still spawns with no container");

        // And the one that DOES need something is refused rather than taking the
        // map load with it, which is what Activator.CreateInstance would have
        // done: it throws on a class with no parameterless constructor.
        registry.Spawn(Other, "test_injected");
        Assert.True(registry.Get(Other) is null, "and one that needs a container is refused, not thrown");
    }

    [Fact]
    public void AnEntityThatCannotBeBuilt()
    {
        var (registry, _) = Server();

        registry.Spawn(Ent, "test_injection_missing");
        Assert.True(registry.Get(Ent) is null, "an entity whose service is missing is refused");

        // The important half: the map carries on. An entity class nobody can
        // build must not stop the next entity in the map from spawning.
        registry.Spawn(Other, "test_injection_plain");
        Assert.True(registry.Get(Other) is TestPlainEntity, "and the next entity in the map still spawns");
    }

    [Fact]
    public void TheSameSystemReachesEveryInstance()
    {
        var (registry, system) = Server(answer: 7);

        // Two instances of one class, which is what a map places: they are two
        // objects sharing one system, not two systems.
        registry.Spawn(Ent, "test_injected");
        registry.Spawn(Other, "test_injected");

        var first = registry.Get(Ent) as TestInjectedEntity;
        var second = registry.Get(Other) as TestInjectedEntity;

        Assert.True(first is not null && second is not null && !ReferenceEquals(first, second), "two placements are two entities");
        Assert.True(first is not null && second is not null && ReferenceEquals(first.System, second.System), "sharing one system");
        Assert.True(first is not null && first.System.Answer == 7, "and it is the live one");

        // The directory an entity is handed is the registry it lives in, which
        // is what makes "find the entity the mapper named" work at all.
        registry.Spawn(new EntityHandle(0x0700_0003u), "test_injection_directory");
        Assert.True(registry.Get(new EntityHandle(0x0700_0003u)) is TestDirectoryEntity directory
            && ReferenceEquals(directory.Directory, registry), "the entity directory is the registry the entity is in");
    }

    /// <summary>
    /// The real client entities, against the real registries.
    ///
    /// <para>
    /// The fixtures above prove the wiring; this proves the wiring is what the
    /// shipped classes actually use. Both of these put THEMSELVES into a
    /// registry at spawn and take themselves out when they go, which is the
    /// other shape a dependency takes — and the shape that was a static list
    /// until now. A static one outlives a managed reload, so the previous
    /// load's lights would go on being refreshed against dlight slots the new
    /// load believes are free.
    /// </para>
    /// </summary>
    [Fact]
    public void RealClientEntitiesReachTheirRegistries()
    {
        var lights = new DynamicLights(ClientDoubles.Fake());
        var corrections = new ColorCorrections(ClientDoubles.Fake());

        var registry = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        registry.RegisterAll(typeof(ClientDynamicLight).Assembly);
        registry.Services = new client::SourceSharp.Abi.ServiceContainer()
            .AddInstance<IDynamicLights>(lights)
            .AddInstance<IColorCorrections>(corrections);

        var light = new EntityHandle(0x0701_0001u);
        var spot = new EntityHandle(0x0701_0002u);
        var correction = new EntityHandle(0x0701_0003u);

        Assert.True(registry.Create(light, EntityNames.LightDynamic), "light_dynamic is created from the container");
        Assert.True(registry.Create(spot, EntityNames.SpotlightEnd), "spotlight_end too");
        Assert.True(registry.Create(correction, EntityNames.ColorCorrection), "color_correction too");

        Assert.True(lights.LiveCount == 0 && corrections.LiveCount == 0, "nothing is registered before they spawn");

        registry.Get(light)!.OnSpawn();
        registry.Get(spot)!.OnSpawn();
        registry.Get(correction)!.OnSpawn();

        Assert.True(lights.LiveCount == 2, "both lights put themselves in the registry they were handed");

        // The correction does NOT, and that is its own rule rather than a gap
        // here: it registers on the first update carrying a lookup filename,
        // because this side has no state at all until one arrives and the file
        // has to load before there is anything to weigh. With no engine the
        // load cannot happen, so what is checked is that it was BUILT with its
        // registry — the injection — and not that it used it.
        Assert.True(corrections.LiveCount == 0, "the correction does not register before its lookup loads");
        Assert.True(registry.Get(correction) is ClientColorCorrection, "but it was built with one all the same");

        registry.Remove(light);
        Assert.True(lights.LiveCount == 1, "removing one takes it out again");

        registry.Remove(spot);
        Assert.True(lights.LiveCount == 0, "and the last one empties it");

        // A second registry is a second world: two loads of the client module
        // do not share a list, which is the whole reason these stopped being
        // statics.
        var second = new DynamicLights(ClientDoubles.Fake());
        var other = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        other.RegisterAll(typeof(ClientDynamicLight).Assembly);
        other.Services = new client::SourceSharp.Abi.ServiceContainer().AddInstance<IDynamicLights>(second);

        other.Create(light, EntityNames.LightDynamic);
        other.Get(light)!.OnSpawn();

        Assert.True(second.LiveCount == 1, "a second registry keeps its own lights");
        Assert.True(lights.LiveCount == 0, "and does not touch the first");
    }

    [Fact]
    public void ClientEntitiesResolve()
    {
        var registry = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        registry.RegisterAll(typeof(EntityInjectionTests).Assembly);

        var system = new TestSystem(11);
        registry.Services = new client::SourceSharp.Abi.ServiceContainer().AddInstance<ITestSystem>(system);

        Assert.True(registry.Create(Ent, "test_injected"), "a client entity with dependencies is created");
        Assert.True(registry.Get(Ent) is TestInjectedClientEntity client
            && ReferenceEquals(client.System, system), "and handed the live system");

        Assert.True(registry.Create(Other, "test_injection_plain"), "a client entity that asks for nothing is created");

        // Refused, and reported as refused — the shell still exists natively and
        // still draws whatever it draws, exactly as an unclaimed classname does.
        Assert.True(!registry.Create(new EntityHandle(0x0700_0004u), "test_injection_missing"), "a client entity whose service is missing is refused");

        // With no container, the same split as the server's.
        var bare = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        bare.RegisterAll(typeof(EntityInjectionTests).Assembly);
        bare.Services = ClientDoubles.Services(out var bareEngine);

        // The index the creation push caches (Phase X4): one ask per entity, so
        // that the lights stop asking once a frame. This test is about which
        // constructor the container can satisfy.
        bareEngine.Ignores("Entities.GetIndex");

        Assert.True(bare.Create(Ent, "test_injection_plain"), "with no container a plain client entity is still created");
        Assert.True(!bare.Create(Other, "test_injected"), "and one that needs a container is refused, not thrown");
    }

    /// <summary>
    /// plan_abi_hotreload D8: a client registry binds its entities to the
    /// engine it was HANDED, with no container in front of it. It used to fall
    /// back to the active side's process-wide defaults, so two registries
    /// (two loads of the client module side by side) shared one engine.
    /// </summary>
    [Fact]
    public void TwoClientRegistriesBindToTheirOwnEngines()
    {
        var firstEngine = ClientDoubles.Fake().Returns("Entities.GetIndex", 3);
        var secondEngine = ClientDoubles.Fake().Returns("Entities.GetIndex", 5);

        var first = new ClientStandaloneRegistry(ClientDoubles.Areas(firstEngine));
        var second = new ClientStandaloneRegistry(ClientDoubles.Areas(secondEngine));
        first.RegisterAll(typeof(EntityInjectionTests).Assembly);
        second.RegisterAll(typeof(EntityInjectionTests).Assembly);

        Assert.True(first.Create(Ent, "test_injection_plain"), "the first registry creates its entity");
        Assert.True(second.Create(Other, "test_injection_plain"), "and the second its own");

        Assert.True(firstEngine.Calls.Count(c => c.Member == "Entities.GetIndex") == 1
            && Equals(firstEngine.Calls.Single(c => c.Member == "Entities.GetIndex").Args[0], Ent),
            "the first registry asked its own engine for its entity's index");
        Assert.True(secondEngine.Calls.Count(c => c.Member == "Entities.GetIndex") == 1
            && Equals(secondEngine.Calls.Single(c => c.Member == "Entities.GetIndex").Args[0], Other),
            "and the second asked its own, not the first's");
        Assert.True(first.GetByIndex(3) is not null && second.GetByIndex(5) is not null,
            "each entity carries the index its own engine answered");
    }
}
