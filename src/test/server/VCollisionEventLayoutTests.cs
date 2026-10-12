using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed view of <c>vcollisionevent_t</c>, one fact per field. The same
/// numbers are pinned against vphysics_interface.h by
/// vtable_index_agreement_test's VCollisionEventLayout.
/// </summary>
public class VCollisionEventLayoutTests
{
    [Fact]
    public void Object0IsAtZero() => Assert.Equal(0, (int)Marshal.OffsetOf<VCollisionEvent>(nameof(VCollisionEvent.Object0)));

    [Fact]
    public void Object1IsAtEight() => Assert.Equal(8, (int)Marshal.OffsetOf<VCollisionEvent>(nameof(VCollisionEvent.Object1)));

    [Fact]
    public void SurfacePropsAreAtSixteen() => Assert.Equal(16, (int)Marshal.OffsetOf<VCollisionEvent>(nameof(VCollisionEvent.SurfaceProps0)));

    [Fact]
    public void IsCollisionIsAtTwentyFour() => Assert.Equal(24, (int)Marshal.OffsetOf<VCollisionEvent>(nameof(VCollisionEvent.IsCollision)));

    [Fact]
    public void IsShadowCollisionIsAtTwentyFive() => Assert.Equal(25, (int)Marshal.OffsetOf<VCollisionEvent>(nameof(VCollisionEvent.IsShadowCollision)));

    [Fact]
    public void DeltaCollisionTimeIsAtTwentyEight() => Assert.Equal(28, (int)Marshal.OffsetOf<VCollisionEvent>(nameof(VCollisionEvent.DeltaCollisionTime)));

    [Fact]
    public void CollisionSpeedIsAtThirtyTwo() => Assert.Equal(32, (int)Marshal.OffsetOf<VCollisionEvent>(nameof(VCollisionEvent.CollisionSpeed)));

    [Fact]
    public void InternalDataIsAtForty() => Assert.Equal(40, (int)Marshal.OffsetOf<VCollisionEvent>(nameof(VCollisionEvent.InternalData)));

    [Fact]
    public void SizeIsFortyEight() => Assert.Equal(48, Marshal.SizeOf<VCollisionEvent>());
}
