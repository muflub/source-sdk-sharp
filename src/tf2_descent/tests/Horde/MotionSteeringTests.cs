using SourceSharp.Ai;
using TF2.Descent.Ai.Motion;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>An order's move as a wish (HO3): corners in order, the kinds, the speed share.</summary>
public sealed class MotionSteeringTests
{
    private const float Arrive = 16f;
    private const float WalkShare = 0.5f;

    private static MoveIntent Follow(params AiVector[] corners) => new(
        MoveKind.Follow, MoveSpeed.Run, (byte)corners.Length,
        corners.Length > 0 ? corners[0] : default, corners.Length > 1 ? corners[1] : default,
        corners.Length > 2 ? corners[2] : default, corners.Length > 3 ? corners[3] : default,
        default, false, 0);

    [Fact]
    public void Follow_wishes_towards_the_next_corner_on_the_floor_plane()
    {
        var move = Follow(At(100, 0, 50));
        int corner = 0;

        var wish = MotionSteering.Wish(in move, ref corner, At(0, 0), Arrive, WalkShare);

        Assert.Equal(At(1, 0, 0), wish.Direction);
        Assert.Equal(1f, wish.SpeedShare);
        Assert.Equal(0, corner);
    }

    [Fact]
    public void A_corner_within_the_arrive_radius_is_consumed_and_the_next_one_steers()
    {
        var move = Follow(At(100, 0), At(100, 100));
        int corner = 0;

        var wish = MotionSteering.Wish(in move, ref corner, At(100, -10), Arrive, WalkShare);

        Assert.Equal(1, corner);
        Assert.Equal(At(0, 1, 0), wish.Direction);
    }

    [Fact]
    public void Past_the_last_corner_the_body_has_arrived_and_wishes_nothing()
    {
        var move = Follow(At(100, 0), At(100, 100));
        int corner = 1;

        var wish = MotionSteering.Wish(in move, ref corner, At(100, 95), Arrive, WalkShare);

        Assert.True(wish.Arrived);
        Assert.False(wish.Wishing);
        Assert.Equal(2, corner);
    }

    [Fact]
    public void Corners_are_consumed_in_order_never_skipping_one_not_yet_reached()
    {
        // Standing on corner 2 while corner 1 is far: corner 1 is next, not 3.
        var move = Follow(At(0, 0), At(500, 0), At(10, 0), At(600, 0));
        int corner = 0;

        MotionSteering.Wish(in move, ref corner, At(5, 0), Arrive, WalkShare);

        Assert.Equal(1, corner);
    }

    [Theory]
    [InlineData(MoveKind.Hold)]
    [InlineData(MoveKind.Pause)]
    public void Hold_and_pause_wish_nothing(MoveKind kind)
    {
        var move = Follow(At(100, 0)) with { Kind = kind };
        int corner = 0;

        var wish = MotionSteering.Wish(in move, ref corner, At(0, 0), Arrive, WalkShare);

        Assert.Equal(MotionWish.None, wish);
    }

    [Fact]
    public void Strafe_goes_along_its_direction_not_its_corners()
    {
        var move = Follow(At(100, 0)) with { Kind = MoveKind.Strafe, Direction = At(0, -3, 7) };
        int corner = 0;

        var wish = MotionSteering.Wish(in move, ref corner, At(0, 0), Arrive, WalkShare);

        Assert.Equal(At(0, -1, 0), wish.Direction);
    }

    [Fact]
    public void Walk_wishes_the_walk_share_of_the_speed()
    {
        var move = Follow(At(100, 0)) with { Speed = MoveSpeed.Walk };
        int corner = 0;

        var wish = MotionSteering.Wish(in move, ref corner, At(0, 0), Arrive, WalkShare);

        Assert.Equal(WalkShare, wish.SpeedShare);
    }

    [Fact]
    public void Crouch_jump_presses_jump_and_crouch_while_it_follows()
    {
        var move = Follow(At(100, 0)) with { Kind = MoveKind.CrouchJump };
        int corner = 0;

        var wish = MotionSteering.Wish(in move, ref corner, At(0, 0), Arrive, WalkShare);

        Assert.True(wish.Jump);
        Assert.True(wish.Crouch);
        Assert.Equal(At(1, 0, 0), wish.Direction);
    }

    [Fact]
    public void Heading_for_the_last_corner_the_wish_says_how_far_it_is()
    {
        var move = Follow(At(100, 0), At(100, 300));
        int corner = 0;

        Assert.False(MotionSteering.Wish(in move, ref corner, At(0, 0), Arrive, WalkShare).ToLast);
        corner = 1;
        Assert.Equal(200f, MotionSteering.Wish(in move, ref corner, At(100, 100), Arrive, WalkShare).LastDistance, 3);
    }

    [Fact]
    public void Arriving_holds_the_speed_to_what_friction_stops_within_the_distance_left()
    {
        Assert.Equal(230f, MotionSteering.Arrive(230f, 500f, 4f, 40f));
        Assert.Equal(120f, MotionSteering.Arrive(230f, 30f, 4f, 40f));
        Assert.Equal(40f, MotionSteering.Arrive(230f, 2f, 4f, 40f));
    }

    [Fact]
    public void A_hull_widens_the_arrive_radius_by_its_share()
    {
        var s = MotionSettings.Default;

        Assert.Equal(s.ArriveRadius + s.ArriveHullShare * 42f, MotionSteering.ArriveRadius(s, 42f));
    }
}
