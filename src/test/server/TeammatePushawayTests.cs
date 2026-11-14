using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Plan A.7's push-apart: <see cref="TeammatePushaway"/> (stock's
/// <c>C_TFPlayer::AvoidPlayers</c>, the players half) and the bots' runner
/// that adds it to a bot's command (<see cref="BotTeammatePushaway"/>). One
/// behaviour per fact.
/// </summary>
public class TeammatePushawayTests
{
    private const float Limit = 450f;

    private static readonly Vector HullMin = new(-16f, -16f, 0f);
    private static readonly Vector HullMax = new(16f, 16f, 72f);

    private static PushawaySelf Self(Vector origin, Vector velocity = default, float yaw = 0f)
        => new(origin, HullMin, HullMax, velocity, yaw, 320f, false);

    private static PushawayBody Body(Vector origin) => new(origin, HullMin, HullMax);

    private static (bool Pushed, float Forward, float Side) Adjust(PushawaySelf self, params PushawayBody[] teammates)
    {
        float forward = 0f, side = 0f;
        bool pushed = TeammatePushaway.Adjust(self, teammates, TeammatePushaway.MaxSeparationForce, Limit, Limit, Limit,
                                              ref forward, ref side);
        return (pushed, forward, side);
    }

    /// <summary>
    /// A teammate overlapping on the right (yaw 0: right is -y) pushes a still
    /// player left: a positive side move is right, so the move is negative.
    /// </summary>
    [Fact]
    public void AnOverlappingTeammateOnTheRightPushesLeft()
    {
        (bool pushed, float forward, float side) = Adjust(Self(Vector.Zero), Body(new Vector(0f, -8f, 0f)));

        Assert.True(pushed);
        Assert.True(side < -1f, $"side {side}");
        Assert.Equal(0f, forward, 3);
    }

    [Fact]
    public void ATeammateClearOfTheBoxDoesNotPush()
    {
        (bool pushed, float forward, float side) = Adjust(Self(Vector.Zero), Body(new Vector(40f, 0f, 0f)));

        Assert.False(pushed);
        Assert.Equal((0f, 0f), (forward, side));
    }

    /// <summary>The deeper the overlap, the harder the push: stock remaps the centres' distance onto the force.</summary>
    [Fact]
    public void ADeeperOverlapPushesHarder()
    {
        float shallow = Adjust(Self(Vector.Zero), Body(new Vector(0f, -30f, 0f))).Side;
        float deep = Adjust(Self(Vector.Zero), Body(new Vector(0f, -4f, 0f))).Side;

        Assert.True(MathF.Abs(deep) > MathF.Abs(shallow), $"deep {deep}, shallow {shallow}");
    }

    /// <summary>A moving player is pushed across their motion, not along it.</summary>
    [Fact]
    public void AMovingPlayerIsPushedAcrossTheirMotion()
    {
        (_, float forward, float side) = Adjust(Self(Vector.Zero, new Vector(200f, 0f, 0f)), Body(new Vector(0f, -8f, 0f)));

        Assert.Equal(0f, forward, 3);
        Assert.NotEqual(0f, side);
    }

    /// <summary>The pushed move stays inside the input limits, keeping its direction.</summary>
    [Fact]
    public void ThePushedMoveIsClampedToTheInputLimits()
    {
        float forward = Limit, side = -Limit;
        Assert.True(TeammatePushaway.Adjust(Self(Vector.Zero), [Body(new Vector(0f, -1f, 0f))],
                                            TeammatePushaway.MaxSeparationForce, Limit, Limit, Limit, ref forward, ref side));

        Assert.True(forward <= Limit && MathF.Abs(side) <= Limit + 0.001f, $"forward {forward}, side {side}");
    }

    /// <summary>
    /// The push separates two stacked teammates over a few seconds of walking:
    /// each moves by its own push, stepped as a velocity, and the distance grows.
    /// </summary>
    [Fact]
    public void OverlappingTeammatesArePushedApart()
    {
        Vector a = Vector.Zero;
        Vector b = new(0f, -2f, 0f);
        float before = (a - b).Length;

        for (int i = 0; i < 60; i++)
        {
            float sa = Adjust(Self(a), Body(b)).Side;
            float sb = Adjust(Self(b), Body(a)).Side;
            // yaw 0: side move is along -y (right), at the push's speed for a 15 ms tick.
            a += new Vector(0f, -sa * 0.015f, 0f);
            b += new Vector(0f, -sb * 0.015f, 0f);
        }

        Assert.True((a - b).Length > before + 16f, $"apart {(a - b).Length}");
    }

