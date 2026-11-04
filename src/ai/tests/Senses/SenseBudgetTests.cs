using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// What the senses read on the main thread (user ruling 2026-11-03): a brain
/// reads a bot's view at its decide tick and no other, so its HUD and holdings
/// are read then and not every tick, and under a starved budget no brain's step
/// goes without its view.
/// </summary>
public sealed class SenseBudgetTests : IDisposable
{
    private sealed class Quiet : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private sealed class Navigation : IHl2mpNavigation
    {
        public LoadedNav? Current => null;
        public string? Refusal => null;
        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(EntityHandle player) => null;
        public NavSight? Sight => null;
        public ChokeSlot? Chokes => null;
    }

    private sealed class Runner : IPlayerCommandRunner
    {
        public void Queue(EntityHandle player, in UserCmd cmd) { }
        public int RunQueued(EntityHandle player) => 1;
    }

    private sealed class Names : IBotNameSource
    {
        public BotNamePools? Pools => null;
        public string Refusal => "no names";
    }

    private sealed class WorldPlayers(SenseWorld world) : IBotPlayers
    {
        public readonly List<BotPlayer> Made = new();

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal)
        {
            SenseWorld.Player p = world.Players.First(x => Made.All(b => b.Player != x.Handle));
            Made.Add(new BotPlayer(p.Handle, p.Slot, name));
            refusal = "";
            return p.Handle;
        }

