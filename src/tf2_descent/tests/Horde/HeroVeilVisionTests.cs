using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Senses;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde's sight per hero (this lane: plan_descent_logic 3.6, 4.7; plan_tf2_rpg B.3): a hero's
/// sight scale multiplies every robot's range for him (the lamp and the dark), and a veiled hero
/// (his respawn's grace) is looked at but never recognised; after the grace, a robot that watched
/// him through it stays blind until it has lost him from view once.
/// </summary>
public sealed class HeroVeilVisionTests
{
    private sealed class Told : IHordeSightSink
    {
        public List<(int Body, int Hero, long Tick)> Gains { get; } = [];
        public List<(int Body, int Hero, long Tick)> Losses { get; } = [];
        public long Now { get; set; }

        public void Gained(int body, int hero, AiVector at, AiVector velocity, float fraction) => Gains.Add((body, hero, Now));

        public void Lost(int body, int hero, AiVector at, AiVector velocity) => Losses.Add((body, hero, Now));
    }

    private const int Recognise = 5;

    /// <summary>One robot at the origin's eye height facing +X, ranging 1000, recognising in 5 ticks.</summary>
    private static BodyEye[] OneRobot(float yaw = 0f) =>
        [default, new BodyEye(true, At(0, 0, 64), yaw, 1000f, MathF.Cos(MathF.PI / 4f), Recognise, 34)];

    private static HeroTarget[] OneHero(float x, float scale = 1f, HeroVeil veil = HeroVeil.None) =>
        [new HeroTarget(true, At(x, 0, 64), At(x, 0, 40), At(x, 0, 0), default, scale, veil)];

    private sealed class Run(HordeVision vision, Told told, FakeLines lines)
    {
        public long Now { get; private set; }

        public void Ticks(int n, BodyEye[] bodies, HeroTarget[] heroes)
        {
            for (int i = 0; i < n; i++)
            {
                told.Now = ++Now;
                vision.Run(Now, bodies, heroes, lines, told);
            }
        }
    }

    private static (HordeVision Vision, Told Told, FakeLines Lines, Run Run) Make()
    {
        var vision = HordeVision.For(1);
        var told = new Told();
        var lines = new FakeLines();
        return (vision, told, lines, new Run(vision, told, lines));
    }

    [Fact]
    public void A_hero_inside_the_robots_range_is_recognised_at_a_scale_of_one()
    {
        var (_, told, _, run) = Make();

        run.Ticks(20, OneRobot(), OneHero(800f));

        Assert.Single(told.Gains);
    }

    [Fact]
    public void A_sight_scale_below_one_shortens_the_range_for_that_hero()
    {
        var (_, told, lines, run) = Make();

        run.Ticks(20, OneRobot(), OneHero(800f, scale: 0.5f));

        Assert.Empty(told.Gains);
        Assert.Equal(0, lines.Lines);   // out of the scaled range: no line traced
    }

    [Fact]
    public void A_sight_scale_above_one_lengthens_it()
    {
        var (_, told, _, run) = Make();

        run.Ticks(20, OneRobot(), OneHero(1400f, scale: 1.5f));

        Assert.Single(told.Gains);
    }

    [Fact]
    public void A_hidden_hero_in_full_view_is_never_recognised()
    {
        var (vision, told, lines, run) = Make();

        run.Ticks(200, OneRobot(), OneHero(300f, veil: HeroVeil.Hidden));

        Assert.Empty(told.Gains);
        Assert.False(vision.Holds(1, 0));
        Assert.True(lines.Lines > 0, "the stimulus: the pair passed the cheap stages and was looked at");
        Assert.True(vision.Watching(1, 0));
    }

    [Fact]
    public void A_sight_held_is_lost_the_tick_the_hero_is_hidden()
    {
        var (vision, told, _, run) = Make();
        run.Ticks(20, OneRobot(), OneHero(300f));
        Assert.True(vision.Holds(1, 0));

        run.Ticks(1, OneRobot(), OneHero(300f, veil: HeroVeil.Hidden));

        Assert.False(vision.Holds(1, 0));
        Assert.Equal(run.Now, Assert.Single(told.Losses).Tick);
    }

