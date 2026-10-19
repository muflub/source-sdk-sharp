using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// A managed particle sim's Update, in and out (plan_client_novalve C2, lane
/// c2-sims): the frame's <see cref="Dt"/>, <see cref="WasDrawnPrevFrame"/>
/// and the owner's pose in; the bounds stock would have handed <c>SetBBox</c>
/// out. Managed only since C7 particles phase 2.3 (ClientParticleManager
/// fills it); it crossed as <c>SourceSharpParticleEmit_t</c> before.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleEmit
{
    public float Dt;
    public int WasDrawnPrevFrame;
    public Vector Origin;
    public Vector Angles;
    public float CurTime;
    public float FrameTime;
    public int AlwaysSimulate;
    public int HasBounds;
    public Vector Mins;
    public Vector Maxs;
    public Vector ViewOrigin;
}

/// <summary>
/// <c>SourceSharpParticleQuad_t</c>: one particle's quad numbers for the
/// native draw executor, keyed by the id native pairs with its particle.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleQuadRaw
{
    public int Id;
    public int Kind;
    public Vector Position;
    public Vector Color;
    public float Alpha;
    public float Size;
    public float Roll;
    public Vector Normal;
    public float FadeNear;
    public float FadeFar;
    public int FixedSort;
    public float SortKey;
    public Vector Corner2;
    public Vector Corner3;
}

/// <summary>
/// <c>SourceSharpWorldTextView_t</c> (sourcesharp.h): a point_worldtext draw's
/// in, as the native text renderer hands it: the place, the view's basis and
/// the clock.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct WorldTextView
{
    public Vector Origin;
    public Vector Angles;
    public Vector Forward;
    public Vector Right;
    public Vector Up;
    public float CurTime;
}

/// <summary><c>SourceSharpWorldTextVertex_t</c>: one built vertex; the colour's r is the low byte.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct WorldTextVertexRaw
{
    public Vector Position;
    public float S;
    public float T;
    public uint Color;
}

/// <summary><c>SourceSharpParticleFrameHeader_t</c>: IManagedParticlesClient::Frame's buffer opens with it.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleFrameHeader
{
    public float Dt;
    public float CurTime;
    public float FrameTime;
    public float AabbBuffer;
    public Vector ViewOrigin;
}

/// <summary><c>SourceSharpParticleFrameIn_t</c>: one live executor, as native read it for the frame.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleFrameIn
{
    public uint Owner;
    public int Channel;
    public Vector Origin;
    public Vector Angles;
    public int MaterialFlags;
    public int Followed;
    public Matrix3x4Raw Matrix;
    public Vector FollowOrigin;
    public int HasBox;
    public Vector Mins;
    public Vector Maxs;
}

/// <summary><c>SourceSharpParticleCommit_t</c>: one binding's state after the frame's update, for native's renderable.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleCommit
{
    public uint Owner;
    public int Channel;
    public Vector Min;
    public Vector Max;
    public int Flags;
    public int Active;
    public int Changed;
}

/// <summary><c>SourceSharpParticleDrawIn_t</c>: what native read for one DrawModel.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleDrawIn
{
    public uint Owner;
    public int Channel;
    public int Flags;
    public int ClientModeDraws;
    public int DrawParticles;
    public int Wireframe;
    public int ShowBBox;
    public int ShowBBoxCost;
    public int ViewKey;
    public float FrameTime;
}

/// <summary><c>SourceSharpParticleDrawOut_t</c>: DrawBegin's buffer opens with it; the quads follow, one per listed particle.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleDrawOut
{
    public int Result;
    public float FrameTime;
    public int RefractUpdate;
    public int ScreenUpdate;
    public int Wireframe;
    public int BBox;
    public int BBoxR;
    public int BBoxG;
    public int BBoxB;
    public int Active;
    public Vector Min;
    public Vector Max;
}

