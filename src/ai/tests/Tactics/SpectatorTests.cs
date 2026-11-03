using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Chat;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Ai.Tests.Trees;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// To a bot a spectator is not there (plan_hl2mp_ai §4.1, user ruling 2026-11-02: "bots
/// should ignore spectators as well"): no sight, no sound, no belief, no rank, no grudge
/// at work, in nobody's way; a person for the slots, and new when they come back.
/// </summary>
public sealed class SpectatorTests
{
    private static readonly AiVector There = new(500f, 0f, 0f);

    private static SkillProfile Sprayer()
    {
        SkillProfile p = Duel.Preset("normal");
        return p.With(p.IndexOf("cunning"), 10).With(p.IndexOf("discipline"), 0);
    }

    private static AiEvent Watching(Duel d, ActorId who, bool now = true) => AiEvent.ActorSpectating(Duel.Bot, new Tick(d.Tick + 6), who, now);

    private static AiEvent Lost(Duel d, AiVector at) => AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, at, default);

    private static AiEvent[] Answer(Duel d)
    {
        DoingRecord doing = d.Decider.Doing;
        return doing.LineAsk > 0 ? [AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 1), doing.LineAt, doing.LineAsk, true)] : [];
    }

    // ---- who it is ----------------------------------------------------------

    /// <summary>Team spectator, or watching while not dead; someone dead and waiting to respawn is in the game, in an observer mode as they are.</summary>
    [Theory]
    [InlineData(1, ObserverMode.Roaming, false, true)]
    [InlineData(1, ObserverMode.None, true, true)]
    [InlineData(0, ObserverMode.Roaming, true, true)]
    [InlineData(0, ObserverMode.Roaming, false, false)]
    [InlineData(0, ObserverMode.None, true, false)]
    [InlineData(0, ObserverMode.None, false, false)]
    public void WhoIsWatching(int team, ObserverMode mode, bool alive, bool watching)
        => Assert.Equal(watching, Spectators.Is(team, mode, alive));

    // ---- vision, the camera, nobody's way --------------------------------------

    /// <summary>
    /// Not an actor, so not a candidate: no read of where it is, no trace budget, no
    /// place on the list GiveWay reads, however alive the engine says it is. The control is a player beside it.
    /// </summary>
    [Fact]
    public void ASpectatorIsNoActor()
    {
        var w = new SenseWorld();
        SenseWorld.Player playing = w.Add(2, new AiVector(100, 0, 0));
        SenseWorld.Player watching = w.Add(3, new AiVector(200, 0, 0));
        watching.Team = Observers.TeamSpectator;
        var list = new List<SensedActor>();
        ActorDirectory.Living(w.Engine(), 4, list, _ => ActorId.None);
        Assert.Equal(new[] { playing.Handle }, list.Select(a => a.Player));
    }

    /// <summary>The field's rosters are solid classes: a player, watching or not, is on none, so it blocks no route.</summary>
    [Fact]
    public void NoPlayerIsOnTheFieldsRosters()
    {
        var w = new EngineObstacleWorld(FakeEngine.Of<IEngineEntities>((_, _) => null));
        for (int r = 0; r < w.RosterCount; r++)
            Assert.DoesNotMatch("(^|_)player$", EngineObstacleWorld.RosterClass(r));
    }

    // ---- hearing --------------------------------------------------------------

    /// <summary>Nothing they emit is a noise to go looking for; the control is a player's, and a sound of nobody's.</summary>
    [Fact]
    public void ASpectatorIsNotHeard()
    {
        var w = new SenseWorld();
        w.Add(2, default);
        w.Add(3, default).Team = Observers.TeamSpectator;
        Assert.True(SoundTap.Hushed(w.Engine(), 3));
        Assert.False(SoundTap.Hushed(w.Engine(), 2));
        Assert.False(SoundTap.Hushed(w.Engine(), 0));
    }

    // ---- memory ------------------------------------------------------------------

    /// <summary>One who goes to watch is forgotten, position, belief and sounds; the bot stops being about them.</summary>
    [Fact]
    public void OneWhoGoesToWatchIsForgotten()
    {
        var d = new Duel(Duel.Preset("normal"));
        d.Step(d.Self(), [(Duel.Opponent, There)], d.SeeAt(Duel.Opponent, There));
        d.Step(d.Self(), [], Lost(d, There), AiEvent.SoundHeard(Duel.Bot, new Tick(d.Tick + 6), SoundKind.Footstep, There, 1f, Duel.Opponent, -1));
        KnownActor a = d.Memory.Board.Of(Duel.Opponent, make: false)!;
        Assert.True(a.Known && a.Confidence > 0.5f && d.Memory.Board.InvestigationCount > 0, "the control: it was remembered");
        d.Step(d.Self(), [], Watching(d, Duel.Opponent));
        Assert.False(a.Known);
        Assert.False(a.EverSeen);
        Assert.Equal(0f, a.Confidence);
        Assert.Equal(0, d.Memory.Board.InvestigationCount);
        Assert.Equal(ActorId.None, d.Decider.Doing.Target);
    }

    /// <summary>The inspector's memory does not list them.</summary>
    [Fact]
    public void TheInspectorDoesNotListThem()
    {
        var d = new Duel(Duel.Preset("normal"));
        d.Step(d.Self(), [(Duel.Opponent, There)], d.SeeAt(Duel.Opponent, There));
        InspectMemory Memory() => BotInspect.Memory(d.Memory, new Tick(d.Tick));
        Assert.Contains(Memory().Actors, x => x.Slot == Duel.Opponent.Slot);
        d.Step(d.Self(), [], Watching(d, Duel.Opponent));
        Assert.DoesNotContain(Memory().Actors, x => x.Slot == Duel.Opponent.Slot);
    }

    /// <summary>Nothing is learnt of them meanwhile, whatever a tap lets through.</summary>
    [Fact]
    public void NothingIsLearntWhileTheyWatch()
    {
        var d = new Duel(Duel.Preset("normal"));
        d.Step(d.Self(), [], Watching(d, Duel.Opponent));
        d.Step(d.Self(), [], [.. d.SeeAt(Duel.Opponent, There),
                              AiEvent.SoundHeard(Duel.Bot, new Tick(d.Tick + 6), SoundKind.Gunfire, There, 1f, Duel.Opponent, Duel.Smg1),
                              d.Row(Duel.Opponent, 30, 1), d.Shots(Duel.Opponent, 3)]);
        KnownActor a = d.Memory.Board.Of(Duel.Opponent, make: false)!;
        Assert.False(a.Known);
        Assert.Equal((false, 0, 0f), (a.EverSeen, a.Rank, a.Confidence));
        Assert.False(d.Memory.TryHabits(Duel.Opponent, out _));
    }

    /// <summary>Back in the game they are new: nothing is remembered of them, and they can be seen.</summary>
    [Fact]
    public void BackInTheGameTheyAreNew()
    {
        var d = new Duel(Duel.Preset("normal"));
        d.Step(d.Self(), [(Duel.Opponent, There)], d.SeeAt(Duel.Opponent, There));
        d.Step(d.Self(), [], Watching(d, Duel.Opponent));
        d.Step(d.Self(), [], Watching(d, Duel.Opponent, now: false));
        KnownActor a = d.Memory.Board.Of(Duel.Opponent, make: false)!;
        Assert.False(a.EverSeen);
        IntentCommand i = d.Step(d.Self(), [(Duel.Opponent, There)], d.SeeAt(Duel.Opponent, There));
        Assert.True(a.EverSeen);
        Assert.Equal(Duel.Opponent, i.Look.Actor);
        Assert.Equal("Fight", d.Decider.Doing.Branch);
    }

    // ---- prefire -----------------------------------------------------------------

    /// <summary>A corner someone just went behind, and then they went to watch: no belief, no burst. The control is PrefireTests' same scene.</summary>
    [Fact]
    public void NoPrefireAtSomeoneGoneToWatch()
    {
        var d = new Duel(Sprayer());
        d.Step(d.Self(), [(Duel.Opponent, There)], d.SeeAt(Duel.Opponent, There));
        d.Step(d.Self(), [], Lost(d, There), Watching(d, Duel.Opponent));
        for (int k = 0; k < 40; k++)
        {
            Assert.NotEqual(ActionKind.Fire, d.Step(d.Self(), [], Answer(d)).Action.Kind);
            Assert.Equal("", d.Decider.Doing.Prefire);
        }
    }

    // ---- targeting ------------------------------------------------------------------

    /// <summary>The leader who goes to watch is hunted no more, by the bot that hunts leaders: the control hunts them first.</summary>
    [Fact]
    public void AHeadhunterDoesNotHuntASpectator()
    {
        SkillProfile p = Duel.Preset("normal");
        p = p.With(p.IndexOf("headhunter"), 10);
        var d = new Duel(p);
        d.Step(d.Self(), [(Duel.Opponent, There)], [.. d.SeeAt(Duel.Opponent, There), d.Row(Duel.Opponent, 30, 1), d.Row(Duel.Other, 0, 2)]);
        d.Step(d.Self(), [], Lost(d, There));
        Assert.Equal(Duel.Opponent, d.Decider.Doing.Target);
        d.Step(d.Self(), [], Watching(d, Duel.Opponent));
        d.Step(d.Self(), []);
        Assert.Equal(ActorId.None, d.Decider.Doing.Target);
    }

    /// <summary>The ranks are of those in the game: a spectator with the most frags has no row, and second place leads.</summary>
    [Fact]
    public void TheScoreboardIsOfThoseInTheGame()
    {
        var w = new SenseWorld();
        SenseWorld.Player top = w.Add(2, default), next = w.Add(3, default);
        top.Team = Observers.TeamSpectator;
        var rows = new List<ScoreRow>();
        (EntityHandle, PlayerRecord)[] all = [(top.Handle, new PlayerRecord { Kills = 30 }), (next.Handle, new PlayerRecord { Kills = 3 })];
        CombatTap.RowsInTheGame(all, h => (int)h.Value & 0x1FFF, _ => ActorId.None, w.Engine(), rows);
        ScoreRow only = Assert.Single(rows);
        Assert.Equal(3, only.Frags);
        Assert.Equal(new[] { 1 }, CombatTap.Ranks(rows));
    }

    /// <summary>Going and coming back each say so once; nothing is said for one who stays.</summary>
    [Fact]
    public void GoingAndComingBackAreToldOnce()
    {
        var watched = new ActorId[8];
        var who = new ActorId(3, 0x4000_0007);
        Assert.False(CombatTap.WatchingChanged(watched, 3, who, false, out _));
        Assert.True(CombatTap.WatchingChanged(watched, 3, who, true, out bool going));
        Assert.True(going);
        Assert.False(CombatTap.WatchingChanged(watched, 3, who, true, out _));
        Assert.True(CombatTap.WatchingChanged(watched, 3, who, false, out bool still));
        Assert.False(still);
        Assert.False(CombatTap.WatchingChanged(watched, 3, who, false, out _));
    }

    // ---- grudges -------------------------------------------------------------------

    /// <summary>A grudge goes dormant: no target while they watch, and it is there when they are back.</summary>
    [Fact]
    public void AGrudgeGoesDormant()
    {
        SkillProfile p = Duel.Preset("normal");
        p = p.With(p.IndexOf("tenacity"), 10);
        var d = new Duel(p);
        d.Step(d.Self(), [], AiEvent.Killed(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent), AiEvent.Killed(Duel.Bot, new Tick(d.Tick + 7), Duel.Opponent));
        KnownActor a = d.Memory.Board.Of(Duel.Opponent, make: false)!;
        float grudge = a.GrudgeAt(new Tick(d.Tick), 0.015f);
        Assert.True(grudge > 1f, $"the control: {grudge}");
        d.Step(d.Self(), [], Watching(d, Duel.Opponent));
        Assert.Equal(ActorId.None, d.Decider.Doing.Target);
        Assert.True(a.GrudgeAt(new Tick(d.Tick), 0.015f) > 1f, "gone, not forgiven");
        d.Step(d.Self(), [], Watching(d, Duel.Opponent, now: false));
        d.Step(d.Self(), [(Duel.Opponent, There)], d.SeeAt(Duel.Opponent, There));
        d.Step(d.Self(), [(Duel.Opponent, There)]);
        Assert.True(d.Decider.Doing.GrudgeTerm > 0f, "back, and it counts");
    }

    /// <summary>Nothing it knows of them while they watch adds to a grudge: a kill told of them is no kill of theirs.</summary>
    [Fact]
    public void NoGrudgeAccruesWhileTheyWatch()
    {
        var d = new Duel(Duel.Preset("normal"));
        d.Step(d.Self(), [], Watching(d, Duel.Opponent));
        d.Step(d.Self(), [], AiEvent.Killed(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent));
        Assert.Equal(0f, d.Memory.Board.Of(Duel.Opponent, make: false)!.GrudgeAt(new Tick(d.Tick), 0.015f));
    }

    // ---- chat ---------------------------------------------------------------------

    /// <summary>A spectator atop the board is no leader to talk about, and no rival above the bot; the control counts them when they play.</summary>
    [Fact]
    public void ChatsBoardHasNoSpectatorOnIt()
    {
        var a = new EntityHandle(1u | (7u << 13));
        var top = new EntityHandle(2u | (7u << 13));
        (EntityHandle, PlayerRecord)[] all = [(a, new PlayerRecord { Kills = 3 }), (top, new PlayerRecord { Kills = 30 })];
        static int Slot(EntityHandle h) => (int)h.Value & 0x1FFF;
        ChatScore playing = Hl2mpBotChat.Score(1, all, Slot, _ => false, _ => "Top");
        Assert.Equal((2, 2, "Top"), (playing.Rank, playing.Players, playing.Leader));
        ChatScore watching = Hl2mpBotChat.Score(1, all, Slot, h => h == top, _ => "Top");
        Assert.Equal((1, 1, "you"), (watching.Rank, watching.Players, watching.Leader));
    }
}
