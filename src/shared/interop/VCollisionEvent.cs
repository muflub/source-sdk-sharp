using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// vphysics' <c>vcollisionevent_t</c> (vphysics_interface.h), as
/// IPhysicsCollisionEvent's Pre/PostCollision hand it. Read in place for the
/// length of the call: neither object, nor <see cref="InternalData"/>,
/// outlives it. Offsets pinned by VCollisionEventLayoutTests and, against the
/// header, by vtable_index_agreement_test's VCollisionEventLayout.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
public struct VCollisionEvent
{
    [FieldOffset(0)] public nint Object0;
    [FieldOffset(8)] public nint Object1;
    [FieldOffset(16)] public int SurfaceProps0;
    [FieldOffset(20)] public int SurfaceProps1;
    [FieldOffset(24)] public byte IsCollision;
    [FieldOffset(25)] public byte IsShadowCollision;
    [FieldOffset(28)] public float DeltaCollisionTime;

    /// <summary>Only valid at PostCollision.</summary>
    [FieldOffset(32)] public float CollisionSpeed;

    /// <summary>The event's IPhysicsCollisionData: contact point and surface normal.</summary>
    [FieldOffset(40)] public nint InternalData;

    public readonly nint Object(int side) => side == 0 ? Object0 : Object1;
}
