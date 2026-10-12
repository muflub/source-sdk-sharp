//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Beams;

/// <summary>The <c>TE_BEAM*</c> kinds a beam draws as (tempentity.h).</summary>
public enum BeamType
{
    Points = 0,
    Sprite = 1,
    Disk = 2,
    Cylinder = 3,
    Follow = 4,
    Ring = 5,
    Spline = 6,
    RingPoint = 7,
    Laser = 8,
    Tesla = 9,
}

/// <summary>The <c>FBEAM_*</c> flags (beam_flags.h).</summary>
[Flags]
public enum BeamFlags
{
    None = 0,
    StartEntity = 0x00000001,
    EndEntity = 0x00000002,
    FadeIn = 0x00000004,
    FadeOut = 0x00000008,
    SineNoise = 0x00000010,
    Solid = 0x00000020,
    ShadeIn = 0x00000040,
    ShadeOut = 0x00000080,
    OnlyNoiseOnce = 0x00000100,
    NoTile = 0x00000200,
    UseHitboxes = 0x00000400,
    StartVisible = 0x00000800,
    EndVisible = 0x00001000,
    IsActive = 0x00002000,
    Forever = 0x00004000,
    HaloBeam = 0x00008000,
    Reversed = 0x00010000,
}

/// <summary>One node of a follow beam's trail (<c>BeamTrail_t</c>).</summary>
public sealed class BeamTrail
{
    public BeamTrail? Next;
    public float Die;
    public Vector Org;
    public Vector Vel;
}

/// <summary>
/// A beam's whole state (<c>Beam_t</c>, beamdraw.h), as the beam system keeps
/// it between frames. The renderable it is drawn through is a native handle.
/// </summary>
public sealed class Beam
{
    /// <summary><c>MAX_BEAM_ENTS</c>.</summary>
    public const int MaxEnts = 10;

    /// <summary><c>NOISE_DIVISIONS</c>.</summary>
    public const int NoiseDivisions = 128;

    public BeamType Type;
    public BeamFlags Flags;
    public int NumAttachments;
    public readonly Vector[] Attachment = new Vector[MaxEnts];
    public Vector Delta;
    public float T;

    /// <summary>
    /// Whether an update has written <see cref="T"/>. Stock's <c>t</c> is never
    /// initialised: a beam whose updates all return early (a ring point whose
    /// entity endpoint never resolves) carries whatever the allocator left.
    /// </summary>
    public bool TWritten;
    public float Freq;
    public float Die;
    public float Width;
    public float EndWidth;
    public float FadeLength;
    public float Amplitude;
    public float Life;
    public float R, G, B;
    public float Brightness;
    public float Speed;
    public float FrameRate;
    public float Frame;
    public int Segments;
    public readonly EntityHandle[] Entity = new EntityHandle[MaxEnts];
    public readonly int[] AttachmentIndex = new int[MaxEnts];
    public int ModelIndex;
    public int HaloIndex;
    public float HaloScale;
    public int FrameCount;
    public readonly float[] Noise = new float[NoiseDivisions + 1];
    public BeamTrail? Trail;
    public float StartRadius;
    public float EndRadius;
    public bool CalculatedNoise;
    public float HdrColorScale = 1.0f;
    public float HaloProxySize;
    public Vector Mins;
    public Vector Maxs;

    /// <summary>The native renderable this beam is drawn through, or -1.</summary>
    public int Renderable = -1;

    public Beam()
    {
        for (int i = 0; i < MaxEnts; i++)
            Entity[i] = EntityHandle.None;
        Reset();
    }

    /// <summary><c>Beam_t::Reset</c>: the members a recycled beam starts from.</summary>
    public void Reset()
    {
        Mins = default;
        Maxs = default;
        Type = 0;
        Flags = 0;
        Trail = null;
        Renderable = -1;
        CalculatedNoise = false;
        HdrColorScale = 1.0f;
        TWritten = false;
    }

