using SourceSharp.Ai;
using TF2.Descent.Ai.Doors;
using TF2.Descent.Ai.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The door shove's rules (plan_descent_logic L9, 3.9, HO11): "a hero in a doorway is pushed out
/// along the door's normal; a hero beside it is not". The door: x = 512, normal +x, 64 wide.
/// </summary>
public sealed class DoorShoveRulesTests
{
    private const double Tps = 66;
    private static readonly DoorShoveRules Rules = new(TheRules.Content.Horde, Tps);
    private static readonly HordeDoor Door = RowDoor(0);   // (512, 256), normal +x, 64 wide

    private static AiVector[] Through => [At(560, 256), At(800, 256)];

    [Fact]
    public void A_hero_in_the_doorway_is_inside_it()
    {
        Assert.True(Rules.InDoorway(Door, At(512, 256)));
        Assert.True(Rules.InDoorway(Door, At(512 + 39, 256 + 31)));
    }

    [Fact]
    public void A_hero_beside_the_door_is_not()
    {
        Assert.False(Rules.InDoorway(Door, At(512, 256 + 33)));
        Assert.False(Rules.InDoorway(Door, At(500, 256 - 80)));
    }

    [Fact]
    public void A_hero_on_open_floor_before_the_door_is_not()
    {
        Assert.False(Rules.InDoorway(Door, At(512 - 41, 256)));
        Assert.False(Rules.InDoorway(Door, At(300, 256)));
    }

    [Fact]
    public void A_hero_on_the_floor_above_or_below_the_door_is_not()
    {
        Assert.False(Rules.InDoorway(Door, At(512, 256, -40)));
        Assert.True(Rules.InDoorway(Door, At(512, 256, -10)));
    }

    [Fact]
    public void A_robot_whose_path_runs_through_the_opening_crosses_it()
    {
        Assert.True(Rules.PathCrosses(Door, At(440, 256), Through));
        Assert.True(Rules.PathCrosses(Door, At(440, 230), [At(464, 256), At(560, 256)]));
    }

    [Fact]
    public void A_robot_making_for_the_hero_who_holds_the_doorway_goes_into_it()
    {
        // A hunter's path ends at its target: the hero standing in the door.
        Assert.True(Rules.PathCrosses(Door, At(420, 256), [At(500, 256)]));
        Assert.False(Rules.PathCrosses(Door, At(420, 256), [At(460, 256)]));   // short of the doorway's depth
    }

    [Fact]
    public void A_robot_lays_hands_on_a_hero_within_its_reach_hull_to_hull()
    {
        Assert.True(DoorShoveRules.InContact(At(400, 256), 24, 48, At(496, 256), 24));
        Assert.False(DoorShoveRules.InContact(At(400, 256), 24, 48, At(497, 256), 24));
        Assert.False(DoorShoveRules.InContact(At(400, 256), 24, 48, At(450, 256, 200), 24));   // a floor above
    }

    [Fact]
    public void The_door_held_is_the_one_the_hero_stands_in_within_reach_of_the_robot()
    {
        var level = Row(3);

        Assert.Equal(0, Rules.DoorHeld(level, At(430, 256), At(512, 256)));
        Assert.Equal(-1, Rules.DoorHeld(level, At(430, 256), At(512, 256 + 60)));   // beside it
        Assert.Equal(-1, Rules.DoorHeld(level, At(100, 256), At(512, 256)));        // past the reach of its plane
    }

    [Fact]
    public void A_robot_whose_path_stays_on_its_side_or_crosses_the_wall_beside_the_door_does_not()
    {
        Assert.False(Rules.PathCrosses(Door, At(440, 256), [At(440, 400)]));
        Assert.False(Rules.PathCrosses(Door, At(440, 100), [At(600, 100)]));   // through the wall, not the opening
    }

    [Fact]
    public void A_robot_far_from_the_door_does_not_cross_it_yet()
    {
        Assert.False(Rules.PathCrosses(Door, At(512 - 200, 256), Through));
    }

    [Fact]
    public void The_door_found_is_the_one_the_hero_stands_in_on_the_robots_way()
    {
        var level = Row(3);

        Assert.Equal(0, Rules.DoorFor(level, At(440, 256), Through, At(512, 256)));
        Assert.Equal(-1, Rules.DoorFor(level, At(440, 256), Through, At(512, 256 + 80)));   // beside it
        Assert.Equal(-1, Rules.DoorFor(level, At(440, 256), [At(440, 400)], At(512, 256)));  // not its way
    }

    [Fact]
    public void The_shove_runs_along_the_normal_away_from_the_robot_either_way_through()
    {
        var push = Rules.Impulse(Door, At(470, 256), At(512, 256), 1.0, 0);
        var back = Rules.Impulse(Door, At(560, 256), At(512, 256), 1.0, 0);

        var s = TheRules.Content.Horde.Shove;
        Assert.Equal(new AiVector((float)s.Speed, 0, (float)s.Lift), push);
        Assert.Equal(new AiVector(-(float)s.Speed, 0, (float)s.Lift), back);
    }

    [Fact]
    public void The_shove_grows_with_the_robots_scale_and_falls_with_the_heros_resistance()
    {
        float Speed(double scale, double resist) => Rules.Impulse(Door, At(470, 256), At(512, 256), scale, resist).X;

        double s = TheRules.Content.Horde.Shove.Speed;
        Assert.Equal(s * 1.75, Speed(1.75, 0), 2);
        Assert.Equal(s * 0.6, Speed(0.6, 0), 2);
        Assert.Equal(s * 0.5, Speed(1.0, 0.5), 2);
        Assert.Equal(0, Speed(1.0, 1.5), 2);
        Assert.Equal(s * 2, Speed(1.0, -2), 2);
    }

    [Fact]
    public void A_low_discipline_robot_shoves_at_once_and_a_drilled_one_after_the_dwell()
    {
        var s = TheRules.Content.Horde.Shove;
        Assert.Equal(0, Rules.DwellTicks(s.LowDiscipline));
        Assert.Equal((long)Math.Round(s.DwellSeconds * Tps), Rules.DwellTicks(s.LowDiscipline + 1));
        Assert.Equal(33, Rules.DwellTicks(10));
        Assert.Equal(66, Rules.CooldownTicks);
    }
}
