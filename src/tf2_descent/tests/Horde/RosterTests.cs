using System.Security.Cryptography;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The roster roll (plan_descent_logic 3.3, 3.10): once at level load, a pure function of
/// (level seed, depth, party size, rooms and markers), one pack per room with markers, robots
/// at the markers in turn, the boss first, and whole packs dropped, loudly, past capacity.
/// </summary>
public sealed class RosterTests
{
    public const double TicksPerSecond = 66;

    internal static RobotRoster Rolled(HordeLevel level, int party = 1)
    {
        var roster = RobotRoster.Create(TheRules, TicksPerSecond);
        roster.Load(level, party);
        return roster;
    }

    internal static string Digest(RobotRoster roster)
    {
        using var bytes = new MemoryStream();
        using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
            roster.WriteState(w);
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    [Fact]
    public void The_same_level_rolls_the_same_bytes()
    {
        Assert.Equal(Digest(Rolled(Row(8, seed: 41, depth: 6), 2)), Digest(Rolled(Row(8, seed: 41, depth: 6), 2)));
    }

    [Fact]
    public void Another_seed_rolls_other_bytes()
    {
        Assert.NotEqual(Digest(Rolled(Row(8, seed: 41, depth: 6), 2)), Digest(Rolled(Row(8, seed: 42, depth: 6), 2)));
    }

    [Fact]
    public void Each_room_with_markers_holds_the_pack_the_rules_roll_for_it()
    {
        // The kit's 208-unit doors, so every robot of the depth fits and leaves (HO9's fit refuses nothing).
        var level = HordeLevel.Create(9, 5, [RowRoom(0, 2), RowRoom(1, 0), RowRoom(2, 3)], [RowDoor(0) with { Width = 208 }, RowDoor(1) with { Width = 208 }]);
        var roster = Rolled(level, 3);

        var p0 = TheRules.Packs.RollPack(9, 0, 5, 3, 2);
        var p2 = TheRules.Packs.RollPack(9, 2, 5, 3, 3);
        Assert.Equal(2, roster.PackCount);
        Assert.Equal(p0.Robots.Length + p2.Robots.Length, roster.Count);
        Assert.Equal((0, 0, p0.Robots.Length), (roster.PackRoom(0), roster.PackFirst(0), roster.PackSize(0)));
        Assert.Equal((2, p0.Robots.Length, p2.Robots.Length), (roster.PackRoom(1), roster.PackFirst(1), roster.PackSize(1)));
        for (int k = 0; k < p2.Robots.Length; k++)
        {
            int i = p0.Robots.Length + k;
            var r = p2.Robots[k];
            Assert.Equal((r.Template, r.Scale, r.Stats.Life, r.Temperament.Style), (roster.Template(i), roster.Scale(i), roster.Life(i), roster.Temperament(i).Style));
            Assert.Equal(r.Affixes, roster.Affixes(i));
            Assert.Equal(roster.Post(i), roster.Position(i));   // each starts at its own post round its marker (HO9)
            Assert.Equal(level.Rooms[2].Markers[r.Marker].Position.Z, roster.Post(i).Z);
            Assert.Equal(r.Index % 3, roster.HomeMarker(i));
        }
    }

    [Fact]
    public void A_rolled_robot_starts_asleep_whole_and_unbodied()
    {
        var roster = Rolled(Row(3));

        Assert.True(roster.Count > 0);
        for (int i = 0; i < roster.Count; i++)
        {
            Assert.Equal(RobotState.Asleep, roster.State(i));
            Assert.Equal(roster.Stats(i).Life, roster.Life(i));
            Assert.Empty(roster.Statuses(i).Active);
            Assert.Equal((0, -1), (roster.Body(i), roster.Clip(i)));
            Assert.Equal(CarriedMemory.None, roster.Carried(i));
            Assert.Equal(roster.HomeRoom(i), roster.Room(i));
        }
    }

    [Fact]
    public void The_boss_is_the_first_row_and_its_own_pack()
    {
        var boss = new HordeBoss("major_crits", 2, new PackMarker(At(1300, 200), 45));
        var roster = Rolled(Row(3, boss: boss));

        Assert.True(roster.IsBoss(0));
        Assert.Equal(("major_crits", 2, At(1300, 200), 45f), (roster.Template(0), roster.Room(0), roster.Position(0), roster.Yaw(0)));
        Assert.Equal((2, 0, 1), (roster.PackRoom(0), roster.PackFirst(0), roster.PackSize(0)));
        Assert.All(Enumerable.Range(1, roster.Count - 1), i => Assert.False(roster.IsBoss(i)));
    }

    [Fact]
    public void A_boss_that_is_no_boss_template_is_refused()
    {
        var roster = RobotRoster.Create(TheRules, TicksPerSecond);

        Assert.Throws<ArgumentException>(() => roster.Load(Row(2, boss: new HordeBoss("scout", 0, new PackMarker(At(0, 0), 0))), 1));
    }

    [Fact]
    public void Past_capacity_whole_packs_are_dropped_and_named()
    {
        // Depth 15, a party of four: packs of 11 to 16, so 40 rooms overflow 256.
        var level = Row(40, markers: 3, seed: 3, depth: 15);
        var roster = RobotRoster.Create(TheRules, TicksPerSecond);

        var roll = roster.Load(level, 4);

        // The rule, replayed: rooms in order, a pack taken whole if it fits, else dropped.
        int kept = 0;
        var expectDropped = new List<DroppedPack>();
        foreach (var room in level.Rooms)
        {
            int size = TheRules.Packs.RollPack(3, room.Id, 15, 4, 3).Robots.Length;
            if (kept + size > roster.Capacity) expectDropped.Add(new DroppedPack(room.Id, size));
            else kept += size;
        }
        Assert.NotEmpty(expectDropped);
        Assert.Equal(expectDropped, roll.Dropped);
        Assert.Equal(kept, roster.Count);
        Assert.InRange(roster.Count, 1, roster.Capacity);
        Assert.Contains("DROPPED", roll.Summary);
        Assert.Contains($"room {expectDropped[0].Room} ({expectDropped[0].Robots} robots)", roll.Summary);
    }

    [Fact]
    public void A_roll_inside_capacity_drops_nothing()
    {
        var roll = RobotRoster.Create(TheRules, TicksPerSecond).Load(Row(5), 1);

        Assert.Empty(roll.Dropped);
        Assert.DoesNotContain("DROPPED", roll.Summary);
    }

    [Fact]
    public void The_roster_holds_the_horde_tables_capacity()
    {
        Assert.Equal(TheRules.Content.Horde.RosterCapacity, RobotRoster.Create(TheRules, TicksPerSecond).Capacity);
    }

    [Fact]
    public void A_reload_moves_every_serial_on_so_old_ids_go_stale()
    {
        var roster = Rolled(Row(3));
        var before = roster.Id(0);

        roster.Load(Row(3), 1);

        Assert.True(roster.IsLive(roster.Id(0)));
        Assert.False(roster.IsLive(before));
        Assert.Equal(before.Serial + 1, roster.Id(0).Serial);
    }
}
