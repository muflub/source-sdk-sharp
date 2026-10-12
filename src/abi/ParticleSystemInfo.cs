using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One <c>CNewParticleEffect</c> the client started, as
/// <c>/client/{slot}/particles</c> reads it. Mirrors
/// <c>SourceSharpParticleSystemInfo_t</c> (sourcesharp.h);
/// ParticleAbiParityTests holds the two together. The system's name crosses
/// beside it, in the slot's own buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleSystemInfo
{
    /// <summary>1: a managed entity's, kept at its owner; 2: an impact's one-shot at a world point.</summary>
    public int Kind;

    /// <summary>The owner's handle; <c>0xFFFFFFFF</c> (EntityHandle.None) for a one-shot.</summary>
    public uint Owner;

    /// <summary>The control points kept at the owner, as a bitmask; 0 when nothing follows.</summary>
    public int FollowMask;

    /// <summary>1 when its owner let it go and it is burning out.</summary>
    public int Stopped;

    /// <summary>Seconds of client time since it started.</summary>
    public float Age;

    /// <summary>Active particles, summed over its children.</summary>
    public int Particles;

    /// <summary>Control point 0, where the system is.</summary>
    public Vector ControlPoint0;

    /// <summary>Control point 1 (burning_character's model point; the world origin when nobody feeds it).</summary>
    public Vector ControlPoint1;

    /// <summary>The sort origin the client leaf system sorts and culls it by.</summary>
    public Vector SortOrigin;
}
