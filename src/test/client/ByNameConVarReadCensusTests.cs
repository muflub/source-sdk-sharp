using Xunit;

namespace SourceSharp.Tests;

public class ByNameConVarReadCensusTests
{
    [Fact]
    public void TheClientReadsByNameOnlyWhatAFormNames()
        => Assert.Equal(
            ["SourceSharp.Client.Vgui.EngineConVarStore"], // a screen's binding, named in its markup
            ByNameConVarReads.Callers(typeof(SourceSharp.Client.ClientModule).Assembly));
}
