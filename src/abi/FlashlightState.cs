using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One projected-texture flashlight's state. Mirrors SourceSharpFlashlightState
/// (sourcesharp.h) word for word: the fields of FlashlightState_t a flashlight
/// sets, which fwd_shadows.cpp's executor copies into a default-constructed
/// one for the engine (the managed shadow manager's flashlight records). The whole state per call, as
/// <see cref="DynamicLight"/> is: it is rewritten every frame the light is on.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashlightState
{
    public float OriginX, OriginY, OriginZ;

    /// <summary>m_quatOrientation.</summary>
    public float OrientationX, OrientationY, OrientationZ, OrientationW;

    public float NearZ;
    public float FarZ;
    public float HorizontalFovDegrees;
    public float VerticalFovDegrees;
    public float QuadraticAtten;
    public float LinearAtten;
    public float ConstantAtten;

    /// <summary>m_Color: r, g, b, and the ambient term in the fourth.</summary>
    public float ColorR, ColorG, ColorB, ColorA;

    /// <summary>The id FlashlightTexture answered; -1 for no texture.</summary>
    public int Texture;
    public int TextureFrame;
    public int EnableShadows;
    public float ShadowMapResolution;
    public float ShadowAtten;
    public float ShadowSlopeScaleDepthBias;
    public float ShadowDepthBias;
}
