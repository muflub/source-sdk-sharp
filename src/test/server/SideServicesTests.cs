using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The side's own services are registered in its container before any binding
/// is built (plan_engineside_removal Phase 2), so a binding or adapter takes
/// each one by constructor rather than reaching into the side for it.
/// </summary>
public class SideServicesTests
{
    private sealed class TakesFactory(NativeInterfaceFactory interfaces)
    {
        public NativeInterfaceFactory Interfaces { get; } = interfaces;
    }

    [Fact]
    public void TheServersOwnServicesAreRegisteredBeforeItsBindings()
    {
        EngineSide side = TestSides.Create(Side.Server);
        var services = new ServiceContainer();

        EngineServices.AddBindingServices(services, side);

        Assert.Same(side.Attachment.Interfaces, services.Require<NativeInterfaceFactory>());
        Assert.Same(side.ShellLess, services.Require<ShellLessRoutes>());
        Assert.Same(side.EntityIo, services.Require<EntityIoRoutes>());
        Assert.Same(side.Removal, services.Require<EntityRemovalRoutes>());
        Assert.Same(side.Log, services.Require<ILog>());
        Assert.Same(side.Lines, services.Require<ConsoleLines>());
        Assert.Same(side.Attachment.VPhysics, services.Require<VPhysicsTables>());
        Assert.Same(side.Liveness, services.Require<SideLiveness>());
        Assert.Same(side.Motion, services.Require<EntityPhysicsMotion>());
    }

    /// <summary>A module's composition holds the same services, through its engine.</summary>
    [Fact]
    public void AModulesCompositionHoldsTheSidesServices()
    {
        EngineSide side = TestSides.Create(Side.Server);
        // The render binding takes the composition's mirror, as the module registers it first.
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());

        int areas = new ServerModuleEngine(side).AddTo(services);

        Assert.True(areas > 0, "the bindings were built");
        Assert.Same(side.ShellLess, services.Require<ShellLessRoutes>());
        Assert.Same(side.Liveness, services.Require<SideLiveness>());
        Assert.Same(side.Motion, services.Require<EntityPhysicsMotion>());
    }

    /// <summary>
    /// The interface factory is the container's like every other service: a
    /// consumer built as a binding is is handed the registered one, and a
    /// container without one cannot build it (no special case answers it from
    /// the side).
    /// </summary>
    [Fact]
    public void AConsumerIsHandedTheContainersInterfaceFactory()
    {
        EngineSide side = TestSides.Create(Side.Server);
        var other = SideAttachment.Detached(Side.Server, new SideState(Side.Server)).Interfaces;
        var services = new ServiceContainer().AddInstance(other);

        var built = (TakesFactory)EngineServices.Create(typeof(TakesFactory), side, services);

        Assert.Same(other, built.Interfaces);
        Assert.NotSame(side.Attachment.Interfaces, built.Interfaces);
        Assert.Throws<ServiceResolutionException>(
            () => EngineServices.Create(typeof(TakesFactory), side, new ServiceContainer()));
    }
}
