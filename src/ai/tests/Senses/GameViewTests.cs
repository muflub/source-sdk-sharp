using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Shared;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// The game's half of a bot's view (plan_bots_teams_tf2 C.3, P6 step 3): what the game's client
/// draws over a sighted player (and an actor it draws nothing of is not in the view), the things
/// that are not players a bot sees through its own eyes, as its client is shown them, and the
/// objectives the HUD shows, the same for every bot.
/// </summary>
public sealed class GameViewTests
{
    private sealed class Hud : IBotHud
    {
        public SelfState Read(EntityHandle bot) => new(default, default, default, 100, 0, 3, 17, 90, SelfFlags.Alive);
    }

    /// <summary>A game view a fact scripts.</summary>
    private sealed class View : IBotGameView
    {
        public Func<EntityHandle, ActorFacts, ActorFacts?> Draw { get; set; } = (_, f) => f;
        public Func<EntityHandle, ThingFacts, ThingFacts?> DrawThing { get; set; } = (_, f) => f;
        public List<ThingRead> All { get; } = new();
        public ObjectiveKnowledge? Objectives { get; set; }
        public bool OwnKillFeed => false;
        public GameHud? GameHudRead { get; set; }

        public SelfState Hud(EntityHandle bot, in SelfState read) => read;

        public GameHud? HudOf(EntityHandle bot) => GameHudRead;

        public bool Render(EntityHandle viewer, EntityHandle them, ref ActorFacts facts)
        {
            if (Draw(viewer, facts) is not { } drawn)
                return false;
            facts = drawn;
            return true;
        }

        public Func<EntityHandle, EntityHandle, ActorId, ActorId> Shown { get; set; } = (_, _, real) => real;

        public ActorId ShownAs(EntityHandle viewer, EntityHandle them, ActorId real) => Shown(viewer, them, real);

        public Func<EntityHandle, EntityHandle, bool> Solids { get; set; } = (_, _) => false;

        public bool Solid(EntityHandle viewer, EntityHandle them) => Solids(viewer, them);

        public void Things(List<ThingRead> into)
        {
            into.Clear();
            into.AddRange(All);
        }

        public bool RenderThing(EntityHandle viewer, ref ThingFacts facts)
        {
            if (DrawThing(viewer, facts) is not { } drawn)
                return false;
            facts = drawn;
            return true;
        }

        public bool Frozen(EntityHandle bot) => false;
    }

    /// <summary>HL2MP's game with a scripted view.</summary>
    private sealed class Game(IBotGameView view) : IBotGame
    {
        private readonly IBotGame _hl2mp = TestGames.Hl2mp();

        public IDecider Decider(BrainSetup setup) => _hl2mp.Decider(setup);
        public WeaponKnowledge Weapons() => _hl2mp.Weapons();
        public AbilityTable Abilities => default;
        public MapKnowledge Map(string mapName, MapEntities entities, WeaponKnowledge weapons) => _hl2mp.Map(mapName, entities, weapons);
        public KeyValuePair<string, string>[] UserInfo(AiRandom random, int team, int teams) => [];
        public string GrenadeWeapon => "";
        public bool Thrown(in WeaponType weapon) => false;
        public IBotGameView View => view;
    }

    private static readonly VisionParams Eyes = new(6000f, 90f, 0.3f, 0.5f);

    private readonly SenseWorld _w = new();
    private readonly View _view = new();
    private readonly SensedBot _bot;
    private readonly SenseWorld.Player _them;
    private readonly VisionPass _pass;
    private readonly SnapshotBuilder _builder;
    private readonly SnapshotRing _ring = new(4, VisionPass.Slots, 8);
    private int _tick;

    public GameViewTests()
    {
        SenseWorld.Player bot = _w.Add(1, new AiVector(0, 0, 0));
        _them = _w.Add(2, new AiVector(500, 0, 0));
        _pass = new VisionPass(_w.Engine(), _w.Entities(), _w.Traces());
        _bot = new SensedBot(new ActorId(1, 1), bot.Handle, Eyes);
        _pass.Add(_bot.Id);
        var game = new Game(_view);
        _builder = new SnapshotBuilder(_w.Engine(), WeaponCatalog.Stock(), new Hud(), _w.Stance(), new FakeTeams(), game,
                                       new ThingSight(_w.Engine(), _w.Entities(), _w.Traces(), game), new ShownActors(game));
        var events = new List<AiEvent>();
        for (_tick = 1; _tick <= 60; _tick++)
            _pass.Run(new Tick(_tick), 0.015f, [_bot], _w.Living(_bot), events);
    }

    private PerceivedView Build()
    {
        int tick = _tick++;
        _pass.Run(new Tick(tick), 0.015f, [_bot], _w.Living(_bot), []);
        AiSnapshot snap = _ring.BeginWrite(new Tick(tick));
        _builder.Build(snap, [_bot], _pass, _ => ActorActivity.None);
        return snap.ViewFor(1)!;
    }

