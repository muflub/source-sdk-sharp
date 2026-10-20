using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Stock's round end (CHL2MPRules::GoToIntermission, Think, CMultiplayRules::
/// ChangeLevel): every player is shown the scoreboard (VGUIMenu "scores",
/// reliable) and frozen; mp_chattime later the server changes to nextlevel,
/// else the map cycle's next map, else the current one -- once.
/// </summary>
public class IntermissionTests
{
    private static readonly EntityHandle P1 = new(0x010001);
    private static readonly EntityHandle P2 = new(0x010002);

    private sealed class Console : IServerConsole
    {
        public readonly List<string> Executed = new();
        public string Execute(string command) { Executed.Add(command); return ""; }
        public void Quit(int exitCode) { }
    }

    private sealed class Gate(Func<bool> answer) : IRoundEndGate
    {
        public int Asked { get; private set; }
        public bool MayEndRound() { Asked++; return answer(); }
    }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private static (Intermission It, RecordingEngine Engine, Console Console, ConVarRegistry ConVars) Rig(string? mapcycle = null,
                                                                                                         params IRoundEndGate[] gates)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = Services(out var engine);
        engine.Returns("Entities.IsValid", true)
              .ReturnsEachCall("Entities.GetIndex", () => engine.Calls[^1].Args[0] is EntityHandle h && h == P2 ? 2 : 1)
              .ReturnsEachCall("Files.ReadGameFile", () => engine.Calls[^1].Args[0] is "cfg/mapcycle.txt" ? mapcycle : null)
              .Ignores("PlayerMovement.SetFrozen");
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        registry.KeyValue(P1, EntityNames.SsPlayer, "targetname", "a");
        registry.KeyValue(P2, EntityNames.SsPlayer, "targetname", "b");

        var convars = new ConVarRegistry(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        var console = new Console();
        var it = new Intermission(new RecordingLog(), convars, registry, engine, engine, engine, console, engine,
                                  gates.Length == 0 ? null : new ListHooks<IRoundEndGate>(gates));
        it.LevelInit("dm_lockdown", 0f);
        engine.Clear();
        return (it, engine, console, convars);
    }

    private static void Set(ConVarRegistry convars, string name, string value)
        => convars.All.Single(c => c.Name == name).Bind(-1, name, value);

    [Fact]
    public void Beginning_shows_every_player_the_scoreboard_and_freezes_them_once()
    {
        var (it, engine, _, _) = Rig();

        it.Begin(5f);
        it.Begin(6f);

        var sends = engine.AllOf("UserMessages.Send").Where(c => (string)c.Args[0]! == "VGUIMenu").ToList();
        Assert.Equal(new[] { 1, 2 }, sends.Select(s => (int)s.Args[2]!).OrderBy(i => i).ToArray());
        Assert.All(sends, s => Assert.True((bool)s.Args[3]!, "reliable"));
        var reader = new UserMessageReader((byte[])sends[0].Args[4]!);
        var menu = VguiMenu.Read(ref reader);
        Assert.Equal(("scores", true), (menu.Name, menu.Show));
        Assert.Equal(2, engine.AllOf("PlayerMovement.SetFrozen").Count(c => (bool)c.Args[1]!));
        Assert.True(it.GameOver);
    }

    [Fact]
    public void The_level_changes_once_after_mp_chattime()
    {
        var (it, _, console, convars) = Rig();
        Set(convars, "mp_chattime", "10");

        it.Begin(5f);
        it.Think(14.9f);
        Assert.Empty(console.Executed);
        it.Think(15.1f);
        it.Think(16f);

        Assert.Equal(new[] { "changelevel dm_lockdown" }, console.Executed);
    }

    /// <summary>The core's default, with gates that all allow it: stock's intermission and changelevel.</summary>
    [Fact]
    public void Gates_that_allow_it_leave_stock_s_round_end_and_change_level()
    {
        var gate = new Gate(() => true);
        var (it, engine, console, convars) = Rig(null, gate);
        Set(convars, "mp_chattime", "5");

        it.Begin(1f);
        it.Think(7f);

        Assert.Equal(1, gate.Asked);
        Assert.True(it.GameOver);
        Assert.Equal(2, engine.AllOf("PlayerMovement.SetFrozen").Count(c => (bool)c.Args[1]!));
        Assert.Equal(new[] { "changelevel dm_lockdown" }, console.Executed);
    }

