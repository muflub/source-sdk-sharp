using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Far;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Ai.Senses;
using TF2.Descent.Rules.Horde;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// How near the horde notices a hero (the aggro lane, 2026-11-15: "the ai seems to trigger from too
/// far away and ambush the player all at once"): sight within about a room's reach, in its cone or
/// very close behind, never through a wall, shortened by a dark room unless his lamp lights him;
/// hearing that stops at the room beyond an open door; a robot that sees him calls only its own
/// pack; a room waking makes nobody hunt. Every number is horde.json's <c>senses</c>.
/// </summary>
public sealed class HordeAggroTests
{
    private static HordeSenses Senses => TheRules.Content.Horde.Senses;

    private static BodyEye Eye(float range = 900f, float halfConeDeg = 50f, float around = 200f) =>
        new(true, At(0, 0, 72), 0f, range, MathF.Cos(halfConeDeg * MathF.PI / 180f), 0, 10, around);

    private static HeroTarget HeroAt(AiVector feet, float sightScale = 1f, float lamp = 0f) =>
        new(true, feet + At(0, 0, 72), feet + At(0, 0, 40), feet, default, sightScale, HeroVeil.None, lamp);

    /// <summary>Whether a robot at the origin facing +X recognises a hero at <paramref name="feet"/> within one tick (recognition 0 ticks).</summary>
    private static bool Sees(BodyEye eye, HeroTarget hero, bool clear = true)
    {
        var vision = HordeVision.For(1);
        var sink = new SightSink();
        var lines = new FakeLines { Clear = (_, _) => clear };
        vision.Run(1, [default, eye], [hero], lines, sink);
        return sink.Gains > 0;
    }

    private sealed class SightSink : IHordeSightSink
    {
        public int Gains { get; private set; }

        public void Gained(int body, int hero, AiVector at, AiVector velocity, float fraction) => Gains++;

        public void Lost(int body, int hero, AiVector at, AiVector velocity) { }
    }

    // ------------------------------------------------------------------ the numbers

    [Fact]
    public void A_robot_sees_about_a_rooms_reach()
    {
        Assert.InRange(Senses.Sight.Range, 600, 1200);
    }

    [Fact]
    public void Gunfire_carries_about_a_room_and_a_step_only_a_few_paces()
    {
        Assert.InRange(Senses.Hearing.Gunfire, 600, 1500);
        Assert.InRange(Senses.Hearing.Footstep, 50, 300);
        Assert.Equal(1, Senses.Hearing.Rooms);
    }

    // ------------------------------------------------------------------ sight

    [Fact]
    public void A_hero_in_range_and_in_the_cone_with_a_clear_line_is_seen() =>
        Assert.True(Sees(Eye(), HeroAt(At(800, 0))));

    [Fact]
    public void A_hero_beyond_the_range_is_not_seen() =>
        Assert.False(Sees(Eye(), HeroAt(At(950, 0))));

    [Fact]
    public void A_hero_behind_a_wall_is_not_seen() =>
        Assert.False(Sees(Eye(), HeroAt(At(400, 0)), clear: false));

    [Fact]
    public void A_hero_outside_the_cone_is_not_seen() =>
        Assert.False(Sees(Eye(), HeroAt(At(-500, 0))));

    [Fact]
    public void A_hero_very_close_behind_is_seen() =>
        Assert.True(Sees(Eye(), HeroAt(At(-120, 0))));

    [Fact]
    public void A_hero_close_behind_but_beyond_the_around_radius_is_not_seen() =>
        Assert.False(Sees(Eye(), HeroAt(At(-260, 0))));

    [Fact]
    public void A_robots_range_is_horde_jsons_times_its_alertness_not_its_size()
    {
        var rig = new BrainRig();
        var id = rig.Roster.Add("heavy", 1.75, 0, new PackMarker(Centre(0), 0f), 42);
        int body = rig.Lend(id);
        rig.Tick();

        var profile = rig.Brains.SkillMap.Profile(rig.Roster.Temperament(id.Index).Levels);
        float alertness = HordeSkillMap.Extra(rig.Brains.Curves, profile, "sightRangeScale");
        Assert.Equal((float)Senses.Sight.Range * alertness, rig.Brains.SenseOf(body).Range, 2);
    }

    // ------------------------------------------------------------------ light

    [Fact]
    public void A_room_at_full_light_scales_sight_by_one() =>
        Assert.Equal(1.0, Senses.Sight.LightScale((float)Senses.Sight.FullLight + 40f, false));