    private static ThingRead Rocket(int id, AiVector at, int team = 3)
        => new(new EntityHandle((uint)(0x0002_0000 | (100 + id))), new ThingFacts(id, ThingKind.Projectile, 1, at, new AiVector(-1100, 0, 0), team), 8f);

    [Fact]
    public void TheGameWritesWhatItsClientDrawsOverASightedPlayer()
    {
        // A disguise: the game says the bot's client draws them in team 2's colours as class 7.
        _view.Draw = (_, f) => f with { Team = 2, Class = 7, NameSlot = 1 };
        Assert.Single(Build().Sighted.ToArray(), f => f is { Team: 2, Class: 7, NameSlot: 1 });
    }

    /// <summary>
    /// Every sighted actor's target ID shows a name, its own slot's when the game draws no other (the Spy's fact 2): the
    /// field reads alike for everyone, so a value of it never sets a disguised Spy apart. Was 0 for all but a disguise.
    /// </summary>
    [Fact]
    public void EverySightedActorShowsItsOwnNameUnlessTheGameDrawsAnother()
    {
        ActorFacts f = Assert.Single(Build().Sighted.ToArray());
        Assert.True(f.NameSlot > 0);
        Assert.Equal(f.Id.Slot, f.NameSlot);
    }

    private Func<int, EntityHandle> PlayerOf => s => _pass.ReadOf(s)?.Player ?? EntityHandle.None;

    /// <summary>One sense tick's events as the bot is told them (AiSenses' order: the vision, the changes in sight, then each told).</summary>
    private List<AiEvent> Told()
    {
        var tick = new Tick(_tick++);
        var events = new List<AiEvent>();
        var changes = new List<AiEvent>();
        _pass.Run(tick, 0.015f, [_bot], _w.Living(_bot), events);
        _builder.Shown.Refresh(tick, [_bot], _pass, changes);
        return [.. events.Select(e => _builder.Shown.Told(e, PlayerOf)), .. changes];
    }

    /// <summary>
    /// The Spy's honesty fix (ShownActors): the sighting that tells the bot of a player names the actor the game says it
    /// takes them for; when that changes in sight (a disguise goes on), the bot loses the one and gains the other where
    /// the body stands; the view, a sound and the loss name the alias too. HL2MP's view changes nothing (the control).
    /// </summary>
    [Fact]
    public void ADrawingThatChangesInSightIsALossAndAGainAndEverythingNamesTheNewActor()
    {
        ActorId real = _pass.ActorIn(2), alias = ActorId.Alias(2, 1);
        Assert.Equal(real, _builder.Shown.Told(AiEvent.SightGained(_bot.Id, new Tick(_tick), real, default, default, 1f), PlayerOf).Other);
        Assert.Empty(Told());   // the control: nothing changed, nothing told

        _view.Shown = (_, _, _) => alias;
        List<AiEvent> changed = Told();
        Assert.Equal([(AiEventKind.SightLost, real), (AiEventKind.SightGained, alias)], changed.Select(e => (e.Kind, e.Other)));
        Assert.Equal(alias, Assert.Single(Build().Sighted.ToArray()).Id);
        Assert.Equal(alias, _builder.Shown.Told(AiEvent.SoundHeard(_bot.Id, new Tick(_tick), SoundKind.Footstep, default, 1f, real, -1), PlayerOf).Other);
        Assert.Equal(alias, _builder.Shown.Told(AiEvent.SightLost(_bot.Id, new Tick(_tick), real, default, default), PlayerOf).Other);
        Assert.Equal(ActorId.None, _builder.Shown.HeldFor(1, 2));
    }

    [Fact]
    public void APlayerTheGameDrawsNothingOfIsNotInTheView()
    {
        Assert.Equal(1, Build().SightedCount);    // the control: drawn, seen
        _view.Draw = (_, _) => null;              // a cloak past what a renderer shows
        Assert.Equal(0, Build().SightedCount);
    }

    [Fact]
    public void TheObjectivesTheHudShowsReachEveryView()
    {
        var flags = new ObjectiveKnowledge([new Objective(5, ObjectiveKind.Carry, 2, 1 << 3, ObjectiveState.Home, new AiVector(1, 2, 3))], 9);
        _view.Objectives = flags;
        Assert.Same(flags, Build().Objectives);
    }

    private sealed class SomeHud : GameHud
    {
        public override byte[] Recorded() => [];
    }

    /// <summary>The game's own HUD read (plan_bots_teams_tf2 P7) reaches the bot's view as the game made it, and a step's copy keeps it.</summary>
    [Fact]
    public void TheGamesHudReachesTheViewAndItsCopy()
    {
        var hud = new SomeHud();
        _view.GameHudRead = hud;
        PerceivedView built = Build();
        var copy = new PerceivedView(8);
        copy.CopyFrom(built);
        Assert.Same(hud, built.GameHud);
        Assert.Same(hud, copy.GameHud);
        copy.Reset(built.Bot, built.Tick, built.Self);
        Assert.Null(copy.GameHud);
    }

