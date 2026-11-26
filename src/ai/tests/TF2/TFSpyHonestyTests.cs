using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using TF2.Server.Player;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Spy's honesty fix (plan_bots_teams_tf2 C.3, :619-656; the P7 Spy research §4.1 and its review): a disguised Spy is,
/// to everyone his disguise fools, an actor of his own (an alias), one per disguise and the same for every viewer, drawn in
/// the borrowed name's slot. Nothing the bot learnt of the real player (his scoreboard team, his frags, the grudge for the
/// time he killed it) reaches the disguise's row, and nothing of the disguise reaches his.
/// </summary>
public sealed class TFSpyHonestyTests
{
    private const int Red = (int)TFTeam.Red, Blue = (int)TFTeam.Blue;
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    private sealed class Facts : ITFActorFacts
    {
        public readonly Dictionary<int, TFActorFacts> By = new();

        public bool TryRead(int playerIndex, out TFActorFacts facts) => By.TryGetValue(playerIndex, out facts);
    }

    private static TFActorFacts Player(TFClass c, TFTeam team)
        => new(c, team, TFClass.Undefined, TFTeam.Unassigned, 0, 0f, 0, Burning: false, Stunned: false, Ubered: false, Disguised: false,
               Cloaked: false, Taunting: false);

    /// <summary>A BLU Spy dressed as a RED <paramref name="dress"/> borrowing <paramref name="name"/>'s name.</summary>
    private static TFActorFacts Spy(TFClass dress = TFClass.Medic, int name = 6)
        => new(TFClass.Spy, TFTeam.Blue, dress, TFTeam.Red, name, 0f, 0, Burning: false, Stunned: false, Ubered: false, Disguised: true,
               Cloaked: false, Taunting: false);

    private static EntityHandle Slot(int index) => new((uint)(0x0001_0000 | index));

    private readonly Facts _facts = new();

    // Only the actors' facts are asked of the view by ShownAs; the rest is never reached.
    private TFBotGameView View() => new(_facts, null!, null!, null!, null!, null!, null!, null!, null!, null!);

    private static readonly ActorId SpyActor = new(5, 4);

    [Fact]
    public void AnAliasesRowIsApartFromTheRealPlayersInItsSlot()
    {
        var board = new Blackboard(8);
        var real = new ActorId(6, 3);
        KnownActor alice = board.Of(real, make: true)!;
        KnownActor drawn = board.Of(ActorId.Alias(6, 1), make: true)!;
        Assert.NotSame(alice, drawn);
        // The control the fix holds: making the alias's row left the real player's as it was.
        Assert.Same(alice, board.Of(real, make: false));
        Assert.Same(drawn, board.Drawn(6));
    }

    /// <summary>
    /// No serial a recording gives a person is an alias (review F1): the anonymiser draws 31 random bits over the person's
    /// mark, and each anonymised person's row is the real players' row of its slot, not an alias's. With the alias mark on
    /// bit 29 half of them went to the alias rows and the replay lost them.
    /// </summary>
    [Fact]
    public void NoAnonymisedSerialIsEverAnAlias()
    {
        const int person = 0x4000_0000;
        var anonymiser = new ActorAnonymiser(_ => true, person, 7);
        var board = new Blackboard(33);
        int drawn = 0;
        for (int i = 1; i <= 2000; i++)
        {
            ActorId recorded = anonymiser.Map(new ActorId(1 + (i % 32), person | i));
            Assert.Same(board.Actor(recorded.Slot), board.Of(recorded, make: true));
            drawn++;
        }

        Assert.Equal(2000, drawn);
        // The control: an alias goes to the drawn row of its slot.
        Assert.Same(board.Drawn(6), board.Of(ActorId.Alias(6, 1), make: true));
    }

    /// <summary>The review's shape: one alias per Spy and disguise, shared by every viewer the disguise fools, in the borrowed name's slot.</summary>
    [Fact]
    public void ADisguiseIsOneAliasToEveryEnemyAndTheRealPlayerToHisTeam()
    {
        _facts.By[2] = Player(TFClass.Soldier, TFTeam.Red);
        _facts.By[3] = Player(TFClass.Pyro, TFTeam.Red);
        _facts.By[4] = Player(TFClass.Scout, TFTeam.Blue);
        _facts.By[5] = Spy();
        TFBotGameView view = View();
        ActorId toTwo = view.ShownAs(Slot(2), Slot(5), SpyActor), toThree = view.ShownAs(Slot(3), Slot(5), SpyActor);
        Assert.NotEqual(SpyActor, toTwo);
        Assert.Equal(toTwo, toThree);
        Assert.Equal(6, toTwo.Slot);
        Assert.Equal(SpyActor, view.ShownAs(Slot(4), Slot(5), SpyActor));
    }