    [Fact]
    public void A_room_of_half_the_full_light_halves_sight() =>
        Assert.Equal(0.5, Senses.Sight.LightScale((float)Senses.Sight.FullLight / 2f, false), 3);

    [Fact]
    public void A_black_room_scales_sight_to_the_dark_floor() =>
        Assert.Equal(Senses.Sight.DarkScale, Senses.Sight.LightScale(0f, false));

    [Fact]
    public void A_room_dark_by_design_scales_sight_to_the_dark_floor() =>
        Assert.Equal(Senses.Sight.DarkScale, Senses.Sight.LightScale(500f, true));

    [Fact]
    public void An_unmeasured_room_scales_sight_by_one() =>
        Assert.Equal(1.0, Senses.Sight.LightScale(null, false));

    [Fact]
    public void A_hero_in_the_dark_is_not_seen_beyond_the_dark_range() =>
        Assert.False(Sees(Eye(), HeroAt(At(500, 0), sightScale: 0.35f)));

    [Fact]
    public void A_lit_lamp_shows_a_hero_in_the_dark_within_its_reach() =>
        Assert.True(Sees(Eye(), HeroAt(At(450, 0), sightScale: 0.35f, lamp: 480f)));

    [Fact]
    public void A_lit_lamp_never_shows_him_beyond_the_robots_own_range() =>
        Assert.Equal(900f, HordeVision.RangeFor(Eye(), HeroAt(At(0, 0), sightScale: 0.35f, lamp: 4000f)));

    [Fact]
    public void The_brains_fold_the_heros_rooms_light_into_his_sight_scale()
    {
        var rooms = Enumerable.Range(0, 3).Select(i => RowRoom(i, 0) with { Light = i == 1 ? 15f : 100f });
        var level = HordeLevel.Create(7, 3, rooms, Enumerable.Range(0, 2).Select(RowDoor));
        var rig = new BrainRig(level);

        Assert.Equal((float)Senses.Sight.DarkScale, rig.Brains.RoomSight(1));
        Assert.Equal(1f, rig.Brains.RoomSight(0));
    }

    [Fact]
    public void A_robot_does_not_see_a_hero_standing_in_a_dark_room_at_half_its_range()
    {
        var (rig, body, range, eye) = DarkRig();
        rig.Hero(1, At(eye.X - range * 0.5f, eye.Y));
        rig.Tick(60);

        Assert.Equal(1, rig.Level.RoomAt(rig.Heroes[0].Feet));
        Assert.False(rig.Brains.Vision.Holds(body, 0));
    }

    [Fact]
    public void A_robot_sees_a_hero_with_his_lamp_lit_in_a_dark_room_within_its_reach()
    {
        var (rig, body, range, eye) = DarkRig();
        var feet = At(eye.X - range * 0.5f, eye.Y);
        rig.Heroes.Add(new HeroPose(1, feet, feet + At(0, 0, 64), rig.Level.RoomAt(feet), 0f, 1f, HeroVeil.None, range * 0.6f));
        rig.Tick(60);

        Assert.Equal(1, rig.Level.RoomAt(feet));
        Assert.True(rig.Brains.Vision.Holds(body, 0));
    }

