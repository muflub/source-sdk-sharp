using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// Firing at where someone is believed to be (plan_hl2mp_ai §5.4, user request
/// 2026-11-02: "Make bots prefire spots it thinks players are at"). Only from what the
/// bot knows, only down a line it was told is clear, in short bursts, ended by seeing
/// anyone; what scales it is Cunning and Discipline.
/// </summary>
public sealed class PrefireTests
{
    private static readonly AiVector There = new(500f, 0f, 0f);

    private static SkillProfile Sprayer(params (string Skill, int Level)[] more)
    {
        SkillProfile p = Duel.Preset("normal");
        p = p.With(p.IndexOf("cunning"), 10);
        p = p.With(p.IndexOf("discipline"), 0);
        foreach ((string skill, int level) in more)
            p = p.With(p.IndexOf(skill), level);
        return p;
    }

    /// <summary>Answers the bot's look along a line, as the vision pass does next tick: clear or not.</summary>
    private static AiEvent[] Answer(Duel d, bool clear)
    {
        DoingRecord doing = d.Decider.Doing;
        return doing.LineAsk > 0 ? [AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 1), doing.LineAt, doing.LineAsk, clear)] : [];
    }

    /// <summary>Seen, then out of sight: the moment someone ducks behind a corner.</summary>
    private static void SeeThenLose(Duel d, SelfState self, AiVector at)
    {
        d.Step(self, [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
        d.Step(self, [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, at, default));
    }

    /// <summary>Decides on which the bot's intent fires, over <paramref name="decides"/>, every line answered as <paramref name="clear"/>.</summary>
    private static int FiringDecides(Duel d, SelfState self, int decides, bool clear = true, Action<IntentCommand>? each = null)
    {
        int firing = 0;
        for (int k = 0; k < decides; k++)
        {
            IntentCommand i = d.Step(self, [], Answer(d, clear));
            each?.Invoke(i);
            firing += i.Action.Kind == ActionKind.Fire ? 1 : 0;
        }

        return firing;
    }

    /// <summary>Sprays the corner: fire on, the view on where they were, and the inspector says so, within a human delay.</summary>
    [Fact]
    public void ItFiresAtTheCornerSomeoneWentBehind()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        SeeThenLose(d, self, There);
        IntentCommand i = default;
        int decide = 0;
        for (; decide < 10 && i.Action.Kind != ActionKind.Fire; decide++)
            i = d.Step(self, [], Answer(d, true));
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
        Assert.Equal(LookKind.CornerCheck, i.Look.Kind);
        Assert.True(i.Look.Point.DistanceTo(There + new AiVector(0f, 0f, Hl2mpDecider.PrefireChest)) < 1f, $"at {i.Look.Point}");
        Assert.Equal("firing", d.Decider.Doing.Prefire);
        Assert.True(decide <= 3, $"it took {decide} decides");
    }

    /// <summary>No belief, no prefire, whatever anyone answers: a line answered clear with nobody it knows of is no reason to fire.</summary>
    [Fact]
    public void ABotWithNoBeliefNeverPrefires()
    {
        var d = new Duel(Sprayer());
        for (int k = 0; k < 2000; k++)
        {
            IntentCommand i = d.Step(d.Self(), [], AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 1), There, k + 1, true));
            Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
            Assert.Equal("", d.Decider.Doing.Prefire);
            Assert.Equal(0, d.Decider.Doing.LineAsk);
        }
    }

    /// <summary>A belief gone stale is no belief: past its seconds it no longer asks.</summary>
    [Fact]
    public void AStaleBeliefIsNotFiredAt()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        SeeThenLose(d, self, There);
        for (int k = 0; k < (int)(Hl2mpDecider.PrefireSeenSeconds / 0.09f) + 2; k++)
            d.Step(self, [], Answer(d, false));
        int firing = FiringDecides(d, self, 100);
        Assert.Equal(0, firing);
    }

    /// <summary>The line is the wall's answer: blocked, it never fires, however sure it is, and it goes on with what it was doing.</summary>
    [Fact]
    public void ItDoesNotFireThroughAWall()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        SeeThenLose(d, self, There);
        int asks = 0;
        Assert.Equal(0, FiringDecides(d, self, 200, clear: false, _ => asks = Math.Max(asks, d.Decider.Doing.LineAsk)));
        Assert.True(asks > 0, "the stimulus: it did look along a line");
    }

    /// <summary>The same scene with no answer at all: it waits for one, it does not assume.</summary>
    [Fact]
    public void NoAnswerIsNoLine()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        SeeThenLose(d, self, There);
        for (int k = 0; k < 40; k++)
            Assert.NotEqual(ActionKind.Fire, d.Step(self, []).Action.Kind);
    }

    /// <summary>An answer for an old place says nothing of this one.</summary>
    [Fact]
    public void AnAnswerAboutElsewhereIsNotClear()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        SeeThenLose(d, self, There);
        for (int k = 0; k < 40; k++)
        {
            DoingRecord doing = d.Decider.Doing;
            AiEvent[] wrong = doing.LineAsk > 0 ? [AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 1), doing.LineAt + new AiVector(100f, 0f, 0f), doing.LineAsk, true)] : [];
            Assert.NotEqual(ActionKind.Fire, d.Step(self, [], wrong).Action.Kind);
        }
    }

    /// <summary>Seeing anyone ends it the decide it sees them, with the burst unfinished, and the view goes to them.</summary>
    [Fact]
    public void SeeingSomeoneEndsIt()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        SeeThenLose(d, self, There);
        IntentCommand i = default;
        for (int k = 0; k < 10 && i.Action.Kind != ActionKind.Fire; k++)
            i = d.Step(self, [], Answer(d, true));
        Assert.Equal("firing", d.Decider.Doing.Prefire);
        var them = new AiVector(300f, 200f, 0f);
        i = d.Step(self, [(Duel.Other, them)], d.SeeAt(Duel.Other, them));
        Assert.Equal("", d.Decider.Doing.Prefire);
        Assert.Equal(LookKind.Actor, i.Look.Kind);
        Assert.Equal(Duel.Other, i.Look.Actor);
    }

    /// <summary>A short burst, then it checks: under a second on the trigger, and a pause before the next.</summary>
    [Fact]
    public void ABurstIsShortThenItChecks()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        SeeThenLose(d, self, There);
        var runs = new List<int>();
        var gaps = new List<int>();
        int run = 0, gap = 0;
        for (int k = 0; k < 400; k++)
        {
            // Hearing them keeps the belief fresh, so the only limit is its own.
            AiEvent[] events = [.. Answer(d, true), AiEvent.SoundHeard(Duel.Bot, new Tick(d.Tick + 6), SoundKind.Footstep, There, 0.8f, Duel.Opponent, -1)];
            if (d.Step(self, [], events).Action.Kind == ActionKind.Fire)
            {
                if (gap > 0 && runs.Count > 0)
                    gaps.Add(gap);
                gap = 0;
                run++;
            }
            else
            {
                if (run > 0)
                    runs.Add(run);
                run = 0;
                gap++;
            }
        }

        Assert.True(runs.Count >= 3, $"{runs.Count} bursts");
        // A decide is 6 ticks of 15 ms.
        Assert.All(runs, r => Assert.True(r * 0.09f <= 1f, $"a burst of {r * 0.09f} s"));
        Assert.All(gaps, g => Assert.True(g * 0.09f >= 1.8f, $"a pause of {g * 0.09f} s"));
    }

    /// <summary>Fire takes no more than Cunning and Discipline allow: over seeds, how many of a hundred lost sights get a burst.</summary>
    private static int Bursts(SkillProfile p)
    {
        int bursts = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var d = new Duel(p, Duel.Bot, seed, null, null, null);
            SelfState self = d.Self();
            SeeThenLose(d, self, There);
            FiringDecides(d, self, 12);
            bursts += d.Decider.Prefires;
        }

        return bursts;
    }

    /// <summary>A Discipline-heavy bot saves its ammo: the bursts fall as the level rises, with the rest at Medium, and the control is that they ever happen.</summary>
    [Fact]
    public void DisciplineFallsMonotonically()
    {
        SkillProfile basis = Duel.Preset("normal");
        basis = basis.With(basis.IndexOf("cunning"), 8);
        int[] byLevel = [.. Enumerable.Range(0, 11).Select(l => Bursts(basis.With(basis.IndexOf("discipline"), l)))];
        string all = string.Join(", ", byLevel);
        for (int l = 1; l < byLevel.Length; l++)
            Assert.True(byLevel[l] <= byLevel[l - 1] + 6, $"{all}");
        Assert.True(byLevel[0] > byLevel[10] * 2, all);
        Assert.True(byLevel[0] > 20, all);
    }

    [Fact]
    public void CunningRisesMonotonically()
    {
        SkillProfile basis = Duel.Preset("normal");
        int[] byLevel = [.. Enumerable.Range(0, 11).Select(l => Bursts(basis.With(basis.IndexOf("cunning"), l)))];
        string all = string.Join(", ", byLevel);
        for (int l = 1; l < byLevel.Length; l++)
            Assert.True(byLevel[l] >= byLevel[l - 1] - 6, all);
        Assert.Equal(0, byLevel[0]);
        Assert.True(byLevel[10] > 30, all);
    }

    /// <summary>A disciplined bot's burst is shorter, as well as rarer.</summary>
    [Fact]
    public void DisciplineShortensTheBurst()
    {
        SkillProfile p = Duel.Preset("normal");
        float wild = SourceSharp.Ai.Tests.Skills.Curves.Game().For(p.With(p.IndexOf("discipline"), 0)).PrefireBurstSeconds;
        float cool = SourceSharp.Ai.Tests.Skills.Curves.Game().For(p.With(p.IndexOf("discipline"), 10)).PrefireBurstSeconds;
        Assert.True(cool < wild, $"{cool}, {wild}");
        Assert.True(wild < 1f, $"a person sprays a corner for under a second: {wild}");
    }

    /// <summary>Not with the last rounds, not with a bolt: the control is a full clip of the same gun.</summary>
    [Theory]
    [InlineData(Duel.Smg1, 45, true)]
    [InlineData(Duel.Smg1, 10, false)]
    [InlineData(Duel.Pistol, 18, true)]
    [InlineData(Duel.Pistol, 4, false)]
    [InlineData(Duel.Crossbow, 1, false)]
    [InlineData(Duel.Crowbar, 0, false)]
    public void ItKeepsItsLastRoundsAndItsBolts(int weapon, int clip, bool fires)
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self(clip: clip, weapon: weapon);
        SeeThenLose(d, self, There);
        int firing = FiringDecides(d, self, 12);
        Assert.Equal(fires, firing > 0);
    }

    /// <summary>What it is, not only how much it holds: a bow of any clip does not spray.</summary>
    [Fact]
    public void ACrossbowOfAnyClipDoesNot()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self(clip: 10, weapon: Duel.BigBow);
        SeeThenLose(d, self, There);
        Assert.Equal(0, FiringDecides(d, self, 12));
    }

    /// <summary>Beyond the gun's reach, and behind it, it holds: the same bot fires at that place within reach and in front.</summary>
    [Fact]
    public void OutOfReachOrBehindItHolds()
    {
        var far = new Duel(Sprayer());
        // Within the hunt's reach, past this gun's: the bot is still about them.
        SeeThenLose(far, far.Self(), new AiVector(2400f, 0f, 0f));
        Assert.Equal(Duel.Opponent, far.Decider.Doing.Target);
        Assert.Equal(0, FiringDecides(far, far.Self(), 12));
        var behind = new Duel(Sprayer());
        SeeThenLose(behind, behind.Self(), new AiVector(-500f, 0f, 0f));
        Assert.Equal(0, FiringDecides(behind, behind.Self(), 12));
    }

    /// <summary>It moved since it looked: the answer was of another line.</summary>
    [Fact]
    public void AnAnswerGoesStaleWhenItMoves()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        SeeThenLose(d, self, There);
        d.Step(self, [], Answer(d, true));
        SelfState moved = self with { Origin = self.Origin + new AiVector(0f, Hl2mpDecider.PrefireLineMoved + 1f, 0f) };
        for (int k = 0; k < 2; k++)
        {
            IntentCommand i = d.Step(moved, [], Answer(d, true));
            if (k == 0)
                Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
        }
    }

    /// <summary>What it heard is a belief as well: a footstep round the corner, nobody ever seen.</summary>
    [Fact]
    public void ItFiresAtWhatItHeard()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        d.Step(self, [], AiEvent.SoundHeard(Duel.Bot, new Tick(d.Tick + 6), SoundKind.Footstep, There, 0.9f, Duel.Opponent, -1));
        Assert.True(FiringDecides(d, self, 12) > 0, $"branch {d.Decider.Doing.Branch}, leaf {d.Decider.Doing.Leaf}");
    }

    /// <summary>Steady: a bot aiming and firing at a corner allocates nothing a decide.</summary>
    [Fact]
    public void PrefiringAllocatesNothing()
    {
        var d = new Duel(Sprayer());
        SelfState self = d.Self();
        SeeThenLose(d, self, There);
        var events = new AiEvent[1];
        long bytes = SourceSharp.Ai.Tests.Core.SteadyAllocation.Measure(() =>
        {
            DoingRecord doing = d.Decider.Doing;
            events[0] = AiEvent.SoundHeard(Duel.Bot, new Tick(d.Tick + 6), SoundKind.Footstep, There, 0.9f, Duel.Opponent, -1);
            d.Step(self, Array.Empty<(ActorId, AiVector)>(), events);
            _ = doing;
        }, 200);
        Assert.Equal(0, bytes);
    }
}
