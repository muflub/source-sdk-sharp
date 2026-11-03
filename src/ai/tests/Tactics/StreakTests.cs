using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// Streaks (plan_hl2mp_ai §5.5, ruling 2026-11-02: "some times bad bots become great for a very
/// short stint"): a run above itself, mostly earned, the largest for a weak bot and never to
/// Expert's; a run below itself after two quick deaths, smaller. Only the aim's error and lead,
/// the reaction and the recognition move.
/// </summary>
public sealed class StreakTests
{
    private const float DecideSeconds = 6 * 0.015f;
    private static readonly ActorId Victim = Duel.Opponent;

    private static AiEvent On(Duel d) => AiEvent.StreaksSet(Duel.Bot, new Tick(d.Tick + 6), AiEvent.StreaksOn);

    private static AiEvent Kill(Duel d) => AiEvent.KillFeed(Duel.Bot, new Tick(d.Tick + 6), Victim, Duel.Bot);

    private static AiEvent Death(Duel d) => AiEvent.Killed(Duel.Bot, new Tick(d.Tick + 6), Victim);

    private static Duel Seeded(string preset, ulong seed)
        => new(Duel.Preset(preset), Duel.Bot, seed, null, null, null);

    /// <summary>Whether <paramref name="seed"/>'s first kill starts one, with streaks told on.</summary>
    private static string AfterAKill(string preset, ulong seed, AiEvent[]? first = null)
    {
        Duel d = Seeded(preset, seed);
        SelfState self = d.Self();
        d.Step(self, [], first ?? [On(d)]);
        d.Step(self, [], Kill(d));
        return d.Decider.Doing.Streak;
    }

    /// <summary>A kill can start one, and does not always: about its chance over many seeds.</summary>
    [Fact]
    public void AKillCanStartAStreakAtItsChance()
    {
        int hot = Enumerable.Range(1, 400).Count(s => AfterAKill("easy", (ulong)s) == "hot");
        Assert.InRange(hot, (int)(400 * Hl2mpDecider.StreakKillChance * 0.6f), (int)(400 * Hl2mpDecider.StreakKillChance * 1.4f));
    }

    /// <summary>Two kills close together are likelier than one: it is earned.</summary>
    [Fact]
    public void TwoKillsCloseTogetherAreLikelierThanOne()
    {
        int Twice(float apart)
        {
            int hot = 0;
            for (ulong s = 1; s <= 400; s++)
            {
                Duel d = Seeded("easy", s);
                SelfState self = d.Self();
                d.Step(self, [], On(d));
                // The first by itself is not counted: a streak it started stands aside.
                d.Step(self, [], AiEvent.KillFeed(Duel.Bot, new Tick(d.Tick + 6), Victim, Duel.Bot));
                bool first = d.Decider.Doing.Streak == "hot";
                for (int k = 0; k < (int)(apart / DecideSeconds); k++)
                    d.Step(self, []);
                d.Step(self, [], Kill(d));
                hot += !first && d.Decider.Doing.Streak == "hot" ? 1 : 0;
            }

            return hot;
        }

        int near = Twice(2f), far = Twice(Hl2mpDecider.StreakDoubleSeconds + 5f);
        Assert.True(near > far * 1.5f, $"near {near}, far {far}");
    }

    /// <summary>Someone else's kill is no reason: the feed shows it, and the bot's luck does not change.</summary>
    [Fact]
    public void SomeoneElsesKillStartsNothing()
    {
        for (ulong s = 1; s <= 100; s++)
        {
            Duel d = Seeded("easy", s);
            SelfState self = d.Self();
            d.Step(self, [], On(d));
            d.Step(self, [], AiEvent.KillFeed(Duel.Bot, new Tick(d.Tick + 6), Duel.Other, Victim));
            Assert.Equal("", d.Decider.Doing.Streak);
        }
    }