        public bool IsBot(EntityHandle player) => Made.Any(b => b.Player == player);
        public bool Kick(EntityHandle bot, string reason) => false;
        public IReadOnlyList<BotPlayer> Bots => Made;
    }

    private sealed class Hud : IBotHud
    {
        public readonly List<(int Tick, EntityHandle Bot)> Reads = new();
        public int Tick;

        public SelfState Read(EntityHandle bot)
        {
            Reads.Add((Tick, bot));
            return new SelfState(default, default, default, 100, 0, 3, 30, 90, SelfFlags.Alive);
        }
    }

    private sealed class Holdings : IBotInventory
    {
        public readonly List<int> ReadAt = new();
        public int Tick;

        public IReadOnlyList<string> Held(EntityHandle player)
        {
            ReadAt.Add(Tick);
            return ["weapon_smg1"];
        }
    }

    private readonly SenseWorld _world = new();
    private readonly Hud _hud = new();
    private readonly Holdings _holdings = new();
    private readonly TestGlobals _globals = new();
    private readonly Hl2mpAiSession _ai;
    private readonly Hl2mpBots _bots;
    private readonly Hl2mpSenses _senses;
    private readonly string _file = Path.GetTempFileName();
    private int _tick = 1200;

    public SenseBudgetTests()
    {
        var log = new Quiet();
        var nav = new Navigation();
        _world.Add(2, new AiVector(0f, 0f, 0f));
        _world.Add(3, new AiVector(500f, 0f, 0f), yaw: 180f);
        IEnginePlayers nobody = FakeEngine.Of<IEnginePlayers>((m, _) => m.Name == "GetHandle" ? EntityHandle.None : throw FakeEngine.Unasked(m));
        _ai = new Hl2mpAiSession(nav, WeaponCatalog.Stock(), null!, nobody, log,
                                 DeclaredConVars.Fresh<AiThreadsConVar>(), DeclaredConVars.Fresh<AiSyncConVar>(),
                                 DeclaredConVars.Fresh<WeaponRespawnTimeConVar>(), DeclaredConVars.Fresh<ItemRespawnTimeConVar>());
        IEngineFrame frame = FakeEngine.Of<IEngineFrame>((m, _) => m.Name == "TickCount" ? _tick : throw FakeEngine.Unasked(m));
        IEngineEntities entities = _world.Entities();
        IEnginePlayers players = _world.Engine();
        Budget = new AiMainBudget(_globals, FakeEngine.Of<IEngineConsole>((m, _) => m.Name == "FindConVarValue" ? "0" : throw FakeEngine.Unasked(m)),
                                  FakeEngine.Of<IEngineHost>((m, _) => m.Name == "get_IsDedicatedServer" ? false : throw FakeEngine.Unasked(m)),
                                  DeclaredConVars.FreshAt<AiBudgetConVar>("1"), Floor, DeclaredConVars.Fresh<AiBudgetWaitConVar>());
        _bots = new Hl2mpBots(new WorldPlayers(_world), new Runner(), players, entities, WeaponCatalog.Stock(), frame, nav, _holdings, new Names(), new ScoreboardTags(), log, _ai, null, Budget);
        var vision = new VisionPass(players, entities, _world.Traces());
        _senses = new Hl2mpSenses(_bots, _ai, vision, new SnapshotBuilder(players, WeaponCatalog.Stock(), _hud, _world.Stance()), new ActorDirectory(players, _globals), frame, log,
                                  new ReferenceRecorder(_ai, vision), _holdings, WeaponCatalog.Stock(), Budget);
        SkillPresets.TryGet("normal", out SkillProfile? p);
        Assert.Null(_bots.Add("Alyx", p!, 1, out _));
        Assert.Null(_bots.Add("Barney", p!, 2, out _));
    }

    private AiBudgetFloorConVar Floor { get; } = DeclaredConVars.FreshAt<AiBudgetFloorConVar>("0");

    private AiMainBudget Budget { get; }

    public void Dispose()
    {
        _ai.Pool.Dispose();
        _ai.Recording.Stop();
        _globals.Dispose();
        File.Delete(_file);
    }

    private void Tick()
    {
        _tick++;
        _hud.Tick = _tick;
        _holdings.Tick = _tick;
        _globals.Frame++;
        _bots.OnPreThink(0.015f);
        _senses.Sense(new Tick(_tick), 0.015f);
        // The senses start the pool (ai_sync is off): each tick's steps done before the next, as a tick's worth of time gives them.
        _ai.Session.Drain(wait: true);
        _ai.Drives.Settle();
    }

    [Fact]
    public void AHudIsReadAtItsBotsDecideTickAndNoOther()
    {
        for (int t = 0; t < 60; t++)
            Tick();
        Assert.Equal(20, _hud.Reads.Count);   // each of two bots every sixth tick
        foreach ((int tick, EntityHandle bot) in _hud.Reads)
        {
            int slot = _bots.Bots.Single(b => _bots.Applier.PlayerOf(b.Id) == bot).Id.Slot;
            Assert.True(DecideSchedule.IsDecideTick(new Tick(tick), slot), $"slot {slot} read at tick {tick}");
        }

        // Every step had its view: none skipped, and they ran.
        Assert.Equal(0L, _ai.Session.Counters.StepsSkipped);
        Assert.True(_ai.Session.Counters.StepsRun >= 18, $"{_ai.Session.Counters.StepsRun} steps");
    }

    [Fact]
    public void ARecordingReadsEveryBotsHudEveryTick()
    {
        _ai.Recording.Start(_file, new AiRecordingHeader(1, "ss_test", 0, 0, VisionPass.Slots, Hl2mpAiSession.SightedCapacity));
        for (int t = 0; t < 12; t++)
            Tick();
        Assert.Equal(24, _hud.Reads.Count);
    }

    /// <summary>A bot's holdings are read when its brain next steps: it hears of a change at the step it would have, a read a tick apart from it.</summary>
    [Fact]
    public void HoldingsAreReadAtADecideTickAndToldToItsStep()
    {
        for (int t = 0; t < 60; t++)
            Tick();
        Assert.Equal(20, _holdings.ReadAt.Count);
        Assert.True(_ai.Session.Counters.EventsIn >= 2, $"each bot told its holdings: {_ai.Session.Counters.EventsIn}");
    }

    /// <summary>With the floor gone the looks wait and the frames run: no step goes without its view, the view being its bot's own reads.</summary>
    [Fact]
    public void UnderAStarvedBudgetTheLooksWaitAndNoStepGoesWithoutItsView()
    {
        _ai.SetSync(false);
        for (int t = 0; t < 120; t++)
        {
            _tick++;
            _hud.Tick = _tick;
            _holdings.Tick = _tick;
            _globals.Frame++;
            _bots.OnPreThink(0.015f);
            Budget.Current!.Charge(1_000_000d);
            _senses.Sense(new Tick(_tick), 0.015f);
            _ai.Session.Drain(wait: true);
            _ai.Drives.Settle();
        }

        Assert.Equal(0L, _ai.Session.Counters.StepsSkipped);
        Assert.True(_ai.Session.Counters.StepsRun >= 36, $"{_ai.Session.Counters.StepsRun} steps");
        Assert.True(Budget.Measure.Deferrals > 0, "the bots' turns waited");
        Assert.True(Budget.Measure.Forced > 0, "and the bound served them");
    }
}
