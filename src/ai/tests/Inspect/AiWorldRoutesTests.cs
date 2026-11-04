using System.Reflection;
using System.Text.Json;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Hl2mpNav;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Shared;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Inspect;

/// <summary>
/// <c>GET /ai/world</c> (plan_hl2mp_ai §7.1), the 3D view's one call: each
/// player, each bot's eye and view with the aim traced along it, its candidates,
/// what it chose, its follower's path and what it heard, over a fake engine with
/// the real vision pass, follower and session. The answer is a golden JSON
/// (<c>AI_GOLDEN_WRITE=1</c> rewrites it).
/// </summary>
public sealed class AiWorldRoutesTests
{
    private static string Golden => RepoLayout.Repo("src", "ai", "tests", "goldens", "ai-world.json");

    private static readonly Lazy<LoadedNav> Course = new(() =>
    {
        byte[] bsp = TinyBsp.Make(TinyBsp.Stock);
        return NavLoader.Load("course", p => p.EndsWith(".bsp", StringComparison.Ordinal) ? bsp
                                             : NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write(), null).Nav!;
    });

    private sealed class Quiet : ILog
    {
        public void Msg(string message) { }

        public void Warning(string message) { }
    }

    private sealed class Navigation : IHl2mpNavigation
    {
        public LoadedNav? Current => Course.Value;

        public string? Refusal => null;

        public NavObstacleState? Obstacles { get; } = new(Course.Value.Nav.Graph.ObstacleCount);
        public NavObstacleState? ObstaclesFor(SourceSharp.Abi.EntityHandle player) => Obstacles;

        public NavSight? Sight => null;


        public ChokeSlot? Chokes => null;
    }

    private sealed class Runner : IPlayerCommandRunner
    {
        public void Queue(EntityHandle player, in UserCmd cmd) { }

        public int RunQueued(EntityHandle player) => 0;
    }

