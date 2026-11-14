using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 Soldier's rocket at the place an enemy it lost from sight is believed to be (lane Gp, the core's prefire) on the
/// synthetic field (<see cref="TFField"/>): at the floor, only down a line the game answered clear, one rocket and then a
/// wait; never so near its blast takes the bot, so far or so late the belief is stale when it lands, with an enemy in
/// sight, with the flag in hand, with the rockets a jump needs, through a teammate, or with the switch off.
/// </summary>
public sealed class TFPrefireTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Mate = new(4, 0x4000_0003), Other = new(6, 0x4000_0002);
    private static readonly AiVector Mid = new(1024f, 256f, 0f);

    private static TFField Sure(TFFeelSwitches? feel = null, float chance = 1f, SourceSharp.Ai.TF2.Jump.IJumpAdvisor? jumps = null, int slot = 2)
        => new(slot: slot, feel: feel, jumps: jumps, tune: p => p with { PrefireChance = chance, PrefireThrift = 1f });

    private static ActorFacts Them(float east, float north = 0f)
        => new(Enemy, Mid + new AiVector(east, north, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);

    private static ActorFacts Friend(AiVector at)
        => new(Mate, at, AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Red, (int)TFClass.Soldier);

    /// <summary>Seen standing <paramref name="east"/> of the bot, then out of sight there.</summary>
    private static void SeeThenLose(TFField f, SelfState self, float east, ObjectiveKnowledge? objectives = null)
    {
        ActorFacts them = Them(east);
        f.StepHearing(self, objectives, [AiEvent.SightGained(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Enemy, them.Origin, AiVector.Zero, 1f)], them);
        f.StepHearing(self, objectives, [AiEvent.SightLost(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Enemy, them.Origin, AiVector.Zero)]);
    }

    /// <summary>The game's answer about the line the bot asked of at its last decide, as the vision pass gives it.</summary>
    private static AiEvent[] Answer(TFField f, bool clear)
    {
        DoingRecord d = f.Decider.Doing;
        return d.LineAsk > 0 ? [AiEvent.LineAnswer(f.Bot, new Tick(f.Tick + 1), d.LineAt, d.LineAsk, clear)] : [];
    }

    /// <summary>Decides, each answering the last ask as <paramref name="clear"/>; the intents.</summary>
    private static List<IntentCommand> Run(TFField f, Func<int, SelfState> self, int decides, bool clear = true, ObjectiveKnowledge? objectives = null,
                                           Func<int, ActorFacts[]>? sighted = null, Func<int, AiEvent[]>? more = null)
    {
        var intents = new List<IntentCommand>();
        for (int k = 0; k < decides; k++)
            intents.Add(f.StepHearing(self(k), objectives, [.. Answer(f, clear), .. more?.Invoke(k) ?? []], sighted?.Invoke(k) ?? []));
        return intents;
    }

    /// <summary>Decides with the line answered clear, where the rocket goes on the first press: the clip is one fewer from the decide after; <paramref name="after"/> gives the events of the k-th decide after it.</summary>
    private static List<IntentCommand> FireOnce(TFField f, int decides, Func<TFField, int, AiEvent[]> after)
    {
        var intents = new List<IntentCommand>();
        int fired = -1;
        for (int k = 0; k < decides; k++)
        {
            AiEvent[] events = [.. Answer(f, true), .. fired >= 0 ? after(f, k - fired) : []];
            IntentCommand i = f.StepHearing(TFField.Self(Mid, clip: fired >= 0 ? 3 : 4), null, events);
            intents.Add(i);
            if (fired < 0 && Prefired(i))
                fired = k;
        }

        return intents;
    }

    private static bool Prefired(IntentCommand i) => i.Action.Kind == ActionKind.Fire && i.Look.Kind == LookKind.CornerCheck;

    private static int Refused(TFField f, TFPrefireRefusal why) => f.Decider.PrefireRefused[(int)why];

    [Fact]
    public void ItFiresARocketAtTheFloorWhereTheEnemyWentOutOfSight()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 600f);
        List<IntentCommand> intents = Run(f, _ => self, 3);
        IntentCommand fire = intents.Find(Prefired);
        Assert.True(Prefired(fire), $"branch {f.Decider.Doing.Branch}, leaf {f.Decider.Doing.Leaf}, prefire '{f.Decider.Doing.Prefire}'");
        // At the floor where it is believed (the chase's cell, by the place it was lost).
        Assert.Equal(TFKnowledgeFeet, fire.Look.Point.Z);
        Assert.True(new AiVector(fire.Look.Point.X, fire.Look.Point.Y, 0f).DistanceTo(Them(600f).Origin) < TFField.CellSize, $"at {fire.Look.Point}");
        Assert.Equal(("firing", Enemy), (f.Decider.Doing.Prefire, fire.Look.Actor));
        Assert.Equal(fire.Look.Point, f.Decider.Doing.LineAt);
        Assert.Equal((1, 1), (f.Decider.PrefireChances, f.Decider.PrefireAims));
    }

    private const float TFKnowledgeFeet = SourceSharp.Ai.TF2.Knowledge.TFKnowledge.FeetAim;

    [Fact]
    public void ABlockedLineIsNoRocket()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 600f);
        Assert.DoesNotContain(Run(f, _ => self, 20, clear: false), Prefired);
        Assert.True(Refused(f, TFPrefireRefusal.Blocked) > 0, "the stimulus: it asked, and was answered blocked");
    }

    [Fact]
    public void NoAnswerIsNoRocket()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 600f);
        for (int k = 0; k < 20; k++)
            Assert.False(Prefired(f.StepHearing(self, null, [])));
        Assert.True(f.Decider.Doing.LineAsk > 0, "the stimulus: it asked");
    }

    [Fact]
    public void OneRocketThenItWaits()
    {
        TFField f = Sure();
        SeeThenLose(f, TFField.Self(Mid), 600f);
        List<IntentCommand> intents = FireOnce(f, 20, (_, _) => []);
        int went = intents.FindIndex(Prefired);
        Assert.True(went >= 0, "the control: it fired");
        // The press, then the clip shows the rocket gone: never again, though the line stays clear.
        Assert.DoesNotContain(intents.Skip(went + 1), Prefired);
        Assert.Equal((1, 0), (f.Decider.PrefireRockets, f.Decider.PrefireGiveUps));
    }

    [Fact]
    public void APressThatSendsNothingIsGivenUp()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 600f);
        List<IntentCommand> intents = Run(f, _ => self, 20);
        int firing = intents.Count(Prefired);
        // A second apart, at most: 1 s of decides of 0.09 s.
        Assert.InRange(firing, 1, (int)(TFDecider.PrefireGiveUpSeconds / 0.09f) + 1);
        Assert.Equal((1, 0), (f.Decider.PrefireGiveUps, f.Decider.PrefireRockets));
        Assert.False(Prefired(intents[^1]));
    }

    [Fact]
    public void TooNearForItsOwnBlastItHolds()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 250f);
        Assert.DoesNotContain(Run(f, _ => self, 12), Prefired);
        Assert.True(Refused(f, TFPrefireRefusal.Near) > 0);
    }

    [Fact]
    public void TooFarItHolds()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 1700f);
        Assert.Equal(Enemy, f.Decider.Doing.Target);
        Assert.DoesNotContain(Run(f, _ => self, 12), Prefired);
        Assert.True(Refused(f, TFPrefireRefusal.Far) > 0);
    }

    /// <summary>1,400 u is 1.3 s of a rocket's flight: answered at once it goes, answered 1.4 s late the belief would be stale when it landed.</summary>
    [Fact]
    public void ABeliefStaleWhenTheRocketWouldLandIsNotFiredAt()
    {
        TFField soon = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(soon, self, 1400f);
        Assert.Contains(Run(soon, _ => self, 4), Prefired);

        TFField late = Sure();
        SeeThenLose(late, self, 1400f);
        for (int k = 0; k < 16; k++)
            Assert.False(Prefired(late.StepHearing(self, null, [])));
        Assert.DoesNotContain(Run(late, _ => self, 6), Prefired);
        Assert.True(Refused(late, TFPrefireRefusal.Old) > 0);
    }

    [Fact]
    public void ATeammateInSightIsNoReasonToStop()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 600f);
        Assert.Contains(Run(f, _ => self, 4, sighted: _ => [Friend(Mid + new AiVector(-300f, 0f, 0f))]), Prefired);
    }

    [Fact]
    public void AnotherEnemyInSightStopsIt()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 600f);
        // In the blue respawn room nobody is fought, so the one in sight is not the target, and the one lost still is.
        ActorFacts inTheirRoom = new(Other, new AiVector(4000f, 256f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);
        Assert.DoesNotContain(Run(f, _ => self, 12, sighted: _ => [inTheirRoom]), Prefired);
        Assert.Equal(Enemy, f.Decider.Doing.Target);
    }

    [Fact]
    public void WithTheFlagInHandItRuns()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        ObjectiveKnowledge carrying = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: f.Bot.Slot);
        SeeThenLose(f, self, 600f, carrying);
        Assert.DoesNotContain(Run(f, _ => self, 12, objectives: carrying), Prefired);
        Assert.Equal(TFDecider.Deliver, f.Branch);
    }

    [Fact]
    public void ItKeepsTheRocketsAJumpNeeds()
    {
        int Fired(int clip, bool jumper)
        {
            TFField f = Sure(jumps: jumper ? new TFJumpAdvice(null) : null);
            SelfState self = TFField.Self(Mid, clip: clip);
            SeeThenLose(f, self, 600f);
            return Run(f, _ => self, 4).Count(Prefired);
        }

        Assert.Equal(0, Fired(TFDecider.RocketJumpRockets, jumper: true));
        Assert.True(Fired(TFDecider.RocketJumpRockets + 1, jumper: true) > 0);
        Assert.True(Fired(1, jumper: false) > 0);
    }

    [Fact]
    public void TheShotgunDoesNotPrefire()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid, weapon: TFField.Shotgun, clip: 6);
        SeeThenLose(f, self, 600f);
        Assert.DoesNotContain(Run(f, _ => self, 12), Prefired);
    }

    [Fact]
    public void ATeammateOnTheLineToThePlaceHoldsIt()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 600f);
        int shots = f.Decider.Shots;
        List<IntentCommand> intents = Run(f, _ => self, 6, sighted: _ => [Friend(Mid + new AiVector(250f, 0f, 0f))]);
        Assert.DoesNotContain(intents, i => i.Action.Kind == ActionKind.Fire);
        Assert.True(f.Decider.PrefireHeldFriend > 0, $"prefire '{f.Decider.Doing.Prefire}'");
        Assert.Equal(shots, f.Decider.Shots);
    }

    [Fact]
    public void WithTheSwitchOffItNeverPrefires()
    {
        TFField f = Sure(feel: TFFeelSwitches.All with { Prefire = false });
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 600f);
        Assert.DoesNotContain(Run(f, _ => self, 30), Prefired);
        Assert.Equal((0, 0), (f.Decider.PrefireChances, f.Decider.Doing.LineAsk));
    }

    [Fact]
    public void AChanceOfNothingIsDrawnAndNeverTaken()
    {
        TFField f = Sure(chance: 0f);
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 600f);
        Assert.DoesNotContain(Run(f, _ => self, 30), Prefired);
        Assert.True(f.Decider.PrefireChances > 0);
        Assert.Equal(0, f.Decider.PrefireAims);
    }

    [Fact]
    public void HeldStillBeforeTheRoundItDoesNot()
    {
        TFField f = Sure();
        SelfState held = TFField.Self(Mid, extra: SelfFlags.Frozen);
        SeeThenLose(f, held, 600f);
        Assert.DoesNotContain(Run(f, _ => held, 12), Prefired);
    }

    /// <summary>A rocket went; then what the memory shows in its window (flight and half a second) is counted to it, and after it nothing.</summary>
    private static TFField Fired(Func<TFField, int, AiEvent[]> after)
    {
        TFField f = Sure();
        SeeThenLose(f, TFField.Self(Mid), 600f);
        FireOnce(f, 30, after);
        Assert.Equal(1, f.Decider.PrefireRockets);
        return f;
    }

    [Fact]
    public void AnEnemyHurtInTheRocketsWindowIsAHit()
    {
        TFField f = Fired((f, k) => k == 4 ? [AiEvent.DealtDamage(f.Bot, new Tick(f.Tick + 1), Enemy, 60f, 140)] : []);
        Assert.Equal((1, 0, 0), (f.Decider.PrefireHits, f.Decider.PrefireSelfHurt, f.Decider.PrefireKills));
    }

    [Fact]
    public void AnEnemyHurtAfterTheWindowIsNot()
    {
        TFField f = Fired((f, k) => k == 20 ? [AiEvent.DealtDamage(f.Bot, new Tick(f.Tick + 1), Enemy, 60f, 140)] : []);
        Assert.Equal(0, f.Decider.PrefireHits);
    }

    [Fact]
    public void ItsOwnBlastIsASelfHurtAndAnEnemysShotIsNot()
    {
        TFField own = Fired((f, k) => k == 3 ? [AiEvent.Damaged(f.Bot, new Tick(f.Tick + 1), f.Bot, 40f, default, 0)] : []);
        TFField theirs = Fired((f, k) => k == 3 ? [AiEvent.Damaged(f.Bot, new Tick(f.Tick + 1), Enemy, 40f, default, 0)] : []);
        Assert.Equal((1, 0), (own.Decider.PrefireSelfHurt, theirs.Decider.PrefireSelfHurt));
    }

    [Fact]
    public void AKillInTheWindowIsCounted()
    {
        TFField f = Fired((f, k) => k == 4 ? [AiEvent.KillFeed(f.Bot, new Tick(f.Tick + 1), Enemy, f.Bot)] : []);
        Assert.Equal(1, f.Decider.PrefireKills);
    }

    /// <summary>A jump begun has the bot: no rocket at a corner while it is under way, though the line is answered clear.</summary>
    [Fact]
    public void NotWhileARocketJumpHasTheBot()
    {
        TFField f = Sure(jumps: new TFJumpAdvice());
        SelfState running = TFField.Self(Mid, velocity: new AiVector(240f, 0f, 0f));
        SeeThenLose(f, running, 600f);
        List<IntentCommand> intents = Run(f, _ => running, 10);
        Assert.True(f.Decider.RocketJumps > 0, $"the stimulus: a jump began ({f.Decider.Doing.Branch}, {f.Decider.Doing.Leaf})");
        int began = intents.FindIndex(i => f.BeginsJump(i));
        Assert.DoesNotContain(intents.Skip(began), Prefired);
    }
    [Fact]
    public void BehindItItHolds()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, -600f);
        Assert.DoesNotContain(Run(f, _ => self, 12), Prefired);
        Assert.True(Refused(f, TFPrefireRefusal.Turn) > 0);
    }

    /// <summary>The view still 50 degrees off the place: the teammate is on the rocket's line, not the view's, and holds it all the same.</summary>
    [Fact]
    public void ATeammateOnTheRocketsLineHoldsItWhereverTheViewPoints()
    {
        TFField f = Sure();
        SelfState self = TFField.Self(Mid) with { Eye = new AiAngles(0f, 50f, 0f) };
        SeeThenLose(f, self, 600f);
        List<IntentCommand> intents = Run(f, _ => self, 6, sighted: _ => [Friend(Mid + new AiVector(250f, 0f, 0f))]);
        Assert.DoesNotContain(intents, i => i.Action.Kind == ActionKind.Fire);
        Assert.True(f.Decider.PrefireHeldFriend > 0, $"prefire '{f.Decider.Doing.Prefire}'");
    }

    /// <summary>A sight table that sees from anywhere only the cells it is given.</summary>
    private sealed class Sees(params int[] cells) : ICellSight
    {
        public float Range => 4096f;

        public bool Visible(int a, int b) => Array.IndexOf(cells, b) >= 0;

        public float Exposure(int cell) => 0f;
    }

    /// <summary>Lost where the bot's place does not see, the rocket goes at the nearest place along the floor that it does: the opening.</summary>
    [Fact]
    public void TheRocketGoesAtTheOpeningTheEnemyIsBehind()
    {
        NavGraph floor = TFField.Floor();
        AiVector opening = floor.Cell(floor.FindCell(new AiVector(Mid.X + 480f, Mid.Y, 1f))).Centre;
        var f = new TFField(floor: floor, sight: new Sees(floor.FindCell(opening + new AiVector(0f, 0f, 1f)).Index),
                            tune: p => p with { PrefireChance = 1f, PrefireThrift = 1f });
        SelfState self = TFField.Self(Mid);
        SeeThenLose(f, self, 700f);
        IntentCommand fire = Run(f, _ => self, 4).Find(Prefired);
        Assert.True(Prefired(fire), $"prefire '{f.Decider.Doing.Prefire}'");
        Assert.Equal(new AiVector(opening.X, opening.Y, opening.Z + TFKnowledgeFeet), fire.Look.Point);
    }
}