    private static (Duel D, SelfState Self) Hot(string preset = "easy")
    {
        Duel d = Seeded(preset, 7);
        SelfState self = d.Self();
        d.Step(self, [], On(d), AiEvent.StreaksSet(Duel.Bot, new Tick(d.Tick + 6), AiEvent.StreakHot));
        return (d, self);
    }

    /// <summary>It lasts its seconds, then it is over, whatever else happens.</summary>
    [Fact]
    public void AHotStreakEndsOnTime()
    {
        (Duel d, SelfState self) = Hot();
        Assert.Equal("hot", d.Decider.Doing.Streak);
        int decides = 0;
        while (d.Decider.Doing.Streak == "hot" && decides < 2000)
        {
            d.Step(self, []);
            decides++;
        }

        float seconds = decides * DecideSeconds;
        Assert.InRange(seconds, Hl2mpDecider.HotMinSeconds - 0.2f, Hl2mpDecider.HotMaxSeconds + 0.2f);
    }

    /// <summary>After one there is a rest: a kill right after a streak starts none.</summary>
    [Fact]
    public void AfterAStreakThereIsARest()
    {
        for (ulong s = 1; s <= 200; s++)
        {
            Duel d = Seeded("easy", s);
            SelfState self = d.Self();
            d.Step(self, [], On(d), AiEvent.StreaksSet(Duel.Bot, new Tick(d.Tick + 6), AiEvent.StreakHot));
            while (d.Decider.Doing.Streak == "hot")
                d.Step(self, []);
            d.Step(self, [], Kill(d));
            Assert.Equal("", d.Decider.Doing.Streak);
        }
    }

    /// <summary>A death ends a hot one: nobody is on a good run through a respawn.</summary>
    [Fact]
    public void ADeathEndsAHotStreak()
    {
        (Duel d, SelfState self) = Hot();
        d.Step(self, [], Death(d));
        Assert.Equal("", d.Decider.Doing.Streak);
    }

    /// <summary>An Easy bot reads about a decent Medium's aim on one, far better than its own, and never up to Expert's.</summary>
    [Fact]
    public void AHotEasyAimsAboutLikeADecentMedium()
    {
        DecisionParams easy = SourceSharp.Ai.Tests.Skills.Curves.Game().For(Duel.Preset("easy")), normal = SourceSharp.Ai.Tests.Skills.Curves.Game().For(Duel.Preset("normal")), expert = SourceSharp.Ai.Tests.Skills.Curves.Game().For(Duel.Preset("expert"));
        Assert.True(easy.Hot.AimErrorDeg < easy.AimErrorDeg * 0.5f, $"hot {easy.Hot.AimErrorDeg}, own {easy.AimErrorDeg}");
        Assert.InRange(easy.Hot.AimErrorDeg, normal.AimErrorDeg * 0.9f, normal.AimErrorDeg * 1.5f);
        Assert.True(easy.Hot.AimErrorDeg > expert.AimErrorDeg);
        Assert.True(easy.Hot.LeadQuality > easy.LeadQuality);
        Assert.True(easy.Hot.ReactionMs < easy.ReactionMs);
        Assert.True(easy.Hot.RecognizeSeconds < VisionCurves.For(Duel.Preset("easy")).RecognizeSeconds);
    }

    /// <summary>Whatever the level, a lift stops under Expert's, and never takes a bot below itself.</summary>
    [Fact]
    public void ALiftNeverReachesExpertsLevelAndNeverLowers()
    {
        for (float level = 0f; level <= StandardSkills.Cap; level += 0.25f)
        {
            float hot = SkillCurves.HotLevel(level);
            Assert.True(hot >= level, $"{level} -> {hot}");
            if (level < SkillCurves.HotCap)
                Assert.True(hot <= SkillCurves.HotCap, $"{level} -> {hot}");
        }

        Assert.True(SkillCurves.HotCap < 7f, "Expert's Alertness is 7");
    }