    /// <summary>Every call answers its type's default: what a fact does not name, an engine answers plainly.</summary>
    public class Defaults : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.ReturnType is { IsValueType: true } t && t != typeof(void) ? Activator.CreateInstance(t) : null;
    }

    private sealed class NoHoldings : IBotInventory
    {
        public IReadOnlyList<string> Held(EntityHandle player) => [];
    }

    private sealed class WorldPlayers(SenseWorld world) : IBotPlayers
    {
        public readonly List<BotPlayer> Made = new();

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal)
        {
            SenseWorld.Player p = world.Players.First(x => Made.All(b => b.Player != x.Handle) && x.Slot != Rig.HumanSlot);
            Made.Add(new BotPlayer(p.Handle, p.Slot, name));
            refusal = "";
            return p.Handle;
        }

        public bool IsBot(EntityHandle player) => Made.Any(b => b.Player == player);

        public bool Kick(EntityHandle bot, string reason) => Made.RemoveAll(b => b.Player == bot) > 0;

        public IReadOnlyList<BotPlayer> Bots => Made;
    }

    /// <summary>Two bots and a human on the course, the real vision pass run over them.</summary>
    private sealed class Rig
    {
        public const int HumanSlot = 1;
        public readonly SenseWorld World = new();
        public readonly SenseWorld.Player Human, Alyx, Barney;
        public readonly VisionPass Vision;
        public readonly Hl2mpBots Bots;
        public readonly Hl2mpAiSession Ai;
        public readonly HeardLog Heard = new();
        public readonly AiWorldRoutes Routes;
        public readonly PlayerNames Names;
        private readonly NavGraph _graph = Course.Value.Nav.Graph;
        private readonly Navigation _nav;
        private int _tick = 100;

        public int Tick { get => _tick; set => _tick = value; }

        public Rig()
        {
            Human = World.Add(HumanSlot, new AiVector(300, 40, 16), 180f);
            Alyx = World.Add(2, new AiVector(40, 40, 16));
            Barney = World.Add(3, new AiVector(40, 300, 16), 90f);
            _nav = new Navigation();
            var log = new Quiet();
            IEngineEntities entities = Entities();
            IEnginePlayers players = DispatchProxy.Create<IEnginePlayers, Defaults>();
            IEngineFrame frame = FakeEngine.Of<IEngineFrame>((m, _) => m.Name == "TickCount" ? _tick : throw FakeEngine.Unasked(m));
            Bots = new Hl2mpBots(new WorldPlayers(World), new Runner(), Alive(players), entities, WeaponCatalog.Stock(), frame, _nav, new NoHoldings(),
                                 DispatchProxy.Create<IBotNameSource, Defaults>(), new ScoreboardTags(), log);
            Vision = new VisionPass(World.Engine(), entities, World.Traces());
            Ai = new Hl2mpAiSession(_nav, WeaponCatalog.Stock(), null!, null!, log,
            DeclaredConVars.Fresh<AiThreadsConVar>(), DeclaredConVars.Fresh<AiSyncConVar>(),
            DeclaredConVars.Fresh<WeaponRespawnTimeConVar>(), DeclaredConVars.Fresh<ItemRespawnTimeConVar>());
            Names = new PlayerNames(DispatchProxy.Create<IEngineGameEvents, Defaults>());
            Names.PutInServer(HumanSlot, "Gordon");
            Routes = new AiWorldRoutes(Bots, Vision, Ai, Heard, frame, entities, World.Traces(), Names, _nav);
            Ai.Session.World = new MemoryWorld(new MapKnowledge("course", [], [], new WeaponKnowledge([new WeaponFacts(3, "weapon_smg1", 45, 0.075f, 1.5f, true, false, 0f)])),
                                               _graph, Hl2mpAiSession.TickSeconds, id => id.ToString());
            Ai.Session.Curves = Skills.Curves.Game();
            Ai.Session.Deciders = setup => new Hl2mpDecider(setup);
            SkillPresets.TryGet("hard", out SkillProfile? hard);
            Assert.Null(Bots.Add("Alyx", hard!));
            Assert.Null(Bots.Add("Barney", hard!));
            foreach (Hl2mpBot b in Bots.Bots)
            {
                Vision.Add(b.Id);
                Ai.Session.AddBot(b.Id, (ulong)b.Id.Slot, [.. hard!.Levels.Select(l => (byte)l)]);
            }
        }

        public ActorId AlyxId => Bots.Bots.First(b => b.Name == "Alyx").Id;

        private IEngineEntities Entities()
        {
            IEngineEntities sense = World.Entities();
            return FakeEngine.Of<IEngineEntities>((m, a) => m.Name is "GetOrigin" or "GetEyePosition" or "GetVelocity" or "GetEyeAngles"
                ? m.Invoke(sense, a)
                : m.ReturnType is { IsValueType: true } t && t != typeof(void) ? Activator.CreateInstance(t) : null);
        }

        private static IEnginePlayers Alive(IEnginePlayers fallback)
            => FakeEngine.Of<IEnginePlayers>((m, a) => m.Name == "IsAlive" ? true : m.Invoke(fallback, a));

        /// <summary>The senses' pass, as the game runs it once a tick.</summary>
        public void Sense(int ticks = 1)
        {
            SensedBot[] sensed = [.. Bots.Bots.Select(b => new SensedBot(b.Id, Bots.Applier.PlayerOf(b.Id), new VisionParams(6000f, 90f, 0.3f, 0.5f)))];
            for (int i = 0; i < ticks; i++)
                Vision.Run(new Tick(++_tick), Hl2mpAiSession.TickSeconds, sensed, World.Living(sensed), []);
        }

        public AiWorld World0() => Routes.Build();

        public WorldBot Bot(string name) => Routes.Build().Bots.Single(b => b.Name == name);

        public void Walk() => Bots.OnPreThink(0.015f);
    }

    [Fact]
    public void EveryPlayerIsAnActorWithItsNamePlaceAndKind()
    {
        var r = new Rig();
        r.Sense();
        WorldActor[] actors = r.World0().Actors;
        Assert.Equal(new WorldActor[]
        {
            new(1, "Gordon", false, true, new AiVector(300, 40, 16)),
            new(2, "Alyx", true, true, new AiVector(40, 40, 16)),
            new(3, "Barney", true, true, new AiVector(40, 300, 16)),
        }, actors);
    }

    [Fact]
    public void ADeadBotIsListedWithNoPlaceAndNoTruth()
    {
        var r = new Rig();
        r.Alyx.Alive = false;
        r.Sense();
        AiWorld w = r.World0();
        Assert.Equal(new WorldActor(2, "Alyx", true, false, null), w.Actors.Single(a => a.Slot == 2));
        Assert.Null(w.Bots.Single(b => b.Slot == 2).Truth);
        Assert.NotNull(w.Bots.Single(b => b.Slot == 3).Truth);
    }

    /// <summary>
    /// A new level counts its ticks from the start, and until the senses have
    /// run on it the vision pass's reads are where players stood on the last
    /// one (live: the 3D view framed the human where it had stood on the old map).
    /// </summary>
    [Fact]
    public void ReadsOfTheLastLevelAreNotShown()
    {
        var r = new Rig();
        r.Tick = 5000;
        r.Sense();
        Assert.Equal(3, r.World0().Actors.Count(a => a.Alive));

        r.Tick = 12;
        AiWorld w = r.World0();
        Assert.DoesNotContain(w.Actors, a => a.Alive);
        Assert.Equal(2, w.Actors.Length);
        Assert.Null(w.Bots[0].Truth);

        r.Sense();
        Assert.Equal(3, r.World0().Actors.Count(a => a.Alive));
    }

    /// <summary>A poll landing a few ticks after the pass still reads it: the route and the senses do not share a frame stage.</summary>
    [Fact]
    public void AReadAFewTicksOldIsStillShown()
    {
        var r = new Rig();
        r.Sense();
        r.Tick += AiWorldRoutes.FreshTicks;
        Assert.Equal(3, r.World0().Actors.Count(a => a.Alive));
        r.Tick += 1;
        Assert.DoesNotContain(r.World0().Actors, a => a.Alive);
    }

    /// <summary>A bot's view is the engine's angles, and its eye the vision pass's read, not its feet.</summary>
    [Fact]
    public void ABotsTruthIsItsEyeAndAngles()
    {
        var r = new Rig();
        r.Alyx.Angles = new AiVector(-20f, 135f, 0f);
        r.Sense();
        WorldTruth t = r.Bot("Alyx").Truth!;
        Assert.Equal((new AiVector(40, 40, 80), 135f, -20f), (t.Eye, t.Yaw, t.Pitch));
    }

    /// <summary>The aim is traced along the view: a box straight ahead on +x stops it at its face; one off to the side does not.</summary>
    [Fact]
    public void TheAimStopsAtWhatIsAlongTheView()
    {
        var r = new Rig();
        r.Barney.Alive = r.Human.Alive = false;
        r.World.Occluder(new AiVector(200, 20, 0), new AiVector(220, 60, 200), SenseWorld.Solid);
        r.Sense();
        Assert.Equal(160f, r.Bot("Alyx").Truth!.AimDistance, 1);

        r.Alyx.Angles = new AiVector(0f, 90f, 0f);
        r.Sense();
        Assert.Equal(-1f, r.Bot("Alyx").Truth!.AimDistance);
    }

    /// <summary>Positive pitch is down (Source's): the ray from the eye meets a floor below, and misses it looking up.</summary>
    [Fact]
    public void TheAimsPitchIsPositiveDown()
    {
        var r = new Rig();
        r.Barney.Alive = r.Human.Alive = false;
        r.World.Occluder(new AiVector(-1000, -1000, -10), new AiVector(1000, 1000, 0), SenseWorld.Solid);
        r.Alyx.Angles = new AiVector(30f, 0f, 0f);
        r.Sense();
        Assert.Equal(80f / MathF.Sin(MathF.PI / 6f), r.Bot("Alyx").Truth!.AimDistance, 0);

        r.Alyx.Angles = new AiVector(-30f, 0f, 0f);
        r.Sense();
        Assert.Equal(-1f, r.Bot("Alyx").Truth!.AimDistance);
    }

    [Fact]
    public void EachCandidateCarriesTheStageItReachedAndWhetherItIsSeen()
    {
        var r = new Rig();
        r.Sense(60);   // past recognition
        WorldCandidate[] seen = r.Bot("Alyx").Truth!.Candidates;
        Assert.Equal([1, 3], seen.Select(c => c.Slot));
        WorldCandidate human = seen[0], barney = seen[1];
        Assert.Equal(("clear", true, 1f), (human.Stage, human.Sees, human.Fraction));
        Assert.Equal(("cone", false), (barney.Stage, barney.Sees));
        Assert.Equal(new AiVector(300, 40, 48), human.At);
    }

    [Fact]
    public void WithNoWalkABotHasNoRoute()
    {
        var r = new Rig();
        r.Sense();
        Assert.Null(r.Bot("Alyx").Route);
    }

    /// <summary>The route is the scripted walk's follower's path, through the door's gap, never a straight line at its target.</summary>
    [Fact]
    public void AScriptedWalksRouteIsItsFollowersPath()
    {
        var r = new Rig();
        r.Bots.Thinking = false;
        r.Alyx.Origin = new AiVector(560, 40, 16);
        var target = new AiVector(700, 40, 16);
        Assert.Null(r.Bots.GoTo("Alyx", target, null));
        r.Walk();
        r.Sense();
        WorldRoute route = r.Bot("Alyx").Route!;
        Assert.Equal(("goto", target), (route.Source, route.Target));
        Assert.True(route.Points.Length >= 3 && route.Points.Any(p => p.Y >= 200f), $"the route bends at the gap: {string.Join(" ", route.Points)}");
        Assert.Equal((target.X, target.Y), (route.Points[^1].X, route.Points[^1].Y));
    }

    /// <summary>What it heard is its belief: the place the log holds (not the truth), for the last few seconds only.</summary>
    [Fact]
    public void WhatABotHeardIsInItsBeliefsForAFewSeconds()
    {
        var r = new Rig();
        r.Sense();
        r.Heard.Add(AiEvent.SoundHeard(r.AlyxId, new Tick(r.Tick), SoundKind.Gunfire, new AiVector(11, 22, 33), 0.75f, new ActorId(1, 5), 3));
        WorldHeard h = Assert.Single(r.Bot("Alyx").Beliefs.Heard);
        Assert.Equal((1, new AiVector(11, 22, 33), r.Tick, "gunfire", 0.75f), (h.Source, h.At, h.Tick, h.Kind, h.Loudness));
        Assert.Empty(r.Bot("Barney").Beliefs.Heard);

        r.Sense((int)(AiWorldRoutes.HeardSeconds / Hl2mpAiSession.TickSeconds) + 2);
        Assert.Empty(r.Bot("Alyx").Beliefs.Heard);
    }

    private static void StepAlyx(Rig r)
    {
        Brain brain = r.Ai.Session.BrainIn(r.AlyxId.Slot)!;
        var them = new ActorId(Rig.HumanSlot, ActorDirectory.HumanSerialBit | 1);
        var view = new PerceivedView(8);
        view.Reset(r.AlyxId, new Tick(r.Tick), new SelfState(r.Alyx.Origin, default, default, 100, 0, 3, 45, 90, SelfFlags.Alive));
        view.AddSighted(new ActorFacts(them, r.Human.Origin, default, 3, ActorActivity.None, 1f));
        brain.Step(new Tick(r.Tick), view, [AiEvent.SightGained(r.AlyxId, new Tick(r.Tick), them, r.Human.Origin, default, 1f)]);
    }

    [Fact]
    public void WhatABotRemembersAndChoseComeFromItsBrain()
    {
        var r = new Rig();
        r.Sense();
        Assert.Empty(r.Bot("Alyx").Beliefs.Remembered!);
        StepAlyx(r);
        WorldBot bot = r.Bot("Alyx");
        WorldRemembered them = Assert.Single(bot.Beliefs.Remembered!);
        Assert.Equal((Rig.HumanSlot, r.Human.Origin, true), (them.Slot, them.At, them.InSight));
        Assert.False(string.IsNullOrEmpty(bot.Going!.Branch));
        Assert.False(bot.Stepping);
    }

    /// <summary>A brain stepping on a worker is never waited for: the bot answers without its mind and says so.</summary>
    [Fact]
    public void ABrainSteppingOnAWorkerIsNotWaitedFor()
    {
        var r = new Rig();
        r.Sense();
        StepAlyx(r);
        Assert.NotNull(r.Bot("Alyx").Going);

        object brainLock = typeof(AiSession).GetField("_lanes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(r.Ai.Session) is Array lanes
            ? lanes.GetValue(r.AlyxId.Slot)!.GetType().GetProperty("BrainLock", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lanes.GetValue(r.AlyxId.Slot))!
            : throw new InvalidOperationException("no lanes");
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var worker = new Thread(() =>
        {
            lock (brainLock)
            {
                held.Set();
                release.Wait();
            }
        });
        worker.Start();
        held.Wait();
        WorldBot bot;
        try
        {
            bot = r.Bot("Alyx");
        }
        finally
        {
            release.Set();
            worker.Join();
        }

        Assert.True(bot.Stepping);
        Assert.Null(bot.Going);
        Assert.Null(bot.Beliefs.Remembered);
        Assert.NotNull(bot.Truth);
    }

    /// <summary>Each answer says what the last one cost, the JSON included, for §12.3's row and the tab's readout.</summary>
    [Fact]
    public void TheAnswerSaysWhatTheLastOneCost()
    {
        var r = new Rig();
        r.Sense();
        DebugRouteReplyShape first = Parse(r);
        DebugRouteReplyShape second = Parse(r);
        Assert.Equal(0f, first.CostUs);
        Assert.True(second.CostUs > 0f, $"the second answer's cost {second.CostUs}");
    }

    private readonly record struct DebugRouteReplyShape(int Status, float CostUs);

    private static DebugRouteReplyShape Parse(Rig r)
    {
        Debug.Api.Server.Interfaces.DebugRouteReply reply = r.Routes.Handle();
        using JsonDocument doc = JsonDocument.Parse(reply.Json);
        return new DebugRouteReplyShape(reply.Status, doc.RootElement.GetProperty("costUs").GetSingle());
    }

    [Fact]
    public void TheProviderServesTheRouteAndNamesIt()
    {
        var r = new Rig();
        r.Sense();
        var provider = new AiDebugRoutes(new Navigation(), world: r.Routes);
        Assert.Contains(provider.Routes, route => route.Path == "/ai/world");
        Debug.Api.Server.Interfaces.DebugRouteReply reply = provider.Handle("/ai/world", new Dictionary<string, string>());
        Assert.Equal(200, reply.Status);
        using JsonDocument doc = JsonDocument.Parse(reply.Json);
        Assert.Equal(3, doc.RootElement.GetProperty("actors").GetArrayLength());
    }

    /// <summary>The whole answer, one of each part, as the web UI reads it.</summary>
    [Fact]
    public void TheAnswerIsTheGolden()
    {
        var r = new Rig();
        r.Bots.Thinking = false;
        r.Alyx.Origin = new AiVector(560, 40, 16);
        r.Alyx.Angles = new AiVector(10f, 45f, 0f);
        r.World.Occluder(new AiVector(700, 0, 0), new AiVector(720, 1000, 200), SenseWorld.Solid);
        Assert.Null(r.Bots.GoTo("Alyx", new AiVector(700, 40, 16), null));
        r.Walk();
        r.Sense(40);
        StepAlyx(r);
        r.Heard.Add(AiEvent.SoundHeard(r.AlyxId, new Tick(r.Tick), SoundKind.Footstep, new AiVector(300, 45, 16), 0.5f, new ActorId(1, 5), -1));
        string json = AiWorldRoutes.Json(r.World0());
        if (Environment.GetEnvironmentVariable("AI_GOLDEN_WRITE") == "1")
            File.WriteAllText(Golden, json);
        Assert.True(File.Exists(Golden), $"no golden at {Golden}: AI_GOLDEN_WRITE=1 writes it");
        Assert.Equal(File.ReadAllText(Golden), json);
    }
}
