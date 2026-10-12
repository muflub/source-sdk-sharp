//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

// The parameter blocks IPhysicsEnvironment's constraint and spring creators
// read (vphysics/constraints.h, vphysics_interface.h), in their own layout
// (plan_physics_bones_native_zero 2.4). Managed builds them and hands their
// address to the creator; vphysics copies what it needs before it returns, so
// none outlives the call. Each offset is pinned twice: by VPhysicsJointParamsLayoutTests
// here and, against the headers, by vtable_index_agreement_test's
// ConstraintParamsLayout. Defaults() is each struct's own, in its order.

/// <summary><c>matrix3x4_t</c>: three rows of four floats, the translation in the fourth column.</summary>
[StructLayout(LayoutKind.Sequential, Size = 48)]
public unsafe struct Float3x4
{
    public fixed float M[12];

    /// <summary>stock's SetIdentityMatrix.</summary>
    public static Float3x4 Identity
    {
        get
        {
            Float3x4 m = default;
            m.M[0] = 1f;
            m.M[5] = 1f;
            m.M[10] = 1f;
            return m;
        }
    }

    /// <summary>Row <paramref name="row"/>, column <paramref name="column"/>.</summary>
    public float this[int row, int column]
    {
        readonly get => M[row * 4 + column];
        set => M[row * 4 + column] = value;
    }
}

/// <summary><c>constraint_breakableparams_t</c>, 24 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct ConstraintBreakableParams
{
    [FieldOffset(0)] public float Strength;
    [FieldOffset(4)] public float ForceLimit;
    [FieldOffset(8)] public float TorqueLimit;
    [FieldOffset(12)] public float BodyMassScale0;
    [FieldOffset(16)] public float BodyMassScale1;
    [FieldOffset(20)] public byte IsActive;

    public static ConstraintBreakableParams Defaults() => new()
    {
        ForceLimit = 0f,
        TorqueLimit = 0f,
        Strength = 1f,
        BodyMassScale0 = 1f,
        BodyMassScale1 = 1f,
        IsActive = 1,
    };
}

/// <summary><c>constraint_axislimit_t</c>, 16 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct ConstraintAxisLimit
{
    [FieldOffset(0)] public float MinRotation;
    [FieldOffset(4)] public float MaxRotation;
    [FieldOffset(8)] public float AngularVelocity;
    [FieldOffset(12)] public float Torque;

    /// <summary>stock's SetAxisFriction.</summary>
    public static ConstraintAxisLimit Friction(float rmin, float rmax, float friction)
        => new() { MinRotation = rmin, MaxRotation = rmax, AngularVelocity = 0f, Torque = friction };
}

/// <summary>
/// <c>constraint_groupparams_t</c>, 12 bytes: what
/// <c>IPhysicsEnvironment::CreateConstraintGroup</c> reads (a ragdoll's group).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 12)]
public struct ConstraintGroupParams
{
    [FieldOffset(0)] public int AdditionalIterations;
    [FieldOffset(4)] public int MinErrorTicks;
    [FieldOffset(8)] public float ErrorTolerance;

    public static ConstraintGroupParams Defaults() => new()
    {
        AdditionalIterations = 0,
        MinErrorTicks = 15,
        ErrorTolerance = 3.0f,
    };
}

/// <summary><c>constraint_fixedparams_t</c>, 72 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 72)]
public struct ConstraintFixedParams
{
    [FieldOffset(0)] public Float3x4 AttachedRefXform;
    [FieldOffset(48)] public ConstraintBreakableParams Constraint;

    public static ConstraintFixedParams Defaults() => new()
    {
        AttachedRefXform = Float3x4.Identity,
        Constraint = ConstraintBreakableParams.Defaults(),
    };
}

/// <summary><c>constraint_hingeparams_t</c>, 64 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct ConstraintHingeParams
{
    [FieldOffset(0)] public Vector WorldPosition;
    [FieldOffset(12)] public Vector WorldAxisDirection;
    [FieldOffset(24)] public ConstraintAxisLimit HingeAxis;
    [FieldOffset(40)] public ConstraintBreakableParams Constraint;

    public static ConstraintHingeParams Defaults() => new()
    {
        HingeAxis = ConstraintAxisLimit.Friction(0f, 0f, 0f),
        Constraint = ConstraintBreakableParams.Defaults(),
    };
}

/// <summary><c>constraint_ballsocketparams_t</c>, 48 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
public struct ConstraintBallsocketParams
{
    [FieldOffset(0)] public Vector ConstraintPosition0;
    [FieldOffset(12)] public Vector ConstraintPosition1;
    [FieldOffset(24)] public ConstraintBreakableParams Constraint;

    public static ConstraintBallsocketParams Defaults() => new()
    {
        Constraint = ConstraintBreakableParams.Defaults(),
    };
}

/// <summary><c>constraint_lengthparams_t</c>, 56 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 56)]
public struct ConstraintLengthParams
{
    [FieldOffset(0)] public ConstraintBreakableParams Constraint;
    [FieldOffset(24)] public Vector ObjectPosition0;
    [FieldOffset(36)] public Vector ObjectPosition1;
    [FieldOffset(48)] public float TotalLength;
    [FieldOffset(52)] public float MinLength;

    public static ConstraintLengthParams Defaults() => new()
    {
        Constraint = ConstraintBreakableParams.Defaults(),
        TotalLength = 1f,
        MinLength = 0f,
    };
}

