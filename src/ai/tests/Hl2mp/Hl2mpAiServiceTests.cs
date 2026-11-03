using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

public class Hl2mpAiServiceTests
{
    private sealed class Lines : ILog
    {
        public List<string> Messages { get; } = new();

        public void Msg(string message) => Messages.Add(message);

        public void Warning(string message) => Messages.Add(message);
    }

    [Fact]
    public void ALevelSaysTheAiComposed()
    {
        var log = new Lines();
        new Hl2mpAiService(log).OnLevelInitPostEntity("dm_lockdown");
        Assert.Equal(["[SourceSharp/Ai] HL2MP AI composed for dm_lockdown\n"], log.Messages);
    }

    [Fact]
    public void TheServiceIsAServerSideService()
        => Assert.Equal(Side.Server, typeof(Hl2mpAiService).GetCustomAttribute<SideServiceAttribute>()?.Side);

    [Fact]
    public void ItRunsAmongTheMods()
        => Assert.True(new Hl2mpAiService(new Lines()).LifecycleOrder >= ModStages.First, "after the core's stages");
}