    [Fact]
    public void AnotherDisguiseIsAnotherAliasAndTheDroppedOneIsTheRealPlayer()
    {
        _facts.By[2] = Player(TFClass.Soldier, TFTeam.Red);
        _facts.By[5] = Spy();
        TFBotGameView view = View();
        ActorId first = view.ShownAs(Slot(2), Slot(5), SpyActor);
        Assert.Equal(first, view.ShownAs(Slot(2), Slot(5), SpyActor));
        _facts.By[5] = Spy(TFClass.Engineer);
        ActorId second = view.ShownAs(Slot(2), Slot(5), SpyActor);
        Assert.NotEqual(first, second);
        _facts.By[5] = Player(TFClass.Spy, TFTeam.Blue);
        Assert.Equal(SpyActor, view.ShownAs(Slot(2), Slot(5), SpyActor));
        // The same dress again after a reveal is a disguise put on again: a third actor, not the first back.
        _facts.By[5] = Spy(TFClass.Engineer);
        ActorId third = view.ShownAs(Slot(2), Slot(5), SpyActor);
        Assert.NotEqual(second, third);
        Assert.Equal(3, view.AliasesMade);
    }

    /// <summary>
    /// The board leak (research §3.3 hole 2): the bot knows the real Spy (his scoreboard row says BLU, he killed it once).
    /// Disguised, the row its view names holds none of that: no scoreboard team, no frags, no grudge, and it is drawn on
    /// the bot's team, so <c>BoardTeam != Team</c> detects nothing. Before, the view named the real actor, whose row held them all.
    /// </summary>
    [Fact]
    public void ADisguisedSpysRowHoldsNoneOfTheRealPlayersMemory()
    {
        var f = new TFField();
        var tick = new Tick(1);
        f.Memory.Apply(AiEvent.ScoreboardRow(f.Bot, tick, SpyActor, 7, 2, 1, Blue, (int)TFClass.Spy));
        f.Memory.Apply(AiEvent.Killed(f.Bot, tick, SpyActor));
        _facts.By[2] = Player(TFClass.Soldier, TFTeam.Red);
        _facts.By[5] = Spy();
        ActorId shown = View().ShownAs(Slot(2), Slot(5), SpyActor);
        var drawn = new ActorFacts(shown, Mid + new AiVector(400f, 0f, 0f), AiVector.Zero, TFField.Shotgun, ActorActivity.None, 1f, Blue, NameSlot: 5);
        TFBotGameView.Draw(Red, _facts.By[5], -1, ref drawn);
        f.StepHearing(TFField.Self(Mid), TFField.Ctf(), [AiEvent.SightGained(f.Bot, new Tick(f.Tick + 1), shown, drawn.Origin, default, 1f)], drawn);

        KnownActor row = f.Memory.Board.Of(drawn.Id, make: false)!;
        Assert.Equal((0, 0, Red), (row.BoardTeam, row.Frags, row.Team));
        Assert.Equal(0f, row.GrudgeAt(new Tick(f.Tick), 0.015f));
        KnownActor real = f.Memory.Board.Of(SpyActor, make: false)!;
        Assert.Equal((Blue, 7), (real.BoardTeam, real.Frags));
        Assert.True(real.GrudgeAt(new Tick(f.Tick), 0.015f) > 0f);
        Assert.False(real.InSight);
    }

    /// <summary>
    /// Research fact 1 through the real drawing: two bots that know the BLU Spy from the scoreboard alone. The one that sees
    /// him undressed fights him (the control); the one that sees the same Spy, at the same place, in a RED disguise with no
    /// cue targets nobody and holds its fire.
    /// </summary>
    [Fact]
    public void ADisguisedSpyWithNoCueIsNeverTargeted()
    {
        _facts.By[2] = Player(TFClass.Soldier, TFTeam.Red);
        AiVector at = Mid + new AiVector(500f, 0f, 0f);
        _facts.By[5] = Player(TFClass.Spy, TFTeam.Blue);
        TFField bareField = Knowing();
        var bare = new ActorFacts(View().ShownAs(Slot(2), Slot(5), SpyActor), at, AiVector.Zero, TFField.Shotgun, ActorActivity.None, 1f, Blue, NameSlot: 5);
        TFBotGameView.Draw(Red, _facts.By[5], -1, ref bare);
        Seen(bareField, bare);
        Assert.Equal(SpyActor, bareField.Decider.Doing.Target);

        _facts.By[5] = Spy();
        TFField dressedField = Knowing();
        var dressed = new ActorFacts(View().ShownAs(Slot(2), Slot(5), SpyActor), at, AiVector.Zero, TFField.Shotgun, ActorActivity.None, 1f, Blue, NameSlot: 5);
        TFBotGameView.Draw(Red, _facts.By[5], -1, ref dressed);
        IntentCommand i = Seen(dressedField, dressed);
        Assert.Equal(ActorId.None, dressedField.Decider.Doing.Target);
        Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
    }

    /// <summary>A bot that knows the Spy from his scoreboard row only: BLU, a Spy.</summary>
    private static TFField Knowing()
    {
        var f = new TFField();
        f.Memory.Apply(AiEvent.ScoreboardRow(f.Bot, new Tick(1), SpyActor, 3, 1, 2, Blue, (int)TFClass.Spy));
        f.Memory.Apply(AiEvent.ActorTeam(f.Bot, new Tick(1), SpyActor, Blue, 0));
        return f;
    }

    /// <summary>Two decides with <paramref name="drawn"/> in sight, the first told by its sighting.</summary>
    private static IntentCommand Seen(TFField f, ActorFacts drawn)
    {
        f.StepHearing(TFField.Self(Mid), TFField.Ctf(), [AiEvent.SightGained(f.Bot, new Tick(f.Tick + 1), drawn.Id, drawn.Origin, default, 1f)], drawn);
        return f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), drawn);
    }
}
