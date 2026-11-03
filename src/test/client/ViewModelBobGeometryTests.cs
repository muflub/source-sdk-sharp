using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed half of stock's <c>AddViewModelBobHelper</c>
/// (tf_weaponbase.cpp:4801-4822): a bob pair and an eye pose give stock's
/// viewmodel origin and angles, the forward and right of the eye's angles
/// taken before any mutation.
/// </summary>
public class ViewModelBobGeometryTests
{
    [Fact]
    public void The_eye_pose_bobs_as_stock_factors_it()
    {
        // Eye at (100, 200, 300), pitch 10 yaw 0 roll 5; vertical 2, lateral 1.
        Span<float> origin = [100f, 200f, 300f];
        Span<float> angles = [10f, 0f, 5f];
        ReadOnlySpan<float> eyeAngles = angles;
        ReadOnlySpan<float> bob = [2f, 1f];

        RenderPlanHost.AddViewModelBob(bob, eyeAngles, origin, angles);

        // forward (0.9848078, 0, -0.1736482); right (-0.0151345, -0.9961947,
        // -0.0858317), the engine's roll-carrying right. Origin += forward*0.8
        // + right*0.2, z += 0.2; pitch -0.8, yaw -0.3, roll +1.0.
        Assert.Equal(100.7848194f, origin[0], 5);
        Assert.Equal(199.8007611f, origin[1], 5);
        Assert.Equal(300.0439150f, origin[2], 5);
        Assert.Equal(9.2f, angles[0], 5);    // PITCH
        Assert.Equal(-0.3f, angles[1], 5);   // YAW
        Assert.Equal(6.0f, angles[2], 5);    // ROLL
    }

    [Fact]
    public void A_zero_pair_is_the_eye_unchanged()
    {
        Span<float> origin = [1f, 2f, 3f];
        Span<float> angles = [10f, 20f, 30f];

        RenderPlanHost.AddViewModelBob([0f, 0f], angles, origin, angles);

        Assert.Equal(1f, origin[0]);
        Assert.Equal(2f, origin[1]);
        Assert.Equal(3f, origin[2]);
        Assert.Equal(10f, angles[0]);
        Assert.Equal(20f, angles[1]);
        Assert.Equal(30f, angles[2]);
    }

    [Fact]
    public void The_right_vector_is_the_eye_roll_not_the_result_roll()
    {
        // Roll 45's right (0, -0.7071068, -0.7071068) carries the lateral; the
        // vertical's +0.5 roll lands after, never back into the vectors.
        Span<float> origin = [0f, 0f, 0f];
        Span<float> angles = [0f, 0f, 45f];

        RenderPlanHost.AddViewModelBob([1f, 4f], angles, origin, angles);

        Assert.Equal(0.4f, origin[0], 5);
        Assert.Equal(-0.5656854f, origin[1], 5);
        Assert.Equal(-0.4656854f, origin[2], 5);
        Assert.Equal(-0.4f, angles[0], 5);
        Assert.Equal(-1.2f, angles[1], 5);
        Assert.Equal(45.5f, angles[2], 5);
    }
}
