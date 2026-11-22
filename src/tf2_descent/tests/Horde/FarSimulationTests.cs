using SourceSharp.Ai;
using TF2.Descent.Ai.Far;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Furniture;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The far simulation (plan_descent_logic 3.5, 3.10): a pure function of its inputs, sound by
/// the walk through doors, an alerted pack converging, the queue at a door into a seen room,
/// patrols, statuses, and no allocation in a steady tick.
/// </summary>
public sealed class FarSimulationTests
{
    private static readonly long Interval = FarSimulation.Interval(TheRules.Content.Horde, RosterTests.TicksPerSecond);

    private sealed class Far
    {
        public Far(HordeLevel level, int party = 1)
        {
            Level = level;
            Roster = RosterTests.Rolled(level, party);
            Sim = FarSimulation.For(TheRules.Content.Horde, level, RosterTests.TicksPerSecond);
            Sight = new FakeSight(level);
        }

        public HordeLevel Level { get; }
        public RobotRoster Roster { get; }
        public FarSimulation Sim { get; }
        public FakeSight Sight { get; }
        public long Now { get; set; } = 1000;

        public void Tick(params SoundEvent[] sounds)
        {
            Now += Interval;
            Sim.Tick(Roster, Sight, sounds, Now);
        }

        /// <summary>
        /// Alerts pack 0 to a hero's contact at <paramref name="at"/> in <paramref name="room"/> and ticks
        /// once: the stimulus of the converging facts (a sound would have to be within the hearing's
        /// rooms of the pack: horde.json's senses.hearing.rooms).
        /// </summary>
        public void Call(int room, AiVector at)
        {
            Roster.AlertPack(0, room, at, Now, 0);
            Tick();
        }

        public void WakeAll()
        {
            for (int r = 0; r < Level.RoomCount; r++) Roster.WakeRoom(r);
        }

        public int[] Pack(int p) => [.. Enumerable.Range(Roster.PackFirst(p), Roster.PackSize(p))];
    }

    /// <summary>Six rooms in a row, the only pack in room 5.</summary>
    private static HordeLevel PackAtTheEnd(ulong seed = 11) =>
        HordeLevel.Create(seed, 4, Enumerable.Range(0, 6).Select(i => RowRoom(i, i == 5 ? 2 : 0)), Enumerable.Range(0, 5).Select(RowDoor));

    //-------------------------------------------------------------------------
    // Purity
    //-------------------------------------------------------------------------

    private static string Run(ulong seed, bool loud)
    {
        var far = new Far(Row(8, markers: 3, seed: 5, depth: 9), 3);
        var roll = new Seeded(seed);
        far.WakeAll();
        far.Roster.ApplyStatus(0, "burn", far.Now);
        far.Roster.ApplyStatus(1, "slow", far.Now);
        for (int t = 0; t < 300; t++)
        {
            for (int r = 0; r < 8; r++) far.Sight.SeeInto[r] = roll.Chance(15);
            if (loud && roll.Chance(10))
            {
                int room = roll.Next(8);
                far.Tick(new SoundEvent(room, Centre(room), 600 + roll.Next(1200), roll.Next(4) - 1));
            }
            else far.Tick();
        }
        return RosterTests.Digest(far.Roster);
    }

    [Fact]
    public void Two_runs_from_the_same_state_and_inputs_end_in_the_same_bytes()
    {
        Assert.Equal(Run(9, loud: true), Run(9, loud: true));
    }

    [Fact]
    public void Other_inputs_end_in_other_bytes()
    {
        Assert.NotEqual(Run(9, loud: true), Run(9, loud: false));
    }

    //-------------------------------------------------------------------------
    // Sound
    //-------------------------------------------------------------------------

