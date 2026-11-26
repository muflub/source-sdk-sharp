using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Round 2's S4: the conditions a player sees on another, past format 10's byte, as a TF2 client draws them: Jarate's and
/// Mad Milk's drips, wet as stock's <c>crit_vs_wet_players</c> reads it, the mark for death over the head, and a body thrown
/// by a blast (stock's <c>InAirDueToExplosion</c>). Each reads on a sighted enemy that has it, is clear without, and an
/// enemy the bot does not see is not in its view to read.
/// </summary>
public sealed class TFViewConditionsTests
{
    private const int Red = (int)TFTeam.Red;

    private static TFActorFacts Soldier(bool jarated = false, bool milked = false, bool gassed = false, bool marked = false, bool blast = false,
                                        bool rocketpack = false, bool cloaked = false, bool disguised = false)
        => new(TFClass.Soldier, TFTeam.Blue, disguised ? TFClass.Pyro : TFClass.Civilian, disguised ? TFTeam.Blue : TFTeam.Unassigned, 0,
               cloaked ? 0.5f : 0f, 0, Burning: false, Stunned: false, Ubered: false, Disguised: disguised, Cloaked: cloaked, Taunting: false,
               Jarated: jarated, Milked: milked, Gassed: gassed, MarkedForDeath: marked, BlastJumping: blast, Rocketpack: rocketpack);

    private static readonly TFBody Dry = new(0, OnGround: true), InAir = new(0, OnGround: false);

    private static ActorConditions Drawn(in TFActorFacts them, TFBody body)
    {
        ActorFacts f = new(new ActorId(4, 9), default, default, 11, ActorActivity.None, 1f, (int)TFTeam.Blue, NameSlot: 4);
        Assert.True(TFBotGameView.Draw(Red, them, -1, ref f, body));
        return f.Conditions;
    }

    [Fact]
    public void AJaratedEnemyReadsJaratedAndWet()
    {
        Assert.Equal(ActorConditions.Jarated | ActorConditions.Wet, Drawn(Soldier(jarated: true), Dry));
        Assert.Equal(ActorConditions.None, Drawn(Soldier(), Dry));
    }

    [Fact]
    public void AMilkedEnemyReadsMilkedAndWet()
    {
        Assert.Equal(ActorConditions.Milked | ActorConditions.Wet, Drawn(Soldier(milked: true), Dry));
        Assert.Equal(ActorConditions.None, Drawn(Soldier(), Dry));
    }

    /// <summary>Stock's wet: any water at his feet (<c>GetWaterLevel() &gt; WL_NotInWater</c>).</summary>
    [Fact]
    public void AnEnemyInWaterReadsWet() => Assert.Equal(ActorConditions.Wet, Drawn(Soldier(), new TFBody(1, OnGround: true)));

    /// <summary>Stock's wet: gas on him.</summary>
    [Fact]
    public void AGassedEnemyReadsWet() => Assert.Equal(ActorConditions.Wet, Drawn(Soldier(gassed: true), Dry));

    /// <summary>Out of the water and soaked in nothing, off the ground or on it, he is dry.</summary>
    [Fact]
    public void ADryEnemyReadsNothing() => Assert.Equal(ActorConditions.None, Drawn(Soldier(), new TFBody(0, OnGround: false)));

    /// <summary>A movement state's <c>FL_ONGROUND</c> is the body's ground, and its water level the body's water.</summary>
    [Fact]
    public void TheMovementStatesGroundFlagIsTheBodysGround()
    {
        Assert.Equal(new TFBody(2, OnGround: true), TFBody.Of(2, (uint)SourceSharp.Abi.GameMovement.FlOnGround));
        Assert.Equal(new TFBody(0, OnGround: false), TFBody.Of(0, 0));
    }

    [Fact]
    public void AMarkedEnemyReadsMarked()
    {
        Assert.Equal(ActorConditions.MarkedForDeath, Drawn(Soldier(marked: true), Dry));
        Assert.Equal(ActorConditions.None, Drawn(Soldier(), Dry));
    }

