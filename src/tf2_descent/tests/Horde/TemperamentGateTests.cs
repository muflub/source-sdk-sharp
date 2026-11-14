using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// plan_tf2_rpg B.11's second gate, "each skill does what it says" (plan_descent_logic HO12): in
/// the inline arena with everything else held equal, each skill's observable moves one way with
/// its level, measured at levels 0, 5 and 10. Cover is not here: nothing in the horde reads it,
/// because the cell-to-cell visibility table it would weigh (B.3) does not exist (the hand-back
/// says so). Agility's "projectiles dodged" is measured as the share of time a robot spends
/// strafing (its dodge), because a horde brain's view carries no projectiles.
/// </summary>
public sealed class TemperamentGateTests
{
    private static readonly AiVector Mid = At(2048, 2048);

    private static void Monotonic(string skill, float low, float mid, float high, bool rising)
    {
        // Each step by more than a twentieth of the largest: a skill that moves the observable by a hair moves nothing.
        float margin = 0.05f * MathF.Max(MathF.Abs(low), MathF.Max(MathF.Abs(mid), MathF.Abs(high)));
        bool ok = rising ? mid - low > margin && high - mid > margin : low - mid > margin && mid - high > margin;
        Assert.True(ok, $"{skill}: {low:0.###} at 0, {mid:0.###} at 5, {high:0.###} at 10, not {(rising ? "rising" : "falling")}");
    }

    //-------------------------------------------------------------------------
    // Accuracy: aim error falls
    //-------------------------------------------------------------------------

