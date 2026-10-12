using System.Text.RegularExpressions;

using SourceSharp.Abi;
using Debug.Api;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Golden JSON for /events (plan_devapi_interfaces P2, group G4), written
/// before outputs reached the dev API through IDebugEntityEvents.
/// </summary>
public class DebugApiGoldenEventsTests
{
    private static readonly EntityHandle Source = new(0x0004_0021u);

    private static HttpReply Get(DebugApiServer server, string path)
    {
        Assert.True(HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request));
        return server.Route(request);
    }

    private static ReloadFixtureEntity Entity()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(DebugApiGoldenEventsTests).Assembly);
        registry.Services = Services();
        registry.KeyValue(Source, "reload_fixture", "speed", "1");
        return (ReloadFixtureEntity)registry.Get(Source)!;
    }

    [Fact]
    public void An_unstarted_server_serves_no_events()
    {
        using var server = NeverPumped.Server();
        Assert.Equal("{\"next\":0,\"head\":0,\"sampling\":true,\"samplePeriod\":0.5,\"events\":[],\"count\":0}",
                     Get(server, "/events").Body);
    }

    [Fact]
    public void A_fired_output_is_served_as_an_output_event()
    {
        var entity = Entity();
        using var server = new DebugApiServer(["-debugapisocket"], TestDebugHooks.Server(registry: entity.Owner), TestDebugApiServers.OwnSocket());
        Assert.True(server.Start(), "the test server could not listen");

        entity.OnFirst.Fire("7");
        entity.OnSecond.Fire();

        string body = Regex.Replace(Get(server, "/events").Body, "\"(seq|next)\":\\d+", "\"$1\":0");

        Assert.Contains(
            "{\"seq\":0,\"time\":0,\"kind\":\"output\",\"classname\":\"reload_fixture\",\"name\":\"OnFirst\"," +
            "\"detail\":\"fired with '7'\",\"index\":-1,\"handle\":262177}," +
            "{\"seq\":0,\"time\":0,\"kind\":\"output\",\"classname\":\"reload_fixture\",\"name\":\"OnSecond\"," +
            "\"detail\":\"fired\",\"index\":-1,\"handle\":262177}",
            body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disposed_server_hears_no_more_outputs()
    {
        var entity = Entity();
        var server = new DebugApiServer(["-debugapisocket"], TestDebugHooks.Server(registry: entity.Owner), TestDebugApiServers.OwnSocket());
        Assert.True(server.Start(), "the test server could not listen");
        entity.OnFirst.Fire("before");
        server.Dispose();
        entity.OnFirst.Fire("after");

        string body = Get(server, "/events").Body;
        Assert.Contains("fired with 'before'", body, StringComparison.Ordinal);
        Assert.DoesNotContain("fired with 'after'", body, StringComparison.Ordinal);
    }
}