    /// <summary>Stock draws no mark over a cloaked or disguised player (c_tf_player.cpp:5183-5187); his own HUD shows him his.</summary>
    [Fact]
    public void NoMarkIsDrawnOverACloakedOrDisguisedPlayerButHisOwnHudShowsIt()
    {
        Assert.Equal(ActorConditions.None, Drawn(Soldier(marked: true, cloaked: true), Dry) & ActorConditions.MarkedForDeath);
        Assert.Equal(ActorConditions.None, Drawn(Soldier(marked: true, disguised: true), Dry) & ActorConditions.MarkedForDeath);
        TFActorFacts own = Soldier(marked: true, disguised: true);
        Assert.Equal(ActorConditions.MarkedForDeath, TFBotGameView.Conditions(in own, disguiseShown: true, Dry, own: true) & ActorConditions.MarkedForDeath);
    }

    /// <summary>Thrown by a blast: off the ground and out of the water with the blast-jump condition; on the ground, or in water, not.</summary>
    [Fact]
    public void AnEnemyABlastThrewReadsAirborneOnlyOffTheGroundAndOutOfWater()
    {
        Assert.Equal(ActorConditions.BlastAirborne, Drawn(Soldier(blast: true), InAir));
        Assert.Equal(ActorConditions.None, Drawn(Soldier(blast: true), Dry));
        Assert.Equal(ActorConditions.Wet, Drawn(Soldier(blast: true), new TFBody(1, OnGround: false)));
        Assert.Equal(ActorConditions.None, Drawn(Soldier(), InAir));
    }

    /// <summary>A scoped Sniper is drawn scoped; one not zoomed, not.</summary>
    [Fact]
    public void AScopedSniperReadsZoomed()
    {
        Assert.Equal(ActorConditions.Zoomed, Drawn(Soldier() with { Class = TFClass.Sniper, Zoomed = true }, Dry));
        Assert.Equal(ActorConditions.None, Drawn(Soldier() with { Class = TFClass.Sniper }, Dry));
    }

    /// <summary>A Thermal Thruster's flight counts as stock's does, whatever the ground read.</summary>
    [Fact]
    public void AThrusterFlightReadsAirborne()
        => Assert.Equal(ActorConditions.BlastAirborne, Drawn(Soldier(rocketpack: true), Dry));

    // ---- through the senses: a seen enemy reads, an unseen one is not in the view

    private sealed class Facts : ITFActorFacts
    {
        public readonly Dictionary<int, TFActorFacts> By = new();

        public bool TryRead(int playerIndex, out TFActorFacts facts) => By.TryGetValue(playerIndex, out facts);
    }

    private sealed class NoPlayers : ITFPlayerData
    {
        public TFPlayerNetState? Predicted(int playerIndex) => null;

        public TFPlayerNetState? Networked(int playerIndex) => null;
    }

    private sealed class Rounds : ITFRoundStateView
    {
        public RoundState State => RoundState.RoundRunning;

        public TFTeam Winner => TFTeam.Unassigned;
    }

    /// <summary>The TF2 view's player drawing, with no things and no HUD: what the senses ask of it for a player.</summary>
    private sealed class PlayersOnly(TFBotGameView tf) : IBotGameView
    {
        public int Renders;

        public SelfState Hud(EntityHandle bot, in SelfState read) => read;

        public GameHud? HudOf(EntityHandle bot) => null;

        public bool Render(EntityHandle viewer, EntityHandle them, ref ActorFacts facts)
        {
            Renders++;
            return tf.Render(viewer, them, ref facts);
        }

        public ActorId ShownAs(EntityHandle viewer, EntityHandle them, ActorId real) => tf.ShownAs(viewer, them, real);

        public bool Solid(EntityHandle viewer, EntityHandle them) => false;

        public void Things(List<ThingRead> into) => into.Clear();

