//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// A safe reference to an entity.
///
/// This is Source's <c>CBaseHandle</c>: an entry index packed with a serial
/// number, in one <see cref="uint"/>. It is deliberately NOT an entity index —
/// Source reuses entity slots, so a bare index silently addresses whichever
/// entity now occupies the slot. Every resolution revalidates the serial and
/// fails closed, which makes use-after-free impossible by construction rather
/// than by discipline. See plan.md Part 4, caveat 4.
///
/// Blittable: one uint, no marshalling.
///
/// <para>
/// <b>STORED INVERTED, SO <c>default</c> IS THE INVALID HANDLE</b> (user
/// decision 2026-10-05). The field holds <c>raw ^ 0xFFFFFFFF</c>: all-zero
/// bits read back as <c>0xFFFFFFFF</c>, <see cref="None"/>. Before this,
/// <c>default(EntityHandle)</c> was entry 0, the world, and an omitted
/// argument or an unset field named a real entity. The world is now named on
/// purpose, by its own handle.
/// </para>
///
/// <para>
/// SO THE STRUCT'S BITS ARE NOT THE RAW HANDLE. Native is handed
/// <see cref="Value"/>, never a pinned run of these: a buffer native reads or
/// writes goes through <see cref="CopyRaw"/> or <see cref="DecodeRawInPlace"/>.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct EntityHandle : IEquatable<EntityHandle>
{
    // raw ^ Invert: default(EntityHandle) is the invalid handle.
    private const uint Invert = uint.MaxValue;

    private readonly uint _value;

    public EntityHandle(uint value) => _value = value ^ Invert;

    /// <summary>An explicitly empty handle. The same as <c>default</c>.</summary>
    public static EntityHandle None => default;

    /// <summary>Raw packed value, for passing back across the boundary.</summary>
    public uint Value => _value ^ Invert;

    /// <summary>
    /// The raw packed values of <paramref name="handles"/>, into
    /// <paramref name="raw"/>, for a native call that reads a run of uint32
    /// handles.
    /// </summary>
    public static void CopyRaw(ReadOnlySpan<EntityHandle> handles, Span<uint> raw)
    {
        if (raw.Length < handles.Length)
            throw new ArgumentException("the raw buffer is shorter than the handles", nameof(raw));

        for (int i = 0; i < handles.Length; i++)
            raw[i] = handles[i].Value;
    }

    /// <summary>
    /// Decodes in place a run native has just written raw uint32 handles into
    /// (through <see cref="RawView"/>). The inversion is its own inverse, so
    /// this is the same flip that encoding is.
    /// </summary>
    public static void DecodeRawInPlace(Span<EntityHandle> written)
    {
        Span<uint> bits = MemoryMarshal.Cast<EntityHandle, uint>(written);
        for (int i = 0; i < bits.Length; i++)
            bits[i] ^= Invert;
    }

    /// <summary>
    /// The storage of <paramref name="handles"/> as uint32, for native to
    /// write raw handles into. Until <see cref="DecodeRawInPlace"/> runs over
    /// what it wrote, those elements are not valid handles.
    /// </summary>
    public static Span<uint> RawView(Span<EntityHandle> handles)
        => MemoryMarshal.Cast<EntityHandle, uint>(handles);

    /// <summary>
    /// <c>NUM_ENT_ENTRIES</c> — <c>public/const.h</c>: <c>1 &lt;&lt;
    /// (MAX_EDICT_BITS + 2)</c>, 8,192. The whole handle space, edicts and the
    /// engine-invisible range together.
    /// </summary>
    public const int EntryCount = 8192;

    /// <summary>
    /// <c>ENT_ENTRY_MASK</c> — <c>(1 &lt;&lt; NUM_SERIAL_NUM_BITS) - 1</c>,
    /// which is SIXTEEN bits and not the thirteen an entry needs. Stock's, not
    /// a simplification of stock's.
    /// </summary>
    public const uint EntryMask = 0xFFFF;

    /// <summary>
    /// <c>MAX_EDICTS</c> — <c>public/const.h</c>: <c>1 &lt;&lt; MAX_EDICT_BITS</c>.
    /// Entries below it are edicts; entries from here to
    /// <see cref="EntryCount"/> are the engine-invisible range.
    /// </summary>
    public const int FirstInvisibleEntry = 2048;

    /// <summary>
    /// <c>CBaseHandle::GetEntryIndex</c>, transliterated including its
    /// deliberate answer for the invalid handle: <see cref="EntryCount"/> - 1,
    /// which is a bindable entry, so a caller refuses the invalid handle first.
    ///
    /// <para>
    /// ON THE HANDLE AND NOT ON A DIRECTORY (plan_abi_hotreload D7): it is
    /// arithmetic on the packed value, and the shell-less route, which stays
    /// beside this type, asks it without naming the server's directory.
    /// </para>
    /// </summary>
    public int EntryIndex
        => Value == uint.MaxValue ? EntryCount - 1 : (int)(Value & EntryMask);

    /// <summary>Whether an entry is in the engine-invisible half of the table.</summary>
    public static bool IsInvisibleEntry(int entry)
        => entry >= FirstInvisibleEntry && entry < EntryCount;

    /// <summary>
    /// Whether this handle's entry is in the engine-invisible half, where only
    /// a shell-less entity can live. The invalid handle answers true (its entry
    /// is the last one), so refuse it first.
    /// </summary>
    public bool IsInvisibleRange => IsInvisibleEntry(EntryIndex);

    // NO ENGINE MEMBERS (plan_abi_hotreload D8). IsValid, Index, ClassName,
    // Health and Origin read the engine through the static Engine, choosing a
    // side by the thread's marker; a handle is a VALUE and names no side. Ask
    // the side's IEngineEntities instead (IsValid, GetIndex, GetClassName,
    // GetHealth/SetHealth, GetOrigin/SetOrigin).

    public bool Equals(EntityHandle other) => _value == other._value;
    public override bool Equals(object? obj) => obj is EntityHandle h && Equals(h);
    public override int GetHashCode() => _value.GetHashCode();
    // The value only (plan_abi_hotreload D8): printing a handle must not ask
    // whichever engine side is active what it names.
    public override string ToString() => this == None ? "(invalid handle)" : $"#{EntryIndex}";

    public static bool operator ==(EntityHandle a, EntityHandle b) => a.Equals(b);
    public static bool operator !=(EntityHandle a, EntityHandle b) => !a.Equals(b);
}

