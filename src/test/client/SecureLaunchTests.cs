using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Steam;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Stock's CSecureLaunchSystem and UnsafeCmdLineProcessor
/// (game/shared/secure_command_line.cpp, the non-TF arm), in C#: a Steam
/// rich-presence join or URL launch hands the client an untrusted line, and
/// only <c>+connect &lt;address&gt;</c> made of the safe characters runs.
/// </summary>
public class SecureLaunchTests
{
    private sealed class Commands : ISteamLaunchCommands
    {
        public readonly List<string> Ran = new();
        public void ConnectFromSteamLaunch(string connectLine) => Ran.Add(connectLine);
    }

    private sealed class Log : ILog
    {
        public readonly List<string> Lines = new();
        public void Msg(string m) => Lines.Add(m);
        public void Warning(string m) => Lines.Add(m);
    }

    private static (SecureLaunch Launch, Commands Commands, Log Log, SteamCallbacksTests.SteamDouble Steam) Make(int developer = 1)
    {
        var commands = new Commands();
        var log = new Log();
        var steam = new SteamCallbacksTests.SteamDouble();
        return (new SecureLaunch(steam, commands, log, () => developer), commands, log, steam);
    }

    // GameRichPresenceJoinRequested_t: CSteamID at 0, char[256] at 8, 264 bytes.
    private static byte[] JoinPayload(string connect, ulong friend = 76561197960287930UL)
    {
        var payload = new byte[SecureLaunch.JoinRequestedBytes];
        BitConverter.TryWriteBytes(payload.AsSpan(0, 8), friend);
        Encoding.UTF8.GetBytes(connect).CopyTo(payload, 8);
        return payload;
    }

    [Fact]
    public void ARichPresenceJoinRunsItsConnect()
    {
        var (launch, commands, _, _) = Make();
        launch.OnGameRichPresenceJoinRequested(JoinPayload("+connect 10.0.0.2:27015"));
        Assert.Equal(new[] { "connect 10.0.0.2:27015" }, commands.Ran);
    }

    [Fact]
    public void EveryPercentTwentyIsASpace()
    {
        var (launch, commands, _, _) = Make();
        launch.OnGameRichPresenceJoinRequested(JoinPayload("+connect%2010.0.0.2:27015%20"));
        Assert.Equal(new[] { "connect 10.0.0.2:27015 " }, commands.Ran);
    }

    [Fact]
    public void AnEmptyConnectIsIgnoredSilently()
    {
        var (launch, commands, log, _) = Make();
        launch.OnGameRichPresenceJoinRequested(JoinPayload(""));
        Assert.Empty(commands.Ran);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void TheRequestIsReportedWithItsSizeAtDeveloperOne()
    {
        var (launch, _, log, _) = Make(developer: 1);
        launch.OnGameRichPresenceJoinRequested(JoinPayload("+connect 1.2.3.4"));
        // nBufferSize = V_strlen + 1
        Assert.Equal(new[] { "Received command line request[17]: '+connect 1.2.3.4'\n" }, log.Lines);
    }

    [Fact]
    public void DeveloperZeroSaysNothing()
    {
        var (launch, commands, log, _) = Make(developer: 0);
        launch.OnGameRichPresenceJoinRequested(JoinPayload("+connect 1.2.3.4"));
        Assert.Empty(log.Lines);
        Assert.Single(commands.Ran);   // the control: it still ran
    }

    [Fact]
    public void ALineWithoutAPlusRunsNothing()
    {
        var (launch, commands, _, _) = Make();
        launch.OnGameRichPresenceJoinRequested(JoinPayload("connect 1.2.3.4"));
        Assert.Empty(commands.Ran);
    }

    [Fact]
    public void TheConnectPrefixIsCaseInsensitiveAsStringAfterPrefixIs()
    {
        var (launch, commands, _, _) = Make();
        launch.OnGameRichPresenceJoinRequested(JoinPayload("+CONNECT 1.2.3.4"));
        Assert.Equal(new[] { "CONNECT 1.2.3.4" }, commands.Ran);
    }

    [Theory]
    [InlineData("+connect 1.2.3.4;quit")]
    [InlineData("+connect 1.2.3.4\nquit")]
    [InlineData("+connect 1.2.3.4&")]
    [InlineData("+connect 1.2.3.4`")]
    public void AnUnsafeCharacterRunsNothing(string line)
    {
        var (launch, commands, _, _) = Make();
        launch.OnGameRichPresenceJoinRequested(JoinPayload(line));
        Assert.Empty(commands.Ran);
    }

    [Fact]
    public void EverySafeCharacterPasses()
    {
        var (launch, commands, _, _) = Make();
        const string safe = "09azAZ_-.:?%/= []@\"'#()!\\$";
        launch.OnGameRichPresenceJoinRequested(JoinPayload("+connect " + safe));
        Assert.Equal(new[] { "connect " + safe }, commands.Ran);
    }

    [Fact]
    public void ACommandOtherThanConnectRunsNothing()
    {
        var (launch, commands, _, _) = Make();
        launch.OnGameRichPresenceJoinRequested(JoinPayload("+quit"));
        Assert.Empty(commands.Ran);
    }

    [Fact]
    public void AUrlLaunchReadsSteamsLaunchLineWithItsByteCount()
    {
        var (launch, commands, log, steam) = Make();
        steam.LaunchLine = "+connect 10.0.0.9";
        steam.LaunchBytes = 18;

        launch.OnNewUrlLaunchParameters();

        Assert.Equal(new[] { "connect 10.0.0.9" }, commands.Ran);
        Assert.Equal(new[] { "Received command line request[18]: '+connect 10.0.0.9'\n" }, log.Lines);
    }

    [Fact]
    public void AnEmptyUrlLaunchSaysSo()
    {
        var (launch, commands, log, steam) = Make();
        steam.LaunchLine = "";
        steam.LaunchBytes = 1;

        launch.OnNewUrlLaunchParameters();

        Assert.Empty(commands.Ran);
        Assert.Equal(new[] { "Received empty command line from steam\n" }, log.Lines);
    }

    [Fact]
    public void AUrlLaunchWithNoSteamAppsDoesNothing()
    {
        var (launch, commands, log, steam) = Make();
        steam.LaunchLine = null;

        launch.OnNewUrlLaunchParameters();

        Assert.Empty(commands.Ran);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void TheJoinPayloadReadsUpToItsTerminator()
    {
        byte[] payload = JoinPayload("+connect 1.2.3.4");
        payload[8 + 16 + 1] = (byte)'x';   // bytes after the terminator are not the string

        Assert.Equal("+connect 1.2.3.4", SecureLaunch.ConnectOf(payload));
    }

    [Fact]
    public void AJoinPayloadWithNoTerminatorStopsAtItsEnd()
    {
        byte[] payload = JoinPayload(new string('1', 256));

        Assert.Equal(256, SecureLaunch.ConnectOf(payload).Length);
    }
}
