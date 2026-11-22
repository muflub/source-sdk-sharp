using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Server.Furniture;
using TF2.Descent.Server.Level;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// The furniture of a level as data and its pool (plan_descent_logic WO3, section 2): what stands
/// on each marker is the rules' roll from the level's seed, a sleeping room's furniture is data, the
/// pool is made once at level ready and nothing is made in play.
/// </summary>
public sealed class FurniturePlacementTests
{
    [Fact]
    public void The_rows_are_the_rules_roll_for_depth_1_seed_1()
    {
        var rig = new FurnitureRig(seed: 1);

        var expected = FurnitureRig.Rules.Furniture.Placement.Furnish(1, LevelAdapters.ToFurniture(FurnitureRig.D1S1)).Placed;

        Assert.NotEmpty(expected);
        Assert.Equal(expected, rig.Service.Rows.Select(r => r.Placed));
        Assert.Equal((1UL, 1), (rig.Service.Seed, rig.Service.Depth));
    }

    [Fact]
    public void Depth_1_seed_1_has_the_markers_the_level_line_counts_and_no_row_beyond_them()
    {
        var rig = new FurnitureRig(seed: 1);
        var level = FurnitureRig.D1S1;

        int[] markers = [.. new[] { LevelMarkerKind.Shrine, LevelMarkerKind.Container, LevelMarkerKind.Dispenser, LevelMarkerKind.Barrel, LevelMarkerKind.Trap }
            .Select(k => level.OfKind(k).Count())];
        int[] rows = [.. new[] { FurnitureKind.Shrine, FurnitureKind.Container, FurnitureKind.Dispenser, FurnitureKind.Explosive, FurnitureKind.Trap }
            .Select(k => rig.Service.Rows.Count(r => r.Kind == k))];

        Assert.Equal([2, 4, 1, 8, 2], markers);
        Assert.All(rows.Zip(markers), p => Assert.InRange(p.First, 0, p.Second));
        Assert.True(rows.Sum() > 0);
    }