/// <summary>A row-major 4x4 (VMatrix's m[4][4]).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Matrix4x4Raw
{
    public float M00, M01, M02, M03, M10, M11, M12, M13, M20, M21, M22, M23, M30, M31, M32, M33;
}

/// <summary><c>SourceSharpNewParticleFrameHeader_t</c>: NewEffectsFrame's buffer opens with it (C7 particles phase 3).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NewParticleFrameHeader
{
    public float Dt;
    public float Timescale;
    public float CurTime;
    public int FrameCount;
    public float AabbBuffer;
    public float RetireCost;
    public int SpikeThresholdMs;
    public int ViewX;
    public int ViewY;
    public int ViewWidth;
    public int ViewHeight;
    public float ViewFov;
    public Vector ViewOrigin;
    public Matrix4x4Raw WorldToScreen;
}

/// <summary><c>SourceSharpNewParticleIn_t</c>'s Flags.</summary>
[Flags]
public enum NewParticleInFlags
{
    None = 0,
    Released = 1 << 0,
    Allocated = 1 << 1,
    RenderHandle = 1 << 2,
    ViewModel = 1 << 3,
    HasReplacement = 1 << 4,
    Added = 1 << 5,
}

/// <summary><c>SourceSharpNewParticleIn_t</c>: one live effect, as native read it for the frame.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NewParticleIn
{
    public uint Id;
    public NewParticleInFlags Flags;
    public ulong Collection;
    public ulong Definition;
    public float NextSleepTime;
    public int Active;
    public int HasMoved;
    public int Finished;
    public Vector SortOrigin;
    public Vector Min;
    public Vector Max;
    public float CullRadius;
    public float CullFillCost;
    public Vector CullPoint;
}

/// <summary><c>SourceSharpNewParticleCommit_t</c>'s Actions.</summary>
[Flags]
public enum NewParticleActions
{
    None = 0,
    Changed = 1 << 0,
    Remove = 1 << 1,
    ViewGroup = 1 << 2,
    Replace = 1 << 3,
}

/// <summary><c>SourceSharpNewParticleCommit_t</c>: what native does to one effect after the frame.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NewParticleCommit
{
    public uint Id;
    public NewParticleActions Actions;
}

/// <summary><c>SourceSharpNewParticleDrawIn_t</c>: what native read for one CNewParticleEffect::DrawModel.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NewParticleDrawInRaw
{
    public uint Id;
    public int Flags;
    public int DrawParticles;
    public int ClientModeDraws;
    public int RenderSystems;
    public int ShowBBox;
    public int ShowBBoxCost;
    public int Active;
    public Vector Camera;
    public Vector Min;
    public Vector Max;
    public float MaxDrawDistance;
    public float NoDrawTimeToGoToSleep;
    public float LastSimulationTime;
    public float NextSleepTime;
    public int SkipRenderControlPoint;
    public int HighestControlPoint;
    public int ThirdPerson;
    public int ObserverMode;
    public int Pad;
    public ulong CameraObject;
    public ulong SkipObject;
    public ulong LocalPlayer;
    public ulong ObserverTarget;
}

/// <summary><c>SourceSharpNewParticleDrawOut_t</c>: the draw's answer.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NewParticleDrawOutRaw
{
    public int Action;
    public int SetSleep;
    public float NextSleepTime;
    public int BBox;
    public int BBoxR;
    public int BBoxG;
    public int BBoxB;
    public int Stats;
}

/// <summary><c>SSParticleSimEntry_t</c> (iparticlelib.h): one collection's step in the library's batch.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleSimEntry
{
    public ulong Collection;
    public float Dt;
    public int BoundsOnly;
}

/// <summary><c>SSParticleSimResult_t</c> (iparticlelib.h): what the batch answers for one collection.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleSimResult
{
    public int Finished;
    public int ActiveParticles;
    public float NextSleepTime;
    public Vector Min;
    public Vector Max;
}
