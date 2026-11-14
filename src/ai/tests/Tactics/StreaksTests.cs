using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// The core's streak (plan_hl2mp_ai §5.5, plan_bots_teams_tf2_part_g G.2.1), over a bot's memory
/// alone: none until told on; a console's ask begins one; a hot one gives its own aim, lead,
/// reaction and recognition to the record and a cold one its own; it ends on time, a death ends a
/// hot one, and turning streaks off ends any. The chances a kill or two deaths start one are held
/// through the HL2MP decider (StreakTests), whose mutation arms edit this class.
/// </summary>
public sealed class StreaksTests
{
    private const float Dt = 0.015f;
    private static readonly ActorId Bot = new(3, 1), Them = new(2, 1);

    // A bot whose streaks are worth showing: hot far better, cold far worse.
    private static readonly DecisionParams P = default(DecisionParams) with
    {
        AimErrorDeg = 4f,
        LeadQuality = 0.5f,
        ReactionMs = 200f,
        RecognizeSeconds = 0.3f,
        Hot = new StreakParams(1f, 0.9f, 120f, 0.1f),
        Cold = new StreakParams(8f, 0.2f, 300f, 0.5f),
    };

    private sealed class Run
    {
        private readonly PerceivedView _view = new(4);
        public BotMemory Memory { get; } = new(Bot, 8, new MemoryParams(4f, 120f), MemoryWorld.Empty(Dt), new HabitStore());
        public Streaks Streaks { get; } = new(new AiRandom(9));
        public int Tick { get; private set; }

        public Run Step(bool alive = true, params Func<Tick, AiEvent>[] events)
        {
            Tick += DecideSchedule.Period;
            var now = new Tick(Tick);
            _view.Reset(Bot, now, new SelfState(default, default, default, alive ? 100 : 0, 0, -1, 0, 0, alive ? SelfFlags.Alive : SelfFlags.None));
            Memory.Step(now, _view, [.. events.Select(e => e(now))]);
            Streaks.Step(now, Dt, Memory, in P, alive);
            return this;
        }

        public Run For(float seconds)
        {
            for (int i = 0; i < (int)(seconds / (DecideSchedule.Period * Dt)); i++)
                Step();
            return this;
        }
    }

    private static AiEvent On(Tick t) => AiEvent.StreaksSet(Bot, t, AiEvent.StreaksOn);

    private static AiEvent Off(Tick t) => AiEvent.StreaksSet(Bot, t, AiEvent.StreaksOff);

    private static AiEvent AskHot(Tick t) => AiEvent.StreaksSet(Bot, t, AiEvent.StreakHot);

    private static AiEvent AskCold(Tick t) => AiEvent.StreaksSet(Bot, t, AiEvent.StreakCold);

    private static AiEvent Death(Tick t) => AiEvent.Killed(Bot, t, Them);

    [Fact]
    public void UntilToldOnAnAskBeginsNone()
    {
        Run r = new Run().Step(true, AskHot);
        Assert.Equal((0, "", 0), (r.Streaks.Kind, r.Streaks.Name, r.Streaks.Begun));
    }

    [Fact]
    public void AnAskBeginsAHotOneAndTheRecordCarriesWhatItGives()
    {
        Run r = new Run().Step(true, On).Step(true, AskHot);
        Assert.Equal((1, "hot", 1), (r.Streaks.Kind, r.Streaks.Name, r.Streaks.Begun));
        Assert.Equal(("hot", 1f, 120f, 0.1f), r.Streaks.Record(in P));
        Assert.Equal((1f, 0.9f), (r.Streaks.AimErrorDeg(in P), r.Streaks.LeadQuality(in P)));
    }

    [Fact]
    public void AnAskBeginsAColdOneAndTheRecordCarriesWhatItGives()
    {
        Run r = new Run().Step(true, On).Step(true, AskCold);
        Assert.Equal((-1, "cold"), (r.Streaks.Kind, r.Streaks.Name));
        Assert.Equal(("cold", 8f, 300f, 0.5f), r.Streaks.Record(in P));
        Assert.Equal((8f, 0.2f), (r.Streaks.AimErrorDeg(in P), r.Streaks.LeadQuality(in P)));
    }

    [Fact]
    public void WithNoneOnTheBotsOwnNumbersAreReadAndTheRecordCarriesNothing()
    {
        Run r = new Run().Step(true, On);
        Assert.Equal(("", 0f, 0f, 0f), r.Streaks.Record(in P));
        Assert.Equal((4f, 0.5f), (r.Streaks.AimErrorDeg(in P), r.Streaks.LeadQuality(in P)));
    }

    /// <summary>A re-aim waits the streak's recognition and reaction: 0.1 s + 120 ms is under a person's least, 0.5 s + 300 ms is not.</summary>
    [Fact]
    public void AReAimWaitsTheStreaksOwnRecognitionAndReaction()
    {
        Assert.Equal(0.5f, new Run().Step(true, On).Streaks.ReAimSeconds(in P), 4);
        Assert.Equal(AimHeightHold.ReAimLeastSeconds, new Run().Step(true, On).Step(true, AskHot).Streaks.ReAimSeconds(in P), 4);
        Assert.Equal(0.8f, new Run().Step(true, On).Step(true, AskCold).Streaks.ReAimSeconds(in P), 4);
    }

    [Fact]
    public void AHotStreakLastsEightToTwentySecondsThenEnds()
    {
        Run r = new Run().Step(true, On).Step(true, AskHot).For(Streaks.HotMinSeconds - 0.5f);
        Assert.Equal(1, r.Streaks.Kind);
        r.For(Streaks.HotMaxSeconds - Streaks.HotMinSeconds + 1f);
        Assert.Equal(0, r.Streaks.Kind);
    }

    [Fact]
    public void ADeathEndsAHotStreak()
    {
        Run r = new Run().Step(true, On).Step(true, AskHot).Step(false, Death);
        Assert.Equal(0, r.Streaks.Kind);
    }

    /// <summary>The control for the fact above: a cold spell is not a death's to end.</summary>
    [Fact]
    public void ADeathDoesNotEndAColdSpell()
    {
        Run r = new Run().Step(true, On).Step(true, AskCold).Step(false, Death);
        Assert.Equal(-1, r.Streaks.Kind);
    }

    [Fact]
    public void TurnedOffAStreakEndsAtOnce()
    {
        Run r = new Run().Step(true, On).Step(true, AskHot).Step(true, Off);
        Assert.Equal((0, ""), (r.Streaks.Kind, r.Streaks.Name));
    }

    [Fact]
    public void EachOneBegunIsCounted()
    {
        Run r = new Run().Step(true, On).Step(true, AskHot).Step().Step(true, AskCold);
        Assert.Equal((2, -1), (r.Streaks.Begun, r.Streaks.Kind));
    }
}
