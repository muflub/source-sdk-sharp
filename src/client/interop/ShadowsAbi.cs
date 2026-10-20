using System.Runtime.InteropServices;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client shadow manager's contract with native code (C7 cluster 12, S1
/// of docs/plans/c7_shadow_manager.md): the flag words both sides read, and
/// the layouts of the engine's answers S2's binding copies out of the engine
/// (<c>ShadowInfo_t</c>, <c>Frustum_t</c>). ShadowManagerOracleTests holds
/// each layout to what the oracle measured of the real struct (the golden's
/// L rows). The seams over these are in SourceSharp.Client.Interface
/// (ShadowSeams.cs); nothing native implements them yet.
/// </summary>
public static class ShadowFlags
{
    // engine/ishadowmgr.h's ShadowFlags_t.
    public const int Flashlight = 1 << 0;
    public const int Shadow = 1 << 1;
    public const int ProjectedTextureTypeMask = Flashlight | Shadow;

    // toolframework/itoolentity.h's ClientShadowFlags_t.
    public const int UseRenderToTexture = 1 << 2;
    public const int AnimatingSource = 1 << 3;
    public const int UseDepthTexture = 1 << 4;

    // clientshadowmgr.cpp's own, past CLIENT_SHADOW_FLAGS_LAST_FLAG.
    public const int TextureDirty = 1 << 5;
    public const int BrushModel = 1 << 6;
    public const int UsingLodShadow = 1 << 7;
    public const int LightWorld = 1 << 8;

    // engine/ishadowmgr.h's ShadowCreateFlags_t.
    public const int CreateCacheVerts = 1 << 0;
    public const int CreateFlashlight = 1 << 1;

    /// <summary>CLIENTSHADOW_INVALID_HANDLE, SHADOW_HANDLE_INVALID, TextureHandle_t's and the fragments' invalid.</summary>
    public const int InvalidHandle = 0xFFFF;

    /// <summary>MODEL_INSTANCE_INVALID.</summary>
    public const int InvalidModelInstance = 0xFFFF;
}

/// <summary>iclientrenderable.h's ShadowType_t.</summary>
public static class ShadowTypes
{
    public const int None = 0;
    public const int Simple = 1;
    public const int RenderToTexture = 2;
    public const int RenderToTextureDynamic = 3;
    public const int RenderToDepthTexture = 4;
}

/// <summary>iclientshadowmgr.h's ShadowReceiver_t.</summary>
public static class ShadowReceivers
{
    public const int BrushModel = 0;
    public const int StaticProp = 1;
    public const int StudioModel = 2;
}

/// <summary>The engine's <c>ShadowInfo_t</c>, byte for byte (engine/ishadowmgr.h).</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ShadowInfoRaw
{
    public fixed float WorldToShadow[16];
    public float FalloffOffset;
    public float MaxDist;
    public float FalloffAmount;
    public float TexOriginX;
    public float TexOriginY;
    public float TexSizeX;
    public float TexSizeY;
    public byte FalloffBias;
}

/// <summary>A <c>cplane_t</c>, byte for byte (mathlib.h).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PlaneRaw
{
    public float NormalX;
    public float NormalY;
    public float NormalZ;
    public float Dist;
    public byte Type;
    public byte SignBits;
    public byte Pad0;
    public byte Pad1;
}

/// <summary>A <c>Frustum_t</c>, byte for byte: six planes, then their absolute normals (mathlib.h).</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FrustumRaw
{
    public PlaneRaw Right;
    public PlaneRaw Left;
    public PlaneRaw Top;
    public PlaneRaw Bottom;
    public PlaneRaw Near;
    public PlaneRaw Far;
    public fixed float AbsNormals[18];
}
