using System.Collections.Concurrent;
using System.Security.Cryptography;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// The server's experience feed (plan_bots_teams_tf2 Part G, G.3): the game
/// thread hands samples, hits and deaths to a worker and never touches the
/// store or the accumulator; the snapshot is loaded when a level's key is
/// known and again only at a round's start; flushes come at a round's end,
/// every minute and at the level's end; off, it opens nothing.
/// </summary>
public sealed partial class ExperienceServiceTests : IDisposable
{
    private const string Db = "/nowhere/experience.db";
    private static readonly byte[] Bsp = [1, 2, 3, 4, 5];
    private static readonly string Checksum = Convert.ToHexStringLower(SHA256.HashData(Bsp))[..16];
    private static readonly ExperienceKey Key = new("ctf_test", Checksum, "ctf");

    private readonly SenseWorld _world = new();
    private readonly TestGlobals _globals = new(maxClients: 16);
    private readonly Game _game = new();
    private readonly SpyStores _stores = new();

    // The level's navigation as the feed sees it: none, unless a fact gives it a graph (lane G4a's routes).
    private IAiNavigation _nav = new NoNavigation();

    // The game's links as the experience keys them (lane G5e): none, unless a fact gives some.
    private readonly GameLinks _links = new();
    private readonly Lines _log = new();
    private readonly HashSet<EntityHandle> _bots = new();
    private readonly List<ExperienceService> _made = new();
    private int _tick = 1000;

    public void Dispose()
    {
        foreach (ExperienceService service in _made)
            service.OnShutdown();
        _globals.Dispose();
    }

    /// <summary>The game's answers, as a fact sets them.</summary>
    private sealed class Game : IExperienceGame
    {
        public bool Keeps { get; set; } = true;
        public string Mode { get; set; } = "ctf";
        public int Stage { get; set; }
        public bool RoundOver { get; set; }
        public int WinningRole { get; set; }
        public int Weapon { get; set; } = 6;
        public Dictionary<EntityHandle, (int Role, int Class)> Players { get; } = new();

        public (int Role, int Class) Of(EntityHandle player) => Players.GetValueOrDefault(player, (2, 3));

        public int WeaponClass(EntityHandle killer) => Weapon;
    }

    private sealed class Lines : ILog
    {
        public ConcurrentQueue<(string Line, int Thread, bool Warning)> All { get; } = new();

        public void Msg(string message) => All.Enqueue((message, Environment.CurrentManagedThreadId, false));

        public void Warning(string message) => All.Enqueue((message, Environment.CurrentManagedThreadId, true));
    }

    /// <summary>Stores that note the thread of every call, over one memory store a path (so a fact can seed it and read it).</summary>
    private sealed class SpyStores : IExperienceStores
    {
        public ConcurrentQueue<(string Call, int Thread)> Calls { get; } = new();
        public ConcurrentQueue<(ExperienceKey Key, ExperienceDelta Delta)> Merged { get; } = new();
        public ConcurrentQueue<ExperienceKey> Loaded { get; } = new();
        public MemoryExperienceStore Kept { get; } = new();
        public string? Refuse { get; set; }

        /// <summary>What the stores say while they wait on a busy file (null: attached); while set they answer as an empty store.</summary>
        public string? Wait
        {
            get => Volatile.Read(ref _wait);
            set => Volatile.Write(ref _wait, value);
        }

        private string? _wait;
        public int Disposed;

        public IExperienceStore Open(string path)
        {
            Calls.Enqueue(("open " + path, Environment.CurrentManagedThreadId));
            return new Spy(this);
        }

        private sealed class Spy(SpyStores owner) : IExperienceStore
        {
            public string? Refusal => owner.Refuse;

            public string? Waiting => owner.Wait;

            public MapExperience Load(ExperienceKey key, double halfLife)
            {
                owner.Calls.Enqueue(("load", Environment.CurrentManagedThreadId));
                owner.Loaded.Enqueue(key);
                return owner.Wait is null ? owner.Kept.Load(key, halfLife) : new MapExperience(key, 0, new ExperienceDelta());
            }

            public bool Merge(ExperienceKey key, ExperienceDelta delta, double halfLife)
            {
                owner.Calls.Enqueue(("merge", Environment.CurrentManagedThreadId));
                owner.Merged.Enqueue((key, delta));
                return owner.Kept.Merge(key, delta, halfLife);
            }

            public ExperienceCounts Counts(ExperienceKey key)
            {
                owner.Calls.Enqueue(("counts", Environment.CurrentManagedThreadId));
                return owner.Kept.Counts(key);
            }

            public void Dispose()
            {
                owner.Calls.Enqueue(("dispose", Environment.CurrentManagedThreadId));
                Interlocked.Increment(ref owner.Disposed);
            }
        }
    }

    /// <summary>A level whose navigation is <paramref name="graph"/> alone.</summary>
    private sealed class GraphNavigation(NavGraph graph) : IAiNavigation
    {
        public LoadedNav? Current { get; } = new("ctf_test", null!, new DerivedNav(graph, [], [], [], 0), null!, "", "", false, 0d);
        public string? Refusal => null;
        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(EntityHandle player) => null;
        public NavSight? Sight => null;
        public ChokeSlot? Chokes => null;
    }

    /// <summary>A game's links, as a fact sets them.</summary>
    private sealed class GameLinks : IExperienceLinks
    {
        public ExperienceLinkKeys? Keys { get; set; }
    }

    /// <summary>A link row's key, as a game would make one.</summary>
    private static LinkKey LinkRow(int fromX = 1)
        => new(3, new WorldBin(fromX, 1, 0), new WorldBin(3, 1, 2), 0, LinkSource.Solver, new LinkRecipe(System.Collections.Immutable.ImmutableArray.Create((byte)1, (byte)2)));

