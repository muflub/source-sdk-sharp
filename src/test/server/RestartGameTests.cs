using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The core's mp_restartgame (stock CHL2MPRules::CheckRestartGame and
/// RestartGame): setting the convar arms a countdown that tells the players,
/// puts the convar back to 0, and at its second brings the map back as it
/// loaded, respawns everyone (holster, strip, respawn), zeroes the counts and
/// fires stock's round_start. All of it on the core's own arm only: a mode
/// that owns the round reads mp_restartgame through its own machine, so none
/// of this runs while one is active.
///
/// <para>
/// The facts drive <see cref="GameRules.Think"/> on a fake clock, the way the
/// game drives it — arming through the convar's change callback, not a
/// backdoor. The mp_readyrestart half of stock's CheckRestartGame and
/// mp_restartgame_immediate (declared but never read by HL2MP) are not here;
/// they are the divergence row's, not the facts'.
/// </para>
/// </summary>
public class RestartGameTests
{
    private static readonly IntPtr Event = new(0x50);
    private static readonly EntityHandle PlayerOne = new(0x0001_0001u);
    private static readonly EntityHandle PlayerTwo = new(0x0001_0002u);

    /// <summary>The mode the game starts in, whose machine IS the rules.</summary>
    private sealed class NullMode { }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class FakeFlow : IRoundFlow
    {
        public int Thinks { get; private set; }
        public bool RoundOver => false;
        public void Think(float now) => Thinks++;
        public void ResetRound() { }
        public void OnPlayerSpawned(EntityHandle player, float now) { }
        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }

    private sealed class Mode(string id, IRoundFlow rounds) : IGameMode
    {
        public string Id { get; } = id;
        public IRoundFlow Rounds { get; } = rounds;
        public IScoring? Scoring => null;
        public IModeSetup? Setup => null;
    }

    private sealed class Selector(Func<string?> pick) : IGameModeSelector
    {
        public string? PickMode() => pick();
    }

    /// <summary>Two players the directory knows, the walk's subjects.</summary>
    private sealed class FakeRoster(params EntityHandle[] players) : IPlayerDirectory
    {
        public PlayerRecord this[EntityHandle player] => new();
        public bool Knows(EntityHandle player) => false;
        public IEnumerable<(EntityHandle Player, PlayerRecord Record)> All
            => players.Select(p => (p, new PlayerRecord()));
        public float WaitRemaining(EntityHandle player, float now) => 0f;
        public float RespawnDelay { get; set; }
        public ObserverMode DeathView { get; set; }
        public bool DropWeaponOnDeath { get; set; }
        public float DropSpeed { get; set; }
        public void OnSpawned(EntityHandle player, float now) { }
        public void OnKilled(EntityHandle player, EntityHandle attacker, float now) { }
        public void Forget(EntityHandle player) { }
    }

    /// <summary>Counts the cleanups and keeps the filter each was asked with.</summary>
    private sealed class CleanupSpy : IMapCleanup
    {
        public int Calls { get; private set; }
        public IMapCleanupFilter? LastFilter { get; private set; }

        public MapCleanupReport CleanUpMap(IMapCleanupFilter filter)
        {
            Calls++;
            LastFilter = filter;
            return default;
        }
    }

