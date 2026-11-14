using TF2.Descent.Ai.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>The level as the horde sees it: room-graph distances, routes, doors and the walk a sound takes.</summary>
public sealed class HordeLevelTests
{
    [Fact]
    public void Room_distance_counts_the_doors_crossed()
    {
        var level = Row(5);

        Assert.Equal(0, level.Distance(2, 2));
        Assert.Equal(1, level.Distance(2, 3));
        Assert.Equal(4, level.Distance(0, 4));
        Assert.Equal(4, level.Distance(4, 0));
    }

    [Fact]
    public void A_room_no_door_reaches_is_unreachable()
    {
        var level = HordeLevel.Create(1, 1, [RowRoom(0, 1), RowRoom(1, 1), RowRoom(2, 1)], [RowDoor(0)]);

        Assert.Equal(HordeLevel.Unreachable, level.Distance(0, 2));
        Assert.Equal(-1, level.NextRoom(0, 2));
        Assert.True(float.IsPositiveInfinity(level.PathDistance(0, Centre(0), 2, Centre(2))));
    }

    [Fact]
    public void The_next_room_is_on_a_shortest_route_and_ties_go_to_the_lowest_id()
    {
        // A square: 0-1, 0-2, 1-3, 2-3. From 0 to 3 both 1 and 2 are on a shortest route.
        var rooms = Enumerable.Range(0, 4).Select(i => RowRoom(i, 0));
        var doors = new[]
        {
            new HordeDoor(0, 1, At(10, 0), At(1, 0, 0), 64), new HordeDoor(0, 2, At(20, 0), At(1, 0, 0), 64),
            new HordeDoor(1, 3, At(30, 0), At(1, 0, 0), 64), new HordeDoor(2, 3, At(40, 0), At(1, 0, 0), 64),
        };
        var level = HordeLevel.Create(1, 1, rooms, doors);

        Assert.Equal(1, level.NextRoom(0, 3));
        Assert.Equal(3, level.NextRoom(1, 3));
        Assert.Equal(-1, level.NextRoom(3, 3));
        Assert.Equal(2, level.DoorBetween(1, 3));
        Assert.Equal(-1, level.DoorBetween(0, 3));
    }

    [Fact]
    public void A_sound_walks_through_doors_not_walls()
    {
        // Rooms 0 and 2 share a wall; the only way between them is the long way round through 1.
        var rooms = new[]
        {
            new HordeRoom(0, At(0, 0), At(512, 512, 256), []),
            new HordeRoom(1, At(0, 512), At(1024, 4096, 256), []),
            new HordeRoom(2, At(512, 0), At(1024, 512, 256), []),
        };
        var doors = new[]
        {
            new HordeDoor(0, 1, At(0, 512), At(0, 1, 0), 64),
            new HordeDoor(1, 2, At(1024, 512), At(0, -1, 0), 64),
        };
        var level = HordeLevel.Create(1, 1, rooms, doors);
        var a = At(500, 256);
        var b = At(524, 256);

        float walked = level.PathDistance(0, a, 2, b);

        Assert.Equal(24, a.DistanceTo(b), 3);
        Assert.Equal(a.DistanceTo(At(0, 512)) + 1024 + At(1024, 512).DistanceTo(b), walked, 2);
    }

    [Fact]
    public void Inside_one_room_a_sound_walks_straight()
    {
        var level = Row(2);

        Assert.Equal(100, level.PathDistance(0, At(100, 100), 0, At(200, 100)), 3);
    }

    [Fact]
    public void A_room_whose_id_is_not_its_index_is_refused()
    {
        var e = Assert.Throws<ArgumentException>(() => HordeLevel.Create(1, 1, [RowRoom(1, 0)], []));

        Assert.Contains("id is its index", e.Message);
    }

    [Fact]
    public void A_door_to_itself_or_off_the_level_is_refused()
    {
        Assert.Throws<ArgumentException>(() => HordeLevel.Create(1, 1, [RowRoom(0, 0)], [new HordeDoor(0, 0, At(0, 0), At(1, 0, 0), 64)]));
        Assert.Throws<ArgumentException>(() => HordeLevel.Create(1, 1, [RowRoom(0, 0)], [new HordeDoor(0, 3, At(0, 0), At(1, 0, 0), 64)]));
    }

    [Fact]
    public void A_second_door_between_one_pair_is_refused()
    {
        Assert.Throws<ArgumentException>(() => HordeLevel.Create(1, 1, [RowRoom(0, 0), RowRoom(1, 0)], [RowDoor(0), RowDoor(0)]));
    }

    [Fact]
    public void A_boss_arena_off_the_level_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Row(2, boss: new HordeBoss("major_crits", 5, new PackMarker(At(0, 0), 0))));
    }

    [Fact]
    public void A_point_is_in_the_room_whose_bounds_hold_it()
    {
        var level = Row(3);

        Assert.Equal(1, level.RoomAt(Centre(1)));
        Assert.Equal(-1, level.RoomAt(At(-10, -10)));
    }

    [Fact]
    public void A_door_leads_into_the_other_room()
    {
        var door = RowDoor(0);

        Assert.Equal(1, door.Other(0));
        Assert.Equal(At(1, 0, 0), door.Into(0));
        Assert.Equal(At(-1, 0, 0), door.Into(1));
        Assert.Equal(new[] { 0 }, Row(2).DoorsOf(0).ToArray());
    }
}
