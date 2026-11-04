using SourceSharp.Ai.Hl2mp.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// Personality you can notice (plan_hl2mp_ai §5.5, user ruling 2026-11-02): grudges,
/// habits a bot visibly acts on, and traits that make bots of one difficulty play
/// differently. None of it gives a bot knowledge it has not earned.
/// </summary>
public sealed class PersonalityTests
{
    private static readonly AiVector Left = new(-500f, 0f, 0f), Right = new(500f, 0f, 0f);

    private static SkillProfile With(string preset, params (string Skill, int Level)[] levels)
    {
        SkillProfile p = Duel.Preset(preset);
        foreach ((string skill, int level) in levels)
            p = p.With(p.IndexOf(skill), level);
        return p;
    }

    private static AiEvent KilledBy(Duel d, ActorId who) => AiEvent.Killed(Duel.Bot, new Tick(d.Tick + 6), who);

    private static AiEvent Feed(Duel d, ActorId victim, ActorId killer) => AiEvent.KillFeed(Duel.Bot, new Tick(d.Tick + 6), victim, killer);

    /// <summary>Both opponents seen and lost, both about as near.</summary>
    private static Duel TwoLost(SkillProfile p)
    {
        var d = new Duel(p);
        d.Step(d.Self(), [(Duel.Opponent, Right), (Duel.Other, Left)], [.. d.SeeAt(Duel.Opponent, Right), .. d.SeeAt(Duel.Other, Left)]);
        return d;
    }