/// <summary><c>constraint_slidingparams_t</c>, 100 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 100)]
public struct ConstraintSlidingParams
{
    [FieldOffset(0)] public Float3x4 AttachedRefXform;
    [FieldOffset(48)] public Vector SlideAxisRef;
    [FieldOffset(60)] public ConstraintBreakableParams Constraint;
    [FieldOffset(84)] public float LimitMin;
    [FieldOffset(88)] public float LimitMax;
    [FieldOffset(92)] public float Friction;
    [FieldOffset(96)] public float Velocity;

    public static ConstraintSlidingParams Defaults() => new()
    {
        AttachedRefXform = Float3x4.Identity,
        Constraint = ConstraintBreakableParams.Defaults(),
    };
}

/// <summary><c>constraint_pulleyparams_t</c>, 84 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 84)]
public struct ConstraintPulleyParams
{
    [FieldOffset(0)] public ConstraintBreakableParams Constraint;
    [FieldOffset(24)] public Vector PulleyPosition0;
    [FieldOffset(36)] public Vector PulleyPosition1;
    [FieldOffset(48)] public Vector ObjectPosition0;
    [FieldOffset(60)] public Vector ObjectPosition1;
    [FieldOffset(72)] public float TotalLength;
    [FieldOffset(76)] public float GearRatio;
    [FieldOffset(80)] public byte IsRigid;

    public static ConstraintPulleyParams Defaults() => new()
    {
        Constraint = ConstraintBreakableParams.Defaults(),
        TotalLength = 1f,
        GearRatio = 1f,
    };
}

/// <summary><c>constraint_ragdollparams_t</c>, 180 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 180)]
public struct ConstraintRagdollParams
{
    [FieldOffset(0)] public ConstraintBreakableParams Constraint;
    [FieldOffset(24)] public Float3x4 ConstraintToReference;
    [FieldOffset(72)] public Float3x4 ConstraintToAttached;
    [FieldOffset(120)] public int ParentIndex;
    [FieldOffset(124)] public int ChildIndex;
    [FieldOffset(128)] public ConstraintAxisLimit Axis0;
    [FieldOffset(144)] public ConstraintAxisLimit Axis1;
    [FieldOffset(160)] public ConstraintAxisLimit Axis2;
    [FieldOffset(176)] public byte OnlyAngularLimits;
    [FieldOffset(177)] public byte IsActive;
    [FieldOffset(178)] public byte UseClockwiseRotations;

    public static ConstraintRagdollParams Defaults() => new()
    {
        Constraint = ConstraintBreakableParams.Defaults(),
        IsActive = 1,
        ConstraintToReference = Float3x4.Identity,
        ConstraintToAttached = Float3x4.Identity,
        ParentIndex = -1,
        ChildIndex = -1,
    };
}

/// <summary><c>springparams_t</c>, 44 bytes; its constructor zeroes it, as <c>default</c> does.</summary>
[StructLayout(LayoutKind.Explicit, Size = 44)]
public struct SpringParams
{
    [FieldOffset(0)] public float Constant;
    [FieldOffset(4)] public float NaturalLength;
    [FieldOffset(8)] public float Damping;
    [FieldOffset(12)] public float RelativeDamping;
    [FieldOffset(16)] public Vector StartPosition;
    [FieldOffset(28)] public Vector EndPosition;
    [FieldOffset(40)] public byte UseLocalPositions;
    [FieldOffset(41)] public byte OnlyStretch;
}

/// <summary>
/// vphysics_interface.h's <c>objectparams_t</c>, 64 bytes: what
/// <c>IPhysicsEnvironment::CreatePolyObject</c> reads, and what a solid
/// defaults handler fills (physics_shared.cpp's <c>g_PhysDefaultObjectParams</c>).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct ObjectParams
{
    [FieldOffset(0)] public nint MassCenterOverride;
    [FieldOffset(8)] public float Mass;
    [FieldOffset(12)] public float Inertia;
    [FieldOffset(16)] public float Damping;
    [FieldOffset(20)] public float RotDamping;
    [FieldOffset(24)] public float RotInertiaLimit;
    [FieldOffset(32)] public nint Name;
    [FieldOffset(40)] public nint GameData;
    [FieldOffset(48)] public float Volume;
    [FieldOffset(52)] public float DragCoefficient;
    [FieldOffset(56)] public byte EnableCollisions;

    /// <summary>physics_shared.cpp's <c>g_PhysDefaultObjectParams</c>, its name ("DEFAULT") handed in as native text.</summary>
    public static ObjectParams Defaults(nint name) => new()
    {
        Mass = 1.0f,
        Inertia = 1.0f,
        Damping = 0.1f,
        RotDamping = 0.1f,
        RotInertiaLimit = 0.05f,
        Name = name,
        Volume = 0f,
        DragCoefficient = 1.0f,
        EnableCollisions = 1,
    };
}

/// <summary>
/// vcollide_parse.h's <c>solid_t</c>, 1616 bytes: three inline names of 512,
/// the mass-centre override, and the object's params. Read and written in
/// place in native memory (the parser may point the params' mass-centre
/// override into it), so only its offsets are spelled here.
/// </summary>
public static class SolidLayout
{
    public const int Index = 0;
    public const int Name = 4;
    public const int Parent = 516;
    public const int SurfaceProp = 1028;
    public const int NameCapacity = 512;
    public const int MassCenterOverride = 1540;
    public const int Params = 1552;
    public const int Size = 1616;
}

/// <summary>vcollide.h's <c>vcollide_t</c>: the solid count (fifteen bits), the solids, the key text.</summary>
public static class VCollideFields
{
    public const int SolidCountAndPacked = 0;
    public const int SolidCountMask = 0x7FFF;
    public const int Solids = 8;
    public const int KeyValues = 16;
}
