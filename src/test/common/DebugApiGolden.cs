using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>What the dev API's golden JSON facts share (plan_devapi_interfaces P2).</summary>
internal static class DebugApiGolden
{
    public static HttpReply Get(DebugApiServer server, string path)
    {
        Assert.True(HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request));
        return server.Route(request);
    }

    /// <summary>Answers <paramref name="path"/> while a thread pumps the server, as the game thread would.</summary>
    public static HttpReply Pumped(DebugApiServer server, string path)
    {
        using var stop = new CancellationTokenSource();
        var pump = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                server.Pump();
                Thread.Sleep(1);
            }
        }) { IsBackground = true };

        pump.Start();
        try { return Get(server, path); }
        finally { stop.Cancel(); pump.Join(); }
    }
}
