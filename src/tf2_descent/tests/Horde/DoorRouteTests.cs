using SourceSharp.Ai;
using TF2.Descent.Ai.Motion;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>The dev drive's route through the room graph's doors (HO3).</summary>
public sealed class DoorRouteTests
{
    [Fact]
    public void Two_rooms_away_the_route_crosses_both_doors_square_on_then_reaches_the_target()
    {
        var level = Row(3);
        var corners = new List<AiVector>();

        DoorRoute.Build(level, Centre(0), Centre(2), corners);

        float d = DoorRoute.DoorApproach;
        Assert.Equal(
            [At(512 - d, 256), At(512 + d, 256), At(1024 - d, 256), At(1024 + d, 256), Centre(2)],
            corners);
    }

    [Fact]
    public void Back_the_other_way_each_door_is_crossed_along_its_reversed_normal()
    {
        var level = Row(2);
        var corners = new List<AiVector>();

        DoorRoute.Build(level, Centre(1), Centre(0), corners);

        float d = DoorRoute.DoorApproach;
        Assert.Equal([At(512 + d, 256), At(512 - d, 256), Centre(0)], corners);
    }

    [Fact]
    public void In_one_room_the_route_is_the_target_alone()
    {
        var corners = new List<AiVector>();

        DoorRoute.Build(Row(3), At(100, 100), At(300, 400), corners);

        Assert.Equal([At(300, 400)], corners);
    }
}
