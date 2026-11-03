using System.Text.Json;
using System.Threading;

using Debug.Api;
using Debug.Api.Server.Interfaces;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>/player/teleport?player=N&amp;settle=1</c> for the JOINER: the tf2 gate's
/// second player is client-predicted, and the server never runs his movement
/// path while he idles (no usercmd arrives, so <c>PlayerMovePublisher</c>
/// never writes his ground word). The settle's ground read therefore answers
/// -1 for a joiner standing anywhere, and the wait reported the correctly
/// placed player as stuck. With <c>simulated=0</c> -- the caller declaring the
/// player one the server does not move -- the placement itself, stable at the
/// target for the settle window, is the evidence the server can actually see;
/// the stuck arm keeps its full meaning for every other player, and for that
/// one too when the placement does not hold.
/// </summary>
public class DebugApiTeleportSettleTests
{
    private static readonly DebugVec3 Target = new(-1200f, 600f, 40f);

    /// <summary>
    /// The request runs on its own thread (as a long-poll session does) and
    /// the test thread is the game thread: it moves the tick and pumps, so
    /// the route's OnGameThread work and its per-tick settle check both run
    /// here (the same shape as DebugApiWaitTests' rig).
    /// </summary>
    private sealed class Rig : IDisposable
    {
        public FakeDebugWorld World { get; } = new() { Tick = 100 };
        public DebugApiServer Server { get; }

        public Rig()
        {
            Server = DebugApiServer.ForTest(TestDebugHooks.Server(world: World));
        }

        /// <summary>The joiner: a predicted player the server does not move, so his ground word reads -1.</summary>
        public FakeDebugWorld.Entity AddJoiner(uint handle, int slot)
        {
            var player = World.Add(handle, slot, "player");
            player.Origin = new DebugVec3(1888f, 1448f, 256f);
            player.Velocity = default;
            player.Ground = -1;
            return player;
        }

        public sealed class Pending : IDisposable
        {
            private readonly ManualResetEventSlim _done = new(false);
            private HttpReply _reply;

            public Pending(DebugApiServer server, string path)
            {
                new Thread(() =>
                {
                    _reply = DebugApiGolden.Get(server, path);
                    _done.Set();
                })
                { IsBackground = true }.Start();
            }

            public bool Wait(int milliseconds) => _done.Wait(milliseconds);
            public bool Wait(TimeSpan timeout) => _done.Wait(timeout);

            public HttpReply Reply => _done.Wait(TimeSpan.FromSeconds(10))
                ? _reply
                : throw new TimeoutException("the teleport request never answered");

            public void Dispose() => _done.Dispose();
        }

        public Pending Start(string path) => new(Server, path);

        /// <summary>One game frame: a new tick, then both pumps, as the managed frame and the engine think do.</summary>
        public void Frame(Action<FakeDebugWorld>? change = null)
        {
            World.Tick++;
            change?.Invoke(World);
            Server.Pump();
            Server.Pump();
            World.EndFrame();
        }

        /// <summary>Pumps frames until the reply answers (or the frame budget runs out); the parsed body.</summary>
        public JsonElement Drive(Pending reply, int frames = 120, Action<int, FakeDebugWorld>? change = null)
        {
            Server.Pump();   // runs the route's OnGameThread work: the placement
            for (int i = 1; i <= frames && !reply.Wait(5); i++)
                Frame(w => change?.Invoke(i, w));
            Assert.True(reply.Wait(TimeSpan.FromSeconds(5)), "the settle wait never answered");
            using var doc = JsonDocument.Parse(reply.Reply.Body);
            return doc.RootElement.Clone();
        }

        public void Dispose() => Server.Dispose();
    }

    [Fact]
    public void A_joiner_stable_at_the_target_without_a_server_read_ground_settles()
    {
        // The joiner's reality: the server never runs his move while he idles,
        // so his ground word stays -1 wherever he stands. Teleporting him to
        // the target and finding him still there for the settle window is a
        // landing, not a hang: this is the case the gate's second player is
        // in, and until the stable arm existed it answered ok:false status
        // 'stuck' while he sat exactly where he was asked. simulated=0 is the
        // caller declaring the player one the server does not move; without
        // that declaration the physics reads alone stay in charge (the two
        // facts below).
        using var rig = new Rig();
        var joiner = rig.AddJoiner(0x20002u, 2);
        var e = rig.Drive(rig.Start(
            "/player/teleport?player=2&pos=-1200,600,40&ang=0,0,0&settle=1&simulated=0"));

        Assert.True(e.GetProperty("ok").GetBoolean(), $"the settle refused the joiner: {e}");
        Assert.Equal("met", e.GetProperty("status").GetString());
        Assert.Equal(2, e.GetProperty("player").GetInt32());
        // Placed, not just answered: the fake world is the engine's side.
        Assert.Equal(Target.X, joiner.Origin.X, 3);
        Assert.Equal(Target.Y, joiner.Origin.Y, 3);
        Assert.Equal(Target.Z, joiner.Origin.Z, 3);
        Assert.Equal(-1, e.GetProperty("value").GetProperty("ground").GetInt32());
    }

    [Fact]
    public void A_player_that_comes_to_rest_somewhere_else_is_still_stuck()
    {
        // The stuck arm keeps its meaning: rest at the WRONG place with no
        // ground is the held-by-solid case, not a landing. Every frame moves
        // him off the target, so the stable-at-target arm cannot fire.
        using var rig = new Rig();
        var player = rig.AddJoiner(0x20002u, 2);
        var away = new DebugVec3(-1200f, 600f, 300f);
        var e = rig.Drive(
            rig.Start("/player/teleport?player=2&pos=-1200,600,40&settle=1"),
            frames: 60,
            change: (_, w) => w[0x20002u].Origin = away);

        Assert.False(e.GetProperty("ok").GetBoolean());
        Assert.Equal("stuck", e.GetProperty("status").GetString());
    }

    [Fact]
    public void Declaring_a_player_unsimulated_still_stucks_him_off_the_target()
    {
        // The declaration loosens what counts as a landing, never what counts
        // as stuck: an unsimulated player the checks find off the target, at
        // rest and off the ground, is the placement NOT holding, and stays
        // stuck. (The predicted joiner who walks away inside the window is
        // this case: his server origin leaves the target with his reports.)
        using var rig = new Rig();
        rig.AddJoiner(0x20002u, 2);
        var away = new DebugVec3(-1200f, 600f, 300f);
        var e = rig.Drive(
            rig.Start("/player/teleport?player=2&pos=-1200,600,40&settle=1&simulated=0"),
            frames: 60,
            change: (_, w) => w[0x20002u].Origin = away);

        Assert.False(e.GetProperty("ok").GetBoolean());
        Assert.Equal("stuck", e.GetProperty("status").GetString());
    }

    [Fact]
    public void A_grounded_server_simulated_player_settles_as_before()
    {
        // The listen host's player keeps the old arm: the server runs his
        // move, reports a ground, and the settle passes on ground + rest.
        using var rig = new Rig();
        var player = rig.AddJoiner(0x20001u, 1);
        player.Ground = 7;
        var e = rig.Drive(rig.Start(
            "/player/teleport?player=1&pos=-1200,600,40&settle=1"));

        Assert.True(e.GetProperty("ok").GetBoolean());
        Assert.Equal("met", e.GetProperty("status").GetString());
        Assert.Equal(7, e.GetProperty("value").GetProperty("ground").GetInt32());
    }
}
