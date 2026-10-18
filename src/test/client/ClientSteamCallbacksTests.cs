using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Steam;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's five Steam callbacks, as stock's STEAM_CALLBACK members
/// declared them: CClientSteamContext's three server-connection callbacks and
/// CSecureLaunchSystem's URL launch and rich-presence join. The ids and
/// payload sizes are the SDK headers'; vtable_index_agreement_test's
/// SteamCallbackLayout.CallbackIdsAndSizes holds the same numbers to them.
/// </summary>
public class ClientSteamCallbacksTests
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

    private sealed class Rig : IDisposable
    {
        private readonly SteamCallbacksTests.Fixture _fixture;

        public readonly Log Log = new();
        public readonly Commands Commands = new();
        public readonly ClientSteamContext Context;

        public Rig()
        {
            _fixture = new SteamCallbacksTests.Fixture(log: Log);
            Context = new ClientSteamContext(Steam, Log);
            new ClientSteamCallbacks(Callbacks, Context, new SecureLaunch(Steam, Commands, Log, () => 1)).RegisterAll();
        }

        public SteamCallbacksTests.SteamDouble Steam => _fixture.Steam;
        public SteamCallbacks Callbacks => _fixture.Callbacks;

        public void Run(string name, byte[] payload) => SteamCallbacksTests.SteamDouble.Run(Callbacks.CallbackOf(name), payload);

        public void Dispose() => _fixture.Dispose();
    }

    [Fact]
    public void TheFiveAreRegisteredUnderTheHeadersIds()
    {
        using var rig = new Rig();

        Assert.Equal(new[] { 101, 102, 103, 1014, 337 }, rig.Steam.Registered.Select(r => r.Id));
    }

    [Theory]
    [InlineData(ClientSteamCallbacks.ServersConnected, 1)]
    [InlineData(ClientSteamCallbacks.ServerConnectFailure, 8)]
    [InlineData(ClientSteamCallbacks.ServersDisconnected, 4)]
    [InlineData(ClientSteamCallbacks.NewUrlLaunchParameters, 1)]
    [InlineData(ClientSteamCallbacks.RichPresenceJoinRequested, 264)]
    public void EachAnswersItsPayloadSize(string name, int bytes)
    {
        using var rig = new Rig();

        // The size the native callback answers Steam with (GetCallbackSizeBytes).
        Assert.Equal(bytes, SteamCallbacksTests.FakeSteamLibrary.PayloadBytesOf(rig.Callbacks.CallbackOf(name)));
    }

    [Theory]
    [InlineData(ClientSteamCallbacks.ServersConnected, "CClientSteamContext OnSteamServersConnected logged on = 1\n")]
    [InlineData(ClientSteamCallbacks.ServerConnectFailure, "CClientSteamContext OnSteamServerConnectFailure logged on = 1\n")]
    [InlineData(ClientSteamCallbacks.ServersDisconnected, "CClientSteamContext OnSteamServersDisconnected logged on = 1\n")]
    public void TheConnectionCallbacksReachTheContext(string name, string line)
    {
        using var rig = new Rig();

        rig.Run(name, new byte[8]);

        Assert.Equal(new[] { line }, rig.Log.Lines);
    }

    [Fact]
    public void ARichPresenceJoinReachesTheLaunch()
    {
        using var rig = new Rig();
        var payload = new byte[264];
        Encoding.UTF8.GetBytes("+connect 10.0.0.2").CopyTo(payload, 8);

        rig.Run(ClientSteamCallbacks.RichPresenceJoinRequested, payload);

        Assert.Equal(new[] { "connect 10.0.0.2" }, rig.Commands.Ran);
    }

    [Fact]
    public void AUrlLaunchReachesTheLaunch()
    {
        using var rig = new Rig();
        rig.Steam.LaunchLine = "+connect 10.0.0.3";
        rig.Steam.LaunchBytes = 18;

        rig.Run(ClientSteamCallbacks.NewUrlLaunchParameters, new byte[1]);

        Assert.Equal(new[] { "connect 10.0.0.3" }, rig.Commands.Ran);
    }
}
