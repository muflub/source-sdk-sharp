using SourceSharp.Abi;
using SourceSharp.Shared;
using TF2.Descent.Server.Level;
using Xunit;

namespace TF2.Descent.Tests.Level;

/// <summary>
/// The level's services as the mod's registration makes them: a consumer that names
/// <see cref="ILevelData"/> by constructor gets the one <see cref="LevelService"/>. The first
/// live boot refused tf2_descent_level ("no service is registered for ILevelData"), because a
/// [SideService] class is registered under the contracts its <c>As</c> names, not under every
/// interface it implements.
/// </summary>
public sealed class LevelRegistrationTests
{
    private static ServiceContainer Registered()
    {
        var services = new ServiceContainer();
        services.AddInstance<IEngineFiles>(new LevelServiceTests.LevelFilesOnDisk(null));
        services.AddInstance<ILog>(new LevelServiceTests.ListLog());
        SideServices.Register(services, Side.Server, typeof(LevelService).Assembly.GetTypes().Where(t => t.Namespace == typeof(LevelService).Namespace));
        return services;
    }

    [Fact]
    public void ILevelData_is_the_registered_level_service()
    {
        ServiceContainer services = Registered();

        Assert.Same(services.Resolve(typeof(LevelService)), services.Resolve(typeof(ILevelData)));
    }

    [Fact]
    public void The_dev_command_can_be_built_from_the_registration()
    {
        ServiceContainer services = Registered();

        Assert.IsType<LevelCommand>(services.Create(typeof(LevelCommand)));
    }

    [Fact]
    public void The_level_service_hears_booting()
    {
        ServiceContainer services = Registered();

        Assert.Contains(services.Resolve(typeof(LevelService)), services.ResolveAll<TF2.Descent.Server.IHostBootHook>());
    }
}