    /// <summary>The lift is largest for the weakest: a level lifted is a level less the higher it starts.</summary>
    [Fact]
    public void TheWeakerTheBotTheLargerTheLift()
    {
        float Lift(float level) => SkillCurves.HotLevel(level) - level;
        Assert.True(Lift(2f) > Lift(4f) + 0.2f && Lift(4f) > Lift(6f) + 0.2f, $"{Lift(2f)}, {Lift(4f)}, {Lift(6f)}");
    }

    /// <summary>Expert's aim does not move, and a streak with nothing to show does not start.</summary>
    [Fact]
    public void ExpertsAimDoesNotMoveAndItRunsNoHotStreak()
    {
        DecisionParams expert = SourceSharp.Ai.Tests.Skills.Curves.Game().For(Duel.Preset("expert"));
        Assert.Equal(expert.AimErrorDeg, expert.Hot.AimErrorDeg);
        Assert.Equal(expert.LeadQuality, expert.Hot.LeadQuality);
        for (ulong s = 1; s <= 200; s++)
            Assert.Equal("", AfterAKill("expert", s));
    }

    /// <summary>On by default (the ruling asks for it), and a setting a server can turn off.</summary>
    [Fact]
    public void TheSettingIsOnByDefault()
    {
        ConVarAttribute a = typeof(AiBotStreaksConVar).GetCustomAttribute<ConVarAttribute>()!;
        Assert.Equal("ai_bot_streaks", a.Name);
        Assert.Equal("1", a.DefaultValue);
    }

    /// <summary>Off means off: a bot never told, or told off, runs none, for a kill or from nothing.</summary>
    [Fact]
    public void OffMeansOff()
    {
        for (ulong s = 1; s <= 200; s++)
            Assert.Equal("", AfterAKill("easy", s, []));

        for (ulong s = 1; s <= 200; s++)
        {
            Duel d = Seeded("easy", s);
            SelfState self = d.Self();
            d.Step(self, [], On(d));
            d.Step(self, [], AiEvent.StreaksSet(Duel.Bot, new Tick(d.Tick + 6), AiEvent.StreaksOff));
            d.Step(self, [], Kill(d));
            Assert.Equal("", d.Decider.Doing.Streak);
        }
    }

    /// <summary>Turned off mid-streak, it is over at once.</summary>
    [Fact]
    public void TurnedOffItEndsAtOnce()
    {
        (Duel d, SelfState self) = Hot();
        d.Step(self, [], AiEvent.StreaksSet(Duel.Bot, new Tick(d.Tick + 6), AiEvent.StreaksOff));
        Assert.Equal("", d.Decider.Doing.Streak);
    }

    /// <summary>From nothing it starts now and then: about its rate over many minutes of many bots.</summary>
    [Fact]
    public void FromNothingOneStartsNowAndThen()
    {
        int started = 0;
        const int Bots = 40;
        float minutes = 0f;
        for (ulong s = 1; s <= Bots; s++)
        {
            Duel d = Seeded("easy", s);
            SelfState self = d.Self();
            d.Step(self, [], On(d));
            for (int k = 0; k < (int)(600f / DecideSeconds); k++)
            {
                string was = d.Decider.Doing.Streak;
                d.Step(self, []);
                started += was.Length == 0 && d.Decider.Doing.Streak == "hot" ? 1 : 0;
            }

            minutes += 10f;
        }

        float rate = started / minutes;
        Assert.InRange(rate, Hl2mpDecider.StreakRandomPerMinute * 0.5f, Hl2mpDecider.StreakRandomPerMinute * 1.5f);
    }