    [Fact]
    public void Another_seed_furnishes_the_same_markers_differently()
    {
        var one = new FurnitureRig(seed: 1).Service.Rows.Select(r => (r.Placed.Marker, r.Variant)).ToList();
        var two = new FurnitureRig(seed: 2).Service.Rows.Select(r => (r.Placed.Marker, r.Variant)).ToList();

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void Every_row_stands_on_its_markers_place_facing_and_room()
    {
        var rig = new FurnitureRig();

        Assert.All(rig.Service.Rows, r =>
        {
            var marker = FurnitureRig.D1S1.Markers.Single(m => LevelAdapters.MarkerKey(m.Id) == r.Placed.Marker);
            Assert.Equal((marker.Position, marker.Yaw, marker.Room), (r.Position, r.Yaw, r.Room));
        });
    }

    [Fact]
    public void A_level_with_no_mode_has_no_furniture_and_makes_nothing()
    {
        var rig = new FurnitureRig(ready: false);
        rig.Mode.Active = false;

        rig.Service.OnLevelReady("tf2_descent-1-s1");

        Assert.Empty(rig.Service.Rows);
        Assert.Equal(0, rig.Spawner.Made);
    }

    //-------------------------------------------------------------------------
    // The pool
    //-------------------------------------------------------------------------

    [Fact]
    public void The_pool_is_one_entity_a_row_up_to_the_ceiling_made_at_level_ready()
    {
        var rig = new FurnitureRig();

        int rows = rig.Pooled.Count;
        Assert.Equal(Math.Min(FurnitureService.PoolCeiling, rows), rig.Service.Made);
        Assert.Equal(rig.Service.Made, rig.Spawner.Made);
        Assert.Equal(rig.Service.Made, rig.Service.PoolEdicts.After - rig.Service.PoolEdicts.Before);
        Assert.True(rig.Service.Made <= FurnitureService.PoolCeiling);
    }

    [Fact]
    public void Every_row_of_an_awake_room_stands_on_an_entity_in_its_look()
    {
        var rig = new FurnitureRig();

        Assert.All(rig.Pooled, r =>
        {
            var e = Assert.IsType<FurnitureEntity>(r.Entity);
            Assert.False(e.Parked);
            Assert.Equal(r.Index, e.Row);
            Assert.Equal(FurnitureCatalog.Number(r.KindId, r.Variant), e.State.Entry);
            Assert.Equal(FurnitureCatalog.Of(e.State.Entry)!.Model, e.Model);
        });
    }

    [Fact]
    public void Sleeping_and_waking_rooms_a_hundred_times_makes_nothing()
    {
        var rig = new FurnitureRig();
        int made = rig.Spawner.Made;
        var rooms = rig.Service.Rows.Select(r => r.Room).Distinct().ToList();

        for (int i = 0; i < 100; i++)
        {
            rig.Rooms.Asleep.UnionWith(rooms);
            rig.Service.CheckRooms();
            rig.Rooms.Asleep.Clear();
            rig.Service.CheckRooms();
        }

        Assert.Equal(made, rig.Spawner.Made);
        Assert.All(rig.Pooled, r => Assert.True(r.OnEntity));
    }

    [Fact]
    public void A_sleeping_rooms_furniture_is_data_and_its_entities_park_and_are_never_sent()
    {
        var rig = new FurnitureRig();
        int room = rig.Service.Rows[0].Room;
        var entities = rig.Pooled.Where(r => r.Room == room).Select(r => r.Entity!).ToList();
        var transmit = new FurnitureTransmit(rig.Parking);

        rig.Rooms.Asleep.Add(room);
        int folded = rig.Service.CheckRooms();

        Assert.Equal(entities.Count, folded);
        Assert.All(rig.Pooled.Where(r => r.Room == room), r => Assert.Null(r.Entity));
        Assert.All(entities, e => Assert.True(e.Parked));
        Assert.All(entities, e => Assert.Equal(TransmitAnswer.Never, transmit.ShouldTransmit(e.Handle, 1)));
        Assert.All(rig.Pooled.Where(r => r.Room != room), r => Assert.Equal(TransmitAnswer.Default, transmit.ShouldTransmit(r.Entity!.Handle, 1)));
    }

    [Fact]
    public void A_second_level_has_its_own_rolls_and_a_full_parked_pool()
    {
        var rig = new FurnitureRig(seed: 1);
        var first = rig.Service.Rows.Select(r => (r.Placed.Marker, r.Variant)).ToList();
        int madeFirst = rig.Spawner.Made;

        rig.NextLevel(seed: 3);

        var second = rig.Service.Rows.Select(r => (r.Placed.Marker, r.Variant)).ToList();
        Assert.NotEqual(first, second);
        Assert.Equal(Math.Min(FurnitureService.PoolCeiling, rig.Pooled.Count), rig.Service.Made);
        Assert.Equal(madeFirst + rig.Service.Made, rig.Spawner.Made);
        Assert.Equal(madeFirst + rig.Service.Made, rig.Service.MadeEver);
        Assert.Equal(3UL, rig.Service.Seed);
    }

    [Fact]
    public void A_level_with_more_rows_than_the_ceiling_keeps_the_rest_as_data()
    {
        var rig = new FurnitureRig(ready: false);
        // A level whose every marker is furniture: more rows than the pool's ceiling.
        var level = FurnitureRig.D1S1;
        var many = Enumerable.Range(0, 60).Select(i =>
            new LevelMarker(level.Markers.Length + i, "descent_mark_barrel", LevelMarkerKind.Barrel,
                level.Rooms[i % level.Rooms.Length].Centre, 0, i % level.Rooms.Length, ""));
        rig.Level.Current = level with { Markers = level.Markers.AddRange(many) };

        rig.LevelReady();

        Assert.True(rig.Service.Rows.Count > FurnitureService.PoolCeiling, $"only {rig.Service.Rows.Count} rows");
        Assert.Equal(FurnitureService.PoolCeiling, rig.Service.Made);
        Assert.Equal(FurnitureService.PoolCeiling, rig.Service.Rows.Count(r => r.OnEntity));
        Assert.Equal(0, rig.Service.Free);
    }
}