/// <summary>A 3D vector, laid out to match Source's <c>Vector</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vector
{
    public float X, Y, Z;

    public Vector(float x, float y, float z)
    {
        X = x; Y = y; Z = z;
    }

    public static readonly Vector Zero = new(0f, 0f, 0f);

    public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector operator *(Vector v, float f) => new(v.X * f, v.Y * f, v.Z * f);
    public static Vector operator *(float f, Vector v) => v * f;

    public float LengthSquared => X * X + Y * Y + Z * Z;
    public float Length => MathF.Sqrt(LengthSquared);

    public float Dot(Vector other) => X * other.X + Y * other.Y + Z * other.Z;

    /// <summary>
    /// The perpendicular, right-handed, exactly as Source's CrossProduct
    /// computes it — a surface's normal is the cross of its two edges, and the
    /// sign decides which way the glass faces.
    /// </summary>
    public Vector Cross(Vector other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    /// <summary>Unit vector in the same direction. A zero vector stays zero rather than becoming NaN.</summary>
    public Vector Normalized
    {
        get
        {
            float length = Length;
            return length > 0f ? this * (1f / length) : Zero;
        }
    }

    public override string ToString() => $"({X:F1}, {Y:F1}, {Z:F1})";
}

/// <summary>
/// A facing: pitch, yaw and roll in degrees. Source's <c>QAngle</c>.
///
/// <para>
/// THREE FLOATS THAT ARE NOT A POSITION. A map writes an angle exactly as it
/// writes a vector — three numbers in a string — and that is precisely why the
/// distinction has to live in the type rather than in a comment: a
/// <c>trigger_push</c>'s "pushdir" and a <c>trigger_teleport_relative</c>'s
/// "teleportoffset" look identical in the .vmf and mean entirely different
/// things. Holding both as <see cref="Vector"/> compiles, reads plausibly, and
/// loses the only fact that matters about them.
/// </para>
///
/// <para>
/// The conversion to a <see cref="Vector"/> is EXPLICIT, and is the seam where
/// this meets the rest of the angle API: <see cref="Angles"/> and
/// <c>ScriptedEntity.SpawnAngles</c> still speak Vector, because they predate
/// this and moving them is a mechanical change with no map keys in it. There
/// are overloads below so a QAngle needs no conversion to be used as what it is.
/// </para>
/// </summary>
public readonly record struct QAngle(float Pitch, float Yaw, float Roll)
{
    public static readonly QAngle Zero = new(0f, 0f, 0f);

    /// <summary>
    /// The same three numbers as a <see cref="Vector"/>, for the parts of the
    /// angle API that still take one. Explicit, so it cannot happen by accident
    /// where a position was meant.
    /// </summary>
    public Vector AsVector() => new(Pitch, Yaw, Roll);

    /// <summary>Three numbers that are known to be a facing.</summary>
    public static QAngle FromVector(Vector v) => new(v.X, v.Y, v.Z);

    public override string ToString() => $"({Pitch:F1}, {Yaw:F1}, {Roll:F1})";
}
