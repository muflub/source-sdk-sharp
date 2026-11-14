using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Whom a TF2 bot fights (plan_bots_teams_tf2_part_g G.2.1) on the synthetic field
/// (<see cref="TFField"/>): the core's score (nearness, a hold on the one it is already about, a
/// grudge, the scoreboard's rank, someone close first) with TF2's own terms: the enemy carrying
/// the bot's flag before anyone, then a Medic before another class as near.
/// </summary>
public sealed class TFTargetTests
{
    private static readonly ActorId A = new(5, 0x4000_0001), B = new(6, 0x4000_0002);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    private static ActorFacts Seen(ActorId id, float east, TFClass drawnAs = TFClass.Soldier, float north = 0f)
        => new(id, Mid + new AiVector(east, north, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)drawnAs);

    private static ActorId Target(TFField f, ObjectiveKnowledge? hud, params ActorFacts[] sighted)
    {
        f.StepDrawn(TFField.Self(Mid), hud ?? TFField.Ctf(), sighted);
        return f.Decider.Doing.Target;
    }

    [Fact]
    public void OfTwoEnemiesInSightTheNearerIsTheTarget()
        => Assert.Equal(A, Target(new TFField(), null, Seen(B, 760f), Seen(A, 700f)));

    [Fact]
    public void ATargetIsNotDroppedForOneSlightlyNearer()
    {
        var f = new TFField();
        Assert.Equal(A, Target(f, null, Seen(A, 700f), Seen(B, 760f)));
        for (int i = 0; i < 5; i++)
            Assert.Equal(A, Target(f, null, Seen(A, 700f), Seen(B, 660f)));
    }

    /// <summary>The control for the fact above: the hold is a share, and an enemy much nearer does take over.</summary>
    [Fact]
    public void ATargetIsDroppedForOneMuchNearer()
    {
        var f = new TFField();
        Assert.Equal(A, Target(f, null, Seen(A, 1200f), Seen(B, 1260f)));
        Assert.Equal(A, Target(f, null, Seen(A, 1200f), Seen(B, 1140f)));
        Assert.Equal(B, Target(f, null, Seen(A, 1200f), Seen(B, 500f)));
    }

    /// <summary>After a life the hold is gone: the bot is about nobody when it comes back.</summary>
    [Fact]
    public void TheHoldDoesNotOutliveTheBotsDeath()
    {
        var f = new TFField();
        Assert.Equal(A, Target(f, null, Seen(A, 700f), Seen(B, 760f)));
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        Assert.Equal(B, Target(f, null, Seen(A, 700f), Seen(B, 660f)));
    }

    private static ObjectiveKnowledge OursCarriedBy(ActorId carrier, float east)
        => TFField.Ctf(red: ObjectiveState.Carried, redAt: Mid + new AiVector(east, 0f, 0f), redCarrier: carrier.Slot);

    [Fact]
    public void TheEnemyCarryingTheBotsFlagIsTheTargetBeforeOneFarNearer()
        => Assert.Equal(B, Target(new TFField(), OursCarriedBy(B, 900f), Seen(A, 200f), Seen(B, 900f)));

    /// <summary>The control: with the flag at home the same two are ranked as anyone, and the close one is the target.</summary>
    [Fact]
    public void WithTheFlagAtHomeTheSameEnemyIsNoOneSpecial()
        => Assert.Equal(A, Target(new TFField(), null, Seen(A, 200f), Seen(B, 900f)));

    [Fact]
    public void AMedicIsTheTargetBeforeAnotherClassALittleNearer()
        => Assert.Equal(B, Target(new TFField(), null, Seen(A, 600f), Seen(B, 760f, TFClass.Medic)));

    /// <summary>The control: the same two as Soldiers, and the nearer is the target.</summary>
    [Fact]
    public void TwoOfAClassAreRankedByNearness()
        => Assert.Equal(A, Target(new TFField(), null, Seen(A, 600f), Seen(B, 760f, TFClass.Soldier)));

    [Fact]
    public void TheCarrierComesBeforeAMedic()
        => Assert.Equal(B, Target(new TFField(), OursCarriedBy(B, 900f), Seen(A, 500f, TFClass.Medic), Seen(B, 900f)));

    [Fact]
    public void SomeoneCloseComesBeforeAMedicFurtherOff()
        => Assert.Equal(A, Target(new TFField(), null, Seen(A, 300f), Seen(B, 600f, TFClass.Medic)));

    [Fact]
    public void AGrudgeTakesItOverAnEnemyALittleNearer()
    {
        var f = new TFField(tune: p => p with { GrudgeWeight = 0.4f });
        Assert.Equal(A, Target(f, null, Seen(A, 700f), Seen(B, 760f)));
        // B kills the bot twice; back alive, with both in sight again.
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        f.Memory.Apply(AiEvent.Killed(f.Bot, new Tick(f.Tick), B));
        f.Memory.Apply(AiEvent.Killed(f.Bot, new Tick(f.Tick), B));
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        Assert.Equal(B, Target(f, null, Seen(A, 700f), Seen(B, 760f)));
    }

    /// <summary>
    /// Dominations as grudges (plan_bots_teams_tf2_part_g G.2.5): a grudge too light to take the bot off an enemy 160 u nearer
    /// does take it once its HUD marks the killer as its nemesis, and no longer after the revenge that takes the mark off.
    /// </summary>
    [Fact]
    public void ANemesisIsTheTargetOverAnEnemyNearerUntilTheMarkIsOff()
    {
        var f = new TFField(tune: p => p with { GrudgeWeight = 0.04f });
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        f.Memory.Apply(AiEvent.Killed(f.Bot, new Tick(f.Tick), B));
        f.Memory.Apply(AiEvent.Killed(f.Bot, new Tick(f.Tick), B));
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        Assert.Equal(A, Target(f, null, Seen(A, 600f), Seen(B, 760f)));

        f.Memory.Apply(AiEvent.Nemesis(f.Bot, new Tick(f.Tick), B, true));
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        Assert.Equal(B, Target(f, null, Seen(A, 600f), Seen(B, 760f)));

        f.Memory.Apply(AiEvent.Nemesis(f.Bot, new Tick(f.Tick), B, false));
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        Assert.Equal(A, Target(f, null, Seen(A, 600f), Seen(B, 760f)));
    }