    private static AiEvent[] Lost(Duel d) =>
    [
        AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, Right, default),
        AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Other, Left, default),
    ];

    // ---- grudges ------------------------------------------------------------

    /// <summary>Two equal targets in sight: it takes the one who has been killing it, at high Tenacity.</summary>
    [Fact]
    public void AGrudgeTakesItOverAnEqualTarget()
    {
        foreach (ActorId enemy in new[] { Duel.Opponent, Duel.Other })
        {
            var d = new Duel(With("normal", ("tenacity", 10)));
            d.Step(d.Self(), [(Duel.Opponent, Right), (Duel.Other, Left)], [.. d.SeeAt(Duel.Opponent, Right), .. d.SeeAt(Duel.Other, Left), KilledBy(d, enemy), KilledBy(d, enemy)]);
            d.Step(d.Self(), [(Duel.Opponent, Right), (Duel.Other, Left)]);
            Assert.Equal(enemy, d.Decider.Doing.Target);
            Assert.True(d.Decider.Doing.GrudgeTerm > 0f);
        }
    }

    /// <summary>The control: nobody has killed it, so it holds whoever it took first, and at Tenacity 0 a grudge weighs nothing.</summary>
    [Fact]
    public void AtTenacityZeroAGrudgeWeighsNothing()
    {
        var d = new Duel(With("normal", ("tenacity", 0)));
        d.Step(d.Self(), [(Duel.Opponent, Right), (Duel.Other, Left)], [.. d.SeeAt(Duel.Opponent, Right), .. d.SeeAt(Duel.Other, Left), KilledBy(d, Duel.Other), KilledBy(d, Duel.Other)]);
        d.Step(d.Self(), [(Duel.Opponent, Right), (Duel.Other, Left)]);
        Assert.Equal(0f, d.Decider.Doing.GrudgeTerm);
        Assert.Equal(Duel.Opponent, d.Decider.Doing.Target);
    }

    /// <summary>A grudge changes the choice, not the information: someone it has no idea of is no target, however it hates them.</summary>
    [Fact]
    public void AGrudgeGivesNoKnowledgeOfWhereTheyAre()
    {
        var d = new Duel(With("normal", ("tenacity", 10)));
        d.Step(d.Self(), [], KilledBy(d, Duel.Other));
        Assert.True(d.Memory.Board.Of(Duel.Other, make: false)!.Confidence < 0.05f);
        d.Step(d.Self(), []);
        Assert.Equal(ActorId.None, d.Decider.Doing.Target);
    }

    /// <summary>It fades by half in its half life.</summary>
    [Fact]
    public void AGrudgeFades()
    {
        var d = new Duel(With("normal", ("tenacity", 10)));
        d.Step(d.Self(), [], KilledBy(d, Duel.Other));
        KnownActor a = d.Memory.Board.Of(Duel.Other, make: false)!;
        var now = new Tick(d.Tick);
        Assert.Equal(1f, a.GrudgeAt(now, 0.015f), 3);
        Assert.Equal(0.5f, a.GrudgeAt(now + (int)(KnownActor.GrudgeHalfLifeSeconds / 0.015f), 0.015f), 2);
    }

    /// <summary>Killing them back halves it and is a moment, for the log and chat; a kill of someone it had no grudge against is not.</summary>
    [Fact]
    public void KillingThemBackIsRevenge()
    {
        var d = new Duel(With("normal", ("tenacity", 10)));
        d.Step(d.Self(), [], KilledBy(d, Duel.Other), KilledBy(d, Duel.Other));
        d.Step(d.Self(), [], Feed(d, Duel.Opponent, Duel.Bot));
        Assert.Equal("", d.Decider.Doing.MomentKind);
        d.Step(d.Self(), [], Feed(d, Duel.Other, Duel.Bot));
        d.Step(d.Self(), []);
        Assert.Equal(("revenge", Duel.Other, 1), (d.Decider.Doing.MomentKind, d.Decider.Doing.MomentOther, d.Decider.Doing.MomentSeq));
        Assert.InRange(d.Memory.Board.Of(Duel.Other, make: false)!.GrudgeAt(new Tick(d.Tick), 0.015f), 0.9f, 1.05f);
    }

    /// <summary>Live, the moment's number fell to 0 on each death, which read as a new moment on the next life: said again and again.</summary>
    [Fact]
    public void ADeathDoesNotSayTheLastMomentAgain()
    {
        var d = new Duel(With("normal", ("tenacity", 10)));
        d.Step(d.Self(), [], KilledBy(d, Duel.Other));
        d.Step(d.Self(), [], Feed(d, Duel.Other, Duel.Bot));
        d.Step(d.Self(), []);
        Assert.Equal(1, d.Decider.Doing.MomentSeq);
        d.Step(d.Self() with { Flags = SelfFlags.None }, []);
        Assert.Equal("dead", d.Decider.Doing.Branch);
        Assert.Equal(1, d.Decider.Doing.MomentSeq);
        d.Step(d.Self(), []);
        Assert.Equal(1, d.Decider.Doing.MomentSeq);
    }

    /// <summary>Hunting: a grudge makes the hunt outrank what it was doing.</summary>
    [Fact]
    public void AGrudgeMakesTheHuntPullHarder()
    {
        static float HuntScore(int tenacity, int killed)
        {
            Duel d = TwoLost(With("normal", ("tenacity", tenacity)));
            AiEvent[] kills = [.. Enumerable.Repeat(KilledBy(d, Duel.Opponent), killed), .. Lost(d)];
            d.Step(d.Self(), [], kills);
            for (int k = 0; k < 120; k++)
                d.Step(d.Self() with { Origin = new AiVector(k % 2 * 40f, 0f, 0f) }, []);
            return d.Decider.Trace.Count > 0 ? d.Decider.Doing.GrudgeTerm : -1f;
        }

        Assert.True(HuntScore(10, 2) > HuntScore(10, 0));
    }

    // ---- habits -------------------------------------------------------------

    /// <summary>A bot hunting someone over a graph, who has camped at a place this many times.</summary>
    private static (Duel D, AiVector Spot) Hunting(int cunning, int camped)
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse([.. Enumerable.Repeat(new string('.', 40), 40)]);
        var d = new Duel(With("normal", ("cunning", cunning), ("cover", 0)), g, new ItemSpawn(0, "item_battery", ItemKind.Armour, new AiVector(9000f, 0f, 0f), 30f));
        CellId cell = SourceSharp.Ai.Tests.Nav.Grids.At(ids, 20, 20);
        for (int i = 0; i < camped; i++)
            d.Memory.HabitsOf(Duel.Opponent).Camped(cell);
        return (d, g.Cell(cell).Centre);
    }

    /// <summary>Seen, lost, and approached: one approach's worth of decides.</summary>
    private static void Approach(Duel d, AiVector at)
    {
        d.Step(d.Self(), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
        d.Step(d.Self(), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, at, default));
        for (int k = 0; k < 10; k++)
            d.Step(d.Self(), []);
    }

    [Fact]
    public void WithNoHabitKnownThereIsNoPreAim()
    {
        (Duel d, AiVector spot) = Hunting(10, 1);
        Approach(d, spot);
        Assert.Equal(0, d.Decider.HabitApproaches);
        Assert.False(d.Decider.Doing.PreAiming);
    }

    /// <summary>The scenario the ruling asks: the first visit it knows nothing and goes the plain way; once it has found them there twice, it looks at the corner.</summary>
    [Fact]
    public void AHabitLearntChangesTheApproachBetweenVisits()
    {
        (Duel d, AiVector spot) = Hunting(10, 2);
        for (int i = 0; i < 30; i++)
            Approach(d, spot);
        Assert.True(d.Decider.HabitApproaches >= 25, $"{d.Decider.HabitApproaches}");
        Assert.True(d.Decider.PreAims >= d.Decider.HabitApproaches * 0.8f, $"pre-aims {d.Decider.PreAims} of {d.Decider.HabitApproaches}");
    }

    /// <summary>Cunning scales how much: pre-aims and other doors rise with it.</summary>
    [Fact]
    public void CunningScalesHowMuchAHabitIsUsed()
    {
        static (double Aim, double Flank) Rate(int cunning)
        {
            (Duel d, AiVector spot) = Hunting(cunning, 3);
            for (int i = 0; i < 150; i++)
                Approach(d, spot);
            return (d.Decider.PreAims / (double)d.Decider.HabitApproaches, d.Decider.HabitFlanks / (double)d.Decider.HabitApproaches);
        }

        (double aim0, double flank0) = Rate(0);
        (double aim5, _) = Rate(5);
        (double aim10, double flank10) = Rate(10);
        Assert.Equal(0, aim0);
        Assert.True(aim5 > aim0 && aim10 > aim5, $"{aim0} {aim5} {aim10}");
        Assert.True(flank10 > flank0, $"{flank0} {flank10}");
    }

    /// <summary>Someone shuffling between cells of one place has one habit; two times found far apart are none.</summary>
    [Fact]
    public void CampsTogetherAreOnePlaceAndApartAreNot()
    {
        static int Known(int dx)
        {
            var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse([.. Enumerable.Repeat(new string('.', 40), 40)]);
            var d = new Duel(With("normal", ("cunning", 10), ("cover", 0)), g, new ItemSpawn(0, "item_battery", ItemKind.Armour, new AiVector(9000f, 0f, 0f), 30f));
            d.Memory.HabitsOf(Duel.Opponent).Camped(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 20, 20));
            d.Memory.HabitsOf(Duel.Opponent).Camped(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 20 + dx, 20));
            Approach(d, g.Cell(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 20, 20)).Centre);
            return d.Decider.HabitApproaches;
        }

        Assert.True(Known(1) > 0, "two cells side by side");
        Assert.Equal(0, Known(15));
    }

    /// <summary>Killing someone where it was just seen is finding it there: a person who dies at their spot has a spot.</summary>
    [Fact]
    public void KillingThemWhereTheyWereSeenCountsAsFindingThemThere()
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse([.. Enumerable.Repeat(new string('.', 40), 40)]);
        var d = new Duel(With("normal", ("cunning", 10)), g, new ItemSpawn(0, "item_battery", ItemKind.Armour, new AiVector(9000f, 0f, 0f), 30f));
        AiVector there = g.Cell(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 20, 20)).Centre;
        d.Step(d.Self(), [(Duel.Opponent, there)], d.SeeAt(Duel.Opponent, there));
        d.Step(d.Self(), [(Duel.Opponent, there)], Feed(d, Duel.Opponent, Duel.Other));
        Assert.True(d.Memory.TryHabits(Duel.Opponent, out OpponentHabits? h));
        Assert.Contains(h!.Camps.ToArray(), c => c.Count >= 0.9f);     // a habit's counts fade from the first tick
    }

    /// <summary>Near the corner the view is on it, wherever it last saw them; with no such look it would be on where it last saw them.</summary>
    [Fact]
    public void APreAimLooksAtTheUsualCornerFromNearby()
    {
        (Duel d, AiVector spot) = Hunting(10, 3);
        AiVector lastSeen = spot + new AiVector(160f, 320f, 0f);
        AiVector near = spot + new AiVector(400f, 0f, 0f);
        IntentCommand i = default;
        bool aimed = false;
        for (int a = 0; a < 20 && !aimed; a++)
        {
            d.Step(d.Self() with { Origin = near }, [(Duel.Opponent, lastSeen)], d.SeeAt(Duel.Opponent, lastSeen));
            d.Step(d.Self() with { Origin = near }, [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, lastSeen, default));
            for (int k = 0; k < 12 && !aimed; k++)
            {
                i = d.Step(d.Self() with { Origin = near + new AiVector(k % 2 * 40f, 0f, 0f) }, []);
                aimed = d.Decider.Doing.PreAiming;
            }
        }

        Assert.True(aimed, "it never pre-aimed");
        Assert.Equal(LookKind.Point, i.Look.Kind);
        Assert.Equal(spot + new AiVector(0f, 0f, Hl2mpDecider.PlaceHeight), i.Look.Point);
    }

    /// <summary>Finding them there is a moment; out in the open it is not.</summary>
    [Fact]
    public void FindingThemAtTheCornerItExpectedIsAMoment()
    {
        (Duel d, AiVector spot) = Hunting(10, 3);
        int seq = 0;
        for (int a = 0; a < 12 && seq == 0; a++)
        {
            Approach(d, spot + new AiVector(1500f, 0f, 0f));
            seq = d.Decider.Doing.MomentSeq;
        }

        Assert.Equal(0, seq);
        for (int a = 0; a < 12 && seq == 0; a++)
        {
            Approach(d, spot);
            seq = d.Decider.Doing.MomentSeq;
        }

        Assert.True(seq > 0, "no moment");
        Assert.Equal(("spot", Duel.Opponent), (d.Decider.Doing.MomentKind, d.Decider.Doing.MomentOther));
    }

    // ---- traits -------------------------------------------------------------

    /// <summary>A small simulation over a graph: the body goes where the intent says, at the run speed; an opponent shows itself now and then.</summary>
    private static (double Walked, double Holding, double FightRange) Play(SkillProfile p)
    {
        var (g, _) = SourceSharp.Ai.Tests.Nav.Grids.Parse([.. Enumerable.Repeat(new string('.', 60), 60)]);
        ItemSpawn[] items = [.. Enumerable.Range(0, 6).Select(i => new ItemSpawn(i, "item_battery", ItemKind.Armour, g.Cell(g.CellCount / 7 * (i + 1)).Centre, 30f))];
        var d = new Duel(p, g, items);
        AiVector at = g.Cell(g.CellCount / 2).Centre;
        double walked = 0, range = 0;
        int holding = 0, sighted = 0;
        const int Steps = 3000;
        for (int s = 0; s < Steps; s++)
        {
            // Every 300 steps someone shows for 40 at 700 u, then goes.
            bool show = s % 300 < 40;
            AiVector them = at + new AiVector(700f, 0f, 0f);
            SelfState self = d.Self() with { Origin = at };
            IntentCommand c = show ? d.Step(self, [(Duel.Opponent, them)], s % 300 == 0 ? d.SeeAt(Duel.Opponent, them) : [])
                                   : d.Step(self, [], s % 300 == 40 ? [AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, them, default)] : []);
            if (show)
            {
                sighted++;
                range += d.Decider.Doing.HasGoal ? at.DistanceTo(d.Decider.Doing.Goal) : 700f;
            }

            if (c.Move.Kind is MoveKind.Follow or MoveKind.Retreat && c.Move.CornerCount > 0)
            {
                AiVector to = c.Move.Corner0 - at;
                float step = Math.Min(to.Length, (c.Move.Speed == MoveSpeed.Walk ? 95f : 190f) * 6 * 0.015f);
                at += to.Normalised() * step;
                walked += step;
            }
            else if (!show)
            {
                holding++;
            }
        }

        return (walked / Steps, holding / (double)Steps, sighted > 0 ? range / sighted : 0);
    }

    private static readonly (string, int)[] Camper = [("cover", 10), ("cunning", 10), ("aggression", 1), ("tenacity", 2)];
    private static readonly (string, int)[] Chaser = [("cover", 1), ("cunning", 1), ("aggression", 10), ("tenacity", 8)];

    /// <summary>At one difficulty, a camper holds still and a chaser covers the ground.</summary>
    [Theory]
    [InlineData("easy")]
    [InlineData("normal")]
    [InlineData("hard")]
    public void OppositeSpreadsPlayDifferently(string preset)
    {
        (double walkedCamp, double holdCamp, _) = Play(With(preset, Camper));
        (double walkedChase, double holdChase, _) = Play(With(preset, Chaser));
        // This floor keeps a chaser hovering at a belief it has reached, so walked says less than holding: the live run has the distances (§12.3).
        Assert.True(walkedChase > walkedCamp, $"walked: chaser {walkedChase:F1}, camper {walkedCamp:F1}; held {holdChase:F2}, {holdCamp:F2}; {preset}");
        Assert.True(holdCamp > holdChase + 0.2, $"holding: camper {holdCamp:F2}, chaser {holdChase:F2}");
    }

    /// <summary>A bot on its spot watches across the place, moving more than a drift: it is holding an angle, not switched off.</summary>
    [Fact]
    public void AHolderWatchesAcrossThePlace()
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse([.. Enumerable.Repeat(new string('.', 60), 60)]);
        AiVector item = g.Cell(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 30, 30)).Centre;
        var d = new Duel(With("normal", ("cover", 10), ("cunning", 10)), g, new ItemSpawn(0, "item_battery", ItemKind.Armour, item, 30f));
        AiVector from = item + new AiVector(300f, 0f, 0f);
        IntentCommand i = default;
        for (int k = 0; k < 40 && d.Decider.Doing.Branch != "Ambush"; k++)
            i = d.Step(d.Self() with { Origin = from }, []);
        AiVector spot = i.Move.Corner0;
        float toward = MathF.Atan2(item.Y - spot.Y, item.X - spot.X);
        float least = float.MaxValue, most = float.MinValue;
        for (int k = 0; k < 60; k++)
        {
            i = d.Step(d.Self() with { Origin = spot }, []);
            Assert.Equal(MoveKind.Hold, i.Move.Kind);
            float yaw = (float)AiMetrics.Delta(new AiAngles(0f, toward * (180f / MathF.PI), 0f), new AiAngles(0f, MathF.Atan2(i.Look.Point.Y - spot.Y, i.Look.Point.X - spot.X) * (180f / MathF.PI), 0f)).Yaw;
            least = Math.Min(least, yaw);
            most = Math.Max(most, yaw);
        }

        Assert.InRange(most - least, 25f, 41f);
    }

    /// <summary>With nobody to hunt: an even bot roams, and a high Cover holds a place.</summary>
    [Theory]
    [InlineData(3, "Roam")]
    [InlineData(10, "Ambush")]
    public void WithNobodyToHuntOnlyAHighCoverHoldsAPlace(int cover, string branch)
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse([.. Enumerable.Repeat(new string('.', 40), 40)]);
        AiVector item = g.Cell(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 25, 25)).Centre;
        var d = new Duel(With("normal", ("cunning", 3), ("cover", cover)), g, new ItemSpawn(0, "item_battery", ItemKind.Armour, item, 30f));
        for (int k = 0; k < 5; k++)
            d.Step(d.Self() with { Origin = new AiVector(k % 2 * 40f, 0f, 0f) }, []);
        Assert.Equal(branch, d.Decider.Doing.Branch);
    }

    /// <summary>Someone remembered, past the chase: at one Cunning, enough to wait over a pickup, high Aggression goes after them and low waits.</summary>
    [Theory]
    [InlineData(10, "Hunt")]
    [InlineData(0, "Ambush")]
    public void AggressionGoesHuntingWhereCunningWouldWait(int aggression, string branch)
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse([.. Enumerable.Repeat(new string('.', 40), 40)]);
        AiVector item = g.Cell(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 25, 25)).Centre;
        var d = new Duel(With("normal", ("cunning", 9), ("cover", 0), ("aggression", aggression), ("tenacity", 10)), g, new ItemSpawn(0, "item_battery", ItemKind.Armour, item, 30f));
        var there = new AiVector(800f, 200f, 0f);
        d.Step(d.Self(), [(Duel.Opponent, there)], d.SeeAt(Duel.Opponent, there));
        d.Step(d.Self(), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, there, default));
        // Past the longest chase (8 s) and the commitment that follows it.
        for (int k = 0; k < 100; k++)
            d.Step(d.Self() with { Origin = new AiVector(k % 2 * 40f, 0f, 0f) }, []);
        Assert.Equal(branch, d.Decider.Doing.Branch);
    }

    /// <summary>The map's sight: only what stands on this side of <paramref name="edge"/> sees the watched place.</summary>
    private sealed class SeenFromLeftOf(float edge, NavGraph graph) : ICellSight
    {
        public float Range => 8192f;

        public bool Visible(int a, int b) => graph.Cell(b).Centre.X < edge;

        public float Exposure(int cell) => 0f;
    }

    [Fact]
    public void ACamperHoldsAPlaceFromWhereItCanSeeIt()
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse([.. Enumerable.Repeat(new string('.', 60), 60)]);
        AiVector item = g.Cell(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 30, 30)).Centre;
        var d = new Duel(With("normal", ("cover", 10), ("cunning", 10)), g, new SeenFromLeftOf(item.X + 200f, g), new ItemSpawn(0, "item_battery", ItemKind.Armour, item, 30f));
        // Coming from the far side, where 384 u back along the line cannot see it.
        AiVector from = item + new AiVector(600f, 0f, 0f);
        IntentCommand i = default;
        for (int k = 0; k < 200 && d.Decider.Doing.Branch != "Ambush"; k++)
            i = d.Step(d.Self() with { Origin = from + new AiVector(k % 2 * 40f, 0f, 0f) }, []);
        Assert.Equal("Ambush", d.Decider.Doing.Branch);
        Assert.True(i.Move.Corner0.X < item.X + 200f, $"it would hold from {i.Move.Corner0.X}, the place at {item.X}");
    }

    /// <summary>A place to hold on no one's habit is for a high Cover or Cunning, not for everyone.</summary>
    [Fact]
    public void OnlyTheCunningOrCoveredCampOnNoHabit()
    {
        static double Holding(params (string, int)[] levels) => Play(With("normal", levels)).Holding;
        double even = Holding(("cover", 3), ("cunning", 3));
        Assert.True(even < 0.1, $"an even bot holds {even:F2} of the time");
        Assert.True(Holding(("cover", 10), ("cunning", 3)) > even + 0.2);
        Assert.True(Holding(("cover", 3), ("cunning", 10)) > even + 0.2);
    }
}