    [Fact]
    public void A_loud_sound_in_the_next_room_through_its_door_is_heard()
    {
        var far = new Far(HordeLevel.Create(1, 3, [RowRoom(0, 0), RowRoom(1, 2)], [RowDoor(0)]));
        var sound = new SoundEvent(0, At(400, 256), 800, 2);
        int robot = 0;
        far.Roster.SetPose(robot, 1, At(1136 - 512, 256), 0);
        Assert.Equal(224, far.Level.PathDistance(0, sound.Position, 1, far.Roster.Position(robot)), 0.01f);

        far.Tick(sound);

        Assert.Equal((AlertLevel.Alerted, 2, sound.Position), (far.Roster.Carried(robot).Alert, far.Roster.Carried(robot).TargetSeat, far.Roster.Carried(robot).LastKnownPosition));
        Assert.Equal((AlertLevel.Alerted, 0, 2), (far.Roster.PackAlert(0), far.Roster.ContactRoom(0), far.Roster.ContactSeat(0)));
        Assert.All(far.Pack(0), i => Assert.Equal(RobotState.Far, far.Roster.State(i)));
    }

    [Fact]
    public void A_loud_sound_two_rooms_away_through_doors_is_not_heard()
    {
        var far = new Far(HordeLevel.Create(1, 3, [RowRoom(0, 0), RowRoom(1, 0), RowRoom(2, 2)], [RowDoor(0), RowDoor(1)]));
        var sound = new SoundEvent(0, At(400, 256), 800, 2);
        int robot = 0;
        far.Roster.SetPose(robot, 2, At(1136, 256), 0);
        Assert.Equal(736, far.Level.PathDistance(0, sound.Position, 2, far.Roster.Position(robot)), 0.01f);   // within its loudness

        far.Tick(sound);

        Assert.Equal(AlertLevel.Calm, far.Roster.Carried(robot).Alert);
        Assert.Equal(AlertLevel.Calm, far.Roster.PackAlert(0));
    }

    [Fact]
    public void The_same_sound_through_a_wall_with_a_long_way_round_is_not_heard()
    {
        // Rooms 0 and 2 share the wall at x = 512; the only way between them is round through room 1.
        var rooms = new[]
        {
            new HordeRoom(0, At(0, 0), At(512, 512, 256), []),
            new HordeRoom(1, At(0, 512), At(1536, 4096, 256), []),
            new HordeRoom(2, At(512, 0), At(1536, 512, 256), [new PackMarker(At(700, 256), 0), new PackMarker(At(800, 256), 0)]),
        };
        var doors = new[] { new HordeDoor(0, 1, At(256, 512), At(0, 1, 0), 64), new HordeDoor(1, 2, At(768, 512), At(0, -1, 0), 64) };
        var far = new Far(HordeLevel.Create(1, 3, rooms, doors));
        var sound = new SoundEvent(0, At(400, 256), 800, 2);
        int robot = 0;
        far.Roster.SetPose(robot, 2, At(1136, 256), 0);
        Assert.Equal(736, sound.Position.DistanceTo(far.Roster.Position(robot)), 0.01f);   // as far as the sound that is heard
        Assert.True(far.Level.PathDistance(0, sound.Position, 2, far.Roster.Position(robot)) > 800);

        far.Tick(sound);

        Assert.Equal(AlertLevel.Calm, far.Roster.Carried(robot).Alert);
        Assert.Equal(AlertLevel.Calm, far.Roster.PackAlert(0));
        Assert.All(far.Pack(0), i => Assert.Equal(RobotState.Asleep, far.Roster.State(i)));
    }

    //-------------------------------------------------------------------------
    // Converging and queueing
    //-------------------------------------------------------------------------

    [Fact]
    public void A_pack_alerted_by_sound_converges_on_it()
    {
        var far = new Far(PackAtTheEnd());
        var contact = Centre(1);

        far.Call(1, contact);
        for (int t = 0; t < 40; t++) far.Tick();

        Assert.True(far.Pack(0).Length >= 2);
        Assert.All(far.Pack(0), i =>
        {
            Assert.Equal(1, far.Roster.Room(i));
            Assert.True(far.Roster.Position(i).DistanceTo(contact) <= TheRules.Content.Horde.ArriveRadius, $"robot {i} at {far.Roster.Position(i)}");
        });
    }