    /// <summary>
    /// The engine side of mp_restartgame: the convar's value lives here, so
    /// CheckRestartGame's SetValue(0) is seen by the same place the next read
    /// looks, the way the engine's change callback delivers a console write.
    /// </summary>
    private sealed class Backend : IConVarBackend
    {
        public string Held = "0";

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max) => 0;
        public void Set(int id, string value) => Held = value;
        public string Get(int id) => Held;
        public string? ValueOf(string name) => Held;
    }

    private sealed class Rig
    {
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(TestServices.UnattachedConsole());
        public readonly RecordingLog Log = new();
        public readonly RecordingEngine Host = TestServices.Fake();
        public readonly PlayerDeathEvent Deaths = new();
        public readonly Scoreboard Board;
        public readonly ServiceContainer Services = TestServices.Services();
        public readonly CleanupSpy Cleanup = new();
        public readonly FakeRoster Roster = new(PlayerOne, PlayerTwo);
        public readonly GameModes Modes;
        public readonly GameRules Rules;
        private readonly Backend _restartBackend = new();

        public Rig()
        {
            ConVars.RegisterAll(typeof(GameRules).Assembly);

            Host.Returns("Host.ActiveSide", Side.Server);
            Host.Ignores("Players.FindByUserId", "Players.GiveItem", "Players.GiveAmmo");
            // The restart walk's answer-carrying calls.
            Host.Returns("Players.HolsterActiveWeapon", false);
            Host.Returns("GameEvents.EventCreate", Event);
            Host.Returns("GameEvents.EventFire", true);

            var restart = ConVars.Get<MpRestartGameConVar>()!;
            restart.Backend = _restartBackend;
            restart.Bind(0, "mp_restartgame", "0");

            Board = new Scoreboard(Host, new PlayerDisconnectEvent(), new RecordingLog());
            Services.AddInstance<IMapCleanup>(Cleanup);

            // The module's order: the proxy before the rules, then Attach.
            Modes = new GameModes(Log, Host, Services, ConVars);
            Rules = new GameRules(Log, ConVars, Host, Host,
                                  TestServices.Fake().Returns("Entities.IsValid", false), Board, Deaths,
                                  modes: Modes, roster: Roster, messages: Host, events: Host);
            Modes.Attach(Rules);
            Services.AddInstance(Modes);
        }

        /// <summary>Registers a mode the way the container hands one out.</summary>
        public void Register(IGameMode mode) => Services.AddInstance(mode.GetType(), mode);

        public void Selectors(params IGameModeSelector[] selectors)
            => Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(selectors));

        public void SetTime(float now) => Host.Returns("Clock.Now", now).Returns("Host.CurrentTime", now);

        /// <summary>Sets mp_restartgame the way the console would.</summary>
        public void SetRestart(int seconds)
        {
            var restart = ConVars.Get<MpRestartGameConVar>()!;
            string value = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _restartBackend.Held = value;
            restart.NotifyChanged(value, restart.RawValue);
        }

        /// <summary>What a read of mp_restartgame answers now.</summary>
        public int RestartValue => ConVars.Get<MpRestartGameConVar>()!.Value;

        public void ThinkAt(float now)
        {
            SetTime(now);
            Rules.Think();
        }
    }

    /// <summary>The restart walk's per-player steps, in the order they ran.</summary>
    private static List<(EntityHandle Player, string Step)> Walk(RecordingEngine host)
        => [.. host.Calls
            .Where(c => c.Member is "Players.HolsterActiveWeapon" or "Players.RemoveAllItems" or "Players.Respawn")
            .Select(c => ((EntityHandle)c.Args[0]!, c.Member))];

    //-------------------------------------------------------------------------
    // The countdown: armed at the second, and not a frame before it.
    //-------------------------------------------------------------------------

    [Fact]
    public void MpRestartGameArmsARestartThatFiresAfterTheCountdownAndNotBefore()
    {
        var rig = new Rig();
        rig.SetRestart(3);

        rig.ThinkAt(10f);

        Assert.True(rig.Rules.RestartPending, "mp_restartgame 3 armed a restart");
        Assert.Equal(0, rig.Cleanup.Calls);   // armed, not yet fired

        rig.ThinkAt(11f);
        rig.ThinkAt(12f);

        Assert.Equal(0, rig.Cleanup.Calls);   // not before its time

        rig.ThinkAt(13f);

        Assert.Equal(1, rig.Cleanup.Calls);   // fired at its second
        Assert.False(rig.Rules.RestartPending, "a fired restart is not pending");
    }

    [Fact]
    public void MpRestartGameGoesBackToZeroOnceTheRestartIsArmed()
    {
        var rig = new Rig();
        rig.SetRestart(3);

        rig.ThinkAt(10f);

        // A convar that stayed 3 would re-arm every periodic pass and never
        // fire; stock zeroes it the frame it arms.
        Assert.Equal(0, rig.RestartValue);
    }

    //-------------------------------------------------------------------------
    // What a fired restart does.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARestartCleansTheMapOnceWithTheCoreFilterAndRespawnsEveryPlayer()
    {
        var rig = new Rig();
        rig.SetRestart(1);

        rig.ThinkAt(10f);
        rig.ThinkAt(11f);   // the restart fires

        Assert.Equal(1, rig.Cleanup.Calls);
        Assert.IsType<HL2MPMapCleanupFilter>(rig.Cleanup.LastFilter);

        // Stock's walk, per player in order: holster, strip, respawn.
        Assert.Equal(
        [
            (PlayerOne, "Players.HolsterActiveWeapon"),
            (PlayerOne, "Players.RemoveAllItems"),
            (PlayerOne, "Players.Respawn"),
            (PlayerTwo, "Players.HolsterActiveWeapon"),
            (PlayerTwo, "Players.RemoveAllItems"),
            (PlayerTwo, "Players.Respawn"),
        ],
        Walk(rig.Host));

        // Stock's RemoveAllItems( true ): the suit goes with the weapons.
        Assert.All(rig.Host.Calls.Where(c => c.Member == "Players.RemoveAllItems"),
                   c => Assert.True((bool)c.Args[1]!, "the strip takes the suit too"));
    }

    [Fact]
    public void ARestartResetsTheScores()
    {
        var rig = new Rig();
        rig.Board.Add(5, 1, 2, 3);
        rig.SetRestart(1);

        rig.ThinkAt(10f);
        rig.ThinkAt(11f);

        Assert.Equal(0, rig.Board.ScoreFor(5));
        Assert.Equal(0, rig.Board.KillsFor(5));
        Assert.Equal(0, rig.Board.DeathsFor(5));
    }

    [Fact]
    public void ARestartFiresStocksRoundStart()
    {
        var rig = new Rig();
        rig.SetRestart(1);

        rig.ThinkAt(10f);
        rig.ThinkAt(11f);

        var created = Assert.Single(rig.Host.Calls, c => c.Member == "GameEvents.EventCreate");
        Assert.Equal("round_start", created.Args[0]);

        Assert.Equal(
            new (string Key, object? Value)[]
            {
                ("fraglimit", 0),
                ("priority", 6),
                ("objective", "DEATHMATCH"),
            },
            rig.Host.Calls.Where(c => c.Member.StartsWith("GameEvents.EventSet", StringComparison.Ordinal))
                          .Select(c => ((string)c.Args[1]!, c.Args[2]))
                          .ToArray());

        Assert.Contains(rig.Host.Calls,
                        c => c.Member == "GameEvents.EventFire" && Equals(c.Args[0], Event));
    }

    //-------------------------------------------------------------------------
    // The announcement: stock's two UTIL_ClientPrintAll lines.
    //-------------------------------------------------------------------------

    [Fact]
    public void ArmingTellsThePlayersInCentrePrintAndConsole()
    {
        var rig = new Rig();
        rig.SetRestart(3);

        rig.ThinkAt(10f);

        var sends = rig.Host.Calls.Where(c => c.Member == "UserMessages.Send").ToList();
        Assert.Equal(2, sends.Count);   // centre and console, nothing else

        // Stock prints centre first, then console; each to everyone (player 0).
        foreach (var (sent, destination) in sends.Zip(new byte[] { 4, 2 }))
        {
            Assert.Equal(0, sent.Args[2]);

            var reader = new UserMessageReader((byte[])sent.Args[4]!);
            var msg = TextMsg.Read(ref reader);
            Assert.Equal(destination, msg.Destination);
            Assert.Equal("Game will restart in %s1 %s2", msg.Text);
            Assert.Equal(["3", "SECONDS"], msg.Params);
        }
    }

    //-------------------------------------------------------------------------
    // The mode gate: a mode that owns the round owns mp_restartgame too.
    //-------------------------------------------------------------------------

    [Fact]
    public void NothingIsRestartedWhileAModeOwnsTheRound()
    {
        var rig = new Rig();
        var flow = new FakeFlow();
        rig.Register(new Mode("owned", flow));
        rig.Selectors(new Selector(() => "owned"));

        rig.SetRestart(3);
        for (float t = 10f; t <= 13f; t += 1f)
            rig.ThinkAt(t);

        Assert.Equal(4, flow.Thinks);   // the mode's machine ran the frames

        // And none of the core's restart: no cleanup, no respawn, and the
        // convar still where the console left it for the mode's own machine.
        Assert.Equal(0, rig.Cleanup.Calls);
        Assert.DoesNotContain(rig.Host.Calls, c => c.Member == "Players.Respawn");
        Assert.Equal(3, rig.RestartValue);
        Assert.False(rig.Rules.RestartPending);
    }
}
