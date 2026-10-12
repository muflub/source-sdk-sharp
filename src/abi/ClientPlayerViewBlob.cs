using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// What native still asks about the viewer — <c>SourceSharpClientPlayerView</c>.
///
/// <para>
/// The managed client player owns its own state; this is the small part of it
/// the client DLL cannot go without, published once a frame through
/// the <c>ClientPlayerPredictionDrive</c> slot. Fixed-width throughout,
/// mirroring the C declaration byte for byte.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ClientPlayerViewBlob
{
    public int StructSize;

    public float OriginX, OriginY, OriginZ;
    public float ViewOffsetX, ViewOffsetY, ViewOffsetZ;
    public float PunchX, PunchY, PunchZ;
    public float PunchVelX, PunchVelY, PunchVelZ;

    /// <summary>Whether the origin above is the replay's answer.</summary>
    public int Driving;

    /// <summary>What the HUD shows. One declaration, one writer.</summary>
    public int Health;
}
