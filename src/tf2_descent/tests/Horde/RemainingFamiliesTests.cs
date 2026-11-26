using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The remaining robot families (plan_descent_logic HO12) in the inline arena (<see cref="FamilySim"/>,
/// HO6's gate): each closes, holds and fires as its decider says against an idle and a moving
/// hero, with numbers; and no robot template is left on the bare hunt.
/// </summary>
public sealed class RemainingFamiliesTests
{
    private static readonly AiVector Mid = At(2048, 2048);

    private static List<(FamilySim.Bot Bot, int Tick, ActionIntent Action, LookIntent Look)> Fires(FamilySim sim, FamilySim.Bot bot) =>
        sim.Acts.Where(a => a.Bot == bot && a.Action.Kind == ActionKind.Fire).ToList();

    //-------------------------------------------------------------------------
    // Every template fights
    //-------------------------------------------------------------------------

    [Fact]
    public void No_robot_template_is_left_on_the_bare_hunt()
    {
        // Each template's robot, by its attack's family, in the arena as the game builds its deciders.
        var sim = new FamilySim();
        var templates = TheRules.Content.Robots.Rows.ToList();
        var bots = templates.Select((t, k) => sim.Robot(HordeWeapons.Of(t.Attack), Mid + At(200 * (k % 6), 200 * (k / 6)))).ToList();
        var stray = sim.Robot(HordeWeapons.None, Mid + At(-400, -400));   // the control: a family nothing is registered for
        sim.Step(2 * DecideSchedule.Period);

        var hunting = templates.Where((t, k) => bots[k].Decider(sim)!.Doing.Branch == HuntDecider.NameOf(HuntDecider.Node.Hunt))
                               .Select(t => $"{t.Id} ({t.Attack})").ToList();

        Assert.Empty(hunting);
        Assert.All(bots, b => Assert.EndsWith("Decider", b.Decider(sim)!.Doing.Branch));
        Assert.Equal(HuntDecider.NameOf(HuntDecider.Node.Hunt), stray.Decider(sim)!.Doing.Branch);
        Assert.True(sim.Faulted == 0, sim.LastFault);   // no decider threw on its worker
    }

    //-------------------------------------------------------------------------
    // The scout
    //-------------------------------------------------------------------------

    [Fact]
    public void A_scout_closes_to_its_band_circles_an_idle_hero_and_fires()
    {
        var levels = Levels.Eleven(5);
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var scout = sim.Robot(HordeWeapons.Scattergun, Mid + At(-1200, 0), levels);

        sim.Step(1500);

        float band = FamilyNumbers.ScoutBandUnits;   // aggression 5: a range scale of 1
        var late = scout.Intents.Where(i => i.FromTick.Value > sim.Now - 600).ToList();
        Assert.Contains(scout.Intents, i => i.Reason.NodeId == (short)FamilyNode.Close);
        Assert.All(late, i => Assert.Equal((short)FamilyNode.Circle, i.Reason.NodeId));
        Assert.All(late, i => Assert.Equal(MoveKind.Strafe, i.Move.Kind));
        Assert.InRange(scout.At.DistanceTo(hero.At), band * 0.7f, band * ScoutDecider.BandOuter + 20f);
        Assert.True(Fires(sim, scout).Count >= 20, $"it fired {Fires(sim, scout).Count} times");
    }

    [Fact]
    public void A_scout_keeps_firing_at_a_hero_running_across_it()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var scout = sim.Robot(HordeWeapons.Scattergun, Mid + At(-300, 0), Levels.Eleven(5, ("accuracy", 10)));
        hero.Velocity = At(0, 250);

        for (int t = 0; t < 600; t++)
        {
            sim.MoveHeroes();
            if (hero.At.Y > Mid.Y + 600 || hero.At.Y < Mid.Y - 600)
                hero.Velocity = hero.Velocity * -1f;
            sim.Step();
        }

        var fires = Fires(sim, scout);
        Assert.True(fires.Count >= 10, $"it fired {fires.Count} times at a moving hero");
        Assert.True(scout.At.DistanceTo(hero.At) <= FamilyNumbers.ScoutBandUnits * 1.5f, $"it kept with the hero: {scout.At.DistanceTo(hero.At):0}");
    }

    [Fact]
    public void A_hurt_scout_backs_off_still_shooting()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var scout = sim.Robot(HordeWeapons.Scattergun, Mid + At(-250, 0), Levels.Eleven(5));
        scout.Life = 0.1f * scout.Most;

        sim.Step(DecideSchedule.Period + 1);
        float before = scout.At.DistanceTo(hero.At);
        sim.Step(120);

        Assert.Contains(scout.Intents, i => i.Reason.NodeId == (short)FamilyNode.BackOff);
        Assert.True(scout.At.DistanceTo(hero.At) > before + 150f, $"it ran: {before:0} to {scout.At.DistanceTo(hero.At):0}");
        Assert.NotEmpty(Fires(sim, scout));
    }

    //-------------------------------------------------------------------------
    // The pyro
    //-------------------------------------------------------------------------

    [Fact]
    public void A_pyro_closes_and_burns_an_idle_hero_only_inside_its_flames_reach()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var pyro = sim.Robot(HordeWeapons.Flamethrower, Mid + At(-900, 0));
        float reach = (float)TheRules.Attacks.Of("flamethrower").RangeUnits;
        var gaps = new List<float>();

        for (int t = 0; t < 900; t++)
        {
            int before = sim.Acts.Count;
            float gap = FamilySim.EyeOf(pyro).DistanceTo(hero.At + At(0, 0, FamilyNumbers.ChestUnits));
            sim.Step();
            if (sim.Acts.Count > before && sim.Acts[^1].Bot == pyro && sim.Acts[^1].Action.Kind == ActionKind.Fire)
                gaps.Add(gap);
        }

        Assert.True(gaps.Count >= 20, $"it burned {gaps.Count} times");
        Assert.True(gaps.Max() <= reach + 10f, $"every burn inside the reach {reach}: the farthest at {gaps.Max():0}");
        Assert.Equal(FamilyNode.Burn, pyro.Node(sim));
        Assert.True(pyro.At.DistanceTo(hero.At) <= reach * FamilyNumbers.BurnShare + 20f, $"it pressed in: {pyro.At.DistanceTo(hero.At):0}");
    }

    [Fact]
    public void A_pyro_runs_down_a_retreating_hero_and_keeps_burning()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var pyro = sim.Robot(HordeWeapons.Flamethrower, Mid + At(-250, 0));
        hero.Velocity = At(150, 0);

        for (int t = 0; t < 600; t++)
        {
            sim.MoveHeroes();
            sim.Step();
        }

        int late = Fires(sim, pyro).Count(f => f.Tick > sim.Now - 300);
        Assert.True(late >= 20, $"it burned the running hero {late} times in the last 300 ticks, {pyro.At.DistanceTo(hero.At):0} off, node {pyro.Node(sim)}, aim {pyro.Eye}");
        Assert.True(pyro.At.DistanceTo(hero.At) <= 200f, $"it stayed on the hero: {pyro.At.DistanceTo(hero.At):0}");
    }

    //-------------------------------------------------------------------------
    // The demoman
    //-------------------------------------------------------------------------

    /// <summary>Where a pipe fired from <paramref name="eye"/> towards <paramref name="look"/> at the rules' speed and gravity comes down to height <paramref name="z"/>, from just outside the hull as the hands launch it.</summary>
    private static AiVector Landing(AiVector eye, AiVector look, float z)
    {
        var a = TheRules.Attacks.Of("grenade_launcher");
        float v = (float)a.Speed, g = (float)a.Gravity;
        AiVector dir = (look - eye).Normalised();
        AiVector p0 = eye + dir * 40f, vel = dir * v;
        // z(t) = p0.Z + vel.Z t - g t^2 / 2 = z, the later root (coming down).
        float A = -0.5f * g, B = vel.Z, C = p0.Z - z;
        float disc = B * B - 4 * A * C;
        float t = (-B - MathF.Sqrt(MathF.Max(0f, disc))) / (2 * A);
        return new AiVector(p0.X + vel.X * t, p0.Y + vel.Y * t, z);
    }

    [Theory]
    [InlineData(300f, 0f)]
    [InlineData(450f, 0f)]
    [InlineData(620f, 0f)]
    [InlineData(300f, -160f)]
    [InlineData(450f, -160f)]
    [InlineData(620f, -160f)]
    public void A_demomans_lob_lands_by_a_standing_hero(float distance, float height)
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(distance, 0, height));
        var demo = sim.Robot(HordeWeapons.GrenadeLauncher, Mid, Levels.Eleven(5, ("accuracy", 10), ("agility", 0)));
        demo.Still = true;

        sim.Step(400);

        var fires = Fires(sim, demo);
        Assert.True(fires.Count >= 3, $"it lobbed {fires.Count} times");
        var misses = fires.Select(f => Landing(FamilySim.EyeOf(demo), f.Look.Point, hero.At.Z + FamilyNumbers.FeetUnits))
                          .Select(l => new AiVector(l.X - hero.At.X, l.Y - hero.At.Y, 0f).Length).ToList();
        Assert.True(misses.Max() <= 40f, $"lobs land within 40 units at {distance} off and {height} high: worst {misses.Max():0.0}, mean {misses.Average():0.0}");

        // The control: a shot straight at the feet, no arc, comes down short, farther off than any lob.
        var flat = Landing(FamilySim.EyeOf(demo), hero.At + At(0, 0, FamilyNumbers.FeetUnits), hero.At.Z + FamilyNumbers.FeetUnits);
        float flatMiss = new AiVector(flat.X - hero.At.X, flat.Y - hero.At.Y, 0f).Length;
        Assert.True(flatMiss > 2f * misses.Max() && flatMiss > 10f, $"the control: no arc misses by {flatMiss:0.0}, the lobs by {misses.Max():0.0} at worst");
    }

    [Fact]
    public void A_demomans_lob_leads_a_running_hero()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(500, -300));
        var demo = sim.Robot(HordeWeapons.GrenadeLauncher, Mid, Levels.Eleven(5, ("accuracy", 10), ("agility", 0)));
        demo.Still = true;
        hero.Velocity = At(0, 120);
        var a = TheRules.Attacks.Of("grenade_launcher");
        var misses = new List<float>();
        var unled = new List<float>();

        for (int t = 0; t < 500; t++)
        {
            int before = sim.Acts.Count;
            var at = hero.At;
            sim.MoveHeroes();
            sim.Step();
            if (sim.Acts.Count > before && sim.Acts[^1].Bot == demo && sim.Acts[^1].Action.Kind == ActionKind.Fire)
            {
                var look = sim.Acts[^1].Look.Point;
                var land = Landing(FamilySim.EyeOf(demo), look, at.Z + FamilyNumbers.FeetUnits);
                float x = (land - FamilySim.EyeOf(demo)).Length2D;
                float flight = x / ((float)a.Speed * (look - FamilySim.EyeOf(demo)).Normalised().Length2D);
                var there = hero.At + hero.Velocity * flight;
                misses.Add(new AiVector(land.X - there.X, land.Y - there.Y, 0f).Length);
                unled.Add(new AiVector(at.X - there.X, at.Y - there.Y, 0f).Length);
            }
        }

        Assert.True(misses.Count >= 5, $"{misses.Count} lobs");
        Assert.True(misses.Average() <= 30f, $"mean miss {misses.Average():0.0} (unled {unled.Average():0.0})");
        Assert.True(unled.Average() >= 60f, $"the control: aiming where the hero stood would miss by {unled.Average():0.0}");
    }

    [Fact]
    public void A_demoman_does_not_lob_into_a_lintel_in_its_arc()
    {
        var sim = new FamilySim();
        sim.Hero(Mid + At(500, 0));
        var demo = sim.Robot(HordeWeapons.GrenadeLauncher, Mid, Levels.Eleven(5, ("agility", 0)));
        demo.Still = true;
        sim.LineOfFire = _ => 70f;   // a lintel 70 units up the arc's first line

        sim.Step(400);
        Assert.Empty(Fires(sim, demo));
        Assert.Contains("lintel", demo.Decider(sim)!.Doing.Verdict);

        sim.LineOfFire = _ => HordeView.FireLineReach;
        sim.Step(120);
        Assert.NotEmpty(Fires(sim, demo));
    }

    [Fact]
    public void A_demoman_keeps_its_distance_from_a_hero_that_comes_close()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var demo = sim.Robot(HordeWeapons.GrenadeLauncher, Mid + At(-120, 0));

        sim.Step(120);

        Assert.Contains(demo.Intents, i => i.Reason.NodeId == (short)FamilyNode.BackOff);
        float crowded = HordeTemperament.At(HordeSkillMap.Curves(), "crowdedShare", 5) * HordeWeapons.EngageRange(HordeWeapons.GrenadeLauncher);
        Assert.True(demo.At.DistanceTo(hero.At) >= crowded - 10f, $"it backed out to {demo.At.DistanceTo(hero.At):0} (crowded inside {crowded:0})");
    }

    //-------------------------------------------------------------------------
    // The heavy
    //-------------------------------------------------------------------------

    [Fact]
    public void A_heavy_holds_its_ground_and_pours_fire_on_an_idle_hero()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var heavy = sim.Robot(HordeWeapons.Minigun, Mid + At(-500, 0), Levels.Eleven(5, ("agility", 0)));
        var start = heavy.At;

        sim.Step(600);

        Assert.Equal(start, heavy.At);
        Assert.Equal(FamilyNode.Suppress, heavy.Node(sim));
        Assert.True(Fires(sim, heavy).Count >= 40, $"it fired {Fires(sim, heavy).Count} times");
    }

    /// <summary>The share of a heavy's decides that ask to fire at a hero circling it at <paramref name="radius"/> at 300 units a second.</summary>
    private static float OnTargetCircling(float radius)
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(radius, 0));
        var heavy = sim.Robot(HordeWeapons.Minigun, Mid, Levels.Eleven(5, ("accuracy", 10)));
        heavy.Still = true;
        float omega = 300f / radius;   // radians a second
        int fires = 0, decides = 0;
        for (int t = 0; t < 900; t++)
        {
            float a = omega * t * FamilySim.Tick;
            var at = Mid + At(radius * MathF.Cos(a), radius * MathF.Sin(a));
            hero.Velocity = (at - hero.At) * (1f / FamilySim.Tick);
            hero.At = at;
            int before = heavy.Intents.Count;
            sim.Step();
            if (heavy.Intents.Count > before && t > 150)
            {
                decides++;
                if (heavy.Intents[^1].Action.Kind == ActionKind.Fire)
                    fires++;
            }
        }

        return decides == 0 ? 0f : fires / (float)decides;
    }

    [Fact]
    public void A_heavys_slow_gun_loses_a_hero_circling_it_close_and_tracks_one_far()
    {
        float close = OnTargetCircling(150f), far = OnTargetCircling(600f);

        Assert.True(far >= 0.8f, $"far: on target {far:0.00} of decides");
        Assert.True(close <= 0.3f, $"close: on target {close:0.00} of decides (300 u/s at 150 units is {300f / 150f * 180f / MathF.PI:0} deg/s against the gun's {TheRules.Attacks.Of("minigun").TurnDegreesPerSecond})");
    }

    [Fact]
    public void A_heavys_gun_never_turns_faster_than_the_rules_rate()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(0, 400));
        var heavy = sim.Robot(HordeWeapons.Minigun, Mid, Levels.Eleven(5, ("accuracy", 10)));
        heavy.Still = true;
        heavy.Eye = new AiAngles(0f, 180f, 0f);   // facing away: a long swing to come

        sim.Step(400);

        float step = (float)TheRules.Attacks.Of("minigun").TurnDegreesPerSecond * DecideSchedule.Period * FamilySim.Tick;
        var yaws = heavy.Intents.Select(i => FamilySim.Angles(FamilySim.EyeOf(heavy), i.Look.Point).Yaw).Prepend(180f).ToList();   // from where the gun pointed
        float worst = 0f;
        for (int k = 1; k < yaws.Count; k++)
        {
            float d = MathF.Abs(((yaws[k] - yaws[k - 1]) % 360f + 540f) % 360f - 180f);
            worst = MathF.Max(worst, d);
        }

        Assert.True(worst <= step + 0.5f, $"the widest turn between two decides {worst:0.0} deg, the rate's {step:0.0}");
        Assert.True(yaws.Count > 10 && MathF.Abs(yaws[^1] - 90f) < 15f, $"the control: it came round to the hero ({yaws[^1]:0})");
    }

    //-------------------------------------------------------------------------
    // The sniper
    //-------------------------------------------------------------------------

    [Fact]
    public void A_sniper_holds_far_back_and_shoots_at_the_head_of_an_idle_hero()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(1300, 0));
        var sniper = sim.Robot(HordeWeapons.SniperRifle, Mid, Levels.Eleven(5, ("accuracy", 10)));
        var start = sniper.At;

        sim.Step(300);

        Assert.Equal(start, sniper.At);
        Assert.Equal(FamilyNode.Aim, sniper.Node(sim));
        var fires = Fires(sim, sniper);
        Assert.NotEmpty(fires);
        Assert.All(fires, f => Assert.True(MathF.Abs(f.Look.Point.Z - (hero.At.Z + FamilyNumbers.HeadUnits)) < 8f, $"aimed at {f.Look.Point.Z:0}"));
    }

    /// <summary>
    /// Found live (2026-11-22, the giant sniper's first boot): a x1.75 sniper's look turns from its
    /// own eye, 126 units up, but it judged its aim from a scale-1 eye (72 up), 2.4 degrees off at
    /// 1300 units against a 1.5-degree tolerance, so it held "settling" and never fired.
    /// </summary>
    [Fact]
    public void A_giant_sniper_judges_its_aim_from_its_own_eye_and_shoots_as_often_as_an_ordinary_one()
    {
        int ordinary = SniperFires(1f), giant = SniperFires(1.75f);

        Assert.True(ordinary >= 10, $"the control: an ordinary sniper fired {ordinary} times");
        Assert.True(Math.Abs(giant - ordinary) <= 1, $"a x1.75 sniper fired {giant} times, an ordinary one {ordinary}");
    }

    /// <summary>The shots a sniper of <paramref name="scale"/> fires in 300 ticks at an idle hero 1300 units off.</summary>
    private static int SniperFires(float scale)
    {
        var sim = new FamilySim();
        sim.Hero(Mid + At(1300, 0));
        var sniper = sim.Robot(HordeWeapons.SniperRifle, Mid, Levels.Eleven(5, ("accuracy", 10)));
        sniper.Scale = scale;

        sim.Step(300);

        Assert.Equal(FamilyNode.Aim, sniper.Node(sim));
        return Fires(sim, sniper).Count;
    }

    [Fact]
    public void A_rushed_sniper_relocates_and_holds_fire()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(250, 0));
        var sniper = sim.Robot(HordeWeapons.SniperRifle, Mid);

        float rush = HordeWeapons.EngageRange(HordeWeapons.SniperRifle) * HordeTemperament.At(HordeSkillMap.Curves(), "crowdedShare", 5);
        var firedAt = new List<float>();
        sim.Step(DecideSchedule.Period + 1);
        float before = sniper.At.DistanceTo(hero.At);
        for (int t = 0; t < 240; t++)
        {
            float d = sniper.At.DistanceTo(hero.At);
            int n = Fires(sim, sniper).Count;
            sim.Step();
            if (Fires(sim, sniper).Count > n)
                firedAt.Add(d);
        }

        Assert.Contains(sniper.Intents, i => i.Reason.NodeId == (short)FamilyNode.Relocate);
        Assert.True(sniper.At.DistanceTo(hero.At) >= rush, $"it ran out of the rush: {before:0} to {sniper.At.DistanceTo(hero.At):0} (rushed inside {rush:0})");
        Assert.All(firedAt, d => Assert.True(d >= rush - 30f, $"a shot from {d:0}, inside the rush {rush:0}"));
    }

    /// <summary>A line of fire as the game traces it at a hero: it stops on his hull, short of the point aimed at.</summary>
    private static Func<FamilySim.Bot, float> StopsOnHull(FamilySim.HeroIn hero) =>
        b => FamilySim.EyeOf(b).DistanceTo(hero.At + At(0, 0, FamilyNumbers.ChestUnits)) - 24f;

    // Found live (2026-11-11): a sniper rushed in a small room ran into a wall and never fired.
    [Fact]
    public void A_cornered_sniper_stands_and_shoots_after_its_spell_of_running()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(250, 0));
        var sniper = sim.Robot(HordeWeapons.SniperRifle, Mid, Levels.Eleven(5, ("accuracy", 10)));
        sniper.Still = true;   // its back to the wall
        sim.LineOfFire = StopsOnHull(hero);
        int spell = (int)MathF.Ceiling(FamilyNumbers.SniperRelocateSeconds / FamilySim.Tick);

        sim.Step(spell - DecideSchedule.Period);
        Assert.Empty(Fires(sim, sniper));
        Assert.Equal(FamilyNode.Relocate, sniper.Node(sim));
        sim.Step(spell);

        Assert.NotEmpty(Fires(sim, sniper));
        Assert.Contains("cornered", sniper.Decider(sim)!.Doing.Verdict);
    }

    // Found live (2026-11-11): a sniper's line of fire stops on the hero's hull, short of his head.
    [Fact]
    public void A_sniper_shoots_when_its_line_stops_on_the_heros_hull()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(1300, 0));
        var sniper = sim.Robot(HordeWeapons.SniperRifle, Mid, Levels.Eleven(5, ("accuracy", 10)));
        sim.LineOfFire = StopsOnHull(hero);

        sim.Step(300);

        Assert.NotEmpty(Fires(sim, sniper));
    }

    // Found live (2026-11-11): a buster beside its hero read its line, stopped on his hull, as blocked, and never blew.
    [Fact]
    public void A_buster_blows_beside_a_hero_whose_hull_stops_its_line()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var buster = sim.Robot(HordeWeapons.SentryBuster, Mid + At(-600, 0));
        sim.LineOfFire = StopsOnHull(hero);

        for (int t = 0; t < 1500 && Fires(sim, buster).Count == 0; t++)
            sim.Step();

        Assert.NotEmpty(Fires(sim, buster));
    }

    //-------------------------------------------------------------------------
    // The spy
    //-------------------------------------------------------------------------

    [Fact]
    public void A_cunning_spy_goes_round_a_hero_facing_it_and_stabs_its_back()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        hero.Yaw = 180f;   // facing the spy, which comes from -x
        var spy = sim.Robot(HordeWeapons.Knife, Mid + At(-900, 60), Levels.Eleven(5, ("cunning", 10)));
        float cone = (float)TheRules.Attacks.Of("knife").BackstabConeDegrees;

        for (int t = 0; t < 1500 && Fires(sim, spy).Count < 3; t++)
            sim.Step();

        var stabs = Fires(sim, spy);
        Assert.True(stabs.Count >= 3, $"{stabs.Count} stabs");
        var facts = new ActorFacts(hero.Id, hero.At, default, HordeView.Hero(hero.Yaw), ActorActivity.None, 1f, Team: HordeBrains.HeroTeam);
        Assert.True(SpyDecider.Behind(facts, spy.At, cone), $"it stabs from behind: at {spy.At - hero.At}");
        Assert.Contains(spy.Intents, i => i.Reason.NodeId == (short)FamilyNode.Flank);
    }

    [Fact]
    public void A_spy_with_no_cunning_goes_straight_in()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        hero.Yaw = 180f;
        var spy = sim.Robot(HordeWeapons.Knife, Mid + At(-900, 60), Levels.Eleven(5, ("cunning", 0)));

        for (int t = 0; t < 1500 && Fires(sim, spy).Count < 3; t++)
            sim.Step();

        Assert.True(Fires(sim, spy).Count >= 3);
        Assert.DoesNotContain(spy.Intents, i => i.Reason.NodeId == (short)FamilyNode.Flank);
        Assert.True(spy.At.X < hero.At.X, "it stabbed from the front");
    }

    [Fact]
    public void A_spy_stalks_cloaked_and_uncloaks_before_its_first_stab()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var spy = sim.Robot(HordeWeapons.Knife, Mid + At(-900, 0), Levels.Eleven(5, ("cunning", 10)));

        for (int t = 0; t < 1500 && Fires(sim, spy).Count == 0; t++)
            sim.Step();

        var acts = sim.Acts.Where(a => a.Bot == spy).ToList();
        int firstStab = acts.FindIndex(a => a.Action.Kind == ActionKind.Fire);
        int lastUncloak = acts.FindLastIndex(firstStab, a => a.Action.Kind == ActionKind.Ability && a.Action.WeaponSlot == HordeAbilities.Decloak);
        Assert.True(firstStab > 0, "it stabbed");
        Assert.True(lastUncloak >= 0, "it uncloaked before its first stab");
        Assert.Contains(acts.Take(firstStab), a => a.Action.Kind == ActionKind.Ability && a.Action.WeaponSlot == HordeAbilities.Cloak);
    }

    //-------------------------------------------------------------------------
    // The sentry buster
    //-------------------------------------------------------------------------

    [Fact]
    public void A_buster_runs_at_an_idle_hero_and_asks_its_blast_inside_the_trigger_then_holds()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var buster = sim.Robot(HordeWeapons.SentryBuster, Mid + At(-1000, 0));
        float trigger = (float)TheRules.Attacks.Of("sentry_buster").TriggerUnits;

        for (int t = 0; t < 1500 && Fires(sim, buster).Count == 0; t++)
            sim.Step();
        var first = Assert.Single(Fires(sim, buster).Take(1));
        float at = buster.At.DistanceTo(hero.At);
        var stood = buster.At;
        sim.Step(200);

        Assert.True(at <= trigger + 5f, $"it asked its blast {at:0} units off (trigger {trigger})");
        Assert.Contains(buster.Intents, i => i.Reason.NodeId == (short)FamilyNode.Run);
        Assert.Equal(stood, buster.At);
        Assert.Equal(FamilyNode.Detonate, buster.Node(sim));
    }

    [Fact]
    public void A_buster_runs_down_a_hero_walking_away()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var buster = sim.Robot(HordeWeapons.SentryBuster, Mid + At(-800, 0));
        hero.Velocity = At(120, 0);

        for (int t = 0; t < 2000 && Fires(sim, buster).Count == 0; t++)
        {
            sim.MoveHeroes();
            sim.Step();
        }

        Assert.NotEmpty(Fires(sim, buster));
        Assert.True(buster.At.DistanceTo(hero.At) <= (float)TheRules.Attacks.Of("sentry_buster").TriggerUnits + 40f);
    }

    //-------------------------------------------------------------------------
    // The shotgunner
    //-------------------------------------------------------------------------

    [Fact]
    public void A_shotgunner_closes_to_its_band_and_fires()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var gunner = sim.Robot(HordeWeapons.Shotgun, Mid + At(-1200, 0), Levels.Eleven(5, ("agility", 0)));

        sim.Step(1200);

        Assert.InRange(gunner.At.DistanceTo(hero.At), FamilyNumbers.GunnerBandUnits * 0.4f, FamilyNumbers.GunnerBandUnits * GunnerDecider.BandOuter + 10f);
        Assert.Equal(FamilyNode.Band, gunner.Node(sim));
        Assert.True(Fires(sim, gunner).Count >= 10, $"it fired {Fires(sim, gunner).Count} times");
    }
}