    /// <summary>A good bot that died twice in quick succession can go cold, and one quick death, or two far apart, does not.</summary>
    [Fact]
    public void ColdFollowsTwoQuickDeaths()
    {
        int Cold(float apart, bool twice)
        {
            int cold = 0;
            for (ulong s = 1; s <= 200; s++)
            {
                Duel d = Seeded("hard", s);
                SelfState self = d.Self();
                d.Step(self, [], On(d));
                if (twice)
                    d.Step(self, [], Death(d));
                for (int k = 0; k < (int)(apart / DecideSeconds); k++)
                    d.Step(self, []);
                d.Step(self, [], Death(d));
                cold += d.Decider.Doing.Streak == "cold" ? 1 : 0;
            }

            return cold;
        }

        int quick = Cold(5f, true);
        Assert.InRange(quick, (int)(200 * Hl2mpDecider.ColdChance * 0.6f), (int)(200 * Hl2mpDecider.ColdChance * 1.4f));
        Assert.Equal(0, Cold(5f, false));
        Assert.Equal(0, Cold(Hl2mpDecider.ColdDeathSeconds + 5f, true));
    }

    /// <summary>Cold is the same mechanism, smaller: a good bot is shakier, a weak one barely, and it is less than hot's swing for the weak.</summary>
    [Fact]
    public void ColdIsSmallAndForTheGood()
    {
        DecisionParams hard = SourceSharp.Ai.Tests.Skills.Curves.Game().For(Duel.Preset("hard")), easy = SourceSharp.Ai.Tests.Skills.Curves.Game().For(Duel.Preset("easy"));
        Assert.True(hard.Cold.AimErrorDeg > hard.AimErrorDeg, $"cold {hard.Cold.AimErrorDeg}, own {hard.AimErrorDeg}");
        Assert.True(hard.Cold.AimErrorDeg < hard.AimErrorDeg * 1.6f, $"cold {hard.Cold.AimErrorDeg}, own {hard.AimErrorDeg}");
        Assert.True(SkillCurves.ColdLevel(2f) > 1.9f, $"{SkillCurves.ColdLevel(2f)}");
        Assert.True(SkillCurves.ColdLevel(8f) < 7.5f, $"{SkillCurves.ColdLevel(8f)}");
        Assert.True(easy.Cold.AimErrorDeg < easy.AimErrorDeg * 1.1f, $"cold {easy.Cold.AimErrorDeg}, own {easy.AimErrorDeg}");
    }

    /// <summary>The same seed runs the same streaks at the same decides: it comes out of its own stream (§7).</summary>
    [Fact]
    public void TheSameSeedRunsTheSameStreaks()
    {
        string[] Run()
        {
            Duel d = Seeded("easy", 9);
            SelfState self = d.Self();
            d.Step(self, [], On(d));
            var seen = new string[6000];
            for (int k = 0; k < seen.Length; k++)
            {
                d.Step(self, [], k % 400 == 399 ? [Kill(d)] : []);
                seen[k] = d.Decider.Doing.Streak;
            }

            return seen;
        }

        string[] one = Run(), two = Run();
        Assert.Contains("hot", one);
        Assert.Equal(one, two);
    }

