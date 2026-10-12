using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// The 3D skybox as sky_camera describes it. Mirrors SourceSharpSkyboxParams.
///
/// <para>
/// Shaped like the engine's own <c>sky3dparams_t</c> rather than like something
/// convenient, because the destination is fixed: every field is copied into the
/// player's networked <c>m_skybox3d</c> and the client renders the miniature
/// world from it. A nicer shape here would only move the translation to a less
/// obvious place.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SkyboxParams
{
    /// <summary>Where in the world the miniature sits.</summary>
    public float OriginX, OriginY, OriginZ;

    /// <summary>How many times smaller the miniature is than the real thing.</summary>
    public int Scale;

    /// <summary>
    /// The visleaf area the camera is in. 255 is the reserved "there is no sky
    /// camera", which is what the native path writes when it finds none.
    /// </summary>
    public int Area;

    public int FogEnable;
    public int FogBlend;
    public int FogRadial;

    public float FogDirX, FogDirY, FogDirZ;

    /// <summary>Packed RGBA in the engine's color32 byte order.</summary>
    public uint FogColorPrimary;
    public uint FogColorSecondary;

    public float FogStart;
    public float FogEnd;
    public float FogMaxDensity;
}