    [Fact]
    public void Once_unveiled_a_hero_is_recognised_by_the_ordinary_rules_from_the_next_tick()
    {
        var (_, told, _, run) = Make();
        run.Ticks(50, OneRobot(), OneHero(300f, veil: HeroVeil.Hidden));
        long lifted = run.Now + 1;

        run.Ticks(20, OneRobot(), OneHero(300f));

        // First clear the tick the veil lifts; recognised Recognise ticks after: the grace gave it no head start.
        Assert.Equal(lifted + Recognise, Assert.Single(told.Gains).Tick);
    }

    [Fact]
    public void A_robot_that_watched_him_through_the_grace_stays_blind_while_he_stays_in_view()
    {
        var (_, told, _, run) = Make();
        run.Ticks(50, OneRobot(), OneHero(300f, veil: HeroVeil.Hidden));

        run.Ticks(300, OneRobot(), OneHero(300f, veil: HeroVeil.Watched));

        Assert.Empty(told.Gains);
    }

    [Fact]
    public void A_watcher_that_loses_him_from_view_once_sees_him_again_by_the_ordinary_rules()
    {
        var (_, told, _, run) = Make();
        run.Ticks(50, OneRobot(), OneHero(300f, veil: HeroVeil.Hidden));
        run.Ticks(10, OneRobot(), OneHero(300f, veil: HeroVeil.Watched));
        Assert.Empty(told.Gains);

        run.Ticks(1, OneRobot(yaw: 180f), OneHero(300f, veil: HeroVeil.Watched));   // it turns away: out of its cone
        long back = run.Now + 1;
        run.Ticks(20, OneRobot(), OneHero(300f, veil: HeroVeil.Watched));

        Assert.Equal(back + Recognise, Assert.Single(told.Gains).Tick);
    }

    [Fact]
    public void A_robot_that_did_not_watch_him_through_the_grace_sees_him_by_the_ordinary_rules()
    {
        var (_, told, _, run) = Make();
        run.Ticks(50, OneRobot(yaw: 180f), OneHero(300f, veil: HeroVeil.Hidden));   // its back to him through the grace
        long turned = run.Now + 1;

        run.Ticks(20, OneRobot(), OneHero(300f, veil: HeroVeil.Watched));

        Assert.Equal(turned + Recognise, Assert.Single(told.Gains).Tick);
    }

    [Fact]
    public void An_unveiled_hero_clears_the_watch_so_a_later_grace_starts_fresh()
    {
        var (vision, _, _, run) = Make();
        run.Ticks(50, OneRobot(), OneHero(300f, veil: HeroVeil.Hidden));
        Assert.True(vision.Watching(1, 0));

        run.Ticks(1, OneRobot(), OneHero(300f));

        Assert.False(vision.Watching(1, 0));
    }

    [Fact]
    public void Forgetting_a_hero_drops_his_pairs_and_tells_no_robot()
    {
        var (vision, told, _, run) = Make();
        run.Ticks(20, OneRobot(), OneHero(300f));
        Assert.True(vision.Holds(1, 0));

        vision.ForgetHero(0);
        run.Ticks(5, OneRobot(), [default]);   // he is gone: absent

        Assert.False(vision.Holds(1, 0));
        Assert.Empty(told.Losses);
    }

    [Fact]
    public void A_hero_merely_absent_is_lost_by_the_robot_that_held_him()
    {
        var (_, told, _, run) = Make();
        run.Ticks(20, OneRobot(), OneHero(300f));

        run.Ticks(1, OneRobot(), [default]);

        Assert.Single(told.Losses);   // the control for the fact above: absence alone tells a loss
    }

    [Fact]
    public void The_probe_names_each_stage_and_the_scaled_range()
    {
        var (vision, _, lines, run) = Make();
        run.Ticks(1, OneRobot(), OneHero(600f, scale: 0.5f));
        lines.Clear = (_, to) => to.Z < 50f;   // the eye line blocked, the chest's clear

        var probe = vision.Probe(1, 0, OneRobot()[1], OneHero(600f, scale: 0.5f)[0], lines);

        Assert.Equal(500f, probe.Range);
        Assert.False(probe.InRange);
        Assert.True(probe.InCone);
        Assert.True(probe.InPvs);
        Assert.False(probe.EyeClear);
        Assert.True(probe.ChestClear);
        Assert.Equal(600f, probe.Distance, 3);
    }
}
