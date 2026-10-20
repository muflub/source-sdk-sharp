using System.Reflection;
using System.Runtime.CompilerServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_abi_hotreload D7: the static routes Abi-resident code called up through
/// are instances now, each owned by one composition. Two kinds of fact per
/// former route: the type holds no static state, and two compositions do not
/// share one.
/// </summary>
public class RouteInjectionTests
{
    // This class's own command queues and shadow run (statics to zero):
    // xUnit builds one instance per fact, so no fact sees another's.
    private readonly PlayerCommandQueue _queue = new(new RecordingLog());

    /// <summary>
    /// Every static field a type (and its nested types) declares, except
    /// constants. A route that kept a static holder would be listed here.
    /// </summary>
    internal static List<string> StaticFields(Type type)
    {
        var found = new List<string>();

        foreach (var t in new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
        {
            foreach (var field in t.GetFields(BindingFlags.Static | BindingFlags.Public
                                              | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!field.IsLiteral)
                    found.Add($"{t.Name}.{field.Name}");
            }
        }

        return found;
    }

    // ---------------------------------------------------------------------
    // EntityFactories
    // ---------------------------------------------------------------------

    [Fact]
    public void EntityFactoriesHoldsNoStaticState()
        => Assert.Empty(StaticFields(typeof(EntityFactories)));

    [Fact]
    public void EachCompositionAsksForItsOwnFactoryTable()
    {
        var engine = new ModuleEngine(TestSides.Create(Side.Server));
        Assert.NotSame(engine.EntityFactories(), engine.EntityFactories());
    }

    [Fact]
    public void APullIntoOneTableLeavesAnotherUnpulled()
    {
        var first = new EntityFactories { NativeCount = () => 1, NativeNameAt = i => i == 0 ? "prop_physics" : null };
        var second = new EntityFactories();

        first.Refresh();

        Assert.True(first.Pulled);          // the stimulus reached the subject
        Assert.False(second.Pulled);
        Assert.Empty(second.NativeNames);
    }

    // ---------------------------------------------------------------------
    // PlayerShadow.Switch
    // ---------------------------------------------------------------------

    [Fact]
    public void ThePlayerShadowHasNoSwitchStatic()
        => Assert.Null(typeof(PlayerShadow).GetProperty(
            "Switch", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));

    /// <summary>
    /// Declaring the convar writes nothing static: it used to publish itself
    /// into <c>PlayerShadow.Switch</c> from its constructor. Since B7 retired
    /// Advance (and its AdvanceSwitch) PlayerShadow has no static an object
    /// could be published into at all, which is the stronger form of this.
    /// </summary>
    [Fact]
    public void DeclaringTheShadowConVarPublishesNothingStatic()
    {
        Assert.Empty(StaticFields(typeof(SourceSharp.Server.PlayerShadowConVar)));

        // Statics to zero: PlayerShadow has no slot an object could be
        // published into at all, so a convar's construction has nowhere to
        // write. The scan's reach: it does find StaticStateWaivers' table.
        Assert.NotEmpty(StaticValues(typeof(StaticStateWaivers)));
        Assert.Empty(StaticValues(typeof(PlayerShadow)));
        _ = new SourceSharp.Server.PlayerShadowConVar();
        Assert.Empty(StaticValues(typeof(PlayerShadow)));
    }

    private static List<string> StaticValues(Type type)
        => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
               .Where(f => !f.IsLiteral && !f.FieldType.IsValueType)   // the slots an object can be published into
               .Select(f => $"{f.Name}={RuntimeHelpers.GetHashCode(f.GetValue(null))}")
               .ToList();

    /// <summary>
    /// Two compositions' bindings each read their OWN switch: a run through one
    /// asks that one's reader and never the other's.
    /// </summary>
    private static MovementRunner ServerRunner()
        => MovementRunner.For(new ModuleEngine(TestSides.Create(Side.Server)));

    [Fact]
    public void EachCompositionsRunReadsItsOwnShadowSwitch()
    {
        int askedFirst = 0, askedSecond = 0;
        var first = SourceSharp.Server.Interop.PlayerCommandsBinding.For(new Crossings(), new RecordingLog(), 
            null!, null!, ServerRunner(), new PlayerCommandQueue(new RecordingLog()), () => { askedFirst++; return false; });
        var second = SourceSharp.Server.Interop.PlayerCommandsBinding.For(new Crossings(), new RecordingLog(), 
            null!, null!, ServerRunner(), new PlayerCommandQueue(new RecordingLog()), () => { askedSecond++; return true; });

        var player = new EntityHandle(0x7FFF_0FEEu);
        try
        {
            first.PlayerRunCommands(player.Value, 0);
        }
        finally
        {
            _queue.Forget(player);
        }

        Assert.Equal(1, askedFirst);
        Assert.Equal(0, askedSecond);
    }

    // ---------------------------------------------------------------------
    // ShellLessRoutes, EntityIoRoutes, EntityRemovalRoutes
    // ---------------------------------------------------------------------

    [Fact]
    public void ShellLessRoutesHoldsNoStaticState()
        => Assert.Empty(StaticFields(typeof(ShellLessRoutes)));

    [Fact]
    public void EntityIoRoutesHoldsNoStaticState()
        => Assert.Empty(StaticFields(typeof(EntityIoRoutes)));

    [Fact]
    public void EntityRemovalRoutesHoldsNoStaticState()
        => Assert.Empty(StaticFields(typeof(EntityRemovalRoutes)));

    /// <summary>
    /// One server composition: a container built over an engine side, and the
    /// registry built from that container's routes, as ServerModule does.
    /// </summary>
    private static (ServiceContainer Services, ScriptedEntityRegistry Registry) Compose()
    {
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, TestSides.Create(Side.Server));

        var registry = new ScriptedEntityRegistry(services.Require<ShellLessRoutes>(),
                                                  services.Require<EntityIoRoutes>(),
                                                  services.Require<EntityRemovalRoutes>(), TestEngines.Server());
        registry.RegisterAll(typeof(RouteInjectionTests).Assembly);
        return (services, registry);
    }

