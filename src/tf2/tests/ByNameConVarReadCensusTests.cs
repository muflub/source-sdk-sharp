extern alias client;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests;

public class ByNameConVarReadCensusTests
{
    [Fact]
    public void TF2ReadsNoConVarByName()
        => Assert.Empty(ByNameConVarReads.Callers(typeof(TF2.Server.TFStickybombConVars).Assembly,
                                                  typeof(client::TF2.Client.PyroVisionConVars).Assembly));
}
