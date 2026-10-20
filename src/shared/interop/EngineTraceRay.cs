//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// <c>Ray_t</c> (public/cmodel.h) as <c>IEngineTrace::TraceRay</c> reads it
/// (plan_server_native_v2 T4f): four <c>VectorAligned</c> and two bools, 80
/// bytes. The rows are trace_layout.h's, and TraceLayoutParityTests holds this
/// struct to them. The engine loads the four vectors with aligned SIMD loads, so
/// the address it is handed must be 16-aligned: <see cref="EngineTrace"/>
/// copies the ray into an aligned buffer before the call.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = SizeOf)]
public struct EngineRay
{
    /// <summary><c>sizeof( Ray_t )</c>.</summary>
    public const int SizeOf = 80;

    /// <summary><c>alignof( Ray_t )</c>.</summary>
    public const int AlignOf = 16;

    /// <summary><c>m_Start</c>: the start, centred within the extents.</summary>
    [FieldOffset(0)] public Vector Start;

    /// <summary><c>m_Delta</c>: direction and length.</summary>
    [FieldOffset(16)] public Vector Delta;

    /// <summary><c>m_StartOffset</c>: add to <see cref="Start"/> for the caller's start.</summary>
    [FieldOffset(32)] public Vector StartOffset;

    /// <summary><c>m_Extents</c>: the box's half-size.</summary>
    [FieldOffset(48)] public Vector Extents;

    /// <summary><c>m_IsRay</c>: the extents are zero.</summary>
    [FieldOffset(64)] public byte IsRay;

    /// <summary><c>m_IsSwept</c>: the delta is not zero.</summary>
    [FieldOffset(65)] public byte IsSwept;

    /// <summary><c>Ray_t::Init( start, end )</c>: a line.</summary>
    public static EngineRay Line(Vector start, Vector end)
    {
        var ray = default(EngineRay);
        ray.Delta = end - start;
        ray.IsSwept = (byte)(ray.Delta.LengthSquared != 0f ? 1 : 0);
        ray.Extents = Vector.Zero;
        ray.IsRay = 1;
        ray.StartOffset = Vector.Zero;
        ray.Start = start;
        return ray;
    }

    /// <summary>
    /// <c>Ray_t::Init( start, end, mins, maxs )</c>: a box swept from start to
    /// end, its corners relative to the start. The start is moved to the box's
    /// centre and <see cref="StartOffset"/> moves it back.
    /// </summary>
    public static EngineRay Hull(Vector start, Vector end, Vector mins, Vector maxs)
    {
        var ray = default(EngineRay);
        ray.Delta = end - start;
        ray.IsSwept = (byte)(ray.Delta.LengthSquared != 0f ? 1 : 0);
        ray.Extents = (maxs - mins) * 0.5f;
        ray.IsRay = (byte)(ray.Extents.LengthSquared < 1e-6 ? 1 : 0);
        ray.StartOffset = (mins + maxs) * 0.5f;
        ray.Start = start + ray.StartOffset;
        ray.StartOffset = ray.StartOffset * -1.0f;
        return ray;
    }
}

/// <summary>
/// <c>CGameTrace</c> (public/trace.h, public/gametrace.h) as
/// <c>IEngineTrace::TraceRay</c> writes it (plan_server_native_v2 T4f): 104
/// bytes, trace_layout.h's rows, held by TraceLayoutParityTests.
/// <see cref="Entity"/> is the engine's <c>IHandleEntity *</c>, which managed
/// never dereferences: libss_fwd's <c>SSFwd_TraceFilter_HandleOf</c> turns it
/// into the entity's handle.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = SizeOf)]
public struct EngineGameTrace
{
    /// <summary><c>sizeof( CGameTrace )</c>.</summary>
    public const int SizeOf = 104;

    /// <summary><c>startpos</c>.</summary>
    [FieldOffset(0)] public Vector StartPos;

    /// <summary><c>endpos</c>: where the trace stopped.</summary>
    [FieldOffset(12)] public Vector EndPos;

    /// <summary><c>plane.normal</c>.</summary>
    [FieldOffset(24)] public Vector PlaneNormal;

    /// <summary><c>plane.dist</c>.</summary>
    [FieldOffset(36)] public float PlaneDist;

    /// <summary><c>plane.type</c>.</summary>
    [FieldOffset(40)] public byte PlaneType;

    /// <summary><c>plane.signbits</c>.</summary>
    [FieldOffset(41)] public byte PlaneSignBits;

    /// <summary><c>fraction</c>: 1 is "reached the end".</summary>
    [FieldOffset(44)] public float Fraction;

    /// <summary><c>contents</c>: the CONTENTS_* on the far side of the surface hit.</summary>
    [FieldOffset(48)] public int Contents;

    /// <summary><c>dispFlags</c>.</summary>
    [FieldOffset(52)] public ushort DispFlags;

    /// <summary><c>allsolid</c>.</summary>
    [FieldOffset(54)] public byte AllSolid;

    /// <summary><c>startsolid</c>.</summary>
    [FieldOffset(55)] public byte StartSolid;

    /// <summary><c>fractionleftsolid</c>.</summary>
    [FieldOffset(56)] public float FractionLeftSolid;

    /// <summary><c>surface.name</c>, the engine's string.</summary>
    [FieldOffset(64)] public nint SurfaceName;

    /// <summary><c>surface.surfaceProps</c>.</summary>
    [FieldOffset(72)] public short SurfaceProps;

    /// <summary><c>surface.flags</c>.</summary>
    [FieldOffset(74)] public ushort SurfaceFlags;

    /// <summary><c>hitgroup</c>.</summary>
    [FieldOffset(80)] public int HitGroup;

    /// <summary><c>physicsbone</c>.</summary>
    [FieldOffset(84)] public short PhysicsBone;

    /// <summary><c>m_pEnt</c>, the engine's pointer: read only through HandleOf.</summary>
    [FieldOffset(88)] public nint Entity;

    /// <summary><c>hitbox</c>.</summary>
    [FieldOffset(96)] public int HitBox;

    /// <summary><c>CGameTrace::DidHit</c>: stopped short, or started inside something.</summary>
    public readonly bool DidHit => Fraction < 1.0f || AllSolid != 0 || StartSolid != 0;
}
