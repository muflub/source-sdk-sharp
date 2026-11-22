using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// The room-name codec (decision E8): a layout room's doors, height class and
/// jump flags ride in its name, one spelling per set of facts.
/// </summary>
public sealed class RoomNameTests
{
    [Fact]
    public void A_name_spells_its_family_variant_doors_height_and_flags()
    {
        var shape = RoomName.Parse("gallery_b-nesw-h-j");

        Assert.Equal(new RoomShape("gallery", "b", Doors.All, HeightClass.Hall, JumpFlags.Jump), shape);
    }

    [Fact]
    public void A_name_without_flags_has_none()
    {
        var shape = RoomName.Parse("arrival_cage-e-c");

        Assert.Equal(new RoomShape("arrival", "cage", Doors.East, HeightClass.Chamber, JumpFlags.None), shape);
    }

    [Fact]
    public void Several_flags_are_joined_by_dots()
    {
        var shape = RoomName.Parse("stope_a2-ew-h-j.s3.b1.b2");

        Assert.Equal(JumpFlags.Jump | JumpFlags.S3 | JumpFlags.B1 | JumpFlags.B2, shape.Jumps);
        Assert.Equal(1, shape.STierSpots);
        Assert.Equal(2, shape.BTierSpots);
        Assert.True(shape.IsJumpRoom);
    }

    [Fact]
    public void A_quiet_room_carries_q_after_its_jump_flags()
    {
        var shape = RoomName.Parse("drift_aq-ew-l-q");
        var jumper = RoomName.Parse("tee_aq-new-c-j.s2.q");

        Assert.True(shape.Quiet);
        Assert.Equal(JumpFlags.None, shape.Jumps);
        Assert.True(jumper.Quiet);
        Assert.Equal(JumpFlags.Jump | JumpFlags.S2, jumper.Jumps);
        Assert.Equal("tee_aq-new-c-j.s2.q", RoomName.Format(jumper));
    }

    [Fact]
    public void A_room_without_q_is_not_quiet()
    {
        Assert.False(RoomName.Parse("drift_a-ew-l").Quiet);
        Assert.False(RoomName.Parse("gallery_b-nesw-h-j").Quiet);
    }

    [Theory]
    [InlineData("drift_a-ew-l-q.j")]
    [InlineData("drift_a-ew-l-q.q")]
    public void A_q_before_a_jump_flag_or_twice_is_refused(string name)
    {
        Assert.False(RoomName.TryParse(name, out _, out var error));
        Assert.Contains("without repeats", error);
    }

    [Fact]
    public void Every_set_of_facts_formats_to_a_name_that_parses_back_to_it()
    {
        var checkedCount = 0;
        for (var doors = 1; doors <= 15; doors++)
            for (var height = 0; height < 4; height++)
                for (var flags = 0; flags < 128; flags++)
                {
                    var shape = new RoomShape("cavern", "x7", (Doors)doors, (HeightClass)height, (JumpFlags)(flags & 63)) { Quiet = flags >= 64 };
                    var name = RoomName.Format(shape);

                    Assert.Equal(shape, RoomName.Parse(name));
                    Assert.Equal(name, RoomName.Format(RoomName.Parse(name)));
                    checkedCount++;
                }

        Assert.Equal(15 * 4 * 128, checkedCount);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("drift_a-ew", "not 3 to 5")]
    [InlineData("drift_a-ew-l-j-x", "only a footprint")]
    [InlineData("drift-ew-l", "exactly one '_'")]
    [InlineData("wide_drift_a-ew-l", "exactly one '_'")]
    [InlineData("Drift_a-ew-l", "family 'Drift'")]
    [InlineData("2drift_a-ew-l", "family '2drift'")]
    [InlineData("drift_-ew-l", "variant ''")]
    [InlineData("drift_a-we-l", "not in the order n, e, s, w")]
    [InlineData("drift_a-ee-l", "not in the order n, e, s, w")]
    [InlineData("drift_a-ex-l", "door 'x'")]
    [InlineData("drift_a--l", "doors are empty")]
    [InlineData("drift_a-ew-x", "height 'x'")]
    [InlineData("drift_a-ew-lc", "height 'lc'")]
    [InlineData("drift_a-ew-l-b4", "flag 'b4'")]
    [InlineData("drift_a-ew-l-s2.j", "not in the order j, s2")]
    [InlineData("drift_a-ew-l-j.j", "not in the order j, s2")]
    [InlineData("drift_a-ew-l-", "flag ''")]
    public void A_malformed_name_is_refused_naming_the_fault(string name, string fault)
    {
        Assert.False(RoomName.TryParse(name, out var shape, out var error));
        Assert.Null(shape);
        Assert.Contains(fault, error);
        Assert.Contains($"room name '{name}'", error);

        var thrown = Assert.Throws<FormatException>(() => RoomName.Parse(name));
        Assert.Equal(error, thrown.Message);
    }

    [Fact]
    public void A_shape_no_name_can_hold_is_refused_by_format()
    {
        Assert.Throws<ArgumentException>(() => RoomName.Format(new RoomShape("drift", "a", Doors.None, HeightClass.Low, JumpFlags.None)));
        Assert.Throws<ArgumentException>(() => RoomName.Format(new RoomShape("wide_drift", "a", Doors.East, HeightClass.Low, JumpFlags.None)));
        Assert.Throws<ArgumentException>(() => RoomName.Format(new RoomShape("drift", "a-b", Doors.East, HeightClass.Low, JumpFlags.None)));
    }

    [Fact]
    public void A_quarter_turn_counter_clockwise_takes_east_to_north()
    {
        Assert.Equal(Doors.North, DoorSets.Rotate(Doors.East, 90));
        Assert.Equal(Doors.West, DoorSets.Rotate(Doors.North, 90));
        Assert.Equal(Doors.South, DoorSets.Rotate(Doors.West, 90));
        Assert.Equal(Doors.East, DoorSets.Rotate(Doors.South, 90));
    }

    [Fact]
    public void Turns_compose_and_a_full_turn_is_none()
    {
        for (var doors = 0; doors <= 15; doors++)
        {
            var d = (Doors)doors;
            Assert.Equal(d, DoorSets.Rotate(d, 360));
            Assert.Equal(d, DoorSets.Rotate(d, 0));
            Assert.Equal(DoorSets.Rotate(DoorSets.Rotate(DoorSets.Rotate(d, 90), 90), 90), DoorSets.Rotate(d, 270));
            Assert.Equal(DoorSets.Rotate(d, 270), DoorSets.Rotate(d, -90));
            Assert.Equal(DoorSets.Count(d), DoorSets.Count(DoorSets.Rotate(d, 90)));
        }
    }

    [Fact]
    public void A_turn_that_is_not_a_quarter_turn_is_refused()
    {
        Assert.Throws<ArgumentException>(() => DoorSets.Rotate(Doors.East, 45));
    }
}
