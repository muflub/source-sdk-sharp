using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed half of stock's min-mode term (tf_viewmodel.cpp:203-221): the
/// seam's x y z along the eye's forward, right and up, added to the viewmodel's
/// origin; a declined seam is no term at all.
/// </summary>
public class ViewModelOffsetGeometryTests
{
    private sealed class Term(float x, float y, float z) : IViewModelOffset
    {
        public bool Offset(Span<float> offset)
        {
            offset[0] = x;
            offset[1] = y;
            offset[2] = z;
            return true;
        }
    }

    private sealed class Declined : IViewModelOffset
    {
        public bool Offset(Span<float> offset) => false;
    }

    [Fact]
    public void A_declined_seam_is_no_term()
    {
        Span<float> origin = [1f, 2f, 3f];
        Span<float> angles = [10f, 20f, 30f];

        RenderPlanHost.AddViewModelOffset(new Declined(), angles, origin);

        Assert.Equal(1f, origin[0]);
        Assert.Equal(2f, origin[1]);
        Assert.Equal(3f, origin[2]);
    }

    [Fact]
    public void The_term_rides_the_eye_axes()
    {
        // Eye yaw 0: forward +x, right -y, up +z; the term (2, 3, 4).
        Span<float> origin = [100f, 200f, 300f];
        Span<float> angles = [0f, 0f, 0f];

        RenderPlanHost.AddViewModelOffset(new Term(2f, 3f, 4f), angles, origin);

        Assert.Equal(102f, origin[0], 5);
        Assert.Equal(197f, origin[1], 5);
        Assert.Equal(304f, origin[2], 5);
    }

    [Fact]
    public void The_forward_turns_with_the_yaw()
    {
        // Yaw 90's forward is +y: the x term lands on y alone.
        Span<float> origin = [0f, 0f, 0f];
        Span<float> angles = [0f, 90f, 0f];

        RenderPlanHost.AddViewModelOffset(new Term(2f, 0f, 0f), angles, origin);

        Assert.Equal(0f, origin[0], 5);
        Assert.Equal(2f, origin[1], 5);
        Assert.Equal(0f, origin[2], 5);
    }
}
