using SourceSharp.Ai;
using TF2.Descent.Ai.Motion;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>Facing, the move blend and the torso dials (HO3).</summary>
public sealed class MotionPoseTests
{
    [Fact]
    public void A_turn_moves_at_most_the_step_the_short_way_round()
    {
        // From 170 to -170 is 20 degrees through 180, not 340 back through 0.
        Assert.Equal(-175f, MotionAngles.Turn(170f, -170f, 15f), 3);
    }

    [Fact]
    public void A_turn_within_the_step_lands_on_the_target()
    {
        Assert.Equal(30f, MotionAngles.Turn(20f, 30f, 15f), 3);
    }

    [Fact]
    public void Running_forward_at_full_speed_is_move_x_one()
    {
        var (x, y) = MotionPoses.Move(230f, 0f, facing: 0f, maxSpeed: 230f, stillSpeed: 1f);

        Assert.Equal(1f, x, 3);
        Assert.Equal(0f, y, 3);
    }

    [Fact]
    public void Moving_to_the_body_s_left_is_move_y_minus_one_in_its_own_frame()
    {
        // Facing +Y (90): a velocity along -X is to the body's left. Stock's dial is y = -sin(move yaw).
        var (x, y) = MotionPoses.Move(-230f, 0f, facing: 90f, maxSpeed: 230f, stillSpeed: 1f);

        Assert.Equal(0f, x, 3);
        Assert.Equal(-1f, y, 3);
    }

    [Fact]
    public void Half_the_speed_is_half_the_blend()
    {
        var (x, _) = MotionPoses.Move(115f, 0f, facing: 0f, maxSpeed: 230f, stillSpeed: 1f);

        Assert.Equal(0.5f, x, 3);
    }

    [Fact]
    public void A_still_body_has_no_move_blend()
    {
        Assert.Equal((0f, 0f), MotionPoses.Move(0.5f, 0f, 0f, 230f, 1f));
    }

    [Fact]
    public void The_torso_twists_against_the_look_s_yaw_and_stops_at_its_reach()
    {
        Assert.Equal(-30f, MotionPoses.Torso(30f, 0f, 0f, 45f, 45f).Yaw, 3);
        Assert.Equal(-45f, MotionPoses.Torso(90f, 0f, 0f, 45f, 45f).Yaw, 3);
    }

    [Fact]
    public void Looking_up_bends_the_torso_up()
    {
        float elevation = MotionAngles.ElevationOf(At(100, 0, 100));

        Assert.Equal(45f, MotionPoses.Torso(0f, elevation, 0f, 45f, 90f).Pitch, 3);
    }

    [Fact]
    public void The_gait_is_air_off_the_ground_else_run_above_the_speed_else_stand()
    {
        Assert.Equal(MotionGait.Air, MotionPoses.Gait(false, AiVector.Zero, 20f));
        Assert.Equal(MotionGait.Run, MotionPoses.Gait(true, At(21, 0), 20f));
        Assert.Equal(MotionGait.Stand, MotionPoses.Gait(true, At(19, 0), 20f));
    }
}
