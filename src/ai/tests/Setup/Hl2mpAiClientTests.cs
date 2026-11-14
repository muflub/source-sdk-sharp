extern alias client;

using System.Reflection;
using SourceSharp.Abi;
using Xunit;

using Hl2mpAiClient = client::SourceSharp.Ai.Hl2mp.Client.Hl2mpAiClient;

namespace SourceSharp.Ai.Tests.Setup;

public class Hl2mpAiClientTests
{
    private sealed class Lines : ILog
    {
        public List<string> Messages { get; } = new();

        public void Msg(string message) => Messages.Add(message);

        public void Warning(string message) => Messages.Add(message);
    }

    [Fact]
    public void ALevelSaysTheClientSideComposed()
    {
        var log = new Lines();
        new Hl2mpAiClient(log).OnLevelInit("dm_lockdown");
        Assert.Equal(["[SourceSharp/Ai] HL2MP AI client composed for dm_lockdown\n"], log.Messages);
    }

    [Fact]
    public void TheHookIsAClientSideService()
        => Assert.Equal(Side.Client, typeof(Hl2mpAiClient).GetCustomAttribute<client::SourceSharp.Abi.SideServiceAttribute>()?.Side);
}

/// <summary>The client assemblies' services register as the client's container takes them, once each: the game-neutral bot setup, then HL2MP's, as mods.yaml lists them.</summary>
public class Hl2mpAiClientCompositionTests
{
    [Fact]
    public void TheClientAssemblyRegistersEachServiceOnce()
    {
        var services = new client::SourceSharp.Abi.ServiceContainer();
        client::SourceSharp.Abi.SideServices.Register(services, Side.Client,
            [.. typeof(client::SourceSharp.Ai.Client.Setup.CreateServerModel).Assembly.GetTypes(), .. typeof(Hl2mpAiClient).Assembly.GetTypes()]);
        Assert.True(services.IsRegistered(typeof(client::SourceSharp.Ai.Client.Setup.IBotSetupGame)));
        Assert.True(services.IsRegistered(typeof(SourceSharp.Ai.Ui.IBotSetupSource)));
        Assert.True(services.IsRegistered(typeof(SourceSharp.Ai.Ui.IBotSetupActions)));
    }
}
