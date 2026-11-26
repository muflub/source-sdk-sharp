using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Senses;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Horde;
using TF2.Descent.Tests.Robots;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// Robots notice a hero whose Lamplighter is on (the user's ruling L26, 2026-11-22): his lit lamp's
/// reach (its radius, how far a robot sees him however dark his room) is times horde.json's
/// <c>sight.beaconReachScale</c> while it is on, the rule's one place being
/// <see cref="SightSenses.LampReach"/>; off, the reach is the radius times <c>lampReachScale</c> as
/// before. The flag rides the hero's pose (<see cref="HeroPose.Beacon"/>), so a replay reads it.
/// </summary>
public sealed class LamplighterSightTests
{
    private static SightSenses Sight => TheRules.Content.Horde.Senses.Sight;

    [Fact]
    public void Off_a_lamps_reach_is_its_radius_times_the_lamp_reach_scale_as_before()
    {
        Assert.Equal(480 * Sight.LampReachScale, Sight.LampReach(480, beacon: false), 9);
    }

    [Fact]
    public void On_a_lamps_reach_is_also_times_the_beacon_reach_scale()
    {
        Assert.Equal(480 * Sight.LampReachScale * Sight.BeaconReachScale, Sight.LampReach(480, beacon: true), 9);
    }

    [Fact]
    public void A_lamp_out_reaches_nowhere_beacon_or_not()
    {
        Assert.Equal(0, Sight.LampReach(0, beacon: true));
    }

    [Fact]
    public void The_shipped_beacon_scale_shows_a_lamplighter_from_farther()
    {
        Assert.True(Sight.BeaconReachScale > 1, $"beaconReachScale {Sight.BeaconReachScale}");
    }

    [Fact]
    public void A_beacon_scale_below_one_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n["senses"]!["sight"]!["beaconReachScale"] = 0.5);

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Rule == ContentRule.OutOfRange && e.Message.Contains("senses.sight.beaconReachScale"));
    }

    [Fact]
    public void A_missing_beacon_scale_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Horde, n => n["senses"]!["sight"]!.AsObject().Remove("beaconReachScale"));

        Assert.Contains(errors, e => e.Table == ContentTables.Horde && e.Message.Contains("beaconReachScale"));
    }

    // ------------------------------------------------------------------ a robot in a dark room

    [Fact]
    public void Off_a_robot_does_not_notice_a_lit_hero_beyond_his_lamps_reach_in_a_dark_room()
    {
        var (rig, body, range, eye) = DarkRig();
        LitHero(rig, At(eye.X - range * 0.5f, eye.Y), reach: Between(range), beacon: false);
        rig.Tick(60);

        Assert.False(rig.Brains.Vision.Holds(body, 0));
        Assert.Equal(0, rig.Brains.Vision.Gains);
    }

    [Fact]
    public void On_the_same_robot_notices_the_same_hero_at_the_same_spot()
    {
        var (rig, body, range, eye) = DarkRig();
        LitHero(rig, At(eye.X - range * 0.5f, eye.Y), reach: Between(range), beacon: true);
        rig.Tick(60);

        Assert.True(rig.Brains.Vision.Holds(body, 0));
    }

    [Fact]
    public void On_with_his_lamp_out_he_is_seen_as_any_hero_in_the_dark()
    {
        var (rig, body, range, eye) = DarkRig();
        LitHero(rig, At(eye.X - range * 0.5f, eye.Y), reach: 0f, beacon: true);
        rig.Tick(60);

        Assert.False(rig.Brains.Vision.Holds(body, 0));
    }

    [Fact]
    public void On_a_lamplighter_beyond_even_the_beacons_reach_is_not_noticed()
    {
        var (rig, body, range, eye) = DarkRig();
        float reach = Between(range);
        LitHero(rig, At(eye.X - reach * (float)(Sight.LampReachScale * Sight.BeaconReachScale) - 64f, eye.Y), reach, beacon: true);
        rig.Tick(60);

        Assert.False(rig.Brains.Vision.Holds(body, 0));
    }

    /// <summary>
    /// A lamp radius whose plain reach falls short of half the robot's range and whose beacon's
    /// reach passes it, so the hero at half its range is beyond the one and within the other.
    /// </summary>
    private static float Between(float range)
    {
        float reach = range * 0.5f / (float)(Sight.LampReachScale * (1 + Sight.BeaconReachScale) / 2);
        Assert.True(reach * Sight.LampReachScale < range * 0.5f && reach * Sight.LampReachScale * Sight.BeaconReachScale > range * 0.5f);
        return reach;
    }

    private static void LitHero(BrainRig rig, AiVector feet, float reach, bool beacon)
    {
        rig.Heroes.Add(new HeroPose(1, feet, feet + At(0, 0, 64), rig.Level.RoomAt(feet), 0f, 1f, HeroVeil.None, reach, beacon));
        Assert.Equal(1, rig.Level.RoomAt(feet));   // he stands in the dark room
    }

    /// <summary>A robot facing west from the east edge of room 1, a dark room (light 15) of a row of three rooms wide enough for its range; its eye's feet.</summary>
    private static (BrainRig Rig, int Body, float Range, AiVector At) DarkRig()
    {
        const float wide = 2048;
        var rooms = Enumerable.Range(0, 3).Select(i => new HordeRoom(i, At(i * wide, 0, 0), At((i + 1) * wide, wide, 256), [], i == 1 ? 15f : 100f));
        var doors = Enumerable.Range(0, 2).Select(a => new HordeDoor(a, a + 1, At((a + 1) * wide, wide / 2), At(1, 0, 0), 64));
        var rig = new BrainRig(HordeLevel.Create(7, 3, rooms, doors));
        var at = At(2 * wide - 64, wide / 2);
        var id = rig.Robot(at);
        int body = rig.Lend(id, yaw: 180f);
        rig.Slide = false;
        rig.Tick();
        Assert.True(rig.Brains.RoomSight(1) * rig.Brains.SenseOf(body).Range < rig.Brains.SenseOf(body).Range * 0.5f);   // the dark shortens its sight below half
        return (rig, body, rig.Brains.SenseOf(body).Range, at);
    }
}