    /// <summary>A robot facing west from the east edge of room 1, a dark room (light 15) of a row of three rooms wide enough for its range; its feet.</summary>
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
        return (rig, body, rig.Brains.SenseOf(body).Range, at);
    }

    // ------------------------------------------------------------------ hearing

    [Fact]
    public void Gunfire_is_heard_in_the_next_room_through_its_door()
    {
        var rig = new BrainRig();
        var id = rig.Robot(Centre(1));
        rig.Lend(id);
        rig.Tick();
        rig.Sounds.Add(new HordeSound(SoundKind.Gunfire, Centre(0), 5000f, 1, 0));
        rig.Tick();

        Assert.Equal(1, rig.Brains.SoundsHeard);
    }

    [Fact]
    public void A_sound_two_rooms_away_is_not_heard_however_loud()
    {
        var rig = new BrainRig();
        var id = rig.Robot(Centre(2));
        rig.Lend(id);
        rig.Tick();
        rig.Sounds.Add(new HordeSound(SoundKind.Gunfire, Centre(0), 50000f, 1, 0));
        rig.Tick();

        Assert.Equal(0, rig.Brains.SoundsHeard);
    }

    [Fact]
    public void An_explosion_no_hero_made_is_heard_in_its_own_room()
    {
        var rig = new BrainRig();
        rig.Lend(rig.Robot(Centre(1)));
        rig.Tick();
        rig.Sounds.Add(new HordeSound(SoundKind.Explosion, Centre(1) + At(200, 0), 5000f, -1, 1));
        rig.Tick();

        Assert.Equal(1, rig.Brains.SoundsHeard);
    }

    [Fact]
    public void An_explosion_no_hero_made_is_not_heard_in_the_next_room()
    {
        var rig = new BrainRig();
        rig.Lend(rig.Robot(Centre(1)));
        rig.Tick();
        rig.Sounds.Add(new HordeSound(SoundKind.Explosion, Centre(0), 5000f, -1, 0));
        rig.Tick();

        Assert.Equal(0, rig.Brains.SoundsHeard);
    }

    [Fact]
    public void A_far_pack_does_not_hear_an_explosion_no_hero_made_in_the_next_room()
    {
        var level = Row(4, markers: 0);
        var roster = RobotRoster.Create(TheRules, BrainRig.Tps);
        roster.Load(level, 1);
        var near = roster.Add("soldier", 1.0, 1, new PackMarker(Centre(1), 0f), 42);
        var far = FarSimulation.For(TheRules.Content.Horde, level, BrainRig.Tps);

        far.Tick(roster, new NoSight(), [new SoundEvent(0, Centre(0), 50000f, -1, SoundKind.Explosion)], 100);

        Assert.Equal(AlertLevel.Calm, roster.PackAlert(roster.Pack(near.Index)));
    }

    [Fact]
    public void A_sound_beside_a_wall_with_no_door_is_not_heard_through_it()
    {
        // Rooms 0 and 1 side by side with no door between them; each opens north into room 2.
        var rooms = new[]
        {
            new HordeRoom(0, At(0, 0), At(512, 512, 256), []),
            new HordeRoom(1, At(512, 0), At(1024, 512, 256), []),
            new HordeRoom(2, At(0, 512), At(1024, 1024, 256), []),
        };
        var doors = new[] { new HordeDoor(0, 2, At(64, 512), At(0, 1, 0), 64), new HordeDoor(1, 2, At(960, 512), At(0, 1, 0), 64) };
        var rig = new BrainRig(HordeLevel.Create(7, 3, rooms, doors));
        var id = rig.Robot(At(540, 256));
        rig.Lend(id);
        rig.Tick();
        rig.Sounds.Add(new HordeSound(SoundKind.Gunfire, At(490, 256), 50000f, 1, 0));
        rig.Tick();

        Assert.Equal(0, rig.Brains.SoundsHeard);
    }

    [Fact]
    public void A_step_is_heard_within_its_reach()
    {
        var (rig, hearing) = Listener();
        float reach = HordeSoundKinds.Reach(Senses.Hearing, SoundKind.Footstep);
        rig.Sounds.Add(new HordeSound(SoundKind.Footstep, Centre(1) + At(-reach * hearing * 0.5f, 0), reach, 1, 1));
        rig.Tick();

        Assert.Equal(1, rig.Brains.SoundsHeard);
    }

    [Fact]
    public void A_step_beyond_its_short_reach_is_not_heard()
    {
        var (rig, hearing) = Listener();
        float reach = HordeSoundKinds.Reach(Senses.Hearing, SoundKind.Footstep);
        rig.Sounds.Add(new HordeSound(SoundKind.Footstep, Centre(1) + At(-reach * hearing * 1.2f, 0), reach, 1, 1));
        rig.Tick();

        Assert.Equal(0, rig.Brains.SoundsHeard);
    }

    private static (BrainRig Rig, float Hearing) Listener()
    {
        var rig = new BrainRig();
        int body = rig.Lend(rig.Robot(Centre(1)));
        rig.Tick();
        return (rig, rig.Brains.SenseOf(body).Hearing);
    }

    [Fact]
    public void Each_kind_carries_horde_jsons_reach()
    {
        var h = Senses.Hearing;
        Assert.Equal((float)h.Gunfire, HordeSoundKinds.Reach(h, SoundKind.Gunfire));
        Assert.Equal((float)h.Footstep, HordeSoundKinds.Reach(h, SoundKind.Footstep));
        Assert.Equal((float)h.Landing, HordeSoundKinds.Reach(h, SoundKind.Physics));
        Assert.Equal(0f, HordeSoundKinds.Reach(h, SoundKind.Unknown));
    }

    [Fact]
    public void A_far_robot_two_rooms_from_a_sound_does_not_hear_it()
    {
        var level = Row(4, markers: 0);
        var roster = RobotRoster.Create(TheRules, BrainRig.Tps);
        roster.Load(level, 1);
        var near = roster.Add("soldier", 1.0, 1, new PackMarker(Centre(1), 0f), 42);
        var farther = roster.Add("soldier", 1.0, 2, new PackMarker(Centre(2), 0f), 43);
        var far = FarSimulation.For(TheRules.Content.Horde, level, BrainRig.Tps);

        far.Tick(roster, new NoSight(), [new SoundEvent(0, Centre(0), 50000f, 1, SoundKind.Gunfire)], 100);

        Assert.Equal(AlertLevel.Alerted, roster.PackAlert(roster.Pack(near.Index)));
        Assert.Equal(AlertLevel.Calm, roster.PackAlert(roster.Pack(farther.Index)));
    }

    private sealed class NoSight : IHeroSight
    {
        public bool CanSee(AiVector point) => false;

        public bool CanSeeInto(int room) => false;
    }

    // ------------------------------------------------------------------ the pack call

    [Fact]
    public void A_robot_that_sees_a_hero_calls_its_own_packmate()
    {
        var (rig, _, mate, _) = TwoPacks();
        rig.Hero(1, Centre(1) + At(100, 0));
        rig.Tick(120);

        Assert.Equal(1, rig.Roster.Carried(mate.Index).TargetSeat);
        Assert.True(rig.Brains.PackCalls >= 1);
    }

    [Fact]
    public void A_robot_that_sees_a_hero_does_not_call_another_pack()
    {
        var (rig, _, _, stranger) = TwoPacks();
        rig.Hero(1, Centre(1) + At(100, 0));
        rig.Tick(120);

        Assert.Equal(AlertLevel.Calm, rig.Roster.Carried(stranger.Index).Alert);
        Assert.Equal(-1, rig.Roster.Carried(stranger.Index).TargetSeat);
    }

    /// <summary>
    /// Room 1: a robot facing east (it will see a hero there), a pack-mate beside it facing west and
    /// one of another pack facing west too (neither sees him); lines from the mates are blocked.
    /// </summary>
    private static (BrainRig Rig, RobotId Seer, RobotId Mate, RobotId Stranger) TwoPacks()
    {
        var rig = new BrainRig();
        var seer = rig.Robot(Centre(1) + At(-150, 0));
        var mate = rig.Robot(Centre(1) + At(-150, 120), joinLastPack: true);
        var stranger = rig.Robot(Centre(1) + At(-150, -120));
        int seerBody = rig.Lend(seer, yaw: 0f);
        rig.Lend(mate, yaw: 180f);
        rig.Lend(stranger, yaw: 180f);
        AiVector seerEye = Centre(1) + At(-150, 0, HordeBrains.EyeHeight);
        rig.Lines.Clear = (from, _) => from.DistanceTo(seerEye) < 1f;
        rig.Slide = false;
        rig.Tick();
        Assert.Equal(rig.Roster.Pack(seer.Index), rig.Roster.Pack(mate.Index));
        Assert.NotEqual(rig.Roster.Pack(seer.Index), rig.Roster.Pack(stranger.Index));
        return (rig, seer, mate, stranger);
    }

    // ------------------------------------------------------------------ waking

    [Fact]
    public void A_room_waking_makes_none_of_its_robots_hunt()
    {
        var rig = new BrainRig(Row(4, markers: 2));
        int room = Enumerable.Range(1, 3).First(r => Enumerable.Range(0, rig.Roster.Count).Any(i => rig.Roster.Room(i) == r));
        var rows = Enumerable.Range(0, rig.Roster.Count).Where(i => rig.Roster.Room(i) == room).ToArray();
        Assert.All(rows, i => Assert.Equal(RobotState.Asleep, rig.Roster.State(i)));
        rig.Roster.WakeRoom(room);
        Assert.All(rows, i => Assert.Equal(RobotState.Far, rig.Roster.State(i)));
        var bodies = rows.Select(i => rig.Lend(rig.Roster.Id(i))).ToArray();
        rig.Hero(1, Centre(0));
        rig.Tick(60);

        Assert.NotEmpty(bodies);
        Assert.All(bodies, b => Assert.Contains(rig.NodeOf(b), new HuntDecider.Node?[] { HuntDecider.Node.Hold, HuntDecider.Node.Home }));
        Assert.All(bodies, b => Assert.Equal(AlertLevel.Calm, rig.Roster.Carried(rig.Brains.RowAt(b)).Alert));
    }
}