    /// <summary>The control: at Tenacity's zero the same kills change nothing.</summary>
    [Fact]
    public void WithNoGrudgeWeightTheKillerIsNoOneSpecial()
    {
        var f = new TFField(tune: p => p with { GrudgeWeight = 0f });
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        f.Memory.Apply(AiEvent.Killed(f.Bot, new Tick(f.Tick), B));
        f.Memory.Apply(AiEvent.Killed(f.Bot, new Tick(f.Tick), B));
        f.StepDrawn(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf());
        Assert.Equal(A, Target(f, null, Seen(A, 700f), Seen(B, 760f)));
    }

    private static TFField Ranked(float rankBonus)
    {
        var f = new TFField(tune: p => p with { RankBonus = rankBonus });
        f.Memory.Apply(AiEvent.ScoreboardRow(f.Bot, new Tick(1), A, 2, 5, rank: 4));
        f.Memory.Apply(AiEvent.ScoreboardRow(f.Bot, new Tick(1), B, 9, 1, rank: 1));
        return f;
    }

    [Fact]
    public void TheEnemysLeaderIsTheTargetBeforeOneALittleNearerByHeadhuntersBonus()
        => Assert.Equal(B, Target(Ranked(0.5f), null, Seen(A, 700f), Seen(B, 760f)));

    [Fact]
    public void WithNoRankBonusTheLeaderIsNoOneSpecial()
        => Assert.Equal(A, Target(Ranked(0f), null, Seen(A, 700f), Seen(B, 760f)));

    /// <summary>Sight comes first: the carrier remembered, worth more than anyone, is still no target while another enemy is in sight.</summary>
    [Fact]
    public void AnEnemyInSightComesBeforeTheCarrierRemembered()
    {
        var f = new TFField();
        Assert.Equal(B, Target(f, OursCarriedBy(B, 450f), Seen(B, 450f)));
        Assert.Equal(A, Target(f, OursCarriedBy(B, 450f), Seen(A, 900f)));
        KnownActor carrier = f.Memory.Board.Actor(B.Slot);
        Assert.True(carrier.Confidence > 0.9f, $"the control: the carrier is remembered well, {carrier.Confidence:F2}");
    }

    /// <summary>With nobody in sight the remembered carrier is the one it is about, before a remembered enemy nearer.</summary>
    [Fact]
    public void OfTheRememberedTheCarrierComesFirst()
    {
        var f = new TFField();
        Target(f, null, Seen(A, 500f), Seen(B, 900f));
        Assert.Equal(B, Target(f, OursCarriedBy(B, 900f)));
    }

    /// <summary>The control: the same two remembered with the flag at home, and the nearer is the one.</summary>
    [Fact]
    public void OfTheRememberedWithTheFlagAtHomeTheNearerComesFirst()
    {
        var f = new TFField();
        Target(f, null, Seen(A, 500f), Seen(B, 900f));
        Assert.Equal(A, Target(f, null));
    }

    // ---------------------------------------------------------------- where an unseen enemy is believed

    /// <summary>A bot with no objective on its HUD that saw <see cref="A"/> run east from 600 u off, then lost it; stepped <paramref name="decides"/> more.</summary>
    private static TFField LostRunningEast(int decides)
    {
        var f = new TFField();
        ActorFacts running = Seen(A, 600f) with { Velocity = new AiVector(300f, 0f, 0f) };
        f.StepHearing(TFField.Self(Mid), null, [AiEvent.SightGained(f.Bot, new Tick(f.Tick + DecideSchedule.Period), A, running.Origin, running.Velocity, 1f)], running);
        f.StepHearing(TFField.Self(Mid), null, [AiEvent.SightLost(f.Bot, new Tick(f.Tick + DecideSchedule.Period), A, running.Origin, running.Velocity)]);
        for (int i = 0; i < decides; i++)
            f.StepHearing(TFField.Self(Mid), null, []);
        return f;
    }

    [Fact]
    public void TheChaseGoesToWhereTheEnemyIsBelievedToHaveGotTo()
    {
        TFField f = LostRunningEast(decides: 22);
        Assert.Equal(("chase", A), (f.Decider.Doing.Leaf, f.Decider.Doing.Target));
        AiVector lastSeen = f.Memory.Board.Actor(A.Slot).LastSeenPos;
        Assert.Equal(Mid.X + 600f, lastSeen.X);
        Assert.True(f.Decider.Doing.Goal.X > lastSeen.X + 128f, $"the chase's goal {f.Decider.Doing.Goal.X:F0}, last seen {lastSeen.X:F0}");
        Assert.Equal(f.Memory.Board.Actor(A.Slot).BelievedAt(f.Graph), f.Decider.Doing.Goal);
    }

    /// <summary>The moment it is lost the belief is where it was: the chase starts from there, and moves on with time.</summary>
    [Fact]
    public void TheBeliefGoesFurtherTheLongerTheEnemyIsUnseen()
    {
        float soon = LostRunningEast(decides: 10).Decider.Doing.Goal.X, later = LostRunningEast(decides: 30).Decider.Doing.Goal.X;
        Assert.True(later > soon + 64f, $"after 10 decides {soon:F0}, after 30 {later:F0}");
    }
}
