using Debug.Api;
using Debug.Api.Server.Interfaces;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Routes a mod adds to the dev API (plan_hl2mp_ai §7.3): a registered
/// <see cref="IDebugRouteProvider"/>'s path is answered by it, on the game
/// thread, with its status; <c>/routes</c> lists it; a path nobody serves is
/// still a 404.
/// </summary>
public class DebugApiRouteProviderTests
{
    private sealed class Provider : IDebugRouteProvider
    {
        public int? Thread;

        public IReadOnlyList<DebugRoute> Routes => [new("GET", "/ai/test", "a test route")];

        public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
        {
            Thread = Environment.CurrentManagedThreadId;
            return new DebugRouteReply(query.TryGetValue("missing", out _) ? 404 : 200, $"{{\"path\":\"{path}\",\"x\":\"{query.GetValueOrDefault("x")}\"}}");
        }
    }

    private static (string Body, int Status) Pumped(DebugApiServer server, HttpRequest request, out int pumpThread)
    {
        using var stop = new CancellationTokenSource();
        int pumped = 0;
        var pump = new Thread(() =>
        {
            pumped = Environment.CurrentManagedThreadId;
            while (!stop.IsCancellationRequested)
            {
                server.Pump();
                Thread.Sleep(1);
            }
        }) { IsBackground = true };
        pump.Start();
        try
        {
            HttpReply reply = server.Route(request);
            return (reply.Body, reply.Status);
        }
        finally
        {
            stop.Cancel();
            pump.Join();
            pumpThread = pumped;
        }
    }

    private static HttpRequest Get(string path, Dictionary<string, string>? query = null) => new("GET", path, query ?? new(), "");

    [Fact]
    public void AProvidersPathIsAnsweredByItOnTheGameThread()
    {
        var provider = new Provider();
        using var server = new DebugApiServer([], TestDebugHooks.Server(routeProviders: () => [provider]));

        (string body, int status) = Pumped(server, Get("/ai/test", new() { ["x"] = "7" }), out int pump);

        Assert.Equal((200, "{\"path\":\"/ai/test\",\"x\":\"7\"}"), (status, body));
        Assert.Equal(pump, provider.Thread);
    }

    [Fact]
    public void TheProvidersStatusIsTheReplys()
    {
        using var server = new DebugApiServer([], TestDebugHooks.Server(routeProviders: () => [new Provider()]));
        Assert.Equal(404, Pumped(server, Get("/ai/test", new() { ["missing"] = "1" }), out _).Status);
    }

    [Fact]
    public void RoutesListsTheProvidersRoutes()
    {
        using var server = new DebugApiServer([], TestDebugHooks.Server(routeProviders: () => [new Provider()]));
        Assert.Equal("{\"routes\":[{\"method\":\"GET\",\"path\":\"/ai/test\",\"description\":\"a test route\"}]}",
                     server.Route(Get("/routes")).Body);
    }

    [Fact]
    public void APathNobodyServesIsStillA404()
    {
        using var server = new DebugApiServer([], TestDebugHooks.Server(routeProviders: () => [new Provider()]));
        Assert.Equal(404, server.Route(Get("/ai/other")).Status);
    }

    private sealed class PrefixProvider : IDebugRouteProvider
    {
        public IReadOnlyList<DebugRoute> Routes => [new("GET", "/ai/bot/", "a bot's parts: /ai/bot/<id>/<part>")];

        public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query) => new(200, $"{{\"path\":\"{path}\"}}");
    }

    [Fact]
    public void ARouteEndingInASlashServesThePathsUnderIt()
    {
        using var server = new DebugApiServer([], TestDebugHooks.Server(routeProviders: () => [new PrefixProvider()]));
        (string body, int status) = Pumped(server, Get("/ai/bot/3/memory"), out _);
        Assert.Equal((200, "{\"path\":\"/ai/bot/3/memory\"}"), (status, body));
        Assert.Equal(404, Pumped(server, Get("/ai/bots"), out _).Status);
    }

    [Fact]
    public void AnExactRouteDoesNotServeThePathsUnderIt()
    {
        using var server = new DebugApiServer([], TestDebugHooks.Server(routeProviders: () => [new Provider()]));
        Assert.Equal(404, Pumped(server, Get("/ai/test/more"), out _).Status);
    }
}
