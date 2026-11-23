using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>Floor-plan rectangles (<see cref="Rects"/>): what is left of one when others are taken out.</summary>
public sealed class DescentOutlineTests
{
    private static float Area(IEnumerable<Rect> rs) => rs.Sum(r => r.Width * r.Depth);

    [Fact]
    public void A_hole_in_the_middle_leaves_the_rectangles_area_less_the_holes()
    {
        var left = Rects.Subtract(new Rect(0, 0, 100, 100), [new Rect(40, 40, 60, 60)]);

        Assert.Equal(100 * 100 - 20 * 20, Area(left));
    }

    [Fact]
    public void What_is_left_never_overlaps_a_hole_or_itself()
    {
        Rect[] holes = [new(10, 0, 30, 50), new(20, 40, 90, 60), new(70, 70, 100, 100)];
        var left = Rects.Subtract(new Rect(0, 0, 100, 100), holes);

        static bool Overlap(Rect a, Rect b) => a.X0 < b.X1 && b.X0 < a.X1 && a.Y0 < b.Y1 && b.Y0 < a.Y1;
        Assert.All(left, r => Assert.DoesNotContain(holes, h => Overlap(r, h)));
        for (int i = 0; i < left.Count; i++)
            for (int j = i + 1; j < left.Count; j++)
                Assert.False(Overlap(left[i], left[j]), $"{left[i]} overlaps {left[j]}");
        Assert.Equal(100 * 100 - Area(holes) + 10 * 10, Area(left));
    }

    [Fact]
    public void Strips_of_one_width_merge_into_one_rectangle()
    {
        var left = Rects.Subtract(new Rect(0, 0, 100, 100), [new Rect(50, 0, 100, 100)]);

        Assert.Equal([new Rect(0, 0, 50, 100)], left);
    }

    [Fact]
    public void A_rectangle_inside_the_union_is_covered_and_one_reaching_out_is_not()
    {
        Rect[] union = [new(0, 0, 50, 100), new(50, 40, 100, 60)];

        Assert.True(Rects.Covers(union, new Rect(30, 45, 90, 55)));
        Assert.False(Rects.Covers(union, new Rect(30, 35, 90, 55)));
    }
}