    /// <summary><c>Beam_t::GetRenderOrigin</c>: a ring's centre, otherwise its start.</summary>
    public Vector RenderOrigin
    {
        get
        {
            if (Type == BeamType.Ring || Type == BeamType.RingPoint)
            {
                // VectorMA( attachment[0], 0.5f, delta, org ).
                Vector a = Attachment[0];
                return new Vector(a.X + Delta.X * 0.5f, a.Y + Delta.Y * 0.5f, a.Z + Delta.Z * 0.5f);
            }

            return Attachment[0];
        }
    }

    /// <summary><c>Beam_t::ComputeBounds</c>: the box relative to the render origin.</summary>
    public void ComputeBounds()
    {
        switch (Type)
        {
            case BeamType.Spline:
                Mins = default;
                Maxs = default;
                for (int i = 1; i < NumAttachments; i++)
                {
                    Vector d = Attachment[i] - Attachment[0];
                    Mins = Min(Mins, d);
                    Maxs = Max(Maxs, d);
                }
                break;

            case BeamType.Disk:
            case BeamType.Cylinder:
            {
                int radius = (int)Delta.Z;
                Mins = new Vector(-radius, -radius, -radius);
                Maxs = new Vector(radius, radius, radius);
                break;
            }

            case BeamType.Ring:
            case BeamType.RingPoint:
            {
                int radius = (int)(Delta.Length * 0.5f);
                Mins = new Vector(-radius, -radius, -radius);
                Maxs = new Vector(radius, radius, radius);
                break;
            }

            default:
                Mins = new Vector(Delta.X > 0.0f ? 0.0f : Delta.X, Delta.Y > 0.0f ? 0.0f : Delta.Y,
                                  Delta.Z > 0.0f ? 0.0f : Delta.Z);
                Maxs = new Vector(Delta.X > 0.0f ? Delta.X : 0.0f, Delta.Y > 0.0f ? Delta.Y : 0.0f,
                                  Delta.Z > 0.0f ? Delta.Z : 0.0f);
                break;
        }

        Vector org = RenderOrigin;
        for (BeamTrail? t = Trail; t is not null; t = t.Next)
        {
            Vector d = t.Org - org;
            Mins = Min(Mins, d);
            Maxs = Max(Maxs, d);
        }
    }

    // Vector::Min / Vector::Max: MIN and MAX, per component.
    private static Vector Min(in Vector a, in Vector b)
        => new(a.X < b.X ? a.X : b.X, a.Y < b.Y ? a.Y : b.Y, a.Z < b.Z ? a.Z : b.Z);

    private static Vector Max(in Vector a, in Vector b)
        => new(a.X > b.X ? a.X : b.X, a.Y > b.Y ? a.Y : b.Y, a.Z > b.Z ? a.Z : b.Z);
}

/// <summary>
/// A creation request (<c>BeamInfo_t</c>), with the endpoint entities as
/// handles. The defaults are the struct's constructor's.
/// </summary>
public struct BeamInfo
{
    public BeamType Type;
    public EntityHandle StartEnt;
    public int StartAttachment;
    public EntityHandle EndEnt;
    public int EndAttachment;
    public Vector Start;
    public Vector End;
    public int ModelIndex;
    public string? ModelName;
    public int HaloIndex;
    public string? HaloName;
    public float HaloScale;
    public float Life;
    public float Width;
    public float EndWidth;
    public float FadeLength;
    public float Amplitude;
    public float Brightness;
    public float Speed;
    public int StartFrame;
    public float FrameRate;
    public float Red, Green, Blue;
    public bool Renderable;
    public int Segments;
    public BeamFlags Flags;
    public Vector Center;
    public float StartRadius;
    public float EndRadius;

    public BeamInfo()
    {
        Type = BeamType.Points;
        StartEnt = EntityHandle.None;
        EndEnt = EntityHandle.None;
        Segments = -1;
        ModelIndex = -1;
        HaloIndex = -1;
        Renderable = true;
    }
}