    [Fact]
    public void AThingInSightIsInTheViewAsTheGameDrewIt()
    {
        _view.All.Add(Rocket(1, new AiVector(300, 0, 40)));
        ThingFacts seen = Assert.Single(Build().Things.ToArray());
        Assert.Equal((1, ThingKind.Projectile, 3), (seen.Id, seen.Kind, seen.Team));
        Assert.Equal(new AiVector(300, 0, 40), seen.Origin);
    }

    [Fact]
    public void AThingBehindAWallIsNotInTheView()
    {
        _view.All.Add(Rocket(1, new AiVector(300, 0, 40)));
        _w.Occluder(new AiVector(150, -100, -10), new AiVector(166, 100, 300), SenseWorld.Solid);
        Assert.Equal(0, Build().ThingCount);
    }

    [Fact]
    public void AThingBehindTheBotIsNotInTheView()
    {
        _view.All.Add(Rocket(1, new AiVector(-300, 0, 40)));
        Assert.Equal(0, Build().ThingCount);
    }

    [Fact]
    public void AThingOutsideThePvsIsNotInTheView()
    {
        _view.All.Add(Rocket(1, new AiVector(300, 0, 40)));
        _w.Pvs = (_, _, _) => false;
        Assert.Equal(0, Build().ThingCount);
    }

    [Fact]
    public void WhatTheBotsClientIsShownOfAThingIsWhatItHolds()
    {
        _view.All.Add(new ThingRead(new EntityHandle(0x0002_0070u), new ThingFacts(9, ThingKind.Building, 2, new AiVector(300, 0, 0), default, 3, 4, 2, 180), 24f));
        _view.DrawThing = (_, f) => f with { Health = -1 };   // its target ID shows no health to the other team
        Assert.Equal((2, -1), Build().Things.ToArray() is [{ } b] ? (b.Level, b.Health) : (0, 0));
        _view.DrawThing = (_, _) => null;
        Assert.Equal(0, Build().ThingCount);
    }

    /// <summary>A sky of stickies costs a tick no more than <see cref="ThingSight.TracesPerTick"/> sight lines: the rest are seen as their lines are traced.</summary>
    [Fact]
    public void ThingSightLinesAreTracedWithinATicksBudget()
    {
        int many = ThingSight.TracesPerTick + 4;
        for (int i = 0; i < many; i++)
            _view.All.Add(Rocket(i + 1, new AiVector(300, i * 4 - 40, 40)));
        int before = _w.Lines;
        int first = Build().ThingCount;
        int traced = _w.Lines - before;
        Assert.Equal(ThingSight.TracesPerTick, first);
        Assert.True(traced <= ThingSight.TracesPerTick + 8, $"{traced} lines in the first tick (the players' pass included)");
        Assert.Equal(many, Build().ThingCount);
    }

    [Fact]
    public void AViewsCopyCarriesItsThingsAndObjectives()
    {
        var view = new PerceivedView(4, 2);
        var objectives = new ObjectiveKnowledge([]);
        view.Reset(new ActorId(1, 1), new Tick(3), default);
        Assert.True(view.AddThing(new ThingFacts(1, ThingKind.Projectile, 1, default, default, 2)));
        Assert.True(view.AddThing(new ThingFacts(2, ThingKind.Building, 1, default, default, 3)));
        Assert.False(view.AddThing(new ThingFacts(3, ThingKind.Building, 1, default, default, 3)));
        view.SetObjectives(objectives);
        var copy = new PerceivedView(4, 2);
        copy.CopyFrom(view);
        Assert.Equal(view.Things.ToArray(), copy.Things.ToArray());
        Assert.Same(objectives, copy.Objectives);
    }

    /// <summary>The wiring: the HUD's game rows are asked by the bot's HUD, a player's drawing by the snapshot builder, and nothing else asks.</summary>
    [Fact]
    public void TheHudAndTheBuilderAskTheGameWhatItDraws()
    {
        System.Reflection.Assembly[] binding = [typeof(VisionPass).Assembly];
        Assert.Equal([typeof(EngineBotHud).FullName!], IlCallCensus.Callers(m => m.DeclaringType == typeof(IBotGameView) && m.Name == nameof(IBotGameView.Hud), binding));
        Assert.Equal([typeof(SnapshotBuilder).FullName!], IlCallCensus.Callers(m => m.DeclaringType == typeof(IBotGameView) && m.Name == nameof(IBotGameView.Render), binding));
        Assert.Equal([typeof(ThingSight).FullName!], IlCallCensus.Callers(m => m.DeclaringType == typeof(IBotGameView) && m.Name == nameof(IBotGameView.RenderThing), binding));
    }
}
