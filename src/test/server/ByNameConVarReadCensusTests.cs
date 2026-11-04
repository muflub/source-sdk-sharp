using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

public class ByNameConVarReadCensusTests
{
    [Fact]
    public void TheServerReadsByNameOnlyWhatDataOrADebugRouteNames()
        => Assert.Equal(
            [
                "SourceSharp.Server.AdminMenuBuilder",  // a mod's IVoteSettingSource rows
                "SourceSharp.Server.DebugCommandRun",  // the dev API's capturing console, passing it on
                "SourceSharp.Server.DebugConsoleHook", // the dev API's /convar, a typed name
                "SourceSharp.Server.GameVoteOptions",   // a mod's IVoteSettingSource rows
            ],
            ByNameConVarReads.Callers(typeof(ServerModule).Assembly));
}