    //---------------------------------------------------------------------
    // The bots' runner.
    //---------------------------------------------------------------------

    private sealed class Inner : IPlayerCommandRunner
    {
        public readonly List<UserCmd> Queued = [];
        public void Queue(EntityHandle player, in UserCmd cmd) => Queued.Add(cmd);
        public int RunQueued(EntityHandle player) => 0;
    }

    private sealed class Directory(params SsPlayer[] players) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => players.FirstOrDefault(p => p.Handle == handle);
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => className == EntityNames.Player ? players : [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Flow : IRoundFlow
    {
        public bool RoundOver => false;
        public void Think(float now) { }
        public void ResetRound() { }
        public void OnPlayerSpawned(EntityHandle player, float now) { }
        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }

    private sealed class Mode(bool pass) : IGameMode
    {
        public string Id => "pushtest";
        public IRoundFlow Rounds { get; } = new Flow();
        public bool TeammatesPass => pass;
    }

    private sealed class Selector : IGameModeSelector
    {
        public string? PickMode() => "pushtest";
    }

    private static readonly EntityHandle BotA = new(0x0001_0001u);
    private static readonly EntityHandle BotB = new(0x0002_0002u);

    private static (BotTeammatePushaway Runner, Inner Inner) Bots(bool pass, int teamB = 2)
    {
        RecordingEngine engine = TestServices.Fake();
        engine.Returns("Host.ActiveSide", Side.Server);
        engine.WhenCalled("Players.GetTeam", call => engine.Returns("Players.GetTeam", (EntityHandle)call.Args[0]! == BotA ? 2 : teamB));

        var convars = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>()));
        ServiceContainer services = TestServices.Services();
        var mode = new Mode(pass);
        services.AddInstance(mode.GetType(), mode);
        services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new Selector()));
        var modes = new GameModes(new RecordingLog(), engine, services, convars.Declare<SvModeSwitchWarmupConVar>(),
                                  convars.Declare<MpWaitingForPlayersTimeConVar>());
        var rule = new TeammatesPassRule(modes, convars.Declare<MpTeammatesCollideConVar>(),
                                         convars.Declare<MpTeammatesPassConVar>(), engine);

        var inner = new Inner();
        var runner = new BotTeammatePushaway(inner, rule, convars.Declare<MpTeammatesPushawayConVar>(),
                                             new Directory(Player(engine, BotA, Vector.Zero), Player(engine, BotB, new Vector(0f, -8f, 0f))),
                                             engine);
        return (runner, inner);
    }

    private static SsPlayer Player(RecordingEngine engine, EntityHandle handle, Vector origin)
    {
        var player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(), TestServices.InfiniteAux(),
                                  TestServices.PrintDamage()) { Handle = handle };
        player.State.BindLocal();
        player.State.Origin = origin;
        player.State.Alive = true;
        player.State.MaxSpeed = 320f;
        return player;
    }

    [Fact]
    public void ABotOverlappingATeammateQueuesAPushedCommand()
    {
        (BotTeammatePushaway runner, Inner inner) = Bots(pass: true);

        runner.Queue(BotA, default);

        Assert.True(inner.Queued.Single().SideMove < -1f, $"side {inner.Queued.Single().SideMove}");
    }

    [Fact]
    public void ABotOverlappingAnEnemyIsNotPushed()
    {
        (BotTeammatePushaway runner, Inner inner) = Bots(pass: true, teamB: 3);

        runner.Queue(BotA, default);

        Assert.Equal(0f, inner.Queued.Single().SideMove);
    }

    /// <summary>Deathmatch: nobody passes, so nobody overlaps on purpose and nobody is pushed.</summary>
    [Fact]
    public void WithoutTeammatesPassingNoBotIsPushed()
    {
        (BotTeammatePushaway runner, Inner inner) = Bots(pass: false);

        runner.Queue(BotA, default);

        Assert.Equal(0f, inner.Queued.Single().SideMove);
    }
}
