using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Boss;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules.Bosses;
using TF2.Descent.Tests.Horde;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The bosses' brains in the engine-free arena (plan_descent_logic HO13, HO6's inline gate over
/// <see cref="FamilySim"/>): each boss fights as its family, enters its phases at their life
/// thresholds, calls each phase's adds once, asks its telegraphed special at its phase's cadence,
/// and never leaves its arena; the Tank rolls its track, stops at each station and brings the
/// roof down after its last.
/// </summary>
public sealed class BossDeciderTests
{
    private static readonly AiVector Mid = At(2048, 2048);

    private static BossRules Rules => TheRules.Bosses;

    private static int Ticks(double seconds) => (int)Math.Ceiling(seconds / FamilySim.Tick);

    private static List<(FamilySim.Bot Bot, int Tick, ActionIntent Action, LookIntent Look)> Abilities(FamilySim sim, FamilySim.Bot bot, int slot) =>
        sim.Acts.Where(a => a.Bot == bot && a.Action.Kind == ActionKind.Ability && a.Action.WeaponSlot == slot).ToList();

    private static FamilySim.Bot Boss(FamilySim sim, string template, AiVector at, float lifeShare = 1f)
    {
        var bot = sim.Robot(HordeWeapons.Of(TheRules.Robots.Template(template).Attack), at);
        bot.Most = 3000f;
        bot.Life = 3000f * lifeShare;
        return bot;
    }

    /// <summary>Two rooms side by side, 2048 square each: room 0 the arena, room 1 beyond its door.</summary>
    private static HordeLevel ArenaAndHall() =>
        HordeLevel.Create(9, 5,
            [new HordeRoom(0, At(0, 0, -64), At(2048, 2048, 1024), ImmutableArray<PackMarker>.Empty),
             new HordeRoom(1, At(2048, 0, -64), At(4096, 2048, 1024), ImmutableArray<PackMarker>.Empty)],
            [new HordeDoor(0, 1, At(2048, 1024), At(1, 0, 0), 208)]);

    // ------------------------------------------------------------------ fights as its family

    [Fact]
    public void Each_boss_fights_with_its_family_decider_under_its_script()
    {
        var sim = new FamilySim();
        var nuke = Boss(sim, "sir_nukesalot", Mid);
        var crits = Boss(sim, "major_crits", Mid + At(300, 0));
        var punch = Boss(sim, "captain_punch", Mid + At(-300, 0));
        sim.Hero(Mid + At(0, 500));

        sim.Step(3 * DecideSchedule.Period);

        foreach (var (bot, family) in new[] { (nuke, "DemomanDecider"), (crits, "SoldierDecider"), (punch, "RusherDecider") })
        {
            var decider = Assert.IsType<BossDecider>(bot.Fights(sim));
            Assert.Equal(family, decider.Doing.Branch);
        }
        Assert.True(sim.Faulted == 0, sim.LastFault);
    }

    [Fact]
    public void A_boss_at_full_life_asks_no_special_before_its_phase_s_cadence_has_run()
    {
        var sim = new FamilySim();
        var nuke = Boss(sim, "sir_nukesalot", Mid);
        sim.Hero(Mid + At(0, 500));
        double every = Rules.Of("sir_nukesalot")!.Phases[0].SpecialSeconds;

        sim.Step(Ticks(every) - 2 * DecideSchedule.Period);

        Assert.Empty(Abilities(sim, nuke, HordeAbilities.BossSpecial));
        Assert.Contains(sim.Acts, a => a.Bot == nuke && a.Action.Kind == ActionKind.Fire);   // the stimulus: it fought
    }

    [Fact]
    public void A_boss_asks_its_special_at_its_phase_s_cadence_with_its_phase_in_the_ask()
    {
        var sim = new FamilySim();
        var nuke = Boss(sim, "sir_nukesalot", Mid);
        sim.Hero(Mid + At(0, 500));
        double every = Rules.Of("sir_nukesalot")!.Phases[0].SpecialSeconds;

        sim.Step(Ticks(3.5 * every));

        var asks = Abilities(sim, nuke, HordeAbilities.BossSpecial);
        Assert.Equal(3, asks.Count);
        Assert.All(asks, a => Assert.Equal(0, a.Action.OneShotSeq));
        int gap = asks[1].Tick - asks[0].Tick;
        Assert.InRange(gap, Ticks(every) - 1, Ticks(every) + DecideSchedule.Period + 1);
    }

