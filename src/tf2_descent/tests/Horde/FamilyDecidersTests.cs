using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The first three robot families (plan_descent_logic HO6) in the inline arena
/// (<see cref="FamilySim"/>): a rusher closes at a run and swings, never standing off at range,
/// and a pack's rushers come in from spread points; a soldier keeps its band, backs off a hero in
/// its face, leads a strafing hero within a bound, and holds fire into a wall or a pack-mate; a
/// medic beams its most hurt pack-mate, goes to one out of reach, flees a hero and fires only
/// alone. And each family parameter of its own moves its behaviour one way with its skill.
/// </summary>
public sealed class FamilyDecidersTests
{
    private static readonly AiVector Mid = At(2048, 2048);

    private static float Band(byte[] levels) =>
        HordeWeapons.EngageRange(HordeWeapons.RocketLauncher) * HordeSkillMap.Curves().For(Levels.Profile(levels)).PreferredRangeScale;

    //-------------------------------------------------------------------------
    // The melee rusher
    //-------------------------------------------------------------------------

    [Fact]
    public void A_rusher_closes_at_a_run_and_swings_at_an_idle_hero()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var rusher = sim.Robot(HordeWeapons.Melee, Mid + At(-700, 0));

        for (int t = 0; t < 1500 && !sim.Acts.Any(a => a.Action.Kind == ActionKind.Fire); t++)
            sim.Step();

