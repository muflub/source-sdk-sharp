using System.Reflection;
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
/// A human reference session's taps (plan_hl2mp_ai §3.4.4): every player's
/// usercmd, a person's and a bot's, through the core's
/// <see cref="IPlayerCommandObserver"/> with the server's tick; a person's
/// sight by the bots' own vision pass, written as Observed records and never
/// pushed to a brain, without touching the bots' trace budget; every living
/// actor's frame each tick. Nothing of it while nothing records.
/// </summary>
public sealed class HumanReferenceTests : IDisposable
{
    private const float TickSeconds = 0.015f;
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"ss-human-ref-{Guid.NewGuid():N}.airec");

    public void Dispose() => File.Delete(_file);

    private sealed class NoProfiles : IBotProfileSource
    {
        public byte[]? Read(string path) => null;
    }

    private sealed class Lines : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private sealed class Players : IBotPlayers
    {
        public readonly List<BotPlayer> Made = new();

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal)
        {
            int index = Made.Count + 3;
            var player = new EntityHandle((uint)(0x0001_0000 * index + index));
            Made.Add(new BotPlayer(player, index, name));
            refusal = "";
            return player;
        }

        public bool IsBot(EntityHandle player) => Made.Any(b => b.Player == player);

        public bool Kick(EntityHandle bot, string reason) => Made.RemoveAll(b => b.Player == bot) > 0;

        public IReadOnlyList<BotPlayer> Bots => Made;
    }

    private sealed class Runner : IPlayerCommandRunner
    {
        public void Queue(EntityHandle player, in UserCmd cmd) { }
        public int RunQueued(EntityHandle player) => 0;
    }

    public class Defaults : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.ReturnType is { IsValueType: true } t && t != typeof(void) ? Activator.CreateInstance(t) : null;
    }

    private sealed class Navigation : IHl2mpNavigation
    {
        public string? Refusal => null;
        public LoadedNav? Current => null;
        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(SourceSharp.Abi.EntityHandle player) => Obstacles;
        public NavSight? Sight => null;

        public ChokeSlot? Chokes => null;
    }

    private sealed class Names : IBotNameSource
    {
        public BotNamePools? Pools { get; } = BotNamePools.Parse(File.ReadAllText(
            SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_bot_names.json")));

        public string Refusal => "";
    }

    private readonly Players _players = new();
    private readonly Hl2mpBots _bots;
    private readonly Hl2mpAiSession _ai;
    private readonly Lines _log = new();

    public HumanReferenceTests()
    {
        _bots = new Hl2mpBots(_players, new Runner(), DispatchProxy.Create<IEnginePlayers, Defaults>(),
                              DispatchProxy.Create<IEngineEntities, Defaults>(), WeaponCatalog.Stock(),
                              DispatchProxy.Create<IEngineFrame, Defaults>(), new Navigation(), DispatchProxy.Create<IBotInventory, Defaults>(),
                              new Names(), new ScoreboardTags(), _log);
        _ai = new Hl2mpAiSession(new Navigation(), WeaponCatalog.Stock(), null!, null!, _log,
            DeclaredConVars.Fresh<AiThreadsConVar>(), DeclaredConVars.Fresh<AiSyncConVar>(),
            DeclaredConVars.Fresh<WeaponRespawnTimeConVar>(), DeclaredConVars.Fresh<ItemRespawnTimeConVar>());
    }

    private static IEngineEntities Indexes() => FakeEngine.Of<IEngineEntities>((m, a) => m.Name switch
    {
        "GetIndex" => (int)((EntityHandle)a[0]!).Value & 0x1FFF,
        _ => throw FakeEngine.Unasked(m),
    });

    private static IEngineFrame AtTick(int tick) => FakeEngine.Of<IEngineFrame>((m, _) => m.Name switch
    {
        "TickCount" => tick,
        _ => throw FakeEngine.Unasked(m),
    });

    private List<(AiRecordType Type, byte[] Payload)> Stopped()
    {
        _ai.Recording.Stop();
        return Record.Recordings.Records(File.ReadAllBytes(_file));
    }

    private static T Read<T>(byte[] payload, Func<BinaryReader, T> read) => read(new BinaryReader(new MemoryStream(payload)));

    private void Start() => _ai.Recording.Start(_file, new AiRecordingHeader(1, "ss_test", 0, 0, VisionPass.Slots, Hl2mpAiSession.SightedCapacity));

    [Fact]
    public void EveryPlayersCommandIsRecordedWithTheServersTick()
    {
        new AiBotAddCommand(new Hl2mpRoster(_bots, new NoProfiles(), new Hl2mp.BotRosterTests.Slots(), _log), _log).RunLine("Alyx hard");
        Hl2mpBot bot = Assert.Single(_bots.Bots);
        var human = new EntityHandle((uint)(0x0005_0000 * 8 + 2));   // slot 2
        var tap = new CommandTap(_ai, _bots, Indexes(), AtTick(4321));
        Start();
        var cmd = new UserCmd { CommandNumber = 9, TickCount = 4300, ForwardMove = 450, SideMove = -450, Buttons = 1, Pitch = 5, Yaw = 123 };
        tap.OnCommand(_players.Made[0].Player, cmd);
        tap.OnCommand(human, cmd);

        AiUserCmd[] cmds = [.. Stopped().Where(r => r.Type == AiRecordType.UserCmd).Select(r => Read(r.Payload, AiRecordFormat.ReadUserCmd))];
        Assert.Equal([bot.Id, ActorDirectory.HumanActor(2, human)], cmds.Select(c => c.Actor));
        Assert.All(cmds, c => Assert.Equal((4321, 4300, 9, 123f, -450f, 1), (c.Tick.Value, c.TickCount, c.CommandNumber, c.View.Yaw, c.SideMove, c.Buttons)));
    }

    [Fact]
    public void NoCommandIsWrittenWhileNothingRecords()
    {
        var tap = new CommandTap(_ai, _bots, Indexes(), AtTick(1));
        tap.OnCommand(new EntityHandle(0x0005_0002), new UserCmd { CommandNumber = 1 });
        Assert.Equal(0, tap.Written);
    }

    /// <summary>A bot at the origin facing +x, a person 500 units on facing it; the world's pass with the reference taps.</summary>
    private sealed class Sight
    {
        public readonly SenseWorld World = new();
        public readonly VisionPass Pass;
        public readonly ReferenceRecorder Reference;
        public readonly SenseWorld.Player Bot, Person;
        public readonly ActorId BotId = new(1, 1);
        public readonly List<AiEvent> Events = new(), Observed = new();
        private int _tick;

        public Sight(Hl2mpAiSession ai)
        {
            Bot = World.Add(1, new AiVector(0, 0, 0));
            Person = World.Add(2, new AiVector(500, 0, 0), yaw: 180f);
            Pass = new VisionPass(World.Engine(), World.Entities(), World.Traces());
            Pass.Add(BotId);
            Reference = new ReferenceRecorder(ai, Pass);
        }

        public ActorId PersonId => ActorDirectory.HumanActor(2, Person.Handle);

        public SensedBot Sensed => new(BotId, Bot.Handle, new VisionParams(6000f, 90f, 0.3f, 0.5f));

        public void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                SensedActor[] living = World.Living(Sensed);
                ReadOnlySpan<SensedBot> observers = Reference.Observers(living, s => s == 1 ? BotId : ActorId.None);
                Observed.Clear();
                var tick = new Tick(++_tick);
                Pass.Run(tick, TickSeconds, [Sensed], living, Events, observers, Observed);
                Reference.Write(tick, living, Observed);
            }
        }
    }

    [Fact]
    public void APersonsSightIsTheVisionPassesWrittenAsObservedAndNeverPushedToABrain()
    {
        var s = new Sight(_ai);
        Start();
        s.Run(30);
        var records = Stopped();

        AiEvent[] observed = [.. records.Where(r => r.Type == AiRecordType.Observed).Select(r => Read(r.Payload, AiRecordFormat.ReadEvent))];
        AiEvent gained = Assert.Single(observed);
        Assert.Equal((AiEventKind.SightGained, s.PersonId, s.BotId, 1), (gained.Kind, gained.Bot, gained.Other, gained.Tick.Value));
        Assert.Contains(s.Events, e => e.Kind == AiEventKind.SightGained && e.Bot == s.BotId);   // the control: the bot saw the person too
        Assert.DoesNotContain(s.Events, e => e.Bot == s.PersonId);
    }

    [Fact]
    public void AWallHidesTheBotFromThePersonAsItHidesThePersonFromTheBot()
    {
        var s = new Sight(_ai);
        s.World.Occluder(new AiVector(250, -200, -10), new AiVector(266, 200, 300), SenseWorld.Solid);
        Start();
        s.Run(30);
        Assert.DoesNotContain(Stopped(), r => r.Type == AiRecordType.Observed);
        Assert.True(s.Pass.ObserverTraces > 0, "the control: the person's lines were traced");
    }

    [Fact]
    public void EveryLivingActorsFrameIsWrittenEachTick()
    {
        var s = new Sight(_ai);
        s.Person.Velocity = new AiVector(-190, 0, 0);
        Start();
        s.Run(10);
        AiActorFrame[] frames = [.. Stopped().Where(r => r.Type == AiRecordType.Frame).Select(r => Read(r.Payload, AiRecordFormat.ReadFrame))];
        Assert.Equal(20, frames.Length);
        AiActorFrame person = frames.Last(f => f.Actor == s.PersonId);
        Assert.Equal((10, new AiVector(500, 0, 0), new AiVector(500, 0, 64), new AiVector(-190, 0, 0)), (person.Tick.Value, person.Origin, person.Eye, person.Velocity));
        Assert.Equal(10, frames.Count(f => f.Actor == s.BotId));
    }

    [Fact]
    public void NoPersonIsObservedWhileNothingRecords()
    {
        var s = new Sight(_ai);
        Start();
        s.Run(2);
        Assert.Equal(s.PersonId, s.Pass.ObserverIn(2));
        _ai.Recording.Stop();
        long traces = s.Pass.ObserverTraces;
        s.Run(20);
        Assert.Equal(ActorId.None, s.Pass.ObserverIn(2));
        Assert.Equal(traces, s.Pass.ObserverTraces);
    }

    /// <summary>
    /// The bots' sight is what it was without the person observed: the same
    /// events at the same ticks, the same traces from their budget, with a
    /// budget too small to serve every pair each tick.
    /// </summary>
    [Fact]
    public void APersonObservedLeavesTheBotsBudgetAndSightAsTheyWere()
    {
        (List<string> Events, long Traces) Bots(bool record)
        {
            var ai = new Hl2mpAiSession(new Navigation(), WeaponCatalog.Stock(), null!, null!, _log,
            DeclaredConVars.Fresh<AiThreadsConVar>(), DeclaredConVars.Fresh<AiSyncConVar>(),
            DeclaredConVars.Fresh<WeaponRespawnTimeConVar>(), DeclaredConVars.Fresh<ItemRespawnTimeConVar>());
            var s = new Sight(ai);
            s.Pass.TracesPerTick = 5;
            for (int i = 0; i < 3; i++)
                s.World.Add(3 + i, new AiVector(300 + 40 * i, 120 * (i - 1), 0));
            if (record)
                ai.Recording.Start(_file, new AiRecordingHeader(1, "ss_test", 0, 0, VisionPass.Slots, Hl2mpAiSession.SightedCapacity));
            s.Run(60);
            ai.Recording.Stop();
            Assert.Equal(record, s.Pass.ObserverTraces > 0);
            return ([.. s.Events.Select(e => $"{e.Kind} {e.Other} {e.Tick}")], s.Pass.Traces);
        }

        (List<string> withPerson, long withTraces) = Bots(record: true);
        (List<string> without, long withoutTraces) = Bots(record: false);
        Assert.True(without.Count >= 3, $"the control: the bot saw {without.Count} times");
        Assert.Equal(without, withPerson);
        Assert.Equal(withoutTraces, withTraces);
    }
    private sealed class Mod(string dir) : IModInfo
    {
        public string Id => "test";
        public string GameDir => dir;
        public string ManagedDir => dir;
        public IReadOnlyList<string> AssembliesFor(Side side) => [];
    }

    private const string PersonName = "Gordon Freeman-4242", SteamId = "STEAM_1:0:12345678";
    private const ulong Steam64 = 76561197984957084UL;

    private static int Find(byte[] hay, byte[] needle) => hay.AsSpan().IndexOf(needle);

    /// <summary>
    /// <c>ai_record human</c> writes the mod's ai-recordings/human/&lt;utc&gt;.airec,
    /// and a person is in it by a random serial only: the bytes hold neither
    /// the person's name nor its Steam id (as text in UTF-8 or UTF-16, or as
    /// a 64-bit number), nor the actor serial the session knew it by.
    /// </summary>
    [Fact]
    public void AHumanRecordingLandsUnderTheModAndHoldsNoNameNoSteamIdAndNoRawActor()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ss-human-mod-{Guid.NewGuid():N}");
        try
        {
            var s = new Sight(_ai);
            var tap = new CommandTap(_ai, _bots, Indexes(), AtTick(10));
            new AiRecordCommand(_ai, s.Pass, AtTick(10), new Mod(dir), _log).RunLine("human");
            for (int i = 0; i < 20; i++)
            {
                s.Run(1);
                tap.OnCommand(s.Person.Handle, new UserCmd { CommandNumber = i, TickCount = i, Yaw = 180, Buttons = 1 });
            }

            new AiRecordCommand(_ai, s.Pass, AtTick(10), new Mod(dir), _log).RunLine("stop");
            string file = Assert.Single(Directory.GetFiles(Path.Combine(dir, "ai-recordings", "human"), "*.airec"));
            Assert.Matches(@"^\d{8}T\d{6}Z\.airec$", Path.GetFileName(file));
            byte[] bytes = File.ReadAllBytes(file);

            var cmds = Record.Recordings.Records(bytes).Where(r => r.Type == AiRecordType.UserCmd)
                             .Select(r => Read(r.Payload, AiRecordFormat.ReadUserCmd).Actor).Distinct().ToArray();
            ActorId anon = Assert.Single(cmds);
            Assert.Equal(2, anon.Slot);
            Assert.NotEqual(s.PersonId, anon);
            Assert.True(Find(bytes, BitConverter.GetBytes(anon.Serial)) >= 0, "the control: the person's drawn serial is in the file");
            Assert.True(Find(bytes, BitConverter.GetBytes(s.PersonId.Serial)) < 0, "the person's own actor serial is in the file");

            byte[][] needles =
            [
                System.Text.Encoding.UTF8.GetBytes(PersonName), System.Text.Encoding.Unicode.GetBytes(PersonName),
                System.Text.Encoding.UTF8.GetBytes(SteamId), System.Text.Encoding.Unicode.GetBytes(SteamId),
                System.Text.Encoding.UTF8.GetBytes(Steam64.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                BitConverter.GetBytes(Steam64),
            ];
            foreach (byte[] needle in needles)
            {
                Assert.True(Find(bytes, needle) < 0, $"the recording holds {Convert.ToHexString(needle)}");
                // The control: the same search finds the needle once it is there.
                Assert.True(Find([.. bytes, .. needle], needle) >= 0);
            }
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
    /// <summary>
    /// Recording a human session needs no sv_cheats; what changes play, the
    /// brains held and stepped (<c>ai_step</c>) or run inline (<c>ai_sync</c>), does.
    /// </summary>
    [Fact]
    public void RecordingNeedsNoCheatsAndWhatChangesPlayDoes()
    {
        static ConVarFlags Command<T>() => typeof(T).GetCustomAttribute<ConCommandAttribute>()!.Flags;
        Assert.Equal(ConVarFlags.None, Command<AiRecordCommand>() & ConVarFlags.Cheat);
        Assert.Equal(ConVarFlags.Cheat, Command<AiStepCommand>() & ConVarFlags.Cheat);
        Assert.Equal(ConVarFlags.Cheat, typeof(AiSyncConVar).GetCustomAttribute<ConVarAttribute>()!.Flags & ConVarFlags.Cheat);
    }
    /// <summary>
    /// A game on an instance (<c>-instance</c>, what <c>make headless</c> and
    /// the tools run: a private game dir of links, removed at exit) records
    /// into the tree's mod dir, which its maps link points into; the tree's
    /// own gameinfo.txt links into the repo's game/ sources and is not followed
    /// (the first cut followed it and wrote into game/mod_test).
    /// </summary>
    [Fact]
    public void AnInstancesRecordingLandsInTheTreesModDir()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ss-human-inst-{Guid.NewGuid():N}");
        try
        {
            string tree = Path.Combine(root, "tree", "test"), instance = Path.Combine(root, "instance", "test"), sources = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(tree, "maps"));
            Directory.CreateDirectory(instance);
            Directory.CreateDirectory(sources);
            File.WriteAllText(Path.Combine(sources, "gameinfo.txt"), "GameInfo {}");
            // The tree's own gameinfo.txt is a link into the repo's sources: never followed.
            File.CreateSymbolicLink(Path.Combine(tree, "gameinfo.txt"), Path.Combine(sources, "gameinfo.txt"));
            File.CreateSymbolicLink(Path.Combine(instance, "gameinfo.txt"), Path.Combine(tree, "gameinfo.txt"));
            Assert.Equal(instance, AiRecordCommand.TreeGameDir(instance));   // the control: no maps link, its own dir
            Assert.Equal(tree, AiRecordCommand.TreeGameDir(tree));
            Directory.CreateSymbolicLink(Path.Combine(instance, "maps"), Path.Combine(tree, "maps"));
            Assert.Equal(tree, AiRecordCommand.TreeGameDir(instance));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- shots and hits (user ruling 2026-11-02) ----------------------------

    private (ShotTap Tap, Sight World) Shots()
    {
        var s = new Sight(_ai);
        return (new ShotTap(_ai, _bots, s.Pass, s.World.Engine(), WeaponCatalog.Stock(), Indexes(), AtTick(50)), s);
    }

    private static AiEvent[] Observed(List<(AiRecordType Type, byte[] Payload)> records, AiEventKind kind)
        => [.. records.Where(r => r.Type == AiRecordType.Observed).Select(r => Read(r.Payload, AiRecordFormat.ReadEvent)).Where(e => e.Kind == kind)];

    [Fact]
    public void EveryPlayersShotIsRecordedWithItsWeapon()
    {
        (ShotTap tap, Sight s) = Shots();
        Start();
        tap.OnWeaponEvent(s.Person.Handle, 3, PlayerWeaponEvent.Fired, 1);
        tap.OnWeaponEvent(s.Person.Handle, 3, PlayerWeaponEvent.ReloadStarted, 0);   // no shot
        AiEvent shot = Assert.Single(Observed(Stopped(), AiEventKind.ShotFired));
        Assert.Equal((s.PersonId, 50, 3), (shot.Bot, shot.Tick.Value, shot.Code));
    }

    [Fact]
    public void AHitIsRecordedWithItsVictimDamageWeaponAndRange()
    {
        (ShotTap tap, Sight s) = Shots();
        Start();
        s.Run(1);   // the reads: the person at 500 units along x, the bot at the origin
        s.Person.Weapon = "weapon_357";
        tap.OnPlayerHurt(new PlayerHurt(s.Bot.Handle, s.Person.Handle, 17, 83));   // before any Fired, as a command's first shot is
        AiEvent hit = Assert.Single(Observed(Stopped(), AiEventKind.HitDealt));
        Assert.Equal((s.PersonId, ActorDirectory.HumanActor(1, s.Bot.Handle), 17f, WeaponCatalog.Stock().IndexOf("weapon_357")), (hit.Bot, hit.Other, hit.Amount, hit.Code));
        Assert.True(hit.Code >= 0, "the control: the catalog knows the weapon");
        Assert.InRange(hit.Code2, 495, 505);
    }

    [Fact]
    public void HarmAPlayerDoesItselfIsNoHit()
    {
        (ShotTap tap, Sight s) = Shots();
        Start();
        s.Run(1);
        tap.OnPlayerHurt(new PlayerHurt(s.Person.Handle, s.Person.Handle, 40, 60));
        tap.OnPlayerHurt(new PlayerHurt(s.Person.Handle, EntityHandle.None, 10, 50));
        tap.OnPlayerHurt(new PlayerHurt(s.Bot.Handle, s.Person.Handle, 10, 90));   // the control: a real hit
        Assert.Single(Observed(Stopped(), AiEventKind.HitDealt));
    }

    /// <summary>The hooks reach the tap: the core tells WeaponEventTap and CombatTap, which hand it on, with no bot in the game too.</summary>
    [Fact]
    public void TheHooksHandTheirEventsToTheShotTap()
    {
        var w = new Sight(_ai);
        var shots = new ShotTap(_ai, _bots, w.Pass, w.World.Engine(), WeaponCatalog.Stock(), Indexes(), AtTick(50));
        var senses = new Hl2mpSenses(_bots, _ai, w.Pass, null!, null!, null!, _log, w.Reference);
        var meter = new AimMeter(_bots, w.Pass, _ai, AtTick(50), Indexes(), w.World.Engine(), WeaponCatalog.Stock());
        var weapons = new WeaponEventTap(senses, w.Pass, _bots, Indexes(), AtTick(50), shots, meter);
        var combat = new CombatTap(senses, w.Pass, _bots, w.World.Engine(), Indexes(), null!, AtTick(50), shots, meter);
        Start();
        w.Run(1);
        weapons.OnWeaponEvent(w.Person.Handle, 3, PlayerWeaponEvent.Fired, 1);
        combat.OnPlayerHurt(new PlayerHurt(w.Bot.Handle, w.Person.Handle, 10, 90));
        var records = Stopped();
        Assert.Single(Observed(records, AiEventKind.ShotFired));
        Assert.Single(Observed(records, AiEventKind.HitDealt));
    }

    /// <summary>A recording names the person it hits by its random serial: the anonymiser sees the hit's victim.</summary>
    [Fact]
    public void AHitsPersonIsAnonymised()
    {
        (ShotTap tap, Sight s) = Shots();
        _ai.Recording.Start(_file, new AiRecordingHeader(1, "ss_test", 0, 0, VisionPass.Slots, Hl2mpAiSession.SightedCapacity), AiRecordCommand.Anonymiser(7));
        s.Run(1);
        tap.OnPlayerHurt(new PlayerHurt(s.Person.Handle, s.Bot.Handle, 10, 90));
        AiEvent hit = Assert.Single(Observed(Stopped(), AiEventKind.HitDealt));
        Assert.Equal(s.PersonId.Slot, hit.Other.Slot);
        Assert.NotEqual(s.PersonId, hit.Other);
    }

    /// <summary>A recording started mid-game names what the bots already see.</summary>
    [Fact]
    public void ARecordingStartedMidGameHoldsTheSightsAlreadyHeld()
    {
        var s = new Sight(_ai);
        s.Run(40);   // the bot has recognised the person by now
        Assert.Contains(s.Events, e => e.Kind == AiEventKind.SightGained && e.Bot == s.BotId);
        string dir = Path.Combine(Path.GetTempPath(), $"ss-human-held-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "x.airec");
            new AiRecordCommand(_ai, s.Pass, AtTick(41), new Mod(dir), _log).RunLine($"start {file} ss_test");
            _ai.Recording.Stop();
            AiEvent held = Assert.Single(Observed(Record.Recordings.Records(File.ReadAllBytes(file)), AiEventKind.SightGained));
            Assert.Equal((s.BotId, AiEvent.HeldSight, 41), (held.Bot, held.Code, held.Tick.Value));
            Assert.Equal(2, held.Other.Slot);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