    [Fact]
    public void A_later_phase_asks_its_special_faster()
    {
        var sim = new FamilySim();
        var early = Boss(sim, "sir_nukesalot", Mid);
        var late = Boss(sim, "sir_nukesalot", Mid + At(400, 0), lifeShare: 0.3f);
        sim.Hero(Mid + At(0, 500));

        sim.Step(Ticks(25));

        Assert.True(Abilities(sim, late, HordeAbilities.BossSpecial).Count > Abilities(sim, early, HordeAbilities.BossSpecial).Count,
                    "phase 2's specials come no faster than phase 0's");
        Assert.All(Abilities(sim, late, HordeAbilities.BossSpecial), a => Assert.Equal(2, a.Action.OneShotSeq));
    }

    [Fact]
    public void A_boss_enters_its_phase_at_the_threshold_and_calls_its_adds_once()
    {
        var sim = new FamilySim();
        var crits = Boss(sim, "major_crits", Mid);
        sim.Hero(Mid + At(0, 600));
        sim.Step(2 * DecideSchedule.Period);
        var decider = Assert.IsType<BossDecider>(crits.Fights(sim));
        Assert.Equal(0, decider.Phase);
        Assert.Empty(Abilities(sim, crits, HordeAbilities.BossSummon));

        crits.Life = crits.Most * 0.65f;
        sim.Step(10 * DecideSchedule.Period);

        Assert.Equal(1, decider.Phase);
        var summons = Abilities(sim, crits, HordeAbilities.BossSummon);
        Assert.Equal([1], summons.Select(a => a.Action.OneShotSeq));
    }

    [Fact]
    public void A_boss_healed_back_over_a_threshold_stays_in_its_phase_and_calls_no_adds_again()
    {
        var sim = new FamilySim();
        var crits = Boss(sim, "major_crits", Mid, lifeShare: 0.5f);
        sim.Hero(Mid + At(0, 600));
        sim.Step(4 * DecideSchedule.Period);

        crits.Life = crits.Most;
        sim.Step(10 * DecideSchedule.Period);

        Assert.Equal(1, Assert.IsType<BossDecider>(crits.Fights(sim)).Phase);
        Assert.Single(Abilities(sim, crits, HordeAbilities.BossSummon));
    }

    [Fact]
    public void A_boss_calls_no_adds_with_no_hero_in_sight()
    {
        var sim = new FamilySim();
        var crits = Boss(sim, "major_crits", Mid, lifeShare: 0.5f);

        sim.Step(10 * DecideSchedule.Period);

        Assert.Empty(Abilities(sim, crits, HordeAbilities.BossSummon));
    }

    [Fact]
    public void A_slam_waits_for_a_hero_inside_its_reach()
    {
        var sim = new FamilySim();
        var punch = Boss(sim, "captain_punch", Mid, lifeShare: 0.7f);
        var hero = sim.Hero(Mid + At(0, 1500));
        punch.Still = true;
        var slam = TheRules.Content.Bosses.Special("ground_slam")!;

        sim.Step(Ticks(15));
        Assert.Empty(Abilities(sim, punch, HordeAbilities.BossSpecial));

        hero.At = Mid + At(0, (float)slam.Radius * 0.5f);
        sim.Step(3 * DecideSchedule.Period);
        Assert.Single(Abilities(sim, punch, HordeAbilities.BossSpecial));
    }

    // ------------------------------------------------------------------ arena-bound

    [Fact]
    public void A_boss_never_leaves_its_arena_for_a_hero_beyond_its_door()
    {
        var sim = new FamilySim(ArenaAndHall());
        var punch = Boss(sim, "captain_punch", At(1500, 1024));
        sim.Hero(At(3000, 1024));
        float margin = (float)TheRules.Content.Bosses.ArenaMargin;

        float farthest = 0f;
        for (int t = 0; t < Ticks(12); t++)
        {
            sim.Step();
            farthest = MathF.Max(farthest, punch.At.X);
        }

        Assert.True(farthest <= 2048 - margin + 1f, $"the boss went to x {farthest}, past its arena's edge at {2048 - margin}");
        Assert.True(farthest > 1600f, $"the stimulus: the boss went after the hero (x {farthest})");
        Assert.Equal(FamilyNode.Arena, Assert.IsType<BossDecider>(punch.Fights(sim)).Last);
    }

    [Fact]
    public void A_family_robot_beside_it_does_follow_the_hero_out()
    {
        // The control for the arena fact: a plain melee robot, same start, same hero, leaves the room.
        var sim = new FamilySim(ArenaAndHall());
        var rusher = sim.Robot(HordeWeapons.Melee, At(1500, 1024));
        sim.Hero(At(3000, 1024));

        sim.Step(Ticks(12));

        Assert.True(rusher.At.X > 2048f, $"the rusher stayed at x {rusher.At.X}");
    }