    [Fact]
    public void A_calm_pack_stays_in_its_room()
    {
        var far = new Far(PackAtTheEnd());
        far.WakeAll();

        for (int t = 0; t < 60; t++) far.Tick();

        Assert.All(far.Pack(0), i => Assert.Equal(5, far.Roster.Room(i)));
    }

    [Fact]
    public void A_far_robot_queues_at_the_door_of_a_room_a_hero_can_see_into_and_never_enters_it()
    {
        var far = new Far(PackAtTheEnd());
        far.Sight.SeeInto[2] = true;

        far.Call(1, Centre(1));
        for (int t = 0; t < 40; t++)
        {
            far.Tick();
            Assert.All(far.Pack(0), i => Assert.NotEqual(2, far.Roster.Room(i)));
        }

        var door = far.Level.Doors[far.Level.DoorBetween(2, 3)];
        var spots = far.Pack(0).Select(far.Roster.Position).ToArray();
        Assert.All(far.Pack(0), i => Assert.Equal((3, FarActivity.Queue), (far.Roster.Room(i), far.Roster.Activity(i))));
        Assert.Equal(spots.Length, spots.Distinct().Count());
        var q = TheRules.Content.Horde.DoorQueue;
        Assert.All(spots, s => Assert.Equal(0, (s.DistanceTo(door.Position) - q.Backoff) % q.Spacing, 2));
    }

    [Fact]
    public void A_queue_moves_on_once_no_hero_can_see_into_the_room()
    {
        var far = new Far(PackAtTheEnd());
        far.Sight.SeeInto[2] = true;
        far.Call(1, Centre(1));
        for (int t = 0; t < 20; t++) far.Tick();
        Assert.All(far.Pack(0), i => Assert.Equal(3, far.Roster.Room(i)));

        far.Sight.SeeInto[2] = false;
        for (int t = 0; t < 20; t++) far.Tick();

        Assert.All(far.Pack(0), i => Assert.Equal(1, far.Roster.Room(i)));
    }

    [Fact]
    public void A_far_robot_in_a_room_a_hero_can_see_into_holds()
    {
        var far = new Far(PackAtTheEnd());
        far.Sight.SeeInto[5] = true;
        int i = far.Pack(0)[0];
        var before = far.Roster.Position(i);

        far.Call(1, Centre(1));
        for (int t = 0; t < 10; t++) far.Tick();

        Assert.Equal((before, FarActivity.Hold), (far.Roster.Position(i), far.Roster.Activity(i)));
    }

    [Fact]
    public void A_pack_forgets_its_contact_and_walks_home()
    {
        var far = new Far(PackAtTheEnd());
        far.Call(1, Centre(1));
        for (int t = 0; t < 30; t++) far.Tick();
        Assert.All(far.Pack(0), i => Assert.Equal(1, far.Roster.Room(i)));

        for (int t = 0; t < 120; t++) far.Tick();

        Assert.Equal(AlertLevel.Calm, far.Roster.PackAlert(0));
        Assert.All(far.Pack(0), i => Assert.Equal(5, far.Roster.Room(i)));
    }

    //-------------------------------------------------------------------------
    // Patrol and statuses
    //-------------------------------------------------------------------------

    [Fact]
    public void A_calm_far_pack_on_one_marker_holds_each_robot_at_its_own_post()
    {
        // One marker: nothing to patrol, so each robot holds at home, which is its post (HO9), not the marker.
        var level = HordeLevel.Create(11, 4, Enumerable.Range(0, 6).Select(i => RowRoom(i, i == 5 ? 1 : 0)), Enumerable.Range(0, 5).Select(RowDoor));
        var far = new Far(level, 4);
        far.WakeAll();
        int[] pack = far.Pack(0);
        for (int t = 0; t < 20; t++) far.Tick();

        Assert.True(pack.Length >= 4);
        Assert.All(pack, i => Assert.Equal(far.Roster.Post(i), far.Roster.Position(i)));
        Assert.Equal(pack.Length, pack.Select(i => far.Roster.Position(i)).Distinct().Count());
    }

