using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The six flames of a plasma fire, and the property that made them worth
/// deriving rather than drawing: they have to DIFFER.
///
/// <para>
/// This is the shape of the bug the side audit caught before it ran. The port
/// first asked the engine for six random yaws; <c>RandomFloat</c> is a
/// server-only ABI slot, so on the client every one of them would have come back
/// zero, all six flames would have stacked on one axis at one frame rate, and
/// the fire would have drawn as a single motionless sprite with nothing said.
/// Every test here would fail against that.
/// </para>
/// </summary>
public class PlasmaRingTests
{
    /// <summary>Entity indices a fire could plausibly land on.</summary>
    public static IEnumerable<object[]> Indices =>
        new[] { 1, 2, 3, 17, 64, 199, 512, 2047 }.Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Indices))]
    public void TheSixFlamesDoNotShareAnAngle(int index)
    {
        var yaws = Enumerable.Range(0, PlasmaRing.FlameCount)
                             .Select(i => PlasmaRing.Yaw(index, i))
                             .ToArray();

        for (int a = 0; a < yaws.Length; a++)
        {
            for (int b = a + 1; b < yaws.Length; b++)
            {
                Assert.True(MathF.Abs(yaws[a] - yaws[b]) > 0.001f,
                    $"entity {index}: flames {a} and {b} sit at different angles");
            }
        }
    }

    /// <summary>
    /// Not merely different, but SPREAD — which is the property stratifying the
    /// ring was introduced to guarantee. Six angles that differed by a degree
    /// would pass the test above and still draw as one sprite.
    ///
    /// <para>
    /// Measured as the mean resultant length of the six directions: 1 is six
    /// flames pointing exactly the same way and 0 is a ring in perfect balance.
    /// Stock's independent uniform draws reach 0.97 for some entity indices,
    /// which is what a sector apiece removes.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Indices))]
    public void TheRingIsSpreadAroundTheCircleRatherThanBunched(int index)
    {
        Assert.True(MeanResultant(index) < 0.5f,
            $"entity {index}: the six flames point six ways ({MeanResultant(index):F3} bunching)");
    }

    /// <summary>
    /// How nearly the six flames all point one way. 1 is exactly one direction.
    /// </summary>
    private static float MeanResultant(int index)
    {
        float x = 0f, y = 0f;

        for (int i = 0; i < PlasmaRing.FlameCount; i++)
        {
            float yaw = PlasmaRing.Yaw(index, i);
            x += MathF.Cos(yaw);
            y += MathF.Sin(yaw);
        }

        return MathF.Sqrt(x * x + y * y) / PlasmaRing.FlameCount;
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void EveryAngleIsAnAngle(int index)
    {
        for (int i = 0; i < PlasmaRing.FlameCount; i++)
        {
            float yaw = PlasmaRing.Yaw(index, i);

            Assert.True(float.IsFinite(yaw), $"entity {index} flame {i}: the angle is a number");
            Assert.True(yaw >= 0f && yaw < MathF.Tau, $"entity {index} flame {i}: the angle is once around the circle");
        }
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void EveryFlameAnimatesAndNoTwoAtTheSameRate(int index)
    {
        var rates = new List<float>();

        for (int i = 0; i < PlasmaRing.FlameCount; i++)
        {
            float rate = PlasmaRing.FrameRate(index, i);

            Assert.True(rate >= PlasmaRing.MinFrameRate && rate <= PlasmaRing.MaxFrameRate,
                $"entity {index} flame {i}: the frame rate is inside stock's fifteen-to-twenty");
            Assert.True(rate > 0f, $"entity {index} flame {i}: the flame actually animates");

            rates.Add(rate);
        }

        Assert.True(rates.Distinct().Count() == PlasmaRing.FlameCount,
            $"entity {index}: no two flames pulse together");
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void TheFlamesStartOnDifferentPicturesOfTheSprite(int index)
    {
        const int frames = 16;

        var starts = new List<float>();

        for (int i = 0; i < PlasmaRing.FlameCount; i++)
        {
            float f = PlasmaRing.StartFrame(index, i, frames);

            Assert.True(f >= 0f && f < frames, $"entity {index} flame {i}: starts on one of the sprite's frames");
            starts.Add(f);
        }

        Assert.True(starts.Distinct().Count() == PlasmaRing.FlameCount,
            $"entity {index}: no two flames start on the same picture");
    }

    /// <summary>
    /// Two fires in the same map must not be the same fire drawn twice.
    /// </summary>
    [Fact]
    public void TwoFiresGetDifferentRings()
    {
        int same = 0;

        for (int i = 0; i < PlasmaRing.FlameCount; i++)
        {
            if (MathF.Abs(PlasmaRing.Yaw(11, i) - PlasmaRing.Yaw(12, i)) < 0.001f)
                same++;
        }

        Assert.True(same == 0, "two fires at neighbouring entity indices lay their rings out differently");
    }

    /// <summary>
    /// The property that makes this better than stock rather than merely equal:
    /// a fire that left the view and came back is the same fire. Stock reruns
    /// <c>Start()</c> on re-entering the PVS and reshuffles.
    /// </summary>
    [Fact]
    public void ARingIsTheSameRingEveryTimeItIsAskedFor()
    {
        for (int i = 0; i < PlasmaRing.FlameCount; i++)
        {
            Assert.True(PlasmaRing.Yaw(42, i) == PlasmaRing.Yaw(42, i),
                $"flame {i}'s angle does not change between one ask and the next");
            Assert.True(PlasmaRing.FrameRate(42, i) == PlasmaRing.FrameRate(42, i),
                $"flame {i}'s frame rate does not change between one ask and the next");
        }
    }

    [Fact]
    public void ASpriteWithNoFramesStartsOnFrameZero()
    {
        for (int i = 0; i < PlasmaRing.FlameCount; i++)
        {
            Assert.True(PlasmaRing.StartFrame(7, i, frameCount: 0) == 0f,
                $"flame {i} of a sprite with no frames is on frame zero");
            Assert.True(PlasmaRing.StartFrame(7, i, frameCount: 1) == 0f,
                $"flame {i} of a one-frame sprite is on that frame");
        }
    }

    [Fact]
    public void TheCentreFlameIsFullSizeAndTheRestTaper()
    {
        Assert.True(PlasmaRing.SizeFraction(0) == 1f, "the centre flame is the fire's full size");

        for (int i = 1; i < PlasmaRing.FlameCount; i++)
        {
            Assert.True(PlasmaRing.SizeFraction(i) < PlasmaRing.SizeFraction(i - 1),
                $"flame {i} is smaller than the one before it");
            Assert.True(PlasmaRing.SizeFraction(i) > 0f,
                $"flame {i} still has a size — the taper never reaches nothing");
        }
    }

    /// <summary>
    /// The whole ring, over every entity index a map could produce, held to the
    /// one thing that must never be true: six flames that are one flame.
    /// </summary>
    [Fact]
    public void NoEntityIndexProducesACollapsedRing()
    {
        for (int index = 0; index < 4096; index++)
        {
            var yaws = new HashSet<float>();

            for (int i = 0; i < PlasmaRing.FlameCount; i++)
                yaws.Add(PlasmaRing.Yaw(index, i));

            Assert.True(yaws.Count == PlasmaRing.FlameCount,
                $"entity {index}: the ring has six distinct angles, not fewer");

            // And spread, over EVERY index rather than the sampled few. This is
            // the assertion stock could not have passed.
            Assert.True(MeanResultant(index) < 0.5f,
                $"entity {index}: the ring is spread rather than bunched");
        }
    }
}
