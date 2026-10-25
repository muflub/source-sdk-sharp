using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's own services, registered before its bindings
/// (plan_engineside_removal Phase 2): the shared set, and none of the
/// server's (its entity motion).
/// </summary>
public class SideServicesClientTests
{
    [Fact]
    public void TheClientsOwnServicesAreRegisteredBeforeItsBindings()
    {
        EngineSide side = TestSides.Create(Side.Client);
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
        Assert.Null(services.Resolve<EntityPhysicsMotion>());
    }
}