    [Fact]
    public void A_calm_far_robot_patrols_its_rooms_markers_and_waits_at_each()
    {
        var far = new Far(PackAtTheEnd());
        far.WakeAll();
        int i = far.Pack(0)[0];
        var markers = far.Level.Rooms[5].Markers;
        var seen = new HashSet<int>();
        int dwellTicks = 0;
        for (int t = 0; t < 80; t++)
        {
            var before = far.Roster.Position(i);
            far.Tick();
            for (int m = 0; m < markers.Length; m++)
                if (far.Roster.Position(i) == markers[m].Position) seen.Add(m);
            if (far.Roster.Position(i) == before) dwellTicks++;
        }

        Assert.Equal(FarActivity.Patrol, far.Roster.Activity(i));
        Assert.Equal([0, 1], seen.Order());
        Assert.True(dwellTicks >= 2 * (int)(TheRules.Content.Horde.PatrolDwellSeconds * TheRules.Content.Horde.FarTickHz) - 2, $"waited {dwellTicks} ticks");
    }

    [Fact]
    public void A_slowed_far_robot_walks_at_its_slowed_speed()
    {
        var plain = new Far(PackAtTheEnd());
        var slowed = new Far(PackAtTheEnd());
        int i = plain.Pack(0)[0];
        foreach (var far in new[] { plain, slowed })
        {
            far.Call(1, Centre(1));
        }
        var start = slowed.Roster.Position(i);
        slowed.Roster.ApplyStatus(i, "slow", slowed.Now);
        plain.Tick();
        slowed.Tick();

        float full = plain.Roster.Position(i).DistanceTo(start);
        float slow = slowed.Roster.Position(i).DistanceTo(start);
        Assert.Equal((float)(full * (1 - TheRules.Statuses.Row("slow").SlowPct / 100)), slow, 0.01f);
        Assert.Equal((float)(plain.Roster.Stats(i).Speed / TheRules.Content.Horde.FarTickHz), full, 0.01f);
    }

    [Fact]
    public void A_stunned_far_robot_holds()
    {
        var far = new Far(PackAtTheEnd());
        far.Call(1, Centre(1));
        int i = far.Pack(0)[0];
        far.Roster.ApplyStatus(i, "stun", far.Now);
        var before = far.Roster.Position(i);

        far.Tick();

        Assert.Equal((before, FarActivity.Hold), (far.Roster.Position(i), far.Roster.Activity(i)));
    }

    [Fact]
    public void A_far_robot_burns_on_and_can_die_of_it()
    {
        var far = new Far(PackAtTheEnd());
        far.WakeAll();
        int i = far.Pack(0).First(r => far.Roster.Stats(r).Immunity != "fire");
        far.Roster.Hurt(i, far.Roster.Life(i) - 0.5);
        far.Roster.ApplyStatus(i, "burn", far.Now);

        for (int t = 0; t < 4; t++) far.Tick();

        Assert.Equal(RobotState.Dead, far.Roster.State(i));
    }

    //-------------------------------------------------------------------------
    // Allocation
    //-------------------------------------------------------------------------

    [Fact]
    public void A_thousand_steady_far_ticks_on_a_full_roster_allocate_nothing()
    {
        var far = new Far(Row(40, markers: 3, seed: 3, depth: 15), 4);
        Assert.InRange(far.Roster.Count, far.Roster.Capacity - 16, far.Roster.Capacity);
        far.WakeAll();
        for (int p = 0; p < far.Roster.PackCount; p += 3) far.Roster.AlertPack(p, 20, Centre(20), far.Now + 1_000_000, 0);   // converging for good
        far.Sight.SeeInto[21] = true;   // and queueing
        for (int t = 0; t < 50; t++) far.Tick();
        var before = RosterTests.Digest(far.Roster);

        // A steady allocation shows in every window; a one-off (the runtime re-jitting a hot method while the box is
        // loaded) shows in one: the quietest of three windows is what a tick costs.
        long allocated = long.MaxValue;
        for (int window = 0; window < 3 && allocated != 0; window++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 1000; t++) far.Tick();
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - bytes);
        }

        Assert.NotEqual(before, RosterTests.Digest(far.Roster));   // the ticks did work
        Assert.Equal(0, allocated);
    }
}
