using System.Text.Json;

using Debug.Api;
using Debug.Api.Server.Interfaces;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// /player/teleport with settle, and /entities/remove: placement and cleanup
/// that answer when the world shows it, counted in ticks (debug-api lane,
/// item 4).
/// </summary>
public sealed class DebugApiPlacementTests : IDisposable
{
    private readonly FakeDebugWorld _world = new() { Tick = 200 };
    private readonly DebugApiServer _server;
    private const uint PlayerHandle = 0x20001u;

    public DebugApiPlacementTests()
    {
        var player = _world.Add(PlayerHandle, 1, "player");
        player.Ground = 0;
        _world.Add(0x00000u, 0, "worldspawn");
        _server = DebugApiServer.ForTest(TestDebugHooks.Server(world: _world));
    }

    public void Dispose() => _server.Dispose();

    private sealed class Pending
    {
        private readonly ManualResetEventSlim _done = new(false);
        private HttpReply _reply;
        public Pending(Func<HttpReply> work) => new Thread(() => { _reply = work(); _done.Set(); }) { IsBackground = true }.Start();
        public bool Done => _done.IsSet;
        public HttpReply Reply => _done.Wait(TimeSpan.FromSeconds(10)) ? _reply : throw new TimeoutException("no reply");
    }

    /// <summary>Runs frames (the test is the game thread) until the reply comes; the reply and the last tick run.</summary>
    private (HttpReply Reply, JsonElement Body) Drive(string path, Action<int, FakeDebugWorld>? change = null, int frames = 200)
    {
        var pending = new Pending(() => DebugApiGolden.Get(_server, path));
        for (int i = 1; i <= frames && !pending.Done; i++)
        {
            _world.Tick++;
            change?.Invoke(i, _world);
            _server.Pump();
            Thread.Sleep(2);
            _server.Pump();
            _world.EndFrame();
        }

        var reply = pending.Reply;
        using var doc = JsonDocument.Parse(reply.Body);
        return (reply, doc.RootElement.Clone());
    }

    [Fact]
    public void A_teleport_without_settle_moves_and_points_the_player_at_once()
    {
        var (reply, body) = Drive("/player/teleport?pos=10,20,30&ang=5,90,0");
        Assert.Equal(200, reply.Status);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Equal(new DebugVec3(10, 20, 30), _world[PlayerHandle].Origin);
        Assert.Equal((PlayerHandle, new DebugVec3(5, 90, 0)), _world.EyeSnaps.Single());
    }

    [Fact]
    public void A_settled_teleport_answers_only_after_the_player_has_landed_and_stopped()
    {
        int landedAt = -1;
        var (reply, body) = Drive("/player/teleport?pos=0,0,100&settle=1&for=3", (i, w) =>
        {
            var p = w[PlayerHandle];
            if (i <= 4) { p.Ground = -1; p.Velocity = new DebugVec3(0, 0, -200); }
            else if (i == 5) { p.Ground = 0; p.Velocity = default; landedAt = w.Tick; }
        });

        Assert.Equal(200, reply.Status);
        Assert.Equal("met", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("tick").GetInt32() >= landedAt + 2,
                    $"answered at tick {body.GetProperty("tick").GetInt32()}, landed at {landedAt}");
        Assert.Equal(0, body.GetProperty("value").GetProperty("ground").GetInt32());
    }

    [Fact]
    public void A_settled_teleport_that_never_lands_times_out()
    {
        var (reply, body) = Drive("/player/teleport?pos=0,0,100&settle=1&timeout=10", (_, w) =>
        {
            w[PlayerHandle].Ground = -1;
            w[PlayerHandle].Velocity = new DebugVec3(0, 0, -200);
        });
        Assert.Equal(408, reply.Status);
        Assert.Equal("timeout", body.GetProperty("status").GetString());
    }

    [Fact]
    public void A_settled_teleport_into_solid_reports_stuck_not_a_timeout()
    {
        // Held where it was put: off the ground and not falling.
        var (reply, body) = Drive("/player/teleport?pos=0,0,200&settle=1&timeout=300", (_, w) =>
        {
            w[PlayerHandle].Ground = -1;
            w[PlayerHandle].Velocity = default;
        });
        Assert.Equal(409, reply.Status);
        Assert.Equal("stuck", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("waitedTicks").GetInt32() < 300, "it waited out the whole budget");
    }

    [Fact]
    public void A_teleport_with_no_player_in_the_slot_is_refused()
        => Assert.Equal(404, Drive("/player/teleport?player=5&pos=0,0,0").Reply.Status);

    [Fact]
    public void A_teleport_with_no_position_is_refused()
        => Assert.Equal(400, DebugApiGolden.Get(_server, "/player/teleport").Status);

    [Fact]
    public void Remove_by_name_glob_removes_what_matches_and_answers_once_it_is_gone()
    {
        _world.Add(0x10040u, 64, name: "ss_livegate_crate_1");
        _world.Add(0x10041u, 65, name: "ss_livegate_crate_2");
        _world.Add(0x10042u, 66, name: "keep_me");

        var (reply, body) = Drive("/entities/remove?name=ss_livegate_*");
        Assert.Equal(200, reply.Status);
        Assert.Equal(2, body.GetProperty("removed").GetInt32());
        Assert.False(_world.IsValid(0x10040u));
        Assert.False(_world.IsValid(0x10041u));
        Assert.True(_world.IsValid(0x10042u));
        // Removal is freed at the end of the frame that asked for it, so the
        // tick that saw it gone is a later one.
        Assert.True(body.GetProperty("tick").GetInt32() > body.GetProperty("removeTick").GetInt32(),
                    "answered on the tick the removal was asked for, before the frame freed it");
    }

    [Fact]
    public void Remove_never_takes_the_world_or_a_player()
    {
        var (_, body) = Drive("/entities/remove?class=*");
        Assert.Equal(0, body.GetProperty("removed").GetInt32());
        Assert.True(_world.IsValid(PlayerHandle));
    }

    [Fact]
    public void A_removal_the_engine_never_carries_out_times_out_rather_than_passing()
    {
        _world.IgnoreRemoves = true;
        _world.Add(0x10040u, 64, name: "stuck");
        var (reply, body) = Drive("/entities/remove?name=stuck&timeout=5");
        Assert.Equal(408, reply.Status);
        Assert.Equal("timeout", body.GetProperty("status").GetString());
    }

    [Fact]
    public void Remove_with_nothing_named_is_refused()
        => Assert.Equal(400, DebugApiGolden.Get(_server, "/entities/remove").Status);
}
