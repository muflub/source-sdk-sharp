using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client composition's own engine interfaces (EngineServices.AddTo):
/// non-null forwarders that acquire on first call, because the engine client
/// exists only once client.so is up, after the module registers.
/// </summary>
public class BoundForwarderClientTests
{
    private static ServiceContainer Registered(EngineSide side)
    {
        var services = new ServiceContainer();
        EngineServices.AddForwarders(services, side);
        return services;
    }

    [Fact]
    public void TheClientsEngineInterfacesAreRegisteredWithoutAskingTheEngine()
    {
        ServiceContainer services = Registered(TestSides.WithoutForwarders(Side.Client));

        Assert.NotNull(services.Require<IFwdEngineClient>());
        Assert.NotNull(services.Require<IFwdInputSystem>());
        Assert.NotNull(services.Require<IFwdEngineSoundClient>());
    }

    [Fact]
    public void TheEngineClientIsAskedForAtItsFirstCallAndARefusalNamesIt()
    {
        IFwdEngineClient engine = Registered(TestSides.WithoutForwarders(Side.Client)).Require<IFwdEngineClient>();

        string why = Assert.Throws<InterfaceAcquisitionException>(() => engine.IsInGame()).Message;

        Assert.Contains(EngineClient.InterfaceVersion, why);
    }

    [Fact]
    public void ATestSidesEngineClientAnswersNothing()
    {
        IFwdEngineClient engine = Registered(TestSides.Create(Side.Client)).Require<IFwdEngineClient>();

        Assert.False(engine.IsInGame());
        Assert.Null(engine.LevelName());
        Assert.False(engine.TryGetPlayerInfo(1, out _));
    }
}
