using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>A 3x4 matrix, row-major, as <c>matrix3x4_t</c> (twelve floats).</summary>
[InlineArray(12)]
public struct Matrix3x4Raw
{
    private float _e0;
}

/// <summary>
/// <c>SourceSharpParticleFollow_t</c> (sourcesharp.h): an executor's local
/// space as native's FollowSpace read it (CLocalSpaceEmitter's attachment),
/// answered by IShimParticlesClient::ParticlesSetSpace. ParticleAbiParityTests
/// holds it against the header.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleFollow
{
    /// <summary>0: no follow ran, nothing below is set.</summary>
    public int Followed;

    public Matrix3x4Raw Matrix;

    public Vector Origin;

    /// <summary>The owner has a collideable: the box below was handed SetBBox( mins, maxs, true ).</summary>
    public int HasBox;

    public Vector Mins;

    public Vector Maxs;
}
