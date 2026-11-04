extern alias client;

using SourceSharp.Tests;

using Xunit;

namespace SourceSharp.Ai.Tests;

public class ByNameConVarReadCensusTests
{
    [Fact]
    public void TheBotsReadNoConVarByName()
        => Assert.Empty(ByNameConVarReads.Callers(typeof(client::SourceSharp.Ai.Hl2mp.Client.Setup.CreateServerModel).Assembly));
}
