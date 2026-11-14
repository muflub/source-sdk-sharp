using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot's hot and cold streaks (plan_bots_teams_tf2_part_g G.2.1) on the synthetic field
/// (<see cref="TFField"/>): the core's <see cref="Streaks"/> reach its doing
/// record (which the motion layer and the senses read), its aim's error and its rockets' lead; a
/// kill starts one at its chance by the bot's seed, and a death ends a hot one.
/// </summary>
public sealed class TFStreakTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f), At = new(2348f, 256f, 0f);

    // A bot whose streaks show: hot far better than its own, cold far worse.
    private static DecisionParams Tuned(DecisionParams p) => p with
    {
        AimErrorDeg = 4f,
        LeadQuality = 0f,
        Hot = new StreakParams(1f, 1f, 120f, 0.1f),
        Cold = new StreakParams(8f, 0f, 300f, 0.5f),
    };

    private static TFField Field(ulong seed = 7) => new(seed: seed, graph: false, tune: Tuned);

    private static AiEvent Set(TFField f, int code) => AiEvent.StreaksSet(f.Bot, new Tick(f.Tick + DecideSchedule.Period), code);

    private static IntentCommand Step(TFField f, params AiEvent[] events) => f.StepHearing(TFField.Self(Mid), TFField.Ctf(), events);

    private static TFField On(ulong seed = 7)
    {
        TFField f = Field(seed);
        Step(f, Set(f, AiEvent.StreaksOn));
        return f;
    }

    [Fact]
    public void AHotStreakReachesTheDoingRecordWithWhatItGives()
    {
        TFField f = On();
        Step(f, Set(f, AiEvent.StreakHot));
        DoingRecord d = f.Decider.Doing;
        Assert.Equal(("hot", 1f, 120f, 0.1f), (d.Streak, d.StreakAimErrorDeg, d.StreakReactionMs, d.StreakRecognizeSeconds));
    }

    [Fact]
    public void AColdSpellReachesTheDoingRecordWithWhatItGives()
    {
        TFField f = On();
        Step(f, Set(f, AiEvent.StreakCold));
        DoingRecord d = f.Decider.Doing;
        Assert.Equal(("cold", 8f, 300f, 0.5f), (d.Streak, d.StreakAimErrorDeg, d.StreakReactionMs, d.StreakRecognizeSeconds));
    }

    [Fact]
    public void WithNoStreakTheRecordCarriesNone()
    {
        TFField f = On();
        Step(f);
        DoingRecord d = f.Decider.Doing;
        Assert.Equal(("", 0f, 0f, 0f), (d.Streak, d.StreakAimErrorDeg, d.StreakReactionMs, d.StreakRecognizeSeconds));
    }

    /// <summary>The settled miss across, RMS over <paramref name="decides"/> decides on a body 300 u off, with the streak <paramref name="ask"/> begun.</summary>
    private static double Miss(int ask, int decides = 80)
    {
        TFField f = On();
        if (ask != 0)
            Step(f, Set(f, ask));
        var body = new ActorFacts(Enemy, At, AiVector.Zero, TFField.Shotgun, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);
        SelfState self = TFField.Self(Mid, weapon: TFField.Shotgun, clip: 6);
        double sum = 0;
        int n = 0;
        for (int i = 0; i < decides; i++)
        {
            float off = f.StepDrawn(self, TFField.Ctf(), body).Look.Point.Y - At.Y;
            if (i < 30)
                continue;
            sum += off * off;
            n++;
        }

        return Math.Sqrt(sum / n);
    }

    /// <summary>The aim's error is the streak's: a quarter of its own hot (1 degree for 4), twice it cold (8), inside a hot streak's 8 s at least.</summary>
    [Fact]
    public void AStreakReachesTheAimsError()
    {
        double own = Miss(0), hot = Miss(AiEvent.StreakHot), cold = Miss(AiEvent.StreakCold, decides: 60);
        Assert.True(own > 3.0, $"the control: its own aim errs, by {own:F1} u");
        Assert.InRange(hot / own, 0.15, 0.4);
        Assert.InRange(cold / own, 1.5, 2.6);
    }

    private static float Lead(int ask)
    {
        TFField f = On();
        if (ask != 0)
            Step(f, Set(f, ask));
        var strafing = new ActorFacts(Enemy, Mid + new AiVector(600f, 0f, 0f), new AiVector(0f, 300f, 0f), TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);
        double sum = 0;
        for (int i = 0; i < 40; i++)
            sum += f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), strafing).Look.LeadSeconds;
        return (float)(sum / 40);
    }

    /// <summary>A rocket's lead is the streak's: none of its own here, a whole flight's worth hot.</summary>
    [Fact]
    public void AHotStreakReachesARocketsLead()
    {
        Assert.Equal(0f, Lead(0), 3);
        Assert.True(Lead(AiEvent.StreakHot) > 100f, $"hot it leads by {Lead(AiEvent.StreakHot):F0} u");
    }

    /// <summary>A death ends a hot streak: back alive, the record carries none.</summary>
    [Fact]
    public void ADeathEndsAHotStreak()
    {
        TFField f = On();
        Step(f, Set(f, AiEvent.StreakHot));
        Assert.Equal("hot", f.Decider.Doing.Streak);
        f.StepHearing(TFField.Self(Mid) with { Flags = SelfFlags.None }, TFField.Ctf(), [AiEvent.Killed(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Enemy)]);
        Step(f);
        Assert.Equal("", f.Decider.Doing.Streak);
    }

    /// <summary>A kill starts one at about its chance, a quarter, over bots of many seeds: its own kills, by its own stream.</summary>
    [Fact]
    public void AKillStartsAStreakAtAboutItsChanceByTheBotsSeed()
    {
        int hot = 0;
        for (ulong seed = 1; seed <= 400; seed++)
        {
            TFField f = On(seed);
            Step(f, AiEvent.KillFeed(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Enemy, f.Bot));
            hot += f.Decider.Doing.Streak == "hot" ? 1 : 0;
        }

        Assert.InRange(hot, 60, 140);
    }

    [Fact]
    public void SomeoneElsesKillStartsNone()
    {
        var other = new ActorId(6, 0x4000_0002);
        for (ulong seed = 1; seed <= 100; seed++)
        {
            TFField f = On(seed);
            Step(f, AiEvent.KillFeed(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Enemy, other));
            Assert.Equal("", f.Decider.Doing.Streak);
        }
    }

    [Fact]
    public void UntilStreaksAreToldOnThereIsNone()
    {
        TFField f = Field();
        Step(f, Set(f, AiEvent.StreakHot));
        Assert.Equal("", f.Decider.Doing.Streak);
    }
}
