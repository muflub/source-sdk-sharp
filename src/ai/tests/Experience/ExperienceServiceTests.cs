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
public sealed class ExperienceServiceTests : IDisposable
{
    private const string Db = "/nowhere/experience.db";
    private static readonly byte[] Bsp = [1, 2, 3, 4, 5];
    private static readonly string Checksum = Convert.ToHexStringLower(SHA256.HashData(Bsp))[..16];
    private static readonly ExperienceKey Key = new("ctf_test", Checksum, "ctf");

    private readonly SenseWorld _world = new();
    private readonly TestGlobals _globals = new(maxClients: 16);
    private readonly Game _game = new();
    private readonly SpyStores _stores = new();
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
        public int Disposed;

        public IExperienceStore Open(string path)
        {
            Calls.Enqueue(("open " + path, Environment.CurrentManagedThreadId));
            return new Spy(this);
        }

        private sealed class Spy(SpyStores owner) : IExperienceStore
        {
            public string? Refusal => owner.Refuse;

            public MapExperience Load(ExperienceKey key, double halfLife)
            {
                owner.Calls.Enqueue(("load", Environment.CurrentManagedThreadId));
                owner.Loaded.Enqueue(key);
                return owner.Kept.Load(key, halfLife);
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

    private ExperienceService Service(string db = Db, string learn = "1", IExperienceStores? stores = null, bool noStores = false, params string[] commandLine)
    {
        IEnginePlayers players = _world.Engine();
        IBotPlayers bots = FakeEngine.Of<IBotPlayers>((m, a) => m.Name == "IsBot" ? _bots.Contains((EntityHandle)a[0]!) : throw FakeEngine.Unasked(m));
        IEngineFrame frame = FakeEngine.Of<IEngineFrame>((m, _) => m.Name == "TickCount" ? _tick : throw FakeEngine.Unasked(m));
        IEngineFiles files = FakeEngine.Of<IEngineFiles>((m, a) => m.Name == "ReadGameFileBytes" && (string)a[0]! == "maps/CTF_Test.bsp" ? Bsp : throw FakeEngine.Unasked(m));
        var service = new ExperienceService(_game, new NoNavigation(), players, _world.Entities(), bots, new ActorDirectory(players, _globals), frame, files, _log,
                                            DeclaredConVars.FreshAt<AiExperienceDbConVar>(db), DeclaredConVars.Fresh<AiExperienceHalfLifeConVar>(),
                                            DeclaredConVars.FreshAt<AiExperienceLearnConVar>(learn), new CommandLine(["hl2_linux64", .. commandLine]),
                                            noStores ? null : stores ?? _stores);
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

    private ExperienceService Playing(string db = Db, string learn = "1")
    {
        ExperienceService service = Service(db, learn);
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
    /// feed the rest of the binding touches the slot (the session, for the level's world) and the
    /// route's two reports.
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
        Assert.Equal(["SourceSharp.Ai.Server.Senses.AiBotRoutes::Counters", "SourceSharp.Ai.Server.Senses.AiBotRoutes::Json", "SourceSharp.Ai.Server.Senses.ServerAiSession::get_Slot"], ofTheFeed);
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