        public bool RenderThing(EntityHandle viewer, ref ThingFacts facts) => true;

        public ObjectiveKnowledge? Objectives => null;

        public bool OwnKillFeed => false;

        public bool Frozen(EntityHandle bot) => false;
    }

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

    private sealed class Hud : IBotHud
    {
        public SelfState Read(EntityHandle bot) => new(default, default, default, 200, 0, 3, 4, 20, SelfFlags.Alive);
    }

    private static readonly VisionParams Eyes = new(6000f, 90f, 0.3f, 0.5f);

    private readonly SenseWorld _w = new();
    private readonly Facts _facts = new();
    private readonly SensedBot _bot;
    private readonly VisionPass _pass;
    private readonly SnapshotBuilder _builder;
    private readonly PlayersOnly _view;
    private readonly SnapshotRing _ring = new(4, VisionPass.Slots, 8);
    private int _tick;

    public TFViewConditionsTests()
    {
        SenseWorld.Player bot = _w.Add(1, new AiVector(0, 0, 0));
        SenseWorld.Player them = _w.Add(2, new AiVector(500, 0, 0));
        // By the handle's entry, as the view asks the mod (the sense world's handles carry a serial above the slot).
        _facts.By[bot.Handle.EntryIndex] = Soldier() with { Team = TFTeam.Red };
        _facts.By[them.Handle.EntryIndex] = Soldier(jarated: true, marked: true);
        var tf = new TFBotGameView(_facts, new NoPlayers(), _w.Entities(), DispatchProxy.Create<IEntityDirectory, TFViewWiringTests.Unasked>(),
                                   new TFDisguiseWeapons(null!, null!, null!, null!, null!, null!), WeaponCatalog.Stock(), null!, new Rounds(),
                                   DeclaredConVars.Fresh<TFPlayerMovementRestartFreezeConVar>(), null!, new TFPlayerLives(),
                                   DeclaredConVars.Fresh<TFAvoidTeammatesConVar>(), new TFViewWiringTests.Bodies());
        _view = new PlayersOnly(tf);
        _pass = new VisionPass(_w.Engine(), _w.Entities(), _w.Traces());
        _bot = new SensedBot(new ActorId(1, 1), bot.Handle, Eyes);
        _pass.Add(_bot.Id);
        var game = new Game(_view);
        _builder = new SnapshotBuilder(_w.Engine(), WeaponCatalog.Stock(), new Hud(), _w.Stance(), new FakeTeams(), game,
                                       new ThingSight(_w.Engine(), _w.Entities(), _w.Traces(), game), new ShownActors(game));
        for (_tick = 1; _tick <= 60; _tick++)
            _pass.Run(new Tick(_tick), 0.015f, [_bot], _w.Living(_bot), []);
    }

    private PerceivedView Build()
    {
        int tick = _tick++;
        _pass.Run(new Tick(tick), 0.015f, [_bot], _w.Living(_bot), []);
        AiSnapshot snap = _ring.BeginWrite(new Tick(tick));
        _builder.Build(snap, [_bot], _pass, _ => ActorActivity.None);
        return snap.ViewFor(1)!;
    }

    /// <summary>The senses' gate: a Jarated, marked enemy in plain sight reads both; behind a wall he is not in the view, and the game is not asked to draw him.</summary>
    [Fact]
    public void ASeenEnemysConditionsReadAndAnUnseenOnesNever()
    {
        ActorFacts seen = Assert.Single(Build().Sighted.ToArray());
        Assert.Equal(ActorConditions.Jarated | ActorConditions.Wet | ActorConditions.MarkedForDeath, seen.Conditions);

        _w.Occluder(new AiVector(240, -200, -100), new AiVector(260, 200, 300), SenseWorld.Solid);
        for (int i = 0; i < 120; i++)
            Build();
        int asked = _view.Renders;
        Assert.Empty(Build().Sighted.ToArray());
        Assert.Equal(asked, _view.Renders);
    }
}
