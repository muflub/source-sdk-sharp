using SourceSharp.Abi;
using SourceSharp.Client.Steam;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's Steam context against stock's CClientSteamContext
/// (game/client/clientsteamcontext.cpp), lane c6-input.
/// </summary>
public class ClientSteamContextTests
{
    private sealed class Steam : ISteamClientApi
    {
        public readonly List<string> Calls = new();
        public bool Available => true;
        public bool HasUserAndUtils { get; set; } = true;
        public bool LoggedOnNow { get; set; } = true;
        public bool LoggedOn { get { Calls.Add("BLoggedOn"); return LoggedOnNow; } }
        public ulong SteamId { get { Calls.Add("GetSteamID"); return 76561197960287930UL; } }
        public int Universe { get { Calls.Add("GetConnectedUniverse"); return 1; } }
        public uint AppId { get { Calls.Add("GetAppID"); return 243750; } }
        public bool InitSafe() { Calls.Add("InitSafe"); return true; }
        public void SetTryCatchCallbacks(bool b) => Calls.Add($"SetTryCatchCallbacks {b}");
        public bool InitFilterText() { Calls.Add("InitFilterText"); return HasUserAndUtils; }
        public string? FilterText(int context, ulong id, string text, int max) => text;
        public void RegisterCallback(nint callback, int callbackId) { }
        public void UnregisterCallback(nint callback) { }
        public string? LaunchCommandLine(out int bytes) { bytes = 0; return null; }
    }

    private sealed class Log : ILog
    {
        public readonly List<string> Lines = new();
        public void Msg(string m) => Lines.Add(m);
        public void Warning(string m) => Lines.Add(m);
    }

    [Fact]
    public void ActivationIsStocksOrderAndPrintsTheLoggedOnState()
    {
        var steam = new Steam();
        var log = new Log();
        var ctx = new ClientSteamContext(steam, log);
        ctx.Activate();
        Assert.Equal(new[] { "InitSafe", "SetTryCatchCallbacks False", "InitFilterText", "BLoggedOn",
                             "GetSteamID", "GetConnectedUniverse", "GetAppID" }, steam.Calls);
        Assert.True(ctx.Active);
        Assert.True(ctx.LoggedOn);
        Assert.Equal(76561197960287930UL, ctx.SteamId);
        Assert.Equal(1, ctx.Universe);
        Assert.Equal(243750u, ctx.AppId);
        Assert.Equal(new[] { "CClientSteamContext logged on = 1\n" }, log.Lines);
    }

    [Fact]
    public void ASecondActivationDoesNothing()
    {
        var steam = new Steam();
        var ctx = new ClientSteamContext(steam, new Log());
        ctx.Activate();
        steam.Calls.Clear();
        ctx.Activate();
        Assert.Empty(steam.Calls);
    }

    [Fact]
    public void WithNoUserOrUtilsItIsNotLoggedOnAndCachesNothing()
    {
        var steam = new Steam { HasUserAndUtils = false };
        var log = new Log();
        var ctx = new ClientSteamContext(steam, log);
        ctx.Activate();
        Assert.False(ctx.LoggedOn);
        Assert.DoesNotContain("GetSteamID", steam.Calls);
        Assert.DoesNotContain("BLoggedOn", steam.Calls);
        Assert.Equal(new[] { "CClientSteamContext logged on = 0\n" }, log.Lines);
    }

    [Fact]
    public void TheIdentityIsCachedOnlyWhenItFirstLogsOn()
    {
        var steam = new Steam();
        var ctx = new ClientSteamContext(steam, new Log());
        ctx.Activate();
        steam.Calls.Clear();
        ctx.OnSteamServersConnected();
        Assert.Equal(new[] { "BLoggedOn" }, steam.Calls);
    }

    [Fact]
    public void ADisconnectRefreshesTheStateAndSaysSo()
    {
        var steam = new Steam();
        var log = new Log();
        var ctx = new ClientSteamContext(steam, log);
        ctx.Activate();
        steam.LoggedOnNow = false;
        ctx.OnSteamServersDisconnected();
        Assert.False(ctx.LoggedOn);
        Assert.Equal("CClientSteamContext OnSteamServersDisconnected logged on = 0\n", log.Lines[^1]);
        ctx.OnSteamServerConnectFailure();
        Assert.Equal("CClientSteamContext OnSteamServerConnectFailure logged on = 0\n", log.Lines[^1]);
        steam.LoggedOnNow = true;
        ctx.OnSteamServersConnected();
        Assert.True(ctx.LoggedOn);
        Assert.Equal("CClientSteamContext OnSteamServersConnected logged on = 1\n", log.Lines[^1]);
    }

    [Fact]
    public void ShutdownClearsTheStateOnceAndOnlyWhenActive()
    {
        var steam = new Steam();
        var ctx = new ClientSteamContext(steam, new Log());
        ctx.Shutdown();
        Assert.False(ctx.Active);
        ctx.Activate();
        ctx.Shutdown();
        Assert.False(ctx.Active);
        Assert.False(ctx.LoggedOn);
        steam.Calls.Clear();
        ctx.Activate();
        Assert.Contains("InitSafe", steam.Calls);
    }
}