    [Fact]
    public void A_boss_standing_outside_its_arena_goes_home()
    {
        var sim = new FamilySim(ArenaAndHall());
        var nuke = Boss(sim, "sir_nukesalot", At(1000, 1024));
        sim.Step(2 * DecideSchedule.Period);
        nuke.At = At(2600, 1024);   // thrown through the door

        sim.Step(Ticks(6));

        Assert.True(nuke.At.X < 2048f, $"the boss stayed out at x {nuke.At.X}");
    }

    // ------------------------------------------------------------------ the Tank

    private static FamilySim.Bot Tank(FamilySim sim, AiVector at) =>
        sim.Robot(HordeWeapons.Of(TheRules.Robots.Template(TheRules.Content.Bosses.Tank.Template).Attack), at);

    [Fact]
    public void The_tank_rolls_to_its_first_station_and_stops_there_drilling()
    {
        var sim = new FamilySim();
        var tank = Tank(sim, Mid + At(-1024, -1024));
        sim.Step(DecideSchedule.Period);
        var decider = Assert.IsType<TankDecider>(tank.Fights(sim));
        var first = decider.Track![0];

        sim.Step(Ticks(first.DistanceTo(tank.At) / sim.Speed + 2));

        var stations = Abilities(sim, tank, HordeAbilities.TankStation);
        Assert.Equal([1], stations.Select(a => a.Action.OneShotSeq));   // stations are numbered from 1 along the track
        Assert.True(decider.Drilling);
        Assert.True(tank.At.DistanceTo(first) <= TheRules.Content.Bosses.Tank.ArriveUnits + 1f);
    }

    [Fact]
    public void The_tank_drills_each_station_for_its_stop_then_rolls_on()
    {
        var sim = new FamilySim();
        var tank = Tank(sim, Mid + At(-1024, -1024));
        var s = TheRules.Content.Bosses.Tank;
        sim.Step(Ticks(10));
        var at = tank.At;

        sim.Step(Ticks(s.StopSeconds) - Ticks(5));
        Assert.True(tank.At.DistanceTo(at) < 1f, "the tank moved while drilling");

        sim.Step(Ticks(10));
        Assert.True(tank.At.DistanceTo(at) > 100f, "the tank did not roll on after its stop");
    }

    [Fact]
    public void The_tank_visits_every_station_in_order_and_brings_the_roof_down_once()
    {
        var sim = new FamilySim();
        var tank = Tank(sim, Mid + At(-1024, -1024));
        var s = TheRules.Content.Bosses.Tank;
        sim.Step(DecideSchedule.Period);
        var track = Assert.IsType<TankDecider>(tank.Fights(sim)).Track!;
        double loop = 0;
        for (int k = 1; k < track.Count; k++)
            loop += track[k].DistanceTo(track[k - 1]);

        sim.Step(Ticks(loop / sim.Speed + (track.Count - 1) * s.StopSeconds + s.FinalSeconds + 20));

        Assert.Equal(Enumerable.Range(1, track.Count), Abilities(sim, tank, HordeAbilities.TankStation).Select(a => a.Action.OneShotSeq));
        Assert.Equal(Enumerable.Range(1, track.Count - 1), Abilities(sim, tank, HordeAbilities.TankDrilled).Select(a => a.Action.OneShotSeq));
        Assert.Single(Abilities(sim, tank, HordeAbilities.TankRoof));
        Assert.True(Assert.IsType<TankDecider>(tank.Fights(sim)).Roofed);
    }

    [Fact]
    public void The_tank_s_track_loops_its_arena_s_markers_from_the_one_after_its_start_and_ends_short_of_the_middle()
    {
        var markers = ImmutableArray.Create(new PackMarker(At(1000, 1500), 0), new PackMarker(At(1500, 1000), 0),
                                            new PackMarker(At(1000, 500), 0), new PackMarker(At(500, 1000), 0));
        var level = HordeLevel.Create(1, 10, [new HordeRoom(0, At(0, 0, -64), At(2000, 2000, 1024), markers)], []);
        var arena = BossArena.Of(level, At(1000, 1000), 96);

        var track = TankTrack.Of(level, arena, At(560, 1000), 320);

        // Counter-clockwise from west: the next is south, then east, then north, then west again, then the middle.
        Assert.Equal([At(1000, 500), At(1500, 1000), At(1000, 1500), At(500, 1000)], track.Take(4));
        Assert.Equal(320f, track[4].DistanceTo(At(1000, 1000)), 2);
        Assert.Equal(TankTrack.Of(level, arena, At(560, 1000), 320), track);   // the same level and start, the same track
    }
}
