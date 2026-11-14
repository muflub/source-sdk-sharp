using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The danger shot (plan_bots_teams_tf2_part_g G.4's "danger places", lane Gp) on the synthetic field (<see cref="TFField"/>):
/// the store says red players dying in bins 28 to 34 of row 4 were killed from one place further east (<see cref="Killer"/>,
/// bin 40); a teammate the kill feed has just named dead, whose scream the bot heard there (or whom it saw there a moment
/// before), makes the bot, on its way east, put a rocket at the floor of that place before it sees anyone. Nothing without the
/// store's row, without the feed, without a place it could know, for an enemy's death, after the shot's seconds, or a second
/// time for one death.
/// </summary>
public sealed class TFDangerShotTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Mate = new(4, 1);
    private static readonly WorldBin Killer = new(40, 4, 0);
    private static readonly AiVector Self0 = new(1600f, 288f, 0f), DiedAt = new(2000f, 288f, 0f);
    private static readonly ExperienceKey Key = new("tf_field", "0123456789abcdef", "ctf");

    private static MapExperience Deaths(int role = TFField.Red)
    {
        var rows = new ExperienceDelta();
        for (int x = 28; x <= 34; x++)
            rows.Add(new SightKey(new ExperienceContext(0, role, 3), new WorldBin(x, 4, 0), Killer, 2), new Tally(1d));
        return new MapExperience(Key, 1, rows);
    }

    private static TFField Field(MapExperience? experience = null, float chance = 1f, TFFeelSwitches? feel = null)
    {
        var f = new TFField(mates: [Mate.Slot], feel: feel, tune: p => p with { PrefireChance = chance, PrefireThrift = 1f });
        f.Memory.World.ExperienceSlot.Current = experience ?? Deaths();
        return f;
    }

    private static Tick Next(TFField f) => new(f.Tick + DecideSchedule.Period);

    private static AiEvent Scream(TFField f, ActorId who, AiVector at) => AiEvent.SoundHeard(f.Bot, Next(f), SoundKind.Death, at, 0.9f, who, -1);

    private static AiEvent Feed(TFField f, ActorId who) => AiEvent.KillFeed(f.Bot, Next(f), who, who == Enemy ? f.Bot : Enemy);

    private static AiEvent[] Answer(TFField f, bool clear = true)
    {
        DoingRecord d = f.Decider.Doing;
        return d.LineAsk > 0 ? [AiEvent.LineAnswer(f.Bot, new Tick(f.Tick + 1), d.LineAt, d.LineAsk, clear)] : [];
    }

    /// <summary><paramref name="first"/> heard on one decide, then <paramref name="decides"/> more with each line answered clear; the intents after.</summary>
    private static List<IntentCommand> After(TFField f, AiEvent[] first, int decides = 6, ObjectiveKnowledge? objectives = null, AiAngles eye = default)
    {
        SelfState self = TFField.Self(Self0) with { Eye = eye };
        objectives ??= TFField.Ctf();
        var intents = new List<IntentCommand> { f.StepHearing(self, objectives, first) };
        for (int k = 0; k < decides; k++)
            intents.Add(f.StepHearing(self, objectives, Answer(f)));
        return intents;
    }

    private static readonly AiVector KillerFloor = new(Killer.Centre.X, Killer.Centre.Y, SourceSharp.Ai.TF2.Knowledge.TFKnowledge.FeetAim);

    private static bool AtTheKiller(IntentCommand i) => i.Action.Kind == ActionKind.Fire && i.Look.Kind == LookKind.CornerCheck && i.Look.Point.DistanceTo(KillerFloor) < 1f;

    [Fact]
    public void AScreamWhereTheStoreSaysTeammatesDieIsARocketAtThePlaceTheyDieFrom()
    {
        TFField f = Field();
        List<IntentCommand> intents = After(f, [Scream(f, Mate, DiedAt), Feed(f, Mate)]);
        Assert.True(intents.Exists(AtTheKiller), $"branch {f.Decider.Doing.Branch}, prefire '{f.Decider.Doing.Prefire}' at {f.Decider.Doing.PrefireAt}; deaths {f.Decider.TeammateDeaths} placed {f.Decider.DeathsPlaced} at danger {f.Decider.DeathsAtDanger} chances {f.Decider.DangerChances} refused {string.Join(",", f.Decider.PrefireRefused.ToArray())}");
        Assert.Equal((1, 1, 1), (f.Decider.TeammateDeaths, f.Decider.DeathsPlaced, f.Decider.DeathsAtDanger));
        Assert.Equal((1, 1), (f.Decider.DangerChances, f.Decider.DangerAims));
        Assert.Equal(TFDecider.Fetch, f.Branch);
    }

    [Fact]
    public void ATeammateSeenThereAMomentBeforeIsPlacedThereToo()
    {
        TFField f = Field();
        ActorFacts mate = new(Mate, DiedAt, AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Red, (int)TFClass.Soldier);
        f.StepHearing(TFField.Self(Self0), TFField.Ctf(), [AiEvent.SightGained(f.Bot, Next(f), Mate, DiedAt, AiVector.Zero, 1f)], mate);
        f.StepHearing(TFField.Self(Self0), TFField.Ctf(), [AiEvent.SightLost(f.Bot, Next(f), Mate, DiedAt, AiVector.Zero)]);
        Assert.Contains(After(f, [Feed(f, Mate)]), AtTheKiller);
    }

    [Fact]
    public void WithNoRowInTheStoreThereIsNoShot()
    {
        TFField f = Field(new MapExperience(Key, 1, new ExperienceDelta()));
        Assert.DoesNotContain(After(f, [Scream(f, Mate, DiedAt), Feed(f, Mate)]), i => i.Action.Kind == ActionKind.Fire);
        Assert.Equal((1, 1, 0), (f.Decider.TeammateDeaths, f.Decider.DeathsPlaced, f.Decider.DeathsAtDanger));
    }

    [Fact]
    public void TheOtherSidesRowsAreNotTheBots()
    {
        TFField f = Field(Deaths(role: TFField.Blue));
        Assert.DoesNotContain(After(f, [Scream(f, Mate, DiedAt), Feed(f, Mate)]), i => i.Action.Kind == ActionKind.Fire);
    }

    [Fact]
    public void ADeathItNeitherHeardNorSawIsNotPlaced()
    {
        TFField f = Field();
        Assert.DoesNotContain(After(f, [Feed(f, Mate)]), i => i.Action.Kind == ActionKind.Fire);
        Assert.Equal((1, 0), (f.Decider.TeammateDeaths, f.Decider.DeathsPlaced));
    }

    [Fact]
    public void AScreamWithoutTheKillFeedIsNoDeath()
    {
        TFField f = Field();
        Assert.DoesNotContain(After(f, [Scream(f, Mate, DiedAt)]), i => i.Action.Kind == ActionKind.Fire);
        Assert.Equal(0, f.Decider.TeammateDeaths);
    }

    [Fact]
    public void AnEnemysDeathThereIsNoReason()
    {
        TFField f = Field();
        Assert.DoesNotContain(After(f, [Scream(f, Enemy, DiedAt), Feed(f, Enemy)]), i => i.Action.Kind == ActionKind.Fire);
        Assert.Equal(0, f.Decider.TeammateDeaths);
    }

    /// <summary>Turned away (west) past the shot's seconds, then back: the death is old news.</summary>
    [Fact]
    public void PastItsSecondsTheShotIsNotTaken()
    {
        TFField f = Field();
        After(f, [Scream(f, Mate, DiedAt), Feed(f, Mate)], decides: (int)(TFDecider.DangerShotSeconds / 0.09f) + 2, eye: new AiAngles(0f, 180f, 0f));
        int old = f.Decider.PrefireRefused[(int)TFPrefireRefusal.Old];
        Assert.DoesNotContain(After(f, [], decides: 6), AtTheKiller);
        Assert.Equal(0, f.Decider.DangerChances);
        // Forgotten, not refused each decide: the death is no longer the bot's business.
        Assert.Equal(old, f.Decider.PrefireRefused[(int)TFPrefireRefusal.Old]);
    }

    /// <summary>Back in time to look, too late for the rocket: 7.6 s on, its 0.9 s of flight would land past the shot's seconds.</summary>
    [Fact]
    public void ARocketThatWouldLandPastItsSecondsIsNotFired()
    {
        TFField f = Field();
        After(f, [Scream(f, Mate, DiedAt), Feed(f, Mate)], decides: (int)(7.6f / 0.09f), eye: new AiAngles(0f, 180f, 0f));
        Assert.DoesNotContain(After(f, [], decides: 4), AtTheKiller);
        Assert.True(f.Decider.PrefireRefused[(int)TFPrefireRefusal.Old] > 0);
    }

    [Fact]
    public void OneChanceADeath()
    {
        TFField f = Field(chance: 0f);
        After(f, [Scream(f, Mate, DiedAt), Feed(f, Mate)], decides: 40);
        Assert.Equal((1, 0), (f.Decider.DangerChances, f.Decider.DangerAims));
    }

    [Fact]
    public void WithTheFlagInHandItRunsOn()
    {
        TFField f = Field();
        ObjectiveKnowledge carrying = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Self0, blueCarrier: f.Bot.Slot);
        Assert.DoesNotContain(After(f, [Scream(f, Mate, DiedAt), Feed(f, Mate)], objectives: carrying), i => i.Action.Kind == ActionKind.Fire);
        Assert.Equal(TFDecider.Deliver, f.Branch);
    }

    [Fact]
    public void WithTheSwitchOffThereIsNoShot()
    {
        TFField f = Field(feel: TFFeelSwitches.All with { Prefire = false });
        Assert.DoesNotContain(After(f, [Scream(f, Mate, DiedAt), Feed(f, Mate)]), i => i.Action.Kind == ActionKind.Fire);
    }
    /// <summary>The rocket going is counted as the danger shot's, and an enemy hurt in its window as its hit.</summary>
    [Fact]
    public void ItsRocketAndAHitInItsWindowAreCounted()
    {
        TFField f = Field();
        f.StepHearing(TFField.Self(Self0), TFField.Ctf(), [Scream(f, Mate, DiedAt), Feed(f, Mate)]);
        int fired = -1;
        for (int k = 0; k < 20; k++)
        {
            AiEvent[] events = [.. Answer(f), .. fired >= 0 && k == fired + 3 ? [AiEvent.DealtDamage(f.Bot, new Tick(f.Tick + 1), Enemy, 50f, 100)] : Array.Empty<AiEvent>()];
            IntentCommand i = f.StepHearing(TFField.Self(Self0, clip: fired >= 0 ? 3 : 4), TFField.Ctf(), events);
            if (fired < 0 && AtTheKiller(i))
                fired = k;
        }

        Assert.True(fired >= 0);
        Assert.Equal((1, 1, 1, 1), (f.Decider.DangerRockets, f.Decider.PrefireRockets, f.Decider.DangerHits, f.Decider.PrefireHits));
    }

    /// <summary>The control for the counts: a prefire at a belief is no danger shot's.</summary>
    [Fact]
    public void APrefireAtABeliefIsNotCountedAsADangerShot()
    {
        TFField f = Field();
        ActorFacts them = new(Enemy, Self0 + new AiVector(600f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);
        f.StepHearing(TFField.Self(Self0), null, [AiEvent.SightGained(f.Bot, Next(f), Enemy, them.Origin, AiVector.Zero, 1f)], them);
        f.StepHearing(TFField.Self(Self0), null, [AiEvent.SightLost(f.Bot, Next(f), Enemy, them.Origin, AiVector.Zero)]);
        int fired = -1;
        for (int k = 0; k < 10; k++)
        {
            IntentCommand i = f.StepHearing(TFField.Self(Self0, clip: fired >= 0 ? 3 : 4), null, Answer(f));
            if (fired < 0 && i.Action.Kind == ActionKind.Fire)
                fired = k;
        }

        Assert.Equal((1, 0, 0), (f.Decider.PrefireRockets, f.Decider.DangerRockets, f.Decider.DangerChances));
    }
}
