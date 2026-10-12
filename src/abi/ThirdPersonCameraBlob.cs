using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// Managed functions handed back to native. Mirrors SourceSharpManagedApi.
///
/// IMPORTANT: every pointer stored here must belong to a NON-collectible
/// assembly (SourceSharp.Bootstrap), never to a game assembly in a collectible
/// ALC. Native caches these for the process lifetime; a pointer into an
/// unloaded ALC is a dangling call. See plan.md Part 5, rule 1.
/// </summary>

/// <summary>
/// Where the third-person camera sits — <c>SourceSharpThirdPersonCamera</c>,
/// mirrored byte for byte.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ThirdPersonCameraBlob
{
    public uint StructSize;

    /// <summary>0 places the camera, 1 resolves it against the sweep.</summary>
    public int Phase;

    public Vector ViewAngles;
    public float IdealPitch;
    public float IdealYaw;
    public float IdealDist;
    public float IdealDistRight;
    public float IdealDistUp;

    public Vector CameraAngles;
    public Vector Wish;
    public float WishLength;

    public float TraceFraction;

    public float SurvivingFraction;

    /// <summary>Whether a camera placed with that much of the wish sees the player.</summary>
    public int SeesOwner;
}