        var swing = Assert.Single(sim.Acts.Where(a => a.Action.Kind == ActionKind.Fire).Take(1));
        Assert.True(rusher.At.DistanceTo(hero.At) <= HordeWeapons.EngageRange(HordeWeapons.Melee) + FamilyNumbers.SwingSlackUnits + 5f,
                    $"it swings in reach: {rusher.At.DistanceTo(hero.At):0} units off");
        Assert.All(rusher.Intents.Where(i => i.Move.Kind != MoveKind.Hold), i => Assert.Equal((MoveKind.Follow, MoveSpeed.Run), (i.Move.Kind, i.Move.Speed)));
        Assert.Contains(rusher.Intents, i => i.Reason.NodeId == (short)FamilyNode.Rush);
        Assert.Equal(FamilyNode.Swing, rusher.Node(sim));
    }

    [Fact]
    public void A_rusher_never_stands_off_at_range()
    {
        var sim = new FamilySim();
        sim.Hero(Mid);
        var rusher = sim.Robot(HordeWeapons.Melee, Mid + At(-400, 0), Levels.Eleven(5, ("aggression", 0)));

        sim.Step(120);

        var standing = rusher.Intents.Where(i => i.Move.Kind == MoveKind.Hold).ToList();
        Assert.Empty(standing);
        Assert.True(rusher.Intents.Count >= 10, "the control: it decided");
    }

    [Fact]
    public void A_packs_rushers_come_in_from_spread_points()
    {
        var sim = new FamilySim();
        sim.Hero(Mid);
        var rushers = Enumerable.Range(0, 3).Select(k => sim.Robot(HordeWeapons.Melee, Mid + At(-900, 10 * k))).ToList();
        foreach (var r in rushers)
            r.Still = true;

        sim.Step(2 * DecideSchedule.Period);

        var goals = rushers.Select(r => r.Decider(sim)!.Doing.Goal).ToList();
        for (int a = 0; a < goals.Count; a++)
            for (int b = a + 1; b < goals.Count; b++)
                Assert.True(goals[a].DistanceTo(goals[b]) >= 80f, $"rushers {a} and {b} aim for points {goals[a].DistanceTo(goals[b]):0} units apart");
        Assert.All(goals, g => Assert.Equal(FamilyNumbers.RushRingUnits, g.DistanceTo(Mid), 1));
    }

    [Fact]
    public void A_rusher_with_no_hero_in_sight_hunts_as_every_robot_does()
    {
        var sim = new FamilySim();
        var rusher = sim.Robot(HordeWeapons.Melee, Mid);

        sim.Step(30);

        Assert.Equal(FamilyNode.Hold, rusher.Node(sim));
        Assert.Empty(sim.Acts);
    }

    //-------------------------------------------------------------------------
    // The rocket soldier
    //-------------------------------------------------------------------------

    [Fact]
    public void A_soldier_closes_to_its_band_and_keeps_it()
    {
        var levels = Levels.Eleven(5, ("agility", 0));
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(-1700, 0), levels);
        var curves = HordeSkillMap.Curves();
        float crowded = HordeTemperament.At(curves, "crowdedShare", 5);

        sim.Step(1500);

        float d = soldier.At.DistanceTo(hero.At), band = Band(levels);
        Assert.InRange(d, band * crowded, band * 1.15f + 10f);
        Assert.Contains(soldier.Intents, i => i.Reason.NodeId == (short)FamilyNode.Close);
        Assert.Equal(FamilyNode.Band, soldier.Node(sim));
    }

    [Fact]
    public void A_soldier_backs_off_a_hero_in_its_face()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(-120, 0));

        sim.Step(DecideSchedule.Period + 1);
        float before = soldier.At.DistanceTo(hero.At);
        var first = soldier.Intents[0];
        sim.Step(60);

        Assert.Equal((MoveKind.Retreat, (short)FamilyNode.BackOff), (first.Move.Kind, first.Reason.NodeId));
        Assert.True(first.Move.Direction.X < -0.99f, $"straight away from the hero: {first.Move.Direction}");
        float crowded = HordeTemperament.At(HordeSkillMap.Curves(), "crowdedShare", 5) * Band(Levels.Eleven());
        Assert.True(soldier.At.DistanceTo(hero.At) >= crowded, $"it got out of the hero's face: {before:0} to {soldier.At.DistanceTo(hero.At):0} (crowded inside {crowded:0})");
    }

    /// <summary>A hero strafing across the soldier's line at 300 units a second, turning every 1.5 s.</summary>
    private static AiVector Strafer(int tick) =>
        Mid + At(0, 300f * Triangle(tick * FamilySim.Tick, 1.5f));

    private static float Triangle(float t, float half)
    {
        float p = t % (2 * half);
        return p < half ? p - half / 2 : 1.5f * half - p;
    }

    [Fact]
    public void A_soldiers_rockets_lead_a_strafing_hero_within_a_bound()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Strafer(sim.Now));
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(-640, 0), Levels.Eleven(5, ("accuracy", 10), ("agility", 0)));
        soldier.Still = true;
        float speed = (float)TheRules.Attacks.Of("rocket_launcher").Speed;

        for (int t = 0; t < 2000; t++)
        {
            var at = Strafer(sim.Now + 1);
            hero.Velocity = (at - hero.At) * (1f / FamilySim.Tick);
            hero.At = at;
            sim.Step();
        }

        var shots = sim.Acts.Where(a => a.Action.Kind == ActionKind.Fire).ToList();
        Assert.True(shots.Count >= 20, $"it fired {shots.Count} times");
        var misses = new List<float>();
        var unled = new List<float>();
        foreach (var s in shots)
        {
            var eye = FamilySim.EyeOf(soldier);
            float flight = eye.DistanceTo(s.Look.Point) / speed;
            int landed = s.Tick + (int)MathF.Round(flight / FamilySim.Tick);
            var there = Strafer(landed);
            // Only shots whose flight the hero does not turn in: a lead cannot know a turn.
            if (MathF.Floor(s.Tick * FamilySim.Tick / 1.5f) != MathF.Floor(landed * FamilySim.Tick / 1.5f))
                continue;
            misses.Add(new AiVector(there.X - s.Look.Point.X, there.Y - s.Look.Point.Y, 0f).Length);
            unled.Add(new AiVector(there.X - Strafer(s.Tick).X, there.Y - Strafer(s.Tick).Y, 0f).Length);
        }

        Assert.True(misses.Count >= 10, $"{misses.Count} shots flew in a straight run");
        Assert.True(misses.Average() <= 30f, $"mean miss {misses.Average():0.0} units over {misses.Count} shots (unled {unled.Average():0.0})");
        Assert.True(unled.Average() >= 100f, $"the control: aiming where the hero stands would miss by {unled.Average():0.0}");
    }

    [Fact]
    public void A_soldier_holds_fire_when_its_line_stops_short_of_its_aim()
    {
        var sim = new FamilySim();
        sim.Hero(Mid);
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(-600, 0), Levels.Eleven(5, ("agility", 0)));
        soldier.Still = true;
        sim.LineOfFire = _ => 150f;

        sim.Step(600);
        Assert.DoesNotContain(sim.Acts, a => a.Action.Kind == ActionKind.Fire);

        sim.LineOfFire = _ => HordeView.FireLineReach;
        sim.Step(120);
        Assert.Contains(sim.Acts, a => a.Action.Kind == ActionKind.Fire);
    }

    [Fact]
    public void A_soldier_holds_fire_with_a_pack_mate_in_its_line()
    {
        var sim = new FamilySim();
        sim.Hero(Mid);
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(-600, 0), Levels.Eleven(5, ("agility", 0)));
        var mate = sim.Robot(HordeWeapons.Melee, Mid + At(-300, 0));
        soldier.Still = mate.Still = true;

        sim.Step(600);
        Assert.DoesNotContain(sim.Acts, a => a.Bot == soldier && a.Action.Kind == ActionKind.Fire);

        mate.Pack = 1;   // no longer a pack-mate it sees: the control
        mate.At = Mid + At(-300, 400);
        sim.Step(120);
        Assert.Contains(sim.Acts, a => a.Bot == soldier && a.Action.Kind == ActionKind.Fire);
    }

    // Found live (2026-11-11): a disciplined soldier holding at its band's outer edge never fired,
    // its fire gate (band x fireRangeGate, 1.0 at Discipline 10) lying inside the band (to x 1.15).
    [Fact]
    public void A_soldier_holding_at_its_bands_outer_edge_fires_at_any_discipline()
    {
        var levels = Levels.Eleven(5, ("discipline", 10), ("agility", 0));
        var sim = new FamilySim();
        sim.Hero(Mid);
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(-Band(levels) * 1.1f, 0), levels);
        soldier.Still = true;

        sim.Step(300);

        Assert.Equal(FamilyNode.Band, soldier.Node(sim));
        Assert.Contains(sim.Acts, a => a.Bot == soldier && a.Action.Kind == ActionKind.Fire);
    }

    [Fact]
    public void A_soldier_with_no_hero_in_sight_hunts_and_holds_fire()
    {
        var sim = new FamilySim();
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Mid);

        sim.Step(30);

        Assert.Equal(FamilyNode.Hold, soldier.Node(sim));
        Assert.Empty(sim.Acts);
    }

    //-------------------------------------------------------------------------
    // The medic
    //-------------------------------------------------------------------------

    [Fact]
    public void A_medic_beams_its_most_hurt_pack_mate_and_heals_it()
    {
        var sim = new FamilySim();
        var medic = sim.Robot(HordeWeapons.SyringeGun, Mid);
        var bad = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(200, 0));
        var scratched = sim.Robot(HordeWeapons.Melee, Mid + At(0, 150));
        bad.Still = scratched.Still = true;
        bad.Life = 100f;
        scratched.Life = 180f;

        sim.Step(120);

        var heals = sim.Acts.Where(a => a.Bot == medic && a.Action.Kind == ActionKind.Ability).ToList();
        Assert.NotEmpty(heals);
        Assert.All(heals, h => Assert.Equal((HordeAbilities.Heal, bad.Id), (h.Action.WeaponSlot, h.Look.Actor)));
        Assert.True(bad.Life > 100f + 0.9f * FamilySim.HealRate * 100 * FamilySim.Tick, $"the patient healed: {bad.Life:0.0}");
    }

    [Fact]
    public void A_medic_goes_to_a_hurt_pack_mate_out_of_reach_then_beams_it()
    {
        var sim = new FamilySim();
        var medic = sim.Robot(HordeWeapons.SyringeGun, Mid);
        var patient = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(1000, 0));
        patient.Still = true;
        patient.Life = 80f;

        sim.Step(DecideSchedule.Period * 2);
        Assert.Equal(FamilyNode.Follow, medic.Node(sim));
        Assert.Empty(sim.Acts);
        sim.Step(600);

        Assert.True(medic.At.DistanceTo(patient.At) <= TheRules.Attacks.Settings.Heal.Range, "it came within the beam's reach");
        Assert.Contains(sim.Acts, a => a.Bot == medic && a.Action.Kind == ActionKind.Ability);
        Assert.True(patient.Life > 80f, "and healed it");
    }

    [Fact]
    public void A_medic_flees_a_hero_in_its_face()
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid + At(150, 0));
        var medic = sim.Robot(HordeWeapons.SyringeGun, Mid);
        var mate = sim.Robot(HordeWeapons.Melee, Mid + At(0, 100));
        mate.Still = true;

        sim.Step(DecideSchedule.Period + 1);
        float before = medic.At.DistanceTo(hero.At);
        sim.Step(90);

        Assert.Contains(medic.Intents, i => i.Reason.NodeId == (short)FamilyNode.Flee);
        Assert.True(medic.At.DistanceTo(hero.At) > before + 150f, $"it ran: {before:0} to {medic.At.DistanceTo(hero.At):0}");
    }

    [Fact]
    public void A_medic_with_a_pack_never_fires_and_alone_it_does()
    {
        var sim = new FamilySim();
        sim.Hero(Mid + At(400, 0));
        var medic = sim.Robot(HordeWeapons.SyringeGun, Mid);
        var mate = sim.Robot(HordeWeapons.Melee, Mid + At(0, 100));
        medic.Still = mate.Still = true;

        sim.Step(300);
        Assert.DoesNotContain(sim.Acts, a => a.Bot == medic && a.Action.Kind == ActionKind.Fire);

        mate.Pack = 1;   // its pack is gone from its view: alone
        sim.Step(120);
        Assert.Contains(sim.Acts, a => a.Bot == medic && a.Action.Kind == ActionKind.Fire);
    }

    //-------------------------------------------------------------------------
    // The families' own parameters move one way with their skill
    //-------------------------------------------------------------------------

    /// <summary>The share of a soldier's aims, standing 100 units above a hero, that are at the feet.</summary>
    private static float FeetShareAt(int cunning)
    {
        var sim = new FamilySim();
        var hero = sim.Hero(Mid);
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(-500, 0, 100), Levels.Eleven(5, ("cunning", cunning), ("agility", 0), ("accuracy", 10)));
        soldier.Still = true;
        sim.Step(1200);
        var aims = soldier.Intents.Where(i => i.Look.Kind == LookKind.Point).Select(i => i.Look.Point.Z - hero.At.Z).ToList();
        Assert.True(aims.Count > 100, $"cunning {cunning}: {aims.Count} aims");
        return aims.Count(z => z < (FamilyNumbers.FeetUnits + FamilyNumbers.ChestUnits) / 2) / (float)aims.Count;
    }

    [Fact]
    public void More_cunning_aims_more_rockets_at_the_feet_from_above()
    {
        float low = FeetShareAt(0), mid = FeetShareAt(5), high = FeetShareAt(10);

        Assert.True(low < mid && mid < high, $"feet shares {low:0.00} < {mid:0.00} < {high:0.00}");
        Assert.Equal(0f, low);
    }

    /// <summary>Whether a soldier of <paramref name="nerve"/> backs off a hero <paramref name="share"/> of its band away.</summary>
    private static bool BacksOffAt(int nerve, float share)
    {
        var levels = Levels.Eleven(5, ("nerve", nerve), ("agility", 0));
        var sim = new FamilySim();
        sim.Hero(Mid);
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Mid + At(-Band(levels) * share, 0), levels);
        soldier.Still = true;
        sim.Step(3 * DecideSchedule.Period);
        return soldier.Last.Move.Kind == MoveKind.Retreat;
    }

    [Fact]
    public void More_nerve_lets_a_hero_closer_before_a_soldier_backs_off()
    {
        float[] probes = [0.25f, 0.35f, 0.45f, 0.55f];
        int Closest(int nerve) => probes.Count(p => BacksOffAt(nerve, p));

        int timid = Closest(0), steady = Closest(5), fearless = Closest(10);

        Assert.True(timid > steady && steady > fearless, $"probes backed off from: nerve 0 {timid}, 5 {steady}, 10 {fearless}");
    }

    /// <summary>Whether a medic of <paramref name="nerve"/> runs from a hero <paramref name="distance"/> away.</summary>
    private static bool MedicFleesAt(int nerve, float distance)
    {
        var sim = new FamilySim();
        sim.Hero(Mid + At(distance, 0));
        var medic = sim.Robot(HordeWeapons.SyringeGun, Mid, Levels.Eleven(5, ("nerve", nerve)));
        var mate = sim.Robot(HordeWeapons.Melee, Mid + At(0, 100));
        medic.Still = mate.Still = true;
        sim.Step(3 * DecideSchedule.Period);
        return medic.Node(sim) == FamilyNode.Flee;
    }

    [Fact]
    public void More_nerve_lets_a_hero_closer_before_a_medic_runs()
    {
        float[] probes = [260f, 380f, 500f, 620f];
        int Closest(int nerve) => probes.Count(d => MedicFleesAt(nerve, d));

        int timid = Closest(0), steady = Closest(5), fearless = Closest(10);

        Assert.True(timid > steady && steady > fearless, $"distances run from: nerve 0 {timid}, 5 {steady}, 10 {fearless}");
    }

    [Fact]
    public void A_temperament_level_reads_back_from_its_parameter()
    {
        var curves = HordeSkillMap.Curves();
        for (int level = 0; level <= 10; level++)
        {
            var p = curves.For(Levels.Profile(Levels.Eleven(5, ("cunning", level), ("nerve", 10 - level))));
            Assert.Equal(level, HordeTemperament.Cunning(curves, p));
            Assert.Equal(10 - level, HordeTemperament.Nerve(curves, p));
        }
    }

    //-------------------------------------------------------------------------
    // The leash (plan L25): horde.json's three doors, on eight rooms in a row
    //-------------------------------------------------------------------------

    /// <summary>A rocket soldier at home in room 0 of eight rooms in a row, standing in <paramref name="room"/>.</summary>
    private static (FamilySim Sim, FamilySim.Bot Soldier) LeashedSoldier(int room)
    {
        var sim = new FamilySim(Row(8, markers: 0));
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Centre(room), home: Centre(0));
        return (sim, soldier);
    }

    [Fact]
    public void A_soldier_fighting_a_hero_in_its_band_past_its_leash_fights_on()
    {
        // Room 3, the hero 300 units on in room 4: four doors from home, within its band.
        var (sim, soldier) = LeashedSoldier(3);
        sim.Hero(Centre(3) + At(300, 0));
        Assert.True(300f < Band(Levels.Eleven()), "within its band");

        sim.Step(60);

        Assert.Contains(soldier.Node(sim), new FamilyNode?[] { FamilyNode.Band, FamilyNode.Strafe, FamilyNode.BackOff });
    }

    [Fact]
    public void A_soldier_seeing_a_hero_past_its_leash_out_of_its_band_walks_home()
    {
        var (sim, soldier) = LeashedSoldier(2);
        sim.Hero(Centre(5));
        Assert.True(Centre(2).DistanceTo(Centre(5)) > Band(Levels.Eleven()), "out of its band");

        sim.Step(60);

        Assert.Equal(FamilyNode.Home, soldier.Node(sim));
        Assert.True(soldier.At.X < Centre(2).X, $"it turned for home, at {soldier.At}");
    }

    [Fact]
    public void A_soldier_seeing_a_hero_on_its_leash_closes_on_him()
    {
        var (sim, soldier) = LeashedSoldier(0);
        sim.Hero(Centre(3));

        sim.Step(60);

        Assert.Equal(FamilyNode.Close, soldier.Node(sim));
        Assert.True(soldier.At.X > Centre(0).X, $"it came on, at {soldier.At}");
    }

    [Fact]
    public void A_boss_walks_and_fights_unleashed_where_its_pack_robots_are_leashed()
    {
        var sim = new FamilySim(Row(8, markers: 0));
        var boss = sim.Robot(HordeWeapons.CritRockets, Centre(1), home: Centre(1));
        var soldier = sim.Robot(HordeWeapons.RocketLauncher, Centre(0), home: Centre(0));

        sim.Step(DecideSchedule.Period * 2);

        Assert.IsType<TF2.Descent.Ai.Boss.BossDecider>(boss.Fights(sim));
        Assert.False(Assert.IsAssignableFrom<IHordeRouted>(boss.Fights(sim)).Route!.Leash.Binds);
        Assert.True(Assert.IsAssignableFrom<IHordeRouted>(soldier.Fights(sim)).Route!.Leash.Binds);
    }
}
