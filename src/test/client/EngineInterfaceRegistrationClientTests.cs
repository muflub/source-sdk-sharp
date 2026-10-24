using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client composition's own engine interfaces (EngineServices.AddForwarders):
/// acquired as they are registered, inside CShellClientDLL::Init, which holds
/// the engine's factory by then.
/// </summary>
public class EngineInterfaceRegistrationClientTests
{
    private static ServiceContainer Registered(EngineSide side)
    {
        var services = new ServiceContainer();
        EngineServices.AddForwarders(services, side);
        return services;
    }

    [Fact]
    public void ATestSideRegistersTheClientsEngineInterfacesAsItsDefaults()
    {
        ServiceContainer services = Registered(TestSides.Create(Side.Client));

        Assert.IsType<DefaultForwarders.NoEngineClient>(services.Require<IFwdEngineClient>());
        Assert.IsType<DefaultForwarders.NoInputSystem>(services.Require<IFwdInputSystem>());
        Assert.IsType<DefaultForwarders.NoSoundClient>(services.Require<IFwdEngineSoundClient>());
    }

    [Fact]
    public void TheEngineClientsSlicesAreTheEngineClientItself()
    {
        ServiceContainer services = Registered(TestSides.Create(Side.Client));
        IFwdEngineClient client = services.Require<IFwdEngineClient>();

        Assert.Same(client, services.Require<ISteamLaunchCommands>());
        Assert.Same(client, services.Require<ISaveMemory>());
        Assert.Same(client, services.Require<SourceSharp.Client.Input.IClientInputEngine>());
    }

    [Fact]
    public void AnEngineClientTheEngineRefusesIsRefusedByName()
    {
        string why = Assert.Throws<InterfaceAcquisitionException>(
            () => EngineBound.Client(TestSides.WithoutForwarders(Side.Client))).Message;

        Assert.Contains(EngineClient.InterfaceVersion, why);
    }
}