    /// <summary>A mod whose levels never change (tf2_descent, hosted): the round end is ignored entirely.</summary>
    [Fact]
    public void A_gate_that_refuses_ignores_the_round_end_entirely()
    {
        var gate = new Gate(() => false);
        var (it, engine, console, convars) = Rig(null, gate);
        Set(convars, "mp_chattime", "5");

        it.Begin(1f);
        it.Think(7f);
        it.Think(60f);

        Assert.True(gate.Asked > 0, "the gate was asked");
        Assert.False(it.GameOver);
        Assert.Empty(engine.AllOf("UserMessages.Send"));
        Assert.Empty(engine.AllOf("PlayerMovement.SetFrozen"));
        Assert.Empty(console.Executed);
        Assert.Equal(0, it.Changes);
    }

    /// <summary>A broken gate must not trap a round that should end (IRespawnGate's rule).</summary>
    [Fact]
    public void A_gate_that_throws_does_not_veto()
    {
        var (it, _, console, convars) = Rig(null, new Gate(() => throw new InvalidOperationException("broken mod")));
        Set(convars, "mp_chattime", "5");

        it.Begin(1f);
        it.Think(7f);

        Assert.True(it.GameOver);
        Assert.Equal(new[] { "changelevel dm_lockdown" }, console.Executed);
    }

    [Fact]
    public void Nextlevel_wins_over_the_map_cycle()
    {
        var (it, _, _, convars) = Rig("dm_a\ndm_b\n");
        Set(convars, "nextlevel", "dm_override");
        Assert.Equal("dm_override", it.NextMap());
    }

    [Fact]
    public void The_map_cycle_is_walked_in_order_skipping_comments_and_wrapping()
    {
        var (it, _, _, _) = Rig("// a comment\r\n dm_a \n\ndm_b\n");
        Assert.Equal(new[] { "dm_a", "dm_b", "dm_a" }, new[] { it.NextMap(), it.NextMap(), it.NextMap() });
    }

    [Fact]
    public void With_no_map_cycle_the_current_map_is_next()
    {
        var (it, _, _, _) = Rig();
        Assert.Equal("dm_lockdown", it.NextMap());
    }

    [Fact]
    public void A_new_level_ends_the_intermission()
    {
        var (it, _, _, _) = Rig();
        it.Begin(5f);
        it.LevelInit("dm_lockdown", 30f);
        Assert.False(it.GameOver);
    }
    /// <summary>
    /// The frag limit, scored off player_death, sends the rules to the
    /// intermission (not the empty native round end), and the level changes
    /// mp_chattime later (CHL2MPRules::Think).
    /// </summary>
    [Fact]
    public void The_frag_limit_goes_to_intermission_and_then_changes_the_level()
    {
        var (it, engine, console, convars) = Rig();
        Set(convars, "mp_chattime", "5");
        engine.Returns("Host.ActiveSide", Side.Server).Returns("Host.CurrentTime", 20f)
              .Ignores("Players.FindByUserId", "Players.GetUserId", "Players.GetTeam");
        var deaths = new PlayerDeathEvent();
        var board = new Scoreboard(engine, new PlayerDisconnectEvent(), new RecordingLog());
        var rules = new GameRules(new RecordingLog(), convars, engine, engine, engine, board, deaths, it);
        rules.ResetRound();
        Set(convars, "mp_fraglimit", "2");

        deaths.Raise(new PlayerDeathArgs(UserId: 7, AttackerUserId: 9, "pistol", false));
        rules.Think();
        Assert.False(it.GameOver, "one frag is under the limit");

        deaths.Raise(new PlayerDeathArgs(UserId: 7, AttackerUserId: 9, "pistol", false));
        rules.Think();
        Assert.True(it.GameOver, "the second reaches mp_fraglimit");

        engine.Returns("Host.CurrentTime", 26f);
        rules.Think();
        Assert.Equal(new[] { "changelevel dm_lockdown" }, console.Executed);
    }

    [Fact]
    public void A_time_limit_ends_the_round()
    {
        var (it, engine, _, convars) = Rig();
        engine.Returns("Host.ActiveSide", Side.Server).Returns("Host.CurrentTime", 0f);
        var rules = new GameRules(new RecordingLog(), convars, engine, engine, engine,
                                  new Scoreboard(engine, new PlayerDisconnectEvent(), new RecordingLog()), new PlayerDeathEvent(), it);
        ((IGameLifecycle)rules).OnLevelInit("dm_lockdown");
        Set(convars, "mp_timelimit", "1");

        engine.Returns("Host.CurrentTime", 59f);
        rules.Think();
        Assert.False(it.GameOver);
        engine.Returns("Host.CurrentTime", 60.5f);
        rules.Think();
        Assert.True(it.GameOver, "a minute is up");
    }
}