    /// <summary>The mean angle between a scout's aim and an idle hero's chest, degrees.</summary>
    private static float AimError(int accuracy)
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var scout = sim.Robot(HordeWeapons.Scattergun, Mid + At(-300, 0), Levels.Eleven(5, ("accuracy", accuracy)));
        scout.Still = true;
        sim.Step(1200);
        var eye = FamilySim.EyeOf(scout);
        var chest = hero.At + At(0, 0, FamilyNumbers.ChestUnits);
        return scout.Intents.Where(i => i.Look.Kind == LookKind.Point)
                     .Average(i => HordeView.AimOff(FamilySim.Angles(eye, i.Look.Point), eye, chest));
    }

    [Fact]
    public void More_accuracy_aims_closer()
    {
        Monotonic("accuracy", AimError(0), AimError(5), AimError(10), rising: false);
    }

    //-------------------------------------------------------------------------
    // Aggression: engagement distance falls
    //-------------------------------------------------------------------------

    /// <summary>A demoman's mean distance from an idle hero over its last 600 ticks, starting far.</summary>
    private static float Engagement(int aggression)
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var demo = sim.Robot(HordeWeapons.GrenadeLauncher, Mid + At(-1500, 0), Levels.Eleven(5, ("aggression", aggression), ("agility", 0)));
        var d = new List<float>();
        for (int t = 0; t < 1500; t++)
        {
            sim.Step();
            if (t >= 900)
                d.Add(demo.At.DistanceTo(hero.At));
        }

        return d.Average();
    }

    [Fact]
    public void More_aggression_fights_closer()
    {
        Monotonic("aggression", Engagement(0), Engagement(5), Engagement(10), rising: false);
    }

    //-------------------------------------------------------------------------
    // Soloness: distance from the pack's centre rises
    //-------------------------------------------------------------------------

    /// <summary>A scout's mean distance from its two still pack-mates' centre while it fights a hero 900 units off them.</summary>
    private static float FromPack(int soloness)
    {
        var sim = new FamilySim();
        sim.Hero(Mid + At(900, 0));
        var scout = sim.Robot(HordeWeapons.Scattergun, Mid, Levels.Eleven(5, ("teamwork", 10 - soloness)));
        var a = sim.Robot(HordeWeapons.Melee, Mid + At(0, 80));
        var b = sim.Robot(HordeWeapons.Melee, Mid + At(0, -80));
        a.Still = b.Still = true;
        var centre = (a.At + b.At) * 0.5f;
        var d = new List<float>();
        for (int t = 0; t < 1500; t++)
        {
            sim.Step();
            if (t >= 600)
                d.Add(scout.At.DistanceTo(centre));
        }

        return d.Average();
    }

    [Fact]
    public void More_soloness_roams_farther_from_the_pack()
    {
        Monotonic("soloness", FromPack(0), FromPack(5), FromPack(10), rising: true);
    }

    //-------------------------------------------------------------------------
    // Alertness: detection delay falls (the server's senses)
    //-------------------------------------------------------------------------

    /// <summary>The ticks from a hero stepping into view 400 units in front of a robot to its sight of him, by alertness.</summary>
    private static int DetectionDelay(int alertness)
    {
        var rig = new BrainRig();
        // Find a roster roll of this alertness: the rules roll it from the seed.
        int index = TheRules.Temperaments.Index("alertness");
        ulong seed = 1;
        Ai.Roster.RobotId id = default;
        for (; seed < 5000; seed++)
        {
            id = rig.Robot(Centre(1), "soldier", seed);
            if (rig.Roster.Temperament(id.Index).Levels[index] == alertness)
                break;
        }

        Assert.True(seed < 5000, $"no soldier roll of alertness {alertness} in 5000 seeds");
        rig.Slide = false;
        int body = rig.Lend(id, yaw: 0f);
        rig.Tick(5);
        rig.Hero(1, Centre(1) + At(200, 0));
        long gains = rig.Brains.Vision.Gains;
        return rig.TickUntil(() => rig.Brains.Vision.Gains > gains, 2000);
    }

    [Fact]
    public void More_alertness_sees_a_hero_sooner()
    {
        int low = DetectionDelay(2), mid = DetectionDelay(5), high = DetectionDelay(8);

        Assert.True(low > mid && mid > high, $"alertness: {low} ticks at 2, {mid} at 5, {high} at 8");
    }

    //-------------------------------------------------------------------------
    // Tenacity: search time rises
    //-------------------------------------------------------------------------

    /// <summary>The ticks a gunner spends searching where it lost a hero it saw.</summary>
    private static int SearchTicks(int tenacity)
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(300, 0));
        var gunner = sim.Robot(HordeWeapons.Shotgun, Mid, Levels.Eleven(5, ("tenacity", tenacity), ("agility", 0)));
        sim.Push(AiEvent.SightGained(gunner.Id, new Tick(sim.Now + 1), hero.Id, hero.At, default, 1f));
        sim.Step(60);
        hero.Seen = false;
        sim.Push(AiEvent.SightLost(gunner.Id, new Tick(sim.Now + 1), hero.Id, hero.At, default));
        int searching = 0;
        for (int t = 0; t < 5000; t++)
        {
            sim.Step();
            if (gunner.Node(sim) == FamilyNode.Search)
                searching++;
        }

        return searching;
    }

    [Fact]
    public void More_tenacity_searches_longer()
    {
        Monotonic("tenacity", SearchTicks(0), SearchTicks(5), SearchTicks(10), rising: true);
    }

    //-------------------------------------------------------------------------
    // Discipline: shots beyond effective range fall
    //-------------------------------------------------------------------------

    /// <summary>Shots a scout standing beyond its band's outer edge fires, over five distances past it.</summary>
    private static float ShotsBeyondRange(int discipline)
    {
        float edge = FamilyNumbers.ScoutBandUnits * ScoutDecider.BandOuter;
        int shots = 0;
        foreach (float past in new[] { 1.1f, 1.3f, 1.5f, 1.7f, 1.9f })
        {
            var sim = new FamilySim();
            sim.Hero(Mid);
            var scout = sim.Robot(HordeWeapons.Scattergun, Mid + At(-edge * past, 0), Levels.Eleven(5, ("discipline", discipline)));
            scout.Still = true;
            sim.Step(300);
            shots += sim.Acts.Count(a => a.Action.Kind == ActionKind.Fire);
        }

        return shots;
    }

    [Fact]
    public void More_discipline_fires_less_beyond_effective_range()
    {
        Monotonic("discipline", ShotsBeyondRange(0), ShotsBeyondRange(5), ShotsBeyondRange(10), rising: false);
    }

    //-------------------------------------------------------------------------
    // Agility: time strafing (the dodge) rises
    //-------------------------------------------------------------------------

    /// <summary>The share of a shotgunner's decides in its band that strafe.</summary>
    private static float StrafeShare(int agility)
    {
        var sim = new FamilySim();
        sim.Hero(Mid);
        var gunner = sim.Robot(HordeWeapons.Shotgun, Mid + At(-350, 0), Levels.Eleven(5, ("agility", agility)));
        sim.Step(3000);
        var band = gunner.Intents.Where(i => i.Reason.NodeId is (short)FamilyNode.Band or (short)FamilyNode.Strafe).ToList();
        return band.Count(i => i.Reason.NodeId == (short)FamilyNode.Strafe) / (float)Math.Max(1, band.Count);
    }

    [Fact]
    public void More_agility_strafes_more()
    {
        Monotonic("agility", StrafeShare(0), StrafeShare(5), StrafeShare(10), rising: true);
    }

    //-------------------------------------------------------------------------
    // Cunning: approaches by the long way round rise
    //-------------------------------------------------------------------------

    /// <summary>The share of 40 spies' approaches (each its own seed) that go round to the hero's back.</summary>
    private static float LongWayShare(int cunning)
    {
        int flanks = 0, approaches = 0;
        for (ulong seed = 0; seed < 40; seed++)
        {
            var sim = new FamilySim();
            var hero = sim.Hero(Mid);
            hero.Yaw = 180f;
            var spy = sim.Robot(HordeWeapons.Knife, Mid + At(-900, 0), Levels.Eleven(5, ("cunning", cunning)), seed: 1000 + seed * 7919);
            spy.Still = true;
            sim.Step(2 * DecideSchedule.Period);
            var decider = sim.Bots[0].Decider(sim)!;
            approaches++;
            if (decider.Doing.Target.IsValid && Flanked(sim, spy))
                flanks++;
        }

        return flanks / (float)approaches;
    }

    private static bool Flanked(FamilySim sim, FamilySim.Bot spy) =>
        // A stalk towards the back: its goal lies behind the hero (who faces the spy).
        spy.Decider(sim)!.Doing.Goal.X > Mid.X + 100f;

    [Fact]
    public void More_cunning_takes_the_long_way_round_more()
    {
        Monotonic("cunning", LongWayShare(0), LongWayShare(5), LongWayShare(10), rising: true);
    }

    //-------------------------------------------------------------------------
    // Nerve: retreats fall
    //-------------------------------------------------------------------------

    /// <summary>How many of nine hurt scouts (life 5 % to 85 %) back off a hero beside them.</summary>
    private static float Retreats(int nerve)
    {
        int n = 0;
        for (int k = 0; k < 9; k++)
        {
            var sim = new FamilySim();
            sim.Hero(Mid);
            var scout = sim.Robot(HordeWeapons.Scattergun, Mid + At(-250, 0), Levels.Eleven(5, ("nerve", nerve)));
            scout.Still = true;
            scout.Life = scout.Most * (0.05f + 0.1f * k);
            sim.Step(3 * DecideSchedule.Period);
            if (scout.Last.Reason.NodeId == (short)FamilyNode.BackOff)
                n++;
        }

        return n;
    }

    [Fact]
    public void More_nerve_retreats_less()
    {
        Monotonic("nerve", Retreats(0), Retreats(5), Retreats(10), rising: false);
    }
}
