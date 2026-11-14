using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Senses;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde's sight (plan_descent_logic HO5) with no engine: the per-tick
/// line budget is never exceeded and serves every pair in turn; range, cone and
/// the PVS cost no line; a hero must stay clear for the robot's recognition
/// time, which Alertness shortens.
/// </summary>
public sealed class HordeVisionTests
{
    private sealed class Told : IHordeSightSink
    {
        public List<(int Body, int Hero, long Tick)> Gains { get; } = [];
        public List<(int Body, int Hero)> Losses { get; } = [];
        public long Now { get; set; }

        public void Gained(int body, int hero, AiVector at, AiVector velocity, float fraction) => Gains.Add((body, hero, Now));

        public void Lost(int body, int hero, AiVector at, AiVector velocity) => Losses.Add((body, hero));
    }

    private static BodyEye Eye(AiVector at, float yaw = 0f, float range = 4000f, int recognize = 5) =>
        new(true, at, yaw, range, MathF.Cos(MathF.PI / 4f), recognize, 34);

    private static HeroTarget Hero(AiVector feet) => new(true, feet + At(0, 0, 64), feet + At(0, 0, 40), feet, default);

    [Fact]
    public void The_line_budget_is_never_exceeded_and_no_pair_waits_past_its_turn()
    {
        const int budget = 32;
        var vision = HordeVision.For(HordeSlots.LastBody, budget);
        var lines = new FakeLines { Clear = (_, _) => false };   // every pair costs both its lines
        var bodies = new BodyEye[HordeSlots.LastBody + 1];
        for (int b = 1; b <= HordeSlots.LastBody; b++)
            bodies[b] = Eye(At(10 * b, 0, 64), yaw: 90f);
        var heroes = new HeroTarget[HordeSlots.Heroes];
        for (int h = 0; h < heroes.Length; h++)
            heroes[h] = Hero(At(100 * h, 500, 0));
        var told = new Told();

        int candidates = 0, maxTraces = 0;
        for (long t = 1; t <= 500; t++)
        {
            int before = lines.Lines;
            vision.Run(t, bodies, heroes, lines, told);
            Assert.True(lines.Lines - before <= budget, $"tick {t}: {lines.Lines - before} lines of {budget}");
            maxTraces = Math.Max(maxTraces, lines.Lines - before);
            candidates = vision.CandidatesThisTick;
        }

        // 49 bodies by 4 heroes, two lines each: every pair is served within ceil(2 * 196 / 32) = 13 ticks.
        Assert.Equal(HordeSlots.LastBody * HordeSlots.Heroes, candidates);
        Assert.Equal(budget, maxTraces);
        Assert.InRange(vision.LongestWait, 1, (2 * candidates + budget - 1) / budget);
    }

    [Fact]
    public void Range_cone_and_the_pvs_cost_no_line()
    {
        var vision = HordeVision.For(4);
        var lines = new FakeLines();
        var told = new Told();
        var bodies = new BodyEye[5];
        bodies[1] = Eye(At(0, 0, 64), range: 100f);           // too far
        bodies[2] = Eye(At(0, 0, 64), yaw: 180f);             // facing away
        bodies[3] = Eye(At(0, 0, 64));                        // PVS refuses hero 0
        var heroes = new HeroTarget[] { Hero(At(600, 0, 0)) };
        lines.Pvs = _ => false;
        vision.Run(1, bodies, heroes, lines, told);
        Assert.Equal(0, lines.Lines);
        bodies[3] = Eye(At(0, 0, 64));
        lines.Pvs = _ => true;
        vision.Run(2, bodies, heroes, lines, told);
        Assert.Equal(1, lines.Lines);
        Assert.Equal(1, vision.CandidatesThisTick);
    }

    /// <summary>Ticks from a hero coming into the open to <c>SightGained</c>, at Alertness <paramref name="alertness"/>.</summary>
    private static long RecognitionTicks(int alertness)
    {
        int recognize = VisionParams.Ticks(VisionCurves.RecognizeAt(alertness), 0.015f);
        var vision = HordeVision.For(1);
        var told = new Told();
        var lines = new FakeLines();
        BodyEye[] bodies = [default, Eye(At(0, 0, 64), recognize: recognize)];
        HeroTarget[] heroes = [Hero(At(300, 0, 0))];
        for (long t = 1; t <= 200 && told.Gains.Count == 0; t++)
        {
            told.Now = t;
            vision.Run(t, bodies, heroes, lines, told);
        }

        return told.Gains.Count == 0 ? long.MaxValue : told.Gains[0].Tick;
    }

    [Fact]
    public void Recognition_takes_less_time_the_more_alert_the_robot()
    {
        long[] ticks = [.. Enumerable.Range(0, 11).Select(RecognitionTicks)];
        for (int l = 1; l < ticks.Length; l++)
            Assert.True(ticks[l] <= ticks[l - 1], $"alertness {l}: {ticks[l]} ticks, after {ticks[l - 1]}");
        Assert.True(ticks[10] < ticks[0], $"alertness 0 and 10 both took {ticks[0]} ticks");
        Assert.True(ticks[0] < long.MaxValue, "the control: alertness 0 recognises at all");
    }

    [Fact]
    public void A_hero_hidden_for_the_lost_time_is_lost_with_its_last_place()
    {
        var vision = HordeVision.For(1);
        var told = new Told();
        var lines = new FakeLines();
        BodyEye[] bodies = [default, Eye(At(0, 0, 64), recognize: 2)];
        HeroTarget[] heroes = [Hero(At(300, 0, 0))];
        for (long t = 1; t <= 10; t++)
            vision.Run(t, bodies, heroes, lines, told);
        Assert.True(vision.Holds(1, 0));

        lines.Clear = (_, _) => false;
        for (long t = 11; t <= 100 && told.Losses.Count == 0; t++)
            vision.Run(t, bodies, heroes, lines, told);
        Assert.Single(told.Losses);
        Assert.False(vision.Holds(1, 0));
    }
}
