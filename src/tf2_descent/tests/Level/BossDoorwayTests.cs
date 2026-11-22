using System.Collections.Immutable;

using SourceSharp.Abi;

using TF2.Descent.Server.Level;

using Xunit;

namespace TF2.Descent.Tests.Level;

/// <summary>The admin menu's Go to the boss: the hero stands outside the boss arena's doorway, facing in.</summary>
public sealed class BossDoorwayTests
{
    private static LevelData Level(int? arena, params LevelDoor[] doors) =>
        new("tf2_descent-5-s1", 5, 1, "map name", [], [.. doors], [], [], null, arena, ImmutableDictionary<LevelPiece, PieceSource>.Empty, []);

    private static LevelDoor Door(int id, int a, int b, Vector at, Vector normal) => new(id, a, b, at, normal, 128f, 128f, "");

    [Fact]
    public void Into_an_arena_that_is_the_doors_second_room_the_hero_stands_back_against_the_normal_facing_along_it()
    {
        var spot = BossDoorway.Outside(Level(4, Door(0, 1, 2, default, new Vector(1, 0, 0)), Door(1, 3, 4, new Vector(100, 200, 16), new Vector(0, 1, 0))));

        Assert.NotNull(spot);
        Assert.Equal(new Vector(100, 200 - BossDoorway.StepBack, 16 + BossDoorway.Lift), spot!.Value.At);
        Assert.Equal(90f, spot.Value.Yaw, 3);
    }

    [Fact]
    public void Into_an_arena_that_is_the_doors_first_room_the_hero_stands_along_the_normal_facing_back()
    {
        var spot = BossDoorway.Outside(Level(3, Door(1, 3, 4, new Vector(100, 200, 16), new Vector(1, 0, 0))));

        Assert.Equal(new Vector(100 + BossDoorway.StepBack, 200, 16 + BossDoorway.Lift), spot!.Value.At);
        Assert.Equal(180f, MathF.Abs(spot.Value.Yaw), 3);
    }

    [Fact]
    public void A_level_with_no_arena_has_no_spot()
        => Assert.Null(BossDoorway.Outside(Level(null, Door(0, 1, 2, default, new Vector(1, 0, 0)))));

    [Fact]
    public void An_arena_with_no_door_has_no_spot()
        => Assert.Null(BossDoorway.Outside(Level(7, Door(0, 1, 2, default, new Vector(1, 0, 0)))));
}