    /// <summary>A graph and one link over it, keyed by <see cref="LinkRow"/>.</summary>
    private static (NavGraph Graph, ExperienceLinkKeys Keys) LinkLevel()
    {
        var (graph, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse("......", "......", "......", "......");
        NavLinks set = NavLinks.Build(graph, [new NavLink(ids[1, 1], ids[4, 1], NavLinkKind.Blast, 1f, 30f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        return (graph, new ExperienceLinkKeys(set, [LinkRow()]));
    }

    /// <summary>Frames until the routes' work is done and published (or 50).</summary>
    private void UntilRoutes(ExperienceService service, Func<bool> done)
    {
        for (int i = 0; i < 50 && !done(); i++)
        {
            Frame(service);
            service.RouteWork?.Wait();
        }
    }

    [Fact]
    public void ALinksTryIsWrittenToItsRowInFull()
    {
        ExperienceService service = Playing();
        Frame(service);
        Assert.True(service.NoteLink(LinkRow(), new LinkStat(1d, 1d, 2.5d, 30d)));
        Assert.True(service.NoteLink(LinkRow(), new LinkStat(1d, 0d, 0d, 0d)));
        service.OnLevelShutdownPreEntity();
        Assert.Equal(new LinkStat(2d, 1d, 2.5d, 30d), Assert.Single(Merged().Links).Value);
        Assert.Equal(2, service.LinksNoted);
    }

    /// <summary>Lane G4b: a bot's try of a way into the enemy's base is one try of its row in full, under its side and class and the stage now.</summary>
    [Fact]
    public void AnOutcomeIsWrittenToItsRowInFull()
    {
        _game.Stage = 1;
        ExperienceService service = Playing();
        Frame(service);
        Assert.True(service.NoteOutcome(2, 3, 1, 42L, 0.25d));
        Assert.True(service.NoteOutcome(2, 3, 1, 42L, 1d));
        Assert.False(service.NoteOutcome(0, 3, 1, 42L, 1d));
        service.OnLevelShutdownPreEntity();
        (OutcomeKey key, OutcomeStat stat) = Assert.Single(Merged().Outcomes);
        Assert.Equal(new OutcomeKey(new ExperienceContext(1, 2, 3), 1, 42L), key);
        Assert.Equal(new OutcomeStat(2d, 1.25d), stat);
        Assert.Equal(2, service.OutcomesNoted);
    }

    [Fact]
    public void AnOutcomeOutsideARoundBeingPlayedIsNotWritten()
    {
        _game.RoundOver = true;
        ExperienceService service = Playing();
        Frame(service);
        Assert.False(service.NoteOutcome(2, 3, 1, 42L, 1d));
        service.OnLevelShutdownPreEntity();
        Assert.Empty(Merged().Outcomes);
    }

    [Fact]
    public void ALinksTryOutsideARoundBeingPlayedIsNotWritten()
    {
        _game.RoundOver = true;
        ExperienceService service = Playing();
        Frame(service);
        Assert.False(service.NoteLink(LinkRow(), new LinkStat(1d, 1d, 2.5d, 30d)));
        service.OnLevelShutdownPreEntity();
        Assert.Empty(Merged().Links);
        Assert.Equal(0, service.LinksNoted);
    }

    [Fact]
    public void TheRoutesSayWhichOfTheGamesLinksTheSnapshotLeavesOut()
    {
        var (graph, keys) = LinkLevel();
        _nav = new GraphNavigation(graph);
        _links.Keys = keys;
        var seeded = new ExperienceDelta();
        seeded.Add(LinkRow(), new LinkStat(6d, 1d, 2d, 30d));
        _stores.Kept.Merge(Key, seeded, 20d);
        ExperienceService service = Playing();
        UntilRoutes(service, () => service.Routes.Current.LinksShut is not null);
        Assert.Same(service.Slot.Current, service.Routes.Current.Of);
        Assert.True(service.Routes.Current.LinksShut!.IsOver(keys.Links));
        Assert.True(service.Routes.Current.LinksShut.Shut(0));
        Assert.Contains(_log.All, l => l.Line.Contains("1 of 1 links left out", StringComparison.Ordinal));
        Assert.Contains("\"links\":1,\"links_shut\":1,\"shut\":[0]", service.Json(), StringComparison.Ordinal);
    }

    [Fact]
    public void LinksFoundAfterTheSnapshotHaveItsRoutesBuiltAgainWithThem()
    {
        var (graph, keys) = LinkLevel();
        _nav = new GraphNavigation(graph);
        var seeded = new ExperienceDelta();
        seeded.Add(LinkRow(), new LinkStat(6d, 1d, 2d, 30d));
        _stores.Kept.Merge(Key, seeded, 20d);
        ExperienceService service = Playing();
        UntilRoutes(service, () => service.RouteBuilds > 0);
        Assert.Null(service.Routes.Current.LinksShut);
        _links.Keys = keys;
        UntilRoutes(service, () => service.Routes.Current.LinksShut is not null);
        Assert.True(service.Routes.Current.LinksShut!.Shut(0));
        Assert.Equal(2, service.RouteBuilds);
    }

    /// <summary>Where the game counts a hold (lane G4b): near the objective of a side, as a fact says; kind 7.</summary>
    private sealed class Holds(Func<int, AiVector, bool> near) : IExperienceHolds
    {
        public int HoldKind => 7;

        public bool NearOwn(int role, AiVector at) => near(role, at);
    }

    /// <summary>
    /// Lane G4b: a player that stays in one place for 5 s near its side's objective is a <c>spot</c> row of the game's hold
    /// kind at the feed's weight, a bot's a quarter of a human's; one that holds away from it is no row.
    /// </summary>
    [Fact]
    public void AHoldNearItsSidesObjectiveIsASpotRowAtTheFeedsWeight()
    {
        SenseWorld.Player human = _world.Add(2, new AiVector(100f, 100f, 0f), yaw: 90f), bot = _world.Add(3, new AiVector(700f, 100f, 0f), yaw: 0f);
        SenseWorld.Player far = _world.Add(4, new AiVector(5000f, 100f, 0f));
        _bots.Add(bot.Handle);
        _game.Players[human.Handle] = (2, 3);
        _game.Players[bot.Handle] = (3, 3);
        _game.Players[far.Handle] = (2, 3);
        ExperienceService service = Playing(holds: new Holds((_, at) => at.X < 1000f));
        for (int i = 0; i < 51; i++)
            Frame(service);
        service.OnLevelShutdownPreEntity();

        ExperienceDelta delta = Merged();
        double seconds = 50 * ExperienceService.SampleTicks * 0.015d;
        Assert.Equal(2, delta.Spots.Count);
        SpotStat held = delta.Spots[new SpotKey(new ExperienceContext(0, 2, 3), 7, new WorldBin(1, 1, 0), 4)];
        Assert.Equal(1d, held.Uses, 6);
        Assert.Equal(seconds, held.SecondsHeld, 6);
        Assert.Equal(0d, held.Kills + held.Deaths, 6);
        SpotStat byBot = delta.Spots[new SpotKey(new ExperienceContext(0, 3, 3), 7, new WorldBin(10, 1, 0), 0)];
        Assert.Equal(0.25d, byBot.Uses, 6);
        Assert.Equal(seconds * 0.25d, byBot.SecondsHeld, 6);
        Assert.Equal(2, service.SpotsNoted);
    }

    [Fact]
    public void AHoldAwayFromItsObjectiveOrInAGameThatCountsNoneIsNoRow()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Playing(holds: new Holds((_, _) => false));
        for (int i = 0; i < 51; i++)
            Frame(service);
        service.OnLevelShutdownPreEntity();
        Assert.Empty(Merged().Spots);
        Assert.Equal(0, service.SpotsNoted);
        Assert.NotEmpty(Merged().Cells);
    }

    [Fact]
    public void AHoldEndingInADeathIsWrittenWithTheDeath()
    {
        SenseWorld.Player holder = _world.Add(2, new AiVector(100f, 100f, 0f)), killer = _world.Add(3, new AiVector(900f, 100f, 0f));
        ExperienceService service = Playing(holds: new Holds((_, at) => at.X < 500f));
        for (int i = 0; i < 51; i++)
            Frame(service);
        service.OnPlayerDied(new PlayerDeath(holder.Handle, 2, killer.Handle, new Vector(100f, 100f, 0f), EntityHandle.None));
        Assert.Equal(1, service.SpotsNoted);
        service.OnLevelShutdownPreEntity();
        Assert.Equal(1d, Assert.Single(Merged().Spots).Value.Deaths);
    }

    private sealed class NoNavigation : IAiNavigation
    {
        public LoadedNav? Current => null;
        public string? Refusal => null;
        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(EntityHandle player) => null;
        public NavSight? Sight => null;
        public ChokeSlot? Chokes => null;
    }

    /// <summary>The process's command line, as a fact has it.</summary>
    private sealed class CommandLine(params string[] args) : ILocalCommandLine
    {
        public IReadOnlyList<string> Args { get; } = args;
    }

    private ExperienceService Service(string db = Db, string learn = "1", IExperienceStores? stores = null, bool noStores = false, IExperienceHolds? holds = null,
                                      params string[] commandLine)
    {
        IEnginePlayers players = _world.Engine();
        IBotPlayers bots = FakeEngine.Of<IBotPlayers>((m, a) => m.Name == "IsBot" ? _bots.Contains((EntityHandle)a[0]!) : throw FakeEngine.Unasked(m));
        IEngineFrame frame = FakeEngine.Of<IEngineFrame>((m, _) => m.Name == "TickCount" ? _tick : throw FakeEngine.Unasked(m));
        IEngineFiles files = FakeEngine.Of<IEngineFiles>((m, a) => m.Name == "ReadGameFileBytes" && (string)a[0]! == "maps/CTF_Test.bsp" ? Bsp : throw FakeEngine.Unasked(m));
        var service = new ExperienceService(_game, _nav, players, _world.Entities(), bots, new ActorDirectory(players, _globals), frame, files, _log,
                                            DeclaredConVars.FreshAt<AiExperienceDbConVar>(db), DeclaredConVars.Fresh<AiExperienceHalfLifeConVar>(),
                                            DeclaredConVars.FreshAt<AiExperienceLearnConVar>(learn), new CommandLine(["hl2_linux64", .. commandLine]),
                                            noStores ? null : stores ?? _stores, links: _links, holds: holds);
        _made.Add(service);
        return service;
    }

    /// <summary>One sampled frame: the tick moves a sample on, the feed thinks, and the worker catches up.</summary>
    private void Frame(ExperienceService service, int ticks = ExperienceService.SampleTicks)
    {
        _tick += ticks;
        service.OnPostThink(0.015f);
        if (service.Worker.Running)
            Assert.True(service.Worker.Settle());
    }

    private ExperienceService Playing(string db = Db, string learn = "1", IExperienceHolds? holds = null)
    {
        ExperienceService service = Service(db, learn, holds: holds);
        service.OnLevelInitPostEntity("CTF_Test");
        return service;
    }

    /// <summary>Everything merged under <see cref="Key"/>, summed.</summary>
    private ExperienceDelta Merged()
    {
        var all = new ExperienceDelta();
        foreach ((ExperienceKey key, ExperienceDelta delta) in _stores.Merged)
        {
            Assert.Equal(Key, key);
            all.Add(delta);
        }

        return all;
    }

    [Fact]
    public void AGameThatKeepsNoneOpensNothingAndStartsNoThread()
    {
        _game.Keeps = false;
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Playing();
        Frame(service);
        Frame(service);
        Assert.False(service.Active);
        Assert.False(service.Worker.Running);
        Assert.Empty(_stores.Calls);
        Assert.True(service.Slot.Current.IsEmpty);
        Assert.Contains(_log.All, l => l.Line.Contains("experience: off (this game keeps none)", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyPathIsOffAndOpensNothing()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Playing(db: "");
        Frame(service);
        Assert.False(service.Active);
        Assert.False(service.Worker.Running);
        Assert.Empty(_stores.Calls);
        Assert.Contains(_log.All, l => l.Line.Contains("experience: off (ai_experience_db is empty)", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLevelsFirstSampleOpensTheFileAndLoadsTheKeyOfTheMapItsChecksumAndTheMode()
    {
        ExperienceService service = Playing();
        Assert.True(service.Active);
        Assert.Empty(_stores.Calls);
        Frame(service);
        Assert.Equal("open " + Db, _stores.Calls.First().Call);
        Assert.Equal(Key, Assert.Single(_stores.Loaded));
    }

    [Fact]
    public void TheLoadedSnapshotIsInTheSlotAtTheNextFrame()
    {
        var seeded = new ExperienceDelta();
        seeded.Add(new CellKey(new ExperienceContext(0, 2, 3), new WorldBin(1, 1, 0), ExperienceSource.Human), new CellStat(9d, 0d, 0d, 0d, 0d));
        _stores.Kept.Merge(Key, seeded, 20d);
        ExperienceService service = Playing();
        Frame(service);
        Assert.True(service.Slot.Current.IsEmpty);
        Frame(service);
        Assert.Equal(Key, service.Slot.Current.Key);
        Assert.Equal(9d, service.Slot.Current.For(ExperienceContext.All).Traffic(new WorldBin(1, 1, 0)));
        Assert.Contains(_log.All, l => l.Line.Contains("experience: ctf_test ctf: 1 rows over 0 rounds", StringComparison.Ordinal));
    }

    /// <summary>
    /// Lane G4a: with a graph, a snapshot goes to the brains only with the routes built from it, which are built on a
    /// task and not on the game's thread: the deaths of red's players in bin (1, 1) cost the cells under it.
    /// </summary>
    [Fact]
    public void WithAGraphTheSnapshotGoesToTheBrainsWithItsRoutes()
    {
        var (graph, _) = SourceSharp.Ai.Tests.Nav.Grids.Parse("......", "......", "......", "......", "......", "......");
        _nav = new GraphNavigation(graph);
        var seeded = new ExperienceDelta();
        seeded.Add(new CellKey(new ExperienceContext(0, 2, 3), new WorldBin(1, 1, 0), ExperienceSource.Human), new CellStat(10d, 1d, 0d, 0d, 0d));
        _stores.Kept.Merge(Key, seeded, 20d);
        ExperienceService service = Playing();
        Frame(service);
        for (int i = 0; i < 50 && service.Routes.Current.Roles.Count == 0; i++)
        {
            Frame(service);
            // Never the snapshot without its routes.
            Assert.True(service.Slot.Current.IsEmpty || ReferenceEquals(service.Routes.Current.Of, service.Slot.Current));
            service.RouteWork?.Wait();
        }

        Assert.Equal(Key, service.Slot.Current.Key);
        Assert.Same(service.Slot.Current, service.Routes.Current.Of);
        Assert.Same(graph, service.Routes.Current.Graph);
        // Bin (1, 1) holds the four 32 u cells from (64, 64) to (128, 128).
        Assert.Equal(4, service.Routes.Current.For(2)!.Costed);
        Assert.Null(service.Routes.Current.For(3));
        Assert.Equal(1, service.RouteBuilds);
        Assert.Contains(_log.All, l => l.Line.Contains("experience: routes for 1 side(s), 4 cells costed", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutAGraphTheSnapshotGoesAtOnceWithNoRoutes()
    {
        var seeded = new ExperienceDelta();
        seeded.Add(new CellKey(new ExperienceContext(0, 2, 3), new WorldBin(1, 1, 0), ExperienceSource.Human), new CellStat(10d, 1d, 0d, 0d, 0d));
        _stores.Kept.Merge(Key, seeded, 20d);
        ExperienceService service = Playing();
        Frame(service);
        Frame(service);
        Assert.Equal(Key, service.Slot.Current.Key);
        Assert.Empty(service.Routes.Current.Roles);
        Assert.Null(service.RouteWork);
    }

    [Fact]
    public void TheLevelsEndEmptiesTheRoutes()
    {
        var (graph, _) = SourceSharp.Ai.Tests.Nav.Grids.Parse("......", "......", "......", "......");
        _nav = new GraphNavigation(graph);
        var seeded = new ExperienceDelta();
        seeded.Add(new CellKey(new ExperienceContext(0, 2, 3), new WorldBin(1, 1, 0), ExperienceSource.Human), new CellStat(10d, 1d, 0d, 0d, 0d));
        _stores.Kept.Merge(Key, seeded, 20d);
        ExperienceService service = Playing();
        for (int i = 0; i < 50 && service.Routes.Current.Roles.Count == 0; i++)
        {
            Frame(service);
            service.RouteWork?.Wait();
        }

        Assert.NotEmpty(service.Routes.Current.Roles);
        service.OnLevelShutdownPreEntity();
        Assert.Empty(service.Routes.Current.Roles);
        Assert.True(service.Slot.Current.IsEmpty);
    }

    /// <summary>The rule (bold in the lane's brief): no store call and no accumulation on the game thread.</summary>
    [Fact]
    public void NoStoreCallAndNoAccumulationRunsOnTheGameThread()
    {
        int game = Environment.CurrentManagedThreadId;
        SenseWorld.Player human = _world.Add(2, new AiVector(100f, 100f, 0f)), other = _world.Add(3, new AiVector(700f, 100f, 0f));
        ExperienceService service = Playing();
        for (int i = 0; i < 5; i++)
            Frame(service);
        service.OnPlayerHurt(new PlayerHurt(human.Handle, other.Handle, 30, 70));
        service.OnPlayerDied(new PlayerDeath(human.Handle, 2, other.Handle, new Vector(100f, 100f, 0f), EntityHandle.None));
        _game.RoundOver = true;
        Frame(service);
        _game.RoundOver = false;
        Frame(service);
        service.OnLevelShutdownPreEntity();

        string[] calls = [.. _stores.Calls.Select(c => c.Call.Split(' ')[0]).Distinct().Order()];
        Assert.Equal(["counts", "dispose", "load", "merge", "open"], calls);
        Assert.DoesNotContain(_stores.Calls, c => c.Thread == game);
        Assert.Single(_stores.Calls.Select(c => c.Thread).Distinct());
        Assert.True(service.Worker.Frames >= 5);
        Assert.Equal(2, service.Worker.Events);
        Assert.Equal(0, service.Worker.RanOnOwner);
        Assert.Equal(0, service.Worker.Faults);
        Assert.Equal(0, service.Worker.Dropped);
        // And the log is the game thread's alone.
        Assert.NotEmpty(_log.All);
        Assert.All(_log.All, l => Assert.Equal(game, l.Thread));
    }

    [Fact]
    public void ASampledFrameIsEachLivingPlayersSecondsUnderItsContext()
    {
        SenseWorld.Player human = _world.Add(2, new AiVector(100f, 100f, 0f), yaw: 90f), bot = _world.Add(3, new AiVector(700f, 100f, 80f)), dead = _world.Add(4, new AiVector(0f, 0f, 0f));
        dead.Alive = false;
        _bots.Add(bot.Handle);
        _game.Stage = 2;
        _game.Players[human.Handle] = (2, 3);
        _game.Players[bot.Handle] = (3, 7);
        ExperienceService service = Playing();
        Frame(service);
        for (int i = 0; i < 10; i++)
            Frame(service);
        service.OnLevelShutdownPreEntity();

        ExperienceDelta delta = Merged();
        double seconds = 10 * ExperienceService.SampleTicks * 0.015d;
        Assert.Equal(2, delta.Cells.Count);
        Assert.Equal(seconds, delta.Cells[new CellKey(new ExperienceContext(2, 2, 3), new WorldBin(1, 1, 0), ExperienceSource.Human)].Seconds, 6);
        Assert.Equal(seconds * 0.25d, delta.Cells[new CellKey(new ExperienceContext(2, 3, 7), new WorldBin(10, 1, 1), ExperienceSource.Bot)].Seconds, 6);
        Assert.Equal(seconds, delta.Looks[new LookKey(new ExperienceContext(2, 2, 3), new WorldBin(1, 1, 0), 4, 1)].Count, 6);
    }

    [Fact]
    public void APlayerOnNoSideIsNotSampled()
    {
        SenseWorld.Player watcher = _world.Add(2, new AiVector(100f, 100f, 0f));
        _game.Players[watcher.Handle] = (0, 0);
        ExperienceService service = Playing();
        for (int i = 0; i < 3; i++)
            Frame(service);
        service.OnLevelShutdownPreEntity();
        Assert.Empty(_stores.Merged);
    }

    [Fact]
    public void AHitAndADeathReachTheDeltaWithWhereBothStood()
    {
        SenseWorld.Player victim = _world.Add(2, new AiVector(100f, 100f, 0f)), killer = _world.Add(3, new AiVector(700f, 100f, 0f));
        _game.Players[victim.Handle] = (2, 3);
        _game.Players[killer.Handle] = (3, 7);
        _game.Weapon = 9;
        ExperienceService service = Playing();
        Frame(service);
        service.OnPlayerHurt(new PlayerHurt(victim.Handle, killer.Handle, 30, 70));
        service.OnPlayerDied(new PlayerDeath(victim.Handle, 2, killer.Handle, new Vector(130f, 100f, 0f), EntityHandle.None));
        service.OnLevelShutdownPreEntity();

        ExperienceDelta delta = Merged();
        var red = new ExperienceContext(0, 2, 3);
        var blue = new ExperienceContext(0, 3, 7);
        Assert.Equal(new CellStat(0d, 0d, 0d, 30d, 0d), delta.Cells[new CellKey(red, new WorldBin(1, 1, 0), ExperienceSource.Human)]);
        // The death is where the victim died, which the death says, not where the player entity is now.
        Assert.Equal(new CellStat(0d, 1d, 0d, 0d, 0d), delta.Cells[new CellKey(red, new WorldBin(2, 1, 0), ExperienceSource.Human)]);
        Assert.Equal(new CellStat(0d, 0d, 1d, 0d, 30d), delta.Cells[new CellKey(blue, new WorldBin(10, 1, 0), ExperienceSource.Human)]);
        Assert.Equal(new KeyValuePair<SightKey, Tally>(new SightKey(red, new WorldBin(2, 1, 0), new WorldBin(10, 1, 0), 9), new Tally(1d)), Assert.Single(delta.Sights));
    }

    [Fact]
    public void ADeathToSomethingThatIsNoPlayerHasNoKiller()
    {
        SenseWorld.Player victim = _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Playing();
        Frame(service);
        service.OnPlayerDied(new PlayerDeath(victim.Handle, 2, new EntityHandle((uint)(200 | (3 << ActorDirectory.EntryBits))), new Vector(100f, 100f, 0f), EntityHandle.None));
        service.OnPlayerDied(new PlayerDeath(victim.Handle, 2, victim.Handle, new Vector(100f, 100f, 0f), EntityHandle.None));
        service.OnLevelShutdownPreEntity();

        ExperienceDelta delta = Merged();
        Assert.Empty(delta.Sights);
        Assert.Equal(new CellStat(0d, 2d, 0d, 0d, 0d), Assert.Single(delta.Cells).Value);
    }

    /// <summary>The content hash of what a server merged when the player who did everything sat in <paramref name="slot"/>.</summary>
    private ulong PlayedFrom(int slot)
    {
        var fact = new ExperienceServiceTests();
        try
        {
            SenseWorld.Player player = fact._world.Add(slot, new AiVector(100f, 100f, 0f), yaw: 45f), other = fact._world.Add(slot + 1, new AiVector(700f, 100f, 0f));
            ExperienceService service = fact.Playing();
            for (int i = 0; i < 6; i++)
                fact.Frame(service);
            service.OnPlayerHurt(new PlayerHurt(player.Handle, other.Handle, 30, 70));
            service.OnPlayerDied(new PlayerDeath(player.Handle, slot, other.Handle, new Vector(100f, 100f, 0f), EntityHandle.None));
            fact._game.RoundOver = true;
            fact.Frame(service);
            service.OnLevelShutdownPreEntity();
            return new MapExperience(Key, 0, fact.Merged()).ContentHash;
        }
        finally
        {
            fact.Dispose();
        }
    }

    /// <summary>No name, id or slot reaches a delta: the same play from another slot and handle merges the same rows.</summary>
    [Fact]
    public void WhoAPlayerIsDoesNotChangeWhatIsMerged()
    {
        ulong from2 = PlayedFrom(2);
        Assert.NotEqual(new MapExperience(Key, 0, new ExperienceDelta()).ContentHash, from2);
        Assert.Equal(from2, PlayedFrom(9));
    }

    [Fact]
    public void ARoundsEndFlushesItsRowAndTheNextRoundsStartReloadsTheSnapshot()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Playing();
        for (int i = 0; i < 4; i++)
            Frame(service);
        Assert.Empty(_stores.Merged);
        Assert.Single(_stores.Loaded);
        Assert.True(service.Slot.Current.IsEmpty);

        _game.RoundOver = true;
        _game.WinningRole = 3;
        Frame(service);
        ExperienceDelta flushed = Assert.Single(_stores.Merged).Delta;
        Assert.Equal(3, Assert.Single(flushed.Rounds).WinningRole);
        Assert.Single(_stores.Loaded);
        Frame(service);
        // Written, and not read back while the round is over.
        Assert.True(service.Slot.Current.IsEmpty);

        _game.RoundOver = false;
        Frame(service);
        Assert.Equal(2, _stores.Loaded.Count);
        Frame(service);
        Assert.Equal(1, service.Slot.Current.Rounds);
        Assert.True(service.Slot.Current.For(ExperienceContext.All).Traffic(new WorldBin(1, 1, 0)) > 0d);
    }

    [Fact]
    public void WhatIsGatheredIsFlushedEveryMinuteAndNeverReadBackMidRound()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Playing();
        Frame(service);
        Frame(service);
        MapExperience atStart = service.Slot.Current;
        // Each sample counts a second at most: sixty of them are the minute.
        for (int i = 0; i < 59; i++)
            Frame(service, ticks: 70);
        Assert.Empty(_stores.Merged);
        Frame(service, ticks: 70);
        Assert.Single(_stores.Merged);
        Frame(service);
        Assert.Single(_stores.Loaded);
        Assert.Same(atStart, service.Slot.Current);
    }

    [Fact]
    public void NothingIsGatheredWhileNoRoundIsPlayed()
    {
        SenseWorld.Player player = _world.Add(2, new AiVector(100f, 100f, 0f)), other = _world.Add(3, new AiVector(700f, 100f, 0f));
        _game.RoundOver = true;
        ExperienceService service = Playing();
        for (int i = 0; i < 5; i++)
            Frame(service);
        service.OnPlayerHurt(new PlayerHurt(player.Handle, other.Handle, 30, 70));
        service.OnPlayerDied(new PlayerDeath(player.Handle, 2, other.Handle, new Vector(100f, 100f, 0f), EntityHandle.None));
        service.OnLevelShutdownPreEntity();
        Assert.Empty(_stores.Merged);
        Assert.Equal(0, service.Worker.Frames + service.Worker.Events);
    }

    [Fact]
    public void WithLearningOffTheSnapshotIsReadAndNothingIsWritten()
    {
        SenseWorld.Player player = _world.Add(2, new AiVector(100f, 100f, 0f)), other = _world.Add(3, new AiVector(700f, 100f, 0f));
        ExperienceService service = Playing(learn: "0");
        for (int i = 0; i < 5; i++)
            Frame(service);
        service.OnPlayerDied(new PlayerDeath(player.Handle, 2, other.Handle, new Vector(100f, 100f, 0f), EntityHandle.None));
        _game.RoundOver = true;
        Frame(service);
        _game.RoundOver = false;
        Frame(service);
        service.OnLevelShutdownPreEntity();
        Assert.Empty(_stores.Merged);
        Assert.Equal(2, _stores.Loaded.Count);
        Assert.Equal(0, service.Worker.Frames + service.Worker.Events);
        Assert.Contains(_log.All, l => l.Line.Contains("read only: ai_experience_learn 0", StringComparison.Ordinal));
    }

    [Fact]
    public void ALevelsEndFlushesClosesTheStoreStopsTheThreadAndEmptiesTheSlot()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Playing();
        for (int i = 0; i < 4; i++)
            Frame(service);
        Assert.True(service.Slot.Current.Key.IsValid);
        Assert.Empty(_stores.Merged);
        service.OnLevelShutdownPreEntity();
        Assert.Single(_stores.Merged);
        Assert.Equal(1, _stores.Disposed);
        Assert.False(service.Worker.Running);
        Assert.False(service.Active);
        Assert.True(service.Slot.Current.IsEmpty);
        Assert.Equal(ExperienceKey.None, service.Slot.Current.Key);
    }

    [Fact]
    public void AModeSwitchedUnderTheLevelFlushesTheOldKeyAndOpensTheNew()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Playing();
        for (int i = 0; i < 4; i++)
            Frame(service);
        _game.Mode = "koth";
        Frame(service);
        Assert.Equal(Key, Assert.Single(_stores.Merged).Key);
        Assert.Equal([Key, Key with { Mode = "koth" }], _stores.Loaded);
        // One file: it is not opened again.
        Assert.Single(_stores.Calls, c => c.Call.StartsWith("open", StringComparison.Ordinal));
    }

    [Fact]
    public void ARefusedFileIsSaidOnceAndTheServerLearnsInMemory()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        _stores.Refuse = Db + " cannot be used as the experience store: it is garbage";
        ExperienceService service = Playing();
        for (int i = 0; i < 4; i++)
            Frame(service);
        Assert.True(service.Worker.Status!.MemoryOnly);
        Assert.Contains("\"state\":\"memory\"", service.Json(), StringComparison.Ordinal);
        _game.RoundOver = true;
        Frame(service);
        _game.RoundOver = false;
        Frame(service);
        Frame(service);
        // The refused store was closed with nothing asked of it; the round is in memory, and the brains read it.
        Assert.Equal(["open", "dispose"], _stores.Calls.Select(c => c.Call.Split(' ')[0]));
        Assert.Equal(1, service.Slot.Current.Rounds);

        service.OnLevelShutdownPreEntity();
        service.OnLevelInitPostEntity("CTF_Test");
        for (int i = 0; i < 3; i++)
            Frame(service);
        (string line, int _, bool warning) = Assert.Single(_log.All, l => l.Line.Contains("experience refused", StringComparison.Ordinal));
        Assert.True(warning);
        Assert.Contains("it is garbage; this server keeps it in memory only", line, StringComparison.Ordinal);
        Assert.Equal(1, service.Refusals);
    }

    /// <summary>
    /// G.11's ruling (2026-11-16): a file another process held locked as it opened is retried, not refused. The game goes on
    /// with the empty snapshot, its round kept (no merge, no failure), says the file is busy; once the store attaches (here
    /// while no round is played, so only the game's look at the waiting store notices it), the file's snapshot reaches the
    /// brains, the route says on, and the kept round is written.
    /// </summary>
    [Fact]
    public void ABusyFileIsSaidRetryingKeepsTheRoundAndWritesItOnceAttached()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        var seeded = new ExperienceDelta();
        seeded.Add(new RoundRow(60d, 1, 1d, 0d, "seed"));
        _stores.Kept.Merge(Key, seeded, 20d);
        _stores.Wait = Db + " is busy (another process held it locked past 2000 ms as it opened); retrying every 1000 ms";
        ExperienceService service = Playing();
        for (int i = 0; i < 4; i++)
            Frame(service);
        Assert.Contains("\"state\":\"waiting\"", service.Json(), StringComparison.Ordinal);
        Assert.Contains("\"ai.experience.waiting\":1,", service.Counters(), StringComparison.Ordinal);
        Assert.False(service.Worker.Status!.MemoryOnly);
        Assert.Null(service.Worker.Status.Refusal);
        _game.RoundOver = true;
        Frame(service);
        Frame(service);
        // The brains read the empty snapshot; the round is kept, not merged and not a failure.
        Assert.Equal(0, service.Slot.Current.Rounds);
        Assert.DoesNotContain(_stores.Calls, c => c.Call == "merge");
        Assert.Equal(0, service.Worker.MergeFailures);
        (string busy, int _, bool warning) = Assert.Single(_log.All, l => l.Line.Contains(" is busy ", StringComparison.Ordinal));
        Assert.False(warning);
        Assert.Contains("the game goes on without it until it attaches", busy, StringComparison.Ordinal);
        Assert.DoesNotContain(_log.All, l => l.Line.Contains("experience refused", StringComparison.Ordinal));

        _stores.Wait = null;
        // No round is played, so nothing is posted but the game's look at the waiting store: that alone must notice it.
        _tick += ExperienceService.SampleTicks;
        service.OnPostThink(0.015f);
        Assert.True(SpinWait.SpinUntil(() => service.Worker.Attaches == 1, TimeSpan.FromSeconds(10)), "the attach was never noticed");
        Frame(service);
        Assert.Equal(1, service.Worker.Attaches);
        Assert.Equal(1, service.Slot.Current.Rounds);
        Assert.Contains(_log.All, l => l.Line.Contains($"experience: {Db} attached", StringComparison.Ordinal));
        Assert.Contains("\"state\":\"on\"", service.Json(), StringComparison.Ordinal);
        Assert.Contains("\"ai.experience.waiting\":0,\"ai.experience.attaches\":1,", service.Counters(), StringComparison.Ordinal);

        service.OnLevelShutdownPreEntity();
        Assert.Single(Merged().Rounds);
        Assert.Equal(0, service.Worker.MergeFailures);
        Assert.Equal(0, service.Refusals);
    }

    [Fact]
    public void AModWithNoDatabaseAssemblyLearnsInMemoryAndSaysSo()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Service(noStores: true);
        service.OnLevelInitPostEntity("CTF_Test");
        for (int i = 0; i < 3; i++)
            Frame(service);
        Assert.True(service.Worker.Status!.MemoryOnly);
        Assert.Contains(_log.All, l => l.Warning && l.Line.Contains("this mod lists no experience database", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRouteSaysTheStateThePathTheKeyTheRowsAndTheSnapshot()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService service = Playing();
        Assert.Contains("\"state\":\"on\"", service.Json(), StringComparison.Ordinal);
        for (int i = 0; i < 4; i++)
            Frame(service);
        _game.RoundOver = true;
        Frame(service);
        string json = service.Json();
        foreach (string part in new[]
                 {
                     "\"state\":\"on\"", $"\"path\":\"{Db}\"", "\"map\":\"ctf_test\"", $"\"checksum\":\"{Checksum}\"", "\"mode\":\"ctf\"", "\"rounds\":1",
                     "\"cell_stat\":1", "\"look\":1", "\"round\":1", "\"flushes\":1", "\"refusal\":\"\"", "\"learn\":true", "\"half_life\":20",
                 })
            Assert.Contains(part, json, StringComparison.Ordinal);
        Assert.Contains("\"ai.experience.on\":1,", service.Counters(), StringComparison.Ordinal);
        Assert.Contains("\"ai.experience.flushes\":1,", service.Counters(), StringComparison.Ordinal);
        Assert.Contains("\"ai.experience.main_thread_work\":0,", service.Counters(), StringComparison.Ordinal);
        Assert.EndsWith(",", service.Counters(), StringComparison.Ordinal);
    }

    [Fact]
    public void OffTheRouteSaysWhy()
    {
        ExperienceService service = Playing(db: "");
        Assert.Contains("\"state\":\"off\"", service.Json(), StringComparison.Ordinal);
        Assert.Contains("\"off\":\"ai_experience_db is empty\"", service.Json(), StringComparison.Ordinal);
        Assert.Contains("\"ai.experience.on\":0,", service.Counters(), StringComparison.Ordinal);
    }

    private sealed class Quiet : ILog
    {
        public void Msg(string message)
        {
        }

        public void Warning(string message)
        {
        }
    }

    /// <summary>A game with nothing to know: the session only has to build its world.</summary>
    private sealed class PlainGame : SourceSharp.Ai.Server.IBotGame
    {
        public IDecider Decider(BrainSetup setup) => throw new InvalidOperationException("no bot decides in these facts");
        public WeaponKnowledge Weapons() => new([]);
        public AbilityTable Abilities => default;
        public MapKnowledge Map(string mapName, MapEntities entities, WeaponKnowledge weapons) => MapKnowledge.Empty(weapons);
        public KeyValuePair<string, string>[] UserInfo(AiRandom random, int team, int teams) => [];
        public string GrenadeWeapon => "";
        public bool Thrown(in WeaponType weapon) => false;
        public IBotGameView View { get; } = new PlainBotGameView();
    }

    [Fact]
    public void TheLevelsWorldHoldsTheFeedsSlot()
    {
        ExperienceService service = Playing();
        IEnginePlayers nobody = FakeEngine.Of<IEnginePlayers>((m, _) => m.Name == "GetHandle" ? EntityHandle.None : throw FakeEngine.Unasked(m));
        var session = new ServerAiSession(new NoNavigation(), new PlainGame(), nobody, new Quiet(), DeclaredConVars.Fresh<AiThreadsConVar>(),
                                          DeclaredConVars.FreshAt<AiSyncConVar>("1"), null, service);
        session.OnLevelInitPostEntity("CTF_Test");
        try
        {
            Assert.Same(service.Slot, session.Session.World!.ExperienceSlot);
            var fresh = new MapExperience(Key, 4, new ExperienceDelta());
            service.Slot.Current = fresh;
            Assert.Same(fresh, session.Session.World.Experience);
        }
        finally
        {
            session.OnShutdown();
        }
    }

    [Fact]
    public void ASessionWithNoFeedHoldsTheEmptyExperience()
    {
        IEnginePlayers nobody = FakeEngine.Of<IEnginePlayers>((m, _) => m.Name == "GetHandle" ? EntityHandle.None : throw FakeEngine.Unasked(m));
        var session = new ServerAiSession(new NoNavigation(), new PlainGame(), nobody, new Quiet(), DeclaredConVars.Fresh<AiThreadsConVar>(),
                                          DeclaredConVars.FreshAt<AiSyncConVar>("1"));
        session.OnLevelInitPostEntity("dm_test");
        try
        {
            Assert.True(session.Session.World!.Experience.IsEmpty);
        }
        finally
        {
            session.OnShutdown();
        }
    }

    /// <summary>
    /// The feed reads where every player is (the perception census names it for that). What it
    /// reads goes to its worker and nowhere else: only the feed posts to the worker, and of the
    /// feed the rest of the binding touches the slot (the session, for the level's world), the
    /// route's two reports, and the two ways a game's own result comes in: TF2's jumps hand it a
    /// link's try (lane G5e), and TF2's bots' tries of a way into the enemy's base (lane G4b), which
    /// the feed posts on as it posts the rest; and the jump watch (lane G6a), which hands it the
    /// jumps players showed, asks whether it gathers and reads its counters and snapshot; and TF2's jumps read the
    /// snapshot for the jumps players showed (lane G6b), to fit them as links.
    /// </summary>
    [Fact]
    public void WhatTheFeedReadsGoesOnlyToItsWorker()
    {
        System.Reflection.Assembly[] binding = [typeof(ExperienceService).Assembly, typeof(SourceSharp.Ai.Hl2mp.Hl2mpBotGame).Assembly, typeof(SourceSharp.Ai.TF2.TFBotGame).Assembly];
        string feed = typeof(ExperienceService).FullName!, worker = typeof(ExperienceWorker).FullName!;
        string[] posters = [.. IlCallCensus.Callers(m => m.DeclaringType == typeof(ExperienceWorker), binding).Where(c => !c.StartsWith(worker, StringComparison.Ordinal))];
        Assert.Equal([feed], posters);
        string[] ofTheFeed = [.. IlCallCensus.Calls(m => m.DeclaringType == typeof(ExperienceService), binding).Where(c => c.Caller != feed)
                                             .Select(c => $"{c.Caller}::{c.Callee}").Distinct().Order(StringComparer.Ordinal)];
        string jumps = typeof(ShownJumpFeed).FullName!;
        Assert.Equal([$"{jumps}::NoteShownJump", $"{jumps}::RecentShownJumps", $"{jumps}::ShownJumpVerdicts", $"{jumps}::get_FeedMicros", $"{jumps}::get_Gathering",
                      $"{jumps}::get_ShownJumpsNoted", $"{jumps}::get_ShownJumpsUnjudged", $"{jumps}::get_Slot",
                      "SourceSharp.Ai.Server.Senses.AiBotRoutes::Counters", "SourceSharp.Ai.Server.Senses.AiBotRoutes::Json", "SourceSharp.Ai.Server.Senses.ServerAiSession::get_Slot",
                      "SourceSharp.Ai.TF2.Jump.TFJumps::NoteLink", "SourceSharp.Ai.TF2.Jump.TFJumps::get_Slot", "SourceSharp.Ai.TF2.Nav.TFExperienceOutcomes::NoteOutcome"], ofTheFeed);
    }

    [Fact]
    public void AnEmptyValueIsOff()
    {
        Assert.Null(ExperienceDbPath.Resolve("", "/home/u/.local/share", toolGame: false));
        Assert.Null(ExperienceDbPath.Resolve("  ", "/home/u/.local/share", toolGame: false));
    }

    [Fact]
    public void TheDefaultIsAFileInTheUsersDataDirectory()
    {
        Assert.Equal(Path.Combine("/home/u/.local/share", "sourcesharp", "ai-experience.db"), ExperienceDbPath.Resolve("default", "/home/u/.local/share", toolGame: false));
        Assert.Equal(Path.Combine("/home/u/.local/share", "sourcesharp", "ai-experience.db"),
                     ExperienceDbPath.Resolve(DeclaredConVars.Fresh<AiExperienceDbConVar>().Value, "/home/u/.local/share", toolGame: false));
    }

    [Fact]
    public void TheDefaultWithNoUserDataDirectoryIsOff()
    {
        Assert.Null(ExperienceDbPath.Resolve("default", "", toolGame: false));
    }

    [Fact]
    public void APathIsTakenAsGiven()
    {
        Assert.Equal("/srv/tf2/experience.db", ExperienceDbPath.Resolve("/srv/tf2/experience.db", "/home/u/.local/share", toolGame: false));
        Assert.Equal("/srv/tf2/experience.db", ExperienceDbPath.Resolve("/srv/tf2/experience.db", "", toolGame: false));
    }

    /// <summary>Tool launches never touch the default file (G.3): in a tool's game the default is off.</summary>
    [Fact]
    public void InAToolsGameTheDefaultIsOff()
    {
        Assert.NotNull(ExperienceDbPath.Resolve("default", "/home/u/.local/share", toolGame: false));
        Assert.Null(ExperienceDbPath.Resolve("default", "/home/u/.local/share", toolGame: true));
        Assert.Null(ExperienceDbPath.Resolve("DEFAULT", "/home/u/.local/share", toolGame: true));
    }

    [Fact]
    public void InAToolsGameAPathOfItsOwnIsUsed()
    {
        Assert.Equal("/tmp/lane/experience.db", ExperienceDbPath.Resolve("/tmp/lane/experience.db", "/home/u/.local/share", toolGame: true));
    }

    [Fact]
    public void AToolsGameIsOneWhoseCommandLineCarriesTheLaunchersSwitch()
    {
        Assert.True(ExperienceDbPath.IsToolGame(["hl2_linux64", "-game", "x", "-toolgame"]));
        Assert.True(ExperienceDbPath.IsToolGame(["hl2_linux64", "-ToolGame"]));
        Assert.False(ExperienceDbPath.IsToolGame(["hl2_linux64", "-game", "x", "-debugapi"]));
        Assert.False(ExperienceDbPath.IsToolGame(["hl2_linux64", "+ai_experience_db", "-toolgames"]));
    }

    /// <summary>The same server, the default file, started by a tool: off, nothing opened, and the log says how to use a store.</summary>
    [Fact]
    public void AToolsGameOnTheDefaultFileOpensNothing()
    {
        _world.Add(2, new AiVector(100f, 100f, 0f));
        ExperienceService played = Service(db: "default");
        played.OnLevelInitPostEntity("CTF_Test");
        Assert.True(played.Active);
        Assert.EndsWith(Path.Combine("sourcesharp", "ai-experience.db"), played.Path, StringComparison.Ordinal);
        // Stopped before its first sample: this fact must not open the box's own file either.
        played.OnLevelShutdownPreEntity();
        Assert.Empty(_stores.Calls);

        ExperienceService tool = Service(db: "default", commandLine: "-toolgame");
        tool.OnLevelInitPostEntity("CTF_Test");
        Frame(tool);
        Frame(tool);
        Assert.False(tool.Active);
        Assert.False(tool.Worker.Running);
        Assert.Empty(_stores.Calls);
        Assert.Contains(_log.All, l => l.Line.Contains("experience: off (a tool's game (-toolgame) uses no default file", StringComparison.Ordinal));
    }

    [Fact]
    public void AToolsGameWithAPathOfItsOwnUsesIt()
    {
        ExperienceService tool = Service(db: Db, commandLine: "-toolgame");
        tool.OnLevelInitPostEntity("CTF_Test");
        Frame(tool);
        Assert.True(tool.Active);
        Assert.Equal("open " + Db, _stores.Calls.First().Call);
    }

    [Fact]
    public void TheConVarsDefaultToTheUsersFileAHalfLifeOfTwentyRoundsAndLearning()
    {
        Assert.Equal("default", DeclaredConVars.Fresh<AiExperienceDbConVar>().Value);
        Assert.Equal(20f, DeclaredConVars.Fresh<AiExperienceHalfLifeConVar>().Value);
        Assert.True(DeclaredConVars.Fresh<AiExperienceLearnConVar>().Value);
    }
}
