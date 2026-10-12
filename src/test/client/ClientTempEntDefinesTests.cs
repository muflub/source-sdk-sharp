using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The half of TempEntNoCallerArmsOracleTests' eject-brass fact that is about
/// THIS build: stock's HL1EjectBrass and CSEjectBrass choose a model only under
/// HL1_CLIENT_DLL and CSTRIKE_DLL/SDK_DLL, so in a client that defines none of
/// them both arms return before drawing -- which is what the legacy tempents'
/// thunk does.
/// </summary>
public class ClientTempEntDefinesTests
{
    [Fact]
    public void TheClientIsNotTheHl1CsOrSdkClient()
    {
        IReadOnlySet<string> defines = CMakeGraph.CompileDefinitions(CMakeGraph.Client);

        Assert.Contains("CLIENT_DLL", defines);
        Assert.DoesNotContain("HL1_CLIENT_DLL", defines);
        Assert.DoesNotContain("CSTRIKE_DLL", defines);
        Assert.DoesNotContain("SDK_DLL", defines);
    }
}