    [Fact]
    public void ASidesContainerHoldsThatSidesOwnRoutes()
    {
        var side = TestSides.Create(Side.Server);
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, side);

        Assert.Same(side.ShellLess, services.Require<ShellLessRoutes>());
        Assert.Same(side.EntityIo, services.Require<EntityIoRoutes>());
        Assert.Same(side.Removal, services.Require<EntityRemovalRoutes>());
    }

    [Fact]
    public void TwoServerContainersEachGetTheirOwnRoutes()
    {
        var (first, _) = Compose();
        var (second, _) = Compose();

        Assert.NotSame(first.Require<ShellLessRoutes>(), second.Require<ShellLessRoutes>());
        Assert.NotSame(first.Require<EntityIoRoutes>(), second.Require<EntityIoRoutes>());
        Assert.NotSame(first.Require<EntityRemovalRoutes>(), second.Require<EntityRemovalRoutes>());
    }

    [Fact]
    public void AShellLessEntityIsClaimedOnlyByItsOwnCompositionsRoute()
    {
        var (first, firstRegistry) = Compose();
        var (second, _) = Compose();

        var handle = firstRegistry.Create("test_shellless_relay", default);

        Assert.True(first.Require<ShellLessRoutes>().Claims(handle));
        Assert.False(second.Require<ShellLessRoutes>().Claims(handle));
    }

    [Fact]
    public void AnInputReachesOnlyItsOwnCompositionsDirectory()
    {
        var (first, firstRegistry) = Compose();
        var (second, _) = Compose();

        var handle = firstRegistry.Create("test_shellless_relay", default);

        Assert.False(second.Require<EntityIoRoutes>().FireInput(handle, "Probe", null, EntityHandle.None));
        Assert.True(first.Require<EntityIoRoutes>().FireInput(handle, "Probe", null, EntityHandle.None));
    }

    [Fact]
    public void ARemovalIsMarkedOnlyInItsOwnCompositionsDirectory()
    {
        var (first, firstRegistry) = Compose();
        var (second, _) = Compose();

        var handle = firstRegistry.Create("test_shellless_relay", default);

        Assert.False(second.Require<EntityRemovalRoutes>().Remove(handle));
        Assert.False(firstRegistry.Directory.IsRemoved(handle));

        Assert.True(first.Require<EntityRemovalRoutes>().Remove(handle));
        Assert.True(firstRegistry.Directory.IsRemoved(handle));
    }
}

