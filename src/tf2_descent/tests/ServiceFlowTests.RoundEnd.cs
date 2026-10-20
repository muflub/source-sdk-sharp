using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Server;
using TF2.Descent.Server;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The round end in a hosted descent level. The console here stands in for
/// the engine as the k3s run of 2026-10-11 saw it: a changelevel disconnects
/// every client, and PlayerService takes each disconnect as a leave, which
/// releases the lease.
/// </summary>
public sealed partial class ServiceFlowTests
{
    private sealed class ReloadingConsole(PlayerService players, int entIndex) : IServerConsole
    {
        public List<string> Executed { get; } = new();

        public string Execute(string command)
        {
            Executed.Add(command);
            if (command.StartsWith("changelevel ", StringComparison.Ordinal))
                players.OnClientDisconnected(entIndex);
            return "";
        }

        public void Quit(int exitCode) { }
    }

    private sealed class NoEntities : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private const string Hero = "76561197960287930";

    /// <summary>A hosted level with player 3 joined, and the core's round end over it.</summary>
    private static (World W, PlayerService Players, Intermission It, ReloadingConsole Console, RecordingEngine Engine) RoundEnd(params IRoundEndGate[] gates)
    {
        var (w, players, clients, _) = Players("level");
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        players.OnClientActive(3);
        Assert.NotNull(players.BySteamId(Hero));

        var engine = new RecordingEngine();
        engine.Returns("Host.CurrentTime", 0f);
        var console = new ReloadingConsole(players, 3);
        var it = new Intermission(w.Log, ConVarRegistry.For(engine), new NoEntities(), engine, engine, engine, console, engine,
                                  gates.Length == 0 ? null : new Hooks<IRoundEndGate>(gates));
        it.LevelInit("tf2_descent-1-0123456789abcdef", 0f);
        return (w, players, it, console, engine);
    }

    private static IRoundEndGate DescentGate()
    {
        var types = typeof(LevelHop).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IRoundEndGate).IsAssignableFrom(t)
                        && t.GetCustomAttributes<SideServiceAttribute>().Any(a => a.Side == Side.Server))
            .ToArray();
        return (IRoundEndGate)Activator.CreateInstance(Assert.Single(types))!;
    }

    /// <summary>The control: without the mod's gate, the round end changes level and the player's lease goes.</summary>
    [Fact]
    public void Stock_round_end_in_a_hosted_level_would_change_level_and_release_the_lease()
    {
        var (w, _, it, console, _) = RoundEnd();

        it.Begin(0f);
        it.Think(60f);

        Assert.Equal(["changelevel tf2_descent-1-0123456789abcdef"], console.Executed);
        Assert.Contains($"leave {Hero} 203.0.113.7:27005", w.Link.Calls);
    }

    [Fact]
    public void Descent_never_lets_a_round_end()
    {
        Assert.False(DescentGate().MayEndRound());
    }

    [Fact]
    public void Game_end_and_cs_sv_end_round_in_descent_leave_the_level_and_its_lease_alone()
    {
        var (w, players, it, console, engine) = RoundEnd(DescentGate());

        // cs_sv_end_round, then game_end's EndGame input, as the map would fire it.
        new EndRoundCommand(w.Log, engine, it).Execute("");
        var gameEnd = new GameEnd(engine, it);
        var endGame = typeof(GameEnd).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(m => m.GetCustomAttribute<ScriptedEventAttribute>() is { Name: "EndGame" });
        endGame.Invoke(gameEnd, []);

        it.Think(60f);
        it.Think(600f);

        Assert.False(it.GameOver);
        Assert.Empty(console.Executed);
        Assert.DoesNotContain(engine.Calls, c => c.Member is "UserMessages.Send" or "PlayerMovement.SetFrozen" or "Host.EndRound");
        Assert.NotNull(players.BySteamId(Hero));
        Assert.DoesNotContain(w.Link.Calls, c => c.StartsWith("leave ", StringComparison.Ordinal));
    }
}
