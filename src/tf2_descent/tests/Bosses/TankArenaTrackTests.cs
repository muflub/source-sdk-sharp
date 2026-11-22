using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Boss;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using TF2.Descent.Ai.Level;
using TF2.Descent.Tests.Horde;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The Tank's track from the arena's layout (the ARNA contract of 2026-11-16) in the engine-free
/// arena: the file's order is the order and the loop closes back to point 0; it stops and drills
/// only at the points flagged station, numbered 1, 2, ... along the file; a point that is no
/// station it rolls through; the final station's drill brings the roof down, after every other
/// station is drilled.
/// </summary>
public sealed class TankArenaTrackTests
{
    private static int Ticks(double seconds) => (int)Math.Ceiling(seconds / FamilySim.Tick);

    private static List<int> Asked(FamilySim sim, FamilySim.Bot bot, int slot) =>
        [.. sim.Acts.Where(a => a.Bot == bot && a.Action.Kind == ActionKind.Ability && a.Action.WeaponSlot == slot).Select(a => a.Action.OneShotSeq)];

    private static readonly AiVector P0 = At(1000, 1000), P1 = At(2000, 1000), P2 = At(3000, 1000), P3 = At(3000, 2000), P4 = At(2000, 2000);

    /// <summary>The hall with a boss whose layout's track is <paramref name="track"/>.</summary>
    private static FamilySim Arena(params (AiVector At, bool Station, bool Final)[] track)
    {
        var room = new HordeRoom(0, At(0, 0, -64), At(4096, 4096, 1024), ImmutableArray<PackMarker>.Empty);
        var boss = new HordeBoss("major_crits", 0, new PackMarker(At(2048, 3000), 0), ArenaLevelTests.HordeLayout(track));
        return new FamilySim(HordeLevel.Create(7, 10, [room], [], boss));
    }

    private static FamilySim.Bot Tank(FamilySim sim, AiVector at) =>
        sim.Robot(HordeWeapons.Of(TheRules.Robots.Template(TheRules.Content.Bosses.Tank.Template).Attack), at);

    /// <summary>Two stations, a point between them, a point after, then the final.</summary>
    private static FamilySim Standard() =>
        Arena((P0, true, false), (P1, false, false), (P2, true, false), (P3, false, false), (P4, true, true));

    private static int Whole(FamilySim sim, int stations, double length) =>
        Ticks(length / sim.Speed + (stations - 1) * TheRules.Content.Bosses.Tank.StopSeconds + TheRules.Content.Bosses.Tank.FinalSeconds + 30);

    [Fact]
    public void The_route_is_the_layout_s_track_in_file_order_with_stations_numbered_along_it()
    {
        var level = Standard().Level;
        var arena = BossArena.Of(level, P0, 96);

        var route = TankTrack.Route(level, arena, At(500, 500), 320);

        Assert.Equal([P0, P1, P2, P3, P4], route.Select(s => s.Position));
        Assert.Equal([1, 0, 2, 0, 3], route.Select(s => s.Station));
        Assert.Equal([false, false, false, false, true], route.Select(s => s.Final));
    }

    [Fact]
    public void The_tank_stops_only_at_stations_rolling_through_the_points_between()
    {
        var sim = Standard();
        var tank = Tank(sim, At(500, 500));

        sim.Step(Ticks((P0.DistanceTo(At(500, 500)) + P0.DistanceTo(P1) + P1.DistanceTo(P2)) / sim.Speed + TheRules.Content.Bosses.Tank.StopSeconds + 10));

        Assert.Equal([1, 2], Asked(sim, tank, HordeAbilities.TankStation));
        Assert.True(Assert.IsType<TankDecider>(tank.Fights(sim)).Drilling);
        Assert.True(tank.At.DistanceTo(P2) <= TheRules.Content.Bosses.Tank.ArriveUnits + 1f, $"the tank is at {tank.At}, not station 2");
    }

    [Fact]
    public void The_tank_tells_each_drilled_station_by_its_number_when_its_drill_ends()
    {
        var sim = Standard();
        var tank = Tank(sim, At(500, 500));

        sim.Step(Ticks(P0.DistanceTo(At(500, 500)) / sim.Speed + 2));
        Assert.Empty(Asked(sim, tank, HordeAbilities.TankDrilled));   // drilling, not drilled

        sim.Step(Ticks(TheRules.Content.Bosses.Tank.StopSeconds + 1));

        Assert.Equal([1], Asked(sim, tank, HordeAbilities.TankDrilled));
    }

    [Fact]
    public void The_final_station_s_drill_brings_the_roof_down_once_under_its_number()
    {
        var sim = Standard();
        var tank = Tank(sim, At(500, 500));
        double length = At(500, 500).DistanceTo(P0) + P0.DistanceTo(P1) + P1.DistanceTo(P2) + P2.DistanceTo(P3) + P3.DistanceTo(P4);

        sim.Step(Whole(sim, 3, length));

        Assert.Equal([1, 2, 3], Asked(sim, tank, HordeAbilities.TankStation));
        Assert.Equal([1, 2], Asked(sim, tank, HordeAbilities.TankDrilled));
        Assert.Equal([3], Asked(sim, tank, HordeAbilities.TankRoof));
        Assert.True(Assert.IsType<TankDecider>(tank.Fights(sim)).Roofed);
    }

    [Fact]
    public void A_final_listed_before_a_station_is_passed_and_drilled_last_round_the_closed_loop()
    {
        // The final is point 1; station 3 comes after it in the file, so the Tank rolls past the
        // final, drills 3, closes the loop through point 0 (already drilled: rolled through) and
        // drills the final last.
        var sim = Arena((P0, true, false), (P4, true, true), (P2, true, false));
        var tank = Tank(sim, At(500, 500));
        double length = At(500, 500).DistanceTo(P0) + P0.DistanceTo(P4) + P4.DistanceTo(P2) + P2.DistanceTo(P0) + P0.DistanceTo(P4);

        sim.Step(Whole(sim, 3, length));

        Assert.Equal([1, 3, 2], Asked(sim, tank, HordeAbilities.TankStation));
        Assert.Equal([1, 3], Asked(sim, tank, HordeAbilities.TankDrilled));
        Assert.Equal([2], Asked(sim, tank, HordeAbilities.TankRoof));
    }

    [Fact]
    public void A_track_with_no_final_flag_makes_its_last_station_the_final()
    {
        var level = Arena((P0, true, false), (P1, false, false), (P2, true, false), (P3, false, false)).Level;

        var route = TankTrack.Route(level, BossArena.Of(level, P0, 96), At(500, 500), 320);

        Assert.Equal([false, false, true, false], route.Select(s => s.Final));
    }

    [Fact]
    public void A_level_without_a_layout_track_routes_round_the_markers_every_point_a_station_the_last_final()
    {
        var markers = ImmutableArray.Create(new PackMarker(At(1000, 1500), 0), new PackMarker(At(1500, 1000), 0),
                                            new PackMarker(At(1000, 500), 0), new PackMarker(At(500, 1000), 0));
        var level = HordeLevel.Create(1, 10, [new HordeRoom(0, At(0, 0, -64), At(2000, 2000, 1024), markers)], []);
        var arena = BossArena.Of(level, At(1000, 1000), 96);

        var route = TankTrack.Route(level, arena, At(560, 1000), 320);

        Assert.Equal(TankTrack.Of(level, arena, At(560, 1000), 320), route.Select(s => s.Position));
        Assert.Equal([1, 2, 3, 4, 5], route.Select(s => s.Station));
        Assert.Equal([false, false, false, false, true], route.Select(s => s.Final));
    }
}
