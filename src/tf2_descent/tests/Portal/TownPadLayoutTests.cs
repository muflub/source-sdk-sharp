using SourceSharp.Abi;

using TF2.Descent.Server.Portal;
using Xunit;

namespace TF2.Descent.Tests.Portal;

/// <summary>Where the town's exit pads stand (<see cref="TownPadLayout"/>): a row that never overlaps, beside the arrival spawns.</summary>
public sealed class TownPadLayoutTests
{
    [Fact]
    public void One_to_eight_pads_in_a_row_never_overlap()
    {
        var first = new Vector(500f, 100f, 16f);
        for (int count = 1; count <= 8; count++)
        {
            var spots = Enumerable.Range(0, count).Select(i => TownPadLayout.Spot(first, 30f, i)).ToList();
            for (int a = 0; a < count; a++)
                for (int b = a + 1; b < count; b++)
                {
                    float dx = spots[a].X - spots[b].X, dy = spots[a].Y - spots[b].Y;
                    Assert.True(MathF.Sqrt(dx * dx + dy * dy) >= 2f * PortalPlacement.Half * MathF.Sqrt(2f),
                                $"{count} pads: pad {a} and pad {b} overlap");
                }
        }
    }

    [Fact]
    public void The_first_pad_stands_at_the_rows_start()
        => Assert.Equal(new Vector(500f, 100f, 16f), TownPadLayout.Spot(new Vector(500f, 100f, 16f), 30f, 0));

    [Fact]
    public void Beside_the_arrivals_the_row_stands_ahead_of_them_and_faces_back_at_them()
    {
        var (first, yaw) = TownPadLayout.BesideArrivals(new Vector(980f, -16f, 16f), 180f);

        Assert.Equal(980f - TownPadLayout.FromArrivals, first.X, 3);
        Assert.Equal(-16f, first.Y, 3);
        Assert.Equal(0f, yaw);
    }
}