    /// <summary>The spread of the aim over a long look at someone: what a streak is for.</summary>
    private static float AimSpread(bool hot)
    {
        var at = new AiVector(700f, 0f, 0f);
        Duel d = Seeded("easy", 3);
        SelfState self = d.Self();
        d.Step(self, [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
        float sum = 0f;
        int n = 0;
        // 8 s is the shortest hot streak; 80 decides is inside it.
        if (hot)
            d.Step(self, [(Duel.Opponent, at)], On(d), AiEvent.StreaksSet(Duel.Bot, new Tick(d.Tick + 6), AiEvent.StreakHot));
        for (int k = 0; k < 80; k++)
        {
            IntentCommand i = d.Step(self, [(Duel.Opponent, at)]);
            sum += MathF.Abs(i.Look.Point.Y);
            n++;
        }

        return sum / n;
    }

    /// <summary>On a hot streak the aim is nearer: it does reach the look, and the look does not change otherwise.</summary>
    [Fact]
    public void OnAHotStreakTheAimIsNearer()
    {
        float own = AimSpread(false), hot = AimSpread(true);
        Assert.True(hot < own * 0.6f, $"hot {hot:F1}, own {own:F1}");
    }

    /// <summary>The main thread's half is told by the record: the reaction and recognition while it lasts, and none after.</summary>
    [Fact]
    public void TheRecordSaysWhatTheMainThreadApplies()
    {
        (Duel d, SelfState self) = Hot();
        DoingRecord on = d.Decider.Doing;
        Assert.Equal(d.Params.Hot.ReactionMs, on.StreakReactionMs);
        Assert.Equal(d.Params.Hot.RecognizeSeconds, on.StreakRecognizeSeconds);
        Assert.Equal("hot", d.Decider.Doing.MomentKind);
        d.Step(self, [], Death(d));
        Assert.Equal(0f, d.Decider.Doing.StreakReactionMs);
        Assert.Equal(0f, d.Decider.Doing.StreakRecognizeSeconds);
    }

    /// <summary>A streak's recognition reaches the vision, and with none the vision is the bot's own.</summary>
    [Fact]
    public void TheVisionTakesAStreaksRecognition()
    {
        SkillProfile easy = Duel.Preset("easy");
        Assert.Equal(VisionCurves.For(easy), VisionCurves.For(easy, 0f));
        Assert.Equal(0.2f, VisionCurves.For(easy, 0.2f).RecognizeSeconds);
    }

    /// <summary>Recognition between levels runs between them.</summary>
    [Fact]
    public void RecognitionBetweenLevelsIsBetweenThem()
    {
        Assert.Equal(0.5f, VisionCurves.RecognizeAt(2f), 3);
        Assert.Equal((0.5f + 0.37f) / 2f, VisionCurves.RecognizeAt(2.5f), 3);
        Assert.Equal(0.04f, VisionCurves.RecognizeAt(10f), 3);
    }

    /// <summary>Only what the ruling names can move: a turn's speed is no part of a streak.</summary>
    [Fact]
    public void AStreakChangesOnlyTheAimLeadReactionAndRecognition()
    {
        string[] names = [.. typeof(StreakParams).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(["AimErrorDeg", "LeadQuality", "ReactionMs", "RecognizeSeconds"], names);
    }

    /// <summary>Aim during a streak stays inside human limits: however quick the reaction asked, the floor holds, and the turn's top speed does not move.</summary>
    [Fact]
    public void AimOnAStreakStaysInsideHumanLimits()
    {
        var parameters = new MotionParams(240f, 400f, 10f, 150f);
        var hot = new HumanMotion(parameters, new AiRandom(5)) { StreakReactionMs = 1f };
        const float Dt = 0.015f;
        hot.Reset(default);
        AiAngles target = new(0f, 120f, 0f);
        float top = 0f;
        int first = -1;
        AiAngles last = hot.View;
        for (int i = 0; i < 400; i++)
        {
            AiAngles now = hot.Step(target, 1, Dt);
            float turned = MathF.Abs(HumanMotion.AngleDelta(last.Yaw, now.Yaw)) / Dt;
            top = MathF.Max(top, turned);
            if (turned > 0f && first < 0)
                first = i;
            last = now;
        }

        Assert.True((first + 1) * Dt >= MotionEnvelope.MinReactionMs / 1000f, $"it moved at decide {first}");
        Assert.True(top <= hot.Parameters.TurnPeakDegPerSec + 1f, $"top {top:F0} over {hot.Parameters.TurnPeakDegPerSec:F0}");
    }

    /// <summary>A streak's reaction is the one a new stimulus waits, and with none it waits its own.</summary>
    [Fact]
    public void ANewStimulusWaitsTheStreaksReaction()
    {
        int Waited(float streak)
        {
            var m = new HumanMotion(new MotionParams(240f, 400f, 10f, 150f), new AiRandom(5)) { StreakReactionMs = streak };
            m.Reset(default);
            for (int i = 0; i < 200; i++)
            {
                if (m.Step(new AiAngles(0f, 120f, 0f), 1, 0.015f).Yaw != 0f)
                    return i;
            }

            return -1;
        }

        Assert.True(Waited(120f) < Waited(0f), $"{Waited(120f)} of {Waited(0f)}");
    }
}
