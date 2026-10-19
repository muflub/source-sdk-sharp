using System.Text.Json;

using Debug.Api;
using Debug.Api.Server.Interfaces;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>/entity: one entity by index, handle or name (debug-api lane, item 4).</summary>
public class DebugApiEntityTests
{
    private static (DebugApiServer Server, FakeDebugWorld World) Serve()
    {
        var world = new FakeDebugWorld { Tick = 7 };
        var crate = world.Add(0x10028u, 40, "prop_physics", "crate_a");
        crate.Origin = new DebugVec3(1, 2, 3);
        crate.Velocity = new DebugVec3(3, 4, 0);
        crate.Asleep = true;
        var player = world.Add(0x20001u, 1, "player");
        player.Ground = -1;
        return (DebugApiServer.ForTest(TestDebugHooks.Server(world: world)), world);
    }

    private static JsonElement Read(DebugApiServer server, string path)
    {
        using var doc = JsonDocument.Parse(DebugApiGolden.Pumped(server, path).Body);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void An_index_finds_the_entity_with_its_velocity_and_speed()
    {
        var (server, _) = Serve();
        using var _s = server;
        var e = Read(server, "/entity?index=40");
        Assert.Equal(0x10028u, e.GetProperty("handle").GetUInt32());
        Assert.Equal(5.0, e.GetProperty("speed").GetDouble(), 3);
        Assert.Equal(3.0, e.GetProperty("velocity")[0].GetDouble(), 3);
    }

    [Fact]
    public void A_name_finds_the_entity()
    {
        var (server, _) = Serve();
        using var _s = server;
        Assert.Equal(40, Read(server, "/entity?name=crate_a").GetProperty("index").GetInt32());
    }

    [Fact]
    public void A_recorded_sleep_is_reported()
    {
        var (server, _) = Serve();
        using var _s = server;
        Assert.True(Read(server, "/entity?index=40").GetProperty("asleep").GetBoolean());
    }

    [Fact]
    public void An_unknown_sleep_state_is_null_not_false()
    {
        var (server, _) = Serve();
        using var _s = server;
        Assert.Equal(JsonValueKind.Null, Read(server, "/entity?index=1").GetProperty("asleep").ValueKind);
    }

    [Fact]
    public void A_player_standing_on_nothing_reports_ground_minus_one()
    {
        var (server, _) = Serve();
        using var _s = server;
        Assert.Equal(-1, Read(server, "/entity?index=1").GetProperty("ground").GetInt32());
    }

    [Fact]
    public void A_non_player_reports_no_ground()
    {
        var (server, _) = Serve();
        using var _s = server;
        Assert.Equal(JsonValueKind.Null, Read(server, "/entity?index=40").GetProperty("ground").ValueKind);
    }

    [Fact]
    public void A_handle_that_is_no_longer_live_is_not_found_and_says_when()
    {
        var (server, world) = Serve();
        using var _s = server;
        world.Remove(0x10028u);
        world.EndFrame();
        var e = Read(server, $"/entity?handle={0x10028u}");
        Assert.False(e.GetProperty("found").GetBoolean());
        Assert.Equal(7, e.GetProperty("tick").GetInt32());
    }

    [Fact]
    public void Two_selectors_are_an_error()
    {
        var (server, _) = Serve();
        using var _s = server;
        Assert.Contains("not more than one", Read(server, "/entity?index=40&name=crate_a").GetProperty("error").GetString());
    }

    [Fact]
    public void No_selector_is_an_error()
    {
        var (server, _) = Serve();
        using var _s = server;
        Assert.Contains("name an entity", Read(server, "/entity").GetProperty("error").GetString());
    }
}
