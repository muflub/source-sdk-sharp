using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>cs_sv_steam_status says what the host's Steam game server answers.</summary>
public class SteamStatusCommandTests
{
    [Theory]
    [InlineData(true, "loggedon=yes")]
    [InlineData(false, "loggedon=no")]
    public void TheReadoutSaysWhetherTheGameServerIsLoggedOn(bool loggedOn, string expected)
    {
        var engine = new RecordingEngine();
        engine.Returns("Host.SteamGameServerLoggedOn", loggedOn);

        new SteamStatusCommand(engine, engine, new NoSteamGameServer()).Execute(string.Empty);

        var said = engine.Calls.Where(c => c.Member == "Console.Msg").Select(c => (string)c.Arg(0)!).ToList();
        Assert.True(said.Count == 1 && said[0].Contains("steam status: " + expected, StringComparison.Ordinal),
            "one line, naming the answer: " + string.Join(" | ", said));
    }
}
