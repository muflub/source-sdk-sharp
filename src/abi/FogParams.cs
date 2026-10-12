using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// The world's fog, as the master <c>env_fog_controller</c> describes it.
/// Mirrors <c>SourceSharpFogParams</c>.
///
/// <para>
/// IT GOES THE OTHER WAY FROM <see cref="SkyboxParams"/>. That one is C# handing
/// the SERVER what <c>sky_camera</c> said; this is C# handing the CLIENT what the
/// fog controller said, so that <c>SourceSharp_ViewerFogParams</c> has something
/// to give <c>viewrender.cpp</c>. Before step 8e-render it had nothing:
/// <c>fogcontroller.cpp</c> is entirely <c>#ifndef SOURCESHARP</c>, so
/// <c>server.so</c> declares no <c>DT_FogController</c> and there was no sender
/// at all — world fog rendered off on every frame since the flip, and step 7c
/// made that a printed ledger row rather than a silent NULL.
/// </para>
///
/// <para>
/// NO LERP FIELDS. The engine's <c>fogparams_t</c> carries
/// <c>startLerpTo</c>/<c>endLerpTo</c>/<c>lerptime</c> so the client can run a
/// fog transition; <c>EnvFogController.OnThink</c> already runs that lerp on the
/// server and publishes the resolved numbers, and two implementations of one
/// transition is worse than one.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FogParams
{
    /// <summary>Whether there is fog at all, and how it blends.</summary>
    public int Enable;
    public int Blend;
    public int Radial;

    /// <summary>Where the secondary colour comes from, for a blended fog.</summary>
    public float DirPrimaryX, DirPrimaryY, DirPrimaryZ;

    /// <summary>Packed RGBA in the engine's color32 byte order.</summary>
    public uint ColorPrimary;
    public uint ColorSecondary;

    /// <summary>Where the fog begins and ends.</summary>
    public float Start;
    public float End;

    /// <summary>How far the far plane is pushed. Zero or less is "the mapper set none".</summary>
    public float FarZ;

    /// <summary>The densest the fog gets.</summary>
    public float MaxDensity;
}
